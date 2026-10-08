using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum MatchingRecastStage { OwnerReady, OwnerCostChildren, OwnerDrawChildren, ChoosingPeer, ChoosingPeerCards, PeerCostChildren, PeerDrawChildren, DamageIssued, Complete }
public sealed record MatchingRecastMaterial(int CardId, CardKind PrintedKind, CardLocation From, bool IsGeneralWeapon = false)
{
    public CardLocation Destination => IsGeneralWeapon && From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile;
}
public sealed record MatchingRecastParticipantReceipt(int Seat)
{
    private IReadOnlyList<MatchingRecastMaterial> _materials = Array.AsReadOnly(Array.Empty<MatchingRecastMaterial>());
    public IReadOnlyList<MatchingRecastMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public bool CostIssued { get; init; }
    public long CostBefore { get; init; }
    public long CostAfter { get; init; }
    public long? CostBatchId { get; init; }
    public bool DrawIssued { get; init; }
    public int ActualDrawCount { get; init; }
    public long DrawBefore { get; init; }
    public long DrawAfter { get; init; }
}
public sealed record ProgramMatchingRecastReceipt
{
    private IReadOnlyList<MatchingRecastMaterial> _eligible = Array.AsReadOnly(Array.Empty<MatchingRecastMaterial>());
    public int InstructionIndex { get; init; }
    public required CardConversionSource Source { get; init; }
    public required string GameplayHash { get; init; }
    public required string UsageGroup { get; init; }
    public required string TargetLedgerId { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public int PhaseInstanceId { get; init; }
    public SkillProgramCardCategory Category { get; init; }
    public MatchingRecastStage Stage { get; init; }
    public required MatchingRecastParticipantReceipt Owner { get; init; }
    public required MatchingRecastParticipantReceipt Peer { get; init; }
    public IReadOnlyList<MatchingRecastMaterial> EligiblePeerMaterials { get => _eligible; init => _eligible = Array.AsReadOnly(value.ToArray()); }
    public bool DamageIssued { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramMatchingRecastReceipt? MatchingRecast { get; init; }
}
// Material ids are public only after their own physical recast payment.
public sealed record MatchingRecastStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    string UsageGroup, string TargetLedgerId, int PeerSeat, int ActualTurnNumber, int ActualTurnOwnerSeat, int PhaseInstanceId) : IGameEvent;
public sealed record MatchingRecastPaidEvent(long FrameId, int Cursor, int ParticipantSeat, int CardId,
    CardKind PrintedKind, CardLocation From, long BatchId, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record MatchingRecastDrawIssuedEvent(long FrameId, int Cursor, int ParticipantSeat,
    int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record MatchingRecastDamageIssuedEvent(long FrameId, int SourceSeat, int TargetSeat, int Amount, DamageNature Nature) : IGameEvent;
public sealed record MatchingRecastCompletedEvent(long FrameId, bool OwnerPaid, bool PeerPaid, bool DamageIssued,
    int OwnerDrawCount, int PeerDrawCount) : IGameEvent;

internal interface IMatchingRecastProgramHost
{
    SkillProgramStepOutcome RecastMatchingCardsThenPeerChoice(ProgramSkillFrame frame, int peerSeat, string ledgerId);
}
internal sealed class MatchingRecastDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice;
    public override ISkillProgramEffectHandler Handler { get; } = new MatchingRecastHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e, c) => c.Draw(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        if (r.RequiredEnum<SkillProgramEffectTarget>("target") != SkillProgramEffectTarget.SelectedTarget)
            throw new InvalidOperationException($"Invalid skill program at {r.Path}: matching recast requires selectedTarget.");
        var effect = new SkillProgramEffect(Op, SkillProgramEffectTarget.SelectedTarget, 1, r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new ReadTargetSet(1, 1), new ConsumeSelectedCards(0)];
}
public sealed class MatchingRecastHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IMatchingRecastProgramHost)host).RecastMatchingCardsThenPeerChoice(f, seat, e.StateId!);
}
internal static class MatchingRecastComposition
{
    internal static void ValidateActivation(string path, SkillProgramActivation a)
    {
        if (!a.Effects.Any(e => e.Op == SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice)) return;
        if (a.Effects is not [{ Op: SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice, Target: SkillProgramEffectTarget.SelectedTarget,
                Amount: 1, Condition.Kind: SkillProgramConditionKind.Always } effect] ||
            effect.StateId != a.Id || a.TargetPhaseLedgerId != a.Id || a.MinCards != 2 || a.MaxCards != int.MaxValue ||
            !a.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) ||
            a.MinTargets != 1 || a.MaxTargets != 1 || a.TargetKind != SkillProgramTargetKind.AnyLiving ||
            a.UsesPerPhase is not null || a.UsesPerTurn is not null || a.UsesPerGame is not null || a.ContinueAfterOwnerDeath ||
            a.MarkerCost is not null || a.CardCountExpression is not null || a.CardKinds.Count != 0 || a.CardSuits.Count != 0 ||
            a.CardCategories.Count != 0 || a.EquipmentSlots.Count != 0 || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits ||
            a.TargetRequiresEmptyEquipmentSlot || a.CategoryTargetLedgerId is not null || a.Condition.Kind != SkillProgramConditionKind.Always)
            throw new InvalidOperationException($"Invalid skill program at {path}: matching recast requires an uncapped HE group, one living target and its matching per-target phase ledger.");
    }
    internal static void ValidateProgram(string path, SkillProgram program)
    {
        if (program.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice)))
            throw new InvalidOperationException($"Invalid skill program at {path}: matching recast is activation-only.");
        foreach (var a in program.Activations) ValidateActivation(path, a);
    }
}
