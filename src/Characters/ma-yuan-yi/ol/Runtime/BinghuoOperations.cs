using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record BinghuoTargetIssuedEvent(long FrameId, int TargetSeat, long JudgmentId) : IGameEvent;
public sealed record BinghuoJudgmentReturnedEvent(long FrameId, long JudgmentId, int? CardId, Suit? Suit,
    [property: JsonIgnore(Condition=JsonIgnoreCondition.WhenWritingNull)] long? HandoffSequence=null) : IGameEvent;
public sealed record BinghuoDamageIssuedEvent(long FrameId, int SourceSeat, int TargetSeat, int Amount, DamageNature Nature) : IGameEvent;
internal sealed class ResolveBinghuoEndingJudgmentDescriptor():MaYuanYiDescriptor(SkillProgramEffectOp.ResolveBinghuoEndingJudgment,SkillProgramTriggerWindow.TurnEnding){}
