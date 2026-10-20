namespace CardGame.Core;

internal sealed class YuejianRevealAndClaimUsedCardDescriptor : BianFuRenMovementDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.YuejianRevealAndClaimUsedCard;
}

public sealed record YuejianAcquisitionIssuedEvent(long ProgramFrameId, long OriginalBatchId,
    long NativeUseId, long ActionId, int OwnerSeat, int RevealedHandCount, bool DifferentSuits,
    int MovementBefore, int MovementAfter, int ActualCount) : IGameEvent;
