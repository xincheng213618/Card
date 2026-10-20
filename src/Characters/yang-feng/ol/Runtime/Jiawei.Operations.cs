using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record JiaweiRevealedEntity(YangFengMaterial Material, bool DamageCard, long RevealSequence);
public sealed record JiaweiMaterialSelectedEvent(long FrameId, int Index, YangFengMaterial Material) : IGameEvent;
public sealed record JiaweiEntityRevealedEvent(long FrameId, int Index, JiaweiRevealedEntity Entity) : IGameEvent;
public sealed record JiaweiDistanceGrantedEvent(long FrameId, int OwnerSeat, int ActualTurn) : IGameEvent;
internal sealed class ResolveJiaweiDiscardRevealDescriptor : YangFengDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveJiaweiDiscardReveal; }
