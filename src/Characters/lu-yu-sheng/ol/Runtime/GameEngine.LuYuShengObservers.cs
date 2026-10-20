namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,26900)]
    private void AssertLuYuSheng(ProgramSkillFrame f)
    {
        if(f.LuYuSheng is not{}r)return;
        if(f.InstructionIndex!=1||f.TriggerId is null||r.Source!=LuYuShengSource(f)||r.GameplayHash!=f.GameplayHash||f.WindowContext is not{}c||r.WindowId!=c.ParentFrameId||r.Window!=c.Window||GetPausedPrivateOfferEffect(f)?.Op!=r.Operation||!LuYuShengComposition.IsOperation(r.Operation)||
            !Enum.IsDefined(r.Stage)||r.Turn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||!IsValidPlayerSeat(r.Payer)||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.Count<0||r.Count>3||r.ActualCount<0||
            r.BottomCards.Count>3||r.BottomCards.Distinct().Count()!=r.BottomCards.Count||r.Selected.Distinct().Count()!=r.Selected.Count||r.Selected.Any(id=>!r.BottomCards.Contains(id))||r.WoodenOxGrainIds.Distinct().Count()!=r.WoodenOxGrainIds.Count||
            !LuYuShengParent(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,c.OccurrenceIndex),c)||
            ProgramEventHistory<LuYuShengStartedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.WindowId==r.WindowId&&e.Operation==r.Operation&&e.Turn==r.Turn&&e.TurnOwner==r.TurnOwner&&e.Payer==r.Payer&&e.AttackId==r.AttackId&&e.Amount==r.OriginalDamage)!=1||
            ProgramEventHistory<LuYuShengBottomRevealedEvent>().Where(e=>e.FrameId==f.Id).OrderBy(e=>e.Index).Select(e=>e.CardId).SequenceEqual(r.BottomCards)==false)
            throw new InvalidOperationException("Lu Yu Sheng lost its exact owning instruction, native candidate, original public bottom entities or start fact.");
        if(r.Operation==SkillProgramEffectOp.DiscardBottomHeartsForDamageReduction)
        {
            if(r.Payer!=f.OwnerSeat||r.AttackId is null||r.OriginalDamage<=0||r.Stage is not(LuYuShengStage.BottomChoice or LuYuShengStage.BottomDiscardChildren)||r.Branch is not null||r.CardId is not null||r.From is not null||
                ProgramEventHistory<LuYuShengStartedEvent>().Count(e=>e.Source.OwnerSeat==f.OwnerSeat&&e.Turn==r.Turn&&e.Operation==r.Operation)!=1)throw new InvalidOperationException("Cangxin lost its once-per-actual-turn before-damage opportunity.");
        }
        else if(r.Operation==SkillProgramEffectOp.RevealBottomHeartsAndDraw)
        {if(r.Payer!=f.OwnerSeat||r.AttackId is not null||r.Stage!=LuYuShengStage.DrawChildren||r.Count!=r.BottomCards.Count(id=>GetAdvancedCard(id).Suit==Suit.Heart)||r.Branch is not null)throw new InvalidOperationException("Cangxin Draw lost its original shown Heart count.");}
        else if(r.Phase is not{Kind:ActualDiscardRecoveryPhaseKind.Discard}phase||!IsRecordedActualDiscardRecoveryPhase(phase)||phase.ActorSeat!=r.Payer||phase.TurnNumber!=r.Turn||r.BottomCards.Count!=0||r.Selected.Count!=0||r.AttackId is not null||
            r.Stage is not(LuYuShengStage.RunweiChoice or LuYuShengStage.RunweiDiscardChoice or LuYuShengStage.RunweiDiscardChildren or LuYuShengStage.DrawChildren)||
            (r.Stage==LuYuShengStage.RunweiChoice?r.Branch is not null:r.Branch is not("draw" or"discard")||ProgramEventHistory<LuYuShengRunweiChosenEvent>().Count(e=>e.FrameId==f.Id&&e.Payer==r.Payer&&e.Branch==r.Branch)!=1))throw new InvalidOperationException("Runwei lost its accepted actual Discard phase and frozen participant/direction.");
        if(f.PendingMovementContinuation is not null)
        {
            if(ProgramEventHistory<LuYuShengMovementIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.Stage==r.Stage&&e.Payer==r.Payer&&e.Before==r.Before&&e.After==r.After&&e.Count==r.ActualCount)!=1||
                _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&(r.Stage!=LuYuShengStage.DrawChildren||m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==LuYuShengDrawReason))!=r.ActualCount||
                r.Reshuffles.Select(s=>s.BatchId).Distinct().Count()!=r.Reshuffles.Count||r.Reshuffles.Any(s=>s.ParentFrameId!=f.Id||s.Movements.Count==0||s.Before<r.Before||s.After>r.After||s.Before!=s.Movements[0].Sequence-1||s.After!=s.Movements[^1].Sequence||
                    s.Movements.Any(m=>m.Sequence<=s.Before||m.Sequence>s.After||m.From!=CardLocation.DiscardPile||m.To!=CardLocation.DrawPile||m.Reason!=CardMoveReasons.Reshuffle||!_cardMovements.Contains(m))||ProgramEventHistory<LuYuShengReshuffleCapturedEvent>().Count(e=>e.FrameId==f.Id&&e.BatchId==s.BatchId&&e.Before==s.Before&&e.After==s.After&&e.Count==s.Movements.Count)!=1)||
                _cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).Any(m=>!IsLuYuShengIssuedMovement(f,m)))throw new InvalidOperationException("Lu Yu Sheng lost its once-issued native draw/discard interval or exact shuffle/Ox attachment.");
        }
        if(ReferenceEquals(f,_resolutionStack.LastOrDefault())&&(r.Stage is LuYuShengStage.BottomChoice or LuYuShengStage.RunweiChoice or LuYuShengStage.RunweiDiscardChoice)&&
            (_pendingDecision is not{Kind:DecisionKind.ProgramTrigger,IsPrivate:true}p||p.PlayerSeat!=LuYuShengChooser(f)||!AssistedChoicesEqual(p.Choices,LuYuShengChoices(f))))throw new InvalidOperationException("Lu Yu Sheng lost its source/payer-private exact choices.");
    }
    private bool IsLuYuShengIssuedMovement(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r=f.LuYuSheng!;if(m.Sequence<=r.Before||m.Sequence>r.After||!_cardMovements.Contains(m))return false;
        return r.Stage switch
        {
            LuYuShengStage.DrawChildren=>m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Payer)&&m.Reason.Value==LuYuShengDrawReason||m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle&&r.Reshuffles.Any(s=>s.ParentFrameId==f.Id&&s.Movements.Contains(m)),
            LuYuShengStage.BottomDiscardChildren=>r.Selected.Contains(m.CardId)&&m.From==CardLocation.DrawPile&&m.To==CardLocation.DiscardPile&&m.Reason.Value==LuYuShengBottomDiscardReason,
            LuYuShengStage.RunweiDiscardChildren=>m.CardId==r.CardId&&m.From==r.From&&m.To==(m.CardKind==CardKind.GeneralWeapon&&m.From.Zone==CardZoneKind.Equipment?CardLocation.OutsideGame:CardLocation.DiscardPile)&&m.Reason.Value==LuYuShengRunweiDiscardReason||
                r.WoodenOxGrainIds.Contains(m.CardId)&&m.From==CardLocation.WoodenOxGrain(r.Payer)&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard&&_cardMovements.Any(ox=>ox.Sequence>r.Before&&ox.Sequence<=r.After&&ox.CardId==r.CardId&&ox.CardKind==CardKind.WoodenOx&&ox.From==CardLocation.Equipment(r.Payer)&&ox.Reason.Value==LuYuShengRunweiDiscardReason),
            _=>false
        };
    }
    private bool LuYuShengSilverLion(ProgramSkillFrame f)=>f.LuYuSheng is{Stage:LuYuShengStage.RunweiDiscardChildren}r&&_cardMovements.Any(m=>IsLuYuShengIssuedMovement(f,m)&&m.CardId==r.CardId&&m.CardKind==CardKind.SilverLion&&m.From==CardLocation.Equipment(r.Payer));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 730)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 670)]
    private bool IsLuYuShengRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.LuYuSheng is{}r&&LuYuShengSilverLion(f)&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.SourceSeat==r.Payer&&recovery.Attempt.TargetSeat==r.Payer&&recovery.Attempt.Amount==1&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==LuYuShengRunweiDiscardReason;
    private bool LuYuShengFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.LuYuSheng is not{}r||f.PendingMovementContinuation is null)return false;AssertLuYuSheng(f);
        if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==f.Id&&(moved.ResumeProgramFrameId==f.Id||moved.Batch.AwaitingProgramFrameId==f.Id)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(m=>IsLuYuShengIssuedMovement(f,m))&&
            (moved.Batch.Movements.All(m=>m.Reason!=CardMoveReasons.Reshuffle)||moved.Batch.ParentBatchId is null&&r.Reshuffles.Any(s=>s.BatchId==moved.Batch.Id&&s.ParentFrameId==moved.Batch.ParentFrameId&&s.Movements.SequenceEqual(moved.Batch.Movements)));
        if(child is RecoveryReplacementFrame recovery)return IsLuYuShengRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount==1&&hp.Change.SourceSeat==r.Payer&&hp.Change.TargetSeat==r.Payer&&LuYuShengSilverLion(f);
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        return child is ProgramLifecycleTriggerWindowFrame state&&state.Continuation==ProgramLifecycleContinuation.ResumeCharacterStateChange&&state.ResumeProgramFrameId==f.Id&&ProgramEventHistory<CharacterStateChangedEvent>().Any(e=>e.Change.Id==state.Id&&e.Change.ParentFrameId==f.Id&&e.Change.TargetSeat==state.OwnerSeat&&e.Change.Window==state.Window);
    }
    private ProgramSkillFrame? LuYuShengObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.LuYuShengFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(engine,c)=>engine.IsMovementObserverEdge(c)||engine.IsYanPuDyingExitEdge(c)||engine.RecoveryReplacementFrameRidesOn(engine._resolutionStack[c],engine._resolutionStack[c-1]),static(engine,c,d)=>engine.IsSuspendedOrPaidRescueObserverSuffix(c,d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 540)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 650)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 680)]
    private bool HasLuYuShengDamageObserver(long id)=>LuYuShengObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 500)]
    private bool IsLuYuShengDying()=>ActiveDying is{}d&&LuYuShengObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1140)]
    private bool AllowsLuYuShengNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||LuYuShengObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,26900)]private bool AdvanceLuYuShengSubtree()=>_pendingDecision is null&&LuYuShengObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
