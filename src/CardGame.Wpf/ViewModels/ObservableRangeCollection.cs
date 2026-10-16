using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace CardGame.Wpf.ViewModels;

/// <summary>Publishes a complete filtered presentation with one collection reset.</summary>
public sealed class ObservableRangeCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> values)
    {
        var replacement = values.ToArray();
        if (this.SequenceEqual(replacement)) return;
        CheckReentrancy();
        Items.Clear();
        foreach (var value in replacement) Items.Add(value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
