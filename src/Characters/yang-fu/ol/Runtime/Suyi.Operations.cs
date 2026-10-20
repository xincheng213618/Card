using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record SuyiNativeIssuedEvent(YangFuNativeOrigin Origin) : IGameEvent;
public sealed record SuyiNativeReturnedEvent(YangFuNativeOrigin Origin) : IGameEvent;
public sealed record SuyiNoEquipmentEvent(long FrameId, int TargetSeat) : IGameEvent;
public sealed record SuyiSlashQuotaPaidEvent(long FrameId, long UseId, int ActorSeat) : IGameEvent;
internal sealed class ResolveSuyiEquipmentThenSlashDescriptor : YangFuDescriptor { public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveSuyiEquipmentThenSlash; }
