namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_lu_xun_wumiao_WuLuXunZhangcai
{
    internal static string WuLuXunZhangcaiBinding(SkillProgramTriggerWindow window) => window switch
    {
        SkillProgramTriggerWindow.CardUseCommitted => "accepted-use",
        SkillProgramTriggerWindow.CardResponseAccepted => "accepted-response",
        SkillProgramTriggerWindow.CardSupplyCompleted => "accepted-supply",
        _ => ""
    };

    internal static WuLuXunZhangcaiStartedEvent WuLuXunZhangcaiStart(long id, WuLuXunZhangcaiReceipt r)
    {
        var q = r.Qualification;
        return new(id, q.Source, q.GameplayHash, r.WindowId, r.NativeParentId, q.ActionId, q.Window,
            q.EffectiveKind, q.EffectiveRank, q.RemainingEights, q.DrawCount, q.RuxianFrameId, q.ActualTurn, q.TurnOwner, q.MovementSequence);
    }
}
