using System.Reflection;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Cloris.Aion2Flow.Services.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack.Locators;
using Velopack.Sources;

namespace Cloris.Aion2Flow.Services;

public enum AppUpdateState
{
    Idle,
    Checking,
    UpToDate,
    Downloading,
    ReadyToRestart,
    Failed
}

public sealed partial class AppUpdateService : ObservableObject
{
    private const string GithubRepositoryUrl = "https://github.com/cloris-chan/Aion2Flow";
    private const string BackupUpdateFeedMetadataKey = "Aion2Flow.BackupUpdateFeedUrl";

    private readonly AppUpdateCoordinator? _updateCoordinator;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _syncRoot = new();

    private Task? _activeTask;
    private PreparedAppUpdate? _pendingUpdate;
    private volatile bool _restartUpdateRequested;

    [ObservableProperty]
    public partial AppUpdateState State { get; private set; } = AppUpdateState.Idle;

    [ObservableProperty]
    public partial int DownloadProgress { get; private set; }

    [ObservableProperty]
    public partial string? AvailableVersion { get; private set; }

    [ObservableProperty]
    public partial string? StatusMessage { get; private set; }

    public AppUpdateService()
    {
        try
        {
            var isPrereleaseBuild = IsPrereleaseBuild();
            UpdateSourceDescriptor[] sources;
            try
            {
                sources = CreateUpdateSources(ResolveBackupUpdateFeedUrl(), isPrereleaseBuild);
            }
            catch (InvalidOperationException ex)
            {
                AppLog.Write(AppLogLevel.Warning, $"Ignoring the invalid backup update feed: {ex.Message}");
                sources = CreateUpdateSources(backupUpdateFeedUrl: null, isPrereleaseBuild);
            }

            var endpoints = new List<IAppUpdateEndpoint>(sources.Length)
            {
                new VelopackUpdateEndpoint(sources[0].Name, sources[0].Source)
            };
            for (var i = 1; i < sources.Length; i++)
            {
                try
                {
                    endpoints.Add(new VelopackUpdateEndpoint(sources[i].Name, sources[i].Source));
                }
                catch (Exception ex)
                {
                    AppLog.Write(AppLogLevel.Warning, $"Ignoring the unavailable {sources[i].Name} update source: {ex.Message}");
                }
            }

            _updateCoordinator = new AppUpdateCoordinator([.. endpoints]);
            CurrentVersion = VelopackLocator.Current.CurrentlyInstalledVersion?.ToString();
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Debug, $"Velopack update service disabled: {ex.Message}");
            _updateCoordinator = null;
            CurrentVersion = null;
        }
    }

    public string? CurrentVersion { get; }

    public bool IsManagedByVelopack => _updateCoordinator?.IsManaged == true;

    private static UpdateSourceDescriptor[] CreateUpdateSources(string? backupUpdateFeedUrl, bool isPrereleaseBuild)
    {
        var github = new UpdateSourceDescriptor("GitHub", new GithubSource(GithubRepositoryUrl, string.Empty, isPrereleaseBuild));
        if (string.IsNullOrWhiteSpace(backupUpdateFeedUrl))
            return [github];

        var value = backupUpdateFeedUrl.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var feedUri) ||
            !string.Equals(feedUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.IsNullOrEmpty(feedUri.UserInfo) ||
            !string.IsNullOrEmpty(feedUri.Query) ||
            !string.IsNullOrEmpty(feedUri.Fragment))
        {
            throw new InvalidOperationException("The configured Aion2Flow update feed must be an absolute HTTPS directory URL without credentials, a query, or a fragment.");
        }

        return
        [
            github,
            new UpdateSourceDescriptor("S3 backup", new SimpleWebSource(new Uri($"{value.TrimEnd('/')}/", UriKind.Absolute)))
        ];
    }

    public void Start() => StartWorkflow();

    public void CheckForUpdates() => StartWorkflow();

    public Task RestartAsync()
    {
        var pending = ResolvePendingUpdate();
        if (pending is not { } update)
            return Task.CompletedTask;

        return Task.Run(() =>
        {
            try
            {
                update.Endpoint.StageForExit(update.Asset, restart: true);
                _restartUpdateRequested = true;
                Dispatcher.UIThread.Post(RequestShutdown);
            }
            catch (Exception ex)
            {
                AppLog.Write(AppLogLevel.Warning, $"Velopack restart-to-update failed: {ex}");
                Dispatcher.UIThread.Post(() =>
                {
                    State = AppUpdateState.Failed;
                    StatusMessage = ex.Message;
                });
            }
        });
    }

    public void PreparePendingUpdateForShutdown()
    {
        _shutdown.Cancel();

        if (_restartUpdateRequested)
        {
            return;
        }

        var pending = ResolvePendingUpdate();
        if (pending is not { } update)
            return;

        try
        {
            update.Endpoint.StageForExit(update.Asset, restart: false);
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, $"Velopack failed to stage the pending update during shutdown: {ex}");
        }
    }

    private void StartWorkflow()
    {
        if (!IsManagedByVelopack)
        {
            return;
        }

        lock (_syncRoot)
        {
            if (_activeTask is { IsCompleted: false })
            {
                return;
            }

            _activeTask = Task.Run(() => RunUpdateWorkflowAsync(_shutdown.Token));
        }
    }

    private async Task RunUpdateWorkflowAsync(CancellationToken cancellationToken)
    {
        var coordinator = _updateCoordinator;
        if (coordinator is null)
            return;

        try
        {
            UpdateState(AppUpdateState.Checking, progress: 0, message: null);

            var candidate = await coordinator.CheckAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate is not { } available)
            {
                UpdateState(AppUpdateState.UpToDate, progress: 0, message: null, version: null, clearVersion: true);
                return;
            }

            var version = available.Update.TargetFullRelease.Version.ToString();
            UpdateState(AppUpdateState.Downloading, progress: 0, message: null, version: version);

            var downloaded = await coordinator.DownloadAsync(
                available,
                pct => Dispatcher.UIThread.Post(() => DownloadProgress = pct),
                cancellationToken).ConfigureAwait(false);

            _pendingUpdate = downloaded;
            UpdateState(AppUpdateState.ReadyToRestart, progress: 100, message: null, version: version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, $"Velopack update workflow failed: {ex}");
            UpdateState(AppUpdateState.Failed, progress: 0, message: ex.Message);
        }
    }

    private void UpdateState(AppUpdateState state, int progress, string? message, string? version = null, bool clearVersion = false)
    {
        Dispatcher.UIThread.Post(() =>
        {
            State = state;
            DownloadProgress = progress;
            StatusMessage = message;
            if (version is not null)
            {
                AvailableVersion = version;
            }
            else if (clearVersion)
            {
                AvailableVersion = null;
            }
        });
    }

    private static bool IsPrereleaseBuild()
    {
        try
        {
            var version = VelopackLocator.Current.CurrentlyInstalledVersion?.ToString();
            return !string.IsNullOrWhiteSpace(version) && version.Contains('-', StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    private PreparedAppUpdate? ResolvePendingUpdate()
    {
        return _updateCoordinator?.ResolvePending(_pendingUpdate);
    }

    private static string? ResolveBackupUpdateFeedUrl()
    {
        foreach (var metadata in typeof(AppUpdateService).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
        {
            if (string.Equals(metadata.Key, BackupUpdateFeedMetadataKey, StringComparison.Ordinal))
            {
                return metadata.Value;
            }
        }

        return null;
    }

    private static void RequestShutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Environment.Exit(0);
    }

    private readonly record struct UpdateSourceDescriptor(string Name, IUpdateSource Source);
}
