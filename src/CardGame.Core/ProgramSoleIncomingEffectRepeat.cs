using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record SoleIncomingEffectGrant(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long WindowFrameId, long UseFrameId, long ActionId, int OriginalActorSeat, int TargetSeat,
    CardKind CardKind, int MaximumDrawCount);
public sealed record SoleIncomingEffectRounds(long ActionId, ExtraCardEffectState Native, int Started = 0, bool Active = false, int DrawIndex = 0, bool AttackCompletionPending = false)
{
    private readonly IReadOnlyList<SoleIncomingEffectGrant> _grants = [];
    public IReadOnlyList<SoleIncomingEffectGrant> Grants { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
}
public enum SoleIncomingRangeDrawStage { Requesting, Children }
public sealed record SoleIncomingRangeDrawReceipt(SoleIncomingEffectGrant Grant, int GrantIndex, int AttackRange,
    int RequestedCount, SoleIncomingRangeDrawStage Stage)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? AwaitingProgramFrameId { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NativeDrawOutcome? Outcome { get; init; }
}
public sealed record SoleIncomingEffectIssuedEvent(SoleIncomingEffectGrant Grant) : IGameEvent;
public sealed record SoleIncomingEffectRoundStartedEvent(long UseFrameId, long ActionId, long ProgramFrameId, int Round) : IGameEvent;
public sealed record SoleIncomingEffectRoundCompletedEvent(long UseFrameId, long ActionId, long ProgramFrameId, int Round) : IGameEvent;
public sealed record SoleIncomingRangeDrawRequestedEvent(long UseFrameId, long ProgramFrameId, int RecipientSeat, int AttackRange, int RequestedCount) : IGameEvent;
public sealed record SoleIncomingRangeDrawPaidEvent(long UseFrameId, long ProgramFrameId, int RecipientSeat, int ActualCount, long SequenceBefore, long SequenceAfter) : IGameEvent;
public sealed record SoleIncomingRangeDrawCompletedEvent(long UseFrameId, long ProgramFrameId, bool Requested) : IGameEvent;
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SoleIncomingEffectRounds? SoleIncomingEffects { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public SoleIncomingRangeDrawReceipt? SoleIncomingRangeDraw { get; init; }
}
public sealed partial record CardsMovedTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ResumeSoleIncomingRangeDrawFrameId { get; init; }
}
internal interface ISoleIncomingEffectRepeatHost { void IssueSoleIncomingEffect(ProgramSkillFrame frame, int maximumDrawCount); }
internal sealed class RepeatIncomingSoleCardEffectAndDrawByRangeDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RepeatIncomingSoleCardEffectAndDrawByRange;
    public override ISkillProgramEffectHandler Handler { get; } = new SoleIncomingEffectRepeatHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (e, c) => c.RepeatIncomingEffectAndDraw(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "amount", "condition");
        var amount = reader.RequiredInt("amount");
        if (amount is < 1 or > 20) throw new InvalidOperationException($"{reader.Path}: attack-range draw cap must be 1..20.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), amount, reader.Condition());
        RequireAlways(effect, reader.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RepeatIncomingSoleCardEffectAndDrawByRange)) return;
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.RepeatIncomingSoleCardEffectAndDrawByRange }] ||
            trigger.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Target ||
            !trigger.Optional || trigger.IncludeResponseUses || trigger.UsageScope != SkillUsageScope.Turn || trigger.UsageLimit != 1 ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.MarkerCost is not null || trigger.ChoiceGroup is not null ||
            trigger.NamedUsageGroup is not null || trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0)
            throw new InvalidOperationException($"Invalid skill program at {path}: sole incoming repeat needs one optional unconditional finalized target-owner use trigger, once per actual turn.");
    }
}
internal sealed class SoleIncomingEffectRepeatHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RepeatIncomingSoleCardEffectAndDrawByRange;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host)
    { host.GetCapability<ISoleIncomingEffectRepeatHost>().IssueSoleIncomingEffect(frame, effect.Amount); return SkillProgramStepOutcome.Continue; }
}
