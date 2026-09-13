using Microsoft.Win32;

namespace MarmaladeDesktopPet;

internal sealed class SettingsForm : Form
{
    private bool updatingControls;

    public SettingsForm(PetForm petForm, Action petChanged)
    {
        Text = "ClippyCat Settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = false;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(360, 250);

        PetSettings current = petForm.GetSettingsSnapshot();

        var petCombo = CreateComboBox();
        petCombo.Items.AddRange(new object[] { "Marmalade", "Merry" });
        petCombo.SelectedItem = current.ActivePet;

        var sizeCombo = CreateComboBox();
        sizeCombo.Items.AddRange(new object[] { "75%", "100%", "125%", "150%" });
        sizeCombo.SelectedItem = $"{current.PetSizePercent}%";

        var frequencyCombo = CreateComboBox();
        frequencyCombo.Items.AddRange(PetSettings.AllowedWanderingFrequencies.Cast<object>().ToArray());
        frequencyCombo.SelectedItem = current.WanderingFrequency;

        var alwaysOnTopCheck = new CheckBox
        {
            Text = "Always keep pet on top",
            AutoSize = true,
            Checked = current.AlwaysOnTop,
            Margin = new Padding(3, 8, 3, 8)
        };

        var startWithWindowsCheck = new CheckBox
        {
            Text = "Start ClippyCat with Windows",
            AutoSize = true,
            Checked = current.StartWithWindows,
            Margin = new Padding(3, 8, 3, 8)
        };

        var closeButton = new Button
        {
            Text = "Close",
            AutoSize = true,
            DialogResult = DialogResult.OK
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            AutoSize = true
        };
        buttonPanel.Controls.Add(closeButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 2,
            RowCount = 6
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));

        layout.Controls.Add(CreateLabel("Preferred pet"), 0, 0);
        layout.Controls.Add(petCombo, 1, 0);
        layout.Controls.Add(CreateLabel("Pet size"), 0, 1);
        layout.Controls.Add(sizeCombo, 1, 1);
        layout.Controls.Add(CreateLabel("Wandering frequency"), 0, 2);
        layout.Controls.Add(frequencyCombo, 1, 2);
        layout.Controls.Add(alwaysOnTopCheck, 0, 3);
        layout.SetColumnSpan(alwaysOnTopCheck, 2);
        layout.Controls.Add(startWithWindowsCheck, 0, 4);
        layout.SetColumnSpan(startWithWindowsCheck, 2);
        layout.Controls.Add(buttonPanel, 0, 5);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
        AcceptButton = closeButton;
        CancelButton = closeButton;

        petCombo.SelectedIndexChanged += (_, _) =>
        {
            if (updatingControls || petCombo.SelectedItem is not string petName)
                return;

            petForm.SwitchPet(petName);
            petChanged();

            updatingControls = true;
            petCombo.SelectedItem = petForm.ActivePetName;
            updatingControls = false;
        };

        alwaysOnTopCheck.CheckedChanged += (_, _) =>
        {
            if (!updatingControls)
                petForm.SetAlwaysOnTopSetting(alwaysOnTopCheck.Checked);
        };

        sizeCombo.SelectedIndexChanged += (_, _) =>
        {
            if (updatingControls || sizeCombo.SelectedItem is not string selectedSize)
                return;

            if (int.TryParse(selectedSize.TrimEnd('%'), out int sizePercent))
                petForm.SetPetSizePercent(sizePercent);
        };

        frequencyCombo.SelectedIndexChanged += (_, _) =>
        {
            if (!updatingControls && frequencyCombo.SelectedItem is string frequency)
                petForm.SetWanderingFrequency(frequency);
        };

        startWithWindowsCheck.CheckedChanged += (_, _) =>
        {
            if (updatingControls)
                return;

            if (petForm.TrySetStartWithWindows(startWithWindowsCheck.Checked, out string? error))
                return;

            updatingControls = true;
            startWithWindowsCheck.Checked = petForm.GetSettingsSnapshot().StartWithWindows;
            updatingControls = false;

            MessageBox.Show(
                this,
                $"ClippyCat could not update the Windows startup setting.\n\n{error}",
                "Startup Setting",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning
            );
        };
    }

    private static ComboBox CreateComboBox() => new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Anchor = AnchorStyles.Left | AnchorStyles.Right,
        Margin = new Padding(3, 5, 3, 5)
    };

    private static Label CreateLabel(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 9, 3, 9)
    };
}

internal static class WindowsStartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ClippyCat";

    public static bool TrySetEnabled(bool enabled, out string? error)
    {
        try
        {
            if (enabled)
            {
                string executablePath = Application.ExecutablePath;
                using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true)
                    ?? throw new InvalidOperationException("The current-user startup key could not be opened.");

                key.SetValue(ValueName, $"\"{executablePath}\"", RegistryValueKind.String);
            }
            else
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
                key?.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
