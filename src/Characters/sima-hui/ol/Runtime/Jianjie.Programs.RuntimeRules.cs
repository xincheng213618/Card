using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_sima_hui_ol_Jianjie_Programs
{
    internal static string SimaHuiSealSource(CardConversionSource issuer,PlayerMarkerKind marker)=>$"sima-hui:seal:{issuer.OwnerSeat}:{issuer.SkillInstanceId}:{marker}";
}
