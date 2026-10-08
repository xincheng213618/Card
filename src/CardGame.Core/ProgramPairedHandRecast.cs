using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum PairedHandRecastStage { ChoosingPartner, Ready, CostChildren, DrawChildren, Complete }
public enum PairedHandRecastSkipReason { ParticipantUnavailable, MaterialUnavailable, MatchEnded }
public sealed record PairedHandRecastMaterial(int CardId, CardKind PrintedKind, CardLocation From);
public sealed record PairedHandRecastParticipantReceipt(int Seat, PairedHandRecastMaterial? Material)
{
    public bool CostIssued { get; init; }
    public long CostBefore { get; init; }
    public long CostAfter { get; init; }
    public long? CostBatchId { get; init; }
    public bool DrawIssued { get; init; }
    public int ActualDrawCount { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
    public bool Skipped { get; init; }
    public PairedHandRecastSkipReason? SkipReason { get; init; }
}

/// <summary>
/// This generic operation freezes both hand choices, then resolves the owner's
/// whole recast before the partner's. This is its operation contract, rather
/// than a claim about the timing of any not-yet-accepted character content.
/// </summary>
public sealed record ProgramPairedHandRecastReceipt
{
    private IReadOnlyList<PairedHandRecastMaterial> _eligible = Array.AsReadOnly(Array.Empty<PairedHandRecastMaterial>());
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required string UsageGroup { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public int PhaseInstanceId { get; init; }
    public PairedHandRecastStage Stage { get; init; }
    public int Cursor { get; init; }
    public required PairedHandRecastParticipantReceipt Owner { get; init; }
    public required PairedHandRecastParticipantReceipt Partner { get; init; }
    public IReadOnlyList<PairedHandRecastMaterial> EligiblePartnerMaterials
    { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPairedHandRecastReceipt? PairedHandRecast { get; init; }
}

// The selected entities remain private until their respective physical costs
// enter the discard pile. In particular, Started and Skipped carry no card ids.
public sealed record PairedHandRecastStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string UsageGroup, int PartnerSeat, int ActualTurnNumber, int ActualTurnOwnerSeat, int PhaseInstanceId) : IGameEvent;
public sealed record PairedHandRecastPaidEvent(long FrameId, int Cursor, int ParticipantSeat, int CardId,
    CardKind PrintedKind, CardLocation From, long BatchId, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record PairedHandRecastDrawIssuedEvent(long FrameId, int Cursor, int ParticipantSeat,
    int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record PairedHandRecastSkippedEvent(long FrameId, int Cursor, int ParticipantSeat,
    PairedHandRecastSkipReason Reason) : IGameEvent;
public sealed record PairedHandRecastCompletedEvent(long FrameId, bool OwnerPaid, bool PartnerPaid,
    int OwnerDrawCount, int PartnerDrawCount) : IGameEvent;

internal interface IPairedHandRecastProgramHost
{
    SkillProgramStepOutcome PairedHandRecast(ProgramSkillFrame frame, int partnerSeat);
}
internal sealed class PairedHandRecastDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.PairedHandRecast;
    public override ISkillProgramEffectHandler Handler { get; } = new PairedHandRecastHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.Draw(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: paired hand recast requires selectedTarget.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 1, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireSelectedTargetKind(SkillProgramTargetKind.OtherLivingWithHand), new ReadTargetSet(1, 1), new ConsumeSelectedCards(1)];
}
public sealed class PairedHandRecastHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.PairedHandRecast;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IPairedHandRecastProgramHost)host).PairedHandRecast(frame, seat);
}
internal static class PairedHandRecastComposition
{
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.PairedHandRecast)) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.PairedHandRecast, Target: SkillProgramEffectTarget.SelectedTarget,
                Amount: 1, Condition.Kind: SkillProgramConditionKind.Always }] ||
            a.MinCards != 1 || a.MaxCards != 1 || !a.SourceZones.SequenceEqual([CardZoneKind.Hand]) ||
            a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.OtherLivingWithHand ||
            a.UsesPerPhase != 1 || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.ContinueAfterOwnerDeath ||
            a.MarkerCost is not null || a.CardCountExpression is not null || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 ||
            a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
            a.TargetRequiresEmptyEquipmentSlot || a.CategoryTargetLedgerId is not null || a.TargetPhaseLedgerId is not null ||
            a.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException($"Invalid skill program at {path}: paired hand recast requires one phase-limited real-hand card, one other living nonempty-hand target and its sole unconditional operation.");
    }
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.PairedHandRecast)))
            throw new InvalidOperationException($"Invalid skill program at {path}: paired hand recast is an activation-only operation.");
        foreach (var activation in program.Activations) ValidateActivation(path, activation);
    }
}
