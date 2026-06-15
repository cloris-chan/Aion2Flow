namespace Cloris.Aion2Flow.Services;

internal sealed class AppSingleInstance : IDisposable
{
    private const string DefaultMutexName = @"Global\Cloris.Aion2Flow.SingleInstance";
    private readonly Mutex? _mutex;

    private AppSingleInstance(Mutex? mutex, bool isPrimary)
    {
        _mutex = mutex;
        IsPrimary = isPrimary;
    }

    public bool IsPrimary { get; }

    public static AppSingleInstance TryAcquire() => TryAcquire(DefaultMutexName);

    internal static AppSingleInstance TryAcquire(string mutexName)
    {
        try
        {
            var mutex = new Mutex(false, mutexName, out var createdNew);
            if (createdNew)
                return new AppSingleInstance(mutex, true);

            mutex.Dispose();
            return new AppSingleInstance(null, false);
        }
        catch (UnauthorizedAccessException)
        {
            return new AppSingleInstance(null, false);
        }
    }

    public void Dispose()
    {
        _mutex?.Dispose();
    }
}
