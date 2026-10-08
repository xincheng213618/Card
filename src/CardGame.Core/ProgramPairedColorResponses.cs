using System.Text.Json.Serialization;

namespace CardGame.Core;

public enum PairedColorDispositionStage { ChoosingCard, MovementChildren }

// The direct native response pair is frozen before response-cost observers run.
// Neither a later response nor a later appearance change can replace this pair.
public sealed record PairedColorResponseLinkedEvent(long ResponseWindowFrameId, long ResponseActionId,
    long NativeParentFrameId, long RootCardUseFrameId, long RootActionId,
    ProgramCardContinuation Continuation, int ResponseSeat, int NativeResponseSeat,
    long PairedActionId, int PairedSeat, CardKind ResponseKind, CardKind PairedKind,
    bool? ResponseIsRed, bool? PairedIsRed) : IGameEvent;

public sealed record ProgramPairedColorDispositionReceipt(int InstructionIndex, CardConversionSource Source,
    string GameplayHash, string StateId, PairedColorResponseLinkedEvent Pair, int CounterpartSeat,
    bool Obtain, PairedColorDispositionStage Stage = PairedColorDispositionStage.ChoosingCard,
    int? PaidCardId = null, CardKind? PaidKind = null, CardLocation? PaidFrom = null,
    bool PaidGeneralWeapon = false, long SequenceBefore = 0, long SequenceAfter = 0, long? BatchId = null);

public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ProgramPairedColorDispositionReceipt? PairedColorDisposition { get; init; }
}

public sealed record PairedColorDispositionStartedEvent(long FrameId, CardConversionSource Source,
    string GameplayHash, string StateId, long ResponseActionId, long PairedActionId,
    int CounterpartSeat, bool Obtain) : IGameEvent;
public sealed record PairedColorDispositionPaidEvent(long FrameId, int CardId, CardKind PrintedKind,
    CardLocation From, CardLocation To, bool IsGeneralWeapon, long SequenceBefore, long SequenceAfter,
    long BatchId) : IGameEvent;
public sealed record PairedColorDispositionCompletedEvent(long FrameId, long ResponseActionId,
    int CounterpartSeat, bool Obtain, bool Paid) : IGameEvent;

internal static class PairedColorResponseContract
{
    internal static void ValidateTrigger(string path, SkillProgramTrigger t)
    {
        if (!t.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferPairedColorCardDisposition)) return;
        if (t.Window != SkillProgramTriggerWindow.CardResponseCompleted || t.Subject is not null || !t.Optional ||
            t.OwnerRelation != SkillProgramCardActionOwnerRelation.Observer || t.IncludeResponseUses || t.SingleActionInstance ||
            t.Condition.Kind != SkillProgramTriggerConditionKind.Always || t.EvaluateConditionAtResolution ||
            t.UsageScope is not null || t.UsageLimit is not null || t.DynamicUsageLimit is not null || t.NamedUsageGroup is not null ||
            t.MarkerCost is not null || t.ChoiceGroup is not null || t.SourceSkillId is not null || t.SourceViewAsId is not null ||
            t.CardKinds.Count != 0 || t.CardCategories.Count != 0 || t.SourceZones.Count != 0 || t.DamageOccurrence is not null ||
            t.MovementOccurrence is not null || t.OnlyDesignatedCardTargets || t.AllowNoEventTarget ||
            t.Effects is not [{ Op: SkillProgramEffectOp.OfferPairedColorCardDisposition,
                Target: SkillProgramEffectTarget.Owner, StateId: not null, Condition.Kind: SkillProgramConditionKind.Always }])
            throw new InvalidOperationException($"Invalid skill program at {path}: paired-color disposition requires one optional observer cardResponseCompleted instruction without filters, usage or response-use conversion.");
    }
}
internal interface IPairedColorResponseHost
{
    SkillProgramStepOutcome OfferPairedColorCardDisposition(ProgramSkillFrame frame, string stateId);
}
internal sealed class OfferPairedColorCardDispositionDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.OfferPairedColorCardDisposition;
    public override ISkillProgramEffectHandler Handler { get; } = new PairedColorDispositionHandler();
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramContextCapability RequiredCapabilities => ProgramContextCapability.CardAction;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (effect, context) => context.PairedColorDispositionValue(effect));
    public override SkillProgramEffect Parse(ProgramOperationNodeReader r)
    {
        r.AllowOnly("op", "target", "stateId", "condition");
        var effect = new SkillProgramEffect(Op, FilterBoundCardsProgramOperationDescriptor.Owner(r), 1,
            r.Condition(), stateId: r.RequiredIdentifier("stateId"));
        RequireAlways(effect, r.Path); return effect;
    }
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) =>
        [new RequireTriggerWindow(SkillProgramTriggerWindow.CardResponseCompleted)];
}
internal sealed class PairedColorDispositionHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.OfferPairedColorCardDisposition;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame, int targetSeat, ISkillProgramEffectHost host) =>
        ((IPairedColorResponseHost)host).OfferPairedColorCardDisposition(frame, effect.StateId!);
}
