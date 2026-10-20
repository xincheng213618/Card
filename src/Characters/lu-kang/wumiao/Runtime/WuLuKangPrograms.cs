using System.Text.Json.Serialization;
namespace CardGame.Core;

public enum WuLuKangPindianStage { BeforeSourceReplacement, PostComparison, GroupClose }
public sealed record WuLuKangPindianInput(int Seat, int CardId, CardKind PrintedKind, CardLocation From, int Rank, bool Top, long? ReservationSequence);
public sealed record WuLuKangPindianAssignment(int Seat, int CardId, int Rank);
public sealed record WuLuKangSourceOpportunity(ProgramTriggerCandidate Candidate, int ParticipantSeat);
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
public sealed record WuLuKangMaximumReservedEvent(long FrameId, long ContestId, int ParticipantSeat) : IGameEvent;
public sealed record WuLuKangPindianInputRevealedEvent(long ContestId, int Seat, int CardId, int Rank, bool Replaced) : IGameEvent;
public sealed record WuLuKangComparisonSettledEvent(long ContestId, int ComparisonIndex, PindianResult Result) : IGameEvent;
public sealed record WuLuKangWinnerBottomedEvent(long FrameId, long ContestId, int ComparisonIndex, int WinnerSeat, int CardId, long MovementSequence) : IGameEvent;
public sealed record WuLuKangKDrawIssuedEvent(long FrameId, long ContestId, int ComparisonIndex, CardConversionSource Source, int RecipientSeat) : IGameEvent;
public sealed record WuLuKangKDrawReturnedEvent(long FrameId, int RecipientSeat, int ActualDrawCount, int ReplacementCount, long? AttemptId, long Before, long After) : IGameEvent;
public sealed record WuLuKangSourcesClosedEvent(long ContestId) : IGameEvent;
public sealed record WuLuKangWindowOpenedEvent(long WindowId, long ContestId, WuLuKangPindianStage Stage, int ComparisonIndex, int CandidateCount) : IGameEvent;
public sealed record WuLuKangWindowCandidateEvent(long WindowId, int Index, CardConversionSource Source, string GameplayHash, int ParticipantSeat) : IGameEvent;
public sealed record WuLuKangWindowClosedEvent(long WindowId, long ContestId, WuLuKangPindianStage Stage, int ComparisonIndex) : IGameEvent;
public sealed record WuLuKangPindianCancelledEvent(long ContestId, long ParentFrameId, bool Simultaneous, string Reason) : IGameEvent;
public sealed record WuLuKangMovementSignedEvent(long FrameId, int CardId, CardKind Kind, CardLocation From, CardLocation To, CardMoveReason Reason, long Sequence) : IGameEvent;

internal interface IWuLuKangHost
{
    SkillProgramStepOutcome Shenduan(ProgramSkillFrame frame);
    SkillProgramStepOutcome Kegou(ProgramSkillFrame frame);
}
