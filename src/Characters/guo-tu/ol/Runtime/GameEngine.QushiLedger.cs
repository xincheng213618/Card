namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasQushiCapability=>_contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveQushiDrawAndPrivateSeal);
    private const string QushiDrawReason="skill-program.guo-tu.qushi.draw",QushiSealReason="skill-program.guo-tu.qushi.seal",QushiRemoveReason="skill-program.guo-tu.qushi.remove",QushiDeathReason="skill-program.guo-tu.qushi.holder-death";
    private static CardConversionSource QushiSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_guo_tu_ol_QushiLedger.QushiSource(f);
    private bool ValidQushiSeal(QushiSeal s)=>s.Source.SkillId=="ol:qushi"&&s.Source.BindingId=="draw-seal"&&IsValidPlayerSeat(s.HolderSeat)&&s.HolderSeat!=s.Source.OwnerSeat&&s.Location==new CardLocation(CardZoneKind.PrivatePersistentPile,s.HolderSeat,$"qushi:{s.SealId}")&&_contentRegistry.GetSkill(s.Source.SkillId).Program?.GameplayHash==s.GameplayHash&&ProgramEventHistory<QushiStartedEvent>().Count(e=>e.FrameId==s.SealId&&e.Source==s.Source&&e.GameplayHash==s.GameplayHash&&e.Op==SkillProgramEffectOp.ResolveQushiDrawAndPrivateSeal)==1&&_cardMovements.Count(m=>m.Sequence==s.MovementSequence&&m.CardId==s.CardId&&m.CardKind==s.PrintedKind&&m.From==CardLocation.Hand(s.Source.OwnerSeat)&&m.To==s.Location&&m.Reason.Value==QushiSealReason)==1;
    private QushiSeal[] LiveQushiSeals()=>!HasQushiCapability?[]:ProgramEventHistory<QushiSealPlacedEvent>().Select(e=>e.Seal).Where(s=>ValidQushiSeal(s)&&!ProgramEventHistory<QushiSealRemovedEvent>().Any(e=>e.SealId==s.SealId)&&_cardZones.GetLocation(s.CardId)==s.Location).ToArray();
    private IReadOnlyList<NamedAmbushPileSnapshot>? ProjectQushiSeals(int holderSeat,int viewerSeat,bool revealAll)
    {
        var seals=LiveQushiSeals().Where(s=>s.HolderSeat==holderSeat).ToArray();if(seals.Length==0)return null;
        return Array.AsReadOnly(seals.GroupBy(s=>s.Source.OwnerSeat).Select(g=>new NamedAmbushPileSnapshot("ol:qushi","趋",g.Count(),revealAll||g.Key==viewerSeat?Array.AsReadOnly(g.Select(s=>new NamedAmbushPrivateEntrySnapshot(ToSnapshot(GetAdvancedCard(s.CardId)),s.PrintedKind)).ToArray()):null)).ToArray());
    }
    [ProgramViewContributor(ProgramViewContribution.NamedAmbushPiles, 10)]
    private IReadOnlyList<NamedAmbushPileSnapshot>? IncludeQushiSealSnapshots(IReadOnlyList<NamedAmbushPileSnapshot>? ordinary,int holder,int viewer,bool revealAll)
    {
        var own=ProjectQushiSeals(holder,viewer,revealAll);if(own is null)return ordinary;
        return Array.AsReadOnly((ordinary??[]).Concat(own).Select(p=>p with{Entries=p.Entries is{}entries?Array.AsReadOnly(entries.ToArray()):null}).ToArray());
    }
    private IReadOnlyList<int> DiscardQushiHolderDeathSeals(DeathFrame death)
    {
        if(!HasQushiCapability)return [];
        if(_resolutionStack.OfType<DeathFrame>().SingleOrDefault(d=>d.Id==death.Id)is not{}live||live.VictimSeat!=death.VictimSeat||_players[death.VictimSeat].IsAlive)throw new InvalidOperationException("Qushi death removal requires its real current dead holder.");
        var seals=LiveQushiSeals().Where(s=>s.HolderSeat==death.VictimSeat).ToArray();
        foreach(var s in seals)MoveCard(GetAdvancedCard(s.CardId),s.Location,CardLocation.DiscardPile,new(QushiDeathReason),m=>AdvanceEventRulesAndQueueFact(new QushiSealRemovedEvent(death.Id,s.SealId,s.HolderSeat,s.CardId,m.Sequence,true)));
        return Array.AsReadOnly(seals.Select(s=>s.CardId).ToArray());
    }
    private void AppendQushiEndingItems(List<TurnEndingBoundaryItem> items)
    {
        if(!HasQushiCapability||!_players[_currentSeat].IsAlive)return;var t=_contentRegistry.GetSkill("ol:qushi").Program!.Triggers.Single(t=>t.Id=="issued-holder-ending");
        foreach(var s in LiveQushiSeals().Where(s=>s.HolderSeat==_currentSeat).OrderBy(s=>RelativeTableOrder(_currentSeat, s.Source.OwnerSeat)).ThenBy(s=>s.SealId))
        {var c=new ProgramTriggerCandidate(s.Source.OwnerSeat,"ol:qushi",t.Id,s.Source.SkillInstanceId,s.GameplayHash,t.Priority,items.Count);items.Add(new(TurnEndingBoundaryItemKind.Program,t.Priority,$"qushi:{s.SealId}",c,CaptureProgramTriggerFacts(_players[c.OwnerSeat])));}
    }
    private ProgramSkillWindowContext CreateQushiEndingContext(TurnEndingBoundaryFrame parent,ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(c.SkillId!="ol:qushi"||c.BindingId!="issued-holder-ending")return context;
        var item=parent.Items[parent.ItemIndex];var s=ProgramEventHistory<QushiSealPlacedEvent>().Select(e=>e.Seal).SingleOrDefault(s=>item.StableIdentity==$"qushi:{s.SealId}");return context with{QushiSealId=s?.SealId};
    }
    private QushiSeal? CurrentQushiEndingSeal(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
    {
        if(!HasQushiCapability||c.SkillId!="ol:qushi"||c.BindingId!="issued-holder-ending"||context.Window!=SkillProgramTriggerWindow.TurnEnding||context.OwnerSeat!=c.OwnerSeat||context.SourceSeat!=_currentSeat||context.TargetSeat!=_currentSeat||context.OccurrenceIndex!=c.OccurrenceIndex||context.QushiSealId is not{}id||_resolutionStack.OfType<TurnEndingBoundaryFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{}p||p.OwnerSeat!=_currentSeat||p.TurnNumber!=_turnNumber||p.ItemIndex<0||p.ItemIndex>=p.Items.Count||p.Items[p.ItemIndex].Candidate!=c||p.Items[p.ItemIndex].StableIdentity!=$"qushi:{id}")return null;
        return ProgramEventHistory<QushiSealPlacedEvent>().Select(e=>e.Seal).SingleOrDefault(s=>s.SealId==id&&s.Source.OwnerSeat==c.OwnerSeat&&s.Source.SkillInstanceId==c.SkillInstanceId&&s.GameplayHash==c.GameplayHash&&s.HolderSeat==p.OwnerSeat&&ValidQushiSeal(s));
    }
    private bool IsIssuedQushiEndingCandidate(ProgramTriggerCandidate c,ProgramSkillWindowContext context)=>_winner==Winner.None&&CurrentQushiEndingSeal(c,context)is{}s&&(LiveQushiSeals().Any(d=>d==s)||_resolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.Qushi is{Seal:{}seal,Paid:true}&&seal==s&&f.WindowContext==context&&MountObserverCandidateMatches(f,c)));
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent,27460)]private void ObserveQushiUses(IGameEvent e)
    {
        if(!HasQushiCapability||e is QushiActualUseEvent||_turnNumber<=0)return;
        if(e is CardActionAcceptedEvent{Action:{Type:CardActionType.Response}a})
        {
            var trueUse=false;long origin=0;
            if(a.EffectiveKind==CardKind.Nullification&&_resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault()is{}n&&a.ParentActionId==NullificationParentActionId(n)&&a.OpponentSeat==n.SourceSeat){trueUse=true;origin=n.Id;}
            if(a.EffectiveKind==CardKind.Dodge&&_resolutionStack.OfType<CardUseFrame>().LastOrDefault(u=>u.Action?.ActionId==a.ParentActionId)is{CardAttack:{}attack}u&&attack.TargetSeat==a.ActorSeat&&IsSlashCard(attack.EffectiveCardKind??u.CardKind)&&a.OpponentSeat==attack.SourceSeat){trueUse=true;origin=u.Id;}
            if(trueUse&&a.ActorSeat==a.ProviderSeat&&a.RequesterSeat is null&&a.ResponderSeat==a.ActorSeat&&!ProgramEventHistory<QushiActualUseEvent>().Any(x=>x.ResponseUse&&x.ActionId==a.ActionId))AdvanceEventRulesAndQueueFact(new QushiActualUseEvent(_turnNumber,_currentSeat,origin,a.ActionId,true,a.ActorSeat,a.EffectiveKind,0));
        }
        foreach(var use in _resolutionStack.OfType<CardUseFrame>().ToArray())
            if(ProgramEventHistory<CardUseDeclaredEvent>().Any(d=>d.ResolutionId==use.Id))RecordQushiUse(use);
    }
    [NativeUsePreparation(27460)]private bool CaptureQushiFinalUse(CardUseFrame use){if(HasQushiCapability&&ProgramEventHistory<CardUseDeclaredEvent>().Any(d=>d.ResolutionId==use.Id))RecordQushiUse(use);return false;}
    private void RecordQushiUse(CardUseFrame use)
    {
        if(use.Action is{Type:not CardActionType.Use})return;
        var targets=use.Action?.EffectiveDesignatedTargetSeats??use.TargetSeats;var count=targets.Distinct().Count();
        if(count==0&&!use.TargetsAdjusted&&(GetProgramCardCategory(use.CardKind)==SkillProgramCardCategory.Equipment||use.CardKind is CardKind.DrawTwo or CardKind.Peach or CardKind.Alcohol))count=1;
        var old=ProgramEventHistory<QushiActualUseEvent>().LastOrDefault(e=>!e.ResponseUse&&e.OriginId==use.Id);
        var next=new QushiActualUseEvent(old?.ActualTurn??_turnNumber,old?.TurnOwner??_currentSeat,use.Id,use.Action?.ActionId??0,false,use.SourceSeat,use.CardKind,count);if(next!=old)AdvanceEventRulesAndQueueFact(next);
    }
    private QushiActualUseEvent[] QushiActualUses(int holder)=>ProgramEventHistory<QushiActualUseEvent>().Where(e=>e.ActualTurn==_turnNumber&&e.TurnOwner==_currentSeat).GroupBy(e=>(e.ResponseUse,e.ResponseUse?e.ActionId:e.OriginId)).Select(g=>g.Last()).Where(e=>e.ActorSeat==holder).ToArray();
}
