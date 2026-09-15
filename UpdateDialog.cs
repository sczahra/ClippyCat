using System.Drawing;

namespace MarmaladeDesktopPet;

internal sealed class UpdateDialog : Form
{
    private readonly UpdateService updateService;
    private readonly UpdateInfo update;
    private readonly Button installButton;
    private readonly Button laterButton;
    private readonly Label statusLabel;
    private readonly ProgressBar progressBar;
    private CancellationTokenSource? downloadCancellation;
    private bool busy;

    public bool InstallerLaunched { get; private set; }

    public UpdateDialog(UpdateService updateService, UpdateInfo update)
    {
        this.updateService = updateService;
        this.update = update;

        Text = "ClippyCat Update Available";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(540, 410);
        Font = SystemFonts.MessageBoxFont;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 6
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var heading = new Label
        {
            Text = "A new version of ClippyCat is ready.",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 12)
        };

        var versions = new Label
        {
            Text = $"Installed:  {UpdateService.FormatVersion(update.InstalledVersion)}\r\n" +
                   $"Available: {UpdateService.FormatVersion(update.AvailableVersion)}",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12)
        };

        var releaseNotes = new TextBox
        {
            Text = update.ReleaseNotes,
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            TabStop = false,
            BackColor = SystemColors.Window,
            Margin = new Padding(0, 0, 0, 10)
        };

        statusLabel = new Label
        {
            Text = "The installer will be downloaded and verified before Windows opens it.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 8)
        };

        progressBar = new ProgressBar
        {
            Dock = DockStyle.Fill,
            Minimum = 0,
            Maximum = 100,
            Visible = false,
            Margin = new Padding(0, 0, 0, 12)
        };

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty
        };

        installButton = new Button
        {
            Text = "Download and Install",
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 2)
        };
        installButton.Click += InstallButton_Click;

        laterButton = new Button
        {
            Text = "Later",
            AutoSize = true,
            Padding = new Padding(8, 2, 8, 2),
            DialogResult = DialogResult.Cancel
        };
        laterButton.Click += LaterButton_Click;

        buttonPanel.Controls.Add(installButton);
        buttonPanel.Controls.Add(laterButton);

        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(versions, 0, 1);
        layout.Controls.Add(releaseNotes, 0, 2);
        layout.Controls.Add(statusLabel, 0, 3);
        layout.Controls.Add(progressBar, 0, 4);
        layout.Controls.Add(buttonPanel, 0, 5);
        Controls.Add(layout);

        AcceptButton = installButton;
        CancelButton = laterButton;
        FormClosing += UpdateDialog_FormClosing;
    }

    private async void InstallButton_Click(object? sender, EventArgs e)
    {
        if (busy)
            return;

        busy = true;
        installButton.Enabled = false;
        laterButton.Enabled = false;
        laterButton.DialogResult = DialogResult.None;
        progressBar.Value = 0;
        progressBar.Style = ProgressBarStyle.Marquee;
        progressBar.Visible = true;
        statusLabel.Text = "Downloading update...";
        downloadCancellation = new CancellationTokenSource();

        var progress = new Progress<UpdateProgress>(updateProgress =>
        {
            if (updateProgress.Stage == UpdateProgressStage.Verifying)
            {
                progressBar.Style = ProgressBarStyle.Marquee;
                statusLabel.Text = "Verifying update...";
                return;
            }

            if (updateProgress.Percentage is int percent)
            {
                progressBar.Style = ProgressBarStyle.Continuous;
                progressBar.Value = Math.Clamp(percent, progressBar.Minimum, progressBar.Maximum);
                statusLabel.Text = $"Downloading update... {percent}%";
            }
            else
            {
                progressBar.Style = ProgressBarStyle.Marquee;
                statusLabel.Text = "Downloading update...";
            }
        });

        try
        {
            UpdateDownloadResult download = await updateService.DownloadAndVerifyAsync(
                update,
                progress,
                downloadCancellation.Token);

            statusLabel.Text = "Starting installer...";
            progressBar.Style = ProgressBarStyle.Marquee;
            await Task.Yield();

            await updateService.LaunchVerifiedInstallerAsync(download, downloadCancellation.Token);
            InstallerLaunched = true;
            busy = false;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (OperationCanceledException)
        {
            statusLabel.Text = "Download canceled.";
            ResetButtons();
        }
        catch (Exception ex)
        {
            PetDiagnostics.Error("UPDATE_ERROR", ex);
            statusLabel.Text = "The update was not installed.";
            MessageBox.Show(
                ex is UpdateException ? ex.Message : "ClippyCat couldn't complete the update.",
                "ClippyCat Update",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            ResetButtons();
        }
        finally
        {
            downloadCancellation?.Dispose();
            downloadCancellation = null;
        }
    }

    private void LaterButton_Click(object? sender, EventArgs e)
    {
        if (busy)
        {
            laterButton.Enabled = false;
            statusLabel.Text = "Canceling download...";
            downloadCancellation?.Cancel();
        }
    }

    private void UpdateDialog_FormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!busy)
            return;

        e.Cancel = true;
        laterButton.Enabled = false;
        statusLabel.Text = "Canceling download...";
        downloadCancellation?.Cancel();
    }

    private void ResetButtons()
    {
        busy = false;
        installButton.Enabled = true;
        laterButton.Enabled = true;
        laterButton.Text = "Later";
        laterButton.DialogResult = DialogResult.Cancel;
        progressBar.Visible = false;
    }
}
