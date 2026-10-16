using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LuochongDiscardChecks
{
    private const string Skill = "ol:luochong";

    public static void AccumulatedDiscardsRemainFrozenAcrossReplay()
    {
        foreach (var handSize in new[] { 2, 1 })
        {
            var (game, registry) = Create(handSize);
            Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == Skill &&
                Prompt(game)!.Choices.Any(choice => Action(choice) is "activate" or "luochong-pick"));
            if (Prompt(game)!.Choices.Any(choice => Action(choice) == "activate"))
                Answer(game, choice => Action(choice) == "activate");
            Reach(game, () => Prompt(game)?.Choices.Any(choice => Action(choice) == "luochong-pick") == true);
            var cards = game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).Order().ToArray();
            Require(cards.Length == handSize, "The real preparation trigger must precede normal drawing.");
            Answer(game, choice => Action(choice) == "luochong-pick" &&
                choice.Parameters.GetValueOrDefault("option") == "discardTwo" &&
                choice.Parameters.GetValueOrDefault("target-seat") == "0");
            Reach(game, () => Prompt(game)?.Choices.Any(choice => Action(choice) == "luochong-discard") == true);
            var frameId = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.LuochongPending is not null).Id;
            Require(Pending(game, frameId).RemainingDiscards == handSize && Resolved(game).Length == 0,
                "An accepted discard option must retain its unpaid count on its owning frame.");
            game = Cold(game, registry);

            for (var index = 0; index < cards.Length; index++)
            {
                var cardId = cards[index];
                Answer(game, choice => Action(choice) == "luochong-discard" && choice.Cards.SequenceEqual([cardId]));
                var paid = cards.Take(index + 1).ToArray();
                Require(paid.All(id => game.CardMovements.Count(move => move.CardId == id &&
                    move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile &&
                    move.Reason.Value == $"skill-program.{Skill}.luochong-discard") == 1),
                    "Every accepted choice must move only its own physical card once.");
                if (index + 1 < cards.Length)
                {
                    var pending = Pending(game, frameId);
                    Require(pending.RemainingDiscards == cards.Length - paid.Length &&
                        pending.DiscardedCardIds is { } accumulated && accumulated.SequenceEqual(paid) &&
                        Resolved(game).Length == 0 && Prompt(game)?.PlayerSeat == 0 &&
                        Prompt(game)!.Choices.All(choice => !choice.Cards.Contains(cardId)),
                        "The second prompt must preserve the first payment on the same frame without resolving early.");
                    Frozen(pending.DiscardedCardIds!);
                    game = Cold(game, registry);
                    Require(Pending(game, frameId).DiscardedCardIds!.SequenceEqual(paid),
                        "Cold replay must retain the complete paid prefix before the next choice.");
                    Frozen(Pending(game, frameId).DiscardedCardIds!);
                }
            }

            Reach(game, () => Resolved(game).Length > 0 && game.ResolutionStack.All(frame => frame.Id != frameId));
            var resolved = Resolved(game).Single();
            Require(resolved.FrameId == frameId && resolved.SkillId == Skill && resolved.OwnerSeat == 0 &&
                resolved.TargetSeat == 0 && resolved.OptionId == "discardTwo" &&
                resolved.DiscardedCardIds.SequenceEqual(cards) &&
                resolved.RecoveredHp == 0 && resolved.LostHp == 0 && resolved.DrawCount == 0,
                $"The real {handSize}-card discard must emit one resolution containing every paid card in choice order.");
            Require(game.CreateSnapshot(0).Players[0].HandCount == 0 &&
                cards.All(id => game.CreateCardZoneDiagnostics().Single(card => card.CardId == id).Location == CardLocation.DiscardPile) &&
                !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.LuochongPending is not null),
                "Both the one-card exhaustion and two-card completion must retire the pending state after payment.");
            Frozen(resolved.DiscardedCardIds);
            game = Cold(game, registry);
            Require(Resolved(game).Single().DiscardedCardIds.SequenceEqual(cards),
                "Cold replay must reproduce the accumulated discard event after the owning frame is gone.");
            Frozen(Resolved(game).Single().DiscardedCardIds);
        }

        var source = new[] { 41, 42 };
        var draft = new LuochongPendingState("discardTwo", 0, 1) { DiscardedCardIds = source };
        source[0] = -1;
        Require(draft.DiscardedCardIds!.SequenceEqual([41, 42]), "Draft initialization must detach its input collection.");
        Frozen(draft.DiscardedCardIds!);
        var replacement = new[] { 43 };
        var changed = draft with { DiscardedCardIds = replacement };
        replacement[0] = -1;
        Require(changed.DiscardedCardIds!.SequenceEqual([43]) && draft.DiscardedCardIds!.SequenceEqual([41, 42]),
            "A with-assignment must freeze its replacement without changing the previous draft.");
        Frozen(changed.DiscardedCardIds!);
        var jsonCopy = JsonSerializer.Deserialize<LuochongPendingState>(JsonSerializer.Serialize(changed))!;
        Require(jsonCopy.DiscardedCardIds!.SequenceEqual([43]), "JSON construction must preserve the canonical payment list.");
        Frozen(jsonCopy.DiscardedCardIds!);
    }

    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static PendingDecision? Prompt(GameEngine game) => game.CreateSnapshot(0).PendingDecision;
    private static LuochongPendingState Pending(GameEngine game, long frameId) =>
        game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == frameId).LuochongPending!;
    private static ProgramLuochongResolvedEvent[] Resolved(GameEngine game) =>
        game.Events.Select(envelope => envelope.Payload).OfType<ProgramLuochongResolvedEvent>().ToArray();
    private static void Frozen(IReadOnlyList<int> cards)
    {
        Require(cards is not int[], "Exposed discard collections must not be mutable arrays.");
        if (cards is not IList<int> list) return;
        try { list[0] = -1; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("Exposed discard collections must reject element replacement.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision,
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack),
        Events = game.Events.Select(envelope => new
        {
            envelope.Id, envelope.ParentId, envelope.Sequence, envelope.Revision, envelope.CorrelationId,
            Payload = JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())
        }).ToArray(),
        game.CardMovements,
        Commands = CommandJson.Serialize(game.AcceptedCommands),
        Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored), "Cold replay must preserve all views, frames, events, commands and physical movements.");
        return restored;
    }
    private static void Reach(GameEngine game, Func<bool> reached)
    {
        for (var step = 0; step < 40; step++)
        {
            if (reached()) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed Luochong fixture did not reach the expected preparation state.");
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = Prompt(game) ?? throw new InvalidOperationException("The Luochong choice must belong to the human target.");
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.Single(predicate).Id, game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The Luochong fixture command was rejected.");
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine Game, ContentRegistry Registry) Create(int handSize)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(handSize));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:luochong-discard",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "ol:teng-fang-lan", game.Revision, Prompt(game)!.PromptId));
        return (game, registry);
    }

    private sealed class Fixture(int handSize) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-luochong-discard", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:teng-fang-lan");
            foreach (var seat in Enumerable.Range(1, 3))
                builder.AddGeneral(new($"fixture:luochong-{seat}", "陪测角色", "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new("fixture:luochong-deck", "落宠固定牌堆", handSize, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, handSize * 4)
                    .Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, index + 1)).ToArray()
            });
            builder.AddMode(new("identity:luochong-discard", "落宠弃牌检查", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:luochong-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["ol:teng-fang-lan", "fixture:luochong-1", "fixture:luochong-2", "fixture:luochong-3"]));
        }
    }
}
