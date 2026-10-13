using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record SoleTargetRedirectRequest(long CardUseFrameId, long ActionId,
    CardConversionSource ProtectionSource, int TargetSeat, int TargetIndex);
public sealed record SoleTargetRedirectReturn(ProgramCardTriggerWindowFrame FinalizedCursor,
    SoleTargetRedirectRequest Request, int ResumeTargetIndex, int ResumeTargetSeat, bool ResumeIgnoresArmor,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int TargetEpisodeCandidateStartIndex = 0)
{
    private ProgramCardTriggerWindowFrame _finalizedCursor = FreezeCursor(FinalizedCursor);
    public ProgramCardTriggerWindowFrame FinalizedCursor { get => _finalizedCursor; init => _finalizedCursor = FreezeCursor(value); }
    private static ProgramCardTriggerWindowFrame FreezeCursor(ProgramCardTriggerWindowFrame value) => value with
    {
        Candidates = Array.AsReadOnly(value.Candidates.Select(c => c.FrozenContext is not { } context ? c : c with
        {
            FrozenContext = PaidTargetFrameCollections.FreezeContexts([context])[0] with
            { CardUse = context.CardUse is not { } use ? null : use with
                { DesignatedTargetSeats = use.DesignatedTargetSeats is null ? null : Array.AsReadOnly(use.DesignatedTargetSeats.ToArray()) } }
        }).ToArray())
    };
}
public sealed record SoleTargetRedirectWindowIssuedEvent(long WindowFrameId, SoleTargetRedirectRequest Request,
    int FinalizedCandidateIndex) : IGameEvent;
public sealed record SoleTargetAfterProtectionRedirectedEvent(long WindowFrameId, long ProgramFrameId,
    long CardUseFrameId, long ActionId, CardConversionSource Source, string GameplayHash,
    int InstructionIndex, int TargetIndex, int PreviousTargetSeat, int NewTargetSeat,
    IReadOnlyList<int> BeforeTargets, IReadOnlyList<int> AfterTargets) : IGameEvent;

public sealed partial record ProgramCardTriggerWindowFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SoleTargetRedirectRequest? SoleTargetRedirectPending { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public SoleTargetRedirectReturn? SoleTargetRedirectReturn { get; init; }
}
