using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum UnequalHandHpStage { Target, Action, Discard, AdjustmentChildren, RewardChildren, Gift, GiftChildren }
public sealed record UnequalHandHpPayment(string Step, int SubjectSeat, long Before, long After, int Count,
    int? CardId, CardLocation? From, CardLocation To);
public sealed record UnequalHandHpReceipt
{
    private IReadOnlyList<int> _targets = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    private IReadOnlyList<UnequalHandHpPayment> _payments = [];
    public long WindowFrameId { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int InstructionIndex { get; init; }
    public UnequalHandHpStage Stage { get; init; }
    public int TargetSeat { get; init; } = -1;
    public int FrozenHandCount { get; init; }
    public int FrozenHp { get; init; }
    public bool? Draw { get; init; }
    public bool? RewardQualified { get; init; }
    public IReadOnlyList<int> Targets { get => _targets; init => _targets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
    public IReadOnlyList<UnequalHandHpPayment> Payments { get => _payments; init => _payments = Array.AsReadOnly(value.ToArray()); }
}
public sealed record UnequalHandHpIssuedEvent(long ProgramFrameId, long WindowFrameId, CardConversionSource Source,
    string GameplayHash, int InstructionIndex) : IGameEvent;
public sealed record UnequalHandHpTargetSelectedEvent(long ProgramFrameId, int TargetSeat, int HandCount, int Hp) : IGameEvent;
public sealed record UnequalHandHpActionSelectedEvent(long ProgramFrameId, bool Draw) : IGameEvent;
public sealed record UnequalHandHpMovementIssuedEvent(long ProgramFrameId, UnequalHandHpPayment Payment) : IGameEvent;
public sealed record UnequalHandHpRewardQualifiedEvent(long ProgramFrameId, int TargetSeat, int HandCount, int Hp, bool Qualified) : IGameEvent;
public sealed record UnequalHandHpCompletedEvent(long ProgramFrameId, int TargetSeat) : IGameEvent;

public sealed record TargetedOrdinaryTrickReceipt
{
    private IReadOnlyList<int> _originalTargets = [];
    private IReadOnlyList<PromptChoice> _choices = [];
    public long WindowFrameId { get; init; }
    public long CardUseFrameId { get; init; }
    public long ActionId { get; init; }
    public CardConversionSource Source { get; init; } = null!;
    public string GameplayHash { get; init; } = "";
    public int InstructionIndex { get; init; }
    public IReadOnlyList<int> OriginalTargets { get => _originalTargets; init => _originalTargets = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices { get => _choices; init => _choices = Array.AsReadOnly(value.Select(PromptChoiceProjection.Freeze).ToArray()); }
}
public sealed record TargetedOrdinaryTrickOfferedEvent(long ProgramFrameId, TargetedOrdinaryTrickReceipt Receipt) : IGameEvent;
public sealed record TargetedOrdinaryTrickResolvedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    CardConversionSource Source, string GameplayHash, IReadOnlyList<int> BeforeTargets,
    IReadOnlyList<int> AddedTargets, int? IneffectiveTarget, IReadOnlyList<int> ResultTargets) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public UnequalHandHpReceipt? UnequalHandHp { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public TargetedOrdinaryTrickReceipt? TargetedOrdinaryTrick { get; init; }
}

internal interface ITargetedTrickAndHandHpHost
{
    SkillProgramStepOutcome ResolveUnequalHandHpEnding(ProgramSkillFrame frame);
    SkillProgramStepOutcome AdjustTargetedOrdinaryTrick(ProgramSkillFrame frame);
}
internal abstract class TargetedTrickAndHandHpDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.ChooseOption,
        static (_, context) => context.TargetedTrickHandHpOpportunity());
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 0, r.Condition());
        RequireAlways(effect, r.Path); return effect;
    }
}
internal sealed partial class ProgramAiEstimateContext
{
    internal void TargetedTrickHandHpOpportunity() => _otherAdjustment += _publicContext.TargetedTrickHandHpScore;
}
internal sealed class ResolveUnequalHandHpEndingDescriptor : TargetedTrickAndHandHpDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveUnequalHandHpEnding;
    public override ISkillProgramEffectHandler Handler { get; } = new TargetedTrickAndHandHpHandler(SkillProgramEffectOp.ResolveUnequalHandHpEnding);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayEnding)];
}
internal sealed class AdjustTargetedOrdinaryTrickDescriptor : TargetedTrickAndHandHpDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AdjustTargetedOrdinaryTrick;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ISkillProgramEffectHandler Handler { get; } = new TargetedTrickAndHandHpHandler(SkillProgramEffectOp.AdjustTargetedOrdinaryTrick);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
internal sealed class TargetedTrickAndHandHpHandler(SkillProgramEffectOp op) : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => op;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) => op switch
    {
        SkillProgramEffectOp.ResolveUnequalHandHpEnding => host.GetCapability<ITargetedTrickAndHandHpHost>().ResolveUnequalHandHpEnding(frame),
        SkillProgramEffectOp.AdjustTargetedOrdinaryTrick => host.GetCapability<ITargetedTrickAndHandHpHost>().AdjustTargetedOrdinaryTrick(frame),
        _ => throw new InvalidOperationException("Unknown targeted trick/hand-HP operation.")
    };
}
internal static class TargetedTrickAndHandHpContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        foreach (var op in new[] { SkillProgramEffectOp.ResolveUnequalHandHpEnding, SkillProgramEffectOp.AdjustTargetedOrdinaryTrick })
        {
            if (!trigger.Effects.Any(e => e.Op == op)) continue;
            if (trigger.Effects is not [var effect] || effect.Op != op || effect.Target != SkillProgramEffectTarget.Owner ||
                effect.Condition.Kind != SkillProgramConditionKind.Always || !trigger.Optional || trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always ||
                trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.IncludeResponseUses || trigger.AllowNoEventTarget ||
                (op == SkillProgramEffectOp.ResolveUnequalHandHpEnding
                    ? trigger.Window != SkillProgramTriggerWindow.PlayEnding || trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.Own ||
                      trigger.Subject != SkillProgramTriggerSubject.Owner || trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 0
                    : trigger.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized || trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Target ||
                      trigger.CardKinds.Count != 0 || !trigger.CardCategories.SequenceEqual([SkillProgramCardCategory.InstantTrick])))
                throw new InvalidOperationException($"Invalid skill program at {path}: {op} requires its exact single optional own ending or targeted ordinary-trick entry.");
        }
    }
}
