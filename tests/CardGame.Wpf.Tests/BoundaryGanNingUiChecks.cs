using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class BoundaryGanNingUiChecks
{
    private const string GeneralId = "boundary:gan-ning";
    private const string SkillId = "boundary:fenwei";

    public static void PrivateFenweiSubsetPrompt(string output)
    {
        Program.Assert(GeneralGalleryCatalog.Classify(GeneralId).Id == "boundary",
            "Boundary Gan Ning must appear in the boundary gallery.");
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, ModeId = Scenario.ModeId, HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "Fenwei WPF fixture did not start.");
        var selection = game.PendingDecision!;
        Program.Assert(game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision,
            selection.PromptId)).Accepted, "Fenwei WPF fixture could not select the general.");
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Program.Assert(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "Fenwei WPF fixture did not reach Play.");
        var card = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.BarbarianAssault);
        Program.Assert(game.Submit(new PlayCardCommand(0, card.CardId!.Value, card.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId)).Accepted, "Fenwei WPF card use failed.");
        Program.Assert(game.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            "Fenwei WPF fixture did not expose activation.");

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var viewModel = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry)
        { IsMotionEnabled = false };
        viewModel.LoadManualGameCommand.Execute(null);
        Program.Assert(!viewModel.HasSaveError && viewModel.IsSkillSelectionPending &&
            viewModel.SkillChoices.Count == 2, "Fenwei activation must restore in the common WPF skill prompt.");
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var activate = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        viewModel.SelectSkillChoiceCommand.Execute(activate);
        var restored = Program.Engine(viewModel);
        Program.Assert(restored.PendingDecision is
            { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true,
              SkillPrompt.SkillId: SkillId } &&
            viewModel.SkillChoices.Any(choice => choice.Targets.Count == 2) &&
            viewModel.SkillChoices.All(choice => choice.Parameters.GetValueOrDefault("maximum-targets") == "4"),
            "Fenwei WPF must show the private actual-size target subset choices.");
        Program.Render(root, 1120, 740, Path.Combine(output, "boundary-gan-ning-fenwei.png"));
        var two = viewModel.SkillChoices.Single(choice => choice.Targets.SequenceEqual([1, 2]));
        viewModel.SelectSkillChoiceCommand.Execute(two);
        Program.Assert(restored.Events.Select(item => item.Payload)
            .OfType<ProgramSelectedCardEffectsNullifiedEvent>().Single().TargetSeats.SequenceEqual([1, 2]),
            "WPF command must submit the selected target subset to the real engine.");
        window.Content = null;
        window.Close();
    }

    private sealed class Scenario : IGameContentPackage
    {
        public const string ModeId = "identity:classic-boundary-gan-ning-wpf-5";
        public PackageManifest Manifest { get; } = new("boundary-gan-ning-wpf", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:gan-ning-wpf-deck", "奋威界面牌堆", 4, 1,
                [new ContentDeckCardCount("standard:barbarian_assault", 100)]));
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:gan-ning-wpf-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "奋威目标", "supporter",
                    "standard:none", "wei", BaseHp: 4));
            builder.AddMode(new ContentModeDefinition(ModeId, "界甘宁界面", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:gan-ning-wpf-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, .. targets]));
        }
    }
}
