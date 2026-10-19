using System.Text.Json.Serialization;

namespace CardGame.Core;

// A single physical entity, rather than a virtual action's effective name,
// decides whether that action is one Fu. All event members below are scalar.
public sealed record WenlanSource(CardConversionSource Source, string GameplayHash);
public sealed record WenlanActionCapture(WenlanSource? Source, int MaterialCount,
    FuEntityNameSnapshot? FuEntity);
public sealed record WenlanActionAcceptedEvent(long EntryId, int OwnerSeat, int Ordinal,
    long ActionId, long NativeFrameId, CardActionType ActionType, int ProviderSeat,
    CardKind EffectiveKind, WenlanActionCapture Capture) : IGameEvent
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ProgramInstructionIndex { get; init; }
}
public sealed record WenlanLegacyActionBoundEvent(long EntryId, long ActionId) : IGameEvent;
public sealed record WenlanActionReassignedEvent(long CardUseFrameId, long ActionId, int PreviousActorSeat,
    int ActorSeat, long? RetiredEntryId, long? NewEntryId) : IGameEvent;
public sealed record WenlanPairQualification(WenlanSource Source, long FirstEntryId,
    long SecondEntryId, long ActionId, SkillProgramTriggerWindow Window)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? DirectDuelEntryId { get; init; }
    public int SecondOrdinal { get; init; }
}
public sealed record WenlanPairQualifiedEvent(WenlanPairQualification Qualification) : IGameEvent;
public sealed record WenlanResponsePreparedEvent(long CaptureId, long NativeParentId,
    int ProviderSeat, CardKind EffectiveKind, int Before, WenlanActionCapture Capture) : IGameEvent;
public sealed record WenlanResponseMaterialEvent(long CaptureId, int Index, CardActionCost Material) : IGameEvent;
public sealed record WenlanResponseBoundEvent(long CaptureId, long ActionId) : IGameEvent;
public sealed record WenlanResponseCapture(long CaptureId, long NativeParentId, int ProviderSeat,
    CardKind EffectiveKind, int Before, WenlanActionCapture Capture)
{
    private readonly IReadOnlyList<CardActionCost> _materials = Array.AsReadOnly(Array.Empty<CardActionCost>());
    public IReadOnlyList<CardActionCost> Materials
    { get => _materials; init => _materials = Array.AsReadOnly(value.ToArray()); }
}

public abstract partial record ResolutionFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenlanResponseCapture? LeCaoZhiWenlanResponse { get; init; }
}
public sealed partial record CardUseFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenlanActionCapture? LeCaoZhiWenlanUse { get; init; }
}

public enum WenlanStage { SelectingHand, MarkingHand, Obtaining, ObtainChildren }
public enum WenlanNativeParentKind { CardAction, DirectDuel }
public sealed record WenlanNamedObtain(CardKind Kind, int? CardId);
public sealed record WenlanReceipt(WenlanPairQualification Qualification, long WindowId,
    long NativeParentId, WenlanStage Stage, bool CommonName)
{
    private readonly IReadOnlyList<int> _hand = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _selected = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<int> _obtained = Array.AsReadOnly(Array.Empty<int>());
    private readonly IReadOnlyList<WenlanNamedObtain> _plan = Array.AsReadOnly(Array.Empty<WenlanNamedObtain>());
    public IReadOnlyList<int> HandIds { get => _hand; init => _hand = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> SelectedIds { get => _selected; init => _selected = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> ObtainedIds { get => _obtained; init => _obtained = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<WenlanNamedObtain> ObtainPlan { get => _plan; init => _plan = Array.AsReadOnly(value.ToArray()); }
    public int Before { get; init; }
    public int After { get; init; }
    public WenlanNativeParentKind NativeParentKind { get; init; }
}
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenlanReceipt? LeCaoZhiWenlan { get; init; }
}
public sealed record WenlanStartedEvent(long FrameId, WenlanPairQualification Qualification,
    long WindowId, long NativeParentId, bool CommonName) : IGameEvent;
public sealed record WenlanHandCandidateEvent(long FrameId, int Index, int CardId) : IGameEvent;
public sealed record WenlanHandToggledEvent(long FrameId, int CardId, bool Selected) : IGameEvent;
public sealed record WenlanHandConfirmedEvent(long FrameId, int Count) : IGameEvent;
public sealed record WenlanNamedObtainPlannedEvent(long FrameId, int Index, CardKind Kind, int? CardId) : IGameEvent;
public sealed record WenlanEntityObtainedEvent(long FrameId, int CardId, CardKind PrintedKind, int Sequence) : IGameEvent;
public sealed record WenlanObtainReturnedEvent(long FrameId, int Count, int Before, int After) : IGameEvent;
public sealed record WenlanCompletedEvent(long FrameId, int OwnerSeat, long FirstEntryId,
    long SecondEntryId, bool CommonName, int Count) : IGameEvent;

public enum WenlanDirectDuelStage { Running, Returned, WenlanWindow }
public sealed record WenlanDirectDuelReceipt(long ProducerFrameId, int InstructionIndex,
    CardConversionSource ProducerSource, string ProducerGameplayHash, int SourceSeat,
    int TargetSeat, long EntryId, WenlanDirectDuelStage Stage,
    long? WindowId = null, bool DamageApplied = false, int? FinalTargetSeat = null);
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenlanDirectDuelReceipt? LeCaoZhiWenlanDirectDuel { get; init; }
}
public sealed record WenlanDirectDuelWindowFrame(long Id, long ParentFrameId,
    WenlanDirectDuelReceipt Duel, ProgramTriggerCandidate Candidate,
    ProgramSkillWindowContext Context, WenlanPairQualification Qualification)
    : ResolutionFrame(Id, ResolutionFrameKind.LeCaoZhiWenlanDirectDuelWindow, ResolutionFrameStep.ResolvingEffect);
public sealed record WenlanDirectDuelAcceptedEvent(long ProducerFrameId, int InstructionIndex,
    long EntryId, int SourceSeat, int TargetSeat, CardConversionSource ProducerSource,
    string ProducerGameplayHash) : IGameEvent;
public sealed record WenlanDirectDuelReturnedEvent(long ProducerFrameId, int InstructionIndex,
    long EntryId, bool DamageApplied, int FinalTargetSeat) : IGameEvent;
public sealed record WenlanDirectDuelResumedEvent(long ProducerFrameId, int InstructionIndex,
    long EntryId, bool? WenlanCompleted) : IGameEvent;

internal interface ILeCaoZhiWenlanHost { SkillProgramStepOutcome Resolve(ProgramSkillFrame frame); }
internal sealed class ResolveLeCaoZhiWenlanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLeCaoZhiWenlan;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards,
        static (_, context) => context.PublicControlValue(4));
    public override ISkillProgramEffectHandler Handler { get; } = new LeCaoZhiWenlanHandler();
}
internal sealed class LeCaoZhiWenlanHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLeCaoZhiWenlan;
    public SkillProgramStepOutcome Execute(SkillProgramEffect effect, ProgramSkillFrame frame,
        int target, ISkillProgramEffectHost host) => host.GetCapability<ILeCaoZhiWenlanHost>().Resolve(frame);
}
