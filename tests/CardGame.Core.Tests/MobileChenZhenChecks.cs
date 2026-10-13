using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class MobileChenZhenChecks
{
    private const string Skill = "mobile:shameng";
    private const string Owner = "fixture:chen-zhen-owner";
    private const string Mode = "identity:classic-chen-zhen-fixture";

    public static void SameColorHandPaymentSharedPhaseQuotaAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId)));
        Play(game);
        var originalTurn = game.State.TurnNumber;
        var equipment = game.CreateSnapshot(0).Players[0].Hand[0].Id;
        Accept(game.Submit(new PlayCardCommand(0, equipment, [], game.Revision, game.PendingDecision!.PromptId)));
        Play(game);

        // Dangxian provides two real Play phases in one actual turn, avoiding a simulated full match.
        foreach (var red in new[] { true, false })
        {
            var branch = red ? "discard-red-pair" : "discard-black-pair";
            var hand = game.CreateSnapshot(0).Players[0].Hand;
            var firstSuit = red ? Suit.Heart : Suit.Spade;
            var secondSuit = red ? Suit.Diamond : Suit.Club;
            var cards = new[] { hand.First(card => card.Suit == firstSuit).Id, hand.First(card => card.Suit == secondSuit).Id };
            var opposite = hand.First(card => IsRed(card.Suit) != red).Id;
            var actions = game.GetHumanLegalActions().Where(action => action.ProgramSkillId == Skill).ToArray();
            var action = actions.Single(candidate => candidate.ProgramActivationId == branch);
            Require(actions.Length == 2 && action.MinCardCount == 2 && action.MaxCardCount == 2 &&
                    action.SelectableTargetSeats.Count == 3 && !action.SelectableTargetSeats.Contains(0) &&
                    action.SelectableCardIds.All(id => IsRed(hand.Single(card => card.Id == id).Suit) == red),
                "Both real menu entries must expose exact hand-only color costs and only other living recipients.");
            Require(game.CreateSnapshot(1).PendingDecision is null, "A different viewer cannot inspect the active player's hand choices.");

            Reject(game, branch, [cards[0], opposite], [1]);
            Reject(game, branch, [cards[0], cards[0]], [1]);
            Reject(game, branch, [cards[0]], [1]);
            Reject(game, branch, cards, [0]);
            Reject(game, branch, [cards[0], equipment], [1]);
            var brain = new SimpleAiBrain(0, 17);
            var view = game.CreateSnapshot(0);
            var aiCards = brain.ChooseActiveSkillCards(view, action);
            var aiTargets = brain.ChooseActiveSkillTargets(view, action, aiCards);
            Require(aiCards.Count == 2 && aiCards.Distinct().Count() == 2 &&
                    aiCards.All(action.SelectableCardIds.Contains) && aiTargets.Count == 1 && aiTargets[0] != 0,
                "The shared AI must choose a complete same-color pair and another recipient from the actual action.");
            var aiGame = GameReplay.Restore(game.CreateCheckpoint(), registry);
            Accept(aiGame.Submit(Command(aiGame, branch, aiCards, aiTargets)));

            var ownerBefore = view.Players[0].HandCount;
            var targetBefore = view.Players[1].HandCount;
            var moveStart = game.CardMovements.Count;
            Accept(game.Submit(Command(game, branch, cards, [1])));
            Replay(game, registry);
            Play(game);
            var moves = game.CardMovements.Skip(moveStart).Where(move => move.Reason.Value.StartsWith("skill-program." + Skill + ".", StringComparison.Ordinal)).ToArray();
            Require(moves.Length == 7 && moves.Take(2).All(move => move.To == CardLocation.DiscardPile) &&
                    moves.Take(2).Select(move => move.CardId).ToHashSet().SetEquals(cards) &&
                    moves.Skip(2).Take(2).All(move => move.To == CardLocation.Hand(1)) &&
                    moves.Skip(4).All(move => move.To == CardLocation.Hand(0)),
                "Different suits of the same color must pay exactly two hand cards before the recipient draws two and the owner draws three.");
            Require(game.CreateSnapshot(0).Players[0].HandCount == ownerBefore + 1 &&
                    game.CreateSnapshot(1).Players[1].HandCount == targetBefore + 2 &&
                    game.GetHumanLegalActions().All(candidate => candidate.ProgramSkillId != Skill),
                "Both color branches must consume one shared actual Play-phase allowance and preserve the exact net gains.");
            var otherCards = game.CreateSnapshot(0).Players[0].Hand.Where(card => IsRed(card.Suit) != red).Take(2).Select(card => card.Id).ToArray();
            Reject(game, red ? "discard-black-pair" : "discard-red-pair", otherCards, [1]);
            Replay(game, registry);
            if (red)
            {
                Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
                Play(game);
                Require(game.State.TurnNumber == originalTurn, "The next actual Play phase must refresh both branches within the same turn.");
            }
        }
    }

    private static bool IsRed(Suit suit) => suit is Suit.Heart or Suit.Diamond;
    private static UseProgramSkillCommand Command(GameEngine game, string branch, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
        new(0, Skill, branch, cards, targets, game.Revision, game.PendingDecision!.PromptId);
    private static void Reject(GameEngine game, string branch, IReadOnlyList<int> cards, IReadOnlyList<int> targets)
    {
        var before = State(game);
        var revision = game.Revision;
        var events = game.Events.Count;
        var moves = game.CardMovements.Count;
        Require(!game.Submit(Command(game, branch, cards, targets)).Accepted && before == State(game) &&
                revision == game.Revision && events == game.Events.Count && moves == game.CardMovements.Count,
            "Invalid color, duplicate, incomplete, self-target or exhausted-branch input must reject atomically.");
    }
    private static void Play(GameEngine game)
    {
        for (var step = 0; step < 96 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, "The small fixture did not return to the human Play boundary.");
    }
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        string[] Events(GameEngine engine) => engine.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray();
        Require(State(game) == State(restored) && game.CardMovements.SequenceEqual(restored.CardMovements) && Events(game).SequenceEqual(Events(restored)),
            "The actual activation's state, card movements and events must survive serialized checkpoint replay.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-chen-zhen", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(Owner, "歃盟阶段测试", "supporter", Skill, "shu", 3, ["classic:dangxian"]));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"fixture:chen-zhen-{seat}", $"目标{seat}", "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:chen-zhen-deck", "歃盟小牌堆", 16, 0, [new("standard:crossbow", 128)]));
            builder.AddMode(new(Mode, "歃盟阶段测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:chen-zhen-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, "fixture:chen-zhen-1", "fixture:chen-zhen-2", "fixture:chen-zhen-3"]));
        }
    }
}
