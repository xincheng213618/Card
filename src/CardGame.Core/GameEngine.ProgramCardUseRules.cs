namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record SlashUseVariant(
        CardKind EffectiveKind,
        CardConversionSource? AdditionalConversionSource,
        bool UsesZhuqueFan);

    private IReadOnlyList<SlashUseVariant> GetSlashUseVariants(
        CharacterState actor,
        CardKind baseEffectiveKind)
    {
        var variants = new List<SlashUseVariant>
        {
            new(baseEffectiveKind, AdditionalConversionSource: null, UsesZhuqueFan: false)
        };
        if (baseEffectiveKind == CardKind.Slash && HasZhuqueFan(actor))
        {
            variants.Add(new SlashUseVariant(
                CardKind.FireSlash,
                AdditionalConversionSource: null,
                UsesZhuqueFan: true));
        }
        return variants;
    }

    private IReadOnlyList<SlashUseVariant> GetSlashUseVariants(
        CharacterState actor,
        Card physicalCard,
        CardKind baseEffectiveKind,
        CardConversionSource? primaryConversionSource)
    {
        var variants = GetSlashUseVariants(actor, baseEffectiveKind).ToList();
        if (primaryConversionSource is null || baseEffectiveKind != CardKind.Slash)
        {
            return variants;
        }

        variants.AddRange(GetProgramChainedViewAsConversions(
                actor,
                physicalCard,
                baseEffectiveKind,
                CardKind.FireSlash,
                primaryConversionSource,
                forResponse: false)
            .Select(source => new SlashUseVariant(
                CardKind.FireSlash,
                source,
                UsesZhuqueFan: false)));
        return variants
            .DistinctBy(variant => (variant.EffectiveKind, variant.AdditionalConversionSource, variant.UsesZhuqueFan))
            .ToArray();
    }

    private IReadOnlyList<CardConversionSource> GetProgramChainedViewAsConversions(
        CharacterState owner,
        Card physicalCard,
        CardKind inputKind,
        CardKind outputKind,
        CardConversionSource primaryConversionSource,
        bool forResponse)
    {
        if (inputKind == outputKind)
        {
            return [];
        }

        var location = _cardZones.GetLocation(physicalCard.Id);
        var zone = location == CardLocation.Hand(owner.Seat)
            ? CardZoneKind.Hand
            : location == CardLocation.Equipment(owner.Seat)
                ? CardZoneKind.Equipment
                : (CardZoneKind?)null;
        if (zone is null) return [];

        var context = CreateSkillContext(owner);
        return GetSkillBindingShard(owner).ProgramInstances
            .Where(instance => instance.Program.ViewAs.Count != 0)
            .SelectMany(instance => instance.Program.ViewAs
                .Where(rule => rule.AllowChainedInput &&
                               rule.InputCount == 1 &&
                               rule.OutputKind == outputKind &&
                               rule.SourceZones.Contains(zone.Value) &&
                               (forResponse ? rule.ForResponse : rule.ForPlay) &&
                               rule.Condition.Evaluate(context) &&
                               (rule.InputKinds.Count == 0 || rule.InputKinds.Contains(inputKind)) &&
                               (rule.InputCategories.Count == 0 ||
                                rule.InputCategories.Contains(GetProgramCardCategory(inputKind))) &&
                               (rule.InputSuits.Count == 0 || rule.InputSuits.Contains(physicalCard.Suit)))
                .Select(rule => new CardConversionSource(
                    instance.SkillId,
                    rule.Id,
                    owner.Seat,
                    instance.SkillInstanceId)))
            .Where(source => source != primaryConversionSource)
            .Distinct()
            .OrderBy(source => source.SkillId, StringComparer.Ordinal)
            .ThenBy(source => source.BindingId, StringComparer.Ordinal)
            .ThenBy(source => source.SkillInstanceId, StringComparer.Ordinal)
            .ToArray();
    }

    private string DescribeAdditionalConversion(CardConversionSource source) =>
        _contentRegistry.Skills.TryGetValue(source.SkillId, out var skill)
            ? skill.Name
            : source.SkillId;

    private void AddProgramTargetCountSlashActions(
        ICollection<LegalAction> actions,
        CharacterState actor,
        Card physicalCard,
        IReadOnlyList<CharacterState> legalTargets,
        string slashName,
        CardKind? playedCardKind,
        CardConversionSource? conversionSource = null,
        IReadOnlyList<CardConversionSource>? additionalConversionSources = null,
        SkillKind? cardKindModifierSkill = null)
    {
        var effectiveKind = playedCardKind ?? physicalCard.Kind;
        var targetCountRule = EvaluateCardTargetCount(actor, effectiveKind);
        var configuredMaximum = ((FiniteRuleQueryValue)targetCountRule.Value).Value;
        var programBonus = configuredMaximum - 1;
        if (programBonus <= 0) return;

        var usesFangtian = UsesFormalFangtianHalberd &&
                           GetHand(actor).Count == 1 &&
                           GetHand(actor)[0].Id == physicalCard.Id &&
                           GetEquipment(actor).Any(card => card.Kind == CardKind.FangtianHalberd);
        var existingMaximum = usesFangtian ? 3 : 1;
        var maximum = Math.Min(existingMaximum + programBonus, legalTargets.Count);
        if (maximum <= existingMaximum) return;

        var orderedTargets = legalTargets
            .OrderBy(player => (player.Seat - actor.Seat + _playerCount) % _playerCount)
            .ToArray();
        var skillNames = GetSkillBindingShard(actor)!
            .GetNumericModifiers(SkillRuleQuery.CardTargetCount)
            .Where(binding => binding.Modifier.CardKinds.Contains(effectiveKind) &&
                              binding.Modifier.Condition.Evaluate(CreateSkillContext(actor)))
            .Select(binding => _contentRegistry!.Skills[binding.Source.SkillId].Name)
            .Concat(_turnCardUseEffects.GetRuleModifiers(_turnNumber, _currentSeat, actor.Seat,
                    SkillRuleQuery.CardTargetCount, effectiveKind)
                .Select(item => item.Source.SkillId is { } skillId &&
                    _contentRegistry!.Skills.TryGetValue(skillId, out var skill)
                    ? skill.Name : item.Source.SkillId ?? "回合加成"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        for (var targetCount = existingMaximum + 1; targetCount <= maximum; targetCount++)
        {
            AddCombinations(0, []);

            void AddCombinations(int startIndex, IReadOnlyList<CharacterState> selected)
            {
                if (selected.Count == targetCount)
                {
                    var targetSeats = Array.AsReadOnly(selected.Select(target => target.Seat).ToArray());
                    var combinedEffects = new List<string>();
                    if (usesFangtian) combinedEffects.Add("方天画戟");
                    combinedEffects.AddRange(skillNames);
                    actions.Add(new LegalAction(
                        LegalActionKind.Slash,
                        physicalCard.Id,
                        targetSeats[0],
                        DescribeConversion(
                            conversionSource,
                            $"发动【{string.Join("】【", combinedEffects)}】，以【{slashName}】指定 {string.Join("、", selected.Select(target => target.Name))}"),
                        PlayedCardKind: playedCardKind,
                        TargetSeats: targetSeats)
                    {
                        ConversionSource = conversionSource,
                        AdditionalConversionSources = additionalConversionSources,
                        CardKindModifierSkill = cardKindModifierSkill
                    });
                    return;
                }

                for (var index = startIndex;
                     index <= orderedTargets.Length - (targetCount - selected.Count);
                     index++)
                {
                    AddCombinations(index + 1, [.. selected, orderedTargets[index]]);
                }
            }
        }
    }

    private bool UsesProgramCardTargetCount(
        CharacterState source,
        Card physicalCard,
        CardKind effectiveKind,
        int targetCount)
    {
        var usesFangtian = UsesFormalFangtianHalberd &&
                           GetHand(source).Count == 1 &&
                           GetHand(source)[0].Id == physicalCard.Id &&
                           GetEquipment(source).Any(card => card.Kind == CardKind.FangtianHalberd);
        var existingMaximum = usesFangtian ? 3 : 1;
        return targetCount > existingMaximum &&
               ((FiniteRuleQueryValue)EvaluateCardTargetCount(source, effectiveKind).Value).Value > 1;
    }
}
