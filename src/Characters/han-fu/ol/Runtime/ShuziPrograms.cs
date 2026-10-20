using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record ShuziDamageIssuedEvent(long FrameId, int SourceSeat, int TargetSeat, int Amount) : IGameEvent;
internal sealed class ResolveShuziExchangeDescriptor : HanFuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveShuziExchange; }
