using System.Collections.ObjectModel;
using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_CurrentTurnHandExchange
{
    internal static string CurrentTurnHandExchangeReason(ProgramSkillFrame frame, string part) =>
        $"skill-program.{frame.SkillId}.{SkillProgramEffectOp.OfferCurrentTurnHandExchange}.{part}";

    internal static CardConversionSource CurrentTurnHandExchangeSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
}
