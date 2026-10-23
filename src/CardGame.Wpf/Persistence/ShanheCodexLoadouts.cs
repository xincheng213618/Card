using System.IO;

namespace CardGame.Wpf.Persistence;

/// <summary>Pure account loadout operations; callers explicitly persist the returned profile.</summary>
public static class ShanheCodexLoadouts
{
    public const int MaximumPlans = 8;
    public const int MaximumNameLength = 24;
    private const int SlotCount = 6;

    public static ShanheCodexProfile EnsurePlans(ShanheCodexProfile profile)
    {
        FileShanheCodexStore.Validate(profile);
        return profile.LoadoutPlans is { } plans
            ? Copy(profile, plans, profile.SelectedLoadoutPlanId!)
            : Copy(profile, [new("default", "方案1", profile.EquippedTacticIds)], "default");
    }

    public static ShanheCodexProfile Select(ShanheCodexProfile profile, string id)
    {
        var next = EnsurePlans(profile);
        RequirePlan(next, id);
        return Copy(next, next.LoadoutPlans!, id);
    }

    public static ShanheCodexProfile Create(ShanheCodexProfile profile, string name, bool copyActive)
    {
        var next = EnsurePlans(profile);
        if (next.LoadoutPlans!.Length >= MaximumPlans)
            throw new InvalidDataException($"最多保留 {MaximumPlans} 套初始战法方案。");
        var id = Guid.NewGuid().ToString("N");
        var plan = new ShanheCodexLoadoutPlan(id, NormalizeName(name), copyActive ? next.EquippedTacticIds : new string?[SlotCount]);
        return Copy(next, [.. next.LoadoutPlans, plan], id);
    }

    public static ShanheCodexProfile Rename(ShanheCodexProfile profile, string id, string name)
    {
        var next = EnsurePlans(profile);
        RequirePlan(next, id);
        var normalized = NormalizeName(name);
        return Copy(next, next.LoadoutPlans!.Select(plan => plan.Id == id ? plan with { Name = normalized } : plan).ToArray(), next.SelectedLoadoutPlanId!);
    }

    public static ShanheCodexProfile Delete(ShanheCodexProfile profile, string id)
    {
        var next = EnsurePlans(profile);
        RequirePlan(next, id);
        if (next.LoadoutPlans!.Length == 1) throw new InvalidDataException("至少保留一套初始战法方案。");
        var remaining = next.LoadoutPlans.Where(plan => plan.Id != id).ToArray();
        return Copy(next, remaining, next.SelectedLoadoutPlanId == id ? remaining[0].Id : next.SelectedLoadoutPlanId!);
    }

    public static ShanheCodexProfile UpdateActiveSlots(ShanheCodexProfile profile, string?[] slots)
    {
        var next = EnsurePlans(profile);
        ValidateSlots(slots, next.UnlockedRewardIds);
        return Copy(next, next.LoadoutPlans!.Select(plan => plan.Id == next.SelectedLoadoutPlanId
            ? plan with { EquippedTacticIds = slots } : plan).ToArray(), next.SelectedLoadoutPlanId!);
    }

    internal static void ValidatePlans(ShanheCodexProfile profile)
    {
        if (profile.LoadoutPlans is null && profile.SelectedLoadoutPlanId is null)
        {
            if (profile.FormatVersion != FileShanheCodexStore.LegacyFormatVersion)
                throw new InvalidDataException("山河册版本与方案目录不一致，原文件已保留。");
            return;
        }
        if (profile.FormatVersion != FileShanheCodexStore.CurrentFormatVersion ||
            profile.LoadoutPlans is not { Length: >= 1 and <= MaximumPlans } plans || profile.SelectedLoadoutPlanId is null)
            throw new InvalidDataException("山河册方案目录不完整，原文件已保留。");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in plans)
        {
            if (plan is null || !ValidId(plan.Id) || !ids.Add(plan.Id) || plan.Name != NormalizeName(plan.Name) || !names.Add(plan.Name))
                throw new InvalidDataException("山河册方案标识或名称无效，原文件已保留。");
            ValidateSlots(plan.EquippedTacticIds, profile.UnlockedRewardIds);
        }
        var selected = plans.SingleOrDefault(plan => plan.Id == profile.SelectedLoadoutPlanId);
        if (selected is null || !selected.EquippedTacticIds.SequenceEqual(profile.EquippedTacticIds, StringComparer.Ordinal))
            throw new InvalidDataException("山河册所选方案与初始战法不一致，原文件已保留。");
    }

    private static bool ValidId(string? id) => id is { Length: >= 1 and <= 64 } &&
        id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    private static string NormalizeName(string? name)
    {
        if (name is null || name.Any(char.IsControl)) throw new InvalidDataException("方案名称不能包含控制字符。");
        var normalized = name.Trim();
        if (normalized.Length is < 1 or > MaximumNameLength)
            throw new InvalidDataException($"方案名称须为 1 至 {MaximumNameLength} 个字符。");
        return normalized;
    }

    private static void ValidateSlots(string?[]? slots, string[] unlocked)
    {
        if (slots is not { Length: SlotCount } ||
            slots.OfType<string>().Distinct(StringComparer.Ordinal).Count() != slots.OfType<string>().Count() ||
            slots.OfType<string>().Any(id => !unlocked.Contains(id, StringComparer.Ordinal)))
            throw new InvalidDataException("方案须包含六个槽位，战法不能重复且必须已经解锁。");
    }

    private static void RequirePlan(ShanheCodexProfile profile, string id)
    {
        if (!profile.LoadoutPlans!.Any(plan => plan.Id == id)) throw new InvalidDataException("指定的初始战法方案不存在。");
    }

    private static ShanheCodexProfile Copy(ShanheCodexProfile profile, ShanheCodexLoadoutPlan[] plans, string selectedId)
    {
        var selected = plans.SingleOrDefault(plan => plan.Id == selectedId)
            ?? throw new InvalidDataException("指定的初始战法方案不存在。");
        var next = profile with
        {
            FormatVersion = FileShanheCodexStore.CurrentFormatVersion,
            UnlockedRewardIds = [.. profile.UnlockedRewardIds],
            ClaimedRewards = [.. profile.ClaimedRewards],
            EquippedTacticIds = [.. selected.EquippedTacticIds],
            LoadoutPlans = plans.Select(plan => plan with { EquippedTacticIds = [.. plan.EquippedTacticIds] }).ToArray(),
            SelectedLoadoutPlanId = selectedId
        };
        FileShanheCodexStore.Validate(next);
        return next;
    }
}
