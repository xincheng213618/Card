using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class ClaimLiubingUndamagingSlashDescriptor() : WuJingDescriptor(SkillProgramEffectOp.ClaimLiubingUndamagingSlash)
{
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.ActualDiscardRecoveryPhase);
}
public sealed record WuJingLiubingAppearance(long UseId, long ActionId, CardConversionSource Source, int ActualTurn, int TurnOwner, int ActorSeat, int CardId, CardKind Kind, Suit Suit);
public sealed record WuJingLiubingAppearanceIssuedEvent(WuJingLiubingAppearance Receipt) : IGameEvent;
public sealed record WuJingLiubingSettledEvent(long UseId, long ActionId, long BatchId, long PlayToken, int FinalActor, int CardId, long MovementSequence) : IGameEvent;
