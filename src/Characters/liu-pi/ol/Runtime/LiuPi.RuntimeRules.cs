namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_liu_pi_ol_LiuPi
{
    internal static string LiuPiMovementReason(LiuPiExchangeStage stage) => stage switch {
        LiuPiExchangeStage.RevealChildren => "skill-program.ol:yicheng.exchange.reveal",
        LiuPiExchangeStage.EqualExchangeChildren => "skill-program.ol:yicheng.equal-exchange",
        LiuPiExchangeStage.FullExchangeChildren => "skill-program.ol:yicheng.full-exchange",
        LiuPiExchangeStage.CleanupChildren => "skill-program.ol:yicheng.return-top", _ => "" };
}
