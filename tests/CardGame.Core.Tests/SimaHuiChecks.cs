using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class SimaHuiChecks
{
 private const string Owner="fixture:sima-hui-owner",Quiet="fixture:sima-hui-quiet",Driver="fixture:sima-hui-driver",Watch="fixture:sima-hui-watch",Loss="fixture:sima-hui-loss",Extra="fixture:sima-hui-extra",Mode="identity:classic-sima-hui-fixture";
 public static void ActualFirstTurnOrderedSealsAndStableGrantedInstancesCold()
 {
  var(g,r)=Start();Reach(g,()=>Root(g)?.SimaHui?.Stage=="initial-dragon");Need(!E<TurnStartedEvent>(g).Any(),"Mandatory ordered initial seals precede this owner's actual first TurnStarted.");
  g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>Root(g)?.SimaHui?.Stage=="initial-phoenix");Need(!P(g)!.Choices.Any(c=>c.Targets.Contains(0)||c.Targets.Contains(1)),"Phoenix must select a distinct living other, without guessing the initial pair.");
  g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([2]));Play(g);Need(Mark(g,1,PlayerMarkerKind.DragonSeal)==1 && Mark(g,2,PlayerMarkerKind.PhoenixSeal)==1 && V(g,1).Skills!.Any(s=>s.ContentId=="ol:huoji-jianjie") && V(g,2).Skills!.Any(s=>s.ContentId=="ol:lianhuan-jianjie"),"Each real public seal grants its exact dedicated current-SP skill.");
  Need(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="ol:jianjie"),"First actual turn offers no active transfer.");_=Cold(g,r);
  Need(!JsonSerializer.Serialize(new ProgramSkillFrame(1,0,"old","old","hash",0,[],[])).Contains("SimaHui",StringComparison.Ordinal),"Old null optional receipt remains absent from canonical JSON.");
 }
 public static void ActualElementalOriginPrivateDistributionAndPaidChildrenCold()
 {
  foreach(var scenario in new[]{"normal","loss","death"})
  {
   var(g,r)=Start(scenario);Play(g);Use(g,"chain",[1]);Play(g);Use(g,"chain",[2]);Play(g);Use(g,"fire",[1]);
   Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:chenghao" && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
   Reach(g,()=>Root(g)?.SimaHui?.Stage=="distribute");var f=Root(g)!;var frame=f.Id;var viewed=E<SimaHuiTopViewedEvent>(g).Single();Need(viewed.CardIds.Count==viewed.Origin.LivingChainedCount+1 && viewed.Origin.Nature==DamageNature.Fire && viewed.Origin.TargetSeat==1 && viewed.CardIds.All(id=>g.CreateSnapshot(0).PrivateRevealedCards?.Any(c=>c.Id==id)==true) && g.CreateSnapshot(3).PrivateRevealedCards?.Any(c=>viewed.CardIds.Contains(c.Id))!=true,"Only the actual elemental chain origin privately views the original X+1 entities; other viewers learn none.");
   g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([0]));
   if(scenario is "loss" or "death")
   {Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option")==true);g=Cold(g,r);Answer(g,_=>true);}
   if(scenario=="death")Reach(g,()=>!V(g,0).IsAlive && !g.ResolutionStack.Any(f=>f.Id==frame));
   else
   {
    Reach(g,()=>Root(g)?.SimaHui?.Stage=="distribute");if(scenario=="loss")Need(V(g,0).IsAlive && !V(g,0).Skills!.Any(s=>s.ContentId=="ol:chenghao"),"Real dying recovery revokes the source after its paid gift.");
    while(g.ResolutionStack.Any(f=>f.Id==frame)){Reach(g,()=>Root(g)?.SimaHui?.Stage=="distribute" || !g.ResolutionStack.Any(f=>f.Id==frame));if(!g.ResolutionStack.Any(f=>f.Id==frame))break;g=Cold(g,r);Answer(g,c=>c.Targets.SequenceEqual([3]));}Play(g);
   }
   Need(E<SimaHuiTopViewedEvent>(g).Count(e=>e.FrameId==frame)==1 && E<ProgramBindingResolvedEvent>(g).Count(e=>e.FrameId==frame)==1 && E<SimaHuiCardDistributedEvent>(g).Where(e=>e.FrameId==frame).Select(e=>e.CardId).Distinct().Count()==E<SimaHuiCardDistributedEvent>(g).Count(e=>e.FrameId==frame),"Cold nested source-loss/death drains the one original private allocation without re-viewing or paying a gift twice.");
   if(scenario=="death")Need(E<SimaHuiUnassignedReturnedEvent>(g).Single(e=>e.FrameId==frame).CardIds.Count==viewed.CardIds.Count-1,"Actual owner death returns all unassigned original processing cards through one native movement receipt.");_=Cold(g,r);
   if(scenario=="normal")
   {
    Need(E<SimaHuiTopViewedEvent>(g).Count()==1,"The propagated second native recipient does not create a second Chenghao origin reward.");
    Use(g,"chain",[1]);Play(g);Use(g,"fire",[1]);Reach(g,()=>P(g)?.SkillPrompt?.SkillId=="ol:chenghao" && P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate"));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
    Reach(g,()=>Root(g)?.SimaHui?.Stage=="distribute");var solo=E<SimaHuiTopViewedEvent>(g).Last();Need(solo.Origin.LivingChainedCount==1 && solo.CardIds.Count==2,"A real solo chained origin with an empty native propagation tail still views exactly two original cards.");g=Cold(g,r);
    for(var i=0;i<2;i++){Answer(g,c=>c.Targets.SequenceEqual([3]));if(i==0)Reach(g,()=>Root(g)?.SimaHui?.Stage=="distribute");}Play(g);
    Need(E<SimaHuiTopViewedEvent>(g).Count()==2 && E<SimaHuiCardDistributedEvent>(g).Count(e=>e.FrameId==solo.FrameId)==2,"The solo native origin allocates its two actual cards once without requiring a nonempty propagation list.");_=Cold(g,r);
   }
  }
 }
 public static void ActualYinshiSealAndArmorGatesAndNativeYeyanCostTail()
 {
  var(g,r)=Start();Play(g);var hp=V(g,0).Hp;Use(g,"fire",[0]);Play(g);Need(V(g,0).Hp==hp && E<ProgramDamagePreventedEvent>(g).Count()==1,"Yinshi truly prevents elemental damage with neither seal nor physical armor.");
  NextOwnPlay(g);Transfer(g,PlayerMarkerKind.DragonSeal,0);Play(g);Use(g,"fire",[0]);Play(g);Need(V(g,0).Hp==hp-1,"A real Dragon seal disables the otherwise applicable prevention.");NextOwnPlay(g);Transfer(g,PlayerMarkerKind.PhoenixSeal,0);Play(g);
  Use(g,"draw-cost",[]);Play(g);var cards=V(g,0).Hand.Where(c=>c.Suit!=Suit.None).GroupBy(c=>c.Suit).Take(4).Select(x=>x.First().Id).ToArray();Need(cards.Length==4,"Native fixture draw supplies four actual distinct-suit hand entities.");var before=V(g,0).Hp;
  Accept(g,new UseProgramSkillCommand(0,"ol:yeyan-jianjie","two-and-one",cards,[1,2],g.Revision,P(g)!.PromptId));Reach(g,()=>E<SimaHuiYeyanSealsPaidEvent>(g).Any());var payment=E<SimaHuiYeyanSealsPaidEvent>(g).Single();g=Cold(g,r);Play(g);
  Need(Mark(g,0,PlayerMarkerKind.DragonSeal)==0 && Mark(g,0,PlayerMarkerKind.PhoenixSeal)==0 && !V(g,0).Skills!.Any(s=>s.ContentId=="ol:yeyan-jianjie") && V(g,0).Hp==before-3 && cards.All(id=>g.CardMovements.Any(m=>m.CardId==id && m.From==CardLocation.Hand(0) && m.To==CardLocation.DiscardPile)) && E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0 && e.TargetSeat==1 && e.Amount==2 && e.Nature==DamageNature.Fire) && E<DamageAppliedEvent>(g).Any(e=>e.SourceSeat==0 && e.TargetSeat==2 && e.Amount==1 && e.Nature==DamageNature.Fire) && E<ProgramSkillResolvedEvent>(g).Count(e=>e.FrameId==payment.FrameId)==1,"Consuming actual seals revokes only their derived skills while the paid native four-suit discard, LoseHp3 and 2+1 fire-damage tail finish once.");_=Cold(g,r);
  Use(g,"armor",[0]);Play(g);hp=V(g,0).Hp;Use(g,"fire",[0]);Play(g);Need(V(g,0).Hp==hp-1,"A genuinely equipped armor also disables Yinshi after both seals were consumed.");
 }
 private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
 private static int Mark(GameEngine g,int s,PlayerMarkerKind m)=>V(g,s).Markers?.FirstOrDefault(x=>x.Kind==m)?.Count??0;
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static ProgramSkillFrame? Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f=>f.SimaHui is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static void Need(bool b,string m){if(!b)throw new InvalidOperationException(m);}
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Need(result.Accepted,(result.Error?.Message??"Rejected Sima Hui native command")+" "+JsonSerializer.Serialize(P(g)));}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(predicate).Id,g.Revision));}
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void NextOwnPlay(GameEngine g){var n=E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0);End(g);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard} && E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0)>n);}
 private static void Use(GameEngine g,string id,IReadOnlyList<int> targets)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,[],targets,g.Revision,P(g)!.PromptId));
 private static void Transfer(GameEngine g,PlayerMarkerKind marker,int target)
 {Accept(g,new UseProgramSkillCommand(0,"ol:jianjie","transfer",[],[],g.Revision,P(g)!.PromptId));Reach(g,()=>Root(g)?.SimaHui?.Stage=="transfer-kind");Answer(g,c=>c.Parameters.GetValueOrDefault("value")==marker.ToString());Answer(g,c=>c.Targets.SequenceEqual([target]));}
 private static void Reach(GameEngine g,Func<bool> done)
 {
  for(var i=0;i<350;i++)
  {
   if(done())return;var p=P(g);var stage=Root(g)?.SimaHui?.Stage;
   if(p is{PlayerSeat:0} && stage is "initial-dragon" or "initial-phoenix")Answer(g,_=>true);
   else if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
   else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
   else Accept(g,new AdvanceOneStepCommand(g.Revision));
  }
  throw new InvalidOperationException("Bounded Sima Hui boundary missing: "+JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack,Players=Enumerable.Range(0,4).Select(s=>V(g,s)),Recent=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));
 }
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var x=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(x),"Cold command replay preserves exact issued seals/grants/private original cards and paid child cursor.");return x;}
 private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-sima-hui."+suffix)!;using var r=new StreamReader(s);return r.ReadToEnd();}
 private static (GameEngine,ContentRegistry)Start(string scenario="normal")
 {
  var r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario is "death" or "yeyan-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=16},r);
  Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));return(g,r);
 }
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("sima-hui-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog c){foreach(var(id,p)in c.Programs)b.AddSkill(new(id,c.Presentations[id].Name,c.Presentations[id].Description){Program=p,ProgramPresentation=c.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}
   Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
   var defs=new List<string>{$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"priority":0,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
    $$"""{"id":"{{Driver}}","revision":1,"activations":[{"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},{"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},{"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},{"id":"armor","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"armor"}]},{"id":"draw-cost","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]}]}"""};
   var driverNode=JsonNode.Parse(defs[1])!;driverNode["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"wound-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}"""));defs[1]=driverNode.ToJsonString();
   var skills=new List<string>{"ol:jianjie","ol:chenghao","ol:yinshi-sima-hui"};
   if(scenario=="quota"){var driver=JsonNode.Parse(defs[1])!;driver["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"arm-extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:sima-hui-extra"]}]}"""));defs[1]=driver.ToJsonString();defs.Add($$"""{"id":"{{Extra}}","revision":1,"triggers":[{"id":"insert","window":"playEnding","subject":"owner","priority":100,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]}""");}
   if(scenario is "loss" or "death")
   {
    skills.Add(Watch);defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-gift-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.sima-hui.chenghao.give"],"optional":false,"priority":0,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");
    if(scenario=="loss"){skills.Add(Loss);defs.Add($$"""{"id":"{{Loss}}","revision":1,"triggers":[{"id":"real-source-loss","window":"dyingEntering","subject":"owner","optional":false,"priority":1000,"usageScope":"game","usageLimit":1,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:chenghao"],"sourceBind":"standard:none"}]}]}""");}
   }
   var labels=defs.Select(d=>(Id:JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!,Choice:d.Contains("chooseOption",StringComparison.Ordinal))).ToDictionary(x=>x.Id,x=>new{name=x.Id,description="真实司马徽原生夹具",optionLabels=x.Choice?new Dictionary<string,string>{["continue"]="继续"}:new Dictionary<string,string>()});
   Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
   b.AddGeneral(new(Owner,"真实司马徽拥有者","supporter",Driver,"qun",12,skills,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:sima-hui-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"原生同桌","supporter",Quiet,"wei",12,[]));
   b.AddDeck(new("fixture:sima-hui-deck","真实固定实体牌",4,0,[]){PhysicalCards=Enumerable.Range(0,128).Select(i=>new ContentDeckPhysicalCard("standard:slash",scenario=="quota"?Suit.Club:new[]{Suit.Club,Suit.Spade,Suit.Heart,Suit.Diamond}[i%4],7)).Concat(Enumerable.Range(0,8).Select(_=>new ContentDeckPhysicalCard("standard:bagua",Suit.Club,7))).ToArray()});
   b.AddMode(new(Mode,"司马徽真实夹具",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:sima-hui-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
