using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record WenyiNativeOrigin(long ProgramFrameId, CardConversionSource Source, string GameplayHash,
    int ActorSeat, int ProviderSeat, int TargetSeat, CardKind Kind, long UseId, long ActionId, CardActionContext OriginalAction);
public sealed record WenyiPaidEvent(long FrameId, int OwnerSeat, int TargetSeat, long HealthWindowId, string Branch) : IGameEvent;
public sealed record WenyiEquipmentGivenEvent(long FrameId, int CardId, int RecipientSeat, long Sequence) : IGameEvent;
public sealed record WenyiNativeIssuedEvent(long FrameId, long UseId, long ActionId, int ActorSeat, int ProviderSeat, int TargetSeat, CardKind Kind) : IGameEvent;
public sealed record WenyiNativeReturnedEvent(long FrameId, long UseId, long ActionId) : IGameEvent;
public sealed record WenyiRestorationIssuedEvent(long FrameId, int TargetSeat, bool FaceState) : IGameEvent;
internal sealed class ResolveWenyiHealthOneAidDescriptor() : HuanShuJunDescriptor(SkillProgramEffectOp.ResolveWenyiHealthOneAid){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
