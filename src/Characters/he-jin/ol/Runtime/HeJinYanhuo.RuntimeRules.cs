using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_he_jin_ol_HeJinYanhuo
{
    internal static string HeJinDeathReturnHash(HeJinYanhuoReceipt r) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(new { r.Source, r.GameplayHash, r.InstructionIndex,
            r.DeathWindowId, r.CandidateIndex, r.WindowStep, r.Candidate, r.OriginalDeath, r.OriginalDying, r.OriginalDamage, r.OriginalAttackOwnerFrameId,
            r.ActualTurn, r.TurnOwnerSeat, r.KillerSeat, r.Limit }))));
}
