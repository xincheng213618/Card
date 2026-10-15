using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class KanZeChecks
{
 private const string Owner="fixture:kan-ze",Driver="fixture:kan-ze-driver",Quiet="fixture:kan-ze-quiet",Mode="identity:kan-ze-fixture";
 public static void ActualWholeHandRecipientPrivateRevealAndBothComplementsCold()
 {
  foreach(var branch in new[]{"shown","unshown"})
  {
   var(g,r)=Start();Reach(g,()=>Optional(g,"ol:xiashu"));var original=g.CreateSnapshot(0).Players[0].Hand.Select(c=>c.Id).ToArray();Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");ChooseTarget(g,1);Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="xiashu")==true);g=Cold(g,r);
   var paid=E<XiashuGiftPaidEvent>(g).Single();Need(paid.Invoice.GiftCount==original.Length&&g.CardMovements.Count(m=>m.Reason.Value=="program.xiashu.gift")==original.Length,"All actual hand entities are given once.");
   Need(g.CreateSnapshot(2).PendingDecision is null,"A third player cannot inspect the recipient's arbitrary hand selection.");
   var shown=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();Answer(g,c=>c.Cards.SequenceEqual(new[]{shown}));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="finish");Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")==branch)==true);
   Need(E<CardsRevealedEvent>(g).Last().Cards.Select(c=>c.Id).SequenceEqual(new[]{shown}),"Only actually selected entities become public.");var expected=g.CreateSnapshot(1).Players[1].Hand.Where(c=>(c.Id==shown)==(branch=="shown")).Select(c=>c.Id).ToArray();Answer(g,c=>c.Parameters.GetValueOrDefault("branch")==branch);Reach(g,()=>E<XiashuCompletedEvent>(g).Any());g=Cold(g,r);
   var done=E<XiashuCompletedEvent>(g).Single();Need(done.ClaimCount==expected.Length&&expected.All(id=>g.CreateSnapshot(0).Players[0].Hand.Any(c=>c.Id==id))&&E<ProgramBindingResolvedEvent>(g).Where(e=>e.FrameId==done.FrameId).ToArray() is [var resolved]&&resolved.Activated&&resolved.Completed,"Chosen shown/complement entities are received once and the paid binding completes true.");
  }
 }
 public static void ActualRepeatedKuanshiAndDeferredDrawSkipCold()
 {
  var(g,r)=Start("kuanshi");Play(g);End(g);Reach(g,()=>Optional(g,"ol:kuanshi"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");ChooseTarget(g,0);Reach(g,()=>E<KuanshiArmedEvent>(g).Any());g=Cold(g,r);
  Reach(g,()=>E<KuanshiDamagePreventedEvent>(g).Count()>=2);g=Cold(g,r);Need(E<KuanshiDamagePreventedEvent>(g).All(e=>e.Amount==2)&&g.CreateSnapshot(0).Players[0].Hp==g.CreateSnapshot(0).Players[0].MaxHp,"Two actual amplified damage attempts are both prevented during one live grant.");
  Reach(g,()=>E<KuanshiDrawSkippedEvent>(g).Any());g=Cold(g,r);Need(E<KuanshiDrawSkippedEvent>(g).Count()==1&&E<KuanshiExpiredEvent>(g).Count()==1,"Next actual owner turn expires protection and consumes one draw debt after repeated prevention.");
  foreach(var scenario in new[]{"kuanshi-source-less","kuanshi-lion"})
  {
   var(t,tr)=Start(scenario);Play(t);
   if(scenario=="kuanshi-lion")
   {
    Accept(t,new UseProgramSkillCommand(0,Driver,"stock",[],[],t.Revision,P(t)!.PromptId));Play(t);var lion=t.CreateSnapshot(0).Players[0].Hand.First(c=>c.Kind==CardKind.SilverLion);var action=t.GetHumanLegalActions().Single(a=>a.CardId==lion.Id&&a.Kind==LegalActionKind.Equip);Accept(t,new PlayCardCommand(0,lion.Id,action.TargetSeats,t.Revision,P(t)!.PromptId,action.PlayedCardKind,action.TargetCardId));Play(t);
   }
   if(scenario=="kuanshi-source-less")
   {
    var lightning=t.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Lightning&&a.TargetSeat==0);Accept(t,new PlayCardCommand(0,lightning.CardId!.Value,[0],t.Revision,P(t)!.PromptId));Play(t);
    var physical=t.CreateSnapshot(0).Players[0].Judgment.Single(c=>c.Kind==CardKind.Lightning).Id;
    Accept(t,new UseProgramSkillCommand(0,Driver,"transfer-lightning",[],[1],t.Revision,P(t)!.PromptId));Reach(t,()=>P(t)?.Choices.Any(c=>c.Cards.Contains(physical))==true);Answer(t,c=>c.Cards.Contains(physical));Play(t);t=Cold(t,tr);
    Need(t.CreateSnapshot(1).Players[1].Judgment.Any(c=>c.Id==physical),"A genuinely used physical Lightning is natively transferred to the protected recipient's judgment zone.");
   }
   End(t);Reach(t,()=>Optional(t,"ol:kuanshi"));Answer(t,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");ChooseTarget(t,scenario=="kuanshi-lion"?0:1);
   if(scenario=="kuanshi-lion")
   {Reach(t,()=>E<DamageAppliedEvent>(t).Any(e=>e.TargetSeat==0));t=Cold(t,tr);Need(E<SilverLionDamageCappedEvent>(t).Any()&&E<DamageAppliedEvent>(t).Last(e=>e.TargetSeat==0).Amount==1&&!E<KuanshiDamagePreventedEvent>(t).Any(),"Physical Lion caps requested two to actual one; Kuanshi must neither prevent it nor create draw debt.");}
   else
   {Reach(t,()=>E<KuanshiDamagePreventedEvent>(t).Any());t=Cold(t,tr);var facts=E<KuanshiDamagePreventedEvent>(t).ToArray();var diagnosis=JsonSerializer.Serialize(new{Prevented=facts,Judgments=E<JudgmentResolvedEvent>(t).ToArray(),DamageRequests=E<DamageRequestedEvent>(t).ToArray(),Frames=t.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Recent=t.Events.TakeLast(20).Select(e=>new{Type=e.Payload.GetType().Name,Value=JsonSerializer.Serialize(e.Payload,e.Payload.GetType())}).ToArray()});
    Need(E<JudgmentResolvedEvent>(t).Any(e=>e.Reason==JudgmentReasons.Lightning&&e.Succeeded),"The actual Lightning judgment must record success. "+diagnosis);
    Need(facts.Length==1,"The actual Lightning attempt prevents exactly once. "+diagnosis);
    Need(facts[0].SourceSeat is null,"Native Lightning prevention must retain the actual source-less authority, without an invented source/killer. "+diagnosis);
    Need(facts[0].TargetSeat==1,"The actual Lightning prevention belongs to the protected transferred-judgment recipient. "+diagnosis);
    Need(facts[0].Amount==3,"The actual Lightning prevention records its native final three damage. "+diagnosis);}
  }

 }
 private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-kan-ze.{suffix}")!;using var r=new StreamReader(s);return r.ReadToEnd();}
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T> E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static bool Optional(GameEngine g,string id)=>P(g)?.SkillPrompt?.SkillId==id&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
 private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected native KanZe command.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static void ChooseTarget(GameEngine g,int seat){Reach(g,()=>P(g)?.Choices.Any(c=>c.Targets.Contains(seat))==true);Answer(g,c=>c.Targets.Contains(seat));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void Reach(GameEngine g,Func<bool> done)
 {for(var i=0;i<100;i++){if(done())return;var p=P(g);if(p is{PlayerSeat:0}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("KanZe native boundary missing: "+JsonSerializer.Serialize(P(g))+" frames "+JsonSerializer.Serialize(g.ResolutionStack)+" events "+JsonSerializer.Serialize(g.Events.TakeLast(12).Select(e=>e.Payload)));}
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Native commands cold-restore private hand choices, invoices, grants and draw debts.");return restored;}
 private static(GameEngine,ContentRegistry) Start(string scenario="normal")
 {var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Rebel:Role.Lord,ModeId=scenario=="kuanshi-lion"?"identity:classic-kan-ze-fixture":Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);}
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("kan-ze-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(r=>r,_=>id==Quiet?10000d:-10000d)});}
   Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
   var quietDamage=scenario is "kuanshi" or "kuanshi-lion"?"""{"op":"selectTargets","target":"owner","targetKind":"anyLiving","minimumTargets":1,"maximumTargets":1,"targetAiOrder":"stable"},{"op":"selectOneSelectedTarget","target":"owner"},{"op":"damage","target":"selectedTarget","amount":2},""":"";
   Add(SkillProgramCatalog.Load($$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[{"id":"transfer-lightning","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["judgment"],"count":1,"cardKinds":["lightning"],"destination":"selectedTargetCorrespondingZone","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true}]},{"id":"stock","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":16}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]}]},{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"hit","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{{quietDamage}}{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
   """,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="native driver",description="native"},[Quiet]=new{name="quiet actual damage",description="native"}}})));
   var extra=new List<string>();
   if(scenario is "loss" or "death")
   {
    var n=JsonNode.Parse($$"""
    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:kan-ze-watch","revision":1,"triggers":[{"id":"gift-paid-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.xiashu.gift"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}]}
    """)!;
    var labels=new Dictionary<string,object>{["fixture:kan-ze-watch"]=new{name="native gift child",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}}};extra.Add("fixture:kan-ze-watch");
    if(scenario=="loss")
    {n["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:kan-ze-loss","revision":1,"triggers":[{"id":"rescue-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:xiashu"],"sourceBind":"standard:none"}]}]}"""));labels["fixture:kan-ze-loss"]=new{name="native rescue source loss",description="native"};extra.Add("fixture:kan-ze-loss");}
    Add(SkillProgramCatalog.Load(n.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
   }
   b.AddGeneral(new(Owner,"真实阚泽","supporter",Driver,"wu",3,["ol:xiashu","ol:kuanshi",..extra],GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:kan-ze-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"实际其他角色","supporter",Quiet,"wei",4));
   b.AddCard(new("fixture:kan-ze-lion","白银狮子","装备牌","native armor cap",CardKind.SilverLion));
   b.AddDeck(new("fixture:kan-ze-deck","small fixed deck",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(scenario=="kuanshi-source-less"?"standard:lightning":scenario=="kuanshi-lion"&&i%4==0?"fixture:kan-ze-lion":i%2==0?"standard:slash":"standard:dodge",scenario=="kuanshi-source-less"?Suit.Spade:i%2==0?Suit.Heart:Suit.Spade,7)).ToArray()});b.AddMode(new(scenario=="kuanshi-lion"?"identity:classic-kan-ze-fixture":Mode,"native KanZe",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:kan-ze-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
