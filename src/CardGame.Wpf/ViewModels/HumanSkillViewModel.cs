namespace CardGame.Wpf.ViewModels;

public sealed record HumanSkillViewModel(
    string Name,
    string Description,
    string TypeText,
    string StateText,
    string SourceText,
    bool IsAvailable,
    bool IsDisabled);
