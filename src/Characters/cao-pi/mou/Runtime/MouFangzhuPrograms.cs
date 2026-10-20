using System.Text.Json.Serialization;

namespace CardGame.Core;
public enum MouCaoPiControlKind { BasicHandOnly, CannotRespond, SkillsInvalid, TrickHandOnly, EquipmentHandOnly }
public sealed record MouCaoPiControl(long FrameId, int SourceSeat, int TargetSeat, int GrantedTurn, MouCaoPiControlKind Kind);
public sealed record MouCaoPiControlGrantedEvent(MouCaoPiControl Control) : IGameEvent;
public sealed record MouCaoPiControlExpiredEvent(long FrameId, int TargetSeat, int ActualTurn) : IGameEvent;
internal sealed class MouCaoPiFangzhuDescriptor() : MouCaoPiDescriptor(SkillProgramEffectOp.MouCaoPiFangzhu) { }
