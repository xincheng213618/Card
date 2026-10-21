namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WushuangJieying = "wushuang:skill-3230", WushuangWangong = "wushuang:skill-3263";
    private const string WushuangCampClaimReason = "skill-program.wushuang.jieying.ended-hand";

    [ProgramCapability]
    private sealed class WushuangAuditCombatHost(GameEngine engine) : IWushuangAuditCombatHost
    {
        public SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffectOp op) =>
            op == SkillProgramEffectOp.ClaimWushuangEndedCampHand ? engine.ClaimWushuangCampHand(frame) : engine.CaptureWushuangWangong(frame);
    }

    private bool IsWushuangCampClaimContext(ProgramSkillFrame frame) => IsWushuangMode && frame.SkillId == WushuangJieying &&
        frame.WindowContext is { Window: SkillProgramTriggerWindow.AfterTurnEnded } context &&
        _resolutionStack.OfType<DeferredTurnEndFrame>().SingleOrDefault(f => f.Id == context.ParentFrameId) is { } parent &&
        parent.OwnerSeat != frame.OwnerSeat && MatchesAfterTurnEndedChild(parent, frame, context);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 63200)]
    private bool AllowsWushuangCampClaim(ProgramTriggerLegalityContext gate) =>
        gate.SkillId != WushuangJieying || gate.WindowContext?.Window != SkillProgramTriggerWindow.AfterTurnEnded ||
        IsWushuangMode && gate.WindowContext.SourceSeat is { } seat && seat != gate.OwnerSeat &&
            _players[seat].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Camp, gate.OwnerSeat)) > 0;

    private SkillProgramStepOutcome ClaimWushuangCampHand(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!IsWushuangCampClaimContext(frame) || frame.WushuangCampClaim is not null ||
            GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.ClaimWushuangEndedCampHand)
            throw new InvalidOperationException("Camp collection requires its exact native completed-turn child.");
        var seat = frame.WindowContext!.SourceSeat!.Value;
        if (_players[seat].MarkerSourceCounts.GetValueOrDefault((PlayerMarkerKind.Camp, frame.OwnerSeat)) <= 0)
            return SkillProgramStepOutcome.Continue;
        var cards = GetHand(_players[seat]).Select(c => c.Id).ToArray();
        var source = new CardConversionSource(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
        var receipt = new WushuangCampClaimReceipt(source, frame.GameplayHash, frame.InstructionIndex,
            frame.WindowContext.ParentFrameId, seat, _turnNumber, _movementSequence) { Cards = cards };
        ReplaceRuntimeTop(frame with { WushuangCampClaim = receipt });
        void Complete(long batch, IReadOnlyList<CardMovementRecord> moves)
        {
            var active = GetActiveProgramFrame(frame.Id);
            var paid = receipt with { SequenceAfter = _movementSequence, BatchId = batch };
            ReplaceRuntimeTop(active = active with { WushuangCampClaim = paid });
            SetStrategicMarker(seat, PlayerMarkerKind.Camp, frame.OwnerSeat, 0, active);
            SetStrategicMarker(frame.OwnerSeat, PlayerMarkerKind.Camp, frame.OwnerSeat, 1, active);
            AdvanceEventRulesAndQueueFact(new WushuangCampClaimedEvent(frame.Id, source, frame.GameplayHash,
                receipt.EndedFrameId, seat, receipt.ActualTurn, cards.Length, paid.SequenceBefore, paid.SequenceAfter, batch));
        }
        if (cards.Length == 0)
        {
            Complete(0, []);
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { WushuangCampClaim = null });
            return SkillProgramStepOutcome.Continue;
        }
        MoveProgramCardsFromMultipleSources(cards, CardLocation.Hand(frame.OwnerSeat), new(WushuangCampClaimReason), Complete);
        var outcome = AwaitProgramBoundCardMovements(frame.Id, frame.OwnerSeat);
        if (outcome == SkillProgramStepOutcome.Continue)
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with { WushuangCampClaim = null });
        return outcome;
    }

    private bool ValidWushuangCampClaim(ProgramSkillFrame frame)
    {
        if (frame.WushuangCampClaim is not { } receipt || receipt.Source != new CardConversionSource(frame.SkillId,
            GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId) || receipt.GameplayHash != frame.GameplayHash ||
            receipt.InstructionIndex != frame.InstructionIndex || receipt.EndedFrameId != frame.WindowContext?.ParentFrameId ||
            receipt.EndedSeat != frame.WindowContext?.SourceSeat || receipt.ActualTurn != _turnNumber ||
            receipt.Cards.Count == 0 || receipt.Cards.Distinct().Count() != receipt.Cards.Count ||
            receipt.BatchId <= 0 || receipt.SequenceAfter <= receipt.SequenceBefore || !IsWushuangCampClaimContext(frame)) return false;
        var moves = _cardMovements.Where(m => m.Sequence > receipt.SequenceBefore && m.Sequence <= receipt.SequenceAfter).ToArray();
        return moves.Length == receipt.Cards.Count && moves.Select(m => m.CardId).SequenceEqual(receipt.Cards) &&
            moves.All(m => m.From == CardLocation.Hand(receipt.EndedSeat) && m.To == CardLocation.Hand(frame.OwnerSeat) && m.Reason.Value == WushuangCampClaimReason) &&
            ProgramEventHistory<WushuangCampClaimedEvent>().Count(e => e.ProgramFrameId == frame.Id && e.Source == receipt.Source &&
                e.GameplayHash == receipt.GameplayHash && e.EndedFrameId == receipt.EndedFrameId && e.EndedSeat == receipt.EndedSeat &&
                e.ActualTurn == receipt.ActualTurn && e.Count == receipt.Cards.Count && e.BatchId == receipt.BatchId &&
                e.SequenceBefore == receipt.SequenceBefore && e.SequenceAfter == receipt.SequenceAfter) == 1;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 63200)]
    private void AssertWushuangCampClaim(ProgramSkillFrame frame, ProgramExecutionPlan plan)
    {
        if (frame.WushuangCampClaim is null) return;
        if (frame.InstructionIndex < 1 || frame.InstructionIndex > plan.Instructions.Count ||
            plan.Instructions[frame.InstructionIndex - 1].Op != SkillProgramEffectOp.ClaimWushuangEndedCampHand ||
            frame.WushuangCampClaim.Cards is not System.Collections.IList { IsReadOnly: true } || !ValidWushuangCampClaim(frame))
            throw new InvalidOperationException("Camp collection lost its exact completed turn, candidate or immutable hand movement.");
    }

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 63200)]
    private bool IsWushuangCampClaimMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.ClaimWushuangEndedCampHand && pending.SubjectSeat == frame.OwnerSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidWushuangCampClaim(frame);

    [ProgramContinuation(ProgramContinuationKind.Movement, 63200)]
    private bool ReturnWushuangCampClaim(ProgramSkillFrame frame)
    {
        if (frame.WushuangCampClaim is null) return false;
        if (!ValidWushuangCampClaim(frame)) throw new InvalidOperationException("Camp collection lost its paid movement return.");
        ReplaceRuntimeTop(frame with { WushuangCampClaim = null, PendingMovementContinuation = null });
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 63200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 63200)]
    private bool RetainWushuangCampClaim(ProgramSkillFrame frame) => frame.WushuangCampClaim is not null;

    private CurrentTurnCardUseKindsRecordedEvent? PreviousWushuangActualUse(int actor, CardUseFrame? current = null)
    {
        var facts = ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>();
        if (current is not null)
            facts = facts.TakeWhile(f => f.ActorSeat != actor || f.OriginFrameId != current.Id ||
                current.Action is not { } action || f.CardActionId != action.ActionId).ToArray();
        return facts.LastOrDefault(f => f.ActorSeat == actor);
    }

    private bool HasWushuangWangongPrivilege(CharacterState actor, long? existingUseId = null)
    {
        if (!IsWushuangMode) return false;
        var current = existingUseId is { } id ? LifecycleCardUse(id) : null;
        if (current?.WushuangWangong is { } receipt) return receipt.Source.OwnerSeat == actor.Seat && ValidWushuangWangongUse(current);
        return HasProgramSkill(actor, WushuangWangong) &&
            PreviousWushuangActualUse(actor.Seat, current)?.CardCategoryMask == 1 << (int)SkillProgramCardCategory.Basic;
    }

    private SkillProgramStepOutcome CaptureWushuangWangong(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (!IsWushuangMode || frame.SkillId != WushuangWangong || frame.WindowContext is not
            { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } context } || context.ActorSeat != frame.OwnerSeat ||
            LifecycleCardUse(context.ParentCardUseFrameId) is not { Action: { } action, WushuangWangong: null } use ||
            action.ActionId != context.CardActionId || action.ActorSeat != frame.OwnerSeat || !IsSlashCard(use.CardKind))
            throw new InvalidOperationException("Wangong requires its exact accepted Slash and skill-source identity.");
        var previous = PreviousWushuangActualUse(frame.OwnerSeat, use);
        if (previous?.CardCategoryMask != 1 << (int)SkillProgramCardCategory.Basic) return SkillProgramStepOutcome.Continue;
        var receipt = new WushuangWangongUseReceipt(new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId),
            frame.GameplayHash, use.Id, action.ActionId, previous.OriginFrameId, previous.CardActionId);
        UpdateLifecycleCardUse(use.Id, current => current with { WushuangWangong = receipt });
        AdvanceEventRulesAndQueueFact(new WushuangWangongUseCapturedEvent(receipt));
        return SkillProgramStepOutcome.Continue;
    }

    private bool ValidWushuangWangongUse(CardUseFrame use) => use.WushuangWangong is { } receipt &&
        receipt.Source.SkillId == WushuangWangong && receipt.Source.OwnerSeat == use.SourceSeat && receipt.UseFrameId == use.Id &&
        use.Action is { } action && action.ActionId == receipt.ActionId && action.ActorSeat == use.SourceSeat && IsSlashCard(use.CardKind) &&
        _contentRegistry.GetSkill(WushuangWangong).Program?.GameplayHash == receipt.GameplayHash &&
        ProgramEventHistory<WushuangWangongUseCapturedEvent>().Count(e => e.Receipt == receipt) == 1 &&
        ProgramEventHistory<CurrentTurnCardUseKindsRecordedEvent>().Any(e => e.ActorSeat == use.SourceSeat &&
            e.OriginFrameId == receipt.PreviousOriginFrameId && e.CardActionId == receipt.PreviousActionId &&
            e.CardCategoryMask == 1 << (int)SkillProgramCardCategory.Basic);

    [ProgramTargetRule(ProgramTargetRulePoint.SlashQuotaAfterLimit, 63201)]
    private bool WushuangWangongQuota(ProgramSlashQuotaQuery query) => IsSlashCard(query.Kind) && HasWushuangWangongPrivilege(query.Actor);

    [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash, 63201)]
    [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash, 63201)]
    [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash, 63201)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 63201)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 63201)]
    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash, 63201)]
    [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption, 63201)]
    private bool WushuangWangongDistance(ProgramSlashDistanceQuery query) =>
        IsSlashCard(query.Kind) && HasWushuangWangongPrivilege(query.Actor, query.ExistingUseFrameId);

    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 63201)]
    private int WushuangWangongDamage(IDamageAttempt attack) => IsWushuangMode && !attack.IsSourceLess && !attack.IsChainPropagation &&
        attack.EffectiveCardKind is { } kind && IsSlashCard(kind) && LifecycleCardUse(attack.ResolutionId) is { } use &&
        use.SourceSeat == attack.SourceSeat && ValidWushuangWangongUse(use) ? 1 : 0;
}
