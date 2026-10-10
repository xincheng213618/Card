using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class SkillProgramUiChecks
{
    public static void ActiveSelectionAndSubmission()
    {
        using var viewModel = FindGiftShowcase();
        Program.Assert(viewModel.TableModes.All(mode => mode.ModeId != "identity:composed-skills-5"),
            "The retained mechanism fixture must not reintroduce a demonstration lobby entry.");

        var gift = viewModel.GeneralChoices.Single(choice => choice.SkillName == "馈赠");
        Program.Assert(gift.SkillDescription.Contains("交给", StringComparison.Ordinal),
            "The JSON presentation description was not exposed during general selection.");
        viewModel.SelectGeneralChoiceCommand.Execute(gift);
        Program.AdvanceToDecision(viewModel);

        var engine = Program.Engine(viewModel);
        var action = engine.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == "composed:give-and-draw" &&
            candidate.ProgramActivationId == "gift");
        Program.Assert(viewModel.CanUseActiveSkill &&
                       viewModel.HumanSkillCards.Any(skill =>
                           skill.Name == "馈赠" && skill.Description.Contains("摸一张牌", StringComparison.Ordinal)),
            "The composed active skill and its separate presentation were not visible in the skill rail.");

        viewModel.SelectActiveSkillCommand.Execute(action);
        var selectedCard = viewModel.Hand.First(card => card.IsPlayable);
        viewModel.SelectCardCommand.Execute(selectedCard);
        var selectedTarget = viewModel.Seats.First(seat => seat.IsLegalTarget);
        viewModel.SelectTargetCommand.Execute(selectedTarget);
        Program.Assert(viewModel.CanConfirmActiveSkill && viewModel.HasSelection,
            "The generic card/target draft did not accept the program action bounds and candidates.");

        var beforeRevision = engine.Revision;
        viewModel.UseActiveSkillCommand.Execute(null);
        Program.Assert(engine.Revision == beforeRevision + 1 && !viewModel.HasSelection,
            "The program action was not submitted once or its WPF draft was not cleared.");
    }

    private static MainViewModel FindGiftShowcase()
    {
        for (var seed = 1; seed <= 256; seed++)
        {
            var candidate = new MainViewModel(
                autoAdvance: false,
                seed: seed,
                showSetup: false,
                saveStore: new MemorySaveStore(),
                useExpandedContent: true)
            {
                IsMotionEnabled = false
            };
            candidate.SelectedTableMode = new TableModeOption(5, "组合技能测试", string.Empty, "identity:composed-skills-5");
            Program.StartLordFixture(candidate);
            if (candidate.GeneralChoices.Any(choice => choice.SkillName == "馈赠")) return candidate;
            candidate.Dispose();
        }

        throw new InvalidOperationException("No deterministic seed exposed the composed gift general.");
    }
}
