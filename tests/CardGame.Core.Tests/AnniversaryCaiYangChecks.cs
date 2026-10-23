using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryCaiYangChecks
{
 private const string Hero="anniversary:cai-yang",Watch="fixture:ann-cai-yang-watch",Follow="fixture:ann-cai-yang-follow",Quiet="fixture:ann-cai-yang-quiet",Inert="fixture:ann-cai-yang-inert",Deck="fixture:ann-cai-yang-deck";
 public static void NextBlackTurnNativeDuelRetaliationAndFrozenFirstDamageCold()
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());var options=Options();var g=GameEngine.CreateStandard(options,registry);Accept(g,new StartGameCommand());Play(g);
  Need(V(g).Hp==2&&V(g).MaxHp==5,"The fixed native setup begins wounded by three, using the formal four-HP general with one explicit scenario maximum bonus.");
  Accept(g,new UseProgramSkillCommand(0,"anniversary:xunji","designate-next-turn",[],[2],g.Revision,P(g)!.PromptId));Play(g);
  var grant=E<CaiYangXunjiArmedEvent>(g).Single().Grant;
  Need(grant.Target==2&&grant.Source.OwnerSeat==0&&
   !g.GetLegalActions().Any(a=>a.ProgramSkillId=="anniversary:xunji"&&a.ProgramActivationId=="designate-next-turn"),"The real selected other target consumes only this actual Play phase's one designation.");
  Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,p=>p.SkillPrompt?.SkillId==Watch);
  var due=E<CaiYangXunjiDueEvent>(g).Single();var duel=E<CaiYangXunjiIssuedEvent>(g).Single().Origin;
  var root=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.AnniversaryCaiYang?.FirstDamage is not null);var first=root.AnniversaryCaiYang!;
  Need(due.GrantFrameId==grant.FrameId&&due.Target==2&&due.ActualTurn==g.State.TurnNumber&&duel.GrantId==grant.FrameId&&duel.Target==2&&duel.ActualTurn==due.ActualTurn&&
   E<CaiYangBlackUseEvent>(g).Any(e=>e.ActualTurn==due.ActualTurn&&e.ActorSeat==2)&&
   E<CardUseAppearanceCapturedEvent>(g).Any(e=>e.Action.ActorSeat==2&&e.Action.Type==CardActionType.Use&&e.Action.EffectiveKind==CardKind.QinggangSword&&e.Action.EffectiveSuit==Suit.Spade)&&
   first is{Stage:AnniversaryCaiYangStage.DrawChildren,FirstDamage.LostHp:3,Draw:{RequestedCount:1,ActualDrawIds.Count:1}}&&V(g).Hp==2&&V(g).HandCount==1&&
   E<DamageAppliedEvent>(g).All(e=>e.SourceSeat!=0),
   "The designated next actual turn really uses its black physical equipment; its true ending Duel reaches the first source window before HP damage, freezes lost HP three, and pays a native draw before the gain child pauses."+Detail(g));
  var firstFrame=root.Id;g=Cold(g,registry);Continue(g);Play(g);
  var hits=E<DamageAppliedEvent>(g);
  Need(hits.Count(e=>e.SourceCardUseResolutionId==duel.UseId&&e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==2)==1&&
   E<CaiYangXunjiDamageBoundEvent>(g).ToArray()is[{UseId:var originalUse,SourceSeat:0,TargetSeat:2,Amount:2}]&&originalUse==duel.UseId&&
   hits.Count(e=>e.SourceSeat==2&&e.TargetSeat==0&&e.Amount==2)==1&&hits.Count(e=>e.SourceSeat==0&&e.TargetSeat==2&&e.Amount==1)==1&&
   E<CaiYangJiaofengStartedEvent>(g).Length==1&&E<CaiYangJiaofengDrawReturnedEvent>(g).Count(e=>e.FrameId==firstFrame&&e.Count==1)==1&&
   E<CaiYangJiaofengRecoveryIssuedEvent>(g).Count(e=>e.FrameId==firstFrame&&e.Requested==1)==1&&
   E<RecoveryAppliedEvent>(g).Count(e=>e.TargetSeat==0&&e.Amount==2)==1&&E<RecoveryAppliedEvent>(g).Count(e=>e.TargetSeat==0&&e.Amount==1)==1&&
   V(g).Hp==3&&V(g,2).Hp==5&&V(g).HandCount==1,
   "The draw child really heals two, but the original frozen third tier still heals one and the second tier still makes the native Duel damage two; its target really deals two back. A later same-actual-turn source hit deals only one without repeating Jiaofeng."+Detail(g));
  Need(E<CaiYangXunjiReturnedEvent>(g).Count(e=>e.FrameId==duel.FrameId&&e.UseId==duel.UseId&&e.ActionId==duel.ActionId)==1&&
   E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==duel.UseId&&e.CardId==0&&e.CardKind==CardKind.Duel)==1&&
   E<CaiYangXunjiConsumedEvent>(g).Count(e=>e.GrantFrameId==grant.FrameId&&e.Issued)==1&&E<ProgramSkillHpLostEvent>(g).All(e=>e.SkillId!="anniversary:xunji")&&
   g.ResolutionStack.OfType<ProgramSkillFrame>().All(f=>f.AnniversaryCaiYang is null),
   "The original zero-material native Duel, its reverse damage subtree and issued producer complete once, and the current rule never substitutes old HP loss for real retaliation."+Detail(g));
  Need(State(g)==State(GameReplay.Replay(options,g.AcceptedCommands,registry)),"The accepted command journal replays the next-actual-turn obligation, native equipment Use, paid draw child, frozen tiers, true Duel/retaliation and original cleanup return exactly.");
 }
 private static GameOptions Options()
 {
  var setup=new ShanheBattleSetup(7,1,"ann-cai-yang",Hero,2,1,0,0,0,[Watch,Follow],[],[],
   [new(Peer(2)){StartingHand=[new(CardKind.QinggangSword,Suit.Spade,6)]},new(Peer(3)){StartingHand=[]}],[new(Peer(1)){StartingHand=[]}],new string('a',64));
  return new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,
   UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8};
 }
 private static string Peer(int s)=>"fixture:ann-cai-yang-peer-"+s;
 private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static T[] E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
 private static void Need(bool b,string text){if(!b)throw new InvalidOperationException(text);}
 private static void Accept(GameEngine g,GameCommand c){var r=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(r.Accepted,r.Error?.Message??"Rejected Cai Yang command.");}
 private static void Continue(GameEngine g){var p=P(g)!;var c=p.Choices.Single(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));}
 private static void Play(GameEngine g)=>Reach(g,p=>p is{Kind:DecisionKind.PlayCard,PlayerSeat:0});
 private static void Reach(GameEngine g,Func<PendingDecision,bool> ready)
 {
  for(var i=0;i<140;i++)
  {
   if(P(g)is{}p)
   {
    if(ready(p))return;
    if(p.SkillPrompt?.SkillId==Watch){Continue(g);continue;}
    if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification){var c=p.Choices.First(c=>c.Cards.Count==0);Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,c.Id,g.Revision));continue;}
    if(p.PlayerSeat==0)throw new InvalidOperationException("Unexpected Cai Yang native boundary: "+JsonSerializer.Serialize(p)+Detail(g));
   }
   Need(g.State.Winner==Winner.None,"The short Cai Yang scene must retain a genuine native boundary."+Detail(g));Accept(g,new AdvanceOneStepCommand(g.Revision));
  }
  throw new InvalidOperationException("Cai Yang missed the bounded native boundary."+Detail(g));
 }
 private static string Detail(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).MaxHp,V(g,s).HandCount}),
  Armed=E<CaiYangXunjiArmedEvent>(g),Due=E<CaiYangXunjiDueEvent>(g),Black=E<CaiYangBlackUseEvent>(g),Duel=E<CaiYangXunjiIssuedEvent>(g),Damage=E<DamageAppliedEvent>(g),First=E<CaiYangJiaofengStartedEvent>(g),Draw=E<CaiYangJiaofengDrawReturnedEvent>(g),Recovery=E<RecoveryAppliedEvent>(g)});
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static GameEngine Cold(GameEngine g,ContentRegistry registry){var r=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),registry);Need(State(g)==State(r),"The original unfinalized source damage and its paid native draw child cold-restore identically.");return r;}
 private sealed class Fixture:IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-anniversary-cai-yang",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(b,Hero);
   var rules=$$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
    {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-first-source-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.anniversary:jiaofeng.draw"],"usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":2}]}]},
    {"id":"{{Follow}}","revision":1,"triggers":[{"id":"one-later-source-damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","usageScope":"game","usageLimit":1,"optional":false,"isSkillInvocation":false,"effects":[{"op":"damage","target":"owner","targetRef":{"kind":"eventSource"},"amount":1}]}]},
    {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
   ]}
   """;
   var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{
    [Watch]=new{name="已支付交锋摸牌",description="首伤摸牌后真实回复两点，后续档位仍按原快照。",optionLabels=new Dictionary<string,string>{["continue"]="继续"}},
    [Follow]=new{name="同回合后继伤害",description="受到原生反伤后，再对原来源造成一次真实伤害。"},[Quiet]=new{name="安静同桌",description="跳过普通出牌。"}}}));
   foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});
   b.AddSkill(new(Inert,"无附加技能","只按原生AI使用一张真实黑色装备牌。"));
   for(var s=1;s<4;s++)b.AddGeneral(new(Peer(s),"固定同桌","",s==2?Inert:Quiet,"wei",8));
   b.AddDeck(new(Deck,"固定小闪牌堆",0,0,[]){PhysicalCards=Enumerable.Range(0,16).Select(_=>new ContentDeckPhysicalCard("standard:dodge",Suit.Heart,2)).ToArray()});
   b.AddMode(new(ShanheBattleSetup.BattleModeId,"寻嫉原生决斗及交锋快照",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Peer(1),Peer(2),Peer(3)],ModeKind:ContentModeKind.Team,
    TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
  }
 }
}
