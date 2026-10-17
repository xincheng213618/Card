using System.IO;
using System.Reflection;
using System.Windows.Media;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class MouCaoPiUiChecks
{
    public static void PortraitSkillsAndFangzhuState(string output)
    {
        using var model = new MainViewModel(false, 31, true, new MemorySaveStore(), contentRegistry: StandardContentRegistry.CreateWithClassicGenerals())
            { IsMotionEnabled = false, IsSoundEnabled = false };
        model.OpenGeneralGalleryCommand.Execute(null); model.GeneralGallerySearchText = "谋曹丕";
        var entry = model.GeneralGalleryEntries.Single(e => e.GeneralId == "mou:cao-pi");
        model.SelectGeneralGalleryEntryCommand.Execute(entry);
        Program.Assert(entry.IsPlayable && entry.HasPortrait && entry.Portrait.Brush is ImageBrush { ImageSource: not null } &&
            entry.Skills.Select(s => s.Name).ToHashSet().SetEquals(new[] { "行殇", "放逐", "颂威" }), "The real gallery exposes the official portrait and all three separate Mou skills.");
        var panel = new GeneralGalleryPanel { DataContext = model };
        try { Program.Render(panel, 1120, 820, Path.Combine(output, "mou-cao-pi-gallery.png")); }
        finally { panel.DataContext = null; }
        var player = Program.Engine(model).CreateSnapshot(0).Players[0] with { MouCaoPiControls = new[] { new MouCaoPiControl(1, 1, 0, 1, MouCaoPiControlKind.CannotRespond) } };
        var badge = (string)typeof(MainViewModel).GetMethod("MouFangzhuBadge", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [player])!;
        var tooltip = (string)typeof(MainViewModel).GetMethod("MouFangzhuTooltip", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [player])!;
        Program.Assert(badge.Contains("不能响应") && tooltip.Contains("下个实际回合结束"), "The seat badge names the actual restriction and target-turn expiry.");
    }
}
