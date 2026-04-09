using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Cloris.Aion2Flow.Battle.Model;
using System.Globalization;

namespace Cloris.Aion2Flow.Converters;

internal sealed class ClassToIconConverter : IValueConverter
{
    private static IImage GladiatorIcon { get => field ??= new Bitmap(AssetLoader.Open(new Uri("avares://Aion2Flow/Assets/Images/Gladiator.webp"))); }
    private static IImage TemplarIcon { get => field ??= new Bitmap(AssetLoader.Open(new Uri("avares://Aion2Flow/Assets/Images/Templar.webp"))); }
    private static IImage AssassinIcon { get => field ??= new Bitmap(AssetLoader.Open(new Uri("avares://Aion2Flow/Assets/Images/Assassin.webp"))); }
    private static IImage RangerIcon { get => field ??= new Bitmap(AssetLoader.Open(new Uri("avares://Aion2Flow/Assets/Images/Ranger.webp"))); }
    private static IImage SorcererIcon { get => field ??= new Bitmap(AssetLoader.Open(new Uri("avares://Aion2Flow/Assets/Images/Sorcerer.webp"))); }
    private static IImage ElementalistIcon { get => field ??= new Bitmap(AssetLoader.Open(new Uri("avares://Aion2Flow/Assets/Images/Elementalist.webp"))); }
    private static IImage ClericIcon { get => field ??= new Bitmap(AssetLoader.Open(new Uri("avares://Aion2Flow/Assets/Images/Cleric.webp"))); }
    private static IImage ChanterIcon { get => field ??= new Bitmap(AssetLoader.Open(new Uri("avares://Aion2Flow/Assets/Images/Chanter.webp"))); }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        CharacterClass.Gladiator => GladiatorIcon,
        CharacterClass.Templar => TemplarIcon,
        CharacterClass.Assassin => AssassinIcon,
        CharacterClass.Ranger => RangerIcon,
        CharacterClass.Sorcerer => SorcererIcon,
        CharacterClass.Elementalist => ElementalistIcon,
        CharacterClass.Cleric => ClericIcon,
        CharacterClass.Chanter => ChanterIcon,
        _ => null,
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
