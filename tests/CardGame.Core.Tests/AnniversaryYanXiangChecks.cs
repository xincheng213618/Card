using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryYanXiangChecks
{
 private const string Hero="anniversary:yan-xiang",Yuzheng="anniversary:yuzheng",Suishi="anniversary:suishi",Peer="fixture:yan-xiang-peer-",NoDraw="fixture:yan-xiang-no-draw",Watch="fixture:yan-xiang-native-watch",Deck="fixture:yan-xiang-deck",Mode="identity:classic-yan-xiang";
 public static void RecipientBranchesActualDiscardRewardAndRoundUseQuotaCold()
 {
  var s=Scene.Start(false);s.Play(0);var crossbow=s.V(0).Hand.Single(c=>c.Kind==CardKind.Crossbow).Id;
  s.UseYuzheng(0);s.Menu(AnniversaryYanXiangStage.Option);s.Answer(c=>Value(c)=="1");s.Menu(AnniversaryYanXiangStage.HandCards);
  var keptDodge=s.V(0).Hand.First(c=>c.Kind==CardKind.Dodge&&c.Suit==Suit.Heart).Id;
  var cost=s.V(0).Hand.Where(c=>c.Id!=crossbow&&c.Id!=keptDodge).Select(c=>c.Id).ToArray();
  foreach(var id in cost){s.Menu(AnniversaryYanXiangStage.HandCards);s.Answer(c=>c.Cards.SequenceEqual([id]));}
  s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch);
  var frame=s.Root!.Id;
  s.Need(s.Root.AnniversaryYanXiang is{Stage:AnniversaryYanXiangStage.MovementChildren,Option:1,Required:3,Moves.Count:3}&&s.V(0).HandCount==2&&
   s.E<AnniversaryYanXiangLeaseEvent>().Length==0,"The recipient really pays its three selected hand materials before the native movement child pauses or any reward lease is issued.");
  s.Cold();s.Continue();s.Play(0);
  var lease=s.E<AnniversaryYanXiangLeaseEvent>().Single(e=>e.Option==1);
  s.Need(lease.Id==frame&&lease.Amount==3&&s.E<NativeLogicalDiscardMaterialCompletedEvent>().Count(e=>cost.Contains(e.CardId)&&e.To==CardLocation.DiscardPile)==3&&
   s.G.CardMovements.Count(m=>cost.Contains(m.CardId)&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DiscardPile)==3,"Cold returns the original once-paid hand adjustment and issues X from three completed actual discards.");
  s.Native(0,crossbow);s.Play(0);
  s.UseYuzheng(3);s.Menu(AnniversaryYanXiangStage.Option);
  s.Need(s.P!.PlayerSeat==3&&s.P.Choices is[{Parameters:var parameters}]&&parameters.GetValueOrDefault("value")=="2","The selected other recipient owns the private option choice, and the owner's already used branch is absent for every target in this phase.");
  s.Answer(c=>Value(c)=="2");s.Play(0);
  var quota=s.E<AnniversaryYanXiangLeaseEvent>().Single(e=>e.Option==2);
  s.Need(quota.Actor==3&&quota.Amount==5&&s.E<AnniversaryYanXiangDrawReturnedEvent>().Single(e=>e.FrameId==quota.Id).Actual==5&&
   !s.G.GetLegalActions(0).Any(a=>a.ProgramSkillId==Yuzheng),"The second branch really draws the capped five and both branch choices are now spent in this actual phase.");
  var response=s.V(3).Hand.Single(c=>c.Kind==CardKind.Dodge).Id;s.NativeKind(0,CardKind.Slash,3);
  s.Reach(()=>s.P is{Kind:DecisionKind.RespondDodge,PlayerSeat:3});s.Answer(c=>c.Cards.SequenceEqual([response]));s.Play(0);
  var dodgeUse=s.E<CardActionAcceptedEvent>().Single(e=>e.Action.ActorSeat==3&&e.Action.EffectiveKind==CardKind.Dodge).Action;
  s.Need(s.E<CurrentTurnCardUseKindsRecordedEvent>().Count(e=>e.CardActionId==dodgeUse.ActionId&&e.ActorSeat==3)==1&&
   s.E<AnniversaryYanXiangRewardRequestedEvent>().Count(e=>e.LeaseId==lease.Id)==2,"The recipient's real Dodge against Slash consumes one true response Use; the owner's Crossbow and Slash completions each reward once.");
  s.End(0);s.Play(3);s.Need(s.V(3).HandLimit==17,"The issued five-card addition is present in the recipient's real current-turn hand limit.");
  s.NativeKind(3,CardKind.Crossbow);s.Play(3);s.NativeKind(3,CardKind.ArrowBarrage);
  s.Reach(()=>s.P is{Kind:DecisionKind.RespondDodge,PlayerSeat:0});s.Answer(c=>c.Cards.SequenceEqual([keptDodge]));s.Play(3);
  var played=s.E<CardActionAcceptedEvent>().Last(e=>e.Action.ActorSeat==0&&e.Action.EffectiveKind==CardKind.Dodge).Action;
  s.Need(!s.E<CurrentTurnCardUseKindsRecordedEvent>().Any(e=>e.CardActionId==played.ActionId)&&s.E<AnniversaryYanXiangRewardRequestedEvent>().Count(e=>e.LeaseId==lease.Id&&e.ActionId==played.ActionId)==1&&
   s.E<AnniversaryYanXiangRewardRequestedEvent>().Count(e=>e.LeaseId==lease.Id)==3&&s.E<AnniversaryYanXiangDrawReturnedEvent>().Count(e=>e.Requested==2&&e.Actual==2)==3,
   "A real Dodge played against Arrow Barrage is excluded from the true-Use ledger yet completes the third once-only draw-two reward, proving Use and Played have distinct native semantics.");
  for(var i=0;i<2;i++){s.NativeKind(3,CardKind.Slash,2);s.Play(3);}
  var admitted=s.E<CardUseDeclaredEvent>().Where(e=>e.SourceSeat==3).ToArray();
  s.Need(admitted.Length==4&&s.E<CurrentTurnCardUseKindsRecordedEvent>().Skip(quota.UseCutoff).Count(e=>e.ActorSeat==3)==5&&admitted.All(e=>s.E<CardUseFinishedEvent>().Count(done=>done.ResolutionId==e.ResolutionId)==1)&&s.V(2).Hp==9&&
   s.V(3).Hand.Any(c=>c.Kind==CardKind.Slash)&&!s.G.GetLegalActions(3).Any(a=>a.Kind==LegalActionKind.Slash),"The fifth admitted native Use finishes normally; its paid declaration cannot reject its own continuation, and the sixth true Use is unavailable.");
  s.End(3);s.Play(3);
  s.Need(s.E<RoundStartedEvent>().Last().RoundNumber>quota.Round&&s.V(3).HandLimit==12,"The issued hand-limit addition expires at the next actual round, rather than at the original owner's turn end.");
  s.Replay();
 }
 public static void EndingDamageNamesNativeHeTargetsAndColorExpiryCold()
 {
  foreach(var name in new[]{CardKind.Slash,CardKind.Duel,CardKind.BarbarianAssault,CardKind.ArrowBarrage,CardKind.FireAttack})
  {
   var s=Scene.Start(true);s.Play(0);s.NativeKind(0,CardKind.Crossbow);s.Play(0);var material=s.V(0).Equipment.Single(c=>c.Kind==CardKind.Crossbow).Id;s.End(0);s.Offer();s.Activate();s.Menu(AnniversaryYanXiangStage.DamageName);
   s.Need(s.G.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Any()&&!s.E<TurnEndedEvent>().Any(e=>e.ActorSeat==0),"Suishi begins in the actual Ending phase before the turn's completed end.");
   s.Answer(c=>Value(c)==name.ToString());s.Menu(AnniversaryYanXiangStage.UseChoice);
   s.Answer(c=>c.Cards.SequenceEqual([material])&&(name is CardKind.BarbarianAssault or CardKind.ArrowBarrage?c.Targets.Count==3:c.Targets.SequenceEqual([3])));
   s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch);
   var issued=s.E<AnniversaryYanXiangUseIssuedEvent>().Single();var native=s.G.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==issued.UseId);
   s.Need(native.Action is{Type:CardActionType.Use,ActorSeat:0,ProviderSeat:0,PhysicalCards:[{CardId:var id,From:var from}],EffectiveIsRed:false}&&id==material&&from==CardLocation.Equipment(0)&&
    native.CardKind==name&&native.Action.ConversionChain is[{SkillId:Suishi}]&&s.Zone(material)==CardLocation.Processing&&
    (name is not(CardKind.BarbarianAssault or CardKind.ArrowBarrage)||native.TargetSeats.Order().SequenceEqual(new[]{1,2,3})),"Every declared damage name pays the selected real equipment material and enters its complete native target/response family with its actual black color.");
   if(name==CardKind.BarbarianAssault)s.Cold();s.Continue();s.Reach(()=>s.E<AnniversaryYanXiangCompletedEvent>().Any(e=>e.FrameId==issued.FrameId));
   var victims=name is CardKind.BarbarianAssault or CardKind.ArrowBarrage?new[]{1,2,3}:new[]{3};
   s.Need(victims.All(v=>s.V(v).Hp==2)&&s.E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==issued.UseId)==1&&
    s.E<AnniversaryYanXiangUseReturnedEvent>().Count(e=>e.UseId==issued.UseId)==1&&s.E<AnniversaryYanXiangColorBannedEvent>().Count(e=>e.UseId==issued.UseId)==victims.Length,
    "The actual native damage, whole-card finish and original typed return occur once for every intended target, issuing color restrictions only for those genuinely hurt.");
   if(name!=CardKind.BarbarianAssault)continue;
   s.Play(3);var black=s.V(3).Hand.Single(c=>c.Suit==Suit.Club).Id;var red=s.V(3).Hand.First(c=>c.Suit==Suit.Heart).Id;
   s.Need(s.G.GetLegalActions(3).All(a=>a.CardId!=black)&&s.G.GetLegalActions(3).Any(a=>a.CardId==red),"The injured role cannot use its black Peach, while its genuinely red usable Peach remains legal.");
   var extra=s.V(3).Hand.Single(c=>c.Kind==CardKind.Dodge).Id;s.End(3);s.Reach(()=>s.P is{Kind:DecisionKind.DiscardCards,PlayerSeat:3});s.Discard([extra]);
   s.Offer();s.Activate();s.Menu(AnniversaryYanXiangStage.DamageName);s.Answer(c=>Value(c)==CardKind.Duel.ToString());s.Menu(AnniversaryYanXiangStage.UseChoice);
   s.Need(s.P!.PlayerSeat==3,"At another role's Ending, the actual executor owns the material and native target choice.");
   s.Answer(c=>c.Cards.SequenceEqual([red])&&c.Targets.SequenceEqual([0]));s.Reach(()=>s.P?.SkillPrompt?.SkillId==Watch);
   var foreign=s.E<AnniversaryYanXiangUseIssuedEvent>().Last();var use=s.G.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==foreign.UseId);
   s.Need(use.SourceSeat==3&&use.Action is{ActorSeat:3,ProviderSeat:3,EffectiveIsRed:true}&&use.AnniversaryYanXiangUse!.Source.OwnerSeat==0&&use.Action.PhysicalCards.Single().From==CardLocation.Hand(3),
    "The original Yan Xiang owns the skill while the foreign actor is the true user/provider paying its own hand card.");
   s.Cold();s.Continue();s.Reach(()=>s.E<AnniversaryYanXiangCompletedEvent>().Any(e=>e.FrameId==foreign.FrameId));
   s.Need(s.V(0).Hp==2&&s.E<AnniversaryYanXiangColorBannedEvent>().Any(e=>e.UseId==foreign.UseId&&e.Victim==0&&e.IsRed),"The foreign native Duel really hurts Yan Xiang and issues a red-use restriction through its restored original native use.");
   s.Play(0);var peach=s.V(0).Hand.Single(c=>c.Kind==CardKind.Peach).Id;
   s.Need(!s.G.GetLegalActions(0).Any(a=>a.CardId==peach)&&s.G.GetLegalActions(0).Any(a=>a.Kind==LegalActionKind.Slash),"Other roles' actual turn ends do not expire the injured role's red restriction before its own next turn.");
   s.End(0);s.Play(3);
   s.Need(s.G.GetLegalActions(3).Any(a=>a.CardId==black)&&s.E<ActualTurnCursorReturnedEvent>().Any(e=>e.OwnerSeat==0&&e.ActualTurn>1),"Each victim's restriction expires only after that victim's own actual turn has returned.");
   s.Replay();
  }
 }
 private static string? Value(PromptChoice c)=>c.Parameters.GetValueOrDefault("value");
 private sealed class Scene(GameEngine game,ContentRegistry registry)
 {
  public GameEngine G{get;private set;}=game;
  public PendingDecision? P=>Enumerable.Range(0,4).Select(i=>G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p=>p is not null);
  public ProgramSkillFrame? Root=>G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.AnniversaryYanXiang is not null);
  public PlayerSnapshot V(int seat)=>G.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
  public T[] E<T>()=>G.Events.Select(e=>e.Payload).OfType<T>().ToArray();
  public CardLocation Zone(int id)=>G.CreateCardZoneDiagnostics().Single(c=>c.CardId==id).Location;
  public void Play(int actor)=>Reach(()=>P is{Kind:DecisionKind.PlayCard}p&&p.PlayerSeat==actor);
  public void End(int actor)=>Accept(new EndPlayPhaseCommand(actor,G.Revision,P!.PromptId));
  public void Discard(IReadOnlyList<int> ids){var p=P!;Accept(new DiscardCardsCommand(p.PlayerSeat,ids,p.PromptId,G.Revision));}
  public void UseYuzheng(int target)=>Accept(new UseProgramSkillCommand(0,Yuzheng,"admonish",[],[target],G.Revision,P!.PromptId));
  public void Offer()=>Reach(()=>P?.SkillPrompt?.SkillId==Suishi&&P.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));
  public void Activate()=>Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
  public void Menu(AnniversaryYanXiangStage stage)=>Reach(()=>Root?.AnniversaryYanXiang?.Stage==stage&&P?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="anniversary-yan-xiang")==true);
  public void Continue()=>Answer(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
  public void NativeKind(int actor,CardKind kind,int? target=null)=>Native(actor,V(actor).Hand.First(c=>c.Kind==kind).Id,target);
  public void Native(int actor,int id,int? target=null)
  {
   var a=G.GetLegalActions(actor).First(a=>a.CardId==id&&a.ConversionSource is null&&(target is null||a.TargetSeats.SequenceEqual([target.Value])));
   Accept(new PlayCardCommand(actor,id,a.TargetSeats,G.Revision,P!.PromptId,a.PlayedCardKind,a.TargetCardId));
  }
  public void Answer(Func<PromptChoice,bool> predicate)
  {var p=P!;var c=p.Choices.FirstOrDefault(predicate);Need(c is not null,"The actual current human must receive the intended private recipient/native choice.");Accept(new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c!.Id,G.Revision));}
  public void Reach(Func<bool> ready)
  {
   for(var step=0;step<500;step++)
   {
    if(ready())return;
    if(P is{}p)
    {
     if(p.Kind==DecisionKind.PlayCard){End(p.PlayerSeat);continue;}
     if(p.Kind==DecisionKind.DiscardCards){var ids=p.ValidCardIds.OrderBy(id=>V(p.PlayerSeat).Hand.Single(c=>c.Id==id).Kind==CardKind.Dodge?1:0).Take(p.RequiredCardCount).ToArray();Accept(new DiscardCardsCommand(p.PlayerSeat,ids,p.PromptId,G.Revision));continue;}
     if(p.Kind==DecisionKind.SelectFaction){Answer(_=>true);continue;}
     if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying){Answer(c=>c.Cards.Count==0);continue;}
     if(p.Kind is DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard){Answer(c=>c.Cards.Count>0);continue;}
     if(p.SkillPrompt?.SkillId==Watch){Continue();continue;}
     if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate")){Answer(c=>c.Parameters.GetValueOrDefault("program-action")!="activate");continue;}
     Need(false,"Unexpected Yan Xiang native boundary.");
    }
    Need(G.State.Winner==Winner.None,"The short native scene retains living participants.");Accept(new AdvanceOneStepCommand(G.Revision));
   }
   Need(false,"The bounded native scene missed its requested original continuation.");
  }
  private string State()=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(i=>SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),Frames=G.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=G.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),G.CardMovements,Zones=G.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(G.AcceptedCommands)});
  public void Cold(){var before=State();G=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())),registry);Need(before==State(),"Cold preserves the original paid child, recipient and native action identically.");}
  public void Replay(){var before=State();G=GameReplay.Replay(G.CreateCheckpoint().Options,G.AcceptedCommands,registry);Need(before==State(),"The accepted journal replays actual branch payments, issued effects and native damage returns exactly.");}
  public void Need(bool okay,string message){if(!okay)throw new InvalidOperationException(message+" "+JsonSerializer.Serialize(new{G.State.TurnNumber,G.State.CurrentSeat,P,Receipt=Root?.AnniversaryYanXiang,Last=G.Events.TakeLast(7).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()}));}
  private void Accept(GameCommand command){var result=G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected original Yan Xiang command.");}
  public static Scene Start(bool damage)
  {
   var hands=Hands(damage);var stock=Enumerable.Range(0,80).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Club,7)).ToArray();
   // One fixed-seed native deal determines entity positions. Replacing the recipe
   // at those IDs preserves the same native shuffle, setup, deal and replay.
   var probe=Boot(ContentRegistry.Build(new StandardContentPackage(),new Fixture(damage,stock)));
   foreach(var seat in Enumerable.Range(0,4))
   {
    var ids=probe.V(seat).Hand.Select(c=>c.Id).ToArray();probe.Need(ids.Length==hands[seat].Length,"The fixed identity table deals its exact sparse initial hand counts.");
    for(var i=0;i<ids.Length;i++)stock[ids[i]-1]=new(probe.registryCard(hands[seat][i].Kind),hands[seat][i].Suit,hands[seat][i].Rank);
   }
   return Boot(ContentRegistry.Build(new StandardContentPackage(),new Fixture(damage,stock)));
  }
  private string registryCard(CardKind kind)=>registry.Cards.Values.First(c=>c.LegacyKind==kind).Id;
  private static Scene Boot(ContentRegistry registry)
  {
   var g=GameEngine.CreateStandard(new GameOptions{Seed=47,PlayerCount=4,HumanSeat=0,HumanSeats=[0,1,2,3],HumanRole=Role.Lord,AiPolicyVersion=AiDecisionPolicy.CurrentVersion,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=true,AdvanceAfterHumanCommands=false,MaxTurns=12},registry);
   var s=new Scene(g,registry);s.Accept(new StartGameCommand());
   for(var n=0;n<30;n++)
   {
    if(s.P is{Kind:DecisionKind.PlayCard,PlayerSeat:0})return s;
    if(s.P is{Kind:DecisionKind.SelectGeneral}p){s.Accept(new SelectGeneralCommand(p.PlayerSeat,p.PlayerSeat==0?Hero:Peer+p.PlayerSeat,g.Revision,p.PromptId));continue;}
    if(s.P is{Kind:DecisionKind.SelectFaction}){s.Answer(_=>true);continue;}
    s.Need(s.P is null,"The fixed four-general identity opening reaches only its actual selection and deal boundaries.");s.Accept(new AdvanceOneStepCommand(g.Revision));
   }
   throw new InvalidOperationException("The fixed identity opening did not reach its first actual Play phase.");
  }
 }
 private static ShanheStartingCard[][] Hands(bool damage)=>damage?
 [
  [new(CardKind.Crossbow,Suit.Spade,1),new(CardKind.Peach,Suit.Heart,2),new(CardKind.Slash,Suit.Club,7),new(CardKind.Dodge,Suit.Heart,2)],
  [new(CardKind.Peach,Suit.Heart,2),new(CardKind.Peach,Suit.Club,3),new(CardKind.Dodge,Suit.Heart,2)],
  [new(CardKind.Peach,Suit.Heart,2),new(CardKind.Peach,Suit.Club,3),new(CardKind.Dodge,Suit.Heart,2)],
  [new(CardKind.Peach,Suit.Heart,2),new(CardKind.Peach,Suit.Club,3),new(CardKind.Dodge,Suit.Heart,2)]
 ]:
 [
  [new(CardKind.Crossbow,Suit.Spade,1),new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Dodge,Suit.Heart,3),new(CardKind.Dodge,Suit.Club,3),new(CardKind.Peach,Suit.Heart,2)],
  [new(CardKind.Peach,Suit.Heart,2),new(CardKind.Peach,Suit.Club,3)],
  [new(CardKind.Peach,Suit.Heart,2),new(CardKind.Peach,Suit.Club,3)],
  [new(CardKind.Crossbow,Suit.Spade,1),new(CardKind.ArrowBarrage,Suit.Spade,7),new(CardKind.Dodge,Suit.Heart,2)]
 ];
 private static string HandSkill(int seat)=>"fixture:yan-xiang-hand-"+seat;
 private sealed class Fixture(bool damage,IReadOnlyList<ContentDeckPhysicalCard> stock):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-anniversary-yan-xiang",new(1,0,0));
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(new OwnerBuilder(b),Hero);
   var rules=JsonNode.Parse($$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
    {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-normal-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
    {"id":"{{Watch}}","revision":1,"triggers":[
     {"id":"paid-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.anniversary:yuzheng.discard"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]},
     {"id":"native-damage-use","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["slash","duel","barbarianAssault","arrowBarrage","fireAttack"],"usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native","options":[{"id":"continue"}]}]}
    ]}
   ]}
   """)!;
   var names=new Dictionary<string,object>{[NoDraw]=new{name="固定真实手牌",description="保留原生阶段，正常摸牌调整为零。"},[Watch]=new{name="原生付款与使用暂停",description="在原帧真实子窗暂停。",optionLabels=new Dictionary<string,string>{["continue"]="继续"}}};
   for(var seat=0;seat<4;seat++)
   {
    rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{HandSkill(seat)}}","revision":1,"modifiers":[{"id":"sparse-opening","query":"initialHandSize","operation":"add","value":{{Hands(damage)[seat].Length}},"priority":0}]}"""));
    names[HandSkill(seat)]=new{name="固定稀疏发牌",description="保留原生实体发牌及洗牌。"};
   }
   var catalog=SkillProgramCatalog.Load(rules.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=names}));
   foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
   for(var seat=1;seat<4;seat++)b.AddGeneral(new(Peer+seat,"固定真实同桌","",NoDraw,"wei",12,[Watch,HandSkill(seat)]){InitialHp=damage?3:12});
   b.AddDeck(new(Deck,"固定实际实体库存",0,2,[]){PhysicalCards=stock});
   b.AddMode(new(Mode,"阎象真实分支和原生伤害",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},Deck,GeneralCandidateCount:4,GeneralPoolIds:[Hero,Peer+1,Peer+2,Peer+3]));
  }
 }
 private sealed class OwnerBuilder(IContentRegistryBuilder b):IContentRegistryBuilder
 {
  public void AddGeneral(ContentGeneralDefinition d)=>b.AddGeneral(d.Id==Hero?d with{InitialHp=2,AdditionalSkillIds=[..d.AdditionalSkillIds??[],NoDraw,Watch,HandSkill(0)]}:d);
  public void AddSkill(ContentSkillDefinition d)=>b.AddSkill(d);public void AddCard(ContentCardDefinition d)=>b.AddCard(d);public void AddDeck(ContentDeckRecipe d)=>b.AddDeck(d);public void AddMode(ContentModeDefinition d)=>b.AddMode(d);
 }
}
