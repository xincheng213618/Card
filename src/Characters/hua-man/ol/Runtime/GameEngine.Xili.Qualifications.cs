namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool HasRuntimeXili(int seat)=>IsValidPlayerSeat(seat)&&GetSkillBindingShard(_players[seat]).ProgramInstances.Any(i=>i.Program.Triggers.Any(t=>t.Effects.Any(e=>e.Op==SkillProgramEffectOp.PayXiliActualOtherOwnerDamage)));

}
