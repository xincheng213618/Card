using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record PindianHandReturnMaterial(int CardId, long Sequence);
public sealed record ProgramPindianHandReturnReceipt(long FrameId, CardConversionSource Source, string GameplayHash,
    long PindianFrameId, int TargetSeat, int SourceRank, int OpponentRank)
{
    public bool AcquisitionPaid { get; init; }
    public long SequenceBefore { get; init; }
    public long SequenceAfter { get; init; }
    public int? ReturnCount { get; init; }
    private readonly IReadOnlyList<PindianHandReturnMaterial> _materials = Array.AsReadOnly(Array.Empty<PindianHandReturnMaterial>());
    public IReadOnlyList<PindianHandReturnMaterial> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record ProgramPindianHandAcquiredEvent(ProgramPindianHandReturnReceipt Receipt) : IGameEvent;
public sealed record ProgramPindianHalfReturnCountEvent(long FrameId, int OwnerSeat, int TargetSeat, int HandCount, int ReturnCount) : IGameEvent;
public sealed record ProgramPindianHandContestReturnedEvent(long FrameId, long PindianFrameId, int OwnerSeat,
    int TargetSeat, int SourceRank, int OpponentRank, bool Won) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPindianHandReturnReceipt? PindianHandReturn { get; init; }
}

internal static class PindianHandReturnContract
{
    internal static bool Matches(IReadOnlyList<SkillProgramEffect> effects) => effects is
        [{ Op: SkillProgramEffectOp.Pindian, Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1, Condition.Kind: SkillProgramConditionKind.Always },
         { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.SelectedTarget, TargetReference: null,
             NumberExpression: SkillProgramNumberExpression.AllOwnedZoneCards, ResultBind: { } taken, Condition.Kind: SkillProgramConditionKind.PindianWon } all,
         { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, TargetReference: null,
             SourceBind: { } obtained, ExceptBind: null, Destination: SkillProgramCardDestination.OwnerHand, DestinationZone: null,
             AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.PindianWon },
         { Op: SkillProgramEffectOp.SelectOwnedCards, Target: SkillProgramEffectTarget.Owner, TargetReference: null,
             NumberExpression: SkillProgramNumberExpression.HandHalfFloor, ResultBind: { } returned, Condition.Kind: SkillProgramConditionKind.PindianWon } half,
         { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, TargetReference: null,
             SourceBind: { } given, ExceptBind: null, Destination: SkillProgramCardDestination.SelectedTargetHand, DestinationZone: null,
             AwaitMovementTriggers: true, Condition.Kind: SkillProgramConditionKind.PindianWon }] &&
        taken == obtained && returned == given && taken != returned &&
        new[] { all, half }.All(e => e.Zones.SequenceEqual([CardZoneKind.Hand]) && e.CardKinds.Count == 0 && e.Suits.Count == 0 &&
            e.MinimumCards == 0 && e.MaximumCards == 0 && !e.AllowDecline && e.RequireExactCount is null) &&
        effects.Skip(1).All(e => e.Condition.SourceBind is null or "__active-pindian-result");

    internal static void Validate(string path, SkillProgramActivation a)
    {
        if (a.PindianHandReturn is null) return;
        if (a.PindianHandReturn != true || a.MinCards != 1 || a.MaxCards != 1 || a.MinTargets != 1 || a.MaxTargets != 1 ||
            a.TargetKind != SkillProgramTargetKind.OtherLivingWithHand || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            a.UsesPerTurn is not null || a.UsesPerPhase != 1 || a.UsesPerGame is not null ||
            a.Condition.Kind != SkillProgramConditionKind.Always || a.MarkerCost is not null || a.ContinueAfterOwnerDeath ||
            a.UsageGroup != a.Id || a.TargetPhaseLedgerId is not null || a.CategoryTargetLedgerId is not null ||
            a.EquipmentSlots.Count != 0 || a.TargetRequiresEmptyEquipmentSlot || a.OtherCharactersOnly ||
            a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits || a.CardCountExpression is not null ||
            a.CardKinds.Count != 0 || a.CardSuits.Count != 0 || a.CardCategories.Count != 0 || !Matches(a.Effects))
            throw new InvalidOperationException($"Invalid skill program at {path}: pindianHandReturn requires one phase-once hand contest and its exact whole-hand acquisition/half-hand return.");
    }
}
