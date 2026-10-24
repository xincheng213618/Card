using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class EquipmentSlotUiChecks
{
    public static void AbolishedSlotsRemainVisibleAndRestore(string output)
    {
        using var model = new MainViewModel(false, 17, false, new MemorySaveStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        Program.StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        Program.AdvanceToDecision(model);
        var snapshot = Program.Engine(model).CreateSnapshot(0);
        var humanSeat = snapshot.Players.Single(player => player.IsHuman).Seat;
        var otherSeat = snapshot.Players.First(player => !player.IsHuman).Seat;
        var crossbow = new CardSnapshot(9001, CardKind.Crossbow, Suit.Club, 1, "诸葛连弩", "A");
        snapshot = snapshot with { Players = snapshot.Players.Select(player => player with
        {
            Equipment = player.Seat == otherSeat ? [crossbow] : [],
            EquipmentSlotCapacities = null, IsEquipmentAreaAbolished = false
        }).ToArray() };
        var partial = snapshot with { Players = snapshot.Players.Select(player => player with
        {
            EquipmentSlotCapacities = player.Seat == humanSeat
                ? new Dictionary<EquipmentSlot, int> { [EquipmentSlot.Weapon] = 2, [EquipmentSlot.Armor] = 0, [EquipmentSlot.OffensiveHorse] = 0 }
                : player.Seat == otherSeat ? new Dictionary<EquipmentSlot, int> { [EquipmentSlot.Armor] = 0 } : null
        }).ToArray() };
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            Refresh(partial);
            Program.Assert(model.HumanEquipmentSlots.Count == 6 && model.HumanEquipmentSlots.Count(slot => slot.IsAbolished) == 2 &&
                model.HumanEquipmentSlots.Count(slot => slot.Slot == EquipmentSlot.Weapon) == 2 &&
                model.HumanEquipmentSlots.Where(slot => slot.IsAbolished).All(slot => !slot.IsOccupied && !slot.IsSelectable &&
                    slot.Tooltip.Contains("已废弃") && slot.Tooltip.Contains("无法置入装备")),
                "Abolished local slots must retain a distinct nonselectable row while expanded available slots remain intact.");
            var peer = model.Seats.Single(seat => seat.Seat == otherSeat);
            Program.Assert(peer.ShowPortraitEquipment && peer.EquipmentSlots.Count == 2 &&
                peer.EquipmentSlots.Any(slot => slot is { Slot: EquipmentSlot.Armor, IsAbolished: true }) &&
                peer.EquipmentSlots.Any(slot => slot.Card?.Id == crossbow.Id && !slot.IsAbolished),
                "Portrait equipment must show abolished slots alongside actual equipment without adding ordinary empty rows.");
            foreach (var width in new[] { 1120, 1440 })
            {
                Program.Render(root, width, 860, Path.Combine(output, $"abolished-equipment-{width}.png"));
                var local = (ItemsControl)window.FindName("HumanEquipmentSlots");
                var overlays = Program.Find<Border>(local).Where(border => border.Name == "AbolishedEquipmentOverlay" && border.Visibility == Visibility.Visible).ToArray();
                Program.Assert(overlays.Length == 2 && overlays.All(border => border.ActualWidth > 100 && border.ActualHeight > 20 &&
                    Program.Find<TextBlock>(border).Any(text => text.Text == "已废弃" && text.Visibility == Visibility.Visible)),
                    "The actual local template must display both abolished overlays and readable labels at supported widths.");
                Program.Assert(Program.Find<Border>(root).Any(border => border.Name == "AbolishedPortraitEquipmentOverlay" &&
                    border.Visibility == Visibility.Visible && border.ActualHeight > 0 && border.DataContext is EquipmentSlotViewModel { Slot: EquipmentSlot.Armor, IsAbolished: true }),
                    "The actual opponent portrait must render its abolished slot.");
            }

            Refresh(partial with { Players = partial.Players.Select(player => player with
                { Equipment = [], IsEquipmentAreaAbolished = true }).ToArray() });
            Program.Render(root, 1120, 860, Path.Combine(output, "abolished-equipment-whole-area.png"));
            Program.Assert(model.HumanEquipmentSlots.Count == 5 && model.HumanEquipmentSlots.All(slot => slot.IsAbolished) &&
                model.Seats.Where(seat => !seat.IsHuman).All(seat => seat.ShowPortraitEquipment && seat.EquipmentSlots.Count == 5 &&
                    seat.EquipmentSlots.All(slot => slot.IsAbolished)),
                "Whole-area abolition must override positive per-slot capacities for local and portrait equipment.");

            Refresh(snapshot);
            Program.Render(root, 1120, 860, null);
            Program.Assert(model.HumanEquipmentSlots.Count == 5 && model.HumanEquipmentSlots.All(slot => !slot.IsAbolished) &&
                ReferenceEquals(peer, model.Seats.Single(seat => seat.Seat == otherSeat)) && peer.EquipmentSlots.Count == 1 &&
                peer.EquipmentSlots.Single().Card?.Id == crossbow.Id &&
                !Program.Find<Border>(root).Any(border => border.Visibility == Visibility.Visible &&
                    border.Name is "AbolishedEquipmentOverlay" or "AbolishedPortraitEquipmentOverlay"),
                "Restoring capacities must retire the old overlays on existing seats and retain ordinary equipment.");
        }
        finally { window.Content = null; window.Close(); }

        void Refresh(GameSnapshot view) => typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [view]);
    }
}
