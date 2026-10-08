namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ValidateOwnedHandRankSumGiftActivation(ProgramSkillFrame frame)
    {
        var program = _contentRegistry.GetSkill(frame.SkillId).Program!;
        var plan = ProgramInstructionResolver.Default.Resolve(frame, program);
        var activation = plan.Activation;
        var move = plan.GetPausedInstruction(frame.InstructionIndex).Effect;
        if (frame.TriggerId is not null || frame.WindowContext is not null ||
            program.GameplayHash != frame.GameplayHash || activation is null ||
            activation.Id != frame.ActivationId || activation.MinCards != 0 || activation.MaxCards != 0 ||
            activation.MinTargets != 1 || activation.MaxTargets != 1 ||
            activation.TargetKind != SkillProgramTargetKind.OtherLiving || frame.SelectedCardIds.Count != 0 ||
            frame.SelectedTargetSeats is not [var recipient] || recipient == frame.OwnerSeat ||
            frame.InstructionIndex != 2 || plan.Instructions[0] is not
                { Op: SkillProgramEffectOp.SelectOwnedHandRankSum, ResultBind: { } bind, ExactRankSum: { } rankSum } ||
            move is not { Op: SkillProgramEffectOp.MoveBoundCards, Target: SkillProgramEffectTarget.Owner,
                Destination: SkillProgramCardDestination.SelectedTargetHand, AwaitMovementTriggers: true,
                Condition.Kind: SkillProgramConditionKind.Always, ExceptBind: null } || move.SourceBind != bind ||
            CompleteProgramEventHistory().OfType<ProgramSkillStartedEvent>().Count(fact =>
                fact.FrameId == frame.Id && fact.OwnerSeat == frame.OwnerSeat && fact.SkillId == frame.SkillId &&
                fact.ActivationId == frame.ActivationId) != 1)
            throw new InvalidOperationException("An awaited rank-sum gift lost its exact original activation, target or paused movement.");
        var source = GetProgramCardSet(frame, bind);
        if (source.CardIds.Count == 0 || source.CardIds.Count > rankSum ||
            source.Visibility != SkillProgramCardSetVisibility.Private ||
            source.SelectionActorSeat is { } actor && actor != frame.OwnerSeat ||
            source.CardIds.Distinct().Count() != source.CardIds.Count ||
            source.SourceLocations.Count != source.CardIds.Count ||
            source.SourceLocations.Any(location => location != CardLocation.Hand(frame.OwnerSeat)) ||
            source.CardIds.Sum(id => GetAdvancedCard(id).Rank) != rankSum)
            throw new InvalidOperationException("An awaited rank-sum gift lost its private frozen owner-hand cost.");
    }
}
