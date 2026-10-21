namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState,27370)]
    private void AssertMaYuanYi(ProgramSkillFrame f)
    {
        if(f.MaYuanYi is not{}r)return;
        if(r.Source!=MaYuanYiSource(f)||r.GameplayHash!=f.GameplayHash||_contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash!=r.GameplayHash||f.InstructionIndex!=1||f.TriggerId is null||
            f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}c||c.ParentFrameId!=r.ParentId||c.OwnerSeat!=f.OwnerSeat||r.ActualTurn!=_turnNumber||r.TurnOwner!=_currentSeat||
            !MaYuanYiComposition.Handles(r.Op)||GetPausedPrivateOfferEffect(f)?.Op!=r.Op||!Enum.IsDefined(r.Stage)||r.FactionCount<1||r.BingBefore<0||r.Before<0||r.After<r.Before||r.After>_movementSequence||r.ActualCount<0||
            ProgramEventHistory<MaYuanYiStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var started]||
            started!=new MaYuanYiStartedEvent(f.Id,r.Source,r.GameplayHash,r.Op,r.ParentId,r.ActualTurn,r.TurnOwner,r.FactionCount,r.BingBefore,r.MaximumBefore,r.HpBefore,r.ActionId,r.OpponentSeat))
            throw new InvalidOperationException("Ma Yuan Yi lost its original source, instruction, actual turn or scalar start invoice.");
        if(r.Op==SkillProgramEffectOp.ResolveWangjingBingOpponentDraw)
        {
            if(r.Stage!=MaYuanYiStage.OpponentDrawChildren||r.ActionId is not{}action||r.OpponentSeat is not{}opposite||r.Pile is not null||r.TargetSeat is not null||r.JudgmentId is not null||
                c.Window is not(SkillProgramTriggerWindow.CardUseTargetsFinalized or SkillProgramTriggerWindow.CardResponseAccepted)||c.CardUse?.CardActionId!=action||
                ProgramEventHistory<MaYuanYiBingActionEvent>().SingleOrDefault(e=>e.ActionId==action&&e.ProviderSeat==f.OwnerSeat)is not{}fact||!ValidBingAction(fact)||
                opposite!=(fact.ActorSeat!=fact.ProviderSeat?fact.RequesterSeat:fact.Type==CardActionType.Response?fact.OpponentSeat:c.TargetSeat)||
                ProgramEventHistory<WangjingDrawIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var draw]||draw.ActionId!=action||draw.OwnerSeat!=f.OwnerSeat||draw.OpponentSeat!=opposite||draw.OpponentHp!=draw.HighestHp||draw.ActualCount!=r.ActualCount||
                ProgramEventHistory<WangjingDrawIssuedEvent>().Count(e=>e.ActionId==action&&e.OwnerSeat==f.OwnerSeat&&e.OpponentSeat==opposite)!=1)
                throw new InvalidOperationException("Wangjing lost the original Bing action, actual provider/requester/opponent or once-issued highest-HP draw.");
        }
        else if(r.Op==SkillProgramEffectOp.ReplaceJibingDrawWithTwoBing)
        {
            if(r.Stage!=MaYuanYiStage.StoreChildren||r.Pile is not{}pile||pile.Zone!=CardZoneKind.PublicPersistentPile||pile.OwnerSeat!=f.OwnerSeat||r.BingBefore>=r.FactionCount||r.ActualCount>2||r.ActionId is not null||r.OpponentSeat is not null||r.TargetSeat is not null||
                c.Window!=SkillProgramTriggerWindow.DrawPhaseStarting||r.TurnOwner!=f.OwnerSeat||
                _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.ParentId)is not{Continuation:ProgramLifecycleContinuation.CompleteDrawPhase}parent||parent.OwnerSeat!=f.OwnerSeat||
                parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||!MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex]))
                throw new InvalidOperationException("Jibing lost its exact replaceable normal-draw parent or uncapped living-faction threshold.");
            var stored=ProgramEventHistory<MaYuanYiStoredEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(stored.Length!=r.ActualCount||stored.Select(e=>e.Index).Where((n,i)=>n!=i).Any()||stored.Select(e=>e.CardId).Distinct().Count()!=stored.Length||stored.Any(e=>e.Source!=r.Source||e.Pile!=pile||
                _cardMovements.Count(m=>m.Sequence==e.Sequence&&m.Sequence>r.Before&&m.Sequence<=r.After&&m.CardId==e.CardId&&m.From==CardLocation.DrawPile&&m.To==pile&&m.Reason.Value==JibingStoreReason)!=1))
                throw new InvalidOperationException("Jibing lost an original ordered deck entity or paid source-pile movement.");
        }
        else if(r.Op==SkillProgramEffectOp.ResolveMoucuanAwakening)
        {
            if(r.Stage is not(MaYuanYiStage.MaximumChildren or MaYuanYiStage.GrantChildren)||c.Window!=SkillProgramTriggerWindow.TurnStartBeforeNormalFlow||r.TurnOwner!=f.OwnerSeat||r.BingBefore<r.FactionCount&&!HasShenGuoJiaAwakeningReceipt(f)||r.MaximumBefore<1||r.ActionId is not null||r.TargetSeat is not null||r.Pile is not null||f.PendingMovementContinuation is not null||
                ProgramEventHistory<MoucuanMaximumPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var maximum]||maximum!=new MoucuanMaximumPaidEvent(f.Id,f.OwnerSeat,r.MaximumBefore,r.MaximumBefore-1,r.HpBefore,Math.Min(r.HpBefore,r.MaximumBefore-1))||
                ProgramEventHistory<MoucuanMaximumPaidEvent>().Count(e=>e.OwnerSeat==f.OwnerSeat)!=1||!ProgramEventHistory<MaximumHpChangedEvent>().Any(e=>e.PlayerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.Delta==-1&&e.MaximumHp==r.MaximumBefore-1)||
                (r.Stage==MaYuanYiStage.GrantChildren?ProgramEventHistory<MoucuanSkillGrantedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat)!=1:ProgramEventHistory<MoucuanSkillGrantedEvent>().Any(e=>e.FrameId==f.Id)))
                throw new InvalidOperationException("Moucuan lost its once-per-player awakening, frozen all-faction threshold or real maximum-HP payment.");
        }
        else
        {
            if(c.Window!=SkillProgramTriggerWindow.TurnEnding||r.Stage is MaYuanYiStage.StoreChildren or MaYuanYiStage.OpponentDrawChildren or MaYuanYiStage.MaximumChildren or MaYuanYiStage.GrantChildren||f.PendingMovementContinuation is not null&&r.Stage!=MaYuanYiStage.JudgmentChildren||r.ActionId is not null||r.OpponentSeat is not null||r.Pile is not null||
                !ProgramEventHistory<MaYuanYiBingActionEvent>().Any(e=>e.ActualTurn==r.ActualTurn&&e.TurnOwner==r.TurnOwner&&e.ProviderSeat==f.OwnerSeat&&ValidBingAction(e)))
                throw new InvalidOperationException("Binghuo lost its complete actual-turn Use-or-played Bing history.");
            var target=ProgramEventHistory<BinghuoTargetIssuedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(r.Stage==MaYuanYiStage.TargetChoice?target.Length!=0||r.TargetSeat is not null||r.JudgmentId is not null:
                target is not[var issued]||issued.TargetSeat!=r.TargetSeat||issued.JudgmentId!=r.JudgmentId||!IsValidPlayerSeat(issued.TargetSeat))
                throw new InvalidOperationException("Binghuo lost its original judgment target or exact native judgment identity.");
            var returned=ProgramEventHistory<BinghuoJudgmentReturnedEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(r.Stage is MaYuanYiStage.TargetChoice or MaYuanYiStage.Judgment)
            {if(returned.Length!=0||r.JudgmentCardId is not null||r.JudgmentSuit is not null||r.JudgmentHandoffSequence is not null)throw new InvalidOperationException("Binghuo cannot resolve a judgment before its native return.");}
            else if(returned is not[var result]||result!=new BinghuoJudgmentReturnedEvent(f.Id,r.JudgmentId!.Value,r.JudgmentCardId,r.JudgmentSuit,r.JudgmentHandoffSequence)||
                ProgramEventHistory<JudgmentResolvedEvent>().Count(e=>e.ResolutionId==r.JudgmentId&&e.ParentResolutionId==f.Id&&e.TargetSeat==r.TargetSeat&&e.Reason=="binghuo"&&e.CardId==r.JudgmentCardId&&e.Suit==r.JudgmentSuit)!=1)
                throw new InvalidOperationException("Binghuo lost its finalized, possibly replaced native judgment.");
            if(r.Stage==MaYuanYiStage.DamageChildren&&(r.JudgmentSuit is not(Suit.Spade or Suit.Club)||ProgramEventHistory<BinghuoDamageIssuedEvent>().Count(e=>e.FrameId==f.Id&&e.SourceSeat==f.OwnerSeat&&e.TargetSeat==r.TargetSeat&&e.Amount==1&&e.Nature==DamageNature.Thunder)!=1))
                throw new InvalidOperationException("Binghuo cannot duplicate or change its black-judgment thunder damage.");
            if(r.Stage is MaYuanYiStage.JudgmentChildren or MaYuanYiStage.DamageChildren)
            {
                var cleanup=_cardMovements.Where(m=>m.Sequence>r.Before&&m.Sequence<=r.After).ToArray();
                if(r.ActualCount!=(r.JudgmentHandoffSequence is null?0:1)||cleanup.Length!=r.ActualCount||cleanup.Any(m=>!IsBinghuoCleanupMovement(r,m))||
                    r.JudgmentHandoffSequence is{}handoff&&!_cardMovements.Any(m=>m.Sequence==handoff&&IsBinghuoHandoffMovement(r,m))||
                    ProgramEventHistory<MaYuanYiMovementEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var movement]||movement!=new MaYuanYiMovementEvent(f.Id,MaYuanYiStage.JudgmentChildren,r.Before,r.After,r.ActualCount)||
                    f.PendingMovementContinuation is{}pending&&!IsMaYuanYiMovement(f,GetPausedPrivateOfferEffect(f),pending))
                    throw new InvalidOperationException("Binghuo lost its exact once-paid native judgment cleanup and child boundary.");
            }
        }
        if(r.Stage is MaYuanYiStage.StoreChildren or MaYuanYiStage.OpponentDrawChildren)
        {
            if(f.PendingMovementContinuation is not null&&!IsMaYuanYiMovement(f,GetPausedPrivateOfferEffect(f),f.PendingMovementContinuation)||
                ProgramEventHistory<MaYuanYiMovementEvent>().Where(e=>e.FrameId==f.Id).ToArray()is not[var movement]||movement!=new MaYuanYiMovementEvent(f.Id,r.Stage,r.Before,r.After,r.ActualCount)||
                _cardMovements.Count(m=>m.Sequence>r.Before&&m.Sequence<=r.After&&m.Reason.Value==(r.Stage==MaYuanYiStage.StoreChildren?JibingStoreReason:WangjingDrawReason))!=r.ActualCount)
                throw new InvalidOperationException("Ma Yuan Yi lost its original once-paid movement range.");
        }
        if(_resolutionStack.LastOrDefault()?.Id==f.Id&&r.Stage==MaYuanYiStage.TargetChoice&&_pendingDecision is{}p&&(p.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual(p.Choices,MaYuanYiTargets(f))))
            throw new InvalidOperationException("Binghuo cannot change its published target menu.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 320)]
    private bool IsMaYuanYiMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)=>f.MaYuanYi is{Stage:MaYuanYiStage.StoreChildren or MaYuanYiStage.OpponentDrawChildren or MaYuanYiStage.JudgmentChildren}r&&e?.Op==r.Op&&p==f.PendingMovementContinuation&&p.SubjectSeat==f.OwnerSeat&&p.BeforeCount==0&&p.CoverageResultBind is null;
    private bool MaYuanYiFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.MaYuanYi is not{}r)return false;AssertMaYuanYi(f);
        if(child is JudgmentFrame judgment)return IsBinghuoJudgment(judgment,f);
        if(child is DeathFrame death)return r.Stage==MaYuanYiStage.MaximumChildren&&r.MaximumBefore==1&&death.ParentFrameId==f.Id&&death.VictimSeat==f.OwnerSeat&&death.ReturnKind==DeathReturnKind.ProgramSkill;
        if(child is ProgramLifecycleTriggerWindowFrame{Window:SkillProgramTriggerWindow.SkillsChanged}skills)return (r.Stage==MaYuanYiStage.GrantChildren||HasPaidMoucuanSourceLoss(f))&&skills.ResumeProgramFrameId==f.Id&&skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        if(child is HpChangedTriggerWindowFrame hp)return r.Stage==MaYuanYiStage.MaximumChildren&&hp.ResumeFrameId==f.Id&&hp.Change.ParentFrameId==f.Id&&hp.Continuation==PostEventContinuation.Program&&hp.Change.Kind==HpChangeKind.MaximumHp&&hp.Change.TargetSeat==f.OwnerSeat&&hp.Change.Amount==1;
        if(child is CardsMovedTriggerWindowFrame moved)return r.Stage is MaYuanYiStage.StoreChildren or MaYuanYiStage.OpponentDrawChildren or MaYuanYiStage.JudgmentChildren&&f.PendingMovementContinuation is not null&&ProgramMovementReturnMatches(f,moved)&&
            moved.Batch.Movements.All(m=>_cardMovements.Contains(m)&&(r.Stage==MaYuanYiStage.JudgmentChildren?IsBinghuoHandoffMovement(r,m)||m.Sequence>r.Before&&m.Sequence<=r.After&&IsBinghuoCleanupMovement(r,m):m.Sequence>r.Before&&m.Sequence<=r.After&&(m.Reason.Value==(r.Stage==MaYuanYiStage.StoreChildren?JibingStoreReason:WangjingDrawReason)||m.Reason==CardMoveReasons.Reshuffle&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile)));
        return r.Stage==MaYuanYiStage.DamageChildren&&f.AttackAttempt is{SourceSeat:var source,TargetSeat:var target,DamageAmount:1,SourceLess:false,Nature:DamageNature.Thunder}&&source==f.OwnerSeat&&target==r.TargetSeat&&f.AttackReturn is not null&&DyingSuitsStructuralEdge(f,child);
    }
    private ProgramSkillFrame? MaYuanYiObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.MaYuanYiFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 830)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 940)]
    private bool HasMaYuanYiDamageObserver(long id)=>MaYuanYiObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is DamageTriggerWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 970)]
    private bool HasMaYuanYiBeforeDamageObserver(long id)=>MaYuanYiObserverRoot()is{}root&&_resolutionStack.Any(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)&&_resolutionStack.FindIndex(f=>f.Id==id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 800)]
    private bool IsMaYuanYiDying()=>ActiveDying is{}d&&MaYuanYiObserverRoot()is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 1370)]
    private bool AllowsMaYuanYiNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference? source,DamageNature? nature,bool sourceLess)
    {
        if(sourceLess||f.AttackAttempt is not null||_resolutionStack.LastOrDefault()?.Id!=f.Id)return false;
        if(f.MaYuanYi is{Stage:MaYuanYiStage.DamageChildren}r)return target==r.TargetSeat&&amount==1&&source is null&&nature==DamageNature.Thunder;
        if(MaYuanYiObserverRoot()is null)return false;var e=GetPausedPrivateOfferEffect(f);
        return e is{Op:SkillProgramEffectOp.Damage}&&e.Amount==amount&&e.ActorReference==source&&e.DamageNature==nature&&target==(e.TargetReference is{}participant?ResolveProgramParticipant(f,participant):ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,27370)] private bool AdvanceMaYuanYiSubtree()=>_pendingDecision is null&&MaYuanYiObserverRoot()is not null&&TryAdvanceObserverSubtree(true);
}
