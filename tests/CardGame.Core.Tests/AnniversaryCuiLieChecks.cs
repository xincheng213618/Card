using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryCuiLieChecks
{
 private const string Hero="anniversary:cui-lie",Zijue="anniversary:zijue",Chibi="anniversary:chibi",Peer="fixture:cui-lie-peer-",NoDraw="fixture:cui-lie-no-draw",Watch="fixture:cui-lie-paid-draw",Deck="fixture:cui-lie-deck",Mode="identity:classic-cui-lie";
 public static void RecipientHeGiftIssuedLeasePindianRewardAndWholeMultiTargetCancellationCold()
 {
  foreach(var kind in new[]{CardKind.IronChain,CardKind.Indulgence})
  {
  var s=Scene.Start(true,kind);s.Play(0);s.NativeKind(0,CardKind.Crossbow);s.Play(0);
  var equipment=s.V(0).Equipment.Single(c=>c.Kind==CardKind.Crossbow).Id;var hand=s.V(0).Hand.Single(c=>c.Kind==CardKind.Peach).Id;var hp=s.V(0).Hp;
  s.Zijue(3);s.Menu(AnniversaryCuiLieStage.Number);s.Need(s.P!.PlayerSeat==3,"The selected foreign recipient really declares the number.");s.Answer(c=>Value(c)=="2");s.Menu(AnniversaryCuiLieStage.Payment);
  s.Answer(c=>c.Cards.SequenceEqual([equipment]));s.Menu(AnniversaryCuiLieStage.Payment);s.Answer(c=>c.Cards.SequenceEqual([hand]));s.Play(0);
  var lease=s.E<AnniversaryCuiLieLeaseEvent>().Single();s.Need(lease.Amount==2&&s.V(0).Hp==hp+1&&s.Zone(equipment)==CardLocation.Hand(3)&&s.Zone(hand)==CardLocation.Hand(3)&&s.G.GetCombatDistance(1,0)==3&&!s.G.GetLegalActions(0).Any(a=>a.ProgramSkillId==Zijue),"Two real selected HE materials transfer, real recovery occurs, and the paid incoming-distance lease starts after the once-per-phase activation.");
  s.End(0);s.Play(1);var card=s.V(1).Hand.First(c=>c.Kind==kind).Id;int[] original=kind==CardKind.IronChain?[2,3]:[3];s.Native(1,card,original);s.Offer();s.Activate();s.Pindian(0,12);s.Pindian(1,2);
  s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch||s.E<AnniversaryCuiLieUseChangedEvent>().Length>0);s.Need(s.P?.SkillPrompt?.SkillId==Watch,"The Pindian reward must reach its actual paid native-draw observer before the original card finishes.");var reward=s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.AnniversaryCuiLie?.Operation==SkillProgramEffectOp.ResolveAnniversaryCuiLieZijuePindianDraw);
  s.Need(reward.AnniversaryCuiLie!.RewardLeases.SequenceEqual([lease.FrameId])&&s.E<AnniversaryCuiLieDrawReturnedEvent>().Any(e=>e.FrameId==reward.Id&&e.Requested==1&&e.Actual==1)&&s.G.ResolutionStack.OfType<PindianRevealWindowFrame>().Count()==1,"The real paid-lease draw occurs at native Pindian reveal before comparison, using the original issuing frame rather than a win-only reward.");
  if(kind==CardKind.IronChain)s.Cold();s.Continue();s.Play(1);
  var mutation=s.E<AnniversaryCuiLieUseChangedEvent>().Single();var contest=s.E<AnniversaryCuiLiePindianReturnedEvent>().Single();
  s.Need(contest is{SourceRank:13,OpponentRank:2,Won:true}&&mutation.Cancelled&&mutation.BeforeTargets.SequenceEqual(original)&&mutation.AfterTargets.Count==0&&s.Zone(card)==CardLocation.Hand(0)&&!s.V(2).IsChained&&!s.V(3).IsChained&&s.E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==mutation.UseId)==1&&s.E<AnniversaryCuiLieMaterialClaimedEvent>().Count(e=>e.UseId==mutation.UseId&&e.Count==1)==1,"The actual rank is capped at thirteen; winning cancels every original native target and obtains the original processing entity once, including the real delayed-placement bridge.");
  s.End(1);s.Play(0);s.Need(s.G.GetCombatDistance(1,0)==1&&s.E<TurnStartedEvent>().Any(e=>e.ActorSeat==0&&e.TurnNumber>lease.Turn),"The issued distance and Pindian lease expires at Cui Lie's actual next turn start.");s.Replay();
  }
 }
 public static void DeclineGiftNativeDrawAndLostContestExtraTargetTurnDisable()
 {
  foreach(var kind in new[]{CardKind.IronChain,CardKind.Indulgence})
  {
  var s=Scene.Start(false,kind);s.Play(0);var count=s.V(0).HandCount;s.Zijue(2);s.Menu(AnniversaryCuiLieStage.Number);s.Answer(c=>Value(c)=="4");s.Menu(AnniversaryCuiLieStage.Payment);s.Answer(c=>c.Parameters.GetValueOrDefault("verb")=="draw");s.Play(0);
  s.Need(s.V(0).HandCount==count+4&&s.E<AnniversaryCuiLieDrawReturnedEvent>() is[{Requested:4,Actual:4}]&&s.E<AnniversaryCuiLieLeaseEvent>().Length==0&&s.G.GetCombatDistance(1,0)==1,"Declining the exact gift really draws the other recipient's declared four, without recovery or a paid lease.");
  s.End(0);s.Play(1);var card=s.V(1).Hand.First(c=>c.Kind==kind).Id;int[] original=kind==CardKind.IronChain?[2,3]:[3];s.Native(1,card,original);s.Offer();s.Activate();s.Pindian(0,2);s.Pindian(1,13);s.Play(1);
  var changed=s.E<AnniversaryCuiLieUseChangedEvent>().Single();s.Need(!changed.Cancelled&&changed.BeforeTargets.SequenceEqual(original)&&changed.AfterTargets.SequenceEqual(original.Append(0))&&s.E<AnniversaryCuiLieDisabledEvent>() is[{Turn:2}]&&s.E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==changed.UseId)==1,"A truly lost native contest retains every old target, really appends Cui Lie, and disables Chibi for the actual turn.");
  if(kind==CardKind.IronChain)s.Need(new[]{0,2,3}.All(i=>s.V(i).IsChained)&&s.Zone(card)==CardLocation.DiscardPile,"The added native Iron Chain target and both original targets all resolve.");
  else s.Need(s.Zone(card)==CardLocation.Judgment(3)&&s.E<DelayedCardPlacedEvent>().Count(e=>e.ResolutionId==changed.UseId&&e.CardId==card&&e.TargetSeat==3)==1&&s.E<CardEffectSkippedEvent>().Count(e=>e.ResolutionId==changed.UseId&&e.TargetSeat==0&&e.Reason==CardEffectSkipReason.PublicTargetMissing)==1,"The single real delayed entity is placed at its first native target; the actual added target is precisely skipped because that entity has already been placed, without cloning.");
  var bindings=s.E<ProgramBindingStartedEvent>().Count(e=>e.SkillId==Chibi);s.NativeKind(1,CardKind.IronChain,[2,3]);s.Play(1);
  var expected=kind==CardKind.Indulgence;s.Need(s.E<ProgramBindingStartedEvent>().Count(e=>e.SkillId==Chibi)==bindings&&s.E<AnniversaryCuiLiePindianIssuedEvent>().Length==1&&s.V(2).IsChained==expected&&s.V(3).IsChained==expected&&s.V(0).IsChained==!expected,"The second distant native card in the same actual turn issues no Chibi binding or contest, and both original Iron Chain effects still flip exactly once.");s.Replay();
  }
 }
 private static string? Value(PromptChoice c)=>c.Parameters.GetValueOrDefault("value");
 private sealed class Scene(GameEngine game,ContentRegistry registry)
 {
  public GameEngine G{get;private set;}=game;
  public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
  public ProgramSkillFrame? Root=>G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.AnniversaryCuiLie is not null);
  public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
  public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
  public CardLocation Zone(int id)=>G.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location;
  public void Play(int seat)=>Reach(()=>P is{Kind:DecisionKind.PlayCard}p&&p.PlayerSeat==seat);
  public void End(int seat)=>Accept(new EndPlayPhaseCommand(seat,G.Revision,P!.PromptId));
  public void Zijue(int target)=>Accept(new UseProgramSkillCommand(0,AnniversaryCuiLieChecks.Zijue,"declare-number",[],[target],G.Revision,P!.PromptId));
  public void Offer()=>Reach(()=>P?.SkillPrompt?.SkillId==Chibi&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
  public void Activate()=>Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
  public void Menu(AnniversaryCuiLieStage stage)=>Reach(()=>Root?.AnniversaryCuiLie?.Stage==stage&&P?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="anniversary-cui-lie")==true);
  public void Continue()=>Answer(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
  public void Pindian(int actor,int rank){Reach(()=>P is{Kind:DecisionKind.SkillModule,SkillPrompt.SkillId:Chibi}p&&p.PlayerSeat==actor);var card=V(actor).Hand.First(c=>c.Rank==rank).Id;Answer(c=>c.Cards.SequenceEqual([card]));}
  public void NativeKind(int actor,CardKind kind,IReadOnlyList<int>? targets=null)=>Native(actor,V(actor).Hand.First(c=>c.Kind==kind).Id,targets);
  public void Native(int actor,int id,IReadOnlyList<int>? targets=null){var a=G.GetLegalActions(actor).First(a=>a.CardId==id&&a.ConversionSource is null&&(targets is null||a.TargetSeats.SequenceEqual(targets)));Accept(new PlayCardCommand(actor,id,a.TargetSeats,G.Revision,P!.PromptId,a.PlayedCardKind,a.TargetCardId));}
  public void Answer(Func<PromptChoice,bool> predicate){var p=P!;var c=p.Choices.FirstOrDefault(predicate);Need(c is not null,"The actual human receives the intended original private or native choice.");Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,G.Revision));}
  public void Reach(Func<bool> ready)
  {
   for(var step=0;step<500;step++)
   {
    if(ready())return;
    if(P is{}p)
    {
     if(p.Kind==DecisionKind.PlayCard){End(p.PlayerSeat);continue;}
     if(p.Kind==DecisionKind.DiscardCards){var ids=p.ValidCardIds.OrderBy(id=>V(p.PlayerSeat).Hand.Single(c=>c.Id==id).Rank==2?1:0).Take(p.RequiredCardCount).ToArray();Accept(new DiscardCardsCommand(p.PlayerSeat,ids,p.PromptId,G.Revision));continue;}
     if(p.Kind==DecisionKind.SelectFaction){Answer(_=>true);continue;}
     if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying){Answer(c=>c.Cards.Count==0);continue;}
     if(p.SkillPrompt?.SkillId==Watch){Continue();continue;}
     if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")){Answer(c=>c.Parameters.GetValueOrDefault("program-action")!="activate");continue;}
     Need(false,"Unexpected Cui Lie native boundary.");
    }
    Need(G.State.Winner==Winner.None,"The short native table retains its living participants.");Accept(new AdvanceOneStepCommand(G.Revision));
   }
   Need(false,"The bounded actual scene missed its requested original continuation.");
  }
  private string State()=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=G.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),G.CardMovements,Zones=G.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(G.AcceptedCommands)});
  public void Cold(){var before=State();G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==State(),"Cold preserves the paid original lease reward, native Pindian pair and suspended original card Use exactly.");}
  public void Replay(){var before=State();G=GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry);Need(before==State(),"The journal replays the actual foreign declaration, real transfers and complete native contest effects.");}
  public void Need(bool okay,string message){if(!okay)throw new InvalidOperationException(message+" "+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Receipt=Root?.AnniversaryCuiLie,CuiLie=G.Events.Select(e=>e.Payload).Where(e=>e.GetType().Name.StartsWith("AnniversaryCuiLie",StringComparison.Ordinal)).Select(e=>JsonSerializer.Serialize(e,e.GetType())).ToArray(),Last=G.Events.TakeLast(7).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()}));}
  private void Accept(GameCommand c){var r=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected actual Cui Lie command.");}
  public static Scene Start(bool win,CardKind first=CardKind.IronChain)
  {
   var stock=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray();var hands=Hands(win,first);var probe=Boot(ContentRegistry.Build(new StandardContentPackage(),new Fixture(win,stock,first)));
   for(var seat=0;seat<4;seat++){var ids=probe.V(seat).Hand.Select(c=>c.Id).ToArray();probe.Need(ids.Length==hands[seat].Length,"The native fixed-seed deal retains its sparse hand sizes.");for(var i=0;i<ids.Length;i++)stock[ids[i]-1]=new(registryCard(probe.registry,hands[seat][i].Kind),hands[seat][i].Suit,hands[seat][i].Rank);}
   return Boot(ContentRegistry.Build(new StandardContentPackage(),new Fixture(win,stock,first)));
  }
  private readonly ContentRegistry registry=registry;
  private static string registryCard(ContentRegistry registry,CardKind kind)=>registry.Cards.Values.First(c=>c.LegacyKind==kind).Id;
  private static Scene Boot(ContentRegistry registry)
  {
   var g=GameEngine.CreateStandard(new GameOptions{Seed=47,PlayerCount=4,HumanSeat=0,HumanSeats=[0,1,2,3],HumanRole=Role.Lord,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=12},registry);var s=new Scene(g,registry);s.Accept(new StartGameCommand());
   for(var n=0;n<30;n++){if(s.P is{Kind:DecisionKind.PlayCard,PlayerSeat:0})return s;if(s.P is{Kind:DecisionKind.SelectGeneral}p){s.Accept(new SelectGeneralCommand(p.PlayerSeat,p.PlayerSeat==0?Hero:Peer+p.PlayerSeat,g.Revision,p.PromptId));continue;}if(s.P is{Kind:DecisionKind.SelectFaction}){s.Answer(_=>true);continue;}s.Need(s.P is null,"The native identity opening retains its actual setup and deal boundaries.");s.Accept(new AdvanceOneStepCommand(g.Revision));}
   throw new InvalidOperationException("The fixed identity opening missed its first actual Play phase.");
  }
 }
 private static ShanheStartingCard[][] Hands(bool win,CardKind first)=>[
  [new(CardKind.Crossbow,Suit.Spade,1),new(CardKind.Peach,Suit.Heart,4),new(CardKind.Slash,Suit.Spade,win?12:2),new(CardKind.Dodge,Suit.Club,6)],
  [new(first,Suit.Heart,7),new(CardKind.Dodge,Suit.Club,win?2:13),new(CardKind.IronChain,Suit.Club,8)],
  [new(CardKind.Peach,Suit.Heart,3)], [new(CardKind.Peach,Suit.Heart,3)] ];
 private static string HandSkill(int seat)=>"fixture:cui-lie-hand-"+seat;
 private sealed class Fixture(bool win,IReadOnlyList<ContentDeckPhysicalCard> stock,CardKind first):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-anniversary-cui-lie",new(1,0,0));
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(new OwnerBuilder(b),Hero);var rules=JsonNode.Parse($$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
    {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-normal-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
    {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-native-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.anniversary:zijue.draw"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hold","options":[{"id":"continue"}]}]}]}
   ]}
   """)!;
   var names=new Dictionary<string,object>{[NoDraw]=new{name="固定真实手牌",description="保留原生阶段，正常摸牌调整为零。"},[Watch]=new{name="已付原生摸牌暂停",description="在原帧真实子窗暂停。",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}};
   for(var seat=0;seat<4;seat++){rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{HandSkill(seat)}}","revision":1,"modifiers":[{"id":"sparse-opening","query":"initialHandSize","operation":"add","value":{{Hands(win,first)[seat].Length}},"priority":0}]}"""));names[HandSkill(seat)]=new{name="固定稀疏发牌",description="使用原生实体洗牌与发牌。"};}
   var catalog=SkillProgramCatalog.Load(rules.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=names}));foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
   for(var seat=1;seat<4;seat++)b.AddGeneral(new(Peer+seat,"固定真实同桌","",NoDraw,"wei",12,[HandSkill(seat)]));
   b.AddDeck(new(Deck,"固定实际实体库存",0,2,[]){PhysicalCards=stock});b.AddMode(new(Mode,"崔烈实际支付与原生拼点",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},Deck,GeneralCandidateCount:4,GeneralPoolIds:[Hero,Peer+1,Peer+2,Peer+3]));
  }
 }
 private sealed class OwnerBuilder(IContentRegistryBuilder b):IContentRegistryBuilder
 {
  public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d.Id==Hero?d with{InitialHp=2,AdditionalSkillIds=[..d.AdditionalSkillIds??[],NoDraw,Watch,HandSkill(0)]}:d);
  public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d);public void AddCard(ContentCardDefinition d)=>b.AddCard(d);public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
 }
}
