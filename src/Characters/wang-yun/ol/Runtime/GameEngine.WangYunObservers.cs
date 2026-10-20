namespace CardGame.Core;
public sealed partial class GameEngine
{
    private static bool WangYunOriginsEqual(WangYunNativeUseOrigin a,WangYunNativeUseOrigin b)=>a.ProgramFrameId==b.ProgramFrameId && a.InstructionIndex==b.InstructionIndex && a.Source==b.Source &&
        a.GameplayHash==b.GameplayHash && a.Turn==b.Turn && a.PhaseInstance==b.PhaseInstance && a.Kind==b.Kind && a.UseId==b.UseId && a.ActionId==b.ActionId && a.Actor==b.Actor &&
        a.OutputKind==b.OutputKind && a.Before==b.Before && a.After==b.After && a.PaymentCompleted==b.PaymentCompleted && a.RoundOncePayment==b.RoundOncePayment && a.Materials.SequenceEqual(b.Materials) && a.Targets.SequenceEqual(b.Targets);
    private void BindWangYunNativeUse(long useId)
    {
        if(!HasWangYunPlanCapability && !HasWangYunTrickCapability)return;
        var index=_resolutionStack.FindIndex(x=>x.Id==useId);if(index<1 || _resolutionStack[index] is not CardUseFrame{Action:{}action}use || _resolutionStack[index-1] is not ProgramSkillFrame f)return;
        string kind;int actor;IReadOnlyList<int> expected;CardKind output;
        if(f.WangYunPlan is{Stage:"issuing-weapon" or "issuing-slash"}plan)
        {kind=plan.Stage=="issuing-weapon"?"weapon":"slash";actor=plan.Actor;expected=plan.IssuingIds;output=plan.IssuingKind!.Value;}
        else if(f.WangYunTrick is{Stage:"issuing-trick"}trick)
        {kind="trick";actor=f.OwnerSeat;expected=[trick.CardId];output=trick.SelectedKind!.Value;}
        else return;
        var roundPayment=f.WangYunPlan?.RoundOncePayment;
        if(expected.Count==0 && (kind!="slash"||roundPayment is null||!ValidWangYunRoundOnceHeader(roundPayment,action,actor)))throw new InvalidOperationException("Lianji zero native issuance lacks its strict independently paid source header.");
        if(use.WangYunNativeOrigin is not null || action.Type!=CardActionType.Use || action.ActorSeat!=actor || action.ProviderSeat!=actor || action.RequesterSeat is not null ||
            action.EffectiveKind!=output || !action.PhysicalCards.Select(c=>c.CardId).SequenceEqual(expected) || use.PhysicalCardIds is null || !use.PhysicalCardIds.SequenceEqual(expected) ||
            action.PhysicalCards.Any(c=>_cardZones.GetLocation(c.CardId)!=c.From || (kind=="weapon"?c.From!=CardLocation.DrawPile:c.From.OwnerSeat!=actor)))
            throw new InvalidOperationException("Wang Yun native issuance lost its actual actor, provider or accepted original materials.");
        var origin=new WangYunNativeUseOrigin{ProgramFrameId=f.Id,InstructionIndex=f.InstructionIndex,Source=WangYunSource(f),GameplayHash=f.GameplayHash,Turn=_turnNumber,
            PhaseInstance=_cardUseDebitPhaseInstanceId,Kind=kind,UseId=use.Id,ActionId=action.ActionId,Actor=actor,OutputKind=output,Materials=action.PhysicalCards,Targets=action.TargetSeats,Before=_movementSequence,After=_movementSequence,RoundOncePayment=roundPayment};
        ReplaceRuntimeFrame(f.Id,f.WangYunPlan is{}p?f with{WangYunPlan=p with{Stage=kind+"-issued",NativeUse=origin}}:f with{WangYunTrick=f.WangYunTrick! with{Stage="trick-issued",NativeUse=origin}});
        UpdateLifecycleCardUse(useId,current=>current with{WangYunNativeOrigin=origin});AdvanceEventRulesAndQueueFact(new WangYunNativeUseIssuedEvent(origin));
    }
    [ProgramCardUsePayment(ProgramCardUsePaymentStage.SlashPaymentCompleted, 10)]
    private void CompleteWangYunNativePayment(long useId)
    {
        if(LifecycleCardUse(useId)?.WangYunNativeOrigin is not{}old || old.PaymentCompleted)return;
        var f=_resolutionStack.OfType<ProgramSkillFrame>().Single(x=>x.Id==old.ProgramFrameId);var after=_movementSequence;
        if(old.Materials.Count==0 && (old.RoundOncePayment is null||LifecycleCardUse(useId)is not{}zero||zero.Action is not{}action||zero.RoundOnceCardlessBasicUse is not{}receipt||receipt.Payment!=old.RoundOncePayment||!IsIssuedRoundOnceCardlessBasicUse(receipt,zero,action)||!ValidWangYunRoundOnceHeader(old.RoundOncePayment,action,old.Actor)))throw new InvalidOperationException("Lianji zero native payment requires the actual independent Use receipt, never an empty material predicate.");
        if(old.Materials.Any(c=>_cardMovements.Count(m=>m.Sequence>old.Before && m.Sequence<=after && m.CardId==c.CardId && m.From==c.From && m.To==CardLocation.Processing)!=1))
            throw new InvalidOperationException("Wang Yun native use did not pay every accepted physical source entity exactly once.");
        var paid=old with{After=after,PaymentCompleted=true};ReplaceRuntimeFrame(f.Id,f.WangYunPlan is{}p?f with{WangYunPlan=p with{NativeUse=paid}}:f with{WangYunTrick=f.WangYunTrick! with{NativeUse=paid}});
        UpdateLifecycleCardUse(useId,use=>use with{WangYunNativeOrigin=paid});AdvanceEventRulesAndQueueFact(new WangYunNativePaymentCompletedEvent(paid));
    }
    private bool ValidWangYunNativeUse(CardUseFrame use)
    {
        if(use.WangYunNativeOrigin is not{}r || use.Id!=r.UseId || use.CardKind!=r.OutputKind || use.PhysicalCardIds is null ||
            !use.PhysicalCardIds.SequenceEqual(r.Materials.Select(c=>c.CardId)) || use.Action is not{}action || action.ActionId!=r.ActionId || action.Type!=CardActionType.Use ||
            action.ProviderSeat!=r.Actor || action.RequesterSeat is not null || !action.PhysicalCards.SequenceEqual(r.Materials) || action.EffectiveKind!=r.OutputKind || r.Before>r.After ||
            _resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.Id==r.ProgramFrameId) is not{}parent || r.Source!=WangYunSource(parent) || r.GameplayHash!=parent.GameplayHash ||
            r.InstructionIndex!=parent.InstructionIndex || r.Turn!=_turnNumber || r.PhaseInstance!=_cardUseDebitPhaseInstanceId)return false;
        var receipt=parent.WangYunPlan?.NativeUse??parent.WangYunTrick?.NativeUse;if(receipt is null || !WangYunOriginsEqual(r,receipt))return false;
        var issued=CompleteProgramEventHistory().OfType<WangYunNativeUseIssuedEvent>().Where(e=>e.Origin.UseId==r.UseId).ToArray();
        if(issued is not[var e] || !WangYunOriginsEqual(e.Origin,r with{After=r.Before,PaymentCompleted=false}))return false;
        var paid=CompleteProgramEventHistory().OfType<WangYunNativePaymentCompletedEvent>().Where(e=>e.Origin.UseId==r.UseId).ToArray();
        if(r.Materials.Count==0 && (r.RoundOncePayment is null||parent.WangYunPlan?.RoundOncePayment!=r.RoundOncePayment||!ValidWangYunRoundOnceStoredUse(use,r)))return false;
        if(!r.PaymentCompleted)return paid.Length==0;
        if(r.Materials.Count==0 && (use.RoundOnceCardlessBasicUse is not{}zeroReceipt||zeroReceipt.Payment!=r.RoundOncePayment||!ValidWangYunRoundOnceStoredUse(use,r)))return false;
        return paid is[var invoice] && WangYunOriginsEqual(invoice.Origin,r) && MatchesDeclaredActualDamageUse(use,action.ActorSeat,r.Actor) &&
            r.Materials.All(c=>_cardMovements.Count(m=>m.Sequence>r.Before && m.Sequence<=r.After && m.CardId==c.CardId && m.From==c.From && m.To==CardLocation.Processing)==1);
    }
    private bool ValidWangYunRoundOnceStoredUse(CardUseFrame use,WangYunNativeUseOrigin origin)
    {
        if(origin.RoundOncePayment is not{}payment||use.Action is not{}current)return false;
        var original=CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Select(e=>e.Action).FirstOrDefault(a=>a.ActionId==origin.ActionId&&a.Type==CardActionType.Use&&a.ActorSeat==origin.Actor&&a.ProviderSeat==origin.Actor&&a.EffectiveKind==CardKind.Slash&&a.PhysicalCards.Count==0&&a.ConversionChain is[var source]&&source==payment.Source);
        if(original is null)return ValidWangYunRoundOnceHeader(payment,current,origin.Actor)&&(!origin.PaymentCompleted||use.RoundOnceCardlessBasicUse is{}initialReceipt&&initialReceipt.Payment==payment&&IsIssuedRoundOnceCardlessBasicUse(initialReceipt,use,current));
        if(!ValidWangYunRoundOnceHeader(payment,original,origin.Actor))return false;
        if(origin.PaymentCompleted&&(use.RoundOnceCardlessBasicUse is not{}receipt||receipt.Payment!=payment||!IsIssuedRoundOnceCardlessBasicUse(receipt,use,original)))return false;
        return current.ActorSeat==origin.Actor||MatchesDeclaredActualDamageUse(use,current.ActorSeat,payment.Source.OwnerSeat);
    }
    private bool ValidWangYunRoundOnceHeader(RoundOnceCardlessBasicPaymentReceipt payment,CardActionContext action,int actor)=>
        payment.Source.OwnerSeat==actor&&payment.ActualTurnNumber==_turnNumber&&payment.ActualTurnOwnerSeat==_currentSeat&&payment.RoundNumber==_roundNumber&&payment.PhaseInstanceId==_cardUseDebitPhaseInstanceId&&action.Type==CardActionType.Use&&action.ActorSeat==actor&&action.ProviderSeat==actor&&action.RequesterSeat is null&&action.EffectiveKind==CardKind.Slash&&action.PhysicalCards.Count==0&&action.ConversionChain is[var source]&&source==payment.Source&&ViewAsRule(source)is{OutputKind:CardKind.Slash,ForPlay:true,ForResponse:false,RoundOnceCardlessBasic:{}policy}&&policy.UsageGroup==payment.UsageGroup&&_contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash==payment.GameplayHash&&CompleteProgramEventHistory().OfType<RoundOnceCardlessBasicQuotaPaidEvent>().Count(e=>e.Receipt==payment)==1;
    private bool WangYunCardUseRidesOn(CardUseFrame use,ProgramSkillFrame parent)=>use.WangYunNativeOrigin is{}r && r.ProgramFrameId==parent.Id && ValidWangYunNativeUse(use) && ValidWangYun(parent);
    // A native Slash still owns its AttackCompletionReceipt. Only that later
    // attack boundary may resume this parent after the once-returned use.
    private void ResumeWangYunCompletedNativeUse(CardUseFrame use)
    {
        if (use.WangYunNativeOrigin!.Kind != "slash")
            AdvanceRuntimeProgram(use.WangYunNativeOrigin.ProgramFrameId);
    }

    [NativeAttackReturn(NativeAttackReturnPhase.BeforeParentResume, 10)]
    private void ValidateWangYunNativeAttackReturn(AttackCompletionReceipt completion)
    {
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame frame &&
            completion.ProgramFrameId is { } frameId &&
            frame.WangYunPlan is { NativeUse: { Kind: "slash" } origin } plan &&
            (!plan.NativeReturned || origin.ProgramFrameId != frameId ||
             origin.UseId != completion.ResolutionId || !CanContinueWangYun(frame)))
            throw new InvalidOperationException("A Wang Yun Slash completion lost its exact once-returned native use.");
    }
    [NativeUsePreparation(20, 80, nameof(ResumeWangYunCompletedNativeUse))]
    private bool ReturnWangYunNativeUse(CardUseFrame use)
    {
        if(use.WangYunNativeOrigin is not{}r)return false;CompleteWangYunNativePayment(use.Id);use=LifecycleCardUse(use.Id)!;r=use.WangYunNativeOrigin!;
        if(!ValidWangYunNativeUse(use) || CompleteProgramEventHistory().OfType<WangYunNativeUseReturnedEvent>().Any(e=>e.UseId==use.Id))throw new InvalidOperationException("Wang Yun native child returned a changed or already consumed invoice.");
        var parent=_resolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.Id==r.ProgramFrameId);
        ReplaceRuntimeFrame(parent.Id,parent.WangYunPlan is{}p?parent with{WangYunPlan=p with{NativeReturned=true}}:parent with{WangYunTrick=parent.WangYunTrick! with{NativeReturned=true}});
        AdvanceEventRulesAndQueueFact(new WangYunNativeUseReturnedEvent(parent.Id,r.UseId,r.ActionId,r.Kind));return true;
    }
    private bool ValidWangYun(ProgramSkillFrame f)
    {
        if(f.WangYunPlan is{}p)
        {
            if(!WangYunActivationMatches(f,SkillProgramEffectOp.ResolveLianjiPaidWeaponAndSlash) || p.Source!=WangYunSource(f) || p.GameplayHash!=f.GameplayHash || p.Turn!=_turnNumber || p.PhaseInstance!=_cardUseDebitPhaseInstanceId ||
                p.InstructionIndex!=f.InstructionIndex || f.SelectedCardIds is not[var cost] || cost!=p.CostCardId || f.SelectedTargetSeats is not[var actor] || actor!=p.Actor || !IsValidPlayerSeat(actor) || actor==f.OwnerSeat)return false;
            var events=CompleteProgramEventHistory().ToArray();
            if(events.OfType<WangYunPlanStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is not[var started] || started!=new WangYunPlanStartedEvent(f.Id,p.Source,p.GameplayHash,p.Turn,p.PhaseInstance,actor,cost))return false;
            var moves=events.OfType<WangYunPlanMovementPaidEvent>().Where(e=>e.FrameId==f.Id).ToArray();
            if(moves.Count(e=>e.Next=="weapon")!=(p.CostPaid?1:0) || moves.Length>2 || moves.Any(e=>e.Before>e.After || _cardMovements.Count(m=>m.Sequence>e.Before && m.Sequence<=e.After && m.CardId==e.CardId && m.From==e.From && m.To==e.To && m.Reason.Value==WangYunPlanMoveReason)!=1) ||
                p.CostPaid && !moves.Any(e=>e.Next=="weapon" && e.CardId==cost && e.From==CardLocation.Hand(f.OwnerSeat) && e.To is var to && (to==CardLocation.DiscardPile || to==CardLocation.OutsideGame)))return false;
            if(p.Stage=="movement" && moves.Count(e=>e.Before==p.Before && e.After==p.After && e.Next==p.ReturnStage)!=1)return false;
            if(p.Stage is not("cost" or "movement" or "weapon" or "issuing-weapon" or "weapon-issued" or "target" or "branch" or "transfer" or "complete" or "issuing-slash" or "slash-issued"))return false;
            if(p.SlashTarget is{}target && (!IsValidPlayerSeat(target) || target==p.Actor))return false;
            return ValidWangYunStoredOrigin(f,p.NativeUse,p.NativeReturned,events);
        }
        if(f.WangYunTrick is{}t)
        {
            if(!WangYunActivationMatches(f,SkillProgramEffectOp.UseThreeRandomTricksFromSelectedMaterial) || t.Source!=WangYunSource(f) || t.GameplayHash!=f.GameplayHash || t.Turn!=_turnNumber || t.PhaseInstance!=_cardUseDebitPhaseInstanceId ||
                t.InstructionIndex!=f.InstructionIndex || !f.SelectedCardIds.SequenceEqual([t.CardId]) || f.SelectedTargetSeats.Count!=0 || t.From.OwnerSeat!=f.OwnerSeat || t.From.Zone is not(CardZoneKind.Hand or CardZoneKind.Equipment) ||
                t.Samples.Count!=3 || t.Samples.Distinct().Count()!=3 || t.Samples.Any(k=>!WangYunRandomTrickPool().Contains(k)) || t.Stage is not("choice" or "issuing-trick" or "trick-issued"))return false;
            var events=CompleteProgramEventHistory().ToArray();
            return events.OfType<WangYunRandomTricksStartedEvent>().Where(e=>e.FrameId==f.Id).ToArray() is[var started] &&
                started==new WangYunRandomTricksStartedEvent(f.Id,t.Source,t.GameplayHash,t.Turn,t.PhaseInstance,t.CardId,t.PrintedKind,t.From,WangYunSamplesFingerprint(t.Samples)) &&
                (t.SelectedKind is null || t.Samples.Contains(t.SelectedKind.Value)) && ValidWangYunStoredOrigin(f,t.NativeUse,t.NativeReturned,events);
        }
        if(f.WangYunAwakening is{}a)return a.Source==WangYunSource(f) && a.GameplayHash==f.GameplayHash && a.ParentFrameId==f.WindowContext?.ParentFrameId && a.Turn==_turnNumber && a.InstructionIndex==f.InstructionIndex &&
            f.WindowContext?.Window==SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && f.OwnerSeat==_currentSeat &&
            CompleteProgramEventHistory().OfType<WangYunAwakenedEvent>().Count(e=>e.FrameId==f.Id && e.OwnerSeat==f.OwnerSeat && e.LostSkillId=="ol:lianji" && e.AcquiredSkillId=="ol:jingong")==1;
        return false;
    }
    private bool ValidWangYunStoredOrigin(ProgramSkillFrame f,WangYunNativeUseOrigin?origin,bool returned,IReadOnlyList<IGameEvent>events)
    {
        if(origin is null)return !returned;
        if(origin.ProgramFrameId!=f.Id || origin.Source!=WangYunSource(f) || origin.GameplayHash!=f.GameplayHash || origin.InstructionIndex!=f.InstructionIndex || origin.Turn!=_turnNumber || origin.PhaseInstance!=_cardUseDebitPhaseInstanceId)return false;
        var issued=events.OfType<WangYunNativeUseIssuedEvent>().Where(e=>e.Origin.UseId==origin.UseId).ToArray();if(issued is not[var first] || !WangYunOriginsEqual(first.Origin,origin with{After=origin.Before,PaymentCompleted=false}))return false;
        var payments=events.OfType<WangYunNativePaymentCompletedEvent>().Where(e=>e.Origin.UseId==origin.UseId).ToArray();
        if(origin.PaymentCompleted?payments is not[var paid] || !WangYunOriginsEqual(paid.Origin,origin):payments.Length!=0)return false;
        return events.OfType<WangYunNativeUseReturnedEvent>().Count(e=>e.ProgramFrameId==f.Id && e.UseId==origin.UseId && e.ActionId==origin.ActionId && e.Kind==origin.Kind)==(returned?1:0);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 180)]
    private void AssertWangYun(ProgramSkillFrame f)
    {
        if(f.WangYunPlan is null && f.WangYunTrick is null && f.WangYunAwakening is null)return;
        if(!ValidWangYun(f))throw new InvalidOperationException("Wang Yun lost the exact hand cost, native actor/material use, private three samples or causal awakening receipt.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 150)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 150)]
    private bool CanContinueWangYun(ProgramSkillFrame f)=>ValidWangYun(f) && (f.WangYunPlan is{CostPaid:true} || f.WangYunTrick?.NativeUse is not null || f.WangYunAwakening is not null);
    private bool WangYunFirstChild(ProgramSkillFrame f,ResolutionFrame child)
    {
        if(!ValidWangYun(f))return false;
        if(child is CardUseFrame use)return WangYunCardUseRidesOn(use,f);
        if(child is CardsMovedTriggerWindowFrame moved && f.WangYunPlan is{Stage:"movement"}p)return moved.Batch.ParentFrameId==f.Id && moved.Batch.AwaitingProgramFrameId==f.Id &&
            moved.Batch.OriginOwnerSeat==f.OwnerSeat && moved.Batch.OriginSkillId==f.SkillId && moved.Batch.OriginSkillInstanceId==f.SkillInstanceId && moved.Batch.Movements.All(m=>_cardMovements.Contains(m) && m.Sequence>p.Before && m.Sequence<=p.After);
        if(child is ProgramLifecycleTriggerWindowFrame skills)return skills.Window==SkillProgramTriggerWindow.SkillsChanged && skills.ResumeProgramFrameId==f.Id && skills.Continuation==ProgramLifecycleContinuation.ResumeParentProgram;
        if(child is HpChangedTriggerWindowFrame hp)return hp.ResumeFrameId==f.Id && hp.Change.ParentFrameId==f.Id && hp.Continuation==PostEventContinuation.Program;
        return false;
    }
    private ProgramSkillFrame? WangYunObserverRoot()=>FindProgramObserverRoot(static(e,r,c)=>e.WangYunFirstChild(r,c),static(e,i)=>e.SameNameHandObserverSuffix(i));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 90)]
    private bool IsWangYunDying()=>ActiveDying is{}d && WangYunObserverRoot() is{}r && _resolutionStack.FindIndex(f=>f.Id==d.Id)>_resolutionStack.FindIndex(f=>f.Id==r.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 1540)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 1650)]
    private bool HasWangYunDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is DamageTriggerWindowFrame) && WangYunObserverRoot() is not null;
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 1650)]
    private bool HasWangYunBeforeDamageObserver(long id)=>_resolutionStack.Any(f=>f.Id==id && f is BeforeDamageProgramWindowFrame) && WangYunObserverRoot() is not null;
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 190)]
    private bool TryAdvanceWangYunSubtree()=>_pendingDecision is null && WangYunObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 430)]
    private bool AllowsWangYunNestedDamage(ProgramSkillFrame f,int target,int amount,ProgramParticipantReference?source,DamageNature?nature,bool sourceLess)
    {
        if(sourceLess || f.AttackAttempt is not null || f.InstructionIndex<1 || _resolutionStack.LastOrDefault()?.Id!=f.Id || WangYunObserverRoot() is null)return false;
        var e=WangYunPausedEffect(f)!;return e.Op==SkillProgramEffectOp.Damage && amount==e.Amount && source==e.ActorReference && nature==e.DamageNature &&
            target==(e.TargetReference is{}r?ResolveProgramParticipant(f,r):ResolveProgramEffectTarget(f,e.Target));
    }
}
