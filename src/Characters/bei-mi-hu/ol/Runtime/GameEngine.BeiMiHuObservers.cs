using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidBeiMiHu(ProgramSkillFrame f)
    {
        if(f.BeiMiHu is not{}r || r.Source!=BeiMiHuSource(f) || r.GameplayHash!=f.GameplayHash || r.ParentFrameId!=f.WindowContext?.ParentFrameId || r.InstructionIndex!=f.InstructionIndex ||
            r.Turn!=BeiMiHuReceiptTurn(f) || f.InstructionIndex!=1 || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=f.GameplayHash)return false;
        var events=CompleteProgramEventHistory().ToArray();
        if(r.Stage is "round-choice" or "before-turn-choice")return ExactBeiMiHuLifecycleParent(f) && r.Candidates.Count>0 && r.Candidates.Distinct().Count()==r.Candidates.Count &&
            events.OfType<BeiMiHuChoiceStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var started] && started.Source==r.Source && started.GameplayHash==r.GameplayHash && started.ParentFrameId==r.ParentFrameId && started.Turn==r.Turn && started.Round==r.Round && started.Candidates.SequenceEqual(r.Candidates);
        if(r.Stage=="draw-children")return r.DamageParent is{}damage && r.DamageTarget is{}target && r.Before<=r.After && r.ActualDraw is >=0 and <=1 &&
            events.OfType<BeiMiHuGujuDrawPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var draw] && draw==new BeiMiHuGujuDrawPaidEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,damage,target,r.Turn,r.Before,r.After,r.ActualDraw) &&
            _cardMovements.Count(m=>m.Sequence>r.Before && m.Sequence<=r.After && m.From==CardLocation.DrawPile && m.To==CardLocation.Hand(f.OwnerSeat) && m.Reason.Value==BeiMiHuDrawReason)==r.ActualDraw;
        if(r.Stage is "maximum-children" or "recovery-children" or "grants-children")return ExactBeiMiHuLifecycleParent(f) && r.CumulativeDraw>=7 && BeiMiHuActualCards(f.OwnerSeat)>=r.CumulativeDraw && r.MaximumAfter==r.MaximumBefore+1 &&
            events.OfType<BeiMiHuAwakeningMaximumPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var maximum] && maximum==new BeiMiHuAwakeningMaximumPaidEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.Turn,r.CumulativeDraw,r.MaximumBefore,r.MaximumAfter) &&
            events.OfType<MaximumHpChangedEvent>().Any(e=>e.PlayerSeat==f.OwnerSeat && e.MaximumHp==r.MaximumAfter && e.Delta==1 && e.SkillId==f.SkillId) && r.RecoveryRequested is >=0 and <=1;
        if(r.Stage=="append-choice")return r.CardUseId is{}id && LifecycleCardUse(id) is{}use && use.Action?.ActionId==r.ActionId && use.TargetSeats.SequenceEqual(r.OriginalTargets) &&
            events.OfType<BeiMiHuChoiceStartedEvent>().Where(e=>e.FrameId==f.Id && e.Kind=="append").ToArray() is[var choice] && choice.Source==r.Source && choice.Candidates.SequenceEqual(r.Candidates) &&
            r.SelectedTargets.All(s=>IsValidPlayerSeat(s)) && r.Choices.All(c=>c.Parameters.GetValueOrDefault("frame-id")==f.Id.ToString(CultureInfo.InvariantCulture));
        return r.Stage is "cancel-paid" or "append-paid" && r.CardUseId is{}useId && LifecycleCardUse(useId) is{}paidUse && ValidBeiMiHuTargetMutation(paidUse);
    }
    private void AssertBeiMiHu(ProgramSkillFrame f){if(f.BeiMiHu is not null && !ValidBeiMiHu(f))throw new InvalidOperationException("Bei Mi Hu lost its actual lifecycle/damage invoice or exact paid target mutation.");}

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 210)]
    private void AssertRegisteredBeiMiHu(ProgramSkillFrame frame)
    {
        if (frame.BeiMiHu is not null) AssertBeiMiHu(frame);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 170)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 170)]
    private bool CanContinueBeiMiHu(ProgramSkillFrame f)=>f.BeiMiHu is{}r && r.Stage is not("round-choice" or "before-turn-choice" or "append-choice") && ValidBeiMiHu(f);
    private bool BeiMiHuFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.BeiMiHu is not{}r || !ValidBeiMiHu(f))return false;
        if(child is CardsMovedTriggerWindowFrame moved)return r.Stage=="draw-children" && moved.Batch.ParentFrameId==f.Id && moved.Batch.AwaitingProgramFrameId==f.Id && moved.Batch.OriginOwnerSeat==f.OwnerSeat &&
            moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.Before && m.Sequence<=r.After);
        if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id && hp.Continuation==PostEventContinuation.Program && hp.Change.ParentFrameId==f.Id && hp.Change.TargetSeat==f.OwnerSeat &&
            (r.Stage=="maximum-children"?hp.Change.Kind==HpChangeKind.MaximumHp: r.Stage=="recovery-children" && hp.Change.Kind==HpChangeKind.Recovery && hp.Change.SourceSeat==f.OwnerSeat && hp.Change.Amount<=r.RecoveryRequested);
        if(child is ProgramLifecycleTriggerWindowFrame skills)return r.Stage=="grants-children" && skills.Window==SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId==f.Id && skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        if(child is RecoveryReplacementFrame recovery)return r.Stage=="recovery-children" && recovery.ParentFrameId==f.Id && recovery.Attempt.SourceSeat==f.OwnerSeat && recovery.Attempt.TargetSeat==f.OwnerSeat &&
            recovery.Attempt.Amount==r.RecoveryRequested && recovery.Attempt.ActualTurnNumber==r.Turn && recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.Program && recovery.Attempt.Completion.InstructionIndex==r.InstructionIndex;
        return false;
    }
    private ProgramSkillFrame? BeiMiHuObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.BeiMiHuFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 100)]
    private bool IsBeiMiHuDying()=>ActiveDying is{}d && BeiMiHuObserverRoot() is{}r && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 130)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 240)]
    private bool HasBeiMiHuDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && BeiMiHuObserverRoot() is not null;
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 330)]
    private bool HasBeiMiHuBeforeDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && BeiMiHuObserverRoot() is not null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 230)]
    private bool TryAdvanceBeiMiHuSubtree()=>_pendingDecision is null && BeiMiHuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 470)]
    private bool AllowsBeiMiHuNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
    {
        if(sourceLess || f.AttackAttempt is not null || f.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=f.Id || BeiMiHuObserverRoot() is null ||
            f.WindowContext?.Window is not(SkillProgramTriggerWindow.CardsGained or SkillProgramTriggerWindow.CardsMoved or SkillProgramTriggerWindow.AfterHpRecovered or SkillProgramTriggerWindow.AfterHealthChanged or SkillProgramTriggerWindow.AfterHpLost or SkillProgramTriggerWindow.AfterDamageApplied or SkillProgramTriggerWindow.SkillsChanged))return false;
        var e=BeiMiHuPausedEffect(f)!;return e.Op==SkillProgramEffectOp.Damage && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature && target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
}
