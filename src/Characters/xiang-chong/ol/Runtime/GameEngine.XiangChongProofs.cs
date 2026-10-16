namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsXiangChongClaimedUseMaterial(long useId,int cardId)
    {
        if(LifecycleCardUse(useId)is not{Action:{Type:CardActionType.Use}a}u)return false;
        return IsXiangChongEquipmentMaterialClaim(useId,cardId,a)||ProgramEventHistory<XiangChongUseMaterialClaimedEvent>().Any(e=>e.UseId==useId&&e.ActionId==a.ActionId&&e.CardId==cardId&&e.Source.SkillId=="ol:guying"&&e.Source.OwnerSeat==e.RecipientSeat&&
            _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash==e.GameplayHash&&a.PhysicalCards.Any(c=>c.CardId==cardId&&c.CardKind==e.Kind&&_cardMovements.Any(m=>m.Sequence==e.LossSequence&&m.CardId==cardId&&m.CardKind==e.Kind&&m.From==c.From&&m.To==CardLocation.Processing))&&
            _cardMovements.Any(m=>m.Sequence==e.ClaimSequence&&m.CardId==cardId&&m.CardKind==e.Kind&&m.From==CardLocation.Processing&&m.To==CardLocation.Hand(e.RecipientSeat)&&m.Reason.Value==XiangReturn)&&
            ProgramEventHistory<XiangChongGuyingInvokedEvent>().Count(x=>x.FrameId==e.ProgramFrameId&&x.OwnerSeat==e.RecipientSeat&&x.Source==e.Source&&x.CardId==cardId&&x.LossSequence==e.LossSequence)==1&&
            _cardMovements.FirstOrDefault(m=>m.CardId==cardId&&m.Sequence>e.LossSequence)?.Sequence==e.ClaimSequence);
    }
    private bool ValidXiang(ProgramSkillFrame f)
    {
        if(f.XiangChong is not{}r||r.Source!=XiangSource(f)||r.GameplayHash!=f.GameplayHash||r.InstructionIndex!=f.InstructionIndex||f.InstructionIndex!=1||r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||!Enum.IsDefined(r.Stage)||r.Branch is not("loss"or"debt"or"gift"or"place")||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||
           r.Before<0||r.After<r.Before||r.After>_movementSequence||r.Selected.Count>r.Required||r.Selected.Select(c=>c.CardId).Distinct().Count()!=r.Selected.Count||r.Selected.Any(c=>!r.Cards.Contains(c))||
           ProgramEventHistory<XiangChongStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var started]||started.Source!=r.Source||started.GameplayHash!=r.GameplayHash||started.ActualTurn!=r.ActualTurn||started.TurnOwner!=r.TurnOwner||started.Branch!=r.Branch||started.PhaseToken!=r.PhaseToken||started.Required!=r.Required||started.LossBatchId!=r.LossBatchId||started.LossSequence!=r.LossSequence)return false;
        var e=GetPausedPrivateOfferEffect(f);
        if(r.Branch=="loss")
        {
            if(f.SkillId!="ol:guying"||e?.Op!=SkillProgramEffectOp.ResolveGuyingAtomicLossChoice||f.WindowContext is not{Window:SkillProgramTriggerWindow.CardsMoved,MovementBatch:{}b}||b.Id!=r.LossBatchId||b.TurnNumber!=r.ActualTurn||b.MovementTiming?.ActualTurnOwnerSeat!=r.TurnOwner||r.TurnOwner==f.OwnerSeat||r.Lost is not{}lost||r.LossDestination is not{}to||
               b.Movements.Where(m=>m.From.OwnerSeat==f.OwnerSeat&&m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment&&m.From!=m.To).ToArray()is not[var movement]||movement.Sequence!=r.LossSequence||movement.CardId!=lost.CardId||movement.From!=lost.From||movement.CardKind!=lost.Kind||movement.To!=to||!_cardMovements.Contains(movement)||
               ProgramEventHistory<XiangChongGuyingInvokedEvent>().Count(x=>x.FrameId==f.Id&&x.OwnerSeat==f.OwnerSeat&&x.ActualTurn==r.ActualTurn&&x.BatchId==b.Id&&x.LossSequence==r.LossSequence&&x.CardId==lost.CardId&&x.Source==r.Source)!=1)return false;
        }
        else if(r.Branch=="debt")
        {
            if(f.SkillId!="ol:guying"||e?.Op!=SkillProgramEffectOp.SettleGuyingPreparationDebt||f.WindowContext?.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow||f.OwnerSeat!=r.TurnOwner||
               ProgramEventHistory<XiangChongDebtConsumedEvent>().Where(x=>x.FrameId==f.Id).ToArray()is not[var debt]||debt.OwnerSeat!=f.OwnerSeat||debt.Count<r.Required||debt.Count<1)return false;
        }
        else
        {
            if(f.SkillId!="ol:muzhen"||f.TriggerId is not null||e?.Op!=SkillProgramEffectOp.ResolveMuzhenExchange||e.StateId!=r.Branch||f.OwnerSeat!=r.TurnOwner||r.PhaseToken<=0||
               !ProgramEventHistory<ActualDiscardRecoveryPhaseStartedEvent>().Any(x=>x.Phase.Token==r.PhaseToken&&x.Phase.ActorSeat==f.OwnerSeat&&x.Phase.TurnNumber==r.ActualTurn&&x.Phase.Kind==ActualDiscardRecoveryPhaseKind.Play)||r.Required!=(r.Branch=="gift"?2:1))return false;
            var paid=ProgramEventHistory<XiangChongBranchPaidEvent>().Where(x=>x.FrameId==f.Id).ToArray();
            if(r.Paid?(paid is not[var p]||p.OwnerSeat!=f.OwnerSeat||p.RecipientSeat!=r.Recipient||p.Branch!=r.Branch||p.PhaseToken!=r.PhaseToken):paid.Length!=0)return false;
        }
        if(r.Stage is XiangChongStage.PaymentChildren or XiangChongStage.ClaimChildren)
        {
            if(!r.Paid||r.After<=r.Before||r.Payments.Count==0)return false;
            foreach(var c in r.Payments)
            {
                var moved=_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==c.CardId&&m.From==c.From).ToArray();
                if(moved.Length!=1||!ProgramEventHistory<XiangChongEntityMovedEvent>().Any(x=>x.FrameId==f.Id&&x.CardId==c.CardId&&x.Kind==c.Kind&&x.From==c.From&&x.To==moved[0].To&&x.Sequence==moved[0].Sequence))return false;
            }
        }
        if(r.Equipment is{}o&&(o.ProgramFrameId!=f.Id||o.ActorSeat!=f.OwnerSeat||o.CardId!=r.Lost?.CardId||o.CardKind!=r.Lost.Kind||o.From!=r.LossDestination||o.CardUseFrameId<=f.Id||
            ProgramEventHistory<XiangChongEquipmentIssuedEvent>().Count(x=>x.Origin==o)!=1))return false;
        if(r.MaterialClaim is{}claim&&(claim.Equipment!=r.Equipment||claim.Source!=r.Source||claim.GameplayHash!=r.GameplayHash||claim.LossBatchId!=r.LossBatchId||claim.LossSequence!=r.LossSequence||claim.OriginalFrom!=r.Lost?.From||!ValidXiangEquipmentMaterialInvoice(claim)))return false;
        if(r.NativeResult is{}result&&(result.Origin!=r.Equipment||!ValidXiangEquipmentResult(result)||r.Replaced?.CardId!=result.ReplacedCardId))return false;
        return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27140)]private void AssertXiang(ProgramSkillFrame f)
    {
        if(f.XiangChong is null)return;if(!ValidXiang(f))throw new InvalidOperationException("XiangChong lost its original actual loss/phase, source generation, frozen material or once-paid child invoice.");
        var r=f.XiangChong;if(_resolutionStack.LastOrDefault()?.Id==f.Id&&_pendingDecision is not null&&r.Stage is XiangChongStage.Choice or XiangChongStage.Targets or XiangChongStage.Cards or XiangChongStage.ClaimChoice)
        {
            var actor=r.Stage==XiangChongStage.Choice?r.TurnOwner:f.OwnerSeat;
            if(_pendingDecision is not{Kind:DecisionKind.ProgramTrigger,IsPrivate:true}p||p.PlayerSeat!=actor||p.SkillPrompt?.SkillId!=f.SkillId||!AssistedChoicesEqual(p.Choices,XiangChoices(f)))throw new InvalidOperationException("XiangChong's private chooser or exact legal payment frontier changed.");
        }
    }
    private bool IsXiangChongRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>f.XiangChong is{}r&&ValidXiang(f)&&XiangSilverLionSeats(r).Contains(recovery.Attempt.TargetSeat)&&recovery.ParentFrameId==f.Id&&recovery.Return==new RecoveryReplacementReturn(PostEventContinuation.Program,f.Id)&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.SourceSeat==recovery.Attempt.TargetSeat&&recovery.Attempt.Amount==1&&
        _cardMovements.Any(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardKind==CardKind.SilverLion&&m.From==CardLocation.Equipment(recovery.Attempt.TargetSeat)&&m.Reason==recovery.Attempt.Completion.MoveReason);
    private static IEnumerable<int> XiangSilverLionSeats(XiangChongReceipt r)=>r.Payments.Concat(r.Replaced is{}p?[p]:Array.Empty<XiangChongCard>()).Where(c=>c.Kind==CardKind.SilverLion&&c.From.Zone==CardZoneKind.Equipment&&c.From.OwnerSeat is not null).Select(c=>c.From.OwnerSeat!.Value);
    private bool XiangFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.XiangChong is not{Paid:true}r||!ValidXiang(f))return false;
        if(child is CardUseFrame use)return r.Stage==XiangChongStage.EquipmentUse&&r.Equipment is{}o&&use.Id==o.CardUseFrameId&&use.XiangChongGuyingEquipment==o&&use.CardId==o.CardId&&use.CardKind==o.CardKind&&ShownEntityUseActorMatches(use,o.ActorSeat,o.ActorSeat);
        if(child is RecoveryReplacementFrame recovery)return IsXiangChongRecoveryParent(f,recovery);
        if(child is HpChangedTriggerWindowFrame hp)return r.Stage is XiangChongStage.PaymentChildren or XiangChongStage.ClaimChildren&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.ParentFrameId==f.Id&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.Amount==1&&hp.Change.SourceSeat==hp.Change.TargetSeat&&XiangSilverLionSeats(r).Contains(hp.Change.TargetSeat);
        if(child is ProgramLifecycleTriggerWindowFrame skills)return skills.Window==SkillProgramTriggerWindow.SkillsChanged&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        if(child is not CardsMovedTriggerWindowFrame m||m.ResumeProgramFrameId!=f.Id||m.Batch.OriginOwnerSeat!=f.OwnerSeat||m.Batch.OriginSkillId!=f.SkillId||m.Batch.OriginSkillInstanceId!=f.SkillInstanceId||m.Batch.Movements.Count==0)return false;
        if(r.Stage==XiangChongStage.NativeChildren&&r.Equipment is{}used&&r.NativeResult is{}result)return m.Batch.ParentFrameId==used.CardUseFrameId&&m.Batch.Movements.All(x=>_cardMovements.Contains(x)&&x.Sequence>r.Before&&x.Sequence<=r.After&&
            (x.CardId==used.CardId&&(x.From==used.From&&x.To==CardLocation.Processing&&x.Reason==CardMoveReasons.EquipmentUse||x.From==CardLocation.Processing&&x.To==CardLocation.Equipment(result.ActorSeat)&&x.Reason==CardMoveReasons.EquipmentEnter)||
             x.CardId==used.CardId&&result.Termination is{}terminal&&x.Sequence==terminal.Sequence&&x.From==CardLocation.Processing&&x.To==CardLocation.DiscardPile&&x.Reason.Value==terminal.Reason&&ValidXiangEquipmentTermination(result,terminal)||
             r.Replaced is{}old&&x.CardId==old.CardId&&x.From==old.From&&x.Reason==CardMoveReasons.EquipmentReplace&&x.To==(GetAdvancedCard(x.CardId).IsGeneralWeapon?CardLocation.OutsideGame:CardLocation.DiscardPile)||
             r.Auxiliaries.Any(c=>c.CardId==x.CardId&&c.From==x.From)&&x.To==CardLocation.DiscardPile&&x.Reason==CardMoveReasons.WoodenOxGrainDiscard));
        if(m.Batch.ParentFrameId!=f.Id)return false;
        if(r.Stage is not(XiangChongStage.PaymentChildren or XiangChongStage.ClaimChildren))return false;
        return m.Batch.Movements.All(x=>_cardMovements.Contains(x)&&x.Sequence>r.Before&&x.Sequence<=r.After&&
            (ProgramEventHistory<XiangChongEntityMovedEvent>().Any(e=>e.FrameId==f.Id&&e.Sequence==x.Sequence&&e.CardId==x.CardId&&e.Kind==x.CardKind&&e.From==x.From&&e.To==x.To)||
             r.Auxiliaries.Any(c=>c.CardId==x.CardId&&c.From==x.From)&&x.To==CardLocation.DiscardPile&&x.Reason==CardMoveReasons.WoodenOxGrainDiscard));
    }
    private bool XiangEquipmentObserverEdge(int child)
    {
        var current=_resolutionStack[child];var parent=_resolutionStack[child-1];
        if(parent is CardUseFrame{XiangChongGuyingEquipment:{}o,Action:{}a}use&&_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==o.ProgramFrameId)is{}root&&MatchesXiangChongEquipment(root,o))
            return current is ProgramCardTriggerWindowFrame w&&w.ParentFrameId==use.Id&&w.Action.Type==CardActionType.Use&&w.Action.ActionId==a.ActionId&&w.Action.PhysicalCards.SequenceEqual(a.PhysicalCards);
        return parent is ProgramCardTriggerWindowFrame window&&current is ProgramSkillFrame p&&p.WindowContext?.ParentFrameId==window.Id&&LifecycleCardUse(window.ParentFrameId)is{XiangChongGuyingEquipment:{}origin}&&
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==origin.ProgramFrameId)is{}owner&&MatchesXiangChongEquipment(owner,origin)&&window.CandidateIndex>=0&&window.CandidateIndex<window.Candidates.Count&&MountObserverCandidateMatches(p,ToSharedCandidate(window.Candidates[window.CandidateIndex]))&&CreateCardActionProgramContext(window,window.Candidates[window.CandidateIndex])==p.WindowContext;
    }
    private ProgramSkillFrame? XiangObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.XiangFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(x,n)=>x.XiangEquipmentObserverEdge(n)||x.IsMovementObserverEdge(n)||RecoveryReplacementFrameRidesOn(x._resolutionStack[n],x._resolutionStack[n-1]),static(x,n,d)=>x.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    private bool HasXiangChongDamageObserver(long id)=>XiangObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasXiangChongBeforeDamageObserver(long id)=>HasXiangChongDamageObserver(id);
    private bool IsXiangChongDying()=>ActiveDying is{}d&&XiangObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsXiangChongNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||XiangObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27140)]private bool AdvanceXiangSubtree()=>_pendingDecision is null&&XiangObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
