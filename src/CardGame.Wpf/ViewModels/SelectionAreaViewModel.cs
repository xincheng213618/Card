using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

/// <summary>One prepared view of the lower action area. Commands keep their native validation.</summary>
public sealed record SelectionAreaViewModel
{
    public string Title { get; init; } = string.Empty;
    public string Hint { get; init; } = string.Empty;
    public string ConfirmText { get; init; } = string.Empty;
    public string CancelText { get; init; } = string.Empty;
    public bool CanConfirm { get; init; }
    public bool CanCancel { get; init; }
    public bool ShowConfirm { get; init; }
    public bool ShowEndTurn { get; init; }
    public bool ShowPrimaryButtons { get; init; }
    public ICommand ConfirmCommand { get; init; } = null!;
    public ICommand CancelCommand { get; init; } = null!;
    public IReadOnlyList<PromptChoice> Choices { get; init; } = [];
    public ICommand ChoiceCommand { get; init; } = null!;
    public bool HasChoices => Choices.Count > 0;
    public IReadOnlyList<PromptChoice> CatalogChoices { get; init; } = [];
    public ICommand CatalogCommand { get; init; } = null!;
    public string CatalogTitle { get; init; } = string.Empty;
    public bool HasCatalogChoices => CatalogChoices.Count > 0;
}
