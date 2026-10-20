namespace CardGame.Core;

// 巧力/清靓 share one host: both duel launches and both reactive payoffs need
// the exact program frame to authenticate their suspended prompts.
internal interface IRuiJiProgramHost
{
    SkillProgramStepOutcome QiaoliWeaponDuel(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome QiaoliArmorDuel(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome QiaoliWeaponDamageDraw(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome QiaoliEndingEquipmentGain(ProgramSkillFrame frame, SkillProgramEffect effect);
    SkillProgramStepOutcome QingliangChooseOption(ProgramSkillFrame frame, SkillProgramEffect effect);
}

// Public-only pricing for the 巧力 launches: a Duel trades response risk against
// target pressure; the unrespondable variant removes the owner's loss branch.
internal sealed partial class ProgramAiEstimateContext
{
    internal void QiaoliDuel(bool unrespondable)
    {
        var otherHand = _publicContext.SelectedTarget?.HandCount ?? 3;
        var ownerHand = _player.HandCount;
        var ownerWin = Math.Clamp(0.5d + (ownerHand - otherHand) * 0.08d + (unrespondable ? 0.25d : 0d), 0.15d, 0.9d);
        _otherAdjustment -= (1d - ownerWin) * (unrespondable ? 6d : 12d);
        _targetAdjustment -= ownerWin * 20d;
    }
}
