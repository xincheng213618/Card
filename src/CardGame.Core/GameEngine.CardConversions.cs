namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardConversionSource? _selectedResponseConversion;
    private CardConversionSource? _selectedUseConversion;

    private string DescribeConversion(CardConversionSource? source, string description) =>
        source is null ? description : $"【{_contentRegistry!.Skills[source.SkillId].Name}】{description}";

    private IReadOnlyList<CardConversionSource> GetProgramViewAsConversions(
        PlayerRuntime owner,
        Card card,
        CardKind outputKind,
        bool forResponse)
    {
        if (_rulesVersion < 80 || card.Kind == outputKind ||
            _cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat))
        {
            return [];
        }

        var context = CreateSkillContext(owner);
        return EnabledSkillPrograms(owner)
            .SelectMany(program => program.ViewAs
                .Where(rule => rule.OutputKind == outputKind &&
                               (forResponse ? rule.ForResponse : rule.ForPlay) &&
                               rule.Condition.Evaluate(context) &&
                               (rule.InputKinds.Count == 0 || rule.InputKinds.Contains(card.Kind)) &&
                               (rule.InputSuits.Count == 0 || rule.InputSuits.Contains(card.Suit)))
                .Select(rule => new CardConversionSource(
                    program.Id,
                    rule.Id,
                    owner.Seat,
                    $"seat-{owner.Seat}:{program.Id}")))
            .OrderBy(source => source.SkillId, StringComparer.Ordinal)
            .ThenBy(source => source.BindingId, StringComparer.Ordinal)
            .ThenBy(source => source.SkillInstanceId, StringComparer.Ordinal)
            .ToArray();
    }

    private bool HasLegacyViewAsConversion(
        PlayerRuntime owner,
        Card card,
        CardKind outputKind,
        bool forResponse)
    {
        if (card.Kind == outputKind) return false;
        var context = CreateSkillContext(owner);
        return EnabledPassiveSkills(owner).Any(skill => forResponse
            ? skill.CanUseAsResponse(context, card, outputKind)
            : outputKind == CardKind.Slash && skill.CanUseAsSlash(context, card));
    }

    private IReadOnlyList<CardConversionSource> GetLegacyViewAsConversions(
        PlayerRuntime owner,
        Card card,
        CardKind outputKind,
        bool forResponse)
    {
        if (_rulesVersion < 80 || _contentRegistry is null || card.Kind == outputKind) return [];
        var skillIds = new List<string>();
        void AddGeneral(GeneralDefinition general)
        {
            if (_contentRegistry.Generals.TryGetValue(general.Id, out var definition))
                skillIds.AddRange(definition.SkillIds);
        }
        if (!IsNationalWarMode || owner.GeneralSelected && owner.GeneralRevealed) AddGeneral(owner.General);
        if (IsNationalWarMode && owner.SecondaryGeneralSelected && owner.SecondaryGeneralRevealed &&
            owner.SecondaryGeneral is { } secondary) AddGeneral(secondary);

        var context = CreateSkillContext(owner);
        return skillIds.Distinct(StringComparer.Ordinal)
            .Select(id => _contentRegistry.Skills[id])
            .Where(skill => skill.LegacyKind is { } kind && kind != SkillKind.None)
            .Where(skill =>
            {
                var rules = SkillRegistry.Get(skill.LegacyKind!.Value);
                return forResponse
                    ? rules.CanUseAsResponse(context, card, outputKind)
                    : outputKind == CardKind.Slash && rules.CanUseAsSlash(context, card);
            })
            .Select(skill => new CardConversionSource(
                skill.Id,
                $"legacy:{skill.LegacyKind}",
                owner.Seat,
                $"seat-{owner.Seat}:{skill.Id}"))
            .OrderBy(source => source.SkillId, StringComparer.Ordinal)
            .ThenBy(source => source.BindingId, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<Card> GetSlashUseCards(PlayerRuntime owner)
    {
        if (_rulesVersion < 80) return GetResponseCards(owner, CardKind.Slash);
        var cards = GetPlayableCards(owner).Where(card =>
            IsSlashCard(card.Kind) ||
            GetLegacyViewAsConversions(owner, card, CardKind.Slash, forResponse: false).Count != 0 ||
            GetProgramViewAsConversions(owner, card, CardKind.Slash, forResponse: false).Count != 0);
        if (UsesFormalWushengEquipment)
            cards = cards.Concat(GetEquipment(owner).Where(card => CanUseAsFormalWushengSlash(owner, card)));
        return cards.DistinctBy(card => card.Id).ToArray();
    }

    private static bool IsJijiangUse(JijiangResolution pending) =>
        pending.IsActiveUse || pending.IsBorrowedSwordUse || pending.IsQinglongCrescentBladeUse;

    private IReadOnlyList<Card> GetJijiangSlashCards(JijiangResolution pending, PlayerRuntime provider) =>
        IsJijiangUse(pending) ? GetSlashUseCards(provider) : GetResponseCards(provider, CardKind.Slash);

    private CardKind GetJijiangEffectiveSlashKind(
        JijiangResolution pending,
        PlayerRuntime provider,
        Card card) =>
        IsJijiangUse(pending)
            ? IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash
            : GetEffectiveResponseKind(provider, card, CardKind.Slash);

    private static void AddConversionParameters(
        IDictionary<string, string> parameters,
        CardConversionSource source)
    {
        parameters["conversion-skill-id"] = source.SkillId;
        parameters["conversion-binding-id"] = source.BindingId;
        parameters["conversion-owner-seat"] = source.OwnerSeat.ToString(
            System.Globalization.CultureInfo.InvariantCulture);
        parameters["conversion-instance-id"] = source.SkillInstanceId;
    }

    private IEnumerable<PromptChoice> CreateConversionChoiceVariants(
        PlayerRuntime owner,
        Card card,
        CardKind effectiveKind,
        bool forResponse,
        string baseChoiceId,
        string description,
        IReadOnlyList<int> cards,
        IReadOnlyList<int> targets,
        IReadOnlyDictionary<string, string> baseParameters)
    {
        var programSources = GetProgramViewAsConversions(owner, card, effectiveKind, forResponse);
        var legacySources = GetLegacyViewAsConversions(owner, card, effectiveKind, forResponse);
        var includeUnspecified = card.Kind == effectiveKind ||
            programSources.Count == 0 && legacySources.Count == 0;
        var sources = new List<CardConversionSource?>();
        if (includeUnspecified) sources.Add(null);
        sources.AddRange(legacySources);
        sources.AddRange(programSources);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var parameters = new Dictionary<string, string>(baseParameters);
            if (source is not null) AddConversionParameters(parameters, source);
            var suffix = source is null ? string.Empty : $".conversion-{index}";
            yield return new PromptChoice(
                new ChoiceId(baseChoiceId + suffix), DescribeConversion(source, description), cards, targets, parameters);
        }
    }

    private static bool TryReadConversionSource(
        IReadOnlyDictionary<string, string> parameters,
        out CardConversionSource? source)
    {
        source = null;
        if (!parameters.TryGetValue("conversion-skill-id", out var skillId) ||
            !parameters.TryGetValue("conversion-binding-id", out var bindingId) ||
            !parameters.TryGetValue("conversion-owner-seat", out var ownerSeatText) ||
            !int.TryParse(ownerSeatText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var ownerSeat) ||
            !parameters.TryGetValue("conversion-instance-id", out var instanceId))
        {
            return false;
        }

        source = new CardConversionSource(skillId, bindingId, ownerSeat, instanceId);
        return true;
    }

    private void CaptureSelectedResponseConversion(PromptChoice choice)
    {
        if (TryReadConversionSource(choice.Parameters, out var source))
        {
            _selectedResponseConversion = source;
            return;
        }

        var startsCardAction = choice.Parameters.TryGetValue("response", out var response) &&
                               response is "dodge" or "slash" or "hujia-dodge" or
                                   "jijiang-slash" or "borrowed-sword-slash" ||
                               choice.Parameters.GetValueOrDefault("action") == "qinglong-slash";
        if (startsCardAction) _selectedResponseConversion = null;
    }

    private CardConversionSource? GetSelectedResponseConversion(
        PlayerRuntime provider,
        Card responseCard,
        CardKind effectiveKind)
    {
        var candidates = GetLegacyViewAsConversions(provider, responseCard, effectiveKind, forResponse: true)
            .Concat(GetProgramViewAsConversions(provider, responseCard, effectiveKind, forResponse: true))
            .ToArray();
        if (_selectedResponseConversion is { } selected)
        {
            _selectedResponseConversion = null;
            return candidates.Contains(selected)
                ? selected
                : throw new InvalidOperationException("The selected response conversion is no longer legal.");
        }

        // AI and legacy local-host adapters do not select a serialized choice.
        // Never infer a program when an equally legal legacy conversion exists.
        return candidates.FirstOrDefault();
    }

    private void SelectUseConversion(LegalAction action) =>
        _selectedUseConversion = action.ConversionSource;

    private CardConversionSource? GetSelectedUseConversion(
        PlayerRuntime provider,
        Card card,
        CardKind effectiveKind)
    {
        var candidates = GetLegacyViewAsConversions(provider, card, effectiveKind, forResponse: false)
            .Concat(GetProgramViewAsConversions(provider, card, effectiveKind, forResponse: false))
            .Distinct()
            .ToArray();
        var selected = _selectedUseConversion ?? _selectedResponseConversion;
        _selectedUseConversion = null;
        _selectedResponseConversion = null;
        if (selected is not null)
        {
            return candidates.Contains(selected)
                ? selected
                : throw new InvalidOperationException("The selected use conversion is no longer legal.");
        }

        return candidates.FirstOrDefault();
    }
}
