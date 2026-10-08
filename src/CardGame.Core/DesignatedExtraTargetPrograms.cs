using System.Collections.ObjectModel;

namespace CardGame.Core;

/// <summary>The public choice belongs to one paused designation instruction.</summary>
public sealed record DesignatedExtraTargetDraft(long CardUseFrameId, long ActionId, int InstructionIndex,
    CardConversionSource Source, string GameplayHash)
{
    private readonly IReadOnlyList<int> _originalTargetSeats = Array.Empty<int>();
    private readonly IReadOnlyList<PromptChoice> _choices = Array.Empty<PromptChoice>();
    public IReadOnlyList<int> OriginalTargetSeats
    { get => _originalTargetSeats; init => _originalTargetSeats = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<PromptChoice> Choices
    { get => _choices; init => _choices = Array.AsReadOnly(value.Select(FreezeChoice).ToArray()); }
    internal static PromptChoice FreezeChoice(PromptChoice choice) => choice with
    {
        Cards = Array.AsReadOnly(choice.Cards.ToArray()), Targets = Array.AsReadOnly(choice.Targets.ToArray()),
        ContentIds = Array.AsReadOnly(choice.ContentIds.ToArray()),
        Parameters = new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(choice.Parameters))
    };
}

public sealed record DesignatedExtraTargetOfferedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    int InstructionIndex, CardConversionSource Source, string GameplayHash,
    IReadOnlyList<int> OriginalTargetSeats, IReadOnlyList<int> CandidateTargetSeats) : IGameEvent;
public sealed record DesignatedExtraTargetResolvedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    int InstructionIndex, CardConversionSource Source, string GameplayHash,
    IReadOnlyList<int> OriginalTargetSeats, IReadOnlyList<int> AddedTargetSeats,
    IReadOnlyList<int> ResultTargetSeats) : IGameEvent;
public sealed record DesignatedExtraTargetRedirectedEvent(long ProgramFrameId, long CardUseFrameId, long ActionId,
    CardConversionSource Source, string GameplayHash, int InstructionIndex, int TargetIndex,
    int OriginalTargetSeat, int NewTargetSeat) : IGameEvent;

internal static class DesignatedExtraTargetContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger trigger)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget)) return;
        if (trigger.Window != SkillProgramTriggerWindow.CardUseTargetsFinalized ||
            trigger.OwnerRelation != SkillProgramCardActionOwnerRelation.Actor || trigger.Subject is not null ||
            !trigger.Optional || !trigger.SingleActionInstance || trigger.IncludeResponseUses ||
            trigger.SourceSkillId is not null || trigger.SourceViewAsId is not null ||
            trigger.UsageScope is not null || trigger.UsageLimit is not null || trigger.DynamicUsageLimit is not null ||
            trigger.NamedUsageGroup is not null || trigger.MarkerCost is not null || trigger.ChoiceGroup is not null ||
            trigger.Condition.Kind != SkillProgramTriggerConditionKind.Always || trigger.EvaluateConditionAtResolution ||
            trigger.CardKinds.Count != 0 || trigger.CardCategories.Count != 2 ||
            !trigger.CardCategories.Contains(SkillProgramCardCategory.Basic) ||
            !trigger.CardCategories.Contains(SkillProgramCardCategory.InstantTrick) ||
            trigger.Suits.Count != 0 || trigger.SourceZones.Count != 0 || trigger.DamageOccurrence is not null ||
            trigger.MovementOccurrence is not null || trigger.OnlyDesignatedCardTargets || trigger.AllowNoEventTarget ||
            trigger.Effects is not [{ Op: SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"Invalid skill program at {path}: a distance-free designated extra target requires one optional actor CardUseTargetsFinalized instruction for Basic and InstantTrick, a single actual action and no response uses or usage costs.");
    }
}

internal interface IDesignatedExtraTargetProgramHost
{
    SkillProgramStepOutcome AddOneDistanceFreeCurrentUseTarget(ProgramSkillFrame frame);
}
internal sealed class AddOneDistanceFreeCurrentUseTargetDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget;
    public override ISkillProgramEffectHandler Handler { get; } = new AddOneDistanceFreeCurrentUseTargetHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GrantTurnCardTargetRestriction,
        static (_, context) => context.PublicControlValue(8d));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader)
    {
        reader.AllowOnly("op", "target", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(reader), 0, reader.Condition());
        RequireAlways(effect, reader.Path);
        return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized), new RequireCardActionActor()];
}
internal sealed class AddOneDistanceFreeCurrentUseTargetHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.AddOneDistanceFreeCurrentUseTarget;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        host is IDesignatedExtraTargetProgramHost targetHost ? targetHost.AddOneDistanceFreeCurrentUseTarget(frame) :
            throw new InvalidOperationException("The program host does not support a distance-free designated extra target.");
}
