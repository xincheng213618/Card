namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasMaChengCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveChenglieExtraTargetsAndHiddenCards);
    private const string ChenglieRevealReason="skill-program.ma-cheng.chenglie.reveal",ChenglieSwapOutReason="skill-program.ma-cheng.chenglie.swap-out",ChenglieSwapInReason="skill-program.ma-cheng.chenglie.swap-in",ChenglieAssignReason="skill-program.ma-cheng.chenglie.assign",ChenglieGiftReason="skill-program.ma-cheng.chenglie.give",ChenglieCleanupReason="skill-program.ma-cheng.chenglie.finished";
    private static CardConversionSource ChenglieSource(ProgramSkillFrame f)=>new(f.SkillId,GetProgramBindingId(f),f.OwnerSeat,f.SkillInstanceId);
    private bool TryGetChenglieUse(ProgramSkillWindowContext c,out CardUseFrame use,out ProgramCardTriggerWindowFrame window)
    {
        use=null!;window=null!;
        if(c.Window!=SkillProgramTriggerWindow.CardUseTargetsFinalized||c.CardUse is not{}identity||LifecycleCardUse(identity.ParentCardUseFrameId)is not{Action:{Type:CardActionType.Use}a}u||!IsSlashCard(u.CardKind)||a.ActionId!=identity.CardActionId||a.ActorSeat!=u.SourceSeat||a.EffectiveKind!=u.CardKind||a.ResponderSeat is not null||a.OpponentSeat is not null||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId)is not{Continuation:ProgramCardContinuation.Slash,CompletedResponseReturn:null}w||w.ParentFrameId!=u.Id||w.Action.ActionId!=a.ActionId||!w.Action.TargetSeats.SequenceEqual(u.TargetSeats)||!a.TargetSeats.SequenceEqual(u.TargetSeats)||!ProgramEventHistory<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==u.Id&&(e.CardKind==u.CardKind||IsCurrentSlashFireChangedUse(u)&&u.CurrentSlashFirePolicy!.OriginalKind==e.CardKind)))return false;
        var at=_resolutionStack.FindIndex(x=>x.Id==w.Id);if(at<1||_resolutionStack[at-1].Id!=u.Id)return false;use=u;window=w;return true;
    }
    private IReadOnlyList<int> ChenglieLegalTargets(CardUseFrame use,IReadOnlyList<int> added)
    {
        var a=use.Action!;var actor=_players[a.ActorSeat];var ids=a.PhysicalCards.Select(c=>c.CardId).ToArray();
        return Array.AsReadOnly(_players.Where(t=>!use.TargetSeats.Contains(t.Seat)&&!added.Contains(t.Seat)&&CanUseSlashTarget(actor,t,new(-1,use.CardKind,a.EffectiveSuit??Suit.None,a.EffectiveRank??0),a.ConversionChain.FirstOrDefault(),use.CardKind,existingUseFrameId:use.Id,specificEffectiveRank:a.EffectiveRank,physicalCardIds:ids)).OrderBy(t=>t.Seat).Select(t=>t.Seat).ToArray());
    }
    private bool ChenglieInitialEligible(ProgramTriggerCandidate candidate,ProgramSkillWindowContext context)=>candidate.SkillId=="ol:chenglie"&&candidate.BindingId=="extra-targets"&&_players[candidate.OwnerSeat].IsAlive&&TryGetChenglieUse(context,out var u,out var w)&&u.SourceSeat==candidate.OwnerSeat&&u.ChenglieLease is null&&DesignatedExtraTargetCandidateMatches(candidate,context,w)&&!ProgramEventHistory<ChenglieStartedEvent>().Any(e=>e.UseId==u.Id&&!e.Outcome)&&ChenglieLegalTargets(u,[]).Count>0;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,27560)]
    private bool QualifyChenglie(ProgramTriggerCandidate c,SkillProgramTrigger t,ProgramSkillWindowContext context)=>t.Effects is[{Op:SkillProgramEffectOp.ResolveChenglieExtraTargetsAndHiddenCards}]?ChenglieInitialEligible(c,context):t.Effects is not[{Op:SkillProgramEffectOp.ResolveChenglieRedHolderOutcome}]||IsIssuedChenglieCandidate(c,context);
    private CardUseFrame RequireChenglieParent(ProgramSkillFrame f)
    {
        if(f.Chenglie is not{}r||LifecycleCardUse(r.UseId)is not{Action:{}a}u||a.ActionId!=r.ActionId||f.WindowContext is not{}c||c.ParentFrameId!=r.WindowId||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==r.WindowId)is not{}window||window.ParentFrameId!=u.Id||!DesignatedExtraTargetCandidateMatches(new(f.OwnerSeat,f.SkillId,GetProgramBindingId(f),f.SkillInstanceId,f.GameplayHash,0),c,window))throw new InvalidOperationException("Chenglie lost its original native use, current candidate or exact target/completion mother.");
        var at=_resolutionStack.FindIndex(x=>x.Id==f.Id);if(at<2||_resolutionStack[at-1].Id!=window.Id||_resolutionStack[at-2].Id!=u.Id)throw new InvalidOperationException("Chenglie requires its immediate original card and card-trigger mother.");return u;
    }
    private bool IsChenglieAppliedTargetFact(CardUseFrame use,ChenglieTargetsAppliedEvent e,IReadOnlyList<int> precedingTargets,out IReadOnlyList<int> resultTargets)
    {
        resultTargets=[];
        if(use.Action is not{}a||e.UseId!=use.Id||e.ActionId!=a.ActionId||e.ProviderSeat!=a.ProviderSeat||e.Source.SkillId!="ol:chenglie"||e.Source.BindingId!="extra-targets"||e.Source.OwnerSeat!=e.ActorSeat||e.AddedCount is<1 or>2||_contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash!=e.GameplayHash||ProgramEventHistory<ChenglieTargetsAppliedEvent>().Count(x=>x==e)!=1||ProgramEventHistory<ChenglieStartedEvent>().SingleOrDefault(x=>x.FrameId==e.FrameId)is not{Outcome:false}started||started.Source!=e.Source||started.GameplayHash!=e.GameplayHash||started.UseId!=e.UseId||started.ActionId!=e.ActionId||started.OriginalCount!=e.OriginalCount)return false;
        var original=ProgramEventHistory<ChenglieTargetSlotEvent>().Where(x=>x.FrameId==e.FrameId&&!x.Added).OrderBy(x=>x.Index).ToArray();var added=ProgramEventHistory<ChenglieTargetSlotEvent>().Where(x=>x.FrameId==e.FrameId&&x.Added).OrderBy(x=>x.Index).ToArray();
        if(original.Length!=e.OriginalCount||added.Length!=e.AddedCount||!original.Select(x=>x.Index).SequenceEqual(Enumerable.Range(0,original.Length))||!added.Select(x=>x.Index).SequenceEqual(Enumerable.Range(0,added.Length))||!original.Select(x=>x.Seat).SequenceEqual(precedingTargets)||added.Any(x=>x.Seat==e.ActorSeat||precedingTargets.Contains(x.Seat))||added.Select(x=>x.Seat).Distinct().Count()!=added.Length)return false;
        resultTargets=Array.AsReadOnly(precedingTargets.Concat(added.Select(x=>x.Seat)).ToArray());return true;
    }
    private bool HasChenglieTargetTail(CardUseFrame use)=>use.Action is{}a&&HasChenglieTargetTail(use,a);
    private bool HasChenglieTargetTail(CardUseFrame use,CardActionContext action)
    {
        if(use.ChenglieLease is not{}r||r.UseId!=use.Id||r.ActionId!=action.ActionId||action.ProviderSeat!=r.ProviderSeat||!IsSlashCard(use.CardKind)||ProgramEventHistory<ChenglieTargetsAppliedEvent>().SingleOrDefault(e=>e.FrameId==r.SetupFrameId)is not{}applied||applied.Source!=r.Source||applied.GameplayHash!=r.GameplayHash||!IsChenglieAppliedTargetFact(use,applied,r.Original,out var final)||!final.SequenceEqual(r.Original.Concat(r.Added))||ProgramEventHistory<CardUseDeclaredEvent>().SingleOrDefault(e=>e.ResolutionId==use.Id)is not{}declared||!ShownEntityUseActorMatches(use,declared.SourceSeat,r.ProviderSeat))return false;
        var designated=final.ToArray();var actual=final.ToArray();var after=false;
        foreach(var fact in CompleteProgramEventHistory())
        {
            if(fact is ChenglieTargetsAppliedEvent own&&own.FrameId==r.SetupFrameId){after=true;continue;}if(!after)continue;
            if(fact is ChenglieRedirectedEvent redirect&&redirect.UseId==use.Id)
            {
                if(redirect.ActionId!=r.ActionId||redirect.TargetIndex<0||redirect.TargetIndex>=actual.Length||actual[redirect.TargetIndex]!=redirect.PreviousSeat||redirect.Source.OwnerSeat!=redirect.PreviousSeat||!IsValidPlayerSeat(redirect.Seat)||_contentRegistry.GetSkill(redirect.Source.SkillId).Program is not{}program||program.GameplayHash!=redirect.GameplayHash||!program.Triggers.Any(t=>t.Id==redirect.Source.BindingId&&t.Window==SkillProgramTriggerWindow.SlashTargetRedirecting&&t.Effects.Any(e=>e.Op==SkillProgramEffectOp.RedirectCurrentAttack))||ProgramEventHistory<ProgramBindingStartedEvent>().Count(e=>e.FrameId==redirect.FrameId&&e.OwnerSeat==redirect.PreviousSeat&&e.SkillId==redirect.Source.SkillId&&e.SkillInstanceId==redirect.Source.SkillInstanceId&&e.BindingId==redirect.Source.BindingId&&e.Window==SkillProgramTriggerWindow.SlashTargetRedirecting)!=1)return false;
                actual[redirect.TargetIndex]=redirect.Seat;
            }
            else if(fact is LiuliRedirectedEvent liuli&&liuli.ResolutionId==use.Id)
            {var index=Array.IndexOf(actual,liuli.OriginalTargetSeat);if(index<0||!IsValidPlayerSeat(liuli.NewTargetSeat))return false;actual[index]=liuli.NewTargetSeat;}
            else if(fact is PijingTargetsAppliedEvent pijing&&pijing.UseId==use.Id)
            {if(!actual.SequenceEqual(designated)||!IsPijingAppliedTargetFact(use,pijing,designated,out var changed))return false;designated=changed.ToArray();actual=changed.ToArray();}
        }
        return action.TargetSeats.SequenceEqual(designated)&&use.TargetSeats.SequenceEqual(actual)&&r.Cards.Select(c=>c.Index).SequenceEqual(Enumerable.Range(0,r.Cards.Count))&&r.Cards.Select(c=>c.CardId).Distinct().Count()==r.Cards.Count&&r.Cards.All(c=>ValidChenglieAttachment(r,c));
    }
    private bool ValidChenglieAttachment(ChenglieLease r,ChenglieAttachment c)=>c.Index>=0&&c.Index<r.Original.Count+r.Added.Count&&c.HolderSeat==r.Original.Concat(r.Added).ElementAt(c.Index)&&c.Location==new CardLocation(CardZoneKind.PrivatePersistentPile,c.HolderSeat,$"chenglie:{r.UseId}:{r.SetupFrameId}:{c.Index}")&&c.Red==IsRedSuit(c.Suit)&&ProgramEventHistory<ChenglieAttachmentPlacedEvent>().Count(e=>e==new ChenglieAttachmentPlacedEvent(r.SetupFrameId,r.UseId,r.ActionId,c.Index,c.HolderSeat,c.CardId,c.Kind,c.Suit,c.Red,c.Location,c.Sequence))==1&&_cardMovements.Any(m=>m.Sequence==c.Sequence&&m.CardId==c.CardId&&m.CardKind==c.Kind&&m.From==CardLocation.Processing&&m.To==c.Location&&m.Reason.Value==ChenglieAssignReason);
    private void RecordChenglieRedirect(ProgramSkillFrame f,long useId,int previousSeat,int nextSeat)
    {
        if(LifecycleCardUse(useId)is not{ChenglieLease:not null,Action:{}a}use)return;
        if(GetPausedPrivateOfferEffect(f)?.Op!=SkillProgramEffectOp.RedirectCurrentAttack||f.OwnerSeat!=previousSeat||f.WindowContext is not{Window:SkillProgramTriggerWindow.SlashTargetRedirecting,CardUse:{}c}context||c.ParentCardUseFrameId!=useId||c.CardActionId!=a.ActionId||context.TargetSeat!=previousSeat||use.TargetIndex<0||use.TargetIndex>=use.TargetSeats.Count||use.TargetSeats[use.TargetIndex]!=nextSeat)throw new InvalidOperationException("Chenglie cannot claim an unrelated native redirect.");
        AdvanceEventRulesAndQueueFact(new ChenglieRedirectedEvent(useId,a.ActionId,f.Id,ChenglieSource(f),f.GameplayHash,use.TargetIndex,previousSeat,nextSeat));
    }
    private bool HasChenglieResponseObserver(CardActionContext a,ProgramCardContinuation continuation)=>HasMaChengCapability&&a.Type==CardActionType.Response&&a.EffectiveKind==CardKind.Dodge&&continuation is(ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge)&&a.ParentActionId is{}parent&&_resolutionStack.OfType<CardUseFrame>().Any(u=>u.Action?.ActionId==parent&&u.ChenglieLease is{Ready:true,Settled:false}r&&r.Cards.Any(c=>c.HolderSeat==a.ActorSeat));
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent,27560)]
    private void RecordChenglieResponse(IGameEvent fact)
    {
        if(!HasMaChengCapability||fact is not CardResponseCompletedEvent{EffectiveKind:CardKind.Dodge}e||e.OriginalContinuation is not(ProgramCardContinuation.Dodge or ProgramCardContinuation.FactionDefenseDodge)||e.AttackOwnerFrameId is not{}id||LifecycleCardUse(id)is not{ChenglieLease:{Ready:true,Settled:false}r}use||e.ParentFrameId!=id||!HasChenglieTargetTail(use)||!r.Cards.Any(c=>c.HolderSeat==e.NativeActorSeat)||ProgramEventHistory<ChenglieResponseRecordedEvent>().Any(x=>x.SetupFrameId==r.SetupFrameId&&x.HolderSeat==e.NativeActorSeat))return;
        AdvanceEventRulesAndQueueFact(new ChenglieResponseRecordedEvent(r.SetupFrameId,use.Id,r.ActionId,e.NativeActorSeat,e.ActionId,e.ProviderSeat,e.OriginalContinuation));
    }
    private bool ChenglieResponded(ChenglieLease r,int holder)=>ProgramEventHistory<ChenglieResponseRecordedEvent>().Any(e=>e.SetupFrameId==r.SetupFrameId&&e.UseId==r.UseId&&e.IncomingActionId==r.ActionId&&e.HolderSeat==holder);
    private CardUseFrame? ChenglieOutcomeUse(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(c.SkillId!="ol:chenglie"||c.BindingId!="red-holder-outcomes"||context.Window!=SkillProgramTriggerWindow.CardUseCompleted||context.CardUse is not{}id||LifecycleCardUse(id.ParentCardUseFrameId)is not{Step:ResolutionFrameStep.Completed,ChenglieLease:{Ready:true,Settled:false}r,Action:{}a}u||r.Source.OwnerSeat!=c.OwnerSeat||r.Source.SkillInstanceId!=c.SkillInstanceId||r.GameplayHash!=c.GameplayHash||r.ActionId!=id.CardActionId||a.ActionId!=id.CardActionId||!HasChenglieTargetTail(u)||_contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash!=c.GameplayHash||_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}window||window.ParentFrameId!=u.Id||window.Continuation is not(ProgramCardContinuation.CompletedSlash or ProgramCardContinuation.CompletedCard)||!DesignatedExtraTargetCandidateMatches(c,context,window)||ProgramEventHistory<CardUseFinishedEvent>().Count(e=>e.ResolutionId==u.Id)!=1)return null;return u;
    }
    private bool IsIssuedChenglieCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>ChenglieOutcomeUse(c,context)is not null;
    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 7)]
    private void CollectIssuedChenglieCandidates(CardActionContext a,SkillProgramTriggerWindow window,List<ProgramCardTriggerCandidate> result)
    {
        if(!HasMaChengCapability||window!=SkillProgramTriggerWindow.CardUseCompleted||_resolutionStack.LastOrDefault()is not CardUseFrame{ChenglieLease:{Ready:true,Settled:false}r}u||a.ActionId!=r.ActionId||!HasChenglieTargetTail(u))return;
        if(result.Any(c=>c.SkillId==r.Source.SkillId&&c.TriggerId=="red-holder-outcomes"&&c.OwnerSeat==r.Source.OwnerSeat))return;var t=_contentRegistry.GetSkill(r.Source.SkillId).Program!.Triggers.Single(t=>t.Id=="red-holder-outcomes");var context=CreateCardActionProgramContext(a,window,0,r.Source.OwnerSeat,r.Source.OwnerSeat,CaptureProgramTriggerFacts(_players[r.Source.OwnerSeat],a));result.Add(new(r.Source.OwnerSeat,r.Source.OwnerSeat,r.Source.SkillId,t.Id,r.GameplayHash,r.Source.SkillInstanceId,t.Priority,context));
    }
}
