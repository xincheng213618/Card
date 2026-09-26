using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class GuYongUiChecks
{
    public static void PortraitAndGallery()
    {
        const string id = "classic:gu-yong";
        var portrait = GeneralArt.GetPortrait(id) as ImageBrush;
        Program.Assert(GeneralArt.HasPortrait(id) &&
                       portrait is { ImageSource: BitmapSource { PixelWidth: 750, PixelHeight: 950 } } &&
                       GeneralGalleryCatalog.Classify(id).Id == "fame-4",
            "Gu Yong must show his own official portrait and belong to the 2014 gallery group.");
        CheckBattleFaction("classic:gu-yong", "吴");
        CheckBattleFaction("classic:li-dian", "魏");
    }

    private static void CheckBattleFaction(string generalId, string expected)
    {
        for (var seed = 1; seed <= 4096; seed++)
        {
            using var viewModel = new MainViewModel(autoAdvance: false, seed: seed,
                showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: true)
            { IsMotionEnabled = false };
            var choice = viewModel.GeneralChoices.SingleOrDefault(item => item.GeneralId == generalId);
            if (choice is null) continue;
            viewModel.SelectGeneralChoiceCommand.Execute(choice);
            Program.AdvanceToDecision(viewModel);
            Program.Assert(viewModel.Seats.Single(seat => seat.IsHuman).Kingdom == expected,
                $"The actual battle seat must use registered faction {expected} for {generalId}.");
            return;
        }
        throw new InvalidOperationException($"Could not find a selectable battle fixture for {generalId}.");
    }
}
