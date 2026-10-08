namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void ResolveDrawFundedDistinctBasicDodge(DrawFundedDistinctBasicPayment payment)
    {
        var actor = _players[payment.Source.OwnerSeat]; var attack = ActiveCardAttack ?? throw new InvalidOperationException("A paid Dodge lost its incoming Slash.");
        var use = LifecycleCardUse(attack.ResolutionId)!;
        if (!ValidDrawFundedDistinctBasicPayment(payment) || payment.Intent != DrawFundedDistinctBasicIntent.OwnSlashDodge ||
            payment.ParentFrameId != use.Id || payment.ParentActionId != use.Action?.ActionId || payment.Cursor != attack.SuccessfulDodgeResponses ||
            actor.Seat != attack.TargetSeat || ActiveFactionDefense is not null || !IsProgramResponseCardUse(actor, CardKind.Dodge))
            throw new InvalidOperationException("A paid Dodge changed its original own Slash-defense direction.");
        PopResponseWindow(attack.ResolutionId); SetCardUseStep(use.Id, ResolutionFrameStep.ResolvingEffect);
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, use.Action?.ActionId, CardActionType.Response,
            actor.Seat, actor.Seat, null, actor.Seat, attack.SourceSeat, CardKind.Dodge, [], [], [payment.Source], effectiveSuit: Suit.None, effectiveRank: 0, effectiveIsRed: false));
        var receipt = new DrawFundedDistinctBasicUseReceipt(payment, use.Id, action.ActionId);
        ReplaceRuntimeFrame(use.Id, LifecycleCardUse(use.Id)! with { DrawFundedDistinctBasicResponse = receipt }); EmitDrawFundedDistinctBasicIssued(receipt);
        RecordProgramUsedBasicCard(actor.Seat, CardKind.Dodge); RecordActualPlayPhaseUse(action);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0, actor.Seat, attack.SourceSeat, CardKind.Dodge));
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        if (TryBeginCommittedResponseUsePrograms(attack, action, ProgramCardContinuation.Dodge)) return;
        if (TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardResponseAccepted, [attack.SourceSeat], ProgramCardContinuation.Dodge)) return;
        if (HasResponseUseCompletionObserver(action, ProgramCardContinuation.Dodge) || HasCardResponseCompletedObserver(action, ProgramCardContinuation.Dodge)) ContinueAcceptedCardResponse(attack, action, ProgramCardContinuation.Dodge);
        else CompleteSuccessfulDodgeResponse(attack);
    }

    private CardActionContext RequireDrawFundedDistinctBasicResponseAction(CardUseFrame use)
    {
        var receipt = use.DrawFundedDistinctBasicResponse ?? throw new InvalidOperationException("A paid Dodge lost its incoming-use receipt.");
        var p = receipt.Payment;
        var facts = CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Where(e => e.Action.ActionId == receipt.CardActionId).ToArray();
        if (facts is not [var fact] || !IsDrawFundedDistinctBasicIssued(receipt) || receipt.OwnerFrameId != use.Id ||
            p.ParentFrameId != use.Id || p.Intent != DrawFundedDistinctBasicIntent.OwnSlashDodge || p.EffectiveKind != CardKind.Dodge ||
            use.CardAttack is not { } attack || !IsSlashCard(use.CardKind) || attack.TargetSeat != p.Source.OwnerSeat ||
            attack.SuccessfulDodgeResponses != p.Cursor || p.TargetSeat != attack.SourceSeat ||
            fact.Action is not { Type: CardActionType.Response, PhysicalCards.Count: 0, EffectiveKind: CardKind.Dodge, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: false } action ||
            action.ParentActionId != use.Action?.ActionId || action.ParentActionId != p.ParentActionId || action.ActorSeat != p.Source.OwnerSeat ||
            action.ProviderSeat != action.ActorSeat || action.ResponderSeat != action.ActorSeat || action.RequesterSeat is not null || action.OpponentSeat != attack.SourceSeat ||
            action.ConversionChain is not [var source] || source != p.Source || action.TargetSeats.Count != 0)
            throw new InvalidOperationException("A paid Dodge changed its unique accepted action, native owner or once-paid original cursor.");
        return fact.Action;
    }

    private void ClearDrawFundedDistinctBasicDodgeResponse(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { DrawFundedDistinctBasicResponse: not null } use) return;
        var action = RequireDrawFundedDistinctBasicResponseAction(use);
        if (_resolutionStack.LastOrDefault()?.Id != use.Id ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.Action.ActionId == action.ActionId) ||
            use.RecoveryPaidContinuation is not null || use.PendingRecoveryAttempts is { Count: > 0 })
            throw new InvalidOperationException("A paid Dodge cannot clear while its response or recovery child is in flight.");
        EmitDrawFundedDistinctBasicReturned(use.DrawFundedDistinctBasicResponse!);
        ReplaceRuntimeFrame(use.Id, use with { DrawFundedDistinctBasicResponse = null });
    }

    private bool TryAdvanceDrawFundedDistinctBasicAi()
    {
        if (_pendingDecision is not { } prompt || _players[prompt.PlayerSeat].IsHuman) return false;
        var choices = prompt.Choices.Where(IsDrawFundedDistinctBasicAnswer).ToArray(); if (choices.Length == 0) return false;
        var actor = _players[prompt.PlayerSeat]; var use = true;
        if (prompt.Kind == DecisionKind.RespondDodge && ActiveCardAttack is { } attack)
        {
            var decision = _aiBrains[actor.Seat].ChooseDodgeResponse(CreateSnapshot(actor.Seat), attack.SourceSeat, ++_thoughtSequence, attack.IgnoresArmor, true);
            use = decision.Item1; AddThought(decision.Item3);
        }
        else if (prompt.Kind == DecisionKind.RescueDying && ActiveDying is { } dying)
        {
            var decision = _aiBrains[actor.Seat].ChooseProgramDyingRescue(CreateSnapshot(actor.Seat), dying.VictimSeat,
                ProgramConversionName(RequireConversionSource(choices[0])), ++_thoughtSequence);
            use = decision.UseRescue; AddThought(decision.Thought);
        }
        if (!use || DrawFundedDistinctBasicContext(prompt) is not { } need) return false;
        var choice = choices.FirstOrDefault(c => DrawFundedDistinctBasicResponseChoices(prompt, need).Any(current => AssistedChoicesEqual([current], [c])));
        if (choice is null || !Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("output-kind"), out var kind) ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("native-target-seat"), out var target)) return false;
        BeginDrawFundedDistinctBasic(actor, RequireConversionSource(choice), kind,
            (need.Intent is DrawFundedDistinctBasicIntent.OwnSlashDodge or DrawFundedDistinctBasicIntent.Dying) ? [target] : choice.Targets, prompt, need);
        AdvanceRulesAndPublishState(); return true;
    }
    private bool TryResolveDrawFundedDistinctBasicAiDodge() => _pendingDecision?.Kind == DecisionKind.RespondDodge && TryAdvanceDrawFundedDistinctBasicAi();
    private bool TryResolveDrawFundedDistinctBasicAiForcedSlash() => _pendingDecision?.Kind is DecisionKind.RespondSlash or DecisionKind.QinglongCrescentBlade && TryAdvanceDrawFundedDistinctBasicAi();
    private bool TryResolveDrawFundedDistinctBasicAiDying()
    {
        if (ActiveDying is not { } dying || _players[dying.ResponderSeat].IsHuman || !HasDrawFundedDistinctBasicDyingResponse(_players[dying.ResponderSeat])) return false;
        if (_pendingDecision is not null) return _pendingDecision.Kind == DecisionKind.RescueDying && TryAdvanceDrawFundedDistinctBasicAi();
        // Publish the same original need before consulting AI. No fabricated hand card/action is involved.
        _pendingDecision = new PendingDecision(DecisionKind.RescueDying, dying.ResponderSeat, "选择本次真实濒死救援。", [], [], dying.VictimSeat)
        { PromptId = CreatePromptId(), TargetSeat = dying.VictimSeat };
        if (TryAdvanceDrawFundedDistinctBasicAi()) return true;
        ClearPendingDecision(); return false;
    }
}
