namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string QingyaDiscardReason=global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramDiscardTopPlacement.QingyaDiscardReason;
    private bool HasYaDanCapability=>_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveQingyaBetweenHandDiscards);
    private static bool IsYaDanOperation(SkillProgramEffectOp op) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ya_dan_ol_YaDanLedger.IsYaDanOperation(op);
    private static CardConversionSource YaDanSource(ProgramSkillFrame f) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_ya_dan_ol_YaDanLedger.YaDanSource(f);
}

