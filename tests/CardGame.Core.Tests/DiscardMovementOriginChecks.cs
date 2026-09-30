using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DiscardMovementOriginChecks
{
    public static void NativeFireAttackDiscardPreservesOriginAndReplay()
    {
        var (game, registry) = Start("standard:fire_attack", Suit.Heart);
        var card = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.FireAttack).CardId!.Value;
        Accept(game.Submit(new PlayCardCommand(0, card, [1], game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, () => game.PendingDecision is { Kind: DecisionKind.FireAttackDiscard });
        var cost = game.PendingDecision!.Choices.First(c => c.Cards.Count == 1);
        var costId = cost.Cards.Single();
        Choose(game, cost);
        Reach(game, () => IsZongxuan(game));
        var middle = game.CreateCheckpoint();
        PutExactDiscardOnTop(game, costId);
        Settle(game);
        var begin = game.CardMovements.Single(m => m.CardId == costId && m.Reason == CardMoveReasons.FireAttackDiscard);
        var completed = game.CardMovements.Single(m => m.CardId == costId && m.Reason == CardMoveReasons.FireAttackDiscardFinished);
        Require(begin.From == CardLocation.Hand(0) && begin.To == CardLocation.Processing &&
            completed.From == CardLocation.Processing && completed.To == CardLocation.DiscardPile,
            "The native FireAttack ledger must preserve its actual two physical movements.");
        Require(game.CardMovements.Any(m => m.CardId == costId && m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile),
            "The discarded payment must remain reclaimable by its original owner's discard-only trigger.");
        Require(!game.CardMovements.Any(m => m.CardId == card && m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile),
            "Used-card cleanup must not be mistaken for a discard payment.");
        ReplayFrom(game, registry, middle);
    }

    public static void NativeDismantlementDiscardPreservesOriginAndReplay()
    {
        var (game, registry) = Start("standard:dismantlement", Suit.Club);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, () => IsZongxuan(game), 500);
        var completed = game.CardMovements.Last(m => m.To == CardLocation.DiscardPile &&
            m.Reason == CardMoveReasons.DismantlementFinished);
        var begin = game.CardMovements.Last(m => m.CardId == completed.CardId && m.Sequence < completed.Sequence);
        Require(begin.From == CardLocation.Hand(0) && begin.To == CardLocation.Processing && completed.From == CardLocation.Processing,
            "Native Dismantlement must expose the discarded owner's processing origin without rewriting its ledger.");
        var middle = game.CreateCheckpoint();
        PutExactDiscardOnTop(game, completed.CardId);
        Settle(game);
        Require(game.CardMovements.Any(m => m.CardId == completed.CardId && m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile),
            "Dismantlement's victim must reclaim the exact discarded card through the opt-in discard origin.");
        ReplayFrom(game, registry, middle);
    }

    private static bool IsZongxuan(GameEngine game) => game.PendingDecision is
        { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: "classic:zongxuan" };
    private static void PutExactDiscardOnTop(GameEngine game, int id)
    {
        var prompt = game.PendingDecision!;
        if (prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"))
            Choose(game, prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Choose(game, game.PendingDecision!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "discard-top-select" && c.Cards.SequenceEqual([id])));
        Choose(game, game.PendingDecision!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "discard-top-finish"));
    }
    private static void ReplayFrom(GameEngine game, ContentRegistry registry, GameCheckpoint middle)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(middle)), registry);
        foreach (var command in game.AcceptedCommands.Skip(middle.Commands.Count)) Accept(replay.Submit(command));
        var full = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(State(game) == State(replay) && State(game) == State(full) && game.CardMovements.SequenceEqual(replay.CardMovements) &&
            game.CardMovements.SequenceEqual(full.CardMovements) && Events(game).SequenceEqual(Events(replay)) && Events(game).SequenceEqual(Events(full)),
            "Processing-origin discard windows must restore their command, movement and event sequence exactly.");
    }
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(e =>
        $"{e.Id}|{e.ParentId}|{e.Sequence}|{e.Revision}|{e.CorrelationId}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray();
    private static void Choose(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));
    private static void Advance(GameEngine game) => Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static void Reach(GameEngine game, Func<bool> done, int budget = 100)
    {
        for (var step = 0; step < budget; step++)
        { if (done()) return; Advance(game); }
        throw new InvalidOperationException("The native discard-origin boundary was not reached.");
    }
    private static void Settle(GameEngine game) => Reach(game, () => game.ResolutionStack.Count == 0, 100);
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Discard-origin command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine Game, ContentRegistry Registry) Start(string cardId, Suit suit)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new Fixture(cardId, suit));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "fixture:discard-origin", UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:discard-owner", game.Revision, game.PendingDecision!.PromptId)));
        Reach(game, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        return (game, registry);
    }
    private sealed class Fixture(string cardId, Suit suit) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("discard-origin-fixture", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            string Read(string suffix)
            { using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith("classic-yu-fan." + suffix, StringComparison.Ordinal)))!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var catalog = SkillProgramCatalog.Load(Read("rules.json"), Read("presentation.json"));
            var program = catalog.Programs["classic:zongxuan"];
            builder.AddSkill(new ContentSkillDefinition("classic:zongxuan", "纵玄", "测试") { Program = program });
            var pool = new List<string> { "fixture:discard-owner" };
            builder.AddGeneral(new("fixture:discard-owner", "弃牌观察", "supporter", "classic:zongxuan", BaseHp: 8));
            for (var i = 0; i < 3; i++)
            { var id = "fixture:discard-bank-" + i; pool.Add(id); builder.AddGeneral(new(id, "测试对手", "supporter", "standard:none", BaseHp: 8)); }
            builder.AddDeck(new("fixture:discard-deck", "弃牌测试", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(cardId, suit, i % 13 + 1)).ToArray() });
            builder.AddMode(new("fixture:discard-origin", "弃牌来源", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:discard-deck", GeneralCandidateCount: 4, GeneralPoolIds: pool));
        }
    }
}
