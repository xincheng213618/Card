using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class BeiMiHuChecks
{
 private const string Owner="fixture:bei-mi-hu-owner",Quiet="fixture:bei-mi-hu-quiet",Driver="fixture:bei-mi-hu-driver",Watch="fixture:bei-mi-hu-watch",Loss="fixture:bei-mi-hu-loss",Mode="identity:classic-bei-mi-hu-fixture";
 public static void ActualMarkedDamageOneDrawAndSevenObtainedAwakening()
 {
  var(g,r)=Start("normal");Play(g);var before=V(g,0).Hand.Count;Use(g,"damage-two",[1]);Play(g);var paid=E<BeiMiHuGujuDrawPaidEvent>(g).Single();
  Need(paid.Actual==1 && V(g,0).Hand.Count==before+1 && E<DamageAppliedEvent>(g).Last().Amount==2,"Actual damage two on a marked peer yields one actual card, not two damage points.");
  Use(g,"damage-one",[3]);Play(g);Need(E<BeiMiHuGujuDrawPaidEvent>(g).Count()==1,"Actual unmarked damage grants no Guju draw.");
  Need(Kui(g,2)==1,"The actual own pre-turn must mark the second recipient before cumulative-draw damage. "+Diagnostic(g));
  for(var i=0;i<5;i++){Use(g,"damage-one",[2]);Play(g);}Need(E<BeiMiHuGujuDrawPaidEvent>(g).Sum(e=>e.Actual)==6,"Six truly acquired cards remain below awakening threshold. "+Diagnostic(g));
  var query=typeof(GameEngine).GetMethod("BeiMiHuActualCards",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;Need((int)query.Invoke(g,[0])! ==6,"The causal ledger counts only actual Guju acquisitions.");
  Use(g,"damage-one",[2]);Play(g);Use(g,"wound",[]);Play(g);Use(g,"lose-zongkui",[]);Play(g);var maximum=V(g,0).MaxHp;var hp=V(g,0).Hp;g=Cold(g,r);
  Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,()=>E<BeiMiHuAwakeningCompletedEvent>(g).Any());Play(g);var awakened=E<BeiMiHuAwakeningCompletedEvent>(g).Single();
  Need(E<BeiMiHuAwakeningMaximumPaidEvent>(g).Single().CumulativeActualCards==7 && V(g,0).MaxHp==maximum+1 && V(g,0).Hp==hp+1 && awakened.MarkedSeats.SequenceEqual([3]) &&
   V(g,0).Skills!.Any(s=>s.ContentId=="ol:canshi") && !V(g,0).Skills!.Any(s=>s.ContentId=="ol:guju") && Enumerable.Range(1,3).All(s=>Kui(g,s)==1),"Seven actual acquisitions awaken once: native maxHP+1/recover1, remaining public mark, exact Guju loss and Canshi grant.");_=Cold(g,r);
 }
 private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
 private static int Kui(GameEngine g,int seat)=>V(g,seat).Markers?.FirstOrDefault(m=>m.Kind==PlayerMarkerKind.Kui)?.Count??0;
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static ProgramSkillFrame? RootOrNull(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f=>f.BeiMiHu is not null);
 private static void Need(bool yes,string reason){if(!yes)throw new InvalidOperationException(reason);}
 private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(c);Need(x.Accepted,(x.Error?.Message??"Rejected native Bei Mi Hu command")+" "+JsonSerializer.Serialize(P(g)));}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> select){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(select).Id,g.Revision));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void Use(GameEngine g,string binding,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,Driver,binding,[],targets,g.Revision,P(g)!.PromptId));
 private static void Reach(GameEngine g,Func<bool> done)
 {
  for(var i=0;i<220;i++)
  {
   if(done())return;var p=P(g);
   if(p is{PlayerSeat:0} && RootOrNull(g)?.BeiMiHu?.Stage is "round-choice" or "before-turn-choice")Answer(g,_=>true);
   else if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("skill-id")=="ol:zongkui" && c.Parameters.GetValueOrDefault("program-action")=="activate"))Answer(g,c=>c.Parameters.GetValueOrDefault("skill-id")=="ol:zongkui" && c.Parameters.GetValueOrDefault("program-action")=="activate");
   else if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
   else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
   else Accept(g,new AdvanceOneStepCommand(g.Revision));
  }
  throw new InvalidOperationException("Bounded Bei Mi Hu native boundary missing: "+Diagnostic(g));
 }
 private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack,Players=Enumerable.Range(0,4).Select(s=>V(g,s)),Bindings=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId=="ol:zongkui").ToArray(),Marks=E<BeiMiHuKuiPaidEvent>(g).ToArray(),Guju=E<BeiMiHuGujuDrawPaidEvent>(g).ToArray(),Recent=g.Events.TakeLast(20).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()});
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static GameEngine Cold(GameEngine g,ContentRegistry registry){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(restored),"Cold native replay preserves exact prelude, public markers, actual obtained cards and paid child cursor.");return restored;}
 private static string Embedded(string suffix,string bundle="ol-bei-mi-hu"){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms."+bundle+"."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
 private static (GameEngine,ContentRegistry)Start(string scenario)
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},registry);
  Accept(g,new StartGameCommand());Reach(g,()=>P(g) is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,registry);
 }
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("bei-mi-hu-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}
   Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));if(scenario=="unknown")Add(SkillProgramCatalog.Load(Embedded("rules.json","ol-xuan-gong-zhu"),Embedded("presentation.json","ol-xuan-gong-zhu")));
   if(scenario=="borrowed")b.AddCard(new("fixture:bei-mi-hu-borrowed-sword","借刀杀人","锦囊牌","原生借刀武器持有者与合法杀目标",CardKind.BorrowedSword,new Dictionary<string,string>{["action"]="force-slash-or-take-weapon",["targets"]="ordered-two"}));
   var defs=new List<string>{$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
    $$"""{"id":"{{Driver}}","revision":1,"activations":[{"id":"damage-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"damage-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},{"id":"lose-zongkui","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:zongkui"],"sourceBind":"standard:none"}]}]}"""};
   if(scenario=="borrowed")
   {
    var node=System.Text.Json.Nodes.JsonNode.Parse(defs[1])!;node["activations"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}"""));defs[1]=node.ToJsonString();
   }
   var skills=new List<string>{"ol:zongkui","ol:guju","ol:baijia"};if(scenario is "cancel" or "append" or "borrowed" or "empty-append-role")skills.Add("ol:canshi");
   if(scenario is "loss" or "death")
   {
    skills.Add(Watch);defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.bei-mi-hu.guju-draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");
    if(scenario=="loss"){skills.Add(Loss);defs.Add($$"""{"id":"{{Loss}}","revision":1,"triggers":[{"id":"actual-source-loss","window":"dyingEntering","subject":"owner","optional":false,"priority":1000,"usageScope":"game","usageLimit":1,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:guju"],"sourceBind":"standard:none"}]}]}""");}
   }
   if(scenario=="empty-append-role")
   {
    const string role="fixture:bei-mi-hu-role";skills.Add(role);
    defs.Add("""{"id":"fixture:bei-mi-hu-role","revision":1,"triggers":[{"id":"replace","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"priority":-1000,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]}""");
   }
   var presentations=defs.Select(d=>(Id:JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!,Choice:d.Contains("chooseOption",StringComparison.Ordinal))).ToDictionary(d=>d.Id,d=>new{name=d.Id,description="真实卑弥呼原生边界夹具",optionLabels=d.Choice?new Dictionary<string,string>{["continue"]="继续"}:new Dictionary<string,string>()});
   Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=presentations})));
   b.AddGeneral(new(Owner,"真实卑弥呼拥有者","supporter",Driver,"qun",12,skills,GeneralGender.Female));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:bei-mi-hu-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"原生同桌","supporter",scenario=="cancel"?"standard:none":Quiet,"wei",12,scenario=="unknown"?["ol:gaoling"]:[]));
   b.AddDeck(new("fixture:bei-mi-hu-deck","真实固定实体牌",4,0,[]){PhysicalCards=Enumerable.Range(0,128).Select(_=>new ContentDeckPhysicalCard(scenario=="append"?"standard:draw_two":scenario=="borrowed"?"fixture:bei-mi-hu-borrowed-sword":"standard:slash",Suit.Club,7)).Concat(scenario=="borrowed"?Enumerable.Range(0,12).Select(_=>new ContentDeckPhysicalCard("standard:crossbow",Suit.Club,7)):[]).ToArray()});
   b.AddMode(new(Mode,"卑弥呼真实夹具",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:bei-mi-hu-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
