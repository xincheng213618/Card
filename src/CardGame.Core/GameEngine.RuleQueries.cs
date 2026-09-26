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

        var outgoing = CollectNumericRuleContributions(source, SkillRuleQuery.OutgoingDistance);
        var incoming = CollectNumericRuleContributions(target, SkillRuleQuery.IncomingDistance);
        return RuleQueryService.EvaluateDirectionalDistance(baseTerms, outgoing, incoming);
    }

    private RuleQueryEvaluation EvaluateAttackRange(CharacterState player, int? excludedEquipmentId = null)
    {
        var baseTerms = new List<RuleQueryBaseTerm>();
        if (_rulesVersion >= 13)
        {
            var weapon = GetEquipment(player).Where(card => card.Id != excludedEquipmentId)
                .SingleOrDefault(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon);
            baseTerms.Add(weapon is null
                ? new RuleQueryBaseTerm($"mode:{_modeDefinition.Id}:attack-range", 1)
                : new RuleQueryBaseTerm(
                    $"equipment:{weapon.Id}:printed-attack-range",
                    EquipmentCatalog.Get(weapon.Kind).WeaponAttackRange ?? 1));
        }
        else
        {
            baseTerms.Add(new RuleQueryBaseTerm($"mode:{_modeDefinition.Id}:attack-range", 1));
            baseTerms.AddRange(GetEquipment(player).Where(card => card.Id != excludedEquipmentId).Select(card => new RuleQueryBaseTerm(
                $"equipment:{card.Id}:attack-range-bonus",
                EquipmentCatalog.Get(card.Kind).AttackRangeBonus)));
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
            new RuleQueryBounds(1, int.MaxValue),
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
        foreach (var card in GetEquipment(player))
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
        AddFiniteContribution(contributions, $"state:{player.Seat}:classic:tianyi:won",
            player.TianyiWonThisTurn ? 1 : 0);
        return RuleQueryService.Evaluate(
            SkillRuleQuery.SlashLimit,
            new RuleQueryBounds(0, int.MaxValue),
            baseTerms,
            contributions);
    }

    private RuleQueryEvaluation EvaluateHandLimit(CharacterState player)
    {
        var woundCount = UsesFormalZhouTai && HasRuntimeSkill(player, SkillKind.Buqu)
            ? GetBuquWounds(player).Count
            : 0;
        var baseTerms = new List<RuleQueryBaseTerm>
        {
            woundCount > 0
                ? new RuleQueryBaseTerm($"state:{player.Seat}:classic:buqu:wounds", woundCount)
                : new RuleQueryBaseTerm($"state:{player.Seat}:current-hp", Math.Max(0, player.Hp))
        };
        if (UsesFormalYuanShao && player.Role == Role.Lord && HasRuntimeSkill(player, SkillKind.Xueyi))
        {
            baseTerms.Add(new RuleQueryBaseTerm(
                $"state:{player.Seat}:classic:xueyi:qun-allies",
                _players.Count(other =>
                    other.IsAlive && other.Seat != player.Seat &&
                    string.Equals(GetEffectiveFactionId(other), "qun", StringComparison.Ordinal)) * 2));
        }

        var contributions = CollectNumericRuleContributions(player, SkillRuleQuery.HandLimit).ToList();
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
                effectiveCardKind));

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
            GetSkillBindingShard(player)?.GetNumericModifiers(query) ?? []);
        return Array.AsReadOnly(programContributions
            .Concat(CollectLegacyNumericRuleContributions(player, query, context.Owner))
            .ToArray());
    }

    private IReadOnlyList<RuleQueryContribution> CollectLegacyNumericRuleContributions(
        CharacterState player,
        SkillRuleQuery query,
        PlayerSkillContext context)
    {
        var skills = EnabledLegacyNumericSkills(player).ToArray();
        var result = new List<RuleQueryContribution>();
        foreach (var skill in skills)
        {
            var value = query switch
            {
                SkillRuleQuery.DrawCount => skill.ModifyDrawCount(context, 0),
                SkillRuleQuery.SlashLimit => skill.ModifySlashLimit(context, 0),
                SkillRuleQuery.OutgoingDistance => skill.ModifyOutgoingDistance(context, 0),
                SkillRuleQuery.IncomingDistance => skill.ModifyIncomingDistance(context, 0),
                _ => 0
            };
            if (value == 0) continue;
            var sourceId = $"legacy-skill:{player.Seat}:{skill.Kind}:{query}";
            if (value == int.MaxValue && query == SkillRuleQuery.SlashLimit)
                result.Add(new UnlimitedRuleQueryContribution(sourceId));
            else
                result.Add(new FiniteRuleQueryContribution(sourceId, SkillRuleOperation.Add, value));
        }
        return result;
    }

    private IEnumerable<IPassiveSkill> EnabledLegacyNumericSkills(CharacterState player)
    {
        if (_contentRegistry is null)
        {
            yield return SkillRegistry.Get(player.General.Skill);
            yield break;
        }

        var emitted = new HashSet<SkillKind>();
        foreach (var definition in GetSkillBindingShard(player)!.Definitions.Values)
        {
            if (definition.LegacyKind is not { } kind || kind == SkillKind.None ||
                definition.Program is { } program && program.MinimumRulesVersion <= _rulesVersion ||
                kind == SkillKind.Yicong && !UsesFormalGongsunZan ||
                !emitted.Add(kind))
                continue;
            yield return SkillRegistry.Get(kind);
        }
    }

    private static void AddFiniteContribution(
        ICollection<RuleQueryContribution> contributions,
        string sourceId,
        int value)
    {
        if (value != 0)
            contributions.Add(new FiniteRuleQueryContribution(sourceId, SkillRuleOperation.Add, value));
    }

    private static int ToLegacyRuleValue(RuleQueryEvaluation evaluation) =>
        evaluation.Value is UnlimitedRuleQueryValue
            ? int.MaxValue
            : ((FiniteRuleQueryValue)evaluation.Value).Value;

    private int GetSlashUseLimit(CharacterState player) =>
        ToLegacyRuleValue(EvaluateSlashUseLimit(player));

    private bool CanSpendSlashUse(CharacterState player, CharacterState target, bool ignoresCount,
        CardKind effectiveKind = CardKind.Slash) =>
        !IsCardUseForbidden(player.Seat, effectiveKind, CardActionType.Use) &&
        !IsDirectedCardTargetProhibited(player.Seat, target.Seat, effectiveKind) &&
        (_phase != TurnPhase.Play || player.Seat != _currentSeat || ignoresCount ||
         _slashCountThisTurn < GetSlashUseLimit(player) ||
         HasDirectedTurnCardPolicy(player.Seat, target.Seat, effectiveKind,
             DirectedTurnCardPolicyEffect.BypassSlashLimit));
}
