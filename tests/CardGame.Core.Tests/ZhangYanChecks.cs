using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class ZhangYanChecks
{
 private const string Suji="ol:suji",Lang="ol:langdao",Owner="fixture:zy-owner",Driver="fixture:zy-driver",Quiet="fixture:zy-quiet",Prep="fixture:zy-prep",Watch="fixture:zy-watch",Conditional="fixture:zy-conditional",Health="fixture:zy-health",Mode="identity:classic-zhang-yan-fixture";
 public static void SujiAnyTargetOwnForeignNormalRangeAndDamageOwnerCold()
 {
  var(g,r)=Start("own",true);Activate(g);Reach(g,()=>Current(g)is{Stage:ZhangYanStage.SujiCard});var material=P(g)!.Choices.First(c=>c.Cards.Count==1).Cards.Single();g=Cold(g,r);Answer(g,c=>c.Cards.SequenceEqual([material]));Reach(g,()=>Current(g)is{Stage:ZhangYanStage.SujiTarget});
  Need(P(g)!.Choices.SelectMany(c=>c.Targets).Order().SequenceEqual([1,3])&&g.CreateSnapshot(1).PendingDecision is null,"Own wounded Play-start accepts any normally in-range legal target; it does not force a self target or expose the private black material choice.");
  g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Play(g);var ret=E<ZhangYanSujiIssuedEvent>(g).Single().Return;
  Need(ret.PhaseOwner==0&&ret.OriginalTarget==1&&g.CardMovements.Count(m=>m.CardId==material&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Use)==1&&!E<ZhangYanRewardIssuedEvent>(g).Any()&&!E<CardUseDebitRecordedEvent>(g).Any()&&g.GetHumanLegalActions().Any(a=>a.Kind==LegalActionKind.Slash),"The real black entity is used once, adds no ordinary Slash debit, and injuring someone other than the wounded phase owner grants no false obtain.");_=Cold(g,r);
  foreach(var hitOwner in new[]{false,true})
  {
   (g,r)=Start("foreign");Use(g,"hurt",targets:[1]);Play(g);End(g);Reach(g,()=>g.State.CurrentSeat==1&&Optional(P(g),Suji));Activate(g);Reach(g,()=>Current(g)is{Stage:ZhangYanStage.SujiCard});Answer(g,c=>c.Cards.Count==1);Reach(g,()=>Current(g)is{Stage:ZhangYanStage.SujiTarget});
   Need(P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([3]))&&!P(g)!.Choices.Any(c=>c.Targets.SequenceEqual([2])),"A foreign wounded participant's actual phase offers another in-range target and rejects the out-of-range seat.");var target=hitOwner?1:3;var hp=V(g,target).Hp;Answer(g,c=>c.Targets.SequenceEqual([target]));
   if(hitOwner)
   {
    Reach(g,()=>Current(g)is{Stage:ZhangYanStage.RewardChoice});Need(Current(g)!.PhaseOwner==1&&P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("source-zone")=="Hand").All(c=>c.Cards.Count==0)&&g.CreateSnapshot(1).PendingDecision is null,"Actual same-use damage to the frozen phase owner issues one source-private opaque foreign hand obtain.");
    g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("source-zone")=="Hand");Reach(g,()=>E<ZhangYanSujiReturnedEvent>(g).Any());var obtained=E<ZhangYanRewardIssuedEvent>(g).Single();
    Need(V(g,target).Hp==hp-1&&obtained.PhaseOwner==1&&g.CardMovements.Count(m=>m.CardId==obtained.CardId&&m.From==CardLocation.Hand(1)&&m.To==CardLocation.Hand(0)&&m.Reason.Value=="skill-program.zhang-yan.suji.obtain")==1,"A genuine injury obtains one actual card from that participant, not from another Slash target.");
   }
   else{Reach(g,()=>E<ZhangYanSujiReturnedEvent>(g).Any());Need(V(g,target).Hp==hp-1&&!E<ZhangYanRewardIssuedEvent>(g).Any(),"Free target selection does not change the conditional reward's original wounded participant.");}_=Cold(g,r);
  }
 }
 private static PlayerSnapshot V(GameEngine g,int seat=0)=>g.CreateSnapshot(seat).Players.Single(p=>p.Seat==seat);
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static ZhangYanReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.ZhangYan is not null)?.ZhangYan;
 private static IEnumerable<T>E<T>(GameEngine g)=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
 private static void Accept(GameEngine g,GameCommand c){var x=g.Submit(c);Need(x.Accepted,x.Error?.Message??"The real command was rejected.");}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Need(p.PlayerSeat==0,"Only the actual human can Answer; every AI advances natively.");Accept(g,new AnswerPromptCommand(0,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static bool Optional(PendingDecision? p,string skill)=>p?.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
 private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
 private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
 private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
 private static void Use(GameEngine g,string id,int[]?targets=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets??[],g.Revision,P(g)!.PromptId));
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void Reach(GameEngine g,Func<bool> done)
 {
  for(var i=0;i<650;i++)
  {
   if(done())return;if(g.State.Status==EngineStatus.Completed)throw new InvalidOperationException("Zhang Yan completed before expected boundary: "+Diagnostic(g));var p=P(g);
   if(p is{PlayerSeat:0}&&(Optional(p,Suji)||Optional(p,Lang)))Skip(g);
   else if(p is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Cards.Count==0);
   else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying})Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
   else if(p is{PlayerSeat:0}&&p.SkillPrompt?.SkillId==Health)Continue(g);
   else if(p is{PlayerSeat:0})throw new InvalidOperationException("Unexpected Zhang Yan prompt: "+Diagnostic(g));else Accept(g,new AdvanceOneStepCommand(g.Revision));
  }
  throw new InvalidOperationException("Zhang Yan did not reach actual boundary: "+Diagnostic(g));
 }
 private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,g.State.Status,Prompt=P(g),Players=Enumerable.Range(0,4).Select(s=>new{Seat=s,V(g,s).Hp,V(g,s).HandCount,V(g,s).IsAlive,Skills=V(g,s).Skills?.Select(k=>k.Id).ToArray()}).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray()});
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),Facts=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),g.CardMovements,Commands=CommandJson.Serialize(g.AcceptedCommands),Zones=g.CreateCardZoneDiagnostics()});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var restored=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(restored),"Cold replay preserves independent private votes, native converted original entity payments, exact phase/damage provenance and issued option lifetime.");return restored;}
 private static(GameEngine,ContentRegistry)Start(string scenario="normal",bool early=false)
 {
  var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=31,PlayerCount=4,HumanSeat=0,HumanRole=scenario is"allies" or"chain"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);
  Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(early)Reach(g,()=>Optional(P(g),Suji));else Play(g);return(g,r);
 }
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-zhang-yan",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(b,"ol:zhang-yan");
   var rules=$$"""
   {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
    {"id":"{{Driver}}","revision":1,"activations":[
     {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
     {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
     {"id":"hurt-seven","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":7}]},
     {"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"}]},
     {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
     {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
     {"id":"lose-lang","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Lang}}"],"sourceBind":"standard:none"}]},
     {"id":"grant-lang","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Lang}}"]}]}]},
    {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
    {"id":"{{Prep}}","revision":1,"triggers":[{"id":"wound","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]},
    {"id":"{{Health}}","revision":1,"triggers":[{"id":"native-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]},
    {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-source","skillId":"{{Suji}}","minimumHp":8}]},
    {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-child","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","movementReasons":["{{CardMoveReasons.Use.Value}}"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":{{(scenario=="payment-death"?20:2)}}}]}]}
   ]}
   """;
   var ids=new[]{Driver,Quiet,Prep,Health,Conditional,Watch};var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=ids.ToDictionary(id=>id,id=>new{name=id,description="张燕原生命令边界夹具",optionLabels=id==Health||id==Watch?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()})}));
   foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实边界"){Program=p,ProgramPresentation=catalog.Presentations[id]});var human=scenario is"allies" or"chain"?Role.Rebel:Role.Lord;
   b.AddSkill(new("fixture:zy-pick-owner","本人公开评分","固定选择"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==human?100000d:-100000d)});
   var skills=new List<string>{Driver,Lang,scenario=="payment-loss"?Conditional:Suji};if(scenario=="own")skills.Add(Prep);if(scenario is"payment-loss" or"payment-death")skills.Add(Watch);
   b.AddGeneral(new(Owner,"张燕真实本人","ol-zhang-yan","fixture:zy-pick-owner","qun",8,skills,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:zy-peer-{i}").ToArray();
   foreach(var(id,index)in peers.Select((id,index)=>(id,index)))
   {var pick="fixture:zy-pick-"+index;b.AddSkill(new(pick,"其他公开评分","固定顺序"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>role==human?-100000d:100003d-index)});b.AddGeneral(new(id,"其他真实角色","supporter",pick,"qun",8,scenario=="reward-lion"?[Health]:scenario=="foreign"?[]:[Quiet]));}
   b.AddCard(new("fixture:zy-lion","白银狮子","装备牌","真实原生移除回复",CardKind.SilverLion));b.AddCard(new("fixture:zy-ox","木牛流马","装备牌","真实原生存粮与移除",CardKind.WoodenOx));b.AddCard(new("fixture:zy-halberd","方天画戟","装备牌","真实原生距离",CardKind.FangtianHalberd));
   b.AddCard(new("fixture:zy-fire","火杀","基本牌","真实原生链伤",CardKind.FireSlash));
   b.AddDeck(new("fixture:zy-deck","固定实体牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,96).Select(i=>new ContentDeckPhysicalCard(scenario=="extra"?"fixture:zy-halberd":scenario=="chain"?"fixture:zy-fire":scenario=="rescued"?"standard:peach":scenario=="dodge"?"standard:dodge":(scenario.StartsWith("payment-",StringComparison.Ordinal)||scenario=="reward-lion")&&i%5==0?scenario=="payment-ox"?"fixture:zy-ox":"fixture:zy-lion":"standard:slash",Suit.Spade,i%13+1)).ToArray()});
   b.AddMode(new(Mode,"张燕原生规则",4,4,scenario is"allies" or"chain"?new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}}:new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Renegade),3}},"fixture:zy-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
