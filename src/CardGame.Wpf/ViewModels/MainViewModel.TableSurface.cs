using System.Windows.Input;
using System.Windows.Media;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<LegalAction> _supplementalActiveSkillActions = [];
    public string ActionDockConfirmText => CanEndTurn ? "确 定" : PlayButtonText;
    public string ActionDockCancelText => HandResponseDeclineChoice is not null || SkillTargetDeclineChoice is not null
        ? "放 弃" : "取 消";
    public int HumanSkillColumns => HumanSkillCards.Count > 4 ? 2 : 1;
    public double HumanSkillRailWidth => HumanSkillColumns == 2 ? 216 : 108;

    // Equipment and multiple activations still need their exact legal-action choices.
    public IReadOnlyList<LegalAction> SupplementalActiveSkillActions
    {
        get
        {
            if (!ShowActiveSkillEntry) return _supplementalActiveSkillActions = [];
            var skills = HumanSkillCards;
            var actions = HumanActiveSkillActions;
            var supplemental = actions.Where(action => !skills.Any(skill => MatchesSkill(action, skill)) ||
                actions.Count(other => other.ProgramSkillId == action.ProgramSkillId &&
                    other.EquipmentKind == action.EquipmentKind &&
                    other.ProgramSkillOwnerSeat == action.ProgramSkillOwnerSeat) > 1).ToArray();
            // The buttons carry action parameters; an equal record from a new view is a new parameter.
            if (!_supplementalActiveSkillActions.SequenceEqual(supplemental, ReferenceEqualityComparer.Instance))
                _supplementalActiveSkillActions = supplemental;
            return _supplementalActiveSkillActions;
        }
    }

    private static bool MatchesSkill(LegalAction action, HumanSkillViewModel skill) =>
        skill.ContentId is not null && action.ProgramSkillId == skill.ContentId &&
        action.ProgramSkillOwnerSeat == skill.SkillOwnerSeat;

    private PlayerSnapshot? _equipmentPlayer;
    private IReadOnlyList<EquipmentSlotViewModel>? _humanEquipmentSlots;
    public IReadOnlyList<EquipmentSlotViewModel> HumanEquipmentSlots
    {
        get
        {
            var human = _snapshot?.Players.SingleOrDefault(player => player.IsHuman);
            if (_humanEquipmentSlots is not null && ReferenceEquals(human, _equipmentPlayer)) return _humanEquipmentSlots;
            var slots = Enum.GetValues<EquipmentSlot>().OrderBy(EquipmentSlotViewModel.DisplayOrder).SelectMany(slot =>
            {
                var cards = human?.Equipment.Where(card => EquipmentCatalog.Get(card.Kind).Slot == slot).ToArray() ?? [];
                var count = Math.Max(human?.EquipmentSlotCapacities?.GetValueOrDefault(slot, 1) ?? 1, cards.Length);
                return Enumerable.Range(0, count).Select(index => new EquipmentSlotViewModel(slot,
                    index < cards.Length ? cards[index] : null));
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
        if (skill.IsAvailable && !skill.IsDisabled && TrySelectWusheng(skill)) return;
        // Resolve the published rail chip against the current legal actions.
        var action = HumanActiveSkillActions.FirstOrDefault(action => MatchesSkill(action, skill));
        if (skill.IsAvailable && !skill.IsDisabled && action is not null) SelectActiveSkillCommand.Execute(action);
    });
}

public sealed record EquipmentSlotViewModel(EquipmentSlot Slot, CardSnapshot? Card)
{
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
    public string Name => Card?.DisplayName ?? string.Empty;
    public ImageSource? Artwork => Card is { } card ? CardArt.GetEquipment(card.Kind) : null;
    private string Suit => Card?.Suit switch
    {
        CardGame.Core.Suit.Heart => "♥", CardGame.Core.Suit.Diamond => "♦",
        CardGame.Core.Suit.Spade => "♠", CardGame.Core.Suit.Club => "♣", _ => string.Empty
    };
    public ImageSource? RankArtwork => CardArt.GetRank(Card?.RankText ?? string.Empty, Suit);
    public ImageSource? SuitArtwork => CardArt.GetSuit(Suit);
    public string Tooltip => Card is { } card
        ? $"{card.DisplayName} · {Suit}{card.RankText}\n{EquipmentCatalog.Get(card.Kind).Description}" : $"{SlotName} · 空槽";
}
