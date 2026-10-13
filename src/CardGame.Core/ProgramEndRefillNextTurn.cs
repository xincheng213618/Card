using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum EndRefillNextTurnStage { Target, MovementChildren }
public sealed record EndRefillNextTurnReceipt
{
    private IReadOnlyList<int> _targets = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    public long WindowFrameId { get; init; }
    public CardUseEffectSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public EndRefillNextTurnStage Stage { get; init; }
    public int TargetSeat { get; init; } = -1;
    public int FrozenHandCount { get; init; }
    public int FrozenMaxHp { get; init; }
    public int DrawRequested { get; init; }
    public int DrawActual { get; init; }
    public long MovementBefore { get; init; }
    public long MovementAfter { get; init; }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed record EndRefillNextTurnOfferedEvent(long ProgramFrameId, EndRefillNextTurnReceipt Receipt) : IGameEvent;
public sealed record EndRefillNextTurnIssuedEvent(long ProgramFrameId, EndRefillNextTurnReceipt Receipt) : IGameEvent;
public sealed record EndRefillNextTurnCompletedEvent(long ProgramFrameId, int TargetSeat) : IGameEvent;
public sealed record EndRefillNextTurnConsumedEvent(long ProgramFrameId, int TargetSeat, int ActualTurnNumber,
    int HandCount, int GreatestLivingHandCount, bool Restricted) : IGameEvent;
public sealed record OtherParticipantDamageReplacedEvent(long AttackFrameId, CardUseEffectSource Source,
    int DamageSourceSeat, int TargetSeat, int BaseAmount, DamageNature Nature, bool ChainPropagation) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public EndRefillNextTurnReceipt? EndRefillNextTurn { get; init; }
}
internal interface IEndRefillNextTurnProgramHost
{ SkillProgramStepOutcome RefillAndScheduleNextTurnSelfOnly(ProgramSkillFrame frame); }
internal sealed class RefillAndScheduleNextTurnSelfOnlyDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.RefillAndScheduleNextTurnSelfOnly;
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ISkillProgramEffectHandler Handler { get; } = new RefillAndScheduleNextTurnSelfOnlyHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.EndRefillNextTurnOpportunity());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var e = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.AfterTurnEnded)];
}
internal sealed partial class ProgramAiEstimateContext
{ internal void EndRefillNextTurnOpportunity() => _otherAdjustment += _publicContext.EndRefillNextTurnScore; }
public sealed class RefillAndScheduleNextTurnSelfOnlyHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.RefillAndScheduleNextTurnSelfOnly;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int seat, ISkillProgramEffectHost host) =>
        ((IEndRefillNextTurnProgramHost)host).RefillAndScheduleNextTurnSelfOnly(f);
}
internal static class EndRefillNextTurnContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.RefillAndScheduleNextTurnSelfOnly)) return;
        if (t.Effects is not [var e] || e.Op != SkillProgramEffectOp.RefillAndScheduleNextTurnSelfOnly ||
            e.Target != SkillProgramEffectTarget.Owner || e.Condition.Kind != SkillProgramConditionKind.Always ||
            t.Window != SkillProgramTriggerWindow.AfterTurnEnded || t.Subject != SkillProgramTriggerSubject.Owner ||
            t.TurnOwnerScope != SkillProgramTurnOwnerScope.Own || !t.Optional || t.UsageScope is not null || t.UsageLimit is not null ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.IncludeResponseUses || t.AllowNoEventTarget ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.OwnerRelation is not null || t.DeferredTurnEndOnly)
            throw new InvalidOperationException($"{path}: end refill requires one optional own actual AfterTurnEnded instruction.");
    }
}
