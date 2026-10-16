using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

/// <summary>A separate hit target so marking a hidden identity never selects a card target.</summary>
public sealed class IdentityMarkButton : Button
{
    public IdentityMarkButton()
    {
        ContextMenu = new ContextMenu { Placement = PlacementMode.Bottom, MinWidth = 112 };
        DataContextChanged += (_, args) =>
        {
            CloseMenu();
            if (args.OldValue is SeatViewModel oldSeat)
                PropertyChangedEventManager.RemoveHandler(oldSeat, SeatChanged, string.Empty);
            if (args.NewValue is SeatViewModel newSeat)
                PropertyChangedEventManager.AddHandler(newSeat, SeatChanged, string.Empty);
        };
        Unloaded += (_, _) => CloseMenu();
    }

    protected override void OnClick()
    {
        base.OnClick();
        OpenIdentityMenu();
    }

    public void OpenIdentityMenu()
    {
        if (!PrepareMenu()) return;
        ContextMenu!.IsOpen = true;
    }

    protected override void OnContextMenuOpening(ContextMenuEventArgs e)
    {
        if (!PrepareMenu()) e.Handled = true;
        base.OnContextMenuOpening(e);
    }

    private bool PrepareMenu()
    {
        if (DataContext is not SeatViewModel { CanMarkIdentity: true } seat) return false;
        var menu = ContextMenu!;
        menu.Items.Clear();
        menu.PlacementTarget = this;
        foreach (var choice in seat.IdentityMarkChoices)
            menu.Items.Add(new MenuItem
            {
                Header = choice.Label, Command = choice.Command,
                IsCheckable = true, IsChecked = choice.IsSelected
            });
        return true;
    }

    private void SeatChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SeatViewModel.IdentityMarkChoices) ||
            DataContext is not SeatViewModel { CanMarkIdentity: true }) CloseMenu();
    }

    private void CloseMenu()
    {
        if (ContextMenu is { } menu) menu.IsOpen = false;
    }
}
