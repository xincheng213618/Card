using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record PlayConversionDraft(string SkillId, PlayConversionMode? Mode);
    private sealed record PlayConversionMode(CardConversionSource Source, CardKind Kind,
        IReadOnlyList<CardConversionSource> AdditionalSources)
    {
        public bool Matches(LegalAction action) => action.ConversionSource == Source &&
            action.PlayedCardKind == Kind && AdditionalSources.SequenceEqual(action.AdditionalConversionSources ?? []);
        public bool MatchesMode(PlayConversionMode other) => Source == other.Source && Kind == other.Kind &&
            AdditionalSources.SequenceEqual(other.AdditionalSources);
    }

    private PlayConversionDraft? _playConversionSelection;
    private IReadOnlyList<LegalAction>? _conversionActions;
    private IReadOnlyDictionary<string, IReadOnlyList<PlayConversionMode>> _conversionModesBySkill =
        new Dictionary<string, IReadOnlyList<PlayConversionMode>>(StringComparer.Ordinal);
    private bool IsPlayConversionSelectionPending => _playConversionSelection is not null &&
        _snapshot.PendingDecision?.Kind == DecisionKind.PlayCard;

    private SkillProgramViewAs? PlayConversionRule(CardConversionSource? source) => source is not null &&
        _game.GetCardConversionDefinition(source) is { ForPlay: true, InputCount: 1 } rule ? rule : null;

    private IEnumerable<CardConversionSource> PlayConversionSources(LegalAction action) =>
        action.CardId is > 0 && action.PlayedCardKind is not null &&
        action.Kind is not (LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect)
            ? (action.ConversionSource is { } source ? new[] { source } : [])
                .Concat(action.AdditionalConversionSources ?? []).Where(source => PlayConversionRule(source) is not null)
            : [];

    private IReadOnlyDictionary<string, IReadOnlyList<PlayConversionMode>> PlayConversionModesBySkill
    {
        get
        {
            var actions = GetViewLegalActions();
            if (ReferenceEquals(actions, _conversionActions)) return _conversionModesBySkill;
            var bySkill = new Dictionary<string, List<PlayConversionMode>>(StringComparer.Ordinal);
            foreach (var action in actions)
            {
                if (action.ConversionSource is not { } source || action.PlayedCardKind is not { } kind) continue;
                foreach (var skillId in PlayConversionSources(action).Select(item => item.SkillId).Distinct(StringComparer.Ordinal))
                {
                    if (!bySkill.TryGetValue(skillId, out var modes)) bySkill.Add(skillId, modes = []);
                    if (!modes.Any(mode => mode.Matches(action))) modes.Add(new(source, kind, action.AdditionalConversionSources ?? []));
                }
            }
            _conversionActions = actions;
            return _conversionModesBySkill = bySkill.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyList<PlayConversionMode>)pair.Value.ToArray(), StringComparer.Ordinal);
        }
    }

    private IReadOnlyList<PlayConversionMode> PlayConversionModes(string skillId) =>
        PlayConversionModesBySkill.GetValueOrDefault(skillId) ?? [];

    private bool MatchesSelectedPlayConversion(LegalAction action) =>
        action.ConversionSource == _selectedConversionSource &&
        (!IsPlayConversionSelectionPending || _playConversionSelection!.Mode?.Matches(action) == true);

    private bool MatchesSelectedPlayChoice(PromptChoice choice) => GetViewLegalActions().Any(action =>
        MatchesSelectedPlayConversion(action) && action.CardId == choice.Cards.SingleOrDefault() &&
        action.TargetSeats.SequenceEqual(choice.Targets) &&
        ConversionSourceMatchesChoice(action.ConversionSource, choice.Parameters) &&
        AdditionalConversionSourcesMatchChoice(action.AdditionalConversionSources, choice.Parameters));

    private CardKind? SelectedPlayConversionKind => _playConversionSelection?.Mode?.Kind;
    private string PlayConversionSkillName => _playConversionSelection is { } draft
        ? CurrentPresentationRegistry.GetSkill(draft.SkillId).Name : "技能";
    private string PlayConversionCardName => SelectedPlayConversionKind is { } kind ? CardCatalog.Get(kind).DisplayName : "牌";
    private string PlayConversionMaterialName
    {
        get
        {
            var rule = PlayConversionRule(_selectedConversionSource);
            if (rule is null) return "合法材料牌";
            var suits = rule.InputSuits.ToHashSet();
            var suit = suits.SetEquals([Suit.Heart, Suit.Diamond]) ? "红色" : suits.SetEquals([Suit.Spade, Suit.Club]) ? "黑色"
                : suits.Count == 1 ? suits.Single() switch
                { Suit.Heart => "红桃", Suit.Diamond => "方块", Suit.Spade => "黑桃", _ => "梅花" } : string.Empty;
            var kinds = rule.InputKinds.Count > 0 ? string.Join("或", rule.InputKinds.Select(kind => $"【{CardCatalog.Get(kind).DisplayName}】")) : string.Empty;
            var zone = rule.FieldMountConversion == true ? "场上坐骑" : rule.SourceZones.SequenceEqual([CardZoneKind.Hand]) ? "手牌" : rule.SourceZones.SequenceEqual([CardZoneKind.Equipment])
                ? "装备牌" : rule.SourceZones.SequenceEqual([CardZoneKind.Judgment]) ? "判定牌"
                : rule.SourceZones.SequenceEqual([CardZoneKind.Authority]) ? "牌堆中的牌" : "牌";
            return suit + kinds + zone;
        }
    }
    private string PlayConversionSelectionText => _playConversionSelection?.Mode is null
        ? $"已选择【{PlayConversionSkillName}】· 请选择要转换的牌型"
        : $"已选择【{PlayConversionSkillName}】· 请选择{PlayConversionMaterialName}当【{PlayConversionCardName}】使用";

    private bool TrySelectPlayConversion(HumanSkillViewModel skill)
    {
        if (_snapshot.PendingDecision?.Kind != DecisionKind.PlayCard || skill.ContentId is not { } skillId) return false;
        var modes = PlayConversionModes(skillId);
        if (modes.Count == 0) return false;
        var cancel = IsPlayConversionSelectionPending && _playConversionSelection!.SkillId == skillId;
        ClearSelection();
        if (cancel) return true;
        _playConversionSelection = new(skillId, modes.Count == 1 ? modes[0] : null);
        _selectedConversionSource = _playConversionSelection.Mode?.Source;
        _humanSkillSnapshot = null;
        SelectedCardText = PlayConversionSelectionText;
        RefreshCurrentView();
        return true;
    }

    private void AddPlayConversionModeChoices()
    {
        if (!IsPlayConversionSelectionPending || _playConversionSelection!.Mode is not null) return;
        var modes = PlayConversionModes(_playConversionSelection!.SkillId);
        if (modes.Count <= 1) return;
        for (var index = 0; index < modes.Count; index++)
        {
            var mode = modes[index];
            var companions = mode.AdditionalSources.Prepend(mode.Source)
                .Where(source => source.SkillId != _playConversionSelection.SkillId)
                .Select(source => CurrentPresentationRegistry.Skills.GetValueOrDefault(source.SkillId)?.Name)
                .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
            var chainLabel = companions.Length == 0 ? string.Empty : $" · 联用{string.Join("、", companions)}";
            EquipmentPlayChoices.Add(new(new ChoiceId($"conversion-mode-{index}"),
                $"【{PlayConversionSkillName}】当作【{CardCatalog.Get(mode.Kind).DisplayName}】{chainLabel}",
                [], [], new Dictionary<string, string> { ["action"] = "select-play-conversion-mode", ["mode-index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture), ["played-kind"] = mode.Kind.ToString() }));
        }
    }

    private bool TrySelectPlayConversionMode(PromptChoice choice)
    {
        if (choice.Parameters.GetValueOrDefault("action") != "select-play-conversion-mode") return false;
        if (!IsPlayConversionSelectionPending || !int.TryParse(choice.Parameters.GetValueOrDefault("mode-index"), out var index)) return true;
        var modes = PlayConversionModes(_playConversionSelection!.SkillId);
        if (index < 0 || index >= modes.Count) return true;
        _playConversionSelection = _playConversionSelection with { Mode = modes[index] };
        _selectedConversionSource = modes[index].Source;
        _selectedCardId = null;
        _selectedTargetSeat = null;
        _selectedCardTargetSeats.Clear();
        _humanSkillSnapshot = null;
        SelectedCardText = PlayConversionSelectionText;
        RefreshCurrentView();
        return true;
    }

    private IEnumerable<CardSnapshot> PlayMaterials(PlayerSnapshot player) => player.Hand
        .Concat(player.WoodenOxGrain ?? []).Concat(player.Equipment).Concat(player.Judgment).Concat(player.AuthorityCards ?? [])
        .Concat(OwnPublicPileMaterials(player)).Concat(ForeignFieldPlayMaterials(player));

    private IEnumerable<CardSnapshot> ForeignFieldPlayMaterials(PlayerSnapshot player)
    {
        var ids = GetViewLegalActions().Where(action => action.ConversionSource is { } source &&
                _game.GetCardConversionDefinition(source)?.FieldMountConversion == true && action.CardId.HasValue)
            .Select(action => action.CardId!.Value).ToHashSet();
        return _snapshot.Players.Where(other => other.Seat != player.Seat)
            .SelectMany(other => other.Equipment.Concat(other.Judgment)).Where(card => ids.Contains(card.Id));
    }

    private static IEnumerable<CardSnapshot> OwnPublicPileMaterials(PlayerSnapshot player) =>
        player.PublicPersistentPiles is { Count: > 0 } piles
            ? piles.SelectMany(pile => pile.Cards) : player.PublicPersistentPileCards ?? [];
}
