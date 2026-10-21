using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed partial class SkillProgramEffect
{
    /// <summary>Legacy Qimou retains the same once-paid receipt chain without the newer Draw benefit.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? PaidHpLossDrawCards { get; internal init; }
}
