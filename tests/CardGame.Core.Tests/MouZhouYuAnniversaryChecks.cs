using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouZhouYuAnniversaryChecks
{
 private const string Hero="mou:zhou-yu",Quiet="fixture:mzy-anniv-quiet",Ally="fixture:mzy-anniv-ally",Enemy="fixture:mzy-anniv-enemy",Actor="fixture:mzy-anniv-actor",Deck="fixture:mzy-anniv-deck";
 public static void AdditiveFireTargetsAndBothNativeYingmouBranchesCold()
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
  var setup=new ShanheBattleSetup(7,1,"mou-zhou-yu-anniversary-native",Hero,20,16,0,0,0,[],
   [new(CardKind.IronChain,Suit.Club,7),new(CardKind.IronChain,Suit.Spade,8),new(CardKind.FireSlash,Suit.Heart,9),new(CardKind.Slash,Suit.Club,3),new(CardKind.Slash,Suit.Club,4)],[],
   [new(Enemy,Hp:20){StartingHand=[new(CardKind.IronChain,Suit.Club,2),new(CardKind.IronChain,Suit.Club,3)]},new(Actor,MaxHpBonus:6,Hp:26){StartingHand=[new(CardKind.Slash,Suit.Club,9),new(CardKind.Duel,Suit.Club,10),new(CardKind.ArrowBarrage,Suit.Club,11),new(CardKind.FireAttack,Suit.Club,12),..Enumerable.Range(0,10).Select(_=>new ShanheStartingCard(CardKind.IronChain,Suit.Club,2))]}],
   [new(Ally,Hp:20){StartingHand=[]}],new string('d',64));
  var game=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=9},registry);
  PendingDecision? Prompt()=>Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
  T[] Events<T>()=>game.Events.Select(e=>e.Payload).OfType<T>().ToArray();
  PlayerSnapshot Player(int s)=>game.CreateSnapshot(0).Players[s];
  MouZhouYuReceipt? Receipt()=>game.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f=>f.MouZhouYuAnniversary is not null)?.MouZhouYuAnniversary;
  string Diagnostic()=>JsonSerializer.Serialize(new{game.State.TurnNumber,game.State.CurrentSeat,game.State.Phase,Prompt=Prompt(),Receipt=Receipt(),Players=game.CreateSnapshot(0).Players.Select(p=>new{p.Seat,p.GeneralId,p.Hp,p.HandCount}),Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=game.Events.TakeLast(30).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
  void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why+Diagnostic());}
  void Submit(GameCommand command){try{var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Native command rejected.");}catch(Exception e){throw new InvalidOperationException(e.Message+Diagnostic(),e);}}
  void Answer(Func<PromptChoice,bool> match){var p=Prompt();Need(p?.PlayerSeat==0,"Only the current actual Human choice is answered.");var c=p!.Choices.FirstOrDefault(match);Need(c is not null,"The exact native choice is published.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,game.Revision));}
  void Reach(Func<bool> done,bool nativeResponses=false,[CallerLineNumber]int line=0)
  {
   for(var i=0;i<160;i++)
   {
    if(done())return;Need(game.State.Winner==Winner.None,"Unexpected terminal result at "+line);
    if(Prompt()is{PlayerSeat:0}p)
    {
     if(nativeResponses&&p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash){Answer(c=>c.Parameters.GetValueOrDefault("response")=="take-damage");continue;}
     throw new InvalidOperationException("Unexpected actual Human boundary at "+line+Diagnostic());
    }
    Submit(new AdvanceOneStepCommand(game.Revision));
   }
   throw new InvalidOperationException("Bounded native Zhou Yu anniversary flow not reached at "+line+Diagnostic());
  }
  void Play()=>Reach(()=>Prompt()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
  void Use(CardKind kind,IReadOnlyList<int> targets)
  {var id=Player(0).Hand.First(c=>c.Kind==kind).Id;var a=game.GetHumanLegalActions().FirstOrDefault(a=>a.CardId==id&&a.ConversionSource is null&&a.Kind!=LegalActionKind.Recast&&a.TargetSeats.SequenceEqual(targets));Need(a is not null,"The genuine physical card has the requested canonical native targets.");Submit(new PlayCardCommand(0,id,a!.TargetSeats,game.Revision,Prompt()!.PromptId,a.PlayedCardKind,a.TargetCardId));}
  void Activate()=>Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
  Submit(new StartGameCommand());Reach(()=>Receipt()?.Stage==MouZhouYuStage.Initial&&Prompt()?.PlayerSeat==0);Answer(c=>c.Parameters.GetValueOrDefault("branch")=="yang");Play();
  Need(Player(0).GeneralId==Hero&&Player(0).MaxHp==20&&Player(3).GeneralId==Actor&&Player(3).Hp==26&&Player(3).MaxHp==26&&Player(3).HandCount==14,"The actual roster and fixed mixed Hand entities are installed before gameplay.");
  Use(CardKind.IronChain,[3]);Reach(()=>Prompt()?.SkillPrompt?.SkillId=="mou:yingmou");Activate();Reach(()=>Receipt()?.Stage==MouZhouYuStage.Target&&Prompt()?.PlayerSeat==0);Answer(c=>c.Targets.SequenceEqual([3]));
  Reach(()=>Receipt()is{Stage:MouZhouYuStage.OfferUse,Yin:false}&&Prompt()?.PlayerSeat==0);
  Need(Events<MouZhouYuDrawReturnedEvent>()is[{Requested:5,Drawn:5,Replacement:0}]&&Receipt()is{Native:null},"Yang really draws the bounded difference through native Draw and its child return before offering FireAttack.");
  Answer(c=>c.Parameters.GetValueOrDefault("branch")=="fire"&&c.Targets.SequenceEqual([3]));
  Reach(()=>Prompt()is{PlayerSeat:0,Kind:DecisionKind.FireAttackDiscard});
  Need(game.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.MouZhouYuYingmou is{CardId:0,Kind:CardKind.FireAttack}&&u.PhysicalCardIds.Count==0&&u.Action is{Type:CardActionType.Use,PhysicalCards.Count:0}),"Yang enters genuine zero-material FireAttack with the original actual actor/provider and target, rather than direct damage.");
  Answer(c=>c.Cards.Count==1);Play();
  Need(Player(3).Hp==23&&Events<DamageAppliedEvent>().Last(e=>e.TargetSeat==3).Amount==3&&Events<MouZhouYuUseReturnedEvent>().Length==1&&Events<MouZhouYuFireGrantedEvent>().Length==1&&Events<MouZhouYuTargetDamagedEvent>().Any(e=>e.Target==3),"The real same-suit payment returns FireAttack once; Ronghuo adds the two current factions to one damage, and no undamaged fallback is added.");
  Use(CardKind.FireSlash,[1,3]);Play();
  Need(Player(1).Hp==17&&Player(3).Hp==20&&Events<MouZhouYuFireConsumedEvent>().Length==1&&Events<MouZhouYuFireGrantedEvent>().Length==3&&Events<MouZhouYuActivatedEvent>().Length==1,"The next actual FireSlash consumes the original fire grant for one extra legal target; each genuine new fire hit grants a later allowance, while Yingmou remains once per actual turn.");
  var firstTurn=game.State.TurnNumber;Submit(new EndPlayPhaseCommand(0,game.Revision,Prompt()!.PromptId));Play();
  Need(game.State.TurnNumber>firstTurn&&Events<TurnEndedEvent>().Any(e=>e.ActorSeat==0&&e.TurnNumber==firstTurn),"The second conversion branch begins in another genuine actual turn.");
  Use(CardKind.IronChain,[2]);Reach(()=>Prompt()?.SkillPrompt?.SkillId=="mou:yingmou");Activate();Reach(()=>Receipt()?.Stage==MouZhouYuStage.Target&&Prompt()?.PlayerSeat==0);Answer(c=>c.Targets.SequenceEqual([2]));
  Reach(()=>Receipt()?.Stage==MouZhouYuStage.MaxHandActor&&Prompt()?.PlayerSeat==0);Need(Prompt()!.Choices.SelectMany(c=>c.Targets).SequenceEqual([3]),"Yin uses the actual global maximum Hand count after the completed native Use.");Answer(c=>c.Targets.SequenceEqual([3]));
  Reach(()=>Receipt()is{Stage:MouZhouYuStage.OfferUse,Yin:true,Actor:3}&&Prompt()?.PlayerSeat==3);
  var frozen=Receipt()!.Frozen.ToArray();Need(frozen.Length==4&&Receipt()!.Remaining.SequenceEqual(frozen)&&Prompt()!.Choices.Any(c=>c.Cards.Count==1),"All actual Slash and damage-Trick Hand entities are frozen on the owning frame and offered to their real holder.");
  var ownerView=SnapshotJson.Serialize(game.CreateSnapshot(0));var actorView=SnapshotJson.Serialize(game.CreateSnapshot(3));
  game=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
  Need(ownerView==SnapshotJson.Serialize(game.CreateSnapshot(0))&&actorView==SnapshotJson.Serialize(game.CreateSnapshot(3))&&Receipt()is{Stage:MouZhouYuStage.OfferUse,Actor:3}&&Receipt()!.Frozen.SequenceEqual(frozen),"One representative Cold preserves the private foreign actor's frozen physical-card request and exact completed-Use parent before any card is used twice.");
  Reach(()=>Prompt()is{PlayerSeat:0,Kind:DecisionKind.PlayCard},nativeResponses:true);
  var issued=Events<MouZhouYuUseIssuedEvent>().Where(e=>e.Actor==3).ToArray();
  Need(issued.Length==4&&issued.Select(e=>e.CardId).Order().SequenceEqual(frozen.Order())&&issued.All(e=>Events<MouZhouYuUseReturnedEvent>().Count(r=>r.UseId==e.UseId&&r.ActionId==e.ActionId)==1)&&Events<MouZhouYuActivatedEvent>().Length==2,
   "Yin uses each frozen genuine physical Slash, Duel, ArrowBarrage and FireAttack exactly once through its own native child/typed return, preserving the actual holder as actor/provider.");
  var group=Events<GroupCardUsedEvent>().Single(e=>e.SourceSeat==3&&e.CardKind==CardKind.ArrowBarrage);
  var aoeIssued=issued.Single(e=>e.UseId==group.ResolutionId&&e.CardId==group.CardId);
  var aoe=Events<CardUseAppearanceCapturedEvent>().Select(e=>e.Action).Single(a=>a.ActionId==aoeIssued.ActionId);
  Need(aoe.Type==CardActionType.Use&&aoe.ActorSeat==3&&aoe.ProviderSeat==3&&aoe.EffectiveKind==CardKind.ArrowBarrage&&group.TargetSeats.SequenceEqual([0,1,2])&&aoe.TargetSeats.SequenceEqual(group.TargetSeats)&&aoe.PhysicalCards is[var material]&&material.CardId==aoeIssued.CardId&&material.From==CardLocation.Hand(3)&&frozen.Contains(material.CardId)&&game.CardMovements.Count(m=>m.CardId==material.CardId&&m.From==CardLocation.Hand(3)&&m.To==CardLocation.Processing)==1&&Events<CardUseFinishedEvent>().Count(e=>e.ResolutionId==group.ResolutionId&&e.CardId==group.CardId&&e.CardKind==CardKind.ArrowBarrage)==1&&Events<MouZhouYuTargetDamagedEvent>().Any(e=>e.Target==2)&&Events<DamageAppliedEvent>().Any(e=>e.SourceSeat==3&&e.TargetSeat==2&&e.Amount>0)&&Events<DamageAppliedEvent>().All(e=>e.SourceSeat!=0||e.TargetSeat!=2)&&Events<MouZhouYuCompletedEvent>().Length==3,
   "The damage AOE keeps all native other-seat targets including the original Yingmou target; real child-chain damage suppresses the final owner fire damage and the paid sequence completes after restoration.");
 }
 private sealed class Fixture:IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-mou-zhou-yu-anniversary",new(1,0,0));
  public void Register(IContentRegistryBuilder builder)
  {
   CharacterFixtureContent.Register(builder,Hero);
   foreach(var kind in new[]{CardKind.IronChain,CardKind.FireSlash,CardKind.Slash,CardKind.Dodge,CardKind.Duel,CardKind.ArrowBarrage,CardKind.FireAttack}){var c=CardCatalog.Get(kind);builder.AddCard(new("fixture:mzy-anniv-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
   var catalog=SkillProgramCatalog.Load($$"""
    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
    """,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Quiet]=new{name="安静同行",description="正常摸牌后跳过出牌"}}}));
   builder.AddSkill(new(Quiet,"安静同行","正常摸牌后跳过出牌"){Program=catalog.Programs[Quiet],ProgramPresentation=catalog.Presentations[Quiet]});
   foreach(var id in new[]{Ally,Enemy,Actor})builder.AddGeneral(new(id,"真实同行","supporter",Quiet,"wei",20));
   builder.AddDeck(new(Deck,"固定真实闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,90).Select(_=>new ContentDeckPhysicalCard("fixture:mzy-anniv-"+CardKind.Dodge,Suit.Club,2)).ToArray()});
   builder.AddMode(new(ShanheBattleSetup.BattleModeId,"谋周瑜十周年真实主流程",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Ally,Enemy,Actor],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
  }
 }
}
