using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLiuBeiChecks
{
    public static void GiftThresholdAndRecipient()
    {
        var (g,r) = Start();
        Gift(g,1,1); ReachPlay(g);
        var before = State(g); var p = Prompt(g)!;
        Require(!g.Submit(new UseProgramSkillCommand(0,"boundary:rende","give",[Hand(g,0)[0].Id],[1],g.Revision,p.PromptId)).Accepted && before == State(g), "The same recipient is atomically unavailable in this actual Play.");
        Gift(g,2,1); Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-basic"));
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single().PhaseGiftReceipt is {PreviousCount:1,ActualCount:1},"Two actual gifts freeze the crossing receipt.");
        Replay(g,r); Reject(g);
        Answer(g,c=>c.Parameters.GetValueOrDefault("basic-option")=="skip"); ReachPlay(g);
        Gift(g,3,1); ReachPlay(g);
        Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(),"Only the first crossing offers a basic use."); Replay(g,r);
    }
    public static void VirtualBasicSlashAndAlcohol()
    {
        foreach(var kind in new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash,CardKind.Alcohol})
        {
            var(g,r)=Start(); Gift(g,1,3); Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-basic"));
            Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Single().PhaseGiftReceipt is {PreviousCount:0,ActualCount:3},"A batch of three crosses once by actual movement.");
            Replay(g,r); Answer(g,c=>c.Parameters.GetValueOrDefault("basic-option")?.StartsWith(kind+":",StringComparison.Ordinal)==true); ReachPlay(g);
            Require(g.Events.Select(e=>e.Payload).OfType<CardUseFinishedEvent>().Any(e=>e.CardKind==kind&&e.CardId==0),"The basic use completes a real typed zero-entity action.");
            Require(g.CardMovements.Count(m=>m.From==CardLocation.Hand(0)&&m.To==CardLocation.Hand(1))==3,"The original gift is paid once across the child."); Replay(g,r);
        }
    }

    public static void ActualOutsideResponseRewardChooser()
    {
        var(g,r)=Start("reward");
        Reach(g,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0);
        Answer(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jijiang"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        var p=Prompt(g)!;
        Require(p.PlayerSeat==0&&p.TargetSeat==0&&p.SourceSeat!=0,"Actual Shu responder chooses; the Lord is the beneficiary, not the chooser.");
        var lord=p.SourceSeat!.Value;
        var candidate=g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Candidates.Single(c=>c.SkillId=="boundary:jijiang");
        Require(candidate.OwnerSeat==lord&&candidate.FrozenContext?.OptionalChooserSeat==0,"Exact source owner remains the Lord.");
        var action=g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action;
        Require(action.Type==CardActionType.Response&&action.ActorSeat==0&&action.ProviderSeat==0&&action.FactionOrigin is {ActorFactionId:"shu"} o&&o.ActualTurnOwnerSeat!=0,"Actual outside response origin is frozen independently of the beneficiary.");
        Replay(g,r); Reject(g); Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        Reach(g,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0); Answer(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jijiang");
        Require(Prompt(g)!.SourceSeat==lord,"Decline preserves the shared Lord quota for a later genuine response.");
        Replay(g,r); var before=g.CreateSnapshot(0).Players[lord].HandCount; Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Require(g.CreateSnapshot(0).Players[lord].HandCount==before+1,"Accepted reward draws one real card for the Lord."); Replay(g,r);
        Reach(g,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0);Answer(g,c=>c.Cards.Count==1);
        Require(!g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w=>w.Candidates.Any(c=>c.SkillId=="boundary:jijiang"&&c.OwnerSeat==lord)),"Accepted named Lord quota excludes a later genuine response in the same actual turn.");Replay(g,r);
    }
    public static void ActualProviderReward()
    {
        var(g,r)=Start("provider");
        Answer(g,c=>c.Cards.Count==1);Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jijiang");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        Reach(g,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="faction-slash-slash"));Replay(g,r);Reject(g);
        var paid=Prompt(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("response")=="faction-slash-slash").Cards.Single();
        Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="faction-slash-slash");Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jijiang");
        var w=g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();
        Require(w.Action.ProviderSeat==0&&w.Action.ActorSeat!=0&&w.Action.FactionOrigin?.ProviderFactionId=="shu"&&Prompt(g)!.PlayerSeat==0,"Actual provider, independently of requester, chooses the reward.");Replay(g,r);
        var lord=Prompt(g)!.SourceSeat!.Value;var before=g.CreateSnapshot(0).Players[lord].HandCount;Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Require(g.CreateSnapshot(0).Players[lord].HandCount==before+1&&g.CardMovements.Count(m=>m.CardId==paid&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1,"Provider cost is paid once and the actual Lord gains one.");Replay(g,r);
        var(d,dr)=Start("provider");Answer(d,c=>c.Cards.Count==1);Reach(d,p=>p.SkillPrompt?.SkillId=="boundary:jijiang");Answer(d,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        Reach(d,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="faction-slash-slash"));Answer(d,c=>c.Parameters.GetValueOrDefault("response")=="faction-slash-slash");Reach(d,p=>p.SkillPrompt?.SkillId=="boundary:jijiang");
        var actionId=d.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action.ActionId;Answer(d,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
        Require(!d.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(x=>x.Action.ActionId==actionId),"Declining the actual provided response completes its exact action window, without a second same-action reward prompt.");Replay(d,dr);
    }
    public static void PaidMovementAndSourceLifecycle()
    {
        var(g,r)=Start("payment"); Gift(g,1,1);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lb-movement");
        var outer=g.ResolutionStack.OfType<ProgramSkillFrame>().First();
        Require(outer.SelectedCardPayment is {MovementCommitted:true}&&outer.PhaseGiftReceipt is {PreviousCount:0,ActualCount:1},"The actual gift commits and counts before its movement child.");
        Replay(g,r);Reject(g);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");ReachPlay(g);Replay(g,r);
        var owner=((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!)[0];
        var source=owner.SkillGrants.Grants.Single(x=>x.SkillId=="boundary:rende");owner.SkillGrants.RemoveGrant(source.GrantId);
        owner.SkillGrants.Grant(source with{GrantId=source.GrantId+".again",SkillInstanceId=source.SkillInstanceId+".again"});
        Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="boundary:rende"&&a.TargetSeats.Contains(1)),"Host source regrant does not refresh recipient usage.");
        Gift(g,2,1);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lb-movement");
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().First().PhaseGiftReceipt is {PreviousCount:1,ActualCount:1},"Host regrant preserves actual phase gift count.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-basic"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("basic-option")=="skip");ReachPlay(g);
        Require(g.CardMovements.Count(m=>m.From==CardLocation.Hand(0)&&m.To.Zone==CardZoneKind.Hand)==2,"Paid entities move once across both suspended movement children.");
    }
    public static void SlashLimitAndPeach()
    {
        var(g,r)=Start("slash");var a=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash);
        Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,Prompt(g)!.PromptId));ReachPlay(g);
        Gift(g,1,2);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-basic"));
        Require(!Prompt(g)!.Choices.Any(c=>new[]{"Slash:","FireSlash:","ThunderSlash:"}.Any(x=>c.Parameters.GetValueOrDefault("basic-option")?.StartsWith(x,StringComparison.Ordinal)==true))&&Prompt(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("basic-option")?.StartsWith("Alcohol:",StringComparison.Ordinal)==true),"Spent real Slash removes only Slash, leaving legal Alcohol.");
        Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("basic-option")=="skip");ReachPlay(g);
        var(peach,pr)=Start("peach");Accept(peach,new UseProgramSkillCommand(0,"fixture:lb-driver","hurt",[],[],peach.Revision,Prompt(peach)!.PromptId));ReachPlay(peach);
        var hp=peach.CreateSnapshot(0).Players[0].Hp;Gift(peach,1,2);Reach(peach,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("basic-option")?.StartsWith("Peach:",StringComparison.Ordinal)==true));Replay(peach,pr);
        Answer(peach,c=>c.Parameters.GetValueOrDefault("basic-option")?.StartsWith("Peach:",StringComparison.Ordinal)==true);ReachPlay(peach);
        Require(peach.CreateSnapshot(0).Players[0].Hp==hp+1,"Virtual Peach enters actual Recovery and returns after completion.");Replay(peach,pr);
    }
    public static void VirtualPeachHpChild()
    {
        var(g,r)=Start("peach-child");Accept(g,new UseProgramSkillCommand(0,"fixture:lb-driver","hurt",[],[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);
        var hp=g.CreateSnapshot(0).Players[0].Hp;Gift(g,1,2);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("basic-option")?.StartsWith("Peach:",StringComparison.Ordinal)==true));
        Answer(g,c=>c.Parameters.GetValueOrDefault("basic-option")?.StartsWith("Peach:",StringComparison.Ordinal)==true);Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:lb-recovery");
        var use=g.ResolutionStack.OfType<CardUseFrame>().Single();var child=g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Require(use.CardId==0&&use.VirtualBasicEffectApplied==true&&child.ResumeFrameId==use.Id&&child.Continuation==PostEventContinuation.VirtualBasicCardUse&&g.CreateSnapshot(0).Players[0].Hp==hp+1,"True Recovery pauses with an exact zero-entity paid-effect parent.");
        Require(!g.Events.Select(e=>e.Payload).OfType<CardUseFinishedEvent>().Any(e=>e.CardKind==CardKind.Peach),"Peach is not finished before its HP child.");Replay(g,r);Reject(g);
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[0].Hp==hp+1&&g.Events.Select(e=>e.Payload).OfType<CardUseFinishedEvent>().Count(e=>e.CardKind==CardKind.Peach)==1&&g.CardMovements.Count(m=>m.From==CardLocation.Hand(0)&&m.To==CardLocation.Hand(1))==2,"Recovery, gift payment and use completion each occur once after child restore.");Replay(g,r);
    }
    public static void ExtraPlayRefresh()
    {
        var(g,r)=Start("extra");Gift(g,1,2);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="virtual-basic"));Answer(g,c=>c.Parameters.GetValueOrDefault("basic-option")=="skip");ReachPlay(g);Replay(g,r);
        var turn=g.CreateSnapshot(0).TurnNumber;Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));ReachPlay(g);
        Require(g.CreateSnapshot(0).TurnNumber==turn,"Dangxian resumes a new Play within the same actual turn.");
        Gift(g,1,1);ReachPlay(g);Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(),"Same recipient is available and the count restarts in the new Play.");Replay(g,r);
    }
    public static void VirtualSlashActorReplacement()
    {
        var(g,r)=Start("role");Gift(g,1,2);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("basic-option")=="Slash:1"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("basic-option")=="Slash:1");
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:zenhui"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.SequenceEqual(new[]{3}));
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="become-user");Replay(g,r);Answer(g,c=>c.Cards.Count==1);
        Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jijiang");
        var use=g.ResolutionStack.OfType<CardUseFrame>().Single();var parent=g.ResolutionStack.OfType<ProgramSkillFrame>().First();
        Require(use.SourceSeat==3&&use.Action is {ActorSeat:3,ProviderSeat:0,FactionOrigin:{ActorFactionId:"shu",ProviderFactionId:"qun"}}&&use.VirtualBasicReturn?.ParentFrameId==parent.Id&&parent.VirtualBasicDraft?.ChildFrameId==use.Id,"Role replacement preserves the original virtual offer/provider ownership and captures the actual new actor's faction.");
        Require(Prompt(g)!.PlayerSeat==3&&Prompt(g)!.SourceSeat==0,"The actual out-of-turn Shu actor chooses the original Lord's reward despite its Qun provider.");Replay(g,r);Reject(g);
        Require(use.FactionRewardOffers is System.Collections.IList{IsReadOnly:true}&&use.FactionRewardOffers.SequenceEqual(new[]{new ProgramFactionRewardOffer(0,"boundary:jijiang")}),"The exact owner/skill reward offer receipt is deeply readonly on its owning Use frame.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");ReachPlay(g);
        Require(g.Events.Select(e=>e.Payload).OfType<CardUseFinishedEvent>().Count(e=>e.CardId==0&&e.CardKind==CardKind.Slash)==1&&g.CardMovements.Count(m=>m.From==CardLocation.Hand(0)&&m.To==CardLocation.Hand(1))==2,"Replaced virtual Slash completes once and returns to the paid gift program.");Replay(g,r);
    }
    public static void NativeProvidedUseDeclineOnce()
    {
        var(g,r)=Start("active-provider");
        for(var request=0;request<4;request++)
        {
            Accept(g,new UseProgramSkillCommand(0,"boundary:jijiang","request-shu-slash",[],[1],g.Revision,Prompt(g)!.PromptId));
            if(request==0)
            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="classic:zenhui");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
                Answer(g,c=>c.Targets.SequenceEqual(new[]{3}));Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="extra-target");
            }
            if(request<2)
            {
                Reach(g,p=>p.SkillPrompt?.SkillId=="boundary:jijiang"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
                var window=g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last();var action=window.Action;var physical=action.PhysicalCards.Select(c=>c.CardId).ToArray();
                Require(action.Type==CardActionType.Use&&action.ActorSeat==0&&action.ProviderSeat!=0&&Prompt(g)!.PlayerSeat==action.ProviderSeat,"A native active faction request publishes the genuine provider's single final-use reward choice.");Replay(g,r);
                Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")== (request==0?"skip":"activate"));
                for(var step=0;step<80&&g.ResolutionStack.Count>0;step++)
                {
                    Require(Prompt(g)?.SkillPrompt?.SkillId!="boundary:jijiang","The same provided use cannot re-prompt its reward after response/final-use continuation, including decline.");
                    if(Prompt(g) is {Kind:DecisionKind.RespondDodge,PlayerSeat:0})Answer(g,c=>c.Cards.Count==0);else Accept(g,new AdvanceOneStepCommand(g.Revision));
                }
                Require(g.ResolutionStack.Count==0&&physical.All(id=>g.CardMovements.Count(m=>m.CardId==id&&m.To==CardLocation.Processing)==1)&&g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Count(e=>e.Action.PhysicalCards.Any(c=>physical.Contains(c.CardId)))==1,"Native supplied use has one actual action/payment rather than duplicate internal Response and final Use facts.");ReachPlay(g);Replay(g,r);
            }
            else
            {
                for(var step=0;step<80&&g.ResolutionStack.Count>0;step++){Require(Prompt(g)?.SkillPrompt?.SkillId!="boundary:jijiang","Accepted per-Lord actual-turn quota excludes a third genuine supplied use.");Accept(g,new AdvanceOneStepCommand(g.Revision));}
                Require(g.ResolutionStack.Count==0,"A repeated real faction request remains legal after reward quota exhaustion.");ReachPlay(g);Replay(g,r);
            }
        }
        Require(g.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().Any(e=>e.Action.Type==CardActionType.Use&&e.Action.ActorSeat==0&&e.Action.ProviderSeat==2),"A second genuine Shu provider fulfills a repeated request while sharing the same exhausted Lord quota.");
    }
    public static void OriginAndLegacyMechanism()
    {
        var(g,rAudit)=Start();var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
        var replace=typeof(GameEngine).GetMethod("CaptureReplacedFactionActor",flags)!;
        var original=new CardActionContext(1,null,CardActionType.Use,0,0,null,null,null,CardKind.Slash,[2],[],[],effectiveIsRed:true,factionOrigin:new(7,3,"qun","qun"));
        var changed=(CardActionContext)replace.Invoke(g,[original,1])!;
        Require(changed.ActorSeat==1&&changed.FactionOrigin==new CardActionFactionOrigin(7,3,"shu","qun")&&original.FactionOrigin!.ActorFactionId=="qun","Explicit actor replacement freezes the new effective actor faction while preserving original turn/provider facts.");
        var chooser=typeof(GameEngine).GetMethod("FactionActionRewardChooser",flags)!;var trigger=rAudit.GetSkill("boundary:jijiang").Program!.Triggers.First();
        var model=new CardActionContext(3,null,CardActionType.Use,1,0,null,null,null,CardKind.Slash,[2],[],[],factionOrigin:new(g.CreateSnapshot(0).TurnNumber,0,"shu","qun"));
        Require((int?)chooser.Invoke(g,[3,trigger,model])==1,"A non-beneficiary actor owns ordinary use reward even when another seat supplied the card.");
        var negative=new CardActionContext(4,null,CardActionType.Use,1,0,null,null,null,CardKind.Slash,[2],[],[],factionOrigin:new(g.CreateSnapshot(0).TurnNumber,0,"qun","shu"));
        Require(chooser.Invoke(g,[3,trigger,negative]) is null,"A non-Shu actor cannot borrow a Shu provider's reward qualification.");
        var ownTurn=new CardActionContext(5,null,CardActionType.Response,1,1,null,1,null,CardKind.Slash,[],[],[],factionOrigin:new(g.CreateSnapshot(0).TurnNumber,1,"shu","shu"));
        Require(chooser.Invoke(g,[3,trigger,ownTurn]) is null,"The genuine actor's own actual turn excludes an otherwise eligible Shu response.");
        var lordUse=new CardActionContext(6,null,CardActionType.Use,3,0,3,null,null,CardKind.Slash,[2],[],[],factionOrigin:new(g.CreateSnapshot(0).TurnNumber,0,"shu","shu"));
        Require(chooser.Invoke(g,[3,trigger,lordUse]) is null,"A Lord's final use cannot qualify a genuine provider who is the actual turn owner.");
        var clone=typeof(GameEngine).GetMethod("CloneRoleAction",flags)!;
        var sameActor=(CardActionContext)clone.Invoke(g,[original,0,new[]{2,3}])!;
        Require(sameActor.FactionOrigin==original.FactionOrigin&&sameActor.EffectiveIsRed==true,"Ordinary opted-in role/target clones preserve frozen faction provenance and effective color.");
        var plain=new CardActionContext(2,null,CardActionType.Use,1,0,null,null,null,CardKind.Slash,[2],[],[]);
        Require(ReferenceEquals(plain,replace.Invoke(g,[plain,1]))&&!JsonSerializer.Serialize(plain).Contains("FactionOrigin",StringComparison.Ordinal),"No-cap actor clone retains absent optional origin.");
        var(old,r)=Start("slash",legacy:true);var a=old.GetHumanLegalActions().First(x=>x.Kind==LegalActionKind.Slash);
        Accept(old,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,old.Revision,Prompt(old)!.PromptId));ReachPlay(old);
        Require(old.Events.Select(e=>e.Payload).OfType<CardActionAcceptedEvent>().All(e=>e.Action.FactionOrigin is null),"Actual native use in a registry without reward capability captures no new faction fact.");Replay(old,r);
    }
    private sealed class WithoutReward(ContentRegistry source):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:lb-legacy",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {foreach(var c in source.Cards.Values)b.AddCard(c);foreach(var s in source.Skills.Values)b.AddSkill(s.Id=="boundary:jijiang"?s with{Program=null}:s);foreach(var g in source.Generals.Values)b.AddGeneral(g);foreach(var d in source.Decks.Values)b.AddDeck(d);foreach(var m in source.Modes.Values)b.AddMode(m);}
    }
    private static (GameEngine,ContentRegistry) Start(string mode="gift",bool legacy=false)
    {
        ContentRegistry r; try { r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(mode)); } catch(Exception e) { throw new InvalidOperationException(e.ToString()); }
        if(legacy)r=ContentRegistry.Build(new WithoutReward(r));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=mode is "reward" or "provider"?Role.Renegade:Role.Lord,ModeId="identity:classic-lb-fixture",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,mode is "reward" or "provider"?Prompt(g)!.Choices[0].ContentIds[0]:"fixture:lb-owner",g.Revision,Prompt(g)!.PromptId));if(mode is "reward" or "provider")Reach(g,p=>p.Kind==DecisionKind.RespondSlash&&p.PlayerSeat==0);else ReachPlay(g);return(g,r);
    }
    private static PendingDecision? Prompt(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Require(bool c,string m){if(!c)throw new InvalidOperationException(m);}
    private static void Accept(GameEngine g,GameCommand c){var q=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(q.Accepted,q.Error?.Message??"Rejected");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> f){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(f).Id,g.Revision));}
    private static void Gift(GameEngine g,int recipient,int count)=>Accept(g,new UseProgramSkillCommand(0,"boundary:rende","give",Hand(g,0).Take(count).Select(c=>c.Id).ToArray(),[recipient],g.Revision,Prompt(g)!.PromptId));
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> f)
    {
        for(var i=0;i<80;i++){var p=Prompt(g);if(p is not null&&f(p))return;if(p is {PlayerSeat:0,Kind:DecisionKind.RespondDodge})Answer(g,c=>c.Cards.Count==0);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
        throw new InvalidOperationException("Fixed fixture prompt not reached: "+JsonSerializer.Serialize(Prompt(g)));
    }
    private static IReadOnlyList<Card> Hand(GameEngine g,int seat)
    {var z=typeof(GameEngine).GetField("_cardZones",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;return(IReadOnlyList<Card>)z.GetType().GetMethod("CardsAt")!.Invoke(z,[CardLocation.Hand(seat)])!;}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=JsonSerializer.Serialize(g.ResolutionStack),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
    private static void Replay(GameEngine g,ContentRegistry r)=>Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Four views, typed stack, events, movement and commands roundtrip.");
    private static void Reject(GameEngine g){var before=State(g);var p=Prompt(g)!;Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,new("fixture:bad"),g.Revision)).Accepted&&before==State(g),"Illegal choice is atomic.");}
    private sealed class Fixture(string mode):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-lb",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var driver=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:lb-driver","revision":1,"activations":[{"id":"hurt","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:lb-driver":{"name":"驱动","description":"真实损失"}}}""");
            foreach(var pair in driver.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            if(mode=="payment")
            {
                var movement=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:lb-movement","revision":1,"triggers":[{"id":"pause","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:rende.GiveSelected"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"step","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:lb-movement":{"name":"移动暂停","description":"真实付款子链","optionLabels":{"continue":"继续"}}}}""");
                foreach(var pair in movement.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            }
            if(mode=="peach-child")
            {
                var recovery=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:lb-recovery","revision":1,"triggers":[{"id":"pause","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"step","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:lb-recovery":{"name":"回复暂停","description":"真实HP子链","optionLabels":{"continue":"继续"}}}}""");
                foreach(var pair in recovery.Programs)b.AddSkill(new(pair.Key,pair.Key,pair.Key){Program=pair.Value});
            }
            b.AddGeneral(new("fixture:lb-owner","固定主公","supporter",mode is "reward" or "provider"?"standard:none":"boundary:rende",mode=="role"?"qun":"shu",20,new[]{"boundary:jijiang","fixture:lb-driver"}.Concat(mode=="provider"?["classic:wusheng"]:Array.Empty<string>()).Concat(mode=="payment"?["fixture:lb-movement"]:mode=="peach-child"?["fixture:lb-recovery"]:mode=="extra"?["classic:dangxian"]:mode=="role"?["classic:zenhui"]:mode=="active-provider"?["classic:paoxiao","classic:zenhui"]:Array.Empty<string>()).ToArray()));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:lb-target-{i}","固定角色","supporter","standard:none","shu",20,mode=="provider"?["boundary:jijiang","classic:wusheng"]:mode=="reward"?["boundary:jijiang"]:mode=="active-provider"?["classic:wusheng"]:[]));
            b.AddDeck(new("fixture:lb-deck","实体",6,2,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard(mode is "reward" or "provider"?(i%2==0?"standard:duel":mode=="provider"?"standard:dodge":"standard:slash"):mode=="slash"?"standard:slash":"standard:dodge",Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-lb-fixture","固定",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Renegade)]=3},"fixture:lb-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:lb-owner","fixture:lb-target-1","fixture:lb-target-2","fixture:lb-target-3"]));
        }
    }
}
