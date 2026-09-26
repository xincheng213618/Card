using CardGame.Wpf.Presentation;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PanZhangMaZhongUiChecks
{
    public static void PortraitGalleryAndWuBattleSeat()
    {
        const string id = "classic:pan-zhang-ma-zhong";
        Program.Assert(GeneralGalleryCatalog.Classify(id).Id == "fame-3" &&
                       GeneralArt.HasPortrait(id) && GeneralArt.GetPortrait(id) is not null,
            "Pan Zhang and Ma Zhong must use official full portrait art and the fame-three gallery.");
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
            Program.Assert(human.Kingdom == "吴" &&
                           human.SkillName.Contains("夺刀", StringComparison.Ordinal) &&
                           human.SkillName.Contains("暗箭", StringComparison.Ordinal),
                "The actual battle seat must show Wu and both skills.");
            return;
        }
        throw new InvalidOperationException("Could not find a selectable Pan Zhang and Ma Zhong WPF fixture.");
    }
}
