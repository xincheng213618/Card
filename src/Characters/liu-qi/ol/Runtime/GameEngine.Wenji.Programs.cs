namespace CardGame.Core;
public sealed partial class GameEngine
{
 private IReadOnlyList<Card> WenjiPayableCards(int seat)=>_cardZones.CardsAt(CardLocation.Hand(seat)).Concat(_cardZones.CardsAt(CardLocation.Equipment(seat))).Where(c=>!c.IsGeneralWeapon).ToArray();
 private void PublishWenjiChoice(ProgramSkillFrame f)
 {
  var r=f.LiuQi!;var seat=r.Stage==LiuQiStage.ChooseRecipient?f.OwnerSeat:r.RecipientSeat!.Value;var choices=LiuQiChoices(f);if(choices.Count==0){FinishLiuQi(f);return;}
  var skill=_contentRegistry.GetSkill(f.SkillId);ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,seat,"请选择问计交牌。",choices.SelectMany(c=>c.Cards).ToArray(),choices.SelectMany(c=>c.Targets).ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=r.Stage==LiuQiStage.ChooseGift,TargetSeat=seat,Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[seat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAnswer("wenji")]
 private void ResolveWenjiChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireLiuQi(f);var r=f.LiuQi!;var seat=r.Stage==LiuQiStage.ChooseRecipient?f.OwnerSeat:r.RecipientSeat!.Value;
  if(_pendingDecision?.PlayerSeat!=seat||!AssistedChoicesEqual([choice],LiuQiChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Wenji lost its exact independent recipient authority.");ClearPendingDecision();
  if(!_players[f.OwnerSeat].IsAlive||!HasRuntimeSkillInstance(_players[f.OwnerSeat],f.SkillId,f.SkillInstanceId)||!_players[seat].IsAlive){FinishLiuQi(f);return;}
  if(r.Stage==LiuQiStage.ChooseRecipient){if(!WenjiEligibleGiftRecipients(f.OwnerSeat,f.SkillId).Any(p=>p.Seat==choice.Targets.Single()))throw new InvalidOperationException("Wenji lost its legal gift recipient.");r=r with{RecipientSeat=choice.Targets.Single(),Stage=LiuQiStage.ChooseGift};ReplaceRuntimeTop(f=f with{LiuQi=r});PublishWenjiChoice(f);return;}
  var id=choice.Cards.Single();var card=GetAdvancedCard(id);r=r with{CardId=id,From=_cardZones.GetLocation(id),Name=card.Kind,Before=_movementSequence,After=_movementSequence,Stage=LiuQiStage.GiftChildren};ReplaceRuntimeTop(f with{LiuQi=r,PendingMovementContinuation=new(f.OwnerSeat,0,null),Step=ResolutionFrameStep.ResolvingEffect});
  MoveProgramCardsFromMultipleSources([id], CardLocation.Hand(f.OwnerSeat), new(WenjiGiftReason), NativeGiveCompletionRecords(f.Id,r.RecipientSeat!.Value,f.OwnerSeat,[id],_movementSequence,(_,records)=>
  {
   var paid=r with{After=records.Single().Sequence};ReplaceRuntimeTop(GetActiveProgramFrame(f.Id)with{LiuQi=paid});AdvanceEventRulesAndQueueFact(new WenjiGiftPaidEvent(f.Id,paid.Source,paid.GameplayHash,paid.ActualTurn,paid.RecipientSeat!.Value,id,paid.From!.Value,paid.Name!.Value,paid.Before,paid.After));
   var grant=new WenjiTurnNameGrant(paid.Source,paid.GameplayHash,paid.ActualTurn,f.Id,paid.RecipientSeat.Value,id,paid.Name.Value,paid.After);_players[f.OwnerSeat].WenjiTurnNameGrants=(_players[f.OwnerSeat].WenjiTurnNameGrants??[]).Append(grant).ToArray();_hasIssuedLiuQi=true;AdvanceEventRulesAndQueueFact(new WenjiNameArmedEvent(grant));
  }));AdvanceRuntimeProgram(f.Id);
 }
}
