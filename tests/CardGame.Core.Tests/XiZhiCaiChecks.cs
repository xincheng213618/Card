using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class XiZhiCaiChecks
{
 private const string Owner="fixture:xi-zhi-cai",Driver="fixture:xi-zhi-driver",Quiet="fixture:xi-zhi-quiet",Mode="identity:classic-xi-zhi-fixture";
 public static void ActualGameStartRelationDamageAndClampedRecoveryCold()
 {
  var(g,r)=Start();Play(g);var hp=V(g).Hp;Use(g,"damage-target",[1]);Reach(g,()=>Optional(g,"ol:chouce"));g=Cold(g,r);Play(g);
  var damage=E<XianfuEchoIssuedEvent>(g).Single();Need(damage.Receipt.Kind==XianfuEchoKind.Damage&&damage.Receipt.ActualAmount==2&&V(g).Hp==hp-2&&E<DamageAppliedEvent>(g).Count(e=>e.TargetSeat==0&&e.SourceLess&&e.Amount==2)==1,"The real permanent other-character relation echoes one actual two damage through native source-less damage.");
  g=Cold(g,r);Use(g,"heal-target",[1]);Play(g);g=Cold(g,r);var heal=E<XianfuEchoIssuedEvent>(g).Last();
  Need(heal.Receipt.Kind==XianfuEchoKind.Recovery&&heal.Receipt.ActualAmount==2&&heal.NativeAmount==2&&V(g).Hp==hp&&E<XianfuEchoCompletedEvent>(g).Count()==2,"Requested five healing clamps to actual two on the recorded target, and only those two natively recover the owner.");
  Need(E<ProgramBindingResolvedEvent>(g).Where(e=>E<XianfuEchoIssuedEvent>(g).Any(x=>x.FrameId==e.FrameId)).All(e=>e.Activated&&e.Completed),"Every issued echo ends in its actual true native binding completion.");
 }
 public static void ActualPerPointRedJudgmentTianduClaimAndRecordedDoubleDrawCold()
 {
  var(g,r)=Start();Play(g);Use(g,"self-two",[]);
  for(var point=0;point<2;point++)
  {Reach(g,()=>Optional(g,"ol:chouce"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,()=>Optional(g,"classic:tiandu"));g=Cold(g,r);AnswerTiandu(g,true);Reach(g,()=>ChouceTarget(g));Answer(g,c=>c.Targets.SequenceEqual(new[]{1}));Reach(g,()=>E<ChouceCompletedEvent>(g).Count()>point);g=Cold(g,r);}
  var points=E<ChouceJudgmentIssuedEvent>(g).ToArray();Need(points.Length==2&&points.Select(e=>e.Receipt.PointIndex).SequenceEqual(new[]{0,1})&&E<ChouceOutcomeIssuedEvent>(g).All(e=>e.Red&&e.TargetSeat==1&&e.Count==2),"Each actual damage point offers one judgment and red grants the recorded Xianfu target exactly two real cards.");
  var claims=E<ProgramJudgmentCardClaimedEvent>(g).Where(e=>e.SkillId=="classic:tiandu").ToArray();Need(claims.Length==2&&claims.All(e=>V(g).Hand.Any(c=>c.Id==e.CardId))&&E<ChouceJudgmentFrozenEvent>(g).All(e=>e.EffectiveSuit==Suit.Heart),"Tiandu claims both actual final judgment entities once; Chouce continues from the previously frozen effective red result.");
 }
 private static bool ChouceTarget(GameEngine g)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("chouce-action")=="target")==true;
 private static int Human(GameEngine g)=>Enumerable.Range(0,4).Single(s=>g.CreateSnapshot(s).Players[s].IsHuman);
 private static PlayerSnapshot V(GameEngine g)=>g.CreateSnapshot(Human(g)).Players[Human(g)];
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T> E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static bool Optional(GameEngine g,string id)
 {
  if(id!="classic:tiandu")return P(g)?.SkillPrompt?.SkillId==id&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
  var window=g.ResolutionStack.LastOrDefault() as ProgramJudgmentTriggerWindowFrame;
  return P(g) is {Kind:DecisionKind.ProgramJudgmentTrigger} p&&window is not null&&window.CandidateIndex>=0&&window.CandidateIndex<window.Candidates.Count&&window.Candidates[window.CandidateIndex] is {SkillId:"classic:tiandu"} candidate&&candidate.OwnerSeat==p.PlayerSeat&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-trigger-activate");
 }
 private static void AnswerTiandu(GameEngine g,bool activate)
 {Need(Optional(g,"classic:tiandu"),"The real current finalized-judgment candidate is Tiandu.");Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-trigger-"+(activate?"activate":"skip"));}
 private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected real XiZhiCai command.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{Kind:DecisionKind.PlayCard} p&&p.PlayerSeat==Human(g));
 private static void Use(GameEngine g,string id,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(Human(g),Driver,id,[],targets,g.Revision,P(g)!.PromptId));
 private static void Reach(GameEngine g,Func<bool> done)
 {for(var i=0;i<160;i++){if(done())return;var p=P(g);if(p is not null&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else if(p is{Kind:DecisionKind.ProgramJudgmentTrigger}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-trigger-skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("action")=="program-judgment-trigger-skip");else if(p is{Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("XiZhiCai native boundary missing: "+JsonSerializer.Serialize(new{Pending=P(g),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.TakeLast(18).Select(e=>e.Payload)}));}
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold actual commands preserve permanent relation, paid echo and point invoices, hidden choices and native movement.");return restored;}
 private static string Embedded(string bundle,string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.{bundle}.{suffix}")!;using var r=new StreamReader(s);return r.ReadToEnd();}
 private static(GameEngine,ContentRegistry) Start(string scenario="red",int human=0)
 {
  var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=human,HumanRole=human==0?Role.Lord:Role.Rebel,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral} p&&p.PlayerSeat==human);Accept(g,new SelectGeneralCommand(human,Owner,g.Revision,P(g)!.PromptId));for(var i=0;i<60&&!(P(g)?.SkillPrompt?.SkillId=="ol:xianfu"&&P(g)!.PlayerSeat==human);i++)
  {if(scenario=="mutual"&&P(g)?.SkillPrompt?.SkillId=="ol:xianfu")Answer(g,c=>c.Targets.SequenceEqual(new[]{human}));else Accept(g,new AdvanceOneStepCommand(g.Revision));}
  Need(P(g)?.SkillPrompt?.SkillId=="ol:xianfu"&&P(g)!.PlayerSeat==human,"Real game start selects the actual human's relation.");
  var target=scenario=="mutual"?Enumerable.Range(0,4).Single(s=>g.CreateSnapshot(s).Players[s].GeneralId=="fixture:xi-zhi-peer-1"):scenario=="target-death"?Enumerable.Range(0,4).First(s=>s!=human&&g.CreateSnapshot(s).Players[s].Role==Role.Rebel):human==0?1:2;
  Answer(g,c=>c.Targets.SequenceEqual(new[]{target}));
  if(scenario=="mutual")for(var i=0;i<60&&E<XianfuRelationArmedEvent>(g).Count()<2;i++)
  {if(P(g)?.SkillPrompt?.SkillId=="ol:xianfu")Answer(g,c=>c.Targets.SequenceEqual(new[]{human}));else Accept(g,new AdvanceOneStepCommand(g.Revision));}
  return(g,r);
 }
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("xi-zhi-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>id==Quiet?10000d:id=="fixture:xi-zhi-mutual"?(role==Role.Lord?-10000d:20000d):-10000d)});}
   Add(SkillProgramCatalog.Load(Embedded("ol-xi-zhi-cai","rules.json"),Embedded("ol-xi-zhi-cai","presentation.json")));
   var tiandu=JsonNode.Parse(Embedded("judgment-cutover","rules.json"))!;var printed=tiandu["skills"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()=="classic:tiandu")!.DeepClone();tiandu["skills"]=new JsonArray(printed);var labels=JsonNode.Parse(Embedded("judgment-cutover","presentation.json"))!;labels["skills"]=new JsonObject{["classic:tiandu"]=labels["skills"]!["classic:tiandu"]!.DeepClone()};Add(SkillProgramCatalog.Load(tiandu.ToJsonString(),labels.ToJsonString()));
   var rules=JsonNode.Parse($$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[{"id":"damage-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"damage-five","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":5}]},{"id":"self-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},{"id":"self-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":2}]},{"id":"damage-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},{"id":"heal-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":5}]},{"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":8}]}]},{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
   """)!;
   var names=new Dictionary<string,object>{[Driver]=new{name="native driver",description="native"},[Quiet]=new{name="quiet",description="native"}};var extra=new List<string>();
   if(scenario is "chouce-loss" or "echo-loss" or "death")
   {
    rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:xi-zhi-watch","revision":1,"triggers":[{"id":"paid-tiandu-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.classic:tiandu.claimJudgmentCard"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}"""));names["fixture:xi-zhi-watch"]=new{name="native Tiandu paid child",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};extra.Add("fixture:xi-zhi-watch");
    if(scenario!="death")
    {rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"fixture:xi-zhi-loss","revision":1,"triggers":[{"id":"real-rescue-source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{(scenario=="echo-loss"?"ol:xianfu":"ol:chouce")}}"],"sourceBind":"standard:none"}]}]}"""));names["fixture:xi-zhi-loss"]=new{name="real rescue source loss",description="native"};extra.Add("fixture:xi-zhi-loss");}
   }
   if(scenario=="mutual"){var q=rules["skills"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()==Quiet)!.DeepClone();q["id"]="fixture:xi-zhi-mutual";rules["skills"]!.AsArray().Add(q);names["fixture:xi-zhi-mutual"]=new{name="actual mutual partner",description="native"};}
   Add(SkillProgramCatalog.Load(rules.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=names})));
   b.AddGeneral(new(Owner,"真实戏志才","supporter",Driver,"wei",scenario is "target-death" or "mutual"?8:3,["classic:tiandu","ol:xianfu","ol:chouce",..extra],GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:xi-zhi-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"实际其他角色","supporter",scenario=="mutual"&&p==peers[0]?"fixture:xi-zhi-mutual":Quiet,"wei",4,scenario=="mutual"&&p==peers[0]?["ol:xianfu"]:[]));
   b.AddCard(new("fixture:xi-zhi-lion","白银狮子","装备牌","native Lion",CardKind.SilverLion));b.AddDeck(new("fixture:xi-zhi-deck","small fixed native deck",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(scenario=="lion"&&i%3==0?"fixture:xi-zhi-lion":"standard:dodge",scenario is "black" or "lion"?Suit.Spade:Suit.Heart,7)).ToArray()});b.AddMode(new(Mode,"native XiZhiCai",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:xi-zhi-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
