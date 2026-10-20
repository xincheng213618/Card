namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27420)]private void AssertQuHuang(ProgramSkillFrame f)
    {
        if(f.QuHuang is not{}r)return;
        if(f.InstructionIndex!=1||f.WindowContext is not{}context||r.Source!=QuHuangSource(f)||r.GameplayHash!=f.GameplayHash||!QuHuangComposition.Handles(r.Op)||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||r.ActualTurn!=_turnNumber||r.TurnOwner!=_turnProgression.OwnerSeat||r.ParentId!=context.ParentFrameId||!Enum.IsDefined(r.Stage)||!IsValidPlayerSeat(r.TargetSeat)||r.Before<0||r.After<r.Before||r.After>_movementSequence||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||ProgramEventHistory<QuHuangStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var start]||start.Source!=r.Source||start.GameplayHash!=r.GameplayHash||start.Op!=r.Op||start.ParentId!=r.ParentId||start.ActualTurn!=r.ActualTurn)throw new InvalidOperationException("Qu Huang lost his exact original source, native parent, issued stage or immutable start.");
        if(r.Op==SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap)
        {var c=new ProgramTriggerCandidate(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,GetProgramTrigger(f).Priority,context.OccurrenceIndex);if(!IsExactActualPhaseEndCandidate(c,context)||r.DebtId is not{}debt||!ProgramEventHistory<NishouSwapDebtIssuedEvent>().Any(e=>e.DebtId==debt&&e.Source.SkillId==r.Source.SkillId&&e.Source.BindingId=="equipment-to-discard"&&e.Source.OwnerSeat==r.Source.OwnerSeat&&e.Source.SkillInstanceId==r.Source.SkillInstanceId&&e.GameplayHash==r.GameplayHash&&e.Phase==context.ActualPhaseEnd!.Phase)||ProgramEventHistory<NishouSwapDebtConsumedEvent>().Count(e=>e.DebtId==debt&&e.FrameId==f.Id&&e.BoundaryId==context.ActualPhaseEnd!.BoundaryFrameId)!=1)throw new InvalidOperationException("Nishou lost or repeated its exact current actual-phase debt.");}
        else
        {if(context.MovementBatch is not{}batch||context.MovementIndex is not{}index||index<0||index>=batch.Movements.Count||_resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.ParentId)is not{}parent||parent.Batch.Id!=batch.Id||parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||!MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex]))throw new InvalidOperationException("Qu Huang lost the original actual movement window and exact candidate.");if(r.Op==SkillProgramEffectOp.ResolveQiejianLastHandLoss&&!LostLastHandCard(batch,start.TargetSeat)||r.Op==SkillProgramEffectOp.ResolveNishouEquipmentLossChoice&&(r.Equipment is not{}eq||!ProgramEventHistory<NishouEquipmentLostEvent>().Contains(eq)||eq.BatchId!=batch.Id||eq.OwnerSeat!=f.OwnerSeat))throw new InvalidOperationException("Qu Huang cannot invent an empty-Hand loss or replace the chosen original equipment.");}
        var moving=r.Stage is QuHuangStage.DrawOwner or QuHuangStage.DrawOther or QuHuangStage.FieldChildren or QuHuangStage.HoldOwner or QuHuangStage.HoldOther or QuHuangStage.GiveOwner or QuHuangStage.GiveOther or QuHuangStage.Cleanup or QuHuangStage.LightningChildren;
        if(moving?f.PendingMovementContinuation is not{}p||!IsQuHuangMovement(f,GetPausedPrivateOfferEffect(f),p):f.PendingMovementContinuation is not null)throw new InvalidOperationException("Qu Huang lost the exact once-paid movement participant.");
        if(moving&&r.Stage!=QuHuangStage.LightningChildren)
        {
            if(!r.Paid||r.Step<=0||ProgramEventHistory<QuHuangMovementPaidEvent>().Count(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Stage==r.Stage&&e.Before==r.Before&&e.After==r.After&&e.Count==r.ActualCount)!=1)throw new InvalidOperationException("Qu Huang lost his completed original movement invoice.");
            var facts=ProgramEventHistory<QuHuangEntityMovedEvent>().Where(e=>e.FrameId==f.Id&&e.Step==r.Step).ToArray();if(facts.Any(e=>e.Stage!=r.Stage||e.Sequence<=r.Before||e.Sequence>r.After||!r.Entities.Any(c=>c.CardId==e.CardId&&c.Kind==e.Kind&&c.From==e.From)||!_cardMovements.Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.CardKind==e.Kind&&m.From==e.From&&m.To==e.To))||r.Stage is not(QuHuangStage.DrawOwner or QuHuangStage.DrawOther)&&facts.Length!=r.ActualCount)throw new InvalidOperationException("Qu Huang changed a real paid entity or movement boundary.");
        }
        if(r.OwnerCards.Concat(r.OtherCards).Select(c=>c.CardId).Distinct().Count()!=r.OwnerCards.Count+r.OtherCards.Count)throw new InvalidOperationException("Nishou requires two distinct original physical Hand sets.");
        foreach(var card in r.OwnerCards.Concat(r.OtherCards))if(ProgramEventHistory<NishouOriginalHandFrozenEvent>().Count(e=>e.FrameId==f.Id&&e.HolderSeat==card.From.OwnerSeat&&e.CardId==card.CardId&&e.Kind==card.Kind)!=1)throw new InvalidOperationException("Nishou cannot add a child's newly drawn Hand card to its frozen exchange.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 360)]
    private bool IsQuHuangMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.QuHuang is{Paid:true}r&&e?.Op==r.Op&&p==f.PendingMovementContinuation&&p.SubjectSeat==r.TargetSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool IsNishouHeldProcessingCard(int cardId)=>HasQuHuangCapability&&_cardZones.GetLocation(cardId)==CardLocation.Processing&&_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.QuHuang is{Op:SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap,Paid:true}r&&r.OwnerCards.Concat(r.OtherCards).Any(c=>c.CardId==cardId)&&ProgramEventHistory<QuHuangEntityMovedEvent>().Any(e=>e.FrameId==f.Id&&e.CardId==cardId&&e.To==CardLocation.Processing&&e.Stage is QuHuangStage.HoldOwner or QuHuangStage.HoldOther&&_cardMovements.LastOrDefault(m=>m.CardId==cardId)is{}last&&last.Sequence==e.Sequence&&last.From==e.From&&last.To==e.To&&last.Reason.Value==NishouHoldReason));
    private IReadOnlyList<Card> WithoutNishouHeldProcessingCards(IReadOnlyList<Card> processing)=>!HasQuHuangCapability?processing:Array.AsReadOnly(processing.Where(c=>!IsNishouHeldProcessingCard(c.Id)).ToArray());
    private IReadOnlyList<CardSnapshot> NishouPrivatelyHeldCards(int viewerSeat)=>!HasQuHuangCapability?[]:Array.AsReadOnly(_resolutionStack.OfType<ProgramSkillFrame>().Where(f=>f.QuHuang?.Op==SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap).SelectMany(f=>f.QuHuang!.OwnerCards.Concat(f.QuHuang.OtherCards)).Where(c=>c.From==CardLocation.Hand(viewerSeat)&&IsNishouHeldProcessingCard(c.CardId)).Select(c=>ToSnapshot(GetAdvancedCard(c.CardId))).ToArray());
    private bool QuHuangMovementMatches(ProgramSkillFrame f,CardMovementRecord m)
    {
        var r=f.QuHuang!;if(!_cardMovements.Contains(m)||m.Sequence<=r.Before||m.Sequence>r.After)return false;
        if(r.Stage is QuHuangStage.DrawOwner or QuHuangStage.DrawOther)return m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(r.Stage==QuHuangStage.DrawOwner?f.OwnerSeat:r.TargetSeat)&&m.Reason.Value==QiejianDrawReason||m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle;
        if(ProgramEventHistory<QuHuangEntityMovedEvent>().Any(e=>e.FrameId==f.Id&&e.Step==r.Step&&e.Sequence==m.Sequence&&e.CardId==m.CardId&&e.From==m.From&&e.To==m.To))return true;
        return r.Grain.Contains(m.CardId)&&m.From.Zone==CardZoneKind.WoodenOxGrain&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.WoodenOxGrainDiscard;
    }
    private bool QuHuangLion(ProgramSkillFrame f,int seat)=>f.QuHuang is{Stage:QuHuangStage.FieldChildren}r&&r.Entities.Any(c=>c.Kind==CardKind.SilverLion&&c.From==CardLocation.Equipment(seat));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 160)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 150)]
    private bool IsQuHuangRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)=>QuHuangLion(f,recovery.Attempt.TargetSeat)&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.Amount==1&&recovery.Attempt.SourceSeat==recovery.Attempt.TargetSeat&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&recovery.Attempt.Completion.MoveReason?.Value==QiejianDiscardReason;
    private bool QuHuangFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.QuHuang is not{Paid:true}r)return false;AssertQuHuang(f);
        if(child is CardUseFrame use)return use.QuHuangNishou is{}o&&MatchesQuHuangNishou(f,o)&&IsQuHuangNishouUse(use.Id);
        if(child is CardsMovedTriggerWindowFrame moved)return r.Stage==QuHuangStage.LightningChildren?moved.ResumeProgramFrameId==f.Id&&IsQuHuangNativeSettlementBatch(f,moved.Batch):f.PendingMovementContinuation is not null&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>QuHuangMovementMatches(f,m));
        if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&hp.Change.SourceSeat==hp.Change.TargetSeat&&QuHuangLion(f,hp.Change.TargetSeat);
        if(child is RecoveryReplacementFrame recovery)return IsQuHuangRecoveryParent(f,recovery);
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? QuHuangObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.QuHuangFirstChild(f,c),static(e,i)=>e.HasObserverSuffix(i,static(g,n)=>g.IsMovementObserverEdge(n)||g.RecoveryReplacementFrameRidesOn(g._resolutionStack[n],g._resolutionStack[n-1]),static(g,n,d)=>g.IsSuspendedOrPaidRescueObserverSuffix(n,d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 870)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 980)]
    private bool HasQuHuangDamageObserver(long id)=>QuHuangObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame or BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1010)]
    private bool HasQuHuangBeforeDamageObserver(long id)=>HasQuHuangDamageObserver(id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 840)]
    private bool IsQuHuangDying()=>ActiveDying is{}d&&QuHuangObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1410)]
    private bool AllowsQuHuangNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {if(f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||QuHuangObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}p?ResolveProgramParticipant(f,p):ResolveProgramEffectTarget(f,e.Target));}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27420)]private bool AdvanceQuHuangChildren()=>_pendingDecision is null&&QuHuangObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
