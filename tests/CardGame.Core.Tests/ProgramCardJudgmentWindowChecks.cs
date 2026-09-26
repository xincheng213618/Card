using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramCardJudgmentWindowChecks
{
    public static void AllCardActionWindowsStartPublicJudgmentsAndReplay()
    {
        foreach (var (window, expected) in new[]
        {
            ("cardUseCommitted", SkillProgramTriggerWindow.CardUseCommitted),
            ("cardUseBeforeTargetEffects", SkillProgramTriggerWindow.CardUseBeforeTargetEffects),
            ("cardUseCompleted", SkillProgramTriggerWindow.CardUseCompleted)
        })
            VerifyWindow(window, expected);
    }

    private static void VerifyWindow(string window, SkillProgramTriggerWindow expected)
    {
        var rules = $$"""
            {"schemaVersion":58,"skills":[{"id":"fixture:card-judgment","revision":1,
              "minimumRulesVersion":168,"triggers":[{"id":"judge-slash","window":"{{window}}",
              "ownerRelation":"actor","cardKinds":["slash"],"optional":true,
              "effects":[{"op":"startJudgment","target":"owner",
                "judgmentReason":"skill.fixture.card-judgment","resultBind":"judgment-card",
                "visibility":"public"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"judgment-card",
                "destination":"discardPile"}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:card-judgment":{
              "name":"卡动作判定","description":"使用杀时可以进行一次判定。"}}}
            """;
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new FixturePackage(rules, presentation));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 1, PlayerCount = 4, ModeId = "identity:card-judgment-fixture", HumanSeat = 0,
            HumanRole = Role.Lord, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, AiPolicyVersion = 2
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted &&
                game.PendingDecision?.Kind == DecisionKind.PlayCard,
            $"{window}: fixture did not reach human Play.");
        var slash = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeats.Count == 1);
        var slashId = slash.CardId!.Value;
        var play = game.Submit(new PlayCardCommand(0, slashId, slash.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, slash.PlayedCardKind));
        Require(play.Accepted, $"{window}: {play.Error?.Message}");
        Require(game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 },
            $"{window}: no activation prompt.");
        var slashLocation = game.CreateCardZoneDiagnostics().Single(card => card.CardId == slashId).Location;
        Require(slashLocation == (expected == SkillProgramTriggerWindow.CardUseCompleted
                ? CardLocation.DiscardPile : CardLocation.Processing),
            $"{window}: parent Slash was in {slashLocation} at activation.");

        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            $"{window}: paused activation did not restore exactly.");
        foreach (var branch in new[] { game, restored })
        {
            var decision = branch.PendingDecision!;
            var activate = decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            var answer = branch.Submit(new AnswerPromptCommand(0, decision.PromptId,
                activate.Id, branch.Revision));
            Require(answer.Accepted, $"{window}: activation failed: {answer.Error?.Message}");
            for (var step = 0; step < 32 && !branch.Events.Select(item => item.Payload)
                     .OfType<ProgramBindingResolvedEvent>()
                     .Any(item => item.SkillId == "fixture:card-judgment"); step++)
            {
                var advance = branch.Submit(new AdvanceOneStepCommand(branch.Revision));
                Require(advance.Accepted, $"{window}: advance failed: {advance.Error?.Message}");
            }
            var requested = branch.Events.Select(item => item.Payload)
                .OfType<JudgmentRequestedEvent>()
                .Single(item => item.Reason == "skill.fixture.card-judgment");
            Require(branch.Events.Select(item => item.Payload).OfType<JudgmentResolvedEvent>()
                    .Any(item => item.ResolutionId == requested.ResolutionId) &&
                    branch.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Single(item => item.SkillId == "fixture:card-judgment") is
                        { Completed: true } resolved && resolved.Window == expected,
                $"{window}: public judgment did not finish in the same binding.");
            Require(branch.CreateCardZoneDiagnostics().Single(card => card.CardId == slashId).Location ==
                    CardLocation.DiscardPile && branch.State.ProcessingCardCount == 0,
                $"{window}: parent or judgment card remained in Processing.");
        }
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, true)),
            $"{window}: replay diverged after judgment.");
    }

    private sealed class FixturePackage(string rules, string presentation) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("card-judgment-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            var program = catalog.Programs["fixture:card-judgment"];
            var text = catalog.Presentations[program.Id];
            builder.AddSkill(new ContentSkillDefinition(program.Id, text.Name, text.Description)
            {
                Program = program
            });
            var generals = Enumerable.Range(0, 4)
                .Select(index => $"fixture:card-judgment-general-{index}").ToArray();
            foreach (var generalId in generals)
                builder.AddGeneral(new ContentGeneralDefinition(generalId, "判定测试",
                    "zhao_yun", "fixture:card-judgment"));
            builder.AddDeck(new ContentDeckRecipe("fixture:card-judgment-deck", "全杀", 4, 0,
                [new ContentDeckCardCount("standard:slash", 60)]));
            builder.AddMode(new ContentModeDefinition("identity:card-judgment-fixture", "判定测试", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1
                }, DeckId: "fixture:card-judgment-deck", GeneralCandidateCount: 1,
                GeneralPoolIds: generals));
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
