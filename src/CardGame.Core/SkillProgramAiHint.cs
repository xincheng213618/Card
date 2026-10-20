namespace CardGame.Core;

/// <summary>
/// Public, pre-evaluated effect summary used by AI policy. It contains no rule
/// graph, hidden cards or target-private state.
/// </summary>
public sealed record SkillProgramAiHint(
    int OwnerDraw,
    int OwnerRecovery,
    int OwnerHpLoss,
    int TargetDraw,
    int TargetRecovery,
    int TargetHpLoss,
    bool GivesSelected,
    bool DiscardsSelected)
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OwnerDamage { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetDamage { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OwnerArmorLoss { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetArmorLoss { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OwnerDirectHpLoss { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetDirectHpLoss { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int OwnerMaximumHpLoss { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public int TargetMaximumHpLoss { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? OwnerWouldEnterDying { get; init; }
    private readonly IReadOnlyList<PublicHealthEffect>? _targetHealthEffects;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<PublicHealthEffect>? TargetHealthEffects
    {
        get => _targetHealthEffects;
        init => _targetHealthEffects = value is null ? null : Array.AsReadOnly(value.Select(effect =>
            effect ?? throw new ArgumentException("A public health estimate cannot contain a null effect.", nameof(value))).ToArray());
    }

    public PublicDamageSequencePrediction PredictTarget(int hp, int maxHp, int armor, int? incomingDamageCap = null) =>
        PublicDamageSequencePrediction.Calculate(hp, maxHp, armor, TargetHealthEffects ??
            (TargetHpLoss > 0 ? [new PublicHealthEffect(PublicHealthEffectKind.HpLoss, TargetHpLoss)] : []), incomingDamageCap);
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool PreferPindianInputOrder { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool MatchingNameOrEquipmentRecastInput { get; init; }
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)]
    public bool ArbitraryOwnedRecastInput { get; init; }
    public double ValueAdjustment { get; init; }
    public double TargetValueAdjustment { get; init; }
}
