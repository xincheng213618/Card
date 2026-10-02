using System.Windows.Input;
using System.Windows.Media;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private IReadOnlyList<LegalAction> _supplementalActiveSkillActions = [];
    public string ActionDockConfirmText => CanEndTurn ? "确 定" : PlayButtonText;
    public int HumanSkillColumns => HumanSkillCards.Count > 2 ? 2 : 1;
    public double HumanSkillRailWidth => HumanSkillColumns == 2 ? 216 : 108;

    // Equipment, borrowed skills and multiple activations still need their exact legal-action choices.
    // Ordinary owned skills already have a button beside the general, so do not repeat them here.
    public IReadOnlyList<LegalAction> SupplementalActiveSkillActions
    {
        get
        {
            if (!ShowActiveSkillEntry) return _supplementalActiveSkillActions = [];
            var skills = HumanSkillCards;
            var actions = HumanActiveSkillActions;
            var supplemental = actions.Where(action => !skills.Any(skill => MatchesSkill(action, skill)) ||
                actions.Count(other => other.ProgramSkillId == action.ProgramSkillId &&
                    other.EquipmentKind == action.EquipmentKind) > 1).ToArray();
            if (!_supplementalActiveSkillActions.SequenceEqual(supplemental)) _supplementalActiveSkillActions = supplemental;
            return _supplementalActiveSkillActions;
        }
    }

    private static bool MatchesSkill(LegalAction action, HumanSkillViewModel skill) =>
        skill.ContentId is not null && action.ProgramSkillId == skill.ContentId;

    private PlayerSnapshot? _equipmentPlayer;
    private IReadOnlyList<EquipmentSlotViewModel>? _humanEquipmentSlots;
    public IReadOnlyList<EquipmentSlotViewModel> HumanEquipmentSlots
    {
        get
        {
            var human = _snapshot?.Players.SingleOrDefault(player => player.IsHuman);
            if (_humanEquipmentSlots is not null && ReferenceEquals(human, _equipmentPlayer)) return _humanEquipmentSlots;
            var slots = Enum.GetValues<EquipmentSlot>().SelectMany(slot =>
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
        if (IsActiveSkillSelectionPending) return;
        // Resolve again against the current legal actions; a stale chip cannot submit an obsolete choice.
        var action = HumanActiveSkillActions.FirstOrDefault(action => MatchesSkill(action, skill));
        if (skill.IsAvailable && !skill.IsDisabled && action is not null) SelectActiveSkillCommand.Execute(action);
    });
}

public sealed record EquipmentSlotViewModel(EquipmentSlot Slot, CardSnapshot? Card)
{
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
