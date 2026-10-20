using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_ding_shang_wan_ol_DingShangWan
{
    internal static bool IsDingShangWanDrawStage(DingShangWanStage stage)=>stage is DingShangWanStage.OpeningDraw or DingShangWanStage.PairOwnerDraw or DingShangWanStage.PairTargetDraw or DingShangWanStage.FengOwnerDraw or DingShangWanStage.FengTargetDraw;
}
