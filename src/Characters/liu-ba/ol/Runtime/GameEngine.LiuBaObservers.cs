namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27390)]private void AssertLiuBa(ProgramSkillFrame f)
    {
        if(f.LiuBa is not{}r)return;
        if(r.Source!=LiuBaSource(f)||r.GameplayHash!=f.GameplayHash||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash||f.InstructionIndex!=1||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||!Enum.IsDefined(r.Stage)||r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||r.ParentId!=(f.WindowContext?.ParentFrameId??0)||!IsValidPlayerSeat(r.Recipient)||r.Before>r.After||r.After>_movementSequence||r.Entities.Select(e=>e.CardId).Distinct().Count()!=r.Entities.Count||r.Coins.Distinct().Count()!=r.Coins.Count||r.SelectedBottom.Distinct().Count()!=r.SelectedBottom.Count||r.Coins.Count>r.ViewedBottom.Count||r.SelectedBottom.Count>r.Coins.Count||ProgramEventHistory<LiuBaStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var start]||start!=new LiuBaStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.ParentId,r.ActualTurn,r.TurnOwner,r.PhaseInstance))throw new InvalidOperationException("Liu Ba lost his original native binding, participant or frozen physical selections.");
        if(!r.Paid){if(f.PendingMovementContinuation is not null||r.Step!=0)throw new InvalidOperationException("Liu Ba cannot claim a movement before physical payment.");return;}
        if(r.Step<=0||ProgramEventHistory<LiuBaMovementIssuedEvent>().Where(e=>e.FrameId==f.Id&&e.Step==r.Step).ToArray()is not[var movement]||movement!=new LiuBaMovementIssuedEvent(f.Id,r.Step,r.Stage,r.Before,r.After,r.ActualCount))throw new InvalidOperationException("Liu Ba lost his once-paid movement boundary.");
        var records=ProgramEventHistory<LiuBaEntityMovedEvent>().Where(e=>e.FrameId==f.Id&&e.Step==r.Step).ToArray();
        if(records.Length!=(r.Stage==LiuBaStage.DrawChildren?0:r.ActualCount)||records.Any(e=>e.Sequence<=r.Before||e.Sequence>r.After||e.BatchId!=r.BatchId||!r.Entities.Any(c=>c.CardId==e.CardId&&c.Kind==e.Kind&&c.From==e.From)||_cardMovements.Count(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==e.From&&m.To==e.To)!=1))throw new InvalidOperationException("Liu Ba lost an exact original physical payment invoice.");
        if(r.Op==SkillProgramEffectOp.IssueTongduHandGift&&ProgramEventHistory<TongduGiftIssuedEvent>().Count(e=>e.FrameId==f.Id&&ValidTongduGift(e))!=1)throw new InvalidOperationException("Tongdu lost the exact gifted Hand entity.");
        if(r.Op==SkillProgramEffectOp.ReturnTongduOriginalCardAtPlayEnd&&(r.DebtId is not{}debt||ProgramEventHistory<TongduDebtConsumedEvent>().Count(e=>e.DebtId==debt&&e.FrameId==f.Id&&e.ParentId==r.ParentId&&e.ActualTurn==r.ActualTurn&&e.PhaseInstance==r.PhaseInstance)!=1))throw new InvalidOperationException("Tongdu cannot repeat or transfer its issued original-card debt.");
        if(f.PendingMovementContinuation is{}p&&!IsLiuBaMovement(f,GetPausedPrivateOfferEffect(f),p))throw new InvalidOperationException("Liu Ba lost the typed waiting participant.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 340)]
    private bool IsLiuBaMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.LiuBa is{Paid:true}r&&e?.Op==r.Op&&p==f.PendingMovementContinuation&&p.SubjectSeat==r.Recipient&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool LiuBaMovementMatches(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r=f.LiuBa!;if(!_cardMovements.Contains(m)||m.Sequence<=r.Before||m.Sequence>r.After)return false;
        if(r.Stage==LiuBaStage.DrawChildren)return m.Reason==CardMoveReasons.RecastDraw&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Recipient)||m.Reason==CardMoveReasons.Reshuffle&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile;
        if(ProgramEventHistory<LiuBaEntityMovedEvent>().Any(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Sequence==m.Sequence&&e.CardId==m.CardId&&e.From==m.From&&e.To==m.To))return true;
        return r.Grain.Contains(m.CardId)&&m.From==CardLocation.WoodenOxGrain(r.Recipient)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard;
    }
    private bool LiuBaLion(ProgramSkillFrame f,int seat)=>f.LiuBa is{Paid:true}r&&r.Stage!=LiuBaStage.DrawChildren&&r.Entities.Any(c=>c.Kind==CardKind.SilverLion&&c.From==CardLocation.Equipment(seat))&&ProgramEventHistory<LiuBaEntityMovedEvent>().Any(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Kind==CardKind.SilverLion&&e.From==CardLocation.Equipment(seat));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 140)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 130)]
    private bool IsLiuBaRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.LiuBa is{Paid:true}&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.Amount==1&&recovery.Attempt.SourceSeat==recovery.Attempt.TargetSeat&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&LiuBaLion(f,recovery.Attempt.TargetSeat)&&recovery.Attempt.Completion.MoveReason?.Value==(f.LiuBa.Stage==LiuBaStage.RecastChildren?CardMoveReasons.RecastDiscard.Value:f.LiuBa.Stage==LiuBaStage.ReturnChildren?TongduReturnReason:LiuBaExchangeReason);
    private bool LiuBaFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.LiuBa is not{Paid:true}r)return false;AssertLiuBa(f);
        if(child is CardsMovedTriggerWindowFrame moved)return f.PendingMovementContinuation is not null&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>LiuBaMovementMatches(f,m));
        if(child is RecoveryReplacementFrame recovery)return IsLiuBaRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==hp.Change.TargetSeat&&hp.Change.Amount>0&&LiuBaLion(f,hp.Change.TargetSeat);
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        return false;
    }
    private ProgramSkillFrame? LiuBaObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.LiuBaFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.IsMovementObserverEdge(n)||x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 850)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 960)]
    private bool HasLiuBaDamageObserver(long id)=>LiuBaObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 990)]
    private bool HasLiuBaBeforeDamageObserver(long id)=>HasLiuBaDamageObserver(id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 820)]
    private bool IsLiuBaDying()=>ActiveDying is{}d&&LiuBaObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1390)]
    private bool AllowsLiuBaNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||LiuBaObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27390)]private bool AdvanceLiuBaChildren()=>_pendingDecision is null&&LiuBaObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
