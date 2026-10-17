namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool UsesTreatedWounded => _contentRegistry.ProgramDependencies
        .HasCardPolicy(SkillProgramCardPolicyKind.OtherWuTreatedWoundedDuringOwnTurn);

    // This is the rule predicate only. Numerical HP, lost HP and native recovery
    // capacity always retain their physical values.
    private bool IsRuleWounded(CharacterState player) => player.HasHpValue &&
        (player.Hp < player.MaxHp || UsesTreatedWounded && IsGuimingTreatedWounded(player));
}
