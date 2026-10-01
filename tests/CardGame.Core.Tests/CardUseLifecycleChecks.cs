using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardUseLifecycleChecks
{
    public static void PhaseOwnerDestinationRequiresPhaseContext()
    {
        const string rules = """
            {"schemaVersion":62,"skills":[{"id":"fixture:phase-owner","revision":1,"minimumRulesVersion":175,
             "triggers":[{"id":"gift","window":"playPhaseStarting","subject":"owner","optional":false,
              "effects":[{"op":"draw","target":"owner","amount":1,"resultBind":"gift"},
               {"op":"moveBoundCards","target":"owner","sourceBind":"gift","destination":"phaseOwnerHand"}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:phase-owner":{"name":"Gift","description":"Phase owner context"}}}
            """;
        _ = SkillProgramCatalog.Load(rules, presentation);
        try
        {
            _ = SkillProgramCatalog.Load(rules.Replace("playPhaseStarting", "turnEnding"), presentation);
        }
        catch (InvalidOperationException error) when (error.Message.Contains("PhaseOwner", StringComparison.Ordinal))
        {
            return;
        }
        throw new InvalidOperationException("Phase-owner movement must reject a boundary without that context.");
    }


    public static void DyingBasicUsesResumeTheirRescueParent()
    {
        foreach (var kind in new[] { CardKind.Peach, CardKind.Alcohol })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, dying: true));
            var verified = false;
            for (var seed = 1; seed <= 32 && !verified; seed++)
            {
                var game = GameEngine.CreateStandard(new GameOptions
                {
                    Seed = seed, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                    ModeId = "fixture:card-use-lifecycle", UseInteractiveSetup = false,
                    UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
                }, registry);
                Require(game.Submit(new StartGameCommand()).Accepted, "The rescue fixture must start.");
                for (var step = 0; step < 40 && game.PendingDecision is null; step++) Advance(game);
                Require(game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 },
                    "The current player must enter a real program-caused dying window.");
                var hand = game.CreateSnapshot(0, true).Players[0].Hand.Where(card => card.Kind == kind)
                    .Select(card => card.Id).ToHashSet();
                var rescue = game.PendingDecision.Choices.FirstOrDefault(choice => choice.Cards.Any(hand.Contains));
                if (rescue is null) continue;
                Require(game.Submit(new AnswerPromptCommand(0, game.PendingDecision.PromptId, rescue.Id, game.Revision)).Accepted &&
                        IsStage(game, "committed") && game.CreateSnapshot(0, true).Players[0].Hp == 0,
                    "A rescue use must pause before recovery, keeping the dying parent alive.");
                var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
                foreach (var branch in new[] { game, restored })
                {
                    Activate(branch);
                    Require(IsStage(branch, "completed") && branch.CreateSnapshot(0, true).Players[0].Hp == 1,
                        "The recovery must apply before the use-completed trigger.");
                    Require(!branch.Events.Any(item => item.Payload is DyingResolvedEvent),
                        "The dying parent must wait until its rescue card finishes all use triggers.");
                    Activate(branch);
                    ReachPlay(branch);
                }
                Require(State(game) == State(restored) &&
                        game.Events.Count(item => item.Payload is DyingResolvedEvent) == 1 &&
                        game.Events.Count(item => item.Payload is DyingResponseEvent) == 1,
                    "Rescue continuation must finish once and replay exactly.");
                verified = true;
            }
            Require(verified, $"No bounded {kind} rescue fixture was found.");
        }
        DyingFrameChecks.ThirdPartyPeachResumesOrderedFrameAndReplay();
    }

    public static void NestedRescueRetainsTheOuterUseWindow()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(CardKind.Slash, nestedRescue: true));
        for (var seed = 1; seed <= 32; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
                ModeId = "fixture:card-use-lifecycle", UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "The nested rescue fixture must start.");
            ReachPlay(game);
            var peach = game.CreateSnapshot(0, true).Players[0].Hand.FirstOrDefault(card => card.Kind == CardKind.Peach);
            var slash = game.GetHumanLegalActions().FirstOrDefault(action => action.Kind == LegalActionKind.Slash);
            if (peach is null || slash is null) continue;
            var start = game.Events.Count;
            Require(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats,
                game.Revision, game.PendingDecision!.PromptId, slash.PlayedCardKind)).Accepted,
                "The outer Slash must begin.");
            Require(game.PendingDecision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 },
                "A use-trigger HP cost must retain its outer use while entering dying.");
            var rescue = game.PendingDecision.Choices.Single(choice => choice.Cards.Contains(peach.Id));
            Require(game.Submit(new AnswerPromptCommand(0, game.PendingDecision.PromptId, rescue.Id, game.Revision)).Accepted &&
                    IsStage(game, "committed") && game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Count() == 2,
                "The rescue must open its own use window inside the suspended Slash window.");
            var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
            foreach (var branch in new[] { game, restored })
            {
                Activate(branch);
                Require(IsStage(branch, "completed"), "The inner Peach must reach completion first.");
                Activate(branch);
                Require(IsStage(branch, "committed") && branch.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Count() == 1,
                    "Completing the inner rescue must resume the original Slash commitment.");
                Activate(branch);
                ReachCompleted(branch);
                Activate(branch);
                ReachPlay(branch);
            }
            Require(State(game) == State(restored), "Nested use windows must restore to the same state.");
            Require(game.Events.Skip(start).Count(item => item.Payload is DyingResolvedEvent) == 1,
                "Nested use windows must finish their dying parent exactly once.");
            var finishes = game.Events.Skip(start).Count(item => item.Payload is CardUseFinishedEvent finish && finish.CardId == slash.CardId);
            Require(finishes == 1, $"The outer Slash must finish exactly once, got {finishes} finishes.");
            return;
        }
        throw new InvalidOperationException("No bounded nested Slash/Peach fixture was found.");
    }

    private static bool IsStage(GameEngine game, string binding) =>
        game.PendingDecision?.SkillPrompt?.SkillId == Fixture.SkillId &&
        game.PendingDecision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("binding-id") == binding);

    private static void Activate(GameEngine game)
    {
        var prompt = game.PendingDecision!;
        var choice = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)).Accepted,
            "The common lifecycle trigger must resume.");
    }

    private static void ReachCompleted(GameEngine game)
    {
        for (var step = 0; step < 120 && !IsStage(game, "completed"); step++) Advance(game);
        Require(IsStage(game, "completed"), "The use must reach its completion window.");
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 120 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++) Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "The lifecycle must return to Play.");
    }

    private static void Advance(GameEngine game)
    {
        var prompt = game.PendingDecision;
        var command = prompt is null
            ? (GameCommand)new AdvanceCommand(game.Revision)
            : new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                (prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip") ??
                 prompt.Choices.First()).Id, game.Revision);
        Require(game.Submit(command).Accepted, "An intervening use step must resume.");
    }

    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static void Require([DoesNotReturnIf(false)] bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Fixture(CardKind kind, bool dying = false, bool nestedRescue = false) : IGameContentPackage
    {
        internal const string SkillId = "fixture:use-trigger";
        public PackageManifest Manifest { get; } = new("card-use-lifecycle", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var kindName = JsonNamingPolicy.CamelCase.ConvertName(kind.ToString());
            var cardKinds = nestedRescue ? "\"slash\",\"peach\"" : $"\"{kindName}\"";
            var nestedTrigger = nestedRescue ? """
                {"id":"nested-rescue","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],
                 "priority":100,"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                """ : "";
            var rules = $$$$"""
                {"schemaVersion":62,"skills":[{"id":"{{{{SkillId}}}}","revision":1,"minimumRulesVersion":175,
                 "modifiers":[{"id":"extra-target","query":"cardTargetCount","operation":"add","value":1,"priority":0,"cardKinds":["slash"]}],
                 "triggers":[
                  {{{{nestedTrigger}}}}
                  {"id":"wound","window":"playPhaseStarting","subject":"owner","optional":false,
                   "effects":[{"op":"loseHp","target":"owner","amount":{{{{(nestedRescue ? 6 : dying ? 7 : 1)}}}}}]},
                  {"id":"committed","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":[{{{{cardKinds}}}}],"optional":true,
                   "effects":[{"op":"draw","target":"owner","amount":1}]},
                  {"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":[{{{{cardKinds}}}}],"optional":true,
                   "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                """;
            var presentation = $$$$"""
                {"schemaVersion":3,"skills":{"{{{{SkillId}}}}":{"name":"Card use lifecycle","description":"Card use lifecycle test"}}}
                """;
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            builder.AddSkill(new ContentSkillDefinition(SkillId, "Card use lifecycle", "Card use lifecycle test") { Program = catalog.Programs[SkillId] });
            var generals = Enumerable.Range(0, 4).Select(index => $"fixture:use-owner-{index}").ToArray();
            foreach (var id in generals) builder.AddGeneral(new ContentGeneralDefinition(id, "Test", "supporter", SkillId, BaseHp: 6));
            var cardIds = new[] { "standard:slash", "standard:peach", "standard:alcohol", "standard:crossbow",
                "standard:draw_two", "standard:peach_garden", "standard:iron_chain", "standard:lightning" };
            builder.AddDeck(new ContentDeckRecipe("fixture:use-deck", "Card use test", 10, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(
                    cardIds[index % cardIds.Length], (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition("fixture:card-use-lifecycle", "Card use test", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:use-deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
        }
    }
}
