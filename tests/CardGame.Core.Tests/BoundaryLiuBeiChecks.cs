using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLiuBeiChecks
{
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
