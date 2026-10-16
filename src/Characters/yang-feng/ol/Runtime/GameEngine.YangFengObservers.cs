namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27150)]
    private void AssertYangFeng(ProgramSkillFrame f)
    {
        if(f.YangFeng is not {}r) return;
        if(r.Source!=YangFengSource(f)||r.GameplayHash!=f.GameplayHash||f.InstructionIndex!=1||!YangFengComposition.Handles(r.Op)||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||
            r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||f.OwnerSeat!=r.TurnOwner||!IsValidPlayerSeat(r.TargetSeat)||f.SelectedCardIds.Count!=0||!Enum.IsDefined(r.Stage)||
            r.HistoryCutoff<0||r.HistoryCutoff>CompleteProgramEventHistory().Count()||r.CompletedTurns!=YangFengCompletedTurns(f.OwnerSeat,r.HistoryCutoff)||r.MaximumHp<1||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash||
            ProgramEventHistory<YangFengStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not [var started]||
            started!=new YangFengStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.ActualTurn,r.TurnOwner,r.Phase,r.ParentId,r.TargetSeat,r.HistoryCutoff,r.CompletedTurns,r.MaximumHp,r.X,r.RequiredDiscard))
            throw new InvalidOperationException("Yang Feng lost his exact first instruction, original source, actual turn or frozen history quota.");
        var jiawei=r.Op==SkillProgramEffectOp.ResolveJiaweiDiscardReveal;
        if(jiawei)
        {
            if(f.TriggerId is not null||f.WindowContext is not null||f.SelectedTargetSeats is not [var target]||target!=r.TargetSeat||r.ParentId!=0||r.Phase!=_cardUseDebitPhaseInstanceId||
                r.X!=Math.Min(r.CompletedTurns,r.MaximumHp)||r.RequiredDiscard!=Math.Min(r.X,r.Candidates.Count)||r.QujiaChoice is not null||
                r.Candidates.Select(m=>m.CardId).Distinct().Count()!=r.Candidates.Count||r.Candidates.Any(m=>m.From.OwnerSeat!=target||m.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)||m.Slot<0)||
                r.Selected.Select(m=>m.CardId).Distinct().Count()!=r.Selected.Count||r.Selected.Any(m=>!r.Candidates.Contains(m))||r.Selected.Count>r.RequiredDiscard||
                r.Stage is YangFengStage.QujiaChoice or YangFengStage.QujiaGainChildren||
                (r.Stage==YangFengStage.DiscardChoice?r.Movements.Count!=0||r.Selected.Count>=r.RequiredDiscard||!r.Candidates.SequenceEqual(JiaweiDiscardCandidates(f,target)):r.Selected.Count!=r.RequiredDiscard))
                throw new InvalidOperationException("Jiawei lost its actual Play activation, original HEJ choices or exact frozen completed-turn discard.");
            var selected=ProgramEventHistory<JiaweiMaterialSelectedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(selected.Length!=r.Selected.Count||selected.Where((e,i)=>e.Index!=i||e.Material!=r.Selected[i]).Any())
                throw new InvalidOperationException("Jiawei's original private choices differ from their scalar invoices.");
            var revealed=ProgramEventHistory<JiaweiEntityRevealedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(revealed.Length!=r.Revealed.Count||r.Revealed.Count>r.X+1||r.Revealed.Select(e=>e.Material.CardId).Distinct().Count()!=r.Revealed.Count||
                revealed.Where((e,i)=>e.Index!=i||e.Entity!=r.Revealed[i]||e.Entity.Material.From!=CardLocation.Processing||e.Entity.Material.Slot!=i||
                    e.Entity.DamageCard!=IsJiaweiDamageCard(e.Entity.Material.PrintedKind)).Any())
                throw new InvalidOperationException("Jiawei lost the exact ordered public top entities and their damage partition.");
            if(r.Stage is YangFengStage.DiscardChoice or YangFengStage.DiscardChildren)
            {if(r.Revealed.Count!=0)throw new InvalidOperationException("Jiawei cannot reveal before its genuine discard children finish.");}
            else if(ProgramEventHistory<ProgramCardsRevealedEvent>().Where(e=>e.FrameId==f.Id&&e.Bind==JiaweiRevealBind).ToArray() is not [var reveal]||
                reveal.OwnerSeat!=f.OwnerSeat||reveal.SkillId!=f.SkillId||!reveal.Cards.Select(c=>c.Id).SequenceEqual(r.Revealed.Select(e=>e.Material.CardId)))
                throw new InvalidOperationException("Jiawei's partition lacks its genuine native public-reveal fact.");
        }
        else
        {
            if(f.SelectedTargetSeats.Count!=0||r.TargetSeat!=f.OwnerSeat||r.X!=0||r.RequiredDiscard!=0||r.Candidates.Count!=0||r.Selected.Count!=0||r.Revealed.Count!=0||
                f.WindowContext is not{Window:SkillProgramTriggerWindow.AfterTurnEnded,Facts.OwnerKilledThisTurn:false}context||context.ParentFrameId!=r.ParentId||
                _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault() is not {}parent||!MatchesAfterTurnEndedChild(parent,f,context)||
                r.Stage is not(YangFengStage.QujiaChoice or YangFengStage.QujiaGainChildren)||ProgramEventHistory<YangFengStartedEvent>().Count(e=>e.Op==r.Op&&e.Source.OwnerSeat==f.OwnerSeat&&e.Source.SkillId==f.SkillId)!=1||
                (r.Stage==YangFengStage.QujiaChoice?r.QujiaChoice is not null||r.Movements.Count!=0:r.QujiaChoice!="quota"||r.Movements.Count!=1))
                throw new InvalidOperationException("Qujia lost its exact actual-ended parent or once-issued limited benefit.");
            var choices=ProgramEventHistory<QujiaChoiceIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(r.Stage==YangFengStage.QujiaChoice?choices.Length!=0:choices is not[var choice]||choice.Source!=r.Source||choice.GameplayHash!=r.GameplayHash||choice.ActualTurn!=r.ActualTurn||choice.ParentId!=r.ParentId||choice.Choice!="quota"||
                ProgramEventHistory<QujiaSlashBonusGrantedEvent>().Count(e=>e.FrameId==f.Id&&e.Source==r.Source&&e.GameplayHash==r.GameplayHash&&e.ActualTurn==r.ActualTurn)!=1)
                throw new InvalidOperationException("Qujia cannot repeat or substitute its selected permanent-quota benefit.");
        }
        var expectedStages=jiawei?new[]{YangFengStage.DiscardChildren,YangFengStage.RevealChildren,YangFengStage.OwnerGainChildren,YangFengStage.TargetGainChildren,YangFengStage.CleanupChildren}:new[]{YangFengStage.QujiaGainChildren};
        var invoices=ProgramEventHistory<YangFengMovementIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
        if(invoices.Length!=r.Movements.Count||r.Movements.Count>expectedStages.Length||r.Movements.Count>0&&r.Movements[^1].Stage!=r.Stage)
            throw new InvalidOperationException("Yang Feng lost the exact ordered native movement cursor.");
        for(var i=0;i<r.Movements.Count;i++)
        {
            var m=r.Movements[i];var reason=m.Stage switch{YangFengStage.DiscardChildren=>JiaweiDiscard,YangFengStage.RevealChildren=>$"skill-program.{f.SkillId}.{GetProgramBindingId(f)}.reveal",YangFengStage.OwnerGainChildren=>JiaweiOwnerGain,YangFengStage.TargetGainChildren=>JiaweiTargetGain,YangFengStage.CleanupChildren=>JiaweiCleanup,_=>QujiaGain};
            if(m.Index!=i||m.Stage!=expectedStages[i]||invoices[i].Movement!=m||m.Reason!=reason||m.Before<0||m.After<m.Before||m.After>_movementSequence||m.Count<0||i>0&&m.Before<r.Movements[i-1].After)
                throw new InvalidOperationException("Yang Feng's native movement sequence or reason differs from its original scalar invoice.");
            var moves=_cardMovements.Where(move=>move.Sequence>m.Before&&move.Sequence<=m.After&&move.Reason.Value==reason).ToArray();
            if(moves.Length!=m.Count)throw new InvalidOperationException("Yang Feng cannot omit or repay a primary physical movement.");
            if(m.Stage==YangFengStage.RevealChildren)
            {
                if(m.Count!=r.Revealed.Count||r.Revealed.Any(e=>!moves.Any(move=>move.CardId==e.Material.CardId&&move.CardKind==e.Material.PrintedKind&&move.From==CardLocation.DrawPile&&move.To==CardLocation.Processing&&move.Sequence==e.RevealSequence)))
                    throw new InvalidOperationException("Jiawei requires the actual ordered native draw-to-Processing entities.");
                continue;
            }
            var paid=ProgramEventHistory<YangFengMaterialMovedEvent>().Where(e=>e.FrameId==f.Id&&e.Stage==m.Stage).ToArray();
            if(paid.Length!=m.Count||paid.Where((e,j)=>e.Index!=j).Any())throw new InvalidOperationException("Yang Feng lost an exact material movement.");
            foreach(var paidEntity in paid)
            {
                var material=paidEntity.Material;var destination=m.Stage switch{YangFengStage.DiscardChildren=>material.From.Zone==CardZoneKind.Equipment&&material.GeneralWeapon?CardLocation.OutsideGame:CardLocation.DiscardPile,YangFengStage.CleanupChildren=>CardLocation.DiscardPile,YangFengStage.TargetGainChildren=>CardLocation.Hand(r.TargetSeat),_=>CardLocation.Hand(f.OwnerSeat)};
                if(paidEntity.To!=destination||!moves.Any(move=>move.Sequence==paidEntity.Sequence&&move.CardId==material.CardId&&move.CardKind==material.PrintedKind&&move.From==material.From&&move.To==destination))
                    throw new InvalidOperationException("Yang Feng's paid entity lost its native original source or exact destination.");
                if(m.Stage==YangFengStage.DiscardChildren)
                {if(!r.Selected.Contains(material)||paid.Length!=r.Selected.Count)throw new InvalidOperationException("Jiawei paid a card outside its original complete private selection.");}
                else if(m.Stage==YangFengStage.QujiaGainChildren)
                {if(paid.Length>1||material.From!=CardLocation.DrawPile||!IsSlashCard(material.PrintedKind))throw new InvalidOperationException("Qujia must obtain an existing actual deck Slash entity.");}
                else
                {
                    var entity=r.Revealed.SingleOrDefault(e=>e.Material==material);
                    if(entity is null||_cardMovements.Any(move=>move.CardId==material.CardId&&move.Sequence>entity.RevealSequence&&move.Sequence<paidEntity.Sequence)||
                        m.Stage!=YangFengStage.CleanupChildren&&entity.DamageCard!=(m.Stage==YangFengStage.OwnerGainChildren))
                        throw new InvalidOperationException("Jiawei cannot replace a revealed entity, change its partition or reclaim a later residency.");
                }
            }
        }
        var children=r.Stage is not(YangFengStage.DiscardChoice or YangFengStage.QujiaChoice);
        if(children?f.PendingMovementContinuation is not{BeforeCount:0,CoverageResultBind:null}p||p.SubjectSeat!=f.OwnerSeat:f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Yang Feng's paid children lost their owning typed movement continuation.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27150)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27150)]
    private bool ContinuePaidYangFeng(ProgramSkillFrame f)
    {if(f.YangFeng is not{Movements.Count:>0})return false;AssertYangFeng(f);return true;}
    private bool IsYangFengMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)
    {if(f.YangFeng is not{Movements.Count:>0}r||e?.Op!=r.Op||p!=f.PendingMovementContinuation)return false;AssertYangFeng(f);return true;}
    private bool YangFengLionProof(ProgramSkillFrame f,int seat,CardMoveReason? reason)
    {var m=f.YangFeng!.Movements[^1];return reason?.Value==JiaweiDiscard&&m.Stage==YangFengStage.DiscardChildren&&_cardMovements.Any(move=>move.Sequence>m.Before&&move.Sequence<=m.After&&move.CardKind==CardKind.SilverLion&&move.From==CardLocation.Equipment(seat)&&move.Reason==reason);}
    private bool IsYangFengRecoveryParent(ProgramSkillFrame f,RecoveryReplacementFrame recovery)
    {
        if(f.YangFeng is not{Movements.Count:>0}||f.PendingMovementContinuation is null)return false;AssertYangFeng(f);
        return RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.Amount==1&&recovery.Attempt.SourceSeat==recovery.Attempt.TargetSeat&&
            recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion&&YangFengLionProof(f,recovery.Attempt.TargetSeat,recovery.Attempt.Completion.MoveReason);
    }
    private bool YangFengFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.YangFeng is not{Movements.Count:>0}r||f.PendingMovementContinuation is null)return false;AssertYangFeng(f);var m=r.Movements[^1];
        if(child is CardsMovedTriggerWindowFrame moved)return ProgramMovementReturnMatches(f,moved)&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&
            moved.Batch.Movements.Count>0&&moved.Batch.Movements.All(move=>_cardMovements.Contains(move)&&move.Sequence>m.Before&&move.Sequence<=m.After);
        if(child is RecoveryReplacementFrame replacement)return IsYangFengRecoveryParent(f,replacement);
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&
            hp.Change.Amount==1&&hp.Change.SourceSeat==hp.Change.TargetSeat&&YangFengLionProof(f,hp.Change.TargetSeat,new(JiaweiDiscard));
        return child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? YangFengObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.YangFengFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    private bool HasYangFengDamageObserver(long id)=>YangFengObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool HasYangFengBeforeDamageObserver(long id)=>YangFengObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool IsYangFengDying()=>ActiveDying is{}dying&&YangFengObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==dying.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    private bool AllowsYangFengNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id||YangFengObserverRoot()is not{}root||root.Id==f.Id)return false;var e=GetPausedPrivateOfferEffect(f);
        return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}reference?ResolveProgramParticipant(f,reference):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27150)]
    private bool AdvanceYangFengSubtree()=>_pendingDecision is null&&YangFengObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
