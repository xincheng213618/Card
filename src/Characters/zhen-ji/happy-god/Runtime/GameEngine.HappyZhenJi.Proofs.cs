namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool IsHappyFeifuClaimedResponseEntity(CardActionContext action,int cardId)
    {
        if(!HasHappyZhenJi||action.Type!=CardActionType.Response||action.EffectiveKind!=CardKind.Dodge||action.PhysicalCards is not[var cost]||cost.CardId!=cardId||
            cost.From.OwnerSeat!=action.ProviderSeat||cost.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment)||
            ProgramEventHistory<HappyZjFeifuClaimedEvent>().Where(e=>e.ActionId==action.ActionId&&e.CardId==cardId).ToArray() is not[var claim]||
            claim.Source.SkillId!=HappyFeifu||claim.Source.BindingId!="finished-feifu"||claim.Source.OwnerSeat!=action.ProviderSeat||claim.From!=CardLocation.DiscardPile||
            !action.ConversionChain.Contains(new(HappyFeifu,"black-as-dodge",action.ProviderSeat,claim.Source.SkillInstanceId))||
            _publicPersistentPiles.GetValueOrDefault((claim.Source.OwnerSeat,HappyFeifu,claim.Source.SkillInstanceId)) is not{}pile||pile.Capacity!=int.MaxValue||pile.Location!=claim.To||
            _cardZones.GetLocation(cardId)!=claim.To||
            ProgramEventHistory<HappyZjStartedEvent>().Where(e=>e.FrameId==claim.FrameId).ToArray() is not[var started]||started.Source!=claim.Source||
            started.Op!=SkillProgramEffectOp.CollectHappyZhenJiFeifu||started.Hash!=_contentRegistry.GetSkill(HappyFeifu).Program!.GameplayHash||
            ProgramEventHistory<CardResponseCompletionStartedEvent>().Where(e=>e.ActionId==action.ActionId).ToArray() is not[var completed]||completed.FrameId!=started.Parent||
            completed.ProviderSeat!=action.ProviderSeat||completed.EffectiveKind!=CardKind.Dodge||completed.ActualTurnNumber!=started.Turn||completed.ActualTurnOwnerSeat!=started.TurnOwner||
            !ProgramEventHistory<CardResponseCompletedEvent>().Contains(new(action.ActionId,completed.ParentFrameId,
                completed.ParentFrameId,
                completed.ActorSeat,completed.ProviderSeat,completed.EffectiveKind,completed.OriginalContinuation,completed.ActualTurnNumber,completed.ActualTurnOwnerSeat,completed.NativeActorSeat)))return false;
        var accepted=ProgramEventHistory<CardActionAcceptedEvent>().Where(e=>e.Action.ActionId==action.ActionId).Select(e=>e.Action).ToArray();
        if(accepted is not[var original]||original.ParentActionId!=action.ParentActionId||original.Type!=action.Type||original.ActorSeat!=action.ActorSeat||original.ProviderSeat!=action.ProviderSeat||
            original.RequesterSeat!=action.RequesterSeat||original.ResponderSeat!=action.ResponderSeat||original.OpponentSeat!=action.OpponentSeat||original.EffectiveKind!=action.EffectiveKind||
            original.EffectiveSuit!=action.EffectiveSuit||original.EffectiveRank!=action.EffectiveRank||original.EffectiveIsRed!=action.EffectiveIsRed||original.FactionOrigin!=action.FactionOrigin||
            !original.PhysicalCards.SequenceEqual(action.PhysicalCards)||!original.ConversionChain.SequenceEqual(action.ConversionChain)||!original.TargetSeats.SequenceEqual(action.TargetSeats)||
            !original.EffectiveDesignatedTargetSeats.SequenceEqual(action.EffectiveDesignatedTargetSeats))return false;
        var moves=_cardMovements.Where(m=>m.CardId==cardId&&m.Sequence<=claim.Sequence).TakeLast(3).ToArray();
        return moves is[var entered,var finished,var stored]&&entered.CardKind==cost.CardKind&&entered.From==cost.From&&entered.To==CardLocation.Processing&&entered.Reason==CardMoveReasons.Respond&&
            finished.CardKind==cost.CardKind&&finished.From==CardLocation.Processing&&finished.To==CardLocation.DiscardPile&&finished.Reason==CardMoveReasons.ResponseFinished&&
            stored.Sequence==claim.Sequence&&stored.CardKind==cost.CardKind&&stored.From==claim.From&&stored.To==claim.To&&stored.Reason.Value=="skill.happy-feifu.store"&&
            ProgramEventHistory<HappyZjPaidEvent>().Count(p=>p.FrameId==claim.FrameId&&p.Ordinal==1&&p.Before>=finished.Sequence&&p.Before<stored.Sequence&&p.After==stored.Sequence)==1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState,41240)]private void RequireHappyZj(ProgramSkillFrame f)
    {
        if(f.HappyZhenJi is not{}r)return;
        var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        if(index<0||f.InstructionIndex!=1||r.Source!=HappyZjSource(f)||r.Hash!=f.GameplayHash||!HappyZhenJiComposition.Own(r.Op)||
            !Enum.IsDefined(r.Stage)||r.Turn!=_turnNumber||r.TurnOwner!=_currentSeat||r.Phase<0||!IsValidPlayerSeat(r.Target)||r.Ordinal<0||r.Reward<0||r.Reward>5||r.Limit<0||
            ProgramEventHistory<HappyZjStartedEvent>().Count(e=>e==new HappyZjStartedEvent(f.Id,r.Source,r.Hash,r.Op,r.Turn,r.TurnOwner,r.Phase,r.Parent,r.Target,r.Ordinal))!=1||
            r.Selected.Distinct().Count()!=r.Selected.Count||r.Entities.Select(e=>e.Id).Distinct().Count()!=r.Entities.Count||
            r.Payments.Where((p,i)=>p.Ordinal!=i+1||p.Before<0||p.After<p.Before||p.After>_movementSequence||p.Moves.Any(m=>m.Sequence<=p.Before||m.Sequence>p.After||_cardMovements.Count(x=>x==m)!=1)||
                ProgramEventHistory<HappyZjPaidEvent>().Count(e=>e==new HappyZjPaidEvent(f.Id,p.Ordinal,p.Before,p.After))!=1).Any())
            throw new InvalidOperationException("Happy Zhen Ji lost her original skill instance, actual occurrence or immutable paid movement ledger.");
        if(r.Op==SkillProgramEffectOp.ResolveHappyZhenJiShenfu)
        {
            if(f.TriggerId is not null||f.ActivationId!="three-options"||f.SelectedTargetSeats is not[var target]||target!=r.Target||r.Parent!=0||r.Ordinal<1||
                HappyShenfuUses(f.OwnerSeat).Count(e=>e.FrameId<=f.Id)!=r.Ordinal||HappyShenfuUses(f.OwnerSeat).Count(e=>e.Target==r.Target)!=1||
                ProgramEventHistory<ProgramSkillStartedEvent>().Count(e=>e.FrameId==f.Id&&e.OwnerSeat==f.OwnerSeat&&e.SkillId==f.SkillId&&e.ActivationId==f.ActivationId)!=1)
                throw new InvalidOperationException("Shenfu lost its accepted target, actual-turn count or original activation.");
            if(r.Branch is{}branch&&ProgramEventHistory<HappyZjBranchEvent>().Count(e=>e==new HappyZjBranchEvent(f.Id,branch))!=1)
                throw new InvalidOperationException("Shenfu changed its already accepted branch.");
            if(r.Refunded)
            {
                if(r.Reward!=Math.Min(5,r.Ordinal+1)||ProgramEventHistory<HappyZjRefundedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not[var refund]||
                    refund.Turn!=r.Turn||refund.Phase!=r.Phase||refund.Target!=r.Target||refund.Ordinal!=r.Ordinal||refund.Reward!=r.Reward||refund.Before<1||refund.After!=refund.Before-1)
                    throw new InvalidOperationException("Shenfu lost its exact once-refunded phase debit and actual-turn reward count.");
            }
            else if(ProgramEventHistory<HappyZjRefundedEvent>().Any(e=>e.FrameId==f.Id))throw new InvalidOperationException("Shenfu lost its successful refund receipt.");
            if(f.AttackAttempt is{}attack&&(r.Stage!=HappyZjStage.Damaging||r.Branch!="thunder"||attack.SourceSeat!=f.OwnerSeat||attack.TargetSeat!=r.Target&& !attack.IsChainPropagation||attack.SourceLess||attack.Nature!=DamageNature.Thunder||f.AttackReturn is null))
                throw new InvalidOperationException("Shenfu lost its original sourced thunder child.");
        }
        else
        {
            if(index<1||_resolutionStack[index-1].Id!=r.Parent||f.WindowContext?.ParentFrameId!=r.Parent)
                throw new InvalidOperationException("Seven strings lost their exact lifecycle/completed-response parent.");
            if(r.Op==SkillProgramEffectOp.CollectHappyZhenJiFeifu)
            {
                if(_resolutionStack[index-1] is not ProgramCardTriggerWindowFrame{ResponseCompletion:{CostsDrained:true}}w||!ValidResponseCompletionWindow(w)||w.Action.ActionId!=r.Action||
                    w.Action.EffectiveKind!=CardKind.Dodge||w.Action.PhysicalCards is not[var cost]||!w.Action.ConversionChain.Contains(new CardConversionSource(HappyFeifu,"black-as-dodge",f.OwnerSeat,f.SkillInstanceId))||
                    r.Entities is not[var entity]||entity.Id!=cost.CardId||entity.Kind!=cost.CardKind||entity.From!=CardLocation.DiscardPile||!IsResponseCompletionCostFinished(w,cost))
                    throw new InvalidOperationException("Feifu lost its exact completed converted Dodge and finished native entity.");
                if(r.Payments.Count>0&&ProgramEventHistory<HappyZjFeifuClaimedEvent>().Count(e=>e.FrameId==f.Id&&e.ActionId==r.Action&&e.Source==r.Source&&e.CardId==entity.Id&&
                    e.From==entity.From&&e.To==r.Pile!.Location&&r.Payments.SelectMany(p=>p.Moves).Any(m=>m.Sequence==e.Sequence&&m.CardId==e.CardId&&m.From==e.From&&m.To==e.To&&m.Reason.Value=="skill.happy-feifu.store"))!=1)
                    throw new InvalidOperationException("Feifu lost the once-claimed real entity movement.");
            }
            if(r.Op==SkillProgramEffectOp.StoreHappyZhenJiQixian&&(r.Selected.Count>r.Limit||r.Stage==HappyZjStage.SelectStore&&r.Selected.Any(id=>_cardZones.GetLocation(id)!=CardLocation.Hand(f.OwnerSeat))))
                throw new InvalidOperationException("Qixian changed its selected current hand or frozen HP limit.");
            if(r.Pile is{}pile&&(pile.OwnerSeat!=f.OwnerSeat||pile.SkillId!=f.SkillId||pile.SkillInstanceId!=f.SkillInstanceId||pile.Capacity!=int.MaxValue||_publicPersistentPiles.GetValueOrDefault((pile.OwnerSeat,pile.SkillId,pile.SkillInstanceId))!=pile))
                throw new InvalidOperationException("Seven strings cannot mix another skill instance's public source.");
            if(r.Op==SkillProgramEffectOp.ReturnHappyZhenJiQixian&&(r.Entities.Count<7||r.Entities.Any(e=>!HappyZjPiles(f.OwnerSeat).Any(p=>p.Location==e.From))))
                throw new InvalidOperationException("Qixian lost the original seven-plus public entity set.");
        }
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment,41240)]private bool HappyZjMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)
    {
        if(f.HappyZhenJi is not{}r||e?.Op!=r.Op||p!=f.PendingMovementContinuation||p.SubjectSeat!=f.OwnerSeat||p.BeforeCount!=0||p.CoverageResultBind is not null||r.Payments.Count==0||
            r.Stage is not(HappyZjStage.EffectChildren or HappyZjStage.RewardChildren or HappyZjStage.StoreChildren or HappyZjStage.FeifuChildren or HappyZjStage.ReturnChildren))return false;
        RequireHappyZj(f);return true;
    }
    private bool HappyZjFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.HappyZhenJi is not{}r)return false;RequireHappyZj(f);
        if(r.Stage==HappyZjStage.Damaging&&f.AttackAttempt is not null&&f.AttackReturn is not null)return DyingSuitsStructuralEdge(f,child);
        if(f.PendingMovementContinuation is not{}p||!HappyZjMovement(f,GetPausedPrivateOfferEffect(f),p))return false;
        var moves=r.Payments.Last().Moves;
        if(child is CardsMovedTriggerWindowFrame moved)return moved.Batch.ParentFrameId==f.Id&&moved.Batch.OriginOwnerSeat==f.OwnerSeat&&moved.Batch.OriginSkillId==f.SkillId&&moved.Batch.OriginSkillInstanceId==f.SkillInstanceId&&ProgramMovementReturnMatches(f,moved)&&moved.Batch.Movements.All(m=>moves.Contains(m));
        if(child is RecoveryReplacementFrame recovery)return moves.Any(m=>m.From.Zone==CardZoneKind.Equipment&&m.CardKind==CardKind.SilverLion&&m.From.OwnerSeat==recovery.Attempt.TargetSeat)&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion;
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&moves.Any(m=>m.From.Zone==CardZoneKind.Equipment&&m.CardKind==CardKind.SilverLion&&m.From.OwnerSeat==hp.Change.TargetSeat);
        return false;
    }
    private ProgramSkillFrame? HappyZjObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.HappyZjFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,41240)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,41240)]private bool HappyZjDamageObserver(long id)=>HappyZjObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,41240)]private bool HappyZjBeforeDamageObserver(long id)=>HappyZjObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,41240)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,41240)]private bool HappyZjDying()=>ActiveDying is{}d&&HappyZjObserverRoot() is{}root&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,41240)]private bool AdvanceHappyZjChildren()=>_pendingDecision is null&&HappyZjObserverRoot() is not null&&TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,41240)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,41240)]private bool RetainHappyZjPaid(ProgramSkillFrame f)=>f.HappyZhenJi is{Stage:not(HappyZjStage.Branch or HappyZjStage.Discard or HappyZjStage.SelectStore)};
}
