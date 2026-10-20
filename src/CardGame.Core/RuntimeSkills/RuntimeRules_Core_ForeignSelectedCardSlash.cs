using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ForeignSelectedCardSlash
{
    internal static CardConversionSource SelectedForeignSlashSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, frame.ActivationId!, frame.OwnerSeat, frame.SkillInstanceId);
}
