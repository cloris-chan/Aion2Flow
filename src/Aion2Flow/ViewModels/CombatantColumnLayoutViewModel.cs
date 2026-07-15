namespace Cloris.Aion2Flow.ViewModels;

public sealed class CombatantColumnLayoutViewModel(UiFrameBatchService frameBatchService) : FrameBatchedObservableObject(frameBatchService)
{
    public bool ShowDamagePerSecondColumn
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = true;

    public bool ShowDamageColumn
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = true;

    public bool ShowBossShareColumn
    {
        get;
        private set => SetFrameProperty(ref field, value);
    }

    public bool ShowTotalDamagePerSecond
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = true;

    public bool UseCompactMainMetrics
    {
        get;
        private set => SetFrameProperty(ref field, value);
    } = true;

    public void ApplyMetricDisplaySettings(bool showDamagePerSecondColumn, bool showDamageColumn, bool showTotalDamagePerSecond, bool useCompactMainMetrics)
    {
        ShowDamagePerSecondColumn = showDamagePerSecondColumn;
        ShowDamageColumn = showDamageColumn;
        ShowTotalDamagePerSecond = showTotalDamagePerSecond;
        UseCompactMainMetrics = useCompactMainMetrics;
    }

    public void SetBossShareColumnVisibility(bool isVisible) => ShowBossShareColumn = isVisible;
}
