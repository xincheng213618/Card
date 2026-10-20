namespace CardGame.Core;
public sealed partial class GameEngine
{
 private int[] LangxiTargets(int owner)=>!_players[owner].IsAlive||!_players[owner].HasHpValue?[]:_players.Where(p=>p.IsAlive&&p.HasHpValue&&p.Seat!=owner&&p.Hp<=_players[owner].Hp).Select(p=>p.Seat).ToArray();
 private IReadOnlyList<PromptChoice> LangxiChoices(ProgramSkillFrame f)=>Array.AsReadOnly(LangxiTargets(f.OwnerSeat).Select(s=>new PromptChoice(new($"langxi.{f.Id}.target-{s}"),$"对 {_players[s].Name} 造成0至2点随机伤害。",[],[s],new Dictionary<string,string>{{"program-action","langxi-target"}})).ToArray());
 private void PublishLangxi(ProgramSkillFrame f)
 {
  var choices=LangxiChoices(f);if(choices.Count==0){FinishLiJue(f);return;}var skill=_contentRegistry.GetSkill(f.SkillId);_pendingDecision=new(DecisionKind.ProgramTrigger,f.OwnerSeat,"选择当前体力不大于你的其他角色。",[],choices.SelectMany(c=>c.Targets).ToArray(),f.OwnerSeat){PromptId=CreatePromptId(),Choices=choices,SkillPrompt=new(f.SkillId,skill.Name,skill.Name,skill.Description)};_status=_players[f.OwnerSeat].IsHuman?EngineStatus.AwaitingHumanResponse:EngineStatus.Running;AdvanceRulesAndPublishState();
 }
 [ProgramAnswer("langxi-target")]
 private void ResolveLangxiChoice(PromptChoice choice)
 {
  var f=(ProgramSkillFrame)_resolutionStack.Last();RequireLiJue(f);if(f.LiJue!.Stage!=LiJueStage.ChoosingTarget||_pendingDecision?.PlayerSeat!=f.OwnerSeat||!AssistedChoicesEqual([choice],LangxiChoices(f).Where(c=>c.Id==choice.Id).ToArray())||!LiJueCurrentSource(f))throw new InvalidOperationException("Langxi changed its unpaid actual HP-qualified target.");ClearPendingDecision();var target=choice.Targets.Single();var roll=_random.Next(3);var r=f.LiJue with{Stage=LiJueStage.DamageChildren,TargetSeat=target,Roll=roll};ReplaceRuntimeTop(f=f with{LiJue=r});AdvanceEventRulesAndQueueFact(new LangxiRandomResolvedEvent(f.Id,r.Source,r.GameplayHash,r.ActualTurn,r.WindowId,target,roll));
  if(roll==0){FinishLiJue(f);return;}BeginProgramSkillDamage(f,target,roll,new(ProgramParticipantRef.Owner));
 }
}
