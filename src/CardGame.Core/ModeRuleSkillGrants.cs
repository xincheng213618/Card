using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed partial record SkillGrant
{
    /// <summary>A battle rule owned by its mode, independent of a general's printed skills.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsModeRule { get; init; }
}
