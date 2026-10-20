namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool CaoYuRoundFirst(ProgramTriggerCandidate c,ProgramSkillWindowContext context)
 {
  if(c.SkillId!="ol:gongjie"||c.BindingId!="round-first"||context.Window!=SkillProgramTriggerWindow.RoundStarting||!_players[c.OwnerSeat].IsAlive||_roundNumber<1||_turnProgression.Kind==ActualTurnKind.Extra||_roundTurnSeats.Count!=1||!_roundTurnSeats.Contains(_currentSeat)||ProgramEventHistory<RoundStartedEvent>().LastOrDefault()is not{}round||round.RoundNumber!=_roundNumber||round.ActorSeat!=_currentSeat||_resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w=>w.Id==context.ParentFrameId)is not{Window:SkillProgramTriggerWindow.RoundStarting}w||w.OwnerSeat!=_currentSeat||w.CandidateIndex<0||w.CandidateIndex>=w.Candidates.Count||w.Candidates[w.CandidateIndex]!=c||ProgramEventHistory<CaoYuStartedEvent>().Any(e=>e.Source.OwnerSeat==c.OwnerSeat&&e.Source.SkillId==c.SkillId&&e.Round==_roundNumber))return false;
  return CaoYuGiftCards(c.OwnerSeat,c.OwnerSeat).Count>0&&_players.Any(p=>p.IsAlive&&p.Seat!=c.OwnerSeat);
 }
}
