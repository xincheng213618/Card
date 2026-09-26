using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class SpLeJinUiChecks
{
    public static void PortraitGalleryAndBattleSeat()
    {
        const string id = "sp:le-jin";
        var portrait = GeneralArt.GetPortrait(id) as ImageBrush;
        Program.Assert(GeneralArt.GetSkin(id)?.LocalPath ==
                           "src/CardGame.Wpf/Assets/official-sp-le-jin.png" &&
                       portrait is { ImageSource: BitmapSource { PixelWidth: 750, PixelHeight: 950 } } &&
                       GeneralGalleryCatalog.Classify(id).Id == "sp",
            "SP Le Jin must use his own official artwork and the SP gallery group.");
        for (var seed = 1; seed <= 4096; seed++)
        {
            using var viewModel = new MainViewModel(autoAdvance: false, seed: seed,
                showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: true)
            { IsMotionEnabled = false };
            var choice = viewModel.GeneralChoices.SingleOrDefault(item => item.GeneralId == id);
            if (choice is null) continue;
            viewModel.SelectGeneralChoiceCommand.Execute(choice);
            Program.AdvanceToDecision(viewModel);
            var human = viewModel.Seats.Single(seat => seat.IsHuman);
            Program.Assert(human.Kingdom == "魏" &&
                           human.SkillName.Contains("骁果", StringComparison.Ordinal),
                "Actual SP Le Jin battle seat must show Wei and Xiaoguo.");
            return;
        }
        throw new InvalidOperationException("Could not find a selectable SP Le Jin WPF fixture.");
    }
}
