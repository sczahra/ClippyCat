using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MarmaladeDesktopPet;

internal sealed class UpdateService
{
    internal const string LatestReleaseEndpoint =
        "https://api.github.com/repos/sczahra/ClippyCat/releases/latest";

    private const long MaximumInstallerBytes = 512L * 1024 * 1024;
    private const long MaximumChecksumBytes = 64L * 1024;
    private const int MaximumReleaseMetadataCharacters = 2_000_000;

    public static Version CurrentVersion { get; } = ReadCurrentVersion();
    public static string CurrentVersionText => FormatVersion(CurrentVersion);
    private static readonly HttpClient HttpClient = CreateHttpClient();

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        PetDiagnostics.Log("UPDATE_CHECK", $"started current={CurrentVersionText}");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseEndpoint);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");

            using HttpResponseMessage response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            response.EnsureSuccessStatusCode();
            string json = await response.Content.ReadAsStringAsync(timeout.Token);

            if (json.Length > MaximumReleaseMetadataCharacters)
                throw new UpdateException("GitHub returned release metadata that was unexpectedly large.");

            UpdateInfo? update = ParseReleaseJson(json, CurrentVersion);

            if (update is null)
                PetDiagnostics.Log("UPDATE_CHECK", $"current={CurrentVersionText} result=no-newer-stable-release");
            else
                PetDiagnostics.Log(
                    "UPDATE_AVAILABLE",
                    $"current={CurrentVersionText} remote={FormatVersion(update.AvailableVersion)}");

            return update;
        }
        catch (UpdateException)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new UpdateException("The update check timed out. Please try again later.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new UpdateException("ClippyCat couldn't contact GitHub Releases. Please try again later.", ex);
        }
        catch (JsonException ex)
        {
            throw new UpdateException("GitHub returned release information ClippyCat couldn't read.", ex);
        }
    }

    public async Task<UpdateDownloadResult> DownloadAndVerifyAsync(
        UpdateInfo update,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        string updateDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MarmaladeDesktopPet",
            "Updates",
            FormatVersion(update.AvailableVersion));

        Directory.CreateDirectory(updateDirectory);

        string installerPath = Path.Combine(updateDirectory, update.InstallerFileName);
        string checksumPath = Path.Combine(updateDirectory, update.ChecksumFileName);
        string installerPartialPath = installerPath + ".download";
        string checksumPartialPath = checksumPath + ".download";

        TryDelete(installerPartialPath);
        TryDelete(checksumPartialPath);

        PetDiagnostics.Log(
            "UPDATE_DOWNLOAD",
            $"started version={FormatVersion(update.AvailableVersion)} file={update.InstallerFileName}");

        try
        {
            await DownloadFileAsync(
                update.ChecksumUri,
                checksumPartialPath,
                MaximumChecksumBytes,
                progress: null,
                cancellationToken);

            string checksumText = await File.ReadAllTextAsync(checksumPartialPath, cancellationToken);
            if (!TryParseChecksum(checksumText, update.InstallerFileName, out string expectedSha256))
                throw new UpdateException("The release checksum file is malformed.");

            await DownloadFileAsync(
                update.InstallerUri,
                installerPartialPath,
                MaximumInstallerBytes,
                progress,
                cancellationToken);

            long installerBytes = new FileInfo(installerPartialPath).Length;
            if (installerBytes == 0)
                throw new UpdateException("The downloaded installer is empty.");

            string actualSha256 = await ComputeSha256Async(installerPartialPath, cancellationToken);
            PetDiagnostics.Log(
                "UPDATE_DOWNLOAD",
                $"completed version={FormatVersion(update.AvailableVersion)} bytes={installerBytes}");

            if (!actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                PetDiagnostics.Log(
                    "UPDATE_HASH_MISMATCH",
                    $"version={FormatVersion(update.AvailableVersion)} expected={expectedSha256} actual={actualSha256}");
                throw new UpdateException("The downloaded installer failed its SHA-256 integrity check.");
            }

            PetDiagnostics.Log(
                "UPDATE_HASH_VERIFIED",
                $"version={FormatVersion(update.AvailableVersion)} sha256={actualSha256}");

            File.Move(checksumPartialPath, checksumPath, overwrite: true);
            File.Move(installerPartialPath, installerPath, overwrite: true);
            progress?.Report(100);

            return new UpdateDownloadResult(installerPath, expectedSha256, actualSha256);
        }
        catch
        {
            TryDelete(installerPartialPath);
            TryDelete(checksumPartialPath);
            throw;
        }
    }

    public async Task LaunchVerifiedInstallerAsync(
        UpdateDownloadResult download,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(download.InstallerPath))
            throw new UpdateException("The verified installer file is no longer available.");

        string actualSha256 = await ComputeSha256Async(download.InstallerPath, cancellationToken);
        if (!actualSha256.Equals(download.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            PetDiagnostics.Log(
                "UPDATE_HASH_MISMATCH",
                $"stage=prelaunch expected={download.ExpectedSha256} actual={actualSha256}");
            TryDelete(download.InstallerPath);
            throw new UpdateException("The installer changed after download and will not be opened.");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = download.InstallerPath,
                WorkingDirectory = Path.GetDirectoryName(download.InstallerPath) ?? AppContext.BaseDirectory,
                UseShellExecute = true
            };

            using Process? process = Process.Start(startInfo);
            if (process is null)
                throw new InvalidOperationException("Windows did not start the installer process.");

            PetDiagnostics.Log("UPDATE_INSTALLER", $"launched file={Path.GetFileName(download.InstallerPath)}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            throw new UpdateException("Windows couldn't launch the ClippyCat installer.", ex);
        }
    }

    internal static UpdateInfo? ParseReleaseJson(string json, Version installedVersion)
    {
        GitHubRelease? release = JsonSerializer.Deserialize<GitHubRelease>(json);
        if (release is null)
            throw new UpdateException("GitHub returned empty release information.");

        if (release.Draft || release.Prerelease)
            return null;

        if (!TryParseReleaseVersion(release.TagName, out Version availableVersion))
            throw new UpdateException("The latest release has an invalid version tag.");

        if (availableVersion.CompareTo(installedVersion) <= 0)
            return null;

        string versionText = FormatVersion(availableVersion);
        string installerFileName = $"ClippyCatSetup-{versionText}.exe";
        string checksumFileName = installerFileName + ".sha256";

        List<GitHubReleaseAsset> assets = release.Assets ?? new List<GitHubReleaseAsset>();
        GitHubReleaseAsset[] installerAssets = assets
            .Where(asset => string.Equals(asset.Name, installerFileName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();
        GitHubReleaseAsset[] checksumAssets = assets
            .Where(asset => string.Equals(asset.Name, checksumFileName, StringComparison.OrdinalIgnoreCase))
            .Take(2)
            .ToArray();

        if (installerAssets.Length != 1 || checksumAssets.Length != 1)
            throw new UpdateException("The latest release is missing its matching installer or SHA-256 file.");

        GitHubReleaseAsset installerAsset = installerAssets[0];
        GitHubReleaseAsset checksumAsset = checksumAssets[0];

        if (!TryCreateTrustedDownloadUri(installerAsset.BrowserDownloadUrl, out Uri? installerUri) ||
            !TryCreateTrustedDownloadUri(checksumAsset.BrowserDownloadUrl, out Uri? checksumUri))
        {
            throw new UpdateException("The latest release contains an invalid download address.");
        }

        return new UpdateInfo(
            installedVersion,
            availableVersion,
            BuildReleaseNotesSummary(release.Body),
            installerUri!,
            checksumUri!,
            installerFileName,
            checksumFileName);
    }

    internal static bool TryParseReleaseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);

        if (string.IsNullOrWhiteSpace(tag))
            return false;

        string normalized = tag.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
            normalized = normalized[1..];

        if (normalized.Split('.').Length != 3 ||
            !Version.TryParse(normalized, out Version? parsed) ||
            parsed.Build < 0 ||
            parsed.Revision >= 0)
        {
            return false;
        }

        version = new Version(parsed.Major, parsed.Minor, parsed.Build);
        return true;
    }

    internal static bool TryParseChecksum(
        string contents,
        string expectedFileName,
        out string sha256)
    {
        sha256 = string.Empty;
        string[] lines = contents
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length != 1)
            return false;

        Match match = Regex.Match(
            lines[0],
            "^(?<hash>[A-Fa-f0-9]{64})(?:\\s+\\*?(?<file>\\S+))?\\s*$",
            RegexOptions.CultureInvariant);

        if (!match.Success)
            return false;

        string fileName = match.Groups["file"].Value;
        if (fileName.Length > 0 &&
            !Path.GetFileName(fileName).Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sha256 = match.Groups["hash"].Value.ToUpperInvariant();
        return true;
    }

    internal static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    internal static async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash);
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"ClippyCat/{CurrentVersionText}");
        return client;
    }

    private static Version ReadCurrentVersion()
    {
        Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
        return version is null
            ? new Version(0, 0, 0)
            : new Version(version.Major, version.Minor, Math.Max(0, version.Build));
    }

    private static async Task DownloadFileAsync(
        Uri uri,
        string destinationPath,
        long maximumBytes,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await HttpClient.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        long? contentLength = response.Content.Headers.ContentLength;
        if (contentLength.HasValue && contentLength.Value > maximumBytes)
            throw new UpdateException("The update download is unexpectedly large.");

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream destination = new(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81920,
            useAsync: true);

        byte[] buffer = new byte[81920];
        long totalBytes = 0;
        int read;

        while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
        {
            totalBytes += read;
            if (totalBytes > maximumBytes)
                throw new UpdateException("The update download exceeded its safe size limit.");

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

            if (contentLength is > 0 && progress is not null)
                progress.Report((int)Math.Clamp(totalBytes * 100 / contentLength.Value, 0, 99));
        }

        await destination.FlushAsync(cancellationToken);
    }

    private static bool TryCreateTrustedDownloadUri(string value, out Uri? uri)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
            !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            uri = null;
            return false;
        }

        bool trustedHost = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);

        if (!trustedHost)
            uri = null;

        return trustedHost;
    }

    private static string BuildReleaseNotesSummary(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "No release notes were provided.";

        string text = WebUtility.HtmlDecode(body);
        text = Regex.Replace(text, "<[^>]+>", string.Empty, RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "!?\\[([^\\]]+)\\]\\([^\\)]+\\)", "$1", RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "^[#>*+`_-]+\\s*", string.Empty, RegexOptions.Multiline | RegexOptions.CultureInvariant);
        text = Regex.Replace(text, "[ \\t]+", " ", RegexOptions.CultureInvariant).Trim();

        const int maximumLength = 1200;
        return text.Length <= maximumLength ? text : text[..maximumLength].TrimEnd() + "…";
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // Cleanup failures must not hide the original updater error.
        }
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = string.Empty;

        [JsonPropertyName("draft")]
        public bool Draft { get; set; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset>? Assets { get; set; } = new();
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = string.Empty;
    }
}

internal sealed record UpdateInfo(
    Version InstalledVersion,
    Version AvailableVersion,
    string ReleaseNotes,
    Uri InstallerUri,
    Uri ChecksumUri,
    string InstallerFileName,
    string ChecksumFileName);

internal sealed record UpdateDownloadResult(
    string InstallerPath,
    string ExpectedSha256,
    string ActualSha256);

internal sealed class UpdateException : Exception
{
    public UpdateException(string message)
        : base(message)
    {
    }

    public UpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
