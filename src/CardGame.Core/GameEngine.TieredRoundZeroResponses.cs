namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<PromptChoice> TieredRoundZeroResponseChoices(CharacterState actor, CardKind kind, bool dyingUse = false)
    {
        var dying = ActiveDying;
        if (dyingUse)
        {
            if (dying is null || dying.ResponderSeat != actor.Seat || !_players[dying.VictimSeat].IsAlive || _players[dying.VictimSeat].Hp > 0 ||
                kind == CardKind.Peach && !CanUsePeachToRescue(actor.Seat, dying.VictimSeat) ||
                kind == CardKind.Alcohol && (dying.VictimSeat != actor.Seat || HasSelfCardTargetProhibition(actor.Seat)) ||
                HasBeneficiarySuitShield(actor.Seat, dying.VictimSeat, Suit.None)) return [];
        }
        else if (kind == CardKind.Dodge && !IsProgramResponseCardUse(actor, kind)) return [];
        else if (kind == CardKind.Nullification && (ActiveNullificationWindow is not { } n ||
            n.CandidateIndex >= n.CandidateSeats.Count || n.CandidateSeats[n.CandidateIndex] != actor.Seat ||
            n.ChainDepth == 0 && IsNearbyTargetResponseProhibited(n.SourceSeat, actor.Seat, n.EffectCardKind, n.TargetSeats))) return [];
        var choices = new List<PromptChoice>();
        foreach (var (source, _, _) in TieredRoundZeroRules(actor, kind, responseUse: true, dyingUse: dyingUse))
        {
            var parameters = new Dictionary<string, string> { ["response"] = "tiered-round-zero-use",
                ["output-kind"] = kind.ToString(), ["dying-use"] = dyingUse ? "true" : "false" };
            AddConversionParameters(parameters, source);
            choices.Add(new(new ChoiceId($"tiered-zero.{kind}.{source.SkillId}.{source.BindingId}.{source.SkillInstanceId}"),
                $"发动【{ProgramConversionName(source)}】，视为使用【{CardCatalog.Get(kind).DisplayName}】。", [], [], parameters));
        }
        return Array.AsReadOnly(choices.ToArray());
    }

    private bool HasTieredRoundZeroDyingResponse(CharacterState responder) =>
        TieredRoundZeroResponseChoices(responder, CardKind.Peach, true).Count > 0 ||
        TieredRoundZeroResponseChoices(responder, CardKind.Alcohol, true).Count > 0;

    private void ResolveTieredRoundZeroResponse(PromptChoice choice, bool advance)
    {
        if (_pendingDecision is not { } prompt || !prompt.Choices.Any(c => c.Id == choice.Id && AssistedChoicesEqual([c], [choice])) ||
            choice.Cards.Count != 0 || choice.Targets.Count != 0 || choice.Parameters.GetValueOrDefault("response") != "tiered-round-zero-use" ||
            !Enum.TryParse<CardKind>(choice.Parameters.GetValueOrDefault("output-kind"), out var kind))
            throw new InvalidOperationException("A zero-material response must be the current published exact choice.");
        var actor = _players[prompt.PlayerSeat]; var source = RequireConversionSource(choice);
        var dyingUse = choice.Parameters.GetValueOrDefault("dying-use") == "true";
        if (!TieredRoundZeroResponseChoices(actor, kind, dyingUse).Any(c => c.Id == choice.Id && AssistedChoicesEqual([c], [choice])) ||
            dyingUse && prompt.Kind != DecisionKind.RescueDying || !dyingUse &&
                prompt.Kind != (kind == CardKind.Nullification ? DecisionKind.Nullification : DecisionKind.RespondDodge))
            throw new InvalidOperationException("A zero-material response lost its use-qualified producer and allowance before acceptance.");
        if (dyingUse)
        {
            var dying = ActiveDying!; var card = new Card(0, kind, Suit.None, 0); var victim = _players[dying.VictimSeat];
            ClearPendingDecision(); SetDyingFrameStep(dying.FrameId, ResolutionFrameStep.ResolvingEffect);
            var policies = kind == CardKind.Peach && actor.Seat != victim.Seat
                ? CardPolicies(victim, SkillProgramCardPolicyKind.RescueRecoveryBonus, CardKind.Peach)
                    .Where(p => string.Equals(GetEffectiveFactionId(actor), p.Policy.FactionId, StringComparison.Ordinal)).ToArray()
                : [];
            ResolveRecoveryCard(actor, victim, card, kind == CardKind.Peach ? "桃" : "酒", kind,
                1 + ViewAsRule(source)!.RecoveryBonus + policies.Sum(p => p.Policy.Value),
                recoveryPolicySources: policies.Select(p => (p.Source.SkillId, p.Policy.Id)).ToArray(), conversionSource: source,
                dyingResponse: new(dying.FrameId, actor.Seat, kind == CardKind.Peach, kind == CardKind.Peach ? 0 : null,
                    kind == CardKind.Alcohol, kind == CardKind.Alcohol ? 0 : null), physicalCards: []);
        }
        else if (kind == CardKind.Nullification) ResolveTieredRoundZeroCounterspell(actor, source);
        else ResolveTieredRoundZeroDodge(actor, source);
        AdvanceRulesAndPublishState(); if (advance) AdvanceToHumanBoundary();
    }

    private TieredRoundConversionUseReceipt IssueTieredRoundResponse(ResolutionFrame owner, CardActionContext action, CardConversionSource source)
    {
        var rule = ViewAsRule(source)!; var policy = rule.TieredRoundConversion!;
        var tier = TieredRoundTier(source.OwnerSeat, policy.StateId);
        if (action.Type != CardActionType.Response || action.PhysicalCards.Count != rule.InputCount || rule.InputCount != (tier == 2 ? 0 : 1) ||
            action.ActorSeat != source.OwnerSeat || action.ProviderSeat != source.OwnerSeat || action.ResponderSeat != source.OwnerSeat ||
            action.RequesterSeat is not null || action.ConversionChain is not [var actual] || actual != source ||
            tier is < 0 or > 2 || action.EffectiveKind != rule.OutputKind)
            throw new InvalidOperationException("A tiered response changed its accepted source/actor/provider/material tuple.");
        var receipt = new TieredRoundConversionUseReceipt(source, _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash,
            policy.StateId, policy.UsageId, tier, _roundNumber, _turnNumber, _turnProgression.OwnerSeat, _cardUseDebitPhaseInstanceId,
            action.ActionId, owner.Id, action.ActorSeat, action.EffectiveKind, action.PhysicalCards.Count);
        ReplaceRuntimeFrame(owner.Id, owner switch
        {
            NullificationWindowFrame n => n with { TieredRoundResponseUse = receipt },
            CardUseFrame use => use with { TieredRoundResponseUse = receipt },
            _ => throw new InvalidOperationException("A zero response has no original typed response owner.")
        });
        AdvanceEventRulesAndQueueFact(new TieredRoundConversionUseIssuedEvent(receipt)); return receipt;
    }

    private void ResolveTieredRoundZeroCounterspell(CharacterState actor, CardConversionSource source)
    {
        var pending = ActiveNullificationWindow!; var parent = LifecycleCardUse(pending.ParentFrameId)!;
        var policy = FreezeUnrespondableCounterspellSource(actor, []);
        ConsumeTieredRoundConversion(source); ClearPendingDecision();
        pending = ReplaceNullificationWindowFrame(pending with { EffectNullified = !pending.EffectNullified,
            ChainDepth = pending.ChainDepth + 1, CandidateSeats = BuildNullificationCandidateSeats(actor.Seat), CandidateIndex = 0,
            Step = ResolutionFrameStep.AwaitingResponse });
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, parent.Action?.ActionId,
            CardActionType.Response, actor.Seat, actor.Seat, null, actor.Seat, pending.SourceSeat, CardKind.Nullification,
            [], [], [source], effectiveSuit: Suit.None, effectiveRank: 0));
        IssueTieredRoundResponse(pending, action, source);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0, actor.Seat, pending.SourceSeat, CardKind.Nullification));
        AdvanceEventRulesAndQueueFact(new NullificationRespondedEvent(pending.ParentFrameId, pending.EffectCardId, pending.EffectCardKind,
            actor.Seat, 0, pending.EffectNullified, pending.ChainDepth));
        RecordActualPlayPhaseUse(action); AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        pending = (NullificationWindowFrame)_resolutionStack.Single(f => f.Id == pending.Id);
        if (TryBeginPolicyCounterspellPayment(pending, action, policy, [])) return;
        if (TryBeginCommittedResponseUsePrograms(null, action, ProgramCardContinuation.NullificationResponse)) return;
        if (TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardResponseAccepted, [], ProgramCardContinuation.NullificationResponse)) return;
        ContinueNullificationAfterResponseUse(action);
    }

    private void ResolveTieredRoundZeroDodge(CharacterState actor, CardConversionSource source)
    {
        var attack = ActiveCardAttack ?? throw new InvalidOperationException("A zero Dodge use has no original Slash.");
        if (!IsProgramResponseCardUse(actor, CardKind.Dodge) || ActiveGroupCard is not null || ActiveFactionDefense is not null)
            throw new InvalidOperationException("A zero Dodge is a real own Slash-defense use, never a faction or response-only group card.");
        var use = LifecycleCardUse(attack.ResolutionId)!;
        ConsumeTieredRoundConversion(source); PopResponseWindow(attack.ResolutionId); ClearPendingDecision();
        SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, use.Action?.ActionId, CardActionType.Response,
            actor.Seat, actor.Seat, null, actor.Seat, attack.SourceSeat, CardKind.Dodge, [], [], [source], effectiveSuit: Suit.None, effectiveRank: 0));
        IssueTieredRoundResponse(LifecycleCardUse(use.Id)!, action, source);
        RecordProgramUsedBasicCard(actor.Seat, CardKind.Dodge); RecordActualPlayPhaseUse(action);
        AdvanceEventRulesAndQueueFact(new CardRespondedEvent(0, actor.Seat, attack.SourceSeat, CardKind.Dodge));
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action));
        if (TryBeginCommittedResponseUsePrograms(attack, action, ProgramCardContinuation.Dodge)) return;
        if (TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardResponseAccepted, [attack.SourceSeat], ProgramCardContinuation.Dodge)) return;
        if (HasResponseUseCompletionObserver(action, ProgramCardContinuation.Dodge) || HasCardResponseCompletedObserver(action, ProgramCardContinuation.Dodge)) ContinueAcceptedCardResponse(attack, action, ProgramCardContinuation.Dodge);
        else CompleteSuccessfulDodgeResponse(attack);
    }

    private void TryIssueTieredRoundResponse(ResolutionFrame owner, CardActionContext action)
    {
        if (action.ConversionChain.SingleOrDefault(s => ViewAsRule(s)?.TieredRoundConversion is not null) is { } source)
            IssueTieredRoundResponse(owner, action, source);
    }

    private bool IsIssuedTieredRoundZeroResponse(ResolutionFrame owner, CardActionContext action) => IsIssuedTieredRoundResponse(owner, action, true);

    private bool IsIssuedTieredRoundResponse(ResolutionFrame owner, CardActionContext action, bool zeroOnly = false)
    {
        var r = owner switch { NullificationWindowFrame n => n.TieredRoundResponseUse, CardUseFrame u => u.TieredRoundResponseUse, _ => null };
        if (r is null || r.OwnerFrameId != owner.Id || r.CardActionId != action.ActionId || r.FrozenTier is < 0 or > 2 ||
            r.MaterialCount != (r.FrozenTier == 2 ? 0 : 1) || zeroOnly && r.FrozenTier != 2 ||
            action.Type != CardActionType.Response || action.PhysicalCards.Count != r.MaterialCount || action.ActorSeat != r.ActorSeat || action.ProviderSeat != r.ActorSeat ||
            action.ResponderSeat != r.ActorSeat || action.RequesterSeat is not null || action.EffectiveKind != r.EffectiveKind ||
            action.ConversionChain is not [var source] || source != r.Source || source.OwnerSeat != r.ActorSeat ||
            _contentRegistry.GetSkill(source.SkillId).Program is not { } definition || definition.GameplayHash != r.GameplayHash ||
            definition.ViewAs.SingleOrDefault(v => v.Id == source.BindingId) is not { UseOnly: true, TieredRoundConversion: { } policy } rule || rule.InputCount != r.MaterialCount ||
            policy.StateId != r.StateId || policy.UsageId != r.UsageId || rule.OutputKind != r.EffectiveKind ||
            _skillRuntimeState.GetUsage(r.ActorSeat, source.SkillId, TieredRoundUsage(policy), r.FrozenTier == 0 ? SkillUsageScope.Phase : SkillUsageScope.Round) != 1 ||
            CompleteProgramEventHistory().OfType<TieredRoundConversionUseIssuedEvent>().Count(e => e.Receipt == r) != 1) return false;
        return owner switch
        {
            NullificationWindowFrame n => r.EffectiveKind == CardKind.Nullification && LifecycleCardUse(n.ParentFrameId) is { } parent &&
                action.ParentActionId == parent.Action?.ActionId && action.OpponentSeat == n.SourceSeat,
            CardUseFrame use => r.EffectiveKind == CardKind.Dodge && use.CardAttack is { } attack && IsSlashCard(use.CardKind) &&
                action.ParentActionId == use.Action?.ActionId && action.ActorSeat == attack.TargetSeat && action.OpponentSeat == attack.SourceSeat,
            _ => false
        };
    }

    private static TieredRoundConversionUseReceipt? TieredRoundResponseReceipt(ResolutionFrame owner) => owner switch
    { NullificationWindowFrame n => n.TieredRoundResponseUse, CardUseFrame use => use.TieredRoundResponseUse, _ => null };

    private CardActionContext RequireTieredRoundIssuedResponseAction(ResolutionFrame owner)
    {
        var receipt = TieredRoundResponseReceipt(owner) ?? throw new InvalidOperationException("A tiered response lost its issued receipt.");
        var accepted = CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>()
            .Where(e => e.Action.ActionId == receipt.CardActionId).ToArray();
        if (accepted is not [var fact] || !IsIssuedTieredRoundResponse(owner, fact.Action))
            throw new InvalidOperationException("A tiered response lost its unique accepted action, typed parent, original source or paid allowance.");
        return fact.Action;
    }

    private void AssertTieredRoundResponseUses()
    {
        foreach (var owner in _resolutionStack.Where(f => TieredRoundResponseReceipt(f) is not null))
            RequireTieredRoundIssuedResponseAction(owner);
        // A removed nullable receipt must not disguise an in-flight new-policy
        // response as an old native response. These windows keep their actual
        // response action, never the incoming Slash/trick's Use action.
        foreach (var window in _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Where(w =>
            w.Action.Type == CardActionType.Response && w.Action.ConversionChain.Any(s => ViewAsRule(s)?.TieredRoundConversion is not null)))
        {
            var owner = _resolutionStack.SingleOrDefault(f => f.Id == window.ParentFrameId);
            if (owner is not (CardUseFrame or NullificationWindowFrame) ||
                !TieredRoundActionsStructurallyMatch(RequireTieredRoundIssuedResponseAction(owner), window.Action))
                throw new InvalidOperationException("A tiered response window lost its exact issued action and owning receipt.");
        }
        foreach (var counter in _resolutionStack.OfType<NullificationWindowFrame>().Where(n =>
            n.CounterspellPayment?.Action.ConversionChain.Any(s => ViewAsRule(s)?.TieredRoundConversion is not null) == true))
            if (!TieredRoundActionsStructurallyMatch(RequireTieredRoundIssuedResponseAction(counter), counter.CounterspellPayment!.Action))
                throw new InvalidOperationException("A tiered response payment lost its exact accepted action.");
    }

    private void ClearTieredRoundDodgeResponse(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { TieredRoundResponseUse: not null } use) return;
        var action = RequireTieredRoundIssuedResponseAction(use);
        if (action.EffectiveKind != CardKind.Dodge || action.ActorSeat != attack.TargetSeat || action.OpponentSeat != attack.SourceSeat ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.Action.ActionId == action.ActionId))
            throw new InvalidOperationException("A tiered response cannot clear before its original Dodge children return.");
        ReplaceRuntimeFrame(use.Id, use with { TieredRoundResponseUse = null });
    }

    private NullificationWindowFrame ClearTieredRoundCounterspellResponse(NullificationWindowFrame input)
    {
        var current = _resolutionStack.OfType<NullificationWindowFrame>().Single(f => f.Id == input.Id);
        if (current.TieredRoundResponseUse is null) return current;
        var action = RequireTieredRoundIssuedResponseAction(current);
        if (action.EffectiveKind != CardKind.Nullification || _resolutionStack.LastOrDefault()?.Id != current.Id ||
            current.CounterspellPayment is not null ||
            _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.Action.ActionId == action.ActionId))
            throw new InvalidOperationException("A tiered response cannot clear before its original counterspell children return.");
        return ReplaceNullificationWindowFrame(current with { TieredRoundResponseUse = null });
    }

    private bool TryResolveTieredRoundZeroAiCounterspell()
    {
        if (ActiveNullificationWindow is not { } n || _pendingDecision is not { Kind: DecisionKind.Nullification } prompt) return false;
        var actor = _players[prompt.PlayerSeat];
        var choice = TieredRoundZeroResponseChoices(actor, CardKind.Nullification).FirstOrDefault();
        if (choice is null) return false;
        var (use, thought) = _aiBrains[actor.Seat].ChooseTieredZeroCounterspell(CreateSnapshot(actor.Seat), n.EffectCardKind,
            n.SourceSeat, n.TargetSeats, n.EffectNullified, n.ChainDepth, ++_thoughtSequence);
        AddThought(thought);
        if (!use) return false;
        ResolveTieredRoundZeroResponse(choice, false); return true;
    }

    private bool TryResolveTieredRoundZeroAiDying()
    {
        if (ActiveDying is not { } dying || _players[dying.ResponderSeat].IsHuman) return false;
        var actor = _players[dying.ResponderSeat];
        var choices = TieredRoundZeroResponseChoices(actor, CardKind.Peach, true).Concat(TieredRoundZeroResponseChoices(actor, CardKind.Alcohol, true)).ToArray();
        if (choices.Length == 0) return false;
        var (use, thought) = _aiBrains[actor.Seat].ChooseProgramDyingRescue(CreateSnapshot(actor.Seat), dying.VictimSeat,
            ProgramConversionName(RequireConversionSource(choices[0])), ++_thoughtSequence);
        AddThought(thought); if (!use) return false;
        // Use the same actual response prompt contract as the human path. No
        // private fake cards, direct HP writes or fabricated Accepted facts.
        _pendingDecision = new(DecisionKind.RescueDying, actor.Seat, "选择已发布的零材料救援。", [], [], dying.VictimSeat)
        { PromptId = CreatePromptId(), TargetSeat = dying.VictimSeat, Choices = Array.AsReadOnly(choices) };
        ResolveTieredRoundZeroResponse(choices[0], false); return true;
    }

    private bool TryResolveTieredRoundZeroAiDodge()
    {
        if (_pendingDecision is not { Kind: DecisionKind.RespondDodge } prompt || ActiveCardAttack is not { } attack ||
            _players[prompt.PlayerSeat].IsHuman) return false;
        var actor = _players[prompt.PlayerSeat]; var choice = TieredRoundZeroResponseChoices(actor, CardKind.Dodge).FirstOrDefault();
        if (choice is null || !prompt.Choices.Any(c => c.Id == choice.Id)) return false;
        var (use, _, thought) = _aiBrains[actor.Seat].ChooseDodgeResponse(CreateSnapshot(actor.Seat), attack.SourceSeat,
            ++_thoughtSequence, attack.IgnoresArmor, true);
        AddThought(thought); if (!use) return false;
        ResolveTieredRoundZeroResponse(choice, false); return true;
    }
}
