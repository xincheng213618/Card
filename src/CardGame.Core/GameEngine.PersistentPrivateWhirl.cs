namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsPrivateWhirlDeposit(SkillProgramEffectOp op) =>
        op is SkillProgramEffectOp.DepositPrivateWhirlUntilNextPlayEnding or
            SkillProgramEffectOp.DepositPersistentPrivateWhirl;

    // The immutable program distinguishes the persistent variant. The existing
    // receipt, physical pile, use qualification and cleanup retain their owner.
    private int PrivateWhirlMaximum(string skillId) =>
        _contentRegistry.GetSkill(skillId).Program!.Triggers.Any(trigger =>
            trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.DepositPersistentPrivateWhirl))
            ? int.MaxValue : 5;
}
