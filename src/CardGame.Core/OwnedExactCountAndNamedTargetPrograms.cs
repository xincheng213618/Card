namespace CardGame.Core;

/// <summary>The named paid card count and public candidate set frozen at one target instruction.</summary>
public sealed record ProgramNamedBoundTargetSelection(
    int InstructionIndex, int OwnerSeat, string SkillId, string BindingId,
    string SkillInstanceId, string GameplayHash, string SourceBind, int FrozenBoundCardCount,
    int MinimumTargets, int MaximumTargets, SkillProgramTargetKind TargetKind,
    SkillProgramTargetAiOrder TargetAiOrder, IReadOnlyList<int> CandidateSeats)
{
    private IReadOnlyList<int> _candidateSeats = Array.AsReadOnly(CandidateSeats.ToArray());
    public IReadOnlyList<int> CandidateSeats
    {
        get => _candidateSeats;
        init => _candidateSeats = Array.AsReadOnly((value ?? throw new ArgumentNullException(nameof(value))).ToArray());
    }
}

internal static class OwnedExactCountContract
{
    internal static void ValidateActivation(string path, IReadOnlyList<SkillProgramEffect> effects,
        int minCards, int maxCards, int minTargets, int maxTargets, SkillProgramTargetKind targetKind)
    {
        var exact = effects.Where(e => e.RequireExactCount == true).ToArray();
        if (exact.Length == 0) return;
        if (exact.Length != 1 || effects[0] != exact[0] ||
            exact[0] is not { Op: SkillProgramEffectOp.SelectOwnedCards,
                Target: SkillProgramEffectTarget.Owner, NumberExpression: SkillProgramNumberExpression.OwnerLostHp,
                Condition.Kind: SkillProgramConditionKind.Always, TargetReference: null,
                MinimumCards: 0, MaximumCards: 0, AllowDecline: false } ||
            minCards != 0 || maxCards != 0 || minTargets != 0 || maxTargets != 0)
            throw new InvalidOperationException($"Invalid skill program at {path}: exact owned-card count requires the first unconditional owner lost-HP selection of a zero-card, zero-target activation.");
    }
}
