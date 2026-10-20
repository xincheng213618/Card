using System.Text.Json.Serialization;
namespace CardGame.Core;
public enum LeChengyanStage { FirstDraw, RevealChildren, SecondDraw, SecondDrawChildren }
public sealed record LeChengyanReceipt(CardConversionSource Source, string GameplayHash, long UseId, long ActionId)
{
    public LeChengyanStage Stage { get; init; }
    public NativeDrawOutcome? Draw { get; init; }
    public int? RevealedCardId { get; init; }
    public CardKind? RevealedKind { get; init; }
    public long Before { get; init; }
    public long After { get; init; }
}
public sealed record LeChengyanRevealedEvent(long FrameId, long UseId, long ActionId, int CardId, CardKind EffectiveKind) : IGameEvent;
public sealed record LeChengyanDrawReturnedEvent(long FrameId, bool Second, int ActualCount, int ReplacementCount, long? AttemptFrameId) : IGameEvent;
public sealed record LeChengyanEffectIssuedEvent(long FrameId, long UseId, long ActionId, CardConversionSource Source, int ShownCardId, CardKind EffectKind) : IGameEvent;
public sealed record LeChengyanCompletedEvent(long FrameId, long UseId, bool Replaced) : IGameEvent;
internal sealed class ResolveLeZhugeGuoChengyanDescriptor : LeZhugeGuoDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveLeZhugeGuoChengyan;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.CardUseTargetsFinalized)];
}
