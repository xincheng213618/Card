namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasAnniversaryHanSongUniqueHand(CharacterState owner)=>owner.IsAlive&&_contentRegistry.Skills.ContainsKey(AnniversaryHanSongComposition.Yinbi)&&HasRuntimeSkill(owner,AnniversaryHanSongComposition.Yinbi)&&
  _players.Where(p=>p.IsAlive&&p.Seat!=owner.Seat).All(p=>GetHand(p).Count!=GetHand(owner).Count);
 private IEnumerable<RuleQueryContribution> AnniversaryHanSongNumericContributions(CharacterState owner,SkillRuleQuery query)
 {
  if(query==SkillRuleQuery.SlashLimit&&HasAnniversaryHanSongUniqueHand(owner))yield return new UnlimitedRuleQueryContribution($"skill:{owner.Seat}:anniversary:yinbi:unique-hand");
 }
 private RuleQueryEvaluation ApplyAnniversaryHanSongHandLimit(CharacterState owner,RuleQueryEvaluation ordinary)
 {
  if(!owner.IsAlive||!_contentRegistry.Skills.ContainsKey(AnniversaryHanSongComposition.Yinbi)||!HasRuntimeSkill(owner,AnniversaryHanSongComposition.Yinbi))return ordinary;
  var highest=_players.Where(p=>p.IsAlive).Select(p=>ConvertRuleValue(EvaluateHandLimit(p,suppressHanSongYinbi:true))).DefaultIfEmpty(ConvertRuleValue(ordinary)).Max();
  var set=new FiniteRuleQueryContribution($"skill:{owner.Seat}:anniversary:yinbi:highest-hand-limit",SkillRuleOperation.Set,highest,45900);
  var terms=Array.AsReadOnly(ordinary.Value.Contributions.Append(set).OrderBy(c=>c.SourceId,StringComparer.Ordinal).ToArray());
  var stage=new RuleQueryStageSnapshot("yinbi-highest-hand-limit",ordinary.Value is FiniteRuleQueryValue n?n.Value:0,false,highest,Array.AsReadOnly<RuleQueryContribution>([set]));
  return new(new FiniteRuleQueryValue(highest,terms),ordinary.BaseTerms,Array.AsReadOnly(ordinary.Stages.Append(stage).ToArray()));
 }
 [ProgramDistanceRule(ProgramDistanceStage.CardUseExemption,45900)] private bool HanSongCardDistance(ProgramCardDistanceQuery q)=>HasAnniversaryHanSongUniqueHand(q.Actor);
 [ProgramDistanceRule(ProgramDistanceStage.CardUseLimitExemption,45900)] private bool HanSongCardDistanceLimit(CharacterState owner)=>HasAnniversaryHanSongUniqueHand(owner);
 [ProgramDistanceRule(ProgramDistanceStage.PhysicalSlash,45900)]
 [ProgramDistanceRule(ProgramDistanceStage.VirtualSlash,45900)]
 [ProgramDistanceRule(ProgramDistanceStage.ProvidedSlash,45900)]
 [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash,45900)]
 [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash,45900)]
 [ProgramDistanceRule(ProgramDistanceStage.BorrowedPaidWeaponSlash,45900)]
 [ProgramDistanceRule(ProgramDistanceStage.SpecificSlashExemption,45900)]
 private bool HanSongSlashDistance(ProgramSlashDistanceQuery q)=>HasAnniversaryHanSongUniqueHand(q.Actor);
 private IReadOnlyList<ProgramTriggerCandidate> AppendAnniversaryHanSongDrawEndedCandidates(CharacterState phaseOwner,IReadOnlyList<ProgramTriggerCandidate> existing)
 {
  if(!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryHanSongShuaiyan))return existing;
  var added=_players.Where(p=>p.IsAlive&&p.Seat!=phaseOwner.Seat&&HasRuntimeSkill(p,AnniversaryHanSongComposition.Shuaiyan))
   .SelectMany(p=>CollectEligibleProgramTriggerCandidates(p,SkillProgramTriggerWindow.DrawPhaseEnded,CaptureProgramTriggerFacts(p)))
   .Where(c=>c.SkillId==AnniversaryHanSongComposition.Shuaiyan&&c.BindingId=="other-draw-ended");
  return Array.AsReadOnly(existing.Concat(added).Distinct().OrderBy(c=>RelativeTableOrder(phaseOwner.Seat,c.OwnerSeat)).ThenByDescending(c=>c.Priority).ThenBy(c=>c.SkillId,StringComparer.Ordinal).ThenBy(c=>c.BindingId,StringComparer.Ordinal).ToArray());
 }
 private bool IsAnniversaryHanSongForeignDrawContext(ProgramTriggerCandidate candidate,ProgramSkillWindowContext c)=>
  candidate.SkillId==AnniversaryHanSongComposition.Shuaiyan&&candidate.BindingId=="other-draw-ended"&&c.OwnerSeat==candidate.OwnerSeat&&c.Window==SkillProgramTriggerWindow.DrawPhaseEnded&&
  c.SourceSeat==_currentSeat&&c.TargetSeat==_currentSeat&&candidate.OwnerSeat!=_currentSeat&&_players[_currentSeat].IsAlive&&_phase==TurnPhase.Draw&&
  _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==c.ParentFrameId) is {Window:SkillProgramTriggerWindow.DrawPhaseEnded,Continuation:ProgramLifecycleContinuation.CompleteDrawPhaseEnded} parent&&
  parent.OwnerSeat==_currentSeat&&parent.Candidates.Contains(candidate);
 [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification,45900,SkillProgramEffectOp.ResolveAnniversaryHanSongShuaiyan)]
 private bool CanOfferAnniversaryHanSong(ProgramTriggerCandidate candidate,SkillProgramTrigger trigger,ProgramSkillWindowContext c)
 {
  if(candidate.SkillId!=AnniversaryHanSongComposition.Shuaiyan||c.SourceSeat is not{} subject||!IsValidPlayerSeat(subject)||!IsValidPlayerSeat(candidate.OwnerSeat)||
   c.OwnerSeat!=candidate.OwnerSeat||c.TargetSeat!=subject||subject!=_currentSeat||!_players[subject].IsAlive||!_players[candidate.OwnerSeat].IsAlive)return false;
  if(c.Window is not(SkillProgramTriggerWindow.DrawPhaseEnded or SkillProgramTriggerWindow.DiscardPhaseEnded)||(_phase!=TurnPhase.Draw&&c.Window==SkillProgramTriggerWindow.DrawPhaseEnded)||(_phase!=TurnPhase.Discard&&c.Window==SkillProgramTriggerWindow.DiscardPhaseEnded))return false;
  return trigger.TurnOwnerScope==SkillProgramTurnOwnerScope.Own?subject==candidate.OwnerSeat:
   trigger.TurnOwnerScope==SkillProgramTurnOwnerScope.OtherLiving&&subject!=candidate.OwnerSeat&&GetHand(_players[subject]).Count==GetHand(_players[candidate.OwnerSeat]).Count;
 }
}
