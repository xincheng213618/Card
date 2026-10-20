using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_bao_san_niang_ol_Bao2025
{
    internal static bool IsBaoOperation(SkillProgramEffectOp op) => op is SkillProgramEffectOp.TakeConvertedSlashTargetAndResetLimitedSkill or
        SkillProgramEffectOp.PayTwoForChosenAssaultAndApplyTargetOutcomes or SkillProgramEffectOp.DyingRecipientChooseReplaceOrGrantThenRecover;
}
