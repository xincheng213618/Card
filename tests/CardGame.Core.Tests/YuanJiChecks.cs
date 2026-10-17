using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class YuanJiChecks
{
 private const string Owner="fixture:yj-owner",Mode="identity:classic-yuan-ji",Driver="fixture:yj-driver",Watch="fixture:yj-watch",Jie="ol:jieyan",Mirror="ol:jinghua",Water="ol:shuiyue";
 private static string Scene="";
 public static void JieyanAtomicHandPairAndTwoEndsSelfCold()
 {
  foreach(var scene in new[]{"pair-other","pair-self","pair-give","pair-mixed","pair-split"})
  {
   var(g,r)=Start(scene);Play(g);var before=E<YuanJiStartedEvent>(g).Length;var target=scene=="pair-self"?0:1;
   if(scene=="pair-mixed"){Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);Use(g,"mixed",[0]);var equipment=V(g,0).Equipment.Single().Id;Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Cards.SequenceEqual([equipment])));Answer(g,c=>c.Cards.SequenceEqual([equipment]));Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"&&c.Cards.Count==1);Play(g);}
   else if(scene=="pair-split"){Use(g,"split",[1]);Play(g);}
   else if(scene=="pair-give"){Use(g,"give-two",[1],V(g,0).Hand.Take(2).Select(c=>c.Id).ToArray());Reach(g,()=>Optional(g,Jie));}
   else{Use(g,"discard-two",[target]);ChooseOwned(g,2);Reach(g,()=>Optional(g,Jie));}
   if(scene is"pair-mixed"or"pair-split"){Need(E<YuanJiLossOpportunityEvent>(g).Length==0&&E<YuanJiStartedEvent>(g).Length==before,"Hand1+Equipment1 and two independent Hand1 losses never become one atomic Hand2 opportunity.");_=Cold(g,r);continue;}
   g=Cold(g,r);Activate(g);Play(g);var started=E<YuanJiStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair);var draws=E<YuanJiDrawCompletedEvent>(g).Where(e=>e.FrameId==started.FrameId).ToArray();var shown=E<YuanJiShownEvent>(g).Where(e=>e.FrameId==started.FrameId).ToArray();
   var loser=scene=="pair-give"?0:target;Need(started.LoserSeat==loser&&draws.Length==2&&draws.Select(d=>d.RecipientSeat).SequenceEqual(new[]{0,loser})&&draws.Select(d=>d.Top).SequenceEqual([true,false])&&draws.All(d=>d.Requested==1&&d.Actual==1)&&shown.Length==2&&shown.Select(s=>s.CardId).Distinct().Count()==2&&E<NativeDrawInvocationRecordedEvent>(g).Count(d=>d.ParentFrameId==started.FrameId&&d.Requested==1&&d.ActualCount==1)==2,"One exact atomic Hand2 issues two separate opposite-end native Draw1 calls, including both when the participants are the same person.");
   Need(E<CardsRevealedEvent>(g).Count(e=>e.ResolutionId==started.FrameId)==2&&E<YuanJiJieyanDisabledEvent>(g).Length==0,"Both real original entities are publicly shown; same suits preserve the skill.");
   Use(g,"discard-two",[1]);ChooseOwned(g,2);Reach(g,()=>Optional(g,Jie));Activate(g);Play(g);Need(E<YuanJiCompletedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair)==2,"Matching suits allow another real atomic-loss activation in the same actual turn.");_=Cold(g,r);
  }
 }
 public static void JieyanSuitLockReshuffleAndPaidLossDeathCold()
 {
  foreach(var scene in new[]{"pair-mismatch","pair-reshuffle","pair-loss","pair-death"})
  {
   var(g,r)=Start(scene);Play(g);
   if(scene=="pair-mismatch")
   {
    Use(g,"stock");Play(g);for(var seat=1;seat<=3;seat++){Use(g,"take-all",[seat]);ChooseOwned(g,V(g,seat).HandCount);Play(g);}var hearts=V(g,0).Hand.Where(c=>c.Suit==Suit.Heart).Take(2).Select(c=>c.Id).ToArray();var heart=hearts[0];var spade=V(g,0).Hand.First(c=>c.Suit==Suit.Spade).Id;
    Use(g,"top",cards:[hearts[1]]);Play(g);Use(g,"bottom",cards:[heart]);Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("end")=="bottom"));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("end")=="bottom");Play(g);Need(V(g,0).Hand.Any(c=>c.Id==hearts[1])&&!V(g,0).Hand.Any(c=>c.Id==heart),"The real native deck-end exchange puts the chosen Heart at bottom and draws the distinct prepared top buffer.");Use(g,"top",cards:[spade]);Play(g);Use(g,"discard-two",[0]);ChooseOwned(g,2);Reach(g,()=>Optional(g,Jie));Activate(g);Play(g);
    var lockFact=E<YuanJiJieyanDisabledEvent>(g).Single();Need(lockFact.FirstSuit==Suit.Spade&&lockFact.SecondSuit==Suit.Heart,"Actual prepared top and bottom suits freeze the mismatch instead of borrowing discarded cost suits.");Use(g,"discard-two",[0]);ChooseOwned(g,2);Play(g);Need(E<YuanJiStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair)==1,"The same actual-turn mismatch suppresses the next atomic loss.");g=Cold(g,r);Use(g,"extra");Play(g);End(g);Play(g);Use(g,"discard-two",[0]);ChooseOwned(g,2);Reach(g,()=>Optional(g,Jie));Need(g.State.TurnNumber!=lockFact.ActualTurn,"A genuine next actual turn restores the opportunity.");Skip(g);_=Cold(g,r);continue;
   }
   if(scene=="pair-reshuffle"){Use(g,"draw-one");Play(g);}Use(g,"discard-two",[1]);ChooseOwned(g,2);Reach(g,()=>Optional(g,Jie));Activate(g);
   if(scene is"pair-loss"or"pair-death"){Reach(g,()=>WatchContinue(g));Need(E<YuanJiShownEvent>(g).Length==1&&E<YuanJiDrawCompletedEvent>(g).Length==1,"The first real draw opens its paid native child before the second draw is issued.");g=Cold(g,r);Continue(g);}
   if(scene=="pair-death"){Reach(g,()=>!V(g,0).IsAlive&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.YuanJi is not null));Need(E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0)&&E<YuanJiDrawCompletedEvent>(g).Length==1&&E<YuanJiCompletedEvent>(g).Length==1,"Real non-Lord owner death drains the paid first draw once and does not issue a new draw for the dead owner.");}
   else{Play(g);Need(E<YuanJiDrawCompletedEvent>(g).Length==2&&E<YuanJiCompletedEvent>(g).Length==1,"Paid source loss and native reshuffle return without repeating either draw.");if(scene=="pair-loss")Need(V(g,0).Skills!.All(s=>s.ContentId!=Jie),"The conditional original Jieyan source is genuinely lost before the second paid draw still completes.");if(scene=="pair-reshuffle"){var second=E<YuanJiDrawCompletedEvent>(g).Last();Need(g.CardMovements.Any(m=>m.Sequence>second.Before&&m.Sequence<=second.After&&m.From==CardLocation.DiscardPile&&m.To==CardLocation.DrawPile&&m.Reason==CardMoveReasons.Reshuffle),"Only the real second end-draw reshuffles the exhausted deck and keeps its native whole invocation.");}}
   _=Cold(g,r);
  }
 }
 public static void JinghuaRoundMarksTurnBottomAndResidencyCold()
 {
  foreach(var scene in new[]{"mirror-reenter","mirror-foreign","mirror-loss"})
  {
   var(g,r)=Start(scene,true);Reach(g,()=>WatchContinue(g)||P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==Watch&&p.Choices.Any(c=>c.Targets.SequenceEqual([1])));var draw=E<YuanJiStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ResolveJinghuaRoundDraw);var marks=E<YuanJiAcquiredEvent>(g).Where(e=>e.FrameId==draw.FrameId).Select(e=>e.CardId).ToArray();Need(marks.Length==2&&marks.All(id=>V(g,0).Hand.Single(c=>c.Id==id).SkillMarkLabel=="镜花")&&Enumerable.Range(1,3).All(s=>(g.CreateSnapshot(s).Players[0].Hand?.Count??0)==0),"The exact private native Draw2 marks only its original owner's visible Hand entities.");
   if(scene!="mirror-loss")
   {
    Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Cards.SequenceEqual([marks[0]])));Answer(g,c=>c.Cards.SequenceEqual([marks[0]]));Reach(g,()=>WatchContinue(g));Need(V(g,1).Hand.Single(c=>c.Id==marks[0]).SkillMarkLabel is null,"A foreign holder does not inherit the original mark.");g=Cold(g,r);Continue(g);Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Cards.Count==1));Answer(g,c=>c.Cards.Count==1&&(scene=="mirror-reenter"?c.Cards[0]==marks[0]:!marks.Contains(c.Cards[0])));
    Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==Watch&&p.Choices.Any(c=>c.Targets.SequenceEqual([1])));Answer(g,c=>c.Targets.SequenceEqual([1]));Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"&&c.Cards.Count==1&&!marks.Contains(c.Cards[0])));var ordinary=P(g)!.Choices.First(c=>c.Cards.Count==1&&!marks.Contains(c.Cards[0])).Cards[0];Answer(g,c=>c.Cards.SequenceEqual([ordinary]));Reach(g,()=>WatchContinue(g));Need(V(g,1).Hand.Single(c=>c.Id==ordinary).SkillMarkLabel is null,"The second physical gain batch of the same native Draw2 has its own genuine watcher, using an ordinary entity without changing original mark residency.");g=Cold(g,r);Continue(g);Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Cards.SequenceEqual([ordinary])));Answer(g,c=>c.Cards.SequenceEqual([ordinary]));
   }
   else{g=Cold(g,r);Continue(g);}
   Reach(g,()=>Current(g)is{Stage:YuanJiStage.ReturnOrder,Op:SkillProgramEffectOp.ResolveJinghuaTurnReturn});var returnFrame=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f=>f.YuanJi?.Op==SkillProgramEffectOp.ResolveJinghuaTurnReturn);Need(returnFrame.YuanJi!.Claims.Count==2&&returnFrame.YuanJi.Eligible.Count==(scene=="mirror-foreign"?1:2),"The true own-turn-start return freezes original claims and current own-Hand residency, not all global holders.");g=Cold(g,r);OrderAll(g);Play(g);
   var paid=E<YuanJiReturnPaidEvent>(g).Single();Need(!paid.Top&&paid.Count==(scene=="mirror-foreign"?1:2)&&E<YuanJiMarkClosedEvent>(g).Length==2&&g.CardMovements.Count(m=>m.Sequence>paid.Before&&m.Sequence<=paid.After&&m.From==CardLocation.Hand(0)&&m.To==CardLocation.DrawPile)==paid.Count,"The selected original marks return in one true bottom batch and every issued claim expires exactly once.");
   if(scene=="mirror-foreign")Need(V(g,1).Hand.Any(c=>c.Id==marks[0])&&E<YuanJiMarkClosedEvent>(g).Single(e=>e.CardId==marks[0]).WasInHand==false,"A foreign-held marked entity is expired without being stolen back.");if(scene=="mirror-loss")Need(V(g,0).Skills!.All(s=>s.ContentId!=Mirror),"Loss of the issuing skill cannot erase its already issued turn-start return.");
   Use(g,"extra");Play(g);End(g);Play(g);Need(E<YuanJiStartedEvent>(g).Count(e=>e.Op==SkillProgramEffectOp.ResolveJinghuaRoundDraw)==1&&E<YuanJiReturnPaidEvent>(g).Length==1,"An actual same-round extra turn neither redraws nor reclaims the expired previous invoice.");_=Cold(g,r);
  }
 }
 public static void ShuiyueActualTurnDrawOldRoundTopAndChildrenCold()
 {
  foreach(var scene in new[]{"water","water-extra","water-loss","water-multiple"})
  {
   var(g,r)=Start(scene);Play(g);if(scene=="water-extra"){Use(g,"extra");Play(g);}End(g);Reach(g,()=>WatchContinue(g));var draw=E<YuanJiStartedEvent>(g).Single(e=>e.Op==SkillProgramEffectOp.ResolveShuiyueTurnDraw&&e.Source.OwnerSeat==0);Need(draw.Window==SkillProgramTriggerWindow.AfterTurnEnded&&E<TurnEndedEvent>(g).Any(e=>e.ActorSeat==0&&e.TurnNumber==draw.ActualTurn)&&E<YuanJiAcquiredEvent>(g).Where(e=>e.FrameId==draw.FrameId).All(e=>V(g,0).Hand.Single(c=>c.Id==e.CardId).SkillMarkLabel=="水月"),"Water is a genuine own actual-turn-end Draw2 with original private marked entities.");g=Cold(g,r);Continue(g);
   if(scene=="water-extra"){Play(g);Need(E<RoundEndedEvent>(g).Length==0,"A pending live extra turn cannot close the old round.");End(g);}
   Reach(g,()=>P(g)is{PlayerSeat:0}&&Current(g)is{Stage:YuanJiStage.ReturnOrder,Op:SkillProgramEffectOp.ResolveShuiyueRoundReturn});var oldRound=E<RoundStartedEvent>(g).Last().RoundNumber;Need(g.ResolutionStack.OfType<RoundEndingBoundaryFrame>().Any()&&E<RoundEndedEvent>(g).Length==0,"The old round remains live while its real RoundEnding mother owns the top return.");var f=g.ResolutionStack.OfType<ProgramSkillFrame>().Single(p=>p.YuanJi?.Op==SkillProgramEffectOp.ResolveShuiyueRoundReturn&&p.OwnerSeat==0);Need(f.YuanJi!.Eligible.Count==(scene=="water-extra"?4:2),"Every marked original from multiple actual turns belongs to one whole owner/skill return.");g=Cold(g,r);OrderAll(g);
   if(scene!="water-extra"){Reach(g,()=>Optional(g,Jie));var returnPaid=E<YuanJiReturnPaidEvent>(g).Single(e=>e.FrameId==f.Id);Need(returnPaid.Top&&returnPaid.Count==2&&E<RoundEndedEvent>(g).Length==0&&E<RoundStartedEvent>(g).Last().RoundNumber==oldRound,"The actual Hand2 top-return child may invoke Jieyan before the old round has ended or the new round begins.");g=Cold(g,r);Activate(g);}
   Reach(g,()=>E<RoundStartedEvent>(g).Any(e=>e.RoundNumber==oldRound+1));var top=E<YuanJiReturnPaidEvent>(g).Single(e=>e.FrameId==f.Id);Need(top.Top&&top.Count==(scene=="water-extra"?4:2)&&E<RoundEndedEvent>(g).Length==1&&E<YuanJiMarkClosedEvent>(g).Count(e=>e.ReturnFrameId==f.Id)==top.Count,"Old-round native children fully return and close their exact marks before the next round starts.");
   if(scene=="water-extra")Need(E<YuanJiLossOpportunityEvent>(g).All(e=>!g.CardMovements.Any(m=>m.Sequence>top.Before&&m.Sequence<=top.After&&m.Sequence==e.FirstSequence)),"One whole Hand4 return cannot be split into two false atomic Hand2 activations.");if(scene=="water-loss")Need(V(g,0).Skills!.All(s=>s.ContentId!=Water),"Issued Water cards retain their true old-round return after source loss.");if(scene=="water-multiple")Need(E<YuanJiReturnPaidEvent>(g).Count(e=>e.Top)>=2,"Multiple live owners finish their separate real return batches in the same old-round mother.");_=Cold(g,r);
  }
 }
 private static PlayerSnapshot V(GameEngine g,int seat)=>g.CreateSnapshot(seat).Players[seat];
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
 private static YuanJiReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.YuanJi is not null)?.YuanJi;
 private static bool Optional(GameEngine g,string skill)=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==skill&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
 private static bool WatchContinue(GameEngine g)=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==Watch&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
 private static void Activate(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="activate");
 private static void Skip(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
 private static void Continue(GameEngine g)=>Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
 private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException("Scene="+Scene+" "+why);}
 private static void Accept(GameEngine g,GameCommand command){try{var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());Need(result.Accepted,result.Error?.Message??"Rejected native command.");}catch(Exception e){throw new InvalidOperationException("YuanJi cause="+e.Message+" "+Diagnostic(g),e);}}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> match){var p=P(g)!;var choice=p.Choices.FirstOrDefault(match);Need(p.PlayerSeat==0&&choice is not null,"Missing human choice "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,choice!.Id,g.Revision));}
 private static void ChooseOwned(GameEngine g,int count){for(var i=0;i<=count;i++){if(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.OwnedCardSelection is not null))return;var p=P(g);if(p is{PlayerSeat:0})Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="select-owned-cards"&&c.Cards.Count==1);else Accept(g,new AdvanceOneStepCommand(g.Revision));}Need(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.OwnedCardSelection is not null),"The real owner finishes its native whole-card selection.");}
 private static void OrderAll(GameEngine g){for(var i=0;i<20&&Current(g)is{Stage:YuanJiStage.ReturnOrder};i++)Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="yuan-ji-order");}
 private static void Use(GameEngine g,string id,int[]? targets=null,int[]? cards=null)=>Accept(g,new UseProgramSkillCommand(0,Driver,id,cards??[],targets??[],g.Revision,P(g)!.PromptId));
 private static void Native(GameEngine g,Func<LegalAction,bool> match){var a=g.GetHumanLegalActions().FirstOrDefault(match);Need(a is not null,"Missing native action "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string method="",[CallerLineNumber]int line=0)
 {for(var i=0;i<400;i++){if(done())return;Need(g.State.Winner==Winner.None,"Unexpected terminal "+Diagnostic(g));var p=P(g);if(p is null||p.PlayerSeat!=0)Accept(g,new AdvanceOneStepCommand(g.Revision));else if(p.Kind==DecisionKind.PlayCard)End(g);else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0);else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");else if(WatchContinue(g))Continue(g);else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Skip(g);else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="yuan-ji-order"))OrderAll(g);else throw new InvalidOperationException($"Unexpected {method}:{line} "+Diagnostic(g));}throw new InvalidOperationException($"Missing {method}:{line} "+Diagnostic(g));}
 private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(4).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Frames=g.ResolutionStack.TakeLast(7).Select(f=>new{f.Id,Type=f.GetType().Name,Skill=(f as ProgramSkillFrame)?.SkillId,Index=(f as ProgramSkillFrame)?.InstructionIndex,Stage=(f as ProgramSkillFrame)?.YuanJi?.Stage}),Started=E<YuanJiStartedEvent>(g),Draws=E<YuanJiDrawCompletedEvent>(g),Returns=E<YuanJiReturnPaidEvent>(g)});
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(cold),"Cold replay keeps exact atomic losses, original marks, private orders and old-round native children.");return cold;}
 private static(GameEngine,ContentRegistry)Start(string scene,bool opening=false)
 {Scene=scene;ContentRegistry r;try{r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));}catch(Exception e){throw new InvalidOperationException("YuanJi fixture Scene="+scene+" cause="+e.Message,e);}var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=scene.StartsWith("mirror-",StringComparison.Ordinal)||scene=="pair-death"?Role.Renegade:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=12},r);Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(!opening)Play(g);return(g,r);}
 private sealed class Fixture(string scene):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-yuan-ji",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(b,"ol:yuan-ji");static JsonNode N(string text)=>JsonNode.Parse(text)!;
   JsonNode A(string id,string effects,int targets=0,int cards=0)=>new JsonObject{["id"]=id,["minCards"]=cards,["maxCards"]=cards,["minTargets"]=targets,["maxTargets"]=targets,["targetKind"]="anyLiving",["usesPerTurn"]=null,["effects"]=N("["+effects+"]")};
   var activations=new JsonArray(
    A("discard-two","""{"op":"selectOwnedCards","target":"selectedTarget","amount":2,"zones":["hand"],"resultBind":"paid"},{"op":"moveBoundCards","target":"owner","sourceBind":"paid","destination":"discardPile","awaitMovementTriggers":true}""",1),
    A("mixed","""{"op":"selectOwnedCards","target":"selectedTarget","amount":2,"zones":["hand","equipment"],"resultBind":"paid"},{"op":"moveBoundCards","target":"owner","sourceBind":"paid","destination":"discardPile","awaitMovementTriggers":true}""",1),
    A("split","""{"op":"discardParticipantCards","target":"selectedTarget","zones":["hand"],"amount":1},{"op":"discardParticipantCards","target":"selectedTarget","zones":["hand"],"amount":1}""",1),
    A("give-two","""{"op":"giveSelected","target":"selectedTarget","amount":2}""",1,2),
    A("stock","""{"op":"draw","target":"owner","amount":20}"""),A("draw-one","""{"op":"draw","target":"owner","amount":1}"""),
    A("take-all","""{"op":"selectOwnedCards","target":"selectedTarget","numberExpression":"allOwnedZoneCards","zones":["hand"],"resultBind":"taken"},{"op":"moveBoundCards","target":"owner","sourceBind":"taken","destination":"ownerHand","awaitMovementTriggers":true}""",1),
    A("top","""{"op":"captureSelectedCards","target":"owner","resultBind":"placed"},{"op":"moveBoundCards","target":"owner","sourceBind":"placed","destination":"drawPileTop","awaitMovementTriggers":true}""",0,1),
    A("bottom","""{"op":"exchangeOwnedCardThroughDeckEnd","target":"owner"}""",0,1),
    A("extra","""{"op":"pendExtraTurn","target":"owner"}"""));
   var deckExchange=activations.Single(a=>a!["id"]!.GetValue<string>()=="bottom")!;deckExchange["sourceZones"]=N("[\"hand\",\"equipment\"]");deckExchange["usesPerPhase"]=1;
   var definitions=new JsonArray(new JsonObject{["id"]=Driver,["revision"]=1,["modifiers"]=N("""[{"id":"small-fixture-hand-limit","query":"handLimit","operation":"add","value":40,"priority":0}]"""),["activations"]=activations},N("""{"id":"fixture:yj-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}"""));
   var mirror=scene.StartsWith("mirror-",StringComparison.Ordinal);var water=scene.StartsWith("water",StringComparison.Ordinal);var conditional=scene=="pair-loss"||scene=="mirror-loss"||scene=="water-loss";
   if(conditional)definitions.Add(new JsonObject{["id"]="fixture:yj-conditional",["revision"]=1,["hpSkillGrants"]=new JsonArray(new JsonObject{["id"]="original-healthy-source",["skillId"]=mirror?Mirror:water?Water:Jie,["minimumHp"]=mirror?12:13})});
   if(mirror||water||scene is"pair-loss"or"pair-death")
   {
    var reason=mirror?"skill-program.yuan-ji.jinghua.draw":water?"skill-program.yuan-ji.shuiyue.draw":"skill-program.yuan-ji.jieyan.draw";var effects=new JsonArray();
    if(mirror&&scene!="mirror-loss"){effects.Add(N("""{"op":"selectTarget","target":"owner","targetKind":"otherLiving"}"""));effects.Add(N("""{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"given"}"""));effects.Add(N("""{"op":"moveBoundCards","target":"owner","sourceBind":"given","destination":"selectedTargetHand"}"""));}
    effects.Add(N("""{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}"""));
    if(mirror&&scene!="mirror-loss"){effects.Add(N("""{"op":"revealTargetHandCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"resultBind":"taken","mode":"chooser"}"""));effects.Add(N("""{"op":"moveBoundCards","target":"owner","sourceBind":"taken","destination":"ownerHand","awaitMovementTriggers":true}"""));}
    if(conditional||scene=="pair-death")effects.Add(new JsonObject{["op"]="loseHp",["target"]="owner",["amount"]=scene=="pair-death"?20:1});
    definitions.Add(new JsonObject{["id"]=Watch,["revision"]=1,["triggers"]=new JsonArray(new JsonObject{["id"]="actual-draw-child",["window"]="cardsGained",["subject"]="owner",["destinationZones"]=N("[\"hand\"]"),["movementOccurrence"]="perBatch",["movementReasons"]=new JsonArray(reason),["optional"]=false,["effects"]=effects})});
   }
   var labels=definitions.ToDictionary(d=>d!["id"]!.GetValue<string>(),d=>new{name=d!["id"]!.GetValue<string>(),description="袁姬真实原生边界夹具",optionLabels=d!["id"]!.GetValue<string>()==Watch?new Dictionary<string,string>{{"continue","继续"}}:new Dictionary<string,string>()});var catalog=SkillProgramCatalog.Load(new JsonObject{["schemaVersion"]=SkillProgramCatalog.RulesSchemaVersion,["skills"]=definitions}.ToJsonString(),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));foreach(var(id,program)in catalog.Programs)b.AddSkill(new(id,id,"真实边界"){Program=program,ProgramPresentation=catalog.Presentations[id]});
   b.AddSkill(new("fixture:yj-pick","公开固定本人","不影响规则的选将夹具"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,_=>-100000000d)});var own=new List<string>{Driver};if(conditional)own.Add("fixture:yj-conditional");else own.Add(mirror?Mirror:water?Water:Jie);if(water)own.Add(Jie);if(mirror||water||scene is"pair-loss"or"pair-death")own.Add(Watch);b.AddGeneral(new(Owner,"实际袁姬","ol-yuan-ji","fixture:yj-pick","wu",12,own,GeneralGender.Female));
   var peers=Enumerable.Range(1,3).Select(i=>"fixture:yj-peer-"+i).ToArray();for(var i=1;i<=3;i++){var index=i;var pick="fixture:yj-peer-pick-"+i;b.AddSkill(new(pick,"公开固定其他人","稳定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(role=>role,role=>index==1&&role==Role.Lord?100000000d:1000000d*(4-index))});var skills=new List<string>{"fixture:yj-quiet"};if(scene=="water-multiple"&&i==1)skills.Add(Water);b.AddGeneral(new(peers[i-1],"真实其他角色","supporter",pick,"wu",12,skills,GeneralGender.Male));}
   var kind=scene=="pair-mixed"?CardKind.SilverLion:CardKind.Dodge;b.AddCard(new("fixture:yj-card","原生"+kind,CardCatalog.Get(kind).CategoryName,"固定小实体",kind));var count=scene=="pair-mismatch"?24:scene=="pair-reshuffle"?20:96;b.AddDeck(new("fixture:yj-deck","固定原生小牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,count).Select(i=>new ContentDeckPhysicalCard("fixture:yj-card",scene=="pair-mismatch"&&i>=12?Suit.Spade:Suit.Heart,i%13+1)).ToArray()});b.AddMode(new(Mode,"袁姬真实原生小局面",4,4,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),2},{nameof(Role.Renegade),1}},"fixture:yj-deck",GeneralCandidateCount:4,GeneralPoolIds:[Owner,..peers]));
  }
 }
}
