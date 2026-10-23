namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HappyZlCannotRespond(CardActionContext action,int responderSeat) => HasHappyZl &&
        (action.Type==CardActionType.Use||action.Type==CardActionType.Response&&action.EffectiveKind==CardKind.Nullification) &&
        IsValidPlayerSeat(action.ActorSeat)&&IsValidPlayerSeat(responderSeat)&&_players[action.ActorSeat].IsAlive&&_players[responderSeat].IsAlive&&
        HappyZlColor(action.ActorSeat,responderSeat,action.EffectiveIsRed??SuitColor(action.EffectiveSuit)) is not null;
    private bool HappyZlCannotRespond(long useId,int responderSeat) => HasHappyZl && LifecycleCardUse(useId) is {Action:{} action} use &&
        action.Type==CardActionType.Use&&action.ActorSeat==use.SourceSeat&&action.EffectiveKind==use.CardKind&&HappyZlCannotRespond(action,responderSeat);
    private bool HappyZlNullificationForbidden(NullificationWindowFrame window,int responderSeat)
    {
        if(!HasHappyZl||!HasNullificationEffectOwner(window))return false;
        if(window.ChainDepth==0)return window.DelayedReturn is null&&HappyZlCannotRespond(window.ParentFrameId,responderSeat);
        if(window.HappyZhangLiao is not {}r||r.Window!=window.Id||r.Parent!=window.ParentFrameId||r.Depth!=window.ChainDepth||
            HappyZlColor(r.Actor,r.Target,r.Color) is not{}grant||grant.FrameId!=r.GrantFrame)return false;
        return ProgramEventHistory<HappyZlCounterspellEvent>().Count(e=>e.Receipt==r)==1&&ProgramEventHistory<CardActionAcceptedEvent>().Any(e=>
            e.Action.ActionId==r.ActionId&&e.Action.ParentActionId==NullificationParentActionId(window)&&e.Action.ActorSeat==r.Actor&&
            e.Action.Type==CardActionType.Response&&e.Action.EffectiveKind==CardKind.Nullification&&HappyZlCannotRespond(e.Action,responderSeat));
    }
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,40980)] private void ObserveHappyZlCounterspell(IGameEvent payload)
    {
        if(!HasHappyZl||payload is not CardActionAcceptedEvent{Action:{Type:CardActionType.Response,EffectiveKind:CardKind.Nullification}a}||
            ActiveNullificationWindow is not {}w||w.ChainDepth<1||!HasNullificationEffectOwner(w)||a.ParentActionId!=NullificationParentActionId(w)||
            a.ResponderSeat!=a.ActorSeat||a.RequesterSeat is not null||a.OpponentSeat!=w.SourceSeat)return;
        var grant=ProgramEventHistory<HappyZlColorEvent>().LastOrDefault(e=>e.Source.OwnerSeat==a.ActorSeat&&HappyZlCannotRespond(a,e.Target));
        if(grant?.Color is not{}color)return;
        var receipt=new HappyZlCounterspell(w.Id,w.ParentFrameId,w.ChainDepth,a.ActionId,a.ActorSeat,grant.Target,color,grant.FrameId);
        ReplaceRuntimeFrame(w.Id,w with{HappyZhangLiao=receipt});AdvanceEventRulesAndQueueFact(new HappyZlCounterspellEvent(receipt));
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState,40980)] private void RequireHappyZl(ProgramSkillFrame f)
    {
        if(f.HappyZhangLiao is not {}r)return;
        var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);
        if(f.InstructionIndex!=1||r.Source!=HappyZlSource(f)||r.Hash!=f.GameplayHash||!HappyZhangLiaoComposition.Own(r.Op)||r.Parent!=f.WindowContext?.ParentFrameId||
            r.Turn!=_turnNumber||!Enum.IsDefined(r.Stage)||r.Target< -1||r.Target>=_players.Count||r.Count<0||r.Count>5||r.Cursor<0||r.Restored<0||r.Restored>5||
            index<1||_resolutionStack[index-1].Id!=r.Parent||ProgramEventHistory<HappyZlStartedEvent>().Count(e=>e==new HappyZlStartedEvent(f.Id,r.Source,r.Hash,r.Op,r.Parent,r.Turn))!=1)
            throw new InvalidOperationException("Happy Zhang Liao lost his exact original parent, instruction, skill instance or scalar receipt.");
        if(r.Op==SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage && f.TriggerId=="colored-damage" &&
            (_resolutionStack[index-1] is not DamageTriggerWindowFrame parent||parent.TriggerWindow!=SkillProgramTriggerWindow.AfterDamageApplied||
             parent.ParentFrameId!=f.WindowContext!.DamageFrameId||parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||
             !MountObserverCandidateMatches(f,parent.Candidates[parent.CandidateIndex].ToProgramCandidate())))
            throw new InvalidOperationException("Happy Duorui lost its actual sourced-damage candidate.");
        if(r.Op==SkillProgramEffectOp.ResolveHappyZhangLiaoDuoruiDamage&&f.TriggerId=="restore-source"&&
            (_resolutionStack[index-1] is not BeforeDamageProgramWindowFrame before||before.SourceSeat!=r.Target||before.TargetSeat!=f.OwnerSeat||before.Amount<=0||
             before.Prevented||before.RedirectedTargetSeat is not null||before.CandidateIndex<0||before.CandidateIndex>=before.Candidates.Count||!MountObserverCandidateMatches(f,before.Candidates[before.CandidateIndex].Candidate)||
             LastDamageSourceOriginalAttack(before.ContinuationAttackResolutionId??before.ParentFrameId) is not{IsSourceLess:false,DamageAmount:>0} attempt||attempt.SourceSeat!=r.Target||attempt.TargetSeat!=f.OwnerSeat||GetDamageNature(attempt)!=before.Nature))
            throw new InvalidOperationException("Happy Duorui lost its exact positive incoming damage attempt and original target candidate.");
        if(r.View.Distinct().Count()!=r.View.Count||r.Payments.Where((p,i)=>p.Ordinal!=i+1||p.Before<0||p.After<p.Before||p.After>_movementSequence||
            p.Moves.Any(m=>m.Sequence<=p.Before||m.Sequence>p.After||_cardMovements.Count(actual=>actual==m)!=1)||
            ProgramEventHistory<HappyZlPaidEvent>().Count(e=>e==new HappyZlPaidEvent(f.Id,p.Ordinal,p.Before,p.After))!=1).Any())
            throw new InvalidOperationException("Happy Zhang Liao lost his frozen view or actual movement invoice.");
        if(r.Slot is {}slot)
        {
            if(r.Capacity<1||r.RandomSlots.Count==0||r.RandomSlots.Distinct().Count()!=r.RandomSlots.Count)throw new InvalidOperationException("Duorui lost its frozen random slot candidates.");
            var random=new DeterministicRandom(unchecked((int)r.RandomBefore));
            if(r.RandomSlots[random.Next(r.RandomSlots.Count)]!=slot||random.State!=r.RandomAfter||ProgramEventHistory<HappyZlLeaseEvent>().Count(e=>e==new HappyZlLeaseEvent(f.Id,r.Source,r.Target,slot,r.Capacity))!=1)
                throw new InvalidOperationException("Duorui lost its private deterministic random result and exact issued abolition.");
        }
        if(r.Restore.Any(e=>e.Source.OwnerSeat!=f.OwnerSeat||e.Target!=r.Target||ProgramEventHistory<HappyZlLeaseEvent>().Count(x=>x==e)!=1)||r.Cursor>Math.Max(r.Count,r.Restore.Count)||
            r.Restored>r.Cursor||r.Restored!=ProgramEventHistory<HappyZlRestoredEvent>().Count(e=>e.FrameId==f.Id))
            throw new InvalidOperationException("Duorui lost its frozen source-specific restoration batch.");
        if(r.Card>0&&(r.Payments.Count==0||!r.Payments[0].Moves.Any(m=>m.CardId==r.Card&&m.From==CardLocation.Hand(r.Target)&&m.To==CardLocation.Hand(f.OwnerSeat)&&m.Reason.Value==HappyZlTake)||
            ProgramEventHistory<HappyZlColorEvent>().Count(e=>e==new HappyZlColorEvent(f.Id,r.Source,r.Turn,r.Target,r.Color))!=1))
            throw new InvalidOperationException("Duorui lost the actual obtained card and its turn color qualification.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment,40980)] private bool HappyZlMovement(ProgramSkillFrame f,SkillProgramEffect? e,ProgramMovementContinuation p)
    {
        if(f.HappyZhangLiao is not{}r||e?.Op!=r.Op||p!=f.PendingMovementContinuation||p.SubjectSeat!=f.OwnerSeat||p.BeforeCount!=0||p.CoverageResultBind is not null||
            r.Stage is not(HappyZlStage.GainChildren or HappyZlStage.SlotChildren or HappyZlStage.RestoreChildren or HappyZlStage.DrawChildren or HappyZlStage.DiscardChildren))return false;
        RequireHappyZl(f);return r.Payments.Count>0&&(r.Stage!=HappyZlStage.DrawChildren||r.Draw is{}draw&&draw.RecipientSeat==f.OwnerSeat&&draw.RequestedCount==r.Restored);
    }
    private bool HappyZlFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(f.HappyZhangLiao is not{}r||f.PendingMovementContinuation is not{}p||!HappyZlMovement(f,GetPausedPrivateOfferEffect(f),p))return false;
        var moves=r.Payments.Last().Moves;
        if(child is CardsMovedTriggerWindowFrame m)return m.Batch.ParentFrameId==f.Id&&m.Batch.OriginOwnerSeat==f.OwnerSeat&&m.Batch.OriginSkillId==f.SkillId&&m.Batch.OriginSkillInstanceId==f.SkillInstanceId&&ProgramMovementReturnMatches(f,m)&&m.Batch.Movements.All(x=>moves.Contains(x));
        if(child is RecoveryReplacementFrame recovery)return moves.Any(m=>m.From.Zone==CardZoneKind.Equipment&&m.CardKind==CardKind.SilverLion&&m.From.OwnerSeat==recovery.Attempt.TargetSeat)&&RecoveryReplacementFrameRidesOn(recovery,f)&&recovery.Return.Continuation==PostEventContinuation.AwaitedProgramMovement&&recovery.Attempt.Completion.Producer==RecoveryAttemptProducer.SilverLion;
        if(child is HpChangedTriggerWindowFrame hp)return hp.Change.ParentFrameId==f.Id&&hp.ResumeFrameId==f.Id&&hp.Continuation==PostEventContinuation.AwaitedProgramMovement&&hp.Change.Kind==HpChangeKind.Recovery&&moves.Any(m=>m.From.Zone==CardZoneKind.Equipment&&m.CardKind==CardKind.SilverLion&&m.From.OwnerSeat==hp.Change.TargetSeat);
        return false;
    }
    private ProgramSkillFrame? HappyZlObserverRoot()=>FindProgramObserverRoot(static(e,f,c)=>e.HappyZlFirstChild(f,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage,40980)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage,40980)] private bool HappyZlDamageObserver(long id)=>HappyZlObserverRoot() is{}r&&_resolutionStack.FindIndex(f=>f.Id==id&&f is DamageTriggerWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage,40980)] private bool HappyZlBeforeDamageObserver(long id)=>HappyZlObserverRoot() is{}r&&_resolutionStack.FindIndex(f=>f.Id==id&&f is BeforeDamageProgramWindowFrame)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant,40980)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime,40980)] private bool HappyZlDying()=>ActiveDying is{}d&&HappyZlObserverRoot() is{}r&&_resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth,40980)] private bool AdvanceHappyZlChildren()=>_pendingDecision is null&&HappyZlObserverRoot() is not null&&TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,40980)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,40980)] private bool RetainHappyZlPaid(ProgramSkillFrame f)
    {if(f.HappyZhangLiao is not{Payments.Count:>0})return false;RequireHappyZl(f);return true;}
}
