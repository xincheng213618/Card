namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,26700)]
    private void AssertDingShangWan(ProgramSkillFrame f)
    {
        if(f.DingShangWan is not{}r)return;
        if(f.TriggerId is null||f.InstructionIndex!=1||r.Instruction!=1||r.Source!=DingShangWanSource(f)||r.GameplayHash!=f.GameplayHash||
            GetPausedPrivateOfferEffect(f)?.Op!=r.Operation||!DingShangWanComposition.IsOperation(r.Operation)||f.WindowContext is not{}c||
            c.ParentFrameId!=r.WindowId||c.Window!=r.Window||c.DamageFrameId!=r.DamageFrameId||c.CardUse?.CardActionId!=r.ActionId||
            r.Turn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||!Enum.IsDefined(r.Stage)||r.Before<0||r.After<r.Before||r.After>_movementSequence||
            r.Minimum<0||r.Maximum<r.Minimum||r.Selected.Count>r.Maximum||r.Selected.Distinct().Count()!=r.Selected.Count||r.Selected.Any(id=>!r.Candidates.Contains(id))||
            r.WoodenOxGrainIds.Distinct().Count()!=r.WoodenOxGrainIds.Count||r.WoodenOxGrainIds.Intersect(r.Selected).Any()||
            !DingShangWanParent(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),c)||
            ProgramEventHistory<DingShangWanStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&
                e.Window==r.Window&&e.Operation==r.Operation&&e.Turn==r.Turn&&e.TurnOwner==r.TurnOwner&&e.Counterpart==r.Counterpart&&e.DamageFrameId==r.DamageFrameId&&e.ActionId==r.ActionId)!=1)
            throw new InvalidOperationException("Ding Shang Wan lost its exact native candidate, owning instruction, start fact or private selected entities.");
        if(r.Operation==SkillProgramEffectOp.InitializeFudaoHandRecord)
        {
            if(r.Window!=SkillProgramTriggerWindow.GameStarting||r.Counterpart is not null||r.Stage is not(DingShangWanStage.OpeningDraw or DingShangWanStage.OpeningGiftChoice or DingShangWanStage.OpeningGiftChildren or DingShangWanStage.OpeningDiscardChoice or DingShangWanStage.OpeningDiscardChildren)||
                DingShangWanRecord(f.OwnerSeat,f.SkillId,f.GameplayHash)is not null)throw new InvalidOperationException("Fudao cannot duplicate its game-opening record.");
        }
        else if(r.Operation==SkillProgramEffectOp.DrawFudaoRecordedPair)
        {
            if(r.Window!=SkillProgramTriggerWindow.AfterTurnEnded||r.Counterpart!=c.TargetSeat||r.Stage is not(DingShangWanStage.PairOwnerDraw or DingShangWanStage.PairTargetDraw)||
                DingShangWanRecord(f.OwnerSeat,f.SkillId,f.GameplayHash)is null)throw new InvalidOperationException("Fudao lost its original fixed count and accepted actual end.");
        }
        else if(r.Counterpart is not{}other||other==f.OwnerSeat||!IsValidPlayerSeat(other)||r.Stage is DingShangWanStage.OpeningDraw or DingShangWanStage.OpeningGiftChoice or DingShangWanStage.OpeningGiftChildren or DingShangWanStage.OpeningDiscardChoice or DingShangWanStage.OpeningDiscardChildren or DingShangWanStage.PairOwnerDraw or DingShangWanStage.PairTargetDraw||
            (r.Window==SkillProgramTriggerWindow.AfterDamageApplied?r.Counterpart!=c.SourceSeat||c.TargetSeat!=f.OwnerSeat||c.Amount<=0:r.Window!=SkillProgramTriggerWindow.CardResponseCompleted||r.Counterpart!=c.TargetSeat)||
            r.Stage!=DingShangWanStage.FengChoice&&(r.Branch is not("owner-draw" or "other-draw")||ProgramEventHistory<DingShangWanBranchEvent>().Count(e=>e.FrameId==f.Id&&e.Branch==r.Branch&&e.Recipient==r.Counterpart)!=1))
            throw new InvalidOperationException("Fengyan lost its real foreign damage/response counterpart and committed option.");
        if(f.PendingMovementContinuation is not null)
        {
            if(ProgramEventHistory<DingShangWanMovementIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Stage==r.Stage&&e.Payer==r.Payer&&e.Recipient==r.Recipient&&e.Before==r.Before&&e.After==r.After&&e.Count==r.ActualCount)!=1||
                _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&(!IsDingShangWanDrawStage(r.Stage)||m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==DingShangWanDrawReason))!=r.ActualCount||
                r.Reshuffles.Select(s=>s.BatchId).Distinct().Count()!=r.Reshuffles.Count||r.Reshuffles.Any(s=>s.ParentFrameId!=f.Id||s.Movements.Count==0||s.Before<r.Before||s.After>r.After||s.After<=s.Before||
                    s.Before!=s.Movements[0].Sequence-1||s.After!=s.Movements[^1].Sequence||s.Movements.Any(m=>m.Sequence<=s.Before||m.Sequence>s.After||m.From!=CardLocation.DiscardPile||m.To!=CardLocation.DrawPile||m.Reason!=CardMoveReasons.Reshuffle||!_cardMovements.Contains(m))||
                    ProgramEventHistory<DingShangWanReshuffleCapturedEvent>().Count(e=>e.FrameId==f.Id&&e.BatchId==s.BatchId&&e.Before==s.Before&&e.After==s.After&&e.Count==s.Movements.Count)!=1)||
                _cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsDingShangWanIssuedMovement(f,m)))
                throw new InvalidOperationException("Ding Shang Wan lost its once-issued real native movement interval.");
        }
    }
    private bool IsDingShangWanIssuedMovement(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r=f.DingShangWan!;
        if(m.Sequence<=r.Before||m.Sequence>r.After||!_cardMovements.Contains(m))return false;
        if(IsDingShangWanDrawStage(r.Stage)&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle&&
            r.Reshuffles.Any(s=>s.ParentFrameId==f.Id&&s.Movements.Contains(m)&&ProgramEventHistory<DingShangWanReshuffleCapturedEvent>().Any(e=>e.FrameId==f.Id&&e.BatchId==s.BatchId&&e.Before==s.Before&&e.After==s.After&&e.Count==s.Movements.Count)))return true;
        if(r.Stage is DingShangWanStage.FengGiftChildren or DingShangWanStage.FengDiscardChildren&&
            r.WoodenOxGrainIds.Contains(m.CardId)&&m.From==CardLocation.WoodenOxGrain(r.Payer)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard&&
            _cardMovements.Any(ox=>ox.Sequence>r.Before&&ox.Sequence<=r.After&&ox.CardKind==CardKind.WoodenOx&&r.Selected.Contains(ox.CardId)&&
                ox.From==CardLocation.Equipment(r.Payer)&&ox.Reason.Value==(r.Stage==DingShangWanStage.FengGiftChildren?FengyanGiftReason:FengyanDiscardReason)))return true;
        return r.Stage switch
        {
            DingShangWanStage.OpeningDraw or DingShangWanStage.PairOwnerDraw or DingShangWanStage.PairTargetDraw or DingShangWanStage.FengOwnerDraw or DingShangWanStage.FengTargetDraw=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==DingShangWanDrawReason,
            DingShangWanStage.OpeningGiftChildren=>r.Selected.Contains(m.CardId)&&m.From==CardLocation.Hand(f.OwnerSeat)&&m.To==CardLocation.Hand(r.Recipient!.Value)&&m.Reason.Value==FudaoGiftReason,
            DingShangWanStage.OpeningDiscardChildren=>r.Selected.Contains(m.CardId)&&m.From==CardLocation.Hand(f.OwnerSeat)&&m.To==CardLocation.DiscardPile&&m.Reason.Value==FudaoDiscardReason,
            DingShangWanStage.FengGiftChildren=>r.Selected.Contains(m.CardId)&&m.From.OwnerSeat==f.OwnerSeat&&m.From.Zone is(CardZoneKind.Hand or CardZoneKind.Equipment)&&m.To==CardLocation.Hand(r.Counterpart!.Value)&&m.Reason.Value==FengyanGiftReason,
            DingShangWanStage.FengDiscardChildren=>r.Selected.Contains(m.CardId)&&m.From.OwnerSeat==r.Counterpart&&m.From.Zone is(CardZoneKind.Hand or CardZoneKind.Equipment)&&m.To==CardLocation.DiscardPile&&m.Reason.Value==FengyanDiscardReason,
            _=>false
        };
    }
    private bool DingShangWanSilverLion(ProgramSkillFrame f)=>f.DingShangWan is{Stage:DingShangWanStage.FengGiftChildren or DingShangWanStage.FengDiscardChildren}r&&
        _cardMovements.Any(m=>IsDingShangWanIssuedMovement(f,m)&&m.From==CardLocation.Equipment(r.Payer)&&m.CardKind==CardKind.SilverLion);
    private bool IsDingShangWanRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.DingShangWan is{}r&&DingShangWanSilverLion(f)&&
        RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&
        recovery.Attempt.SourceSeat==r.Payer&&recovery.Attempt.TargetSeat==r.Payer&&recovery.Attempt.Amount==1&&
        recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&
        recovery.Attempt.Completion.MoveReason?.Value==(r.Stage==DingShangWanStage.FengGiftChildren?FengyanGiftReason:FengyanDiscardReason);
    private bool DingShangWanFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.DingShangWan is not{}r)return false;
        AssertDingShangWan(f);
        if(r.Stage==DingShangWanStage.OpeningDraw&&f.NativeDraw is not null)return NativeDrawAttemptRidesOn(child,f);
        if(f.PendingMovementContinuation is null)return false;
        if(child is CardsMovedTriggerWindowFrame moved)return ProgramMovementReturnMatches(f,moved)&&
            moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&
            moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsDingShangWanIssuedMovement(f,m))&&
            (moved.Batch.Movements.All(m=>m.Reason!=CardMoveReasons.Reshuffle)||moved.Batch.ParentBatchId is null&&r.Reshuffles.Any(s=>s.BatchId==moved.Batch.Id&&s.ParentFrameId==moved.Batch.ParentFrameId&&s.Movements.SequenceEqual(moved.Batch.Movements)));
        if(child is RecoveryReplacementFrame recovery)return IsDingShangWanRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&
            hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount==1&&hp.Change.SourceSeat==r.Payer&&hp.Change.TargetSeat==r.Payer&&DingShangWanSilverLion(f);
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&state.ResumeProgramFrameId==f.Id&&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id&&e.Change.TargetSeat==state.OwnerSeat&&e.Change.Window==state.Window);
    }
    private ProgramSkillFrame? DingShangWanObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.DingShangWanFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,
        static(engine,c)=>engine.IsMovementObserverEdge(c)||engine.IsYanPuDyingExitEdge(c)||engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[c],engine._resolutionStack[c-1])||NativeDrawAttemptRidesOn(engine._resolutionStack[c],engine._resolutionStack[c-1]),static(engine,c,d)=>engine.IsSuspendedOrPaidRescueObserverSuffix(c,d)));
    private bool HasDingShangWanDamageObserver(long id)=>DingShangWanObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool IsDingShangWanDying()=>ActiveDying is{}d&&DingShangWanObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsDingShangWanNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||DingShangWanObserverRoot()is not{}root||root.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,26700)]private bool AdvanceDingShangWanSubtree()=>_pendingDecision is null&&DingShangWanObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
