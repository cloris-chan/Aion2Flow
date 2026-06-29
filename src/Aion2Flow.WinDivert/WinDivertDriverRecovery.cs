using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Windows.Win32;
using Windows.Win32.Storage.FileSystem;
using Windows.Win32.System.Services;

namespace Cloris.Aion2Flow.WinDivert;

internal interface IWinDivertDriverRecovery
{
    WinDivertRecoveryResult TryRecover(int openError);
}

internal readonly record struct WinDivertRecoveryResult(bool Succeeded, int ErrorCode, string Detail)
{
    public static WinDivertRecoveryResult Success(string detail) => new(true, 0, detail);

    public static WinDivertRecoveryResult Failure(int errorCode, string detail) => new(false, errorCode, detail);
}

internal sealed class WinDivertDriverRecovery : IWinDivertDriverRecovery
{
    private const string StandardServiceName = "WinDivert";
    private const string LegacyRecoveryServiceNamePrefix = "Aion2FlowWinDivert";
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidHandle = 6;
    private const int ErrorGenFailure = 31;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorMoreData = 234;
    private const int ErrorOperationAborted = 995;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorServiceMarkedForDelete = 1072;
    private const int ErrorServiceExists = 1073;
    private readonly Lock _sync = new();
    private CloseServiceHandleSafeHandle? _ownedService;
    private string _ownedServiceName = string.Empty;
    private bool _shutdownRequested;

    public static WinDivertDriverRecovery Instance { get; } = new();

    private WinDivertDriverRecovery()
    {
    }

    public void Initialize()
    {
        var driverPath = ResolveDriverPath();
        try
        {
            lock (_sync)
            {
                if (!_shutdownRequested && _ownedService is not { IsInvalid: false, IsClosed: false })
                    CleanupRecoveryServices("startup-cleanup");
            }
        }
        catch (Exception ex)
        {
            WinDivertLog.Write(WinDivertLogLevel.Warning, $"WinDivert startup cleanup failed: {ex}");
        }
    }

    public WinDivertRecoveryResult TryRecover(int openError)
    {
        var driverPath = ResolveDriverPath();
        LogEnvironment(openError, driverPath);
        LogService(StandardServiceName);

        if (!File.Exists(driverPath))
        {
            var detail = $"Bundled driver not found at '{driverPath}'.";
            WinDivertLog.Write(WinDivertLogLevel.Error, detail);
            return WinDivertRecoveryResult.Failure(2, detail);
        }

        try
        {
            lock (_sync)
            {
                if (_shutdownRequested)
                    return WinDivertRecoveryResult.Failure(ErrorOperationAborted, "WinDivert runtime shutdown has started.");

                if (_ownedService is { IsInvalid: false, IsClosed: false })
                    return WinDivertRecoveryResult.Success($"Service '{_ownedServiceName}' is already owned by this process.");

                CleanupBlockingStandardServiceForRecovery(driverPath);
                CleanupRecoveryServices("recovery-cleanup");

                const string serviceName = StandardServiceName;
                var startResult = StartDriverService(driverPath, serviceName);
                if (!startResult.Recovery.Succeeded || startResult.Service is null)
                    return startResult.Recovery;

                _ownedService = startResult.Service;
                _ownedServiceName = serviceName;
                return startResult.Recovery;
            }
        }
        catch (Exception ex)
        {
            var detail = $"WinDivert recovery failed: {ex}";
            WinDivertLog.Write(WinDivertLogLevel.Error, detail);
            var error = Marshal.GetLastPInvokeError();
            return WinDivertRecoveryResult.Failure(error == 0 ? ErrorGenFailure : error, detail);
        }
    }

    public void Shutdown()
    {
        CloseServiceHandleSafeHandle? service;
        string serviceName;
        var driverPath = ResolveDriverPath();
        lock (_sync)
        {
            _shutdownRequested = true;
            service = _ownedService;
            serviceName = _ownedServiceName;
            _ownedService = null;
            _ownedServiceName = string.Empty;
        }

        try
        {
            if (service is not null)
                StopAndDeleteService(service, serviceName, "shutdown");

            CleanupWinDivertServicesForShutdown(driverPath, serviceName);
        }
        catch (Exception ex)
        {
            WinDivertLog.Write(WinDivertLogLevel.Error, $"WinDivert service shutdown failed: {ex}");
        }
        finally
        {
            service?.Dispose();
        }
    }

    private static void StopAndDeleteService(CloseServiceHandleSafeHandle service, string serviceName, string phase)
    {
        var beforeStop = QuerySnapshot(service);
        LogSnapshot(serviceName, beforeStop, $"{phase}-before-stop");
        if (beforeStop.CurrentState != (uint)SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED)
        {
            var stopped = false;
            if (!PInvoke.ControlService(service, PInvoke.SERVICE_CONTROL_STOP, out _))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != ErrorServiceNotActive)
                    WinDivertLog.Write(WinDivertLogLevel.Warning, $"ControlService('{serviceName}', STOP) failed: {FormatError(error)}.");
                else
                    stopped = true;
            }
            else
            {
                WinDivertLog.Write(WinDivertLogLevel.Info, $"Stopped service '{serviceName}'.");
                stopped = WaitForStopped(service);
                if (!stopped)
                    WinDivertLog.Write(WinDivertLogLevel.Warning, $"Service '{serviceName}' did not report STOPPED before shutdown timeout.");
            }

            if (!stopped)
            {
                var afterStopAttempt = QuerySnapshot(service);
                if (afterStopAttempt.CurrentState != (uint)SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED)
                {
                    LogSnapshot(serviceName, afterStopAttempt, $"{phase}-stop-incomplete");
                    WinDivertLog.Write(WinDivertLogLevel.Warning, $"Leaving service '{serviceName}' registered because it is still running; marking a running driver service for deletion can leave it disabled until reboot.");
                    return;
                }
            }
        }

        MarkServiceForDeletion(service, serviceName);
        LogSnapshot(serviceName, QuerySnapshot(service), $"{phase}-after-stop");
    }

    private static WinDivertServiceStartResult StartDriverService(string driverPath, string serviceName)
    {
        using var manager = PInvoke.OpenSCManager(
            null!,
            null!,
            PInvoke.SC_MANAGER_CONNECT | PInvoke.SC_MANAGER_CREATE_SERVICE);
        if (manager.IsInvalid)
            return new WinDivertServiceStartResult(Failure("OpenSCManager", Marshal.GetLastPInvokeError()), null);

        WinDivertLog.Write(WinDivertLogLevel.Debug, "Opened Service Control Manager for WinDivert driver service recovery.");

        var service = OpenOrCreateDriverService(manager, serviceName, driverPath, out var created);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            service.Dispose();
            return new WinDivertServiceStartResult(Failure("OpenOrCreateDriverService", error), null);
        }

        var retained = false;
        var deleteOnFailure = created;
        try
        {
            var snapshot = QuerySnapshot(service);
            LogSnapshot(serviceName, snapshot, created ? "created" : "existing");

            if (!created &&
                snapshot.Exists &&
                snapshot.CurrentState == (uint)SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED &&
                ShouldReplaceStoppedStandardServiceBinary(snapshot, driverPath))
            {
                if (!PInvoke.ChangeServiceConfig(service, ENUM_SERVICE_TYPE.SERVICE_NO_CHANGE, SERVICE_START_TYPE.SERVICE_DEMAND_START, SERVICE_ERROR.SERVICE_NO_CHANGE, driverPath, null!, null!, null!, null!, serviceName))
                {
                    return new WinDivertServiceStartResult(Failure("ChangeServiceConfig", Marshal.GetLastPInvokeError()), null);
                }

                WinDivertLog.Write(WinDivertLogLevel.Info, $"Updated WinDivert service binary path to '{driverPath}'.");
                deleteOnFailure = true;
            }

            if (snapshot.CurrentState == (uint)SERVICE_STATUS_CURRENT_STATE.SERVICE_RUNNING)
            {
                WinDivertLog.Write(WinDivertLogLevel.Info, $"WinDivert service '{serviceName}' is already running.");
            }
            else if (!PInvoke.StartService(service, ReadOnlySpan<string>.Empty))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != ErrorServiceAlreadyRunning)
                {
                    LogSnapshot(serviceName, QuerySnapshot(service), "start-failed");
                    return new WinDivertServiceStartResult(Failure("StartService", error), null);
                }

                WinDivertLog.Write(WinDivertLogLevel.Info, "WinDivert service was already running.");
            }
            else
            {
                WinDivertLog.Write(WinDivertLogLevel.Info, "Started WinDivert service.");
            }

            var runningSnapshot = QuerySnapshot(service);
            LogSnapshot(serviceName, runningSnapshot, "after-start");
            retained = true;
            return new WinDivertServiceStartResult(
                WinDivertRecoveryResult.Success($"Started '{serviceName}' from '{driverPath}'."),
                service);
        }
        finally
        {
            if (!retained)
            {
                if (deleteOnFailure)
                    MarkServiceForDeletion(service, serviceName);

                service.Dispose();
            }
        }
    }

    private static bool WaitForStopped(CloseServiceHandleSafeHandle service)
    {
        var deadline = Environment.TickCount64 + 5_000;
        while (Environment.TickCount64 < deadline)
        {
            var snapshot = QuerySnapshot(service);
            if (snapshot.CurrentState == (uint)SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED)
                return true;

            Thread.Sleep(50);
        }

        return false;
    }

    private static void MarkServiceForDeletion(CloseServiceHandleSafeHandle service, string serviceName)
    {
        if (!PInvoke.DeleteService(service))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorServiceMarkedForDelete)
                WinDivertLog.Write(WinDivertLogLevel.Warning, $"DeleteService('{serviceName}') failed: {FormatError(error)}.");
            else
                WinDivertLog.Write(WinDivertLogLevel.Debug, $"Service '{serviceName}' was already marked for deletion.");
        }
        else
        {
            WinDivertLog.Write(WinDivertLogLevel.Info, $"Marked service '{serviceName}' for deletion.");
        }
    }

    private static void CleanupBlockingStandardServiceForRecovery(string driverPath)
    {
        using var manager = PInvoke.OpenSCManager(
            null!,
            null!,
            PInvoke.SC_MANAGER_CONNECT);
        if (manager.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            WinDivertLog.Write(WinDivertLogLevel.Warning, $"Unable to inspect standard WinDivert service during recovery: OpenSCManager failed: {FormatError(error)}.");
            return;
        }

        var service = PInvoke.OpenService(manager, StandardServiceName, RecoveryServiceAccess);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            service.Dispose();
            var level = error == ErrorServiceDoesNotExist ? WinDivertLogLevel.Debug : WinDivertLogLevel.Warning;
            WinDivertLog.Write(level, $"Standard WinDivert service is unavailable during recovery cleanup: {FormatError(error)}.");
            return;
        }

        using (service)
        {
            var snapshot = QuerySnapshot(service);
            LogSnapshot(StandardServiceName, snapshot, "recovery-scan");
            if (!ShouldDeleteStandardWinDivertService(snapshot, driverPath, AppContext.BaseDirectory, out var reason))
            {
                WinDivertLog.Write(WinDivertLogLevel.Debug, $"Leaving standard WinDivert service untouched during recovery scan.");
                return;
            }

            WinDivertLog.Write(WinDivertLogLevel.Warning, $"Removing standard WinDivert service before recovery because it {reason}.");
            StopAndDeleteService(service, StandardServiceName, "recovery-scan");
        }
    }

    private void CleanupRecoveryServices(string phase)
    {
        using var manager = PInvoke.OpenSCManager(
            null!,
            null!,
            PInvoke.SC_MANAGER_CONNECT | PInvoke.SC_MANAGER_ENUMERATE_SERVICE);
        if (manager.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            WinDivertLog.Write(WinDivertLogLevel.Warning, $"Unable to enumerate recovery services: OpenSCManager failed: {FormatError(error)}.");
            return;
        }

        var serviceNames = EnumerateServiceNames(manager, WinDivertServiceNameFilter.RecoveryPrefix);
        for (var i = 0; i < serviceNames.Count; i++)
        {
            var serviceName = serviceNames[i];
            if (string.Equals(serviceName, _ownedServiceName, StringComparison.Ordinal))
                continue;

            var service = PInvoke.OpenService(manager, serviceName, RecoveryServiceAccess);
            if (service.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                service.Dispose();
                WinDivertLog.Write(WinDivertLogLevel.Warning, $"Unable to open orphaned recovery service '{serviceName}': {FormatError(error)}.");
                continue;
            }

            var snapshot = QuerySnapshot(service);
            LogSnapshot(serviceName, snapshot, phase);
            WinDivertLog.Write(WinDivertLogLevel.Info, $"Removing legacy Aion2Flow recovery service '{serviceName}' during {phase}.");
            StopAndDeleteService(service, serviceName, phase);
            service.Dispose();
        }
    }

    private static void CleanupWinDivertServicesForShutdown(string driverPath, string alreadyDeletedServiceName)
    {
        using var manager = PInvoke.OpenSCManager(
            null!,
            null!,
            PInvoke.SC_MANAGER_CONNECT | PInvoke.SC_MANAGER_ENUMERATE_SERVICE);
        if (manager.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            WinDivertLog.Write(WinDivertLogLevel.Warning, $"Unable to enumerate WinDivert services during shutdown: OpenSCManager failed: {FormatError(error)}.");
            return;
        }

        var serviceNames = EnumerateServiceNames(manager, WinDivertServiceNameFilter.ContainsWinDivert);
        for (var i = 0; i < serviceNames.Count; i++)
        {
            var serviceName = serviceNames[i];
            if (string.Equals(serviceName, alreadyDeletedServiceName, StringComparison.Ordinal))
                continue;

            var service = PInvoke.OpenService(manager, serviceName, RecoveryServiceAccess);
            if (service.IsInvalid)
            {
                var error = Marshal.GetLastPInvokeError();
                service.Dispose();
                WinDivertLog.Write(WinDivertLogLevel.Warning, $"Unable to open WinDivert shutdown cleanup service '{serviceName}': {FormatError(error)}.");
                continue;
            }

            try
            {
                var snapshot = QuerySnapshot(service);
                LogSnapshot(serviceName, snapshot, "shutdown-scan");
                if (ShouldDeleteServiceDuringShutdown(serviceName, snapshot, driverPath, out var reason))
                {
                    WinDivertLog.Write(WinDivertLogLevel.Info, $"Removing WinDivert service '{serviceName}' during shutdown because it {reason}.");
                    StopAndDeleteService(service, serviceName, "shutdown-scan");
                }
                else
                {
                    WinDivertLog.Write(WinDivertLogLevel.Debug, $"Leaving WinDivert service '{serviceName}' untouched during shutdown.");
                }
            }
            finally
            {
                service.Dispose();
            }
        }
    }

    private static bool ShouldDeleteServiceDuringShutdown(string serviceName, WinDivertServiceSnapshot snapshot, string driverPath, out string reason)
    {
        if (IsRecoveryServiceName(serviceName))
        {
            reason = "is an Aion2Flow recovery service";
            return true;
        }

        if (string.Equals(serviceName, StandardServiceName, StringComparison.OrdinalIgnoreCase))
            return ShouldDeleteStandardWinDivertService(snapshot, driverPath, AppContext.BaseDirectory, out reason);

        reason = string.Empty;
        return false;
    }

    internal static bool ShouldDeleteStandardWinDivertService(string binaryPath, string driverPath, string baseDirectory, out string reason) =>
        ShouldDeleteStandardWinDivertService(new WinDivertServiceSnapshot(true, 0, 0, 0, binaryPath, 0, 0, 0, 0), driverPath, baseDirectory, out reason);

    private static bool ShouldDeleteStandardWinDivertService(in WinDivertServiceSnapshot snapshot, string driverPath, string baseDirectory, out string reason)
    {
        if (snapshot.QueryError != 0)
        {
            reason = string.Empty;
            return false;
        }

        if (IsAion2FlowBundledDriverPath(snapshot.BinaryPath, driverPath, baseDirectory))
        {
            reason = "points to the Aion2Flow bundled driver";
            return true;
        }

        if (TryResolveServiceBinaryPath(snapshot.BinaryPath, out var path) &&
            string.Equals(Path.GetExtension(path), ".sys", StringComparison.OrdinalIgnoreCase) &&
            !File.Exists(path))
        {
            reason = $"points to missing driver path '{path}'";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    internal static bool IsAion2FlowBundledDriverPath(string binaryPath, string driverPath) =>
        IsAion2FlowBundledDriverPath(binaryPath, driverPath, AppContext.BaseDirectory);

    internal static bool IsAion2FlowBundledDriverPath(string binaryPath, string driverPath, string baseDirectory)
    {
        if (!TryResolveServiceBinaryPath(binaryPath, out var path))
            return false;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var fullDriverPath = Path.GetFullPath(driverPath);
            if (string.Equals(fullPath, fullDriverPath, StringComparison.OrdinalIgnoreCase))
                return true;

            return string.Equals(Path.GetExtension(fullPath), ".sys", StringComparison.OrdinalIgnoreCase) &&
                   IsPathUnderDirectory(fullPath, baseDirectory);
        }
        catch (Exception ex)
        {
            WinDivertLog.Write(WinDivertLogLevel.Warning, $"Unable to classify WinDivert driver path '{binaryPath}': {ex}");
            return false;
        }
    }

    private static bool IsPathUnderDirectory(string path, string directory)
    {
        var normalizedPath = Path.GetFullPath(path);
        var normalizedDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> EnumerateServiceNames(CloseServiceHandleSafeHandle manager, WinDivertServiceNameFilter filter)
    {
        var names = new List<string>();
        var buffer = Array.Empty<byte>();
        var resumeHandle = 0u;
        while (true)
        {
            if (PInvoke.EnumServicesStatusEx(
                    manager,
                    SC_ENUM_TYPE.SC_ENUM_PROCESS_INFO,
                    ENUM_SERVICE_TYPE.SERVICE_DRIVER,
                    ENUM_SERVICE_STATE.SERVICE_STATE_ALL,
                    buffer,
                    out var bytesNeeded,
                    out var servicesReturned,
                    ref resumeHandle,
                    null))
            {
                AppendServiceNames(buffer, servicesReturned, names, filter);
                break;
            }

            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorMoreData && error != ErrorInsufficientBuffer)
            {
                WinDivertLog.Write(WinDivertLogLevel.Warning, $"EnumServicesStatusEx failed while searching recovery services: {FormatError(error)}.");
                break;
            }

            if (servicesReturned != 0)
                AppendServiceNames(buffer, servicesReturned, names, filter);

            if (bytesNeeded == 0)
                break;

            buffer = GC.AllocateUninitializedArray<byte>((int)bytesNeeded);
        }

        return names;
    }

    private static void AppendServiceNames(byte[] buffer, uint servicesReturned, List<string> names, WinDivertServiceNameFilter filter)
    {
        var count = checked((int)servicesReturned);
        var bytes = buffer.AsSpan(0, count * Unsafe.SizeOf<ENUM_SERVICE_STATUS_PROCESSW>());
        var services = MemoryMarshal.Cast<byte, ENUM_SERVICE_STATUS_PROCESSW>(bytes);
        for (var i = 0; i < services.Length; i++)
        {
            var name = services[i].lpServiceName.ToString();
            if (ShouldIncludeServiceName(name, filter))
                names.Add(name);
        }
    }

    private static bool ShouldIncludeServiceName(string serviceName, WinDivertServiceNameFilter filter) => filter switch
    {
        WinDivertServiceNameFilter.RecoveryPrefix => IsRecoveryServiceName(serviceName),
        WinDivertServiceNameFilter.ContainsWinDivert => serviceName.Contains("WinDivert", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    internal static bool IsRecoveryServiceName(string serviceName) => serviceName.StartsWith(LegacyRecoveryServiceNamePrefix, StringComparison.Ordinal);

    private static uint RecoveryServiceAccess => PInvoke.SERVICE_QUERY_CONFIG | PInvoke.SERVICE_CHANGE_CONFIG | PInvoke.SERVICE_QUERY_STATUS | PInvoke.SERVICE_START | PInvoke.SERVICE_STOP | (uint)FILE_ACCESS_RIGHTS.DELETE;

    private static CloseServiceHandleSafeHandle OpenOrCreateDriverService(
        CloseServiceHandleSafeHandle manager,
        string serviceName,
        string driverPath,
        out bool created)
    {
        var access = RecoveryServiceAccess;
        var service = PInvoke.OpenService(manager, serviceName, access);
        if (!service.IsInvalid)
        {
            created = false;
            return service;
        }

        var openError = Marshal.GetLastPInvokeError();
        service.Dispose();
        if (openError != ErrorServiceDoesNotExist && openError != ErrorInvalidHandle)
        {
            created = false;
            Marshal.SetLastPInvokeError(openError);
            return new CloseServiceHandleSafeHandle();
        }

        service = PInvoke.CreateService(manager, serviceName, serviceName, access, ENUM_SERVICE_TYPE.SERVICE_KERNEL_DRIVER, SERVICE_START_TYPE.SERVICE_DEMAND_START, SERVICE_ERROR.SERVICE_ERROR_NORMAL, driverPath, null!, null!, null!, null!);
        if (!service.IsInvalid)
        {
            created = true;
            WinDivertLog.Write(WinDivertLogLevel.Info, $"Created WinDivert service '{serviceName}' for '{driverPath}'.");
            return service;
        }

        var createError = Marshal.GetLastPInvokeError();
        service.Dispose();
        if (createError == ErrorServiceExists || createError == ErrorServiceMarkedForDelete)
        {
            service = PInvoke.OpenService(manager, serviceName, access);
            created = false;
            return service;
        }

        created = false;
        Marshal.SetLastPInvokeError(createError);
        return new CloseServiceHandleSafeHandle();
    }

    private static bool ShouldReplaceStoppedStandardServiceBinary(in WinDivertServiceSnapshot snapshot, string driverPath)
    {
        if (PathsEqual(snapshot.BinaryPath, driverPath))
            return false;

        return ShouldDeleteStandardWinDivertService(snapshot, driverPath, AppContext.BaseDirectory, out _);
    }

    private static void LogEnvironment(int openError, string driverPath)
    {
        var dllPath = Path.Combine(AppContext.BaseDirectory, "WinDivert.dll");
        WinDivertLog.Write(WinDivertLogLevel.Warning, $"WinDivertOpen failed: {FormatError(openError)}. Attempting isolated service recovery.");
        WinDivertLog.Write(WinDivertLogLevel.Info, $"WinDivert environment: processArchitecture={RuntimeInformation.ProcessArchitecture}, osArchitecture={RuntimeInformation.OSArchitecture}, framework='{RuntimeInformation.FrameworkDescription}', os='{RuntimeInformation.OSDescription}', elevated={IsElevated()}, baseDirectory='{AppContext.BaseDirectory}'.");
        LogFile("WinDivert DLL", dllPath);
        LogFile("WinDivert driver", driverPath);
    }

    private static void LogFile(string label, string path)
    {
        if (!File.Exists(path))
        {
            WinDivertLog.Write(WinDivertLogLevel.Error, $"{label}: missing path='{path}'.");
            return;
        }

        try
        {
            var info = new FileInfo(path);
            var version = FileVersionInfo.GetVersionInfo(path);
            using var stream = File.OpenRead(path);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            WinDivertLog.Write(
                WinDivertLogLevel.Info,
                $"{label}: path='{path}', size={info.Length.ToString(CultureInfo.InvariantCulture)}, fileVersion='{version.FileVersion}', productVersion='{version.ProductVersion}', sha256={hash}.");
        }
        catch (Exception ex)
        {
            WinDivertLog.Write(WinDivertLogLevel.Warning, $"{label}: failed to inspect path='{path}': {ex}");
        }
    }

    private static bool IsElevated()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static string ResolveDriverPath() =>
        Path.Combine(AppContext.BaseDirectory, Environment.Is64BitOperatingSystem ? "WinDivert64.sys" : "WinDivert32.sys");

    private static void LogService(string serviceName)
    {
        using var manager = PInvoke.OpenSCManager(null!, null!, PInvoke.SC_MANAGER_CONNECT);
        if (manager.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            WinDivertLog.Write(WinDivertLogLevel.Warning, $"Unable to inspect service '{serviceName}': OpenSCManager failed: {FormatError(error)}.");
            return;
        }

        using var service = PInvoke.OpenService(
            manager,
            serviceName,
            PInvoke.SERVICE_QUERY_CONFIG | PInvoke.SERVICE_QUERY_STATUS);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            var level = error == ErrorServiceDoesNotExist ? WinDivertLogLevel.Info : WinDivertLogLevel.Warning;
            WinDivertLog.Write(level, $"Service '{serviceName}' is unavailable: {FormatError(error)}.");
            return;
        }

        var snapshot = QuerySnapshot(service);
        LogSnapshot(serviceName, snapshot, "diagnostic");
        if (TryResolveServiceBinaryPath(snapshot.BinaryPath, out var binaryPath))
            LogFile($"Service '{serviceName}' binary", binaryPath);
    }

    private static WinDivertServiceSnapshot QuerySnapshot(CloseServiceHandleSafeHandle service)
    {
        var status = default(SERVICE_STATUS_PROCESS);
        var statusBuffer = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref status, 1));
        if (!PInvoke.QueryServiceStatusEx(
                service,
                SC_STATUS_TYPE.SC_STATUS_PROCESS_INFO,
                statusBuffer,
                out _))
        {
            var error = Marshal.GetLastPInvokeError();
            return new WinDivertServiceSnapshot(true, 0, 0, 0, string.Empty, 0, error, 0, 0);
        }

        _ = PInvoke.QueryServiceConfig(service, [], out var configBytes);
        var configError = Marshal.GetLastPInvokeError();
        if (configBytes == 0 || configError != ErrorInsufficientBuffer)
            return new WinDivertServiceSnapshot(true, (uint)status.dwServiceType, 0, 0, string.Empty, (uint)status.dwCurrentState, configError, status.dwWin32ExitCode, status.dwServiceSpecificExitCode);

        var buffer = GC.AllocateUninitializedArray<byte>((int)configBytes);
        if (!PInvoke.QueryServiceConfig(service, buffer, out _))
        {
            var error = Marshal.GetLastPInvokeError();
            return new WinDivertServiceSnapshot(true, (uint)status.dwServiceType, 0, 0, string.Empty, (uint)status.dwCurrentState, error, status.dwWin32ExitCode, status.dwServiceSpecificExitCode);
        }

        ref readonly var config = ref MemoryMarshal.AsRef<QUERY_SERVICE_CONFIGW>(buffer);
        return new WinDivertServiceSnapshot(true, (uint)config.dwServiceType, (uint)config.dwStartType, (uint)config.dwErrorControl, config.lpBinaryPathName.ToString(), (uint)status.dwCurrentState, 0, status.dwWin32ExitCode, status.dwServiceSpecificExitCode);
    }

    private static void LogSnapshot(string serviceName, in WinDivertServiceSnapshot snapshot, string phase)
    {
        if (!snapshot.Exists)
        {
            WinDivertLog.Write(WinDivertLogLevel.Info, $"Service '{serviceName}' phase={phase}: not found.");
            return;
        }

        WinDivertLog.Write(
            snapshot.QueryError == 0 ? WinDivertLogLevel.Info : WinDivertLogLevel.Warning,
            $"Service '{serviceName}' phase={phase}: state={snapshot.CurrentState}, type={snapshot.ServiceType}, startType={snapshot.StartType}, errorControl={snapshot.ErrorControl}, binaryPath='{snapshot.BinaryPath}', win32Exit={snapshot.Win32ExitCode}, serviceExit={snapshot.ServiceSpecificExitCode}, queryError={snapshot.QueryError}.");
    }

    private static bool PathsEqual(string left, string right)
    {
        if (!TryResolveServiceBinaryPath(left, out var normalized))
            return false;

        return string.Equals(Path.GetFullPath(normalized), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryResolveServiceBinaryPath(string value, out string path)
    {
        path = value.Trim();
        if (path.Length == 0)
            return false;

        if (path[0] == '"')
        {
            var closingQuote = path.IndexOf('"', 1);
            if (closingQuote < 0)
                return false;

            path = path[1..closingQuote];
        }

        if (path.StartsWith(@"\??\", StringComparison.Ordinal))
            path = path[4..];

        if (path.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase))
        {
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            path = Path.Combine(windowsDirectory, path[12..]);
        }

        return Path.IsPathFullyQualified(path);
    }

    private static WinDivertRecoveryResult Failure(string operation, int error)
    {
        var detail = $"{operation} failed: {FormatError(error)}.";
        WinDivertLog.Write(error == ErrorAccessDenied ? WinDivertLogLevel.Error : WinDivertLogLevel.Warning, detail);
        return WinDivertRecoveryResult.Failure(error, detail);
    }

    internal static string FormatError(int error) =>
        $"{error} ({new Win32Exception(error).Message})";
}

internal readonly record struct WinDivertServiceSnapshot(bool Exists, uint ServiceType, uint StartType, uint ErrorControl, string BinaryPath, uint CurrentState, int QueryError, uint Win32ExitCode, uint ServiceSpecificExitCode);

internal readonly record struct WinDivertServiceStartResult(WinDivertRecoveryResult Recovery, CloseServiceHandleSafeHandle? Service);

internal enum WinDivertServiceNameFilter
{
    RecoveryPrefix,
    ContainsWinDivert
}
