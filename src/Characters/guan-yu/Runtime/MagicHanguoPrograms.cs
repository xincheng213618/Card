namespace CardGame.Core;
public sealed record DemonRoundFocusEvent(long FrameId, CardConversionSource Source, int TargetSeat, int RoundNumber) : IGameEvent;
internal sealed class HoldRoundFocusCardsDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.HoldRoundFocusCards; }
internal sealed class KillRoundFocusSlashVictimDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.KillRoundFocusSlashVictim; }
