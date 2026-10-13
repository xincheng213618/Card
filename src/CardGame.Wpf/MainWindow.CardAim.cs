using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf;

public partial class MainWindow
{
    private sealed record CardAimGesture(MainViewModel Model, GameSnapshot Snapshot, CardViewModel Card,
        Button Source, Point Start, MainViewModel.PlayCardAimDraft? Draft)
    {
        public bool Started { get; set; }
    }

    private CardAimGesture? _cardAim;
    private MainViewModel? _aimModel;
    private Button? _aimTarget;
    private bool _updatingCardAim;

    private void InitializeCardAim()
    {
        DataContextChanged += CardAimDataContextChanged;
        PreviewMouseMove += CardAimMouseMove;
        PreviewMouseUp += CardAimMouseUp;
        LostMouseCapture += CardAimCaptureLost;
        Deactivated += CardAimInterrupted;
        Unloaded += CardAimInterrupted;
        IsVisibleChanged += (_, _) => { if (!IsVisible) CancelCardAim(); };
        HandViewport.Unloaded += CardAimInterrupted;
        SizeChanged += CardAimSizeChanged;
        HandViewport.ScrollChanged += CardAimHandScrolled;
        ObserveCardAimModel(DataContext as MainViewModel);
    }

    private void CardAimDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        ObserveCardAimModel(e.NewValue as MainViewModel);

    private void ObserveCardAimModel(MainViewModel? model)
    {
        CancelCardAim();
        if (_aimModel is not null)
        {
            _aimModel.PropertyChanged -= CardAimModelChanged;
            _aimModel.Hand.CollectionChanged -= CardAimHandChanged;
        }
        _aimModel = model;
        if (model is not null)
        {
            model.PropertyChanged += CardAimModelChanged;
            model.Hand.CollectionChanged += CardAimHandChanged;
        }
    }

    private void CardAimModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updatingCardAim || _cardAim is not { } gesture) return;
        if (!IsCurrentCardAim(gesture) || gesture.Started && !gesture.Card.IsSelected) CancelCardAim();
    }

    private void CardAimHandChanged(object? sender, NotifyCollectionChangedEventArgs e) => CancelCardAim();
    private void CardAimInterrupted(object? sender, EventArgs e) => CancelCardAim();
    private void CardAimSizeChanged(object sender, SizeChangedEventArgs e) => CancelCardAim();

    private void CardAimHandScrolled(object sender, ScrollChangedEventArgs e)
    {
        if (e.HorizontalChange != 0 || e.ViewportWidthChange != 0) CancelCardAim();
    }

    private void CardAimCaptureLost(object sender, MouseEventArgs e)
    {
        if (_cardAim is { Started: true } && !ReferenceEquals(Mouse.Captured, this)) CancelCardAim();
    }

    private void CardAimMouseMove(object sender, MouseEventArgs e)
    {
        if (_updatingCardAim || _cardAim is null) return;
        if (e.LeftButton != MouseButtonState.Pressed) { CancelCardAim(); return; }
        if (UpdateCardAim(e.GetPosition(CardTargeting))) e.Handled = true;
    }

    private void CardAimMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && ReleaseCardAim(e.GetPosition(CardTargeting))) e.Handled = true;
    }

    // Press remains the ordinary hand click. Crossing the drag threshold turns
    // that reversible selection into an aim; only release can confirm a play.
    internal bool ArmCardAim(HandPanel hand, Point handPoint)
    {
        CancelCardAim();
        if (DataContext is not MainViewModel model || IsTableModalOpen(model) || !IsLoaded || !IsVisible ||
            hand.CardButtonAt(handPoint) is not { IsEnabled: true, IsLoaded: true, IsVisible: true } source ||
            source.DataContext is not CardViewModel card || !model.IsCardAimContext(card) ||
            !ReferenceEquals(source.Command, model.SelectCardCommand) ||
            !ContainsAimPoint(HandViewport, hand.TranslatePoint(handPoint, CardTargeting))) return false;
        _cardAim = new(model, model.ChoiceSnapshot, card, source,
            hand.TranslatePoint(handPoint, CardTargeting), model.CapturePlayCardAimDraft(card));
        return true;
    }

    private bool IsCurrentCardAim(CardAimGesture gesture) =>
        ReferenceEquals(DataContext, gesture.Model) && ReferenceEquals(gesture.Model.ChoiceSnapshot, gesture.Snapshot) &&
        !IsTableModalOpen(gesture.Model) && gesture.Model.IsCardAimContext(gesture.Card) &&
        gesture.Source.IsLoaded && gesture.Source.IsVisible && gesture.Source.IsEnabled &&
        ReferenceEquals(gesture.Source.DataContext, gesture.Card) && IsLoaded && IsVisible;

    internal bool UpdateCardAim(Point point)
    {
        if (_cardAim is not { } gesture) return false;
        if (!IsCurrentCardAim(gesture) || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
        { CancelCardAim(); return false; }
        if (!gesture.Started)
        {
            if (Math.Abs(point.X - gesture.Start.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(point.Y - gesture.Start.Y) < SystemParameters.MinimumVerticalDragDistance) return false;
            _updatingCardAim = true;
            try
            {
                if (gesture.Draft is not null)
                {
                    if (!gesture.Model.RestorePlayCardAimDraft(gesture.Draft)) { CancelCardAim(); return false; }
                }
                else if (!gesture.Card.IsSelected) gesture.Model.SelectCardCommand.Execute(gesture.Card);
            }
            finally { _updatingCardAim = false; }
            if (!IsCurrentCardAim(gesture) || !gesture.Card.IsSelected) { CancelCardAim(); return false; }
            // Capture synchronizes the mouse and may synchronously re-enter
            // PreviewMouseMove before the original move has finished.
            _updatingCardAim = true;
            try
            {
                gesture.Started = true;
                if (!Mouse.Capture(this, CaptureMode.SubTree)) { CancelCardAim(); return false; }
            }
            finally { _updatingCardAim = false; }
        }
        if (!ReferenceEquals(_cardAim, gesture) || !IsCurrentCardAim(gesture) ||
            !ReferenceEquals(Mouse.Captured, this)) { CancelCardAim(); return false; }
        var seatButton = SeatButtonAtAimPoint(point);
        var seat = seatButton?.CommandParameter as SeatViewModel;
        var validTarget = seat is { IsLegalTarget: true } && gesture.Model.Seats.Contains(seat);
        var openTable = seatButton is null && gesture.Model.CanAimCardWithoutTarget(gesture.Card) &&
            ContainsAimPoint(Battlefield, point) && CardTargeting.IsOpenTablePoint(point);
        SetAimTarget(validTarget ? seatButton : null);
        CardTargeting.PointerAimPoint = point;
        CardTargeting.PointerAimAnchor = validTarget ? seatButton : openTable ? FeedbackCenter : null;
        CardTargeting.IsPointerAimValid = validTarget || openTable;
        CardTargeting.PointerAimLabel = validTarget
            ? NeedsExplicitAimConfirmation(gesture.Model) ? "松开选定目标" : "松开出牌"
            : openTable ? "松开出牌"
            : seatButton is not null ? "不能选择该目标"
            : gesture.Model.CanAimCardWithoutTarget(gesture.Card) ? "移到牌桌空处出牌" : "移向亮起的武将";
        return true;
    }

    internal bool ReleaseCardAim(Point point)
    {
        if (_cardAim is not { } gesture) return false;
        if (!gesture.Started) { CancelCardAim(); return false; }
        // Re-read the physical drop and current decision; a previous green
        // hover cannot authorize a later stale release.
        if (!UpdateCardAim(point) || !IsCurrentCardAim(gesture)) { CancelCardAim(); return true; }
        var seat = CardTargeting.IsPointerAimValid ? _aimTarget?.CommandParameter as SeatViewModel : null;
        var openTable = CardTargeting.IsPointerAimValid && _aimTarget is null &&
            gesture.Model.CanAimCardWithoutTarget(gesture.Card);
        var explicitConfirmation = NeedsExplicitAimConfirmation(gesture.Model);
        CancelCardAim();
        if (seat is { IsLegalTarget: true } && gesture.Model.Seats.Contains(seat))
        {
            if (!seat.IsSelectedTarget) gesture.Model.SelectTargetCommand.Execute(seat);
            if (!explicitConfirmation && !NeedsExplicitAimConfirmation(gesture.Model) &&
                gesture.Model.IsCardAimContext(gesture.Card) && gesture.Card.IsSelected &&
                gesture.Model.ConfirmSelectedCommand.CanExecute(null))
                gesture.Model.ConfirmSelectedCommand.Execute(null);
        }
        else if (openTable && gesture.Model.CanAimCardWithoutTarget(gesture.Card) &&
                 gesture.Model.ConfirmSelectedCommand.CanExecute(null))
            gesture.Model.ConfirmSelectedCommand.Execute(null);
        return true;
    }

    private static bool NeedsExplicitAimConfirmation(MainViewModel model) =>
        model.IsMultiTargetCardSelected || model.HasTargetCombinationChoices || model.HasPublicTargetChoices ||
        model.HasAlternateSlash;

    internal void CancelCardAim()
    {
        _cardAim = null;
        SetAimTarget(null);
        CardTargeting.PointerAimPoint = null;
        CardTargeting.PointerAimAnchor = null;
        CardTargeting.IsPointerAimValid = false;
        CardTargeting.PointerAimLabel = string.Empty;
        if (ReferenceEquals(Mouse.Captured, this)) Mouse.Capture(null);
    }

    private void SetAimTarget(Button? target)
    {
        if (ReferenceEquals(_aimTarget, target)) return;
        if (_aimTarget is not null) CardTargetingLayer.SetIsAimTarget(_aimTarget, false);
        _aimTarget = target;
        if (target is not null) CardTargetingLayer.SetIsAimTarget(target, true);
    }

    private bool ContainsAimPoint(FrameworkElement element, Point point)
    {
        if (!element.IsVisible || element.ActualWidth <= 0 || element.ActualHeight <= 0) return false;
        var local = CardTargeting.TranslatePoint(point, element);
        return new Rect(new Size(element.ActualWidth, element.ActualHeight)).Contains(local);
    }

    private Button? SeatButtonAtAimPoint(Point point)
    {
        if (DataContext is not MainViewModel model || !ContainsAimPoint(GameArea, point)) return null;
        var node = GameArea.InputHitTest(CardTargeting.TranslatePoint(point, GameArea)) as DependencyObject;
        while (node is not null && !ReferenceEquals(node, GameArea))
        {
            if (node is Button button && ReferenceEquals(button.Command, model.SelectTargetCommand) &&
                button.CommandParameter is SeatViewModel seat && model.Seats.Contains(seat)) return button;
            node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return null;
    }
}
