namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsArmorIneffectiveForTurn(CharacterState target) => _turnCardUseEffects.TargetRestrictions.Any(restriction =>
        restriction.TurnNumber == _turnNumber && restriction.Restriction == SkillProgramCardTargetRestriction.ArmorIneffectiveForTurn && restriction.TargetSeat == target.Seat);
    private bool TryResolveExtendedAiResponse(CharacterState owner, Card card, CardKind kind)
    {
        if (IsNativeResponseCard(card, kind) || GetProgramViewAsConversions(owner, card, kind, true).Count > 0) return false;
        var selection = GetProgramMultiCardViewAsSelections(owner, kind, true).FirstOrDefault(item => item.Cards.Any(cost => cost.Id == card.Id));
        if (selection is null) return false;
        ResolveExtendedViewAsResponse(owner, selection);
        return true;
    }
    private IReadOnlyList<PromptChoice> ExtendedViewAsResponseChoices(CharacterState owner, CardKind kind)
    {
        if (kind == CardKind.Peach && _pendingDying is { } dying && !CanUsePeachToRescue(owner.Seat, dying.VictimSeat)) return [];
        return GetProgramMultiCardViewAsSelections(owner, kind, true).Select(selection =>
        {
            var parameters = new Dictionary<string, string> { ["response"] = "extended-view-as", ["output-kind"] = kind.ToString() };
            AddConversionParameters(parameters, selection.Source);
            return new PromptChoice(new ChoiceId("extended-view-as." + kind + "." + selection.Source.SkillId + "." +
                string.Join('-', selection.Cards.Select(card => card.Id))),
                $"发动【{ProgramConversionName(selection.Source)}】，将 {selection.Cards.Count} 张牌当【{CardCatalog.Get(kind).DisplayName}】。",
                selection.Cards.Select(card => card.Id).ToArray(), [], parameters);
        }).ToArray();
    }

    private EngineRunResult ResolveExtendedViewAsResponse(PromptChoice choice)
    {
        var owner = _players[_pendingDecision!.PlayerSeat];
        var kind = Enum.Parse<CardKind>(choice.Parameters["output-kind"]);
        var source = RequireConversionSource(choice);
        var selection = FindProgramMultiCardViewAsSelection(owner, choice.Cards, kind, true, source)
            ?? throw new InvalidOperationException("The extended conversion lost its physical costs.");
        ResolveExtendedViewAsResponse(owner, selection);
        PublishState();
        return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolveExtendedViewAsResponse(CharacterState owner, ProgramMultiCardViewAsSelection selection)
    {
        var kind = selection.OutputKind;
        if (kind == CardKind.Peach)
        {
            var dying = _pendingDying ?? throw new InvalidOperationException("Missing dying response.");
            if (dying.ResponderSeat != owner.Seat || !CanUsePeachToRescue(owner.Seat, dying.VictimSeat))
                throw new InvalidOperationException("The rescue responder is no longer legal.");
            ClearPendingDecision();
            var rule = GetEnabledSkillProgram(owner, selection.Source.SkillId).ViewAs.Single(item => item.Id == selection.Source.BindingId);
            var victim = _players[dying.VictimSeat];
            var recoveryPolicies = owner.Seat != victim.Seat
                ? CardPolicies(victim, SkillProgramCardPolicyKind.RescueRecoveryBonus, CardKind.Peach)
                    .Where(item => string.Equals(GetEffectiveFactionId(owner), item.Policy.FactionId, StringComparison.Ordinal)).ToArray()
                : [];
            ResolveRecoveryCard(owner, _players[dying.VictimSeat], selection.Cards[0], "桃", CardKind.Peach,
                1 + rule.RecoveryBonus + recoveryPolicies.Sum(item => item.Policy.Value),
                recoveryPolicySources: recoveryPolicies.Select(item => (item.Source.SkillId, item.Policy.Id)).ToArray(), conversionSource: selection.Source,
                dyingResponse: new(dying.FrameId, owner.Seat, true, selection.Cards[0].Id, false, null),
                physicalCards: selection.Cards);
            return;
        }
        if (kind == CardKind.Nullification)
        {
            var pending = _pendingNullification ?? throw new InvalidOperationException("Missing counterspell window.");
            ClearPendingDecision();
            var action = MoveProgramMultiCardResponse(owner, selection, pending.ResolutionId, pending.SourceSeat);
            FinishProgramMultiCardResponse(selection);
            pending.EffectNullified = !pending.EffectNullified;
            pending.ChainDepth++;
            pending.CandidateSeats = BuildNullificationCandidateSeats(owner.Seat);
            pending.CandidateIndex = 0;
            UpdateNullificationWindowFrame(pending, ResolutionFrameStep.AwaitingResponse);
            QueueGameEvent(new NullificationRespondedEvent(pending.ResolutionId, pending.EffectCard.Id,
                pending.EffectiveCardKind, owner.Seat, selection.Cards[0].Id, pending.EffectNullified, pending.ChainDepth));
            if (!TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardResponseAccepted, [],
                ProgramCardContinuation.NullificationResponse)) ContinueNullificationWindow(pending);
            return;
        }
        var attack = _pendingAttack ?? throw new InvalidOperationException("Missing attack response.");
        if (_resolutionStack.LastOrDefault() is ResponseWindowFrame) PopResponseWindow(attack.ResolutionId);
        ClearPendingDecision();
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        var responseAction = MoveProgramMultiCardResponse(owner, selection, attack.ResolutionId, attack.SourceSeat);
        var continuation = _pendingGroupCard is { } ? ProgramCardContinuation.GroupResponse : ProgramCardContinuation.Dodge;
        if (!TryBeginProgramCardWindow(attack, responseAction, SkillProgramTriggerWindow.CardResponseAccepted, [], continuation))
            ContinueAcceptedCardResponse(attack, responseAction, continuation);
    }

    private int ProgramConversionDamageBonus(AttackResolution attack)
    {
        var action = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(frame => frame.Id == attack.ResolutionId)?.Action;
        if (action is null || attack.IsChainPropagation) return 0;
        return action.ConversionChain.Sum(source => _contentRegistry.Skills.GetValueOrDefault(source.SkillId)?.Program?.ViewAs
            .SingleOrDefault(rule => rule.Id == source.BindingId)?.DamageBonus ?? 0);
    }
}
