using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryHanSongChecks
{
 private const string Hero="anniversary:han-song",Skill="anniversary:shuaiyan",Driver="fixture:ann-han-song-driver",Watch="fixture:ann-han-song-watch",Quiet="fixture:ann-han-song-quiet",Deck="fixture:ann-han-song-deck";
 public static void RealPhaseBenefitsDynamicYinbiNativeDiscardCold()
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());var options=Options();var g=GameEngine.CreateStandard(options,registry);Accept(g,new StartGameCommand());Offer(g);Activate(g);Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
  var root=Root(g)!;var first=root.Id;
  Need(root.AnniversaryHanSong is{Stage:AnniversaryHanSongStage.DrawChildren,SubjectSeat:0,DrawCount:2,Draw:{RequestedCount:2,ActualDrawIds.Count:2}}&&
   root.WindowContext?.Window==SkillProgramTriggerWindow.DrawPhaseEnded&&V(g).Hp==3&&V(g).MaxHp==3&&V(g).HandCount==9&&V(g,1).HandCount==7,
   "The real own Draw phase ends at seven matching one other hand; X includes the owner and freezes two real native draws before their paid gain child pauses."+Detail(g));
  g=Cold(g,registry);Continue(g);Play(g);
  Need(V(g).HandLimit==8&&V(g).Equipment.Count==0&&E<AnniversaryHanSongDrawReturnedEvent>(g).Count(e=>e.FrameId==first&&e.Count==2)==1,
   "Yinbi copies the actual highest native hand limit without a fake equipment or duplicate paid draw."+Detail(g));
  var slashes=V(g).Hand.Where(c=>c.Kind==CardKind.Slash).Select(c=>c.Id).ToArray();var wines=V(g).Hand.Where(c=>c.Kind==CardKind.Alcohol).Select(c=>c.Id).ToArray();
  Need(slashes.Length==3&&wines.Length==2,"The fixed physical recipe supplies the exact three Slashes and two Wines.");
  Native(g,Find(g,slashes[0],[2]));Play(g);Native(g,Find(g,slashes[1],[2]));Play(g);
  Need(V(g).HandCount==7&&V(g,1).HandCount==7&&V(g).HandLimit==7&&V(g,2).Hp==6&&
   !g.GetHumanLegalActions().Any(a=>a.CardId==slashes[2]),
   "Two real range-two Slashes work while the hand is unique; payment then matches another hand, so Yinbi immediately restores the normal exhausted Slash quota and copies the changed actual highest HP-based limit."+Detail(g));
  Accept(g,new UseProgramSkillCommand(0,Driver,"draw-three",[],[],g.Revision,P(g)!.PromptId));Play(g);
  Native(g,Find(g,wines[0],null));Play(g);var hp=V(g,2).Hp;
  Native(g,Find(g,slashes[2],[2]));Play(g);
  Need(!V(g).HasAlcoholEffect&&V(g,2).Hp==hp-2,"The actual third Slash consumes native Wine and deals two, rather than changing a counter or fabricating damage."+Detail(g));
  Need(g.GetHumanLegalActions().Any(a=>a.CardId==wines[1]),"A newly unique hand also lifts the real already-used Wine count restriction."+Detail(g));
  Native(g,Find(g,wines[1],null));Play(g);
  Need(E<AlcoholAppliedEvent>(g).Count(e=>e.SourceSeat==0)==2&&V(g).HasAlcoholEffect&&V(g).HandCount==7,
   "Two distinct physical Wines are really used in the same actual turn, with an intervening real Slash consumption."+Detail(g));
  Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Offer(g);Activate(g);
  Offer(g);
  var ownDiscard=E<AnniversaryHanSongStartedEvent>(g).Single(e=>e.SubjectSeat==0&&e.FrameId!=first);
  Need(V(g).HandCount==9&&E<AnniversaryHanSongDrawReturnedEvent>(g).Count(e=>e.FrameId==ownDiscard.FrameId&&e.Count==2)==1&&
   E<ProgramBindingStartedEvent>(g).Any(e=>e.FrameId==ownDiscard.FrameId&&e.BindingId=="own-discard-ended")&&
   g.State.TurnNumber!=E<AnniversaryHanSongStartedEvent>(g).First().ActualTurn&&V(g,1).HandCount==9,
   "The separate own Discard completion draws two after normal discarding; the next real other Draw completion reaches the same hand count and issues its own optional opportunity."+Detail(g));
  Activate(g);Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("verb")=="discard"));Answer(g,c=>c.Parameters.GetValueOrDefault("verb")=="discard");
  Reach(g,p=>p.Choices.Any(c=>c.Parameters.GetValueOrDefault("verb")=="card"));
  Need(P(g)!.Choices.All(c=>c.Cards.Count==0&&c.Parameters.GetValueOrDefault("card-owner-seat")=="1"),"The real foreign-hand discard menu exposes only the other holder's opaque hand positions.");
  var selected=V(g,1).Hand[0].Id;var before=V(g,1).HandCount;var foreign=Root(g)!.Id;
  Need(Root(g)!.AnniversaryHanSong is{Stage:AnniversaryHanSongStage.CardChoice,SubjectSeat:1,DrawCount:1},"The original other Draw subject owns the actual opaque-card choice.");
  g=Cold(g,registry);
  Answer(g,c=>c.Parameters.GetValueOrDefault("source-zone")=="Hand"&&c.Parameters.GetValueOrDefault("slot-index")=="0");FinishRoot(g,foreign);
  Need(V(g,1).HandCount==before-1&&g.CardMovements.Count(m=>m.CardId==selected&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.DiscardPile&&m.Reason.Value=="anniversary.shuaiyan.discard")==1&&
   g.CardMovements.All(m=>m.CardId!=selected||m.To!=CardLocation.Processing)&&
   E<AnniversaryHanSongCompletedEvent>(g).Count(e=>e.FrameId==foreign)==1&&E<AnniversaryHanSongDiscardPaidEvent>(g).Count(e=>e.FrameId==foreign)==1&&
   V(g).HandCount==9&&V(g,1).HandCount==8,
   "The restored opaque slot pays one real logical discard, and its original Program completes exactly once without a fake Processing move or repeating the native benefit."+Detail(g));
  Need(State(g)==State(GameReplay.Replay(options,g.AcceptedCommands,registry)),
   "The accepted journal replays dynamic privileges, both own phase benefits, the paid native draw child and original foreign-hand choice/payment exactly."+Detail(g));
 }
 private static GameOptions Options()
 {
  var setup=new ShanheBattleSetup(7,1,"ann-han-song",Hero,3,0,0,0,0,[Driver,Watch],
   [new(CardKind.Slash,Suit.Spade,7),new(CardKind.Slash,Suit.Club,8),new(CardKind.Slash,Suit.Heart,9),new(CardKind.Alcohol,Suit.Spade,3),new(CardKind.Alcohol,Suit.Club,9)],[],
   [new(Peer(2)){StartingHand=[]},new(Peer(3)){StartingHand=[]}],
   [new(Peer(1)){StartingHand=Enumerable.Range(0,7).Select(_=>new ShanheStartingCard(CardKind.Dodge,Suit.Heart,2)).ToArray()}],new string('a',64));
  return new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,
   UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=6};
 }
 private static string Peer(int s)=>"fixture:ann-han-song-peer-"+s;
 private static PlayerSnapshot V(GameEngine g,int s=0)=>g.CreateSnapshot(s).Players.Single(p=>p.Seat==s);
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static ProgramSkillFrame? Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.AnniversaryHanSong is not null);
 private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
 private static void Need(bool b,string text){if(!b)throw new InvalidOperationException(text);}
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Rejected native Han Song command.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> pick){var p=P(g)!;var c=p.Choices.FirstOrDefault(pick);Need(c is not null,"Missing Han Song choice: "+JsonSerializer.Serialize(p));Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,g.Revision));}
 private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
 private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
 private static void Offer(GameEngine g)=>Reach(g,p=>p.SkillPrompt?.SkillId==Skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
 private static void Play(GameEngine g)=>Reach(g,p=>p is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
 private static LegalAction Find(GameEngine g,int id,int[]? targets)=>g.GetHumanLegalActions().FirstOrDefault(a=>a.CardId==id&&(targets is null||a.TargetSeats.SequenceEqual(targets)))??throw new InvalidOperationException("Missing exact real Han Song Use "+id+Detail(g));
 private static void Native(GameEngine g,LegalAction a)=>Accept(g,new PlayCardCommand(0,a.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource});
 private static void Reach(GameEngine g,Func<PendingDecision,bool> ready)
 {
  for(var i=0;i<130;i++)
  {
   if(P(g)is{} p){if(ready(p))return;if(p.SkillPrompt?.SkillId==Watch){Continue(g);continue;}if(p.Kind is DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash){Answer(g,c=>c.Cards.Count==0);continue;}if(p.PlayerSeat==0)throw new InvalidOperationException("Unexpected Han Song boundary: "+JsonSerializer.Serialize(p)+Detail(g));}
   Need(g.State.Winner==Winner.None,"The short Han Song scene retains its native boundary."+Detail(g));Accept(g,new AdvanceOneStepCommand(g.Revision));
  }
  throw new InvalidOperationException("Han Song missed its bounded native boundary."+Detail(g));
 }
 private static void FinishRoot(GameEngine g,long id)
 {for(var i=0;i<25&&g.ResolutionStack.Any(f=>f.Id==id);i++){if(P(g)?.SkillPrompt?.SkillId==Watch)Continue(g);else{Need(P(g)?.PlayerSeat!=0,"Unexpected unpaid Han Song menu."+Detail(g));Accept(g,new AdvanceOneStepCommand(g.Revision));}}Need(g.ResolutionStack.All(f=>f.Id!=id),"The original Han Song frame must finish once.");}
 private static string Detail(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).HandLimit,V(g,s).HasAlcoholEffect}),Root=Root(g)?.AnniversaryHanSong,Started=E<AnniversaryHanSongStartedEvent>(g),Draw=E<AnniversaryHanSongDrawReturnedEvent>(g),Paid=E<AnniversaryHanSongDiscardPaidEvent>(g),Damage=E<DamageAppliedEvent>(g)});
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static GameEngine Cold(GameEngine g,ContentRegistry registry){var copy=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(copy),"The actual original phase, fixed X, native draw and paid child cold-restore identically.");return copy;}
 private sealed class Fixture:IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-anniversary-han-song",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(b,Hero);
   var rules=$$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
    {"id":"{{Driver}}","revision":1,"activations":[{"id":"draw-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]}]},
    {"id":"{{Watch}}","revision":1,"triggers":[
     {"id":"native-draw-paid","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["anniversary.shuaiyan.draw"],"usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-paid","options":[{"id":"continue"}]}]}]},
    {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
   ]}
   """;
   var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{
    [Driver]=new{name="真实补牌",description="原生摸三张牌改变当前手牌数量。"},[Watch]=new{name="真实已付子窗",description="原生摸牌或逻辑弃牌之后暂停。",optionLabels=new Dictionary<string,string>{["continue"]="继续"}},[Quiet]=new{name="安静同桌",description="保留真实摸牌和弃牌，只跳过普通出牌。"}}}));
   foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
   for(var s=1;s<4;s++)b.AddGeneral(new(Peer(s),"固定同桌","",Quiet,"wei",s==1?7:s==2?8:4));
   b.AddDeck(new(Deck,"固定小闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,32).Select(_=>new ContentDeckPhysicalCard("standard:dodge",Suit.Heart,2)).ToArray()});
   b.AddMode(new(ShanheBattleSetup.BattleModeId,"韩嵩真实阶段结束与实时隐避",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer(1),Peer(2),Peer(3)],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
  }
 }
}
