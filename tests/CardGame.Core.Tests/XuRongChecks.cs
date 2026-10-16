using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class XuRongChecks
{
 private const string Owner="fixture:xu-rong",Driver="fixture:xu-rong-driver",Quiet="fixture:xu-rong-quiet",Mode="identity:classic-xu-rong-fixture";
 public static void ActualForeignPlayRandomPaymentsAndTurnRestrictionsCold()
 {
  var(g,r)=Start("phase");Play(g);foreach(var target in new[]{1,2,3})Transfer(g,target);End(g);
  for(var i=0;i<3;i++)
  {
   var index=i;Reach(g,()=>E<XionghuoPlayDebtPaidEvent>(g).Count()>index);var paid=E<XionghuoPlayDebtPaidEvent>(g).ElementAt(index);Reach(g,()=>E<XuRongCompletedEvent>(g).Any(e=>e.FrameId==paid.FrameId));g=Cold(g,r);
   Need(paid.Branch is >=0 and <=2&&paid.RandomBefore!=paid.RandomAfter&&paid.RemovedCount==1&&Baoli(g,paid.SubjectSeat)==0&&E<XionghuoPlayDebtPaidEvent>(g).Count(e=>e.FrameId==paid.FrameId)==1,"Each real marked foreign Play removes its actual grant and freezes one native random result once.");
   if(paid.Branch==0)
   {
    Need(E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0&&e.TargetSeat==paid.SubjectSeat&&e.Amount==1&&e.Nature==DamageNature.Fire),"Fire branch issues real native fire damage.");
    var ban=typeof(GameEngine).GetMethod("IsXuRongSlashTargetForbidden",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;Need((bool)ban.Invoke(g,[paid.SubjectSeat,0,CardKind.Slash])!&&!(bool)ban.Invoke(g,[paid.SubjectSeat,0,CardKind.Dodge])!,"The actual paid fire branch forbids directed Slash use against its real source only during this genuine turn.");
   }
   if(paid.Branch==1)
   {
    Need(E<ProgramSkillHpLostEvent>(g).Count(e=>e.FrameId==paid.FrameId&&e.TargetSeat==paid.SubjectSeat&&e.Amount==1)==1,"HP branch pays actual native HP loss once.");
    var penalty=typeof(GameEngine).GetMethod("XuRongHandLimitPenalty",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;var players=(System.Collections.IList)typeof(GameEngine).GetField("_players",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!;Need((int)penalty.Invoke(g,[players[paid.SubjectSeat]])! ==1,"The paid real HP branch contributes exactly minus one to native current-turn hand limit.");
   }
   if(paid.Branch==2){var move=E<XuRongCardsPaidEvent>(g).Single(e=>e.FrameId==paid.FrameId);Need(move.CardIds.Count==1&&g.CardMovements.Count(m=>m.Sequence>move.Before&&m.Sequence<=move.After&&m.Reason.Value=="program.xionghuo.random-take"&&m.To==CardLocation.Hand(0))==1,"Empty equipment contributes zero; opaque actual hand contributes one native random acquisition.");}
   Need(E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==paid.FrameId&&e.Activated&&e.Completed)==1,"Every paid marked phase binding finishes successfully once.");
  }
  _=Cold(g,r);
 }
 private static int Baoli(GameEngine g,int seat)=>V(g,seat).Markers?.FirstOrDefault(m=>m.Kind==PlayerMarkerKind.Baoli)?.Count??0;
 private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected actual Xu Rong command.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is {Kind:DecisionKind.PlayCard,PlayerSeat:0});
 private static void Transfer(GameEngine g,int target){Accept(g,new UseProgramSkillCommand(0,"ol:xionghuo","transfer-one-marker",[],[target],g.Revision,P(g)!.PromptId));Play(g);}
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void Reach(GameEngine g,Func<bool> done)
 {for(var n=0;n<200;n++){if(done())return;var p=P(g);if(p is {PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="take-damage"))Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="take-damage");else if(p is {PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")is "pass" or "let-die");else if(p is {PlayerSeat:0}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Xu Rong actual boundary missing: "+JsonSerializer.Serialize(new{Prompt=P(g)?.Kind,Skill=P(g)?.SkillPrompt?.SkillId,Paid=E<XionghuoPlayDebtPaidEvent>(g),Frames=g.ResolutionStack.Select(f=>new{f.Id,f.Kind}),HpLoss=E<ProgramSkillHpLostEvent>(g),Bindings=E<ProgramBindingStartedEvent>(g).Where(e=>e.SkillId.StartsWith("fixture:xu-rong")),Resolved=E<ProgramBindingResolvedEvent>(g).Where(e=>e.SkillId.StartsWith("fixture:xu-rong")),Damage=E<DamageAppliedEvent>(g).TakeLast(4),Players=Enumerable.Range(0,4).Select(seat=>new{Seat=seat,Hp=V(g,seat).Hp,Skills=V(g,seat).Skills?.Select(k=>k.Id)}),Recent=g.Events.TakeLast(7).Select(e=>e.Payload.GetType().Name)}));}
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements});
 private static GameEngine Cold(GameEngine g,ContentRegistry registry){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(cold),"Cold actual command prefixes retain native marker grants, random invoices, causal dying material and all private views.");return cold;}
 private static string Embedded(string suffix){using var stream=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-xu-rong.{suffix}")!;using var r=new StreamReader(stream);return r.ReadToEnd();}
 private static(GameEngine,ContentRegistry)Start(string scenario="normal")
 {var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=8},registry);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is {Kind:DecisionKind.SelectGeneral,PlayerSeat:0});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,registry);}
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("xu-rong-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}
   Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
   var rules=JsonNode.Parse($$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},{"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},{"id":"overkill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":6}]}]},{"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"native-limit","query":"handLimit","operation":"add","value":1,"priority":0}]}]}
   """)!;
   var labels=new Dictionary<string,object>{[Driver]=new{name="actual native driver",description="native"},[Quiet]=new{name="quiet",description="native"}};var ownExtra=new List<string>();var peerExtra=new List<string>();
   if(scenario=="rescue")rules["skills"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()==Driver)!["modifiers"]=JsonNode.Parse("""[{"id":"second-native-slash","query":"slashLimit","operation":"add","value":1,"priority":0}]""");
   if(scenario is "rescue" or "pair")
   {rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:xu-rong-rescue","revision":1,"triggers":[{"id":"native-prior-rescue","window":"dyingEntering","subject":"owner","optional":false,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true}]}]}"""));labels["fixture:xu-rong-rescue"]=new{name="actual earlier rescue",description="native"};peerExtra.Add("fixture:xu-rong-rescue");}
   if(scenario is "loss" or "death")
   {
    rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:xu-rong-paid-watch","revision":1,"triggers":[{"id":"paid-fire-child","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}"""));labels["fixture:xu-rong-paid-watch"]=new{name="actual paid fire child",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};ownExtra.Add("fixture:xu-rong-paid-watch");
    rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:xu-rong-hp-relay","revision":1,"triggers":[{"id":"native-paid-hp-child","window":"afterHpLost","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingHighestHp"},{"op":"damage","target":"selectedTarget","amount":1}]}]}"""));labels["fixture:xu-rong-hp-relay"]=new{name="actual paid HP child",description="native"};peerExtra.Add("fixture:xu-rong-hp-relay");
    if(scenario=="loss"){rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:xu-rong-source-loss","revision":1,"triggers":[{"id":"native-rescue-replace","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:xionghuo"],"sourceBind":"standard:none"}]}]}"""));labels["fixture:xu-rong-source-loss"]=new{name="actual source loss",description="native"};ownExtra.Add("fixture:xu-rong-source-loss");}
   }
   if(scenario=="pair")rules["skills"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()==Driver)!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"equip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useRandomDeckEquipment","target":"owner","resultBind":"actual-weapon"}]}"""));
   if(scenario=="pair")
   {
    rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:xu-rong-claim-equip","revision":1,"triggers":[{"id":"native-claim-equip","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["program.shajue2023.claim"],"movementOccurrence":"perBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"selectOwnedCards","target":"owner","zones":["hand"],"minimumCards":1,"maximumCards":1,"resultBind":"claimed-equipment"},{"op":"revealBoundCards","target":"owner","sourceBind":"claimed-equipment"},{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"useBoundCardByTarget","target":"selectedTarget","sourceBind":"claimed-equipment"}]}]}"""));
    labels["fixture:xu-rong-claim-equip"]=new{name="actual claimed material equipment use",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};ownExtra.Add("fixture:xu-rong-claim-equip");
   }
   Add(SkillProgramCatalog.Load(rules.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
   b.AddGeneral(new(Owner,"实际徐荣","supporter",Driver,"qun",scenario is "loss" or "death"?6:4,["ol:xionghuo","ol:shajue",..ownExtra],GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:xu-rong-peer-{i}").ToArray();foreach(var peer in peers)b.AddGeneral(new(peer,"其他实际角色","supporter",Quiet,"wei",4,peerExtra));
   if(scenario=="pair")b.AddCard(new("fixture:xu-rong-zhangba","丈八蛇矛","装备牌","actual native weapon",CardKind.ZhangbaSerpentSpear));
   b.AddDeck(new("fixture:xu-rong-deck","small real deck",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(i=>new ContentDeckPhysicalCard(scenario=="pair"?"fixture:xu-rong-zhangba":"standard:slash",Suit.Heart,7)).ToArray()});b.AddMode(new(Mode,"native Xu Rong",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:xu-rong-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
