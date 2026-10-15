using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuihuanRetaliationChecks
{
    private const string Skill = "ol:zhuihuan";
    private const string Driver = "fixture:zhuihuan-incoming";
    private const string Pause = "fixture:zhuihuan-damage-child";

    public static void AccumulatedPunishmentsSurviveDamageChildrenAndReplay()
    {
        AssertOwnedCollections();
        var (game, registry) = Create();
        Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        var players = game.CreateSnapshot(0).Players;
        var selected = Facts<GeneralSelectedEvent>(game);
        Require(selected.Length == 4 && selected.Select(fact => fact.GeneralId).Distinct().Count() == 4,
            "Interactive selection must assign each fixed fixture general exactly once.");
        var discardSeat = selected.Single(fact => fact.GeneralId == "fixture:zhuihuan-low").ActorSeat;
        var damagedSeats = selected.Where(fact => fact.GeneralId.StartsWith("fixture:zhuihuan-high-", StringComparison.Ordinal))
            .Select(fact => fact.ActorSeat).Order().ToArray();
        Require(damagedSeats.Length == 2 && players[discardSeat].MaxHp == 4 && damagedSeats.All(seat => players[seat].MaxHp == 9),
            "Classic identity must use the printed HP of one low-HP payer and both distinct high-HP damagers.");
        var initialHp = players.ToDictionary(player => player.Seat, player => player.Hp);
        var discardedCards = game.CreateSnapshot(discardSeat).Players[discardSeat].Hand.Select(card => card.Id).Order().ToArray();
        Require(discardedCards.Length == 2, "The discard source must start with exactly two genuine hand cards.");

        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == Skill &&
            Prompt(game)!.Choices.Any(choice => Action(choice) is "activate" or "zhuihuan-arm"));
        if (Prompt(game)!.Choices.Any(choice => Action(choice) == "activate"))
            Answer(game, choice => Action(choice) == "activate");
        Reach(game, () => HasAction(game, "zhuihuan-arm"));
        Require(Prompt(game) is { IsPrivate: true, PlayerSeat: 0 } &&
            Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "The real ending trigger must keep the marked character secret from other viewers.");
        Answer(game, choice => Action(choice) == "zhuihuan-arm" && choice.Targets.SequenceEqual([0]));
        var armId = Facts<ProgramZhuihuanArmedEvent>(game).Single().FrameId;
        Trace(game, "armed");
        game = Cold(game, registry);

        // Real sourceRef damage after the arm establishes the ledger in a fixed
        // order. No event, HP, card zone or pending frame is injected by the test.
        foreach (var source in new[] { discardSeat }.Concat(damagedSeats))
        {
            Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == Driver && HasAction(game, "select-target"));
            Answer(game, choice => Action(choice) == "select-target" && choice.Targets.SequenceEqual([source]));
        }
        Reach(game, () => Facts<DamageAppliedEvent>(game).Count(fact => fact.TargetSeat == 0 && fact.Amount == 1) == 3 &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().All(frame => frame.SkillId != Driver));
        Require(Facts<DamageAppliedEvent>(game).Where(fact => fact.TargetSeat == 0 && fact.Amount == 1)
                .Select(fact => fact.SourceSeat).SequenceEqual(new[] { discardSeat }.Concat(damagedSeats)) &&
            game.CreateSnapshot(0).Players[0].Hp == initialHp[0] - 3 &&
            initialHp[discardSeat] <= initialHp[0] - 3 && damagedSeats.All(seat => initialHp[seat] > initialHp[0] - 3),
            "Three genuine attributed damages must establish both punishment branches before the marked preparation.");
        Trace(game, "attributed-damage-complete");
        game = Cold(game, registry);

        long? retaliationId = null;
        for (var index = 0; index < damagedSeats.Length; index++)
        {
            var damagedSeat = damagedSeats[index];
            Reach(game, () => Prompt(game)?.SkillPrompt?.SkillId == Pause && Prompt(game)!.PlayerSeat == damagedSeat);
            var owner = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.ZhuihuanRetaliate is not null);
            retaliationId ??= owner.Id;
            var pending = owner.ZhuihuanRetaliate!;
            Require(owner.Id == retaliationId && owner.SkillId == Skill && owner.OwnerSeat == 0 &&
                pending.ChosenSeat == 0 && pending.DamagedSeats.SequenceEqual(damagedSeats.Take(index + 1)) &&
                pending.DiscardedSeats.SequenceEqual([discardSeat]) && pending.DiscardedCardCount == 2 &&
                pending.PendingDamagers.SequenceEqual(damagedSeats.Skip(index + 1)) &&
                Facts<ProgramZhuihuanResolvedEvent>(game).Length == 0,
                "The same live parent must retain earlier discards and every issued punishment across each damage child.");
            AssertFrozen(pending);
            var child = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
            var damageWindow = game.ResolutionStack.OfType<DamageTriggerWindowFrame>()
                .Single(window => window.Id == child.WindowContext?.ParentFrameId);
            var damage = game.ResolutionStack.OfType<DamageFrame>()
                .Single(frame => frame.Id == damageWindow.ParentFrameId);
            Require(child.SkillId == Pause && child.OwnerSeat == damagedSeat && child.Id != owner.Id &&
                child.WindowContext?.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                damage.ParentFrameId == owner.Id && damage.SourceSeat == 0 && damage.TargetSeat == damagedSeat &&
                damage.Amount == 2 && damageWindow.SourceSeat == 0 && damageWindow.TargetSeat == damagedSeat &&
                owner.InstructionIndex == 1 && owner.ReexecuteParticipantInstruction &&
                Array.FindIndex(game.ResolutionStack.ToArray(), frame => frame.Id == child.Id) >
                    Array.FindIndex(game.ResolutionStack.ToArray(), frame => frame.Id == owner.Id) &&
                game.CreateSnapshot(0).Players[damagedSeat].Hp == initialHp[damagedSeat] - 2,
                "The pause must be a real after-damage child above the still-suspended retaliation parent.");
            AssertDiscardPaidOnce(game, discardSeat, discardedCards);
            foreach (var paid in damagedSeats.Take(index + 1))
                Require(Facts<DamageAppliedEvent>(game).Count(fact => fact.SourceSeat == 0 &&
                    fact.TargetSeat == paid && fact.Amount == 2) == 1,
                    "Each issued retaliation must apply exactly one two-point damage before its child returns.");
            Trace(game, $"damage-child-{index + 1}");
            game = Cold(game, registry);
            Accept(game, new AdvanceOneStepCommand(game.Revision)); // The actual AI answers its own child prompt.
        }

        Reach(game, () => Facts<ProgramZhuihuanResolvedEvent>(game).Length == 1 &&
            game.ResolutionStack.All(frame => frame.Id != retaliationId));
        var resolved = Facts<ProgramZhuihuanResolvedEvent>(game).Single();
        Require(resolved.FrameId == retaliationId && resolved.SkillId == Skill && resolved.OwnerSeat == 0 &&
            resolved.ChosenSeat == 0 && resolved.ArmFrameIds.SequenceEqual([armId]) &&
            resolved.DamagedSeats.SequenceEqual(damagedSeats) && resolved.DiscardedSeats.SequenceEqual([discardSeat]) &&
            resolved.DiscardedCardCount == 2 &&
            !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.ZhuihuanRetaliate is not null),
            "Returning from both damage children must emit one complete punishment event and retire the owning state.");
        AssertDiscardPaidOnce(game, discardSeat, discardedCards);
        Require(damagedSeats.All(seat => game.CreateSnapshot(0).Players[seat].Hp == initialHp[seat] - 2 &&
            Facts<DamageAppliedEvent>(game).Count(fact => fact.SourceSeat == 0 && fact.TargetSeat == seat && fact.Amount == 2) == 1),
            "Finalization must neither repeat a child damage nor consume any additional discard payment.");
        Trace(game, "resolved");
        game = Cold(game, registry);
        Reach(game, () => Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Require(Facts<ProgramZhuihuanResolvedEvent>(game).Length == 1,
            "The second own/foreign preparation binding must not settle the already-consumed arm again.");
        Trace(game, "normal-play-resumed");
        _ = Cold(game, registry);
    }

    private static void AssertOwnedCollections()
    {
        var pending = new[] { 2, 1, 2 };
        var damaged = new[] { 3, 3 };
        var discarded = new[] { 1, 1 };
        var state = new ZhuihuanRetaliateState(0, pending, 4)
        { DamagedSeats = damaged, DiscardedSeats = discarded };
        pending[0] = damaged[0] = discarded[0] = 99;
        Require(state.PendingDamagers.SequenceEqual([2, 1, 2]) && state.DamagedSeats.SequenceEqual([3, 3]) &&
            state.DiscardedSeats.SequenceEqual([1, 1]),
            "Construction must copy every collection while preserving ledger order and repeated seats.");
        AssertFrozen(state);

        var nextPending = new[] { 1, 2 };
        var nextDamaged = new[] { 3, 3, 2 };
        var nextDiscarded = new[] { 1, 1, 2 };
        var next = state with
        { PendingDamagers = nextPending, DamagedSeats = nextDamaged, DiscardedSeats = nextDiscarded, DiscardedCardCount = 6 };
        nextPending[0] = nextDamaged[0] = nextDiscarded[0] = 99;
        Require(next.PendingDamagers.SequenceEqual([1, 2]) && next.DamagedSeats.SequenceEqual([3, 3, 2]) &&
            next.DiscardedSeats.SequenceEqual([1, 1, 2]) && state.PendingDamagers.SequenceEqual([2, 1, 2]) &&
            state.DamagedSeats.SequenceEqual([3, 3]) && state.DiscardedSeats.SequenceEqual([1, 1]),
            "Record updates must copy replacement lists without changing the previously published parent state.");
        AssertFrozen(next);
        var json = JsonSerializer.Serialize(next);
        var restored = JsonSerializer.Deserialize<ZhuihuanRetaliateState>(json)!;
        Require(JsonSerializer.Serialize(restored) == json,
            "Diagnostic JSON construction must retain the canonical collection values.");
        AssertFrozen(restored);
        AssertFrozen(new ZhuihuanRetaliateState(0, []));
    }

    private static void AssertFrozen(ZhuihuanRetaliateState state)
    {
        foreach (var values in new[] { state.PendingDamagers, state.DamagedSeats, state.DiscardedSeats })
        {
            Require(values is IList<int> { IsReadOnly: true }, "Every exposed retaliation collection must be frozen.");
            var list = (IList<int>)values;
            RejectMutation(() => list.Add(99));
            RejectMutation(list.Clear);
            if (list.Count > 0) RejectMutation(() => list[0] = 99);
        }
    }
    private static void RejectMutation(Action mutation)
    {
        try { mutation(); }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A diagnostic retaliation collection accepted external mutation.");
    }

    private static void AssertDiscardPaidOnce(GameEngine game, int seat, IReadOnlyList<int> cards)
    {
        var paid = game.CardMovements.Where(move => move.Reason.Value == $"skill-program.{Skill}.zhuihuan-discard").ToArray();
        Require(paid.Length == cards.Count && paid.Select(move => move.CardId).Order().SequenceEqual(cards.Order()) &&
            paid.All(move => move.From == CardLocation.Hand(seat) && move.To == CardLocation.DiscardPile) &&
            game.CreateSnapshot(seat).Players[seat].HandCount == 0 &&
            cards.All(id => game.CreateCardZoneDiagnostics().Single(card => card.CardId == id).Location == CardLocation.DiscardPile),
            "The early discard branch must pay its two actual hand cards once and preserve their public destinations.");
    }
    private static T[] Facts<T>(GameEngine game) => game.Events.Select(envelope => envelope.Payload).OfType<T>().ToArray();
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static bool HasAction(GameEngine game, string action) => Prompt(game)?.Choices.Any(choice => Action(choice) == action) == true;
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = Prompt(game)!;
        Require(prompt.PlayerSeat == 0, "Only the human owner's published setup choices may be submitted directly.");
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.Single(predicate).Id, game.Revision));
    }
    private static void Reach(GameEngine game, Func<bool> reached)
    {
        for (var step = 0; step < 160; step++)
        {
            if (reached()) return;
            if (Prompt(game) is { PlayerSeat: 0 } prompt)
                throw new InvalidOperationException($"Unexpected human prompt in the fixed retaliation fixture: {JsonSerializer.Serialize(prompt)}");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed retaliation fixture did not reach its expected child or boundary.");
    }
    private static string State(GameEngine game, bool includeFrames = true) => JsonSerializer.Serialize(new
    {
        game.Revision,
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray(),
        Frames = includeFrames ? JsonSerializer.Serialize(game.ResolutionStack) : null,
        Events = game.Events.Select(envelope => new
        {
            envelope.Id, envelope.ParentId, envelope.Sequence, envelope.Revision, envelope.CorrelationId,
            Payload = JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())
        }).ToArray(),
        game.CardMovements,
        Commands = CommandJson.Serialize(game.AcceptedCommands),
        Zones = game.CreateCardZoneDiagnostics(),
        Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint()),
        Random = ((DeterministicRandom)typeof(GameEngine).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game)!).State
    });
    private static void Trace(GameEngine game, string boundary) => Console.WriteLine(
        $"ZHUIHUAN-TRACE {boundary} revision={game.Revision} sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(State(game, includeFrames: false))))}");
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored), "Cold replay must preserve all viewers, live parents/children, events, commands and physical payments.");
        foreach (var frame in restored.ResolutionStack.OfType<ProgramSkillFrame>())
            if (frame.ZhuihuanRetaliate is { } state) AssertFrozen(state);
        return restored;
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        CommandResult result;
        try { result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); }
        catch
        {
            Console.WriteLine("ZHUIHUAN-FAULT " + JsonSerializer.Serialize(new
            {
                game.Revision, Command = command.GetType().Name,
                Frames = game.ResolutionStack.Select(frame => new
                {
                    Type = frame.GetType().Name, frame.Id,
                    Program = frame is ProgramSkillFrame program ? new
                    {
                        program.SkillId, program.InstructionIndex, program.ReexecuteParticipantInstruction,
                        program.ZhuihuanRetaliate, Window = program.WindowContext?.Window
                    } : null
                }).ToArray()
            }));
            throw;
        }
        Require(result.Accepted, result.Error?.Message ?? "The retaliation fixture command was rejected.");
    }
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine Game, ContentRegistry Registry) Create()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-zhuihuan-retaliation",
            UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:zhuihuan-owner", game.Revision, Prompt(game)!.PromptId));
        return (game, registry);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhuihuan-retaliation", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:yang-wan");
            var incoming = Enumerable.Range(1, 3).Select(index => new
            {
                id = $"incoming-{index}", window = "afterTurnEnded", subject = "owner", priority = 4 - index,
                usageScope = "game", usageLimit = 1, optional = false,
                effects = new object[]
                {
                    new { op = "selectTarget", target = "owner", targetKind = "otherLiving" },
                    new { op = "damage", target = "owner", sourceRef = new { kind = "selectedTarget" }, amount = 1 }
                }
            }).ToArray();
            var rules = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                skills = new object[]
                {
                    new { id = Driver, revision = 1, triggers = incoming },
                    new { id = Pause, revision = 1, triggers = new[] { new
                    {
                        id = "paused-child", window = "afterDamageApplied", subject = "owner", damageOccurrence = "perDamage", optional = false,
                        effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "resume",
                            options = new[] { new { id = "continue" } } } }
                    } } }
                }
            });
            var presentation = JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实伤害来源", description = "追还标记后记录三次真实来源伤害" },
                    [Pause] = new { name = "反伤子窗口", description = "真实反伤后保留未返回的父程序",
                        optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            });
            var catalog = SkillProgramCatalog.Load(rules, presentation);
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "追还真实回归驱动") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new("fixture:zhuihuan-owner", "追还拥有者", "supporter", Skill, "shu", 9, [Driver]));
            builder.AddGeneral(new("fixture:zhuihuan-low", "弃牌来源", "supporter", "standard:none", "wei", 4));
            builder.AddGeneral(new("fixture:zhuihuan-high-1", "反伤来源一", "supporter", Pause, "wei", 9));
            builder.AddGeneral(new("fixture:zhuihuan-high-2", "反伤来源二", "supporter", Pause, "wei", 9));
            builder.AddDeck(new("fixture:zhuihuan-deck", "追还八张固定实体", 2, 0, [])
            {
                PhysicalCards = Enumerable.Range(1, 8)
                    .Select(rank => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, rank)).ToArray()
            });
            builder.AddMode(new("identity:classic-zhuihuan-retaliation", "追还恢复检查", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:zhuihuan-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:zhuihuan-owner", "fixture:zhuihuan-low", "fixture:zhuihuan-high-1", "fixture:zhuihuan-high-2"]));
        }
    }
}
