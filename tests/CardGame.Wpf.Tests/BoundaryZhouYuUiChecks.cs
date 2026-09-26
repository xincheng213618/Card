using CardGame.Wpf.Presentation;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BoundaryZhouYuUiChecks
{
    public static void GalleryPortraitAndBattleSeat()
    {
        const string id = "boundary:zhou-yu";
        Program.Assert(GeneralGalleryCatalog.Classify(id).Id == "boundary" &&
                       GeneralArt.HasPortrait(id) && GeneralArt.GetSkin(id)?.Id == "30800" &&
                       GeneralArt.GetPortrait(id) is not null,
            "2014 Zhou Yu must have official hero/308 portrait and boundary gallery classification.");
        for (var seed = 1; seed <= 4096; seed++)
        {
            using var vm = new MainViewModel(autoAdvance: false, seed: seed,
                showSetup: false, saveStore: new MemorySaveStore(), useExpandedContent: true)
            { IsMotionEnabled = false };
            var choice = vm.GeneralChoices.SingleOrDefault(item => item.GeneralId == id);
            if (choice is null) continue;
            vm.SelectGeneralChoiceCommand.Execute(choice);
            Program.AdvanceToDecision(vm);
            var human = vm.Seats.Single(seat => seat.IsHuman);
            Program.Assert(human.Kingdom == "吴" && human.SkillName.Contains("英姿", StringComparison.Ordinal) &&
                           human.SkillName.Contains("反间", StringComparison.Ordinal),
                "Battle seat must show Zhou Yu's actual Wu faction and two distinct skills.");
            return;
        }
        throw new InvalidOperationException("Could not find a selectable 2014 Zhou Yu WPF fixture.");
    }
}
