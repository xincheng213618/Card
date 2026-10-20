namespace CardGame.Core;

// These facts are scalar. Issued policies survive skill loss and expire at their
// actual phase/turn/round boundary; unfinished choices remain on ProgramSkillFrame.
public sealed record GuanPhaseFocusEvent(long FrameId, int OwnerSeat, string SkillId, int TargetSeat, int TurnNumber, int PhaseSerial) : IGameEvent;
public sealed record GuanFocusedSlashEvent(long ActionId, int OwnerSeat, int TargetSeat, int TurnNumber, int PhaseSerial) : IGameEvent;
internal sealed class ArmPhaseSlashFocusDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ArmPhaseSlashFocus; }
internal sealed class RecordPhaseFocusedSlashDescriptor : GuanYuVariantDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.RecordPhaseFocusedSlash; }
