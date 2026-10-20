using System.Text.Json.Serialization;

namespace CardGame.Core;
public sealed record MiaoxinBenefitIssuedEvent(long FrameId, int OwnerSeat, long DamageWindowId, int Occurrence, int DrawCount, bool RefreshWenyi) : IGameEvent;
public sealed record MiaoxinWenyiRefreshedEvent(long FrameId, int OwnerSeat) : IGameEvent;
internal sealed class ResolveMiaoxinDamageDrawAndRefreshDescriptor() : HuanShuJunDescriptor(SkillProgramEffectOp.ResolveMiaoxinDamageDrawAndRefresh){
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => context.State.CanContinueAfterOwnerDeath(context.Frame);
}
