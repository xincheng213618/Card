using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MoucuanMaximumPaidEvent(long FrameId, int OwnerSeat, int MaximumBefore, int MaximumAfter, int HpBefore, int HpAfter) : IGameEvent;
public sealed record MoucuanSkillGrantedEvent(long FrameId, int OwnerSeat) : IGameEvent;
internal sealed class ResolveMoucuanAwakeningDescriptor():MaYuanYiDescriptor(SkillProgramEffectOp.ResolveMoucuanAwakening,SkillProgramTriggerWindow.TurnStartBeforeNormalFlow){}
