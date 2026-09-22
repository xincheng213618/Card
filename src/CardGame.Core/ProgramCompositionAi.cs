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
    Func<string, bool>? BooleanState = null);

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
        var context = new ProgramAiEstimateContext(player, faceDown, publicContext);
        foreach (var effect in effects)
        {
            if (!EvaluateCondition(effect.Condition, player, publicContext)) continue;
            ProgramOperationCatalog.Default.Resolve(effect.Op).AiPolicy.Apply(effect, context);
        }
        return context.Build();
    }

    private static bool EvaluateCondition(SkillProgramCondition condition, PlayerSkillContext player,
        ProgramAiPublicContext? context)
    {
        if (condition.Kind == SkillProgramConditionKind.PindianWon && context?.PindianWon is null) return false;
        if (condition.Kind == SkillProgramConditionKind.BooleanState && context?.BooleanState is null) return false;
        return condition.Evaluate(player,
            context?.PindianWon ?? (_ => false), context?.BooleanState ?? (_ => false),
            context?.CardUseIsRed);
    }
}

internal sealed class ProgramAiEstimateContext
{
    private sealed record CardSetEstimate(double Count, double[] Suits, bool OwnerHeld);

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
    private double _estimatedHp;
    private double _estimatedHandCount;
    private bool _becameSelfLethal;
    private bool _faceDown;
    private bool _givesSelected;
    private bool _discardsSelected;
    private bool _canUseSlashOnOther;

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
            SkillProgramNumberExpression.LivingFactionCount => _publicContext.LivingFactionCount,
            SkillProgramNumberExpression.TargetMaxHpMinusHandCount =>
                TargetsOwner(effect)
                    ? Math.Max(0, _player.MaxHp - _estimatedHandCount)
                    : _publicContext.SelectedTarget is { } target
                        ? Math.Max(0, target.MaxHp - target.HandCount)
                        : 1d,
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
        else _targetRecovery += Rounded(amount);
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
        foreach (var suit in effect.Suits) suits[(int)suit] = source.Suits[(int)suit];
        _bindings[effect.ResultBind!] = new CardSetEstimate(suits.Sum(), suits, source.OwnerHeld);
    }

    internal void Subset(SkillProgramEffect effect)
    {
        var source = Binding(effect.SourceBind);
        var rankCapacity = effect.MaximumRankSum / 7d;
        var count = Math.Min(source.Count, Math.Min(effect.MaximumCards, rankCapacity));
        count = Math.Min(source.Count, Math.Max(effect.MinimumCards, count));
        var ratio = source.Count <= 0 ? 0 : count / source.Count;
        _bindings[effect.ResultBind!] = new CardSetEstimate(
            count, source.Suits.Select(value => value * ratio).ToArray(), source.OwnerHeld);
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
            _ownerDraw -= count;
            _estimatedHandCount = Math.Max(0d, _estimatedHandCount - count);
        }
        // Bindings retain their original location. A move can consume only
        // part of a source through exceptBind; its remaining cards did not move.
    }

    internal void Gift(SkillProgramEffect effect)
    {
        _ = Binding(effect.SourceBind); // The owner may keep the card, so the choice has no forced cost.
    }

    internal void SelectTarget(SkillProgramEffect effect) { }

    internal void SelectTargets(SkillProgramEffect effect) { }

    internal void SelectSourceCard(SkillProgramEffect effect) =>
        _bindings[effect.ResultBind!] = UnknownCards(1d, ownerHeld: false);

    internal void TakeRandomHandCards(SkillProgramEffect effect)
    {
        _ownerDraw += effect.Amount;
        _estimatedHandCount += effect.Amount;
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

    internal void AdjustNormalDraw(SkillProgramEffect effect) => _ownerDraw += effect.Amount;

    internal void GrantTurnCardDamageModifier(SkillProgramEffect effect) =>
        _otherAdjustment += effect.Amount * 6d;

    internal void GrantTurnCardActionProhibition(SkillProgramEffect effect)
    {
        _otherAdjustment -= 8d;
        if (effect.ActionTypes.Contains(CardActionType.Use) &&
            new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash }.All(effect.CardKinds.Contains))
            _canUseSlashOnOther = false;
    }

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

    internal void StartJudgment(SkillProgramEffect effect) =>
        _bindings[effect.ResultBind!] = UnknownCards(1d, ownerHeld: false);

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

    internal void SetChainedState(SkillProgramEffect effect) =>
        _otherAdjustment += effect.Chained == true ? -4d : 4d;

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
            _otherAdjustment += effect.FaceDown == true ? 12d : -12d;
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

    internal void Damage(SkillProgramEffect effect) => _targetHpLoss += effect.Amount;
    internal void Pindian(SkillProgramEffect effect) => _otherAdjustment += 4d;
    internal void ChangeMaximumHp(SkillProgramEffect effect) =>
        _otherAdjustment += effect.Amount * 18d;
    internal void GrantSkills(SkillProgramEffect effect) =>
        _otherAdjustment += effect.SkillIds.Count * 12d;
    internal ProgramAiEstimate Build()
    {
        var ownerDraw = Rounded(Math.Max(0, _ownerDraw));
        var adjustment = _otherAdjustment + Math.Min(0, _ownerDraw) * 8d;
        var hint = new SkillProgramAiHint(
            ownerDraw, Rounded(_ownerRecovery), Rounded(_ownerHpLoss),
            Rounded(_targetDraw), Rounded(_targetRecovery), Rounded(_targetHpLoss),
            _givesSelected, _discardsSelected)
        {
            ValueAdjustment = adjustment
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
        effect.Target == SkillProgramEffectTarget.Owner ||
        effect.Target == SkillProgramEffectTarget.Actor && _publicContext.CardActionActorIsOwner;

    private static CardSetEstimate UnknownCards(double count, bool ownerHeld) =>
        new(count, Enumerable.Repeat(count / 4d, 4).ToArray(), ownerHeld);

    private static int Rounded(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);
}
