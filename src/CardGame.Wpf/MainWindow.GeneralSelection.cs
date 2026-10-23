using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf;

public partial class MainWindow
{
    private MainViewModel? _generalPreviewScrollModel;
    private string? _displayedGeneralPreviewId;
    private PromptId? _displayedGeneralPromptId;
    private bool _generalPreviewScrollQueued;

    private void InitializeGeneralPreviewScroll()
    {
        DataContextChanged += (_, args) => ObserveGeneralPreviewScroll(args.NewValue as MainViewModel);
        Loaded += (_, _) => QueueGeneralPreviewScroll();
        Closed += (_, _) => ObserveGeneralPreviewScroll(null);
        ObserveGeneralPreviewScroll(DataContext as MainViewModel);
    }

    private void ObserveGeneralPreviewScroll(MainViewModel? model)
    {
        if (ReferenceEquals(_generalPreviewScrollModel, model)) return;
        if (_generalPreviewScrollModel is not null)
            PropertyChangedEventManager.RemoveHandler(_generalPreviewScrollModel, GeneralPreviewChanged,
                nameof(MainViewModel.SelectedGeneralChoice));
        _generalPreviewScrollModel = model;
        _displayedGeneralPreviewId = null;
        _displayedGeneralPromptId = null;
        if (model is null) return;
        PropertyChangedEventManager.AddHandler(model, GeneralPreviewChanged, nameof(MainViewModel.SelectedGeneralChoice));
        QueueGeneralPreviewScroll();
    }

    private void GeneralPreviewChanged(object? sender, PropertyChangedEventArgs args)
    {
        // A replacement match can reuse the same numeric prompt ID. Remember
        // the empty selection even when its next prompt arrives before layout.
        if (_generalPreviewScrollModel?.SelectedGeneralChoice is null) _displayedGeneralPromptId = null;
        QueueGeneralPreviewScroll();
    }

    private void QueueGeneralPreviewScroll()
    {
        if (_generalPreviewScrollQueued || !IsLoaded) return;
        _generalPreviewScrollQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _generalPreviewScrollQueued = false;
            if (!IsLoaded || _generalPreviewScrollModel is not { } model || !ReferenceEquals(DataContext, model)) return;
            // A snapshot refresh temporarily visits candidate previews while
            // rebinding them. Compare the final edition after bindings settle.
            var generalId = model.SelectedGeneralChoice?.GeneralId;
            var promptId = model.IsGeneralSelectionPending ? model.ChoiceSnapshot.PendingDecision?.PromptId : null;
            if (_displayedGeneralPromptId != promptId)
            {
                _displayedGeneralPromptId = promptId;
                GeneralCandidateScroller.ScrollToHome();
            }
            if (_displayedGeneralPreviewId == generalId) return;
            _displayedGeneralPreviewId = generalId;
            GeneralSelectionSkillsScroller.ScrollToTop();
        }));
    }
}
