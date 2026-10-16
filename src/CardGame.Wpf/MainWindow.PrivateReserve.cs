using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf;

public partial class MainWindow
{
    private Point _reserveDragStart;
    private Button? _reserveDragButton;

    private void ReserveCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _reserveDragButton = sender as Button;
        _reserveDragStart = e.GetPosition(this);
    }

    private void ReserveCard_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button button ||
            !ReferenceEquals(button, _reserveDragButton) || button.CommandParameter is not ReserveCardViewModel card ||
            DataContext is not MainViewModel model || !model.MoveReserveCardCommand.CanExecute(card)) return;
        var point = e.GetPosition(this);
        if (Math.Abs(point.X - _reserveDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(point.Y - _reserveDragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _reserveDragButton = null;
        button.ReleaseMouseCapture();
        DragDrop.DoDragDrop(button, new DataObject(typeof(ReserveCardViewModel), card), DragDropEffects.Move);
        e.Handled = true;
    }

    private bool CanDropReserveCard(object sender, DragEventArgs e, out ReserveCardViewModel? card)
    {
        card = e.Data.GetData(typeof(ReserveCardViewModel)) as ReserveCardViewModel;
        if (card is null || DataContext is not MainViewModel model ||
            !model.MoveReserveCardCommand.CanExecute(card) || sender is not FrameworkElement row) return false;
        var destination = row.Tag as string == "stars" ? model.ReserveDialogStars : model.ReserveDialogHand;
        var draggedCard = card;
        return !destination.Any(current => ReferenceEquals(current, draggedCard));
    }

    private void ReserveRow_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = CanDropReserveCard(sender, e, out _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void ReserveRow_Drop(object sender, DragEventArgs e)
    {
        if (CanDropReserveCard(sender, e, out var card) && DataContext is MainViewModel model)
            model.MoveReserveCardCommand.Execute(card);
        e.Handled = true;
    }
}
