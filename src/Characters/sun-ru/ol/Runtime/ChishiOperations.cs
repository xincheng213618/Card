using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record SunRuOlChishiReceipt(CardConversionSource Source, string GameplayHash, int InstructionIndex,
    long MovementWindowId, SunRuOlTurnToken Turn, int SequenceBefore, int SequenceAfter);
// All newly exposed payloads are scalar or recursively immutable records.
public sealed record SunRuOlChishiAppliedEvent(long FrameId, CardConversionSource Source, long MovementWindowId,
    SunRuOlTurnToken Turn) : IGameEvent;
