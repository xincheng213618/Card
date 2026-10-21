using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouJiangJiAnniversaryChecks
{
 private const string Hero="mou:jiang-ji",Quiet="fixture:mjj-anniv-quiet",Giver="fixture:mjj-anniv-giver",Enemy="fixture:mjj-anniv-enemy",Peer="fixture:mjj-anniv-peer",Deck="fixture:mjj-anniv-deck";
 public static void PerPlayGiftsAndSelectedTargetNativeEffectsCold()
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
  var setup=new ShanheBattleSetup(7,1,"mou-jiang-ji-anniversary-native",Hero,20,17,0,0,0,[],
   [new(CardKind.Crossbow,Suit.Club,5),new(CardKind.DrawTwo,Suit.Heart,6),new(CardKind.DrawTwo,Suit.Spade,7),new(CardKind.IronChain,Suit.Club,8),new(CardKind.Duel,Suit.Spade,9)],[],
   [new(Enemy,Hp:20){StartingHand=[new(CardKind.Dodge,Suit.Club,2)]},new(Peer,Hp:20){StartingHand=[new(CardKind.Dodge,Suit.Club,3)]}],
   [new(Giver,Hp:20){StartingHand=[new(CardKind.Dodge,Suit.Club,4)]}],new string('f',64));
  var game=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=9},registry);
  PendingDecision? Prompt()=>Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
  T[] Events<T>()=>game.Events.Select(e=>e.Payload).OfType<T>().ToArray();
  PlayerSnapshot Player(int s)=>game.CreateSnapshot(0).Players[s];
  MouJiangJiReceipt? Receipt()=>game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.MouJiangJi is not null)?.MouJiangJi;
  string Diagnostic()=>JsonSerializer.Serialize(new{game.State.TurnNumber,game.State.CurrentSeat,game.State.Phase,Prompt=Prompt(),Receipt=Receipt(),Players=game.CreateSnapshot(0).Players.Select(p=>new{p.Seat,p.GeneralId,p.Hp,p.HandCount,p.Equipment}),Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=game.Events.TakeLast(30).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
  void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+Diagnostic());}
  void Submit(GameCommand c){try{var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Native command rejected.");}catch(Exception e){throw new InvalidOperationException(e.Message+Diagnostic(),e);}}
  void Answer(Func<PromptChoice,bool> match){var p=Prompt();Need(p?.PlayerSeat==0,"Only the actual Human chooser answers.");var choice=p!.Choices.FirstOrDefault(match);Need(choice is not null,"The exact native choice is published.");Submit(new AnswerPromptCommand(0,p.PromptId,choice!.Id,game.Revision));}
  void Reach(Func<bool> done,[CallerLineNumber]int line=0){for(var i=0;i<160;i++){if(done())return;Need(game.State.Winner==Winner.None,"Unexpected terminal result at "+line);if(Prompt()is{PlayerSeat:0})throw new InvalidOperationException("Unexpected actual Human boundary at "+line+Diagnostic());Submit(new AdvanceOneStepCommand(game.Revision));}throw new InvalidOperationException("Bounded native Jiang Ji flow not reached at "+line+Diagnostic());}
  void Play()=>Reach(()=>Prompt()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
  void Use(CardKind kind,IReadOnlyList<int>? targets=null)
  {var id=Player(0).Hand.First(c=>c.Kind==kind).Id;var a=game.GetHumanLegalActions().FirstOrDefault(a=>a.CardId==id&&a.ConversionSource is null&&a.Kind!=LegalActionKind.Recast&&(targets is null||a.TargetSeats.SequenceEqual(targets)));Need(a is not null,"The actual physical card has its canonical native Use.");Submit(new PlayCardCommand(0,id,a!.TargetSeats,game.Revision,Prompt()!.PromptId,a.PlayedCardKind,a.TargetCardId));}
  void Shiju(int card)
  {Submit(new UseProgramSkillCommand(0,"mou:shiju","give-and-use",[],[],game.Revision,Prompt()!.PromptId));Reach(()=>Receipt()?.Stage==MouJiangJiStage.Give&&Prompt()?.PlayerSeat==0);Answer(c=>c.Cards.SequenceEqual([card]));Reach(()=>Receipt()?.Stage==MouJiangJiStage.Use&&Prompt()?.PlayerSeat==0);}
  void Yingshi(int target)
  {Reach(()=>Prompt()?.SkillPrompt?.SkillId=="mou:yingshi");Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(()=>Receipt()?.Stage==MouJiangJiStage.YingshiTarget&&Prompt()?.PlayerSeat==0);Answer(c=>c.Targets.SequenceEqual([target]));Reach(()=>Receipt()?.Stage==MouJiangJiStage.YingshiChoice);}
  Submit(new StartGameCommand());Play();
  Need(Player(0).GeneralId==Hero&&Player(0).Hp==20&&Player(0).MaxHp==20&&Player(1).GeneralId==Giver&&Player(2).GeneralId==Enemy,"The real fixed Shanhe roster is installed.");
  var firstTurn=game.State.TurnNumber;var crossbow=Player(0).Hand.Single(c=>c.Kind==CardKind.Crossbow).Id;Shiju(crossbow);
  Need(Receipt()is{Delivered:true,Giver:0,Card.Id:var promised}&&promised==crossbow&&Events<MouJiangJiSelfCardCommittedEvent>()is[{CardId:var committed}]&&committed==crossbow&&Player(0).Hand.Any(c=>c.Id==crossbow)&&!Events<MouJiangJiGivenEvent>().Any()&&!game.CardMovements.Any(m=>m.CardId==crossbow&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Hand(0)),"Self Hand giving commits its exact existing entity without a fake Move or Give before optional Use.");
  Answer(c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Cards.SequenceEqual([crossbow]));Play();
  Need(Player(0).Equipment.Any(c=>c.Id==crossbow)&&Events<MouJiangJiUseIssuedEvent>().Length==1&&Events<MouJiangJiUseReturnedEvent>().Length==1&&Events<MouJiangJiDrawReturnedEvent>().Select(e=>(e.Cursor,e.Recipient,e.Actual)).SequenceEqual([(0,0,2),(1,0,2)])&&game.GetAttackRange(0)==2&&!game.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="mou:shiju"),"The true gifted equipment Use returns once, grants range from the resulting Equipment, and both self identities separately draw2 while spending the actual Play allowance.");
  var hand=Player(0).HandCount;Use(CardKind.DrawTwo);Yingshi(0);Answer(c=>c.Parameters.GetValueOrDefault("branch")=="repeat");Play();
  var repeated=Events<MouJiangJiEffectIssuedEvent>().Single().Grant;
  Need(Player(0).HandCount==hand+3&&Events<CardUseDeclaredEvent>().Count(e=>e.ResolutionId==repeated.UseId&&e.CardKind==CardKind.DrawTwo)==1&&Events<CardUseFinishedEvent>().Count(e=>e.ResolutionId==repeated.UseId)==1&&Events<MouJiangJiEffectStartedEvent>()is[{Target:0,Round:1}]&&Events<MouJiangJiEffectCompletedEvent>()is[{Target:0,Round:1}],"Yingshi adds only the selected target's native DrawTwo effect to the same paid Use, rather than paying or using a second card.");
  Use(CardKind.IronChain,[0]);Yingshi(0);Answer(c=>c.Parameters.GetValueOrDefault("branch")=="discard");Reach(()=>Receipt()?.Stage==MouJiangJiStage.Discard&&Prompt()?.PlayerSeat==0);
  var cost=Player(0).Hand.Where(c=>c.Kind==CardKind.Dodge).Take(2).Select(c=>c.Id).ToArray();Need(cost.Length==2&&Receipt()?.Requested==2,"The discard branch freezes current Equipment+1, with real payable HE.");Answer(c=>c.Parameters.GetValueOrDefault("branch")=="add"&&c.Cards.SequenceEqual([cost[0]]));
  var before=SnapshotJson.Serialize(game.CreateSnapshot(0));var peerBefore=SnapshotJson.Serialize(game.CreateSnapshot(2));var paidBefore=Events<MouJiangJiDiscardPaidEvent>().Length;
  game=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
  Need(before==SnapshotJson.Serialize(game.CreateSnapshot(0))&&peerBefore==SnapshotJson.Serialize(game.CreateSnapshot(2))&&Receipt()is{Stage:MouJiangJiStage.Discard,Selected.Count:1}&&Receipt()!.Selected.SequenceEqual([cost[0]])&&Events<MouJiangJiDiscardPaidEvent>().Length==paidBefore,"A representative Cold retains the private partial HE selection and exact original Trick/window without issuing a repeated effect or paying the unfinished cost.");
  Answer(c=>c.Parameters.GetValueOrDefault("branch")=="add"&&c.Cards.SequenceEqual([cost[1]]));Answer(c=>c.Parameters.GetValueOrDefault("branch")=="done");Play();
  Need(Events<MouJiangJiDiscardPaidEvent>().Select(e=>e.CardId).Order().SequenceEqual(cost.Order())&&cost.All(id=>game.CardMovements.Count(m=>m.CardId==id&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DiscardPile)==1)&&Events<MouJiangJiDisabledEvent>().Length==1&&Events<ProgramBooleanStateChangedEvent>().Any(e=>e.OwnerSeat==0&&e.SkillId=="mou:yingshi"&&e.StateId=="disabled"&&e.Value),"The selected target pays actual logical HE discard once after Cold, then Yingshi disables for this actual turn.");
  var starts=Events<MouJiangJiStartedEvent>().Count(e=>e.Operation==SkillProgramEffectOp.ResolveMouJiangJiYingshi);Use(CardKind.DrawTwo);Play();Need(Events<MouJiangJiStartedEvent>().Count(e=>e.Operation==SkillProgramEffectOp.ResolveMouJiangJiYingshi)==starts,"A later ordinary Trick in this same actual turn cannot activate disabled Yingshi.");
  Submit(new EndPlayPhaseCommand(0,game.Revision,Prompt()!.PromptId));Reach(()=>Receipt()is{Stage:MouJiangJiStage.Use,Giver:1}&&Prompt()?.PlayerSeat==0);
  var foreign=Receipt()!;var given=foreign.Card!.Id;var foreignFrame=Events<MouJiangJiStartedEvent>().Single(e=>e.Giver==1).FrameId;
  Need(foreign.Delivered&&foreign.Card.From==CardLocation.Hand(1)&&Events<ProgramSharedActivationStartedEvent>().Any(e=>e.FrameId==foreignFrame&&e.ActorSeat==1&&e.Source.OwnerSeat==0)&&Events<MouJiangJiGivenEvent>().Count(e=>e.FrameId==foreignFrame&&e.Giver==1&&e.Recipient==0&&e.CardId==given)==1&&game.CardMovements.Count(m=>m.CardId==given&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0))==1,"The real other-seat Play chooses its own actual gift and records the exact owner/actor/phase and physical Give before the owner decides to use it.");
  Answer(c=>c.Parameters.GetValueOrDefault("branch")=="decline");
  Need(Events<MouJiangJiRangeGrantedEvent>().Any(e=>e.FrameId==foreignFrame&&e.Recipient==1&&e.Amount==1)&&game.GetAttackRange(1)==2&&Events<MouJiangJiDrawReturnedEvent>().Length==2,"Declining the non-equipment gift still grants the giver current Equipment-based range, with no false damage reward.");Play();
  Need(Events<TurnEndedEvent>().Any(e=>e.ActorSeat==0&&e.TurnNumber==firstTurn)&&Events<TurnEndedEvent>().Any(e=>e.ActorSeat==1&&e.TurnNumber==foreign.Turn)&&game.GetAttackRange(1)==1&&Events<ProgramBooleanStateChangedEvent>().Any(e=>e.OwnerSeat==0&&e.SkillId=="mou:yingshi"&&e.StateId=="disabled"&&!e.Value),"The real TurnEnded boundaries expire the given range and restore Yingshi; each giver's next actual Play receives its own allowance.");
  var duel=Player(0).Hand.Single(c=>c.Kind==CardKind.Duel).Id;var hp=Player(2).Hp;Shiju(duel);Answer(c=>c.Parameters.GetValueOrDefault("branch")=="use"&&c.Cards.SequenceEqual([duel])&&c.Targets.SequenceEqual([2]));Yingshi(2);Play();
  var native=Events<MouJiangJiUseIssuedEvent>().Single(e=>e.CardId==duel);var action=Events<CardUseAppearanceCapturedEvent>().Select(e=>e.Action).Single(a=>a.ActionId==native.ActionId);
  Need(action.Type==CardActionType.Use&&action.ActorSeat==0&&action.ProviderSeat==0&&action.PhysicalCards is[{CardId:var material,From:var from}]&&material==duel&&from==CardLocation.Hand(0)&&action.TargetSeats.SequenceEqual([2])&&Player(2).Hp==hp-2&&Events<MouJiangJiUseReturnedEvent>().Count(e=>e.UseId==native.UseId&&e.ActionId==native.ActionId&&e.Damaged)==1&&Events<CardUseFinishedEvent>().Count(e=>e.ResolutionId==native.UseId&&e.CardId==duel)==1&&Events<MouJiangJiEffectCompletedEvent>().Any(e=>e.UseId==native.UseId&&e.Target==2)&&Events<MouJiangJiDrawReturnedEvent>().Length==4&&Events<MouJiangJiDrawReturnedEvent>().TakeLast(2).Select(e=>(e.Cursor,e.Recipient,e.Actual)).SequenceEqual([(0,0,2),(1,0,2)]),"The refreshed self gift keeps its real native actor/material and one whole Duel Use, repeats only the selected target, and actual child damage earns both separate native Draw2 returns.");
 }
 private sealed class Fixture:IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-mou-jiang-ji-anniversary",new(1,0,0));
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(b,Hero);foreach(var kind in new[]{CardKind.Crossbow,CardKind.DrawTwo,CardKind.IronChain,CardKind.Duel,CardKind.Dodge}){var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mjj-anniv-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
   var catalog=SkillProgramCatalog.Load($$"""
    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
    """,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静同行",description="正常摸牌后跳过出牌"}}}));
   b.AddSkill(new(Quiet,"安静同行","正常摸牌后跳过出牌"){Program=catalog.Programs[Quiet],ProgramPresentation=catalog.Presentations[Quiet]});b.AddSkill(new("fixture:mjj-anniv-giver-idle","真实给牌者","无主动规则"));b.AddGeneral(new(Giver,"真实给牌者","supporter","fixture:mjj-anniv-giver-idle","wei",20));foreach(var id in new[]{Enemy,Peer})b.AddGeneral(new(id,"真实同行","supporter",Quiet,"wei",20));
   b.AddDeck(new(Deck,"固定真实闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,90).Select(_=>new ContentDeckPhysicalCard("fixture:mjj-anniv-"+CardKind.Dodge,Suit.Club,2)).ToArray()});b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋蒋济十周年真实主流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Giver,Enemy,Peer],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
  }
 }
}
