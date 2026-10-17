namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ValidDaQiaoXiaoQiao(ProgramSkillFrame f)=>f.DaQiaoXiaoQiao is{}r&&f.SkillId=="ol:xingwu"&&f.TriggerId=="discard-start-dance"&&f.ActivationId==f.TriggerId&&f.InstructionIndex==1&&f.PendingMovementContinuation is null&&
        r.Source==DaQiaoXiaoQiaoSource(f)&&r.GameplayHash==f.GameplayHash&&r.ActualTurn==_turnNumber&&r.ParentId==f.WindowContext?.ParentFrameId&&DaQiaoXiaoQiaoParent(f)&&Enum.IsDefined(r.Stage)&&r.Pile.OwnerSeat==f.OwnerSeat&&r.Pile.SkillId==f.SkillId&&r.Pile.SkillInstanceId==f.SkillInstanceId&&r.Pile.Capacity==int.MaxValue&&
        r.Selected.Select(c=>c.Id).Distinct().Count()==r.Selected.Count&&r.Entities.Select(c=>c.Id).Distinct().Count()==r.Entities.Count&&r.Before<=r.After&&
        ProgramEventHistory<DaQiaoXiaoQiaoStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.ParentId==r.ParentId&&e.ActualTurn==r.ActualTurn)==1&&
        (r.MovementStep==0||ValidDaQiaoXiaoQiaoPayment(f,r))&&
        (r.TargetSeat is null||IsValidPlayerSeat(r.TargetSeat.Value)&&r.TargetSeat!=f.OwnerSeat);
    private bool ValidDaQiaoXiaoQiaoPayment(ProgramSkillFrame f,DaQiaoXiaoQiaoReceipt r)
    {
        if(ProgramEventHistory<DaQiaoXiaoQiaoMovementPaidEvent>().Where(e=>e.FrameId==f.Id&&e.Step==r.MovementStep).ToArray()is not[var paid]||paid.Before!=r.Before||paid.After!=r.After||paid.Count!=r.Entities.Count)return false;
        var reason=paid.Stage==DaQiaoXiaoQiaoStage.DepositChildren?XingwuDepositReason:paid.Stage==DaQiaoXiaoQiaoStage.CostChildren?r.Branch=="pile"?XingwuPileReason:XingwuHandReason:XingwuEquipmentReason;
        return r.Entities.All(entity=>ProgramEventHistory<DaQiaoXiaoQiaoEntityPaidEvent>().Where(e=>e.FrameId==f.Id&&e.Step==r.MovementStep&&e.CardId==entity.Id).ToArray()is[var receipt]&&receipt.From==entity.From&&receipt.Sequence>r.Before&&receipt.Sequence<=r.After&&
            _cardMovements.Count(m=>m.Sequence==receipt.Sequence&&m.CardId==entity.Id&&m.From==entity.From&&m.To==receipt.To&&m.Reason.Value==reason)==1&&
            receipt.To==(entity.From.Zone==CardZoneKind.Equipment&&GetAttackCard(entity.Id).IsGeneralWeapon?CardLocation.OutsideGame:paid.Stage==DaQiaoXiaoQiaoStage.DepositChildren?r.Pile.Location:CardLocation.DiscardPile));
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27340)]private void AssertDaQiaoXiaoQiao(ProgramSkillFrame f){if(f.DaQiaoXiaoQiao is not null&&!ValidDaQiaoXiaoQiao(f))throw new InvalidOperationException("Xingwu lost its exact actual-phase parent, original source, frozen paid entities or once-only native child receipt.");}
    private bool DaQiaoXiaoQiaoMovementMatches(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r=f.DaQiaoXiaoQiao!;if(!_cardMovements.Contains(m)||m.Sequence<=r.Before||m.Sequence>r.After)return false;
        if(m.Reason==CardMoveReasons.WoodenOxGrainDiscard)return r.Grain.Contains(m.CardId)&&m.From.Zone==CardZoneKind.WoodenOxGrain&&m.To==CardLocation.DiscardPile;
        return ProgramEventHistory<DaQiaoXiaoQiaoEntityPaidEvent>().Any(e=>e.FrameId==f.Id&&e.Step==r.MovementStep&&e.CardId==m.CardId&&e.From==m.From&&e.To==m.To&&e.Sequence==m.Sequence)&&r.Entities.Any(e=>e.Id==m.CardId&&e.From==m.From)&&
            m.Reason.Value==(r.Stage==DaQiaoXiaoQiaoStage.DepositChildren?XingwuDepositReason:r.Stage==DaQiaoXiaoQiaoStage.CostChildren?r.Branch=="pile"?XingwuPileReason:XingwuHandReason:XingwuEquipmentReason);
    }
    private bool IsDaQiaoXiaoQiaoRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.DaQiaoXiaoQiao is{}r&&ValidDaQiaoXiaoQiao(f)&&r.Stage is(DaQiaoXiaoQiaoStage.DepositChildren or DaQiaoXiaoQiaoStage.EquipmentChildren)&&
        r.Entities.Any(c=>c.From.Zone==CardZoneKind.Equipment&&GetAttackCard(c.Id).Kind==CardKind.SilverLion&&c.From.OwnerSeat==recovery.Attempt.TargetSeat)&&recovery.ParentFrameId==f.Id&&recovery.Return==new RecoveryReplacementReturn(PostEventContinuation.Program,f.Id)&&
        recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==(r.Stage==DaQiaoXiaoQiaoStage.DepositChildren?XingwuDepositReason:XingwuEquipmentReason)&&recovery.Attempt.SourceSeat==recovery.Attempt.TargetSeat&&recovery.Attempt.Amount==1;
    private bool DaQiaoXiaoQiaoFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.DaQiaoXiaoQiao is not{}r||!ValidDaQiaoXiaoQiao(f))return false;
        if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==f.Id&&moved.ResumeProgramFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>DaQiaoXiaoQiaoMovementMatches(f,m));
        if(child is RecoveryReplacementFrame recovery)return IsDaQiaoXiaoQiaoRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount==1&&r.Entities.Any(c=>c.From==CardLocation.Equipment(hp.Change.TargetSeat)&&GetAttackCard(c.Id).Kind==CardKind.SilverLion);
        if(child is ProgramLifecycleTriggerWindowFrame lifecycle)return lifecycle.ResumeProgramFrameId==f.Id&&(lifecycle.Window==SkillProgramTriggerWindow.SkillsChanged&&lifecycle.Continuation==ProgramLifecycleContinuation.ResumeParentProgram||r.Stage==DaQiaoXiaoQiaoStage.TurnoverChildren&&lifecycle.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&lifecycle.CharacterStateContinuation==CharacterStateContinuation.Program&&lifecycle.OwnerSeat==f.OwnerSeat);
        if(r.Stage!=DaQiaoXiaoQiaoStage.DamageChildren||f.AttackAttempt is not{}attack||attack.TargetSeat!=r.TargetSeat)return false;
        return child is BeforeDamageProgramWindowFrame before&&before.ParentFrameId==f.Id||child is DamageTriggerWindowFrame damage&&damage.ParentFrameId==f.Id||child is DyingFrame dying&&dying.ParentFrameId==f.Id&&dying.VictimSeat==attack.TargetSeat;
    }
    private ProgramSkillFrame? DaQiaoXiaoQiaoObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.DaQiaoXiaoQiaoFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.IsMovementObserverEdge(n)||x.RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private bool HasDaQiaoXiaoQiaoDamageObserver(long id)=>DaQiaoXiaoQiaoObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasDaQiaoXiaoQiaoBeforeDamageObserver(long id)=>HasDaQiaoXiaoQiaoDamageObserver(id);
    private bool IsDaQiaoXiaoQiaoDying()=>ActiveDying is{}dying&&DaQiaoXiaoQiaoObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsDaQiaoXiaoQiaoNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id)return false;
        if(f.DaQiaoXiaoQiao is{Stage:DaQiaoXiaoQiaoStage.DamageChildren}r&&ValidDaQiaoXiaoQiao(f))return source is null&&nature is null&&target==r.TargetSeat&&amount==r.Damage&&ProgramEventHistory<DaQiaoXiaoQiaoDamageIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.TargetSeat==target&&e.Amount==amount)==1;
        if(DaQiaoXiaoQiaoObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27340)]private bool AdvanceDaQiaoXiaoQiaoChildren()=>_pendingDecision is null&&DaQiaoXiaoQiaoObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
