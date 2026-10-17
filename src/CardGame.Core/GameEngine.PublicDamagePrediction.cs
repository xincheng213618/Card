namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasPredictionHpPolicy(CharacterState owner, SkillProgramCardPolicyKind kind,
        PlayerSkillContext context) => owner.IsAlive && GetSkillBindingShard(owner).GetCardPolicies(kind)
        .Any(binding => (binding.Policy.OwnerRole is null || HasSkillRoleQualification(owner,
            binding.Source.SkillId, binding.Source.SkillInstanceId, binding.Policy.OwnerRole.Value)) &&
            binding.Policy.Condition.Evaluate(context));

    /// <summary>Pure estimate of the viewer's proposed card and visible table; never issues an attempt or consumes usage.</summary>
    public PublicDamagePrediction PredictPublicActionDamage(int viewerSeat, LegalAction action, int targetSeat)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (!IsValidPlayerSeat(viewerSeat) || !IsValidPlayerSeat(targetSeat))
            throw new ArgumentOutOfRangeException(nameof(viewerSeat));
        var kind = action.PlayedCardKind ?? action.Kind switch
        {
            LegalActionKind.Slash => CardKind.Slash, LegalActionKind.Duel => CardKind.Duel,
            LegalActionKind.BarbarianAssault => CardKind.BarbarianAssault,
            LegalActionKind.ArrowBarrage => CardKind.ArrowBarrage, LegalActionKind.FireAttack => CardKind.FireAttack,
            _ => (CardKind?)null
        };
        var paidHand = action.CardId is { } id && _cardZones.GetLocation(id) == CardLocation.Hand(viewerSeat) ? 1 : 0;
        var amount = kind is { } card && IsSlashCard(card)
            ? checked(1 + (_players[viewerSeat].HasAlcoholEffect ? Math.Max(1, _players[viewerSeat].AlcoholDamageBonus) : 0)) : 1;
        return PredictVisibleDamage(CreateSnapshot(viewerSeat), viewerSeat, targetSeat, kind is null ? 0 : amount,
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
            attack.IsChainPropagation, attack.DamageAmountFinalized, 0, attack.IgnoresArmor);
    }

    private PublicDamagePrediction PredictVisibleDamage(GameSnapshot view, int sourceSeat, int targetSeat,
        int amount, CardKind? kind, bool cardUse, bool sourceLess, bool chain, bool finalized,
        int paidHandCards, bool ignoresEquipmentArmor)
    {
        var target = view.Players.Single(player => player.Seat == targetSeat);
        var source = view.Players.Single(player => player.Seat == sourceSeat);
        var sourceState = _players[sourceSeat];
        var targetState = _players[targetSeat];
        var sourceVisible = VisiblePredictionSkillIds(source);
        var targetVisible = VisiblePredictionSkillIds(target);
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
                    ? modifier.Condition == SkillProgramDamageModifierCondition.SourceHandBelowHp &&
                        !source.HpUndefined && Math.Max(0, source.HandCount - paidHandCards) < source.Hp
                    : modifier.SourceScope == SkillProgramDamageModifierSourceScope.OwnerUsed && cardUse && !chain &&
                        kind is { } effective && modifier.CardKinds.Contains(effective) && (modifier.Condition switch
                        {
                            SkillProgramDamageModifierCondition.Always => true,
                            SkillProgramDamageModifierCondition.SourceHandBelowLimit =>
                                Math.Max(0, source.HandCount - paidHandCards) < modifier.SourceHandCountLimit,
                            SkillProgramDamageModifierCondition.SourceNotFewerHandAndEquipmentThanTarget =>
                                target.HandCount <= Math.Max(0, source.HandCount - paidHandCards) &&
                                target.Equipment.Count <= source.Equipment.Count,
                            SkillProgramDamageModifierCondition.SourceOutsideTargetAttackRange =>
                                !IsWithinAttackRange(targetSeat, sourceSeat),
                            _ => false
                        });
                if (!eligible) continue;
                if (cardUse && modifier.DamageLayer == SkillProgramDamageLayer.CardDamage)
                    cardLayer = checked(cardLayer + modifier.Amount);
                else additional = checked(additional + modifier.Amount);
            }
        }
        cardLayer = Math.Max(0, cardLayer);
        if (!sourceLess && (HasVisiblePolicy(sourceState, sourceVisible, SkillProgramCardPolicyKind.DamageBecomesHpLoss) ||
            sourceSeat != targetSeat && (HasVisiblePolicy(sourceState, sourceVisible, SkillProgramCardPolicyKind.OtherParticipantDamageBecomesHpLoss) ||
                HasVisiblePolicy(targetState, targetVisible, SkillProgramCardPolicyKind.OtherParticipantDamageBecomesHpLoss))))
            return PublicDamagePrediction.Calculate(target.Hp, target.Armor, cardLayer, PublicDamagePaymentKind.HpLoss, target.MaxHp);

        if (!sourceLess && cardUse && !chain && !finalized && _roundNumber >= 1 && kind is { } replacementKind && !target.HpUndefined &&
            CardPolicies(sourceState, SkillProgramCardPolicyKind.FirstRoundCardDamageBecomesMaximumHpLoss, replacementKind)
                .Any(binding => sourceVisible.Contains(binding.Source.SkillId) && _skillRuntimeState.GetUsage(sourceSeat,
                    binding.Source.SkillId, MaximumHpReplacementUsage(binding.Policy.Id), SkillUsageScope.Round) == 0))
            return PublicDamagePrediction.Calculate(target.Hp, target.Armor, cardLayer, PublicDamagePaymentKind.MaximumHpLoss, target.MaxHp);

        var full = Math.Max(0, checked(cardLayer + additional));
        if (!finalized && UsesFormalSilverLion && (chain || !ignoresEquipmentArmor) && HasSilverLion(targetState))
            full = Math.Min(1, full);
        return PublicDamagePrediction.Calculate(target.Hp, target.Armor, full, maxHp: target.MaxHp);
    }

    private static HashSet<string> VisiblePredictionSkillIds(PlayerSnapshot player) =>
        (player.Skills ?? []).Concat(player.SecondarySkills ?? []).Select(skill => skill.Id).ToHashSet(StringComparer.Ordinal);
}
