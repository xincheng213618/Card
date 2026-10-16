using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OwnedHandRankSumChecks
{
    private const string Ziyuan = "ol:ziyuan";
    private const string Activation = "gift-exact-thirteen-and-recover";
    private const string AiSkill = "fixture:owned-rank-sum-ai";
    private const string Gain = "fixture:owned-rank-sum-gain";
    private const string Hp = "fixture:owned-rank-sum-hp";
    private const string Owner = "fixture:owned-rank-sum-owner";
    private const string Mode = "identity:classic-owned-rank-sum-fixture";
    private const string GiftReason = "skill-program.ol:ziyuan.MoveBoundCards";
    private const string AiGiftReason = "skill-program.fixture:owned-rank-sum-ai.MoveBoundCards";

    public static void ExactOwnedHandRankSumGiftsAllThirteenAndReturnsThroughChildrenOnce()
    {
        var (game, registry) = Start(rank: 1);
        var originalHand = game.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        var targetHand = game.CreateSnapshot(1).Players[1].Hand.Select(c => c.Id).ToArray();
        Require(originalHand.Length == 13 && game.CreateSnapshot(0).Players[0].Hand.All(c => c.Rank == 1),
            "The small fixed physical deck genuinely deals a unique thirteen-card solution, larger than the legacy eight-card subset bound.");
        Require(game.GetHumanLegalActions().Single(a => a.ProgramSkillId == Ziyuan) is
                { MinCardCount: 0, MaxCardCount: 0, MinTargetCount: 1, MaxTargetCount: 1 },
            "The exact-sum activation asks for a real other recipient before private hand selection.");
        Reject(game, new UseProgramSkillCommand(0, Ziyuan, Activation, [originalHand[0]], [1], game.Revision, P(game)!.PromptId));
        Reject(game, new UseProgramSkillCommand(0, Ziyuan, Activation, [], [0], game.Revision, P(game)!.PromptId));
        Accept(game, new UseProgramSkillCommand(0, Ziyuan, Activation, [], [1], game.Revision, P(game)!.PromptId));
        Reach(game, p => IsRankSum(p, 0));
        var parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Ziyuan);
        var parentId = parent.Id;
        var instance = parent.SkillInstanceId;
        var hash = parent.GameplayHash;
        var retained = game.CreateSnapshot(0);
        var retainedJson = SnapshotJson.Serialize(retained);
        AssertPrivateSelection(game, 0, originalHand);
        AssertFrozenSelection(retained.PendingDecision!);
        game = Cold(game, registry);

        for (var index = 0; index < originalHand.Length; index++)
        {
            parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
            var draft = parent.OwnedHandRankSumSelection!;
            Require(parent.InstructionIndex == 1 && parent.CardSetBindings.Count == 0 && parent.BoundParticipantGift is null &&
                    parent.SkillInstanceId == instance && parent.GameplayHash == hash &&
                    draft.InstructionIndex == 1 && draft.BindingId == Activation && draft.RequiredRankSum == 13 &&
                    draft.CandidateCardIds.Order().SequenceEqual(originalHand.Order()) && draft.CandidateRanks.All(r => r == 1) &&
                    draft.SelectedCardIds.Count == index && P(game)!.Choices.Count == 13 - index &&
                    P(game)!.Choices.All(c => c.Cards.Count == 1) &&
                    !game.CardMovements.Any(m => m.Reason.Value == GiftReason),
                "Each positive-rank pick remains on the same unpaid cursor, keeps every actual hand candidate, and publishes no premature finish.");
            AssertPrivateSelection(game, 0, originalHand);
            RejectWrongActor(game);
            Reject(game, new AnswerPromptCommand(0, P(game)!.PromptId,
                new ChoiceId($"owned-hand-rank-sum.frame-{parentId}.finish-{index}"), game.Revision));
            var oldPrompt = P(game)!;
            var choice = oldPrompt.Choices.First(c => c.Cards.SequenceEqual([originalHand[index]]));
            Accept(game, new AnswerPromptCommand(0, oldPrompt.PromptId, choice.Id, game.Revision));
            Require(SnapshotJson.Serialize(retained) == retainedJson,
                "Advancing a real private draft cannot mutate the already prepared immutable player snapshot.");
            Reject(game, new AnswerPromptCommand(0, oldPrompt.PromptId, choice.Id, game.Revision));
            if (index == 7) game = Cold(game, registry);
        }
        parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
        Require(parent.OwnedHandRankSumSelection!.SelectedCardIds.Count == 13 && parent.InstructionIndex == 1 &&
                P(game)!.Choices is { Count: 1 } exactFinish && exactFinish[0].Cards.Count == 0 &&
                !game.CardMovements.Any(m => m.Reason.Value == GiftReason),
            "Completion is explicitly published only after the physical rank sum reaches exactly thirteen; selection itself is not a payment.");
        game = Cold(game, registry);
        AnswerHuman(game, c => c.Cards.Count == 0);
        Reach(game, p => IsOption(p, Gain, "gift-return"));
        parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.ResumeProgramFrameId == parentId);
        var gainChild = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Gain);
        var gift = game.CardMovements.Where(m => m.Reason.Value == GiftReason).ToArray();
        Require(parent.InstructionIndex == 2 && parent.OwnedHandRankSumSelection is null &&
                parent.BoundParticipantGift is { InstructionIndex: 2, SourceBind: "ziyuan-gift", RecipientSeat: 1 } &&
                parent.CardSetBindings.Single(b => b.Name == "ziyuan-gift") is { Visibility: SkillProgramCardSetVisibility.Private, SelectionActorSeat: 0 } binding &&
                binding.CardIds.SequenceEqual(originalHand) && binding.SourceLocations.All(l => l == CardLocation.Hand(0)) &&
                gift.Length == 13 && gift.Select(m => m.CardId).Order().SequenceEqual(originalHand.Order()) &&
                gift.All(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) &&
                movement.Batch.ParentFrameId == parentId && movement.Batch.AwaitingProgramFrameId is null &&
                movement.Batch.OriginOwnerSeat == 0 && movement.Batch.OriginSkillId == Ziyuan && movement.Batch.OriginSkillInstanceId == instance &&
                movement.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(originalHand.Order()) &&
                gainChild.WindowContext?.ParentFrameId == movement.Id && gainChild.OwnerSeat == 1 &&
                game.CreateSnapshot(1).Players[1].Hand.Select(c => c.Id).Order().SequenceEqual(targetHand.Concat(originalHand).Order()) &&
                game.CreateSnapshot(0).Players[0].HandCount == 0 && game.CreateSnapshot(0).Players[1].Hp == 2 &&
                !Facts<RecoveryAppliedEvent>(game).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1),
            "One exact thirteen-entity atomic gift pauses on its typed original parent before the recipient's real recovery; the frozen binding retains its physical origin. " +
            JsonSerializer.Serialize(new
            {
                parent.InstructionIndex, parent.BoundParticipantGift, parent.CardSetBindings,
                movement.ResumeProgramFrameId, movement.Batch.ParentFrameId, movement.Batch.AwaitingProgramFrameId,
                movement.Batch.OriginOwnerSeat, movement.Batch.OriginSkillId, movement.Batch.OriginSkillInstanceId,
                BatchIds = movement.Batch.Movements.Select(m => m.CardId).ToArray(), GiftCount = gift.Length,
                gainChild.OwnerSeat, GainParent = gainChild.WindowContext?.ParentFrameId,
                ActualTargetIds = game.CreateSnapshot(1).Players[1].Hand.Select(c => c.Id).ToArray(), originalHand, targetHand,
                OwnerHandCount = game.CreateSnapshot(0).Players[0].HandCount,
                TargetHp = game.CreateSnapshot(0).Players[1].Hp, Recovery = Facts<RecoveryAppliedEvent>(game).ToArray()
            }));
        AssertPrivateChild(game, 1);
        game = Cold(game, registry);
        Reach(game, p => IsOption(p, Hp, "hp-return"));
        parent = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
        var hpWindow = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Require(parent.InstructionIndex == 3 && parent.BoundParticipantGift is null &&
                hpWindow.Change is { ParentFrameId: var recoveredParent, SourceSeat: 0, TargetSeat: 1,
                    Kind: HpChangeKind.Recovery, Amount: 1, HpBefore: 2, HpAfter: 3 } && recoveredParent == parentId &&
                hpWindow.ResumeFrameId == parentId && hpWindow.Continuation == PostEventContinuation.Program &&
                Facts<RecoveryAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1 && e.RemainingHp == 3) == 1 &&
                game.CardMovements.Count(m => m.Reason.Value == GiftReason) == 13,
            "After the single gift child returns, the actual recover instruction pauses on its exact HP child and does not repay or advance the parent twice.");
        AssertPrivateChild(game, 1);
        game = Cold(game, registry);
        Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Require(!game.ResolutionStack.Any(f => f.Id == parentId) &&
                Facts<ProgramSkillStartedEvent>(game).Count(e => e.FrameId == parentId && e.OwnerSeat == 0 && e.SkillId == Ziyuan && e.ActivationId == Activation) == 1 &&
                Facts<ProgramSkillResolvedEvent>(game).Count(e => e.FrameId == parentId && e.OwnerSeat == 0 && e.SkillId == Ziyuan && e.ActivationId == Activation && e.Completed) == 1 &&
                Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Gain && e.Activated && e.Completed) == 1 &&
                Facts<ProgramBindingResolvedEvent>(game).Count(e => e.SkillId == Hp && e.Activated && e.Completed) == 1 &&
                game.CardMovements.Count(m => m.Reason.Value == GiftReason) == 13 &&
                !game.CardMovements.Any(m => m.CardId <= 0) &&
                !game.GetHumanLegalActions().Any(a => a.ProgramSkillId == Ziyuan),
            "All real children complete once; the actual per-Play ability is spent without a second gift, recovery or synthetic card.");
        Reject(game, new UseProgramSkillCommand(0, Ziyuan, Activation, [], [1], game.Revision, P(game)!.PromptId));
        _ = Cold(game, registry);
    }

    private static bool IsRankSum(PendingDecision p, int actor) => p.PlayerSeat == actor &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "owned-hand-rank-sum");
    private static bool IsOption(PendingDecision p, string skill, string bind) => p.SkillPrompt?.SkillId == skill &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == bind);
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static void AnswerHuman(GameEngine game, Func<PromptChoice, bool> select)
    {
        var prompt = P(game)!;
        Require(prompt.PlayerSeat == 0, "Only the real human participant answers a player command.");
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(select).Id, game.Revision));
    }
    private static void StepNative(GameEngine game)
    {
        var prompt = P(game);
        if (prompt is { PlayerSeat: 0 } && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"))
            AnswerHuman(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        else if (prompt is { PlayerSeat: 0 })
            throw new InvalidOperationException("Unexpected human boundary: " + JsonSerializer.Serialize(prompt));
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    {
        for (var step = 0; step < 160; step++)
        {
            if (P(game) is { } p && condition(p)) return;
            StepNative(game);
        }
        throw new InvalidOperationException("The bounded rank-sum fixture did not reach its native boundary: " + JsonSerializer.Serialize(P(game)));
    }
    private static void AssertPrivateSelection(GameEngine game, int actor, IReadOnlyList<int> ownIds)
    {
        Require(P(game) is { IsPrivate: true } prompt && prompt.PlayerSeat == actor &&
                prompt.Choices.SelectMany(c => c.Cards).All(ownIds.Contains) &&
                Enumerable.Range(0, 4).Where(s => s != actor).All(s =>
                    game.CreateSnapshot(s).PendingDecision is null && game.CreateSnapshot(s).Players[actor].Hand.Count == 0),
            "Only the true selecting owner sees physical hand candidates or the private exact-sum choices.");
    }
    private static void AssertPrivateChild(GameEngine game, int actor) => Require(P(game) is { IsPrivate: true } p && p.PlayerSeat == actor &&
        Enumerable.Range(0, 4).Where(s => s != actor).All(s => game.CreateSnapshot(s).PendingDecision is null),
        "The real gift/recovery child prompt remains private to its actual participant.");
    private static void AssertFrozenSelection(PendingDecision p)
    {
        var immutableCards = false;
        var immutableParameters = false;
        try { ((IList<int>)p.Choices[0].Cards)[0] = -1; } catch (NotSupportedException) { immutableCards = true; }
        try { ((IDictionary<string, string>)p.Choices[0].Parameters)["frame-id"] = "-1"; } catch (NotSupportedException) { immutableParameters = true; }
        Require(immutableCards && immutableParameters, "Prepared private nested choice cards and parameters are frozen against observer mutation.");
    }
    private static void RejectWrongActor(GameEngine game) => Reject(game,
        new AnswerPromptCommand(1, P(game)!.PromptId, P(game)!.Choices.First().Id, game.Revision));
    private static void Reject(GameEngine game, GameCommand command)
    {
        var before = State(game);
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(!result.Accepted && result.Error is not null && State(game) == before,
            "Invalid actor, sum, prompt or target input must reject without changing player views, entity costs, quota or accepted history.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The real serialized rank-sum command was rejected.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), game.CardMovements,
        Events = game.Events.Select(e => $"{e.Sequence}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray(),
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(cold) == State(game), "The complete private rank-sum draft, paid gift/HP children, typed original cursor and entity journal cold-restore identically.");
        return cold;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private static string FixtureRules() => $$$"""
        {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
          {"id":"{{{AiSkill}}}","revision":1,"cardPolicies":[{"id":"preserve-foreign-equipment","kind":"preventForeignEquipmentDiscard","cardKinds":[]}],"activations":[{"id":"give-and-draw","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[
            {"op":"selectOwnedHandRankSum","target":"owner","exactRankSum":13,"resultBind":"rank-gift"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"rank-gift","destination":"selectedTargetHand","awaitMovementTriggers":true},
            {"op":"recover","target":"selectedTarget","amount":1},{"op":"draw","target":"owner","amount":13}]}]},
          {"id":"{{{Gain}}}","revision":1,"triggers":[{"id":"actual-gift","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{{GiftReason}}}","{{{AiGiftReason}}}"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gift-return","options":[{"id":"continue"}]}]}]},
          {"id":"{{{Hp}}}","revision":1,"triggers":[{"id":"actual-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-return","options":[{"id":"continue"}]}]}]}]}
        """;
    private static string FixturePresentation() => JsonSerializer.Serialize(new
    {
        schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
        skills = new[] { AiSkill, Gain, Hp }.ToDictionary(id => id, id => new
        { name = id, description = "真实精确点数赠牌与返回", optionLabels = id == AiSkill ? new Dictionary<string, string>() : new Dictionary<string, string> { ["continue"] = "继续" } })
    });
    private static (GameEngine, ContentRegistry) Start(int rank, bool mixedRanks = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(rank, mixedRanks));
        Require(registry.Skills.Values.Any(s => s.Program?.CardPolicies.Any(p => p.Kind == SkillProgramCardPolicyKind.PreventForeignEquipmentDiscard) == true),
            "This real complete catalog opts into selection-actor metadata through its registered foreign-equipment protection policy.");
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        return (game, registry);
    }
    private sealed class Fixture(int rank, bool mixedRanks) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("owned-hand-rank-sum-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(FixtureRules(), FixturePresentation());
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "真实点数选择与一次赠牌")
            {
                Program = program, ProgramPresentation = catalog.Presentations[id],
                SelectionWeights = id == AiSkill ? Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -10000d : 10000d) : null
            });
            builder.AddGeneral(new(Owner, "实际资援选牌人", "supporter", Ziyuan, "shu", 4, [Gain, Hp]) { InitialHp = 3 });
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:owned-rank-sum-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "固定原生AI选牌人", "supporter", AiSkill, "wei", 4, [Gain, Hp]) { InitialHp = 2 });
            builder.AddDeck(new("fixture:owned-rank-sum-deck", "固定真实完整手牌", 13, 0, [])
            { PhysicalCards = Enumerable.Range(0, 104).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart,
                mixedRanks ? new[] { 6, 7, 4 }[i % 3] : rank)).ToArray() });
            builder.AddMode(new(Mode, "共享手牌精确点数和", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:owned-rank-sum-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
