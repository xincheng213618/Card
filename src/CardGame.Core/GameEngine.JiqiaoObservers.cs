namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 90)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 90)]
    private bool CanContinueIssuedJiqiao(ProgramSkillFrame f)
    {
        if(f.JiqiaoPaid is not {} r)return false;var i=r.Invoice;
        if(i.Source.OwnerSeat!=f.OwnerSeat || i.Source.SkillId!=f.SkillId || i.Source.SkillInstanceId!=f.SkillInstanceId || i.Source.BindingId!=f.TriggerId || i.GameplayHash!=f.GameplayHash || i.InstructionIndex!=f.InstructionIndex || i.WindowId!=f.WindowContext?.ParentFrameId ||
            f.WindowContext is not {Window:SkillProgramTriggerWindow.PlayPhaseStarting,SourceSeat:{} actor} || actor!=f.OwnerSeat || i.ActualTurnNumber!=_turnNumber ||
            GetPausedPrivateOfferEffect(f) is not {Op:SkillProgramEffectOp.DiscardEquipmentAndResolveJiqiao} e || e.SourceBind!=i.CostBind || e.ResultBind!=i.RevealBind || i.PaidCount<=0 || i.CostBefore<0 || i.CostAfter<i.CostBefore || r.Before<0 || r.After<r.Before || r.After>_movementSequence || r.ActualCount<0 || !Enum.IsDefined(r.Stage))return false;
        var index=_resolutionStack.FindIndex(p=>p.Id==f.Id);
        if(index<1 || _resolutionStack[index-1] is not PlayPhaseStartingBoundaryFrame w || w.Id!=i.WindowId || w.OwnerSeat!=f.OwnerSeat || w.ItemIndex<0 || w.ItemIndex>=w.Items.Count || w.Items[w.ItemIndex] is not {Kind:TurnEndingBoundaryItemKind.Program,Candidate:{} candidate} || candidate.OwnerSeat!=f.OwnerSeat || candidate.SkillId!=f.SkillId || candidate.SkillInstanceId!=f.SkillInstanceId || candidate.BindingId!=f.TriggerId || candidate.GameplayHash!=f.GameplayHash)return false;
        var cost=f.CardSetBindings.SingleOrDefault(b=>b.Name==i.CostBind);
        if(cost is null || cost.CardIds.Count!=i.PaidCount || CompleteProgramEventHistory().OfType<JiqiaoCostPaidEvent>().Count(x=>x.ProgramFrameId==f.Id && x.Invoice==i)!=1 ||
            cost.CardIds.Any(cid=>_cardMovements.Count(m=>m.Sequence>i.CostBefore && m.Sequence<=i.CostAfter && m.CardId==cid && m.From.OwnerSeat==f.OwnerSeat && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && m.To==CardLocation.DiscardPile && m.Reason.Value==JiqiaoCostReason)!=1))return false;
        return CompleteProgramEventHistory().OfType<JiqiaoStageIssuedEvent>().Count(x=>x.ProgramFrameId==f.Id && x.Stage==r.Stage && x.Before==r.Before && x.After==r.After && x.ActualCount==r.ActualCount)==1 &&
            _cardMovements.Count(m=>m.Sequence>r.Before && m.Sequence<=r.After && IsExactJiqiaoStageMovement(f,r,m))==r.ActualCount;
    }
    private bool IsExactJiqiaoStageMovement(ProgramSkillFrame f,JiqiaoPaidPending r,CardMovementRecord m)=>r.Stage switch
    {
        JiqiaoPaidStage.CostChildren=>m.Reason.Value==JiqiaoCostReason && m.From.OwnerSeat==f.OwnerSeat && m.To==CardLocation.DiscardPile && GetProgramCardSet(f,r.Invoice.CostBind).CardIds.Contains(m.CardId),
        JiqiaoPaidStage.RevealChildren=>m.Reason.Value==$"skill-program.{f.SkillId}.{GetProgramBindingId(f)}.reveal" && m.From==CardLocation.DrawPile && m.To==CardLocation.Processing && GetProgramCardSet(f,r.Invoice.RevealBind).CardIds.Contains(m.CardId),
        JiqiaoPaidStage.ClaimChildren=>m.Reason.Value==JiqiaoClaimReason && m.From==CardLocation.Processing && m.To==CardLocation.Hand(f.OwnerSeat) && GetProgramCardSet(f,r.Invoice.RevealBind).CardIds.Contains(m.CardId),
        JiqiaoPaidStage.CleanupChildren=>m.Reason.Value==JiqiaoRemainderReason && m.From==CardLocation.Processing && m.To==CardLocation.DiscardPile && GetProgramCardSet(f,r.Invoice.RevealBind).CardIds.Contains(m.CardId),_=>false
    };
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 150)]
    private void RequireIssuedJiqiao(ProgramSkillFrame f)
    {if(f.JiqiaoPaid is not null && !CanContinueIssuedJiqiao(f))throw new InvalidOperationException("Jiqiao lost its actual paid equipment, native stage movements or owning source invoice.");}
    private bool IsJiqiaoMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>e?.Op==SkillProgramEffectOp.DiscardEquipmentAndResolveJiqiao && p.SubjectSeat==f.OwnerSeat && p.BeforeCount==0 && p.CoverageResultBind is null && CanContinueIssuedJiqiao(f);
    private bool JiqiaoFirstChild(ProgramSkillFrame root,ResolutionFrame child)
    {
        if(!CanContinueIssuedJiqiao(root) || root.JiqiaoPaid is not {} r)return false;
        if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==root.Id && moved.Batch.AwaitingProgramFrameId==root.Id && moved.Batch.OriginOwnerSeat==root.OwnerSeat && moved.Batch.OriginSkillId==root.SkillId && moved.Batch.OriginSkillInstanceId==root.SkillInstanceId && moved.Batch.Movements.Count>0 &&
            moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>r.Before && m.Sequence<=r.After && IsExactJiqiaoStageMovement(root,r,m));
        if(r.Stage!=JiqiaoPaidStage.CostChildren)return false;
        // Removal can issue genuine Silver Lion recovery before its movement window.
        if(child is RecoveryReplacementFrame recovery)return recovery.Attempt.TargetSeat==root.OwnerSeat && recovery.Attempt.SourceSeat==root.OwnerSeat && recovery.Attempt.Amount==1 && recovery.Attempt.Completion is {Producer:RecoveryAttemptProducer.SilverLion,MoveReason:{} moveReason} && moveReason.Value==JiqiaoCostReason && HasPaidJiqiaoSilverLion(root);
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==root.Id && hp.ResumeFrameId==root.Id && hp.Change.TargetSeat==root.OwnerSeat && hp.Change.Kind==HpChangeKind.Recovery && hp.Change.Amount==1 && hp.Continuation==PostEventContinuation.AwaitedProgramMovement && HasPaidJiqiaoSilverLion(root);
        return false;
    }
    private bool HasPaidJiqiaoSilverLion(ProgramSkillFrame f)=>f.JiqiaoPaid is {} r && _cardMovements.Any(m=>m.Sequence>r.Invoice.CostBefore && m.Sequence<=r.Invoice.CostAfter && m.From==CardLocation.Equipment(f.OwnerSeat) && m.To==CardLocation.DiscardPile && m.Reason.Value==JiqiaoCostReason && JiqiaoCard(m.CardId).Kind==CardKind.SilverLion);
    private ProgramSkillFrame? JiqiaoObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.JiqiaoFirstChild(r,c),static(e,i)=>e.HasMovementObserverSuffix(i));
    private bool HasJiqiaoDamageObserver(long id)=>JiqiaoObserverRoot() is {} root && _resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && _resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool IsJiqiaoDying()=>ActiveDying is {} d && JiqiaoObserverRoot() is {} root && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 150)]
    private bool TryAdvanceJiqiaoSubtree()=>_pendingDecision is null && JiqiaoObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement:true);
    private bool AllowsJiqiaoNestedDamage(ProgramSkillFrame o,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess || o.AttackAttempt is not null || o.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=o.Id || JiqiaoObserverRoot() is not {} root || root.Id==o.Id)return false;
        var e=GetPausedPrivateOfferEffect(o);return e is {Op:SkillProgramEffectOp.Damage} && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature && target==(e.TargetReference is {} p?ResolveProgramParticipant(o,p):ResolveProgramEffectTarget(o,e.Target));
    }
    [ProgramCapability]
    private sealed class JiqiaoProgramCapability(GameEngine engine) : IJiqiaoProgramHost
    {public SkillProgramStepOutcome ExecutePaidJiqiao(ProgramSkillFrame f,SkillProgramEffect e)=>engine.ExecutePaidJiqiao(f,e);}
}
