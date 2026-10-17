using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HulaoTransitionChecks
{
    private const string Mode = "fixture:hulao-transition";
    private const string Driver = "fixture:hulao-driver";
    private const string Quiet = "fixture:hulao-quiet";
    private const string DeathChild = "fixture:hulao-death-child";
    private const int BossSeat = 3;

    public static void PaidDamageInterruptsAtFourOrZeroPreservesZonesAndColdReplay()
    {
        foreach (var activation in new[] { "threshold", "burst" })
        {
            var registry = Registry();
            var game = Create(registry);
            Accept(game, new StartGameCommand());
            ReachPlay(game);
            var indulgence = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Indulgence &&
                action.TargetSeats.SequenceEqual([BossSeat]));
            Accept(game, new PlayCardCommand(0, indulgence.CardId!.Value, indulgence.TargetSeats,
                game.Revision, game.PendingDecision!.PromptId, indulgence.PlayedCardKind)
            { ConversionSource = indulgence.ConversionSource, AdditionalConversionSources = indulgence.AdditionalConversionSources });
            ReachPlay(game);
            var weapon = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip &&
                View(game).Players[0].Hand.Any(card => card.Id == action.CardId && card.Kind == CardKind.Crossbow));
            Accept(game, new PlayCardCommand(0, weapon.CardId!.Value, weapon.TargetSeats,
                game.Revision, game.PendingDecision!.PromptId, weapon.PlayedCardKind));
            ReachPlay(game);
            Activate(game, "prepare", [], [BossSeat]);
            ReachPlay(game);
            var before = View(game);
            var paid = before.Players[0].Hand.First(card => card.Kind == CardKind.Crossbow).Id;
            var zones = PreservedState(game, paid);
            Require(before.Players[0] is { IsChained: true, IsFaceDown: true },
                $"The real prepare activation must leave the human chained and face down: chained={before.Players[0].IsChained}, faceDown={before.Players[0].IsFaceDown}.");
            Require(before.Players[BossSeat] is { IsChained: true, IsFaceDown: true },
                $"The real prepare activation must leave the boss chained and face down: chained={before.Players[BossSeat].IsChained}, faceDown={before.Players[BossSeat].IsFaceDown}.");
            Require(game.CreateCardZoneDiagnostics().Any(card => card.CardId == weapon.CardId &&
                    card.CardKind == CardKind.Crossbow && card.Location == CardLocation.Equipment(0)),
                $"The native equipment use must place the selected physical Crossbow {weapon.CardId} in the human equipment zone.");
            Require(game.CreateCardZoneDiagnostics().Any(card => card.CardId == indulgence.CardId &&
                    card.Location == CardLocation.Judgment(BossSeat)),
                $"The native delayed-trick use must retain physical card {indulgence.CardId} in the boss judgment zone.");
            Cold(game, registry);
            var firstEvent = game.Events.Count;
            Activate(game, activation, [paid], [BossSeat, 1, 2]);
            var after = View(game);
            var recent = game.Events.Skip(firstEvent).Select(e => e.Payload).ToArray();
            var damage = recent.OfType<DamageAppliedEvent>().Single();
            Require(damage.TargetSeat == BossSeat && damage.RemainingHp == (activation == "threshold" ? 4 : 0) &&
                after.Players[BossSeat] is { Hp: 6, MaxHp: 6, IsAlive: true } &&
                after.Players[BossSeat].GeneralId == "fixture:hulao-second" &&
                after.CurrentSeat == 0 && after.Phase == TurnPhase.NotStarted && after.Winner == Winner.None,
                "Four HP and overkill to zero must both immediately replace the living first form with the six-HP second form and restart at the vanguard.");
            Require(recent.OfType<HulaoTurnInterruptedEvent>().Single().Cause == HulaoTransformationCause.HealthThreshold &&
                recent.OfType<BossActivityStageStartedEvent>().Single().StageIndex == 1 &&
                !recent.OfType<PlayerDyingEvent>().Any(e => e.VictimSeat == BossSeat) &&
                !recent.OfType<PlayerDiedEvent>().Any(e => e.VictimSeat == BossSeat) &&
                !recent.OfType<ProgramSkillHpLostEvent>().Any(e => e.SkillId == Driver) &&
                !recent.OfType<ProgramBindingResolvedEvent>().Any(e => e.SkillId == DeathChild) &&
                after.Players[1].Hp == before.Players[1].Hp && after.Players[2].Hp == before.Players[2].Hp,
                "Transformation must preempt dying, old death skills, the remaining multi-target damage and the paid activation's later effects.");
            Require(PreservedState(game, paid) == zones &&
                game.CardMovements.Count(move => move.CardId == paid && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.DiscardPile) == 1 && after.ProcessingCardCount == 0,
                "Transformation preserves all hand/equipment/judgment entities and face/chain states, while retaining the single paid discard without redealing.");
            Cold(game, registry);
        }
    }

    public static void RealDrawShuffleInterruptsThenSecondShuffleWaitsForActualTurnEnd()
    {
        var registry = Registry(deckSize: 40, dodgeDeck: true);
        var game = Create(registry);
        Accept(game, new StartGameCommand());
        ReachPlay(game);
        var before = View(game);
        var paid = before.Players[0].Hand.Take(3).Select(card => card.Id).ToArray();
        var firstMove = game.CardMovements.Count;
        Activate(game, "exhaust", paid, []);
        var first = View(game);
        var prefixDraws = game.CardMovements.Skip(firstMove).Where(move =>
            move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0)).ToArray();
        Require(first is { CurrentSeat: BossSeat, Phase: TurnPhase.NotStarted, Winner: Winner.None } &&
            first.Players[BossSeat] is { Hp: 6, IsAlive: true } &&
            Shuffles(game).Select(e => e.ShuffleNumber).SequenceEqual([1]) &&
            game.Events.Select(e => e.Payload).OfType<HulaoTurnInterruptedEvent>().Single().Cause == HulaoTransformationCause.FirstShuffle &&
            prefixDraws.Length == before.DrawPileCount &&
            first.Players[0].HandCount == before.Players[0].HandCount - paid.Length + before.DrawPileCount &&
            first.DrawPileCount == paid.Length,
            "The genuine draw must exhaust the old pile, shuffle paid cards once, interrupt before drawing any recycled card, and give the second form first action.");
        Cold(game, registry);

        // The quiet boss draws two and discards down to six. The vanguard's
        // next real draw consumes the short recycled pile and reshuffles those
        // actual boss discards, without directly changing the card zones.
        ReachPlay(game);
        var second = View(game);
        Require(Shuffles(game).Select(e => e.ShuffleNumber).SequenceEqual([1, 2]) &&
            Shuffles(game)[1].ActualTurnNumber == second.TurnNumber &&
            second is { CurrentSeat: 0, Phase: TurnPhase.Play, Winner: Winner.None } &&
            game.Events.Select(e => e.Payload).OfType<HulaoTurnInterruptedEvent>().Count() == 1,
            "The second shuffle must continue the actual draw and leave the coalition alive until this real turn ends.");
        Cold(game, registry);
        var firstEvent = game.Events.Count;
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, () => game.State.Status == EngineStatus.Completed);
        var recent = game.Events.Skip(firstEvent).Select(e => e.Payload).ToArray();
        var ended = Array.FindIndex(recent, e => e is TurnEndedEvent t && t.ActorSeat == 0 && t.TurnNumber == second.TurnNumber);
        var winner = Array.FindIndex(recent, e => e is WinnerDeterminedEvent);
        Require(ended >= 0 && winner > ended && game.State.WinnerTeamId == "team:boss" &&
            game.Events.Count(e => e.Payload is WinnerDeterminedEvent) == 1,
            "Shuffle timeout must declare exactly one boss victory after, never before, the matching actual TurnEnded fact.");
        Cold(game, registry);
    }

    public static void AlliedAndFinalBossDeathsFinishChildrenBeforeVictory()
    {
        var registry = Registry();
        var game = Create(registry);
        Accept(game, new StartGameCommand());
        ReachPlay(game);
        Activate(game, "threshold", [View(game).Players[0].Hand[0].Id], [BossSeat]);
        Require(View(game).Players[BossSeat] is { Hp: 6, IsAlive: true }, "The final-death fixture must enter the second form first.");
        ReachPlay(game);
        var firstEvent = game.Events.Count;
        Activate(game, "kill", [], [1]);
        ReachPlay(game);
        var alliedDeath = game.Events.Skip(firstEvent).Select(e => e.Payload).ToArray();
        Require(!View(game).Players[1].IsAlive && game.State.Winner == Winner.None &&
            CompletedDeathChild(alliedDeath, 1) > Array.FindIndex(alliedDeath, e => e is PlayerDiedEvent { VictimSeat: 1 }) &&
            alliedDeath.OfType<ProgramSkillHpLostEvent>().Count(e => e.SkillId == DeathChild) == 1,
            "An allied death in Hulao must settle its real mandatory death child and keep the living coalition in the match.");
        Cold(game, registry);

        firstEvent = game.Events.Count;
        Activate(game, "kill", [], [BossSeat]);
        var replayedDeathBoundary = false;
        Reach(game, () =>
        {
            if (!View(game).Players[BossSeat].IsAlive && game.State.Winner == Winner.None && !replayedDeathBoundary)
            { Cold(game, registry); replayedDeathBoundary = true; }
            return game.State.Status == EngineStatus.Completed;
        });
        var finalDeath = game.Events.Skip(firstEvent).Select(e => e.Payload).ToArray();
        var returned = CompletedDeathChild(finalDeath, BossSeat);
        Require(replayedDeathBoundary && returned >= 0 &&
            finalDeath.OfType<ProgramSkillHpLostEvent>().Count(e => e.SkillId == DeathChild) == 1 &&
            Array.FindIndex(finalDeath, e => e is WinnerDeterminedEvent) > returned &&
            game.State.WinnerTeamId == "team:allies" &&
            game.Events.Select(e => e.Payload).OfType<BossActivityStageStartedEvent>().Select(e => e.StageIndex).SequenceEqual([1]),
            "Only the final form's complete death and nested child return may award allied victory; the first form is never killed or redealt.");
        Cold(game, registry);
    }

    private static int CompletedDeathChild(IGameEvent[] events, int owner) => Array.FindIndex(events,
        e => e is ProgramBindingResolvedEvent r && r.OwnerSeat == owner && r.SkillId == DeathChild &&
            r.Window == SkillProgramTriggerWindow.OwnerDied && r.Activated && r.Completed);
    private static HulaoDeckShuffledEvent[] Shuffles(GameEngine game) =>
        game.Events.Select(e => e.Payload).OfType<HulaoDeckShuffledEvent>().ToArray();
    private static ContentRegistry Registry(int deckSize = 100, bool dodgeDeck = false) =>
        ContentRegistry.Build(new StandardContentPackage(), new Fixture(deckSize, dodgeDeck));
    private static GameEngine Create(ContentRegistry registry) => GameEngine.CreateStandard(new GameOptions
    {
        Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = "team:allies", ModeId = Mode,
        UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
    }, registry);
    private static GameSnapshot View(GameEngine game) => game.CreateSnapshot(0, revealAll: true);
    private static void Activate(GameEngine game, string activation, int[] cards, int[] targets) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, activation, cards, targets, game.Revision, game.PendingDecision!.PromptId));
    private static void ReachPlay(GameEngine game) => Reach(game,
        () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine game, Func<bool> done)
    {
        for (var step = 0; step < 160; step++)
        {
            if (done()) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded Hulao fixture did not reach its requested boundary.");
    }
    private static string PreservedState(GameEngine game, int ignoredCard) => JsonSerializer.Serialize(new
    {
        Zones = game.CreateCardZoneDiagnostics().Where(card => card.CardId != ignoredCard &&
            card.Location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)
            .OrderBy(card => card.CardId).Select(card => new { card.CardId, card.CardKind, card.Location }),
        States = View(game).Players.Select(player => new { player.Seat, player.IsChained, player.IsFaceDown, player.IsAlive })
    });
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(restored.CardMovements) &&
            JsonSerializer.Serialize(game.Events) == JsonSerializer.Serialize(restored.Events),
            "Cold JSON replay must preserve every viewer, movement, cancellation, form and shuffle fact.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Hulao command rejected."); }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(int deckSize, bool dodgeDeck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("hulao-transition-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,
               "viewAs":[{"id":"real-delayed-trick","inputKinds":["crossbow"],"inputSuits":[],"outputKind":"indulgence","forPlay":true,"forResponse":false}],
               "activations":[
                 {"id":"prepare","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                   {"op":"setChainedState","target":"owner","chained":true},{"op":"setChainedState","target":"selectedTarget","chained":true},
                   {"op":"setFaceState","target":"owner","faceDown":true},{"op":"setFaceState","target":"selectedTarget","faceDown":true}]},
                 {"id":"threshold","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":3,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                   {"op":"discardSelected","target":"owner","amount":1},{"op":"damageParticipants","target":"selectedTargets","amount":4},{"op":"loseHp","target":"owner","amount":1}]},
                 {"id":"burst","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":3,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                   {"op":"discardSelected","target":"owner","amount":1},{"op":"damageParticipants","target":"selectedTargets","amount":20},{"op":"loseHp","target":"owner","amount":1}]},
                 {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                 {"id":"exhaust","minCards":3,"maxCards":3,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":3},{"op":"draw","target":"owner","amount":20}]}
               ]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{DeathChild}}","revision":1,"triggers":[{"id":"real-death-child","window":"ownerDied","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"loseHp","target":"selectedTarget","amount":1}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, DeathChild }.ToDictionary(id => id, id =>
                    new { name = id, description = "Small actual-command Hulao transition fixture." })
            }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var players = Enumerable.Range(0, 3).Select(i => $"fixture:hulao-player-{i}").ToArray();
            foreach (var id in players)
                builder.AddGeneral(new(id, id, "", Driver, "shu", BaseHp: 4, AdditionalSkillIds: [DeathChild]));
            builder.AddGeneral(new("fixture:hulao-first", "First form", "", Quiet, "god", BaseHp: 8, AdditionalSkillIds: [DeathChild]));
            builder.AddGeneral(new("fixture:hulao-second", "Second form", "", Quiet, "god", BaseHp: 6, AdditionalSkillIds: [DeathChild]));
            builder.AddDeck(new("fixture:hulao-deck", "Uniform actual fixture cards", 4, 2,
                [new(dodgeDeck ? "standard:dodge" : "standard:crossbow", deckSize)]));
            builder.AddMode(new(Mode, "Small actual Hulao", 4, 4, new Dictionary<string, int>(), "fixture:hulao-deck", 3,
                [.. players, "fixture:hulao-first", "fixture:hulao-second"], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
            {
                BossActivity = new(players, [new("First", ["fixture:hulao-first"], 8), new("Second", ["fixture:hulao-second"], 4)], 5, []),
                HulaoActivity = new()
            });
        }
    }
}
