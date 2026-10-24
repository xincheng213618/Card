using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PhaseNamePrediction
{
    internal static CardKind PredictionName(CardKind kind) => kind is CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash ? CardKind.Slash : kind;

    internal static bool IsPhaseNamePredictionResolver(ProgramTriggerCandidate c, SkillProgram p) =>
        p.Triggers.Single(t => t.Id == c.BindingId).Effects is [{ Op: SkillProgramEffectOp.SettlePhaseNamePrediction }];
}
