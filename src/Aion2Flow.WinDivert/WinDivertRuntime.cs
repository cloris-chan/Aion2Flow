namespace Cloris.Aion2Flow.WinDivert;

public static class WinDivertRuntime
{
    public static void Shutdown() => WinDivertDriverRecovery.Instance.Shutdown();
}
