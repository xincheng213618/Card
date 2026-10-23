using System.IO;
using CardGame.Core.Campaigns;

namespace CardGame.Wpf.Persistence;

/// <summary>Prepares one account credit from a trusted, fully completed campaign session.</summary>
public static class ShanheCodexSettlement
{
    // The verified client screenshot shows ten books for a complete normal-1
    // Zhuhou campaign. Local difficulty 1 uses that sample award. No amounts are
    // inferred for failures, other difficulties, month-end activity or chests.
    public const int NormalOneCompletionBooks = 10;

    /// <summary>The confirmed award before checking whether this account has already claimed it.</summary>
    public static int ConfirmedBookAmount(ShanheCampaignSession session, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(session);
        return difficulty == 1 && session.Snapshot.Phase == ShanheCampaignPhase.Won && GetSettlementKey(session) is not null
            ? NormalOneCompletionBooks : 0;
    }

    public static string? GetSettlementKey(ShanheCampaignSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        var options = session.CreateCheckpoint().Options;
        return options.UseCodexRewardPool && Guid.TryParse(options.ExpeditionId, out var expeditionId) && expeditionId != Guid.Empty
            ? "expedition:" + expeditionId.ToString("N") : null;
    }

    public static bool HasClaimed(ShanheCodexProfile profile, ShanheCampaignSession session)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return GetSettlementKey(session) is { } claimKey && profile.ClaimedRewards?.Contains(claimKey, StringComparer.Ordinal) == true;
    }

    public static int EligibleBookAmount(ShanheCodexProfile profile, ShanheCampaignSession session, int difficulty)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(session);
        if (profile.Books > int.MaxValue - NormalOneCompletionBooks ||
            profile.ClaimedRewards is not { Length: < 20000 } || HasClaimed(profile, session)) return 0;
        try { FileShanheCodexStore.Validate(profile); }
        catch (InvalidDataException) { return 0; }
        return ConfirmedBookAmount(session, difficulty);
    }

    public static bool TryPrepare(ShanheCodexProfile profile, ShanheCampaignSession session, int difficulty,
        out ShanheCodexProfile next)
    {
        next = profile;
        var amount = EligibleBookAmount(profile, session, difficulty);
        if (amount == 0 || GetSettlementKey(session) is not { } claimKey) return false;
        next = profile with
        {
            Books = checked(profile.Books + amount),
            ClaimedRewards = [.. profile.ClaimedRewards, claimKey],
            UnlockedRewardIds = [.. profile.UnlockedRewardIds],
            EquippedTacticIds = [.. profile.EquippedTacticIds],
            LoadoutPlans = profile.LoadoutPlans?.Select(plan => plan with { EquippedTacticIds = [.. plan.EquippedTacticIds] }).ToArray()
        };
        return true;
    }
}
