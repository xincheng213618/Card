using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_BlackGiftContests
{
    internal static CardConversionSource RecipientConsequencesSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static string BlackGiftReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest}.gift";

    internal static string BlackGiftDiscardReason(ProgramSkillFrame f) => $"skill-program.{f.SkillId}.{SkillProgramEffectOp.GiveBlackHandAndResolveRecipientContest}.discard";

    internal static int? BlackGiftWinner(PindianResult p) => p.SourceRank == p.OpponentRank ? null : p.SourceWon ? p.SourceSeat : p.OpponentSeat;

    internal static int[] BlackGiftLosers(PindianResult p) => new[] { p.SourceSeat, p.OpponentSeat }.Where(s => s != CardGame.Core.RuntimeSkills.RuntimeRules_Core_BlackGiftContests.BlackGiftWinner(p)).ToArray();

    internal static Dictionary<string, string> BlackGiftParameters(ProgramSkillFrame f, string stage) => new()
    { ["program-action"] = "black-gift-contest", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["stage"] = stage };

    internal static string BlackGiftResultBind(ProgramSkillFrame f) => $"black-gift-result-{f.Id}";
}
