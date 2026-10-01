using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class FengLinZhugeZhanChecks
{
    public static void PrivateQuotaAndTopReplay()
    {
        var (g,r)=Create(); ReachPlay(g);
        var hand=g.CreateSnapshot(0).Players[0].Hand.Select(c=>c.Id).ToArray();
        Activate(g,"give",hand,[1]); ReachPlay(g); End(g); ActivateEnding(g);
        var frame=g.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        Require(frame.QuotaTop is {Quota:2,Stage:"gain"},"Giving real cards is not discarding; minimum hand qualifies.");
        var viewed=frame.QuotaTop!.ViewedIds.ToArray();
        Require(g.CreateSnapshot(0).PrivateRevealedCards?.Count==3 && Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PrivateRevealedCards is null),"Only the peek viewer sees actual top faces."); Replay(g,r);
        var before=State(g); var p=Prompt(g)!;var bad=g.Submit(new AnswerPromptCommand(1,p.PromptId,p.Choices[0].Id,g.Revision));
        Require(!bad.Accepted&&before==State(g),"Wrong chooser is rejected atomically.");
        Answer(g,c=>c.Cards.Contains(viewed[1]));Replay(g,r);Answer(g,c=>c.Cards.Contains(viewed[0]));
        Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="quota-top-order"));
        Require(g.CreateSnapshot(0).Players[0].Hand.Count==2,"Exactly the frozen quota is obtained once."); Replay(g,r);
        Answer(g,c=>c.Cards.Contains(viewed[2]));
        Require(g.CreateCardZoneDiagnostics().Single(c=>c.CardId==viewed[2]).Location==CardLocation.DrawPile,"Only remaining entity returns to top."); Replay(g,r);
        var (native,nativeRegistry)=Create(native:true);ReachPlay(native);End(native);ActivateEnding(native);
        Require(native.CreateSnapshot(0).Players[0].GeneralId=="classic:zhuge-zhan" && native.ResolutionStack.OfType<ProgramSkillFrame>().Last().QuotaTop is {Quota:1,Stage:"gain"},"Official native general reaches a real human private peek after normal discard.");Replay(native,nativeRegistry);
    }
    public static void ZeroQuotaIssuedDeath()
    {
        var (g,r)=Create(dying:true);ReachPlay(g);Activate(g,"weaken",[],[]);ReachPlay(g);Activate(g,"discard",[g.CreateSnapshot(0).Players[0].Hand[0].Id],[]);ReachPlay(g);End(g);ActivateEnding(g);
        Require(g.ResolutionStack.OfType<ProgramSkillFrame>().Last().QuotaTop?.Quota==0,"Real own discard and nonminimum hand give zero without damage.");
        while(Prompt(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="quota-top-order")){Replay(g,r);Answer(g,c=>c.Cards.Count==1);}
        Require(Prompt(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="quota-top-loss"),"Choose the other living participant before any HP loss.");
        var hp=g.CreateSnapshot(0).Players[1].Hp; Answer(g,c=>c.Targets.Contains(1));
        Require(g.ResolutionStack.OfType<DyingFrame>().Any(d=>d.VictimSeat==0)&&g.CreateSnapshot(0).Players[1].Hp==hp,"Owner's real dying child precedes the other committed loss.");Replay(g,r);
        for(var n=0;n<180&&g.CreateSnapshot(0).Players[1].Hp==hp;n++)Drive(g);
        Require(!g.CreateSnapshot(0).Players[0].IsAlive&&g.CreateSnapshot(0).Players[1].Hp==hp-1,"Issued exact operation continues after owner death while game remains live.");Replay(g,r);
    }
    public static void FirstSharedTargetAndAi()
    {
        var (g,r)=Create();ReachPlay(g);Activate(g,"draw",[],[]);ReachPlay(g);var equipment=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);Play(g,equipment);ReachPlay(g);var hp=g.CreateSnapshot(0).Players[1].Hp;
        Play(g,g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(1)&&a.ConversionSource is not null));ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[1].Hp==hp&&g.Events.Any(e=>e.Payload is CardEffectSkippedEvent {TargetSeat:1}),"Actual first Slash target is nullified.");
        Play(g,g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Duel&&a.TargetSeats.Contains(1)&&a.ConversionSource is not null));ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[1].Hp==hp-1,"Second actual use in the same turn cannot repeat first-target protection.");Replay(g,r);
        End(g); for(var n=0;n<80;n++)Drive(g);
        Require(g.Events.Any(e=>e.Payload is ProgramBindingResolvedEvent {SkillId:"classic:zuilun",Completed:true}),"Normal AdvanceOneStep AI completes the native private quota operation.");Replay(g,r);
    }
    public static void FailedFirstComparisonAndNestedGain()
    {
        var (g,r)=Create();ReachPlay(g);var cards=g.CreateSnapshot(0).Players[0].Hand.Take(2).Select(c=>c.Id).ToArray();Activate(g,"give",cards,[1]);ReachPlay(g);
        Play(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip));ReachPlay(g);var hp=g.CreateSnapshot(0).Players[1].Hp;
        Play(g,g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Slash&&a.TargetSeats.Contains(1)&&a.ConversionSource is not null));ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[1].Hp==hp-1,"First encounter consumes the ledger even when hand comparison fails.");Activate(g,"draw",[],[]);ReachPlay(g);
        Play(g,g.GetHumanLegalActions().First(a=>a.PlayedCardKind==CardKind.Duel&&a.TargetSeats.Contains(1)&&a.ConversionSource is not null));ReachPlay(g);
        Require(g.CreateSnapshot(0).Players[1].Hp==hp-2,"A later Duel with successful hand comparison is not a second first encounter.");Replay(g,r);
        var (n,nr)=Create(nested:true);ReachPlay(n);Activate(n,"give",n.CreateSnapshot(0).Players[0].Hand.Select(c=>c.Id).ToArray(),[1]);ReachPlay(n);End(n);ActivateEnding(n);
        var ids=n.ResolutionStack.OfType<ProgramSkillFrame>().Last().QuotaTop!.ViewedIds.ToArray();Answer(n,c=>c.Cards.Contains(ids[0]));Answer(n,c=>c.Cards.Contains(ids[1]));
        Reach(n,p=>p.SkillPrompt?.SkillId=="fixture:quota-observer");Require(n.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.QuotaTop is {Stage:"gain-movement"}),"Real gain suspends its paid cursor in a nested observer.");Replay(n,nr);
        Answer(n,c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option");
        Require(n.CreateSnapshot(0).Players[0].Hand.Count==3 && n.CardMovements.Count(m=>m.Reason.Value=="program.quota-top.obtain")==2,"Nested draw may obtain the remaining viewed entity; no quota entity is obtained twice.");Replay(n,nr);
    }
    public static void StrictComposition()
    {
        var a=typeof(StandardClassicGeneralPackage).Assembly;
        string Read(string suffix){using var stream=a.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-zhuge-zhan."+suffix+".json")!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
        var rules=Read("rules");foreach(var bad in new[]{rules.Replace("turnEnding","playEnding"),rules.Replace("cardUseTargetsFinalized","cardUseCompleted"),rules.Replace("\"ownerRelation\": \"target\"", "\"ownerRelation\": \"actor\""),rules.Replace("\"amount\": 3","\"amount\": 2")})
        {var rejected=false;try{SkillProgramCatalog.Load(bad,Read("presentation"));}catch(InvalidOperationException){rejected=true;}Require(rejected,"New descriptors reject wrong windows, relations and quota bounds.");}
    }
    private static PendingDecision? Prompt(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
    private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.Kind==DecisionKind.PlayCard&&p.PlayerSeat==0);
    private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var n=0;n<180;n++){if(Prompt(g)is{}p&&predicate(p))return;Drive(g);}throw new InvalidOperationException("Missing boundary: "+Prompt(g));}
    private static void Play(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,Prompt(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
    private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));
    private static void Activate(GameEngine g,string id,int[] cards,int[] targets)=>Accept(g,new UseProgramSkillCommand(0,"fixture:zhuge-driver",id,cards,targets,g.Revision,Prompt(g)!.PromptId));
    private static void ActivateEnding(GameEngine g){Reach(g,p=>p.SkillPrompt?.SkillId=="classic:zuilun"&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
    private static void Drive(GameEngine g){if(Prompt(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard})End(g);else if(Prompt(g)is{PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("response")=="let-die"||c.Cards.Count==0||c.Cards.Count==1);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
    private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
    private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
    private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack,Commands=CommandJson.Serialize(g.AcceptedCommands)});
    private static void Replay(GameEngine g,ContentRegistry r){Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Every viewer and exact paused cursor replays.");Require(g.CreateCardZoneDiagnostics().Count==100&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==100,"Entities conserved.");}
    private static void Require(bool b,string s){if(!b)throw new InvalidOperationException(s);}
    private static (GameEngine,ContentRegistry) Create(bool dying=false,bool nested=false,bool native=false)
    {
        var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(dying,nested,native));
        var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=dying?Role.Rebel:Role.Lord,ModeId="identity:classic-zhuge-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);
        Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,native?"classic:zhuge-zhan":dying?Prompt(g)!.Choices[0].ContentIds[0]:"fixture:zhuge-owner",g.Revision,Prompt(g)!.PromptId));return(g,r);
    }
    private sealed class Fixture(bool dying,bool nested,bool native):IGameContentPackage
    {
        public PackageManifest Manifest{get;}=new("fixture-zhuge",new Version(1,0,0),[]);
        public void Register(IContentRegistryBuilder b)
        {
            var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:zhuge-driver","revision":1,"activations":[{"id":"give","minCards":0,"maxCards":16,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"giveSelected","target":"selectedTarget"}]},{"id":"discard","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"owner","amount":8}]}],"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},{"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true}]}]}""";
            if(dying)
            {
                var node=System.Text.Json.Nodes.JsonNode.Parse(rules)!;var skill=node["skills"]![0]!;skill.AsObject().Remove("viewAs");
                var acts=skill["activations"]!.AsArray();acts.RemoveAt(2);acts.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"weaken","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":19}]}"""));
                skill["modifiers"]=System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"test-hand-limit","query":"handLimit","operation":"add","value":20,"priority":0}]""");rules=node.ToJsonString();
            }
            var catalog=SkillProgramCatalog.Load(rules,"""{"schemaVersion":3,"skills":{"fixture:zhuge-driver":{"name":"真实测试动作","description":"测试"}}}""");foreach(var s in catalog.Programs)b.AddSkill(new(s.Key,s.Key,"测试"){Program=s.Value});
            if(nested)
            {
                var observer=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:quota-observer","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.quota-top.obtain"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observed","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]}]}""","""{"schemaVersion":3,"skills":{"fixture:quota-observer":{"name":"真实获牌暂停","description":"测试","optionLabels":{"continue":"继续"}}}}""");foreach(var pair in observer.Programs)b.AddSkill(new(pair.Key,pair.Key,"测试"){Program=pair.Value});
            }
            b.AddGeneral(new("fixture:zhuge-owner","私看拥有者","supporter","classic:zuilun","shu",20,nested?["classic:fuyin","fixture:zhuge-driver","fixture:quota-observer"]:["classic:fuyin","fixture:zhuge-driver"]));
            for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:zhuge-target-{i}","目标","supporter","classic:fuyin","shu",20,dying?["classic:zuilun","fixture:zhuge-driver"]:["classic:zuilun"]));
            b.AddDeck(new("fixture:zhuge-deck","固定实体",4,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(i=>new ContentDeckPhysicalCard("standard:crossbow",Suit.Spade,i%13+1)).ToArray()});
            b.AddMode(new("identity:classic-zhuge-check","测试",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:zhuge-deck",GeneralCandidateCount:4,GeneralPoolIds:[native?"classic:zhuge-zhan":"fixture:zhuge-owner","fixture:zhuge-target-1","fixture:zhuge-target-2","fixture:zhuge-target-3"]));
        }
    }
}
