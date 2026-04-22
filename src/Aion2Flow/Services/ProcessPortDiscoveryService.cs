using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using Cloris.Aion2Flow.Services.Logging;
using Cloris.Aion2Flow.WinDivert;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Networking.WinSock;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.System.Diagnostics.ToolHelp;


namespace Cloris.Aion2Flow.Services;

public sealed class ProcessPortDiscoveryService : IAsyncDisposable
{
    private enum PortEventType { Add, Remove }
    private readonly record struct PortPair(ushort LocalPort, ushort RemotePort);
    private readonly record struct QueueEventItem(long ExpiredAt, PortEventType Type, uint ProcessId, PortPair PortPair);

    private const string ProcessName = "Aion2";
    private const int PollInterval = 1000;
    private const int QueueExpiration = 2500;

    private readonly ConcurrentDictionary<uint, HashSet<PortPair>> _processPorts = new();

    private readonly ConcurrentQueue<QueueEventItem> _eventQueue = new();

    private volatile bool _snapshotDirty = true;
    private ImmutableArray<uint> _processIdsSnapshot = [];
    private ImmutableArray<ushort> _allPortsSnapshot = [];

    public ImmutableArray<uint> ProcessIds
    {
        get
        {
            if (_snapshotDirty) RebuildProcessIdsAllPortsSnapshot();
            return _processIdsSnapshot;
        }
    }

    public ImmutableArray<ushort> AllPorts
    {
        get
        {
            if (_snapshotDirty) RebuildProcessIdsAllPortsSnapshot();
            return _allPortsSnapshot;
        }
    }

    private CancellationTokenSource? _cts;
    private Task? _pollTask;
    private Task? _divertTask;
    private WinDivertSession? _divert;

    public bool IsMonitoring { get; private set; }

    public event Action<uint, ushort>? Discovered;
    public event Action<uint, ushort>? Removed;

    public Task StartAsync()
    {
        if (IsMonitoring) return Task.CompletedTask;

        _cts = new CancellationTokenSource();
        _divertTask = StartDivertPortCaptureLoop(_cts.Token);
        _pollTask = StartProcessPollLoop(_cts.Token);

        IsMonitoring = true;
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (_cts is null) return;

        _cts.Cancel();

        try
        {
            var tasks = new List<Task>();
            if (_pollTask is not null) tasks.Add(_pollTask);
            if (_divertTask is not null) tasks.Add(_divertTask);
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, $"Stop error: {ex.Message}"); }

        _divert?.Dispose();
        _divert = null;
        _processPorts.Clear();
        _cts.Dispose();
        _cts = null;
        IsMonitoring = false;
    }

    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    private Task StartDivertPortCaptureLoop(CancellationToken token)
    {
        _divert = new WinDivertSession("tcp", WinDivertLayer.Flow, WinDivertFlags.Sniff | WinDivertFlags.ReceiveOnly);

        return Task.Factory.StartNew(() =>
        {
            var address = new WinDivertAddress();
            var sw = Stopwatch.StartNew();
            var buffer = Span<byte>.Empty;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    _divert.Receive(buffer, ref address);
                    var addr = address;

                    if (!address.TryGetFlowData(out var flow) || flow.ProcessId == 0)
                        continue;

                    var eventType = addr.Event == WinDivertEvent.FlowEstablished ? PortEventType.Add :
                                    addr.Event == WinDivertEvent.FlowDeleted ? PortEventType.Remove : (PortEventType?)null;

                    if (eventType == null) continue;

                    if (_processPorts.ContainsKey(flow.ProcessId))
                    {
                        UpdatePortState(flow.ProcessId, new(flow.LocalPort, flow.RemotePort), eventType.Value);
                    }
                    else
                    {
                        _eventQueue.Enqueue(new(sw.ElapsedMilliseconds + QueueExpiration, eventType.Value, flow.ProcessId, new(flow.LocalPort, flow.RemotePort)));
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception) { }
            }
        }, TaskCreationOptions.LongRunning);
    }

    private Task StartProcessPollLoop(CancellationToken token)
    {
        return Task.Factory.StartNew(async () =>
        {
            var knownPids = new HashSet<uint>();
            var sw = Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (!TryGetPidsByProcessName(ProcessName, out var currentPids))
                        continue;

                    long now = sw.ElapsedMilliseconds;

                    foreach (var pid in currentPids)
                    {
                        if (!knownPids.Add(pid)) continue;

                        _processPorts.TryAdd(pid, []);

                        if (!TryGetTcpPortsForPid(pid, out var currentConnections))
                            continue;

                        foreach (var portPair in currentConnections)
                        {
                            UpdatePortState(pid, portPair, PortEventType.Add);
                        }

                        foreach (var item in _eventQueue)
                        {
                            if (item.ProcessId == pid)
                                UpdatePortState(pid, item.PortPair, item.Type);
                        }
                    }

                    List<uint>? vanishedPids = null;
                    foreach (var pid in knownPids)
                    {
                        if (!currentPids.Contains(pid))
                        {
                            vanishedPids ??= [];
                            vanishedPids.Add(pid);
                        }
                    }

                    if (vanishedPids is not null)
                    {
                        foreach (var pid in vanishedPids)
                        {
                            knownPids.Remove(pid);
                            if (_processPorts.TryRemove(pid, out var portSet))
                            {
                                lock (portSet)
                                {
                                    var uniqueLocals = new HashSet<ushort>();
                                    foreach (var (LocalPort, _) in portSet) uniqueLocals.Add(LocalPort);
                                    foreach (var lp in uniqueLocals) Removed?.Invoke(pid, lp);
                                }
                                _snapshotDirty = true;
                            }
                        }
                    }
                    while (_eventQueue.TryPeek(out var item) && item.ExpiredAt < now)
                    {
                        _eventQueue.TryDequeue(out _);
                    }
                }
                catch
                {
                }

                await Task.Delay(PollInterval, token).ConfigureAwait(false);
            }
        }, TaskCreationOptions.LongRunning).Unwrap();
    }
    private void UpdatePortState(uint pid, PortPair portPair, PortEventType type)
    {
        if (!_processPorts.TryGetValue(pid, out var portSet)) return;

        bool changed = false;
        bool isFirstLocal = false;
        bool isLastLocal = false;

        lock (portSet)
        {
            if (type == PortEventType.Add)
            {
                bool alreadyHasLocal = portSet.Any(x => x.LocalPort == portPair.LocalPort);
                if (portSet.Add(portPair))
                {
                    changed = true;
                    if (!alreadyHasLocal) isFirstLocal = true;
                }
            }
            else
            {
                if (portSet.Remove(portPair))
                {
                    changed = true;
                    if (portSet.All(x => x.LocalPort != portPair.LocalPort)) isLastLocal = true;
                }
            }
        }

        if (changed)
        {
            _snapshotDirty = true;
            if (isFirstLocal) Discovered?.Invoke(pid, portPair.LocalPort);
            if (isLastLocal) Removed?.Invoke(pid, portPair.LocalPort);
        }
    }

    private void RebuildProcessIdsAllPortsSnapshot()
    {
        var uniquePorts = new HashSet<ushort>();
        var processIds = ImmutableArray.CreateBuilder<uint>(_processPorts.Count);
        foreach (var kvp in _processPorts)
        {
            processIds.Add(kvp.Key);
            lock (kvp.Value)
            {
                foreach (var (LocalPort, _) in kvp.Value) uniquePorts.Add(LocalPort);
            }
        }

        var sortedPorts = uniquePorts.ToArray();
        Array.Sort(sortedPorts);
        _processIdsSnapshot = processIds.MoveToImmutable();
        _allPortsSnapshot = [.. sortedPorts];
        _snapshotDirty = false;
    }

    private static unsafe bool TryGetTcpPortsForPid(uint targetPid, [NotNullWhen(true)] out HashSet<PortPair>? ports)
    {
        uint size = 0;

        PInvoke.GetExtendedTcpTable(default, ref size, true, (uint)ADDRESS_FAMILY.AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0)
        {
            ports = null;
            return false;
        }

        using var buffer = MemoryPool<byte>.Shared.Rent((int)size);

        var res = PInvoke.GetExtendedTcpTable(buffer.Memory.Span, ref size, true, (uint)ADDRESS_FAMILY.AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (res != (uint)WIN32_ERROR.NO_ERROR)
        {
            ports = null;
            return false;
        }

        ports = [];

        fixed (byte* pBuffer = buffer.Memory.Span)
        {
            uint rowCount = *(uint*)pBuffer;
            var pRow = (MIB_TCPROW_OWNER_PID*)(pBuffer + sizeof(uint));

            for (int i = 0; i < rowCount; i++)
            {
                ref var row = ref pRow[i];
                if (row.dwOwningPid != targetPid) continue;
                if (row.dwState == MIB_TCP_STATE.MIB_TCP_STATE_DELETE_TCB) continue;

                ushort localPort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwLocalPort);
                ushort remotePort = (ushort)IPAddress.NetworkToHostOrder((short)row.dwRemotePort);
                ports.Add(new(localPort, remotePort));
            }
        }

        return true;
    }

    private static unsafe bool TryGetPidsByProcessName(string targetName, [NotNullWhen(true)] out HashSet<uint>? pids)
    {
        var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);

        if (snapshot.IsInvalid)
        {
            pids = null;
            return false;
        }

        try
        {
            pids = [];
            PROCESSENTRY32W entry = default;
            entry.dwSize = (uint)sizeof(PROCESSENTRY32W);

            if (PInvoke.Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    var processName = entry.szExeFile.AsReadOnlySpan();

                    if (processName.IndexOf('\0') is int length and not -1)
                        processName = processName[..length];

                    if (processName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase)
                        && (processName.Length == targetName.Length || processName[targetName.Length..].Equals(".exe", StringComparison.OrdinalIgnoreCase)))
                    {
                        pids.Add(entry.th32ProcessID);
                    }
                }
                while (PInvoke.Process32NextW(snapshot, ref entry));
            }
        }
        finally
        {
            snapshot.Close();
        }

        return true;
    }
}
