using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramActivationLimitChecks
{
    private const string SkillId = "classic:zhiheng";
    private const string ActivationId = "exchange-owned-cards";
    private const string GeneralId = "fixture:phase-exchange-owner";
    private const string ModeId = "identity:classic-phase-exchange";
    private const string Rules = """
        {"schemaVersion":62,"skills":[{"id":"fixture:turn-draw","revision":1,
        "activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
        "targetKind":"anyLiving","usesPerTurn":1,"usesPerPhase":1,
        "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
        """;
    private const string Presentation = """
        {"schemaVersion":3,"skills":{"fixture:turn-draw":{"name":"限次摸牌","description":"回合和阶段分别限次。"}}}
        """;


    public static void MixedZonesAtomicityAndReplay()
    {
        var (game, registry) = Create();
        var equipmentId = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip).CardId!.Value;
        Accept(game.Submit(new PlayCardCommand(0, equipmentId, [], game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
        var owner = game.CreateSnapshot(0).Players[0];
        var action = Exchange(game);
        Require(action.SelectableCardIds.Contains(equipmentId) &&
                action.MaxCardCount == owner.HandCount + owner.Equipment.Count &&
                game.CreateSnapshot(1).PendingDecision is null,
            "An exchange must advertise its current owned cards only to the acting player.");
        var cards = owner.Hand.Take(2).Select(card => card.Id).Append(equipmentId).ToArray();
        Require(action.ProgramAiHint is { OwnerDraw: 1, DiscardsSelected: true, ValueAdjustment: 0 },
            "An active exchange must expose its draw benefit without deducting the input-card cost twice.");
        var ai = new SimpleAiBrain(0, seed: 17);
        var view = game.CreateSnapshot(0);
        var decision = ai.ChoosePlay(view, [action, new LegalAction(LegalActionKind.EndPlay, null, null, "结束")], 1);
        Require(decision.Action.ProgramSkillId == SkillId && ai.ChooseActiveSkillCards(view, action).Count == 1,
            "Shared AI must recognize a useful exchange and choose a legal owned card.");
        RejectUse(game, [], []);
        RejectUse(game, [cards[0], cards[0]], []);
        RejectUse(game, [game.CreateSnapshot(0, revealAll: true).Players[1].Hand[0].Id], []);
        RejectUse(game, cards, [1]);
        var before = game.CardMovements.Count;
        Use(game, cards);
        ReachPlay(game);
        var after = game.CreateSnapshot(0).Players[0];
        var moves = game.CardMovements.Skip(before).Where(move =>
            move.Reason.Value == "skill-program.classic:zhiheng.MoveBoundCards").ToArray();
        Require(after.HandCount == owner.HandCount + 1 && after.Equipment.All(card => card.Id != equipmentId) &&
                moves.Length == cards.Length && moves.Select(move => move.CardId).ToHashSet().SetEquals(cards) &&
                moves.All(move => move.To == CardLocation.DiscardPile) &&
                moves.Single(move => move.CardId == equipmentId).From == CardLocation.Equipment(0),
            "One shared exchange must discard the exact hand/equipment set and draw the same count.");
        Require(game.GetHumanLegalActions().All(item => item.ProgramSkillId != SkillId) &&
                game.Events.Count(item => item.Payload is ProgramSkillResolvedEvent { SkillId: SkillId, Completed: true }) == 1,
            "The allowance must be consumed once with one completed Program activation.");
        RejectUse(game, [after.Hand[0].Id], []);
        AssertReplay(game, registry);
    }




    private static LegalAction Exchange(GameEngine game) => game.GetHumanLegalActions().Single(action => action.ProgramSkillId == SkillId);
    private static void Use(GameEngine game, IReadOnlyList<int> cards) => Accept(game.Submit(
        new UseProgramSkillCommand(0, SkillId, ActivationId, cards, [], game.Revision, game.PendingDecision!.PromptId)));
    private static void RejectUse(GameEngine game, IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        var state = State(game);
        var revision = game.Revision;
        var eventCount = game.Events.Count;
        var moves = game.CardMovements.Count;
        var result = game.Submit(new UseProgramSkillCommand(0, SkillId, ActivationId, cards, targets,
            revision, game.PendingDecision!.PromptId));
        Require(!result.Accepted && game.Revision == revision && game.Events.Count == eventCount &&
                game.CardMovements.Count == moves && State(game) == state,
            "Invalid or exhausted exchange commands must reject atomically.");
    }
    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        string[] Events(GameEngine engine) => engine.Events.Select(item =>
            item.Payload.GetType().Name + JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray();
        Require(State(game) == State(replay) && game.CardMovements.SequenceEqual(replay.CardMovements) &&
                Events(game).SequenceEqual(Events(replay)), "Exchange state, movement and events must replay exactly.");
    }
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "Fixture did not reach its Play prompt.");
    }
    private static void Reject(string rules, string expected)
    {
        try { SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException exception) when (exception.Message.Contains(expected, StringComparison.Ordinal)) { return; }
        throw new InvalidOperationException($"The definition did not reject {expected}.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command failed.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine Game, ContentRegistry Registry) Create(int initialHand = 4, bool extraPhase = false,
        bool equipmentTrigger = false, bool healing = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(initialHand, extraPhase, equipmentTrigger, healing));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = ModeId,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, GeneralId, game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
        return (game, registry);
    }
    private sealed class Fixture(int initialHand, bool extraPhase, bool equipmentTrigger, bool healing) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-phase-exchange", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            builder.AddSkill(new ContentSkillDefinition("fixture:turn-draw", "限次摸牌", "回合和阶段分别限次。")
            { Program = catalog.Programs["fixture:turn-draw"], ProgramPresentation = catalog.Presentations["fixture:turn-draw"] });
            var additional = new List<string> { "fixture:turn-draw" };
            if (extraPhase) additional.Add("classic:dangxian");
            if (equipmentTrigger) additional.Add("classic:xiaoji");
            if (healing) additional.AddRange(["classic:qingnang", "classic:kujin"]);
            builder.AddGeneral(new ContentGeneralDefinition(GeneralId, "牌主", "supporter", SkillId, "wu", BaseHp: 4,
                AdditionalSkillIds: additional));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:phase-exchange-{seat}", $"目标{seat}", "supporter",
                    "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:phase-exchange-deck", "换牌测试", initialHand, 0,
                [new ContentDeckCardCount("standard:crossbow", 400)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "阶段换牌测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:phase-exchange-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, "fixture:phase-exchange-1", "fixture:phase-exchange-2", "fixture:phase-exchange-3"]));
        }
    }
}
