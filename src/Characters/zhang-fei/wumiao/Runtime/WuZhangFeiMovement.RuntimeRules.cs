namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhang_fei_wumiao_WuZhangFeiMovement
{
    internal const string WuZishengMovementBinding = "retaliate-foreign-cards";

    internal static bool IsWuZishengMovementTrigger(SkillProgramTrigger t) => t.Id == WuZishengMovementBinding &&
        t.Window == SkillProgramTriggerWindow.CardsMoved && t.Effects is [{ Op: SkillProgramEffectOp.ResolveWuZhangFeiMovementDamage }];

    internal static string WuZishengCursorHash(ResolutionFrame frame) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(frame))));
}
