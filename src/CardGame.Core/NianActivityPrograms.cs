namespace CardGame.Core;

internal interface INianActivityProgramHost
{
    SkillProgramStepOutcome ExecuteNianActivity(ProgramSkillFrame frame, SkillProgramEffect effect);
}

internal abstract class NianActivityOperationDescriptor : ProgramOperationDescriptorBase
{
    public override ISkillProgramEffectHandler Handler => new NianActivityEffectHandler(Op);
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, _) => { });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r) => ParseOwnerAlways(r);
}
internal sealed class ApplyNianMengtaiDiscardDescriptor : NianActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ApplyNianMengtaiDiscard;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DiscardPhaseStarting)];
}
internal sealed class DrawNianMengtaiEndingDescriptor : NianActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawNianMengtaiEnding;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnEnding)];
}
internal sealed class DiscardNianRuizhiRemainderDescriptor : NianActivityOperationDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DiscardNianRuizhiRemainder;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow), new ReadCardSet("ruizhi-hand"), new ReadCardSet("ruizhi-equipment")];
}
internal sealed class RecoverNianJingjueLossDescriptor : NianActivityOperationDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecoverNianJingjueLoss;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardsMoved)];
}
internal sealed class DrawNianRenxingDamageDescriptor : NianActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawNianRenxingDamage;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
}
internal sealed class DrawNianRenxingRecoveryDescriptor : NianActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawNianRenxingRecovery;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterHpRecovered)];
}
internal sealed class DrawNianBaonuReplacementDescriptor : NianActivityOperationDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawNianBaonuReplacement;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.DrawPhaseStarting)];
}
internal sealed class NianActivityEffectHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<INianActivityProgramHost>().ExecuteNianActivity(frame, effect);
}

/// <summary>Only an actually skipped phase earns Mengtai, never a scheduled future skip.</summary>
public sealed record NianActualPhaseSkippedEvent(int ActualTurnNumber, int OwnerSeat, SkillProgramTurnPhase Phase) : IGameEvent;
public sealed record NianJingjueLossRecoveredEvent(long MovementBatchId, int OwnerSeat) : IGameEvent;
public sealed record NianBaonuDrawRolledEvent(long ProgramFrameId, int OwnerSeat, int HpAtRoll, int DrawCount) : IGameEvent;
public sealed record NianOwnedTransferProof(int CardId, int OwnerSeat, CardLocation OriginalFrom, int RecipientSeat,
    long EntryBatchId, int EntrySequence, int ExitSequence, long ParentFrameId, CardConversionSource? Source,
    string? GameplayHash, int? InstructionIndex, bool NativeSnatch = false);
public sealed partial record CardMovementBatchContext
{
    private IReadOnlyList<NianOwnedTransferProof>? _nianOwnedTransfers;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<NianOwnedTransferProof>? NianOwnedTransfers
    { get => _nianOwnedTransfers; init => _nianOwnedTransfers = value is null ? null : Array.AsReadOnly(value.ToArray()); }
}

public sealed partial class GameEngine
{
    [ProgramTargetRule(ProgramTargetRulePoint.DirectedProhibition, 20)]
    [ProgramTargetRule(ProgramTargetRulePoint.DeclaredGlobalProhibition, 10)]
    private bool NianDongmianDirectedTargetProhibited(ProgramDirectedTargetQuery query) =>
        NianDongmianForbidsTarget(query.ActorSeat, query.TargetSeat);

    [ProgramCapability]
    private sealed class NianActivityProgramHost(GameEngine engine) : INianActivityProgramHost
    {
        public SkillProgramStepOutcome ExecuteNianActivity(ProgramSkillFrame f, SkillProgramEffect e) => engine.ExecuteNianActivity(f, e);
    }

    private bool NianEnabled(CharacterState player, string id) => player.IsAlive && _contentRegistry.Skills.ContainsKey(id) && HasRuntimeSkill(player, id);
    private bool NianOwnActualTurn(int seat) => _turnNumber > 0 && _turnProgression.TurnNumber == _turnNumber &&
        _turnProgression.OwnerSeat == seat && _phase is not (TurnPhase.NotStarted or TurnPhase.Finished);
    private bool NianPhaseWasActuallySkipped(int seat, SkillProgramTurnPhase phase) =>
        ProgramEventHistory<NianActualPhaseSkippedEvent>().Any(e => e.ActualTurnNumber == _turnNumber && e.OwnerSeat == seat && e.Phase == phase);

    private void RecordNianActualPhaseSkips(int owner, bool draw, bool play)
    {
        if (!NianEnabled(_players[owner], "nian:mengtai") || !NianOwnActualTurn(owner)) return;
        foreach (var phase in new[] { SkillProgramTurnPhase.Draw, SkillProgramTurnPhase.Play })
            if ((phase == SkillProgramTurnPhase.Draw ? draw : play) && !NianPhaseWasActuallySkipped(owner, phase))
                AdvanceEventRulesAndQueueFact(new NianActualPhaseSkippedEvent(_turnNumber, owner, phase));
    }

    private SkillProgramStepOutcome ExecuteNianActivity(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        var owner = _players[frame.OwnerSeat];
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{effect.Op}");
        switch (effect.Op)
        {
            case SkillProgramEffectOp.ApplyNianMengtaiDiscard:
                return NianPhaseWasActuallySkipped(owner.Seat, SkillProgramTurnPhase.Play)
                    ? SkipProgramTurnPhases(frame, [SkillProgramTurnPhase.Discard]) : SkillProgramStepOutcome.Continue;
            case SkillProgramEffectOp.DrawNianMengtaiEnding:
                if (NianPhaseWasActuallySkipped(owner.Seat, SkillProgramTurnPhase.Draw))
                    return BeginOperationProgramDraw(frame, owner.Seat, 3, reason);
                break;
            case SkillProgramEffectOp.DiscardNianRuizhiRemainder:
            {
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, TargetSeat: { } other } context ||
                    other == frame.OwnerSeat || other != _currentSeat || !_players[other].IsAlive ||
                    _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId) is not { OwnerSeat: var turnOwner } || turnOwner != other)
                    throw new InvalidOperationException("Ruizhi requires the exact other character's actual preparation window.");
                var hand = GetProgramCardSet(frame, "ruizhi-hand"); var equipment = GetProgramCardSet(frame, "ruizhi-equipment");
                if (hand.CardIds.Count > 1 || equipment.CardIds.Count > 1 ||
                    hand.CardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Hand(other)) ||
                    equipment.CardIds.Any(id => _cardZones.GetLocation(id) != CardLocation.Equipment(other)))
                    throw new InvalidOperationException("Ruizhi lost a privately selected kept entity in its original zone.");
                var keep = hand.CardIds.Concat(equipment.CardIds).ToHashSet();
                var discarded = GetHand(_players[other]).Concat(GetEquipment(_players[other]))
                    .Where(c => !keep.Contains(c.Id) && !IsSelfHandCategoryDiscardForbidden(other, c, FindOwnedCardLocation(_players[other], c), OwnedCardMoveIntent.Discard))
                    .Select(c => c.Id).ToArray();
                if (discarded.Length == 0) break;
                ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(other, 0, null) });
                MoveProgramLogicalDiscard(frame, other, discarded, new("program.nian.ruizhi.discard"));
                break;
            }
            case SkillProgramEffectOp.RecoverNianJingjueLoss:
            {
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardsMoved, MovementBatch: { } batch } context ||
                    _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId)?.Batch != batch)
                    throw new InvalidOperationException("Jingjue requires its actual atomic loss batch.");
                if (MatchingNianJingjueMovements(batch, owner.Seat).Length == 0 ||
                    ProgramEventHistory<NianJingjueLossRecoveredEvent>().Any(e => e.MovementBatchId == batch.Id && e.OwnerSeat == owner.Seat)) break;
                // The receipt precedes the real recovery and its child windows, so two
                // qualifying movement bindings can never repay a single loss batch.
                AdvanceEventRulesAndQueueFact(new NianJingjueLossRecoveredEvent(batch.Id, owner.Seat));
                new ProgramSkillHost(this).Recover(frame.Id, owner.Seat, owner.Seat, 1, null, null);
                break;
            }
            case SkillProgramEffectOp.DrawNianRenxingDamage:
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } damage ||
                    _resolutionStack.OfType<DamageTriggerWindowFrame>().LastOrDefault(w => w.Id == damage.ParentFrameId) is not { } damageParent ||
                    GetDamageTriggerAttack(damageParent).DamageAmount <= 0)
                    throw new InvalidOperationException("Renxing requires an actual positive applied-damage occurrence, including source-less damage.");
                if (!NianOwnActualTurn(owner.Seat)) return BeginOperationProgramDraw(frame, owner.Seat, 1, reason);
                break;
            case SkillProgramEffectOp.DrawNianRenxingRecovery:
                if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterHpRecovered, HpChange: { Kind: HpChangeKind.Recovery, Amount: > 0 } })
                    throw new InvalidOperationException("Renxing requires an actual positive recovered-HP occurrence.");
                if (!NianOwnActualTurn(owner.Seat)) return BeginOperationProgramDraw(frame, owner.Seat, 1, reason);
                break;
            case SkillProgramEffectOp.DrawNianBaonuReplacement:
                // The source describes endpoints 4 and current HP, but not their
                // reversed order below 4. This local completion uses both inclusively.
                var lower = Math.Min(4, owner.Hp); var upper = Math.Max(4, owner.Hp);
                var count = lower + _random.Next(checked(upper - lower + 1));
                AdvanceEventRulesAndQueueFact(new NianBaonuDrawRolledEvent(frame.Id, owner.Seat, owner.Hp, count));
                return BeginOperationProgramDraw(frame, owner.Seat, count, reason);
            default: throw new InvalidOperationException("Unsupported Nian activity operation.");
        }
        return SkillProgramStepOutcome.Continue;
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.DrawNianMengtaiEnding, SkillProgramEffectOp.DrawNianRenxingDamage,
        SkillProgramEffectOp.DrawNianRenxingRecovery, SkillProgramEffectOp.DrawNianBaonuReplacement)]
    private void ReturnNianOperationDraw(ProgramSkillFrame frame, NativeDrawOutcome outcome)
    {
        var plan = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!);
        var operation = plan.Instructions[frame.InstructionIndex - 1].Op;
        var expected = operation == SkillProgramEffectOp.DrawNianBaonuReplacement
            ? ProgramEventHistory<NianBaonuDrawRolledEvent>().Single(e => e.ProgramFrameId == frame.Id && e.OwnerSeat == frame.OwnerSeat).DrawCount
            : operation == SkillProgramEffectOp.DrawNianMengtaiEnding ? 3 : 1;
        var reason = new CardMoveReason($"skill-program.{frame.SkillId}.{operation}");
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || frame.NativeDraw is not null ||
            outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != expected ||
            outcome.SequenceAfter < outcome.SequenceBefore || outcome.SequenceAfter > OutsidePhaseMovementSequence ||
            outcome.ActualDrawIds.Count > expected || outcome.ActualDrawIds.Distinct().Count() != outcome.ActualDrawIds.Count ||
            outcome.Replacement is not null && outcome.ActualDrawIds.Count != 0 ||
            outcome.AttemptFrameId is { } attempt && ProgramEventHistory<NativeDrawAttemptDecidedEvent>().Count(e =>
                e.AttemptFrameId == attempt && e.ParentFrameId == frame.Id && e.RecipientSeat == frame.OwnerSeat &&
                e.RequestedCount == expected && e.Replaced == (outcome.Replacement is not null)) != 1)
            throw new InvalidOperationException("Nian's native draw lost its exact paid operation, requested count or typed replacement return.");
        CompleteProgramDrawCards(frame.Id, frame.OwnerSeat, null, SkillProgramCardSetVisibility.Private, reason, 0, outcome.ActualDrawIds);
        AdvanceRuntimeProgram(frame.Id);
    }

    private bool NianDongmianForbidsTarget(int actor, int target) => actor != target && _roundNumber == 1 &&
        _players[target].HasHpValue && _players[target].Hp <= 6 && NianEnabled(_players[target], "nian:dongmian");
    [ProgramDistanceRule(ProgramDistanceStage.CardUseLimitExemption, 10)]
    private bool NianShouyiUnlimitedCardDistance(CharacterState player) => NianEnabled(player, "nian:shouyi");
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 250)]
    private int NianBaonuDamageBonus(IDamageAttempt attack) => !attack.IsSourceLess && !attack.IsChainPropagation &&
        attack.CardUserSeat == attack.SourceSeat && attack.EffectiveCardKind is { } kind && IsSlashCard(kind) &&
        _players[attack.SourceSeat].Hp < 5 && NianEnabled(_players[attack.SourceSeat], "nian:baonu") ? 1 : 0;

    [ProgramMovementProof(ProgramMovementProofPhase.BeforePrivatePayments, 20)]
    private bool IsNianRuizhiMovement(ProgramSkillFrame frame, SkillProgramEffect? paidEffect, ProgramMovementContinuation pending) =>
        paidEffect?.Op == SkillProgramEffectOp.DiscardNianRuizhiRemainder && frame.SkillId == "nian:ruizhi" &&
        frame.WindowContext is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, TargetSeat: { } other } context &&
        other != frame.OwnerSeat && other == _currentSeat && pending.SubjectSeat == other && pending.BeforeCount == 0 &&
        pending.CoverageResultBind is null && frame.CardSetBindings.Count(b => b.Name is "ruizhi-hand" or "ruizhi-equipment") == 2 &&
        _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Id == context.ParentFrameId && w.OwnerSeat == other &&
            w.Window == context.Window && w.CandidateIndex < w.Candidates.Count && MountObserverCandidateMatches(frame, w.Candidates[w.CandidateIndex]));

    private CardMovementBatchContext CaptureNianOwnedTransferBatch(CardMovementBatchContext batch, ProgramSkillFrame? origin)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RecoverNianJingjueLoss) ||
            !_players.Any(p => NianEnabled(p, "nian:jingjue"))) return batch;
        var proofs = new List<NianOwnedTransferProof>();
        // Native Snatch has its own card-use parent and TargetCardTaken invoice;
        // the hidden selected hand identity need not become a public target ID.
        if (batch.ParentFrameId is { } useId && LifecycleCardUse(useId) is { CardKind: CardKind.Snatch } use &&
            ProgramEventHistory<CardUseDeclaredEvent>().Any(e => e.ResolutionId == useId && e.SourceSeat == use.SourceSeat && e.CardKind == CardKind.Snatch))
            foreach (var exit in batch.Movements.Where(m => m.From == CardLocation.Processing && m.To == CardLocation.Hand(use.SourceSeat)))
            {
                var entry = _cardMovements.LastOrDefault(m => m.CardId == exit.CardId && m.Sequence < exit.Sequence);
                if (entry is not { From.Zone: CardZoneKind.Hand or CardZoneKind.Equipment, From.OwnerSeat: { } originalOwner } || originalOwner == use.SourceSeat ||
                    !use.TargetSeats.Contains(originalOwner) || entry.To != CardLocation.Processing || entry.Sequence >= exit.Sequence ||
                    entry.Reason != CardMoveReasons.Snatch || exit.Reason != CardMoveReasons.SnatchFinished ||
                    !ProgramEventHistory<TargetCardTakenEvent>().Any(e => e.ResolutionId == useId && e.SourceSeat == use.SourceSeat && e.TargetSeat == originalOwner &&
                        e.FromZone == entry.From.Zone && (e.PublicCardId is null || e.PublicCardId == exit.CardId))) continue;
                var entryBatch = _pendingCardsMovedBatches.SingleOrDefault(b => b.Id != batch.Id && b.Movements.Contains(entry) &&
                    b.ParentFrameId == batch.ParentFrameId && b.ParentBatchId == batch.ParentBatchId && b.TurnNumber == batch.TurnNumber);
                if (entryBatch is not null) proofs.Add(new(exit.CardId, originalOwner, entry.From, use.SourceSeat, entryBatch.Id, entry.Sequence,
                    exit.Sequence, useId, null, null, null, NativeSnatch: true));
            }
        if (origin is null || batch.ParentFrameId != origin.Id || batch.OriginOwnerSeat != origin.OwnerSeat ||
            batch.OriginSkillId != origin.SkillId || batch.OriginSkillInstanceId != origin.SkillInstanceId || origin.InstructionIndex < 1 ||
            _contentRegistry.GetSkill(origin.SkillId).Program?.GameplayHash != origin.GameplayHash)
            return proofs.Count == 0 ? batch : batch with { NianOwnedTransfers = proofs };
        var effect = ProgramInstructionResolver.Default.Resolve(origin, _contentRegistry.GetSkill(origin.SkillId).Program!).GetPausedInstruction(origin.InstructionIndex).Effect;
        if (effect is not { Op: SkillProgramEffectOp.SelectAndMoveOwnedCard, Destination: SkillProgramCardDestination.SelectedTargetHand,
                CardOwnerRef: { } ownerRef, TargetReference: { } recipientRef }) return batch;
        var owner = ResolveProgramParticipant(origin, ownerRef); var recipient = ResolveProgramParticipant(origin, recipientRef);
        if (owner == recipient) return batch;
        foreach (var exit in batch.Movements.Where(m => m.From == CardLocation.Processing && m.To == CardLocation.Hand(recipient)))
        {
            var entry = _cardMovements.LastOrDefault(m => m.CardId == exit.CardId && m.Sequence < exit.Sequence);
            if (entry is not { From.Zone: CardZoneKind.Hand or CardZoneKind.Equipment } || entry.From.OwnerSeat != owner ||
                entry.To != CardLocation.Processing || entry.Sequence >= exit.Sequence || entry.TurnNumber != batch.TurnNumber ||
                entry.Reason != exit.Reason || !effect.Zones.Contains(entry.From.Zone)) continue;
            var entryBatch = _pendingCardsMovedBatches.SingleOrDefault(b => b.Id != batch.Id && b.Movements.Contains(entry) &&
                b.ParentFrameId == batch.ParentFrameId && b.ParentBatchId == batch.ParentBatchId && b.TurnNumber == batch.TurnNumber &&
                b.OriginSkillId == origin.SkillId && b.OriginSkillInstanceId == origin.SkillInstanceId && b.OriginOwnerSeat == origin.OwnerSeat);
            if (entryBatch is null) continue;
            proofs.Add(new(exit.CardId, owner, entry.From, recipient, entryBatch.Id, entry.Sequence, exit.Sequence, origin.Id,
                new(origin.SkillId, GetProgramBindingId(origin), origin.OwnerSeat, origin.SkillInstanceId), origin.GameplayHash, origin.InstructionIndex - 1));
        }
        return proofs.Count == 0 ? batch : batch with { NianOwnedTransfers = proofs };
    }

    private static bool IsNianJingjueTrigger(SkillProgramTrigger trigger) => trigger.Window == SkillProgramTriggerWindow.CardsMoved &&
        trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RecoverNianJingjueLoss);
    private int[] MatchingNianJingjueMovements(CardMovementBatchContext batch, int owner) => batch.Movements.Select((m, i) => (Move: m, Index: i))
        .Where(item =>
            GetProgramDiscardSource(item.Move) is { Zone: CardZoneKind.Hand or CardZoneKind.Equipment, OwnerSeat: var discarder } && discarder == owner ||
            item.Move.From is { Zone: CardZoneKind.Hand or CardZoneKind.Equipment, OwnerSeat: var source } && source == owner &&
                item.Move.To is { Zone: CardZoneKind.Hand, OwnerSeat: { } recipient } && recipient != owner ||
            batch.NianOwnedTransfers?.Any(p => p.OwnerSeat == owner && p.CardId == item.Move.CardId && p.ExitSequence == item.Move.Sequence &&
                p.ParentFrameId == batch.ParentFrameId && p.OriginalFrom.OwnerSeat == owner && p.RecipientSeat != owner &&
                item.Move.From == CardLocation.Processing && item.Move.To == CardLocation.Hand(p.RecipientSeat) && p.EntrySequence < p.ExitSequence &&
                (p.NativeSnatch
                    ? p.Source is null && p.GameplayHash is null && p.InstructionIndex is null && item.Move.Reason == CardMoveReasons.SnatchFinished &&
                        ProgramEventHistory<TargetCardTakenEvent>().Any(e => e.ResolutionId == p.ParentFrameId && e.SourceSeat == p.RecipientSeat && e.TargetSeat == owner && e.FromZone == p.OriginalFrom.Zone)
                    : p.Source is { } producer && p.GameplayHash == _contentRegistry.GetSkill(producer.SkillId).Program?.GameplayHash && p.InstructionIndex >= 0 &&
                        batch.OriginSkillId == producer.SkillId && batch.OriginSkillInstanceId == producer.SkillInstanceId && batch.OriginOwnerSeat == producer.OwnerSeat) &&
                _cardMovements.Any(m => m.CardId == p.CardId && m.Sequence == p.EntrySequence && m.From == p.OriginalFrom && m.To == CardLocation.Processing &&
                    m.TurnNumber == batch.TurnNumber && (p.NativeSnatch ? m.Reason == CardMoveReasons.Snatch : m.Reason == item.Move.Reason))) == true)
        .Select(item => item.Index).ToArray();
    private IEnumerable<ProgramTriggerCandidate> CollectNianJingjueCandidates(CardMovementBatchContext batch) => _players.Where(p => p.IsAlive)
        .SelectMany(p => CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.CardsMoved))
        .Where(c => IsNianJingjueTrigger(GetProgramTrigger(c)) && MatchingNianJingjueMovements(batch, c.OwnerSeat).Length != 0);
    private ProgramSkillWindowContext CreateNianJingjueContext(CardsMovedTriggerWindowFrame parent, ProgramTriggerCandidate candidate) =>
        new(SkillProgramTriggerWindow.CardsMoved, parent.Id, candidate.OwnerSeat, SourceSeat: candidate.OwnerSeat, TargetSeat: candidate.OwnerSeat,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with { MovedCardCount = MatchingNianJingjueMovements(parent.Batch, candidate.OwnerSeat).Length },
            MovementBatch: parent.Batch);
    private bool CanRunNianJingjueCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        context.MovementBatch is { } batch && context.OwnerSeat == candidate.OwnerSeat && context.SourceSeat == candidate.OwnerSeat && context.TargetSeat == candidate.OwnerSeat &&
        _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault() is { } parent && parent.Id == batch.Id && parent.Batch == batch &&
        parent.Candidates.ElementAtOrDefault(parent.CandidateIndex) == candidate && MatchingNianJingjueMovements(batch, candidate.OwnerSeat).Length != 0;

    private static bool IsNianRecoveryTrigger(SkillProgramTrigger trigger) => trigger.Window == SkillProgramTriggerWindow.AfterHpRecovered &&
        trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.DrawNianRenxingRecovery);
    private IEnumerable<ProgramTriggerCandidate> CollectNianRecoveryCandidates(HpChangeContext change) =>
        change.Kind != HpChangeKind.Recovery || change.Amount <= 0 ? [] : _players.Where(p => p.IsAlive)
            .SelectMany(p => CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.AfterHpRecovered))
            .Where(c => IsNianRecoveryTrigger(GetProgramTrigger(c)));
    private ProgramSkillWindowContext CreateNianRecoveryContext(HpChangeContext change, ProgramTriggerCandidate candidate) =>
        new(SkillProgramTriggerWindow.AfterHpRecovered, change.Id, candidate.OwnerSeat, SourceSeat: change.SourceSeat,
            TargetSeat: change.TargetSeat, Amount: change.Amount, OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]) with { HpChangeAmount = change.Amount, HpBeforeChange = change.HpBefore, HpAfterChange = change.HpAfter }, HpChange: change);
    private bool IsNianRecoveryCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context) =>
        IsNianRecoveryTrigger(GetProgramTrigger(candidate)) && context.HpChange is { Kind: HpChangeKind.Recovery, Amount: > 0 } hp &&
        context.OwnerSeat == candidate.OwnerSeat && context.ParentFrameId == hp.Id && context.TargetSeat == hp.TargetSeat &&
        _resolutionStack.OfType<HpChangedTriggerWindowFrame>().LastOrDefault() is { } parent && parent.Id == hp.Id && parent.Change == hp &&
        parent.Candidates.ElementAtOrDefault(parent.CandidateIndex) == candidate;

    [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption, 60)]
    private bool DistanceCardUseExemption60NianShouyiUnlimitedCardDistance(ProgramCardDistanceQuery q) =>
        NianShouyiUnlimitedCardDistance(q.Actor);

}
