namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_huang_yueying_ol_mou_MouHuangYueyingBingcai_Rounds
{
    internal static MouBingcaiExtraEffectIssuedEvent MouBingcaiIssuedEvent(long useId, long actionId, MouBingcaiExtraEffectGrant g) =>
        new(g.FrameId, useId, actionId, g.Source, g.GameplayHash, g.WindowId, g.BasicKind, g.RecastInvocationId,
            g.MaterialId, g.PrintedKind, g.EffectiveKind, g.From, g.BatchId, g.Before, g.After, g.MovementSequence);
}
