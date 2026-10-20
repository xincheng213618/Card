using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record WangjingDrawIssuedEvent(long FrameId, long ActionId, int OwnerSeat, int OpponentSeat, int OpponentHp, int HighestHp, int ActualCount) : IGameEvent;
internal sealed class ResolveWangjingBingOpponentDrawDescriptor():MaYuanYiDescriptor(SkillProgramEffectOp.ResolveWangjingBingOpponentDraw,SkillProgramTriggerWindow.CardUseTargetsFinalized)
{ public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[]; }
