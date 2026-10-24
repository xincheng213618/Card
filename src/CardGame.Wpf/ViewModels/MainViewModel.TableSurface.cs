using System.Windows.Input;
using System.Windows.Media;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<LegalAction> _supplementalActiveSkillActions = [];
    public string ActionDockConfirmText => PlayButtonText;
    public string ActionDockCancelText => IsMobileShiZhuangshiSelectionPending ? "清 空" : IsBaguaActivationPending ? "不发动" : IsStoneAxeSelectionPending ? "否" : SkillTargetDeclineChoice is not null ? "取 消" : OwnedCardDeclineChoice is not null || HandResponseDeclineChoice is not null
        ? "放 弃" : "取 消";
    public int HumanSkillColumns => HumanSkillCards.Count > 2 ? 2 : 1;
    public double HumanSkillRailWidth => HumanSkillColumns == 2 ? 216 : 108;

    // Equipment activates from its slot. Keep explicit alternatives for other activations.
    public IReadOnlyList<LegalAction> SupplementalActiveSkillActions
    {
        get
        {
            if (!ShowActiveSkillEntry) return _supplementalActiveSkillActions = [];
            var skills = HumanSkillCards;
            var published = HumanActiveSkillActions;
            var actions = published.Where((action, index) => !IsTieredRoundZeroAction(action) ||
                !published.Take(index).Any(previous => SameTieredRoundZeroFamily(previous, action))).ToArray();
            var equipment = _snapshot.Players.Single(player => player.IsHuman).Equipment;
            var supplemental = actions.Where(action => PlayConversionActivation(action) is null)
                .Where(action => action.EquipmentKind is null ||
                !equipment.Any(card => card.Kind == action.EquipmentKind)).Where(action => !skills.Any(skill => MatchesSkill(action, skill)) ||
                actions.Count(other => ActiveSkillSourceId(other) == ActiveSkillSourceId(action) &&
                    other.EquipmentKind == action.EquipmentKind &&
                    ActiveSkillSourceOwner(other) == ActiveSkillSourceOwner(action)) > 1).ToArray();
            // The buttons carry action parameters; an equal record from a new view is a new parameter.
            if (!_supplementalActiveSkillActions.SequenceEqual(supplemental, ReferenceEqualityComparer.Instance))
                _supplementalActiveSkillActions = supplemental;
            return _supplementalActiveSkillActions;
        }
    }

    private static string? ActiveSkillSourceId(LegalAction action) =>
        action.ProgramSkillId ?? (IsTieredRoundZeroAction(action) ? action.ConversionSource?.SkillId : null);

    private int ActiveSkillSourceOwner(LegalAction action) =>
        action.ProgramSkillOwnerSeat ?? (IsTieredRoundZeroAction(action) ? action.ConversionSource?.OwnerSeat : null) ?? _snapshot.HumanSeat;

    private bool MatchesSkill(LegalAction action, HumanSkillViewModel skill) =>
        skill.ContentId is not null && ActiveSkillSourceId(action) == skill.ContentId &&
        ActiveSkillSourceOwner(action) == (skill.SkillOwnerSeat ?? _snapshot.HumanSeat);

    private PlayerSnapshot? _equipmentPlayer;
    private IReadOnlyList<EquipmentSlotViewModel>? _humanEquipmentSlots;
    public IReadOnlyList<EquipmentSlotViewModel> HumanEquipmentSlots
    {
        get
        {
            var human = _snapshot?.Players.SingleOrDefault(player => player.IsHuman);
            if (_humanEquipmentSlots is not null && ReferenceEquals(human, _equipmentPlayer)) return _humanEquipmentSlots;
            var slots = EquipmentSlotViewModel.FromPlayer(human, includeEmpty: true).Select(slot =>
            {
                var card = slot.Card;
                return slot with
                {
                    IsSelected = !slot.IsAbolished && card is not null && (IsCardPaymentSelectionPending ? _paymentCardIds.Contains(card.Id) :
                            IsOwnedCardSelectionPending ? IsOwnedSelectionCardSelected(card.Id) :
                            _selectedActiveSkillCardIds.Contains(card.Id) || IsSelectedEquipmentActivation(card) ||
                            IsPlayConversionSelectionPending && _selectedCardId == card.Id),
                    IsSelectable = !slot.IsAbolished && (IsOwnedSelectionMaterial(card?.Id) || IsCardPaymentMaterial(card?.Id) || CanSelectActiveEquipment(card))
                };
            }).ToArray();
            _equipmentPlayer = human;
            // A new committed snapshot need not replace unchanged equipment controls.
            if (_humanEquipmentSlots is null || !_humanEquipmentSlots.SequenceEqual(slots))
                _humanEquipmentSlots = Array.AsReadOnly(slots);
            return _humanEquipmentSlots;
        }
    }

    private ICommand? _activateHumanSkillCommand;
    public ICommand ActivateHumanSkillCommand => _activateHumanSkillCommand ??= new RelayCommand<HumanSkillViewModel>(skill =>
    {
        // Drafts commit only through the primary confirmation; re-clicking a chip must not pay its cost.
        if (IsActiveSkillSelectionPending ||
            !HumanSkillCards.Any(current => ReferenceEquals(current, skill))) return;
        if (skill.IsAvailable && !skill.IsDisabled && TrySelectResponseConversion(skill)) return;
        if (skill.IsAvailable && !skill.IsDisabled && TrySelectPlayConversion(skill)) return;
        // Resolve the published rail chip against the current legal actions.
        var action = HumanActiveSkillActions.FirstOrDefault(action => MatchesSkill(action, skill));
        if (skill.IsAvailable && !skill.IsDisabled && action is not null) SelectActiveSkillCommand.Execute(action);
    });
}

public sealed record EquipmentSlotViewModel(EquipmentSlot Slot, CardSnapshot? Card, bool IsSelected = false, bool IsSelectable = false, bool IsAbolished = false)
{
    internal static IReadOnlyList<EquipmentSlotViewModel> FromPlayer(PlayerSnapshot? player, bool includeEmpty = false) =>
        Array.AsReadOnly(Enum.GetValues<EquipmentSlot>().OrderBy(DisplayOrder).SelectMany(slot =>
        {
            var cards = player?.Equipment.Where(card => CardSlot(card) == slot).ToArray() ?? [];
            var capacity = player?.IsEquipmentAreaAbolished == true ? 0 : player?.EquipmentSlotCapacities?.GetValueOrDefault(slot, 1) ?? 1;
            var abolished = capacity <= 0;
            var count = Math.Max(cards.Length, abolished ? 1 : includeEmpty ? Math.Max(1, capacity) : 0);
            return Enumerable.Range(0, count).Select(index =>
                new EquipmentSlotViewModel(slot, index < cards.Length ? cards[index] : null, IsAbolished: abolished));
        }).ToArray());

    public static EquipmentSlot CardSlot(CardSnapshot card) => card.OccupiedEquipmentSlot ?? EquipmentCatalog.Get(card.Kind).Slot;
    public static int DisplayOrder(EquipmentSlot slot) => slot switch
    {
        EquipmentSlot.Weapon => 0, EquipmentSlot.Armor => 1, EquipmentSlot.DefensiveHorse => 2,
        EquipmentSlot.OffensiveHorse => 3, _ => 4
    };
    public string CompactSlotName => Slot switch
    {
        EquipmentSlot.Weapon => "武", EquipmentSlot.Armor => "防",
        EquipmentSlot.DefensiveHorse => "+1", EquipmentSlot.OffensiveHorse => "−1", _ => "宝"
    };
    public string CardMark => Card is { } card ? $"{Suit}{card.RankText}" : string.Empty;
    public Brush CardMarkBrush => Card?.Suit is CardGame.Core.Suit.Heart or CardGame.Core.Suit.Diamond ? Brushes.DarkRed : Brushes.Black;
    public string SlotName => EquipmentCatalog.GetSlotName(Slot);
    public bool IsOccupied => Card is not null;
    public bool HasDynamicWeaponName => CardArt.NeedsRuntimeNameOverlay(Card?.Kind);
    public bool NeedsNameOverlay => Card?.IsBone == true || HasDynamicWeaponName;
    public string Name => Card?.DisplayName ?? string.Empty;
    public ImageSource? Artwork => Card is { IsBone: false } card ? CardArt.GetEquipment(card.Kind) : null;
    private string Suit => Card?.Suit switch
    {
        CardGame.Core.Suit.Heart => "♥", CardGame.Core.Suit.Diamond => "♦",
        CardGame.Core.Suit.Spade => "♠", CardGame.Core.Suit.Club => "♣", _ => string.Empty
    };
    public ImageSource? RankArtwork => CardArt.GetRank(Card?.RankText ?? string.Empty, Suit);
    public ImageSource? SuitArtwork => CardArt.GetSuit(Suit);
    public string Tooltip => IsAbolished ? $"{SlotName} · 已废弃，无法置入装备" : Card is { } card
        ? $"{card.DisplayName} · {Suit}{card.RankText}\n{(card.IsBone ? "骨 · 仅占用栏位，不提供原装备效果" : EquipmentCatalog.Get(card.Kind).Description)}" : $"{SlotName} · 空槽";
}
