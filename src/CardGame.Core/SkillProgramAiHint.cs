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
    public double ValueAdjustment { get; init; }
    public double TargetValueAdjustment { get; init; }
}
