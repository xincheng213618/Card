namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SpGuanYuWushengSkillId = "sp:guan-yu-wusheng";

    private bool IgnoresSpGuanYuWushengDistance(CharacterState player, Card card) =>
        SupportsRuntimeSkillAcquisition &&
        HasRuntimeSkill(player, SpGuanYuWushengSkillId) &&
        card.Suit == Suit.Diamond;

    private NuzhanModifiers GetNuzhanModifiers(long frameId, CharacterState source)
    {
        if (!SupportsRuntimeSkillAcquisition || !HasRuntimeSkill(source, NuzhanSkillId))
            return default;

        var action = _resolutionStack
            .OfType<CardUseFrame>()
            .Single(frame => frame.Id == frameId)
            .Action;
        if (action is null ||
            action.EffectiveKind != CardKind.Slash ||
            action.PhysicalCards.Count != 1 ||
            !action.ConversionChain.Any(conversion =>
                conversion.OwnerSeat == source.Seat &&
                string.Equals(conversion.SkillId, SpGuanYuWushengSkillId, StringComparison.Ordinal)))
        {
            return default;
        }

        var physical = action.PhysicalCards[0];
        var modifiers = new NuzhanModifiers(
            IgnoresSlashLimit: CardCatalog.Get(physical.CardKind).CategoryName == "锦囊牌",
            DamageBonus: EquipmentCatalog.IsEquipment(physical.CardKind) ? 1 : 0,
            PhysicalCardId: physical.CardId);
        if (!modifiers.IgnoresSlashLimit && modifiers.DamageBonus == 0) return default;

        QueueGameEvent(new NuzhanAppliedEvent(
            frameId,
            source.Seat,
            modifiers.PhysicalCardId,
            modifiers.IgnoresSlashLimit,
            modifiers.DamageBonus));
        AddLog(
            "SkillTriggered",
            modifiers.IgnoresSlashLimit
                ? $"{source.Name} 的【怒斩】令此【杀】不计入出牌阶段次数。"
                : $"{source.Name} 的【怒斩】令此【杀】的伤害值+1。",
            source.Seat);
        return modifiers;
    }

    private void AddNuzhanUnlimitedTrickSlashActions(
        ICollection<LegalAction> actions,
        CharacterState actor,
        IReadOnlyList<Card> playableCards)
    {
        if (!SupportsRuntimeSkillAcquisition || !HasRuntimeSkill(actor, NuzhanSkillId)) return;

        foreach (var converted in playableCards.Where(card =>
                     CardCatalog.Get(card.Kind).CategoryName == "锦囊牌"))
        {
            var source = GetLegacyViewAsConversions(
                    actor,
                    converted,
                    CardKind.Slash,
                    forResponse: false)
                .SingleOrDefault(candidate =>
                    string.Equals(candidate.SkillId, SpGuanYuWushengSkillId, StringComparison.Ordinal));
            if (source is null) continue;

            var targets = GetFangtianOrderedSlashTargets(
                actor,
                converted,
                source,
                ignoresSlashLimit: true);
            foreach (var target in targets)
            {
                actions.Add(new LegalAction(
                    LegalActionKind.Slash,
                    converted.Id,
                    target.Seat,
                    DescribeConversion(source,
                        $"将【{converted.DisplayName}】当作【杀】对 {target.Name} 使用（【怒斩】不计次数）"),
                    PlayedCardKind: CardKind.Slash)
                {
                    ConversionSource = source
                });
            }

            AddFangtianHalberdSlashActions(
                actions,
                actor,
                converted,
                targets,
                "杀",
                CardKind.Slash,
                source);
            AddTianyiSlashActions(
                actions,
                actor,
                converted,
                targets,
                "杀",
                CardKind.Slash,
                source);
        }
    }

    private readonly record struct NuzhanModifiers(
        bool IgnoresSlashLimit,
        int DamageBonus,
        int PhysicalCardId);

    private sealed record ProgramCardIdentityMatch(
        SkillProgram Program,
        SkillProgramCardIdentity Identity,
        CardConversionSource Source);

    private CardConversionSource? _selectedResponseConversion;
    private CardConversionSource? _selectedUseConversion;

    private string DescribeConversion(CardConversionSource? source, string description) =>
        source is null ? description : $"【{_contentRegistry!.Skills[source.SkillId].Name}】{description}";

    private IReadOnlyList<ProgramCardIdentityMatch> GetProgramCardIdentityMatches(
        CharacterState owner,
        Card card)
    {
        if (_rulesVersion < 94 || _cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat))
            return [];

        var context = CreateSkillContext(owner);
        return EnabledCardIdentityPrograms(owner)
            .SelectMany(program => program.CardIdentities
                .Where(identity => identity.Zones.Contains(CardZoneKind.Hand) &&
                                   identity.Condition.Evaluate(context) &&
                                   (identity.InputKinds.Count == 0 || identity.InputKinds.Contains(card.Kind)) &&
                                   (identity.InputSuits.Count == 0 || identity.InputSuits.Contains(card.Suit)))
                .Select(identity => new ProgramCardIdentityMatch(
                    program,
                    identity,
                    new CardConversionSource(
                        program.Id,
                        identity.Id,
                        owner.Seat,
                        $"seat-{owner.Seat}:{program.Id}"))))
            .OrderBy(match => match.Source.SkillId, StringComparer.Ordinal)
            .ThenBy(match => match.Source.BindingId, StringComparer.Ordinal)
            .ThenBy(match => match.Source.SkillInstanceId, StringComparer.Ordinal)
            .ToArray();
    }

    private IReadOnlyList<CardConversionSource> GetProgramCardIdentitySources(
        CharacterState owner,
        Card card,
        CardKind effectiveKind,
        bool forResponse)
    {
        var matches = GetProgramCardIdentityMatches(owner, card);
        if (matches.Count == 0) return [];
        return matches
            .Where(match => match.Identity.OutputKind == effectiveKind ||
                            !forResponse && match.Identity.OutputKind == CardKind.Slash &&
                            IsSlashCard(effectiveKind))
            .Select(match => match.Source)
            .ToArray();
    }

    private bool HasProgramCardIdentity(CharacterState owner, Card card) =>
        GetProgramCardIdentityMatches(owner, card).Count != 0;

    private bool IgnoresProgramSlashDistance(
        CharacterState owner,
        CardConversionSource? source)
    {
        if (source is null || source.OwnerSeat != owner.Seat) return false;
        var context = CreateSkillContext(owner);
        return GetSkillBindingShard(owner)!.GetNumericModifiers(SkillRuleQuery.SlashDistanceLimit).Any(binding =>
            binding.Source.SkillId == source.SkillId &&
            binding.Modifier is { } modifier &&
                modifier.Query == SkillRuleQuery.SlashDistanceLimit &&
                modifier.Operation == SkillRuleOperation.Unlimited &&
                modifier.SourceCardIdentityId == source.BindingId &&
                modifier.Condition.Evaluate(context));
    }

    private IReadOnlyList<CardConversionSource> GetProgramViewAsConversions(
        CharacterState owner,
        Card card,
        CardKind outputKind,
        bool forResponse)
    {
        if (_rulesVersion < 80 || card.Kind == outputKind ||
            HasProgramCardIdentity(owner, card) ||
            _cardZones.GetLocation(card.Id) != CardLocation.Hand(owner.Seat))
        {
            return [];
        }

        var context = CreateSkillContext(owner);
        var configured = EnabledViewAsPrograms(owner)
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
            .ToArray();
        var turnScoped = forResponse
            ? Array.Empty<CardConversionSource>()
            : _turnCardUseEffects.GetConversions(
                    _turnNumber,
                    _currentSeat,
                    owner.Seat,
                    outputKind,
                    IsRedSuit(EffectiveSuit(owner, card)))
                .Select(item => new CardConversionSource(
                    item.Source.SkillId,
                    $"{item.Source.BindingId}.turn-{item.EffectIndex}",
                    item.Source.OwnerSeat,
                    item.Source.SkillInstanceId))
                .ToArray();
        return configured.Concat(turnScoped)
            .Distinct()
            .OrderBy(source => source.SkillId, StringComparer.Ordinal)
            .ThenBy(source => source.BindingId, StringComparer.Ordinal)
            .ThenBy(source => source.SkillInstanceId, StringComparer.Ordinal)
            .ToArray();
    }

    private bool HasLegacyViewAsConversion(
        CharacterState owner,
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
        CharacterState owner,
        Card card,
        CardKind outputKind,
        bool forResponse)
    {
        if (_rulesVersion < 80 || _contentRegistry is null || card.Kind == outputKind) return [];
        var context = CreateSkillContext(owner);
        return EnabledContentSkillIds(owner)
            .Select(id => _contentRegistry.Skills[id])
            .Where(skill => skill.LegacyKind is { } kind && kind != SkillKind.None &&
                            skill.Program?.UsesCompositionKernel != true)
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

    private IReadOnlyList<Card> GetSlashUseCards(CharacterState owner)
    {
        if (SlashKinds.All(kind => IsCardUseForbidden(owner.Seat, kind, CardActionType.Use))) return [];
        if (_rulesVersion < 80) return GetResponseCards(owner, CardKind.Slash);
        var cards = GetPlayableCards(owner).Where(card =>
        {
            if (IsQianxiHandCardRestricted(owner, card)) return false;
            var identities = GetProgramCardIdentityMatches(owner, card);
            return identities.Count != 0
                ? identities.Any(match => match.Identity.OutputKind == CardKind.Slash)
                : IsSlashCard(card.Kind) ||
                  GetLegacyViewAsConversions(owner, card, CardKind.Slash, forResponse: false).Count != 0 ||
                  GetProgramViewAsConversions(owner, card, CardKind.Slash, forResponse: false).Count != 0;
        });
        if (UsesFormalWushengEquipment)
            cards = cards.Concat(GetEquipment(owner).Where(card => CanUseAsFormalWushengSlash(owner, card)));
        return cards.DistinctBy(card => card.Id).ToArray();
    }

    private static bool IsJijiangUse(JijiangResolution pending) =>
        pending.IsActiveUse || pending.IsBorrowedSwordUse || pending.IsQinglongCrescentBladeUse;

    private IReadOnlyList<Card> GetJijiangSlashCards(JijiangResolution pending, CharacterState provider) =>
        IsJijiangUse(pending) ? GetSlashUseCards(provider) : GetResponseCards(provider, CardKind.Slash);

    private CardKind GetJijiangEffectiveSlashKind(
        JijiangResolution pending,
        CharacterState provider,
        Card card)
    {
        var identity = GetProgramCardIdentityMatches(provider, card).FirstOrDefault();
        if (identity is not null) return identity.Identity.OutputKind;
        return IsJijiangUse(pending)
            ? IsSlashCard(card.Kind) ? card.Kind : CardKind.Slash
            : GetEffectiveResponseKind(provider, card, CardKind.Slash);
    }

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
        CharacterState owner,
        Card card,
        CardKind effectiveKind,
        bool forResponse,
        string baseChoiceId,
        string description,
        IReadOnlyList<int> cards,
        IReadOnlyList<int> targets,
        IReadOnlyDictionary<string, string> baseParameters)
    {
        var hasIdentity = HasProgramCardIdentity(owner, card);
        var identitySources = GetProgramCardIdentitySources(owner, card, effectiveKind, forResponse);
        var programSources = GetProgramViewAsConversions(owner, card, effectiveKind, forResponse);
        var legacySources = GetLegacyViewAsConversions(owner, card, effectiveKind, forResponse);
        var includeUnspecified = !hasIdentity &&
            (card.Kind == effectiveKind || programSources.Count == 0 && legacySources.Count == 0);
        var sources = new List<CardConversionSource?>();
        if (includeUnspecified) sources.Add(null);
        if (hasIdentity) sources.AddRange(identitySources);
        else
        {
            sources.AddRange(legacySources);
            sources.AddRange(programSources);
        }
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
        CharacterState provider,
        Card responseCard,
        CardKind effectiveKind)
    {
        var hasIdentity = HasProgramCardIdentity(provider, responseCard);
        var identitySources = GetProgramCardIdentitySources(
            provider, responseCard, effectiveKind, forResponse: true);
        var candidates = hasIdentity
            ? identitySources.ToArray()
            : GetLegacyViewAsConversions(provider, responseCard, effectiveKind, forResponse: true)
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
        CharacterState provider,
        Card card,
        CardKind effectiveKind)
    {
        var hasIdentity = HasProgramCardIdentity(provider, card);
        var identitySources = GetProgramCardIdentitySources(
            provider, card, effectiveKind, forResponse: false);
        var candidates = hasIdentity
            ? identitySources.ToArray()
            : GetLegacyViewAsConversions(provider, card, effectiveKind, forResponse: false)
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
