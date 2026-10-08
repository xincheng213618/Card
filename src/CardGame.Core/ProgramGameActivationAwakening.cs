using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum GameActivationAwakeningStage { MaximumPaid, GrantsIssued, Complete }
public sealed record ProgramGameActivationAwakeningReceipt
{
    private IReadOnlyList<long> _activations = Array.AsReadOnly(Array.Empty<long>());
    private IReadOnlyList<string> _grants = Array.AsReadOnly(Array.Empty<string>());
    [JsonConstructor]
    public ProgramGameActivationAwakeningReceipt(int instructionIndex, CardConversionSource source, string gameplayHash,
        long lifecycleFrameId, int actualTurnNumber, int actualTurnOwnerSeat, string countedSkillId, string countedActivationId,
        int minimumValue, IReadOnlyList<long> activationFrameIds, IReadOnlyList<string> skillIds, int maximumBefore,
        int maximumAfter, int hpBefore, int hpAfter, GameActivationAwakeningStage stage)
    {
        InstructionIndex = instructionIndex; Source = source; GameplayHash = gameplayHash; LifecycleFrameId = lifecycleFrameId;
        ActualTurnNumber = actualTurnNumber; ActualTurnOwnerSeat = actualTurnOwnerSeat; CountedSkillId = countedSkillId;
        CountedActivationId = countedActivationId; MinimumValue = minimumValue; ActivationFrameIds = activationFrameIds;
        SkillIds = skillIds; MaximumBefore = maximumBefore; MaximumAfter = maximumAfter; HpBefore = hpBefore; HpAfter = hpAfter; Stage = stage;
    }
    public int InstructionIndex { get; init; }
    public CardConversionSource Source { get; init; }
    public string GameplayHash { get; init; }
    public long LifecycleFrameId { get; init; }
    public int ActualTurnNumber { get; init; }
    public int ActualTurnOwnerSeat { get; init; }
    public string CountedSkillId { get; init; }
    public string CountedActivationId { get; init; }
    public int MinimumValue { get; init; }
    public IReadOnlyList<long> ActivationFrameIds { get => _activations; init => _activations = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<string> SkillIds { get => _grants; init => _grants = Array.AsReadOnly(value.ToArray()); }
    public int MaximumBefore { get; init; }
    public int MaximumAfter { get; init; }
    public int HpBefore { get; init; }
    public int HpAfter { get; init; }
    public GameActivationAwakeningStage Stage { get; init; }
}
public sealed record GameActivationAwakeningMaximumPaidEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    long LifecycleFrameId, int ActualTurnNumber, int ActualTurnOwnerSeat, string CountedSkillId, string CountedActivationId,
    int MinimumValue, int ActualActivationCount, int MaximumBefore, int MaximumAfter, int HpBefore, int HpAfter) : IGameEvent;
public sealed record GameActivationAwakeningGrantIssuedEvent(long FrameId, int OwnerSeat, string SourceSkillId, string GrantedSkillId) : IGameEvent;
public sealed record GameActivationAwakeningCompletedEvent(long FrameId, bool GrantsIssued, bool OwnerAlive) : IGameEvent;

internal interface IGameActivationAwakeningProgramHost
{
    SkillProgramStepOutcome BeginGameActivationAwakening(ProgramSkillFrame frame, SkillProgramEffect effect);
}
internal sealed class AwakenAfterGameActivationsDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AwakenAfterGameActivations;
    public override ISkillProgramEffectHandler Handler { get; } = new AwakenAfterGameActivationsHandler();
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantSkills, static (e, c) => c.GrantSkills(e));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "sourceSkillId", "activationId", "minimumValue", "skillIds", "condition");
        var owner = FilterBoundCardsProgramOperationDescriptor.Owner(r); var minimum = r.RequiredInt("minimumValue");
        var ids = r.RequiredIdentifierArray("skillIds");
        if (minimum is < 1 or > 1000 || ids.Count == 0)
            throw new InvalidOperationException("A game-activation awakening needs a positive bounded count and at least one skill grant.");
        var e = new SkillProgramEffect(Op, owner, -1, r.Condition(), sourceBind: r.RequiredIdentifier("sourceSkillId"),
            stateId: r.RequiredIdentifier("activationId"), minimumValue: minimum, skillIds: ids);
        RequireAlways(e, r.Path); return e;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.TurnStartBeforeNormalFlow)];
}
public sealed class AwakenAfterGameActivationsHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AwakenAfterGameActivations;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int seat, ISkillProgramEffectHost host) =>
        ((IGameActivationAwakeningProgramHost)host).BeginGameActivationAwakening(frame, effect);
}
internal static class GameActivationAwakeningComposition
{
    internal static void ValidateTrigger(string path, IReadOnlyList<SkillProgramEffect> effects, SkillProgramTriggerWindow window,
        SkillProgramTriggerSubject? subject, bool optional, SkillUsageScope? usageScope, int? usageLimit, SkillProgramTurnOwnerScope turnOwnerScope)
    {
        if (effects.Any(e => e.Op == SkillProgramEffectOp.AwakenAfterGameActivations) &&
            (effects is not [{ Op: SkillProgramEffectOp.AwakenAfterGameActivations, Target: SkillProgramEffectTarget.Owner,
                Amount: -1, Condition.Kind: SkillProgramConditionKind.Always }] || window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
                subject is not null && subject != SkillProgramTriggerSubject.Owner || optional || usageScope != SkillUsageScope.Game || usageLimit != 1 ||
                turnOwnerScope != SkillProgramTurnOwnerScope.Own))
            throw new InvalidOperationException($"Invalid skill program at {path}: game-activation awakening requires one mandatory own-preparation game-once instruction.");
    }
}
