using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Wpf.Controls;

internal static class InteractionMotionChecks
{
    public static void NativeLifecyclePreservesLayoutAndRetiresAnimations()
    {
        var button = new Button
        {
            Width = 240, Height = 90, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Template = (ControlTemplate)XamlReader.Parse("""
                <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                                 xmlns:controls="clr-namespace:CardGame.Wpf.Controls;assembly=CardGame.Wpf"
                                 TargetType="Button">
                    <Grid Background="Transparent">
                        <Border x:Name="Artwork" Background="#AD8151" IsHitTestVisible="False"
                                controls:InteractionMotion.IsEnabled="True"
                                controls:InteractionMotion.HoverOffset="-3"
                                controls:InteractionMotion.PressedScale="0.985">
                            <Border.RenderTransform><TranslateTransform X="2" /></Border.RenderTransform>
                            <TextBlock Text="进入战场" HorizontalAlignment="Center" VerticalAlignment="Center" />
                        </Border>
                    </Grid>
                </ControlTemplate>
                """)
        };
        var root = new Grid { Width = 400, Height = 200 };
        root.Children.Add(button);
        InteractionMotion.SetMotionEnabled(root, true);
        using var source = new HwndSource(new HwndSourceParameters("Card interaction motion lifecycle check")
        {
            Width = 400, Height = 200, WindowStyle = unchecked((int)0x80000000),
            ExtendedWindowStyle = 0x08000080
        });
        try
        {
            source.RootVisual = root;
            Pump(root);
            var surface = (FrameworkElement)button.Template.FindName("Artwork", button);
            Program.Assert(surface.IsLoaded && surface.IsVisible, "The motion fixture must have a loaded native presentation source.");
            var group = (TransformGroup)surface.RenderTransform;
            var original = group.Children[0];
            var scale = group.Children.OfType<ScaleTransform>().Single();
            var offset = group.Children.OfType<TranslateTransform>().Last();
            var bounds = Bounds(button, root);
            var size = button.RenderSize;
            Program.Assert(group.Children.Count == 3 && original.Value.OffsetX == 2,
                "Attaching interaction motion replaced the template's original transform.");

            // Move the real template artwork without advancing time. Its stationary
            // transparent parent must continue to own the complete original input slot.
            scale.ScaleX = scale.ScaleY = .985;
            offset.X = -6;
            offset.Y = -6;
            Program.Assert(OwnedBy(root.InputHitTest(new Point(bounds.Right - 1, bounds.Bottom - 1)), button) &&
                           OwnedBy(root.InputHitTest(new Point(bounds.Right - 1, bounds.Top + bounds.Height / 2)), button) &&
                           !OwnedBy(root.InputHitTest(new Point(bounds.Left - 1, bounds.Top + bounds.Height / 2)), button) &&
                           !OwnedBy(root.InputHitTest(new Point(bounds.Left + bounds.Width / 2, bounds.Top - 1)), button),
                "Moving artwork shrank the original bottom/right input edges or created clickable space above/left of the button.");
            scale.ScaleX = scale.ScaleY = 1;
            offset.X = offset.Y = 0;

            Hover(button, true);
            Program.Assert(button.IsMouseOver && InteractionMotion.GetMotionEnabled(surface) && surface.IsVisible && button.IsEnabled,
                $"Hover setup was not active: hover={button.IsMouseOver}, motion={InteractionMotion.GetMotionEnabled(surface)}, visible={surface.IsVisible}, enabled={button.IsEnabled}, systemMotion={SystemParameters.ClientAreaAnimation}.");
            AssertDestination(scale, offset, 1, SystemParameters.ClientAreaAnimation ? -3 : 0);
            Press(button, true);
            AssertDestination(scale, offset, SystemParameters.ClientAreaAnimation ? .985 : 1,
                SystemParameters.ClientAreaAnimation ? 1 : 0);
            Program.Assert(!SystemParameters.ClientAreaAnimation || scale.HasAnimatedProperties,
                "A loaded enabled button did not start its short press transition.");

            // Reverse several times in the same dispatcher turn: no delays or match advancement.
            for (var index = 0; index < 4; index++)
            {
                Press(button, false);
                AssertDestination(scale, offset, 1, SystemParameters.ClientAreaAnimation ? -3 : 0);
                Press(button, true);
            }
            Press(button, false);
            Hover(button, false);
            AssertDestination(scale, offset, 1, 0);
            Program.Assert(Bounds(button, root) == bounds && button.RenderSize == size &&
                           button.RenderTransform.Value.IsIdentity && group.Children.Count == 3 &&
                           ReferenceEquals(group.Children[0], original),
                "Rapid interaction reversal changed the button's input/layout slot or accumulated transforms.");

            Press(button, true);
            InteractionMotion.SetMotionEnabled(root, false);
            AssertRest(scale, offset, "Disabling motion during a press did not cancel animation and snap to rest.");
            Program.Assert(surface.RenderTransform.Value == original.Value,
                "The motion-off pose lost the template's original transform.");
            InteractionMotion.SetMotionEnabled(root, true);
            Press(button, false);
            Hover(button, true);
            button.IsEnabled = false;
            AssertRest(scale, offset, "A disabled button retained interaction animation.");
            button.IsEnabled = true;

            Press(button, true);
            source.RootVisual = null;
            Pump(root);
            Program.Assert(!surface.IsLoaded && ReferenceEquals(surface.RenderTransform, original),
                "Unloading did not restore the original template transform.");
            AssertRest(scale, offset, "Unloading left animation clocks on the retired artwork.");
            Press(button, false);
            Hover(button, false);
            Press(button, true);
            AssertRest(scale, offset, "A detached button still invoked retired motion handlers.");

            Press(button, false);
            source.RootVisual = root;
            Pump(root);
            var reloaded = (TransformGroup)surface.RenderTransform;
            Program.Assert(reloaded.Children.Count == 3 && ReferenceEquals(reloaded.Children[0], original),
                "Reloading nested interaction transforms or lost the original artwork transform.");
            Press(button, true);
            InteractionMotion.SetIsEnabled(surface, false);
            Program.Assert(ReferenceEquals(surface.RenderTransform, original),
                "Removing the interaction behavior did not restore the template transform.");
            AssertRest(scale, offset, "Removing the behavior left live animation clocks.");
            Press(button, false);
            Hover(button, true);
            AssertRest(scale, offset, "A removed behavior retained input subscriptions.");
        }
        finally
        {
            source.RootVisual = null;
            Pump(root);
        }
    }

    private static void AssertDestination(ScaleTransform scale, TranslateTransform offset, double expectedScale, double expectedOffset) =>
        Program.Assert((double)scale.GetAnimationBaseValue(ScaleTransform.ScaleXProperty) == expectedScale &&
                       (double)scale.GetAnimationBaseValue(ScaleTransform.ScaleYProperty) == expectedScale &&
                       (double)offset.GetAnimationBaseValue(TranslateTransform.YProperty) == expectedOffset,
            $"The latest input did not replace the previous interaction animation destination: expected scale={expectedScale}, offset={expectedOffset}; base scale=({scale.GetAnimationBaseValue(ScaleTransform.ScaleXProperty)},{scale.GetAnimationBaseValue(ScaleTransform.ScaleYProperty)}), offset={offset.GetAnimationBaseValue(TranslateTransform.YProperty)}; current scale=({scale.ScaleX},{scale.ScaleY}), offset={offset.Y}.");

    private static void AssertRest(ScaleTransform scale, TranslateTransform offset, string message) =>
        Program.Assert(scale.ScaleX == 1 && scale.ScaleY == 1 && offset.Y == 0 &&
                       !scale.HasAnimatedProperties && !offset.HasAnimatedProperties, message);

    private static Rect Bounds(FrameworkElement element, FrameworkElement ancestor) =>
        element.TransformToAncestor(ancestor).TransformBounds(new Rect(element.RenderSize));

    private static bool OwnedBy(IInputElement? hit, Button button)
    {
        for (var current = hit as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            if (ReferenceEquals(current, button)) return true;
        return false;
    }

    private static void Press(Button button, bool value) => SetInputState(button, typeof(ButtonBase), "IsPressedPropertyKey", value);

    private static void Hover(Button button, bool value)
    {
        SetInputState(button, typeof(UIElement), "IsMouseOverPropertyKey", value);
        button.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = value ? Mouse.MouseEnterEvent : Mouse.MouseLeaveEvent
        });
    }

    private static void SetInputState(Button button, Type owner, string field, bool value)
    {
        if (owner == typeof(UIElement) && field == "IsMouseOverPropertyKey")
        {
            // UIElement.IsMouseOver reads a cached CoreFlags bit, whereas a template
            // trigger reads the dependency property. Keep both halves of the native
            // input state in sync without moving the user's desktop pointer.
            var writeFlag = typeof(UIElement).GetMethod("WriteFlag", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var flag = Enum.Parse(writeFlag.GetParameters()[0].ParameterType, "IsMouseOverCache");
            writeFlag.Invoke(button, [flag, value]);
        }
        var key = (DependencyPropertyKey)owner.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        if (value) button.SetValue(key, true);
        else button.ClearValue(key);
    }

    private static void Pump(FrameworkElement root)
    {
        root.Measure(new Size(400, 200));
        root.Arrange(new Rect(0, 0, 400, 200));
        root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
    }
}
