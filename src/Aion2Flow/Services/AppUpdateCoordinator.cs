using Velopack;
using Velopack.Sources;

namespace Cloris.Aion2Flow.Services;

internal interface IAppUpdateEndpoint
{
    string Name { get; }

    bool IsManaged { get; }

    VelopackAsset? UpdatePendingRestart { get; }

    Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken);

    Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken);

    void StageForExit(VelopackAsset asset, bool restart);
}

internal sealed class VelopackUpdateEndpoint : IAppUpdateEndpoint
{
    private readonly UpdateManager _manager;

    public VelopackUpdateEndpoint(string name, IUpdateSource source)
    {
        Name = name;
        _manager = new UpdateManager(source);
    }

    public string Name { get; }

    public bool IsManaged => _manager.IsInstalled || _manager.IsPortable;

    public VelopackAsset? UpdatePendingRestart => _manager.UpdatePendingRestart;

    public async Task<UpdateInfo?> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var update = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return update;
    }

    public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellationToken)
    {
        return _manager.DownloadUpdatesAsync(update, progress, cancellationToken);
    }

    public void StageForExit(VelopackAsset asset, bool restart)
    {
        _manager.WaitExitThenApplyUpdates(asset, true, restart, []);
    }
}

internal sealed class AppUpdateCoordinator
{
    private readonly IAppUpdateEndpoint[] _endpoints;

    public AppUpdateCoordinator(IAppUpdateEndpoint[] endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentOutOfRangeException.ThrowIfZero(endpoints.Length);
        _endpoints = [.. endpoints];
    }

    public bool IsManaged => _endpoints[0].IsManaged;

    public async Task<AppUpdateCandidate?> CheckAsync(CancellationToken cancellationToken)
    {
        var failures = new List<Exception>(_endpoints.Length);
        for (var i = 0; i < _endpoints.Length; i++)
        {
            var endpoint = _endpoints[i];
            try
            {
                var update = await endpoint.CheckAsync(cancellationToken).ConfigureAwait(false);
                return update is null ? null : new AppUpdateCandidate(endpoint, i, update);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException($"{endpoint.Name} update check failed.", ex));
            }
        }

        throw new AggregateException("All configured update sources failed.", failures);
    }

    public async Task<PreparedAppUpdate> DownloadAsync(
        AppUpdateCandidate initialCandidate,
        Action<int> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(progress);

        var failures = new List<Exception>(_endpoints.Length - initialCandidate.EndpointIndex);
        var targetRelease = initialCandidate.Update.TargetFullRelease;
        for (var i = initialCandidate.EndpointIndex; i < _endpoints.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var candidate = initialCandidate;
            if (i != initialCandidate.EndpointIndex)
            {
                var endpoint = _endpoints[i];
                try
                {
                    var fallbackUpdate = await endpoint.CheckAsync(cancellationToken).ConfigureAwait(false);
                    if (fallbackUpdate is null || !MatchesRelease(fallbackUpdate.TargetFullRelease, targetRelease))
                    {
                        throw new InvalidOperationException(
                            $"{endpoint.Name} does not contain the expected {targetRelease.PackageId} update version {targetRelease.Version}.");
                    }

                    candidate = new AppUpdateCandidate(endpoint, i, fallbackUpdate);
                    progress(0);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failures.Add(new InvalidOperationException($"{endpoint.Name} backup lookup failed.", ex));
                    continue;
                }
            }

            try
            {
                await candidate.Endpoint.DownloadAsync(candidate.Update, progress, cancellationToken).ConfigureAwait(false);
                return new PreparedAppUpdate(candidate.Endpoint, candidate.Update.TargetFullRelease);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures.Add(new InvalidOperationException($"{candidate.Endpoint.Name} update download failed.", ex));
            }
        }

        throw new AggregateException(
            $"Unable to download {targetRelease.PackageId} update version {targetRelease.Version} from any configured source.",
            failures);
    }

    public PreparedAppUpdate? ResolvePending(PreparedAppUpdate? downloadedThisSession)
    {
        if (downloadedThisSession is { } prepared)
            return prepared;

        return _endpoints[0].UpdatePendingRestart is { } pending
            ? new PreparedAppUpdate(_endpoints[0], pending)
            : null;
    }

    private static bool MatchesRelease(VelopackAsset candidate, VelopackAsset expected)
    {
        return string.Equals(candidate.PackageId, expected.PackageId, StringComparison.Ordinal) &&
               Equals(candidate.Version, expected.Version);
    }
}

internal readonly record struct AppUpdateCandidate(
    IAppUpdateEndpoint Endpoint,
    int EndpointIndex,
    UpdateInfo Update);

internal readonly record struct PreparedAppUpdate(
    IAppUpdateEndpoint Endpoint,
    VelopackAsset Asset);
