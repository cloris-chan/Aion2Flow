namespace Cloris.Aion2Flow.WinDivert;

public static class WinDivertRuntime
{
    public static void Initialize() => WinDivertDriverRecovery.Instance.Initialize();

    public static void Shutdown() => WinDivertDriverRecovery.Instance.Shutdown();
}
