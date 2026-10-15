using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class ZhangJiChecks
{
 private const string Owner="fixture:zhang-ji-owner",Quiet="fixture:zhang-ji-quiet",Driver="fixture:zhang-ji-driver",Watch="fixture:zhang-ji-watch",Loss="fixture:zhang-ji-loss",Mode="identity:classic-zhang-ji-fixture";
 public static void ActualTargetRankChooserJudgmentAndTwoDamageCold()
 {
  var(g,r)=Start("match");Play(g);Need(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="ol:lueming"),"Equal equipment counts cannot activate Lueming.");Equip(g);Play(g);
  Lueming(g);Reach(g,()=>Root(g)?.ZhangJi?.Stage=="rank");var frame=Root(g)!;var p=P(g)!;
  Need(p.PlayerSeat==1 && p.IsPrivate && p.Choices.Count==13 && p.Choices.Select(c=>c.Parameters["rank"]).SequenceEqual(Enumerable.Range(1,13).Select(i=>i.ToString())),"The actual other target owns all thirteen legal rank choices.");
  var reject=g.Submit(new AnswerPromptCommand(0,p.PromptId,p.Choices[0].Id,g.Revision));Need(!reject.Accepted,"The owner cannot answer a foreign private rank decision.");
  g=Cold(g,r);var hp=V(g,1).Hp;Reach(g,()=>!g.ResolutionStack.Any(f=>f.Id==frame.Id));Play(g);
  var choice=E<ZhangJiRankChosenEvent>(g).Single();var result=E<ZhangJiJudgmentReturnedEvent>(g).Single();
  Need(choice.Chooser==1 && choice.Rank==7 && result.Rank==7 && result.CardId is not null && V(g,1).Hp==hp-2 && E<DamageAppliedEvent>(g).Count(e=>e.SourceSeat==0 && e.TargetSeat==1 && e.Amount==2)==1 && E<ProgramSkillResolvedEvent>(g).Count(e=>e.FrameId==frame.Id)==1,"A true native final judgment rank matches and pays exactly one two-damage child.");
  Need(g.CardMovements.Count(m=>m.CardId==result.CardId && m.From==CardLocation.Processing && m.To==CardLocation.DiscardPile && m.Reason.Value=="skill-program.ol:lueming.choose-rank.complete-cleanup")==1,"The actual judged result is owned by its native bound cleanup and leaves Processing once after the paid damage tail.");
  Need(!g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="ol:lueming"),"The actual Play usage remains consumed.");_=Cold(g,r);
  var slots=new List<EquipmentSlot>{EquipmentSlot.Weapon};var receipt=new ZhangJiReceipt{Slots=slots};slots.Clear();Need(receipt.Slots.SequenceEqual([EquipmentSlot.Weapon]),"Owning receipt freezes equipment subtype history.");
  Need(!JsonSerializer.Serialize(new ProgramSkillFrame(1,0,"old","old","hash",0,[],[])).Contains("ZhangJi",StringComparison.Ordinal),"Null additive receipts leave old canonical frame JSON unchanged.");
 }
 public static void ActualHistoricalActivationsIssueDistinctNativeEquipmentCold()
 {
  var(z,zr)=Start("match");Play(z);Accept(z,new UseProgramSkillCommand(0,"ol:tunjun-sp","distinct-equipment",[],[1],z.Revision,P(z)!.PromptId));Play(z);Need(!E<ZhangJiNativeUseIssuedEvent>(z).Any() && !z.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="ol:tunjun-sp"),"Zero actual Lueming activations still pays the limited activation without creating an equipment entity.");_=Cold(z,zr);
  var(g,r)=Start("equipment-child");Play(g);Equip(g);Play(g);Lueming(g);Play(g);NextOwnPlay(g);Lueming(g);Play(g);
  Need(E<ZhangJiStartedEvent>(g).Count(e=>!e.Equipment)==2,"Two actual Play activations, including their complete native judgments, form the historical count.");
  var checkpoint=GameCheckpointJson.Serialize(g.CreateCheckpoint());for(var i=0;i<3;i++)_=g.GetHumanLegalActions();Need(GameCheckpointJson.Serialize(g.CreateCheckpoint())==checkpoint,"Legal previews do not sample equipment or consume RNG.");
  Accept(g,new UseProgramSkillCommand(0,"ol:tunjun-sp","distinct-equipment",[],[1],g.Revision,P(g)!.PromptId));Reach(g,()=>P(g)?.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="choose-option")==true || E<ZhangJiNativeUseReturnedEvent>(g).Count()==2);
  g=Cold(g,r);Play(g);var issued=E<ZhangJiNativeUseIssuedEvent>(g).Select(e=>e.Origin).ToArray();var returned=E<ZhangJiNativeUseReturnedEvent>(g).ToArray();
  Need(issued.Length==2 && issued.Select(o=>o.Slot).Distinct().Count()==2 && issued.All(o=>o.Actor==1 && o.Source.OwnerSeat==0) && returned.Length==2 && E<ProgramBindingResolvedEvent>(g).Any(e=>e.SkillId==Watch),"Tunjun issues two different native equipment subtypes to the beneficiary, including an actual equipment-use observer child.");
  Need(issued.All(o=>g.CardMovements.Count(m=>m.Sequence>o.Before && m.CardId==o.CardId && m.From==CardLocation.DrawPile && m.To==CardLocation.Processing)==1) && !g.GetHumanLegalActions().Any(a=>a.ProgramSkillId=="ol:tunjun-sp"),"Each real draw-pile equipment entity pays once and the limited usage cannot refresh.");_=Cold(g,r);
  var(d,dr)=Start("equipment-death");Play(d);Equip(d);Play(d);Lueming(d);Play(d);NextOwnPlay(d);Lueming(d);Play(d);
  Accept(d,new UseProgramSkillCommand(0,"ol:tunjun-sp","distinct-equipment",[],[1],d.Revision,P(d)!.PromptId));Reach(d,()=>P(d)is{PlayerSeat:0} && P(d)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));
  var root=Root(d)!;Need(E<ZhangJiNativeUseIssuedEvent>(d).Count()==1 && root.ZhangJi!.HistoricalCount==2,"The first genuinely issued equipment observer pauses the paid two-use sequence.");d=Cold(d,dr);Answer(d,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");Reach(d,()=>!d.ResolutionStack.Any(f=>f.Id==root.Id));
  Need(!V(d,0).IsAlive && V(d,1).IsAlive && E<ZhangJiNativeUseIssuedEvent>(d).Count()==2 && E<ZhangJiNativeUseReturnedEvent>(d).Count()==2 && E<ZhangJiNativeUseIssuedEvent>(d).All(e=>e.Origin.Actor==1 && e.Origin.Source.OwnerSeat==0) && E<ProgramSkillResolvedEvent>(d).Count(e=>e.FrameId==root.Id)==1,"Actual owner death inside the first equipment use does not cancel the already-paid limited sequence for its living native beneficiary.");_=Cold(d,dr);
 }
 private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static IEnumerable<T>E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>();
 private static ProgramSkillFrame? Root(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().FirstOrDefault(f=>f.ZhangJi is not null);
 private static void Need(bool value,string message){if(!value)throw new InvalidOperationException(message);}
 private static void Accept(GameEngine g,GameCommand c){var result=g.Submit(c);Need(result.Accepted,(result.Error?.Message??"Rejected Zhang Ji command")+" "+JsonSerializer.Serialize(P(g)));}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> choose){var p=P(g)!;Accept(g,new AnswerPromptCommand(p.PlayerSeat,p.PromptId,p.Choices.First(choose).Id,g.Revision));}
 private static void Play(GameEngine g)=>Reach(g,()=>Root(g)is null && P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void Equip(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,Driver,"equip",[],[0],g.Revision,P(g)!.PromptId));
 private static void Lueming(GameEngine g)=>Accept(g,new UseProgramSkillCommand(0,"ol:lueming","choose-rank",[],[1],g.Revision,P(g)!.PromptId));
 private static void NextOwnPlay(GameEngine g){var n=E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0);Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard} && E<TurnStartedEvent>(g).Count(e=>e.ActorSeat==0)>n);}
 private static void Reach(GameEngine g,Func<bool> done)
 {
  for(var i=0;i<220;i++)
  {
   if(done())return;var p=P(g);
   if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
   else if(p is{PlayerSeat:0} && p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"))Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
   else if(p is{PlayerSeat:0,Kind:DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification})Answer(g,c=>c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
   else Accept(g,new AdvanceOneStepCommand(g.Revision));
  }
  throw new InvalidOperationException("Bounded Zhang Ji boundary missing: "+JsonSerializer.Serialize(new{Prompt=P(g),Frames=g.ResolutionStack,Players=Enumerable.Range(0,4).Select(s=>V(g,s)),Recent=g.Events.TakeLast(12).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))}));
 }
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())).ToArray(),g.CardMovements,Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())).ToArray(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(cold),"Cold command replay retains exact original rank, native result, entity payment and equipment subtype cursor.");return cold;}
 private static string Embedded(string suffix){using var s=typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-zhang-ji."+suffix)!;using var reader=new StreamReader(s);return reader.ReadToEnd();}
 private static (GameEngine,ContentRegistry)Start(string scenario)
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scenario));var game=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scenario is "death" or "equipment-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},registry);
  Accept(game,new StartGameCommand());Reach(game,()=>P(game)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(game,new SelectGeneralCommand(0,Owner,game.Revision,P(game)!.PromptId));return(game,registry);
 }
 private sealed class Fixture(string scenario):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("zhang-ji-fixture",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   void Add(SkillProgramCatalog catalog){foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id],SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>id==Quiet?10000d:-10000d)});}
   Add(SkillProgramCatalog.Load(Embedded("rules.json"),Embedded("presentation.json")));
   var defs=new List<string>{$$"""{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"priority":0,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}""",
    $$"""{"id":"{{Driver}}","revision":1,"activations":[{"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"actual-equipment"}]}]}"""};
   var skills=new List<string>{"ol:lueming","ol:tunjun-sp"};
   if(scenario is "loss" or "death")
   {
    skills.Add(Watch);defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.zhang-ji.lueming.gain"],"optional":false,"priority":0,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");
    if(scenario=="loss"){skills.Add(Loss);defs.Add($$"""{"id":"{{Loss}}","revision":1,"triggers":[{"id":"actual-source-removal","window":"dyingEntering","subject":"owner","optional":false,"priority":1000,"usageScope":"game","usageLimit":1,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":12,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:lueming"],"sourceBind":"standard:none"}]}]}""");}
   }
   if(scenario=="equipment-child")defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-equipment-observer","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["crossbow","baguaFormation"],"optional":false,"priority":0,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"draw","target":"owner","amount":1}]}]}""");
   if(scenario=="equipment-death")
   {
    skills.Add(Watch);defs.Add($$"""{"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-equipment-owner-death","window":"cardUseCompleted","ownerRelation":"observer","cardKinds":["crossbow","baguaFormation"],"condition":{"kind":"cardUseConversionSkillIs","conversionSkillId":"ol:tunjun-sp"},"optional":false,"priority":0,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}""");
   }
   var labels=defs.Select(d=>(Id:JsonDocument.Parse(d).RootElement.GetProperty("id").GetString()!,Choice:d.Contains("chooseOption",StringComparison.Ordinal))).ToDictionary(x=>x.Id,x=>new{name=x.Id,description="原生张济行为夹具",optionLabels=x.Choice?new Dictionary<string,string>{["continue"]="继续"}:new Dictionary<string,string>()});
   Add(SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{{string.Join(',',defs)}}]}""",JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels})));
   b.AddGeneral(new(Owner,"真实张济拥有者","supporter",Driver,"qun",12,skills,GeneralGender.Male));var peers=Enumerable.Range(1,3).Select(i=>$"fixture:zhang-ji-peer-{i}").ToArray();foreach(var id in peers)b.AddGeneral(new(id,"安静原生同桌","supporter",Quiet,"wei",12,scenario=="equipment-child"?[Watch]:[]));
   var rank=scenario is "match" or "equipment-child" or "equipment-death"?7:6;
   b.AddDeck(new("fixture:zhang-ji-deck","固定实际牌与不同装备副类型",4,0,[]){PhysicalCards=Enumerable.Range(0,128).Select(_=>new ContentDeckPhysicalCard("standard:slash",Suit.Spade,rank)).Concat(Enumerable.Range(0,12).Select(_=>new ContentDeckPhysicalCard("standard:crossbow",Suit.Club,rank))).Concat(Enumerable.Range(0,12).Select(_=>new ContentDeckPhysicalCard("standard:bagua",Suit.Heart,rank))).ToArray()});
   b.AddMode(new(Mode,"真实张济夹具",4,4,new Dictionary<string,int>{[nameof(Role.Lord)]=1,[nameof(Role.Rebel)]=3},"fixture:zhang-ji-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
