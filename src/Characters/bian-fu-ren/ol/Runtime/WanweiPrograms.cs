namespace CardGame.Core;

internal sealed class WanweiObtainMatchingLostCardDescriptor : BianFuRenMovementDescriptor
{
    public override SkillProgramEffectOp Op => SkillProgramEffectOp.WanweiObtainMatchingLostCard;
}

public sealed record WanweiAcquisitionIssuedEvent(long ProgramFrameId, long OriginalBatchId,
    int OriginalMovementSequence, int OwnerSeat, int OtherActorSeat, bool MatchedName,
    int MovementBefore, int MovementAfter, int ActualCount) : IGameEvent;
