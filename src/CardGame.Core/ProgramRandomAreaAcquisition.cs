using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum RandomAreaAcquisitionStage { Visiting, MovementChildren, TurnoverChildren, Finished }
public sealed record RandomAreaAcquisitionStepReceipt(int SeatIndex, int TargetSeat, CardZoneKind SelectedZone,
    uint RandomBefore, uint RandomAfter, long MovementBatchId, long SequenceBefore, long SequenceAfter)
{
    private readonly IReadOnlyList<CardZoneKind> _zones = [];
    private readonly IReadOnlyList<DamageTargetRegionMaterial> _materials = [];
    private readonly IReadOnlyList<CardMovementRecord> _movements = [];
    public IReadOnlyList<CardZoneKind> EligibleZones { get => _zones; init => _zones = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<DamageTargetRegionMaterial> Materials { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<CardMovementRecord> Movements { get => _movements; init => _movements = Array.AsReadOnly(value.ToArray()); }
    public int ActualObtained => Movements.Count(m => m.To.Zone == CardZoneKind.Hand);
}
public sealed record ProgramRandomAreaAcquisitionReceipt(long FrameId, int InstructionIndex, CardConversionSource Source,
    string GameplayHash, long DamageWindowId, long DamageFrameId, int OccurrenceIndex, int DamageAmount)
{
    private readonly IReadOnlyList<int> _seats = [];
    private readonly IReadOnlyList<RandomAreaAcquisitionStepReceipt> _steps = [];
    public IReadOnlyList<int> OtherSeats { get => _seats; init => _seats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<RandomAreaAcquisitionStepReceipt> Steps
    { get => _steps; init => _steps = Array.AsReadOnly(value.Select(s => s with { EligibleZones = s.EligibleZones, Materials = s.Materials, Movements = s.Movements }).ToArray()); }
    public RandomAreaAcquisitionStage Stage { get; init; }
    public int NextSeatIndex { get; init; }
    public bool TurnoverIssued { get; init; }
    public bool TurnoverPrevented { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? FaceDownBefore { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? FaceDownAfter { get; init; }
    public bool Cancelled { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramRandomAreaAcquisitionReceipt? RandomAreaAcquisition { get; init; }
}
public sealed record RandomAreaAcquisitionStartedEvent(ProgramRandomAreaAcquisitionReceipt Receipt) : IGameEvent;
public sealed record RandomAreaAcquisitionRegionPaidEvent(long ProgramFrameId, RandomAreaAcquisitionStepReceipt Step) : IGameEvent;
public sealed record RandomAreaAcquisitionSeatSkippedEvent(long ProgramFrameId, int SeatIndex, int TargetSeat) : IGameEvent;
public sealed record RandomAreaAcquisitionTurnoverIssuedEvent(long ProgramFrameId, bool FaceDownBefore, bool FaceDownAfter, bool Prevented) : IGameEvent;
public sealed record RandomAreaAcquisitionCompletedEvent(long ProgramFrameId, int Regions, int ActualObtained, bool TurnoverIssued, bool Cancelled) : IGameEvent;

internal interface IRandomAreaAcquisitionHost { SkillProgramStepOutcome ObtainRandomAreaFromEachOtherThenTurnOver(ProgramSkillFrame frame); }
internal sealed class ObtainRandomAreaFromEachOtherThenTurnOverDescriptor : ProgramOperationDescriptorBase
{
    internal static readonly CardZoneKind[] Areas = [CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment];
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainRandomAreaFromEachOtherThenTurnOver;
    public override ISkillProgramEffectHandler Handler { get; } = new ObtainRandomAreaFromEachOtherThenTurnOverHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.TakeRandomCardFromEveryOtherCharacter,
        static (e, c) => { c.TakeRandomCardFromEveryOtherCharacter(e); c.TurnOver(e); });
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "zones", "condition");
        var zones = reader.RequiredEnumArray<CardZoneKind>("zones");
        if (!zones.SequenceEqual(Areas)) throw new InvalidOperationException($"{reader.Path}.zones: random whole-area acquisition requires hand, equipment, judgment in that order.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition(), zones: zones);
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
    internal static bool IsTrigger(SkillProgramTrigger trigger) => trigger.Effects is [{ Op: SkillProgramEffectOp.ObtainRandomAreaFromEachOtherThenTurnOver }];
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ObtainRandomAreaFromEachOtherThenTurnOver)) return;
        if (!IsTrigger(trigger) || trigger.Window != SkillProgramTriggerWindow.AfterDamageApplied || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamagePoint || !trigger.Optional || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null || trigger.NamedUsageGroup is not null ||
            trigger.ChoiceGroup is not null || trigger.MarkerCost is not null || trigger.RequiredPolarity is not null ||
            trigger.Effects[0] is not { Target: SkillProgramEffectTarget.Owner, TargetReference: null, Condition.Kind: SkillProgramConditionKind.Always })
            throw new InvalidOperationException($"Invalid skill program at {path}: random whole-area acquisition and turnover need one optional unconditional perDamagePoint owner instruction.");
    }
}
internal sealed class ObtainRandomAreaFromEachOtherThenTurnOverHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ObtainRandomAreaFromEachOtherThenTurnOver;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host.GetCapability<IRandomAreaAcquisitionHost>().ObtainRandomAreaFromEachOtherThenTurnOver(frame);
}
