using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
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
    private const int SearchPollInterval = 1000;
    private const int KnownProcessPollInterval = 5000;
    private const int QueueExpiration = 10_000;

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
    private byte[] _tcpTableBuffer = [];

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
        _divert?.ShutdownReceive();

        try
        {
            if (_pollTask is not null && _divertTask is not null)
                await Task.WhenAll(_pollTask, _divertTask).ConfigureAwait(false);
            else if (_pollTask is not null)
                await _pollTask.ConfigureAwait(false);
            else if (_divertTask is not null)
                await _divertTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppLog.Write(AppLogLevel.Warning, $"Process port discovery stop error: {ex}"); }

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
                catch (Exception ex)
                {
                    AppLog.Write(AppLogLevel.Error, $"WinDivert flow session stopped: {ex}");
                    break;
                }
            }
        }, TaskCreationOptions.LongRunning);
    }

    private Task StartProcessPollLoop(CancellationToken token)
    {
        return Task.Factory.StartNew(async () =>
        {
            var knownPids = new HashSet<uint>();
            var currentPids = new HashSet<uint>();
            var vanishedPids = new List<uint>();
            var currentConnections = new HashSet<PortPair>();
            var sw = Stopwatch.StartNew();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    currentPids.Clear();
                    if (TryGetPidsByProcessName(ProcessName, currentPids))
                    {
                        long now = sw.ElapsedMilliseconds;

                        foreach (var pid in currentPids)
                        {
                            if (!knownPids.Add(pid)) continue;

                            _processPorts.TryAdd(pid, []);

                            currentConnections.Clear();
                            if (!TryGetTcpPortsForPid(pid, currentConnections))
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

                        vanishedPids.Clear();
                        foreach (var pid in knownPids)
                        {
                            if (!currentPids.Contains(pid))
                                vanishedPids.Add(pid);
                        }

                        if (vanishedPids.Count != 0)
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
                }
                catch (Exception ex)
                {
                    AppLog.Write(AppLogLevel.Warning, $"Process port discovery polling failed: {ex}");
                }

                var delay = knownPids.Count == 0 ? SearchPollInterval : KnownProcessPollInterval;
                await Task.Delay(delay, token).ConfigureAwait(false);
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
                bool alreadyHasLocal = HasLocalPort(portSet, portPair.LocalPort);
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
                    if (!HasLocalPort(portSet, portPair.LocalPort)) isLastLocal = true;
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

    private static bool HasLocalPort(HashSet<PortPair> portSet, ushort localPort)
    {
        foreach (var pair in portSet)
        {
            if (pair.LocalPort == localPort)
                return true;
        }

        return false;
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

    private unsafe bool TryGetTcpPortsForPid(uint targetPid, HashSet<PortPair> ports)
    {
        ports.Clear();
        uint size = 0;

        PInvoke.GetExtendedTcpTable(default, ref size, true, (uint)ADDRESS_FAMILY.AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0)
        {
            return false;
        }

        if (_tcpTableBuffer.Length < size)
            _tcpTableBuffer = GC.AllocateUninitializedArray<byte>((int)size);

        var buffer = _tcpTableBuffer.AsSpan(0, (int)size);
        var res = PInvoke.GetExtendedTcpTable(buffer, ref size, true, (uint)ADDRESS_FAMILY.AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        if (res != (uint)WIN32_ERROR.NO_ERROR && size > _tcpTableBuffer.Length)
        {
            _tcpTableBuffer = GC.AllocateUninitializedArray<byte>((int)size);
            buffer = _tcpTableBuffer.AsSpan(0, (int)size);
            res = PInvoke.GetExtendedTcpTable(buffer, ref size, true, (uint)ADDRESS_FAMILY.AF_INET, TCP_TABLE_CLASS.TCP_TABLE_OWNER_PID_ALL, 0);
        }

        if (res != (uint)WIN32_ERROR.NO_ERROR)
        {
            return false;
        }

        fixed (byte* pBuffer = buffer)
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

    private static unsafe bool TryGetPidsByProcessName(string targetName, HashSet<uint> pids)
    {
        var snapshot = PInvoke.CreateToolhelp32Snapshot_SafeHandle(CREATE_TOOLHELP_SNAPSHOT_FLAGS.TH32CS_SNAPPROCESS, 0);

        if (snapshot.IsInvalid)
        {
            return false;
        }

        try
        {
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
