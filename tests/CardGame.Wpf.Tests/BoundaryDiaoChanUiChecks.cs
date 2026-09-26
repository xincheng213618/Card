using System.IO;
using System.Windows;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class BoundaryDiaoChanUiChecks
{
    private const string GeneralId = "boundary:diao-chan";
    private const string SkillId = "boundary:biyue";

    public static void BoundaryCardAndBiyuePrompt(string output)
    {
        Program.Assert(GeneralGalleryCatalog.Classify(GeneralId).Id == "boundary" &&
                       GeneralArt.HasPortrait(GeneralId) &&
                       GeneralArt.GetPortrait(GeneralId) is not null,
            "The 2019 Diao Chan must have an independent boundary gallery card and official portrait.");

        var (game, registry) = CreateReadyGame();
        using var viewModel = Load(game, registry);
        var window = new MainWindow(viewModel);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        var restored = Program.Engine(viewModel);
        var prompt = restored.PendingDecision;
        Program.Assert(prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0, IsPrivate: true } &&
                       prompt.SkillPrompt is { SkillId: SkillId, Name: "闭月", Title: "闭月 · 是否发动" } &&
                       viewModel.IsSkillSelectionPending &&
                       viewModel.SkillChoices.Select(choice =>
                               choice.Parameters.GetValueOrDefault("program-action"))
                           .Order(StringComparer.Ordinal).SequenceEqual(["activate", "skip"]),
            "Boundary Biyue must restore its private generic WPF prompt with both actions.");
        var handBefore = restored.CreateSnapshot(0, true).Players[0].HandCount;
        var activate = viewModel.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Program.Render(root, 1120, 740,
            Path.Combine(output, "boundary-diao-chan-biyue.png"));
        viewModel.SelectSkillChoiceCommand.Execute(activate);
        Program.Assert(restored.CreateSnapshot(0, true).Players[0].HandCount == handBefore + 1 &&
                       restored.PendingDecision?.PromptId != prompt!.PromptId &&
                       restored.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                           .Count(item => item.SkillId == SkillId && item.Activated && item.Completed) == 1 &&
                       viewModel.BattleCues.Count(cue => cue.Label == "闭月 · 已发动") == 1,
            "The WPF Biyue action must draw once and complete its turn-ending window.");
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
        Program.Assert(game.Submit(new StartGameCommand()).Accepted, "Boundary Diao Chan WPF fixture did not start.");
        var selection = game.PendingDecision!;
        Program.Assert(game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision,
            selection.PromptId)).Accepted, "Boundary Diao Chan WPF fixture did not select its general.");
        for (var step = 0; step < 40 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Boundary Diao Chan WPF fixture did not reach Play.");
        var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Program.Assert(ended.Accepted, ended.Error?.Message ?? "Boundary Diao Chan WPF fixture could not end Play.");
        for (var step = 0; step < 40 && game.PendingDecision?.SkillPrompt?.SkillId != SkillId; step++)
            Advance(game);
        Program.Assert(game.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            "Boundary Diao Chan WPF fixture did not reach Biyue.");
        return (game, registry);
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Program.Assert(result.Accepted, result.Error?.Message ?? "Boundary Diao Chan WPF fixture did not advance.");
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
        public const string ModeId = "identity:classic-boundary-diao-chan-wpf-5";
        public PackageManifest Manifest { get; } = new("boundary-diao-chan-wpf", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:diao-wpf-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "闭月目标", "supporter",
                    "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:diao-wpf-deck", "闭月牌堆", 4, 1,
                [new ContentDeckCardCount("standard:crossbow", 80)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "界貂蝉界面", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:diao-wpf-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [GeneralId, .. targets]));
        }
    }
}
