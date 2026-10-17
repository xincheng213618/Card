namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidQushi(ProgramSkillFrame f)
    {
        if(f.Qushi is not{}r||r.Source!=QushiSource(f)||r.GameplayHash!=f.GameplayHash||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash||f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||r.ParentId!=(f.WindowContext?.ParentFrameId??0)||r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||r.Before<0||r.After<r.Before||r.After>_movementSequence||!Enum.IsDefined(r.Stage)||ProgramEventHistory<QushiStartedEvent>().Count(e=>e==new QushiStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.ActualTurn,r.TurnOwner,r.ParentId))!=1)return false;
        if(r.Op==SkillProgramEffectOp.ResolveQushiDrawAndPrivateSeal)
        {if(f.TriggerId is not null||f.ActivationId!="draw-seal"||f.WindowContext is not null)return false;}
        else
        {if(f.WindowContext is not{}context||r.Seal is not{}seal||CurrentQushiEndingSeal(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex),context)!=seal)return false;}
        if(!r.Paid)return false;
        var invoice=ProgramEventHistory<QushiMovementPaidEvent>().Where(e=>e.FrameId==f.Id&&e.Step==r.Step).ToArray();
        if(invoice is not[var m]||m.Before!=r.Before||m.After!=r.After||m.Count<0)return false;
        if(f.PendingMovementContinuation is{}p&&!IsQushiMovement(f,GetPausedPrivateOfferEffect(f),p))return false;
        if(r.Stage==QushiStage.SealChildren&&(r.Seal is not{}deposited||deposited.SealId!=f.Id||!ValidQushiSeal(deposited)||m.Stage!=r.Stage||m.Count!=1))return false;
        if(r.Stage is QushiStage.RemoveChildren or QushiStage.RewardChildren)
        {if(r.Seal is not{}s||ProgramEventHistory<QushiSealRemovedEvent>().Count(e=>e.FrameId==f.Id&&e.SealId==s.SealId&&e.HolderSeat==s.HolderSeat&&e.CardId==s.CardId&&!e.HolderDeath&&_cardMovements.Any(v=>v.Sequence==e.Sequence&&v.CardId==s.CardId&&v.From==s.Location&&v.To==CardLocation.DiscardPile&&v.Reason.Value==QushiRemoveReason))!=1)return false;}
        return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27460)]private void AssertQushi(ProgramSkillFrame f){if(f.Qushi is not null&&!ValidQushi(f))throw new InvalidOperationException("Qushi lost its original source, typed holder Ending, physical seal or once-paid native movement interval.");}
    private bool IsQushiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.Qushi is{Paid:true}r&&GuoTuQushiComposition.Handles(r.Op)&&e?.Op==r.Op&&p==f.PendingMovementContinuation&&p.BeforeCount==0&&p.CoverageResultBind is null&&p.SubjectSeat==(r.Stage is QushiStage.DrawChildren or QushiStage.RewardChildren?f.OwnerSeat:r.HolderSeat);
    private bool QushiMovementMatches(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r=f.Qushi!;if(m.Sequence<=r.Before||m.Sequence>r.After||!_cardMovements.Contains(m))return false;
        if(r.Stage is QushiStage.DrawChildren or QushiStage.RewardChildren)return m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==QushiDrawReason||m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle;
        return r.Seal is{}s&&m.CardId==s.CardId&&(r.Stage==QushiStage.SealChildren?m.From==CardLocation.Hand(f.OwnerSeat)&&m.To==s.Location&&m.Reason.Value==QushiSealReason:m.From==s.Location&&m.To==CardLocation.DiscardPile&&m.Reason.Value==QushiRemoveReason);
    }
    private bool QushiFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.Qushi is not{Paid:true}||!ValidQushi(f))return false;
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is not null&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>QushiMovementMatches(f,m));
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        return false;
    }
    private ProgramSkillFrame? QushiObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.QushiFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.IsMovementObserverEdge(n)||x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private bool HasQushiDamageObserver(long id)=>QushiObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasQushiBeforeDamageObserver(long id)=>HasQushiDamageObserver(id);
    private bool IsQushiDying()=>ActiveDying is{}d&&QushiObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsQushiNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||QushiObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27460)]private bool AdvanceQushiChildren()=>_pendingDecision is null&&QushiObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
