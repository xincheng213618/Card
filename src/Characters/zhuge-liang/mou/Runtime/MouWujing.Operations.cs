namespace CardGame.Core;
internal sealed class MouWujingDrawDescriptor : MouZhugeOperationDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.MouWujingDraw; }
public sealed record MouWujingAllowanceGrantedEvent(int OwnerSeat, long BatchId) : IGameEvent;
