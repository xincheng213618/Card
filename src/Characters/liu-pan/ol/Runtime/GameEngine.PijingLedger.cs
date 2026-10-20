namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramTargetWindow(ProgramTargetWindowStage.NormalizeImplicitDrawTwo, 40)]
    private bool LiuPanNormalizeImplicitDrawTwoTargetWindow(ProgramTargetCardWindowQuery query) =>
        HasLiuPanCapability;


    private bool HasLiuPanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolvePijingTargetTogglesAndRandomGifts);
    private static CardConversionSource PijingSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_liu_pan_ol_PijingLedger.PijingSource(f);
    private bool TryGetPijingUse(ProgramSkillWindowContext context,out CardUseFrame use,out ProgramCardTriggerWindowFrame window)
    {
        use=null!;window=null!;
        if(context is not{Window:SkillProgramTriggerWindow.CardUseTargetsFinalized,CardUse:{}id}||LifecycleCardUse(id.ParentCardUseFrameId)is not{Action:{Type:CardActionType.Use}a}u||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{CompletedResponseReturn:null}w||w.ParentFrameId!=u.Id||w.Continuation is not(ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard)||a.ActionId!=id.CardActionId||a.ActorSeat!=u.SourceSeat||a.EffectiveKind!=u.CardKind||w.Action.ActionId!=a.ActionId||!w.Action.TargetSeats.SequenceEqual(u.TargetSeats)||!a.TargetSeats.SequenceEqual(u.TargetSeats)||a.ResponderSeat is not null||a.OpponentSeat is not null||!ProgramEventHistory<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==u.Id&&(e.CardKind==u.CardKind||IsCurrentSlashFireChangedUse(u)&&u.CurrentSlashFirePolicy!.OriginalKind==e.CardKind)))return false;
        var at=_resolutionStack.FindIndex(f=>f.Id==w.Id);if(at<1||_resolutionStack[at-1].Id!=u.Id)return false;
        if(u.CardKind==CardKind.BorrowedSword&&(u.TargetSeats.Count%2!=0||u.TargetSeats.Where((_,i)=>i%2==0).Distinct().Count()!=u.TargetSeats.Count/2))return false;
        use=u;window=w;return true;
    }
    private CardUseFrame RequirePijingUse(ProgramSkillFrame f,out ProgramCardTriggerWindowFrame window)
    {
        if(f.WindowContext is not{}c||!TryGetPijingUse(c,out var u,out window)||!DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0),c,window))throw new InvalidOperationException("Pijing lost its exact current native designation candidate.");
        var at=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(at<2||_resolutionStack[at-1].Id!=window.Id||_resolutionStack[at-2].Id!=u.Id)throw new InvalidOperationException("Pijing requires its immediate original use and designation window.");return u;
    }
    private bool PijingInitialEligible(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>c.SkillId=="ol:pijing"&&c.BindingId=="toggle-targets"&&_players[c.OwnerSeat].IsAlive&&TryGetPijingUse(context,out var u,out var w)&&u.SourceSeat==c.OwnerSeat&&DesignatedExtraTargetCandidateMatches(c,context,w)&&(IsSlashCard(u.CardKind)||IsOrdinaryTrick(u.CardKind))&&u.CardKind!=CardKind.Nullification&&u.Action!.EffectiveDesignatedTargetSeats.Count>0&&!ProgramEventHistory<PijingStartedEvent>().Any(e=>!e.Reciprocal&&e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillId==c.SkillId&&e.ActualTurn==_turnNumber)&&PijingInitialOptions(u,[]).Count>0;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27410)]
    private bool QualifyPijing(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>t.Effects is[{Op:SkillProgramEffectOp.ResolvePijingTargetTogglesAndRandomGifts}]?PijingInitialEligible(c,context):t.Effects is not[{Op:SkillProgramEffectOp.ResolvePijingNextSoleTargetChoice}]||IsIssuedPijingCandidate(c,context);
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 20)]
    private void CollectIssuedPijingNextUseCandidates(CardActionContext action,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
    {
        if(!HasLiuPanCapability||window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||action.Type!=CardActionType.Use||GetProgramCardCategory(action.EffectiveKind)!=SkillProgramCardCategory.Basic&&!IsOrdinaryTrick(action.EffectiveKind)||action.EffectiveDesignatedTargetSeats.Count!=1||_resolutionStack.OfType<CardUseFrame>().LastOrDefault(u=>u.Action?.ActionId==action.ActionId)is not{}use||!_players[action.ActorSeat].IsAlive)return;
        if(use.PijingNextUses is null)
        {
            var grants=ProgramEventHistory<PijingGrantIssuedEvent>().Where(g=>g.HolderSeat==action.ActorSeat&&!ProgramEventHistory<PijingGrantConsumedEvent>().Any(c=>c.GrantId==g.GrantId)).OrderBy(g=>g.GrantId).GroupBy(g=>(g.Source.OwnerSeat,g.Source.SkillId));var groups=new List<PijingNextUseGroup>();
            foreach(var pending in grants)
            {
                var first=pending.First();var ids=pending.Select(g=>g.GrantId).ToArray();var group=new PijingNextUseGroup(first.GrantId,first.Source,first.GameplayHash,action.ActorSeat,ids);groups.Add(group);
                AdvanceEventRulesAndQueueFact(new PijingNextUseIssuedEvent(use.Id,action.ActionId,group.GroupId,group.Source,group.GameplayHash,group.HolderSeat,ids.Length));for(var i=0;i<ids.Length;i++)AdvanceEventRulesAndQueueFact(new PijingNextUseGrantEvent(use.Id,group.GroupId,i,ids[i]));
            }
            if(groups.Count==0)return;UpdateLifecycleCardUse(use.Id,u=>u with{PijingNextUses=groups});use=LifecycleCardUse(use.Id)!;
        }
        foreach(var g in use.PijingNextUses!)
        {
            if(result.Any(c=>c.SkillId==g.Source.SkillId&&c.TriggerId=="next-sole-use"&&c.OwnerSeat==g.Source.OwnerSeat))continue;
            var trigger=_contentRegistry.GetSkill(g.Source.SkillId).Program!.Triggers.Single(t=>t.Id=="next-sole-use");var context=CreateCardActionProgramContext(action,window,0,g.Source.OwnerSeat,g.HolderSeat,CaptureProgramTriggerFacts(_players[g.Source.OwnerSeat],action))with{PijingIssuedGroupId=g.GroupId,OptionalChooserSeat=g.HolderSeat};
            result.Add(new(g.Source.OwnerSeat,g.HolderSeat,g.Source.SkillId,trigger.Id,g.GameplayHash,g.Source.SkillInstanceId,trigger.Priority,context));
        }
    }
    private ProgramSkillWindowContext CreatePijingIssuedContext(ProgramCardTriggerWindowFrame frame,ProgramCardTriggerCandidate candidate,ProgramSkillWindowContext context)
    {
        if(candidate.SkillId!="ol:pijing"||candidate.TriggerId!="next-sole-use"||LifecycleCardUse(frame.ParentFrameId)?.PijingNextUses?.SingleOrDefault(g=>g.Source.OwnerSeat==candidate.OwnerSeat&&g.Source.SkillInstanceId==candidate.SkillInstanceId)is not{}group)return context;
        return context with{PijingIssuedGroupId=group.GroupId,OptionalChooserSeat=group.HolderSeat,SourceSeat=group.HolderSeat,TargetSeat=group.HolderSeat};
    }
    private PijingNextUseGroup? PijingIssuedGroup(ProgramTriggerCandidate c,ProgramSkillWindowContext context,out CardUseFrame use,out ProgramCardTriggerWindowFrame window)
    {
        use=null!;window=null!;
        if(c.SkillId!="ol:pijing"||c.BindingId!="next-sole-use"||context.PijingIssuedGroupId is not{}id||!TryGetPijingUse(context,out use,out window)||!DesignatedExtraTargetCandidateMatches(c,context,window)||use.PijingNextUses?.SingleOrDefault(g=>g.GroupId==id)is not{}group||group.Source.OwnerSeat!=c.OwnerSeat||group.Source.SkillId!=c.SkillId||group.Source.SkillInstanceId!=c.SkillInstanceId||group.GameplayHash!=c.GameplayHash||context.OptionalChooserSeat!=group.HolderSeat||_contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash!=c.GameplayHash)return null;
        var currentUse=use;
        if(ProgramEventHistory<PijingNextUseIssuedEvent>().Count(e=>e==new PijingNextUseIssuedEvent(currentUse.Id,currentUse.Action!.ActionId,group.GroupId,group.Source,group.GameplayHash,group.HolderSeat,group.Grants.Count))!=1||!ProgramEventHistory<PijingNextUseGrantEvent>().Where(e=>e.UseId==currentUse.Id&&e.GroupId==id).OrderBy(e=>e.Index).Select(e=>e.GrantId).SequenceEqual(group.Grants)||group.Grants.Any(g=>ProgramEventHistory<PijingGrantIssuedEvent>().SingleOrDefault(e=>e.GrantId==g)is not{}issued||issued.HolderSeat!=group.HolderSeat||issued.Source.OwnerSeat!=group.Source.OwnerSeat||issued.Source.SkillId!=group.Source.SkillId))return null;
        return group;
    }
    private bool IsIssuedPijingCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>PijingIssuedGroup(c,context,out var use,out _)is{}g&&_players[g.HolderSeat].IsAlive&&use.SourceSeat==g.HolderSeat&&g.Grants.All(id=>!ProgramEventHistory<PijingGrantConsumedEvent>().Any(e=>e.GrantId==id));
    private bool HasPijingTargetTail(CardUseFrame use)=>use.Action is{}a&&HasPijingTargetTail(use,a);
    private bool HasPijingTargetTail(CardUseFrame use,CardActionContext action)
    {
        if(use.PijingTail is not{}r||use.Id!=r.UseId||action.ActionId!=r.ActionId||action.ProviderSeat!=r.ProviderSeat||!action.TargetSeats.SequenceEqual(r.Final)||_contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash!=r.GameplayHash||ProgramEventHistory<PijingTargetsAppliedEvent>().Count(e=>e==new PijingTargetsAppliedEvent(r.FrameId,r.UseId,r.ActionId,r.Source,r.GameplayHash,r.ActorSeat,r.ProviderSeat,r.Original.Count,r.Final.Count,r.Reciprocal))!=1||!ProgramEventHistory<PijingTargetSlotEvent>().Where(e=>e.FrameId==r.FrameId&&!e.Final).OrderBy(e=>e.Index).Select(e=>e.Seat).SequenceEqual(r.Original)||!ProgramEventHistory<PijingTargetSlotEvent>().Where(e=>e.FrameId==r.FrameId&&e.Final).OrderBy(e=>e.Index).Select(e=>e.Seat).SequenceEqual(r.Final)||ProgramEventHistory<CardUseDeclaredEvent>().SingleOrDefault(e=>e.ResolutionId==use.Id)is not{}declared||!ShownEntityUseActorMatches(use,declared.SourceSeat,r.ProviderSeat))return false;
        var actual=r.Final.ToArray();var after=false;
        foreach(var fact in CompleteProgramEventHistory())
        {
            if(fact is PijingTargetsAppliedEvent applied&&applied.FrameId==r.FrameId){after=true;continue;}if(!after)continue;
            if(fact is PijingRedirectedEvent e&&e.UseId==use.Id){if(!IsSlashCard(use.CardKind)||e.ActionId!=r.ActionId||e.TargetIndex<0||e.TargetIndex>=actual.Length||actual[e.TargetIndex]!=e.PreviousSeat||e.Source.OwnerSeat!=e.PreviousSeat||_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash!=e.GameplayHash)return false;actual[e.TargetIndex]=e.Seat;}
            if(fact is LiuliRedirectedEvent liuli&&liuli.ResolutionId==use.Id){var at=Array.IndexOf(actual,liuli.OriginalTargetSeat);if(!IsSlashCard(use.CardKind)||at<0)return false;actual[at]=liuli.NewTargetSeat;}
        }
        return actual.SequenceEqual(use.TargetSeats);
    }
    private bool HasPijingZeroTargetTail(CardUseFrame use)=>HasPijingTargetTail(use)&&use.TargetSeats.Count==0;
    private bool IsPijingAppliedTargetFact(CardUseFrame use,PijingTargetsAppliedEvent fact,IReadOnlyList<int> precedingTargets,out IReadOnlyList<int> resultTargets)
    {
        resultTargets=[];
        if(use.Action is not{}action||fact.UseId!=use.Id||fact.ActionId!=action.ActionId||fact.ProviderSeat!=action.ProviderSeat||fact.Source.SkillId!="ol:pijing"||_contentRegistry.GetSkill(fact.Source.SkillId).Program?.GameplayHash!=fact.GameplayHash||ProgramEventHistory<PijingTargetsAppliedEvent>().Count(e=>e==fact)!=1||ProgramEventHistory<PijingStartedEvent>().SingleOrDefault(e=>e.FrameId==fact.FrameId)is not{}start||start.Source!=fact.Source||start.GameplayHash!=fact.GameplayHash||start.UseId!=fact.UseId||start.ActionId!=fact.ActionId||start.ActorSeat!=fact.ActorSeat||start.ProviderSeat!=fact.ProviderSeat||start.Reciprocal!=fact.Reciprocal||start.OriginalCount!=fact.OriginalCount)return false;
        var before=ProgramEventHistory<PijingTargetSlotEvent>().Where(e=>e.FrameId==fact.FrameId&&!e.Final).OrderBy(e=>e.Index).ToArray();var after=ProgramEventHistory<PijingTargetSlotEvent>().Where(e=>e.FrameId==fact.FrameId&&e.Final).OrderBy(e=>e.Index).ToArray();
        if(before.Length!=fact.OriginalCount||after.Length!=fact.FinalCount||!before.Select(e=>e.Index).SequenceEqual(Enumerable.Range(0,before.Length))||!after.Select(e=>e.Index).SequenceEqual(Enumerable.Range(0,after.Length))||!before.Select(e=>e.Seat).SequenceEqual(precedingTargets))return false;
        var selected=ProgramEventHistory<PijingToggleSelectedEvent>().Where(e=>e.FrameId==fact.FrameId).OrderBy(e=>e.Index).ToArray();
        if(!selected.Select(e=>e.Index).SequenceEqual(Enumerable.Range(0,selected.Length))||selected.Length>start.X||selected.Select(e=>e.Seat).Distinct().Count()!=selected.Length||selected.Any(e=>e.Seat==start.ActorSeat||e.Remove&&start.Reciprocal))return false;
        var toggles=new List<PijingToggle>();
        foreach(var item in selected)
        {var pair=ProgramEventHistory<PijingTogglePairEvent>().Where(e=>e.FrameId==fact.FrameId&&e.Index==item.Index).OrderBy(e=>e.PairIndex).ToArray();if(pair.Length!=item.PairCount||!pair.Select(e=>e.PairIndex).SequenceEqual(Enumerable.Range(0,pair.Length))||pair.Length!=(use.CardKind==CardKind.BorrowedSword?2:1)||pair[0].Seat!=item.Seat)return false;toggles.Add(new(item.Seat,item.Remove,pair.Select(e=>e.Seat).ToArray()));}
        if(ProgramEventHistory<PijingTogglePairEvent>().Any(e=>e.FrameId==fact.FrameId&&(e.Index<0||e.Index>=selected.Length)))return false;
        var folded=PijingFoldTargets(precedingTargets,toggles,use.CardKind);if(!folded.SequenceEqual(after.Select(e=>e.Seat)))return false;resultTargets=folded;return true;
    }
    private void RecordPijingRedirect(ProgramSkillFrame f,long useId,int previousSeat,int nextSeat)
    {
        if(LifecycleCardUse(useId)is not{PijingTail:not null,Action:{}a}use)return;
        if(GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.RedirectCurrentAttack||f.OwnerSeat!=previousSeat||f.WindowContext is not{Window:SkillProgramTriggerWindow.SlashTargetRedirecting,CardUse:{}c}context||c.ParentCardUseFrameId!=useId||c.CardActionId!=a.ActionId||context.TargetSeat!=previousSeat||use.TargetIndex<0||use.TargetIndex>=use.TargetSeats.Count||use.TargetSeats[use.TargetIndex]!=nextSeat)throw new InvalidOperationException("Pijing cannot borrow an unrelated target redirect.");
        AdvanceEventRulesAndQueueFact(new PijingRedirectedEvent(use.Id,a.ActionId,f.Id,PijingSource(f),f.GameplayHash,f.InstructionIndex,use.TargetIndex,previousSeat,nextSeat));
    }
}
