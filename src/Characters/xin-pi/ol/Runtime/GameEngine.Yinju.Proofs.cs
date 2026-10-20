namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool YinjuRelationValid(YinjuTurnRelation r)=>r.ActualTurn==_turnNumber&&IsValidPlayerSeat(r.Source.OwnerSeat)&&IsValidPlayerSeat(r.TargetSeat)&&r.Source.OwnerSeat!=r.TargetSeat&&_contentRegistry.GetSkill(r.Source.SkillId).Program?.GameplayHash==r.GameplayHash&&CompleteProgramEventHistory().OfType<YinjuRelationArmedEvent>().Count(e=>e.Relation==r)==1&&CompleteProgramEventHistory().OfType<ProgramSkillStartedEvent>().Count(e=>e.FrameId==r.ActivationFrameId&&e.OwnerSeat==r.Source.OwnerSeat&&e.SkillId==r.Source.SkillId&&e.ActivationId==r.Source.BindingId)==1&&_contentRegistry.GetSkill(r.Source.SkillId).Program!.Activations.Any(a=>a.Id==r.Source.BindingId&&a.Effects is[{Op:SkillProgramEffectOp.ArmYinjuActualTurnRelation}]);
 private YinjuTurnRelation? CurrentYinju(int owner)=>IsValidPlayerSeat(owner)&&_players[owner].IsAlive&&_players[owner].YinjuRelation is{}r&&_players[r.TargetSeat].IsAlive&&YinjuRelationValid(r)?r:null;
}
