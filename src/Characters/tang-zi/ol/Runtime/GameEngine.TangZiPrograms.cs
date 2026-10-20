namespace CardGame.Core;
public sealed partial class GameEngine
{
 private bool MatchesTangZiWoundedGrant(CharacterState owner,SkillProgramHpSkillGrant rule)=>
  XingzhaoRuntime.MatchesWoundedGrant(owner.Seat,rule,ProgramSelectionQueries);

 [ProgramCapability]
 private sealed class SelectionWoundedQueries(GameEngine engine) : ISelectionWoundedQueries
 {
  public bool IdentityConcealed(int seat)=>engine._players[seat].IdentityConcealed;
  public bool IsRuleWounded(int seat)=>engine.IsRuleWounded(engine._players[seat]);
 }
}
