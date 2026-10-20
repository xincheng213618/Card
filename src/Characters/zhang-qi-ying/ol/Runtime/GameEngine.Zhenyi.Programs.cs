namespace CardGame.Core;
public sealed partial class GameEngine
{
 private IReadOnlyList<PromptChoice> ZhenyiJudgmentChoices(ProgramSkillFrame f)=>Array.AsReadOnly(new[]{Suit.Spade,Suit.Heart}.Select(s=>new PromptChoice(new($"zhenyi.{f.Id}.{s}"),s==Suit.Spade?"黑桃5":"红桃5",[],[],new Dictionary<string,string>{{"program-action","zhenyi-judgment"},{"suit",s.ToString()}})).ToArray());
 private void PublishZhenyiJudgment(ProgramSkillFrame f)
 {
  ReplaceRuntimeTop(f with{Step=ResolutionFrameStep.AwaitingResponse});_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"将实际判定结果改为黑桃5或红桃5",[],[],f.OwnerSeat){PromptId=CreatePromptId(),IsPrivate=false,Choices=ZhenyiJudgmentChoices(f)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAiChoice(SkillProgramEffectOp.ChooseFinalJudgmentFiveByZiwei)] private PromptChoice ZhenyiAiJudgment(ProgramSkillFrame f)=>ZhenyiJudgmentChoices(f).First();
 [ProgramAnswer("zhenyi-judgment")] private void ResolveZhenyiJudgment(PromptChoice choice)
 {
  if(_resolutionStack.LastOrDefault()is not ProgramSkillFrame{ZhangQiYing:{Stage:"judgment-choice"}}f||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],ZhenyiJudgmentChoices(f).Where(c=>c.Id==choice.Id).ToArray()))throw new InvalidOperationException("Zhenyi judgment lost its exact actual chooser.");
  AssertZhangQiYing(f);var j=f.WindowContext!.Judgment!;var pending=ActiveJudgment!;if(pending.Id!=j.JudgmentFrameId||GetJudgmentCard(pending)?.Id!=j.CardId||_players[f.OwnerSeat].Markers.GetValueOrDefault(PlayerMarkerKind.Ziwei)!=1)throw new InvalidOperationException("Zhenyi judgment source/result changed before payment.");
  var receipt=new ZhenyiJudgmentOverride(f.Id,pending.Id,ZhangQiYingSource(f),f.GameplayHash,j.CardId,Enum.Parse<Suit>(choice.Parameters["suit"]),5);ClearPendingDecision();SetFaluToken(f,PlayerMarkerKind.Ziwei,0,"ziwei");ReplaceRuntimeFrame(pending.Id,pending with{ZhenyiOverride=receipt,Suit=receipt.Suit});AdvanceEventRulesAndQueueFact(new ZhenyiJudgmentOverriddenEvent(receipt));ReplaceRuntimeTop(f=f with{ZhangQiYing=null});FinishProgramSkill(f,true);
 }
}
