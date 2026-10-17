namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidRuanYu(ProgramSkillFrame f)
    {
        if(f.RuanYu is not{}r)return false;
        if(f.InstructionIndex!=1||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}context||r.Source!=RuanYuSource(f)||r.GameplayHash!=f.GameplayHash||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||!RuanYuComposition.Handles(r.Op)||r.ParentId!=context.ParentFrameId||r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||!Enum.IsDefined(r.Stage)||!IsValidPlayerSeat(r.Recipient)||r.Before<0||r.After<r.Before||r.After>_movementSequence||ProgramEventHistory<RuanYuStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.Op==r.Op&&e.ParentId==r.ParentId&&e.ActualTurn==r.ActualTurn&&e.TurnOwner==r.TurnOwner)!=1)return false;
        if(r.Op==SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange)
        {var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);if(CurrentRuanYuEndingDebt(c,context)is not{}debt||r.DebtId!=debt.DebtId||ProgramEventHistory<XingzuoEndingConsumedEvent>().Count(e=>e.DebtId==debt.DebtId&&e.FrameId==f.Id&&e.ParentId==r.ParentId)!=1)return false;}
        else if(r.Op==SkillProgramEffectOp.ResolveMiaoxianUniqueRedUseDraw)
        {if(r.ActionId is not{}action||context.CardUse?.CardActionId!=action||ProgramEventHistory<MiaoxianRedDrawIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.ActionId==action&&e.Source==r.Source&&e.ActualTurn==r.ActualTurn)!=1||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.ParentId)is not{}window||window.CandidateIndex<0||window.CandidateIndex>=window.Candidates.Count||window.Candidates[window.CandidateIndex]is not{}candidate||candidate.OwnerSeat!=f.OwnerSeat||candidate.SkillId!=f.SkillId||candidate.TriggerId!=f.TriggerId||candidate.SkillInstanceId!=f.SkillInstanceId||candidate.GameplayHash!=r.GameplayHash||!HasRuanYuRedPayment(window.Action,r.Source,r.GameplayHash,window.ParentFrameId))return false;}
        else
        {
            var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
            if(context.Window!=SkillProgramTriggerWindow.PlayPhaseStarting||context.SourceSeat!=f.OwnerSeat||context.TargetSeat!=f.OwnerSeat||index<1||_resolutionStack[index-1]is not PlayPhaseStartingBoundaryFrame start||start.Id!=r.ParentId||start.OwnerSeat!=f.OwnerSeat||start.ItemIndex<0||start.ItemIndex>=start.Items.Count||start.Items[start.ItemIndex]is not{Kind:TurnEndingBoundaryItemKind.Program,Candidate:{}candidate}||!MountObserverCandidateMatches(f,candidate)||context.OccurrenceIndex!=candidate.OccurrenceIndex)return false;
        }
        if(r.SelectedHand.Distinct().Count()!=r.SelectedHand.Count||r.SelectedBottom.Distinct().Count()!=r.SelectedBottom.Count||r.OrderedBottom.Distinct().Count()!=r.OrderedBottom.Count||r.ViewedBottom.Distinct().Count()!=r.ViewedBottom.Count||r.ViewedBottom.Count>3||r.Entities.Select(c=>c.CardId).Distinct().Count()!=r.Entities.Count)return false;
        var moving=r.Stage is RuanYuStage.FrontChildren or RuanYuStage.EndingChildren or RuanYuStage.RedChildren;
        if(moving)
        {
            if(!r.Paid||f.PendingMovementContinuation is not{}p||!IsRuanYuMovement(f,GetPausedPrivateOfferEffect(f),p)||r.Step<=0||ProgramEventHistory<RuanYuMovementPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Stage==r.Stage&&e.Before==r.Before&&e.After==r.After&&e.Count==r.ActualCount)!=1)return false;
            var paid=ProgramEventHistory<RuanYuEntityMovedEvent>().Where(e=>e.FrameId==f.Id&&e.Step==r.Step).ToArray();if(paid.Any(e=>e.Sequence<=r.Before||e.Sequence>r.After||!r.Entities.Any(c=>c.CardId==e.CardId&&c.Kind==e.Kind&&c.From==e.From)||!_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==e.From&&m.To==e.To&&m.Reason.Value==RuanYuExchangeReason))||r.Stage!=RuanYuStage.RedChildren&&paid.Length!=r.ActualCount)return false;
        }
        else if(f.PendingMovementContinuation is not null)return false;
        if(r.Stage is RuanYuStage.LoseHpIssuing or RuanYuStage.LoseHpChildren)
        {
            if(!r.Paid||r.Op!=SkillProgramEffectOp.ResolveXingzuoIssuedEndingExchange||r.SelectedHand.Count<=3||ProgramEventHistory<RuanYuMovementPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Stage==RuanYuStage.EndingChildren&&e.Before==r.Before&&e.After==r.After&&e.Count==r.ActualCount)!=1)return false;
            var paid=ProgramEventHistory<ProgramSkillHpLostEvent>().Where(e=>e.FrameId==f.Id).ToArray();var own=ProgramEventHistory<XingzuoHpLossPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray();if(r.Stage==RuanYuStage.LoseHpIssuing?paid.Length!=0||own.Length!=0:paid is not[var hp]||hp.SkillId!=f.SkillId||hp.TargetSeat!=f.OwnerSeat||hp.Amount!=1||own is not[var receipt]||receipt.RemainingHp!=hp.RemainingHp)return false;
        }
        return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27430)]private void AssertRuanYu(ProgramSkillFrame f){if(f.RuanYu is not null&&!ValidRuanYu(f))throw new InvalidOperationException("Ruan Yu lost his exact original source, native parent, private physical sets or once-paid stage.");}
    private bool IsRuanYuMovement(ProgramSkillFrame f,SkillProgramEffect? effect,ProgramMovementContinuation pending)=>f.RuanYu is{Paid:true}r&&r.Stage is RuanYuStage.FrontChildren or RuanYuStage.EndingChildren or RuanYuStage.RedChildren&&effect?.Op==r.Op&&pending==f.PendingMovementContinuation&&pending.SubjectSeat==r.Recipient&&pending.BeforeCount==0&&pending.CoverageResultBind is null;
    private bool RuanYuFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.RuanYu is not{Paid:true}r||!ValidRuanYu(f))return false;
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is not null&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&(r.Stage==RuanYuStage.RedChildren?m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==MiaoxianDrawReason||m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle:ProgramEventHistory<RuanYuEntityMovedEvent>().Any(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Sequence==m.Sequence&&e.CardId==m.CardId&&e.From==m.From&&e.To==m.To)));
        if(child is HpChangedTriggerWindowFrame hp)return r.Stage==RuanYuStage.LoseHpChildren&&hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.SourceSeat is null&&hp.Change.TargetSeat==f.OwnerSeat&&hp.Change.Kind==HpChangeKind.Loss&&hp.Change.Amount==1;
        if(child is DyingFrame dying)return r.Stage==RuanYuStage.LoseHpChildren&&dying.ParentFrameId==f.Id&&dying.VictimSeat==f.OwnerSeat&&dying.Continuation==DyingContinuationKind.ProgramSkill&&dying.KillerSeat is null&&ProgramEventHistory<ProgramSkillHpLostEvent>().Count(e=>e.FrameId==f.Id&&e.Amount==1&&e.RemainingHp<=0)==1;
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? RuanYuObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.RuanYuFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    private bool HasRuanYuDamageObserver(long id)=>RuanYuObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasRuanYuBeforeDamageObserver(long id)=>HasRuanYuDamageObserver(id);
    private bool IsRuanYuDying()=>ActiveDying is{}d&&RuanYuObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsRuanYuNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||RuanYuObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27430)]private bool AdvanceRuanYuChildren()=>_pendingDecision is null&&RuanYuObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
