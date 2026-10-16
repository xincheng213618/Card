using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class Fame2017XueZongChecks
{
 public static void SlashDuelAndCounterspellEntityExchange()
 {
  foreach(var kind in new[]{CardKind.Slash,CardKind.Duel,CardKind.DrawTwo})foreach(var multi in new[]{false,true})
  {
   var(g,r)=Create(multi);ReachPlay(g);var action=g.GetHumanLegalActions().First(a=>a.PlayedCardKind==kind&&a.ConversionSource is not null&& (a.TargetSeats.Count==0||a.TargetSeats.Contains(1)));var original=action.CardId!.Value;
   Accept(g,new PlayCardCommand(0,original,action.TargetSeats,g.Revision,Prompt(g)!.PromptId,kind){ConversionSource=action.ConversionSource});
   Reach(g,p=>p.SkillPrompt?.SkillId=="classic:funan");var accepted=g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action;var costs=accepted.PhysicalCards.Select(c=>c.CardId).ToArray();Require(costs.Length==(multi?2:1),"The response keeps its true one/multiple physical costs.");Atomic(g,new AnswerPromptCommand(accepted.ActorSeat,Prompt(g)!.PromptId,Prompt(g)!.Choices[0].Id,g.Revision));Replay(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
   Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:xue-observe");Require(g.CreateCardZoneDiagnostics().Single(c=>c.CardId==original).Location==CardLocation.Hand(accepted.ActorSeat)&&costs.All(id=>g.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location.Zone is CardZoneKind.Processing or CardZoneKind.DiscardPile),"Giving the original actual card waits for nested movement before claiming the response.");Require(Enumerable.Range(0,4).All(seat=>g.CreateSnapshot(seat).TurnProhibitedPhysicalCards!.Single().CardIds.SequenceEqual([original])&&g.CreateSnapshot(seat).ProgramResponseExchangeStates!.Single().IsUpgraded==false),"Every viewer sees only the acquired public entity restriction and exact current upgrade state, without hidden locations.");Replay(g,r);Atomic(g,new AnswerPromptCommand(0,Prompt(g)!.PromptId,new("bad"),g.Revision));Drive(g);
   Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:xue-observe");Require(costs.All(id=>g.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location==CardLocation.Hand(0)),"All actual response entities are acquired once after the original gift.");Replay(g,r);Drive(g);
   for(var n=0;n<50&&g.ResolutionStack.OfType<CardUseFrame>().Any();n++)Drive(g);
   Require(g.CardMovements.Count(m=>m.CardId==original&&m.Reason.Value=="skill-program.response-entity-exchange")==1&&costs.All(id=>g.CardMovements.Count(m=>m.CardId==id&&m.Reason.Value=="skill-program.response-entity-exchange")==1),"Continued Slash/Duel/trick settlement never re-pays or discards acquired entities.");Replay(g,r);
  }
 }
 public static void ClaimedMultiTargetTrickKeepsItsNextTargetAndColdReplay()
 {
  var(g,r)=Create(false,extraTarget:true);ReachPlay(g);
  Accept(g,new UseProgramSkillCommand(0,"fixture:xue-driver","add-target",[],[],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);
  var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Duel&&a.ConversionSource is not null&&a.TargetSeats.SequenceEqual([1,3]));var cardId=action.CardId!.Value;
  Atomic(g,new PlayCardCommand(0,cardId,[1,1],g.Revision,Prompt(g)!.PromptId,CardKind.Duel){ConversionSource=action.ConversionSource});
  Accept(g,new PlayCardCommand(0,cardId,action.TargetSeats,g.Revision,Prompt(g)!.PromptId,CardKind.Duel){ConversionSource=action.ConversionSource});
  for(var n=0;n<48&&Prompt(g)?.SkillPrompt?.SkillId!="classic:funan";n++){Require(!g.Events.Select(e=>e.Payload).OfType<CardUseFinishedEvent>().Any(),"The original accepted trick identity must exist before its first real response exchange.");Drive(g);}
  Require(Prompt(g)?.SkillPrompt?.SkillId=="classic:funan","The first actual response must offer its original owner's exchange.");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
  var sawClaim=false;var sawNext=false;
  for(var n=0;n<160&&!g.Events.Select(e=>e.Payload).OfType<CardUseFinishedEvent>().Any();n++)
  {
   if(g.Events.Select(e=>e.Payload).OfType<ProgramResponseEntityClaimedEvent>().Any(e=>e.CardIds.Contains(cardId)))
   {
    if(!sawClaim){Replay(g,r);RejectMismatchedClaimReceipt(g,r,cardId);sawClaim=true;}
    if(g.ResolutionStack.OfType<CardUseFrame>().Any(f=>f.SequentialTrick is not null&&f.TargetIndex==1)&&!sawNext){Replay(g,r);sawNext=true;}
   }
   Drive(g);
  }
  Require(sawClaim&&sawNext&&!g.IsFaulted&&g.Events.Select(e=>e.Payload).OfType<CardUseFinishedEvent>().Count()==1&&g.ResolutionStack.Count==0,"A response-exchanged multi-target trick must retain the exact next target, drain its response windows and finish once.");
  Require(g.CardMovements.Count(m=>m.CardId==cardId&&m.Reason.Value=="skill-program.response-entity-exchange")==1&&!g.CardMovements.Any(m=>m.CardId==cardId&&m.Reason==CardMoveReasons.UseFinished),"The next target cannot discard or exchange the already acquired original entity again.");Replay(g,r);
 }
 public static void EscalatingDiscardZeroAndUpgrade()
 {
  var(g,r)=Create(false);ReachPlay(g);
  for(var turn=0;turn<4&&!g.Events.Select(e=>e.Payload).OfType<ProgramResponseExchangeUpgradedEvent>().Any();turn++)
  {
   Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId=="classic:jiexun");Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Targets.SequenceEqual([1]));
   var start=g.Events.Select(e=>e.Payload).OfType<ProgramEscalatingDiscardStartedEvent>().Last();Require(start.PreviousCount==turn&&start.DrawCount==0,"Discard X is the old count; no public diamond equipment/judgment means no draw.");
   for(var n=0;n<200&&g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.SkillId=="classic:jiexun");n++){Replay(g,r);Drive(g);}
   if(turn==0)Require(!g.Events.Select(e=>e.Payload).OfType<ProgramResponseExchangeUpgradedEvent>().Any(),"X=0 cannot upgrade without a real discard.");
   ReachPlay(g);
  }
  Require(g.Events.Select(e=>e.Payload).OfType<ProgramResponseExchangeUpgradedEvent>().Any()&&g.CreateSnapshot(0).Players[0].SkillRuntimeStates!.All(s=>s.SkillId!="classic:jiexun")&&g.CreateSnapshot(2).ProgramResponseExchangeStates!.Single().IsUpgraded,"A true HE exhaust removes the source skill and upgrades the retained exact response skill.");Replay(g,r);
  Accept(g,new UseProgramSkillCommand(0,"fixture:xue-driver","draw-target",[],[3],g.Revision,Prompt(g)!.PromptId));ReachPlay(g);var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Slash&&a.ConversionSource is not null&&a.TargetSeats.Contains(3));var original=action.CardId!.Value;Accept(g,new PlayCardCommand(0,original,action.TargetSeats,g.Revision,Prompt(g)!.PromptId,CardKind.Slash){ConversionSource=action.ConversionSource});Reach(g,p=>p.SkillPrompt?.SkillId=="classic:funan");var response=g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Last().Action;Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:xue-observe");Require(g.CreateCardZoneDiagnostics().Single(c=>c.CardId==original).Location==CardLocation.Processing&&response.PhysicalCards.All(c=>g.CreateCardZoneDiagnostics().Single(z=>z.CardId==c.CardId).Location==CardLocation.Hand(0))&&g.CreateSnapshot(2).TurnProhibitedPhysicalCards is null,"Upgraded exchange acquires the response without gifting or restricting the original use.");Replay(g,r);Drive(g);ReachPlay(g);Require(g.CardMovements.Count(m=>m.CardId==original&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile)==1,"The retained upgraded original card settles exactly once.");Replay(g,r);
 }
 private static void RejectMismatchedClaimReceipt(GameEngine game,ContentRegistry registry,int cardId)
 {
  var forged=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
  var events=(List<EventEnvelope>)typeof(GameEngine).GetField("_events",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(forged)!;
  var index=events.FindIndex(e=>e.Payload is ProgramResponseEntityClaimedEvent claim&&claim.CardIds.Contains(cardId));Require(index>=0,"The negative fixture must begin with a genuine native material claim.");
  var receipt=(ProgramResponseEntityClaimedEvent)events[index].Payload;events[index]=events[index]with{Payload=receipt with{ClaimedActionId=receipt.ClaimedActionId+10000}};
  var rejected=false;try{forged.Submit(new AdvanceOneStepCommand(forged.Revision));}catch(InvalidOperationException){rejected=true;}
  Require(rejected&&forged.IsFaulted,"A receipt for another action cannot excuse missing physical material or continue a corrupted session.");
 }
 private static (GameEngine,ContentRegistry) Create(bool multi,bool ai=false,bool death=false,bool diamonds=false,bool extraTarget=false)
 {
  var packages=new List<IGameContentPackage>{new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage()};if(!extraTarget)packages.Add(new StandardClassicGeneralPackage());packages.Add(new Fixture(multi,death,diamonds,ai,extraTarget));var r=ContentRegistry.Build(packages.ToArray());var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-xue-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=20},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,ai?"fixture:xue-target-1":"fixture:xue-owner",g.Revision,Prompt(g)!.PromptId));return(g,r);
 }
 private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
 private static void Reach(GameEngine g,Func<PendingDecision,bool> predicate){for(var n=0;n<300;n++){if(Prompt(g) is{}p&&predicate(p))return;Drive(g);}throw new InvalidOperationException("Missing boundary: "+Prompt(g));}
 private static PendingDecision? Prompt(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static void Drive(GameEngine g){if(Prompt(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard}p)Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));else if(Prompt(g) is{PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("response")=="let-die"||c.Parameters.GetValueOrDefault("token")=="finish"||c.Cards.Count==0);else Accept(g,new AdvanceOneStepCommand(g.Revision));}
 private static void Accept(GameEngine g,GameCommand command){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack,Moves=g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static void Replay(GameEngine g,ContentRegistry r){Require(g.CreateCardZoneDiagnostics().Count==100&&g.CreateCardZoneDiagnostics().Select(c=>c.CardId).Distinct().Count()==100,"All entities are conserved.");Require(State(g)==State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r)),"Every viewer, prompt, exact physical cost and nested cursor replays identically.");}
 private static void Atomic(GameEngine g,GameCommand command){var before=State(g);Require(!g.Submit(command).Accepted&&before==State(g),"Unpublished/wrong-actor input rejects atomically.");}
 private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 private sealed class InvalidUpgradeLink(SkillProgramCatalog catalog):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-xue-invalid",new Version(1,0,0),[]);
  public void Register(IContentRegistryBuilder b){foreach(var p in catalog.Programs)b.AddSkill(new(p.Key,p.Key,"测试"){Program=p.Value});}
 }
 private sealed class Fixture(bool multi,bool death,bool diamonds,bool ai,bool extraTarget=false):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-xue",new Version(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   if(extraTarget)CharacterFixtureContent.Register(b,"classic:xue-zong");
   var count=multi?2:1;var rules=$$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:xue-driver","revision":1,"activations":[{"id":"draw-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1}]}],"viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false},{"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},{"id":"draw","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true}],"triggers":[{"id":"skipdraw","window":"drawPhaseStarting","drawPhaseMode":"replacement","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":0}]}]},{"id":"fixture:xue-response","revision":1,"viewAs":[{"id":"dodge","inputKinds":[],"inputSuits":[],"inputCount":{{count}},"outputKind":"dodge","forPlay":false,"forResponse":true},{"id":"slash","inputKinds":[],"inputSuits":[],"inputCount":{{count}},"outputKind":"slash","forPlay":false,"forResponse":true},{"id":"counter","inputKinds":[],"inputSuits":[],"inputCount":{{count}},"outputKind":"nullification","forPlay":false,"forResponse":true,"extendedUse":true}],"triggers":[{"id":"skipdraw","window":"drawPhaseStarting","drawPhaseMode":"replacement","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":0}]}]},{"id":"fixture:xue-observe","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","movementReasons":["skill-program.response-entity-exchange","skill-program.public-suit-escalating-draw"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observe","options":[{"id":"continue"}]}],"destinationZones":["hand"]},{"id":"discarded","window":"cardsMoved","subject":"owner","movementReasons":["skill-program.public-suit-escalating-discard"],"movementOccurrence":"perOwnerBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observe","options":[{"id":"continue"}]}],"sourceZones":["hand","equipment"]}]}]}""";
   if(extraTarget){var doc=System.Text.Json.Nodes.JsonNode.Parse(rules)!;doc["skills"]![0]!["activations"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"add-target","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"grantNextCardTargetAdjustment","target":"owner"}]}"""));rules=doc.ToJsonString();}
   if(death){var doc=System.Text.Json.Nodes.JsonNode.Parse(rules)!;var effects=doc["skills"]![2]!["triggers"]![0]!["effects"]!.AsArray();effects.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":20}"""));effects.Add(System.Text.Json.Nodes.JsonNode.Parse("""{"op":"loseHp","target":"owner","amount":1}"""));rules=doc.ToJsonString();}
   var catalog=SkillProgramCatalog.Load(rules,"""{"schemaVersion":3,"skills":{"fixture:xue-driver":{"name":"真实用牌驱动","description":"测试"},"fixture:xue-response":{"name":"真实响应转换","description":"测试"},"fixture:xue-observe":{"name":"真实移动观察","description":"测试","optionLabels":{"continue":"继续"}}}}""");foreach(var d in catalog.Programs)b.AddSkill(new(d.Key,d.Key,"测试"){Program=d.Value});
   b.AddGeneral(new("fixture:xue-owner","薛综测试","supporter","classic:funan","wu",20,["classic:jiexun","fixture:xue-driver","fixture:xue-response","fixture:xue-observe"]));for(var n=1;n<4;n++)b.AddGeneral(new($"fixture:xue-target-{n}","目标","supporter","fixture:xue-response","shu",20,ai?["fixture:xue-observe","fixture:xue-driver"]:["fixture:xue-observe"]));
   b.AddDeck(new("fixture:xue-deck","固定牌堆",4,0,[]){PhysicalCards=Enumerable.Range(0,100).Select(n=>new ContentDeckPhysicalCard("standard:crossbow",diamonds?Suit.Diamond:Suit.Spade,n%13+1)).ToArray()});b.AddMode(new("identity:classic-xue-check","测试",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:xue-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:xue-owner","fixture:xue-target-1","fixture:xue-target-2","fixture:xue-target-3"]));
  }
 }
}
