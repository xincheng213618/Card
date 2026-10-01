using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class FengLinKuaiChecks
{
    public static void JianxiangActualFinalTargetMinimumTie()
    {
        var(g,r)=Create(slashDeck:true);ReachPlay(g);Driver(g,"discard",[]);Driver(g,"grow-owner",[]);Driver(g,"request",[1]);
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:jianxiang");
        Require(g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action.ActorSeat==1,"Actual other actor finalized card target triggers native Jianxiang.");
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        var candidates=P(g)!.Choices.SelectMany(c=>c.Targets).Distinct().Order().ToArray();
        Require(candidates.SequenceEqual(new[]{0,1}),"Actual finalized-target prompt keeps owner and tied other minimum after source Slash payment.");
        var before=Hand(g,0).Length;Replay(g,r);Answer(g,c=>c.Targets.Contains(0));
        ReachPlay(g);Require(Hand(g,0).Length==before+1,"One native reward draw completes before original Slash effect resumes.");Replay(g,r);
    }
    public static void YangGiftAndPrivateYinReplay()
    {
        var (g,r)=Create(); ReachPlay(g); Driver(g,"grow",[1]);
        var id=Hand(g,0).First(); Yang(g,id,1); Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:gift-observer");
        Require(!g.Events.Any(e=>e.Payload is DamageAppliedEvent),"Real gift movement child precedes Yang damage."); Replay(g,r);
        ReachPlay(g);Require(g.Events.Any(e=>e.Payload is DamageAppliedEvent{SourceSeat:0,TargetSeat:1,Amount:1}),"Damage follows completed gift child.");
        Require(Polarity(g)==SkillPolarity.Yin,"Yang commits Yin once.");
        Driver(g,"hurt",[1]);Reach(g,p=>p.SkillPrompt?.SkillId=="classic:shenshi"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("gift-action")=="observed"));
        var observed=Hand(g,1).Order().ToArray();
        Require(g.CreateSnapshot(0).PrivateRevealedCards!.Select(c=>c.Id).Order().SequenceEqual(observed) && g.CreateSnapshot(0,true).PrivateRevealedCards!.Count==observed.Length,"Owning human and owner AI view see whole precise source hand.");
        Require(Enumerable.Range(1,3).All(v=>g.CreateSnapshot(v).PrivateRevealedCards is null&&g.CreateSnapshot(v,true).PrivateRevealedCards is null),"Source/others and other AI views receive no private observation projection.");
        Require(!g.CreateSnapshot(2).PublicRevealedCards.Any(c=>observed.Contains(c.Id)),"Source hand never enters public reveal.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("gift-action")=="observed");var gift=Hand(g,0).First();Answer(g,c=>c.Cards.Contains(gift));ReachPlay(g);
        Require(Polarity(g)==SkillPolarity.Yang && g.GetHumanLegalActions().All(a=>a.ProgramSkillId!="classic:shenshi"),"Yin returns Yang without reopening spent Yang quota in this Play.");
        var due=g.Events.Select(e=>e.Payload).OfType<GiftHandRetentionScheduledEvent>().Single().Obligation;
        Require(due.CardId==gift&&g.CardMovements.Single(m=>m.Sequence==due.GiftOrdinal) is {To:{Zone:CardZoneKind.Hand,OwnerSeat:1}},"Delay starts at actual recipient-hand movement ordinal.");
        Driver(g,"lose",[]);Driver(g,"discard",[]);End(g);ReachUntil(g,()=>g.Events.Any(e=>e.Payload is GiftHandRetentionConsumedEvent));
        Require(g.Events.Select(e=>e.Payload).OfType<GiftHandRetentionConsumedEvent>().Single() is {Applied:true} && g.CreateSnapshot(0).Players[0].HandCount==4,"Issued obligation survives source skill loss and draws owner to four at current Ending.");
        Require(P(g)?.SkillPrompt?.SkillId=="fixture:retention-observer","Retention reward pauses a real gain child under its Ending coordinator.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");ReachPlay(g);Require(g.Events.Count(e=>e.Payload is GiftHandRetentionConsumedEvent)==1,"Retention reward resumption never consumes or draws the due twice.");Replay(g,r);
    }
    public static void EndingChildGiftJoinsCurrentBoundary()
    {
        var(g,r)=Create(endingDamage:true);ReachPlay(g);Driver(g,"grow",[1]);Yang(g,Hand(g,0).First(),1);ReachPlay(g);
        Driver(g,"discard",[]);Driver(g,"grow-owner",[]);End(g);
        Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:ending-hurt" && p.Choices.Any(c=>c.Targets.Contains(1)));
        Answer(g,c=>c.Targets.Contains(1));
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:shenshi" && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
        Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("gift-action")=="observed");Answer(g,c=>c.Cards.Contains(Hand(g,0).First()));
        var due=g.Events.Select(e=>e.Payload).OfType<GiftHandRetentionScheduledEvent>().Single().Obligation;
        var boundary=g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        Require(boundary.TurnNumber==due.CreatedTurn && boundary.Items.Skip(boundary.ItemIndex+1).Any(i=>i.RetentionId==due.Id),"Ending child inserts exact due after its stable running item.");Replay(g,r);
        ReachUntil(g,()=>g.Events.Any(e=>e.Payload is GiftHandRetentionConsumedEvent));
        Require(g.Events.Select(e=>e.Payload).OfType<GiftHandRetentionConsumedEvent>().Single() is {Applied:true} && Hand(g,0).Length==4,"Gift created during Ending pays in that same Ending.");
        Require(P(g)?.SkillPrompt?.SkillId=="fixture:retention-observer","Reward movement remains under its typed Ending boundary.");Replay(g,r);
        Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
        ReachUntil(g,()=>!g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any());
        Require(g.Events.Count(e=>e.Payload is GiftHandRetentionConsumedEvent)==1 && g.CardMovements.Count(m=>m.Reason.Value=="skill-program.gift-retention.draw")==2,"Nested reward resumes without duplicate payment.");Replay(g,r);
    }
    public static void ExactDeathAndRetentionLeaveReturn()
    {
        var(g,r)=Create(lethal:true);ReachPlay(g);Driver(g,"grow",[1]);ReachPlay(g);Require(g.CreateSnapshot(0).Players[1].Hp==1,"Lethal fixture victim HP: "+g.CreateSnapshot(0).Players[1].Hp);Yang(g,Hand(g,0).First(),1);
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("gift-action")=="reward"));
        Require(!g.CreateSnapshot(0).Players[1].IsAlive,"Yang exact damage completes dying death before reward selection.");Replay(g,r);
        Answer(g,c=>c.Targets.Contains(2));ReachPlay(g);Require(g.CreateSnapshot(0).Players[2].HandCount>=4,"Exact death reward draws selected living player to four.");Replay(g,r);
        (g,r)=Create();ReachPlay(g);Driver(g,"grow",[1]);Yang(g,Hand(g,0).First(),1);ReachPlay(g);Driver(g,"hurt",[1]);
        Reach(g,p=>p.SkillPrompt?.SkillId=="classic:shenshi"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
        Answer(g,c=>c.Parameters.GetValueOrDefault("gift-action")=="observed");var gift=Hand(g,0).First();Answer(g,c=>c.Cards.Contains(gift));ReachPlay(g);
        var ordinal=g.Events.Select(e=>e.Payload).OfType<GiftHandRetentionScheduledEvent>().Single().Obligation.GiftOrdinal;
        Driver(g,"take",[1]);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-and-move-owned-card"));
        var slot=g.CreateCardZoneDiagnostics().Single(c=>c.CardId==gift).ZoneIndex;
        Answer(g,c=>c.Parameters.GetValueOrDefault("slot-index")==slot.ToString());ReachPlay(g);Driver(g,"give-back",[1],[gift]);ReachPlay(g);
        Require(g.CreateCardZoneDiagnostics().Single(c=>c.CardId==gift).Location==CardLocation.Hand(1)&&g.CardMovements.Any(m=>m.CardId==gift&&m.Sequence>ordinal&&m.From==CardLocation.Hand(1)),"Physical card truly leaves and returns to same recipient hand.");
        Driver(g,"discard",[]);End(g);ReachUntil(g,()=>g.Events.Any(e=>e.Payload is GiftHandRetentionConsumedEvent));
        Require(g.Events.Select(e=>e.Payload).OfType<GiftHandRetentionConsumedEvent>().Single() is {Applied:false} && g.CreateSnapshot(0).Players[0].HandCount==0,"Leave-return permanently fails retention reward.");Replay(g,r);
    }
    public static void GiftChildDeathHasNoDamageReward()
    {
        var(g,r)=Create(giftDeath:true);ReachPlay(g);Driver(g,"grow",[1]);Yang(g,Hand(g,0).First(),1);ReachPlay(g);
        Require(!g.CreateSnapshot(0).Players[1].IsAlive && !g.Events.Any(e=>e.Payload is DamageAppliedEvent),"Gift child death produces no Yang damage and no damage-death reward.");
        Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.ConvertingGift is not null),"Gift operation retired with no false reward cursor.");Replay(g,r);
    }
    public static void ExtremalTargetsAndStrictResources()
    {
        var(g,r)=Create();ReachPlay(g);Driver(g,"discard",[]);Driver(g,"minimum",[]);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-target"));
        Require(P(g)!.Choices.SelectMany(c=>c.Targets).Distinct().SequenceEqual([0]),"Minimum includes owner, even when uniquely empty.");Answer(g,c=>c.Targets.Contains(0));ReachPlay(g);
        Driver(g,"grow",[1]);Driver(g,"grow",[2]);ReachPlay(g);var action=g.GetHumanLegalActions().Single(a=>a.ProgramSkillId=="classic:shenshi");
        Require(action.SelectableTargetSeats!.Order().SequenceEqual(new[]{1,2}),"Tied other maximum hand targets are both legal: "+string.Join(",",action.SelectableTargetSeats!)+" hands "+string.Join(",",g.CreateSnapshot(0).Players.Select(p=>p.HandCount)));Replay(g,r);
        try{SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:bad-gift","revision":1,"activations":[{"id":"bad","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"giveSelectedOwnedCardAndDamage","target":"owner","amount":4}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:bad-gift":{"name":"测试","description":"测试"}}}""");throw new InvalidOperationException("Invalid gift resources accepted.");}catch(InvalidOperationException e){Require(e.Message.Contains("gift damage needs"),"Strict resources reject non-HE zero-cost gift damage: "+e.Message);}
    }
    private static SkillPolarity? Polarity(GameEngine g)=>g.CreateSnapshot(0).Players[0].SkillRuntimeStates!.Single(s=>s.SkillId=="classic:shenshi").Polarity;
    private static int[] Hand(GameEngine g,int s)=>g.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.Hand(s)).OrderBy(c=>c.ZoneIndex).Select(c=>c.CardId).ToArray();
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Submit(GameEngine g,GameCommand c){var result=g.Submit(c);Require(result.Error is null,result.Error?.Message??"Rejected");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Submit(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static void Driver(GameEngine g,string id,int[] targets,int[]? cards=null){ReachPlay(g);Submit(g,new UseProgramSkillCommand(0,"fixture:kuai-driver",id,cards??[],targets,g.Revision,P(g)!.PromptId));}
    private static void Yang(GameEngine g,int card,int target){ReachPlay(g);Submit(g,new UseProgramSkillCommand(0,"classic:shenshi","yang",[card],[target],g.Revision,P(g)!.PromptId));}
    private static void End(GameEngine g){ReachPlay(g);Submit(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));}
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)
    {for(var i=0;i<100;i++){if(P(g)is{}p&&predicate(p))return;Tick(g);}throw new InvalidOperationException("Missing fixed boundary: "+JsonSerializer.Serialize(P(g)));}
    private static void ReachUntil(GameEngine g,Func<bool> predicate){for(var i=0;i<100;i++){if(predicate())return;Tick(g);}throw new InvalidOperationException("Missing fixed state.");}
    private static void Tick(GameEngine g)
    {if(P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard})End(g);else if(P(g)is{PlayerSeat:0}p)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("response")=="let-die"||c.Cards.Count==0);else {if(g.CreateSnapshot(0).Status==EngineStatus.Completed)throw new InvalidOperationException("Unexpected completion: "+JsonSerializer.Serialize(g.Events.Select(e=>e.Payload).Where(e=>e is PlayerDiedEvent or ProgramConversionPolarityCommittedEvent))+" frames "+JsonSerializer.Serialize(g.ResolutionStack));Submit(g,new AdvanceOneStepCommand(g.Revision));}}
    private static void Replay(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(restored),"All viewer and AI snapshots and physical zones replay identically.");Require(g.CreateCardZoneDiagnostics().Count==100&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==100,"Entities conserved.");}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(Enumerable.Range(0,4).SelectMany(v=>new[]{g.CreateSnapshot(v),g.CreateSnapshot(v,true)}))+JsonSerializer.Serialize(g.CreateCardZoneDiagnostics());
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static (GameEngine,ContentRegistry) Create(bool lethal=false,bool giftDeath=false,bool slashDeck=false,bool endingDamage=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(lethal,giftDeath,slashDeck,endingDamage));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-kuai-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},r);
        Submit(g,new StartGameCommand());Submit(g,new SelectGeneralCommand(0,"fixture:kuai-owner",g.Revision,P(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(bool lethal,bool giftDeath,bool slashDeck,bool endingDamage):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture:kuai",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var c=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:kuai-driver","revision":1,"activations":[             {"id":"grow-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]}, {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"request"}]}, {"id":"grow","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":5}]},             {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},             {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:shenshi"],"sourceBind":"standard:none"}]},             {"id":"discard","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},             {"id":"minimum","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingLeastHandCount"},{"op":"draw","target":"selectedTarget","amount":1}]},             {"id":"take","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","awaitMovementTriggers":true}]},             {"id":"give-back","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget"}]}]},             {"id":"fixture:gift-observer","revision":1,"triggers":[{"id":"gift","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.converting-gift.give"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}]}""",             """{"schemaVersion":3,"skills":{"fixture:kuai-driver":{"name":"固定驱动","description":"测试"},"fixture:gift-observer":{"name":"赠牌暂停","description":"测试","optionLabels":{"continue":"继续"}}}}""");
            if(giftDeath)
            {
                var extra=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:gift-kill","revision":1,"triggers":[{"id":"death","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.converting-gift.give"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:gift-kill":{"name":"移动死亡","description":"测试"}}}""");
                b.AddSkill(new("fixture:gift-kill","移动死亡","测试"){Program=extra.Programs["fixture:gift-kill"]});
            }
            var retention=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:retention-observer","revision":1,"triggers":[{"id":"reward","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.gift-retention.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:retention-observer":{"name":"延期摸牌暂停","description":"测试","optionLabels":{"continue":"继续"}}}}""");
            b.AddSkill(new("fixture:retention-observer","延期摸牌暂停","测试"){Program=retention.Programs["fixture:retention-observer"]});
            foreach(var id in c.Programs.Keys)b.AddSkill(new(id,id,"测试"){Program=c.Programs[id]});
            var ending=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ending-hurt","revision":1,"triggers":[{"id":"hurt","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:ending-hurt":{"name":"结束阶段伤害","description":"测试"}}}""");
            b.AddSkill(new("fixture:ending-hurt","结束阶段伤害","测试"){Program=ending.Programs["fixture:ending-hurt"]});
            b.AddSkill(new("fixture:kuai-idle","静态","测试"));
            b.AddGeneral(new("fixture:kuai-owner","拥有者","supporter","classic:shenshi","wei",20,endingDamage?["classic:jianxiang","fixture:kuai-driver","fixture:retention-observer","fixture:ending-hurt"]:["classic:jianxiang","fixture:kuai-driver","fixture:retention-observer"]));
            b.AddGeneral(new("fixture:kuai-target-1","受赠者","supporter",giftDeath?"fixture:gift-kill":"fixture:gift-observer","wei",lethal?1:20));
            for(var i=2;i<4;i++)b.AddGeneral(new($"fixture:kuai-target-{i}","目标","supporter",giftDeath?"fixture:gift-kill":"fixture:gift-observer","wei",lethal?1:20));
            b.AddDeck(new("fixture:kuai-deck","固定",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard(slashDeck?"standard:slash":"standard:crossbow",i%2==0?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-kuai-check","测试",4,4,slashDeck?new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}}:new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}},"fixture:kuai-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:kuai-owner","fixture:kuai-target-1","fixture:kuai-target-2","fixture:kuai-target-3"]));
        }
    }
}
