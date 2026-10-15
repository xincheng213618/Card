namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsSimultaneousPindianMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)
    {
        if (e?.Op != SkillProgramEffectOp.StartSimultaneousPindian || f.SimultaneousPindian is not { } r ||
            r.Stage is not (SimultaneousPindianStage.MovementChildren or SimultaneousPindianStage.RevealChildren or SimultaneousPindianStage.Cleanup or SimultaneousPindianStage.DeathCleanup or SimultaneousPindianStage.ClaimMovementChildren or SimultaneousPindianStage.TopRefillChildren) ||
            p.BeforeCount != 0 || p.CoverageResultBind is not null || p.SubjectSeat != (r.Stage is SimultaneousPindianStage.MovementChildren or SimultaneousPindianStage.ClaimMovementChildren ? r.PaymentSeat : f.OwnerSeat)) return false;
        AssertSimultaneousPindian(f);
        if (r.Stage == SimultaneousPindianStage.TopRefillChildren)
        {
            var movements = _cardMovements.Where(m => m.Sequence > r.MovementBefore && m.Sequence <= r.MovementAfter).ToArray();
            if (r.PaymentKind == "top-refill") return movements.Length > 0 && movements.All(m => m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle);
            return r.PaymentKind == "top-reserve" && movements is [var reserve] && r.TopReceipts.Any(t => t.CardId == reserve.CardId && t.ReservationMovementSequence == reserve.Sequence) && SimultaneousMovementMatches(f,reserve);
        }
        if (r.Stage == SimultaneousPindianStage.ClaimMovementChildren)
        {
            var claim = r.Claims!.Claims[r.Claims.Index];
            var claims = CompleteProgramEventHistory().OfType<SimultaneousPindianClaimPaidEvent>().Where(e => e.ProgramFrameId == f.Id && e.ComparisonIndex == r.ComparisonIndex && e.ClaimIndex == r.Claims.Index).ToArray();
            return claims is [var paidClaim] && paidClaim.Source == claim.Source && paidClaim.Taken == (r.PaymentKind == "claim") &&
                paidClaim.MovementBefore == r.MovementBefore && paidClaim.MovementAfter == r.MovementAfter && paidClaim.MovementAfter - paidClaim.MovementBefore == paidClaim.CardIds.Count &&
                paidClaim.CardIds.All(id => claim.CardIds.Contains(id));
        }
        if (r.Stage != SimultaneousPindianStage.MovementChildren) return r.PaymentKind == (r.Stage == SimultaneousPindianStage.RevealChildren ? "reveal" : "cleanup");
        var paid = CompleteProgramEventHistory().OfType<SimultaneousPindianConsequencePaidEvent>().Where(x => x.ProgramFrameId == f.Id &&
            x.ComparisonIndex == r.ComparisonIndex && x.ConsequenceIndex == r.ConsequenceIndex).ToArray();
        return paid is [var fact] && fact.ParticipantSeat == r.PaymentSeat && fact.Branch == r.PaymentKind && fact.CardId == r.PaidCardId &&
            fact.MovementBefore == r.MovementBefore && fact.MovementAfter == r.MovementAfter;
    }
    private bool SimultaneousMovementMatches(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r = f.SimultaneousPindian!;
        if (!_cardMovements.Contains(m) || m.Sequence <= r.MovementBefore || m.Sequence > r.MovementAfter) return false;
        if (r.PaymentKind == "top-reserve") return r.TopReceipts.Any(t => t.CardId == m.CardId && t.ReservationMovementSequence == m.Sequence) && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing && m.Reason == SimultaneousTopReservationReason;
        if (r.PaymentKind == "top-refill") return m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile && m.Reason == CardMoveReasons.Reshuffle;
        if (r.PaymentKind == "reveal") return r.Cards.Any(c => c.CardId == m.CardId && m.From == SimultaneousOriginalLocation(r,c)) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.PindianReveal;
        if (r.PaymentKind == "cleanup") return r.Cards.Any(c => c.CardId == m.CardId) && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.PindianFinish;
        if (r.PaymentKind == "claim") return r.Claims!.Claims[r.Claims.Index].CardIds.Contains(m.CardId) && m.From == CardLocation.Processing &&
            m.To == CardLocation.Hand(r.PaymentSeat!.Value) && m.Reason.Value == "program.pindian.claim";
        return r.PaymentKind == "discard" ? m.CardId == r.PaidCardId && m.From == r.PaidFrom && m.To == CardLocation.DiscardPile && m.Reason.Value == SimultaneousDiscardReason :
            r.PaymentKind == "draw" && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == SimultaneousDrawReason;
    }
    private bool SimultaneousFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if (root.PendingMovementContinuation is not { } p || !IsSimultaneousPindianMovement(root,GetPausedPrivateOfferEffect(root),p)) return false;
        var r = root.SimultaneousPindian!;
        if (child is CardsMovedTriggerWindowFrame moved)
            return moved.Batch.ParentFrameId == root.Id && moved.Batch.AwaitingProgramFrameId == root.Id && moved.ResumeProgramFrameId is null &&
                moved.Batch.OriginOwnerSeat == root.OwnerSeat && moved.Batch.OriginSkillId == root.SkillId && moved.Batch.OriginSkillInstanceId == root.SkillInstanceId &&
                moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => SimultaneousMovementMatches(root,m));
        if (child is ProgramLifecycleTriggerWindowFrame state && state.Window == SkillProgramTriggerWindow.SkillsChanged)
            return state.ResumeProgramFrameId == root.Id && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        var lion = r.PaymentKind == "discard" && r.PaidCardId is { } id && r.PaidFrom == CardLocation.Equipment(r.PaymentSeat!.Value) &&
            _cardMovements.Any(m => m.CardId == id && m.CardKind == CardKind.SilverLion && SimultaneousMovementMatches(root,m));
        if (!lion) return false;
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == root.Id && hp.ResumeFrameId == root.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery &&
            hp.Change.SourceSeat == r.PaymentSeat && hp.Change.TargetSeat == r.PaymentSeat && hp.Change.Amount == 1;
        return child is RecoveryReplacementFrame recovery && RecoveryReplacementFrameRidesOn(recovery,root) &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
            recovery.Attempt.SourceSeat == r.PaymentSeat && recovery.Attempt.TargetSeat == r.PaymentSeat && recovery.Attempt.Amount == 1;
    }
    private ProgramSkillFrame? SimultaneousObserverRoot() => FindProgramObserverRoot(
        static (engine,root,child) => engine.SimultaneousFirstChild(root,child),static (engine,index) => engine.HasMovementObserverSuffix(index));
    private bool TryAdvanceSimultaneousSubtree() => _pendingDecision is null && SimultaneousObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    private bool HasSimultaneousDamageObserver(long id) => SimultaneousObserverRoot() is { } root && _resolutionStack.Any(f => f.Id == id && f is DamageTriggerWindowFrame) &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsSimultaneousProgramDying() => ActiveDying is { } dying && SimultaneousObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsSimultaneousNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || f.InstructionIndex < 1 || _resolutionStack.LastOrDefault()?.Id != f.Id ||
            SimultaneousObserverRoot() is not { } root || root.Id == f.Id || f.WindowContext?.Window is not (SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.CardsGained or
                SkillProgramTriggerWindow.DiscardPileReceived or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.SkillsChanged)) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } reference ? ResolveProgramParticipant(f,reference) : ResolveProgramEffectTarget(f,e.Target));
    }
    private IReadOnlyList<int> SimultaneousHeldProcessingCards() => Array.AsReadOnly(_resolutionStack.OfType<ProgramSkillFrame>()
        .Where(f => f.SimultaneousPindian is not null && IsSimultaneousReceiptSource(f)).SelectMany(f => f.SimultaneousPindian!.Cards.Where(c => f.SimultaneousPindian.Revealed || f.SimultaneousPindian.TopReceipts.Any(t => t.CardId == c.CardId)))
        .Select(c => c.CardId).Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).Distinct().Order().ToArray());
}
