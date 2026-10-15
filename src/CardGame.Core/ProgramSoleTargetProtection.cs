using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum SoleTargetProtectionStage { ChoosingCost, CostChildren, ChoosingBranch, DrawChildren, QuarantineChildren, Finished }
public sealed record SoleTargetProtectionMaterial(int CardId, CardKind Kind, CardLocation From);
public sealed record SoleTargetProtectionReceipt(CardConversionSource Source, string GameplayHash,
    long ProgramFrameId, int InstructionIndex, long WindowFrameId, long CardUseFrameId, long ActionId,
    int ActualTurnNumber, int ActualTurnOwnerSeat, int ActorSeat, int ProviderSeat, CardKind EffectiveKind,
    int ProtectedSeat, int OwnerHp, int ProtectedHp, IReadOnlyList<int> OriginalTargets,
    IReadOnlyList<SoleTargetProtectionMaterial> Materials, SoleTargetProtectionStage Stage,
    SoleTargetProtectionMaterial? Cost = null, long CostSequenceBefore = 0, long CostSequenceAfter = 0,
    bool SubstitutionChosen = false, long RewardSequenceBefore = 0, long RewardSequenceAfter = 0)
{
    private IReadOnlyList<int> _originalTargets = Array.AsReadOnly(OriginalTargets.ToArray());
    public IReadOnlyList<int> OriginalTargets { get => _originalTargets; init => _originalTargets = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<SoleTargetProtectionMaterial> _materials = Array.AsReadOnly(Materials.ToArray());
    public IReadOnlyList<SoleTargetProtectionMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record SoleTargetQuarantineReceipt(long CardUseFrameId, long ActionId, CardConversionSource Source,
    string GameplayHash, int ActualTurnNumber, int ActualTurnOwnerSeat, int ActorSeat, int ProviderSeat,
    CardKind EffectiveKind, IReadOnlyList<int> TargetSeats, IReadOnlyList<SoleTargetProtectionMaterial> Materials,
    long QuarantineSequenceBefore, long QuarantineSequenceAfter)
{
    private IReadOnlyList<int> _targetSeats = Array.AsReadOnly(TargetSeats.ToArray());
    public IReadOnlyList<int> TargetSeats { get => _targetSeats; init => _targetSeats = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<SoleTargetProtectionMaterial> _materials = Array.AsReadOnly(Materials.ToArray());
    public IReadOnlyList<SoleTargetProtectionMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record SoleTargetProtectionOriginReceipt(long CardUseFrameId, long ActionId, int ActorSeat,
    int ProviderSeat, CardKind EffectiveKind, IReadOnlyList<int> OriginalTargets,
    IReadOnlyList<SoleTargetProtectionMaterial> Materials)
{
    private IReadOnlyList<int> _originalTargets = Array.AsReadOnly(OriginalTargets.ToArray());
    public IReadOnlyList<int> OriginalTargets { get => _originalTargets; init => _originalTargets = Array.AsReadOnly(value.ToArray()); }
    private IReadOnlyList<SoleTargetProtectionMaterial> _materials = Array.AsReadOnly(Materials.ToArray());
    public IReadOnlyList<SoleTargetProtectionMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}
public sealed record SoleTargetProtectionPaidEvent(SoleTargetProtectionReceipt Receipt) : IGameEvent;
public sealed record SoleTargetReplacedEvent(long CardUseFrameId, long ActionId, CardConversionSource Source,
    int PreviousTargetSeat, int NewTargetSeat, IReadOnlyList<int> BeforeTargets, IReadOnlyList<int> AfterTargets) : IGameEvent;
public sealed record SoleTargetQuarantineIssuedEvent(SoleTargetQuarantineReceipt Receipt) : IGameEvent;
public sealed record SoleTargetQuarantineSettledEvent(long CardUseFrameId, long ActionId, int ActorSeat,
    int ActualTurnNumber, int ActualTurnOwnerSeat, CardLocation Destination, IReadOnlyList<int> CardIds,
    long SequenceBefore, long SequenceAfter, bool GameEnded = false) : IGameEvent;
public sealed record SoleTargetQuarantineReturnCursor(IReadOnlyList<SoleTargetQuarantineReceipt> Receipts,
    int Index = 0, long? MovementChildFrameId = null, long? MovementBatchId = null)
{
    private IReadOnlyList<SoleTargetQuarantineReceipt> _receipts = Array.AsReadOnly(Receipts.ToArray());
    public IReadOnlyList<SoleTargetQuarantineReceipt> Receipts { get => _receipts; init => _receipts = Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public CardMovementBatchContext? CurrentBatch { get; init; }
}

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SoleTargetProtectionReceipt? SoleTargetProtection { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SoleTargetQuarantineReceipt? SoleTargetQuarantine { get; init; }
    private IReadOnlyList<CardConversionSource>? _soleTargetProtectionSources;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public IReadOnlyList<CardConversionSource>? SoleTargetProtectionSources
    { get => _soleTargetProtectionSources; init => _soleTargetProtectionSources = value is null ? null : Array.AsReadOnly(value.ToArray()); }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SoleTargetProtectionOriginReceipt? SoleTargetProtectionOrigin { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public long? SoleTargetQuarantineEndReturn { get; init; }
}

internal interface ISoleTargetProtectionHost { SkillProgramStepOutcome ProtectOtherSoleCardTarget(ProgramSkillFrame frame); }
internal sealed class ProtectOtherSoleCardTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ProtectOtherSoleCardTarget;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new ProtectOtherSoleCardTargetHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.NullifySelectedCardEffects, static (_, c) => c.PublicControlValue(12d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireContext(ProgramContextCapability.CardAction)];
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.ProtectOtherSoleCardTarget)) return;
        if (t.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer ||
            !t.Optional || t.IncludeResponseUses || t.CardKinds.Count != 0 || t.CardCategories.Count != 0 ||
            t.Effects is not [{ Op: SkillProgramEffectOp.ProtectOtherSoleCardTarget, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"Invalid skill program at {path}: sole-target protection requires one optional observer finalized-target operation with actual eligibility checked against the owning use.");
    }
}
internal sealed class ProtectOtherSoleCardTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ProtectOtherSoleCardTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) => h.GetCapability<ISoleTargetProtectionHost>().ProtectOtherSoleCardTarget(f);
}
