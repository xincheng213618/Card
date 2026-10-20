using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record MouCaoPiInheritedEvent(long FrameId, int OwnerSeat, int DeadSeat, string GeneralId) : IGameEvent;
internal sealed class MouCaoPiXingshangDescriptor() : MouCaoPiDescriptor(SkillProgramEffectOp.MouCaoPiXingshang) { }
