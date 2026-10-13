using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class TargetSelectionFeedbackChecks
{
    public static void NativePortraitAcquisition(string output)
    {
        using var model = new MainViewModel(false, 721019, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        AdvanceToDecision(model);
        var game = Engine(model);
        var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash &&
            item.CardId is > 0 && item.ConversionSource is null && model.Hand.Any(card => card.Id == item.CardId && card.Kind == CardKind.Slash));
        var card = model.Hand.Single(item => item.Id == action.CardId);
        var accepted = game.AcceptedCommands.Count;
        var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
            .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var window = new MainWindow(model)
        {
            ShowActivated = false, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -30000, Top = -30000, Width = 1120, Height = 820
        };
        var root = (FrameworkElement)window.Content;
        try
        {
            Render(root, 1120, 740, Path.Combine(output, "target-feedback-before-card.png"));
            Assert(Buttons().All(button => ReferenceEquals(button.Cursor, Cursors.Arrow) &&
                AutomationProperties.GetHelpText(button).Length == 0),
                "Idle portraits must not advertise a target click before any card is selected.");
            model.SelectCardCommand.Execute(card);
            foreach (var width in new[] { 1120, 1920 })
            {
                var height = width == 1120 ? 740 : 1080;
                Render(root, width, height, Path.Combine(output, $"target-feedback-candidates-{width}.png"));
                var buttons = Buttons();
                var originalBounds = buttons.ToDictionary(button => ((SeatViewModel)button.DataContext).Seat, Bounds);
                foreach (var button in buttons)
                {
                    var seat = (SeatViewModel)button.DataContext;
                    Assert(ReferenceEquals(button.Cursor, seat.IsLegalTarget ? Cursors.Hand : Cursors.Arrow) &&
                        AutomationProperties.GetHelpText(button) == (seat.IsLegalTarget ? "可选目标，点击选择" : string.Empty),
                        "Only targets from the currently published Slash action may advertise selection.");
                }
                var target = model.Seats.Single(seat => seat.Seat == action.TargetSeat);
                var targetButton = buttons.Single(button => ReferenceEquals(button.DataContext, target));
                var invalidButton = buttons.First(button => !((SeatViewModel)button.DataContext).IsLegalTarget);
                Hover(targetButton, true);
                Render(root, width, height, Path.Combine(output, $"target-feedback-hover-candidate-{width}.png"));
                Assert(HoverOutline(targetButton).Visibility == Visibility.Visible &&
                    BadgeText(targetButton) == "点击选择" && Unchanged(),
                    "Injected WPF pointer state must reveal the native candidate action without submitting a target.");
                AssertBadgeLayout(targetButton);
                Assert(buttons.All(button => Bounds(button) == originalBounds[((SeatViewModel)button.DataContext).Seat]),
                    "A candidate hover must keep all portraits at their original bounds.");
                Hover(targetButton, false);
                Assert(HoverOutline(targetButton).Visibility == Visibility.Collapsed &&
                    BadgeText(targetButton) == "可选目标" && Unchanged(),
                    "Leaving the native candidate trigger must clear its acquisition highlight and restore the candidate label.");
                Hover(invalidButton, true);
                Assert(HoverOutline(invalidButton).Visibility == Visibility.Collapsed && Unchanged(),
                    "An unavailable portrait must not advertise target acquisition under the same injected pointer state.");
                Hover(invalidButton, false);
                Click(invalidButton);
                Assert(!model.Seats.Any(seat => seat.IsSelectedTarget) && !model.CanConfirmSelected && Unchanged(),
                    "Clicking an unavailable portrait must preserve the current card and native command boundary.");
                Click(targetButton);
                Render(root, width, height, Path.Combine(output, $"target-feedback-selected-{width}.png"));
                Assert(target.IsSelectedTarget && model.CanConfirmSelected &&
                    AutomationProperties.GetHelpText(targetButton) == "已选目标，再次点击取消" &&
                    Find<Border>(targetButton).Single(border => border.Name == "SeatSelectedOutline").Opacity == 1 && Unchanged(),
                    "The selected portrait must identify its reversible draft without submitting the actual Slash.");
                Hover(targetButton, true);
                Render(root, width, height, Path.Combine(output, $"target-feedback-hover-selected-{width}.png"));
                Assert(HoverOutline(targetButton).Visibility == Visibility.Visible && BadgeText(targetButton) == "点击取消" && Unchanged(),
                    "The selected native portrait must advertise cancellation under injected pointer state.");
                AssertBadgeLayout(targetButton);
                Assert(buttons.All(button => Bounds(button) == originalBounds[((SeatViewModel)button.DataContext).Seat]),
                    "Target acquisition feedback must not move or resize any portrait.");
                Hover(targetButton, false);
                Assert(HoverOutline(targetButton).Visibility == Visibility.Collapsed && BadgeText(targetButton) == "✓ 已选定" && Unchanged(),
                    "Leaving the selected native trigger must restore its committed-draft label without residual hover.");
                Click(targetButton);
                Assert(!target.IsSelectedTarget && !model.CanConfirmSelected &&
                    AutomationProperties.GetHelpText(targetButton) == "可选目标，点击选择" && Unchanged(),
                    "Clicking the chosen portrait again must restore its selectable state without command submission.");
            }

            // The same real hand contains Iron Chain, whose published targets
            // include the actor. Normal PlayCard has no separate decision role.
            var chain = model.Hand.First(item => item.Kind == CardKind.IronChain && item.IsPlayable);
            model.SelectCardCommand.Execute(chain);
            foreach (var width in new[] { 1120, 1920 })
            {
                var height = width == 1120 ? 740 : 1080;
                Render(root, width, height, Path.Combine(output, $"target-feedback-chain-before-{width}.png"));
                var human = Buttons().Single(button => ((SeatViewModel)button.DataContext).IsHuman);
                var decision = Find<Border>(human).Single(border => border.Name == "DecisionRoleBadge");
                Assert(((SeatViewModel)human.DataContext).IsLegalTarget && decision.Visibility == Visibility.Collapsed,
                    "The native Iron Chain draft must expose the actor as a target without inventing a separate PlayCard decision role.");
                Hover(human, true);
                Render(root, width, height, Path.Combine(output, $"target-feedback-chain-hover-{width}.png"));
                Assert(BadgeText(human) == "点击选择" && Unchanged(), "The real self-target candidate must advertise its reversible selection.");
                AssertBadgeLayout(human);
                Click(human);
                Render(root, width, height, Path.Combine(output, $"target-feedback-chain-selected-{width}.png"));
                Assert(BadgeText(human) == "点击取消" && decision.Visibility == Visibility.Collapsed && Unchanged(),
                    "Selecting a real self target must not invent a decision role or submit the Iron Chain.");
                AssertBadgeLayout(human);
                Click(human);
                Hover(human, false);
            }
            model.SelectCardCommand.Execute(card);

            // Own a loaded, non-visible HWND. Keyboard.Focus exercises WPF focus
            // acquisition without showing or activating a foreground game window.
            var handle = new WindowInteropHelper(window).EnsureHandle();
            var previousParent = SetParent(handle, new nint(-3));
            Assert(previousParent != 0 || Marshal.GetLastPInvokeError() == 0,
                "The target-focus fixture could not attach its owned message-only HWND.");
            window.Show();
            ShowWindow(handle, 0);
            SetFocus(handle);
            Pump();
            Assert(window.IsLoaded && window.IsVisible && !IsWindowVisible(handle) && GetForegroundWindow() != handle,
                "The target-focus check must remain loaded without a visible or foreground native window.");
            Render(root, 1120, 740, Path.Combine(output, "target-feedback-keyboard-before.png"));
            var focusButtons = Buttons();
            var focusedTarget = focusButtons.Single(button => ((SeatViewModel)button.DataContext).Seat == action.TargetSeat);
            var focusBounds = focusButtons.ToDictionary(button => ((SeatViewModel)button.DataContext).Seat, Bounds);
            Assert(focusedTarget.Focus() && ReferenceEquals(Keyboard.FocusedElement, focusedTarget),
                "The actual portrait button must receive keyboard focus in its hidden presentation source.");
            Pump();
            var focusRing = Find<Border>(focusedTarget).Single(border => border.Name == "SeatFocusOutline");
            Assert(focusRing.Visibility == Visibility.Visible && !focusRing.IsHitTestVisible &&
                !((SeatViewModel)focusedTarget.DataContext).IsSelectedTarget && Unchanged(),
                "Keyboard focus must visibly locate a portrait without selecting or committing it.");
            Render(root, 1120, 740, Path.Combine(output, "target-feedback-keyboard-focused.png"));
            Assert(focusButtons.All(button => Bounds(button) == focusBounds[((SeatViewModel)button.DataContext).Seat]),
                "The keyboard focus outline must preserve the complete seat layout.");
            Click(focusedTarget);
            model.SaveGameCommand.Execute(null);
            model.LoadManualGameCommand.Execute(null);
            game = Engine(model);
            Pump();
            Assert(!model.HasSaveError && !model.HasSelection && !model.CanConfirmSelected &&
                Buttons().All(button => ReferenceEquals(button.Cursor, Cursors.Arrow) &&
                    AutomationProperties.GetHelpText(button).Length == 0) && Unchanged(),
                "Reload must clear the local target and its acquisition feedback without replaying a draft.");
            model.SelectCardCommand.Execute(model.Hand.Single(item => item.Id == card.Id));
            var restoredButton = Buttons().Single(button => ((SeatViewModel)button.DataContext).Seat == action.TargetSeat);
            Click(restoredButton);
            Assert(model.CanConfirmSelected && Unchanged(), "The restored published Slash must still prepare the same reversible draft.");
            Click((Button)window.FindName("PlayCardButton"));
            Assert(game.AcceptedCommands.Count == accepted + 1 && game.AcceptedCommands.Last() is PlayCardCommand command &&
                command.CardId == card.Id && command.TargetSeats.SequenceEqual(action.TargetSeats) &&
                !model.Seats.Any(seat => seat.IsSelectedTarget),
                "The actual confirmation button must submit the original card and target exactly once, then clear acquisition feedback.");
            Console.WriteLine($"Target acquisition: native Slash {card.Id}, target={action.TargetSeat}, sizes=1120/1920, injected WPF hover triggers, hidden keyboard focus, reload, accepted={accepted}->{game.AcceptedCommands.Count}.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }
        NativeDecisionRoleLayout(output);

        Button[] Buttons() => Find<Button>(root).Where(button => button.DataContext is SeatViewModel &&
            BattleFeedbackLayer.GetSeatAnchor(button) >= 0).ToArray();
        Rect Bounds(Button button) => button.TransformToAncestor(root).TransformBounds(new Rect(button.RenderSize));
        Border HoverOutline(Button button) => Find<Border>(button).Single(border => border.Name == "SeatHoverOutline");
        string BadgeText(Button button) => Find<TextBlock>(button).Single(text => text.Name == "TargetBadgeText").Text;
        bool Unchanged() => game.AcceptedCommands.Count == accepted && Enumerable.Range(0, views.Length)
            .All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat)));
    }

    private static void NativeDecisionRoleLayout(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new SkillTargetSelectionChecks.TargetFixture("set", 4));
        var engine = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = SkillTargetSelectionChecks.TargetFixture.ModeId, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(new StartGameCommand());
        Accept(new SelectGeneralCommand(0, SkillTargetSelectionChecks.TargetFixture.OwnerId, engine.Revision, engine.PendingDecision!.PromptId));
        for (var step = 0; step < 30 && engine.PendingDecision?.SkillPrompt?.SkillId != SkillTargetSelectionChecks.TargetFixture.SkillId; step++)
            Accept(new AdvanceOneStepCommand(engine.Revision));
        Assert(engine.PendingDecision?.SkillPrompt?.SkillId == SkillTargetSelectionChecks.TargetFixture.SkillId,
            "The existing shared target fixture must reach its actual native mandatory target prompt.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, engine.CreateCheckpoint()));
        using var model = new MainViewModel(false, 31, true, store, useExpandedContent: false, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        engine = Engine(model);
        var accepted = engine.AcceptedCommands.Count;
        var views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(engine.CreateSnapshot(seat))).ToArray();
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            Assert(model.IsSkillTargetSelectionPending && model.CurrentDecisionContext?.TargetSeat == 0,
                "The real skill target prompt must expose its native deciding actor rather than a fabricated PlayCard role.");
            foreach (var width in new[] { 1120, 1920 })
            {
                var height = width == 1120 ? 740 : 1080;
                Render(root, width, height, Path.Combine(output, $"target-feedback-role-before-{width}.png"));
                var human = Find<Button>(root).Single(button => button.DataContext is SeatViewModel { IsHuman: true } &&
                    BattleFeedbackLayer.GetSeatAnchor(button) >= 0);
                var seat = (SeatViewModel)human.DataContext;
                var decision = Find<Border>(human).Single(border => border.Name == "DecisionRoleBadge");
                var beforeBounds = human.TransformToAncestor(root).TransformBounds(new Rect(human.RenderSize));
                Assert(seat.IsLegalTarget && decision.Visibility == Visibility.Visible && seat.DecisionRoleLabel == "正在选择",
                    "The native anyLiving prompt must publish its actor as a candidate alongside the actual visible decision role.");
                Hover(human, true);
                Render(root, width, height, Path.Combine(output, $"target-feedback-role-hover-{width}.png"));
                Assert(Find<TextBlock>(human).Single(text => text.Name == "TargetBadgeText").Text == "点击选择" && Unchanged(),
                    "The actual mandatory target candidate must reveal its acquisition action without submitting the prompt.");
                AssertBadgeLayout(human);
                Click(human);
                Render(root, width, height, Path.Combine(output, $"target-feedback-role-selected-{width}.png"));
                Assert(seat.IsSelectedTarget && decision.Visibility == Visibility.Visible && !model.CanConfirmSelected &&
                    Find<TextBlock>(human).Single(text => text.Name == "TargetBadgeText").Text == "点击取消" && Unchanged(),
                    "The incomplete real target pair must retain both public labels without early confirmation or command submission.");
                AssertBadgeLayout(human);
                Assert(human.TransformToAncestor(root).TransformBounds(new Rect(human.RenderSize)) == beforeBounds,
                    "Reversible target feedback must preserve the deciding actor's original portrait bounds.");
                Click(human);
                Hover(human, false);
                Assert(!seat.IsSelectedTarget && decision.Visibility == Visibility.Visible && Unchanged(),
                    "Cancelling the first native skill target must leave the public decision role and original prompt intact.");
            }
            Console.WriteLine($"Target acquisition public roles: native mandatory prompt, actor=0, sizes=1120/1920, decision={model.Seats.Single(seat => seat.IsHuman).DecisionRoleLabel}, accepted={accepted} unchanged.");
        }
        finally { window.Content = null; window.DataContext = null; window.Close(); }

        void Accept(GameCommand command)
        {
            var result = engine.Submit(command);
            Assert(result.Accepted, result.Error?.Message ?? "The existing fixture command was rejected.");
        }
        bool Unchanged() => engine.AcceptedCommands.Count == accepted && Enumerable.Range(0, 4)
            .All(seat => views[seat] == SnapshotJson.Serialize(engine.CreateSnapshot(seat)));
    }

    private static void AssertBadgeLayout(Button button)
    {
        var target = Find<Border>(button).Single(border => border.Name == "TargetBadge");
        var targetBounds = target.TransformToAncestor(button).TransformBounds(new Rect(target.RenderSize));
        var caption = Find<TextBlock>(target).Single();
        Assert(target.Visibility == Visibility.Visible && caption.ActualWidth + .01 >= caption.DesiredSize.Width &&
            caption.ActualHeight + .01 >= caption.DesiredSize.Height && new Rect(button.RenderSize).Contains(targetBounds),
            "The native target caption must fit fully inside its portrait without clipping.");
        foreach (var badge in Find<Border>(button).Where(item => item != target && item.Visibility == Visibility.Visible &&
                     item.ActualWidth > 0 && item.ActualHeight > 0 && item.Child is TextBlock))
        {
            var bounds = badge.TransformToAncestor(button).TransformBounds(new Rect(badge.RenderSize));
            Assert(!targetBounds.IntersectsWith(bounds), "The target caption overlapped an existing public role, status or equipment badge.");
        }
    }

    private static void Click(Button button) => typeof(Button).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static void Hover(Button button, bool value)
    {
        // Drive the existing WPF input-state dependency property rather than
        // moving the desktop pointer. This verifies the native template triggers;
        // it is not physical mouse or foreground-window acceptance.
        var key = (DependencyPropertyKey)typeof(UIElement).GetField("IsMouseOverPropertyKey", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        if (value) button.SetValue(key, true);
        else button.ClearValue(key);
        Pump();
    }
    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint handle, int command);
    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint handle);
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);
}
