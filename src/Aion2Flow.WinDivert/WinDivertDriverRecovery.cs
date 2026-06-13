using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
    private const string RecoveryServiceNamePrefix = "Aion2FlowWinDivert22";
    private const string RecoveryMutexName = @"Global\Aion2FlowWinDivert22InstallMutex";
    private const int ErrorAccessDenied = 5;
    private const int ErrorInvalidHandle = 6;
    private const int ErrorGenFailure = 31;
    private const int ErrorInsufficientBuffer = 122;
    private const int ErrorOperationAborted = 995;
    private const int ErrorServiceAlreadyRunning = 1056;
    private const int ErrorServiceDoesNotExist = 1060;
    private const int ErrorServiceNotActive = 1062;
    private const int ErrorServiceMarkedForDelete = 1072;
    private const int ErrorServiceExists = 1073;
    private readonly object _sync = new();
    private CloseServiceHandleSafeHandle? _ownedService;
    private string _ownedServiceName = string.Empty;
    private bool _shutdownRequested;
    private static int _nextRecoveryServiceId;

    public static WinDivertDriverRecovery Instance { get; } = new();

    private WinDivertDriverRecovery()
    {
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

        using var mutex = new Mutex(false, RecoveryMutexName);
        var ownsMutex = false;
        try
        {
            try
            {
                ownsMutex = mutex.WaitOne(TimeSpan.FromSeconds(15));
            }
            catch (AbandonedMutexException)
            {
                ownsMutex = true;
                WinDivertLog.Write(WinDivertLogLevel.Warning, "Acquired abandoned WinDivert recovery mutex.");
            }

            if (!ownsMutex)
            {
                const string detail = "Timed out waiting for the WinDivert recovery mutex.";
                WinDivertLog.Write(WinDivertLogLevel.Error, detail);
                return WinDivertRecoveryResult.Failure(1460, detail);
            }

            lock (_sync)
            {
                if (_shutdownRequested)
                    return WinDivertRecoveryResult.Failure(ErrorOperationAborted, "WinDivert runtime shutdown has started.");

                if (_ownedService is { IsInvalid: false, IsClosed: false })
                    return WinDivertRecoveryResult.Success($"Service '{_ownedServiceName}' is already owned by this process.");

                var recoveryServiceName = $"{RecoveryServiceNamePrefix}_{Environment.ProcessId:X8}_{Interlocked.Increment(ref _nextRecoveryServiceId):X8}";
                var startResult = StartRecoveryService(driverPath, recoveryServiceName);
                if (!startResult.Recovery.Succeeded || startResult.Service is null)
                    return startResult.Recovery;

                _ownedService = startResult.Service;
                _ownedServiceName = recoveryServiceName;
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
        finally
        {
            if (ownsMutex)
                mutex.ReleaseMutex();
        }
    }

    public void Shutdown()
    {
        CloseServiceHandleSafeHandle? service;
        string serviceName;
        lock (_sync)
        {
            _shutdownRequested = true;
            service = _ownedService;
            serviceName = _ownedServiceName;
            _ownedService = null;
            _ownedServiceName = string.Empty;
        }

        if (service is null)
            return;

        try
        {
            var beforeStop = QuerySnapshot(service);
            LogSnapshot(serviceName, beforeStop, "shutdown-before-stop");
            if (beforeStop.CurrentState != (uint)SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED)
            {
                if (!PInvoke.ControlService(service, PInvoke.SERVICE_CONTROL_STOP, out _))
                {
                    var error = Marshal.GetLastPInvokeError();
                    if (error != ErrorServiceNotActive)
                        WinDivertLog.Write(WinDivertLogLevel.Warning, $"ControlService('{serviceName}', STOP) failed: {FormatError(error)}.");
                }
                else
                {
                    WinDivertLog.Write(WinDivertLogLevel.Info, $"Stopped recovery service '{serviceName}'.");
                    if (!WaitForStopped(service))
                        WinDivertLog.Write(WinDivertLogLevel.Warning, $"Recovery service '{serviceName}' did not report STOPPED before shutdown timeout.");
                }
            }

            MarkRecoveryServiceForDeletion(service, serviceName);
            LogSnapshot(serviceName, QuerySnapshot(service), "shutdown-after-stop");
        }
        catch (Exception ex)
        {
            WinDivertLog.Write(WinDivertLogLevel.Error, $"WinDivert service shutdown failed: {ex}");
        }
        finally
        {
            service.Dispose();
        }
    }

    private static WinDivertServiceStartResult StartRecoveryService(string driverPath, string serviceName)
    {
        using var manager = PInvoke.OpenSCManager(
            null!,
            null!,
            PInvoke.SC_MANAGER_CONNECT | PInvoke.SC_MANAGER_CREATE_SERVICE);
        if (manager.IsInvalid)
            return new WinDivertServiceStartResult(Failure("OpenSCManager", Marshal.GetLastPInvokeError()), null);

        WinDivertLog.Write(WinDivertLogLevel.Debug, "Opened Service Control Manager for WinDivert recovery.");

        var service = OpenOrCreateRecoveryService(manager, serviceName, driverPath, out var created);
        if (service.IsInvalid)
        {
            var error = Marshal.GetLastPInvokeError();
            service.Dispose();
            return new WinDivertServiceStartResult(Failure("OpenOrCreateRecoveryService", error), null);
        }

        var retained = false;
        try
        {
            var snapshot = QuerySnapshot(service);
            LogSnapshot(serviceName, snapshot, created ? "created" : "existing");

            if (!created &&
                snapshot.Exists &&
                snapshot.CurrentState == (uint)SERVICE_STATUS_CURRENT_STATE.SERVICE_STOPPED &&
                !PathsEqual(snapshot.BinaryPath, driverPath))
            {
                if (!PInvoke.ChangeServiceConfig(service, ENUM_SERVICE_TYPE.SERVICE_NO_CHANGE, SERVICE_START_TYPE.SERVICE_DEMAND_START, SERVICE_ERROR.SERVICE_NO_CHANGE, driverPath, null!, null!, null!, null!, serviceName))
                {
                    return new WinDivertServiceStartResult(Failure("ChangeServiceConfig", Marshal.GetLastPInvokeError()), null);
                }

                WinDivertLog.Write(WinDivertLogLevel.Info, $"Updated recovery service binary path to '{driverPath}'.");
            }

            if (!PInvoke.StartService(service, ReadOnlySpan<string>.Empty))
            {
                var error = Marshal.GetLastPInvokeError();
                if (error != ErrorServiceAlreadyRunning)
                {
                    LogSnapshot(serviceName, QuerySnapshot(service), "start-failed");
                    return new WinDivertServiceStartResult(Failure("StartService", error), null);
                }

                WinDivertLog.Write(WinDivertLogLevel.Info, "WinDivert recovery service was already running.");
            }
            else
            {
                WinDivertLog.Write(WinDivertLogLevel.Info, "Started WinDivert recovery service.");
            }

            var runningSnapshot = QuerySnapshot(service);
            LogSnapshot(serviceName, runningSnapshot, "after-start");
            MarkRecoveryServiceForDeletion(service, serviceName);
            retained = true;
            return new WinDivertServiceStartResult(
                WinDivertRecoveryResult.Success($"Started '{serviceName}' from '{driverPath}'."),
                service);
        }
        finally
        {
            if (!retained)
            {
                MarkRecoveryServiceForDeletion(service, serviceName);
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

    private static void MarkRecoveryServiceForDeletion(CloseServiceHandleSafeHandle service, string serviceName)
    {
        if (!PInvoke.DeleteService(service))
        {
            var error = Marshal.GetLastPInvokeError();
            if (error != ErrorServiceMarkedForDelete)
                WinDivertLog.Write(WinDivertLogLevel.Warning, $"DeleteService('{serviceName}') failed: {FormatError(error)}.");
            else
                WinDivertLog.Write(WinDivertLogLevel.Debug, $"Recovery service '{serviceName}' was already marked for deletion.");
        }
        else
        {
            WinDivertLog.Write(WinDivertLogLevel.Info, $"Marked recovery service '{serviceName}' for deletion.");
        }
    }

    private static CloseServiceHandleSafeHandle OpenOrCreateRecoveryService(
        CloseServiceHandleSafeHandle manager,
        string serviceName,
        string driverPath,
        out bool created)
    {
        var access = PInvoke.SERVICE_QUERY_CONFIG | PInvoke.SERVICE_CHANGE_CONFIG | PInvoke.SERVICE_QUERY_STATUS | PInvoke.SERVICE_START | PInvoke.SERVICE_STOP | (uint)FILE_ACCESS_RIGHTS.DELETE;
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
            WinDivertLog.Write(WinDivertLogLevel.Info, $"Created recovery service '{serviceName}' for '{driverPath}'.");
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
