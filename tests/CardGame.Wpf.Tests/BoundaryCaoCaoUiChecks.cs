using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BoundaryCaoCaoUiChecks
{
    public static void PortraitGalleryAndBattleSeat()
    {
        const string id = "boundary:cao-cao";
        var portrait = GeneralArt.GetPortrait(id) as ImageBrush;
        Program.Assert(GeneralArt.HasPortrait(id) &&
                       portrait is { ImageSource: BitmapSource { PixelWidth: 750, PixelHeight: 950 } } &&
                       GeneralGalleryCatalog.Classify(id).Id == "boundary",
            "2014 boundary Cao Cao must use his independent official portrait and boundary gallery group.");
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
                           human.SkillName.Contains("奸雄", StringComparison.Ordinal) &&
                           human.SkillName.Contains("护驾", StringComparison.Ordinal),
                "Actual Cao Cao battle seat must show Wei and both 2014 skills.");
            return;
        }
        throw new InvalidOperationException("Could not find a selectable 2014 boundary Cao Cao WPF fixture.");
    }
}
