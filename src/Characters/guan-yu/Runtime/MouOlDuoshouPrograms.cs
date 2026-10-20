namespace CardGame.Core;
public sealed record GuanOpeningUseEvent(long ActionId, int OwnerSeat, int TurnNumber, bool IsRed, bool IsBasic, bool IsWeilin) : IGameEvent;
internal sealed class RecordOpeningTurnUseDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordOpeningTurnUse; }
