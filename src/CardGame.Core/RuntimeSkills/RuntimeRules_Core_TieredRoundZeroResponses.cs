namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_TieredRoundZeroResponses
{
    internal static TieredRoundConversionUseReceipt? TieredRoundResponseReceipt(ResolutionFrame owner) => owner switch
    { NullificationWindowFrame n => n.TieredRoundResponseUse, CardUseFrame use => use.TieredRoundResponseUse, _ => null };
}
