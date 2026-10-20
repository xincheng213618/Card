using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouDiaoChanMobileBiyueEvent(long FrameId, int TurnNumber, int DamagedRoles, int RequestedDraw) : IGameEvent;
internal sealed class MouDiaoChanBiyueDescriptor : MouDiaoChanMobileDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouDiaoChanBiyue; }
