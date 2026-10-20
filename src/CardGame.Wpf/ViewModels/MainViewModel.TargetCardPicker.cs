using System.Globalization;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record TargetCardRegionViewModel(CardZoneKind Zone, int OwnerSeat, string Label,
    string OwnerName, string Hint, IReadOnlyList<PromptChoice> Choices, ICommand Command, bool IsPublicPlay);

public sealed partial class MainViewModel
{
    private IReadOnlyList<TargetCardRegionViewModel> _targetCardRegions = [];
    private IReadOnlyList<PromptChoice> _ungroupedCenterSkillChoices = [];
    public IReadOnlyList<TargetCardRegionViewModel> TargetCardRegions => _targetCardRegions;
    public IReadOnlyList<PromptChoice> UngroupedCenterSkillChoices => _ungroupedCenterSkillChoices;
    public bool HasTargetCardRegions => TargetCardRegions.Count > 0;
    public bool HasTargetCardPicker => HasTargetCardRegions || IsTargetCardSelectionPending;
    public string TargetCardPickerTitle => IsTargetCardSelectionPending && _snapshot.PendingDecision?.IncomingCard is { } incoming
        ? $"{CardCatalog.Get(incoming).DisplayName} · 选择一张手牌"
        : HasPublicTargetChoices ? PublicTargetChoices.FirstOrDefault()?.Parameters.GetValueOrDefault("action") switch
        {
            "snatch" => "顺手牵羊 · 选择目标的一张牌",
            "dismantlement" => "过河拆桥 · 选择目标的一张牌",
            _ => "选择目标的一张牌"
        }
        : _snapshot.PendingDecision?.SkillPrompt?.Name is { } skillName ? $"{skillName} · 选择牌"
        : CurrentDecisionContext?.Title ?? "选择牌";

    private static bool HasOpaqueRegionSlot(PromptChoice choice) => choice.Cards.Count == 0 &&
        new[] { "slot-index", "hand-slot", "slot" }.Any(key =>
            int.TryParse(choice.Parameters.GetValueOrDefault(key), NumberStyles.None,
                CultureInfo.InvariantCulture, out var slot) && slot >= 0);

    private (CardZoneKind Zone, int Owner)? TargetChoiceRegion(PromptChoice choice, bool publicPlay)
    {
        var players = _snapshot.Players;
        if (publicPlay)
        {
            if (!choice.Parameters.ContainsKey("target-card-id"))
                return choice.Targets.Count > 0 ? (CardZoneKind.Hand, choice.Targets[0]) : null;
            if (!int.TryParse(choice.Parameters["target-card-id"], out var targetId)) return null;
            foreach (var player in players.Where(player => choice.Targets.Contains(player.Seat)))
            {
                if (player.Equipment.Any(card => card.Id == targetId)) return (CardZoneKind.Equipment, player.Seat);
                if (player.Judgment.Any(card => card.Id == targetId)) return (CardZoneKind.Judgment, player.Seat);
            }
            return null;
        }
        if (choice.Cards.Count == 1)
        {
            foreach (var player in players)
            {
                if (player.Hand.Any(card => card.Id == choice.Cards[0])) return (CardZoneKind.Hand, player.Seat);
                if (player.Equipment.Any(card => card.Id == choice.Cards[0])) return (CardZoneKind.Equipment, player.Seat);
                if (player.Judgment.Any(card => card.Id == choice.Cards[0])) return (CardZoneKind.Judgment, player.Seat);
            }
        }
        if (choice.Cards.Count is not (0 or 1) || choice.Cards.Count == 0 && !HasOpaqueRegionSlot(choice)) return null;
        var zoneName = choice.Parameters.GetValueOrDefault("source-zone") ?? choice.Parameters.GetValueOrDefault("zone") ??
            choice.Parameters.GetValueOrDefault("target-zone");
        var zone = Enum.TryParse<CardZoneKind>(zoneName, true, out var parsed) ? parsed
            : choice.Parameters.ContainsKey("hand-slot") ? CardZoneKind.Hand : (CardZoneKind?)null;
        if (zone is not (CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment)) return null;
        var owner = int.TryParse(choice.Parameters.GetValueOrDefault("card-owner-seat"), out var declaredOwner)
            ? declaredOwner : choice.Targets.Count == 1 ? choice.Targets[0] : _snapshot.PendingDecision?.TargetSeat;
        return owner is { } seat && players.Any(player => player.Seat == seat) ? (zone.Value, seat) : null;
    }

    private void RefreshTargetCardPicker()
    {
        var publicPlay = HasPublicTargetChoices;
        var skillChoices = CenterSkillChoices;
        IReadOnlyList<PromptChoice> source = publicPlay ? PublicTargetChoices
            : IsSkillSelectionPending && !IsOwnedCardSelectionPending && !IsHandResponsePending && !HasDockSkillChoices
                ? skillChoices : [];
        var entries = source.Select(choice => (Choice: choice, Region: TargetChoiceRegion(choice, publicPlay)))
            .Where(entry => entry.Region is not null).ToArray();
        var severalOwners = entries.Select(entry => entry.Region!.Value.Owner).Distinct().Count() > 1;
        var regions = entries.GroupBy(entry => entry.Region!.Value).OrderBy(group => group.Key.Owner)
            .ThenBy(group => group.Key.Zone switch { CardZoneKind.Hand => 0, CardZoneKind.Equipment => 1, _ => 2 })
            .Select(group => new TargetCardRegionViewModel(group.Key.Zone, group.Key.Owner,
                group.Key.Zone switch { CardZoneKind.Hand => "手牌区", CardZoneKind.Equipment => "装备区", _ => "判定区" },
                severalOwners ? _snapshot.Players.Single(player => player.Seat == group.Key.Owner).GeneralName : string.Empty,
                publicPlay && group.Key.Zone == CardZoneKind.Hand
                    ? $"{_snapshot.Players.Single(player => player.Seat == group.Key.Owner).HandCount} 张暗牌 · 点击展开" : string.Empty,
                Array.AsReadOnly(group.Select(entry => entry.Choice).ToArray()),
                publicPlay ? SelectPublicTargetChoiceCommand : SelectSkillChoiceCommand, publicPlay)).ToArray();
        if (!_targetCardRegions.Select(region => (region.Zone, region.OwnerSeat, region.OwnerName, region.Hint, region.Command, region.IsPublicPlay))
                .SequenceEqual(regions.Select(region => (region.Zone, region.OwnerSeat, region.OwnerName, region.Hint, region.Command, region.IsPublicPlay))) ||
            !_targetCardRegions.SelectMany(region => region.Choices)
                .SequenceEqual(regions.SelectMany(region => region.Choices), ReferenceEqualityComparer.Instance))
        {
            _targetCardRegions = Array.AsReadOnly(regions);
            RaisePropertyChanged(nameof(TargetCardRegions));
        }
        PromptChoice[] grouped = publicPlay ? [] : entries.Select(entry => entry.Choice).ToArray();
        var ungrouped = skillChoices.Where(choice => !grouped.Contains(choice, ReferenceEqualityComparer.Instance)).ToArray();
        if (!_ungroupedCenterSkillChoices.SequenceEqual(ungrouped, ReferenceEqualityComparer.Instance))
        {
            _ungroupedCenterSkillChoices = Array.AsReadOnly(ungrouped);
            RaisePropertyChanged(nameof(UngroupedCenterSkillChoices));
        }
        RaisePropertyChanged(nameof(HasTargetCardRegions));
        RaisePropertyChanged(nameof(HasTargetCardPicker));
        RaisePropertyChanged(nameof(TargetCardPickerTitle));
        RaisePropertyChanged(nameof(HasPinnedPublicModuleChoices));
    }
}
