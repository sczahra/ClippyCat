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
    private readonly ContextMenuStrip trayMenu;
    private readonly ToolStripMenuItem hideShowItem;
    private readonly ToolStripMenuItem pauseItem;
    private readonly ToolStripMenuItem marmaladeItem;
    private readonly ToolStripMenuItem merryItem;

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

        var doSomethingItem = new ToolStripMenuItem("Do Something");
        doSomethingItem.Click += (_, _) => petForm.TriggerRandomAction();

        var aboutItem = new ToolStripMenuItem("About");
        aboutItem.Click += (_, _) =>
        {
            MessageBox.Show(
                "Marmalade Desktop Pet\nVersion 1.4\n\n" +
                "Marmalade and Merry now share the same behavior rules;\n" +
                "their only difference is artwork and active pet identity.",
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
        trayMenu.Items.Add(doSomethingItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(aboutItem);
        trayMenu.Items.Add(new ToolStripSeparator());
        trayMenu.Items.Add(quitItem);

        trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Marmalade Desktop Pet",
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

    private void QuitApplication()
    {
        trayIcon.Visible = false;
        petForm.AllowClose();
        petForm.Close();
        trayIcon.Dispose();
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
    Pawing,
    Review,
    Purring,
    Landing,
    LookingAtMouse,
    Dragging
}

internal sealed class PetSettings
{
    public int? X { get; set; }
    public string ActivePet { get; set; } = "Marmalade";
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

    private PetState state = PetState.Idle;
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

    public PetForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        Width = CellWidth;
        Height = CellHeight;

        settingsPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarmaladeDesktopPet",
            "settings.json"
        );

        PetSettings settings = LoadSettings();
        ActivePetName = settings.ActivePet;

        if (!PetAssetExists(ActivePetName))
            ActivePetName = "Marmalade";

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

    public void TriggerRandomAction()
    {
        if (!Visible)
            Show();

        int roll = random.Next(5);

        switch (roll)
        {
            case 0: EnterWaving(); break;
            case 1: EnterJumping(); break;
            case 2: EnterGrooming(); break;
            case 3: EnterPawing(); break;
            default: EnterReview(); break;
        }

        RenderCurrentFrame(true);
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
                return JsonSerializer.Deserialize<PetSettings>(
                    File.ReadAllText(settingsPath)
                ) ?? new PetSettings();
            }
        }
        catch { }

        return new PetSettings();
    }

    private void PositionAtStartup(PetSettings settings)
    {
        var working = Screen.PrimaryScreen?.WorkingArea
            ?? new Rectangle(0, 0, 1280, 720);

        int x = working.Right - Width - 40;

        if (settings.X is int savedX)
            x = Math.Clamp(savedX, working.Left, working.Right - Width);

        Location = new Point(x, working.Bottom - Height);
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

            File.WriteAllText(
                settingsPath,
                JsonSerializer.Serialize(
                    new PetSettings
                    {
                        X = Left,
                        ActivePet = ActivePetName
                    },
                    new JsonSerializerOptions { WriteIndented = true }
                )
            );
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
        if (energy <= 25)
        {
            int tiredRoll = random.Next(100);

            if (tiredRoll < 52) EnterResting();
            else if (tiredRoll < 72) EnterIdle();
            else if (tiredRoll < 88) EnterGrooming();
            else EnterWaiting();

            return;
        }

        if (energy >= 75)
        {
            int activeRoll = random.Next(100);

            if (activeRoll < 22) EnterWalkLeft();
            else if (activeRoll < 44) EnterWalkRight();
            else if (activeRoll < 56) EnterPawing();
            else if (activeRoll < 68) EnterJumping();
            else if (activeRoll < 78) EnterWaving();
            else if (activeRoll < 88) EnterReview();
            else EnterIdle();

            return;
        }

        int roll = random.Next(100);

        if (roll < 34) EnterIdle();
        else if (roll < 46) EnterWalkLeft();
        else if (roll < 58) EnterWalkRight();
        else if (roll < 69) EnterWaiting();
        else if (roll < 79) EnterReview();
        else if (roll < 88) EnterGrooming();
        else if (roll < 93) EnterPawing();
        else if (roll < 97) EnterWaving();
        else EnterJumping();
    }

    private void SetState(PetState newState, int row, int frames, int minMs, int maxMs)
    {
        state = newState;
        currentRow = row;
        currentFrameCount = frames;
        frameIndex = 0;

        long now = Environment.TickCount64;
        nextAnimationFrameAt = now + GetFrameDelay();
        stateEndsAt = now + random.Next(minMs, maxMs);

        lastRenderedRow = -1;
        lastRenderedFrame = -1;
    }

    private void EnterIdle() =>
        SetState(PetState.Idle, IdleRow, IdleFrames, 2500, 7000);

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
        stateEndsAt = Environment.TickCount64 + random.Next(900, 1900);
        lastRenderedRow = -1;
        lastRenderedFrame = -1;
    }

    private void EnterDragging()
    {
        state = PetState.Dragging;
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

    private int GetFrameDelay() => state switch
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
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memoryDc = NativeMethods.CreateCompatibleDC(screenDc);

        IntPtr hBitmap = IntPtr.Zero;
        IntPtr oldBitmap = IntPtr.Zero;

        try
        {
            hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            oldBitmap = NativeMethods.SelectObject(memoryDc, hBitmap);

            NativeMethods.SIZE size = new() { cx = bitmap.Width, cy = bitmap.Height };
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

