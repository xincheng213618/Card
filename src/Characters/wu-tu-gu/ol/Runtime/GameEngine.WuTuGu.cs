namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasWuTuGuCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ArmHanyongNativeGroupDamage)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AccumulateRanshangFirePoint)||_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveRanshangEndingHpLoss);
    private static CardConversionSource WuTuGuSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    [ProgramCapability]private sealed class WuTuGuCapability(GameEngine e):IWuTuGuHost{public SkillProgramStepOutcome Resolve(ProgramSkillFrame f,SkillProgramEffectOp op)=>e.BeginWuTuGu(f,op);}
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27320)]
    private void CaptureWuTuGuHanyongUse(IGameEvent payload)
    {
        if(!HasWuTuGuCapability||payload is not CardUseDeclaredEvent declared||LifecycleCardUse(declared.ResolutionId)is not{Action:{Type:CardActionType.Use}action}use||
            use.CardId!=declared.CardId||use.SourceSeat!=declared.SourceSeat||action.ActorSeat!=use.SourceSeat||action.EffectiveKind!=use.CardKind||use.CardKind is not(CardKind.BarbarianAssault or CardKind.ArrowBarrage)||!_players[use.SourceSeat].IsAlive)return;
        foreach(var binding in GetSkillBindingShard(_players[use.SourceSeat]).GetInstanceTriggers(SkillProgramTriggerWindow.CardUseCommitted).Where(b=>b.Trigger.Effects is[{Op:SkillProgramEffectOp.ArmHanyongNativeGroupDamage}]))
        {
            var source=new CardConversionSource(binding.SkillId,binding.Trigger.Id,use.SourceSeat,binding.SkillInstanceId);
            if(ProgramEventHistory<WuTuGuHanyongQualifiedEvent>().Any(e=>e.UseId==use.Id&&e.Source==source))continue;
            AdvanceEventRulesAndQueueFact(new WuTuGuHanyongQualifiedEvent(use.Id,action.ActionId,source,binding.Program.GameplayHash,_turnNumber,_roundNumber,_players[use.SourceSeat].Hp,action.ProviderSeat,use.CardKind));
        }
    }
    private WuTuGuHanyongQualifiedEvent? WuTuGuHanyongQualification(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(context.Window!=SkillProgramTriggerWindow.CardUseCommitted||context.CardUse is not{}card||context.OwnerSeat!=c.OwnerSeat||card.ActorSeat!=c.OwnerSeat||LifecycleCardUse(card.ParentCardUseFrameId)is not{Action:{}a}use||a.ActionId!=card.CardActionId)return null;
        var source=new CardConversionSource(c.SkillId,c.BindingId,c.OwnerSeat,c.SkillInstanceId);
        return ProgramEventHistory<WuTuGuHanyongQualifiedEvent>().SingleOrDefault(e=>e.UseId==use.Id&&e.ActionId==a.ActionId&&e.Source==source&&e.GameplayHash==c.GameplayHash&&e.Kind==use.CardKind&&e.ProviderSeat==a.ProviderSeat&&e.ActualTurn==_turnNumber&&e.Round>0&&e.HpAtUse<e.Round);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27320)]
    private bool CanOfferWuTuGu(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>
        t.Effects is[{Op:SkillProgramEffectOp.ArmHanyongNativeGroupDamage}]?WuTuGuHanyongQualification(c,context)is not null:t.Effects is not[{Op:SkillProgramEffectOp.ResolveRanshangEndingHpLoss}]||_players[c.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.RanshangRan)>0;
    private SkillProgramStepOutcome BeginWuTuGu(ProgramSkillFrame supplied,SkillProgramEffectOp op)
    {
        var f=GetActiveProgramFrame(supplied.Id);
        if(f.InstructionIndex!=1||f.TriggerId is null||f.ActivationId!=f.TriggerId||f.SelectedCardIds.Count!=0||f.SelectedTargetSeats.Count!=0||f.WindowContext is not{}context||GetPausedPrivateOfferEffect(f)?.Op!=op)
            throw new InvalidOperationException("Wu Tu Gu requires the exact original native binding.");
        if(op==SkillProgramEffectOp.ArmHanyongNativeGroupDamage)
        {
            if(f.SkillId!="ol:hanyong"||context.Window!=SkillProgramTriggerWindow.CardUseCommitted||_resolutionStack.Count<2||_resolutionStack[^2]is not ProgramCardTriggerWindowFrame parent||parent.Id!=context.ParentFrameId||
                parent.CandidateIndex<0||parent.CandidateIndex>=parent.Candidates.Count||!MountObserverCandidateMatches(f,ToSharedCandidate(parent.Candidates[parent.CandidateIndex]))||
                WuTuGuHanyongQualification(ToSharedCandidate(parent.Candidates[parent.CandidateIndex]),context)is not{}q)throw new InvalidOperationException("Hanyong lost its frozen native-use HP/round qualification.");
            var use=LifecycleCardUse(q.UseId)!;if(use.WuTuGuHanyong?.Grants.Any(g=>g.Source==q.Source)==true)throw new InvalidOperationException("Hanyong cannot arm one original source twice.");
            var grant=new WuTuGuHanyongGrant(f.Id,q.UseId,q.ActionId,q.Source,q.GameplayHash,q.ActualTurn,q.Round,q.HpAtUse,q.ProviderSeat,q.Kind);
            UpdateLifecycleCardUse(use.Id,u=>u with{WuTuGuHanyong=new(){Grants=(u.WuTuGuHanyong?.Grants??[]).Append(grant).ToArray()}});AdvanceEventRulesAndQueueFact(new WuTuGuHanyongArmedEvent(grant));return SkillProgramStepOutcome.Continue;
        }
        if(op==SkillProgramEffectOp.AccumulateRanshangFirePoint)
        {
            if(f.SkillId!="ol:ranshang"||context.Window!=SkillProgramTriggerWindow.AfterDamageApplied||context.TargetSeat!=f.OwnerSeat||context.Amount<=0||
                _resolutionStack.Count<3||_resolutionStack[^2]is not DamageTriggerWindowFrame damage||damage.Id!=context.ParentFrameId||damage.ParentFrameId!=context.DamageFrameId||damage.TargetSeat!=f.OwnerSeat||damage.TriggerWindow!=context.Window||
                damage.CandidateIndex<0||damage.CandidateIndex>=damage.Candidates.Count||!MountObserverCandidateMatches(f,damage.Candidates[damage.CandidateIndex].ToProgramCandidate())||
                GetDamageNature(GetDamageTriggerAttack(damage))!=DamageNature.Fire||!GetDamageTriggerAttack(damage).DamageWasApplied||context.OccurrenceIndex<0||context.OccurrenceIndex>=context.Amount)
                throw new InvalidOperationException("Ranshang requires one exact point of truly applied native fire damage.");
            var source=WuTuGuSource(f);if(ProgramEventHistory<WuTuGuFirePointAddedEvent>().Any(e=>e.DamageFrameId==context.DamageFrameId&&e.Source==source&&e.Point==context.OccurrenceIndex))throw new InvalidOperationException("Ranshang cannot count a fire point twice.");
            var count=checked(_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.RanshangRan)+1);SetStrategicMarker(f.OwnerSeat,PlayerMarkerKind.RanshangRan,f.OwnerSeat,count,f);
            AdvanceEventRulesAndQueueFact(new WuTuGuFirePointAddedEvent(f.Id,context.DamageFrameId!.Value,context.ParentFrameId,source,f.GameplayHash,context.OccurrenceIndex,context.Amount,count));return SkillProgramStepOutcome.Continue;
        }
        if(op!=SkillProgramEffectOp.ResolveRanshangEndingHpLoss||f.SkillId!="ol:ranshang"||f.WuTuGuRanshang is not null||!WuTuGuEndingParentMatches(f))throw new InvalidOperationException("Ranshang requires its original actual Ending binding.");
        var amount=_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.RanshangRan);if(amount<=0)return SkillProgramStepOutcome.Continue;
        var r=new WuTuGuRanshangReceipt(WuTuGuSource(f),f.GameplayHash,context.ParentFrameId,_turnNumber,amount,WuTuGuRanshangStage.Issuing);
        ReplaceRuntimeTop(f=f with{WuTuGuRanshang=r});AdvanceEventRulesAndQueueFact(new WuTuGuRanshangIssuedEvent(f.Id,r.Source,r.GameplayHash,r.ParentId,r.ActualTurn,r.Amount));ContinueWuTuGu(f.Id);return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ValidWuTuGuHanyongUse(CardUseFrame use)=>use.WuTuGuHanyong is{Grants.Count:>0}r&&use.Action is{Type:CardActionType.Use}a&&r.Grants.Select(g=>g.Source).Distinct().Count()==r.Grants.Count&&r.Grants.All(g=>
        g.UseId==use.Id&&g.ActionId==a.ActionId&&g.Kind==use.CardKind&&a.EffectiveKind==g.Kind&&g.Kind is(CardKind.BarbarianAssault or CardKind.ArrowBarrage)&&g.ProviderSeat==a.ProviderSeat&&g.Round>0&&g.HpAtUse<g.Round&&
        ShownEntityUseActorMatches(use,g.Source.OwnerSeat,g.ProviderSeat)&&ProgramEventHistory<WuTuGuHanyongArmedEvent>().Count(e=>e.Grant==g)==1&&
        ProgramEventHistory<WuTuGuHanyongQualifiedEvent>().Count(e=>e.UseId==g.UseId&&e.ActionId==g.ActionId&&e.Source==g.Source&&e.GameplayHash==g.GameplayHash&&e.ActualTurn==g.ActualTurn&&e.Round==g.Round&&e.HpAtUse==g.HpAtUse&&e.ProviderSeat==g.ProviderSeat&&e.Kind==g.Kind)==1);
    [NativeUseValidation(27320)]private void AssertWuTuGuNativeUse(CardUseFrame use){if(use.WuTuGuHanyong is not null&&!ValidWuTuGuHanyongUse(use))throw new InvalidOperationException("Hanyong lost its exact once-accepted native use, original source or frozen HP/round.");}
    [ProgramDamageHook(ProgramDamagePhase.AdditiveAmount, 170)]
    private int WuTuGuHanyongDamageBonus(IDamageAttempt attack)
    {
        if(!HasWuTuGuCapability||attack.IsChainPropagation||LifecycleCardUse(attack.ResolutionId)is not{WuTuGuHanyong:not null}use)return 0;
        if(!ValidWuTuGuHanyongUse(use)||ActiveGroupCard is not{}group||group.ResolutionId!=use.Id||group.Effect!=GroupCardEffect.ResponseAttack||attack.EffectiveCardKind!=use.CardKind||!group.TargetSeats.Contains(attack.TargetSeat))throw new InvalidOperationException("Hanyong damage must remain on the original native group-use target cursor.");
        return use.WuTuGuHanyong!.Grants.Count;
    }
    private bool WuTuGuEndingParentMatches(ProgramSkillFrame f)=>f.WindowContext is{Window:SkillProgramTriggerWindow.TurnEnding,TargetSeat:{}target,SourceSeat:{}source}c&&target==f.OwnerSeat&&source==f.OwnerSeat&&f.OwnerSeat==_currentSeat&&
        _resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is{}end&&end.OwnerSeat==f.OwnerSeat&&end.TurnNumber==_turnNumber&&end.ItemIndex>=0&&end.ItemIndex<end.Items.Count&&end.Items[end.ItemIndex].Candidate is{}candidate&&MountObserverCandidateMatches(f,candidate);
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,27320)]
    private void CaptureWuTuGuRanshangPayment(ProgramSkillHpLostEvent paid)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WuTuGuRanshang:{Stage:WuTuGuRanshangStage.Issuing}r}f||paid.FrameId!=f.Id||paid.SkillId!=f.SkillId||paid.TargetSeat!=f.OwnerSeat||paid.Amount!=r.Amount)return;
        ReplaceRuntimeTop(f with{WuTuGuRanshang=r with{Stage=WuTuGuRanshangStage.HpChildren}});
        AdvanceEventRulesAndQueueFact(new WuTuGuRanshangPaidEvent(f.Id,paid.Amount,paid.RemainingHp));
    }
    private void ContinueWuTuGu(long id)
    {
        if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WuTuGuRanshang:{}r}f||f.Id!=id)return;AssertWuTuGuRanshang(f);
        if(r.Stage==WuTuGuRanshangStage.Issuing)
        {
            // Use the existing native unclamped LoseHp primitive: one actual
            // loss of X, one Dying procedure, never one rescue window per mark.
            _=ExecuteParticipantReserve(new SkillProgramEffect(SkillProgramEffectOp.LoseHpUnclamped,SkillProgramEffectTarget.Owner,r.Amount,new(SkillProgramConditionKind.Always,0,[])),f);
            var owner=_resolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(p=>p.Id==id);if(owner is null)return;
            if(_resolutionStack.LastOrDefault()?.Id!=id)return;f=GetActiveProgramFrame(id);r=f.WuTuGuRanshang!;if(r.Stage==WuTuGuRanshangStage.Issuing)return;
        }
        if(TryBeginCharacterStateProgramWindow(id,CharacterStateContinuation.Program)||TryBeginHpChangedProgramWindow(id,PostEventContinuation.Program)||TryBeginCardsMovedProgramWindow(id)||TryBeginAdvancedSkillsChanged(id))return;
        f=GetActiveProgramFrame(id);AdvanceEventRulesAndQueueFact(new WuTuGuRanshangCompletedEvent(id));ReplaceRuntimeTop(f=f with{WuTuGuRanshang=null});FinishProgramSkill(f,true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume,27320)]private bool ResumeWuTuGu(long id){if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{WuTuGuRanshang:not null}f||f.Id!=id)return false;ContinueWuTuGu(id);return true;}
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss,27320)][ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath,27320)]private bool ContinuePaidWuTuGu(ProgramSkillFrame f)=>f.WuTuGuRanshang is{Stage:WuTuGuRanshangStage.HpChildren}&&ValidWuTuGuRanshang(f);
}
