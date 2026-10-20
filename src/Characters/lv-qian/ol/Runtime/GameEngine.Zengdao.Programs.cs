namespace CardGame.Core;
public sealed partial class GameEngine
{
 private const string ZengdaoDepositReason="skill-program.zengdao.deposit",ZengdaoConsumeReason="skill-program.zengdao.consume";
 private IReadOnlyList<PromptChoice> ZengdaoDamageChoices(ProgramSkillFrame f)=>Array.AsReadOnly(f.LvQian!.Choices.Select((entry,index)=>new PromptChoice(new($"zengdao.{f.Id}.{entry.CardId}"),$"移去{GetAdvancedCard(entry.CardId).DisplayName}",[entry.CardId],[],new Dictionary<string,string>{{"program-action","zengdao-damage"},{"choice",index.ToString()}})).ToArray());
 private void PublishZengdaoChoice(ProgramSkillFrame f)
 {
  var s=_contentRegistry.GetSkill(f.SkillId);_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"赠刀：选择移去一张实际赠刀牌。",f.LvQian!.Choices.Select(c=>c.CardId).ToArray(),[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,Choices=ZengdaoDamageChoices(f),SkillPrompt=new(f.SkillId,s.Name,s.Name,s.Description)};ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ConsumeZengdaoForActualSourceDamage)]private PromptChoice SelectZengdaoDamageChoice(ProgramSkillFrame f)=>ZengdaoDamageChoices(f).First();
 [ProgramAnswer("zengdao-damage")]private void ResolveZengdaoDamageChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();AssertLvQian(f);if(f.LvQian is not{Stage:LvQianStage.DamageChoice}r||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!ZengdaoDamageChoices(f).Any(c=>AssistedChoicesEqual([c],[choice])))throw new InvalidOperationException("Zengdao payment lost its real recipient chooser.");ClearPendingDecision();PayZengdaoDamage(f,r.Choices[int.Parse(choice.Parameters["choice"])]);
 }
 private void PayZengdaoDamage(ProgramSkillFrame f,ZengdaoEntityChoice choice)
 {
  var r=f.LvQian!;var grant=choice.Grant;var cardId=choice.CardId;
  if(r.Stage!=LvQianStage.DamageChoice||!r.Choices.Contains(choice)||!IsDurableZengdaoGrant(grant)||_cardZones.GetLocation(cardId)!=grant.Location||_resolutionStack[^2]is not BeforeDamageProgramWindowFrame damageWindow||damageWindow.Id!=r.WindowId||damageWindow.Prevented||CurrentDamageAttempt is not{IsSourceLess:false,IsDelayedJudgmentDamage:false}attack||attack.ResolutionId!=r.DamageAttemptResolutionId||attack.SourceSeat!=f.OwnerSeat||attack.TargetSeat!=r.Targets.Single())throw new InvalidOperationException("Zengdao cannot substitute a grant, physical card or actual damage after the recipient chooses.");
  var before=_movementSequence;ReplaceRuntimeTop(f=f with{LvQian=r with{Stage=LvQianStage.DamagePaymentChildren,Before=before,After=before,Grant=grant,Entities=[cardId]},PendingMovementContinuation=new(f.OwnerSeat,0,null)});MoveCard(GetAdvancedCard(cardId),grant.Location,CardLocation.DiscardPile,new(ZengdaoConsumeReason));f=GetActiveProgramFrame(f.Id);r=f.LvQian!with{After=_movementSequence,ActualCount=1};ReplaceRuntimeTop(f=f with{LvQian=r});attack.IncreaseFinalizedDamageAmount(1);ReplaceRuntimeFrame(damageWindow.Id,damageWindow with{Amount=checked(damageWindow.Amount+1)});AdvanceEventRulesAndQueueFact(new ZengdaoDamagePaidEvent(f.Id,attack.ResolutionId,grant.Source,f.OwnerSeat,damageWindow.TargetSeat,cardId,before,_movementSequence));IssueLvQianSegment(f);
 }
}
