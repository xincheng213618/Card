using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Content.Standard;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class QuYiUiChecks
{
    public static void GalleryAndOfficialPortrait()
    {
        const string generalId = "classic:qu-yi";
        var general = StandardContentRegistry.CreateWithClassicGenerals().Generals[generalId];
        var portrait = GeneralArt.GetPortrait(generalId) as ImageBrush;
        Program.Assert(general.Name == "麹义" &&
                       general.SkillIds.SequenceEqual(["classic:fuqi", "classic:jiaozi"]) &&
                       GeneralGalleryCatalog.Classify(generalId).Id == "other" &&
                       GeneralArt.GetSkins(generalId).Count == 8 &&
                       portrait is { ImageSource: BitmapSource { PixelWidth: 574, PixelHeight: 761 } },
            "Qu Yi must show both skills, the expansion gallery group and an official 574x761 portrait.");
    }
}
