namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryGetCompletedGiftRecipientSlashRide(ProgramCardTriggerWindowFrame window, out CardUseFrame child)
    {
        child = null!;
        var index = _resolutionStack.FindIndex(item => item.Id == window.Id);
        if (index < 1 || index + 2 >= _resolutionStack.Count ||
            _resolutionStack[index - 1] is not CardUseFrame original ||
            _resolutionStack[index + 1] is not ProgramSkillFrame gift ||
            _resolutionStack[index + 2] is not CardUseFrame followup ||
            window.Continuation != ProgramCardContinuation.CompletedSlash || window.ParentFrameId != original.Id ||
            window.AttackOwnerFrameId != original.Id || original.Step != ResolutionFrameStep.Completed ||
            original.Action is not { Type: CardActionType.Use } originalAction || !IsSlashCard(original.CardKind) ||
            window.Action.ActionId != originalAction.ActionId ||
            gift.CompletedCardGiftDraft is not { RecipientSeat: { } recipient, GiftWasRed: true, RequestTargetSeat: null } draft ||
            draft.CardActionId != originalAction.ActionId || originalAction.ActorSeat != gift.OwnerSeat ||
            !IsValidCompletedGiftTargetSelection(gift) ||
            gift.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } context } ||
            context.ParentCardUseFrameId != original.Id || context.CardActionId != originalAction.ActionId ||
            context.ActorSeat != gift.OwnerSeat || gift.WindowContext.ParentFrameId != window.Id ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var candidate = window.Candidates[window.CandidateIndex];
        if (candidate.OwnerSeat != gift.OwnerSeat || candidate.SkillId != gift.SkillId ||
            candidate.SkillInstanceId != gift.SkillInstanceId || candidate.TriggerId != gift.TriggerId ||
            candidate.GameplayHash != gift.GameplayHash || CreateCardActionProgramContext(window, candidate) != gift.WindowContext ||
            ProgramInstructionResolver.Default.Resolve(gift, _contentRegistry.GetSkill(gift.SkillId).Program!)
                .GetPausedInstruction(gift.InstructionIndex).Effect.Op != SkillProgramEffectOp.OfferCompletedCardGift ||
            followup.CardAttack?.ProgramSkillCardUseFrameId != gift.Id ||
            followup.Action is not { Type: CardActionType.Use } action ||
            action.ParentActionId != originalAction.ActionId || action.ActorSeat != recipient || followup.SourceSeat != recipient ||
            action.ActionId == originalAction.ActionId || action.EffectiveKind != followup.CardKind || !IsSlashCard(followup.CardKind) ||
            action.TargetSeats.Count != 1 || !followup.TargetSeats.SequenceEqual(action.TargetSeats) ||
            action.TargetSeats[0] == gift.OwnerSeat || action.TargetSeats[0] == recipient ||
            action.PhysicalCards.Count == 0 || followup.PhysicalCardIds is not { } physical ||
            !physical.SequenceEqual(action.PhysicalCards.Select(cost => cost.CardId)) ||
            !action.PhysicalCards.Where(cost => originalAction.PhysicalCards.Any(old => old.CardId == cost.CardId)).All(cost =>
                CompleteProgramEventHistory().OfType<CompletedCardGiftedEvent>().Any(given => given.FrameId == gift.Id &&
                    given.CardActionId == originalAction.ActionId && given.OwnerSeat == gift.OwnerSeat && given.RecipientSeat == recipient &&
                    given.IsRed && given.CardIds.Contains(cost.CardId))) ||
            !CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(declared => declared.ResolutionId == followup.Id &&
                declared.SourceSeat == recipient && declared.CardId == followup.CardId && declared.CardKind == followup.CardKind) ||
            !action.PhysicalCards.All(cost => cost.From.OwnerSeat == recipient && _cardMovements.Any(move =>
                move.CardId == cost.CardId && move.From == cost.From && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use)))
            return false;
        child = followup;
        return true;
    }

    // Qinglong finishes the dodged use before its new Slash. That completion can
    // open Zhongyong's first, still-unselected gift prompt. The existing typed
    // Qinglong continuation saves precisely that prompt on the original use.
    // This is not a recipient Slash, and cannot qualify an arbitrary child use.
    private bool IsUnselectedCompletedGiftQinglongRide(ProgramSkillFrame frame)
    {
        if (frame.CompletedCardGiftDraft is not { RecipientSeat: null, GiftWasRed: false, RequestTargetSeat: null } draft ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCompleted, CardUse: { } context } ||
            context.ActorSeat != frame.OwnerSeat || context.CardActionId != draft.CardActionId ||
            frame.SelectedTargetSeats.Count != 0) return false;
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index < 2 || index + 1 >= _resolutionStack.Count ||
            _resolutionStack[index - 1] is not ProgramCardTriggerWindowFrame window ||
            _resolutionStack[index - 2] is not CardUseFrame original ||
            _resolutionStack[index + 1] is not CardUseFrame followup ||
            window.ParentFrameId != original.Id || window.Id != frame.WindowContext.ParentFrameId ||
            window.Continuation != ProgramCardContinuation.CompletedSlash || window.AttackOwnerFrameId != original.Id ||
            original.Id != context.ParentCardUseFrameId || original.Action is not { Type: CardActionType.Use } originalAction ||
            originalAction.ActionId != draft.CardActionId || window.Action.ActionId != originalAction.ActionId ||
            originalAction.ActorSeat != frame.OwnerSeat || original.SourceSeat != frame.OwnerSeat ||
            !IsSlashCard(original.CardKind) || original.Step != ResolutionFrameStep.Completed ||
            original.CardAttack is not { } oldAttack ||
            original.Continuations.QinglongFollowup is not { Active: true, Decision: { } saved } suspension ||
            suspension.NextAttackOwnerId != original.Id || suspension.FangtianOwnerId is not null ||
            window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count) return false;
        var candidate = window.Candidates[window.CandidateIndex];
        if (candidate.OwnerSeat != frame.OwnerSeat || candidate.SkillId != frame.SkillId ||
            candidate.SkillInstanceId != frame.SkillInstanceId || candidate.TriggerId != frame.TriggerId ||
            candidate.GameplayHash != frame.GameplayHash ||
            CreateCardActionProgramContext(window, candidate) != frame.WindowContext ||
            saved.Kind != DecisionKind.ProgramTrigger || !saved.IsPrivate ||
            saved.PlayerSeat != frame.OwnerSeat || saved.SourceSeat != frame.OwnerSeat || saved.TargetSeat != frame.OwnerSeat ||
            saved.SkillPrompt?.SkillId != frame.SkillId ||
            saved.Choices.Count == 0 || saved.Choices.Any(choice =>
                choice.Parameters.GetValueOrDefault("program-action") != "completed-card-gift" ||
                choice.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)) ||
            !AssistedChoicesEqual(saved.Choices, CompletedCardGiftChoices(frame))) return false;
        if (followup.Action is not { Type: CardActionType.Use } action ||
            action.ActionId == originalAction.ActionId || action.ParentActionId != originalAction.ActionId ||
            action.ActorSeat != frame.OwnerSeat || followup.SourceSeat != frame.OwnerSeat ||
            action.EffectiveKind != followup.CardKind || !IsSlashCard(followup.CardKind) ||
            !action.TargetSeats.SequenceEqual([oldAttack.TargetSeat]) ||
            !followup.TargetSeats.SequenceEqual(action.TargetSeats) || action.PhysicalCards.Count == 0 ||
            followup.PhysicalCardIds is not { } physical ||
            !physical.SequenceEqual(action.PhysicalCards.Select(cost => cost.CardId))) return false;
        return CompleteProgramEventHistory().OfType<QinglongCrescentBladeResolvedEvent>().Any(issued =>
            issued.ResolutionId == original.Id && issued.SourceSeat == frame.OwnerSeat &&
            issued.TargetSeat == oldAttack.TargetSeat && issued.Used && issued.EffectiveSlashKind == action.EffectiveKind &&
            issued.SlashCardIds.SequenceEqual(physical)) &&
            CompleteProgramEventHistory().OfType<CardUseDeclaredEvent>().Any(issued =>
                issued.ResolutionId == followup.Id && issued.SourceSeat == frame.OwnerSeat &&
                issued.CardId == followup.CardId && issued.CardKind == followup.CardKind) &&
            action.PhysicalCards.All(cost => _cardMovements.Any(move => move.CardId == cost.CardId &&
                move.From == cost.From && move.To == CardLocation.Processing && move.Reason == CardMoveReasons.Use));
    }
}
