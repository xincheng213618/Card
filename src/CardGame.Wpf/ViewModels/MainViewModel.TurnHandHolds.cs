using System.Collections.ObjectModel;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed record TurnHandHoldViewModel(
    int OwnerSeat, long HoldId, string Title, string ReturnHint,
    string PrivateTitle, IReadOnlyList<CardViewModel> PrivateCards)
{
    public bool HasPrivateCards => PrivateCards.Count > 0;
}

public sealed partial class MainViewModel
{
    public ObservableCollection<TurnHandHoldViewModel> TurnHandHolds { get; } = [];
    public bool HasTurnHandHolds => TurnHandHolds.Count > 0;

    private void RebuildTurnHandHoldViews()
    {
        TurnHandHolds.Clear();
        foreach (var player in _snapshot.Players)
        {
            var holds = player.PrivateTurnHolds ?? [];
            for (var index = 0; index < holds.Count; index++)
            {
                var hold = holds[index];
                var skillName = _game.ContentRegistry?.Skills.GetValueOrDefault(hold.SkillId)?.Name ?? "暂存";
                var suffix = holds.Count > 1 ? $" · 第{index + 1}组" : string.Empty;
                var canReadPrivate = player.Seat == _snapshot.HumanSeat && hold.OwnerSeat == _snapshot.HumanSeat || IsDeveloperView;
                var cards = canReadPrivate && hold.Cards is { } faces
                    ? faces.Select(card => CreateViewedCard(card, privateView: true)).ToArray()
                    : Array.Empty<CardViewModel>();
                TurnHandHolds.Add(new(player.Seat, hold.HoldId,
                    $"{player.GeneralName} · {skillName}{suffix} · {hold.Count}张",
                    "回合结束时返还",
                    player.Seat == _snapshot.HumanSeat ? "暂存的手牌 · 仅你可见" : "暂存的手牌 · 开发视图",
                    Array.AsReadOnly(cards)));
            }
        }
        RaisePropertyChanged(nameof(HasTurnHandHolds));
    }
}
