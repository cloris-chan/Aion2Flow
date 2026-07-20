using Cloris.Aion2Flow.Services;
using Cloris.Aion2Flow.ViewModels;
using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace Cloris.Aion2Flow.Tests.Support;

internal static class ViewTestServices
{
    private static readonly Lock s_gate = new();
    private static LocalizationService? s_localization;
    private static UiFrameBatchService? s_frameBatch;

    internal static (LocalizationService Localization, UiFrameBatchService FrameBatch) Get()
    {
        lock (s_gate)
        {
            if (s_localization is null)
            {
                var language = new LanguageService();
                language.SetLanguage(LanguageService.TraditionalChinese);
                s_frameBatch = new UiFrameBatchService();
                s_localization = new LocalizationService(language);
                Ioc.Default.ConfigureServices(new ServiceCollection()
                    .AddSingleton(s_localization)
                    .BuildServiceProvider());
            }

            return (s_localization, s_frameBatch!);
        }
    }
}
