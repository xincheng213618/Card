using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class SkillRailLayoutChecks
{
    public static void CountsKeepOrderAndAlignLastSkill(string output)
    {
        using var model = new MainViewModel(false, 721019, showSetup: false, saveStore: new MemorySaveStore())
            { IsMotionEnabled = false };
        Program.StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        Program.AdvanceToDecision(model);
        var host = new NativeResponseStatusChecks.NativeHost(model);
        var window = host.Window;
        try
        {
            var root = (FrameworkElement)window.Content;
            root.Width = 1120;
            root.Height = 740;
            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.UpdateLayout();
            var rail = (FrameworkElement)window.FindName("HumanSkillPanel");
            var items = (ItemsControl)window.FindName("HumanSkillCards");
            var viewport = (Border)window.FindName("HumanSkillViewport");
            var handDock = (FrameworkElement)window.FindName("HandDock");
            var space = (FrameworkElement)window.FindName("HumanSkillSpace");
            double? bottom = null;
            // Vary only presentation data, without introducing character fixtures for a layout check.
            _ = model.HumanSkillCards;
            var cache = typeof(MainViewModel).GetField("_humanSkillCards", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var notify = typeof(ObservableObject).GetMethod("RaisePropertyChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
            foreach (var count in new[] { 1, 2, 3, 4, 5, 6, 10, 20, 2, 0 })
            {
                var skills = Enumerable.Range(1, count).Select(index =>
                    new HumanSkillViewModel($"技能{index}", "布局检查", "主动技", "当前可发动", "", true, false)).ToArray();
                cache.SetValue(model, skills);
                foreach (var property in new[] { nameof(model.HumanSkillCards), nameof(model.HumanSkillColumns), nameof(model.HumanSkillRailWidth) })
                    notify.Invoke(model, [property]);
                var path = count is 3 or 6 or 20 ? Path.Combine(output, $"skill-rail-{count}.png") : null;
                Program.Render(root, 1120, 740, path);

                var columns = count <= 2 ? 1 : 2;
                var panel = Program.Find<SkillRailPanel>(items).Single();
                Program.Assert(model.HumanSkillColumns == columns && panel.Columns == columns &&
                    Math.Abs(viewport.Width - columns * 108) < 0.1,
                    $"The {count}-skill rail must use {columns} columns and reserve the matching width.");
                var buttons = Program.Find<Button>(items).ToArray();
                Program.Assert(buttons.Select(button => button.CommandParameter).SequenceEqual(skills) &&
                    buttons.All(button => ReferenceEquals(button.Command, model.ActivateHumanSkillCommand)),
                    "Changing skill layout must retain the original item order and activation parameters.");
                for (var index = 0; index < count; index++)
                {
                    var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromIndex(index);
                    var slot = LayoutInformation.GetLayoutSlot(container);
                    var column = columns == 2 && index == count - 1 && count % 2 == 1 ? 1 : index % columns;
                    Program.Assert(Math.Abs(slot.X - column * 108) < 0.1 &&
                        Math.Abs(slot.Y - index / columns * slot.Height) < 0.1 &&
                        slot.Height > 0 && buttons[index].ActualHeight == 32,
                        $"Skill {index + 1} of {count} must keep its row and place an unpaired last skill beside the portrait.");
                }
                var currentBottom = viewport.TranslatePoint(new Point(0, viewport.ActualHeight), root).Y;
                bottom ??= currentBottom;
                var rows = (count + columns - 1) / columns;
                Program.Assert(rail.VerticalAlignment == VerticalAlignment.Bottom &&
                    double.IsPositiveInfinity(viewport.MaxHeight) &&
                    Math.Abs(currentBottom - bottom.Value) < 0.1 &&
                    Math.Abs(viewport.TranslatePoint(new Point(), root).X - space.TranslatePoint(new Point(), root).X) < 0.1 &&
                    Math.Abs(panel.ActualHeight - rows * 35) < 0.1,
                    $"The rail must keep its bottom edge fixed and expand upward without limiting or shrinking its rows ({count} skills): " +
                    $"bottom={currentBottom}/{bottom}, x={viewport.TranslatePoint(new Point(), root).X}/{space.TranslatePoint(new Point(), root).X}, " +
                    $"height={panel.ActualHeight}/{rows * 35}.");
                if (count == 20)
                {
                    Program.Assert(items.TranslatePoint(new Point(), handDock).Y < 0,
                        "Additional skill rows must extend above the hand dock.");
                    foreach (var button in new[] { buttons[0], buttons[^1] })
                    {
                        var hit = root.InputHitTest(button.TranslatePoint(
                            new Point(button.ActualWidth / 2, button.ActualHeight / 2), root));
                        Program.Assert(hit is DependencyObject element && button.IsAncestorOf(element),
                            $"Skills above the hand dock and at the fixed bottom must remain reachable by pointer input: {button.DataContext}, hit={hit}.");
                    }
                }
            }
        }
        finally
        {
            host.Dispose();
        }
    }
}
