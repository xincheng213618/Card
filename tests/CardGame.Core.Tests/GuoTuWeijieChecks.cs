using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuoTuWeijieChecks
{
 private const string Owner="fixture:wj-owner",Mode="identity:classic-weijie",Driver="fixture:wj-driver",Quiet="fixture:wj-quiet",Insert="fixture:wj-insert",Watch="fixture:wj-watch",Request="fixture:wj-request",Identity="fixture:wj-identity",Wj="ol:weijie";
 private static string Scene="";
 public static void WeijieSixBasicNamesNativeDyingAndFailureCold()
 {
  foreach(var kind in new[]{CardKind.Slash,CardKind.FireSlash,CardKind.ThunderSlash})
  {
   var(g,r)=Start("basic-"+kind);End(g);Reach(g,()=>Open(g));g=Cold(g,r);Begin(g,kind,1);Pay(ref g,r,3);var paid=E<WeijieCostPaidEvent>(g).Single();
   Reach(g,()=>E<WeijieUseReturnedEvent>(g).Any(e=>e.AttemptId==paid.FrameId));var issued=E<WeijieUseIssuedEvent>(g).Single(e=>e.AttemptId==paid.FrameId);var declared=E<CardUseDeclaredEvent>(g).Single(e=>e.ResolutionId==issued.UseId);
   Need(declared.CardId==0&&declared.CardKind==kind&&issued.ActorSeat==0&&issued.ProviderSeat==0&&issued.RequesterSeat is null&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==issued.UseId)==1,"Each declared Slash variant is a genuine once-issued zero-entity native Use through a real foreign actor's request.");
   Need(paid.EffectiveName==CardKind.Slash&&paid.ActualTurnOwner==1&&g.CardMovements.Count(m=>m.Sequence==paid.Sequence&&m.CardId==paid.CardId&&m.From==CardLocation.Hand(3)&&m.To==CardLocation.DiscardPile)==1,"Slash-family names match the actual mandatory Hand identity and pay one genuine foreign discard.");_=Cold(g,r);
  }
  foreach(var kind in new[]{CardKind.Peach,CardKind.Alcohol})
  {
   var(g,r)=Start("basic-"+kind);if(kind==CardKind.Peach){Use(g,"wound-peer",[1]);Play(g);}
   Use(g,"ordered-three-items",[1],skill:"ol:pingduan");Reach(g,()=>P(g)is{PlayerSeat:1}p&&p.Choices.Any(c=>c.Parameters.ContainsKey("weijie-request")));g=Cold(g,r);
   Reach(g,()=>Current(g)is{Stage:WeijieStage.Card});Need(Current(g)!.Need is{Intent:WeijieIntent.GenericBasic,ProviderSeat:1,ActorSeat:1},"A real own-turn Pingduan asks a different outside-turn basic-card user; AI advances its actual published Weijie choices.");
   var need=Current(g)!.Need;g=Cold(g,r);Reach(g,()=>E<WeijieUseReturnedEvent>(g).Any()&&!g.ResolutionStack.Any(f=>f.Id==need.OwnerFrameId));var issued=E<WeijieUseIssuedEvent>(g).Single();var paid=E<WeijieCostPaidEvent>(g).Single();
   Need(issued.ActorSeat==1&&issued.ProviderSeat==1&&issued.RequesterSeat is null&&issued.OutputKind==kind&&paid.VictimSeat==0&&paid.ActualTurnOwner==0&&paid.EffectiveName==kind&&E<CardUseDeclaredEvent>(g).Count(e=>e.ResolutionId==issued.UseId&&e.CardId==0&&e.CardKind==kind)==1&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==issued.UseId)==1,"A genuinely requested ordinary Peach or Alcohol pays the AI's opaque foreign Hand cost once and returns through the original Pingduan mother.");
   if(kind==CardKind.Peach)Need(E<RecoveryAppliedEvent>(g).Any(e=>e.SourceSeat==1&&e.TargetSeat==1&&e.Amount==1),"Ordinary Peach follows the actual recovery path.");
   else{Need(E<AlcoholAppliedEvent>(g).Any(e=>e.ResolutionId==issued.UseId&&e.SourceSeat==1),"Ordinary Alcohol uses the native effect and actual-turn quota.");Reach(g,()=>E<ActualTurnCursorReturnedEvent>(g).Any(e=>e.ActualTurn==paid.ActualTurn));Need(E<AlcoholExpiredEvent>(g).Any(e=>e.PlayerSeat==1),"An outside-turn ordinary Alcohol expires at its actual cursor return.");}
   _=Cold(g,r);
  }
  {
   var(g,r)=Start("basic-Dodge");End(g);Reach(g,()=>Open(g));Begin(g,CardKind.Dodge);Pay(ref g,r,3);Reach(g,()=>E<WeijieResponseIssuedEvent>(g).Length==1);var response=E<WeijieResponseIssuedEvent>(g).Single();var action=E<CardActionAcceptedEvent>(g).Single(e=>e.Action.ActionId==response.ActionId).Action;
   Need(response.OutputKind==CardKind.Dodge&&response.Continuation==ProgramCardContinuation.Dodge&&action.PhysicalCards.Count==0&&action.EffectiveSuit is null&&action.EffectiveRank is null&&action.EffectiveIsRed is null,"The real Slash-defense Dodge uses native empty-cost response appearance; the discarded card supplies neither suit, rank nor color.");Play(g);_=Cold(g,r);
  }
  foreach(var kind in new[]{CardKind.Peach,CardKind.Alcohol})
  {
   var(g,r)=Start("dying-"+kind);End(g);Reach(g,()=>Open(g));Need(P(g)!.Kind==DecisionKind.RescueDying,"The same skill enters the genuine current dying request, rather than a Play substitute.");Begin(g,kind);Pay(ref g,r,3);Reach(g,()=>E<WeijieUseReturnedEvent>(g).Any());Need(V(g,0).Hp==1&&V(g,0).IsAlive&&E<DyingResponseEvent>(g).Any(e=>e.ResponderSeat==0&&(kind==CardKind.Peach?e.UsedPeach&&e.PeachCardId==0:e.UsedAlcohol&&e.AlcoholCardId==0)),"A paid Peach or self-dying Alcohol returns directly to the original native dying mother after cold restoration.");_=Cold(g,r);
  }
 }
 public static void WeijiePlayedDuelAoeAndTrueDodgeSupplyCold()
 {
  foreach(var scene in new[]{"duel","barbarian","arrows"})
  {
   var(g,r)=Start(scene);End(g);Reach(g,()=>Open(g));Begin(g,scene=="arrows"?CardKind.Dodge:CardKind.ThunderSlash);var need=Current(g)!.Need;
   Need(need.Intent==(scene=="duel"?WeijieIntent.Duel:WeijieIntent.Group),"The real foreign native Duel or AOE publishes its exact played-card need.");
   var victim=P(g)!.Choices.Where(c=>c.Parameters.GetValueOrDefault("weijie-choice")=="victim").Select(c=>c.Targets.Single()).First(s=>s!=need.OpponentSeat);Pay(ref g,r,victim);Reach(g,()=>E<WeijieResponseIssuedEvent>(g).Length==1);var response=E<WeijieResponseIssuedEvent>(g).Single();var action=E<CardActionAcceptedEvent>(g).Single(e=>e.Action.ActionId==response.ActionId).Action;
   Need(action.Type==CardActionType.Response&&action.PhysicalCards.Count==0&&action.ActorSeat==0&&action.ProviderSeat==0&&action.RequesterSeat==need.RequesterSeat&&action.OpponentSeat==need.OpponentSeat&&E<WeijieUseIssuedEvent>(g).Length==0,"Duel and both AOEs retain their original requester/opponent and pure played-card continuation without inventing another native Use.");_=Cold(g,r);
  }
  foreach(var scene in new[]{"faction-dodge","faction-played","faction-use"})
  {
   var(g,r)=Start(scene);
   if(scene=="faction-dodge"){Incoming(g,3,0);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge});Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="faction-defense-request");}
   else if(scene=="faction-played"){Use(g,"incoming-duel",[3]);Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondSlash});Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="faction-slash-request");}
   else Use(g,"request-shu-slash",[3],skill:"classic:jijiang");
   Reach(g,()=>P(g)is{PlayerSeat:1}p&&p.Choices.Any(c=>c.Parameters.ContainsKey("weijie-request")));g=Cold(g,r);Reach(g,()=>Current(g)is{Stage:WeijieStage.Card});var need=Current(g)!.Need;
   Need(need.Intent==(scene=="faction-dodge"?WeijieIntent.FactionDodge:scene=="faction-played"?WeijieIntent.FactionSlashPlayed:WeijieIntent.FactionSlashUse)&&need.ActorSeat==0&&need.ProviderSeat==1&&need.RequesterSeat==0,"A real Lord request retains the actual user, separate foreign-turn provider and requester before the AI pays its opaque slot.");g=Cold(g,r);
   if(scene=="faction-use")
   {
    Reach(g,()=>E<WeijieUseIssuedEvent>(g).Length==1);var use=E<WeijieUseIssuedEvent>(g).Single();Need(use.ActorSeat==0&&use.ProviderSeat==1&&use.RequesterSeat==0&&E<WeijieResponseIssuedEvent>(g).Length==0,"The genuine provided Slash belongs to its requesting user; GuoTu is its zero-entity provider.");
    Reach(g,()=>E<CardSupplyCompletedEvent>(g).Any(e=>e.ActionId==use.ActionId)&&E<WeijieUseReturnedEvent>(g).Any(e=>e.AttemptId==use.AttemptId));Need(E<CardSupplyCompletedEvent>(g).Count(e=>e.ActionId==use.ActionId)==1&&E<CardSupplyCompletionStartedEvent>(g).Count(e=>e.ActionId==use.ActionId)==1,"The supply-completion observer accepts the exact zero-material provider once and the native Use returns completely.");
   }
   else
   {
    Reach(g,()=>E<WeijieResponseIssuedEvent>(g).Length==1);var response=E<WeijieResponseIssuedEvent>(g).Single();var action=E<CardActionAcceptedEvent>(g).Single(e=>e.Action.ActionId==response.ActionId).Action;
    Need(action.Type==CardActionType.Response&&action.PhysicalCards.Count==0&&action.ActorSeat==0&&action.ProviderSeat==1&&action.RequesterSeat==0&&action.OpponentSeat==need.OpponentSeat&&E<WeijieUseIssuedEvent>(g).Length==0,"A faction supply preserves actor, provider, requester and incoming opponent independently without fabricating provider-owned Use.");
    if(scene=="faction-dodge")Need(E<FactionDefenseResolvedEvent>(g).Any(e=>e.ProviderSeat==1&&e.Succeeded),"Real Hujia obtains its zero-entity Dodge through its native response continuation.");
   }
   Need(E<WeijieCostPaidEvent>(g).Single().Source.OwnerSeat==1&&E<WeijieCostPaidEvent>(g).Single().ActualTurnOwner==0,"The genuine foreign-turn provider spends the once-per-actual-turn opaque discard.");_=Cold(g,r);
  }
 }
 public static void WeijieForcedAndProgramRequestFamiliesCold()
 {
  foreach(var scene in new[]{"target-request","nearest-request","assisted-request","legal-nearest-request","fixed-request","borrowed","qinglong"})
  {
   var(g,r)=Start(scene);
   if(scene=="legal-nearest-request")
   {
    Use(g,"native-legal-nearest",skill:Request);Reach(g,()=>P(g)is{PlayerSeat:1}p&&p.Choices.Any(c=>c.Parameters.ContainsKey("weijie-request")));g=Cold(g,r);Reach(g,()=>Current(g)is{Stage:WeijieStage.Card});var originalNeed=Current(g)!.Need;
    Need(originalNeed is{Intent:WeijieIntent.NearestLegalSlash,ActorSeat:1,ProviderSeat:1},"The real own-Play activation asks another outside-turn actor through the production nearest-legal request.");g=Cold(g,r);Reach(g,()=>E<WeijieUseReturnedEvent>(g).Any()&&!g.ResolutionStack.Any(f=>f.Id==originalNeed.OwnerFrameId));var actual=E<WeijieUseIssuedEvent>(g).Single();
    Need(actual.ActorSeat==1&&actual.ProviderSeat==1&&actual.OutputKind==CardKind.Slash&&E<WeijieCostPaidEvent>(g).Single()is{VictimSeat:0,PrintedKind:CardKind.Crossbow,EffectiveName:CardKind.Slash,ActualTurnOwner:0}&&E<CardUseDeclaredEvent>(g).Count(e=>e.ResolutionId==actual.UseId&&e.CardId==0)==1&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==actual.UseId)==1,"An AI makes its real opaque selection, pays the mandatory foreign Hand identity once and returns the genuine zero-entity Slash to the original active request.");_=Cold(g,r);continue;
   }
   if(scene=="borrowed"){Use(g,"weapon");Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:wj-weapon"));Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="fixture:wj-weapon");Answer(g,c=>c.Parameters.GetValueOrDefault("advanced-value")=="finish");Play(g);Need(V(g,0).Equipment is[{Kind:CardKind.GeneralWeapon}]&&V(g,0).Hand.All(c=>c.Kind==CardKind.BorrowedSword),"The forced holder has a real weapon and no Hand Slash or Hand Slash converter.");End(g);}
   else if(scene=="qinglong")
   {
    Native(g,a=>a.Kind==LegalActionKind.Equip);Play(g);Use(g,"wound-peer",[1]);Play(g);End(g);Reach(g,()=>P(g)?.PlayerSeat==0&&P(g)!.Choices.Any(c=>c.Parameters.GetValueOrDefault("offer-option")=="slash"));Answer(g,c=>c.Parameters.GetValueOrDefault("offer-option")=="slash");
   }
   else if(scene=="fixed-request")End(g);
   else End(g);
   if(scene=="assisted-request")Reach(g,()=>Open(g)||P(g) is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("request-option")=="target"));
   if(scene=="assisted-request"&&!Open(g))Answer(g,c=>c.Targets.SequenceEqual([1]));
   Reach(g,()=>Open(g));var expected=scene switch{"target-request"=>WeijieIntent.ProgramSlash,"nearest-request"=>WeijieIntent.ProgramNearestSlash,"assisted-request"=>WeijieIntent.AssistedSlash,"legal-nearest-request"=>WeijieIntent.NearestLegalSlash,"fixed-request"=>WeijieIntent.FixedSlash,"borrowed"=>WeijieIntent.BorrowedSword,_=>WeijieIntent.Qinglong};
   Begin(g,CardKind.FireSlash);var need=Current(g)!.Need;Need(need.Intent==expected,"Every native Slash-request family retains its original typed caller and exact cursor.");Pay(ref g,r,3);Reach(g,()=>E<WeijieUseReturnedEvent>(g).Any()&&!g.ResolutionStack.Any(f=>f.Id==need.OwnerFrameId));var issued=E<WeijieUseIssuedEvent>(g).Single();
   Need(E<CardUseDeclaredEvent>(g).Count(e=>e.ResolutionId==issued.UseId&&e.CardId==0&&e.CardKind==CardKind.FireSlash)==1&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==issued.UseId)==1&&E<WeijieCostPaidEvent>(g).Length==1,"A genuine forced/program request uses the selected variant once, consumes one actual opaque cost and completes the native card through its original typed return.");
   if(scene=="borrowed")Need(E<BorrowedSwordResolvedEvent>(g).Any()||g.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.Id==need.OwnerFrameId),"The real Borrowed Sword parent is preserved until its own original native return finishes.");
   _=Cold(g,r);
  }
  {
   var(g,r)=Start("duel-wrong");End(g);Reach(g,()=>Open(g)&&P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondSlash});var original=P(g)!;Begin(g,CardKind.Slash);var need=Current(g)!.Need;var native=g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==need.OwnerFrameId);var originalDuel=native.Continuations.Duel;
   Need(need.Intent==WeijieIntent.Duel&&originalDuel is{ResponderSeat:0,SuccessfulSlashResponses:0}&&original.Choices.All(c=>c.Cards.Count==0)&&V(g,0).Hand.All(c=>c.Kind==CardKind.Duel),"The real foreign Duel has no ordinary Slash, weapon pair or Slash converter; only the native decline and Weijie offer are available.");var victim=P(g)!.Choices.First(c=>c.Parameters.GetValueOrDefault("weijie-choice")=="victim").Targets.Single();Pay(ref g,r,victim);Reach(g,()=>E<WeijieAttemptResolvedEvent>(g).Length==1);var result=E<WeijieAttemptResolvedEvent>(g).Single();
   Need(!result.Matched&&!result.NativeLegal&&E<WeijieCostPaidEvent>(g)is[{PrintedKind:CardKind.Duel,EffectiveName:CardKind.Duel}]&&E<WeijieUseIssuedEvent>(g).Length==0&&E<WeijieResponseIssuedEvent>(g).Length==0&&P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondSlash}returned&&returned.PromptId!=original.PromptId&&returned.Choices.Count==1&&returned.Choices.All(c=>c.Cards.Count==0&&!c.Parameters.ContainsKey("weijie-request"))&&Equals(g.ResolutionStack.OfType<CardUseFrame>().Single(u=>u.Id==need.OwnerFrameId).Continuations.Duel,originalDuel)&&E<DuelResponseEvent>(g).All(e=>e.ResolutionId!=need.OwnerFrameId)&&E<DamageRequestedEvent>(g).All(e=>e.SourceCard!=CardKind.Duel||e.TargetSeat!=0)&&E<CardUseFinishedEvent>(g).All(e=>e.ResolutionId!=need.OwnerFrameId),"A paid wrong name preserves the same still-unconsumed Duel cursor and freshly publishes its sole native decline; no response, damage or completion is invented.");
   g=Cold(g,r);var hp=V(g,0).Hp;Answer(g,c=>c.Cards.Count==0&&!c.Parameters.ContainsKey("weijie-request"));Reach(g,()=>E<CardUseFinishedEvent>(g).Any(e=>e.ResolutionId==need.OwnerFrameId));Need(E<DuelResponseEvent>(g).Count(e=>e.ResolutionId==need.OwnerFrameId&&!e.UsedSlash)==1&&E<DamageRequestedEvent>(g).Count(e=>e.SourceCard==CardKind.Duel&&e.TargetSeat==0)==1&&V(g,0).Hp==hp-1&&E<WeijieCostPaidEvent>(g).Length==1,"Only the player's genuine native decline after cold restoration advances that Duel, applies its damage and returns once without another payment.");_=Cold(g,r);
  }
  {
   var(g,r)=Start("qinglong-death");Answer(g,c=>c.Parameters.GetValueOrDefault("offer-option")=="slash");Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.QinglongCrescentBlade}&&Open(g));Begin(g,CardKind.FireSlash);var need=Current(g)!.Need;
   Need(need.Intent==WeijieIntent.Qinglong&&need.ActorSeat==0&&E<CardRespondedEvent>(g).Any(e=>e.ResponderSeat==need.Targets.Single()&&e.EffectiveCardKind==CardKind.Dodge),"A real foreign-turn offered Slash and actual native Dodge open Qinglong while GuoTu has no Hand Slash.");Pay(ref g,r,1);
   Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==Watch&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
   Reach(g,()=>!V(g,0).IsAlive&&!g.ResolutionStack.Any(f=>f.Id==need.OwnerFrameId)&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.Weijie is not null));
   Need(E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0)&&E<WeijieCostPaidEvent>(g).Single().EffectiveName==CardKind.Slash&&E<WeijieUseIssuedEvent>(g).Length==0&&E<WeijieResponseIssuedEvent>(g).Length==0&&E<QinglongCrescentBladeResolvedEvent>(g).Count(e=>e.ResolutionId==need.OwnerFrameId&&!e.Used)==1&&E<CardUseFinishedEvent>(g).Count(e=>e.ResolutionId==need.OwnerFrameId)==1&&P(g)?.PlayerSeat!=0,"A matching paid Qinglong attempt whose actor genuinely dies issues no new card or dead menu; its exact original native no-follow-up return finishes once.");_=Cold(g,r);
  }
 }
 public static void WeijieOpaqueCostSourceLossDeathAndTurnQuotaCold()
 {
  foreach(var scene in new[]{"cancel","wrong","loss","death","identity","quota"})
  {
   var(g,r)=Start(scene);Reach(g,()=>Open(g));var original=P(g)!;g=Cold(g,r);Answer(g,c=>c.Parameters.ContainsKey("weijie-request"));Reach(g,()=>Current(g)is{Stage:WeijieStage.Declare});
   if(scene=="cancel"){var old=P(g)!.PromptId;Answer(g,c=>c.Parameters.GetValueOrDefault("weijie-choice")=="cancel");Need(E<WeijieCostPaidEvent>(g).Length==0&&Open(g)&&P(g)!.PromptId!=original.PromptId&&P(g)!.PromptId!=old,"A prepayment cancellation spends nothing and refreshes the same unconsumed native need with a new prompt token.");Begin(g,CardKind.Dodge);}
   else{Declare(g,CardKind.Dodge);Reach(g,()=>Current(g)is{Stage:WeijieStage.Victim});}
   var victimPrompt=P(g)!;Need(victimPrompt.Choices.Where(c=>c.Parameters.GetValueOrDefault("weijie-choice")=="victim").All(c=>c.Targets.Count==1&&!c.Targets.Contains(2)),"Outgoing distance exactly one excludes the opposite seat; no implicit range exemption is granted.");
   Answer(g,c=>c.Parameters.GetValueOrDefault("weijie-choice")=="victim"&&c.Targets.SequenceEqual([3]));Reach(g,()=>Current(g)is{Stage:WeijieStage.Card});var prompt=P(g)!;var unchanged=State(g);
   Need(prompt.IsPrivate&&prompt.Choices.All(c=>c.Cards.Count==0)&&Enumerable.Range(1,3).All(s=>g.CreateSnapshot(s).PendingDecision is null),"The chooser receives only real opaque Hand slots, not foreign card identities, equipment or pile materials.");
   Need(!g.Submit(new AnswerPromptCommand(1,prompt.PromptId,prompt.Choices[0].Id,g.Revision)).Accepted&&State(g)==unchanged&&!g.Submit(new AnswerPromptCommand(0,original.PromptId,prompt.Choices[0].Id,g.Revision)).Accepted&&State(g)==unchanged,"Other actors and stale native prompts cannot mutate the paid request.");
   var nativeOwner=Current(g)!.Need.OwnerFrameId;g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("weijie-choice")=="card");
   if(PaidWatchScene(scene))
   {Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId==Watch&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("option-id")=="continue"));Need(E<WeijieCostPaidEvent>(g).Length==1&&E<WeijieUseIssuedEvent>(g).Length==0&&E<WeijieResponseIssuedEvent>(g).Length==0,"The genuine logical-discard child pauses before any virtual card is issued.");g=Cold(g,r);Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");}
   if(scene=="death"){Reach(g,()=>!V(g,0).IsAlive&&!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f=>f.Weijie is not null)&&!g.ResolutionStack.OfType<CardUseFrame>().Any(u=>u.Id==nativeOwner));Need(E<PlayerDiedEvent>(g).Any(e=>e.VictimSeat==0)&&E<WeijieCostPaidEvent>(g).Length==1&&E<WeijieUseIssuedEvent>(g).Length==0&&E<WeijieResponseIssuedEvent>(g).Length==0,"Actual non-lord payer death drains the cost once and never issues a new basic card for the dead actor.");_=Cold(g,r);continue;}
   Reach(g,()=>E<WeijieAttemptResolvedEvent>(g).Length==1);var paid=E<WeijieCostPaidEvent>(g).Single();var result=E<WeijieAttemptResolvedEvent>(g).Single();Need(E<NativeLogicalDiscardInvocationRecordedEvent>(g).Any(e=>e.InitiatorSeat==0&&e.ExecutorSeat==0&&e.SubjectSeat==3&&e.ActualCount==1&&e.HandCount==1&&e.Reason.Value=="skill-program.guo-tu.weijie.discard"),"Foreign Hand payment is one real logical discard initiated and executed by GuoTu.");
   if(scene=="wrong")
   {Need(!result.Matched&&!result.NativeLegal&&E<WeijieResponseIssuedEvent>(g).Length==0&&P(g)is{Kind:DecisionKind.RespondDodge,PlayerSeat:0}&&P(g)!.PromptId!=original.PromptId&&!Open(g)&&P(g)!.Choices.Any(c=>c.Cards.Count==1),"A wrong paid name spends the actual-turn attempt but returns to the unchanged native response cursor with newly built normal options.");Answer(g,c=>c.Cards.Count==1);Play(g);}
   else{Reach(g,()=>E<WeijieResponseIssuedEvent>(g).Length==1);Need(result.Matched&&result.NativeLegal,"A paid matching identity resumes its original real response.");if(scene=="loss")Need(!V(g,0).Skills!.Any(s=>s.ContentId==Wj),"The exact original skill instance was genuinely lost in the payment child.");if(scene=="identity")Need(paid.PrintedKind==CardKind.Crossbow&&paid.EffectiveName==CardKind.Dodge,"A mandatory actual Hand identity matches while an optional view-as alone is not a card's current name.");if(scene!="quota")Play(g);}
   if(scene=="quota"){Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge}&&E<CardUseDeclaredEvent>(g).Count(e=>e.CardKind==CardKind.Slash)>=2);Need(!Open(g)&&E<WeijieCostPaidEvent>(g).Length==1,"Another real need in the same actual foreign turn cannot spend a second attempt.");Answer(g,c=>c.Cards.Count==1);Play(g);}
   _=Cold(g,r);
  }
 }
 private static PlayerSnapshot V(GameEngine g,int s)=>g.CreateSnapshot(s).Players[s];
 private static bool CostScene(string scene)=>scene is"cancel"or"wrong"or"loss"or"death"or"identity"or"quota";
 private static bool QinglongScene(string scene)=>scene is"qinglong"or"qinglong-death";
 private static bool PaidWatchScene(string scene)=>scene is"loss"or"death"or"qinglong-death";
 private static PendingDecision? P(GameEngine g)=>Enumerable.Range(0,4).Select(s=>g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p=>p is not null);
 private static T[] E<T>(GameEngine g)where T:IGameEvent=>g.Events.Select(e=>e.Payload).OfType<T>().ToArray();
 private static WeijieReceipt? Current(GameEngine g)=>g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f=>f.Weijie is not null)?.Weijie;
 private static bool Open(GameEngine g)=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.ContainsKey("weijie-request"));
 private static void Need(bool yes,string why){if(!yes)throw new InvalidOperationException(why+" Scene="+Scene);}
 private static void Accept(GameEngine g,GameCommand c){try{var result=g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single());Need(result.Accepted,result.Error?.Message??"Native command rejected.");}catch(Exception e){throw new InvalidOperationException("Weijie command cause="+e.Message+" "+Diagnostic(g),e);}}
 private static void Answer(GameEngine g,Func<PromptChoice,bool> predicate){var p=P(g)!;var c=p.Choices.FirstOrDefault(predicate);Need(p.PlayerSeat==0&&c is not null,"Missing real human choice "+Diagnostic(g));Accept(g,new AnswerPromptCommand(0,p.PromptId,c!.Id,g.Revision));}
 private static void Declare(GameEngine g,CardKind kind,int? target=null)=>Answer(g,c=>c.Parameters.GetValueOrDefault("weijie-choice")=="declare"&&c.Parameters.GetValueOrDefault("kind")==kind.ToString()&&(target is null||c.Parameters.GetValueOrDefault("target")==target.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
 private static void Begin(GameEngine g,CardKind kind,int? target=null){Answer(g,c=>c.Parameters.ContainsKey("weijie-request"));Reach(g,()=>Current(g)is{Stage:WeijieStage.Declare});Declare(g,kind,target);Reach(g,()=>Current(g)is{Stage:WeijieStage.Victim});}
 private static void Pay(ref GameEngine g,ContentRegistry r,int victim){Answer(g,c=>c.Parameters.GetValueOrDefault("weijie-choice")=="victim"&&c.Targets.SequenceEqual([victim]));var current=g;Reach(current,()=>Current(current)is{Stage:WeijieStage.Card});g=Cold(current,r);Answer(g,c=>c.Parameters.GetValueOrDefault("weijie-choice")=="card");}
 private static void Use(GameEngine g,string activation,int[]? targets=null,string skill=Driver)=>Accept(g,new UseProgramSkillCommand(0,skill,activation,[],targets??[],g.Revision,P(g)!.PromptId));
 private static void Incoming(GameEngine g,int source,int target){Use(g,"incoming",[source]);Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("offer-option")==$"target-{target}"));Answer(g,c=>c.Targets.SequenceEqual([target]));}
 private static void Native(GameEngine g,Func<LegalAction,bool> predicate){var a=g.GetHumanLegalActions().FirstOrDefault(predicate);Need(a is not null,"Missing native action "+Diagnostic(g));Accept(g,new PlayCardCommand(0,a!.CardId!.Value,a.TargetSeats,g.Revision,P(g)!.PromptId,a.PlayedCardKind,a.TargetCardId){ConversionSource=a.ConversionSource,AdditionalConversionSources=a.AdditionalConversionSources??[]});}
 private static void End(GameEngine g)=>Accept(g,new EndPlayPhaseCommand(0,g.Revision,P(g)!.PromptId));
 private static void Play(GameEngine g)=>Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.PlayCard});
 private static void Reach(GameEngine g,Func<bool> done,[CallerMemberName]string method="",[CallerLineNumber]int line=0)
 {
  for(var i=0;i<380;i++){if(done())return;Need(g.State.Winner==Winner.None,"Unexpected terminal native state "+Diagnostic(g));var p=P(g);
   if(p is null||p.PlayerSeat!=0)Accept(g,new AdvanceOneStepCommand(g.Revision));
   else if(p.Kind==DecisionKind.PlayCard)End(g);
   else if(p.SkillPrompt?.SkillId==Watch)Answer(g,c=>c.Parameters.GetValueOrDefault("option-id")=="continue");
   else if(p.Choices.Any(c=>c.Parameters.GetValueOrDefault("program-action")=="skip"))Answer(g,c=>c.Parameters.GetValueOrDefault("program-action")=="skip");
   else if(p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)Answer(g,c=>c.Cards.Count==0&&!c.Parameters.ContainsKey("weijie-request"));
   else if(p.Kind==DecisionKind.RescueDying)Answer(g,c=>c.Parameters.GetValueOrDefault("response")=="let-die");
   else if(p.Kind==DecisionKind.QinglongCrescentBlade)Answer(g,c=>c.Cards.Count==0&&!c.Parameters.ContainsKey("weijie-request")&&c.Parameters.GetValueOrDefault("action")=="qinglong-skip");
   else if(p.SkillPrompt?.SkillId=="ol:pingduan")Answer(g,c=>c.Parameters.GetValueOrDefault("option")=="decline");
   else throw new InvalidOperationException($"Unexpected native prompt {method}:{line} "+Diagnostic(g));}
  throw new InvalidOperationException($"Missing boundary {method}:{line} "+Diagnostic(g));
 }
 private static string Diagnostic(GameEngine g)=>JsonSerializer.Serialize(new{Scene,g.State.TurnNumber,g.State.CurrentSeat,g.State.Phase,Prompt=P(g)is{}p?new{p.PlayerSeat,p.Kind,Skill=p.SkillPrompt?.SkillId,Choices=p.Choices.Take(6).Select(c=>new{c.Cards,c.Targets,c.Parameters})}:null,Costs=E<WeijieCostPaidEvent>(g),Issued=E<WeijieUseIssuedEvent>(g),Responses=E<WeijieResponseIssuedEvent>(g),Frames=g.ResolutionStack.TakeLast(7).Select(f=>new{f.Id,Type=f.GetType().Name,Index=(f as ProgramSkillFrame)?.InstructionIndex,Skill=(f as ProgramSkillFrame)?.SkillId,Stage=(f as ProgramSkillFrame)?.Weijie?.Stage})});
 private static string State(GameEngine g)=>JsonSerializer.Serialize(new{Views=Enumerable.Range(0,4).Select(s=>SnapshotJson.Serialize(g.CreateSnapshot(s))),Frames=g.ResolutionStack.Select(f=>JsonSerializer.Serialize(f,f.GetType())),g.CardMovements,Zones=g.CreateCardZoneDiagnostics(),Events=g.Events.Select(e=>JsonSerializer.Serialize(e.Payload,e.Payload.GetType())),Commands=CommandJson.Serialize(g.AcceptedCommands)});
 private static GameEngine Cold(GameEngine g,ContentRegistry r){var cold=GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())),r);Need(State(g)==State(cold),"Cold restoration preserves the original native request, opaque cost slot, scalar invoices and typed return.");return cold;}
 private static(GameEngine,ContentRegistry)Start(string scene)
 {
  Scene=scene;ContentRegistry r;try{r=ContentRegistry.Build(new StandardContentPackage(),new Fixture(scene));}catch(Exception e){throw new InvalidOperationException("Weijie fixture Scene="+scene+" cause="+e.Message,e);}
  var g=GameEngine.CreateStandard(new GameOptions{Seed=7,PlayerCount=4,HumanSeat=0,HumanRole=CostScene(scene)||scene=="qinglong-death"?Role.Rebel:Role.Lord,ModeId=Mode,UseInteractiveSetup=true,UseInteractiveDiscard=false,AdvanceAfterHumanCommands=false,MaxTurns=8},r);
  Accept(g,new StartGameCommand());Reach(g,()=>P(g)is{PlayerSeat:0,Kind:DecisionKind.SelectGeneral});Accept(g,new SelectGeneralCommand(0,Owner,g.Revision,P(g)!.PromptId));if(scene=="qinglong-death"){Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.SkillPrompt?.SkillId=="fixture:wj-real-start-weapon"&&p.Choices.Any(c=>c.Targets.SequenceEqual([0])));Answer(g,c=>c.Targets.SequenceEqual([0]));Reach(g,()=>P(g)is{PlayerSeat:0}p&&p.Choices.Any(c=>c.Parameters.GetValueOrDefault("offer-option")=="slash"));}else if(CostScene(scene)){Reach(g,()=>Open(g));var lord=g.CreateSnapshot(0).Players.Single(p=>p.Role==Role.Lord).Seat;Need(lord!=0&&g.State.CurrentSeat==lord&&E<CardUseDeclaredEvent>(g).Last().SourceSeat==lord&&P(g)is{PlayerSeat:0,Kind:DecisionKind.RespondDodge},"A real AI Lord's actual turn issues the original Slash-defense request before any human turn. "+Diagnostic(g));}else Play(g);return(g,r);
 }
 private sealed class Fixture(string scene):IGameContentPackage
 {
  public PackageManifest Manifest{get;}=new("fixture-weijie",new(1,0,0),[]);
  public void Register(IContentRegistryBuilder b)
  {
   CharacterFixtureContent.Register(b,"ol:guo-tu","ol:zhang-shi-ping","ol:ke-bi-neng");
   if(scene=="faction-dodge")CharacterFixtureContent.Register(b,"classic:cao-cao");if(scene is"faction-played"or"faction-use")CharacterFixtureContent.Register(b,"classic:liu-bei");
   var defs=new List<JsonNode>();void S(string id,string body)=>defs.Add(JsonNode.Parse("{\"id\":\""+id+"\",\"revision\":1,"+body+"}")!);
   string A(string id,string effects,int targets=0,string targetKind="anyLiving")=>"{\"id\":\""+id+"\",\"minCards\":0,\"maxCards\":0,\"minTargets\":"+targets+",\"maxTargets\":"+targets+",\"targetKind\":\""+targetKind+"\",\"usesPerTurn\":null"+(id=="incoming-duel"?",\"usesPerPhase\":2":"")+",\"effects\":["+effects+"]}";
   var activations=new[]{
    A("incoming","{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}",1),
    A("self-offer","{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}",1),
    A("incoming-duel","{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}",1,"otherLiving"),
    A("wound","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"),
    A("die","{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":12}"),
    A("wound-peer","{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":11}",1,"otherLiving"),
    A("damage-peer","{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}",1,"otherLiving"),
    A("weapon","{\"op\":\"equipSampledGenerals\",\"target\":\"owner\",\"amount\":1}")};
   S(Driver,"\"modifiers\":[{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0}],\"activations\":["+string.Join(',',activations)+"],\"viewAs\":[{\"id\":\"duel\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"duel\",\"forPlay\":true,\"forResponse\":false,\"singleCardTrickUse\":true,\"allowSameKind\":true},{\"id\":\"dodge\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"dodge\",\"forPlay\":false,\"forResponse\":true,\"allowSameKind\":true}]");
   if(CostScene(scene)||scene=="basic-Dodge")S(Insert,"\"triggers\":[{\"id\":\"true-foreign-incoming\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingHighestHand\"},{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}"+(scene=="quota"?",{\"op\":\"useVirtualSlash\",\"target\":\"selectedTarget\"}":"")+"]}]");
   S(Quiet,"\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]");
   var deckKind=scene.Contains("Peach",StringComparison.Ordinal)?CardKind.Peach:scene.Contains("Alcohol",StringComparison.Ordinal)?CardKind.Alcohol:scene is"basic-Slash"or"basic-FireSlash"or"basic-ThunderSlash"?CardKind.Slash:scene is"duel"or"duel-wrong"?CardKind.Duel:scene.StartsWith("faction-",StringComparison.Ordinal)?CardKind.Crossbow:scene=="barbarian"?CardKind.BarbarianAssault:scene=="arrows"?CardKind.ArrowBarrage:scene=="borrowed"?CardKind.BorrowedSword:QinglongScene(scene)?CardKind.QinglongCrescentBlade:scene is"identity"or"faction-use"or"faction-played"or"target-request"or"nearest-request"or"assisted-request"or"legal-nearest-request"or"fixed-request"?CardKind.Crossbow:CardKind.Dodge;
   var identityKind=scene is"arrows"or"identity"or"faction-dodge"?CardKind.Dodge:CardKind.Slash;
   S(Identity,"\"cardIdentities\":[{\"id\":\"mandatory-real-hand-name\",\"zones\":[\"hand\"],\"inputKinds\":[\""+char.ToLowerInvariant(deckKind.ToString()[0])+deckKind.ToString()[1..]+"\"],\"inputSuits\":[],\"outputKind\":\""+char.ToLowerInvariant(identityKind.ToString()[0])+identityKind.ToString()[1..]+"\"}]");
   if(QinglongScene(scene))S("fixture:wj-peer-dodge","\"viewAs\":[{\"id\":\"real-dodge\",\"inputKinds\":[],\"inputSuits\":[],\"outputKind\":\"dodge\",\"forPlay\":false,\"forResponse\":true}]");
   var requestOp=scene.StartsWith("basic-",StringComparison.Ordinal)&&scene is not("basic-Peach"or"basic-Alcohol"or"basic-Dodge")?"{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingHighestHand\"},{\"op\":\"requestSlashByTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"asked\"}":scene switch{"target-request"=>"{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingHighestHand\"},{\"op\":\"requestSlashByTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"asked\"}","nearest-request"=>"{\"op\":\"requestSlashByNearest\",\"target\":\"owner\",\"targetKind\":\"otherLiving\",\"amount\":1}","assisted-request"=>"{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingHighestHand\"},{\"op\":\"requestSlashAgainstChosenTarget\",\"target\":\"selectedTarget\",\"resultBind\":\"asked\",\"chooserRef\":{\"kind\":\"selectedTarget\"}}","legal-nearest-request"=>"{\"op\":\"requestLegalSlashByNearest\",\"target\":\"owner\",\"targetKind\":\"otherLiving\",\"amount\":1}","qinglong"or"qinglong-death"=>"{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingHighestHand\"},{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}",_=>""};
   if(scene=="legal-nearest-request")S(Request,"\"activations\":["+A("native-legal-nearest",requestOp,0,"otherLiving")+"]");else if(requestOp!="")S(Request,"\"triggers\":[{\"id\":\"genuine-request\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":["+requestOp+"]}]");
   if(scene=="fixed-request")S(Request,"\"triggers\":[{\"id\":\"genuine-fixed\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"useOwnerSlashAgainstTurnOwner\",\"target\":\"owner\",\"ignoreDistance\":true}]}]");
   if(scene=="loss")S("fixture:wj-conditional","\"hpSkillGrants\":[{\"id\":\"original-healthy-source\",\"skillId\":\"ol:weijie\",\"minimumHp\":13}]");
   if(scene=="loss")S("fixture:wj-initial-hp","\"triggers\":[{\"id\":\"healthy\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"changeMaximumHp\",\"target\":\"owner\",\"amount\":1},{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}]}]");
   if(PaidWatchScene(scene))S(Watch,"\"triggers\":[{\"id\":\"real-paid-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"skill-program.guo-tu.weijie.discard\"],\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLivingHighestHand\"},{\"op\":\"chooseOption\",\"target\":\"selectedTarget\",\"resultBind\":\"child\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":"+ (scene is"death"or"qinglong-death"?20:1)+"}]}]");
   if(scene=="faction-use")S(Watch,"\"triggers\":[{\"id\":\"actual-supply\",\"window\":\"cardSupplyCompleted\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"],\"optional\":false,\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]");
   if(CostScene(scene)||scene.StartsWith("basic-",StringComparison.Ordinal)||scene.StartsWith("dying-",StringComparison.Ordinal)||scene.EndsWith("-request",StringComparison.Ordinal)||QinglongScene(scene)){var driver=defs.Single(n=>n["id"]!.GetValue<string>()==Driver);if(CostScene(scene)||scene.StartsWith("basic-",StringComparison.Ordinal)&&scene is not("basic-Peach"or"basic-Alcohol")||scene.StartsWith("dying-",StringComparison.Ordinal))driver.AsObject().Remove("activations");driver["modifiers"]!.AsArray().Add(JsonNode.Parse("""{"id":"real-target-hand","query":"initialHandSize","operation":"add","value":4,"priority":0}"""));}
   if(scene is"basic-Peach"or"basic-Alcohol"||QinglongScene(scene))S("fixture:wj-no-slash-target","\"cardPolicies\":[{\"id\":\"native-no-slash-target\",\"kind\":\"prohibitTarget\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"]}]");
   if(scene.StartsWith("dying-",StringComparison.Ordinal))S("fixture:wj-foreign-dying","\"triggers\":[{\"id\":\"genuine-foreign-dying\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":13}]}]");
   if(scene=="qinglong-death"){S("fixture:wj-real-start-weapon","\"triggers\":[{\"id\":\"genuine-setup-equipment\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"anyLiving\"},{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"equipped\"}]}]");S("fixture:wj-low-lord","\"triggers\":[{\"id\":\"native-fragile-lord\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":12}]}]");}
   var labels=defs.ToDictionary(n=>n["id"]!.GetValue<string>(),n=>new{name=n["id"]!.GetValue<string>(),description="真实诿解原生请求边界",optionLabels=new[]{"activations","triggers"}.SelectMany(k=>n[k]?.AsArray()??new JsonArray()).SelectMany(t=>t!["effects"]!.AsArray()).SelectMany(e=>e!["options"]?.AsArray()??new JsonArray()).Select(o=>o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x=>x,_=>"继续")});
   var catalog=SkillProgramCatalog.Load(JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.RulesSchemaVersion,skills=defs}),JsonSerializer.Serialize(new{schemaVersion=SkillProgramCatalog.PresentationSchemaVersion,skills=labels}));
   foreach(var(id,p)in catalog.Programs)b.AddSkill(new(id,id,"真实诿解夹具"){Program=p,ProgramPresentation=catalog.Presentations[id]});
   var skills=new List<string>{Driver,"ol:pingduan"};if(scene=="qinglong-death")skills.Add("fixture:wj-real-start-weapon");if(scene is not("basic-Peach"or"basic-Alcohol")&&!scene.StartsWith("faction-",StringComparison.Ordinal)&&scene!="legal-nearest-request")skills.Add(scene=="loss"?"fixture:wj-conditional":Wj);if(scene.StartsWith("faction-",StringComparison.Ordinal)||scene=="legal-nearest-request")skills.Add(Identity);if(scene=="legal-nearest-request")skills.Add(Request);if(scene=="faction-dodge")skills.Add("classic:hujia");if(scene is"faction-played"or"faction-use")skills.Add("classic:jijiang");if(scene is"basic-Peach"or"basic-Alcohol")skills.Add("fixture:wj-no-slash-target");if(scene.StartsWith("dying-",StringComparison.Ordinal))skills.Add("fixture:wj-foreign-dying");if(scene=="loss")skills.Add("fixture:wj-initial-hp");if(PaidWatchScene(scene))skills.Add(Watch);if(scene=="fixed-request")skills.Add(Request);
   b.AddSkill(new("fixture:wj-pick","本人","公开固定选择"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-100000000d)});
   b.AddGeneral(new(Owner,"真实郭图","ol-guo-tu","fixture:wj-pick",scene.StartsWith("faction-",StringComparison.Ordinal)?scene=="faction-dodge"?"wei":"shu":"qun",scene is"duel"or"duel-wrong"or"barbarian"or"arrows"?2:12,skills,GeneralGender.Male));
   var natural=scene is"duel"or"duel-wrong"or"barbarian"or"arrows";var peers=Enumerable.Range(1,3).Select(i=>"fixture:wj-peer-"+i).ToArray();
   for(var i=1;i<=3;i++)
   {
    var pick="fixture:wj-peer-pick-"+i;b.AddSkill(new(pick,"其他真实角色","公开固定选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,x=>natural?(i==1&&x==Role.Rebel?100000000d:i>1&&x==Role.Loyalist?1000000d*(4-i):-100000d):(i==1&&x==Role.Lord?100000000d:1000000d*(4-i)))});
    var list=new List<string>();if(i!=1||scene is not("duel"or"duel-wrong"or"barbarian"or"arrows"or"borrowed"or"faction-use")&&!scene.StartsWith("dying-",StringComparison.Ordinal))list.Add(Quiet);if(i==1&&(scene is"basic-Peach"or"basic-Alcohol"||scene.StartsWith("faction-",StringComparison.Ordinal)||scene=="legal-nearest-request"))list.Add(Wj);if(i!=1&&(scene is"basic-Peach"or"basic-Alcohol"||QinglongScene(scene)))list.Add("fixture:wj-no-slash-target");if(i==1&&(CostScene(scene)||scene=="basic-Dodge"))list.Add(Insert);if(i==1&&requestOp!=""&&scene!="legal-nearest-request")list.Add(Request);if(i==1&&QinglongScene(scene))list.Add("fixture:wj-peer-dodge");if(i==1&&scene=="qinglong-death")list.Add("fixture:wj-low-lord");if(i==1&&scene=="faction-use")list.Add(Watch);if(scene!="duel-wrong"&&(natural&&i!=1||scene is"wrong"or"identity"||scene=="qinglong-death"&&i!=1||i==3&&scene is not("basic-Peach"or"basic-Alcohol"or"dying-Peach"or"dying-Alcohol"or"basic-Dodge"or"cancel"or"loss"or"death"or"quota")))list.Add(Identity);
    if(PaidWatchScene(scene))list.Add(Watch);b.AddGeneral(new(peers[i-1],"真实其他角色","supporter",pick,i==1&&scene=="faction-dodge"?"wei":i==1&&scene is"faction-played"or"faction-use"?"shu":"wei",12,list.Distinct().ToArray(),GeneralGender.Male));
   }
   b.AddSkill(new("fixture:wj-unused","武器素材","不参与选将"){SelectionWeights=Enum.GetValues<Role>().ToDictionary(x=>x,_=>-200000000d)});b.AddGeneral(new("fixture:wj-weapon","真实备用武将","supporter","fixture:wj-unused","qun",4,[]));
   b.AddCard(new("fixture:wj-card","原生"+deckKind,CardCatalog.Get(deckKind).CategoryName,"固定原实体",deckKind));b.AddDeck(new("fixture:wj-deck","固定小牌堆",4,2,[]){PhysicalCards=Enumerable.Range(0,84).Select(i=>new ContentDeckPhysicalCard("fixture:wj-card",new[]{Suit.Spade,Suit.Heart,Suit.Club,Suit.Diamond}[i%4],i%13+1)).ToArray()});
   b.AddMode(new(Mode,"诿解真实原生小局面",4,4,natural?new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),1},{nameof(Role.Loyalist),2}}:new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Rebel),3}},"fixture:wj-deck",GeneralCandidateCount:5,GeneralPoolIds:[Owner,..peers,"fixture:wj-weapon"]));
  }
 }
}
