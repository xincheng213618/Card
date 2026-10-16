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
    private static int[] Hand(GameEngine g,int s)=>g.CreateCardZoneDiagnostics().Where(c=>c.Location==CardLocation.Hand(s)).OrderBy(c=>c.ZoneIndex).Select(c=>c.CardId).ToArray();
    private static PendingDecision? P(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void Submit(GameEngine g,GameCommand c){var result=g.Submit(c);Require(result.Error is null,result.Error?.Message??"Rejected");}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Submit(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static void Driver(GameEngine g,string id,int[] targets,int[]? cards=null){ReachPlay(g);Submit(g,new UseProgramSkillCommand(0,"fixture:kuai-driver",id,cards??[],targets,g.Revision,P(g)!.PromptId));}
    private static void End(GameEngine g){ReachPlay(g);Submit(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));}
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate)
    {for(var i=0;i<100;i++){if(P(g)is{}p&&predicate(p))return;Tick(g);}throw new InvalidOperationException("Missing fixed boundary: "+JsonSerializer.Serialize(P(g)));}
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
