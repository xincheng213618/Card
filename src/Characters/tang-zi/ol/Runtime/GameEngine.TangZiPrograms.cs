namespace CardGame.Core;
public sealed partial class GameEngine
{
 // Absent metadata preserves every existing owner-HP grant without querying population.
 private bool MatchesTangZiWoundedGrant(CharacterState owner,SkillProgramHpSkillGrant rule)=>rule.MinimumLivingWounded is not{}minimum ||
  owner.IsAlive && !owner.IdentityConcealed && _players.Count(p=>p.IsAlive && IsRuleWounded(p))>=minimum;
}
