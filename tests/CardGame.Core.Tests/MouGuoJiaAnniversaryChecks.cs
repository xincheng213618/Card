using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouGuoJiaAnniversaryChecks
{
 private const string Hero="mou:guo-jia",Plain="fixture:mgj-anniv-plain",Ally="fixture:mgj-anniv-ally",Actor="fixture:mgj-anniv-actor",Quiet="fixture:mgj-anniv-quiet",Shield="fixture:mgj-anniv-shield",Driver="fixture:mgj-anniv-driver",Idle="fixture:mgj-anniv-idle",Deck="fixture:mgj-anniv-deck";
 public static void PrivateConversionCurrentYijiNativeLightningLunshiAndCold()
 {
  var registry=ContentRegistry.Build(new StandardContentPackage(),new Fixture());
  var setup=new ShanheBattleSetup(7,1,"mou-guo-jia-anniversary-native",Hero,Hp:12,MaxHpBonus:9,InitialHandBonus:0,DrawBonus:0,SlashBonus:0,ExtraSkillIds:[Driver],
   StartingCards:[new(CardKind.Dodge,Suit.Heart,2),new(CardKind.Dodge,Suit.Diamond,3),new(CardKind.Dodge,Suit.Club,4),new(CardKind.Dodge,Suit.Spade,6)],StartingEquipment:[],
   EnemyRoster:[new(Actor,Hp:14){StartingHand=[new(CardKind.DrawTwo,Suit.Spade,6),new(CardKind.Duel,Suit.Spade,7)]},new(Plain,Hp:14){StartingHand=[]}],
   AllyRoster:[new(Ally,Hp:14){StartingHand=[]}],CampaignHash:new string('b',64));
  var game=GameEngine.CreateStandard(new(){Seed=setup.BattleSeed,PlayerCount=4,HumanSeat=0,HumanRole=null,HumanTeamId=ShanheBattleSetup.PlayerTeamId,ModeId=ShanheBattleSetup.BattleModeId,ShanheBattle=setup,UseInteractiveSetup=false,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=10},registry);
  PendingDecision? P()=>Enumerable.Range(0,4).Select(s=>game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
  PlayerSnapshot V(int seat=0)=>game.CreateSnapshot(0).Players[seat];T[] E<T>()=>game.Events.Select(e=>e.Payload).OfType<T>().ToArray();
  ProgramSkillFrame? F()=>game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.MouGuoJia is not null);
  string Diagnostic()=>JsonSerializer.Serialize(new{game.State.TurnNumber,game.State.CurrentSeat,game.State.Phase,Prompt=P(),Players=game.CreateSnapshot(0).Players.Select(p=>new{p.Seat,p.GeneralId,p.Hp,p.HandCount}),Frames=game.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Facts=game.Events.TakeLast(34).Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType()))});
  void Need(bool yes,string text){if(!yes)throw new InvalidOperationException(text+Diagnostic());}
  void Submit(GameCommand command){try{var result=game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Actual native command rejected.");}catch(Exception ex){throw new InvalidOperationException(ex.Message+Diagnostic(),ex);}}
  void Answer(Func<PromptChoice,bool> match){var p=P();Need(p?.PlayerSeat==0,"Only the true Human viewer or responder is answered.");var c=p!.Choices.FirstOrDefault(match);Need(c is not null,"The exact actual Guo Jia choice is published.");Submit(new AnswerPromptCommand(0,p.PromptId,c!.Id,game.Revision));}
  var skipYiji=false;
  void Reach(Func<bool> done,[CallerLineNumber]int line=0)
  {
   for(var i=0;i<180;i++)
   {
    if(done())return;Need(game.State.Winner==Winner.None,"Unexpected terminal state at "+line);
    if(P()is{PlayerSeat:0,Kind:DecisionKind.RespondDodge or DecisionKind.RespondSlash}){Answer(c=>c.Cards.Count==0);continue;}
    if(skipYiji&&P()?.SkillPrompt?.SkillId=="mou-guo-jia:yiji"){Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="skip");continue;}
    if(P()is{PlayerSeat:0})throw new InvalidOperationException("Unexpected actual Human boundary at "+line+Diagnostic());Submit(new AdvanceOneStepCommand(game.Revision));
   }
   throw new InvalidOperationException("Bounded actual Guo Jia flow not reached at "+line+Diagnostic());
  }
  void Play()=>Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
  void ActivateXianmou(){Reach(()=>P()?.SkillPrompt?.SkillId=="mou:xianmou");Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");}
  Submit(new StartGameCommand());Reach(()=>F()?.MouGuoJia?.Stage==MouGuoJiaStage.Opening&&P()?.PlayerSeat==0);Answer(c=>c.Parameters.GetValueOrDefault("branch")=="yang");Play();
  var firstTurn=game.State.TurnNumber;Need(V(1).GeneralId==Ally&&V(2).GeneralId==Actor,"The actual fixed roster provides one legal foreign Duel target and a quiet shielded ally.");
  var drop=V().Hand.Where(c=>c.Suit is Suit.Spade or Suit.Club).Take(2).Select(c=>c.Id).ToArray();Submit(new UseProgramSkillCommand(0,Driver,"drop",drop,[],game.Revision,P()!.PromptId));Play();
  Need(V().Hand.Count(c=>c.Suit is Suit.Heart or Suit.Diamond)==2&&V().Hand.Count(c=>c.Suit is Suit.Spade or Suit.Club)==2,"The actual two-card batch produces two losses and leaves the real hand red/black balanced.");
  Submit(new EndPlayPhaseCommand(0,game.Revision,P()!.PromptId));ActivateXianmou();Reach(()=>F()?.MouGuoJia is{Stage:MouGuoJiaStage.Select,Yin:false}&&P()?.PlayerSeat==0);
  var first=F()!;var viewed=first.MouGuoJia!.Viewed.Select(c=>c.Id).ToArray();
  Need(first.MouGuoJia.X==2&&viewed.Length==5&&E<MouGjLossSampleEvent>().Single(e=>e.Turn==firstTurn&&e.Owner==0).Count==2&&viewed.All(id=>game.CreateSnapshot(0).PrivateRevealedCards?.Any(c=>c.Id==id)==true)&&game.CreateSnapshot(2).PrivateRevealedCards is null,"Yang uses the actual ended turn's frozen two-card loss count and privately views the real top five without exposing the entities to another viewer.");
  Answer(c=>c.Parameters.GetValueOrDefault("branch")=="add"&&c.Cards.SequenceEqual([viewed[0]]));var ownerBefore=SnapshotJson.Serialize(game.CreateSnapshot(0));var peerBefore=SnapshotJson.Serialize(game.CreateSnapshot(2));var moveBefore=game.CardMovements.Count;
  game=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())),registry);
  Need(ownerBefore==SnapshotJson.Serialize(game.CreateSnapshot(0))&&peerBefore==SnapshotJson.Serialize(game.CreateSnapshot(2))&&F()?.Id==first.Id&&F()!.MouGuoJia!.Selected.SequenceEqual([viewed[0]])&&F()!.MouGuoJia!.Viewed.Select(c=>c.Id).SequenceEqual(viewed)&&game.CardMovements.Count==moveBefore,"Representative Cold preserves the exact original private top five, partial selection and ended-turn source, without obtaining or reordering an unfinished selection.");
  Answer(c=>c.Parameters.GetValueOrDefault("branch")=="remove"&&c.Cards.SequenceEqual([viewed[0]]));Answer(c=>c.Parameters.GetValueOrDefault("branch")=="done");
  Need(E<MouGjYijiGrantEvent>().Any(e=>e.FrameId==first.Id&&e.Granted)&&E<MouGjMovementPaidEvent>().All(e=>e.FrameId!=first.Id),"Choosing zero is a real below-X Yang result and grants the independently sourced current Yiji without inventing Draw or a fake gain.");
  Reach(()=>P()is{PlayerSeat:0,Kind:DecisionKind.Nullification}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")=="mou:lunshi"));
  var window=game.ResolutionStack.OfType<NullificationWindowFrame>().Last();var original=game.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==window.ParentFrameId);var originalId=original.Id;var originalAction=original.Action!.ActionId;
  Need(original.SourceSeat==2&&original.CardKind==CardKind.Duel&&original.TargetSeats.SequenceEqual([0])&&window.TargetSeats.SequenceEqual([0]),"Lunshi is offered for the original actual other actor's ordinary Trick against another role.");
  var nullChoice=P()!.Choices.First(c=>c.Parameters.GetValueOrDefault("conversion-skill-id")=="mou:lunshi");var material=nullChoice.Cards.Single();Answer(c=>c.Id==nullChoice.Id);
  Reach(()=>P()?.SkillPrompt?.SkillId=="mou:xianmou");
  var unrespondable=E<UnrespondableCounterspellIssuedEvent>().Single(e=>e.Source.SkillId=="mou:lunshi");var accepted=E<CardActionAcceptedEvent>().Single(e=>e.Action.ActionId==unrespondable.ActionId).Action;
  Need(accepted.EffectiveKind==CardKind.Nullification&&accepted.ActorSeat==0&&accepted.ProviderSeat==0&&accepted.ParentActionId==originalAction&&accepted.ConversionChain.Contains(unrespondable.Source)&&accepted.PhysicalCards is[{CardId:var real,From:var from}]&&real==material&&from==CardLocation.Hand(0)&&game.CardMovements.Count(m=>m.CardId==material&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.Processing&&m.Reason==CardMoveReasons.Nullification)==1&&game.CardMovements.Count(m=>m.CardId==material&&m.From==CardLocation.Processing&&m.To==CardLocation.DiscardPile&&m.Reason==CardMoveReasons.NullificationFinished)==1&&E<CardUseFinishedEvent>().Count(e=>e.ResolutionId==originalId)==1&&V().Hp==12,"The selected single Hand entity truly pays Lunshi's native Nullification once, carries only its own unrespondable source and finishes the same original Duel without damage.");
  Need(E<CardUseDeclaredEvent>().Any(e=>e.SourceSeat==2&&e.CardKind==CardKind.DrawTwo)&&E<UnrespondableCounterspellIssuedEvent>().Count(e=>e.Source.SkillId=="mou:lunshi")==1,"The actor's actual self-targeted DrawTwo does not create an additional Lunshi counterspell.");
  ActivateXianmou();Reach(()=>F()?.MouGuoJia is{Stage:MouGuoJiaStage.Target,Yin:true}&&P()?.PlayerSeat==0);Answer(c=>c.Targets.SequenceEqual([2]));Reach(()=>F()?.MouGuoJia is{Stage:MouGuoJiaStage.Select,Yin:true});var yin=F()!;var yinCard=yin.MouGuoJia!.Viewed.First().Id;
  Need(yin.MouGuoJia.X==1&&game.CreateSnapshot(0).PrivateRevealedCards?.Any(c=>c.Id==yinCard)==true&&game.CreateSnapshot(3).PrivateRevealedCards is null,"The foreign actual turn freezes the one genuine Lunshi cost loss and Yin privately inspects the chosen other hand.");
  Answer(c=>c.Parameters.GetValueOrDefault("branch")=="add"&&c.Cards.SequenceEqual([yinCard]));Answer(c=>c.Parameters.GetValueOrDefault("branch")=="done");
  Reach(()=>P()?.SkillPrompt?.SkillId=="mou-guo-jia:yiji");var judgment=E<MouGjJudgmentReturnedEvent>().Single(e=>e.FrameId==yin.Id);
  Need(judgment.Suit==Suit.Spade&&judgment.Rank==5&&E<JudgmentResolvedEvent>().Single(e=>e.ResolutionId==judgment.JudgmentId)is{Reason:JudgmentReasons.Lightning,Succeeded:true}&&E<MouGjLightningIssuedEvent>().Count(e=>e.FrameId==yin.Id&&e.Amount==3)==1&&E<DamageAppliedEvent>().Any(e=>e.TargetSeat==0&&e.Amount==3&&e.Nature==DamageNature.Thunder&&e.SourceLess)&&V().Hp==9&&game.CardMovements.Count(m=>m.CardId==yinCard&&m.From==CardLocation.Hand(2)&&m.To==CardLocation.DiscardPile)==1,"Yin pays one real target Hand discard and runs its native finalized Lightning judgment; the actual Spade5 causes exactly three source-less Thunder damage and retains Yiji through this Yin activation.");
  Answer(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");Reach(()=>F()?.MouGuoJia?.Stage==MouGuoJiaStage.YijiCard);var yiji=F()!;var topTwo=yiji.MouGuoJia!.Viewed.Select(c=>c.Id).ToArray();var yijiMovementStart=game.CardMovements.Last().Sequence;
  Need(topTwo.Length==2&&topTwo.All(id=>game.CreateSnapshot(0).PrivateRevealedCards?.Any(c=>c.Id==id)==true),"The derived current Yiji views two genuine top entities rather than drawing two unrelated Hand cards.");
  Answer(c=>c.Cards.SequenceEqual([topTwo[0]]));Answer(c=>c.Targets.SequenceEqual([1]));Reach(()=>F()?.Id==yiji.Id&&F()?.MouGuoJia?.Stage==MouGuoJiaStage.YijiCard);Answer(c=>c.Cards.SequenceEqual([topTwo[1]]));Answer(c=>c.Targets.SequenceEqual([3]));skipYiji=true;Play();
  var gifts=E<MouGjMovementPaidEvent>().Where(e=>e.FrameId==yiji.Id).OrderBy(e=>e.Cursor).ToArray();var recipients=new[]{1,3};
  Need(gifts.Length==2&&gifts.Select(e=>e.Cursor).SequenceEqual([0,1])&&gifts.All(e=>e.Actual==1&&e.After>e.Before)&&Enumerable.Range(0,2).All(i=>game.CardMovements.Count(m=>m.Sequence>yijiMovementStart&&m.CardId==topTwo[i]&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(recipients[i]))==1&&game.CardMovements.Count(m=>m.Sequence>gifts[i].Before&&m.Sequence<=gifts[i].After&&m.CardId==topTwo[i]&&m.From==CardLocation.DrawPile&&m.To==CardLocation.Hand(recipients[i])&&m.Reason==new CardMoveReason("skill-program.mou-guo-jia:yiji.give"))==1)&&E<MouGjStartedEvent>().Count(e=>e.FrameId==yiji.Id&&e.Source==yiji.MouGuoJia!.Source&&e.Operation==SkillProgramEffectOp.ResolveMouGuoJiaYiji)==1,"Each viewed original Yiji entity is genuinely given from DrawPile to its separately selected recipient once, matching its own Cursor and native movement Before/After receipt on the same issued Yiji source.");
  Need(game.CardMovements.All(m=>m.Sequence<=yijiMovementStart||!topTwo.Contains(m.CardId)||m.To!=CardLocation.Hand(0))&&E<MouGjCompletedEvent>().Count(e=>e.FrameId==yiji.Id)==1&&E<MouGjYijiGrantEvent>().Count(e=>!e.Granted&&e.Count>0)==0,"The two real Yiji gifts never route through the owner's Hand and finish their owning frame once; Yin never removes the prior Yang grant source.");
  var finalTurn=game.State.TurnNumber;var finalDrop=V().Hand.First().Id;Submit(new UseProgramSkillCommand(0,Driver,"drop",[finalDrop],[],game.Revision,P()!.PromptId));Play();Submit(new EndPlayPhaseCommand(0,game.Revision,P()!.PromptId));ActivateXianmou();Reach(()=>F()?.MouGuoJia is{Stage:MouGuoJiaStage.Select,Yin:false});var last=F()!;
  Need(last.MouGuoJia!.X==1&&E<MouGjYijiGrantEvent>().Count(e=>e.FrameId==last.Id&&!e.Granted&&e.Count==1)==1,"Only this subsequent actual Yang activation retracts the exact old Xianmou-source Yiji grant.");
  var gain=last.MouGuoJia.Viewed.First().Id;Answer(c=>c.Parameters.GetValueOrDefault("branch")=="add"&&c.Cards.SequenceEqual([gain]));Answer(c=>c.Parameters.GetValueOrDefault("branch")=="done");
  Need(E<MouGjMovementPaidEvent>().Single(e=>e.FrameId==last.Id).Actual==1&&V().Hand.Any(c=>c.Id==gain)&&E<MouGjYijiGrantEvent>().All(e=>e.FrameId!=last.Id||!e.Granted)&&E<MouGjCompletedEvent>().Count(e=>e.FrameId==last.Id)==1&&E<MouGjLossSampleEvent>().Single(e=>e.Turn==finalTurn&&e.Owner==0).Count==1,"The renewed Yang actually gains its selected original top entity, reaches X and issues no replacement Yiji; the exact native source and once-only completion survive the earlier Cold.");
 }
 private sealed class Fixture:IGameContentPackage
 {
  public PackageManifest Manifest {get;}=new("fixture-mou-guo-jia-anniversary",new(1,0,0));
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(b,Hero);foreach(var kind in new[]{CardKind.Dodge,CardKind.DrawTwo,CardKind.Duel}){var c=CardCatalog.Get(kind);b.AddCard(new("fixture:mgj-anniv-"+kind,c.DisplayName,c.CategoryName,c.Description,kind));}
   var rules=$$"""
    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
     {"id":"{{Driver}}","revision":1,"activations":[{"id":"drop","minCards":1,"maxCards":2,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"drop"},{"op":"moveBoundCards","target":"owner","sourceBind":"drop","destination":"discardPile","awaitMovementTriggers":true}]}]},
     {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"selectOwnedCards","target":"owner","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"empty"},{"op":"moveBoundCards","target":"owner","sourceBind":"empty","destination":"discardPile","awaitMovementTriggers":true},{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
     {"id":"{{Shield}}","revision":1,"cardPolicies":[{"id":"black-trick-shield","kind":"prohibitBlackTrickTarget"}]}
    ]}
    """;
   var catalog=SkillProgramCatalog.Load(rules,JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=new Dictionary<string,object>{[Driver]=new{name="真实失牌输入",description="将选定真实手牌移动到弃牌堆。"},[Quiet]=new{name="安静同行",description="正常摸牌后真实弃手并跳过出牌。"},[Shield]=new{name="黑锦囊帷幕",description="防止黑色锦囊指定，固定实际决斗目标。"}}}));
   foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,catalog.Presentations[id].Name,catalog.Presentations[id].Description){Program=p,ProgramPresentation=catalog.Presentations[id]});b.AddSkill(new(Idle,"实际普通角色","无主动技能"));b.AddGeneral(new(Actor,"实际锦囊使用者","",Idle,"shu",14));b.AddGeneral(new(Plain,"实际安静角色","",Quiet,"shu",14));b.AddGeneral(new(Ally,"实际安静盟友","",Quiet,"wei",14,[Shield]));
   b.AddDeck(new(Deck,"固定黑桃五闪牌堆",0,2,[]){PhysicalCards=Enumerable.Range(0,100).Select(_=>new ContentDeckPhysicalCard("fixture:mgj-anniv-"+CardKind.Dodge,Suit.Spade,5)).ToArray()});b.AddMode(new(ShanheBattleSetup.BattleModeId,"谋郭嘉真实转换与不可响应无懈",4,4,new Dictionary<string,int>(),Deck,GeneralPoolIds:[Hero,Ally,Actor,Plain],ModeKind:ContentModeKind.Team,TeamCounts:new Dictionary<string,int>{[ShanheBattleSetup.PlayerTeamId]=2,[ShanheBattleSetup.EnemyTeamId]=2}));
  }
 }
}
