using System.Diagnostics;
using Velopack;
using Velopack.Locators;
using Velopack.Sources;

namespace Cloris.Aion2Flow.Services;

public sealed class AppUpdateService
{
    private const string RepositoryUrl = "https://github.com/cloris-chan/Aion2Flow";

    private readonly UpdateManager _updateManager;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Lock _syncRoot = new();

    private Task? _updateTask;
    private VelopackAsset? _pendingUpdate;

    public AppUpdateService()
    {
        _updateManager = new UpdateManager(new GithubSource(RepositoryUrl, string.Empty, IsPrereleaseBuild()));
    }

    public void Start()
    {
        if (!_updateManager.IsInstalled && !_updateManager.IsPortable)
        {
            return;
        }

        lock (_syncRoot)
        {
            _updateTask ??= Task.Run(() => DownloadLatestUpdateAsync(_shutdown.Token));
        }
    }

    public void PreparePendingUpdateForShutdown()
    {
        _shutdown.Cancel();

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
            Trace.TraceWarning($"Velopack failed to stage the pending update during shutdown: {ex}");
        }
    }

    private async Task DownloadLatestUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var update = await _updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            await _updateManager.DownloadUpdatesAsync(update, null, cancellationToken).ConfigureAwait(false);
            _pendingUpdate = update.TargetFullRelease;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Velopack update check failed: {ex}");
        }
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
}
