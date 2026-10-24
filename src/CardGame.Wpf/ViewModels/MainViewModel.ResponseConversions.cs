using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private sealed record ResponseConversionMode(CardConversionSource Source, CardKind Kind, int Count)
    {
        public bool Matches(PromptChoice choice) => choice.Cards.Count == Count &&
            TryReadConversionSource(choice.Parameters, out var source) && source == Source;
    }

    private sealed record ResponseConversionDraft(string SkillId, ResponseConversionMode? Mode);
    private ResponseConversionDraft? _responseConversionSelection;
    private IReadOnlyList<ResponseConversionMode> _responseConversionOfferedModes = [];
    private IReadOnlyList<PromptChoice> _responseConversionModeChoices = [];
    private GameSnapshot? _responseConversionSnapshot;
    private IReadOnlyList<ResponseConversionMode> _responseConversionModes = [];

    private bool IsResponseConversionSelectionPending => IsHandResponsePending && _responseConversionSelection is not null;
    private bool IsResponseConversionPaymentPending => IsResponseConversionSelectionPending && _responseConversionSelection!.Mode is not null;

    // Only the viewer's published hand/equipment payments enter this draft.
    // Public-pile and special response operations retain their own controls.
    private SkillProgramViewAs? ResponseConversionRule(PromptChoice choice)
    {
        if (choice.Cards.Count == 0 || !TryReadConversionSource(choice.Parameters, out var source) ||
            _game.GetCardConversionDefinition(source) is not { ForResponse: true } rule ||
            rule.SourceZones.Any(zone => zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))) return null;
        var human = _snapshot.Players.Single(player => player.IsHuman);
        return choice.Cards.All(id => human.Hand.Concat(human.Equipment).Concat(human.WoodenOxGrain ?? []).Any(card => card.Id == id))
            ? rule : null;
    }

    private IReadOnlyList<ResponseConversionMode> ResponseConversionModes
    {
        get
        {
            if (ReferenceEquals(_responseConversionSnapshot, _snapshot)) return _responseConversionModes;
            _responseConversionSnapshot = _snapshot;
            return _responseConversionModes = !IsHandResponsePending || IsBaguaActivationPending ? [] :
                _snapshot.PendingDecision!.Choices.Where(choice => ResponseConversionRule(choice) is not null)
                    .Select(choice => { TryReadConversionSource(choice.Parameters, out var source);
                        return new ResponseConversionMode(source, ResponseConversionRule(choice)!.OutputKind, choice.Cards.Count); })
                    .Distinct().ToArray();
        }
    }

    private IEnumerable<PromptChoice> AvailableHandResponseChoices => _snapshot.PendingDecision!.Choices.Where(choice =>
        IsResponseConversionSelectionPending
            ? choice.Cards.Count == 0 || _responseConversionSelection!.Mode?.Matches(choice) == true
            : ResponseConversionRule(choice) is null);

    private bool TrySelectResponseConversion(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not { } id) return false;
        var modes = ResponseConversionModes.Where(mode => mode.Source.SkillId == id).ToArray();
        if (modes.Length == 0) return false;
        var cancel = _responseConversionSelection?.SkillId == id;
        ClearSelection();
        if (cancel) return true;
        _responseConversionSelection = new(id, modes.Length == 1 ? modes[0] : null);
        SetResponseConversionModeChoices(modes);
        RefreshResponseConversionDraft();
        return true;
    }

    private void SetResponseConversionModeChoices(ResponseConversionMode[] modes)
    {
        _responseConversionOfferedModes = modes;
        _responseConversionModeChoices = modes.Length == 1 ? [] : modes.Select((mode, index) =>
            new PromptChoice(new ChoiceId($"response-conversion-mode-{index}"),
                $"{mode.Count}张牌当【{CardCatalog.Get(mode.Kind).DisplayName}】", [], [],
                new Dictionary<string, string> { ["action"] = "select-response-conversion-mode", ["mode-index"] = index.ToString(System.Globalization.CultureInfo.InvariantCulture) })).ToArray();
    }

    private bool TrySelectResponseConversionMode(PromptChoice choice)
    {
        if (choice.Parameters.GetValueOrDefault("action") != "select-response-conversion-mode") return false;
        if (!_responseConversionModeChoices.Any(current => ReferenceEquals(current, choice)) ||
            _responseConversionSelection is not { } draft ||
            !int.TryParse(choice.Parameters.GetValueOrDefault("mode-index"), out var index)) return true;
        if (index < 0 || index >= _responseConversionOfferedModes.Count ||
            !ResponseConversionModes.Contains(_responseConversionOfferedModes[index])) return true;
        _responseConversionSelection = draft with { Mode = _responseConversionOfferedModes[index] };
        _responseConversionModeChoices = [];
        RefreshResponseConversionDraft();
        return true;
    }

    private void RefreshResponseConversionDraft()
    {
        _paymentSnapshot = null;
        _humanSkillSnapshot = null;
        _selectedCardId = null;
        _paymentCardIds.Clear();
        _paymentChoiceId = null;
        SelectedCardText = ResponseConversionSelectionText;
        RefreshEquipmentMaterialSelection();
        RefreshCurrentView();
    }

    private string ResponseConversionSelectionText => _responseConversionSelection is { } draft
        ? $"已选择【{CurrentPresentationRegistry.GetSkill(draft.SkillId).Name}】· " + (draft.Mode is { } mode
            ? $"选择{mode.Count}张合法牌当【{CardCatalog.Get(mode.Kind).DisplayName}】；确认后发动"
            : "请选择转化方式，再选择材料牌") : string.Empty;

    private void ResetResponseConversionSelection()
    {
        _responseConversionSelection = null;
        _responseConversionModeChoices = [];
        _responseConversionOfferedModes = [];
        _paymentSnapshot = null;
    }

    private void SyncResponseConversionSelection()
    {
        if (_responseConversionSelection is not { } draft) return;
        var modes = ResponseConversionModes.Where(mode => mode.Source.SkillId == draft.SkillId).ToArray();
        if (modes.Length == 0 || draft.Mode is { } selected && !modes.Contains(selected))
        {
            ResetResponseConversionSelection();
            _paymentCardIds.Clear();
            _paymentChoiceId = null;
            _humanSkillSnapshot = null;
        }
        else if (draft.Mode is null && !_responseConversionOfferedModes.SequenceEqual(modes))
        {
            if (modes.Length == 1)
            {
                _responseConversionSelection = draft with { Mode = modes[0] };
                _paymentSnapshot = null;
            }
            SetResponseConversionModeChoices(modes);
        }
    }
}
