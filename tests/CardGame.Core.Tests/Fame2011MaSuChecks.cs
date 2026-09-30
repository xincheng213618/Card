using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2011MaSuChecks
{
    public static void MaximumTargetsAndSourcePreventionReplay()
    {
        var (game, registry) = Create();
        var initial = game.CreateSnapshot(0, true);
        var hpActions = game.GetHumanLegalActions().Where(a => a.ProgramSkillId == "classic:sanyao" && a.ProgramActivationId == "highest-hp").ToArray();
        Require(hpActions.Length > 0 && hpActions.All(a => a.SelectableTargetSeats.SequenceEqual(new[] { 0 })), "Only the highest-HP Lord must be eligible, including self.");
        var revision = game.Revision;
        Require(!game.Submit(new UseProgramSkillCommand(0, "classic:sanyao", "highest-hp", [initial.Players[0].Hand.First().Id], [1], revision, game.PendingDecision!.PromptId)).Accepted && game.Revision == revision,
            "Below-maximum targets must be rejected before paying a cost.");
        Activate(game, "classic:sanyao", "highest-hp", [initial.Players[0].Hand.First().Id], [0]); Drain(game); Play(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hp == initial.Players[0].Hp - 1 &&
            !game.Events.Any(e => e.Payload is ProgramDamagePreventedEvent), "Self Sanyao must damage self without Zhiman.");
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:sanyao" && a.ProgramActivationId == "highest-hp") &&
            game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:sanyao" && a.ProgramActivationId == "highest-hand"), "The two phase budgets must be independent.");
        var handActions = game.GetHumanLegalActions().Where(a => a.ProgramSkillId == "classic:sanyao" && a.ProgramActivationId == "highest-hand").ToArray();
        Require(handActions.SelectMany(a => a.SelectableTargetSeats).Distinct().Order().SequenceEqual(new[] { 1, 2, 3 }), "All tied maximum-hand peers must be eligible, excluding the smaller hand.");
        var handBefore = game.CreateSnapshot(0, true).Players[0].Hand.Count;
        var hpBefore = game.CreateSnapshot(0, true).Players[1].Hp;
        Activate(game, "classic:sanyao", "highest-hand", [game.CreateSnapshot(0, true).Players[0].Hand.First().Id], [1]);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "classic:zhiman", "Source damage must offer Zhiman.");
        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, replay })
        {
            Answer(branch, "activate");
            Require(branch.PendingDecision?.Choices.All(choice => choice.Cards.Count == 0 && choice.Parameters.GetValueOrDefault("source-zone") == "Hand") == true,
                "Zhiman must expose only opaque slots for another player's hand.");
            Require(branch.CreateSnapshot(2).PendingDecision is null, "Observers must not see the owner's private card choice.");
            var selectionReplay = GameReplay.Restore(branch.CreateCheckpoint(), registry);
            Step(branch); Drain(branch); Step(selectionReplay); Drain(selectionReplay); Equal(branch, selectionReplay);
            Require(branch.CreateSnapshot(0, true).Players[1].Hp == hpBefore && branch.CreateSnapshot(0, true).Players[0].Hand.Count == handBefore,
                "Zhiman must prevent the whole Sanyao damage and gain one card after paying the cost.");
        }
        Equal(game, replay);
        foreach (var zone in new[] { "Equipment", "Judgment", "Empty" }) GainFromPublicZoneOrPreventEmpty(zone);
        var (declined, declineRegistry) = Create();
        var declineHp = declined.CreateSnapshot(0, true).Players[1].Hp;
        Activate(declined, "classic:sanyao", "highest-hand", [declined.CreateSnapshot(0, true).Players[0].Hand.First().Id], [1]);
        var declinedReplay = GameReplay.Restore(declined.CreateCheckpoint(), declineRegistry);
        foreach (var branch in new[] { declined, declinedReplay })
        {
            Answer(branch, "skip"); Drain(branch);
            Require(branch.CreateSnapshot(0, true).Players[1].Hp == declineHp - 1 && branch.Events.Any(e => e.Payload is DamageAppliedEvent) && !branch.Events.Any(e => e.Payload is ProgramDamagePreventedEvent), "Declining source prevention must resume the original damage exactly once.");
        }
        Equal(declined, declinedReplay);
        ActualSlashPreventionAwaitsMovementTrigger();
        var (equipmentCost, _) = Create();
        var equip = equipmentCost.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Accept(equipmentCost.Submit(new PlayCardCommand(0, equip.CardId!.Value, [], equipmentCost.Revision, equipmentCost.PendingDecision!.PromptId)));
        Drain(equipmentCost); Play(equipmentCost);
        var weapon = equipmentCost.CreateSnapshot(0, true).Players[0].Equipment.Single();
        Activate(equipmentCost, "classic:sanyao", "highest-hp", [weapon.Id], [0]); Drain(equipmentCost);
        Require(equipmentCost.CardMovements.Any(move => move.CardId == weapon.Id && move.From == CardLocation.Equipment(0) && move.To == CardLocation.DiscardPile),
            "Sanyao must accept an equipped card as its discard cost.");
    }
    private static void ActualSlashPreventionAwaitsMovementTrigger()
    {
        var (game, registry) = Create("standard:slash", hand: 1, nestedMovement: true);
        var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == 1);
        Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, [1], game.Revision, game.PendingDecision!.PromptId)));
        for (var i = 0; i < 40 && game.PendingDecision?.SkillPrompt?.SkillId != "classic:zhiman"; i++) Step(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "classic:zhiman", "Actual Slash must offer source prevention.");
        Answer(game, "activate"); Step(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "fixture:ma-driver", "Preventing Slash and taking a card must suspend in its nested movement trigger.");
        var damage = game.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single();
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "classic:zhiman");
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
        Require(parent.WindowContext?.ParentFrameId == damage.Id && parent.PendingMovementContinuation is not null && movement.Batch.AwaitingProgramFrameId == parent.Id && movement.Batch.ParentFrameId == parent.Id && game.CreateSnapshot(0, true).Players[1].HandCount == 0, "The last-hand-card observer must retain the original before-damage parent chain.");
        var checkpoint = game.CreateCheckpoint();
        foreach (var action in new[] { "activate", "skip" })
        {
            var live = GameReplay.Restore(checkpoint, registry);
            var replay = GameReplay.Restore(checkpoint, registry);
            foreach (var branch in new[] { live, replay })
            {
                Answer(branch, action); Drain(branch);
                Require(branch.CreateSnapshot(0, true).Players[0].HandCount == (action == "activate" ? 2 : 1) && branch.Events.Any(e => e.Payload is ProgramDamagePreventedEvent) && !branch.Events.Any(e => e.Payload is DamageAppliedEvent), "Either observer answer must preserve the already prevented Slash and resume its original frame.");
            }
            Equal(live, replay);
            Equal(live, GameReplay.Replay(live.CreateCheckpoint().Options, CommandJson.Deserialize(CommandJson.Serialize(live.AcceptedCommands)), registry));
        }
    }
    private static void GainFromPublicZoneOrPreventEmpty(string zone)
    {
        var (game, registry) = Create(zone == "Judgment" ? "standard:indulgence" : "standard:crossbow", zone == "Empty" ? 0 : 6);
        if (zone == "Equipment") { Activate(game, "fixture:ma-driver", "weapon", [], [1]); Drain(game); Play(game); }
        if (zone == "Judgment")
        {
            var delayed = game.GetHumanLegalActions().First(a => a.CardId is { } id && game.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Id == id && card.Kind == CardKind.Indulgence) && a.TargetSeat == 1);
            Accept(game.Submit(new PlayCardCommand(0, delayed.CardId!.Value, [1], game.Revision, game.PendingDecision!.PromptId)));
            Drain(game); Play(game);
        }
        var hp = game.CreateSnapshot(0, true).Players[1].Hp;
        var start = game.Events.Count;
        Activate(game, "fixture:ma-driver", "damage", [], [1]);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "classic:zhiman", "Zhiman must work with program damage, including an empty target.");
        var replay = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var branch in new[] { game, replay })
        {
            Answer(branch, "activate");
            if (zone != "Empty")
            {
                var choice = branch.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("source-zone") == zone);
                Accept(branch.Submit(new AnswerPromptCommand(0, branch.PendingDecision.PromptId, choice.Id, branch.Revision)));
            }
            Drain(branch);
            Require(branch.CreateSnapshot(0, true).Players[1].Hp == hp && branch.Events.Skip(start).Any(e => e.Payload is ProgramDamagePreventedEvent { Amount: 3 }) &&
                !branch.Events.Skip(start).Any(e => e.Payload is DamageAppliedEvent), "Zhiman must prevent all three damage, without damage events, even when no card exists.");
            if (zone != "Empty") Require(branch.CardMovements.Any(move => move.From == new CardLocation(Enum.Parse<CardZoneKind>(zone), 1) && move.To == CardLocation.Hand(0)),
                "Zhiman must gain the selected public-zone card.");
        }
        Equal(game, replay);
    }
    private static (GameEngine, ContentRegistry) Create(string card = "standard:crossbow", int hand = 6, bool nestedMovement = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(card, hand, nestedMovement));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "fixture:ma-su",
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game.Submit(new StartGameCommand())); Play(game); return (game, registry);
    }
    private static void Activate(GameEngine game, string skill, string activation, int[] cards, int[] targets) =>
        Accept(game.Submit(new UseProgramSkillCommand(0, skill, activation, cards, targets, game.Revision, game.PendingDecision!.PromptId)));
    private static void Play(GameEngine game) { for (var i = 0; i < 100 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Step(game); Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Play boundary missing."); }
    private static void Drain(GameEngine game) { for (var i = 0; i < 100 && game.ResolutionStack.Count != 0; i++) Step(game); Require(game.ResolutionStack.Count == 0, "Frames did not finish."); }
    private static void Answer(GameEngine game, string action) { var prompt = game.PendingDecision!; var choice = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == action); Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision))); }
    private static void Step(GameEngine game) { var prompt = game.PendingDecision; Accept(game.Submit(prompt is null || prompt.PlayerSeat != 0 ? (GameCommand)new AdvanceCommand(game.Revision) : new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First().Id, game.Revision))); }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Rejected");
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private static void Equal(GameEngine game, GameEngine replay)
    {
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)), "Ma Su checkpoints must replay exactly.");
        Require(game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(replay.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))), "Ma Su typed events must replay exactly.");
        Require(JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(replay.CardMovements), "Ma Su physical movements must replay exactly.");
    }
    private sealed class Fixture(string card, int hand, bool nestedMovement) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("ma-su-fixture", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var movementTrigger = nestedMovement ? """{"id":"after-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.classic:zhiman.SelectAndMoveOwnedCard"],"movementOccurrence":"perBatch","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}""" : "";
            var rules = $$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:ma-driver","revision":1,"minimumRulesVersion":{{{GameCheckpoint.CurrentRulesVersion}}},"activations":[
                {"id":"weapon","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"placeNamedWeapon","target":"selectedTarget","outputKind":"crossbow"}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3}]}],"triggers":[{{{movementTrigger}}}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:ma-driver":{"name":"Fixture","description":"Test"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:ma-driver", "Fixture", "Test") { Program = catalog.Programs["fixture:ma-driver"] });
            var generals = Enumerable.Range(0, 4).Select(i => $"fixture:ma-su-{i}").ToArray();
            foreach (var id in generals) builder.AddGeneral(new ContentGeneralDefinition(id, "Test", "supporter", "fixture:ma-driver", BaseHp: 3, AdditionalSkillIds: ["classic:sanyao", "classic:zhiman"]));
            builder.AddDeck(new ContentDeckRecipe("fixture:ma-su-deck", "Fixture", hand, 0, [new ContentDeckCardCount(card, 100)]));
            builder.AddMode(new ContentModeDefinition("fixture:ma-su", "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:ma-su-deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
        }
    }
}
