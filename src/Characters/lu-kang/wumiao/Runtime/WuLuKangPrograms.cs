using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum WuLuKangPindianStage { BeforeSourceReplacement, PostComparison, GroupClose }
public enum WuLuKangShenduanStage { DiscardChoice, DiscardChildren, RefillChildren, ReserveChildren, BottomChildren, Drawing, DrawChildren, CancelChildren, Completed }
public enum WuLuKangKegouStage { OpponentChoice, Pindian, PindianChildren, GainChildren, OpponentSlashChoice, SlashIssuing, NativeSlash, NativeSlashChildren, RepeatChoice, Completed }
public sealed record WuLuKangPindianInput(int Seat, int CardId, CardKind PrintedKind, CardLocation From, int Rank, bool Top, long? ReservationSequence);
public sealed record WuLuKangPindianAssignment(int Seat, int CardId, int Rank);
public sealed record WuLuKangSourceOpportunity(ProgramTriggerCandidate Candidate, int ParticipantSeat);
public sealed record WuLuKangShenduanPayment(long FrameId, CardConversionSource Source, string GameplayHash, int ParticipantSeat,
    int CostCardId, CardKind CostKind, CardLocation CostFrom, CardLocation CostTo, long CostSequence, long Before, long After,
    int? ReservedCardId = null, int? ReservedPrintedRank = null, long? ReservationSequence = null);
public sealed record WuLuKangSettledComparison(int Index, PindianResult Result);
public sealed record WuLuKangContestReceipt(long ContestId, bool Simultaneous, int SourceSeat, int ActualTurn, int TurnOwner, int TurnOrdinal)
{
    private readonly IReadOnlyList<WuLuKangPindianInput> _original = Array.AsReadOnly(Array.Empty<WuLuKangPindianInput>());
    private readonly IReadOnlyList<WuLuKangPindianAssignment> _assignments = Array.AsReadOnly(Array.Empty<WuLuKangPindianAssignment>());
    private readonly IReadOnlyList<WuLuKangShenduanPayment> _replacements = Array.AsReadOnly(Array.Empty<WuLuKangShenduanPayment>());
    private readonly IReadOnlyList<WuLuKangSettledComparison> _comparisons = Array.AsReadOnly(Array.Empty<WuLuKangSettledComparison>());
    private readonly IReadOnlyList<int> _bottomed = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<WuLuKangPindianInput> Original { get => _original; init => _original = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<WuLuKangPindianAssignment> Assignments { get => _assignments; init => _assignments = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<WuLuKangShenduanPayment> Replacements { get => _replacements; init => _replacements = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<WuLuKangSettledComparison> Comparisons { get => _comparisons; init => _comparisons = Array.AsReadOnly(value.ToArray()); }
    public IReadOnlyList<int> BottomedCardIds { get => _bottomed; init => _bottomed = Array.AsReadOnly(value.ToArray()); }
    public bool SourcesReady { get; init; }
    public bool Revealed { get; init; }
    public bool Cancelled { get; init; }
    public bool Closing { get; init; }
    public string? CancellationReason { get; init; }
}
public sealed record WuLuKangPindianWindowEntry(ProgramTriggerCandidate Candidate, int ParticipantSeat);
public sealed record WuLuKangPindianFrame : ResolutionFrame
{
    private readonly IReadOnlyList<WuLuKangPindianWindowEntry> _entries = Array.AsReadOnly(Array.Empty<WuLuKangPindianWindowEntry>());
    public WuLuKangPindianFrame(long id, long parent, WuLuKangPindianStage stage, int comparisonIndex,
        IReadOnlyList<WuLuKangPindianWindowEntry> entries, PindianResult? result = null)
        : base(id, ResolutionFrameKind.WuLuKangPindian, ResolutionFrameStep.ResolvingEffect)
        => (ParentFrameId, Stage, ComparisonIndex, Entries, Result) = (parent, stage, comparisonIndex, entries, result);
    public long ParentFrameId { get; init; }
    public WuLuKangPindianStage Stage { get; init; }
    public int ComparisonIndex { get; init; }
    public IReadOnlyList<WuLuKangPindianWindowEntry> Entries { get => _entries; init => _entries = Array.AsReadOnly(value.ToArray()); }
    public PindianResult? Result { get; init; }
    public int CandidateIndex { get; init; }
}
public sealed record WuLuKangPindianContext(long ContestId, WuLuKangPindianStage Stage, int ComparisonIndex, int ParticipantSeat);
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
public sealed record WuLuKangKegouQualification(int ActualTurn, int TurnOwner, int TurnOrdinal, long HistoryCutoff, bool Used, bool Played);
public sealed record WuLuKangKegouGain(int CardId, int PrintedRank, CardLocation From, CardLocation To, long Sequence);
public sealed record WuLuKangKegouReceipt(CardConversionSource Source, string GameplayHash, long WindowId, WuLuKangKegouQualification Qualification)
{
    private readonly IReadOnlyList<int> _nativeTargets = Array.AsReadOnly(Array.Empty<int>());
    public IReadOnlyList<int> NativeTargets { get => _nativeTargets; init => _nativeTargets = Array.AsReadOnly(value.ToArray()); }
    public bool NativeTargetsAdjusted { get; init; }
    private readonly IReadOnlyList<WuLuKangKegouGain> _gains = Array.AsReadOnly(Array.Empty<WuLuKangKegouGain>());
    public IReadOnlyList<WuLuKangKegouGain> Gains { get => _gains; init => _gains = Array.AsReadOnly(value.ToArray()); }
    public WuLuKangKegouStage Stage { get; init; }
    public int Iteration { get; init; }
    public int? OpponentSeat { get; init; }
    public long? PindianId { get; init; }
    public PindianResult? Result { get; init; }
    public bool PindianCancelled { get; init; }
    public int GainIndex { get; init; }
    public bool SlashAccepted { get; init; }
    public long NativeUseId { get; init; }
    public long ActionId { get; init; }
    public bool NativeReturned { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed record WuLuKangKegouSlashOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int Iteration, long PindianId, int ActorSeat, int TargetSeat, long UseId, long ActionId);
public abstract partial record ResolutionFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuLuKangContestReceipt? WuLuKangContest { get; init; } }
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuLuKangShenduanReceipt? WuLuKangShenduan { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuLuKangKegouReceipt? WuLuKangKegou { get; init; }
}
public sealed partial record ProgramSkillWindowContext
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuLuKangPindianContext? WuLuKangPindian { get; init; } }
public sealed partial record CardUseFrame
{ [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public WuLuKangKegouSlashOrigin? WuLuKangKegouSlash { get; init; } }

public sealed record WuLuKangContestStartedEvent(long ContestId, bool Simultaneous, int SourceSeat, int Participants, int ActualTurn, int TurnOwner, int TurnOrdinal) : IGameEvent;
public sealed record WuLuKangShenduanStartedEvent(long FrameId, long WindowId, long ContestId, CardConversionSource Source,
    string GameplayHash, WuLuKangPindianStage Stage, int ComparisonIndex, int ParticipantSeat) : IGameEvent;
public sealed record WuLuKangShenduanDiscardPaidEvent(long FrameId, long ContestId, int ParticipantSeat,
    int CardId, CardKind PrintedKind, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record WuLuKangMaximumReservedEvent(long FrameId, long ContestId, int ParticipantSeat) : IGameEvent;
public sealed record WuLuKangPindianInputRevealedEvent(long ContestId, int Seat, int CardId, int Rank, bool Replaced) : IGameEvent;
public sealed record WuLuKangComparisonSettledEvent(long ContestId, int ComparisonIndex, PindianResult Result) : IGameEvent;
public sealed record WuLuKangWinnerBottomedEvent(long FrameId, long ContestId, int ComparisonIndex, int WinnerSeat, int CardId, long MovementSequence) : IGameEvent;
public sealed record WuLuKangKDrawIssuedEvent(long FrameId, long ContestId, int ComparisonIndex, CardConversionSource Source, int RecipientSeat) : IGameEvent;
public sealed record WuLuKangKDrawReturnedEvent(long FrameId, int RecipientSeat, int ActualDrawCount, int ReplacementCount, long? AttemptId, long Before, long After) : IGameEvent;
public sealed record WuLuKangShenduanCompletedEvent(long FrameId, long WindowId, long ContestId, int ComparisonIndex) : IGameEvent;
public sealed record WuLuKangSourcesClosedEvent(long ContestId) : IGameEvent;
public sealed record WuLuKangWindowOpenedEvent(long WindowId, long ContestId, WuLuKangPindianStage Stage, int ComparisonIndex, int CandidateCount) : IGameEvent;
public sealed record WuLuKangWindowCandidateEvent(long WindowId, int Index, CardConversionSource Source, string GameplayHash, int ParticipantSeat) : IGameEvent;
public sealed record WuLuKangWindowClosedEvent(long WindowId, long ContestId, WuLuKangPindianStage Stage, int ComparisonIndex) : IGameEvent;
public sealed record WuLuKangPindianCancelledEvent(long ContestId, long ParentFrameId, bool Simultaneous, string Reason) : IGameEvent;
public sealed record WuLuKangKegouStartedEvent(long FrameId, CardConversionSource Source, string GameplayHash, long WindowId, WuLuKangKegouQualification Qualification) : IGameEvent;
public sealed record WuLuKangKegouPindianIssuedEvent(long FrameId, int Iteration, long PindianId, int OpponentSeat) : IGameEvent;
public sealed record WuLuKangKegouPindianReturnedEvent(long FrameId, int Iteration, long PindianId, PindianResult Result) : IGameEvent;
public sealed record WuLuKangKegouGainPaidEvent(long FrameId, int Iteration, int CardId, int PrintedRank, CardLocation From, CardLocation To, long Sequence) : IGameEvent;
public sealed record WuLuKangKegouSlashDecidedEvent(long FrameId, int Iteration, int OpponentSeat, bool Accepted) : IGameEvent;
public sealed record WuLuKangKegouSlashTargetEvent(long FrameId, int Iteration, int Index, int Seat, bool Adjusted) : IGameEvent;
public sealed record WuLuKangKegouSlashIssuedEvent(WuLuKangKegouSlashOrigin Origin) : IGameEvent;
public sealed record WuLuKangKegouSlashReturnedEvent(long FrameId, int Iteration, long UseId, long ActionId) : IGameEvent;
public sealed record WuLuKangKegouRepeatDecidedEvent(long FrameId, int Iteration, bool Repeat) : IGameEvent;
public sealed record WuLuKangKegouCompletedEvent(long FrameId, int Iterations) : IGameEvent;
public sealed record WuLuKangMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;

internal interface IWuLuKangHost
{
    SkillProgramStepOutcome Shenduan(ProgramSkillFrame frame);
    SkillProgramStepOutcome Kegou(ProgramSkillFrame frame);
}
internal sealed class ResolveWuLuKangShenduanDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangShenduan;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [new RequireTriggerWindow(SkillProgramTriggerWindow.WuLuKangPindian)];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(4));
    public override ISkillProgramEffectHandler Handler { get; } = new WuLuKangShenduanHandler();
}
internal sealed class ResolveWuLuKangKegouDescriptor : ProgramOperationDescriptorBase
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangKegou;
    public override SkillProgramEffect Parse(ProgramOperationNodeReader reader) => ParseOwnerAlways(reader);
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect effect) => [];
    public override ProgramOperationInteraction Interaction => ProgramOperationInteraction.Choice;
    public override ProgramOperationAiPolicy AiPolicy { get; } = new(ProgramOperationAiSemantic.GainCards, static (_, c) => c.PublicControlValue(5));
    public override ISkillProgramEffectHandler Handler { get; } = new WuLuKangKegouHandler();
}
internal sealed class WuLuKangShenduanHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangShenduan;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IWuLuKangHost>().Shenduan(f);
}
internal sealed class WuLuKangKegouHandler : ISkillProgramEffectHandler
{
    public SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveWuLuKangKegou;
    public SkillProgramStepOutcome Execute(SkillProgramEffect e, ProgramSkillFrame f, int target, ISkillProgramEffectHost h) => h.GetCapability<IWuLuKangHost>().Kegou(f);
}
