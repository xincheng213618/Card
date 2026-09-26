using CardGame.Wpf.Presentation;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BoundaryXuChuUiChecks
{
    public static void GalleryAndBattleSeat()
    {
        const string id = "boundary:xu-chu";
        Program.Assert(GeneralGalleryCatalog.Classify(id).Id == "boundary" &&
                       GeneralArt.HasPortrait(id) && GeneralArt.GetPortrait(id) is not null,
            "2014 Xu Chu must have his own portrait and boundary gallery assignment.");
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
                           human.SkillName.Contains("裸衣", StringComparison.Ordinal),
                "Actual Xu Chu battle seat must show Wei and his 2014 skill.");
            return;
        }
        throw new InvalidOperationException("Could not find a selectable 2014 Xu Chu WPF fixture.");
    }
}
