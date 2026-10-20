using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum PrepPoisonOutcomeStage { SkillLossChildren, HpLossChildren }
public sealed record PrepPoisonOutcomeReceipt(CardConversionSource Source, string GameplayHash, long PrepWindowId,
    ProgramJudgmentOutcomeBinding Outcome, int RoundNumber, bool Cured, int RequestedHpLoss, int HpBefore)
{
    public PrepPoisonOutcomeStage Stage { get; init; }
    public bool HpLossApplied { get; init; }
    public int HpAfter { get; init; }
    public int ActualHpLoss { get; init; }
    private readonly IReadOnlyList<ProgramSkillGrantIdentity> _revoked = Array.AsReadOnly(Array.Empty<ProgramSkillGrantIdentity>());
    public IReadOnlyList<ProgramSkillGrantIdentity> RevokedGrants { get => _revoked; init => _revoked = Array.AsReadOnly(value.ToArray()); }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public PrepPoisonOutcomeReceipt? PrepPoisonOutcome { get; init; }
}
public sealed record PrepPoisonOutcomeIssuedEvent(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    long PrepWindowId, long JudgmentFrameId, string OutcomeBind, int RoundNumber, bool Cured, int RequestedHpLoss) : IGameEvent;
public sealed record PrepPoisonGrantRevokedEvent(long ProgramFrameId, int Index, int OwnerSeat, ProgramSkillGrantIdentity Grant) : IGameEvent;
public sealed record PrepPoisonHpLossPaidEvent(long ProgramFrameId, int OwnerSeat, int RequestedCount, int ActualCount, int HpBefore, int HpAfter) : IGameEvent;
public sealed record PrepPoisonOutcomeCompletedEvent(long ProgramFrameId, bool Cured, int ActualHpLoss) : IGameEvent;

internal interface IPrepPoisonOutcomeHost
{ SkillProgramStepOutcome ResolvePrepPoisonOutcomeByRound(ProgramSkillFrame frame); }
internal sealed class ResolvePrepPoisonOutcomeByRoundDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePrepPoisonOutcomeByRound;
    public override ISkillProgramEffectHandler Handler { get; } = new ResolvePrepPoisonOutcomeByRoundHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.Damage, static (_, c) => c.PublicControlValue(-1));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceBind", "suits", "condition");
        var suits = r.RequiredEnumArray<Suit>("suits");
        if (!suits.SequenceEqual([Suit.Diamond])) throw new InvalidOperationException($"{r.Path}: preparation poison requires its final Diamond outcome.");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition(),
            sourceBind: r.RequiredIdentifier("sourceBind"), suits: suits);
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
    internal static bool IsTrigger(SkillProgramTrigger trigger) => Matches(trigger.Effects);
    private static bool Matches(IReadOnlyList<SkillProgramEffect> effects) => effects is
        [{ Op: SkillProgramEffectOp.StartJudgment, Target: SkillProgramEffectTarget.Owner, OutcomeBind: { } outcome, ResultBind: { } cards,
            Condition.Kind: SkillProgramConditionKind.Always, SourceRef: null },
         { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner, Destination: SkillProgramCardDestination.DiscardPile,
            SourceBind: { } cleanup, ExceptBind: null, DestinationZone: null, Condition.Kind: SkillProgramConditionKind.Always },
         { Op: SkillProgramEffectOp.ResolvePrepPoisonOutcomeByRound, SourceBind: { } source }]
        && outcome == source && cards == cleanup;
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolvePrepPoisonOutcomeByRound)) return;
        if (!IsTrigger(trigger) || trigger.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || trigger.Subject != SkillProgramTriggerSubject.Owner ||
            trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || trigger.Optional || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null || trigger.MarkerCost is not null ||
            trigger.NamedUsageGroup is not null || trigger.ChoiceGroup is not null || trigger.RequiredPolarity is not null)
            throw new InvalidOperationException($"{path}: preparation poison requires its exact mandatory own judgment, cleanup and terminal outcome instruction.");
    }
}
public sealed class ResolvePrepPoisonOutcomeByRoundHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolvePrepPoisonOutcomeByRound;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost h) =>
        h.GetCapability<IPrepPoisonOutcomeHost>().ResolvePrepPoisonOutcomeByRound(f);
}
