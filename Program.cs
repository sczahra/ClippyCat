using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace MarmaladeDesktopPet;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var context = new PetApplicationContext();
        Application.Run(context);
    }
}

internal sealed class PetApplicationContext : ApplicationContext
{
    private readonly PetForm petForm;
    private readonly NotifyIcon trayIcon;
    private readonly Icon? loadedTrayIcon;
    private readonly ContextMenuStrip trayMenu;
    private readonly ToolStripMenuItem hideShowItem;
    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem marmaladeItem;
    private readonly ToolStripMenuItem merryItem;
    private readonly ToolStripMenuItem checkForUpdatesItem;
    private readonly UpdateService updateService = new();
    private System.Windows.Forms.Timer? startupUpdateTimer;
    private bool updateCheckInProgress;
    private bool automaticUpdatePromptShown;
    private bool isQuitting;

    public PetApplicationContext()
    {
        petForm = new PetForm();
        trayMenu = new ContextMenuStrip();

        var choosePetItem = new ToolStripMenuItem("Choose Pet");
        marmaladeItem = new ToolStripMenuItem("Marmalade");
        merryItem = new ToolStripMenuItem("Merry");

        marmaladeItem.Click += (_, _) => SelectPet("Marmalade");
        merryItem.Click += (_, _) => SelectPet("Merry");

        choosePetItem.DropDownItems.Add(marmaladeItem);
        choosePetItem.DropDownItems.Add(merryItem);

        hideShowItem = new ToolStripMenuItem("Hide");
        hideShowItem.Click += (_, _) =>
        {
            if (petForm.Visible)
                petForm.HidePet();
            else
                petForm.ShowPet();

            UpdateHideShowText();
        };

        pauseItem = new ToolStripMenuItem("Pause Wandering");
        pauseItem.Click += (_, _) =>
        {
            petForm.TogglePaused();
            pauseItem.Text = petForm.IsPaused ? "Resume Wandering" : "Pause Wandering";
        };

        var callItem = new ToolStripMenuItem("Call Pet");
        callItem.Click += (_, _) => petForm.CallToCursor();

        var restItem = new ToolStripMenuItem("Rest");
        restItem.Click += (_, _) => petForm.ForceRest();

        ToolStripMenuItem[] visualActionItems = petForm.TrayVisualActions
            .Select(definition =>
            {
                string actionId = definition.Id;
                var item = new ToolStripMenuItem(definition.DisplayName);
                item.Click += (_, _) => petForm.TriggerVisualAction(actionId);
                return item;
            })
            .ToArray();

        var doSomethingItem = new ToolStripMenuItem("Do Something");
        doSomethingItem.Click += (_, _) => petForm.TriggerRandomAction();

        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => ShowSettings();

        checkForUpdatesItem = new ToolStripMenuItem("Check for Updates...");
        checkForUpdatesItem.Click += async (_, _) => await CheckForUpdatesAsync(isAutomatic: false);

        var aboutItem = new ToolStripMenuItem("About");
        aboutItem.Click += (_, _) =>
        {
            MessageBox.Show(
                $"ClippyCat\nVersion {UpdateService.CurrentVersionText}\n" +
                "Updater validation release\n\n" +
                "A standalone Windows desktop pet with\n" +
                "manifest-driven visual actions.",
                "About",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
        };

        var quitItem = new ToolStripMenuItem("Quit");
        quitItem.Click += (_, _) => QuitApplication();

        trayMenu.Items.Add(choosePetItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(hideShowItem);
        trayMenu.Items.Add(pauseItem);
        trayMenu.Items.Add(callItem);
        trayMenu.Items.Add(restItem);
        foreach (ToolStripMenuItem item in visualActionItems)
            trayMenu.Items.Add(item);
        trayMenu.Items.Add(doSomethingItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(settingsItem);
        trayMenu.Items.Add(checkForUpdatesItem);
        trayMenu.Items.Add(aboutItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(quitItem);

        loadedTrayIcon = LoadTrayIcon();
        trayIcon = new NotifyIcon
        {
            Icon = loadedTrayIcon ?? SystemIcons.Application,
            Text = "ClippyCat",
            ContextMenuStrip = trayMenu,
            Visible = true
        };

        trayIcon.DoubleClick += (_, _) =>
        {
            petForm.ShowPet();
            UpdateHideShowText();
        };

        UpdatePetChecks();
        petForm.ShowPet();
        ScheduleStartupUpdateCheck();
    }

    private static Icon? LoadTrayIcon()
    {
        string iconPath = Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "Icon",
            "ClippyCatTrayIcon.ico");

        try
        {
            return File.Exists(iconPath) ? new Icon(iconPath) : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return null;
        }
    }

    private void SelectPet(string petName)
    {
        petForm.SwitchPet(petName);
        UpdatePetChecks();
        UpdateHideShowText();
    }

    private void UpdatePetChecks()
    {
        marmaladeItem.Checked =
            petForm.ActivePetName.Equals("Marmalade", StringComparison.OrdinalIgnoreCase);

        merryItem.Checked =
            petForm.ActivePetName.Equals("Merry", StringComparison.OrdinalIgnoreCase);
    }

    private void UpdateHideShowText()
    {
        hideShowItem.Text = petForm.Visible ? "Hide" : "Show";
    }

    private void ShowSettings()
    {
        using var settingsForm = new SettingsForm(petForm, UpdatePetChecks);
        settingsForm.ShowDialog();
        UpdatePetChecks();
    }

    private void ScheduleStartupUpdateCheck()
    {
        startupUpdateTimer = new System.Windows.Forms.Timer { Interval = 4000 };
        startupUpdateTimer.Tick += StartupUpdateTimer_Tick;
        startupUpdateTimer.Start();
    }

    private async void StartupUpdateTimer_Tick(object? sender, EventArgs e)
    {
        startupUpdateTimer?.Stop();
        startupUpdateTimer?.Dispose();
        startupUpdateTimer = null;
        await CheckForUpdatesAsync(isAutomatic: true);
    }

    private async Task CheckForUpdatesAsync(bool isAutomatic)
    {
        if (updateCheckInProgress || isQuitting)
            return;

        updateCheckInProgress = true;
        checkForUpdatesItem.Enabled = false;
        checkForUpdatesItem.Text = "Checking for Updates...";

        try
        {
            UpdateInfo? update = await updateService.CheckForUpdateAsync();
            if (update is null)
            {
                if (!isAutomatic)
                {
                    MessageBox.Show(
                        "You're running the latest version of ClippyCat.",
                        "ClippyCat Update",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            if (isAutomatic && automaticUpdatePromptShown)
                return;

            automaticUpdatePromptShown = true;
            using var dialog = new UpdateDialog(updateService, update);
            dialog.ShowDialog();

            if (dialog.InstallerLaunched)
                QuitApplication();
        }
        catch (Exception ex)
        {
            PetDiagnostics.Error("UPDATE_ERROR", ex);

            if (!isAutomatic)
            {
                MessageBox.Show(
                    ex is UpdateException ? ex.Message : "ClippyCat couldn't check for updates right now.",
                    "ClippyCat Update",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
        finally
        {
            updateCheckInProgress = false;

            if (!isQuitting)
            {
                checkForUpdatesItem.Enabled = true;
                checkForUpdatesItem.Text = "Check for Updates...";
            }
        }
    }

    private void QuitApplication()
    {
        if (isQuitting)
            return;

        isQuitting = true;
        startupUpdateTimer?.Stop();
        startupUpdateTimer?.Dispose();
        startupUpdateTimer = null;
        trayIcon.Visible = false;
        petForm.AllowClose();
        petForm.Close();
        trayIcon.Dispose();
        loadedTrayIcon?.Dispose();
        trayMenu.Dispose();
        ExitThread();
    }
}

internal enum PetState
{
    Idle,
    WalkLeft,
    WalkRight,
    Waiting,
    Resting,
    Waving,
    Jumping,
    Grooming,
    VisualAction,
    Pawing,
    Review,
    Purring,
    Landing,
    LookingAtMouse,
    Dragging
}

internal enum PetAction
{
    Idle,
    WalkLeft,
    WalkRight,
    Wait,
    Rest,
    Wave,
    Jump,
    Groom,
    Paw,
    Review
}

internal sealed class ActionManifest
{
    public int SchemaVersion { get; set; }
    public int CellWidth { get; set; }
    public int CellHeight { get; set; }
    public int AtlasColumns { get; set; }
    public int BaseRowCount { get; set; }
    public List<VisualActionDefinition> Actions { get; set; } = new();
}

internal sealed class VisualActionDefinition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public int? MenuOrder { get; set; }
    public int FrameCount { get; set; }
    public int FrameMs { get; set; }
    public int MinDurationMs { get; set; }
    public int MaxDurationMs { get; set; }
    public int AutonomousWeight { get; set; }
    public bool TrayVisible { get; set; } = true;
    public bool RandomEligible { get; set; }
    public Dictionary<string, ActionPetDefinition> Pets { get; set; } = new();
}

internal sealed class ActionPetDefinition
{
    public bool Enabled { get; set; }
    public int? AtlasRow { get; set; }
    public string SourceFolder { get; set; } = string.Empty;
}

internal sealed class PetSettings
{
    public static readonly int[] AllowedPetSizes = { 75, 100, 125, 150 };
    public static readonly string[] AllowedWanderingFrequencies = { "Low", "Normal", "High" };

    public int? X { get; set; }
    public string ActivePet { get; set; } = "Marmalade";
    public bool AlwaysOnTop { get; set; } = true;
    public int PetSizePercent { get; set; } = 100;
    public string WanderingFrequency { get; set; } = "Normal";
    public bool StartWithWindows { get; set; }
}

internal sealed class PetForm : Form
{
    private const int CellWidth = 192;
    private const int CellHeight = 208;

    private const int IdleRow = 0;
    private const int RunRightRow = 1;
    private const int RunLeftRow = 2;
    private const int WavingRow = 3;
    private const int JumpingRow = 4;
    private const int GroomingRow = 5;
    private const int WaitingRow = 6;
    private const int PawingRow = 7;
    private const int ReviewRow = 8;

    private const int IdleFrames = 6;
    private const int RunFrames = 8;
    private const int WavingFrames = 4;
    private const int JumpingFrames = 5;
    private const int GroomingFrames = 8;
    private const int WaitingFrames = 6;
    private const int PawingFrames = 6;
    private const int ReviewFrames = 6;

    private Bitmap atlas = null!;
    private readonly System.Windows.Forms.Timer mainTimer;
    private readonly Random random = new();
    private readonly string settingsPath;
    private readonly Dictionary<string, VisualActionDefinition> visualActionDefinitions;
    private PetSettings settings = new();

    private PetState state = PetState.Idle;
    private VisualActionDefinition? activeVisualAction;
    private int currentRow = IdleRow;
    private int currentFrameCount = IdleFrames;
    private int frameIndex;

    private long nextAnimationFrameAt;
    private long stateEndsAt;
    private long nextCuriosityCheckAt;
    private long curiosityCooldownUntil;
    private long nextEnergyTickAt;

    private int movementSpeed;
    private int energy = 72;

    private bool mouseDown;
    private bool dragging;
    private Point mouseDownPoint;
    private Point lastDragScreenPoint;
    private int dragSwingX;
    private long lastPetClickAt;
    private int pettingStreak;
    private bool allowClose;
    private bool paused;

    private int lastRenderedRow = -1;
    private int lastRenderedFrame = -1;
    private Point lastRenderedPosition = new(int.MinValue, int.MinValue);

    public bool IsPaused => paused;
    public string ActivePetName { get; private set; } = "Marmalade";
    public IEnumerable<VisualActionDefinition> TrayVisualActions => visualActionDefinitions.Values
        .Where(definition => definition.Enabled && definition.TrayVisible)
        .OrderBy(definition => definition.MenuOrder ?? int.MaxValue);

    public PetForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Width = CellWidth;
        Height = CellHeight;

        visualActionDefinitions = LoadVisualActionDefinitions();

        settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarmaladeDesktopPet",
            "settings.json"
        );

        settings = LoadSettings();
        ActivePetName = settings.ActivePet;

        if (!PetAssetExists(ActivePetName))
            ActivePetName = "Marmalade";

        settings.ActivePet = ActivePetName;
        TopMost = settings.AlwaysOnTop;
        Size = GetPetSize(settings.PetSizePercent);

        LoadAtlas(ActivePetName);
        PositionAtStartup(settings);

        MouseDown += PetMouseDown;
        MouseMove += PetMouseMove;
        MouseUp += PetMouseUp;

        mainTimer = new System.Windows.Forms.Timer { Interval = 50 };
        mainTimer.Tick += (_, _) => UpdatePet();

        Shown += (_, _) =>
        {
            EnterIdle();
            nextEnergyTickAt = Environment.TickCount64 + 1000;
            RenderCurrentFrame(true);
            mainTimer.Start();
        };

        FormClosing += (_, e) =>
        {
            if (!allowClose)
            {
                e.Cancel = true;
                HidePet();
            }
        };

        FormClosed += (_, _) =>
        {
            SaveSettings();
            mainTimer.Dispose();
            atlas.Dispose();
        };
    }

    public void AllowClose() => allowClose = true;
    public void HidePet() => Hide();

    public void ShowPet()
    {
        if (!Visible)
            Show();

        RenderCurrentFrame(true);
    }

    public PetSettings GetSettingsSnapshot() => new()
    {
        X = Left,
        ActivePet = ActivePetName,
        AlwaysOnTop = settings.AlwaysOnTop,
        PetSizePercent = settings.PetSizePercent,
        WanderingFrequency = settings.WanderingFrequency,
        StartWithWindows = settings.StartWithWindows
    };

    public void SetAlwaysOnTopSetting(bool enabled)
    {
        if (settings.AlwaysOnTop == enabled)
            return;

        settings.AlwaysOnTop = enabled;
        TopMost = enabled;
        SaveSettings();
    }

    public void SetPetSizePercent(int sizePercent)
    {
        if (!PetSettings.AllowedPetSizes.Contains(sizePercent) ||
            settings.PetSizePercent == sizePercent)
            return;

        int centerX = Left + Width / 2;
        int bottom = Top + Height;
        settings.PetSizePercent = sizePercent;
        Size = GetPetSize(sizePercent);

        Rectangle working = Screen.FromPoint(new Point(centerX, bottom - 1)).WorkingArea;
        int x = Math.Clamp(centerX - Width / 2, working.Left, working.Right - Width);
        int y = Math.Clamp(bottom - Height, working.Top, working.Bottom - Height);
        Location = new Point(x, y);

        lastRenderedRow = -1;
        lastRenderedFrame = -1;
        RenderCurrentFrame(true);
        SaveSettings();
    }

    public void SetWanderingFrequency(string frequency)
    {
        if (!PetSettings.AllowedWanderingFrequencies.Contains(frequency) ||
            settings.WanderingFrequency.Equals(frequency, StringComparison.Ordinal))
            return;

        settings.WanderingFrequency = frequency;

        if (state == PetState.Idle && !paused)
            EnterIdle();

        SaveSettings();
    }

    public bool TrySetStartWithWindows(bool enabled, out string? error)
    {
        if (settings.StartWithWindows == enabled)
        {
            error = null;
            return true;
        }

        if (!WindowsStartupManager.TrySetEnabled(enabled, out error))
            return false;

        settings.StartWithWindows = enabled;
        SaveSettings();
        return true;
    }

    public void SwitchPet(string petName)
    {
        if (petName.Equals(ActivePetName, StringComparison.OrdinalIgnoreCase))
        {
            ShowPet();
            return;
        }

        if (!PetAssetExists(petName))
        {
            MessageBox.Show(
                $"{petName}'s sprite sheet could not be found.",
                "Desktop Pet",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            return;
        }

        LoadAtlas(petName);
        ActivePetName = petName;
        settings.ActivePet = ActivePetName;
        energy = 72;
        paused = false;

        EnterIdle();
        SaveSettings();
        ShowPet();
        RenderCurrentFrame(true);
    }

    public void TogglePaused()
    {
        paused = !paused;

        if (paused)
            EnterIdle();
        else
            ChooseNextState();

        RenderCurrentFrame(true);
    }

    public void CallToCursor()
    {
        if (!Visible)
            Show();

        Point cursor = Cursor.Position;
        Point center = new(Left + Width / 2, Top + Height / 2);

        if (Math.Abs(cursor.X - center.X) < 70)
            EnterMouseLook();
        else if (cursor.X < center.X)
            EnterCalledWalkLeft();
        else
            EnterCalledWalkRight();

        curiosityCooldownUntil = Environment.TickCount64 + 1800;
        RenderCurrentFrame(true);
    }

    public void ForceRest()
    {
        energy = Math.Min(energy, 30);
        EnterResting();
        RenderCurrentFrame(true);
    }

    public void TriggerAction(PetAction action)
    {
        if (!Visible)
            Show();

        PerformPetAction(action);
        RenderCurrentFrame(true);
    }

    public void TriggerVisualAction(string actionId)
    {
        if (!Visible)
            Show();

        PerformVisualAction(actionId, showPending: true);
        RenderCurrentFrame(true);
    }

    public void TriggerRandomAction()
    {
        VisualActionDefinition[] visualActions = visualActionDefinitions.Values
            .Where(definition => definition.Enabled &&
                                 definition.RandomEligible &&
                                 IsVisualActionAvailable(definition))
            .ToArray();

        int selection = random.Next(5 + visualActions.Length);
        if (selection >= 5)
        {
            TriggerVisualAction(visualActions[selection - 5].Id);
            return;
        }

        TriggerAction(selection switch
        {
            0 => PetAction.Wave,
            1 => PetAction.Jump,
            2 => PetAction.Groom,
            3 => PetAction.Paw,
            _ => PetAction.Review
        });
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int WS_EX_LAYERED = 0x00080000;
            const int WS_EX_TOOLWINDOW = 0x00000080;
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TOOLWINDOW;
            return cp;
        }
    }

    private PetSettings LoadSettings()
    {
        try
        {
            if (File.Exists(settingsPath))
            {
                PetSettings? loaded = JsonSerializer.Deserialize<PetSettings>(
                    File.ReadAllText(settingsPath)
                );

                return NormalizeSettings(loaded);
            }
        }
        catch { }

        return new PetSettings();
    }

    private static PetSettings NormalizeSettings(PetSettings? loaded)
    {
        PetSettings normalized = loaded ?? new PetSettings();

        if (!string.Equals(normalized.ActivePet, "Marmalade", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(normalized.ActivePet, "Merry", StringComparison.OrdinalIgnoreCase))
            normalized.ActivePet = "Marmalade";
        else
            normalized.ActivePet = normalized.ActivePet.Equals("Merry", StringComparison.OrdinalIgnoreCase)
                ? "Merry"
                : "Marmalade";

        if (!PetSettings.AllowedPetSizes.Contains(normalized.PetSizePercent))
            normalized.PetSizePercent = 100;

        normalized.WanderingFrequency = PetSettings.AllowedWanderingFrequencies
            .FirstOrDefault(value => value.Equals(normalized.WanderingFrequency, StringComparison.OrdinalIgnoreCase))
            ?? "Normal";

        return normalized;
    }

    private static Size GetPetSize(int sizePercent) => new(
        CellWidth * sizePercent / 100,
        CellHeight * sizePercent / 100
    );

    private void PositionAtStartup(PetSettings settings)
    {
        var working = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, 1280, 720);

        int x = working.Right - Width - 40;

        if (settings.X is int savedX)
            x = Math.Clamp(savedX, working.Left, working.Right - Width);

        Location = new Point(x, working.Bottom - Height);
    }

    private static Dictionary<string, VisualActionDefinition> LoadVisualActionDefinitions(
        string? manifestPath = null
    )
    {
        string path = manifestPath ?? Path.Combine(
            AppContext.BaseDirectory,
            "Assets",
            "actions",
            "actions.json"
        );

        try
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("Action manifest was not found.", path);

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            ActionManifest? manifest = JsonSerializer.Deserialize<ActionManifest>(
                File.ReadAllText(path),
                options
            );

            if (manifest is null || manifest.SchemaVersion != 1)
                throw new InvalidDataException("Unsupported or empty action manifest.");

            if (manifest.CellWidth != CellWidth ||
                manifest.CellHeight != CellHeight ||
                manifest.AtlasColumns != 8)
            {
                throw new InvalidDataException("Action manifest atlas dimensions do not match the app.");
            }

            if (manifest.BaseRowCount <= 0 || manifest.Actions is null)
                throw new InvalidDataException("Action manifest structure is invalid.");

            string[] petNames = { "Marmalade", "Merry" };
            var definitions = new Dictionary<string, VisualActionDefinition>(
                StringComparer.OrdinalIgnoreCase
            );
            var rowAssignments = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var autonomousTotals = petNames.ToDictionary(name => name, _ => 0);
            var warnings = new List<string>();

            for (int index = 0; index < manifest.Actions.Count; index++)
            {
                VisualActionDefinition definition = manifest.Actions[index];

                try
                {
                    bool usesLegacyName = string.IsNullOrWhiteSpace(definition.Id);
                    string id = usesLegacyName ? definition.Name.Trim() : definition.Id.Trim();
                    string displayName = !string.IsNullOrWhiteSpace(definition.DisplayName)
                        ? definition.DisplayName.Trim()
                        : usesLegacyName ? definition.Name.Trim() : string.Empty;

                    if (string.IsNullOrWhiteSpace(id))
                        throw new InvalidDataException("Visual action id cannot be empty.");
                    if (!usesLegacyName && !IsValidVisualActionId(id))
                        throw new InvalidDataException($"Visual action id '{id}' must use lowercase kebab-case.");
                    if (string.IsNullOrWhiteSpace(displayName))
                        throw new InvalidDataException($"Visual action '{id}' has no displayName.");
                    if (definitions.ContainsKey(id))
                        throw new InvalidDataException($"Duplicate visual action id '{id}'.");
                    if (definition.FrameCount <= 0 || definition.FrameCount > manifest.AtlasColumns)
                        throw new InvalidDataException($"Visual action '{id}' has an invalid frameCount.");
                    if (definition.FrameMs <= 0 ||
                        definition.MinDurationMs <= 0 ||
                        definition.MaxDurationMs <= definition.MinDurationMs)
                    {
                        throw new InvalidDataException($"Visual action '{id}' has invalid timing metadata.");
                    }
                    if (definition.AutonomousWeight < 0 || definition.AutonomousWeight > 12)
                        throw new InvalidDataException($"Visual action '{id}' has an invalid autonomousWeight.");
                    if (definition.MenuOrder < 0)
                        throw new InvalidDataException($"Visual action '{id}' has an invalid menuOrder.");
                    if (definition.Pets is null)
                        throw new InvalidDataException($"Visual action '{id}' has no pet availability metadata.");

                    foreach (string configuredPet in definition.Pets.Keys)
                    {
                        if (!petNames.Contains(configuredPet, StringComparer.OrdinalIgnoreCase))
                            throw new InvalidDataException($"Visual action '{id}' names unknown pet '{configuredPet}'.");
                    }

                    definition.Pets = new Dictionary<string, ActionPetDefinition>(
                        definition.Pets,
                        StringComparer.OrdinalIgnoreCase
                    );

                    var pendingRows = new List<(string Key, string PetName)>();
                    var pendingAutonomousTotals = new Dictionary<string, int>(autonomousTotals);

                    foreach (string petName in petNames)
                    {
                        if (!definition.Pets.TryGetValue(petName, out ActionPetDefinition? pet))
                            throw new InvalidDataException($"Visual action '{id}' is missing pet configuration for '{petName}'.");
                        if (string.IsNullOrWhiteSpace(pet.SourceFolder))
                            throw new InvalidDataException($"Visual action '{id}' has no sourceFolder for '{petName}'.");
                        if (pet.AtlasRow.HasValue && pet.AtlasRow.Value < manifest.BaseRowCount)
                            throw new InvalidDataException($"Visual action '{id}' uses protected {petName} row {pet.AtlasRow.Value}.");
                        if (definition.Enabled && pet.Enabled && !pet.AtlasRow.HasValue)
                            throw new InvalidDataException($"Enabled visual action '{id}' has no atlasRow for '{petName}'.");

                        if (pet.AtlasRow.HasValue)
                        {
                            string rowKey = $"{petName}|{pet.AtlasRow.Value}";
                            if (rowAssignments.TryGetValue(rowKey, out string? assignedId))
                            {
                                throw new InvalidDataException(
                                    $"{petName} row {pet.AtlasRow.Value} is already assigned to '{assignedId}'."
                                );
                            }

                            pendingRows.Add((rowKey, petName));
                        }

                        if (definition.Enabled && pet.Enabled)
                        {
                            pendingAutonomousTotals[petName] += definition.AutonomousWeight;
                            if (pendingAutonomousTotals[petName] > 12)
                            {
                                throw new InvalidDataException(
                                    $"{petName} autonomous visual-action weights exceed 12."
                                );
                            }
                        }
                    }

                    definition.Id = id;
                    definition.DisplayName = displayName;
                    definitions.Add(id, definition);
                    autonomousTotals = pendingAutonomousTotals;

                    foreach (var pendingRow in pendingRows)
                        rowAssignments.Add(pendingRow.Key, id);
                }
                catch (Exception ex)
                {
                    warnings.Add($"Entry {index + 1}: {ex.Message}");
                }
            }

            if (warnings.Count > 0)
            {
                MessageBox.Show(
                    "Some optional visual actions were skipped:\n\n" + string.Join("\n", warnings),
                    "ClippyCat Action Manifest",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
            }

            return definitions;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Dedicated action metadata could not be loaded. Those actions will remain pending.\n\n{ex.Message}",
                "ClippyCat Action Manifest",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );

            return new Dictionary<string, VisualActionDefinition>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static bool IsValidVisualActionId(string id)
    {
        if (id.Length == 0 || id[0] == '-' || id[^1] == '-' || id.Contains("--"))
            return false;

        return id.All(character =>
            character is >= 'a' and <= 'z' ||
            character is >= '0' and <= '9' ||
            character == '-'
        );
    }

    private bool PetAssetExists(string petName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", petName, "spritesheet.png");
        return File.Exists(path);
    }

    private void LoadAtlas(string petName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Assets", petName, "spritesheet.png");

        if (!File.Exists(path))
        {
            MessageBox.Show(
                $"{petName}'s sprite sheet could not be found:\n{path}",
                "Desktop Pet",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );
            Environment.Exit(1);
        }

        Bitmap replacement = new(path);

        if (atlas is not null)
            atlas.Dispose();

        atlas = replacement;
    }

    private void SaveSettings()
    {
        try
        {
            string? folder = Path.GetDirectoryName(settingsPath);

            if (!string.IsNullOrWhiteSpace(folder))
                Directory.CreateDirectory(folder);

            settings.X = Left;
            settings.ActivePet = ActivePetName;

            string json = JsonSerializer.Serialize(
                settings,
                new JsonSerializerOptions { WriteIndented = true }
            );

            if (File.Exists(settingsPath) && File.ReadAllText(settingsPath) == json)
                return;

            File.WriteAllText(settingsPath, json);
        }
        catch { }
    }

    private void UpdatePet()
    {
        if (!Visible || dragging || mouseDown)
            return;

        long now = Environment.TickCount64;

        UpdateEnergy(now);

        if (paused)
        {
            if (state != PetState.Idle)
                EnterIdle();

            if (now >= nextAnimationFrameAt)
            {
                AdvanceAnimation();
                nextAnimationFrameAt = now + GetFrameDelay();
                RenderCurrentFrame();
            }

            return;
        }

        MaybeNoticeMouse(now);

        bool moved = UpdateMovement();

        if (state == PetState.LookingAtMouse)
        {
            RenderMouseLookFrame();

            if (now >= stateEndsAt)
                ChooseNextState();

            return;
        }

        bool frameChanged = false;

        if (now >= nextAnimationFrameAt)
        {
            AdvanceAnimation();
            nextAnimationFrameAt = now + GetFrameDelay();
            frameChanged = true;
        }

        if (frameChanged || moved)
            RenderCurrentFrame();

        if (now >= stateEndsAt)
        {
            ChooseNextState();
            RenderCurrentFrame(true);
        }
    }

    private void UpdateEnergy(long now)
    {
        if (now < nextEnergyTickAt)
            return;

        nextEnergyTickAt = now + 1000;

        switch (state)
        {
            case PetState.WalkLeft:
            case PetState.WalkRight:
                energy -= 2;
                break;

            case PetState.Jumping:
            case PetState.Pawing:
                energy -= 3;
                break;

            case PetState.Waving:
            case PetState.Review:
            case PetState.VisualAction:
            case PetState.Landing:
                energy -= 1;
                break;

            case PetState.Resting:
                energy += 5;
                break;

            case PetState.Purring:
                energy += 3;
                break;

            case PetState.Idle:
            case PetState.Waiting:
            case PetState.Grooming:
                energy += 2;
                break;
        }

        energy = Math.Clamp(energy, 0, 100);

        if (energy <= 12 &&
            state != PetState.Resting &&
            state != PetState.LookingAtMouse)
        {
            EnterResting();
        }
    }

    private void MaybeNoticeMouse(long now)
    {
        if (now < nextCuriosityCheckAt || now < curiosityCooldownUntil)
            return;

        nextCuriosityCheckAt = now + 650;

        if (state == PetState.Resting)
            return;

        if (state != PetState.Idle &&
            state != PetState.Waiting &&
            state != PetState.Review)
            return;

        Point center = new(Left + Width / 2, Top + Height / 2);
        Point cursor = Cursor.Position;

        double dx = cursor.X - center.X;
        double dy = cursor.Y - center.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);

        int attentionChance = energy > 55 ? 64 : 36;

        if (distance <= 300 && random.Next(100) < attentionChance)
        {
            EnterMouseLook();
            curiosityCooldownUntil = now + random.Next(2500, 5500);
        }
        else if (energy > 42 &&
                 distance <= 600 &&
                 Math.Abs(dy) < 250 &&
                 random.Next(100) < 17)
        {
            if (cursor.X < center.X)
                EnterShortCuriousWalkLeft();
            else
                EnterShortCuriousWalkRight();

            curiosityCooldownUntil = now + random.Next(4000, 7500);
        }
    }

    private void ChooseNextState()
    {
        (PetAction builtInAction, string? visualActionId) = SelectNextAutonomousAction();

        if (visualActionId is not null)
            PerformVisualAction(visualActionId, showPending: false);
        else
            PerformPetAction(builtInAction);
    }

    private (PetAction BuiltInAction, string? VisualActionId) SelectNextAutonomousAction()
    {
        if (energy <= 25)
        {
            int tiredRoll = random.Next(100);

            if (tiredRoll < 52) return (PetAction.Rest, null);
            if (tiredRoll < 72) return (PetAction.Idle, null);
            if (tiredRoll < 88) return (PetAction.Groom, null);
            return (PetAction.Wait, null);
        }

        if (energy >= 75)
        {
            int activeRoll = random.Next(100);

            if (activeRoll < 22) return (PetAction.WalkLeft, null);
            if (activeRoll < 44) return (PetAction.WalkRight, null);
            if (activeRoll < 56) return (PetAction.Paw, null);
            if (activeRoll < 68) return (PetAction.Jump, null);
            if (activeRoll < 78) return (PetAction.Wave, null);
            if (activeRoll < 88) return (PetAction.Review, null);
            return SelectIdleOrVisualAction(activeRoll - 88, 12);
        }

        int roll = random.Next(100);

        if (roll < 34)
            return SelectIdleOrVisualAction(roll, 34);
        if (roll < 46) return (PetAction.WalkLeft, null);
        if (roll < 58) return (PetAction.WalkRight, null);
        if (roll < 69) return (PetAction.Wait, null);
        if (roll < 79) return (PetAction.Review, null);
        if (roll < 88) return (PetAction.Groom, null);
        if (roll < 93) return (PetAction.Paw, null);
        if (roll < 97) return (PetAction.Wave, null);
        return (PetAction.Jump, null);
    }

    private (PetAction BuiltInAction, string? VisualActionId) SelectIdleOrVisualAction(
        int idleRoll,
        int idleBucketSize
    )
    {
        VisualActionDefinition[] eligibleActions = visualActionDefinitions.Values
            .Where(definition => definition.Enabled &&
                                 definition.AutonomousWeight > 0 &&
                                 IsVisualActionAvailable(definition))
            .ToArray();
        int totalActionWeight = eligibleActions.Sum(definition => definition.AutonomousWeight);

        if (idleRoll < idleBucketSize - totalActionWeight)
            return (PetAction.Idle, null);

        int actionRoll = idleRoll - (idleBucketSize - totalActionWeight);
        foreach (VisualActionDefinition definition in eligibleActions)
        {
            if (actionRoll < definition.AutonomousWeight)
                return (PetAction.Idle, definition.Id);

            actionRoll -= definition.AutonomousWeight;
        }

        return (PetAction.Idle, null);
    }

    private void PerformPetAction(PetAction action)
    {
        switch (action)
        {
            case PetAction.Idle: EnterIdle(); break;
            case PetAction.WalkLeft: EnterWalkLeft(); break;
            case PetAction.WalkRight: EnterWalkRight(); break;
            case PetAction.Wait: EnterWaiting(); break;
            case PetAction.Rest: EnterResting(); break;
            case PetAction.Wave: EnterWaving(); break;
            case PetAction.Jump: EnterJumping(); break;
            case PetAction.Groom: EnterGrooming(); break;
            case PetAction.Paw: EnterPawing(); break;
            case PetAction.Review: EnterReview(); break;
            default: throw new ArgumentOutOfRangeException(nameof(action));
        }
    }

    private void PerformVisualAction(string actionId, bool showPending)
    {
        if (!visualActionDefinitions.TryGetValue(actionId, out VisualActionDefinition? definition) ||
            !definition.Enabled)
        {
            if (showPending)
                ShowPendingAnimation(actionId);

            return;
        }

        if (TryGetEnabledVisualAction(definition, out ActionPetDefinition? pet))
        {
            SetState(
                PetState.VisualAction,
                pet.AtlasRow!.Value,
                definition.FrameCount,
                definition.MinDurationMs,
                definition.MaxDurationMs,
                definition
            );
        }
        else if (showPending)
        {
            ShowPendingAnimation(definition.DisplayName);
        }
    }

    private bool IsVisualActionAvailable(VisualActionDefinition definition) =>
        TryGetEnabledVisualAction(definition, out _);

    private bool TryGetEnabledVisualAction(
        VisualActionDefinition definition,
        out ActionPetDefinition pet
    )
    {
        pet = null!;

        if (!definition.Enabled ||
            !definition.Pets.TryGetValue(ActivePetName, out ActionPetDefinition? foundPet) ||
            !foundPet.Enabled ||
            !foundPet.AtlasRow.HasValue)
        {
            return false;
        }

        pet = foundPet;
        return true;
    }

    private void ShowPendingAnimation(string action)
    {
        MessageBox.Show(
            this,
            $"{action} animation coming soon.",
            "ClippyCat",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information
        );
    }

    private void SetState(
        PetState newState,
        int row,
        int frames,
        int minMs,
        int maxMs,
        VisualActionDefinition? visualAction = null
    )
    {
        state = newState;
        activeVisualAction = visualAction;
        currentRow = row;
        currentFrameCount = frames;
        frameIndex = 0;

        long now = Environment.TickCount64;
        nextAnimationFrameAt = now + GetFrameDelay();
        stateEndsAt = now + random.Next(minMs, maxMs);

        lastRenderedRow = -1;
        lastRenderedFrame = -1;
    }

    private void EnterIdle()
    {
        (int minMs, int maxMs) = settings.WanderingFrequency switch
        {
            "Low" => (5000, 12000),
            "High" => (1500, 4500),
            _ => (2500, 7000)
        };

        SetState(PetState.Idle, IdleRow, IdleFrames, minMs, maxMs);
    }

    private void EnterWalkLeft()
    {
        movementSpeed = random.Next(2, 4);
        SetState(PetState.WalkLeft, RunLeftRow, RunFrames, 1500, 3900);
    }

    private void EnterWalkRight()
    {
        movementSpeed = random.Next(2, 4);
        SetState(PetState.WalkRight, RunRightRow, RunFrames, 1500, 3900);
    }

    private void EnterShortCuriousWalkLeft()
    {
        movementSpeed = 2;
        SetState(PetState.WalkLeft, RunLeftRow, RunFrames, 650, 1300);
    }

    private void EnterShortCuriousWalkRight()
    {
        movementSpeed = 2;
        SetState(PetState.WalkRight, RunRightRow, RunFrames, 650, 1300);
    }

    private void EnterCalledWalkLeft()
    {
        movementSpeed = 4;
        SetState(PetState.WalkLeft, RunLeftRow, RunFrames, 1200, 2400);
    }

    private void EnterCalledWalkRight()
    {
        movementSpeed = 4;
        SetState(PetState.WalkRight, RunRightRow, RunFrames, 1200, 2400);
    }

    private void EnterWaiting() =>
        SetState(PetState.Waiting, WaitingRow, WaitingFrames, 2400, 5200);

    private void EnterResting() =>
        SetState(PetState.Resting, WaitingRow, WaitingFrames, 6500, 12000);

    private void EnterWaving() =>
        SetState(PetState.Waving, WavingRow, WavingFrames, 1200, 1700);

    private void EnterJumping() =>
        SetState(PetState.Jumping, JumpingRow, JumpingFrames, 1100, 1500);

    private void EnterGrooming() =>
        SetState(PetState.Grooming, GroomingRow, GroomingFrames, 2400, 4200);

    private void EnterPawing() =>
        SetState(PetState.Pawing, PawingRow, PawingFrames, 1500, 2600);

    private void EnterPurring() =>
        SetState(PetState.Purring, WaitingRow, WaitingFrames, 2800, 4800);

    private void EnterLanding()
    {
        state = PetState.Landing;
        activeVisualAction = null;
        currentRow = JumpingRow;
        currentFrameCount = JumpingFrames;
        frameIndex = JumpingFrames - 1;

        long now = Environment.TickCount64;
        nextAnimationFrameAt = now + 90;
        stateEndsAt = now + 550;

        lastRenderedRow = -1;
        lastRenderedFrame = -1;
    }

    private void EnterReview() =>
        SetState(PetState.Review, ReviewRow, ReviewFrames, 1700, 3300);

    private void EnterMouseLook()
    {
        state = PetState.LookingAtMouse;
        activeVisualAction = null;
        stateEndsAt = Environment.TickCount64 + random.Next(900, 1900);
        lastRenderedRow = -1;
        lastRenderedFrame = -1;
    }

    private void EnterDragging()
    {
        state = PetState.Dragging;
        activeVisualAction = null;
        dragSwingX = 0;
        lastRenderedRow = -1;
        lastRenderedFrame = -1;
    }

    private bool UpdateMovement()
    {
        if (state != PetState.WalkLeft && state != PetState.WalkRight)
            return false;

        var center = new Point(Left + Width / 2, Top + Height / 2);
        var working = Screen.FromPoint(center).WorkingArea;

        int oldX = Left;
        int nextX = Left + (state == PetState.WalkLeft ? -movementSpeed : movementSpeed);
        int minimumX = working.Left;
        int maximumX = working.Right - Width;

        if (nextX <= minimumX)
        {
            Left = minimumX;
            EnterWalkRight();
        }
        else if (nextX >= maximumX)
        {
            Left = maximumX;
            EnterWalkLeft();
        }
        else
        {
            Left = nextX;
        }

        Top = working.Bottom - Height;
        return Left != oldX;
    }

    private int GetFrameDelay()
    {
        if (state == PetState.VisualAction && activeVisualAction is not null)
            return activeVisualAction.FrameMs;

        return state switch
        {
            PetState.WalkLeft => 105,
            PetState.WalkRight => 105,
            PetState.Waving => 180,
            PetState.Jumping => 160,
            PetState.Grooming => 230,
            PetState.Pawing => 210,
            PetState.Purring => 280,
            PetState.Landing => 90,
            PetState.Resting => 300,
            PetState.Waiting => 220,
            PetState.Review => 220,
            _ => 180
        };
    }

    private void AdvanceAnimation()
    {
        if (state == PetState.LookingAtMouse || state == PetState.Dragging)
            return;

        if (state == PetState.Landing)
        {
            frameIndex--;

            if (frameIndex < 0)
                frameIndex = 0;

            return;
        }

        frameIndex = (frameIndex + 1) % currentFrameCount;
    }

    private void ReactToClick()
    {
        long now = Environment.TickCount64;

        if (now - lastPetClickAt <= 1300)
            pettingStreak++;
        else
            pettingStreak = 1;

        lastPetClickAt = now;

        // Repeated gentle clicks read as petting rather than random commands.
        if (pettingStreak >= 3)
        {
            pettingStreak = 0;
            energy = Math.Min(100, energy + 6);
            EnterPurring();
            curiosityCooldownUntil = now + 2600;
            return;
        }

        if (state == PetState.Resting)
        {
            energy = Math.Min(100, energy + 8);
            EnterMouseLook();
            curiosityCooldownUntil = now + 1800;
            return;
        }

        int reaction = random.Next(100);

        if (pettingStreak == 2)
        {
            if (reaction < 65)
                EnterPawing();
            else
                EnterWaving();

            curiosityCooldownUntil = now + 1700;
            return;
        }

        if (energy < 30)
        {
            if (reaction < 50) EnterGrooming();
            else if (reaction < 80) EnterMouseLook();
            else EnterWaving();
        }
        else
        {
            if (reaction < 36) EnterWaving();
            else if (reaction < 60) EnterPawing();
            else if (reaction < 79) EnterMouseLook();
            else if (reaction < 93) EnterJumping();
            else EnterGrooming();
        }

        curiosityCooldownUntil = now + 2300;
    }

    private void RenderMouseLookFrame()
    {
        Point petCenter = new(Left + Width / 2, Top + Height / 2);
        Point mouse = Cursor.Position;

        double dx = mouse.X - petCenter.X;
        double dy = mouse.Y - petCenter.Y;

        double degrees = Math.Atan2(dx, -dy) * 180.0 / Math.PI;

        if (degrees < 0)
            degrees += 360;

        int direction = (int)Math.Round(degrees / 22.5) % 16;
        int row = direction < 8 ? 9 : 10;
        int column = direction < 8 ? direction : direction - 8;

        RenderAtlasCell(row, column, false);
    }

    private void RenderDraggingFrame(bool force = false)
    {
        // Use Marmalade's real airborne frame for both pets so pickup is
        // visibly different from standing/idle. Merry shares the same layout.
        const int DragRow = JumpingRow;
        const int DragColumn = 1;

        Rectangle source = new(
            DragColumn * CellWidth,
            DragRow * CellHeight,
            CellWidth,
            CellHeight
        );

        using var sourceFrame = new Bitmap(CellWidth, CellHeight, PixelFormat.Format32bppArgb);

        using (Graphics sg = Graphics.FromImage(sourceFrame))
        {
            sg.Clear(Color.Transparent);
            sg.DrawImage(
                atlas,
                new Rectangle(0, 0, CellWidth, CellHeight),
                source,
                GraphicsUnit.Pixel
            );
        }

        using var frame = new Bitmap(CellWidth, CellHeight, PixelFormat.Format32bppArgb);

        using (Graphics g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

            // Rotate gently around an upper anchor point. Mouse movement changes
            // dragSwingX, which makes the hanging cat sway instead of staying rigid.
            float angle = dragSwingX * 1.35f;

            g.TranslateTransform(CellWidth / 2f, 28f);
            g.RotateTransform(angle);
            g.TranslateTransform(-CellWidth / 2f, -28f);

            g.DrawImage(
                sourceFrame,
                new Rectangle(5, 12, CellWidth - 10, CellHeight - 16),
                0,
                0,
                CellWidth,
                CellHeight,
                GraphicsUnit.Pixel
            );

            g.ResetTransform();
        }

        SetBitmap(frame);

        lastRenderedRow = DragRow;
        lastRenderedFrame = DragColumn;
        lastRenderedPosition = Location;
    }

    private void RenderCurrentFrame(bool force = false)
    {
        if (state == PetState.Dragging)
        {
            RenderDraggingFrame(force);
            return;
        }

        RenderAtlasCell(currentRow, frameIndex, force);
    }

    private void RenderAtlasCell(int row, int column, bool force)
    {
        if (!force &&
            row == lastRenderedRow &&
            column == lastRenderedFrame &&
            Location == lastRenderedPosition)
            return;

        Rectangle source = new(
            column * CellWidth,
            row * CellHeight,
            CellWidth,
            CellHeight
        );

        using var frame = new Bitmap(CellWidth, CellHeight, PixelFormat.Format32bppArgb);

        using (Graphics g = Graphics.FromImage(frame))
        {
            g.Clear(Color.Transparent);
            g.DrawImage(
                atlas,
                new Rectangle(0, 0, CellWidth, CellHeight),
                source,
                GraphicsUnit.Pixel
            );
        }

        SetBitmap(frame);

        lastRenderedRow = row;
        lastRenderedFrame = column;
        lastRenderedPosition = Location;
    }

    private void PetMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        mouseDown = true;
        dragging = false;
        mouseDownPoint = e.Location;
        lastDragScreenPoint = PointToScreen(e.Location);
        Capture = true;
    }

    private void PetMouseMove(object? sender, MouseEventArgs e)
    {
        if (!mouseDown)
            return;

        Point screen = PointToScreen(e.Location);

        if (!dragging)
        {
            int dx = Math.Abs(e.X - mouseDownPoint.X);
            int dy = Math.Abs(e.Y - mouseDownPoint.Y);

            if (dx >= 5 || dy >= 5)
            {
                dragging = true;
                EnterDragging();
            }
        }

        if (!dragging)
            return;

        int deltaX = screen.X - lastDragScreenPoint.X;
        dragSwingX = Math.Clamp(deltaX / 2, -6, 6);
        lastDragScreenPoint = screen;

        // The window hangs below the cursor so it feels like the cat
        // is being picked up rather than merely repositioned.
        Location = new Point(
            screen.X - (Width / 2),
            screen.Y - 24
        );

        RenderDraggingFrame(true);
    }

    private void PetMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
            return;

        bool wasDragging = dragging;

        mouseDown = false;
        dragging = false;
        Capture = false;
        dragSwingX = 0;

        if (wasDragging)
        {
            SnapToGround();
            SaveSettings();
            EnterLanding();
            curiosityCooldownUntil = Environment.TickCount64 + 1400;
        }
        else
        {
            ReactToClick();
        }

        RenderCurrentFrame(true);
    }

    private void SnapToGround()
    {
        var center = new Point(Left + Width / 2, Top + Height / 2);
        var working = Screen.FromPoint(center).WorkingArea;

        int x = Math.Clamp(Left, working.Left, working.Right - Width);

        Location = new Point(x, working.Bottom - Height);
    }

    private void SetBitmap(Bitmap bitmap)
    {
        Bitmap? scaledBitmap = null;
        Bitmap displayBitmap = bitmap;

        if (bitmap.Width != Width || bitmap.Height != Height)
        {
            scaledBitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);

            using Graphics scaleGraphics = Graphics.FromImage(scaledBitmap);
            scaleGraphics.Clear(Color.Transparent);
            scaleGraphics.CompositingMode = System.Drawing.Drawing2D.CompositingMode.SourceCopy;
            scaleGraphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
            scaleGraphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            scaleGraphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            scaleGraphics.DrawImage(
                bitmap,
                new Rectangle(0, 0, Width, Height),
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                GraphicsUnit.Pixel
            );

            displayBitmap = scaledBitmap;
        }

        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memoryDc = NativeMethods.CreateCompatibleDC(screenDc);

        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = displayBitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = NativeMethods.SelectObject(memoryDc, hBitmap);

            NativeMethods.SIZE size = new() { cx = displayBitmap.Width, cy = displayBitmap.Height };
            NativeMethods.POINT sourcePoint = new() { x = 0, y = 0 };
            NativeMethods.POINT topPosition = new() { x = Left, y = Top };

            NativeMethods.BLENDFUNCTION blend = new()
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA
            };

            NativeMethods.UpdateLayeredWindow(
                Handle,
                screenDc,
                ref topPosition,
                ref size,
                memoryDc,
                ref sourcePoint,
                0,
                ref blend,
                NativeMethods.ULW_ALPHA
            );
        }
        finally
        {
            if (oldBitmap != IntPtr.Zero)
                NativeMethods.SelectObject(memoryDc, oldBitmap);

            if (hBitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(hBitmap);

            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
            scaledBitmap?.Dispose();
        }
    }
}

internal static class NativeMethods
{
    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;
    public const int ULW_ALPHA = 0x00000002;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE
    {
        public int cx;
        public int cy;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteDC(IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(
        IntPtr hWnd,
        IntPtr hdcDst,
        ref POINT pptDst,
        ref SIZE psize,
        IntPtr hdcSrc,
        ref POINT pptSrc,
        int crKey,
        ref BLENDFUNCTION pblend,
        int dwFlags
    );
}

