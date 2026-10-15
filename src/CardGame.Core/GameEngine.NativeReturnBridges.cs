namespace CardGame.Core;

public sealed partial class GameEngine
{
    [NativeAttackReturn(NativeAttackReturnPhase.BeforeCleanup, 10)]
    private bool ReturnQinglongNativeAttack(AttackCompletionReceipt completion) =>
        RestoreContinuationAfterQinglongFollowup(completion.ResolutionId);

    [NativeUseReturn(180)]
    private void ReturnDyingNativeUse(NativeUseReturnContext context)
    {
        if (context.DyingResponse is { } response) CompleteDyingCardResponse(response);
    }
}
