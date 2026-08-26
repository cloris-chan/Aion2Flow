using Avalonia;
using Avalonia.Controls;

namespace Cloris.Aion2Flow.Controls;

public sealed class SkillIcon : Image
{
    public static readonly DirectProperty<SkillIcon, string?> AssetNameProperty = AvaloniaProperty.RegisterDirect<SkillIcon, string?>(nameof(AssetName), control => control.AssetName, (control, value) => control.AssetName = value);

    public string? AssetName
    {
        get;
        set => SetAndRaise(AssetNameProperty, ref field, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == AssetNameProperty)
            Source = DisplayIconCache.ResolveSkillIcon(AssetName);
    }
}
