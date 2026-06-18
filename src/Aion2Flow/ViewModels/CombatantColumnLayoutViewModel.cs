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

    public bool ShowBossColumn
    {
        get;
        private set => SetFrameProperty(ref field, value);
    }

    public bool UseCompactMainMetrics
    {
        get;
        set => SetFrameProperty(ref field, value);
    } = true;

    public void Update(bool hasBossColumn, CombatantSortMetric sortMetric)
    {
        ShowDamagePerSecondColumn = !hasBossColumn || sortMetric == CombatantSortMetric.DamagePerSecond;
        ShowDamageColumn = !hasBossColumn || sortMetric == CombatantSortMetric.TotalDamage;
        ShowBossColumn = hasBossColumn;
    }
}
