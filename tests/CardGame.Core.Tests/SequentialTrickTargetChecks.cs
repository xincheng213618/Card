using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SequentialTrickTargetChecks
{
    public static void AllFiveTricksResolveEveryTargetAndReplay()
    {
        foreach (var kind in new[] { CardKind.DrawTwo, CardKind.Dismantlement, CardKind.Snatch, CardKind.Duel, CardKind.FireAttack })
        {
            var (game, registry) = Create(kind);
            Accept(game.Submit(new UseProgramSkillCommand(0, "fixture:next-target", "grant", [], [], game.Revision, game.PendingDecision!.PromptId)));
            ReachPlay(game);
            var action = game.GetHumanLegalActions().First(item => item.CardId is not null &&
                item.ProgramActivationId == "next-card-target-adjustment" && item.TargetSeats.Count == 2 &&
                item.TargetSeats[0] == (kind == CardKind.DrawTwo ? 0 : 1) && item.TargetSeats[1] == 3);
            var cardId = action.CardId!.Value;
            var before = game.CreateSnapshot(0, revealAll: true);
            var startEvents = game.Events.Count;
            Accept(game.Submit(new PlayCardCommand(0, cardId, action.TargetSeats, game.Revision,
                game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)));
            var declared = game.Events.Skip(startEvents).Select(item => item.Payload).OfType<CardUseDeclaredEvent>().Single();
            var confirmed = game.Events.Skip(startEvents).Select(item => item.Payload).OfType<TargetsConfirmedEvent>().Single(item => item.ResolutionId == declared.ResolutionId);
            Require(confirmed.TargetSeats.SequenceEqual(action.TargetSeats), "The declared card must freeze both configured targets.");
            var replay = Restore(game, registry);
            var checkedSecondCursor = false;
            for (var step = 0; step < 256 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            {
                var use = game.ResolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.CardId == cardId);
                if (use is { SequentialTrick: not null, TargetIndex: 1 })
                {
                    Require(game.CreateCardZoneDiagnostics().Single(card => card.CardId == cardId).Location == CardLocation.Processing,
                        "The trick's physical card must remain processing until the final target finishes.");
                    if (!checkedSecondCursor)
                    {
                        var secondReplay = Restore(game, registry);
                        Equivalent(game, secondReplay);
                        checkedSecondCursor = true;
                    }
                }
                StepPair(game, replay);
            }
            Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, $"{kind} did not return to Play after both targets.");
            Require(checkedSecondCursor, "Each extra target must expose its own resumable response window.");
            var after = game.CreateSnapshot(0, revealAll: true);
            var targets = action.TargetSeats;
            switch (kind)
            {
                case CardKind.DrawTwo:
                    Require(after.Players[0].HandCount == before.Players[0].HandCount + 1 && after.Players[3].HandCount == before.Players[3].HandCount + 2,
                        "DrawTwo must draw two for the normal user and the extra target.");
                    break;
                case CardKind.Dismantlement:
                case CardKind.Snatch:
                    Require(targets.All(seat => after.Players[seat].HandCount == before.Players[seat].HandCount - 1),
                        "Each target must lose one selected card.");
                    Require(after.Players[0].HandCount == before.Players[0].HandCount + (kind == CardKind.Snatch ? 1 : -1),
                        "Snatch must give the source both cards; Dismantlement gives neither.");
                    break;
                case CardKind.Duel:
                case CardKind.FireAttack:
                    Require(targets.All(seat => after.Players[seat].Hp == before.Players[seat].Hp - 1),
                        "Both targets must independently resolve damage, responses and continuation.");
                    var damage = game.Events.Skip(startEvents).Select(item => item.Payload).OfType<DamageAppliedEvent>().ToArray();
                    Require(damage.Select(item => item.TargetSeat).SequenceEqual(targets), "Damage must resolve in declared target order.");
                    break;
            }
            Require(game.CardMovements.Count(move => move.CardId == cardId && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1,
                "A multi-target trick must finish its physical card exactly once.");
            Require(game.Events.Skip(startEvents).Count(item => item.Payload is CardUseDeclaredEvent) == 1 &&
                game.Events.Skip(startEvents).Count(item => item.Payload is CardUseFinishedEvent) == 1,
                "Target continuation must not declare a second card use or finish the first twice.");
            Equivalent(game, replay);
        }
    }

    private static GameEngine Restore(GameEngine game, ContentRegistry registry) =>
        GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
    private static void ReachPlay(GameEngine game)
    { for (var step = 0; step < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++) Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture failed to reach Play."); }
    private static void StepPair(GameEngine game, GameEngine replay)
    {
        if (game.PendingDecision is { PlayerSeat: 0 } prompt)
        {
            var choice = prompt.Kind == DecisionKind.Nullification
                ? prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("response") == "pass")
                : prompt.Kind == DecisionKind.FireAttackDiscard
                    ? prompt.Choices.First(item => item.Cards.Count == 1)
                    : prompt.Choices[0];
            Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)));
            Accept(replay.Submit(new AnswerPromptCommand(0, replay.PendingDecision!.PromptId, choice.Id, replay.Revision)));
        }
        else
        {
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            Accept(replay.Submit(new AdvanceOneStepCommand(replay.Revision)));
        }
        Equivalent(game, replay);
    }
    private static void Equivalent(GameEngine game, GameEngine replay) => Require(
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) &&
        JsonSerializer.Serialize(game.AcceptedCommands) == JsonSerializer.Serialize(replay.AcceptedCommands) &&
        JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(replay.Events) && game.CardMovements.SequenceEqual(replay.CardMovements),
        "Every response and target continuation must replay identical snapshots, commands, events and movements.");
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(CardKind kind)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new Fixture(kind));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "fixture:sequential-mode", UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:sequential-owner", game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game); return (game, registry);
    }
    private sealed class Fixture(CardKind kind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sequential-targets", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:next-target","revision":1,"viewAs":[{"id":"response-window","inputKinds":[],"inputSuits":["spade"],"outputKind":"nullification","forPlay":false,"forResponse":true}],"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"grantNextCardTargetAdjustment","target":"owner"}]}]}]}""";
            var presentation = """{"schemaVersion":3,"skills":{"fixture:next-target":{"name":"目标调整","description":"下一张牌增减一名目标"}}}""";
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            builder.AddSkill(new ContentSkillDefinition("fixture:next-target", "目标调整", "下一张牌增减一名目标") { Program = catalog.Programs["fixture:next-target"], ProgramPresentation = catalog.Presentations["fixture:next-target"] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:sequential-owner", "源", "supporter", "fixture:next-target", "wu", BaseHp: 8));
            for (var index = 1; index < 4; index++) builder.AddGeneral(new ContentGeneralDefinition($"fixture:sequential-{index}", $"目标{index}", "supporter", "standard:none", "wei", BaseHp: 8));
            var definition = kind switch { CardKind.DrawTwo => "standard:draw_two", CardKind.Dismantlement => "standard:dismantlement", CardKind.Snatch => "standard:snatch", CardKind.Duel => "standard:duel", CardKind.FireAttack => "standard:fire_attack", _ => throw new InvalidOperationException() };
            builder.AddDeck(new ContentDeckRecipe("fixture:sequential-deck", "顺序锦囊测试", 12, 0, [])
            { PhysicalCards = Enumerable.Range(0, 160).Select(_ => new ContentDeckPhysicalCard(definition, Suit.Spade, 7)).ToArray() });
            builder.AddMode(new ContentModeDefinition("fixture:sequential-mode", "顺序锦囊测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:sequential-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:sequential-owner", "fixture:sequential-1", "fixture:sequential-2", "fixture:sequential-3"]));
        }
    }
}
