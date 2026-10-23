using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using CardGame.Wpf.Persistence;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private bool _isShanheCodexLoadoutManagerOpen;
    private string _shanheCodexLoadoutName = "";
    private string? _presentedCodexLoadoutId;
    public ObservableCollection<ShanheCodexLoadoutOption> ShanheCodexLoadoutPlans { get; } = [];
    public string ShanheCodexSelectedLoadoutName => _shanheCodexProfile.LoadoutPlans?
        .FirstOrDefault(plan => plan.Id == _shanheCodexProfile.SelectedLoadoutPlanId)?.Name ?? "方案1";
    public string ShanheCodexLoadoutName
    {
        get => _shanheCodexLoadoutName;
        set
        {
            if (SetProperty(ref _shanheCodexLoadoutName, value ?? ""))
                (RenameShanheCodexLoadoutCommand as RelayCommand)?.NotifyCanExecuteChanged();
        }
    }
    public bool IsShanheCodexLoadoutManagerOpen
    {
        get => _isShanheCodexLoadoutManagerOpen;
        private set => SetProperty(ref _isShanheCodexLoadoutManagerOpen, value);
    }
    public ICommand OpenShanheCodexLoadoutManagerCommand { get; private set; } = null!;
    public ICommand CloseShanheCodexLoadoutManagerCommand { get; private set; } = null!;
    public ICommand SelectShanheCodexLoadoutCommand { get; private set; } = null!;
    public ICommand NewShanheCodexLoadoutCommand { get; private set; } = null!;
    public ICommand CopyShanheCodexLoadoutCommand { get; private set; } = null!;
    public ICommand RenameShanheCodexLoadoutCommand { get; private set; } = null!;
    public ICommand DeleteShanheCodexLoadoutCommand { get; private set; } = null!;
    private bool CanEditShanheCodexLoadouts => CanPrepareShanheExpedition && !_shanheCodexReadFailed;

    private void InitializeShanheCodexLoadouts()
    {
        OpenShanheCodexLoadoutManagerCommand = new RelayCommand(() =>
        {
            if (!CanEditShanheCodexLoadouts) return;
            IsShanheCodexPickerOpen = false;
            ShanheCodexLoadoutName = ShanheCodexSelectedLoadoutName;
            IsShanheCodexLoadoutManagerOpen = true;
        }, () => CanEditShanheCodexLoadouts);
        CloseShanheCodexLoadoutManagerCommand = new RelayCommand(() => IsShanheCodexLoadoutManagerOpen = false);
        SelectShanheCodexLoadoutCommand = new RelayCommand<ShanheCodexLoadoutOption>(plan =>
        {
            if (!SelectShanheCodexLoadoutCommand.CanExecute(plan) || plan is null) return;
            ApplyShanheCodexLoadout(() => ShanheCodexLoadouts.Select(_shanheCodexProfile, plan.Id),
                $"已切换至{plan.Name}，下次出征生效。");
        }, plan => CanEditShanheCodexLoadouts && plan is not null && !plan.IsSelected && ShanheCodexLoadoutPlans.Contains(plan));
        NewShanheCodexLoadoutCommand = new RelayCommand(() => CreateShanheCodexLoadout(copyActive: false), CanAddShanheCodexLoadout);
        CopyShanheCodexLoadoutCommand = new RelayCommand(() => CreateShanheCodexLoadout(copyActive: true), CanAddShanheCodexLoadout);
        RenameShanheCodexLoadoutCommand = new RelayCommand(() =>
        {
            if (!CanRenameShanheCodexLoadout()) return;
            ApplyShanheCodexLoadout(() => ShanheCodexLoadouts.Rename(_shanheCodexProfile,
                _shanheCodexProfile.SelectedLoadoutPlanId!, ShanheCodexLoadoutName), "方案名称已保存。");
        }, CanRenameShanheCodexLoadout);
        DeleteShanheCodexLoadoutCommand = new RelayCommand(() =>
        {
            if (!CanDeleteShanheCodexLoadout()) return;
            ApplyShanheCodexLoadout(() => ShanheCodexLoadouts.Delete(_shanheCodexProfile,
                _shanheCodexProfile.SelectedLoadoutPlanId!), $"已删除{ShanheCodexSelectedLoadoutName}。");
        }, CanDeleteShanheCodexLoadout);
    }

    private bool CanAddShanheCodexLoadout() => CanEditShanheCodexLoadouts &&
        _shanheCodexProfile.LoadoutPlans is { Length: < ShanheCodexLoadouts.MaximumPlans };
    private bool CanDeleteShanheCodexLoadout() => CanEditShanheCodexLoadouts && _shanheCodexProfile.LoadoutPlans is { Length: > 1 };
    private bool CanRenameShanheCodexLoadout()
    {
        var name = ShanheCodexLoadoutName.Trim();
        return CanEditShanheCodexLoadouts && name.Length is > 0 and <= ShanheCodexLoadouts.MaximumNameLength &&
            !ShanheCodexLoadoutName.Any(char.IsControl) && name != ShanheCodexSelectedLoadoutName &&
            !_shanheCodexProfile.LoadoutPlans!.Any(plan => plan.Id != _shanheCodexProfile.SelectedLoadoutPlanId &&
                string.Equals(plan.Name, name, StringComparison.OrdinalIgnoreCase));
    }
    private void CreateShanheCodexLoadout(bool copyActive)
    {
        if (!CanAddShanheCodexLoadout()) return;
        var names = _shanheCodexProfile.LoadoutPlans!.Select(plan => plan.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var name = Enumerable.Range(1, ShanheCodexLoadouts.MaximumPlans + 1)
            .Select(index => "方案" + index.ToString(CultureInfo.InvariantCulture)).First(candidate => !names.Contains(candidate));
        ApplyShanheCodexLoadout(() => ShanheCodexLoadouts.Create(_shanheCodexProfile, name, copyActive),
            copyActive ? "当前构筑已保存为独立方案，可继续调整。" : "已新建空方案，可安排六个初始战法。");
    }
    private void ApplyShanheCodexLoadout(Func<ShanheCodexProfile> prepare, string message)
    {
        if (!CanEditShanheCodexLoadouts) return;
        try
        {
            if (SaveCodexProfile(prepare(), message))
            {
                IsShanheCodexPickerOpen = false;
                ShanheCodexLoadoutName = ShanheCodexSelectedLoadoutName;
            }
        }
        catch (Exception error) when (IsSaveException(error))
        { ShanheCodexMessage = $"方案操作未生效：{ReadableSaveError(error)}"; }
    }
    private void RefreshShanheCodexLoadouts(IReadOnlySet<string> knownRewards)
    {
        ShanheCodexLoadoutPlans.Clear();
        foreach (var plan in _shanheCodexProfile.LoadoutPlans ?? [])
        {
            var count = plan.EquippedTacticIds.OfType<string>().Count();
            var available = plan.EquippedTacticIds.OfType<string>().Count(knownRewards.Contains);
            ShanheCodexLoadoutPlans.Add(new(plan.Id, plan.Name,
                $"已装备 {count}/6 · 本篇可用 {available} 项", plan.Id == _shanheCodexProfile.SelectedLoadoutPlanId));
        }
        if (_presentedCodexLoadoutId != _shanheCodexProfile.SelectedLoadoutPlanId)
        {
            _presentedCodexLoadoutId = _shanheCodexProfile.SelectedLoadoutPlanId;
            ShanheCodexLoadoutName = ShanheCodexSelectedLoadoutName;
        }
        RaisePropertyChanged(nameof(ShanheCodexSelectedLoadoutName));
        foreach (var command in new[] { OpenShanheCodexLoadoutManagerCommand, NewShanheCodexLoadoutCommand,
            CopyShanheCodexLoadoutCommand, RenameShanheCodexLoadoutCommand, DeleteShanheCodexLoadoutCommand }.OfType<RelayCommand>())
            command.NotifyCanExecuteChanged();
        (SelectShanheCodexLoadoutCommand as RelayCommand<ShanheCodexLoadoutOption>)?.NotifyCanExecuteChanged();
    }
}

public sealed record ShanheCodexLoadoutOption(string Id, string Name, string Summary, bool IsSelected);
