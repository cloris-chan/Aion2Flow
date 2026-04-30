using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Cloris.Aion2Flow.Services.Logging;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;
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
    private const string RepositoryUrl = "https://github.com/cloris-chan/Aion2Flow";

    private readonly UpdateManager _updateManager;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _syncRoot = new();

    private Task? _activeTask;
    private VelopackAsset? _pendingUpdate;
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
        _updateManager = new UpdateManager(new GithubSource(RepositoryUrl, string.Empty, IsPrereleaseBuild()));
        CurrentVersion = VelopackLocator.Current.CurrentlyInstalledVersion?.ToString();
    }

    public string? CurrentVersion { get; }

    public bool IsManagedByVelopack => _updateManager.IsInstalled || _updateManager.IsPortable;

    public void Start() => StartWorkflow();

    public void CheckForUpdates() => StartWorkflow();

    public Task RestartAsync()
    {
        var pending = _pendingUpdate ?? _updateManager.UpdatePendingRestart;
        if (pending is null)
        {
            return Task.CompletedTask;
        }

        return Task.Run(() =>
        {
            try
            {
                _updateManager.WaitExitThenApplyUpdates(pending, true, true, []);
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

        var updateToApply = _pendingUpdate ?? _updateManager.UpdatePendingRestart;
        if (updateToApply is null)
        {
            return;
        }

        try
        {
            _updateManager.WaitExitThenApplyUpdates(updateToApply, true, false, []);
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
        try
        {
            UpdateState(AppUpdateState.Checking, progress: 0, message: null);

            var update = await _updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (update is null)
            {
                UpdateState(AppUpdateState.UpToDate, progress: 0, message: null, version: null, clearVersion: true);
                return;
            }

            var version = update.TargetFullRelease.Version.ToString();
            UpdateState(AppUpdateState.Downloading, progress: 0, message: null, version: version);

            await _updateManager.DownloadUpdatesAsync(
                update,
                pct => Dispatcher.UIThread.Post(() => DownloadProgress = pct),
                cancellationToken).ConfigureAwait(false);

            _pendingUpdate = update.TargetFullRelease;
            UpdateState(AppUpdateState.ReadyToRestart, progress: 100, message: null, version: version);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            AppLog.Write(AppLogLevel.Warning, $"Velopack update check failed: {ex}");
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

    private static void RequestShutdown()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
            return;
        }

        Environment.Exit(0);
    }
}
