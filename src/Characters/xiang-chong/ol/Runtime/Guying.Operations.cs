using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record XiangChongGuyingInvokedEvent(long FrameId, int OwnerSeat, int ActualTurn, long BatchId, long LossSequence, int CardId, CardConversionSource Source) : IGameEvent;
internal sealed class ResolveGuyingAtomicLossChoiceDescriptor:XiangChongDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.ResolveGuyingAtomicLossChoice;}
internal sealed class SettleGuyingPreparationDebtDescriptor:XiangChongDescriptor{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.SettleGuyingPreparationDebt;}
