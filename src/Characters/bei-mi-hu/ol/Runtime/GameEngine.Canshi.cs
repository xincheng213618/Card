using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedInstantTrickRequirement, 70)]
    private bool KuiCancelTargetFinalizedInstantTrickRequirementTargetWindow(ProgramTargetCardWindowQuery query) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget);

    [ProgramTargetWindow(ProgramTargetWindowStage.FinalizedInstantTrickRequirement, 80)]
    private bool KuiAppendTargetFinalizedInstantTrickRequirementTargetWindow(ProgramTargetCardWindowQuery query) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ConsumeKuiAppendLegalTargets);

    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 160)]
    private bool KuiCancelTargetNormalizeImplicitDrawTwoTargetWindow(ProgramTargetCardWindowQuery query) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget);

    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 170)]
    private bool KuiAppendTargetNormalizeImplicitDrawTwoTargetWindow(ProgramTargetCardWindowQuery query) =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ConsumeKuiAppendLegalTargets);


    private static int[] BeiMiHuPrimaryTargets(CardUseFrame use)=>(use.CardKind==CardKind.DrawTwo && use.TargetSeats.Count==0?[use.SourceSeat]:
        use.CardKind==CardKind.BorrowedSword?use.TargetSeats.Where((_,i)=>i%2==0):use.TargetSeats).Distinct().ToArray();
    private bool TryGetBeiMiHuCardUse(int owner,ProgramSkillWindowContext context,SkillProgramEffectOp op,out CardUseFrame use,out ProgramCardTriggerWindowFrame window)
    {
        use=null!;window=null!;
        if(context is not{Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,CardUse:{}identity} ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId) is not{}p || p.ParentFrameId!=identity.ParentCardUseFrameId ||
            GetCardActionWindow(p)!=context.Window || p.CompletedResponseReturn is not null || p.Continuation is not(ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard) ||
            LifecycleCardUse(p.ParentFrameId) is not{Action:{Type:CardActionType.Use}action}native || native.Step==ResolutionFrameStep.Completed ||
            action.ActionId!=identity.CardActionId || action.ActorSeat!=native.SourceSeat || action.EffectiveKind!=native.CardKind || p.Action.ActionId!=action.ActionId ||
            !action.TargetSeats.SequenceEqual(native.TargetSeats) || !p.Action.TargetSeats.SequenceEqual(native.TargetSeats) || native.DyingResponse is not null || action.ResponderSeat is not null ||
            GetProgramCardCategory(native.CardKind)!=SkillProgramCardCategory.Basic && !IsOrdinaryTrick(native.CardKind) ||
            native.CardKind==CardKind.BorrowedSword && native.TargetSeats.Count%2!=0 || BeiMiHuPrimaryTargets(native) is not[var sole] ||
            op==SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget && (sole!=owner || _players[action.ActorSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Kui)!=1) ||
            op==SkillProgramEffectOp.ConsumeKuiAppendLegalTargets && action.ActorSeat!=owner)return false;
        var index=_resolutionStack.FindIndex(f=>f.Id==p.Id);if(index<1 || _resolutionStack[index-1].Id!=native.Id)return false;
        if(!CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==native.Id && e.CardId==native.CardId))return false;
        use=native;window=p;return true;
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 400)]
    private bool CanOfferBeiMiHuCardAction(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext context)
    {
        var op=BeiMiHuOperation(trigger);if(op is not(SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget or SkillProgramEffectOp.ConsumeKuiAppendLegalTargets))return true;
        if(!TryGetBeiMiHuCardUse(candidate.OwnerSeat,context,op.Value,out var use,out var window) || !DesignatedExtraTargetCandidateMatches(candidate,context,window) ||
            CompleteProgramEventHistory().OfType<BeiMiHuTargetsPaidEvent>().Any(e=>e.Receipt.CardUseId==use.Id && e.Receipt.Source==new CardConversionSource(candidate.SkillId,candidate.BindingId,candidate.OwnerSeat,candidate.SkillInstanceId)))return false;
        return op==SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget || BeiMiHuTargetGroups(use).Count>0;
    }
    private CardUseFrame RequireBeiMiHuCardParent(ProgramSkillFrame f,out ProgramCardTriggerWindowFrame window)
    {
        var op=BeiMiHuPausedEffect(f)?.Op;if(f.TriggerId is null || f.WindowContext is not{}context || op is null ||
            !TryGetBeiMiHuCardUse(f.OwnerSeat,context,op.Value,out var use,out window) ||
            !DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat,f.SkillId,f.TriggerId,f.SkillInstanceId,f.GameplayHash,0),context,window))throw new InvalidOperationException("Canshi lost its exact sole native designation parent.");
        var index=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(index<2 || _resolutionStack[index-1].Id!=window.Id || _resolutionStack[index-2].Id!=use.Id || !window.Activated)throw new InvalidOperationException("Canshi requires its immediate native window and source candidate.");return use;
    }
    private bool BeiMiHuTargetGroupLegal(CardUseFrame use,IReadOnlyList<int> group)
    {
        if(group.Count!=(use.CardKind==CardKind.BorrowedSword?2:1) || !IsValidPlayerSeat(group[0]) || _players[group[0]].Markers.GetValueOrDefault(PlayerMarkerKind.Kui)!=1 ||
            !CanBeDistanceFreeDesignatedTarget(use,_players[group[0]]))return false;
        var action=use.Action!;var actor=_players[action.ActorSeat];var target=_players[group[0]];
        if(IsSlashCard(use.CardKind) && !CanUseSlashTarget(actor,target,new(-1,use.CardKind,action.EffectiveSuit??Suit.None,action.EffectiveRank??0),action.ConversionChain.FirstOrDefault(),use.CardKind,
            existingUseFrameId:use.Id,specificEffectiveRank:action.EffectiveRank,physicalCardIds:action.PhysicalCards.Select(c=>c.CardId).ToArray()))return false;
        if(use.CardKind==CardKind.Snatch && !HasCardPolicy(actor,SkillProgramCardPolicyKind.IgnoreUseDistance,CardKind.Snatch) && !HasCardDistanceExemption(actor,target,CardKind.Snatch) &&
            !use.GuanFirstRedDistance && !HasPhaseSuitAllowance(actor.Seat,action.EffectiveSuit) && !HasProvenanceUseDistance(actor,action.PhysicalCards.Select(c=>c.CardId).ToArray()) &&
            !HasGrantedPhaseEntityDistance(actor,action.PhysicalCards.Select(c=>c.CardId).ToArray()) && !action.ConversionChain.Any(s=>ViewAsRule(s)?.DistanceUnlimited==true) && GetCombatDistance(actor.Seat,target.Seat)!=1)return false;
        if(use.CardKind==CardKind.Peach && !target.HasHpValue)return false;
        return group.Count==1 || IsValidPlayerSeat(group[1]) && IsLegalBorrowedSwordSlashTarget(target,_players[group[1]]);
    }
    private IReadOnlyList<IReadOnlyList<int>> BeiMiHuTargetGroups(CardUseFrame use)
    {
        var result=new List<IReadOnlyList<int>>();foreach(var target in _players.Where(p=>p.IsAlive && p.Markers.GetValueOrDefault(PlayerMarkerKind.Kui)==1))
            if(use.CardKind==CardKind.BorrowedSword){foreach(var victim in _players.Where(p=>p.IsAlive)){int[]pair=[target.Seat,victim.Seat];if(BeiMiHuTargetGroupLegal(use,pair))result.Add(Array.AsReadOnly(pair));}}
            else{int[]single=[target.Seat];if(BeiMiHuTargetGroupLegal(use,single))result.Add(Array.AsReadOnly(single));}
        return Array.AsReadOnly(result.ToArray());
    }
    private SkillProgramStepOutcome BeginBeiMiHuCardTargets(ProgramSkillFrame f,SkillProgramEffectOp op)
    {
        var use=RequireBeiMiHuCardParent(f,out var window);if(!CanOfferBeiMiHuCardAction(new(f.OwnerSeat,f.SkillId,f.TriggerId!,f.SkillInstanceId,f.GameplayHash,0),GetProgramTrigger(f),f.WindowContext!))throw new InvalidOperationException("Canshi lost its original marked action qualification.");
        var r=NewBeiMiHuReceipt(f,op==SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget?"cancel-paid":"append-choice") with{CardUseId=use.Id,ActionId=use.Action!.ActionId,OriginalTargets=use.TargetSeats};
        ReplaceRuntimeTop(f=f with{BeiMiHu=r});
        if(op==SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget)
        {
            PayBeiMiHuKui(f,use.SourceSeat,-1,"cancel");ApplyBeiMiHuTargetMutation(f,use,window,[],[use.SourceSeat],"cancel");AdvanceRuntimeProgram(f.Id);return SkillProgramStepOutcome.AwaitChild;
        }
        var choices=BeiMiHuTargetGroups(use).Select(group=>new PromptChoice(new($"bei-mi-hu.{f.Id}.append-{string.Join('-',group)}"),group.Count==2?$"追加 {_players[group[0]].Name}，令其对 {_players[group[1]].Name} 使用杀":$"追加 {_players[group[0]].Name}",[],group,
            new Dictionary<string,string>{["program-action"]="bei-mi-hu",["frame-id"]=f.Id.ToString(CultureInfo.InvariantCulture),["branch"]="append"})).ToArray();
        ReplaceRuntimeTop(f=f with{BeiMiHu=r with{Choices=choices,Candidates=choices.Select(c=>c.Targets[0]).Distinct().ToArray()}});
        AdvanceEventRulesAndQueueFact(new BeiMiHuChoiceStartedEvent(f.Id,r.Source,r.GameplayHash,r.ParentFrameId,r.Turn,r.Round,"append",f.BeiMiHu!.Candidates));PublishBeiMiHuChoice(f);return SkillProgramStepOutcome.AwaitChoice;
    }
    private void CompleteBeiMiHuTargetAddition(ProgramSkillFrame f)
    {
        var r=f.BeiMiHu!;var use=RequireBeiMiHuCardParent(f,out var window);
        if(r.SelectedTargets.Count==0){FinishBeiMiHu(f);return;}
        var width=use.CardKind==CardKind.BorrowedSword?2:1;var groups=r.SelectedTargets.Chunk(width).Select(g=>(IReadOnlyList<int>)g).ToArray();
        if(groups.Any(g=>!BeiMiHuTargetGroupLegal(use,g)) || groups.Select(g=>g[0]).Distinct().Count()!=groups.Length)throw new InvalidOperationException("Canshi lost a selected legal marked primary target.");
        var primary=groups.Select(g=>g[0]).ToArray();var original=r.OriginalTargets;if(use.CardKind==CardKind.DrawTwo && original.Count==0)original=[use.SourceSeat];
        ReplaceRuntimeTop(f=f with{BeiMiHu=r with{Stage="append-paid"}});foreach(var seat in primary)PayBeiMiHuKui(f,seat,-1,"append");
        ApplyBeiMiHuTargetMutation(f,use,window,original.Concat(r.SelectedTargets).ToArray(),primary,"append");AdvanceRuntimeProgram(f.Id);
    }
    private void ApplyBeiMiHuTargetMutation(ProgramSkillFrame f,CardUseFrame use,ProgramCardTriggerWindowFrame window,IReadOnlyList<int> targets,IReadOnlyList<int> paid,string kind)
    {
        var r=f.BeiMiHu!;var receipt=new BeiMiHuTargetMutation(f.Id,use.Id,use.Action!.ActionId,r.Source,r.GameplayHash,r.Turn,kind){OriginalAction=use.Action,BeforeTargets=r.OriginalTargets,AfterTargets=targets,PaidMarkerSeats=paid};
        var action=CloneDesignatedExtraTargetAction(use.Action!,targets);var simple=use.AdjustedSimpleContinuation;
        if(kind=="append" && window.Continuation==ProgramCardContinuation.FinalizedSimpleCard)simple??=window.SimpleContinuation??throw new InvalidOperationException("Canshi basic use lost its native continuation.");
        UpdateProgramRoleCardUse(use with{TargetSeats=receipt.AfterTargets,Action=action,TargetIndex=0,AdjustedSimpleContinuation=simple,TargetsAdjusted=true,
            SlashTargetsCancelled=IsSlashCard(use.CardKind) && targets.Count==0,Enhancements=kind=="append"?use.Enhancements|CurrentCardEnhancement.ExtraTarget:use.Enhancements,BeiMiHuTargetMutation=receipt},action);
        AdvanceEventRulesAndQueueFact(new BeiMiHuTargetsPaidEvent(receipt));
    }
    private bool ValidBeiMiHuTargetMutation(CardUseFrame use)
    {
        if(use.BeiMiHuTargetMutation is not{}r || use.Id!=r.CardUseId || use.Action is not{}a || a.ActionId!=r.ActionId || !a.TargetSeats.SequenceEqual(r.AfterTargets) || !use.TargetSeats.SequenceEqual(r.AfterTargets) ||
            r.Turn!=_turnNumber || _contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash!=r.GameplayHash || r.Kind is not("cancel" or "append") || r.OriginalAction is not{}originalAction ||
            !originalAction.TargetSeats.SequenceEqual(r.BeforeTargets) || !BeiMiHuActionIdentityEqual(originalAction,a) ||
            !ActualHandGainMutationBinding(r.ProgramFrameId,r.Source.SkillId,r.Source.OwnerSeat,SkillProgramTriggerWindow.CardUseTargetsFinalized,
             r.Kind=="cancel"?SkillProgramEffectOp.ConsumeKuiCancelSoleIncomingTarget:SkillProgramEffectOp.ConsumeKuiAppendLegalTargets) ||
            CompleteProgramEventHistory().OfType<ProgramBindingStartedEvent>().Count(e=>e.FrameId==r.ProgramFrameId && e.SkillInstanceId==r.Source.SkillInstanceId && e.BindingId==r.Source.BindingId)!=1)return false;
        var events=CompleteProgramEventHistory().OfType<BeiMiHuTargetsPaidEvent>().Where(e=>e.Receipt.ProgramFrameId==r.ProgramFrameId).ToArray();
        if(events is not[var paid] || !BeiMiHuMutationEqual(paid.Receipt,r))return false;
        var costs=CompleteProgramEventHistory().OfType<BeiMiHuKuiPaidEvent>().Where(e=>e.FrameId==r.ProgramFrameId && e.Kind==r.Kind).ToArray();
        if(costs.Length!=r.PaidMarkerSeats.Count || costs.Any(e=>e.Source!=r.Source || e.GameplayHash!=r.GameplayHash || e.Turn!=r.Turn || e.Delta!=-1 || e.Remaining!=0) || !costs.Select(e=>e.TargetSeat).SequenceEqual(r.PaidMarkerSeats))return false;
        if(r.Kind=="cancel")return (use.CardKind==CardKind.DrawTwo && r.BeforeTargets.Count==0?new[]{a.ActorSeat}:use.CardKind==CardKind.BorrowedSword?r.BeforeTargets.Where((_,i)=>i%2==0):r.BeforeTargets).SequenceEqual([r.Source.OwnerSeat]) &&
            r.AfterTargets.Count==0 && r.PaidMarkerSeats.SequenceEqual([a.ActorSeat]);
        var original=use.CardKind==CardKind.DrawTwo && r.BeforeTargets.Count==0?new[]{a.ActorSeat}:r.BeforeTargets;
        return r.AfterTargets.Take(original.Count).SequenceEqual(original) && r.AfterTargets.Skip(original.Count).Where((_,i)=>use.CardKind!=CardKind.BorrowedSword || i%2==0).SequenceEqual(r.PaidMarkerSeats);
    }
    private static bool BeiMiHuMutationEqual(BeiMiHuTargetMutation x,BeiMiHuTargetMutation y)=>x.ProgramFrameId==y.ProgramFrameId && x.CardUseId==y.CardUseId && x.ActionId==y.ActionId && x.Source==y.Source &&
        x.GameplayHash==y.GameplayHash && x.Turn==y.Turn && x.Kind==y.Kind && BeiMiHuActionIdentityEqual(x.OriginalAction,y.OriginalAction) && x.BeforeTargets.SequenceEqual(y.BeforeTargets) && x.AfterTargets.SequenceEqual(y.AfterTargets) && x.PaidMarkerSeats.SequenceEqual(y.PaidMarkerSeats);
    private static bool BeiMiHuActionIdentityEqual(CardActionContext x,CardActionContext y)=>x is not null && y is not null && x.ActionId==y.ActionId && x.ParentActionId==y.ParentActionId && x.Type==y.Type &&
        x.ActorSeat==y.ActorSeat && x.ProviderSeat==y.ProviderSeat && x.RequesterSeat==y.RequesterSeat && x.ResponderSeat==y.ResponderSeat && x.OpponentSeat==y.OpponentSeat && x.EffectiveKind==y.EffectiveKind &&
        x.PhysicalCards.SequenceEqual(y.PhysicalCards) && x.ConversionChain.SequenceEqual(y.ConversionChain) && x.EffectiveSuit==y.EffectiveSuit && x.EffectiveRank==y.EffectiveRank && x.EffectiveIsRed==y.EffectiveIsRed && x.FactionOrigin==y.FactionOrigin;
}
