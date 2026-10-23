using CardGame.Core;
using CardGame.Content.Standard;
internal static class CaoChunChecks
{
 private const string Owner="fixture:cao-chun-owner",Driver="fixture:cao-chun-driver",Quiet="fixture:cao-chun-quiet",Watch="fixture:cao-chun-watch",Mode="identity:classic-cao-chun";
 public static void InitialMarksPrintedEquipmentNativeLossAndCold()
 {
  var(g,r)=Start();Play(g);Need(Enumerable.Range(0,4).All(v=>g.CreateSnapshot(v).Players[0].Markers!.Single(m=>m.Kind==PlayerMarkerKind.ShanjiaLoss).Count==3),"Every actual viewer sees the derived public loss marker without a second persistent counter.");Need(Marks(g)==3&&E<ShanjiaMarksChangedEvent>(g).Count(e=>e.MovementSequence==0)==1,"Native game start grants exactly three loss markers.");var action=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip);var id=action.CardId!.Value;var before=g.CardMovements.Count;Native(g,action);Play(g);Need(Marks(g)==2&&g.CardMovements.Skip(before).Any(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)&&E<ShanjiaMarksChangedEvent>(g).Count(e=>e.MovementSequence>0)==1,"Printed hand equipment loses held ownership once at native payment; processing cleanup cannot decrement twice.");g=Cold(g,r);Need(Marks(g)==2&&Enumerable.Range(0,4).All(v=>g.CreateSnapshot(v).Players[0].Markers!.Single(m=>m.Kind==PlayerMarkerKind.ShanjiaLoss).Count==2),"Cold replay preserves the same native equipment-loss and derived public marker for every viewer.");
 }
 public static void AcquiredAfterStartGrantsThreeMarksAndPreservesEndingConditionCold()
 {
  foreach(var(useSlash,discardKind)in new (bool,CardKind?)[]{(true,CardKind.Crossbow),(false,null),(false,CardKind.Dodge)})
  {
   var(g,r)=Start(adapter:"acquired");Play(g);
   Need(!E<ShanjiaMarksChangedEvent>(g).Any(),"A player without Shanjia at game start has no loss marks.");
   Accept(g,new UseProgramSkillCommand(0,Driver,"gain-shanjia",[],[],g.Revision,P(g)!.PromptId));Play(g);
   Need(Marks(g)==3&&E<ShanjiaMarksChangedEvent>(g).Count()==1&&Enumerable.Range(0,4).All(v=>
    g.CreateSnapshot(v).Players[0].Markers!.Single(m=>m.Kind==PlayerMarkerKind.ShanjiaLoss).Count==3),
    "A real mid-game grant immediately issues three public loss marks exactly once.");
   g=Cold(g,r);
   Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip));Play(g);
   Need(Marks(g)==2,"A real equipment loss after acquisition removes one loss mark before activating Shanjia.");
   Accept(g,new UseProgramSkillCommand(0,"ol:shanjia","draw-and-slash",[],[],g.Revision,P(g)!.PromptId));
   Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="shanjia")==true);
   g=Cold(g,r);
   var material=useSlash?P(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("effective-kind")==nameof(CardKind.FireSlash)).Cards.Single():0;
   Answer(g,c=>useSlash?c.Cards.Contains(material)&&c.Parameters.GetValueOrDefault("effective-kind")==nameof(CardKind.FireSlash):c.Parameters.GetValueOrDefault("branch")=="skip");
   if(useSlash)
   {
    Need(g.ResolutionStack.OfType<CardUseFrame>().Any(f=>f.CaoChunSlash is not null),"The real Fire Slash suspends on its native response child.");
    g=Cold(g,r);
   }
   Play(g);
   Need(!g.IsFaulted&&E<ShanjiaDebtArmedEvent>(g).Single() is{FrozenX:2},
    "Both a completed Slash and a declined Slash arm the phase debt from the actual remaining loss marks.");
   if(useSlash)
   {
    Need(E<ShanjiaNativeSlashReturnedEvent>(g).Count()==1&&E<CardActionAcceptedEvent>(g).Any(e=>e.Action.Type==CardActionType.Response&&e.Action.EffectiveKind==CardKind.Dodge),
     "The reported real Dodge path returns to its exact Shanjia activation once.");
    Need(g.CardMovements.Count(m=>m.CardId==material&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing)==1&&
     g.CreateCardZoneDiagnostics().Single(c=>c.CardId==material).Location==CardLocation.DiscardPile,"Fire Slash is paid and discarded exactly once.");
   }
   if(discardKind is{}kind)
   {
    Native(g,g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.Equip));
    Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="discard")==true);
    Need(E<ShanjiaHandUseDebitedEvent>(g).Single() is{RemainingBefore:2,RemainingAfter:1},
     "The next actual hand use pays the frozen debt once even if equipment movement changes the live marks.");
    g=Cold(g,r);
    var discard=P(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("branch")=="discard"&&
     c.Cards.Count==1&&g.CreateSnapshot(0).Players[0].Hand.Any(card=>card.Id==c.Cards[0]&&card.Kind==kind)).Cards.Single();
    Answer(g,c=>c.Cards.SequenceEqual([discard]));Play(g);
    Need(g.CardMovements.Count(m=>m.CardId==discard&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.shanjia.discard")==1,
     "The selected equipment or non-equipment discard is a real once-paid movement.");
   }
   g=Cold(g,r);var turn=g.State.TurnNumber;End(g);
   Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:shanjia"||E<TurnEndedEvent>(g).Any(e=>e.TurnNumber==turn&&e.ActorSeat==0));
   if(discardKind==CardKind.Dodge)
   {
    Need(P(g)?.SkillPrompt?.SkillId!="ol:shanjia"&&!E<ShanjiaNativeSlashIssuedEvent>(g).Any(e=>e.Ending),
     "A Shanjia non-equipment discard suppresses the optional ending Slash.");
   }
   else
   {
    Need(P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")==true,
     "No Shanjia discard or equipment-only Shanjia discards offer the ending Slash.");
    Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="virtual")==true);
    g=Cold(g,r);var hand=g.CreateSnapshot(0).Players[0].HandCount;
    Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="virtual");
    Reach(g,()=>!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.CaoChun is not null));
    Need(!g.IsFaulted&&E<ShanjiaNativeSlashIssuedEvent>(g).Count(e=>e.Ending)==1&&
     E<ShanjiaDebtArmedEvent>(g).Count()==1&&g.CreateSnapshot(0).Players[0].HandCount==hand,
     "The ending Slash uses no physical hand card and cannot rearm or repay the completed debt.");
   }
   Cold(g,r);
  }
 }
 private static int Marks(GameEngine g)=>E<ShanjiaMarksChangedEvent>(g).Last().After;
 private static void Native(GameEngine g,LegalAction action)=>Accept(g,new PlayCardCommand(0,action.CardId!.Value,action.TargetSeats,g.Revision,P(g)!.PromptId,action.PlayedCardKind,action.TargetCardId){ConversionSource=action.ConversionSource});
    private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();private static PendingDecision?P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p!=null);
    private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(x.Accepted,(x.Error?.Message??"Rejected CaoChun command.")+" "+Diagnostic(g));}private static void Answer(GameEngine g,Func<PromptChoice,bool>test){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(test).Id,g.Revision));}
private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
    private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
    private static void Reach(GameEngine g,Func<bool>done){for(var i=0;i<200;i++){if(done())return;var p=P(g);if(p?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip")==true)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");else if(p is{Kind:DecisionKind.RescueDying,PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");else if(p is{PlayerSeat:0,Kind:DecisionKind.PlayCard})End(g);else if(p is{PlayerSeat:0,RequiredCardKind:CardKind.Slash}&&p.Choices.Any(c=>c.Cards.Count==0))Answer(g,c=>c.Cards.Count==0);else if(p is{PlayerSeat:0,Kind:DecisionKind.Nullification}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("response")=="pass"))Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="pass");else Accept(g,new AdvanceOneStepCommand(g.Revision));}throw new InvalidOperationException("Small native CaoChun fixture exceeded its boundary. "+Diagnostic(g));}
    private static string Diagnostic(GameEngine g)=>System.Text.Json.JsonSerializer.Serialize(new{g.State,Pending=P(g),Stack=g.ResolutionStack.Select(f=>new{Type=f.GetType().Name,Frame=System.Text.Json.JsonSerializer.Serialize(f,f.GetType())}),Last12Events=g.Events.TakeLast(12).Select(e=>new{Type=e.Payload.GetType().Name,Fact=System.Text.Json.JsonSerializer.Serialize(e.Payload,e.Payload.GetType())}),Last12Movements=g.CardMovements.TakeLast(12),NativeUses=g.Events.Select(e=>e.Payload).Where(e=>e is CardUseDeclaredEvent or CardActionAcceptedEvent or CardUseFinishedEvent).TakeLast(16).Select(e=>new{Type=e.GetType().Name,Fact=System.Text.Json.JsonSerializer.Serialize(e,e.GetType())})});
    private static string State(GameEngine g)=>System.Text.Json.JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>System.Text.Json.JsonSerializer.Serialize(f,f.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Every private viewer, exact acquired lease/debt and once-paid native child survives cold replay.");return restored;}
 private static (GameEngine,ContentRegistry)Start(bool watch=false,bool death=false,bool counterspell=false,string? adapter=null)
 {
  var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(watch,death,counterspell,adapter));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=watch?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
 }
 private sealed class Fixture(bool watch,bool death,bool counterspell,string? adapter):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("cao-chun-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   string Read(string suffix){using var stream=typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-cao-chun."+suffix)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}Add(SkillProgramCatalog.Load(Read("rules.json"),Read("presentation.json")));
   var driverRules=System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":62,"skills":[{"id":"fixture:cao-chun-driver","revision":1,"activations":[{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]}]},{"id":"fixture:cao-chun-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}""")!;driverRules["skills"]![0]!["activations"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"native-duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]}"""));if(counterspell)driverRules["skills"]![0]!["viewAs"]=System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"actual-duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true}]""");if(adapter is not null){var driver=driverRules["skills"]![0]!;driver["activations"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"chain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}"""));if(adapter=="funded")driver["viewAs"]=System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"funded-slash","inputCount":0,"sourceZones":[],"inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"drawFundedDistinctBasic":{"methodLedgerId":"fixture:cao-chun-funded"}}]""");if(adapter=="chained")driver["viewAs"]=System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"release-slash","inputCount":0,"sourceZones":[],"inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"useOnly":true,"chainedStateCost":false}]""");if(adapter=="elemental")driver["viewAs"]=System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"elemental-slash","inputKinds":["drawTwo"],"inputSuits":[],"inputCount":1,"sourceZones":["hand"],"outputKind":"fireSlash","forPlay":true,"forResponse":false,"extendedUse":true}]""");if(adapter=="faction")driver["cardPolicies"]=System.Text.Json.Nodes.JsonNode.Parse("""[{"id":"faction-supply","kind":"factionResponseRequest","requiredCardKinds":["slash"],"factionId":"wei","ownerRole":"lord"}]""");}if(adapter=="acquired")driverRules["skills"]![0]!["activations"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""{"id":"gain-shanjia","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:shanjia"]}]}"""));Add(SkillProgramCatalog.Load(driverRules.ToJsonString(),"""{"schemaVersion":3,"skills":{"fixture:cao-chun-driver":{"name":"原生装备","description":"真实装备其他角色"},"fixture:cao-chun-quiet":{"name":"静默","description":"跳出牌"}}}"""));
   if(watch){var node=System.Text.Json.Nodes.JsonNode.Parse("""{"schemaVersion":62,"skills":[{"id":"fixture:cao-chun-watch","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementReasons":["skill-program.shanjia.discard"],"movementOccurrence":"perOwnerBatch","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]},{"id":"loss","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"recover","target":"owner","amount":20},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:shanjia","fixture:cao-chun-watch"],"sourceBind":"fixture:cao-chun-driver"}]}]}]}""")!;if(death)node["skills"]![0]!["triggers"]!.AsArray().RemoveAt(1);Add(SkillProgramCatalog.Load(node.ToJsonString(),"""{"schemaVersion":3,"skills":{"fixture:cao-chun-watch":{"name":"付款子链","description":"真实失源或死亡","optionLabels":{"continue":"继续"}}}}"""));}
   var extras=new List<string>{Driver};if(watch)extras.Add(Watch);b.AddGeneral(new(Owner,"真实曹纯","supporter",adapter=="acquired"?Driver:"ol:shanjia","wei",adapter=="funded"?16:4,adapter=="acquired"?[]:extras.ToArray(),GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>"fixture:cao-chun-peer-"+i).ToArray();foreach(var p in peers)b.AddGeneral(new(p,"其他角色","supporter",Quiet,"wei",12,[],GeneralGender.Male));
   b.AddDeck(new("fixture:cao-chun-deck","真实实体",12,2,[]){PhysicalCards=Enumerable.Range(0,120).Select(i=>new ContentDeckPhysicalCard(counterspell?"standard:nullification":i%3==0?"standard:crossbow":i%3==1?(adapter=="acquired"?"standard:fire_slash":"standard:slash"):(adapter=="acquired"?"standard:dodge":"standard:draw_two"),Suit.Heart,7)).ToArray()});b.AddMode(new(Mode,"真实曹纯",4,4,adapter=="faction"?new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2}}:new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:cao-chun-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
