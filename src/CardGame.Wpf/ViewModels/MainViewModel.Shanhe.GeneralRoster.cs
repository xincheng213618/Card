using System.Windows.Input;
using CardGame.Content.Standard;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private const int ShanheGeneralPageSize = 8;
    private string _shanheGeneralSearch = string.Empty;
    private int _shanheGeneralPageIndex;
    private ShanheGeneralOption[] _filteredShanheGenerals = [];
    private bool _shanheGeneralRosterReady;

    public ObservableRangeCollection<ShanheGeneralOption> ShanheGeneralPage { get; } = [];
    public string ShanheGeneralSearch
    {
        get => _shanheGeneralSearch;
        set
        {
            if (!SetProperty(ref _shanheGeneralSearch, value ?? string.Empty)) return;
            _shanheGeneralPageIndex = 0;
            FilterShanheGenerals();
        }
    }
    public string ShanheGeneralCountText => $"{_filteredShanheGenerals.Length} / {ShanheGenerals.Count} 名武将";
    public string ShanheGeneralPageText => $"第 {_shanheGeneralPageIndex + 1} / {ShanheGeneralPageCount} 页";
    public bool HasNoShanheGeneralResults => _filteredShanheGenerals.Length == 0;
    public ICommand PreviousShanheGeneralPageCommand { get; private set; } = null!;
    public ICommand NextShanheGeneralPageCommand { get; private set; } = null!;
    public ICommand ClearShanheGeneralSearchCommand { get; private set; } = null!;
    private int ShanheGeneralPageCount => Math.Max(1, (_filteredShanheGenerals.Length + ShanheGeneralPageSize - 1) / ShanheGeneralPageSize);

    private void InitializeShanheGeneralRoster()
    {
        PreviousShanheGeneralPageCommand = new RelayCommand(() => ChangeShanheGeneralPage(-1), () => _shanheGeneralPageIndex > 0);
        NextShanheGeneralPageCommand = new RelayCommand(() => ChangeShanheGeneralPage(1), () => _shanheGeneralPageIndex + 1 < ShanheGeneralPageCount);
        ClearShanheGeneralSearchCommand = new RelayCommand(() => ShanheGeneralSearch = string.Empty);
    }

    private void RefreshShanheGeneralRoster()
    {
        var selectedId = IsShanheRunInProgress ? _shanheSession?.Snapshot.PlayerGeneralId : SelectedShanheGeneral?.Id;
        if (!_shanheGeneralRosterReady)
        {
            var registry = StandardShanheCampaignCatalog.PlayerRosterRegistry;
            ShanheGenerals.ReplaceAll(StandardShanheCampaignCatalog.GetPlayerGeneralIds().Select(id =>
            {
                var general = registry.Generals[id];
                var skills = general.SkillIds.Select(registry.GetSkill).ToArray();
                return new ShanheGeneralOption(id, general.Name)
                {
                    Kingdom = FactionName(general.FactionId),
                    SkillSummary = string.Join(" · ", skills.Select(skill => skill.Name)),
                    Description = string.Join("\n\n", skills.Select(skill => $"{skill.Name}：{GetVisibleSkillDescription(skill)}"))
                };
            }));
            _shanheGeneralRosterReady = true;
            FilterShanheGenerals();
        }
        SelectedShanheGeneral = ShanheGenerals.FirstOrDefault(general => general.Id == selectedId) ?? ShanheGenerals.FirstOrDefault();
    }

    private void FilterShanheGenerals()
    {
        var terms = ShanheGeneralSearch.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _filteredShanheGenerals = ShanheGenerals.Where(general => terms.All(term =>
            general.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase))).ToArray();
        RefreshShanheGeneralPage();
        RaisePropertyChanged(nameof(ShanheGeneralCountText));
        RaisePropertyChanged(nameof(HasNoShanheGeneralResults));
    }

    private void ChangeShanheGeneralPage(int direction)
    {
        var page = Math.Clamp(_shanheGeneralPageIndex + direction, 0, ShanheGeneralPageCount - 1);
        if (page == _shanheGeneralPageIndex) return;
        _shanheGeneralPageIndex = page;
        RefreshShanheGeneralPage();
    }

    private void RefreshShanheGeneralPage()
    {
        _shanheGeneralPageIndex = Math.Clamp(_shanheGeneralPageIndex, 0, ShanheGeneralPageCount - 1);
        ShanheGeneralPage.ReplaceAll(_filteredShanheGenerals.Skip(_shanheGeneralPageIndex * ShanheGeneralPageSize).Take(ShanheGeneralPageSize));
        // Reassert the same selected object after ItemsSource reset clears its visual row.
        RaisePropertyChanged(nameof(SelectedShanheGeneral));
        RaisePropertyChanged(nameof(ShanheGeneralPageText));
        (PreviousShanheGeneralPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (NextShanheGeneralPageCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    private void RevealSelectedShanheGeneral()
    {
        var index = Array.IndexOf(_filteredShanheGenerals, SelectedShanheGeneral);
        if (index < 0 || index / ShanheGeneralPageSize == _shanheGeneralPageIndex) return;
        _shanheGeneralPageIndex = index / ShanheGeneralPageSize;
        RefreshShanheGeneralPage();
    }

    public void PrepareShanheGeneralSelection()
    {
        ShanheGeneralSearch = string.Empty;
        RevealSelectedShanheGeneral();
    }
}
