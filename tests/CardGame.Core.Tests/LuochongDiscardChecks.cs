using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LuochongDiscardChecks
{
    private const string Skill = "ol:luochong";
    private const string LethalDriver = "fixture:luochong-lethal-driver";

    public static void LethalLossKeepsCommittedCursorAcrossDyingReplay()
    {
        foreach (var targetSeat in new[] { 0, 1 })
        {
            var (game, registry) = CreateLethal();
            Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == LethalDriver &&
                Prompt(game)!.Choices.Any(choice => Action(choice) == "select-target"));
            var players = game.CreateSnapshot(0).Players;
            Require(players.All(player => player.Hp == 4 && player.MaxHp == 4) &&
                players[0].GeneralId == "fixture:luochong-lethal-owner" &&
                players[0].Skills!.Select(skill => skill.Id).SequenceEqual([Skill, "ol:aichen", LethalDriver]) &&
                players.Skip(1).All(player => player.GeneralId.StartsWith("fixture:luochong-lethal-peer-", StringComparison.Ordinal) &&
                    player.Skills!.Select(skill => skill.Id).SequenceEqual(["standard:none"])),
                "Every selected actual fixture general must have four HP; only its human owner carries the native skills and rescue conversion.");
            Answer(game, choice => Action(choice) == "select-target" && choice.Targets.SequenceEqual([targetSeat]));
            Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == Skill);
            Require(game.CreateSnapshot(0).Players[targetSeat].Hp == 1 &&
                Facts<ProgramSkillHpLostEvent>(game).Count(fact => fact.SkillId == LethalDriver &&
                    fact.TargetSeat == targetSeat && fact.Amount == 3 && fact.RemainingHp == 1) == 1,
                "The real game-start program must lower the selected four-HP target to one before native preparation.");
            if (Prompt(game)!.Choices.Any(choice => Action(choice) == "activate"))
                Answer(game, choice => Action(choice) == "activate");
            Reach(game, () => Prompt(game)?.Choices.Any(choice => Action(choice) == "luochong-pick") == true);
            var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Skill);
            Require(parent.TriggerId == "luochong-preparation" && parent.InstructionIndex == 1 &&
                registry.GetSkill(Skill).Program!.Triggers.Single(trigger => trigger.Id == parent.TriggerId).Effects.Count == 1,
                "The native preparation binding must pause on its first and only committed operation.");
            var parentId = parent.Id;
            game = Cold(game, registry);
            Answer(game, choice => Action(choice) == "luochong-pick" &&
                choice.Parameters.GetValueOrDefault("option") == "loseHp" &&
                choice.Targets.SequenceEqual([targetSeat]));
            var dying = game.ResolutionStack.OfType<DyingFrame>().Single(frame => frame.ParentFrameId == parentId);
            AssertLethalReceipt(game, parentId, dying.Id, targetSeat);

            if (targetSeat == 0)
            {
                Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == "ol:aichen" &&
                    Prompt(game)!.Choices.Any(choice => Action(choice) == "aichen-remove"));
                Require(game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == "ol:aichen")
                    .WindowContext is { Window: SkillProgramTriggerWindow.DyingEntering, TargetSeat: 0 },
                    "Losing the owner's final HP must expose the native Aichen dying-entry child before rescue.");
                game = Cold(game, registry);
                AssertLethalReceipt(game, parentId, dying.Id, targetSeat);
                Answer(game, choice => Action(choice) == "aichen-remove" &&
                    choice.Parameters.GetValueOrDefault("option") == "recover");
            }
            Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } prompt &&
                prompt.TargetSeat == targetSeat && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "peach"));
            AssertLethalReceipt(game, parentId, dying.Id, targetSeat);
            Require(Facts<ProgramAichenRemovedEvent>(game).Length == (targetSeat == 0 ? 1 : 0),
                "Aichen removes one option for the skill owner entering dying and never for an unrelated target.");
            var rescue = Prompt(game)!.Choices.First(choice => choice.Parameters.GetValueOrDefault("response") == "peach");
            var rescueCard = rescue.Cards.Single();
            game = Cold(game, registry);
            AssertLethalReceipt(game, parentId, dying.Id, targetSeat);
            Answer(game, choice => choice.Id == rescue.Id);
            Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            var resolved = Resolved(game).Single();
            Require(resolved.FrameId == parentId && resolved.BindingId == "luochong-preparation" &&
                resolved.OwnerSeat == 0 && resolved.TargetSeat == targetSeat && resolved.OptionId == "loseHp" &&
                resolved.LostHp == 1 && resolved.RecoveredHp == 0 && resolved.DrawCount == 0 &&
                resolved.DiscardedCardIds.Count == 0 && game.CreateSnapshot(0).Players[targetSeat] is { Hp: 1, IsAlive: true } &&
                Facts<ProgramSkillHpLostEvent>(game).Count(fact => fact.FrameId == parentId && fact.TargetSeat == targetSeat &&
                    fact.Amount == 1 && fact.RemainingHp == 0) == 1 &&
                Facts<DyingResolvedEvent>(game).Count(fact => fact.ResolutionId == dying.Id && fact.VictimSeat == targetSeat && fact.Survived) == 1 &&
                Facts<DyingResponseEvent>(game).Count(fact => fact.ResolutionId == dying.Id && fact.ResponderSeat == 0 &&
                    fact.UsedPeach && fact.PeachCardId == rescueCard) == 1 &&
                game.CardMovements.Count(move => move.CardId == rescueCard && move.From == CardLocation.Hand(0) &&
                    move.To == CardLocation.Processing) == 1 &&
                Facts<ProgramBindingResolvedEvent>(game).Count(fact => fact.FrameId == parentId && fact.Completed) == 1 &&
                !game.ResolutionStack.Any(frame => frame.Id == parentId || frame.Id == dying.Id) &&
                !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.LuochongPending is not null),
                "The real rescue must return once, pay one HP and one physical rescue card, publish one ledger fact and retire both owning frames.");
            game = Cold(game, registry);
            Accept(game, new UseProgramSkillCommand(0, LethalDriver, "wound-owner", [], [], game.Revision, Prompt(game)!.PromptId));
            Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == Skill);
            if (Prompt(game)!.Choices.Any(choice => Action(choice) == "activate"))
                Answer(game, choice => Action(choice) == "activate");
            Reach(game, () => Prompt(game)?.Choices.Any(choice => Action(choice) == "luochong-pick") == true);
            var next = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Skill);
            Require(next.TriggerId == "luochong-damage" && Resolved(game).Length == 1 &&
                !Prompt(game)!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option") == "loseHp" ||
                    choice.Targets.Contains(targetSeat)),
                "A later real native damage window in the same round must exclude both the paid option and its target from the replayed ledger.");
            game = Cold(game, registry);
            Answer(game, choice => Action(choice) == "luochong-decline");
            Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Require(Resolved(game).Length == 1 && Facts<ProgramSkillHpLostEvent>(game).Count(fact => fact.FrameId == parentId) == 1 &&
                !game.ResolutionStack.Any(frame => frame.Id == parentId || frame.Id == dying.Id || frame.Id == next.Id),
                "Declining the later native opportunity must keep the original once-only payment and retire the new frame.");
            _ = Cold(game, registry);
        }
    }

    private static void AssertLethalReceipt(GameEngine game, long parentId, long dyingId, int targetSeat)
    {
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.Id == parentId);
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single(frame => frame.Id == dyingId);
        Require(parent is { InstructionIndex: 1, ReexecuteParticipantInstruction: true,
                LuochongPending: { OptionId: "loseHp", RemainingDiscards: 0 } } &&
            parent.LuochongPending.TargetSeat == targetSeat && parent.LuochongPending.DiscardedCardIds.Count == 0 &&
            dying.ParentFrameId == parentId && dying.VictimSeat == targetSeat && dying.KillerSeat is null &&
            dying.Continuation == DyingContinuationKind.ProgramSkill &&
            game.CreateSnapshot(0).Players[targetSeat] is { Hp: 0, IsAlive: true } && Resolved(game).Length == 0 &&
            Facts<ProgramSkillHpLostEvent>(game).Count(fact => fact.FrameId == parentId && fact.SkillId == Skill &&
                fact.TargetSeat == targetSeat && fact.Amount == 1 && fact.RemainingHp == 0) == 1 &&
            Facts<PlayerDyingEvent>(game).Count(fact => fact.ResolutionId == dyingId && fact.VictimSeat == targetSeat) == 1,
            "The accepted lethal loss must retain its committed first cursor and once-paid receipt beneath its exact real dying child.");
    }

    private static T[] Facts<T>(GameEngine game) => game.Events.Select(envelope => envelope.Payload).OfType<T>().ToArray();

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

    private static (GameEngine Game, ContentRegistry Registry) CreateLethal()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new LethalFixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-luochong-lethal",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:luochong-lethal-owner", game.Revision, Prompt(game)!.PromptId));
        return (game, registry);
    }

    private sealed class LethalFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-luochong-lethal", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryTengFangLanContent", throwOnError: true)!
                .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{LethalDriver}}","revision":1,"viewAs":[
                    {"id":"real-rescue","inputKinds":["dodge"],"inputSuits":[],"outputKind":"peach","forPlay":false,"forResponse":true}],
                   "triggers":[{"id":"real-opening-hp-loss","window":"gameStarting","subject":"owner","optional":false,"effects":[
                    {"op":"selectTarget","target":"owner","targetKind":"anyLiving"},
                    {"op":"loseHp","target":"selectedTarget","amount":3}]}],
                   "activations":[{"id":"wound-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                    "targetKind":"anyLiving","usesPerTurn":1,"effects":[
                     {"op":"recover","target":"owner","amount":3},
                     {"op":"damage","target":"owner","amount":1}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object>
                    {
                        [LethalDriver] = new { name = "真实体力和救援夹具", description = "开局先真实失去体力，再用手牌转化桃救援。" }
                    }
                }));
            builder.AddSkill(new(LethalDriver, "真实体力和救援夹具", "走原生濒死与返回规则。")
            { Program = catalog.Programs[LethalDriver], ProgramPresentation = catalog.Presentations[LethalDriver] });
            builder.AddGeneral(new("fixture:luochong-lethal-owner", "落宠致命续行拥有者", "supporter", Skill, "wu", 3,
                ["ol:aichen", LethalDriver]));
            var peers = Enumerable.Range(1, 3).Select(seat => $"fixture:luochong-lethal-peer-{seat}").ToArray();
            foreach (var peer in peers)
                builder.AddGeneral(new(peer, "无救援转换陪测角色", "supporter", "standard:none", "wei", 4));
            builder.AddDeck(new("fixture:luochong-lethal-deck", "真实救援固定牌堆", 2, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 16)
                    .Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, 7)).ToArray()
            });
            builder.AddMode(new("identity:classic-luochong-lethal", "落宠致命续行检查", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:luochong-lethal-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:luochong-lethal-owner", .. peers]));
        }
    }

    private sealed class Fixture(int handSize) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-luochong-discard", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.OrdinaryTengFangLanContent", throwOnError: true)!
                .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
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
