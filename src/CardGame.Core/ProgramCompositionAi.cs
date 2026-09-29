namespace CardGame.Core;

internal sealed record ProgramAiEstimate(
    SkillProgramAiHint Hint,
    double Score,
    bool IsSelfLethal,
    string Approximation);

internal sealed record ProgramAiPublicContext(
    int LivingFactionCount,
    int NormalDrawCount = 0,
    bool ReplacesNormalDraw = false,
    PlayerSkillContext? SelectedTarget = null,
    bool CanUseSlashOnOther = true,
    bool? CardUseIsRed = null,
    bool CardActionActorIsOwner = false,
    bool CardUseDebitActive = false,
    Func<string, bool>? PindianWon = null,
    Func<string, bool>? BooleanState = null,
    Func<string, string?>? ChoiceResult = null,
    PlayerSkillContext? Actor = null,
    CardKind? CardUseEffectiveKind = null,
    // Active action scoring prices these exact input cards separately from effects.
    int? ActivationCardCount = null,
    bool HasClaimableDamageCards = false,
    int? EligibleTargetCount = null,
    Func<string, bool>? AttackRangeCoverageDecreased = null,
    Func<IReadOnlyList<CardZoneKind>, IReadOnlyList<SkillProgramCardCategory>, bool>? HasOwnedCardCategory = null,
    double CardEffectInterventionScore = 0d,
    Func<string, int>? PhaseUsageCount = null,
    int AttackRange = 0,
    int? LivingPlayersMinHp = null,
    int? TurnOwnerDiscardPhaseHandDiscardCount = null);

/// <summary>
/// Pure, public-state estimate for schema-23 program compositions. Unknown cards use a
/// uniform four-suit prior; subset size uses the configured maximum and mean rank seven.
/// </summary>
internal static class ProgramCompositionAi
{
    internal static ProgramAiEstimate Estimate(
        IEnumerable<SkillProgramEffect> effects,
        PlayerSkillContext player,
        bool faceDown = false,
        ProgramAiPublicContext? publicContext = null)
    {
        ArgumentNullException.ThrowIfNull(effects);
        ArgumentNullException.ThrowIfNull(player);
        var instructions = effects.ToArray();
        var choices = new Dictionary<string, string>(StringComparer.Ordinal);
        var supplied = publicContext ?? new ProgramAiPublicContext(0);
        var facts = supplied with { ChoiceResult = name => choices.GetValueOrDefault(name) ?? supplied.ChoiceResult?.Invoke(name) };
        var context = new ProgramAiEstimateContext(player, faceDown, facts);
        for (var index = 0; index < instructions.Length; index++)
        {
            var effect = instructions[index];
            if (!EvaluateCondition(effect.Condition, player, facts)) continue;
            if (effect.Op == SkillProgramEffectOp.AccumulateSelectedCardCount)
            {
                var before = supplied.PhaseUsageCount?.Invoke(effect.StateId!) ?? 0;
                var after = before + Math.Max(0, supplied.ActivationCardCount ?? 0);
                choices[effect.ResultBind!] = before < effect.Amount && after >= effect.Amount
                    ? "crossed" : "not-crossed";
                continue;
            }
            if (effect.Op is SkillProgramEffectOp.ChooseOption or SkillProgramEffectOp.ChooseDifferentCategoryDiscard)
            {
                if (supplied.ChoiceResult?.Invoke(effect.ResultBind!) is { } committed)
                {
                    choices[effect.ResultBind!] = committed;
                    continue;
                }
                if (effect.Op == SkillProgramEffectOp.ChooseDifferentCategoryDiscard)
                {
                    choices[effect.ResultBind!] = ChooseDifferentCategoryDiscardProgramOperationDescriptor.DeclinedOption;
                    continue;
                }
                var chooser = ProgramChoiceAi.Target(effect.Target, player, facts);
                if (chooser is null) continue;
                var selected = effect.Options.Where(option => option.Condition.EvaluateOption(chooser,
                        () => supplied.HasClaimableDamageCards,
                        facts.AttackRangeCoverageDecreased,
                        hasOwnedCardCategory: facts.HasOwnedCardCategory,
                        activationCardCount: facts.ActivationCardCount,
                        boundCardSuitMatchesChoice: (_, _) => false))
                    .OrderByDescending(option => ProgramChoiceAi.Score(instructions.Skip(index + 1),
                        player, chooser, facts with { ChoiceResult = name => name == effect.ResultBind ? option.Id : facts.ChoiceResult!(name) }))
                    .ThenBy(option => option.Id, StringComparer.Ordinal).FirstOrDefault();
                if (selected is not null) choices[effect.ResultBind!] = selected.Id;
                continue;
            }
            ProgramOperationCatalog.Default.Resolve(effect.Op).AiPolicy.Apply(effect, context);
        }
        return context.Build();
    }

    internal static bool EvaluateCondition(SkillProgramCondition condition, PlayerSkillContext player,
        ProgramAiPublicContext? context)
    {
        if (condition.Kind == SkillProgramConditionKind.PindianWon && context?.PindianWon is null) return false;
        if (condition.Kind == SkillProgramConditionKind.BooleanState && context?.BooleanState is null) return false;
        return condition.Evaluate(player, context?.SelectedTarget,
            context?.PindianWon ?? (_ => false), context?.BooleanState ?? (_ => false),
            context?.CardUseIsRed, context?.ChoiceResult,
            attackRangeCoverageDecreased: context?.AttackRangeCoverageDecreased,
            activationCardCount: context?.ActivationCardCount,
            boundCardSuitMatchesChoice: (_, _) => false,
            boundCardCategoryMatchesCardAction: (_, _) => false);
    }
}

internal sealed class ProgramAiEstimateContext
{
    private sealed record CardSetEstimate(double Count, double[] Suits, bool OwnerHeld, bool TargetHeld = false,
        bool ActivationInput = false);

    private readonly PlayerSkillContext _player;
    private readonly ProgramAiPublicContext _publicContext;
    private readonly Dictionary<string, CardSetEstimate> _bindings = new(StringComparer.Ordinal);
    private double _ownerDraw;
    private double _ownerRecovery;
    private double _ownerHpLoss;
    private double _targetDraw;
    private double _targetRecovery;
    private double _targetHpLoss;
    private double _otherAdjustment;
    private double _targetAdjustment;
    private double _estimatedHp;
    private double _estimatedHandCount;
    private bool _becameSelfLethal;
    private bool _faceDown;
    private bool _givesSelected;
    private bool _discardsSelected;
    private bool _canUseSlashOnOther;
    private int? _estimatedSelectedTargetCount;

    internal ProgramAiEstimateContext(
        PlayerSkillContext player,
        bool faceDown,
        ProgramAiPublicContext? publicContext = null)
    {
        _player = player;
        _publicContext = publicContext ?? new ProgramAiPublicContext(0);
        _canUseSlashOnOther = _publicContext.CanUseSlashOnOther;
        _estimatedHp = player.Hp;
        _estimatedHandCount = player.HandCount;
        _faceDown = faceDown;
        if (_publicContext.ReplacesNormalDraw)
            _ownerDraw = -Math.Max(0, _publicContext.NormalDrawCount);
    }

    internal void Draw(SkillProgramEffect effect)
    {
        var amount = effect.NumberExpression switch
        {
            SkillProgramNumberExpression.OwnerLostHp => Math.Max(0, _player.MaxHp - _player.Hp),
            SkillProgramNumberExpression.LivingFactionCount => _publicContext.LivingFactionCount,
            SkillProgramNumberExpression.LivingPlayersMinHp => _publicContext.LivingPlayersMinHp ?? _player.Hp,
            SkillProgramNumberExpression.TargetMaxHpMinusHandCount =>
                TargetsOwner(effect)
                    ? Math.Max(0, _player.MaxHp - _estimatedHandCount)
                    : _publicContext.SelectedTarget is { } target
                        ? Math.Max(0, target.MaxHp - target.HandCount)
                        : 1d,
            SkillProgramNumberExpression.BoundCardCount => Binding(effect.SourceBind).Count,
            SkillProgramNumberExpression.CurrentAttackRange =>
                Math.Max(1, _publicContext.AttackRange > 0 ? _publicContext.AttackRange : 1),
            _ => effect.Amount
        };
        if (TargetsOwner(effect))
        {
            _ownerDraw += amount;
            _estimatedHandCount += amount;
        }
        else _targetDraw += amount;
        if (effect.ResultBind is { } bind)
            _bindings[bind] = UnknownCards(amount, ownerHeld: true);
    }

    internal void Recover(SkillProgramEffect effect)
    {
        var amount = effect.NumberExpression == SkillProgramNumberExpression.BoundCardCount
            ? Binding(effect.SourceBind).Count
            : effect.Amount;
        if (TargetsOwner(effect))
        {
            var actual = Math.Min(amount, Math.Max(0, _player.MaxHp - _estimatedHp));
            _ownerRecovery += actual;
            _estimatedHp += actual;
        }
        else _targetRecovery += Rounded(amount * (effect.Target == SkillProgramEffectTarget.SelectedTargets
            ? _estimatedSelectedTargetCount ?? 2 : 1));
    }

    internal void LoseHp(SkillProgramEffect effect)
    {
        if (TargetsOwner(effect))
        {
            _ownerHpLoss += effect.Amount;
            _estimatedHp -= effect.Amount;
            if (_estimatedHp <= 0) _becameSelfLethal = true;
        }
        else _targetHpLoss += effect.Amount;
    }

    internal void TakeRandomCardsFromParticipant(SkillProgramEffect effect)
    {
        // A random take from a bound participant enriches the owner by the configured
        // amount at the participant's expense; the exact hidden identity is irrelevant.
        _ownerDraw += effect.Amount;
        _estimatedHandCount += effect.Amount;
        _otherAdjustment += effect.Amount * 2d;
    }

    internal void Reveal(SkillProgramEffect effect)
    {
        var amount = effect.NumberExpression == SkillProgramNumberExpression.OwnerLostHp
            ? Math.Max(0, _player.MaxHp - _player.Hp)
            : effect.Amount;
        _bindings[effect.ResultBind!] = UnknownCards(amount, ownerHeld: false);
    }

    internal void Filter(SkillProgramEffect effect)
    {
        var source = Binding(effect.SourceBind);
        var suits = new double[4];
        foreach (var suit in Enum.GetValues<Suit>())
            suits[(int)suit] = source.Suits[(int)suit] *
                ProgramCardSetFilter.PriorForSuit(suit, effect.Suits, effect.CardCategories,
                    effect.EquipmentSlots, effect.CardKinds);
        _bindings[effect.ResultBind!] = new CardSetEstimate(suits.Sum(), suits, source.OwnerHeld, source.TargetHeld,
            source.ActivationInput);
    }

    internal void Subset(SkillProgramEffect effect)
    {
        var source = Binding(effect.SourceBind);
        var rankCapacity = effect.MaximumRankSum / 7d;
        var count = Math.Min(source.Count, Math.Min(effect.MaximumCards, rankCapacity));
        count = Math.Min(source.Count, Math.Max(effect.MinimumCards, count));
        var ratio = source.Count <= 0 ? 0 : count / source.Count;
        _bindings[effect.ResultBind!] = new CardSetEstimate(
            count, source.Suits.Select(value => value * ratio).ToArray(), source.OwnerHeld, source.TargetHeld,
            source.ActivationInput);
        if (!source.OwnerHeld && count > 0 && source.Count > count)
            _otherAdjustment += Math.Min(8d, (source.Count - count) * 2d);
    }

    internal void Move(SkillProgramEffect effect)
    {
        var source = Binding(effect.SourceBind);
        var except = effect.ExceptBind is null ? null : Binding(effect.ExceptBind);
        var count = Math.Max(0, source.Count - (except?.Count ?? 0));
        if (effect.Destination == SkillProgramCardDestination.OwnerHand && !source.OwnerHeld)
        {
            _ownerDraw += count;
            _estimatedHandCount += count;
        }
        else if (effect.Destination == SkillProgramCardDestination.DiscardPile && source.OwnerHeld)
        {
            if (source.ActivationInput && _publicContext.ActivationCardCount is not null)
                _discardsSelected = true;
            else
                _ownerDraw -= count;
            _estimatedHandCount = Math.Max(0d, _estimatedHandCount - count);
        }
        else if (effect.Destination == SkillProgramCardDestination.OwnerPersistentZone && source.OwnerHeld)
        {
            _otherAdjustment += count * 4d;
            _estimatedHandCount = Math.Max(0d, _estimatedHandCount - count);
        }
        if (source.TargetHeld && effect.Destination is SkillProgramCardDestination.DiscardPile or
                SkillProgramCardDestination.OwnerHand or SkillProgramCardDestination.DrawPileTop)
            _targetDraw -= count;
        // Bindings retain their original location. A move can consume only
        // part of a source through exceptBind; its remaining cards did not move.
    }

    internal void Gift(SkillProgramEffect effect)
    {
        _ = Binding(effect.SourceBind); // The owner may keep the card, so the choice has no forced cost.
    }

    internal void TransferRandomOwnedCard(SkillProgramEffect effect)
    {
        _ownerDraw -= 1d;
        _estimatedHandCount = Math.Max(0d, _estimatedHandCount - 1d);
        _targetDraw += 1d;
        _bindings[effect.ResultBind!] = UnknownCards(1d, ownerHeld: false) with { TargetHeld = true };
    }

    internal void AccumulateSelectedCardCount(SkillProgramEffect effect) { }

    internal void SelectTarget(SkillProgramEffect effect) { }

    internal void SelectTargets(SkillProgramEffect effect)
    {
        var expressionLimit = effect.NumberExpression switch
        {
            SkillProgramNumberExpression.PlannedNormalDrawCount => Math.Max(0, _publicContext.NormalDrawCount),
            SkillProgramNumberExpression.CurrentHandCount => Math.Max(0, (int)Math.Floor(_estimatedHandCount)),
            _ => effect.MaximumTargets
        };
        var upperBound = Math.Min(effect.MaximumTargets, expressionLimit);
        // A draw-phase candidate supplies an exact public target count. Other callers
        // may omit it; use the minimum legal selection rather than inventing zero.
        _estimatedSelectedTargetCount = _publicContext.EligibleTargetCount is { } eligible
            ? Math.Min(upperBound, Math.Max(0, eligible))
            : Math.Min(upperBound, effect.MinimumTargets);
    }

    internal void SelectSourceCard(SkillProgramEffect effect) =>
        _bindings[effect.ResultBind!] = UnknownCards(1d, ownerHeld: false);

    internal void DyingRescue(SkillProgramEffect effect) => _otherAdjustment += 6d;

    internal void SelectOwnedCards(SkillProgramEffect effect)
    {
        var count = effect.MinimumCards > 0 ? effect.MinimumCards :
            effect.NumberExpression switch
            {
                SkillProgramNumberExpression.OwnerLostHp => Math.Max(0, _player.MaxHp - _player.Hp),
                SkillProgramNumberExpression.HandHalfFloor => (int)(_estimatedHandCount / 2d),
                SkillProgramNumberExpression.LivingPlayersMinHp =>
                    _publicContext.LivingPlayersMinHp ?? _player.Hp,
                SkillProgramNumberExpression.SelectedPairHandDifference => Math.Max(0,
                    (_publicContext.SelectedTarget?.HandCount ?? 0) - _estimatedHandCount),
                _ => effect.Amount
            };
        var ownedByActor = TargetsOwner(effect);
        // Only hand counts and a bounded public-zone prior are available here.
        var knownHand = ownedByActor ? _estimatedHandCount
            : (_publicContext.SelectedTarget?.HandCount ?? 1) + _targetDraw;
        var available = (effect.Zones.Contains(CardZoneKind.Hand) ? Math.Max(0, knownHand) : 0) +
            effect.Zones.Count(zone => zone is CardZoneKind.Equipment or CardZoneKind.Judgment);
        var selected = Math.Min(count, available);
        _bindings[effect.ResultBind!] = new(selected, Enumerable.Repeat(selected / 4d, 4).ToArray(),
            ownedByActor, !ownedByActor);
    }

    internal void CaptureSelectedCards(SkillProgramEffect effect) =>
        _bindings[effect.ResultBind!] = UnknownCards(_publicContext.ActivationCardCount ?? 1d, ownerHeld: true)
            with
        { ActivationInput = true };

    internal void TakeRandomHandCards(SkillProgramEffect effect)
    {
        var count = _estimatedSelectedTargetCount ?? effect.Amount;
        _ownerDraw += count;
        _estimatedHandCount += count;
    }

    internal void TakeRandomCardFromEveryOtherCharacter(SkillProgramEffect effect)
    {
        // Every other living character gives up exactly one card, and the public areas
        // are known, so this is scored as one guaranteed card per other character. When
        // the public context carries no eligible count, one card is the conservative prior.
        var others = Math.Max(1, _publicContext.EligibleTargetCount ?? 1);
        _ownerDraw += others;
        _estimatedHandCount += others;
        if (effect.Zones.Any(zone => zone is CardZoneKind.Equipment or CardZoneKind.Judgment))
            _otherAdjustment += 1d;
    }

    internal void ClaimDamageCards(SkillProgramEffect effect) =>
        _otherAdjustment += 8d; // The concrete processing-card count is outside PlayerSkillContext.

    internal void InsertPhase(SkillProgramEffect effect) => _otherAdjustment += 16d;

    internal void RecoverTo(SkillProgramEffect effect)
    {
        var targetHp = effect.NumberExpression switch
        {
            SkillProgramNumberExpression.IntegerConstant => effect.MinimumValue,
            SkillProgramNumberExpression.LivingFactionCount =>
                Math.Max(effect.MinimumValue, _publicContext.LivingFactionCount),
            SkillProgramNumberExpression.LivingPlayersMinHp =>
                Math.Max(effect.MinimumValue, _publicContext.LivingPlayersMinHp ?? _player.Hp),
            _ => effect.MinimumValue
        };
        if (TargetsOwner(effect))
        {
            var actual = Math.Max(0d, Math.Min(_player.MaxHp, targetHp) - _estimatedHp);
            _ownerRecovery += actual;
            _estimatedHp += actual;
        }
        else
        {
            // The selected target's HP is not part of the public owner context.
            // Use one missing HP as the explicit public-only recovery prior.
            _targetRecovery += targetHp > 0 ? 1 : 0;
        }
    }

    internal void AdjustNormalDraw(SkillProgramEffect effect) =>
        _ownerDraw += effect.NumberExpression == SkillProgramNumberExpression.SelectedTargetCount
            ? -(_estimatedSelectedTargetCount ?? 1) : effect.Amount;

    internal void GrantTurnCardDamageModifier(SkillProgramEffect effect) =>
        _otherAdjustment += effect.Amount *
            (effect.DamageModifierExpiration == SkillProgramDamageModifierExpiration.NextOwnerTurnStart ? 11d : 6d);

    internal void GrantTurnCardActionProhibition(SkillProgramEffect effect)
    {
        _otherAdjustment -= 8d;
        if (effect.ActionTypes.Contains(CardActionType.Use) &&
            new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }.All(effect.CardKinds.Contains))
            _canUseSlashOnOther = false;
    }

    internal void GrantTurnHandColorRestriction(SkillProgramEffect effect) =>
        _targetAdjustment -= 10d;

    internal void PreventCurrentDamage(SkillProgramEffect effect) =>
        _otherAdjustment += 32d;

    internal void RedirectCurrentDamage(SkillProgramEffect effect) =>
        _otherAdjustment += effect.BooleanValue == true ? 8d : 12d;

    internal void NullifyCurrentCardEffect()
    {
        if (_publicContext.CardUseEffectiveKind is
            CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
            CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
            CardKind.Dismantlement or CardKind.Snatch or CardKind.FireAttack or
            CardKind.BorrowedSword)
            _otherAdjustment += 32d;
    }

    internal void NullifySelectedCardEffects() =>
        _otherAdjustment += _publicContext.CardEffectInterventionScore;

    internal void ProhibitCurrentResponse() => _otherAdjustment += 20d;

    internal void RedirectCurrentAttack() => _otherAdjustment += 8d;

    internal void GrantTurnRuleModifier(SkillProgramEffect effect)
    {
        if (!_canUseSlashOnOther && effect.RuleQuery is
            SkillRuleQuery.SlashLimit or SkillRuleQuery.SlashDistanceLimit or SkillRuleQuery.AttackRange)
            return;
        _otherAdjustment += 8d;
    }

    internal void GrantTurnCardTargetRestriction(SkillProgramEffect effect)
    {
        _otherAdjustment -= 5d;
        if (effect.TargetRestriction == SkillProgramCardTargetRestriction.SelfOnly)
            _canUseSlashOnOther = false;
    }

    internal void StartJudgment(SkillProgramEffect effect)
    {
        _bindings[effect.ResultBind!] = UnknownCards(1d, ownerHeld: false);
        // A free public judgment can enable later conditional effects. Its
        // unknown suit must not make an optional judgment look strictly inert.
        _otherAdjustment += 1d;
    }

    internal void StartPindian(SkillProgramEffect effect) =>
        _otherAdjustment += 2d; // Public-only neutral contest prior; no hand cards are inspected.

    internal void SetBooleanState(SkillProgramEffect effect) { }
    internal void ToggleBooleanState(SkillProgramEffect effect) { }
    internal void GrantDirectedTurnCardPolicy(SkillProgramEffect effect) => _otherAdjustment += 8d;

    internal void GrantTurnCardConversion(SkillProgramEffect effect)
    {
        _ = Binding(effect.SourceBind);
        _otherAdjustment += 10d;
    }

    internal void DiscardOwnedZoneCards(SkillProgramEffect effect)
    {
        if (effect.Zones.Contains(CardZoneKind.Hand))
            _otherAdjustment -= _player.HandCount * 8d;
        _otherAdjustment -= effect.Zones.Count(zone => zone != CardZoneKind.Hand) * 4d;
    }

    internal void SetChainedState(SkillProgramEffect effect)
    {
        var target = ProgramChoiceAi.Target(effect.Target, _player, _publicContext);
        if (target is not null && effect.Chained == target.IsChained) return;
        var value = effect.Chained == true ? -4d : 4d;
        if (TargetsOwner(effect)) _otherAdjustment += value;
        else _targetAdjustment += value;
    }

    internal void HoldTargetCards(SkillProgramEffect effect)
    {
        var target = _publicContext.SelectedTarget;
        if (target is null) return;
        // Public prior: the target's visible hand count plus one possible equipment card.
        var available = Math.Max(0, target.HandCount) + 1d;
        var held = Math.Min(Math.Max(1, target.Hp), available);
        _otherAdjustment += held * 6d;
    }

    internal void UseBoundCardByTarget(SkillProgramEffect effect) =>
        _otherAdjustment += 8d;

    internal void PendExtraTurn() => _otherAdjustment += 20d;

    internal void ClaimDeathCleanupCards() => _otherAdjustment += 10d;

    internal void BindDiscardPhaseDiscards(SkillProgramEffect effect) =>
        _bindings[effect.ResultBind!] = UnknownCards(
            Math.Max(0, _publicContext.TurnOwnerDiscardPhaseHandDiscardCount ?? 0), ownerHeld: false);

    internal void ClaimMovedCards()
    {
        _ownerDraw += 1d;
        _estimatedHandCount += 1d;
    }

    internal void SelectAndMoveOwnedCard(SkillProgramEffect effect)
    {
        if (effect.CardOwnerRef?.Kind == ProgramParticipantRef.Owner &&
            effect.Destination == SkillProgramCardDestination.DiscardPile)
        {
            _ownerDraw -= 1d;
            _estimatedHandCount = Math.Max(0d, _estimatedHandCount - 1d);
        }
        else if (effect.CardOwnerRef?.Kind == ProgramParticipantRef.Actor &&
                 !_publicContext.CardActionActorIsOwner &&
                 effect.Destination == SkillProgramCardDestination.OwnerHand)
        {
            _ownerDraw += 1d;
            _estimatedHandCount += 1d;
        }
        else if (effect.CardOwnerRef?.Kind == ProgramParticipantRef.SelectedTarget &&
                 effect.Destination == SkillProgramCardDestination.OwnerHand)
        {
            // A participant-relative transfer has the same public one-card value as
            // taking an actor card. The exact hidden card kind is never inspected.
            _ownerDraw += 1d;
            _estimatedHandCount += 1d;
            _targetDraw -= 1d;
        }
        else if (effect.CardOwnerRef?.Kind == ProgramParticipantRef.Owner &&
                 effect.Destination == SkillProgramCardDestination.SelectedTargetEquipment)
        {
            // An equipment gift trades one owner hand card for public board value
            // on the target; the accompanying draw refunds the card count.
            _ownerDraw -= 1d;
            _estimatedHandCount = Math.Max(0d, _estimatedHandCount - 1d);
            _otherAdjustment += 2d;
        }
        else if (effect.CardOwnerRef?.Kind == ProgramParticipantRef.Owner &&
                 effect.Destination == SkillProgramCardDestination.SelectedTargetCorrespondingZone)
        {
            // Equipping a selected target with an owned board card; the follow-up
            // draw effect restores the owner's hand count separately.
            _targetDraw += 1d;
        }
    }

    internal void RestorePhaseHandDiscards(SkillProgramEffect effect)
    {
        // Returning a discarded card to the phase owner in exchange for the rest
        // is only worthwhile when the phase produced at least two discards; the
        // exact set is public discard-pile state the estimate does not model, so
        // a single-card phase conservatively scores neutral.
        _otherAdjustment += 4d;
    }

    internal void ChooseOtherOwnedCardDiscard(SkillProgramEffect effect) =>
        _otherAdjustment += 8d;

    internal void ChooseOwnCardDiscard(SkillProgramEffect effect) =>
        _otherAdjustment += effect.ChooserRef is null ? -8d : 8d;

    internal void ExchangeSelectedTargetHands(SkillProgramEffect effect)
    {
        // The pair is ordered ascending by hand count, so the exchange moves the
        // smaller hand toward the larger one from the actor's scoring view.
        var gain = Math.Max(0, (_publicContext.SelectedTarget?.HandCount ?? 0) - _estimatedHandCount);
        _ownerDraw += gain;
        _otherAdjustment += 4d;
    }

    internal void RequestSlashByTarget(SkillProgramEffect effect)
    {
        // Split prior: about half the time the target declines and loses a card,
        // otherwise the owner faces a dodgeable Slash.
        _targetDraw -= 0.5d;
        _ownerHpLoss += 0.5d;
    }

    internal void RefundCardUseDebit(SkillProgramEffect effect)
    {
        if (_publicContext.CardUseDebitActive) _otherAdjustment += 8d;
    }

    internal void TurnOver(SkillProgramEffect effect)
    {
        if (!TargetsOwner(effect))
        {
            _otherAdjustment += 12d;
            return;
        }
        _otherAdjustment += _faceDown ? 12d : -12d;
        _faceDown = !_faceDown;
    }

    internal void SetFaceState(SkillProgramEffect effect)
    {
        if (!TargetsOwner(effect))
        {
            var target = ProgramChoiceAi.Target(effect.Target, _player, _publicContext);
            if (target is null || effect.FaceDown != target.IsFaceDown)
                _targetAdjustment += effect.FaceDown == true ? -24d : 24d;
            return;
        }
        if (effect.FaceDown == _faceDown) return;
        _otherAdjustment += effect.FaceDown == true ? -12d : 12d;
        _faceDown = effect.FaceDown!.Value;
    }

    internal void GiveSelected(SkillProgramEffect effect) => _givesSelected = true;


    internal void DiscardSelected(SkillProgramEffect effect)
    {
        _discardsSelected = true;
    }

    internal void Damage(SkillProgramEffect effect) => _targetHpLoss +=
        effect.Condition.Kind == SkillProgramConditionKind.BoundCardSuitMatchesChoice
            ? effect.Amount * 0.75d : effect.Amount;
    internal void Pindian(SkillProgramEffect effect) => _otherAdjustment += 4d;
    internal void ChangeMaximumHp(SkillProgramEffect effect) =>
        _otherAdjustment += effect.Amount * 18d;
    internal void GrantSkills(SkillProgramEffect effect) =>
        _otherAdjustment += effect.SkillIds.Count * 12d;
    internal void GrantTurnSkills(SkillProgramEffect effect) =>
        _otherAdjustment += effect.SkillIds.Count * 8d;
    internal void UseSelectedCardsAs(SkillProgramEffect effect) =>
        _targetHpLoss += effect.OutputKind switch
        {
            CardKind.Slash => 1d,
            CardKind.ArrowBarrage => Math.Max(1d, _publicContext.EligibleTargetCount ?? 2),
            _ => 0d
        };
    internal ProgramAiEstimate Build()
    {
        var ownerDraw = Rounded(Math.Max(0, _ownerDraw));
        var adjustment = _otherAdjustment + Math.Min(0, _ownerDraw) * 8d;
        var hint = new SkillProgramAiHint(
            ownerDraw, Rounded(_ownerRecovery), Rounded(_ownerHpLoss),
            Rounded(Math.Max(0, _targetDraw)), Rounded(_targetRecovery), Rounded(_targetHpLoss),
            _givesSelected, _discardsSelected)
        {
            ValueAdjustment = adjustment,
            TargetValueAdjustment = _targetAdjustment + Math.Min(0, _targetDraw) * 7d
        };
        var score = _ownerDraw * 8d + _ownerRecovery * 18d - _ownerHpLoss * 22d + _otherAdjustment;
        return new ProgramAiEstimate(hint, score, _becameSelfLethal,
            "未知牌按四花色等概率，子集按平均点数7与最大可选数估计；条件使用当前公开快照；" +
            "替代摸牌扣除当前正常摸牌数；未知目标回复按缺失1点体力估计；" +
            "伤害牌、装备区、连环和目标翻面使用不读取私密状态的固定先验。");
    }

    private CardSetEstimate Binding(string? name) =>
        name is not null && _bindings.TryGetValue(name, out var binding)
            ? binding
            : UnknownCards(0d, ownerHeld: false);

    private bool TargetsOwner(SkillProgramEffect effect) =>
        effect.TargetReference is not null ? effect.TargetReference.Kind == ProgramParticipantRef.Owner :
        effect.Target == SkillProgramEffectTarget.Owner ||
        effect.Target == SkillProgramEffectTarget.Actor && _publicContext.CardActionActorIsOwner;

    private static CardSetEstimate UnknownCards(double count, bool ownerHeld) =>
        new(count, Enumerable.Repeat(count / 4d, 4).ToArray(), ownerHeld);

    private static int Rounded(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
