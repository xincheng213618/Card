namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPredictionHpPolicy(CharacterState owner, SkillProgramCardPolicyKind kind,
        PlayerSkillContext context, IReadOnlySet<string>? visibleSkillIds = null) =>
        owner.IsAlive && GetSkillBindingShard(owner).GetCardPolicies(kind)
        .Any(binding => (visibleSkillIds is null || visibleSkillIds.Contains(binding.Source.SkillId)) &&
            binding.Policy.CardKinds.Count == 0 && binding.Policy.RequiredCardKinds.Count == 0 &&
            (binding.Policy.OwnerRole is null || HasSkillRoleQualification(owner,
            binding.Source.SkillId, binding.Source.SkillInstanceId, binding.Policy.OwnerRole.Value)) &&
            binding.Policy.Condition.Evaluate(context));

    // Real rule contexts retain every enabled source. AI contexts additionally
    // require that another player's policy is visible to this exact viewer.
    private PlayerSkillContext CreateAiSkillContext(CharacterState player, int viewerSeat,
        GameSnapshot? snapshot = null)
    {
        var context = CreateSkillContext(player);
        if (player.Seat == viewerSeat) return context;
        snapshot ??= CreateSnapshot(viewerSeat);
        var visible = VisiblePredictionSkillIds(snapshot.Players.Single(target => target.Seat == player.Seat));
        return context with
        {
            IncomingDamageCap = GetIncomingFixedDamageCap(player, visible),
            DamageBecomesHpLoss = HasPredictionHpPolicy(player, SkillProgramCardPolicyKind.DamageBecomesHpLoss, context, visible),
            OtherParticipantDamageBecomesHpLoss = HasPredictionHpPolicy(player, SkillProgramCardPolicyKind.OtherParticipantDamageBecomesHpLoss, context, visible)
        };
    }

    /// <summary>Pure estimate of the viewer's proposed card and visible table; never issues an attempt or consumes usage.</summary>
    public PublicDamagePrediction PredictPublicActionDamage(int viewerSeat, LegalAction action, int targetSeat)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsValidPlayerSeat(viewerSeat) || !IsValidPlayerSeat(targetSeat))
            throw new ArgumentOutOfRangeException(nameof(viewerSeat));
        return PredictPublicActionDamage(CreateSnapshot(viewerSeat), viewerSeat, action, targetSeat);
    }

    private PublicDamagePrediction PredictPublicActionDamage(GameSnapshot view, int viewerSeat,
        LegalAction action, int targetSeat)
    {
        var self = view.Players.Single(player => player.Seat == viewerSeat);
        // An unchanged physical Fire/Thunder Slash has no PlayedCardKind
        // override. Resolve only material already visible in the viewer's own
        // zones; an arbitrary foreign ID must never probe the zone registry.
        var ownMaterial = action.PlayedCardKind is null && action.Kind == LegalActionKind.Slash && action.CardId is { } materialId
            ? self.Hand.Concat(self.WoodenOxGrain ?? []).Concat(self.Equipment)
                .Concat(self.AuthorityCards ?? []).FirstOrDefault(card => card.Id == materialId)
            : null;
        var nativeSlashKind = ownMaterial is { Kind: var printedKind } && IsSlashCard(printedKind)
            ? printedKind : CardKind.Slash;
        var kind = action.PlayedCardKind ?? action.Kind switch
        {
            LegalActionKind.Slash => nativeSlashKind, LegalActionKind.Duel => CardKind.Duel,
            LegalActionKind.BarbarianAssault => CardKind.BarbarianAssault,
            LegalActionKind.ArrowBarrage => CardKind.ArrowBarrage, LegalActionKind.FireAttack => CardKind.FireAttack,
            _ => (CardKind?)null
        };
        if (kind is not { } damageKind || !(IsSlashCard(damageKind) || damageKind is
                CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.FireAttack))
        {
            var harmlessTarget = view.Players.Single(player => player.Seat == targetSeat);
            return new(harmlessTarget.Hp, harmlessTarget.Armor, 0, 0, 0,
                harmlessTarget.Hp, harmlessTarget.Armor, harmlessTarget.MaxHp);
        }
        var ownHand = self.Hand;
        var paidHand = action.CardId is { } id && ownHand.Any(card => card.Id == id) ? 1 : 0;
        var amount = kind is { } card && IsSlashCard(card)
            ? checked(1 + (_players[viewerSeat].HasAlcoholEffect ? Math.Max(1, _players[viewerSeat].AlcoholDamageBonus) : 0)) : 1;
        if (damageKind == CardKind.Duel && TryGetShanheS5ShuangxiongPublicDuelBaseDamage(viewerSeat, action, out var scaled))
            amount = scaled;
        return PredictVisibleDamage(view, viewerSeat, targetSeat, kind is null ? 0 : amount,
            kind, kind is not null, false, false, false, paidHand, HasArmorBypass(_players[viewerSeat]));
    }

    private PublicDamagePrediction PredictPublicIncomingDamage(IDamageAttempt attack, int viewerSeat,
        int? sourceSeat = null, int? targetSeat = null, int? damageAmount = null)
    {
        var source = sourceSeat ?? attack.SourceSeat;
        var target = targetSeat ?? attack.TargetSeat;
        return PredictVisibleDamage(CreateSnapshot(viewerSeat), source, target, damageAmount ?? attack.DamageAmount,
            attack.EffectiveCardKind, attack is CardAttackHandle && !attack.IsDelayedJudgmentDamage &&
            !attack.IsProgramJudgmentDamage && attack.CardUserSeat == source, attack.IsSourceLess,
            attack.IsChainPropagation, attack.DamageAmountFinalized, 0, attack.IgnoresArmor, GetDamageNature(attack), attack);
    }

    private PublicDamagePrediction PredictVisibleDamage(GameSnapshot view, int sourceSeat, int targetSeat,
        int amount, CardKind? kind, bool cardUse, bool sourceLess, bool chain, bool finalized,
        int paidHandCards, bool ignoresEquipmentArmor, DamageNature? damageNature = null, IDamageAttempt? issuedAttack = null)
    {
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var source = view.Players.Single(player => player.Seat == sourceSeat);
        var sourceState = _players[sourceSeat];
        var targetState = _players[targetSeat];
        var sourceVisible = VisiblePredictionSkillIds(source);
        var targetVisible = VisiblePredictionSkillIds(target);
        var fire = damageNature == DamageNature.Fire || damageNature is null &&
            (kind is CardKind.FireSlash or CardKind.FireAttack || !sourceLess &&
                (sourceVisible.Contains("shenwu:zhaohuo") && ShenwuEnabled(sourceState, "shenwu:zhaohuo") ||
                 HasWeaponAbility(sourceState, CardKind.CrimsonSoulZither)));
        if (!finalized && fire && targetVisible.Contains("shenwu:zhaohuo") && ShenwuEnabled(targetState, "shenwu:zhaohuo"))
            return PublicDamagePrediction.Calculate(target.Hp, target.Armor, 0, maxHp: target.MaxHp);
        bool HasVisiblePolicy(CharacterState player, HashSet<string> visible, SkillProgramCardPolicyKind policy) =>
            player.IsAlive && CardPolicies(player, policy).Any(binding => visible.Contains(binding.Source.SkillId));
        var cardLayer = amount;
        var additional = 0;
        if (!finalized && !sourceLess && source.IsAlive)
        {
            if (cardUse && !chain && kind is { } card)
                cardLayer = checked(cardLayer + _turnCardUseEffects.GetDamageModifiers(_turnNumber, _currentSeat,
                    sourceSeat, sourceSeat, card, false).Sum(modifier => modifier.Amount));
            foreach (var instance in GetSkillBindingShard(sourceState).ProgramInstances.Where(instance => sourceVisible.Contains(instance.SkillId)))
            foreach (var modifier in instance.Program.DamageModifiers)
            {
                if (modifier.CapsToAmount) continue;
                var eligible = modifier.SourceScope == SkillProgramDamageModifierSourceScope.DamageSource
                    ? MatchesPerOccurrenceSourceDamageModifier(modifier, !source.HpUndefined, source.Hp,
                        Math.Max(0, source.HandCount - paidHandCards),
                        modifier.Condition == SkillProgramDamageModifierCondition.SourceToTargetDistanceGreaterThan &&
                        sourceSeat != targetSeat ? GetCombatDistance(sourceSeat, targetSeat) : 0, _roundNumber)
                    : modifier.SourceScope == SkillProgramDamageModifierSourceScope.OwnerUsed && cardUse && !chain &&
                        kind is { } effective && ProgramCardKindMatch.MatchesDamageModifier(modifier, effective) && (modifier.Condition switch
                        {
                            SkillProgramDamageModifierCondition.Always => true,
                            SkillProgramDamageModifierCondition.SourceHandBelowLimit =>
                                Math.Max(0, source.HandCount - paidHandCards) < modifier.SourceHandCountLimit,
                            SkillProgramDamageModifierCondition.SourceNotFewerHandAndEquipmentThanTarget =>
                                target.HandCount <= Math.Max(0, source.HandCount - paidHandCards) &&
                                target.Equipment.Count <= source.Equipment.Count,
                            SkillProgramDamageModifierCondition.SourceOutsideTargetAttackRange =>
                                !IsWithinAttackRange(targetSeat, sourceSeat),
                            SkillProgramDamageModifierCondition.OwnerRoundSlashUseOrdinalWithinLimit or
                            SkillProgramDamageModifierCondition.OwnerActualTurnElementalSlashUseOrdinalWithinLimit =>
                                MatchesSlashUseOrdinalModifier(modifier, issuedAttack, sourceSeat, effective),
                            _ => false
                        });
                if (!eligible) continue;
                if (cardUse && modifier.DamageLayer == SkillProgramDamageLayer.CardDamage)
                    cardLayer = checked(cardLayer + modifier.Amount);
                else additional = checked(additional + modifier.Amount);
            }
        }
        if (!finalized)
            additional = checked(additional + ShanheS5JiaoziPublicDamageBonus(view, sourceSeat, targetSeat, sourceLess, paidHandCards));
        cardLayer = Math.Max(0, cardLayer);
        if (!sourceLess && (HasVisiblePolicy(sourceState, sourceVisible, SkillProgramCardPolicyKind.DamageBecomesHpLoss) ||
            sourceSeat != targetSeat && (HasVisiblePolicy(sourceState, sourceVisible, SkillProgramCardPolicyKind.OtherParticipantDamageBecomesHpLoss) ||
                HasVisiblePolicy(targetState, targetVisible, SkillProgramCardPolicyKind.OtherParticipantDamageBecomesHpLoss))))
            return PublicDamagePrediction.Calculate(target.Hp, target.Armor, cardLayer, PublicDamagePaymentKind.HpLoss, target.MaxHp);

        if (!sourceLess && source.IsAlive && cardUse && !chain && !finalized && _roundNumber >= 1 && kind is { } replacementKind && !target.HpUndefined &&
            CardPolicies(sourceState, SkillProgramCardPolicyKind.FirstRoundCardDamageBecomesMaximumHpLoss, replacementKind)
                .Any(binding => sourceVisible.Contains(binding.Source.SkillId) && _skillRuntimeState.GetUsage(sourceSeat,
                    binding.Source.SkillId, MaximumHpReplacementUsage(binding.Policy.Id), SkillUsageScope.Round) == 0))
            return PublicDamagePrediction.Calculate(target.Hp, target.Armor, cardLayer, PublicDamagePaymentKind.MaximumHpLoss, target.MaxHp);

        var full = Math.Max(0, checked(cardLayer + additional));
        if (!finalized && UsesFormalSilverLion && (chain || !ignoresEquipmentArmor) && HasSilverLion(targetState))
            full = Math.Min(1, full);
        if (!finalized && FindPassiveProgramDamageCap(targetState, sourceLess ? null : sourceSeat, targetVisible) is { } damageCap)
            full = Math.Min(damageCap.Cap, full);
        return PublicDamagePrediction.Calculate(target.Hp, target.Armor, full, maxHp: target.MaxHp);
    }

    private static HashSet<string> VisiblePredictionSkillIds(PlayerSnapshot player) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_PublicDamagePrediction.VisiblePredictionSkillIds(player);

    private Func<int, int, int>? CreatePublicSourceOccurrenceDamageBonus(int viewerSeat)
    {
        if (!_contentRegistry.ProgramDependencies.HasSourceDistanceDamageModifiers &&
            !_contentRegistry.ProgramDependencies.HasSourceRoundDamageModifiers) return null;
        // One detached viewer projection is shared by all candidate estimates.
        // No skill IDs or predicates are added to a player-facing snapshot.
        var view = CreateSnapshot(viewerSeat);
        var visibleSkills = view.Players.ToDictionary(player => player.Seat, VisiblePredictionSkillIds);
        return (sourceSeat, targetSeat) =>
        {
            if (!IsValidPlayerSeat(sourceSeat) || !IsValidPlayerSeat(targetSeat) ||
                !_players[sourceSeat].IsAlive || !_players[targetSeat].IsAlive) return 0;
            var modifiers = GetSkillBindingShard(_players[sourceSeat]).ProgramInstances
                .Where(instance => sourceSeat == viewerSeat || visibleSkills[sourceSeat].Contains(instance.SkillId))
                .SelectMany(instance => instance.Program.DamageModifiers)
                .Where(modifier => modifier.SourceScope == SkillProgramDamageModifierSourceScope.DamageSource &&
                    modifier.Condition is SkillProgramDamageModifierCondition.SourceToTargetDistanceGreaterThan or
                        SkillProgramDamageModifierCondition.SourceRoundIsOdd or SkillProgramDamageModifierCondition.SourceRoundIsEven)
                .ToArray();
            var distance = sourceSeat != targetSeat && modifiers.Any(modifier =>
                modifier.Condition == SkillProgramDamageModifierCondition.SourceToTargetDistanceGreaterThan)
                ? GetCombatDistance(sourceSeat, targetSeat) : 0;
            return modifiers.Where(modifier => MatchesPerOccurrenceSourceDamageModifier(modifier, false, 0, 0, distance, _roundNumber))
                .Sum(modifier => modifier.Amount);
        };
    }
}
