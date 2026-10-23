using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DualHealthAndBoneUiChecks
{
    public static void ExplicitPoolsAndOccupiedBoneSlots(string output)
    {
        using var model = new MainViewModel(false, 17, true, new MemorySaveStore()) { IsMotionEnabled = false };
        var window = new MainWindow(model);
        var seat = Seat(new(3, 2, 3));
        var host = new ContentControl { Content = seat, ContentTemplate = (DataTemplate)window.FindResource("SeatTemplate") };
        var root = new Grid(); root.Children.Add(host); window.Content = root;
        try
        {
            Program.Render(root, 360, 270, Path.Combine(output, "dual-health-and-bone-seat.png"));
            var rail = Program.Find<Border>(host).Single(item => item.Name == "SeatHealthRail");
            var numeric = Program.Find<Viewbox>(host).Single(item => item.Name == "SeatNumericHealth");
            Program.Assert(rail.Visibility == Visibility.Visible && numeric.Visibility == Visibility.Visible &&
                numeric.ActualWidth == 42 && Program.Find<TextBlock>(numeric).Single().Text == "大3/3\n小2/3" &&
                !seat.HealthImages.Any() && seat.HealthValue == "大乔 3/3 · 小乔 2/3",
                "Explicit dual health must remain visible when the legacy scalar HP is undefined, with separately labelled pools and no fabricated combined pips.");
            typeof(SeatViewModel).GetMethod("UpdateFrom", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(seat, [Seat(new(3, -1, 3))]);
            Program.Render(root, 360, 270, null);
            Program.Assert(Program.Find<TextBlock>(numeric).Single().Text == "大3/3\n小-1/3" && rail.Visibility == Visibility.Visible,
                "Updating the existing seat must retain the independently dying pool's actual negative value.");
            root.Children.Clear();
            root.Children.Add(new ContentControl { Content = seat, ContentTemplate = host.ContentTemplate });
            Program.Render(root, 360, 270, Path.Combine(output, "dual-health-negative-seat.png"));
            var bone = new CardSnapshot(1001, CardSnapshot.UndisclosedKind, Suit.Heart, 7, "骨", "7")
            { IsBone = true, OccupiedEquipmentSlot = EquipmentSlot.Weapon };
            var slot = new EquipmentSlotViewModel(EquipmentSlotViewModel.CardSlot(bone), bone);
            Program.Assert(slot is { Slot: EquipmentSlot.Weapon, IsOccupied: true, Artwork: null } &&
                slot.Name == "骨" && slot.CardMark == "♥7" && slot.Tooltip.Contains("仅占用栏位") &&
                !slot.Tooltip.Contains("闪"),
                "A masked basic-card bone occupies its assigned equipment slot without decoding a printed equipment kind, artwork or hidden category.");
            var boneHost = new ContentControl { Content = slot, ContentTemplate = (DataTemplate)window.FindResource("EquipmentSlotTemplate") };
            root.Children.Clear(); root.Children.Add(boneHost);
            Program.Render(root, 360, 100, Path.Combine(output, "bone-equipment-slot.png"));
            Program.Assert(Program.Find<TextBlock>(boneHost).Single(text => text.Name == "EquipmentRuntimeName") is { Text: "骨", Visibility: Visibility.Visible },
                "The human equipment template must show a readable bone label when it has no printed equipment artwork.");
        }
        finally { window.Close(); }
    }

    private static SeatViewModel Seat(HappyQiaoDualHealth health) => new()
    {
        Seat = 1, Name = "神大小乔", GeneralName = "神大小乔", Kingdom = "神", RoleLabel = "反贼",
        HpText = $"大乔 {health.DaQiaoHp}/3 · 小乔 {health.XiaoQiaoHp}/3", HpUndefined = true,
        Hp = 0, MaxHp = 0, HappyQiaoHealth = health, HandText = "手牌 3", EquipmentText = "装备区 —",
        JudgmentText = "判定区 —", DistanceText = "距离 1", SkillText = "双姝", IsAlive = true,
        IsCurrent = false, IsHuman = false
    };
}
