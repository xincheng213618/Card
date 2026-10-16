using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;
internal static class SpPangTongChecks
{
 private const string Owner="fixture:sp-pang-tong-owner",Driver="fixture:sp-pang-tong-driver",Quiet="fixture:sp-pang-tong-quiet",Watch="fixture:sp-pang-tong-watch",Mode="identity:classic-sp-pang-tong";
 public static void ActualOpaqueRevealOptionalExchangeAndEqualRanksCold()
 {
  var(g,r)=Start(equal:true);Play(g);Guolun(g);Reach(g,()=>Root(g)?.SpPangTong?.Stage=="reveal");
  var p=P(g)!;Need(p.IsPrivate&&p.PlayerSeat==0&&p.Choices.Count==V(g,1).Hand.Count&&p.Choices.All(c=>c.Cards.Count==0)&&g.CreateSnapshot(2).PendingDecision?.Choices.Count is null or 0,"Foreign hand choices use only real opaque slots and remain private to the chooser.");g=Cold(g,r);Answer(g,_=>true);Need(E<GuolunRevealedEvent>(g).Count()==1&&Root(g)?.SpPangTong?.Stage=="exchange","One actual original foreign hand entity is publicly revealed before the optional payment.");
  var before=g.CardMovements.Count;g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("branch")=="decline");Play(g);Need(g.CardMovements.Count==before&&!E<SpPangTongSegmentPaidEvent>(g).Any(),"Declining after the actual reveal neither moves a material nor creates a reward.");
  Next(g);Guolun(g);Reach(g,()=>Root(g)?.SpPangTong?.Stage=="reveal");Answer(g,_=>true);var revealed=Root(g)!.SpPangTong!.RevealedCard!.Value;var own=V(g,0).Hand[0].Id;var frame=Root(g)!.Id;g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([own]));Play(g);
  Need(V(g,0).Hand.Any(c=>c.Id==revealed)&&V(g,1).Hand.Any(c=>c.Id==own)&&E<SpPangTongSegmentPaidEvent>(g).Count(e=>e.Stage=="exchange-children"&&e.Count==2)==1&&!E<SpPangTongSegmentPaidEvent>(g).Any(e=>e.Stage=="draw-children")&&E<ProgramSkillResolvedEvent>(g).Count(e=>e.FrameId==frame)==1,"The equal pre-exchange ranks really exchange two original entities atomically, without inventing either player's draw.");_=Cold(g,r);
 }
 private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static ProgramSkillFrame?Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f=>f.SpPangTong is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
 private static void Accept(GameEngine g,GameCommand c){var a=g.Submit(c);Need(a.Accepted,(a.Error?.Message??"Rejected SP Pang Tong command")+" "+JsonSerializer.Serialize(P(g)));}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> filter){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(filter).Id,g.Revision));}
 private static void Guolun(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"ol:guolun","reveal-exchange",[],[1],g.Revision,P(g)!.PromptId));
 private static void Play(GameEngine g)=>Reach(g,()=>Root(g)is null&&P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void Next(GameEngine g){var turns=E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard}&&Root(g)is null&&E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0)>turns);}
 private static void Reach(GameEngine g,Func<bool>done)
 {
  for(var i=0;i<220;i++)
  {
   if(done())return;var p=P(g);
   if(p is{PlayerSeat:0}&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
   else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response")is"pass"or"take-damage"or"let-die");
   else Accept(g,new AdvanceOneStepCommand(g.Revision));
  }
  throw new InvalidOperationException("Bounded SP Pang Tong boundary missing: "+JsonSerializer.Serialize(new{Pending=P(g),Frames=g.ResolutionStack,Players=Enumerable.Range(0,4).Select(s=>V(g,s)),Recent=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));
 }
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
 private static GameEngine Cold(GameEngine g,ContentRegistry registry){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(cold),"Cold accepted-command replay preserves private slots, original entities, paid bounds and native child cursors.");return cold;}
 private static(GameEngine,ContentRegistry)Start(bool equal=false,bool watch=false,bool silver=false)
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(equal,watch,silver));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=Role.Lord,ModeId=Mode,UseInteractiveSetup=true,AdvanceAfterHumanCommands=false,UseInteractiveDiscard=false,MaxTurns=8},registry);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,registry);
 }
 private sealed class Fixture(bool equal,bool watch,bool silver):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("sp-pang-tong-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   string Read(string suffix){using var stream=typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-pang-tong-sp."+suffix)!;using var reader=new StreamReader(stream);return reader.ReadToEnd();}
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}Add(SkillProgramCatalog.Load(Read("rules.json"),Read("presentation.json")));
   object Act(string id,int targets,object[] effects)=>new{id,minCards=0,maxCards=0,minTargets=targets,maxTargets=targets,targetKind="anyLiving",usesPerTurn=(int?)null,effects};
   var acts=new[]{Act("kill",1,[new{op="damage",target="selectedTarget",amount=20}]),Act("hurt",0,[new{op="loseHp",target="owner",amount=1}]),Act("draw-three",0,[new{op="draw",target="owner",amount=3}]),Act("obtain",0,[new{op="revealTopCards",target="owner",amount=1,resultBind="obtained",visibility="public"},new{op="moveBoundCards",target="owner",sourceBind="obtained",destination="ownerHand"}]),Act("remove-songsang",0,[new{op="loseOwnerSkillsAndGrant",target="owner",skillIds=new[]{"ol:songsang"},sourceBind=Driver}]),Act("equip",1,[new{op="useRandomDeckEquipment",target="owner",resultBind="gear"}])};
   var defs=new List<object>{new{id=Driver,revision=1,activations=acts},new{id=Quiet,revision=1,triggers=new[]{new{id="quiet",window="afterNormalDraw",subject="owner",optional=false,effects=new[]{new{op="skipTurnPhases",target="owner",phases=new[]{"play"}}}}}}};
   if(watch)defs.Add(new{id=Watch,revision=1,triggers=new object[]{new{id="paid",window="cardsGained",subject="owner",destinationZones=new[]{"hand"},movementReasons=new[]{"skill-program.guolun.exchange"},movementOccurrence="perBatch",optional=false,usageScope="game",usageLimit=1,effects=new object[]{new{op="chooseOption",target="owner",resultBind="seen",options=new[]{new{id="continue"}}},new{op="loseHp",target="owner",amount=20}}},new{id="loss",window="dyingEntering",subject="owner",optional=false,usageScope="game",usageLimit=1,effects=new object[]{new{op="recover",target="owner",amount=20},new{op="loseOwnerSkillsAndGrant",target="owner",skillIds=new[]{"ol:guolun",Watch},sourceBind=Driver}}}}});
   if(silver)defs.Add(new{id=Watch,revision=1,triggers=new[]{new{id="recover-child",window="afterHpRecovered",subject="owner",optional=false,usageScope="game",usageLimit=1,effects=new[]{new{op="chooseOption",target="owner",resultBind="seen",options=new[]{new{id="continue"}}}}}}});
   var labels=defs.Select(d=>JsonSerializer.SerializeToElement(d).GetProperty("id").GetString()!).ToDictionary(id=>id,id=>id==Watch?(object)new{name=id,description="真实原生子窗口",optionLabels=new Dictionary<string,string>{{"continue","继续"}}}:new{name=id,description="真实原生命令夹具"});Add(SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
   var extras=new List<string>{"ol:guolun","ol:songsang"};if(watch||silver)extras.Add(Watch);b.AddGeneral(new(Owner,"真实SP庞统","supporter",Driver,"wu",12,extras,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>"fixture:sp-pang-tong-peer-"+i).ToArray();foreach(var p in peers)b.AddGeneral(new(p,"原生安静同桌","supporter",Quiet,"wei",12));
   if(silver)b.AddCard(new("fixture:sp-pang-tong-lion","白银狮子","装备牌","原生失装回复",CardKind.SilverLion));
   b.AddDeck(new("fixture:sp-pang-tong-deck","原生实际手牌",8,2,[]){PhysicalCards=Enumerable.Range(0,160).Select(i=>new ContentDeckPhysicalCard("standard:slash",Suit.Spade,equal?7:i%13+1)).Concat(silver?Enumerable.Range(0,16).Select(_=>new ContentDeckPhysicalCard("fixture:sp-pang-tong-lion",Suit.Club,7)):[]).ToArray()});b.AddMode(new(Mode,"真实SP庞统",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:sp-pang-tong-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
