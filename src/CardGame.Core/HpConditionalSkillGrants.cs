using System.Text.Json;

namespace CardGame.Core;

/// <summary>A leaf skill owned while its exact source and declared HP conditions qualify.</summary>
public sealed record SkillProgramHpSkillGrant(string Id, string SkillId, int? MinimumHp, int? MaximumHp,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    int? MinimumLivingWounded = null)
{
    public bool Matches(int hp) => (MinimumHp is null || hp >= MinimumHp) &&
        (MaximumHp is null || hp <= MaximumHp);
}

public sealed record HpConditionalSkillGrantSource(string CapabilityGrantId, string CapabilitySkillId,
    string CapabilitySkillInstanceId, string RuleId);

public sealed record HpConditionalSkillGrantChangedEvent(int OwnerSeat, string GrantId, string SkillId,
    string SkillInstanceId, HpConditionalSkillGrantSource Source, bool Added, int OwnerHp) : IGameEvent;

public sealed record HpConditionalProgramSourceReceipt(long FrameId, int OwnerSeat, string SkillId,
    string SkillInstanceId, string GameplayHash, string GrantId, HpConditionalSkillGrantSource Source);

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public HpConditionalProgramSourceReceipt? HpConditionalSourceReceipt { get; init; }
}

internal static class HpConditionalSkillGrantParser
{
    internal static IReadOnlyList<SkillProgramHpSkillGrant> Parse(JsonElement skill, string path)
    {
        if (!skill.TryGetProperty("hpSkillGrants", out var array)) return [];
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() is 0 or > 16)
            throw new InvalidOperationException(path + ".hpSkillGrants requires one to sixteen rules.");
        var result = new List<SkillProgramHpSkillGrant>();
        foreach (var node in array.EnumerateArray())
        {
            if (node.ValueKind != JsonValueKind.Object || node.EnumerateObject().Any(property =>
                    property.Name is not ("id" or "skillId" or "minimumHp" or "maximumHp" or "minimumLivingWounded")))
                throw new InvalidOperationException(path + ".hpSkillGrants contains an invalid property.");
            string ReadId(string name)
            {
                if (!node.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(value.GetString()) || value.GetString()!.Length > 128)
                    throw new InvalidOperationException(path + ".hpSkillGrants requires " + name + ".");
                return value.GetString()!;
            }
            int? ReadBound(string name)
            {
                if (!node.TryGetProperty(name, out var value)) return null;
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
                    throw new InvalidOperationException(path + ".hpSkillGrants requires integer HP bounds.");
                return number;
            }
            var id = ReadId("id");
            var skillId = ReadId("skillId");
            _ = new ContentId(skillId);
            var minimum = ReadBound("minimumHp");
            var maximum = ReadBound("maximumHp");
            var minimumLivingWounded = ReadBound("minimumLivingWounded");
            if (minimum is null && maximum is null && minimumLivingWounded is null || minimum > maximum ||
                minimumLivingWounded is < 1 or > 16 ||
                result.Any(rule => rule.Id == id || rule.SkillId == skillId))
                throw new InvalidOperationException(path + ".hpSkillGrants requires distinct rules, targets and a nonempty HP condition.");
            result.Add(new(id, skillId, minimum, maximum, minimumLivingWounded));
        }
        return Array.AsReadOnly(result.ToArray());
    }
}
