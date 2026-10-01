namespace CardGame.Core;

public sealed partial class GameEngine
{
    private int GetLivingFactionCount() =>
        _players
            .Where(player => player.IsAlive)
            .Select(GetEffectiveFactionId)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Count();

    private int GetLivingPlayersMinHp() =>
        _players
            .Where(player => player.IsAlive)
            .Select(player => player.Hp)
            .Min();

    private RuleQueryEvaluation EvaluateDistance(CharacterState source, CharacterState target,
        int? excludedEquipmentId = null)
    {
        var baseTerms = new List<RuleQueryBaseTerm>
        {
            new($"mode:{_modeDefinition.Id}:alive-seat-distance", GetAliveSeatDistance(source.Seat, target.Seat))
        };
        baseTerms.AddRange(GetEquipment(source).Where(card => card.Id != excludedEquipmentId).Select(card => new RuleQueryBaseTerm(
            $"equipment:{card.Id}:outgoing-distance",
            EquipmentCatalog.Get(card.Kind).OutgoingDistanceModifier)));
        baseTerms.AddRange(GetEquipment(target).Where(card => card.Id != excludedEquipmentId).Select(card => new RuleQueryBaseTerm(
            $"equipment:{card.Id}:incoming-distance",
            EquipmentCatalog.Get(card.Kind).IncomingDistanceModifier)));

        var outgoing = CollectNumericRuleContributions(source, SkillRuleQuery.OutgoingDistance).ToList();
        outgoing.AddRange(_turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, source.Seat, SkillRuleQuery.OutgoingDistance)
            .Select(item => new FiniteRuleQueryContribution(
                $"turn:{item.TurnNumber}:{item.Source.SkillId}:{item.Source.BindingId}:{item.GrantSequence}", SkillRuleOperation.Add, item.Amount)));
        var incoming = CollectNumericRuleContributions(target, SkillRuleQuery.IncomingDistance);
        return RuleQueryService.EvaluateDirectionalDistance(baseTerms, outgoing, incoming);
    }

    private RuleQueryEvaluation EvaluateAttackRange(CharacterState player, int? excludedEquipmentId = null)
    {
        var baseTerms = new List<RuleQueryBaseTerm>();
        var minimumRange = 1;
        {
            var weapon = GetEquipment(player).Where(card => card.Id != excludedEquipmentId)
                .Where(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon)
                .MaxBy(card => GetWeaponAttackRange(player, card));
            if (weapon is { Kind: CardKind.RedBloodBlade } && GetWeaponAttackRange(player, weapon) == 0)
                minimumRange = 0;
            baseTerms.Add(weapon is null
                ? new RuleQueryBaseTerm($"mode:{_modeDefinition.Id}:attack-range", 1)
                : new RuleQueryBaseTerm(
                    $"equipment:{weapon.Id}:printed-attack-range",
                    GetWeaponAttackRange(player, weapon)));
        }

        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.AttackRange).ToList();
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));
        contributions.AddRange(_turnCardUseEffects
            .GetRuleModifiers(_turnNumber, _currentSeat, player.Seat, SkillRuleQuery.AttackRange)
            .Where(item => item.Operation == SkillRuleOperation.Unlimited)
            .Select(item => new UnlimitedRuleQueryContribution(
                $"turn:{item.TurnNumber}:{item.Source.SkillId}:{item.Source.BindingId}:{item.GrantSequence}")));
        return RuleQueryService.Evaluate(
            SkillRuleQuery.AttackRange,
            new RuleQueryBounds(minimumRange, int.MaxValue),
            baseTerms,
            contributions);
    }

    private RuleQueryEvaluation EvaluateDrawCount(CharacterState player)
    {
        var baseTerms = new List<RuleQueryBaseTerm>
        {
            new($"mode:{_modeDefinition.Id}:draw-count", _drawPerTurn)
        };
        baseTerms.AddRange(GetEquipment(player)
            .Where(card => EquipmentCatalog.Get(card.Kind).DrawCountBonus != 0)
            .Select(card => new RuleQueryBaseTerm(
                $"equipment:{card.Id}:draw-count",
                EquipmentCatalog.Get(card.Kind).DrawCountBonus)));
        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.DrawCount).ToList();
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));
        AddFiniteContribution(contributions, $"turn:{player.Seat}:draw-count", GetAdditiveTurnRuleModifier(player.Seat, SkillRuleQuery.DrawCount));
        return RuleQueryService.Evaluate(
            SkillRuleQuery.DrawCount,
            new RuleQueryBounds(0, int.MaxValue),
            baseTerms,
            contributions);
    }

    private RuleQueryEvaluation EvaluateSlashUseLimit(CharacterState player)
    {
        var baseTerms = new[]
        {
            new RuleQueryBaseTerm($"mode:{_modeDefinition.Id}:slash-use-limit", 1)
        };
        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.SlashLimit).ToList();
        if (HasNextUnlimitedCard(player)) contributions.Add(new UnlimitedRuleQueryContribution($"skill:{player.Seat}:next-card-unlimited"));
        foreach (var card in GetEquipmentRuleCards(player))
        {
            var bonus = EquipmentCatalog.Get(card.Kind).SlashLimitBonus;
            if (bonus == int.MaxValue)
                contributions.Add(new UnlimitedRuleQueryContribution($"equipment:{card.Id}:slash-use-limit"));
            else
                AddFiniteContribution(contributions, $"equipment:{card.Id}:slash-use-limit", bonus);
        }
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));
        AddFiniteContribution(contributions, $"turn:{player.Seat}:slash-limit",
            GetAdditiveTurnRuleModifier(player.Seat, SkillRuleQuery.SlashLimit));
        return RuleQueryService.Evaluate(
            SkillRuleQuery.SlashLimit,
            new RuleQueryBounds(0, int.MaxValue),
            baseTerms,
            contributions);
    }

    private RuleQueryEvaluation EvaluateHandLimit(CharacterState player)
    {
        var woundCount = HasProgramSkill(player, "classic:buqu")
            ? GetBuquWounds(player).Count
            : 0;
        var baseTerms = new List<RuleQueryBaseTerm>
        {
            woundCount > 0
                ? new RuleQueryBaseTerm($"state:{player.Seat}:classic:buqu:wounds", woundCount)
                : new RuleQueryBaseTerm($"state:{player.Seat}:current-hp", Math.Max(0, player.Hp))
        };
        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.HandLimit).ToList();
        contributions.AddRange(ProgramDamageHandLimitContributions(player));
        contributions.AddRange(PersistentHandLimitContributions(player));
        AddFiniteContribution(contributions, $"turn:{player.Seat}:hand-limit",
            GetAdditiveTurnRuleModifier(player.Seat, SkillRuleQuery.HandLimit));
        foreach (var (source, policy) in CardPolicies(player,
                     SkillProgramCardPolicyKind.FactionHandLimitBonus))
        {
            var allies = _players.Count(other => other.IsAlive && other.Seat != player.Seat &&
                string.Equals(GetEffectiveFactionId(other), policy.FactionId, StringComparison.Ordinal));
            AddFiniteContribution(contributions,
                $"skill:{source.SkillId}:{source.SkillInstanceId}:policy:{policy.Id}",
                checked(allies * policy.Value));
        }
        AddFiniteContribution(contributions, $"state:{player.Seat}:hengye:growth", GetHengyeGrowth(player));
        return RuleQueryService.Evaluate(
            SkillRuleQuery.HandLimit,
            new RuleQueryBounds(0, int.MaxValue),
            baseTerms,
            contributions);
    }

    private RuleQueryEvaluation EvaluateCardTargetCount(
        CharacterState player,
        CardKind effectiveCardKind) =>
        RuleQueryService.Evaluate(
            SkillRuleQuery.CardTargetCount,
            new RuleQueryBounds(1, int.MaxValue),
            [new RuleQueryBaseTerm($"card:{effectiveCardKind}:base-target-count", 1)],
            CollectNumericRuleContributions(
                player,
                SkillRuleQuery.CardTargetCount,
                effectiveCardKind).Concat(
                _turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, player.Seat,
                        SkillRuleQuery.CardTargetCount, effectiveCardKind)
                    .Where(item => item.Operation == SkillRuleOperation.Add)
                    .Select(item => (RuleQueryContribution)new FiniteRuleQueryContribution(
                        $"turn:{item.GrantSequence}:card-target-count", SkillRuleOperation.Add, item.Amount))).ToArray());

    private int GetCardUseDistanceLimit(CharacterState player, CardKind effectiveCardKind) =>
        ConvertRuleValue(RuleQueryService.Evaluate(
            SkillRuleQuery.CardUseDistanceLimit,
            new RuleQueryBounds(1, int.MaxValue),
            [new RuleQueryBaseTerm($"card:{effectiveCardKind}:base-use-distance", 1)],
            CollectNumericRuleContributions(player, SkillRuleQuery.CardUseDistanceLimit,
                effectiveCardKind)));

    private IReadOnlyList<RuleQueryContribution> CollectNumericRuleContributions(
        CharacterState player,
        SkillRuleQuery query,
        CardKind? effectiveCardKind = null)
    {
        var context = new SkillProgramRuleContext(
            CreateSkillContext(player),
            GetLivingFactionCount(),
            zone => _cardZones.Count(new CardLocation(zone, player.Seat)),
            effectiveCardKind);
        var programContributions = SkillProgramRules.CollectIndexedContributions(
            query,
            context,
            GetSkillBindingShard(player).GetNumericModifiers(query));
        return programContributions.Concat(CollectStrategicRuleContributions(player, query)).ToArray();
    }

    private static void AddFiniteContribution(
        ICollection<RuleQueryContribution> contributions,
        string sourceId,
        int value)
    {
        if (value != 0)
            contributions.Add(new FiniteRuleQueryContribution(sourceId, SkillRuleOperation.Add, value));
    }

    private static int ConvertRuleValue(RuleQueryEvaluation evaluation) =>
        evaluation.Value is UnlimitedRuleQueryValue
            ? int.MaxValue
            : ((FiniteRuleQueryValue)evaluation.Value).Value;

    private int GetSlashUseLimit(CharacterState player) =>
        ConvertRuleValue(EvaluateSlashUseLimit(player));

    private bool CanSpendSlashUse(CharacterState player, CharacterState target, bool ignoresCount,
        CardKind effectiveKind = CardKind.Slash, Card? physicalCard = null) =>
        !IsCardUseForbidden(player.Seat, effectiveKind, CardActionType.Use) &&
        (physicalCard is null || !HasBeneficiarySuitShield(player.Seat, target.Seat, EffectiveSuit(player, physicalCard))) &&
        !IsDirectedCardTargetProhibited(player.Seat, target.Seat, effectiveKind) &&
        (_phase != TurnPhase.Play || player.Seat != _currentSeat || ignoresCount ||
         _slashCountThisTurn < GetSlashUseLimit(player) ||
         physicalCard is not null && BypassesSlashLimitBySuit(player, physicalCard, effectiveKind) ||
         HasDirectedTurnCardPolicy(player.Seat, target.Seat, effectiveKind,
             DirectedTurnCardPolicyEffect.BypassSlashLimit));
}
