using System.Text.Json.Serialization;
namespace CardGame.Core;
public sealed record MouHanDangGongqiGrantedEvent(long FrameId, CardConversionSource Source, string GameplayHash, ActualDiscardRecoveryPhaseKey Phase, int CardId, Suit Suit, long PaymentSequence) : IGameEvent;
public sealed record MouHanDangUseRestrictedEvent(long ActionId, int ActorSeat, long GrantFrameId) : IGameEvent;
internal sealed class ResolveMouHanDangGongqiDescriptor : MouHanDangDescriptor
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase | ProgramTrackedFact.NativeLogicalDiscard);

    public override SkillProgramEffectOp Op => SkillProgramEffectOp.ResolveMouHanDangGongqi;
    public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e) => [new RequireTriggerWindow(SkillProgramTriggerWindow.PlayPhaseStarting)];
}
