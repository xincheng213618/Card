using System.Text.Json.Serialization;

namespace CardGame.Core;

/// <summary>A judgment's public final value, independent of who subsequently owns its card.</summary>
public sealed record ProgramJudgmentOutcomeBinding(string Name, long JudgmentFrameId, int InstructionIndex,
    int TargetSeat, string Reason, string ResultBind, int? CardId, Suit? Suit, int? Rank);

public sealed record ProgramJudgmentOutcomeRecordedEvent(long ProgramFrameId,
    ProgramJudgmentOutcomeBinding Outcome) : IGameEvent;

public sealed partial record ProgramSkillFrame
{
    private readonly IReadOnlyList<ProgramJudgmentOutcomeBinding>? _judgmentOutcomeBindings;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<ProgramJudgmentOutcomeBinding>? JudgmentOutcomeBindings
    {
        get => _judgmentOutcomeBindings;
        init => _judgmentOutcomeBindings = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
}

internal sealed record CreateJudgmentOutcome(string Name) : ProgramResourceOperation;
