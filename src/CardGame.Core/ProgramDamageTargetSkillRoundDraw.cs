using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record ProgramSkillGrantIdentity(string GrantId, string SkillId, string SkillInstanceId, string SourceId);
public enum DamageTargetSkillRoundDrawStage { GrantChildren, DrawAttempt, DrawChildren }
public sealed record DamageTargetSkillRoundDrawReceipt(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int TargetSeat, int DamageAmount, DamageNature Nature, int RoundNumber,
    int DrawCount, string GrantedSkillId, ProgramSkillGrantIdentity? GrantedGrant, ProgramSkillGrantIdentity? ExistingGrant)
{
    public DamageTargetSkillRoundDrawStage Stage { get; init; }
    public bool TargetDeadAtGrant { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public NativeDrawOutcome? DrawOutcome { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] public bool Cancelled { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DamageTargetSkillRoundDrawReceipt? DamageTargetSkillRoundDraw { get; init; }
}
public sealed record DamageTargetSkillRoundGrantedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long DamageWindowId, long DamageFrameId, int TargetSeat, int DamageAmount, DamageNature Nature, int RoundNumber,
    int DrawCount, string GrantedSkillId, ProgramSkillGrantIdentity? GrantedGrant, ProgramSkillGrantIdentity? ExistingGrant,
    bool TargetDeadAtGrant = false) : IGameEvent;
public sealed record DamageTargetSkillRoundDrawRequestedEvent(long ProgramFrameId, int OwnerSeat, int RequestedCount) : IGameEvent;
public sealed record DamageTargetSkillRoundDrawPaidEvent(long ProgramFrameId, int OwnerSeat, int RequestedCount, int ActualCount,
    long SequenceBefore, long SequenceAfter, long? AttemptFrameId, bool Replaced, bool Cancelled = false) : IGameEvent;
public sealed record DamageTargetSkillRoundCompletedEvent(long ProgramFrameId, bool DrawRequested, bool Cancelled) : IGameEvent;

internal interface IDamageTargetSkillRoundDrawHost
{ SkillProgramStepOutcome GrantDamageTargetSkillAndDrawByRound(ProgramSkillFrame frame); }
internal sealed class GrantDamageTargetSkillAndDrawByRoundDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.GrantDamageTargetSkillAndDrawByRound;
    public override ISkillProgramEffectHandler Handler { get; } = new GrantDamageTargetSkillAndDrawByRoundHandler();
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.Damage;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(2));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "amount", "skillIds", "condition");
        var cap = r.RequiredInt("amount"); var ids = r.RequiredIdentifierArray("skillIds");
        if (cap is < 1 or > 20 || ids.Count != 1) throw new InvalidOperationException($"{r.Path}: damage target skill/round draw requires one skill and cap 1..20.");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), cap, r.Condition(), skillIds: ids);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterDamageApplied)];
    internal static bool IsTrigger(SkillProgramTrigger trigger) => trigger.Effects is [{ Op: SkillProgramEffectOp.GrantDamageTargetSkillAndDrawByRound }];
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.GrantDamageTargetSkillAndDrawByRound)) return;
        if (!IsTrigger(trigger) || trigger.Window != SkillProgramTriggerWindow.AfterDamageApplied || trigger.Subject != SkillProgramTriggerSubject.DamageSource ||
            trigger.Optional || trigger.DamageOccurrence != SkillProgramDamageOccurrence.PerDamage || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null || trigger.MarkerCost is not null ||
            trigger.NamedUsageGroup is not null || trigger.ChoiceGroup is not null || trigger.RequiredPolarity is not null || trigger.DamageCardKinds.Count != 0)
            throw new InvalidOperationException($"{path}: damage target skill/round draw requires one mandatory unconditional source-per-damage instruction.");
    }
}
public sealed class GrantDamageTargetSkillAndDrawByRoundHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.GrantDamageTargetSkillAndDrawByRound;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IDamageTargetSkillRoundDrawHost>().GrantDamageTargetSkillAndDrawByRound(f);
}
