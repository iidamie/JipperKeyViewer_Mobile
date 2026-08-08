using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.IO.Compression;
using System.Numerics;
using ImGuiNET;
using StArray.ModManager.Manager;

namespace JipperKeyViewer.Mobile;

/// <summary>
/// Checks the mobile releases on GitHub and applies the packaged mod update.
/// The current plugin remains loaded; the new files take effect after a restart.
/// </summary>
internal sealed class GitHubUpdateService : IDisposable
{
    private const string LogTag = "JipperKeyViewer/Updater";
    private const string RepositoryOwner = "iidamie";
    private const string RepositoryName = "JipperKeyViewer_Mobile";
    private const string PackagePrefix = "JipperKeyViewer-";
    private const string PackageRoot = "JipperKeyViewer/";
    private const long MaxPackageBytes = 32L * 1024 * 1024;
    private const long MaxFileBytes = 16L * 1024 * 1024;
    private static readonly string[] RequiredFiles =
    {
        "JipperKeyViewer.dll",
    };

    private readonly string _modDirectory;
    private readonly string _currentVersion;
    private readonly HttpClient _client;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateLock = new();

    private UpdateState _state;
    private string _status = "";
    private ReleaseInfo? _release;
    private string _notification = "";
    private DateTime _notificationUntilUtc;
    private bool _disposed;

    internal GitHubUpdateService(string modDirectory, string currentVersion)
    {
        _modDirectory = Path.GetFullPath(modDirectory);
        _currentVersion = currentVersion;
        _client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30),
        };
        _client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("JipperKeyViewer-Mobile", currentVersion));
        _client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    internal void StartAutomaticCheck() => StartCheck();

    internal void CheckNow() => StartCheck();

    internal void DownloadUpdate()
    {
        ReleaseInfo? release;
        lock (_stateLock)
        {
            if (_disposed || _state != UpdateState.Available || _release == null)
                return;

            release = _release;
            _state = UpdateState.Downloading;
            _status = $"Downloading {release.Version}...";
        }

        _ = DownloadAndInstallAsync(release, _lifetime.Token);
    }

    internal void DrawGui()
    {
        UpdateSnapshot snapshot = GetSnapshot();
        if (snapshot.State == UpdateState.Idle)
            return;

        ImGui.Separator();
        ImGui.TextUnformatted("Updates");

        switch (snapshot.State)
        {
            case UpdateState.Checking:
            case UpdateState.Downloading:
                ImGui.TextDisabled(snapshot.Status);
                break;
            case UpdateState.Available:
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.78f, 0.25f, 1f), snapshot.Status);
                if (ImGui.Button("Download and install update##jipper-update"))
                    DownloadUpdate();
                break;
            case UpdateState.ReadyToRestart:
                ImGui.TextColored(new System.Numerics.Vector4(0.45f, 1f, 0.65f, 1f), snapshot.Status);
                break;
            case UpdateState.UpToDate:
                ImGui.TextDisabled(snapshot.Status);
                break;
            case UpdateState.Failed:
                ImGui.TextColored(new System.Numerics.Vector4(1f, 0.4f, 0.4f, 1f), snapshot.Status);
                break;
        }

        if (snapshot.State is not (UpdateState.Checking or UpdateState.Downloading or UpdateState.ReadyToRestart)
            && ImGui.Button("Check for updates##jipper-check-update"))
        {
            CheckNow();
        }
    }

    /// <summary>
    /// Shows the short, non-interactive foreground notification used when a
    /// release becomes available. The download action remains in the mod page.
    /// </summary>
    internal void DrawForegroundNotification()
    {
        string notification;
        lock (_stateLock)
        {
            if (string.IsNullOrEmpty(_notification))
                return;
            if (DateTime.UtcNow >= _notificationUntilUtc)
            {
                _notification = "";
                _notificationUntilUtc = default;
                return;
            }
            notification = _notification;
        }

        Vector2 display = ImGui.GetIO().DisplaySize;
        if (display.X < 180f || display.Y < 100f)
            return;

        ImGuiStylePtr style = ImGui.GetStyle();
        float margin = GetOverlayMargin();
        float desiredWidth = ImGui.CalcTextSize(notification).X + style.WindowPadding.X * 2f;
        float width = ClampOverlayWidth(display.X, margin, 420f, desiredWidth);
        float wrapWidth = Math.Max(1f, width - style.WindowPadding.X * 2f);
        float textHeight = ImGui.CalcTextSize(notification, false, wrapWidth).Y;
        Vector2 size = new(width, Math.Max(64f, textHeight + style.WindowPadding.Y * 2f));
        ImGui.SetNextWindowPos(new Vector2((display.X - size.X) * 0.5f, margin), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.94f);
        ImGuiWindowFlags flags = ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoMove
            | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoInputs;
        if (ImGui.Begin("##JipperKeyViewerUpdateNotification", flags))
            ImGui.TextWrapped(notification);
        ImGui.End();
    }

    private static float GetOverlayMargin()
    {
        return Math.Max(18f, ImGui.GetFontSize() * 0.65f);
    }

    private static float ClampOverlayWidth(float displayWidth, float margin, float minimum, float desired)
    {
        float available = Math.Max(1f, displayWidth - margin * 2f);
        return Math.Min(available, Math.Max(Math.Min(minimum, available), desired));
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
                return;
            _disposed = true;
        }

        _lifetime.Cancel();
        _client.Dispose();
        _lifetime.Dispose();
    }

    private void StartCheck()
    {
        lock (_stateLock)
        {
            if (_disposed || _state is UpdateState.Checking or UpdateState.Downloading)
                return;
            _state = UpdateState.Checking;
            _status = "Checking GitHub releases...";
            _release = null;
            _notification = "";
            _notificationUntilUtc = default;
        }

        _ = CheckLatestAsync(_lifetime.Token);
    }

    private async Task CheckLatestAsync(CancellationToken token)
    {
        try
        {
            // The repository also publishes PC packages, so the newest GitHub
            // release is not necessarily the newest release containing a mobile package.
            string url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases?per_page=50";
            using HttpResponseMessage response = await _client.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                token);
            response.EnsureSuccessStatusCode();
            await using Stream stream = await response.Content.ReadAsStreamAsync(token);
            using JsonDocument document = await JsonDocument.ParseAsync(stream, cancellationToken: token);

            ReleaseInfo release = ParseLatestMobileRelease(document.RootElement);
            bool updateAvailable = CompareVersions(release.Version, _currentVersion) > 0;
            lock (_stateLock)
            {
                if (_disposed)
                    return;
                _release = updateAvailable ? release : null;
                _state = updateAvailable ? UpdateState.Available : UpdateState.UpToDate;
                _status = updateAvailable
                    ? $"Update available: {release.Version}"
                    : $"Up to date ({_currentVersion})";
                if (updateAvailable)
                {
                    _notification =
                        $"Jipper Key Viewer update available: {release.Version}. Open Mod settings to download.";
                    _notificationUntilUtc = DateTime.UtcNow.AddSeconds(4);
                }
            }

            if (updateAvailable)
            {
                Logger.Warn(LogTag, $"Update available: {_currentVersion} -> {release.Version}");
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_stateLock)
            {
                if (_disposed)
                    return;
                _state = UpdateState.Failed;
                _status = $"Update check failed: {exception.Message}";
            }
            Logger.Warn(LogTag, $"Release check failed: {exception.Message}");
        }
    }

    private async Task DownloadAndInstallAsync(ReleaseInfo release, CancellationToken token)
    {
        string id = Guid.NewGuid().ToString("N");
        string archivePath = Path.Combine(_modDirectory, $".jipper-update-{id}.zip");
        string stagingDirectory = Path.Combine(_modDirectory, $".jipper-update-{id}");

        try
        {
            Directory.CreateDirectory(_modDirectory);
            using HttpResponseMessage response = await _client.GetAsync(
                release.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                token);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is > MaxPackageBytes)
                throw new InvalidDataException("The update package is too large");

            await using (Stream input = await response.Content.ReadAsStreamAsync(token))
            await using (FileStream output = new(
                archivePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous))
            {
                await CopyWithLimitAsync(input, output, MaxPackageBytes, token);
            }

            Directory.CreateDirectory(stagingDirectory);
            await ExtractPackageAsync(archivePath, stagingDirectory, token);
            ValidateAssembly(Path.Combine(stagingDirectory, "JipperKeyViewer.dll"));
            InstallStagedFiles(stagingDirectory);

            lock (_stateLock)
            {
                if (!_disposed)
                {
                    _state = UpdateState.ReadyToRestart;
                    _status = $"Updated to {release.Version}. Restart the game to apply it.";
                    _notification = _status;
                    _notificationUntilUtc = DateTime.UtcNow.AddSeconds(4);
                }
            }
            Logger.Info(LogTag, $"Updated JipperKeyViewer to {release.Version}; restart the game to apply it");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            lock (_stateLock)
            {
                if (!_disposed)
                {
                    _state = UpdateState.Failed;
                    _status = $"Update failed: {exception.Message}";
                }
            }
            Logger.Error(LogTag, $"Update installation failed: {exception}");
        }
        finally
        {
            TryDeleteFile(archivePath);
            TryDeleteDirectory(stagingDirectory);
        }
    }

    private static ReleaseInfo ParseLatestMobileRelease(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("GitHub releases response is not an array");

        ReleaseInfo? latest = null;
        foreach (JsonElement release in root.EnumerateArray())
        {
            ReleaseInfo? candidate = TryParseMobileRelease(release);
            if (candidate == null)
                continue;
            if (latest == null || CompareVersions(candidate.Version, latest.Version) > 0)
                latest = candidate;
        }

        return latest ?? throw new InvalidDataException(
            "GitHub releases do not contain a JipperKeyViewer mobile zip");
    }

    private static ReleaseInfo? TryParseMobileRelease(JsonElement root)
    {
        if (!root.TryGetProperty("tag_name", out JsonElement tagElement))
            return null;
        string tag = tagElement.GetString() ?? "";
        if (string.IsNullOrWhiteSpace(tag))
            return null;

        string? downloadUrl = null;
        if (root.TryGetProperty("assets", out JsonElement assets)
            && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement asset in assets.EnumerateArray())
            {
                if (!asset.TryGetProperty("name", out JsonElement name))
                    continue;
                string assetName = name.GetString() ?? "";
                bool packageName = assetName.StartsWith(PackagePrefix, StringComparison.OrdinalIgnoreCase)
                    && assetName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && assetName.Contains("mobile", StringComparison.OrdinalIgnoreCase);
                if (!packageName) continue;
                if (asset.TryGetProperty("browser_download_url", out JsonElement url))
                    downloadUrl = url.GetString();
                break;
            }
        }

        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? packageUri)
            || packageUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        return new ReleaseInfo(tag, NormalizeVersionText(tag), packageUri);
    }

    private async Task ExtractPackageAsync(
        string archivePath,
        string stagingDirectory,
        CancellationToken token)
    {
        await using FileStream stream = new(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using ZipArchive archive = new(stream, ZipArchiveMode.Read);

        foreach (string fileName in RequiredFiles)
        {
            ZipArchiveEntry? entry = archive.GetEntry(PackageRoot + fileName);
            if (entry == null)
                throw new InvalidDataException($"Update package is missing {PackageRoot}{fileName}");
            if (entry.Length < 0 || entry.Length > MaxFileBytes)
                throw new InvalidDataException($"Update file is too large: {fileName}");

            string destination = Path.GetFullPath(Path.Combine(stagingDirectory, fileName));
            string root = Path.GetFullPath(stagingDirectory).TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Invalid update file path");

            await using Stream input = entry.Open();
            await using FileStream output = new(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous);
            await input.CopyToAsync(output, 81920, token);
        }
    }

    private void InstallStagedFiles(string stagingDirectory)
    {
        string backupDirectory = Path.Combine(
            _modDirectory,
            $".jipper-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(backupDirectory);

        List<InstalledFile> files = RequiredFiles
            .Select(fileName => new InstalledFile(
                Path.Combine(stagingDirectory, fileName),
                Path.Combine(_modDirectory, fileName),
                Path.Combine(backupDirectory, fileName),
                File.Exists(Path.Combine(_modDirectory, fileName))))
            .ToList();

        try
        {
            foreach (InstalledFile file in files)
            {
                if (file.HadOriginal)
                    File.Copy(file.Target, file.Backup, true);
            }

            foreach (InstalledFile file in files)
                File.Move(file.Staged, file.Target, true);
        }
        catch
        {
            foreach (InstalledFile file in files.AsEnumerable().Reverse())
            {
                try
                {
                    if (file.HadOriginal && File.Exists(file.Backup))
                        File.Copy(file.Backup, file.Target, true);
                    else if (!file.HadOriginal && File.Exists(file.Target))
                        File.Delete(file.Target);
                }
                catch (Exception restoreException)
                {
                    Logger.Error(LogTag, $"Could not restore {Path.GetFileName(file.Target)}: {restoreException.Message}");
                }
            }
            throw;
        }
        finally
        {
            TryDeleteDirectory(backupDirectory);
        }
    }

    private static void ValidateAssembly(string path)
    {
        if (!File.Exists(path))
            throw new InvalidDataException("The update DLL was not extracted");

        AssemblyName? assembly = AssemblyName.GetAssemblyName(path);
        if (!string.Equals(assembly.Name, "JipperKeyViewer", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The update DLL is not JipperKeyViewer");
    }

    private static async Task CopyWithLimitAsync(
        Stream input,
        Stream output,
        long limit,
        CancellationToken token)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await input.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            total += count;
            if (total > limit)
                throw new InvalidDataException("The update package is too large");
            await output.WriteAsync(buffer.AsMemory(0, count), token);
        }
    }

    private static int CompareVersions(string left, string right)
    {
        int[] leftParts = ExtractNumericParts(left);
        int[] rightParts = ExtractNumericParts(right);
        int count = Math.Max(leftParts.Length, rightParts.Length);
        for (int index = 0; index < count; index++)
        {
            int leftPart = index < leftParts.Length ? leftParts[index] : 0;
            int rightPart = index < rightParts.Length ? rightParts[index] : 0;
            if (leftPart != rightPart)
                return leftPart.CompareTo(rightPart);
        }
        return string.Compare(
            NormalizeVersionText(left),
            NormalizeVersionText(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static int[] ExtractNumericParts(string value)
    {
        List<int> parts = new();
        int current = 0;
        bool reading = false;
        foreach (char character in value)
        {
            if (character is >= '0' and <= '9')
            {
                reading = true;
                current = Math.Min(999999, current * 10 + character - '0');
                continue;
            }

            if (reading)
            {
                parts.Add(current);
                current = 0;
                reading = false;
            }
        }
        if (reading)
            parts.Add(current);
        return parts.ToArray();
    }

    private static string NormalizeVersionText(string value)
    {
        string normalized = value.Trim();
        while (normalized.StartsWith("v", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[1..];
        return normalized;
    }

    private static UpdateSnapshot GetSnapshotUnsafe(
        UpdateState state,
        string status)
        => new(state, status);

    private UpdateSnapshot GetSnapshot()
    {
        lock (_stateLock)
            return GetSnapshotUnsafe(_state, _status);
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, true);
        }
        catch
        {
        }
    }

    private sealed record ReleaseInfo(string Tag, string Version, Uri DownloadUrl);

    private readonly record struct InstalledFile(
        string Staged,
        string Target,
        string Backup,
        bool HadOriginal);

    private readonly record struct UpdateSnapshot(UpdateState State, string Status);

    private enum UpdateState
    {
        Idle,
        Checking,
        UpToDate,
        Available,
        Downloading,
        ReadyToRestart,
        Failed,
    }
}
