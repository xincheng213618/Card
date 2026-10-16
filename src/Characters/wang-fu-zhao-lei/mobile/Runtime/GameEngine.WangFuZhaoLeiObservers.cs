namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IDamageAttempt? XunyiOriginalAttack(XunyiDamageReceipt receipt) => _resolutionStack.SingleOrDefault(f => f.Id == receipt.OriginalAttackId) switch
    {
        ProgramSkillFrame { AttackAttempt: not null } => new ProgramAttackHandle(this, receipt.OriginalAttackId),
        CardUseFrame { CardAttack.Active: true } or ProgramSkillFrame { CardAttack.Active: true } or JudgmentFrame { CardAttack.Active: true } => new CardAttackHandle(this, receipt.OriginalAttackId),
        _ => null
    };
    private bool ValidXunyi(ProgramSkillFrame frame)
    {
        if (frame.Xunyi is not { } pending) return false;
        var receipt = pending.Receipt;
        var relation = receipt.Relation;
        if (relation.Source.OwnerSeat != frame.OwnerSeat || relation.Source.SkillId != frame.SkillId || relation.Source.SkillInstanceId != frame.SkillInstanceId ||
            relation.GameplayHash != frame.GameplayHash || frame.InstructionIndex != pending.InstructionIndex || pending.InstructionIndex != 1 ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.AfterDamageApplied } context || context.DamageFrameId != receipt.DamageFrameId ||
            context.Amount != receipt.ActualAmount || context.TargetSeat != receipt.ActualTargetSeat || receipt.ActualAmount <= 0 ||
            relation.HolderSeat == frame.OwnerSeat || !IsValidPlayerSeat(relation.HolderSeat) || XunyiBranches(receipt) is < 1 or > 2 ||
            pending.StepIndex < 0 || pending.StepIndex > XunyiStepCount(receipt) || !Enum.IsDefined(pending.Stage) ||
            GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.SettleXunyiDamagePoint ||
            CompleteProgramEventHistory().OfType<XunyiRelationChangedEvent>().Count(e => e.Relation == relation) != 1 ||
            CompleteProgramEventHistory().OfType<XunyiDamageQualifiedEvent>().Count(e => e.Receipt == receipt) != 1 ||
            CompleteProgramEventHistory().OfType<XunyiDamageIssuedEvent>().Count(e => e.FrameId == frame.Id && e.Receipt == receipt && e.InstructionIndex == pending.InstructionIndex) != 1)
            return false;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index < 2 || _resolutionStack[index - 1] is not DamageTriggerWindowFrame window || window.Id != context.ParentFrameId ||
            window.ParentFrameId != receipt.DamageFrameId || window.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            !MountObserverCandidateMatches(frame, window.Candidates[window.CandidateIndex].ToProgramCandidate()) ||
            _resolutionStack[index - 2] is not DamageFrame damage || damage.Id != receipt.DamageFrameId || damage.ParentFrameId != receipt.OriginalAttackId ||
            damage.TargetSeat != receipt.ActualTargetSeat || damage.Amount != receipt.ActualAmount) return false;
        var attack = XunyiOriginalAttack(receipt);
        if (attack is null || !attack.DamageWasApplied || attack.TargetSeat != receipt.ActualTargetSeat || attack.DamageAmount != receipt.ActualAmount ||
            (attack.IsSourceLess || attack.IsDelayedJudgmentDamage ? (int?)null : attack.SourceSeat) != receipt.ActualSourceSeat) return false;
        var source = receipt.ActualSourceSeat;
        var owner = frame.OwnerSeat;
        var other = relation.HolderSeat;
        int? discard = receipt.ActualTargetSeat == owner && source != other ? other : receipt.ActualTargetSeat == other && source != owner ? owner : null;
        int? draw = source == owner && receipt.ActualTargetSeat != other ? other : source == other && receipt.ActualTargetSeat != owner ? owner : null;
        if (discard != receipt.DiscardRecipientSeat || draw != receipt.DrawRecipientSeat) return false;
        var payments = CompleteProgramEventHistory().OfType<XunyiPointPaidEvent>().Where(e => e.FrameId == frame.Id).ToArray();
        var paidCount = pending.StepIndex + (pending.Stage == XunyiStage.Children ? 1 : 0);
        if (payments.Length != paidCount || payments.Select(e => e.StepIndex).Order().Where((step, n) => step != n).Any() ||
            payments.Any(e => e.DamageFrameId != receipt.DamageFrameId || e.StepIndex >= XunyiStepCount(receipt))) return false;
        if (pending.Stage == XunyiStage.DiscardChoice) return frame.PendingMovementContinuation is null && pending.Before == 0 && pending.After == 0 && pending.Count == 0 && pending.CardId is null && pending.From is null;
        if (pending.StepIndex >= XunyiStepCount(receipt) || pending.Before < 0 || pending.After < pending.Before || pending.After > _movementSequence || pending.Count is < 0 or > 1) return false;
        var stepInfo = XunyiStep(pending);
        if (frame.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } movement || movement.SubjectSeat != stepInfo.Seat ||
            payments.Single(e => e.StepIndex == pending.StepIndex) != new XunyiPointPaidEvent(frame.Id, receipt.DamageFrameId, pending.StepIndex,
                stepInfo.Seat, stepInfo.Draw, pending.Before, pending.After, pending.Count, pending.CardId, pending.From)) return false;
        if (stepInfo.Draw)
            return pending.CardId is null && pending.From is null && _cardMovements.Count(m => m.Sequence > pending.Before && m.Sequence <= pending.After &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(stepInfo.Seat) && m.Reason.Value == "skill-program.xunyi.draw") == pending.Count;
        return pending.Count == 0 ? pending.CardId is null && pending.From is null : pending.CardId is { } id && pending.From is { OwnerSeat: { } cardOwner } from && cardOwner == stepInfo.Seat &&
            from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && _cardMovements.Count(m => m.Sequence > pending.Before && m.Sequence <= pending.After && m.CardId == id &&
                m.From == from && m.Reason.Value == "skill-program.xunyi.discard" &&
                (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && GetAdvancedCard(id).IsGeneralWeapon)) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26320)]
    private void RequireXunyi(ProgramSkillFrame frame)
    {
        if (frame.Xunyi is not null && !ValidXunyi(frame))
            throw new InvalidOperationException("Xunyi lost its exact native damage, frozen relation, point cursor or once-paid consequence.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26320)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26320)]
    private bool ContinueIssuedXunyi(ProgramSkillFrame frame) => ValidXunyi(frame);
    private bool IsXunyiMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation continuation) =>
        effect?.Op == SkillProgramEffectOp.SettleXunyiDamagePoint && frame.Xunyi is { Stage: XunyiStage.Children } pending &&
        continuation is { BeforeCount: 0, CoverageResultBind: null } && continuation.SubjectSeat == XunyiStep(pending).Seat && ValidXunyi(frame);

    private bool XunyiFirstChild(ProgramSkillFrame frame, ResolutionFrame child)
    {
        if (!ValidXunyi(frame) || frame.Xunyi is not { Stage: XunyiStage.Children } pending) return false;
        var (seat, draw) = XunyiStep(pending);
        var reason = draw ? "skill-program.xunyi.draw" : "skill-program.xunyi.discard";
        if (child is CardsMovedTriggerWindowFrame moved)
            return ProgramMovementReturnMatches(frame, moved) && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                m.Sequence > pending.Before && m.Sequence <= pending.After && m.Reason.Value == reason);
        if (draw || pending.CardId is not { } id || GetAdvancedCard(id).Kind != CardKind.SilverLion || pending.From != CardLocation.Equipment(seat)) return false;
        if (child is RecoveryReplacementFrame recovery)
            return recovery.ParentFrameId == frame.Id && recovery.Attempt.SourceSeat == seat && recovery.Attempt.TargetSeat == seat && recovery.Attempt.Amount == 1 &&
                recovery.Attempt.Completion is { Producer: RecoveryAttemptProducer.SilverLion, MoveReason: { } moveReason } && moveReason.Value == reason;
        return child is HpChangedTriggerWindowFrame hp && ProgramHpReturnMatches(frame, hp) && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            hp.Change.SourceSeat == seat && hp.Change.TargetSeat == seat && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.Amount == 1;
    }
    private bool IsXunyiRecoveryParent(ProgramSkillFrame frame, RecoveryReplacementFrame child) => XunyiFirstChild(frame, child);
    private ProgramSkillFrame? XunyiObserverRoot() => FindProgramObserverRoot(
        static (engine, frame, child) => engine.XunyiFirstChild(frame, child), static (engine, index) => engine.HasMovementObserverSuffix(index));
    private bool HasXunyiDamageObserver(long id) => XunyiObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasXunyiBeforeDamageObserver(long id) => XunyiObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is BeforeDamageProgramWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsXunyiDying() => ActiveDying is { } dying && XunyiObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 26320)]
    private bool AdvanceXunyiSubtree() => _pendingDecision is null && XunyiObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    private bool AllowsXunyiNestedDamage(ProgramSkillFrame frame, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || frame.AttackAttempt is not null || frame.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != frame.Id ||
            XunyiObserverRoot() is not { } root || root.Id == frame.Id) return false;
        var effect = GetPausedPrivateOfferEffect(frame);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } reference ? ResolveProgramParticipant(frame, reference) : ResolveProgramEffectTarget(frame, effect.Target));
    }
}
