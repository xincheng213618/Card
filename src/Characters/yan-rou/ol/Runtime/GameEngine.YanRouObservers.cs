namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,26800)]
    private void AssertYanRou(ProgramSkillFrame f)
    {
        if(f.YanRou is not{}r)return;
        if(f.TriggerId is null||f.InstructionIndex!=1||r.Source!=YanRouSource(f)||r.GameplayHash!=f.GameplayHash||f.WindowContext is not{}c||c.ParentFrameId!=r.WindowId||c.Window!=r.Window||
            GetPausedPrivateOfferEffect(f)?.Op!=r.Operation||!YanRouComposition.IsOperation(r.Operation)||r.Turn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||!Enum.IsDefined(r.Stage)||
            !IsValidPlayerSeat(r.Payer)||r.Amount<0||r.Amount>5||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.ActualCount<0||r.WoodenOxGrainIds.Distinct().Count()!=r.WoodenOxGrainIds.Count||
            !YanRouParent(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),c)||
            ProgramEventHistory<YanRouStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&e.Window==r.Window&&e.Operation==r.Operation&&e.Turn==r.Turn&&e.TurnOwner==r.TurnOwner&&
                e.Payer==(r.UseId is null?f.OwnerSeat:r.Payer)&&e.Amount==r.Amount&&e.UseId==r.UseId&&e.ActionId==r.ActionId)!=1)
            throw new InvalidOperationException("Yan Rou lost its exact owning instruction, native candidate, start fact or actual turn.");
        if(r.Operation==SkillProgramEffectOp.DiscardSlashUserForUncancelable)
        {
            if(r.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||r.UseId!=c.CardUse?.ParentCardUseFrameId||r.ActionId!=c.CardUse?.CardActionId||r.Stage is not(YanRouStage.DiscardChoice or YanRouStage.DiscardChildren)||r.Amount!=0||r.Target is not null||
                r.Stage==YanRouStage.DiscardChildren&&(r.CardId is null||r.From is null)||r.Debit is{}debit&&(debit.CardActionId!=r.ActionId||!IsValidPlayerSeat(debit.ActorSeat)||debit!=GetCardUseDebit(r.ActionId!.Value)))
                throw new InvalidOperationException("Choutao lost its accepted native Slash and original actual debit identity.");
        }
        else if(r.Window!=SkillProgramTriggerWindow.TurnEnding||r.TurnOwner!=f.OwnerSeat||r.UseId is not null||r.ActionId is not null||r.Debit is not null||r.CardId is not null||r.From is not null||
            r.Stage is not(YanRouStage.TargetChoice or YanRouStage.RecoveryChildren or YanRouStage.DrawChildren)||
            (r.Stage==YanRouStage.TargetChoice?YanRouSpent(f.OwnerSeat,f.SkillId,f.GameplayHash):r.Target is not{}target||r.Payer!=target||
             ProgramEventHistory<YanRouXiangshuSpentEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&e.Turn==r.Turn&&e.Target==target&&e.Amount==r.Amount)!=1))
            throw new InvalidOperationException("Xiangshu lost its frozen actual-turn amount or permanent paid limited fact.");
        if(f.PendingMovementContinuation is not null)
        {
            if(ProgramEventHistory<YanRouMovementIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Stage==r.Stage&&e.Payer==r.Payer&&e.CardId==r.CardId&&e.From==r.From&&e.Before==r.Before&&e.After==r.After&&e.Count==r.ActualCount)!=1||
                _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&(r.Stage!=YanRouStage.DrawChildren||m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==YanRouDrawReason))!=r.ActualCount||
                r.Reshuffles.Select(s=>s.BatchId).Distinct().Count()!=r.Reshuffles.Count||r.Reshuffles.Any(s=>s.ParentFrameId!=f.Id||s.Movements.Count==0||s.Before<r.Before||s.After>r.After||s.Before!=s.Movements[0].Sequence-1||s.After!=s.Movements[^1].Sequence||
                    s.Movements.Any(m=>m.Sequence<=s.Before||m.Sequence>s.After||m.From!=CardLocation.DiscardPile||m.To!=CardLocation.DrawPile||m.Reason!=CardMoveReasons.Reshuffle||!_cardMovements.Contains(m))||
                    ProgramEventHistory<YanRouReshuffleCapturedEvent>().Count(e=>e.FrameId==f.Id&&e.BatchId==s.BatchId&&e.Before==s.Before&&e.After==s.After&&e.Count==s.Movements.Count)!=1)||
                _cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsYanRouIssuedMovement(f,m)))
                throw new InvalidOperationException("Yan Rou lost its once-issued native movement interval or exact shuffle/spill attachment.");
        }
        if(ReferenceEquals(f,_resolutionStack.LastOrDefault())&&(r.Stage is YanRouStage.TargetChoice or YanRouStage.DiscardChoice)&&
            (_pendingDecision is not{Kind:DecisionKind.ProgramTrigger,IsPrivate:true}p||p.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual(p.Choices,YanRouChoices(f))))
            throw new InvalidOperationException("Yan Rou lost its exact private choice collection.");
    }
    private bool IsYanRouIssuedMovement(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r=f.YanRou!;if(m.Sequence<=r.Before||m.Sequence>r.After||!_cardMovements.Contains(m))return false;
        if(r.Stage==YanRouStage.DrawChildren)return m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==YanRouDrawReason||
            m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle&&r.Reshuffles.Any(s=>s.ParentFrameId==f.Id&&s.Movements.Contains(m));
        if(r.Stage!=YanRouStage.DiscardChildren)return false;
        return m.CardId==r.CardId&&m.From==r.From&&m.To==(m.CardKind==CardKind.GeneralWeapon&&m.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:CardLocation.DiscardPile)&&m.Reason.Value==YanRouDiscardReason||
            r.WoodenOxGrainIds.Contains(m.CardId)&&m.From==CardLocation.WoodenOxGrain(r.Payer)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard&&
            _cardMovements.Any(ox=>ox.Sequence>r.Before&&ox.Sequence<=r.After&&ox.CardId==r.CardId&&ox.CardKind==CardKind.WoodenOx&&ox.From==CardLocation.Equipment(r.Payer)&&ox.Reason.Value==YanRouDiscardReason);
    }
    private bool YanRouSilverLion(ProgramSkillFrame f)=>f.YanRou is{Stage:YanRouStage.DiscardChildren}r&&_cardMovements.Any(m=>IsYanRouIssuedMovement(f,m)&&m.CardId==r.CardId&&m.From==CardLocation.Equipment(r.Payer)&&m.CardKind==CardKind.SilverLion);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 720)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 660)]
    private bool IsYanRouRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if(f.YanRou is not{}r||!RecoveryReplacementFrameRidesOn(recovery,f)||recovery.Return.Continuation!=PostEventContinuation.AwaitedProgramMovement)return false;
        return r.Stage==YanRouStage.RecoveryChildren&&recovery.Attempt.SourceSeat==f.OwnerSeat&&recovery.Attempt.TargetSeat==r.Target&&recovery.Attempt.Amount==r.Amount&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.Program&&recovery.Attempt.Completion.InstructionIndex==1||
            r.Stage==YanRouStage.DiscardChildren&&YanRouSilverLion(f)&&recovery.Attempt.SourceSeat==r.Payer&&recovery.Attempt.TargetSeat==r.Payer&&recovery.Attempt.Amount==1&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==YanRouDiscardReason;
    }
    private bool YanRouFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.YanRou is not{}r||f.PendingMovementContinuation is null)return false;AssertYanRou(f);
        if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==f.Id&&(moved.ResumeProgramFrameId==f.Id||moved.Batch.AwaitingProgramFrameId==f.Id)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsYanRouIssuedMovement(f,m))&&
            (moved.Batch.Movements.All(m=>m.Reason!=CardMoveReasons.Reshuffle)||moved.Batch.ParentBatchId is null&&r.Reshuffles.Any(s=>s.BatchId==moved.Batch.Id&&s.ParentFrameId==moved.Batch.ParentFrameId&&s.Movements.SequenceEqual(moved.Batch.Movements)));
        if(child is RecoveryReplacementFrame recovery)return IsYanRouRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&
            (r.Stage==YanRouStage.RecoveryChildren&&hp.Change.SourceSeat==f.OwnerSeat&&hp.Change.TargetSeat==r.Target&&hp.Change.Amount>0&&hp.Change.Amount<=r.Amount||
             YanRouSilverLion(f)&&hp.Change.SourceSeat==r.Payer&&hp.Change.TargetSeat==r.Payer&&hp.Change.Amount==1);
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&state.ResumeProgramFrameId==f.Id&&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id&&e.Change.TargetSeat==state.OwnerSeat&&e.Change.Window==state.Window);
    }
    private ProgramSkillFrame? YanRouObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.YanRouFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,
        static(engine,c)=>engine.IsMovementObserverEdge(c)||engine.IsYanPuDyingExitEdge(c)||engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[c],engine._resolutionStack[c-1]),static(engine,c,d)=>engine.IsSuspendedOrPaidRescueObserverSuffix(c,d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 520)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 630)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 660)]
    private bool HasYanRouDamageObserver(long id)=>YanRouObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 490)]
    private bool IsYanRouDying()=>ActiveDying is{}d&&YanRouObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1130)]
    private bool AllowsYanRouNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||YanRouObserverRoot()is not{}root||root.Id==f.Id)return false;
        var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,26800)]private bool AdvanceYanRouSubtree()=>_pendingDecision is null&&YanRouObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
