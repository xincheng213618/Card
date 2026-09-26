using System.Windows.Input;
using System.Windows.Media;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    public IReadOnlyList<EquipmentSlotViewModel> HumanEquipmentSlots => Enum.GetValues<EquipmentSlot>()
        .Select(slot => new EquipmentSlotViewModel(slot, _snapshot?.Players.SingleOrDefault(player => player.IsHuman)?
            .Equipment.SingleOrDefault(card => EquipmentCatalog.Get(card.Kind).Slot == slot)))
        .ToArray();

    private ICommand? _activateHumanSkillCommand;
    public ICommand ActivateHumanSkillCommand => _activateHumanSkillCommand ??= new RelayCommand<HumanSkillViewModel>(skill =>
    {
        // Resolve again against the current legal actions; a stale chip cannot submit an obsolete choice.
        var action = HumanActiveSkillActions.FirstOrDefault(action =>
            skill.ContentId is not null && action.ProgramSkillId == skill.ContentId ||
            action.ProgramSkillId is null && skill.LegacyKind is not null && action.Skill == skill.LegacyKind);
        if (skill.IsAvailable && !skill.IsDisabled && action is not null) SelectActiveSkillCommand.Execute(action);
    });
}

public sealed record EquipmentSlotViewModel(EquipmentSlot Slot, CardSnapshot? Card)
{
    public string SlotName => EquipmentCatalog.GetSlotName(Slot);
    public bool IsOccupied => Card is not null;
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
