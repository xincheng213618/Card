using System.Text.Json;
using System.Reflection;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class Fame2017XinXianyingChecks
{
 public static void ComparisonIndependentBranchesQuotaAndReplay()
 {
  foreach(var mode in new[]{"both","rank","color","none"})
  {
   var(g,r)=Create();ChooseCaishi(g,"limit");ReachPlay(g);var limit=Limit(g);Require(g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.DrawTwo),"The +1 limit choice preserves uses on self and other targets.");var pair=Pair(g,mode);HurtToOne(g,pair.Seat);var source=pair.Source;
   Compare(g,source.Id,pair.Seat);Require(g.CreateSnapshot(2).PublicRevealedCards.Select(c=>c.Id).SequenceEqual([source.Id]),"Only the selected owner entity is initially public.");
   var d=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().HandComparisonDraft!;var slot=Array.IndexOf(d.CandidateIds.ToArray(),pair.Card.Id).ToString();
   Require(Prompt(g)!.Choices.All(c=>c.Cards.Count==0)&&!g.CreateSnapshot(0).Players[pair.Seat].Hand.Any(),"Concealed choices never expose unselected hand faces or entity IDs.");Replay(g,r);Atomic(g,new AnswerPromptCommand(pair.Seat,Prompt(g)!.PromptId,Prompt(g)!.Choices.First().Id,g.Revision));Atomic(g,new AnswerPromptCommand(0,Prompt(g)!.PromptId,new("unpublished"),g.Revision));
   Answer(g,c=>c.Parameters.GetValueOrDefault("token")==slot);var result=g.Events.Select(e=>e.Payload).OfType<ProgramHandComparisonResolvedEvent>().Last();Require(g.CreateSnapshot(2).Players[0].SkillRuntimeStates!.Single(x=>x.SkillId=="classic:zhongjian").PublicRuleStates!.Single(x=>x.Kind==ProgramPublicRuleStateKind.ActivationLimit) is {Used:1} current && current.Value==(result.RankMatched?2:1),"Public generic rule state exposes the actual quota and current usage.");
   Require(result.ColorMatched==(mode is "both" or "color")&&result.RankMatched==(mode is "both" or "rank"),"Color and rank are independent real revealed-card facts.");
   Require(g.CreateSnapshot(2).PublicRevealedCards.Select(c=>c.Id).Order().SequenceEqual(new[]{source.Id,pair.Card.Id}.Order()),"The final revealed entity remains publicly visible at the reward/completion prompt.");Replay(g,r);
   if(mode is "both" or "color"){Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="draw");ResumeObserver(g,r);}else Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="complete");ReachPlay(g);
   Require(g.CreateCardZoneDiagnostics().Single(z=>z.CardId==source.Id).Location==CardLocation.Hand(0),"A reveal never pays or moves its own selected hand card.");
   Require(Limit(g)==limit-(mode=="none"?1:0),"Only neither match applies the persistent -1 limit.");
   if(mode is "both" or "rank")
   {
    Compare(g,source.Id,pair.Seat);Answer(g,c=>c.Parameters.GetValueOrDefault("token")==slot);if(mode=="both"){Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="draw");ResumeObserver(g,r);}else Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="complete");ReachPlay(g);
    Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="classic:zhongjian"),"A second rank match cannot stack the quota above two.");
   }
   else Require(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="classic:zhongjian"),"Without rank match the phase remains once.");
   Atomic(g,new UseProgramSkillCommand(0,"classic:zhongjian","compare",[source.Id],[pair.Seat],g.Revision,Prompt(g)!.PromptId));Replay(g,r);
  }
 }
 public static void DiscardRealHandEquipmentAndNestedReplay()
 {
  foreach(var equipment in new[]{false,true})
  {
   var(g,r)=Create();ChooseCaishi(g,"limit");ReachPlay(g);
   if(equipment){Use(g,"equip-target",[1]);Answer(g,c=>c.Cards.Count==1);ReachPlay(g);}
   var pair=Pair(g,"color");HurtToOne(g,pair.Seat);Compare(g,pair.Source.Id,pair.Seat);var d=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().HandComparisonDraft!;Answer(g,c=>c.Parameters.GetValueOrDefault("token")==Array.IndexOf(d.CandidateIds.ToArray(),pair.Card.Id).ToString());
   var target=equipment?g.CreateSnapshot(0,true).Players.First(p=>p.Seat!=0&&p.Equipment.Count>0).Seat:pair.Seat;
   Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="target"&&c.Targets.SequenceEqual([target]));
   var choice=equipment?Prompt(g)!.Choices.First(c=>c.Cards.Count==1):Prompt(g)!.Choices.First(c=>c.Cards.Count==0);var id=equipment?choice.Cards.Single():g.CreateSnapshot(target,true).Players[target].Hand[int.Parse(choice.Parameters["token"][5..])].Id;
   var from=g.CreateCardZoneDiagnostics().Single(z=>z.CardId==id).Location;Answer(g,c=>c.Id==choice.Id);ResumeObserver(g,r);ReachPlay(g);
   Require(g.CardMovements.Count(m=>m.CardId==id&&m.From==from&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="skill-program.hand-comparison.reward-discard")==1,"HE reward moves exactly one actual entity before nested response.");Replay(g,r);
  }
 }
 public static void PersistentStateSelfTargetAndInsufficientHand()
 {
  var(g,r)=Create(borrowed:true);ChooseCaishi(g,"limit");ReachPlay(g);var baseLimit=Limit(g);var pair=Pair(g,"none");HurtToOne(g,pair.Seat);Compare(g,pair.Source.Id,pair.Seat);var d=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().HandComparisonDraft!;Answer(g,c=>c.Parameters["token"]==Array.IndexOf(d.CandidateIds.ToArray(),pair.Card.Id).ToString());Answer(g,c=>c.Parameters["branch"]=="complete");ReachPlay(g);Require(Limit(g)==baseLimit-1,"Persistent reduction is applied.");
  Use(g,"hurt-owner",[]);ReachPlay(g);NextCaishi(g);var hp=g.CreateSnapshot(0).Players[0].Hp;ChooseCaishi(g,"recover");ReachPlay(g);Require(g.CreateSnapshot(0).Players[0].Hp==hp+1&&Limit(g)==baseLimit-1,"Recovery restores HP while persistent previous hand-limit changes survive the new turn.");
  Require(g.CreateSnapshot(2).Players[0].SkillRuntimeStates!.Single(x=>x.SkillId=="classic:caishi").PublicRuleStates!.Single(x=>x.Kind==ProgramPublicRuleStateKind.SelfTargetProhibition).Value==1,"Every viewer can read the generic active self-target restriction.");Require(g.GetHumanLegalActions().All(a=>a.Kind is not(LegalActionKind.Equip or LegalActionKind.Peach or LegalActionKind.Alcohol or LegalActionKind.DrawTwo)&&!(a.Kind==LegalActionKind.BorrowedSword?a.TargetSeats.Where((_,index)=>index%2==0):a.TargetSeats).Contains(0)),"Self prohibition blocks implicit self-use and actual designated self targets.");
  Require(g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.IronChain&&a.TargetSeats.Count>0),"Other-target ordinary use remains available.");var excluded=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.FiveGrains&&a.ConversionSource is not null);Accept(g,new PlayCardCommand(0,excluded.CardId!.Value,excluded.TargetSeats,g.Revision,Prompt(g)!.PromptId,excluded.PlayedCardKind){ConversionSource=excluded.ConversionSource});Require(g.ResolutionStack.OfType<CardUseFrame>().Last().TargetSeats.All(seat=>seat!=0),"ExcludeOwnerEffects keeps the actual use free of forbidden self targets.");ReachPlay(g);Replay(g,r);
  Use(g,"equip-target",[1]);Reach(g,p=>p.PlayerSeat==0&&(p.Kind==DecisionKind.PlayCard||p.Choices.Any(c=>c.Cards.Count==1)));if(Prompt(g)!.Kind!=DecisionKind.PlayCard)Answer(g,c=>c.Cards.Count==1);ReachPlay(g);
  var borrowed=g.GetHumanLegalActions().First(a=>a.Kind==LegalActionKind.BorrowedSword&&a.ConversionSource is not null&&a.TargetSeats.SequenceEqual(new[]{1,0}));
  Accept(g,new PlayCardCommand(0,borrowed.CardId!.Value,borrowed.TargetSeats,g.Revision,Prompt(g)!.PromptId,borrowed.PlayedCardKind){ConversionSource=borrowed.ConversionSource});
  ReachPlay(g);var borrowedResult=g.Events.Select(e=>e.Payload).OfType<BorrowedSwordResolvedEvent>().Last(e=>e.SourceSeat==0&&e.WeaponOwnerSeat==1&&e.SlashTargetSeat==0);
  Require(g.Events.Select(e=>e.Payload).OfType<CardUseDeclaredEvent>().Any(e=>e.ResolutionId==borrowedResult.ResolutionId&&e.CardId==borrowed.CardId&&e.CardKind==CardKind.BorrowedSword),"The restricted actor can be the later Slash victim of its actual paid Borrowed Sword, and the exact holder/victim continuation completes.");Replay(g,r);
  var target=g.CreateSnapshot(0,true).Players.First(p=>p.Seat!=0&&p.Hp>p.Hand.Count&&p.Hand.Count>0);var source=g.CreateSnapshot(0,true).Players[0].Hand.First();Compare(g,source.Id,target.Seat);d=g.ResolutionStack.OfType<ProgramSkillFrame>().Last().HandComparisonDraft!;Require(d.Required==target.Hand.Count,"Insufficient hand reveals all actual cards, never the old hand-minus-HP quantity.");
  while(Prompt(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("branch")=="reveal")==true){Replay(g,r);Answer(g,_=>true);}Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="draw"||c.Parameters.GetValueOrDefault("branch")=="complete");if(Prompt(g)?.SkillPrompt?.SkillId=="fixture:xin-observer")ResumeObserver(g,r);ReachPlay(g);NextCaishi(g);ChooseCaishi(g,"limit");ReachPlay(g);Require(g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.DrawTwo),"The prior turn self-target prohibition expires before a new limit choice.");Replay(g,r);
 }
 public static void NativeAiAndStrictLoader()
 {
  var(rescue,rescueRegistry)=Create();ChooseCaishi(rescue,"limit");ReachPlay(rescue);Use(rescue,"hurt-owner",[]);ReachPlay(rescue);NextCaishi(rescue);ChooseCaishi(rescue,"recover");ReachPlay(rescue);
  Require(rescue.CreateSnapshot(0,true).Players[0].Hand.Any(c=>c.Kind==CardKind.Peach),"The self rescue guard is exercised with a real Peach in hand.");
  var rescueHp=rescue.CreateSnapshot(0).Players[0].Hp;for(var n=0;n<rescueHp;n++){Use(rescue,"hurt-owner",[]);if(n+1<rescueHp)ReachPlay(rescue);}
  Require(rescue.ResolutionStack.OfType<DyingFrame>().Any(),"The forbidden self target is tested in a real dying chain.");Replay(rescue,rescueRegistry);
  for(var n=0;n<100&&rescue.ResolutionStack.OfType<DyingFrame>().Any();n++){Require(Prompt(rescue) is not {PlayerSeat:0},"The prohibited owner receives no self Peach/Alcohol use choice.");Advance(rescue);}
  Require(rescue.CreateSnapshot(0).Players[0].Hp>0&&rescue.Events.Select(e=>e.Payload).OfType<DyingResponseEvent>().Any(e=>e.ResponderSeat!=0&&e.UsedPeach),"Another responder can still pay a real Peach to rescue the restricted owner.");Replay(rescue,rescueRegistry);
  var(g,r)=Create(true);for(var i=0;i<500;i++)
  {
   if(g.Events.Select(e=>e.Payload).OfType<ProgramSkillResolvedEvent>().Any(e=>e.OwnerSeat!=0&&e.SkillId=="classic:zhongjian"&&e.Completed)){Require(!g.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c=>c.ActorSeat!=0),"AI chooses actual comparison reveal and reward by AdvanceOneStep.");Replay(g,r);break;}
   if(Prompt(g) is{PlayerSeat:0,Kind:DecisionKind.PlayCard}p)Accept(g,new EndPlayPhaseCommand(0,g.Revision,p.PromptId));else if(Prompt(g) is{PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip"||c.Parameters.GetValueOrDefault("token")=="finish"||c.Cards.Count==0);else Advance(g);
   if(i==499)throw new InvalidOperationException("Normal AI failed to execute comparison.");
  }
  var assembly=typeof(StandardClassicGeneralPackage).Assembly;string Read(string suffix){using var stream=assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.classic-xin-xianying."+suffix+".json")!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}var rules=Read("rules");var presentation=Read("presentation");
  foreach(var bad in new[]{rules.Replace("\"minCards\": 1","\"minCards\": 0"),rules.Replace("\"hand\"","\"equipment\""),rules.Replace("\"otherLiving\"","\"anyLiving\"")}){var refused=false;try{SkillProgramCatalog.Load(bad,presentation);}catch(InvalidOperationException){refused=true;}Require(refused,"Shared resource validation rejects unpaid count, wrong zone and target relation.");}
 }
 private static (int Seat,CardSnapshot Source,CardSnapshot Card) Pair(GameEngine g,string mode)
 {
  foreach(var source in g.CreateSnapshot(0,true).Players[0].Hand)foreach(var p in g.CreateSnapshot(0,true).Players.Where(p=>p.Seat!=0&&p.Hp==8))foreach(var card in p.Hand)
  {var color=(source.Suit is Suit.Heart or Suit.Diamond)==(card.Suit is Suit.Heart or Suit.Diamond);var rank=source.Rank==card.Rank;if(color==(mode is "both" or "color")&&rank==(mode is "both" or "rank"))return(p.Seat,source,card);}throw new InvalidOperationException("Fixed comparison fixture missing "+mode+" players="+JsonSerializer.Serialize(g.CreateSnapshot(0,true).Players.Select(p=>new{p.Seat,p.Hp,p.GeneralId,p.Hand})));
 }
 private static int Limit(GameEngine g){var players=(System.Collections.IList)typeof(GameEngine).GetField("_players",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(g)!;return(int)typeof(GameEngine).GetMethod("GetHandLimit",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(g,[players[0]])!;}
 private static void HurtToOne(GameEngine g,int seat){Use(g,"hurt",[seat]);ReachPlay(g);}
 private static void Use(GameEngine g,string id,int[]targets)=>Accept(g,new UseProgramSkillCommand(0,"fixture:xin-driver",id,[],targets,g.Revision,Prompt(g)!.PromptId));
 private static void Compare(GameEngine g,int card,int seat)=>Accept(g,new UseProgramSkillCommand(0,"classic:zhongjian","compare",[card],[seat],g.Revision,Prompt(g)!.PromptId));
 private static void ChooseCaishi(GameEngine g,string option){Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")==option);}
 private static void NextCaishi(GameEngine g){Accept(g,new EndPlayPhaseCommand(0,g.Revision,Prompt(g)!.PromptId));Reach(g,p=>p.PlayerSeat==0&&p.SkillPrompt?.SkillId=="classic:caishi");}
 private static void ResumeObserver(GameEngine g,ContentRegistry r){Reach(g,p=>p.SkillPrompt?.SkillId=="fixture:xin-observer");Replay(g,r);Atomic(g,new AnswerPromptCommand(Prompt(g)!.PlayerSeat,Prompt(g)!.PromptId,new("invalid"),g.Revision));if(Prompt(g)!.PlayerSeat==0)Answer(g,_=>true);else Advance(g);}
 private static void ReachPlay(GameEngine g)=>Reach(g,p=>p.PlayerSeat==0&&p.Kind==DecisionKind.PlayCard);
 private static void Reach(GameEngine g,Func<PendingDecision,bool> condition){for(var i=0;i<550;i++){if(Prompt(g) is{}p&&condition(p))return;Advance(g);}throw new InvalidOperationException("Missing fixture boundary: "+Prompt(g)?.ToString());}
 private static PendingDecision? Prompt(GameEngine g)=>g.PendingDecision??Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s,true).PendingDecision).FirstOrDefault(p=>p is not null);
 private static void Answer(GameEngine g,Func<PromptChoice,bool> select){var p=Prompt(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(select).Id,g.Revision));}
 private static void Advance(GameEngine g)=>Accept(g,new AdvanceOneStepCommand(g.Revision));
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Require(result.Accepted,result.Error?.Message??"Rejected");}
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack,Moves=g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static void Replay(GameEngine g,ContentRegistry r){Require(g.CreateCardZoneDiagnostics().Count==140&&g.CreateCardZoneDiagnostics().Select(z=>z.CardId).Distinct().Count()==140,"Every real entity is conserved exactly once.");var replay=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Require(State(g)==State(replay),"All observer views, private choices, frames and movements replay exactly.");}
 private static void Atomic(GameEngine g,GameCommand c){var before=State(g);Require(!g.Submit(c).Accepted&&before==State(g),"Illegal commands are atomically rejected.");}
 private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 private static (GameEngine,ContentRegistry) Create(bool ai=false,bool borrowed=false)
 {
  var r=ContentRegistry.Build(new StandardContentPackage(),new StandardActiveSkillExpansionPackage(true),new StandardRescueSkillExpansionPackage(),new StandardClassicGeneralPackage(),new Fixture(ai,borrowed));var g=GameEngine.CreateStandard(new GameOptions{Seed=17,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId="identity:classic-xin-check",UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Accept(g,new SelectGeneralCommand(0,ai?"fixture:xin-target-1":"fixture:xin-owner",g.Revision,Prompt(g)!.PromptId));if(ai)ReachPlay(g);else Reach(g,p=>p.PlayerSeat==0&&p.SkillPrompt?.SkillId=="classic:caishi");return(g,r);
 }
 private sealed class Fixture(bool ai,bool borrowed):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-xin",new Version(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   var catalog=SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:xin-driver","revision":1,"viewAs":[{"id":"peach","inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":true,"forResponse":false,"useOnly":true},{"id":"alcohol","inputKinds":[],"inputSuits":[],"outputKind":"alcohol","forPlay":true,"forResponse":false,"useOnly":true},{"id":"draw","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},{"id":"chain","inputKinds":[],"inputSuits":[],"outputKind":"ironChain","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},{"id":"excluded-grains","inputKinds":[],"inputSuits":[],"outputKind":"fiveGrains","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true,"excludeOwnerEffects":true}],"activations":[{"id":"equip-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"targetRef":{"kind":"selectedTarget"},"zones":["hand"],"cardCategories":["equipment"],"count":1,"destination":"selectedTargetEquipment"}]},{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":7}]},{"id":"hurt-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},{"id":"fixture:xin-observer","revision":1,"triggers":[{"id":"observe","window":"cardsMoved","subject":"owner","movementReasons":["skill-program.hand-comparison.reward-discard"],"movementOccurrence":"perOwnerBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observe","options":[{"id":"continue"}]}],"sourceZones":["hand","equipment"]},{"id":"gain","window":"cardsGained","subject":"owner","movementReasons":["skill-program.hand-comparison.reward-draw"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"observe","options":[{"id":"continue"}]}],"destinationZones":["hand"]}]}]}""".Replace("\"viewAs\":[",borrowed?"\"viewAs\":[{\"id\":\"borrowed\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"borrowedSword\",\"forPlay\":true,\"forResponse\":false,\"useOnly\":true,\"singleCardTrickUse\":true},":"\"viewAs\":["),"""{"schemaVersion":3,"skills":{"fixture:xin-driver":{"name":"真实驱动","description":"测试"},"fixture:xin-observer":{"name":"移动观察","description":"测试","optionLabels":{"continue":"继续"}}}}""");foreach(var d in catalog.Programs)b.AddSkill(new(d.Key,d.Key,"测试"){Program=d.Value});
   b.AddGeneral(new("fixture:xin-owner","辛宪英测试","supporter","classic:zhongjian","wei",3,ai?["classic:caishi"]:["classic:caishi","fixture:xin-driver","fixture:xin-observer"],GeneralGender.Female));
   for(var i=1;i<4;i++)b.AddGeneral(new($"fixture:xin-target-{i}","目标","supporter","fixture:xin-observer","shu",8));
   b.AddDeck(new("fixture:xin-deck","小牌堆",4,0,[]){PhysicalCards=Enumerable.Range(0,140).Select(i=>new ContentDeckPhysicalCard(i%3==0?"standard:crossbow":"standard:peach",i%4==0?Suit.Heart:i%4==1?Suit.Spade:i%4==2?Suit.Diamond:Suit.Club,i%3+1)).ToArray()});
   b.AddMode(new("identity:classic-xin-check","测试",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Loyalist)]=1,[nameof(Role.Rebel)]=2},"fixture:xin-deck",GeneralCandidateCount:4,GeneralPoolIds:["fixture:xin-owner","fixture:xin-target-1","fixture:xin-target-2","fixture:xin-target-3"]));
  }
 }
}
