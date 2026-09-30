using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2011MovementChecks
{
    private const string Driver = "fixture:fame-movement-driver";
    private const string General = "fixture:fame-movement-owner";
    private const string Mode = "fixture:fame-movement-mode";

    public static void EquipmentLossOptionalDiscardsAndReplay()
    {
        foreach (var requested in new[] { 0, 1, 2 })
        {
            var (game, registry) = Create();
            Use(game, "draw");
            foreach (var kind in new[] { CardKind.Crossbow, CardKind.BaguaFormation })
            {
                var card = game.CreateSnapshot(0, true).Players[0].Hand.First(item => item.Kind == kind);
                var action = game.GetHumanLegalActions().First(item => item.CardId == card.Id && item.Kind == LegalActionKind.Equip);
                Accept(game.Submit(new PlayCardCommand(0, card.Id, [], game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind)));
                Drain(game, requested);
            }
            var before = Discarded(game);
            var equipment = game.CreateSnapshot(0, true).Players[0].Equipment.First();
            Accept(game.Submit(new UseProgramSkillCommand(0, Driver, "drop-equipment", [equipment.Id], [], game.Revision, game.PendingDecision!.PromptId)));
            for (var steps = 0; steps < 100 && game.PendingDecision?.Choices.All(choice =>
                     choice.Parameters.GetValueOrDefault("program-action") != "choose-other-owned-card-discard") != false; steps++)
                Step(game, requested, new HashSet<int>());
            var prompt = game.PendingDecision!;
            Require(prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard"),
                "The equipment loss must reach its optional hostile discard choices.");
            Require(prompt.Choices.Where(choice => choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand))
                    .All(choice => choice.Cards.Count == 0), "Another player's hand must remain opaque.");
            var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Drain(game, requested);
            Drain(restored, requested);
            EqualReplay(game, restored);
            Require(Discarded(game) - before == requested, "Optional repeated selection must discard exactly zero, one or two cards.");
            var losses = game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                .Where(item => item.SkillId == "classic:xuanfeng" && item.BindingId == "equipment-left-discard").ToArray();
            Require(losses.Length == 1, "One equipment loss must offer one Xuanfeng binding.");
            if (requested == 2)
            {
                var targets = game.CardMovements.Where(move => move.Reason.Value.Contains("classic:xuanfeng", StringComparison.Ordinal))
                    .Select(move => move.From.OwnerSeat).Distinct().ToArray();
                Require(targets.Length == 2, "Two choices must be able to discard one card from each of two other players.");
            }
            // A full owner discard phase supplies a second, independent timing.
            Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
            Drain(game, 0);
            Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>().Count(item =>
                    item.SkillId == "classic:xuanfeng" && item.BindingId == "discard-phase-discard") == 1,
                "Discarding at least two hand cards in the owner's discard phase must offer Xuanfeng once.");
            EqualReplay(game, GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry));
        }
    }

    private static int Discarded(GameEngine game) => game.CardMovements.Count(move =>
        move.To == CardLocation.DiscardPile && move.Reason.Value.Contains("classic:xuanfeng", StringComparison.Ordinal));
    private static void Use(GameEngine game, string id)
    {
        Accept(game.Submit(new UseProgramSkillCommand(0, Driver, id, [], [], game.Revision, game.PendingDecision!.PromptId)));
        Drain(game, 0);
    }
    private static void Drain(GameEngine game, int requested)
    {
        var selected = new HashSet<int>();
        for (var steps = 0; steps < 500; steps++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0) return;
            Step(game, requested, selected);
        }
        throw new InvalidOperationException("The movement fixture did not return to play.");
    }
    private static void Step(GameEngine game, int requested, HashSet<int> selected)
    {
        if (game.PendingDecision is not { } prompt)
        { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); return; }
        var choices = prompt.Choices;
        PromptChoice? chosen = null;
        if (choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard"))
        {
            if (selected.Count < requested)
            {
                chosen = choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-discard" &&
                    !selected.Contains(choice.Targets[0]));
                selected.Add(chosen.Targets[0]);
            }
            else chosen = choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "choose-other-owned-card-decline");
        }
        chosen ??= choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate") ??
            choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "pass") ?? choices.First();
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, chosen.Id, game.Revision)));
    }
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) => GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static void EqualReplay(GameEngine game, GameEngine replay) => Require(
        SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)) &&
        game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(
            replay.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))),
        "Paused optional hostile discards must restore and replay exactly.");
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, General, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game, 0);
        return (game, registry);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fame-movement-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,
             "minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
             {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,
              "effects":[{"op":"draw","target":"owner","amount":20}]},
             {"id":"drop-equipment","minCards":1,"maxCards":1,"sourceZones":["equipment"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
              "effects":[{"op":"discardSelected","target":"owner","amount":1}]}]}]}
            """, """{"schemaVersion":3,"skills":{"fixture:fame-movement-driver":{"name":"Fixture","description":"Fixture"}}}""");
            builder.AddSkill(new ContentSkillDefinition(Driver, "Fixture", "Fixture") { Program = catalog.Programs[Driver] });
            builder.AddGeneral(new ContentGeneralDefinition(General, "测试旋风", "ling_tong", "classic:xuanfeng", "wu", 4, [Driver]));
            var others = Enumerable.Range(1, 3).Select(index => "fixture:fame-movement-other-" + index).ToArray();
            foreach (var id in others) builder.AddGeneral(new ContentGeneralDefinition(id, "测试对手", "supporter", "standard:none", "qun", 8));
            builder.AddDeck(new ContentDeckRecipe("fixture:fame-movement-deck", "Fixture", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 208).Select(index => new ContentDeckPhysicalCard(
                index % 2 == 0 ? "standard:crossbow" : "standard:bagua", (Suit)(index % 4), index / 4 % 13 + 1)).ToArray() });
            builder.AddMode(new ContentModeDefinition(Mode, "Fixture", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 },
                "fixture:fame-movement-deck", GeneralCandidateCount: 4, GeneralPoolIds: [General, .. others]));
        }
    }
}
