using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class BoundaryZhangLiaoUiChecks
{
    private const string GeneralId = "boundary:zhang-liao";
    private const string SkillId = "boundary:tuxi";

    public static void PortraitAndPrivateDrawPlan(string output)
    {
        Program.Assert(GeneralGalleryCatalog.Classify(GeneralId).Id == "boundary" &&
                       GeneralArt.HasPortrait(GeneralId) && GeneralArt.GetPortrait(GeneralId) is not null,
            "The 2018 Zhang Liao must have an independent boundary portrait and gallery card.");
        var (game, registry) = CreateReadyGame();
        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var restored = Program.Engine(viewModel);
        Program.Assert(restored.PendingDecision is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true,
                  SkillPrompt.SkillId: SkillId } &&
                viewModel.IsSkillSelectionPending &&
                viewModel.SkillChoices.Select(choice => choice.Parameters.GetValueOrDefault("program-action"))
                    .Order(StringComparer.Ordinal).SequenceEqual(["activate", "skip"]),
            "The Tuxi activation must restore in the shared private WPF prompt.");
        var activate = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        viewModel.SelectSkillChoiceCommand.Execute(activate);
        var targetPrompt = restored.PendingDecision;
        Program.Assert(targetPrompt is
                { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true,
                  SkillPrompt.SkillId: SkillId } &&
                targetPrompt.Choices.Any(choice => choice.Targets.Count == 2) &&
                viewModel.SkillChoices.Any(choice => choice.Targets.Count == 2),
            "The WPF target prompt must show the current two-card plan's legal target sets.");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "boundary-zhang-liao-tuxi.png"));
        var before = restored.CreateSnapshot(0, true).Players[0].HandCount;
        var two = viewModel.SkillChoices.First(choice => choice.Targets.Count == 2);
        viewModel.SelectSkillChoiceCommand.Execute(two);
        Program.Assert(restored.State.Phase == TurnPhase.Play &&
                       restored.CreateSnapshot(0, true).Players[0].HandCount == before + 2 &&
                       restored.Events.Select(item => item.Payload).OfType<ProgramNormalDrawAdjustedEvent>()
                           .Any(item => item.SkillId == SkillId && item.Adjustment == -2),
            "Selecting two targets in WPF must reduce normal draw by two and take two cards.");
        window.Content = null;
        window.Close();
    }

    private static (GameEngine Game, ContentRegistry Registry) CreateReadyGame()
    {
        var registry = ContentRegistry.Build(
            new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(),
            new Scenario());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 5, ModeId = Scenario.ModeId,
            HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10
        }, registry);
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "Zhang Liao WPF fixture did not start.");
        var selection = game.PendingDecision!;
        Program.Assert(game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision,
            selection.PromptId)).Accepted, "Zhang Liao WPF fixture could not select the general.");
        for (var step = 0; step < 40 && game.PendingDecision?.SkillPrompt?.SkillId != SkillId; step++)
        {
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Program.Assert(result.Accepted, result.Error?.Message ?? "Zhang Liao WPF fixture did not advance.");
        }
        Program.Assert(game.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            "Zhang Liao WPF fixture did not reach Tuxi.");
        return (game, registry);
    }

    private static MainViewModel Load(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var viewModel = new MainViewModel(
            autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry)
        { IsMotionEnabled = false };
        viewModel.LoadManualGameCommand.Execute(null);
        Program.Assert(!viewModel.HasSaveError, viewModel.SaveStatus);
        return viewModel;
    }

    private sealed class Scenario : IGameContentPackage
    {
        public const string ModeId = "identity:boundary-zhang-liao-wpf-5";
        public PackageManifest Manifest { get; } = new("boundary-zhang-liao-wpf", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:zhang-liao-wpf-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "突袭目标", "supporter",
                    "standard:none", "wei", BaseHp: 4));
            builder.AddMode(new ContentModeDefinition(ModeId, "界张辽界面", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "classic:standard-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, .. targets]));
        }
    }
}
