using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class HuangQuanChecks
{
 private const string Owner="fixture:huang-quan",Driver="fixture:huang-quan-driver",Quiet="fixture:huang-quan-quiet",Mode="identity:classic-huang-quan-fixture";
 public static void ActualRecordedDamageAndForeignRecoveryCold()
 {
  var(g,r)=Start("slash");Play(g);var relation=E<DianhuArmedEvent>(g).Single().Relation;Need(relation.TargetSeat==1,"Actual game-start choice records the selected other player permanently.");var hand=V(g,0).Hand.Count;Use(g,"damage",[1]);Play(g);g=Cold(g,r);Need(V(g,0).Hand.Count==hand+1&&E<HuangQuanDrawPaidEvent>(g).Count(e=>e.Kind=="dianhu")==1,"One actual two-point owner damage occurrence draws one, not two.");
  hand=V(g,0).Hand.Count;Use(g,"recover",[1]);Play(g);g=Cold(g,r);Need(V(g,0).Hand.Count==hand+1&&E<HuangQuanDrawPaidEvent>(g).Count(e=>e.Kind=="dianhu"&&e.Occurrence!.Recovery)==1,"Foreign target's actual recovery qualifies the recorded owner through the formal HP collector.");
  hand=V(g,0).Hand.Count;Use(g,"recover",[1]);Play(g);Use(g,"damage",[2]);Play(g);Need(V(g,0).Hand.Count==hand,"Recovery clamped to zero and unrelated target damage do not manufacture occurrences.");_=Cold(g,r);
 }
 public static void ActualJianjiDrawTwoEquipmentAndDelayedNativeUsesCold()
 {
  ActualOffTurnAlcoholQuotaAndExpiryCold();
  foreach(var scenario in new[]{"draw-two","equipment","lightning"})
  {
   var(g,r)=Start(scenario);Play(g);Jianji(g);Reach(g,()=>IsJianjiOffer(g));var parent=Root(g)!;var card=parent.HuangQuan!.CardIds.Single();var recipientBefore=V(g,1).Hand.Count;g=Cold(g,r);Accept(g,new AdvanceOneStepCommand(g.Revision));Reach(g,()=>E<HuangQuanCompletedEvent>(g).Any(e=>e.FrameId==parent.Id));Play(g);g=Cold(g,r);var issued=E<JianjiUseIssuedEvent>(g).Single().Receipt;
   Need(issued.ActorSeat==1&&issued.CardId==card&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==issued.UseId)==1&&E<ProgramSkillResolvedEvent>(g).Count(e=>e.FrameId==parent.Id&&e.Completed)==1,"Arbitrary supported native card use finishes its exact original paid activation once.");
   if(scenario=="draw-two")Need(V(g,1).Hand.Count==recipientBefore+1,"DrawTwo executes its genuine two-card draw, not an equipment shortcut.");
   if(scenario=="equipment")Need(V(g,1).Equipment.Any(c=>c.Id==card),"Real recipient equips the exact drawn physical entity.");
   if(scenario=="lightning")Need(V(g,1).Judgment.Any(c=>c.Id==card),"Real recipient uses the exact delayed trick into its native judgment zone.");
  }
 }
 private static void ActualOffTurnAlcoholQuotaAndExpiryCold()
 {
  var(g,r)=Start("alcohol");Play(g);var wine=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Alcohol);Accept(g,new PlayCardCommand(0,wine.CardId!.Value,[],g.Revision,P(g)!.PromptId));Play(g);var old=E<JianjiOrdinaryAlcoholPaidEvent>(g).Single(e=>e.ActorSeat==0);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,()=>IsJianjiOffer(g)&&P(g)!.PlayerSeat==0);var parent=Root(g)!;Need(!V(g,0).HasAlcoholEffect&&parent.HuangQuan!.ActualTurn!=old.ActualTurn,"Previous own-turn alcohol has really expired while its old use flag predates the current giver turn.");g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option")!="decline");Reach(g,()=>E<HuangQuanCompletedEvent>(g).Any(e=>e.FrameId==parent.Id));g=Cold(g,r);var paid=E<JianjiOrdinaryAlcoholPaidEvent>(g).Last(e=>e.ActorSeat==0);Need(paid.ActualTurn!=old.ActualTurn&&paid.JianjiProgramFrameId==parent.Id&&paid.CountsQuota&&V(g,0).HasAlcoholEffect,"Foreign actual-turn alcohol uses a fresh true-turn quota and real native effect, without reusing a stale previous-own-turn flag.");Reach(g,()=>E<TurnEndedEvent>(g).Any(e=>e.TurnNumber==paid.ActualTurn));g=Cold(g,r);Need(!V(g,0).HasAlcoholEffect&&E<AlcoholExpiredEvent>(g).Count(e=>e.PlayerSeat==0)==2,"Jianji's foreign alcohol expires at this true giver turn end, rather than surviving until recipient's future turn.");
 }
 private static bool IsJianjiOffer(GameEngine g)=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="jianji-use")==true;
 private static ProgramSkillFrame? Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f=>f.HuangQuan?.Kind=="jianji");
 private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected native Huang Quan command.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static void Use(GameEngine g,string id,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets,g.Revision,P(g)!.PromptId));
 private static void Jianji(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"ol:jianji","draw-and-offer-exact-card",[],[1],g.Revision,P(g)!.PromptId));
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
 private static void Reach(GameEngine g,Func<bool> done)
 {for(var n=0;n<160;n++){if(done())return;var p=P(g);if(p is{Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="take-damage"))Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="take-damage");else if(p is{Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"let-die");else if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Huang Quan boundary missing: "+JsonSerializer.Serialize(new{Prompt=P(g)?.Kind,Skill=P(g)?.SkillPrompt?.SkillId,Frames=g.ResolutionStack.Select(f=>new{f.Id,f.Kind}),Paid=E<HuangQuanDrawPaidEvent>(g),Recent=g.Events.TakeLast(7).Select(e=>e.Payload.GetType().Name)}));}
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements});
 private static GameEngine Cold(GameEngine g,ContentRegistry registry){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(cold),"Cold native command prefixes retain all private views, original occurrence, exact drawn entity and once issued use return.");return cold;}
 private static string Embedded(string suffix){using var stream=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream($"CardGame.Content.Standard.SkillPrograms.ol-huang-quan.{suffix}")!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
 private static(GameEngine,ContentRegistry)Start(string scenario)
 {var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario=="death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=8},registry);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{Kind:DecisionKind.SelectGeneral,PlayerSeat:0});Accept(g,new SelectGeneralCommand(0,scenario is "decline" or "alcohol"?"fixture:huang-quan-peer-1":Owner,g.Revision,P(g)!.PromptId));if(scenario is "decline" or "alcohol")Reach(g,()=>E<DianhuArmedEvent>(g).Any());else{Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:dianhu");Answer(g,c=>c.Targets.SequenceEqual([1]));}return(g,registry);}
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("huang-quan-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>id==Quiet?10000d:(scenario is "decline" or "alcohol")&&id=="ol:jianji"?(role==Role.Loyalist?100000d:-100000d):-10000d)});}
   Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
   var rules=JsonNode.Parse($$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Driver}}","revision":1,"activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},{"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":2}]}]},{"id":"{{Quiet}}","revision":1,"modifiers":[{"id":"native-limit","query":"handLimit","operation":"add","value":1,"priority":0}]}]}
   """)!;var labels=new Dictionary<string,object>{[Driver]=new{name="actual native driver",description="native"},[Quiet]=new{name="quiet",description="native"}};var own=new List<string>();var peer=new List<string>();
   if(scenario=="slash")
   {rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:huang-quan-quota-observer","revision":1,"triggers":[{"id":"actual-foreign-committed-use","window":"cardUseTargetsFinalized","ownerRelation":"observer","cardKinds":["slash","fireSlash","thunderSlash"],"condition":{"kind":"not","children":[{"kind":"cardActionActorIsCurrentTurn"}]},"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}"""));labels["fixture:huang-quan-quota-observer"]=new{name="actual foreign committed observer",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};own.Add("fixture:huang-quan-quota-observer");}
   if(scenario=="death")rules["skills"]!.AsArray().Single(n=>n!["id"]!.GetValue<string>()==Quiet)!["triggers"]=JsonNode.Parse("""[{"id":"native-prior-peer-skip","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]""");
   if(scenario is"loss"or"death")
   {
    rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:huang-quan-relay","revision":1,"triggers":[{"id":"native-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["program.jianji.draw"],"movementOccurrence":"perBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"anyLivingHighestHp"},{"op":"damage","target":"selectedTarget","amount":1}]}]}"""));labels["fixture:huang-quan-relay"]=new{name="actual paid draw relay",description="native"};peer.Add("fixture:huang-quan-relay");
    rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:huang-quan-watch","revision":1,"triggers":[{"id":"native-damage-child","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}"""));labels["fixture:huang-quan-watch"]=new{name="actual paid source loss",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};own.Add("fixture:huang-quan-watch");
    if(scenario=="loss"){rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:huang-quan-loss","revision":1,"triggers":[{"id":"native-rescue-replace","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:jianji"],"sourceBind":"standard:none"}]}]}"""));labels["fixture:huang-quan-loss"]=new{name="actual source replacement",description="native"};own.Add("fixture:huang-quan-loss");}
   }
   if(scenario=="depart")
   {rules["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:huang-quan-depart","revision":1,"triggers":[{"id":"native-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["program.jianji.draw"],"movementOccurrence":"perBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"draw","target":"owner","amount":1}]}]}"""));labels["fixture:huang-quan-depart"]=new{name="actual draw entity departure",description="native",optionLabels=new Dictionary<string,string>{["continue"]="continue"}};peer.Add("fixture:huang-quan-depart");}
   Add(SkillProgramCatalog.Load(rules.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
   b.AddGeneral(new(Owner,"实际黄权","supporter",scenario is "decline" or "alcohol"?"ol:jianji":Driver,"shu",scenario is"loss"or"death"?6:4,scenario is "decline" or "alcohol"?["ol:dianhu",..own]:["ol:dianhu","ol:jianji",..own],GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:huang-quan-peer-{i}").ToArray();foreach(var p in peers)b.AddGeneral(new(p,"实际其他角色","supporter",Quiet,"wei",4,peer));
   b.AddCard(new("fixture:huang-quan-card","实际实体牌","原生牌","native",scenario switch{"alcohol"=>CardKind.Alcohol,"equipment"=>CardKind.Crossbow,"lightning"=>CardKind.Lightning,"draw-two"or"loss"or"death"=>CardKind.DrawTwo,_=>CardKind.Slash}));
   b.AddDeck(new("fixture:huang-quan-deck","small native deck",4,0,[]){PhysicalCards=Enumerable.Range(0,64).Select(_=>new ContentDeckPhysicalCard("fixture:huang-quan-card",Suit.Heart,7)).ToArray()});b.AddMode(new(Mode,"native Huang Quan",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:huang-quan-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
