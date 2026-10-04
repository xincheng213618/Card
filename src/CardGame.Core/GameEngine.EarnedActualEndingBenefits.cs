namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void IssueEarnedActualEnding(ProgramSkillFrame frame, string skillId, string bindingId)
    {
        frame = GetActiveProgramFrame(frame.Id);
        if (frame.PaidOwnTarget is not { Applied: true } paid || frame.WindowContext?.ActualUseTarget != paid.Use ||
            frame.ChoiceBindings.SingleOrDefault(b => b.Name == "benefit")?.OptionId != "ending")
            throw new InvalidOperationException("An earned Ending benefit requires its exact paid and applied original target.");
        var candidate = CollectProgramTriggerCandidates(_players[frame.OwnerSeat], SkillProgramTriggerWindow.TurnEnding)
            .FirstOrDefault(c => c.SkillId == skillId && c.BindingId == bindingId &&
                GetProgramTrigger(c) is { TurnOwnerScope: SkillProgramTurnOwnerScope.EarnedActualEnding, Optional: false } t &&
                t.Effects is [{ Op: SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo }]);
        if (candidate is null || CompleteProgramEventHistory().OfType<EarnedActualEndingBenefitIssuedEvent>().Any(e => e.Benefit.Id == frame.Id)) return;
        AdvanceEventRulesAndQueueFact(new EarnedActualEndingBenefitIssuedEvent(new(frame.Id, frame.Id, paid.Use.CardUseFrameId,
            paid.Source, paid.GameplayHash, paid.Use.ActualTurnNumber, paid.Use.ActualTurnOwnerSeat,
            new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId), candidate.GameplayHash)));
    }

    private bool EarnedActualEndingWasConsumed(long id) => CompleteProgramEventHistory()
        .OfType<EarnedActualEndingBenefitConsumedEvent>().Any(e => e.Id == id);
    private bool EarnedActualEndingIssued(EarnedActualEndingBenefit benefit)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var cost = history.OfType<PaidOwnTargetHpEvent>().Where(e => e.ProgramFrameId == benefit.ProducerProgramFrameId).ToArray();
        if (benefit.Id != benefit.ProducerProgramFrameId || benefit.Id < 1 || !IsValidPlayerSeat(benefit.PaidSource.OwnerSeat) ||
            benefit.PaidSource.OwnerSeat != benefit.BenefitSource.OwnerSeat || cost.Length != 1 ||
            cost[0].Receipt.Source != benefit.PaidSource || cost[0].Receipt.GameplayHash != benefit.PaidGameplayHash ||
            cost[0].Receipt.Use.CardUseFrameId != benefit.CardUseFrameId || cost[0].Receipt.Use.TargetSeat != benefit.PaidSource.OwnerSeat ||
            cost[0].Receipt.Use.ActualTurnNumber != benefit.ActualTurnNumber || cost[0].Receipt.Use.ActualTurnOwnerSeat != benefit.ActualTurnOwnerSeat ||
            cost[0].Receipt.ActualLost != 1 || cost[0].Receipt.HpAfter != cost[0].Receipt.HpBefore - 1 ||
            history.OfType<EarnedActualEndingBenefitIssuedEvent>().Count(e => e.Benefit.Id == benefit.Id) != 1 ||
            !history.OfType<EarnedActualEndingBenefitIssuedEvent>().Any(e => e.Benefit == benefit)) return false;
        return history.OfType<PaidOwnTargetAppliedEvent>().Count(e => e.ProgramFrameId == benefit.ProducerProgramFrameId &&
            e.CardUseFrameId == benefit.CardUseFrameId && e.Source == benefit.PaidSource && e.GameplayHash == benefit.PaidGameplayHash &&
            e.ActionId == cost[0].Receipt.Use.ActionId && e.ActorSeat == cost[0].Receipt.Use.ActorSeat &&
            e.TargetSeat == benefit.PaidSource.OwnerSeat && e.EffectiveKind == cost[0].Receipt.Use.EffectiveKind) == 1;
    }

    private void AppendEarnedActualEndingItems(List<TurnEndingBoundaryItem> items)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ScheduleEarnedActualEndingBenefit)) return;
        foreach (var benefit in CompleteProgramEventHistory().OfType<EarnedActualEndingBenefitIssuedEvent>().Select(e => e.Benefit)
            .Where(b => b.ActualTurnNumber == _turnNumber && b.ActualTurnOwnerSeat == _currentSeat && !EarnedActualEndingWasConsumed(b.Id)).ToArray())
        {
            var owner = benefit.BenefitSource.OwnerSeat;
            var candidate = IsValidPlayerSeat(owner) && _players[owner].IsAlive
                ? CollectProgramTriggerCandidates(_players[owner], SkillProgramTriggerWindow.TurnEnding).SingleOrDefault(c =>
                    c.SkillId == benefit.BenefitSource.SkillId && c.BindingId == benefit.BenefitSource.BindingId &&
                    c.SkillInstanceId == benefit.BenefitSource.SkillInstanceId && c.GameplayHash == benefit.BenefitGameplayHash) : null;
            if (candidate is null || !EarnedActualEndingIssued(benefit)) { ConsumeEarnedActualEnding(benefit, false); continue; }
            var trigger = GetProgramTrigger(candidate);
            if (trigger.TurnOwnerScope != SkillProgramTurnOwnerScope.EarnedActualEnding || trigger.Optional ||
                trigger.Effects is not [{ Op: SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo }])
            { ConsumeEarnedActualEnding(benefit, false); continue; }
            // Each promise is a distinct occurrence, even for the same skill instance.
            candidate = candidate with { OccurrenceIndex = items.Count };
            items.Add(new(TurnEndingBoundaryItemKind.Program, candidate.Priority,
                $"earned-ending:{benefit.Id}", candidate, CaptureProgramTriggerFacts(_players[owner])) { EarnedBenefit = benefit });
        }
    }
    private void ConsumeEarnedActualEnding(EarnedActualEndingBenefit benefit, bool applied)
    {
        if (!EarnedActualEndingWasConsumed(benefit.Id)) AdvanceEventRulesAndQueueFact(new EarnedActualEndingBenefitConsumedEvent(
            benefit.Id, benefit.ActualTurnNumber, benefit.ActualTurnOwnerSeat, applied));
    }
    private void ConsumeEarnedActualEndingAtBegin(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.EarnedBenefit is not { } benefit) return;
        if (!MatchesEarnedActualEnding(candidate, context, false)) throw new InvalidOperationException("The Ending promise changed before activation.");
        ConsumeEarnedActualEnding(benefit, true);
    }
    private void ConsumeSkippedEarnedActualEnding(TurnEndingBoundaryFrame frame)
    {
        if (frame.Items[frame.ItemIndex].EarnedBenefit is { } benefit) ConsumeEarnedActualEnding(benefit, false);
    }
    private bool MatchesEarnedActualEnding(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context, bool begun)
    {
        if (context.EarnedBenefit is not { } benefit || context.Window != SkillProgramTriggerWindow.TurnEnding ||
            benefit.ActualTurnNumber != _turnNumber || benefit.ActualTurnOwnerSeat != _currentSeat ||
            benefit.BenefitSource != new CardConversionSource(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId) ||
            benefit.BenefitGameplayHash != candidate.GameplayHash || !EarnedActualEndingIssued(benefit) ||
            EarnedActualEndingWasConsumed(benefit.Id) != begun ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().LastOrDefault() is not { } ending || ending.Id != context.ParentFrameId ||
            ending.TurnNumber != benefit.ActualTurnNumber || ending.OwnerSeat != benefit.ActualTurnOwnerSeat ||
            ending.ItemIndex < 0 || ending.ItemIndex >= ending.Items.Count || ending.Items[ending.ItemIndex].Candidate != candidate ||
            ending.Items[ending.ItemIndex].EarnedBenefit != benefit) return false;
        return !begun || CompleteProgramEventHistory().OfType<EarnedActualEndingBenefitConsumedEvent>().Any(e => e.Id == benefit.Id &&
            e.Applied && e.ActualTurnNumber == benefit.ActualTurnNumber && e.ActualTurnOwnerSeat == benefit.ActualTurnOwnerSeat);
    }
    private bool MatchesLostHpOwnedGiftEnding(ProgramSkillFrame frame)
    {
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.TurnEnding } context ||
            _resolutionStack.OfType<TurnEndingBoundaryFrame>().LastOrDefault() is not { } ending || ending.Id != context.ParentFrameId ||
            ending.TurnNumber != _turnNumber || ending.OwnerSeat != _currentSeat || ending.ItemIndex < 0 || ending.ItemIndex >= ending.Items.Count ||
            ending.Items[ending.ItemIndex].Candidate is not { } candidate || !MountObserverCandidateMatches(frame, candidate)) return false;
        var trigger = GetProgramTrigger(candidate);
        return trigger.Effects is [{ Op: SkillProgramEffectOp.DrawLostHpThenOfferOwnedCardsUpTo }] &&
            (trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.Own && context.EarnedBenefit is null && frame.OwnerSeat == ending.OwnerSeat ||
             trigger.TurnOwnerScope == SkillProgramTurnOwnerScope.EarnedActualEnding && MatchesEarnedActualEnding(candidate, context, true));
    }
}
