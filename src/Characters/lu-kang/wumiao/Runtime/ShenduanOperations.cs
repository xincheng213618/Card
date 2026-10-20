using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum WuLuKangShenduanStage { DiscardChoice, DiscardChildren, RefillChildren, ReserveChildren, BottomChildren, Drawing, DrawChildren, CancelChildren, Completed }
public sealed record WuLuKangShenduanPayment(long FrameId, CardConversionSource Source, string GameplayHash, int ParticipantSeat,
    int CostCardId, CardKind CostKind, CardLocation CostFrom, CardLocation CostTo, long CostSequence, long Before, long After,
    int? ReservedCardId = null, int? ReservedPrintedRank = null, long? ReservationSequence = null);
public sealed record WuLuKangShenduanReceipt(CardConversionSource Source, string GameplayHash, long WindowId,
    long ContestId, WuLuKangPindianStage WindowStage, int ComparisonIndex, int ParticipantSeat)
{
    private readonly IReadOnlyList<int> _recipients = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> Recipients { get => _recipients; init => _recipients = Array.AsReadOnly(value.ToArray()); }
    public WuLuKangShenduanStage Stage { get; init; }
    public int RecipientIndex { get; init; }
    public WuLuKangShenduanPayment? Payment { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
    public int? SelectedMinimumCardId { get; init; }
    public int? SelectedMinimumRank { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public int DrawOrdinal { get; init; }
}
public sealed record WuLuKangShenduanStartedEvent(long FrameId, long WindowId, long ContestId, CardConversionSource Source,
    string GameplayHash, WuLuKangPindianStage Stage, int ComparisonIndex, int ParticipantSeat) : IGameEvent;
public sealed record WuLuKangShenduanDiscardPaidEvent(long FrameId, long ContestId, int ParticipantSeat,
    int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record WuLuKangShenduanCompletedEvent(long FrameId, long WindowId, long ContestId, int ComparisonIndex) : IGameEvent;
internal sealed class ResolveWuLuKangShenduanDescriptor : ProgramOperationDescriptorBase
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangShenduan;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.WuLuKangPindian)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ISkillProgramEffectHandler Handler { get; } = new WuLuKangShenduanHandler();

    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
internal sealed class WuLuKangShenduanHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangShenduan;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IWuLuKangHost>().Shenduan(f);
}
