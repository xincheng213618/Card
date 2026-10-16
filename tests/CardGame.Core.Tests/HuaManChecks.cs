using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class HuaManChecks
{
 private const string Owner="fixture:huaman-general",Driver="fixture:huaman-driver",Quiet="fixture:huaman-quiet",Incoming="fixture:huaman-incoming",Watch="fixture:huaman-watch",Mode="identity:classic-huaman-fixture";
 public static void ActualMansiAllHandBarbarianAndManyiNativeImmunityCold()
 {
  foreach(var scenario in new[]{"mansi","mansi-wide"})
  {var(g,r)=Start(scenario);Play(g);var hand=V(g,0).Hand.Select(c=>c.Id).ToArray();Need(hand.Length==(scenario=="mansi-wide"?65:4),"The wide scenario really obtains sixty-five physical initial Hand entities, rather than merely accepting a schema bound.");Accept(g,new UseProgramSkillCommand(0,"ol:mansi","all-hand-barbarian",hand,[],g.Revision,P(g)!.PromptId));Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="use-all-hand-as-ordinary-trick")==true);g=Cold(g,r);Answer(g,_=>true);Play(g);g=Cold(g,r);var use=E<CardActionAcceptedEvent>(g).Select(e=>e.Action).Single(a=>a.ActorSeat==0&&a.Type==CardActionType.Use&&a.EffectiveKind==CardKind.BarbarianAssault);var immune=g.CreateSnapshot(0).Players.Single(p=>p.GeneralId=="fixture:huaman-manyi-peer").Seat;Need(use.PhysicalCards.Select(c=>c.CardId).Order().SequenceEqual(hand.Order())&&!use.EffectiveDesignatedTargetSeats.Contains(immune)&&!E<DamageAppliedEvent>(g).Any(e=>e.TargetSeat==immune),"All real original Hand entities pay one native Barbarian use; a real Manyi holder is excluded without damage.");Need(E<MansiActualCardsObtainedEvent>(g).Sum(e=>e.ActualObtained)==2&&E<DamageAppliedEvent>(g).Count()==2&&!g.GetHumanLegalActions().Any(a=>a.SkillId=="ol:mansi"),"Each actual Barbarian damage draws one, and the whole-hand conversion cannot be used twice in this actual Play.");
  }
 }
 private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
 private static PendingDecision?P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool yes,string message){if(!yes)throw new InvalidOperationException(message);}
 private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(c);Need(r.Accepted,r.Error?.Message??"Hua Man actual command rejected.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> test){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(test).Id,g.Revision));}
 private static void Reach(GameEngine g,Func<bool> done,bool endPlays=false){for(var i=0;i<220;i++){if(done())return;var p=P(g);if(endPlays&&p is{PlayerSeat:0,Kind:DecisionKind.PlayCard})End(g);else if(p?.PlayerSeat==0&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")is"pass"or"let-die"or"take-damage"))Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"let-die"or"take-damage");else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Hua Man native boundary missing "+JsonSerializer.Serialize(new{Prompt=P(g)?.Kind,Skill=P(g)?.SkillPrompt?.SkillId,Frames=g.ResolutionStack.Select(f=>new{f.Id,f.Kind}),Recent=g.Events.TakeLast(8).Select(e=>e.Payload.GetType().Name)}));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"All actual gain counters, direction history, paid native returns and private views reproduce through command-prefix cold recovery.");return restored;}
 private static string Read(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-hua-man."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
 private static(GameEngine,ContentRegistry)Start(string scenario){var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,HumanSeat=0,HumanRole=scenario.EndsWith("death",StringComparison.Ordinal)?Role.Rebel:Role.Lord,PlayerCount=4,ModeId=Mode,UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);}
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("huaman-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Incoming?100000d:id=="ol:manyi"?50000d:id==Quiet?10000d:-10000d)});}Add(SkillProgramCatalog.Load(Read("rules.json"),Read("presentation.json")));
   var node=JsonNode.Parse("""{"skills":[]} """)!;node["schemaVersion"]=SkillProgramCatalog.RulesSchemaVersion;var skills=node["skills"]!.AsArray();skills.Add(JsonNode.Parse("""{"id":"fixture:huaman-driver","revision":1,"modifiers":[{"id":"actual-slash-limit","query":"slashLimit","operation":"add","value":4,"priority":0}]}"""));skills.Add(JsonNode.Parse("""{"id":"fixture:huaman-quiet","revision":1,"triggers":[{"id":"empty-original-hand","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]},{"id":"skip-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
   if(scenario=="mansi-wide")skills[0]!["modifiers"]!.AsArray().Add(JsonNode.Parse("""{"id":"actual-wide-initial-hand","query":"initialHandSize","operation":"add","value":61,"priority":0}"""));
   if(scenario is "souying-fire" or "souying-snatch" or "souying-dismantlement")skills[1]!["triggers"]!.AsArray().RemoveAt(0);
   if(scenario=="souying-grain")skills[0]!["activations"]=JsonNode.Parse("""[{"id":"gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]""");
   if(scenario is "souying-borrowed" or "souying-group")skills[0]!["activations"]=JsonNode.Parse("""[{"id":"wake","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"}]},{"id":"gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},{"id":"stock","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]}]""");
   if(scenario=="souying-borrowed")skills[1]!["viewAs"]=JsonNode.Parse("""[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":true}]""");
   var labels=new Dictionary<string,object>{{Driver,new{name="真实驱动",description="native"}},{Quiet,new{name="静默",description="native"}}};
   var incoming=scenario=="incoming";var xili=scenario.StartsWith("xili",StringComparison.Ordinal);var watched=scenario.Contains("loss",StringComparison.Ordinal)||scenario.Contains("death",StringComparison.Ordinal);
   var soleGroup=scenario is "souying-recovery" or "souying-draft";
   if(soleGroup)
   {
    skills[0]!["activations"]=JsonNode.Parse("""[{"id":"wake","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"}]}]""");
    skills[0]!["viewAs"]=JsonNode.Parse($$"""[{"id":"group","inputKinds":[],"inputSuits":[],"outputKind":"{{(scenario=="souying-recovery"?"peachGarden":"fiveGrains")}}","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true,"excludeOwnerEffects":true}]""");
    skills.Add(JsonNode.Parse("""{"id":"fixture:huaman-global-immune","revision":1,"cardPolicies":[{"id":"immune-groups","kind":"prohibitTargetBySuit","inputSuit":"heart","cardKinds":["peachGarden","fiveGrains"]}]}"""));labels["fixture:huaman-global-immune"]=new{name="真实群体目标免疫",description="native"};
   }
   if(incoming||xili)
   {
    var effects=incoming?"""[{"op":"selectTarget","target":"owner","targetKind":"anyLivingHighestHp"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]""":"""[{"op":"damage","target":"owner","amount":1},{"op":"selectTarget","target":"owner","targetKind":"anyLivingHighestHp"},{"op":"damage","target":"selectedTarget","amount":1},{"op":"damage","target":"selectedTarget","amount":1}]""";
    var rule=new JsonObject{{"id",Incoming},{"revision",1},{"triggers",new JsonArray(new JsonObject{{"id","native-other-uses"},{"window","afterNormalDraw"},{"subject","owner"},{"optional",false},{"usageScope","game"},{"usageLimit",1},{"priority",1000},{"effects",JsonNode.Parse(effects)}})}};skills.Add(rule);labels[Incoming]=new{name="真实其他角色用牌伤害",description="native"};
   }
   if(watched)
   {
    var watch=JsonNode.Parse("""{"id":"fixture:huaman-watch","revision":1,"triggers":[{"id":"paid-discard-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.huaman.discard"],"movementDiscardOnly":true,"movementOccurrence":"perOwnerBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]},{"id":"recover-and-lose-source","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"priority":1000,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:souying","ol:xili","fixture:huaman-watch"],"sourceBind":"fixture:huaman-driver"}]}]}""")!;if(scenario.EndsWith("death",StringComparison.Ordinal))watch["triggers"]!.AsArray().RemoveAt(1);skills.Add(watch);labels[Watch]=new{name="真实已付子链",description="native",optionLabels=new Dictionary<string,string>{{"continue","继续"}}};
   }
   Add(SkillProgramCatalog.Load(node.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
   var extras=new List<string>{"ol:manyi","ol:mansi","ol:souying","ol:zhanyuan"};if(xili)extras.Add("ol:xili");if(watched)extras.Add(Watch);b.AddGeneral(new(Owner,"真实花鬘","supporter",Driver,"shu",incoming?14:3,extras,GeneralGender.Female));var peers=new List<string>();
   if(incoming||xili){peers.Add("fixture:huaman-native-peer");b.AddGeneral(new(peers.Last(),"真实其他来源","supporter",Incoming,"wei",12,xili?[Quiet,"ol:xili"]:[Quiet],GeneralGender.Male));}
   else{peers.Add("fixture:huaman-manyi-peer");b.AddGeneral(new(peers.Last(),"真实蛮裔对照","supporter","ol:manyi","shu",12,soleGroup?[Quiet,"fixture:huaman-global-immune"]:[Quiet],GeneralGender.Male));}
   for(var i=peers.Count;i<3;i++){var id="fixture:huaman-peer-"+i;peers.Add(id);b.AddGeneral(new(id,"真实其他角色","supporter",Quiet,"wei",xili&&i==1?16:12,scenario=="souying-group"&&i==1?["ol:manyi"]:soleGroup&&i==1?["fixture:huaman-global-immune"]:[],GeneralGender.Male));}
   if(scenario=="souying-grain")b.AddCard(new("fixture:huaman-ox","木牛流马","装备牌","真实原生木牛",CardKind.WoodenOx));
   if(scenario=="souying-borrowed")b.AddCard(new("fixture:huaman-borrowed","借刀杀人","锦囊牌","真实原生借刀",CardKind.BorrowedSword));
   string DeckCard(int index)=>scenario switch
   {
    "souying-grain" when index==48=>"fixture:huaman-ox",
    "souying-borrowed" when index is >=48 and <=50=>"standard:crossbow",
    "souying-borrowed"=>"fixture:huaman-borrowed",
    "awakening" or "souying-group"=>"standard:barbarian_assault",
    "souying-trick"=>"standard:duel",
    "souying-fire"=>"standard:fire_attack",
    "souying-snatch"=>"standard:snatch",
    "souying-dismantlement"=>"standard:dismantlement",
    _=>"standard:slash"
   };
   b.AddDeck(new("fixture:huaman-deck","small",4,0,[]){PhysicalCards=Enumerable.Range(0,80).Select(index=>new ContentDeckPhysicalCard(DeckCard(index),Suit.Heart,7)).ToArray()});b.AddMode(new(Mode,"真实花鬘",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:huaman-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
