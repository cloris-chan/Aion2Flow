using System.Collections.Concurrent;
using Avalonia;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using Cloris.Aion2Flow.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.DependencyInjection;

namespace Cloris.Aion2Flow.Markups;

public sealed class TranslateExtension(string key) : MarkupExtension
{
    public string Key { get; set; } = key;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (string.IsNullOrEmpty(Key))
        {
            return AvaloniaProperty.UnsetValue;
        }

        return CompiledBinding.Create<LocalizedString, string>(p => p.Value, LocalizedString.For(Key), mode: BindingMode.OneWay);
    }
}

internal sealed partial class LocalizedString : ObservableObject
{
    private static readonly ConcurrentDictionary<string, LocalizedString> _cache = new(StringComparer.Ordinal);
    private static readonly LocalizationService _service = Ioc.Default.GetRequiredService<LocalizationService>();

    static LocalizedString()
    {
        _service.LanguageChanged += static (_, _) =>
        {
            foreach (var entry in _cache.Values)
            {
                entry.OnPropertyChanged(nameof(Value));
            }
        };
    }

    private readonly string _key;

    private LocalizedString(string key)
    {
        _key = key;
    }

    public static LocalizedString For(string key)
        => _cache.GetOrAdd(key, static k => new LocalizedString(k));

    public string Value => _service.Get(_key);
}
