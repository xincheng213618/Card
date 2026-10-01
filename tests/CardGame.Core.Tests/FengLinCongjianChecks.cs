using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinCongjianChecks
{
    public static void EquipmentGiftNestedReplay()=>Gift(true);
    public static void OrdinaryGiftAndDecline()=>Gift(false);
    private static void Gift(bool equipment)
    {
        var(g,r)=Create();Play(g);
        var conversion=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.IronChain && a.TargetSeats.SequenceEqual(new[]{0,1}) && g.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Id==a.CardId).Kind==CardKind.Peach);
        Submit(g,new PlayCardCommand(0,conversion.CardId!.Value,conversion.TargetSeats,g.Revision,P(g)!.PromptId,conversion.PlayedCardKind){ConversionSource=conversion.ConversionSource});
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:congjian");Replay(g,r);
        Require(g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action.EffectiveKind==CardKind.IronChain,"Actual converted Trick finalized window.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Require(P(g)!.Choices.All(c=>c.Targets is [var target] && target!=0) && P(g)!.Choices.SelectMany(c=>c.Targets).Distinct().Order().SequenceEqual(new[]{1}),"Recipient set is exact final other targets.");
        var paused=GameCheckpointJson.Serialize(g.CreateCheckpoint());
        var rejected=g.Submit(new AnswerPromptCommand(0,P(g)!.PromptId,new("invalid.non-target-or-card"),g.Revision));
        Require(!rejected.Accepted && GameCheckpointJson.Serialize(g.CreateCheckpoint())==paused,"Invalid unpublished payment rejects atomically.");
        var snapshots=g.CreateSnapshot(0).Players[0].Hand;
        var choice=P(g)!.Choices.First(c=>c.Targets.Contains(1) && c.Cards.Any(id=>EquipmentCatalog.IsEquipment(snapshots.Single(card=>card.Id==id).Kind)==equipment));
        var id=choice.Cards.Single();var count=Hand(g,0);Answer(g,c=>c.Id==choice.Id);
        var receipt=g.Events.Select(e=>e.Payload).OfType<ProgramFinalTargetGiftCommittedEvent>().Single();
        Require(receipt.CardId==id && receipt.RecipientSeat==1 && receipt.ReceiptOrdinal is {} ordinal && g.CardMovements.Single(m=>m.Sequence==ordinal).To==CardLocation.Hand(1),"Gift receipt is real owner HE to other final Hand.");
        Require(!g.CardMovements.Any(m=>m.Reason.Value=="skill-program.final-target-gift.draw"),"Reward waits for nested gift movement children.");Replay(g,r);
        Until(g,()=>g.CardMovements.Any(m=>m.Reason.Value=="skill-program.final-target-gift.draw"));
        Require(g.CardMovements.Count(m=>m.Reason.Value=="skill-program.final-target-gift.draw")== (equipment?2:1) && Hand(g,0)==count-1+(equipment?2:1),"Frozen physical category gives exact reward after child.");
        Require(P(g)?.SkillPrompt?.SkillId=="fixture:reward-pause","Reward real movement child pauses before original Trick effects.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
        Until(g,()=>!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="classic:congjian"));
        Require(g.Events.Count(e=>e.Payload is ProgramFinalTargetGiftCommittedEvent)==1 && g.CardMovements.Count(m=>m.Reason.Value=="skill-program.final-target-gift.give")==1,"Restore never pays twice.");Replay(g,r);
        if(!equipment)
        {
            (g,r)=Create();Play(g);conversion=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.IronChain && a.TargetSeats.SequenceEqual(new[]{0,1}) && g.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Id==a.CardId).Kind==CardKind.Peach);
            Submit(g,new PlayCardCommand(0,conversion.CardId!.Value,conversion.TargetSeats,g.Revision,P(g)!.PromptId,conversion.PlayedCardKind){ConversionSource=conversion.ConversionSource});
            Reach(g,p=>p.SkillPrompt?.SkillId=="classic:congjian");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
            Require(!g.Events.Any(e=>e.Payload is ProgramFinalTargetGiftCommittedEvent) && !g.CardMovements.Any(m=>m.Reason.Value.StartsWith("skill-program.final-target-gift")),"Decline has no cost or reward.");Replay(g,r);
        }
    }
    public static void OwnerDeathAndMultiGrantBoundaries()
    {
        var(g,r)=Create();Play(g);
        Submit(g,new UseProgramSkillCommand(0,"fixture:trick","duplicate",[],[],g.Revision,P(g)!.PromptId));Play(g);
        var a=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.IronChain && a.TargetSeats.SequenceEqual(new[]{0,1}));
        Submit(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind){ConversionSource=a.ConversionSource});
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:congjian");
        Require(g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Candidates.Count(c=>c.SkillId=="classic:congjian")==1,"Multiple real grants give one exact-instance action candidate.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");Play(g);
        Require(g.Events.Select(e=>e.Payload).OfType<ProgramCardTriggerResolvedEvent>().Count(e=>e.SkillId=="classic:congjian")==1,"Declined duplicate grants do not re-offer same action.");
        (g,r)=Create();Play(g);a=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.IronChain && a.TargetSeats.SequenceEqual(new[]{0}) && g.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Id==a.CardId).Kind==CardKind.Peach);
        Submit(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind){ConversionSource=a.ConversionSource});Play(g);
        Require(!g.Events.Any(e=>e.Payload is ProgramBindingStartedEvent{SkillId:"classic:congjian"}),"Single finalized Trick target is negative.");Replay(g,r);
        var equipment=g.CreateSnapshot(0).Players[0].Hand.First(c=>c.Kind==CardKind.Crossbow);
        Submit(g,new PlayCardCommand(0,equipment.Id,[],g.Revision,P(g)!.PromptId));Play(g);
        Require(!g.Events.Any(e=>e.Payload is ProgramBindingStartedEvent{SkillId:"classic:congjian"}),"Equipment use is negative.");
        (g,r)=Create(giftKillsOwner:true);Play(g);a=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.IronChain && a.TargetSeats.SequenceEqual(new[]{0,1}));
        Submit(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind){ConversionSource=a.ConversionSource});Reach(g,p=>p.SkillPrompt?.SkillId=="classic:congjian");
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.Contains(1));
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:gift-pause" && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));Replay(g,r);
        Answer(g,c=>c.Targets.Contains(0));Until(g,()=>!g.CreateSnapshot(0).Players[0].IsAlive);
        Require(g.CardMovements.Count(m=>m.Reason.Value=="skill-program.final-target-gift.give")==1 && !g.CardMovements.Any(m=>m.Reason.Value=="skill-program.final-target-gift.draw"),"Owner death during gift child cancels reward without repeating payment.");Replay(g,r);
    }
    public static void ChangedFinalTargetsEquipmentZoneAndMovedReceipt()
    {
        var(g,r)=Create(discardGift:true,addTarget:true);Play(g);
        var weapon=g.CreateSnapshot(0).Players[0].Hand.Single(c=>c.Kind==CardKind.Crossbow);
        Submit(g,new PlayCardCommand(0,weapon.Id,[],g.Revision,P(g)!.PromptId));Play(g);
        var a=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.IronChain && a.TargetSeats.SequenceEqual(new[]{0}));
        Submit(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind){ConversionSource=a.ConversionSource});
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:target-add");Answer(g,c=>c.Targets.Contains(1));
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:congjian");
        Require(g.ResolutionStack.OfType<CardUseFrame>().Last().Action!.EffectiveDesignatedTargetSeats.SequenceEqual(new[]{0,1}),"Real finalized target change occurs before gift candidate.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Require(P(g)!.Choices.All(c=>c.Targets.SequenceEqual(new[]{1})),"Gift uses updated final recipient set rather than original single designation.");
        Answer(g,c=>c.Cards.Contains(weapon.Id));
        var due=g.Events.Select(e=>e.Payload).OfType<ProgramFinalTargetGiftCommittedEvent>().Single();
        Require(g.CardMovements.Single(m=>m.Sequence==due.ReceiptOrdinal).From==CardLocation.Equipment(0),"Equipped HE entity is real payment.");Replay(g,r);
        Until(g,()=>g.CardMovements.Any(m=>m.Reason.Value=="skill-program.final-target-gift.draw"));
        Require(g.CreateCardZoneDiagnostics().Single(c=>c.CardId==weapon.Id).Location==CardLocation.DiscardPile && g.CardMovements.Count(m=>m.Reason.Value=="skill-program.final-target-gift.draw")==2,"Receipt remains paid after child moves gifted Equipment out of hand.");Replay(g,r);
    }
    public static void StrictFinalGiftResources()
    {
        foreach(var (window,target,extra) in new[]{("cardUseBeforeTargetEffects","owner",""),("cardUseTargetsFinalized","selectedTarget",""),("cardUseTargetsFinalized","owner",",\"amount\":2")})
        {
            var rejected=false;
            try
            {
                SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid-gift","revision":1,"triggers":[{"id":"gift","window":"{{window}}","ownerRelation":"target","optional":true,"effects":[{"op":"giveOwnedCardToOtherFinalTargetAndDraw","target":"{{target}}"{{extra}}}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:invalid-gift":{"name":"非法","description":"测试"}}}""");
            }
            catch(InvalidOperationException){rejected=true;}
            Require(rejected,"Generic final gift rejects wrong window, nonowner target and unused amount resource.");
        }
    }
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static int Hand(GameEngine g,int s)=>g.CreateSnapshot(0).Players[s].HandCount;
    private static void Submit(GameEngine g,GameCommand c){var x=g.Submit(c);Require(x.Accepted,x.Error?.Message??"Rejected");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> f){var p=P(g)!;Submit(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(f).Id,g.Revision));}
    private static void Play(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0 && p.Kind==DecisionKind.PlayCard);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> f)=>Until(g,()=>P(g)is{}p&&f(p));
    private static void Until(GameEngine g,Func<bool> f){for(int i=0;i<70;i++){if(f())return;Tick(g);}throw new InvalidOperationException("Boundary missing: "+JsonSerializer.Serialize(P(g)));}
    private static void Tick(GameEngine g){if(P(g)is{PlayerSeat:0}p)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("response")=="let-die"||c.Cards.Count==0);else Submit(g,new AdvanceOneStepCommand(g.Revision));}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).SelectMany(s=>new[]{g.CreateSnapshot(s),g.CreateSnapshot(s,true)}))+JsonSerializer.Serialize(g.CreateCardZoneDiagnostics());
    private static void Replay(GameEngine g,ContentRegistry r){var x=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(x),"All four normal/AI views and physical zones replay.");Require(g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==100,"All physical entities conserved.");}
    private static void Require(bool b,string m){if(!b)throw new InvalidOperationException(m);}
    private static (GameEngine,ContentRegistry) Create(bool giftKillsOwner=false,bool discardGift=false,bool addTarget=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(giftKillsOwner,discardGift,addTarget));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-congjian-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Submit(g,new StartGameCommand());Submit(g,new SelectGeneralCommand(0,"fixture:congjian-owner",g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(bool giftKillsOwner,bool discardGift,bool addTarget):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:congjian",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var giftPrefix=discardGift?"{\"op\":\"discardOwnedZoneCards\",\"target\":\"owner\",\"zones\":[\"hand\"]},":giftKillsOwner?"{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"anyLiving\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20},":"";
            var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:trick","revision":1,"activations":[{"id":"duplicate","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantTurnSkills","target":"owner","skillIds":["classic:congjian"]}]}],"viewAs":[{"id":"grace","inputKinds":["crossbow","peach"],"inputSuits":[],"outputKind":"ironChain","forPlay":true,"forResponse":false}]},{"id":"fixture:gift-pause","revision":1,"triggers":[{"id":"gift","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.final-target-gift.give"],"optional":false,"effects":[{{giftPrefix}}{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]},{"id":"fixture:reward-pause","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.final-target-gift.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:trick":{"name":"转换","description":"测试"},"fixture:gift-pause":{"name":"赠牌暂停","description":"测试","optionLabels":{"continue":"继续"}},"fixture:reward-pause":{"name":"奖励暂停","description":"测试","optionLabels":{"continue":"继续"}}}}""");
            var add=SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:target-add","revision":1,"triggers":[{"id":"add","window":"cardUseTargetsFinalized","ownerRelation":"actor","optional":false,"priority":100,"condition":{"kind":"compare","left":{"kind":"cardUseDesignatedTargetCount"},"operator":"equal","right":{"kind":"integerConstant","value":1}},"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"addCurrentCardUseTarget","target":"selectedTarget"}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:target-add":{"name":"增加最终目标","description":"测试"}}}""");
            if(addTarget)b.AddSkill(new("fixture:target-add","增加最终目标","测试"){Program=add.Programs["fixture:target-add"]});
            else b.AddSkill(new("fixture:target-add","静态","测试"));
            foreach(var id in c.Programs.Keys)b.AddSkill(new(id,id,"测试"){Program=c.Programs[id]});
            b.AddGeneral(new("fixture:congjian-owner","拥有者","supporter","classic:congjian","qun",giftKillsOwner?3:20,["fixture:trick","fixture:reward-pause", "fixture:target-add"]));
            for(int i=1;i<4;i++)b.AddGeneral(new($"fixture:congjian-target-{i}","目标","supporter","fixture:gift-pause","wei",20));
            b.AddDeck(new("fixture:congjian-deck","固定",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(giftKillsOwner||i%2==0?"standard:crossbow":"standard:peach",i%2==0?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-congjian-check","测试",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:congjian-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:congjian-owner","fixture:congjian-target-1","fixture:congjian-target-2","fixture:congjian-target-3"]));
        }
    }
}
