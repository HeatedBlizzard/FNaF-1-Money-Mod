
using System.Diagnostics;
using System.Security.Cryptography;

namespace FNaFModInstaller;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
    }
}

public sealed class InstallerForm : Form
{
    private const string OriginalHash =
        "862CD7AB7C81B20A4E848888BC2493DDA0181759C39958A42CC5C3D02DFB195A";

    private const string ModifiedHash =
        "21A87674127EF3C02ACAA3F21B546BC0C1D48DB85A54ED366162AE6AD85DD35E";

    private readonly Button installButton = new()
    {
        Text = "Install FNaF Money Mod",
        AutoSize = true
    };

    private readonly Button restoreButton = new()
    {
        Text = "Restore Original",
        AutoSize = true
    };

    private readonly Label status = new()
    {
        Text = "Select FiveNightsAtFreddys.exe.",
        AutoSize = true,
        MaximumSize = new Size(440, 0)
    };

    public InstallerForm()
    {
        Text = "FNaF Money Mod Installer";
        ClientSize = new Size(490, 190);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };

        layout.Controls.Add(new Label
        {
            Text = "FNaF Money Mod",
            AutoSize = true,
            Font = new Font("Segoe UI", 14, FontStyle.Bold)
        });

        layout.Controls.Add(status);
        layout.Controls.Add(installButton);
        layout.Controls.Add(restoreButton);
        Controls.Add(layout);

        installButton.Click += async (_, _) => await InstallAsync();
        restoreButton.Click += (_, _) => Restore();
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string BackupPath =>
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "FNaFMoneyMod",
            "Original.exe");

    private static string? SelectGame()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select your Steam copy of FNaF 1",
            Filter = "FNaF 1 executable|FiveNightsAtFreddys.exe",
            FileName = "FiveNightsAtFreddys.exe",
            CheckFileExists = true
        };

        return dialog.ShowDialog() == DialogResult.OK
            ? dialog.FileName
            : null;
    }

    private static string RequireHash(string path, string expected)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Required file not found.", path);

        string actual = HashFile(path);

        if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"File verification failed:\n{path}\n\n" +
                $"Expected: {expected}\nActual: {actual}");

        return actual;
    }

    private async Task InstallAsync()
    {
        string? game = SelectGame();
        if (game is null) return;

        installButton.Enabled = false;
        restoreButton.Enabled = false;

        string? temp = null;

        try
        {
            string currentHash = HashFile(game);

            if (currentHash.Equals(
                ModifiedHash, StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "The mod is already installed.";
                return;
            }

            if (!currentHash.Equals(
                OriginalHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "This is not the supported original executable. " +
                    "No changes were made.");
            }

            string root = AppContext.BaseDirectory;
            string patch = Path.Combine(root, "FNaFMod.xdelta");
            string xdelta = Path.Combine(root, "xdelta3.exe");

            if (!File.Exists(patch) || !File.Exists(xdelta))
                throw new FileNotFoundException(
                    "FNaFMod.xdelta or xdelta3.exe is missing. " +
                    "Keep both files beside the installer.");

            Directory.CreateDirectory(
                Path.GetDirectoryName(BackupPath)!);

            if (File.Exists(BackupPath))
            {
                RequireHash(BackupPath, OriginalHash);
            }
            else
            {
                File.Copy(game, BackupPath);
                RequireHash(BackupPath, OriginalHash);
            }

            temp = Path.Combine(
                Path.GetDirectoryName(game)!,
                $".FNaFMod-{Guid.NewGuid():N}.tmp");

            status.Text = "Applying patch...";

            var info = new ProcessStartInfo
            {
                FileName = xdelta,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };

            info.ArgumentList.Add("-d");
            info.ArgumentList.Add("-s");
            info.ArgumentList.Add(game);
            info.ArgumentList.Add(patch);
            info.ArgumentList.Add(temp);

            using var process = Process.Start(info)
                ?? throw new InvalidOperationException(
                    "Could not start xdelta3.");

            Task<string> errorTask =
                process.StandardError.ReadToEndAsync();
            Task<string> outputTask =
                process.StandardOutput.ReadToEndAsync();

            await process.WaitForExitAsync();

            string error = await errorTask;
            string output = await outputTask;

            if (process.ExitCode != 0 || !File.Exists(temp))
                throw new InvalidOperationException(
                    $"Patch failed.\n{error}\n{output}");

            RequireHash(temp, ModifiedHash);

            // Recheck before replacing the game file.
            RequireHash(game, OriginalHash);

            // Temp file is in the game directory, on the same volume.
            File.Move(temp, game, true);
            temp = null;

            status.Text = "Mod installed successfully!";

            MessageBox.Show(
                "FNaF Money Mod is installed. Launch the game through Steam.",
                "Installation complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            status.Text = "Installation failed.";
            MessageBox.Show(
                ex.Message, "Installer error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            if (temp is not null && File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch { /* A leftover temporary file can be removed later. */ }
            }

            installButton.Enabled = true;
            restoreButton.Enabled = true;
        }
    }

    private void Restore()
    {
        string? game = SelectGame();
        if (game is null) return;

        try
        {
            if (!File.Exists(BackupPath))
                throw new FileNotFoundException(
                    "No backup exists. Reinstall FNaF 1 through Steam " +
                    "to restore the original executable.");

            RequireHash(BackupPath, OriginalHash);

            string current = HashFile(game);

            if (current.Equals(
                OriginalHash, StringComparison.OrdinalIgnoreCase))
            {
                status.Text = "The original game is already installed.";
                return;
            }

            if (!current.Equals(
                ModifiedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "The selected file is neither the expected original " +
                    "nor the expected mod. It was not changed.");

            string temp = Path.Combine(
                Path.GetDirectoryName(game)!,
                $".FNaFRestore-{Guid.NewGuid():N}.tmp");

            try
            {
                File.Copy(BackupPath, temp);
                RequireHash(temp, OriginalHash);
                RequireHash(game, ModifiedHash);
                File.Move(temp, game, true);
            }
            finally
            {
                if (File.Exists(temp)) File.Delete(temp);
            }

            status.Text = "Original game restored.";

            MessageBox.Show(
                "The original executable has been restored.",
                "Restoration complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            status.Text = "Restoration failed.";
            MessageBox.Show(
                ex.Message, "Restoration error",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
