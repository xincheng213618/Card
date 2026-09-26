using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly List<GeneralGalleryEntryViewModel> _allGeneralGalleryEntries = [];
    private bool _isGeneralGalleryOpen;
    private string _generalGallerySearchText = string.Empty;
    private string _selectedGeneralGalleryFaction = "all";
    private string _selectedGeneralGallerySeries = "all";
    private string _selectedGeneralGalleryGroup = "all";
    private GeneralGalleryEntryViewModel? _selectedGeneralGalleryEntry;

    public ObservableCollection<GeneralGalleryEntryViewModel> GeneralGalleryEntries { get; } = [];
    public ObservableCollection<GeneralGalleryGroupViewModel> GeneralGalleryGroups { get; } = [];
    public ObservableCollection<GeneralGalleryFactionOption> GeneralGallerySubgroups { get; } = [];

    public IReadOnlyList<GeneralGalleryFactionOption> GeneralGalleryFactions { get; } =
    [
        new("all", "全部势力"), new("wei", "魏"), new("shu", "蜀"),
        new("wu", "吴"), new("qun", "群"), new("god", "神")
    ];

    public IReadOnlyList<GeneralGallerySeriesOption> GeneralGallerySeries { get; } =
        GeneralGalleryCatalog.Series.Select(series =>
            new GeneralGallerySeriesOption(series.Id, series.Name, series.Description)).ToArray();

    public bool IsGeneralGalleryOpen
    {
        get => _isGeneralGalleryOpen;
        set
        {
            if (!SetProperty(ref _isGeneralGalleryOpen, value)) return;
            if (value) RefreshGeneralGallery();
            else
            {
                GeneralGalleryEntries.Clear();
                GeneralGalleryGroups.Clear();
                SelectedGeneralGalleryEntry = null;
                RaisePropertyChanged(nameof(GeneralGalleryCountText));
            }
        }
    }

    public string GeneralGallerySearchText
    {
        get => _generalGallerySearchText;
        set { if (SetProperty(ref _generalGallerySearchText, value)) RefreshGeneralGallery(); }
    }

    public string SelectedGeneralGalleryFaction
    {
        get => _selectedGeneralGalleryFaction;
        private set { if (SetProperty(ref _selectedGeneralGalleryFaction, value)) RefreshGeneralGallery(); }
    }

    public string SelectedGeneralGallerySeries
    {
        get => _selectedGeneralGallerySeries;
        private set
        {
            if (!SetProperty(ref _selectedGeneralGallerySeries, value)) return;
            _selectedGeneralGalleryGroup = "all";
            RaisePropertyChanged(nameof(SelectedGeneralGalleryGroup));
            RaisePropertyChanged(nameof(GeneralGallerySeriesDescription));
            RefreshGeneralGallery();
        }
    }

    public string SelectedGeneralGalleryGroup
    {
        get => _selectedGeneralGalleryGroup;
        private set { if (SetProperty(ref _selectedGeneralGalleryGroup, value)) RefreshGeneralGallery(); }
    }

    public GeneralGalleryEntryViewModel? SelectedGeneralGalleryEntry
    {
        get => _selectedGeneralGalleryEntry;
        private set
        {
            if (SetProperty(ref _selectedGeneralGalleryEntry, value))
                RaisePropertyChanged(nameof(HasGeneralGallerySelection));
        }
    }

    public bool HasGeneralGallerySelection => SelectedGeneralGalleryEntry is not null;
    public bool HasGeneralGallerySubgroups => GeneralGallerySubgroups.Count > 2;
    public bool HasNoGeneralGalleryResults => GeneralGalleryEntries.Count == 0 && GeneralGalleryGroups.Count == 0;
    public string GeneralGallerySeriesDescription =>
        GeneralGallerySeries.First(option => option.Id == SelectedGeneralGallerySeries).Description;
    public string GeneralGalleryCountText => $"{GeneralGalleryEntries.Count} / {_allGeneralGalleryEntries.Count} 名武将";

    public ICommand OpenGeneralGalleryCommand { get; private set; } = null!;
    public ICommand CloseGeneralGalleryCommand { get; private set; } = null!;
    public ICommand SelectGeneralGalleryFactionCommand { get; private set; } = null!;
    public ICommand SelectGeneralGallerySeriesCommand { get; private set; } = null!;
    public ICommand SelectGeneralGalleryGroupCommand { get; private set; } = null!;
    public ICommand SelectGeneralGalleryEntryCommand { get; private set; } = null!;
    public ICommand CloseGeneralGalleryDetailsCommand { get; private set; } = null!;
    public ICommand ClearGeneralGalleryFiltersCommand { get; private set; } = null!;

    private void InitializeGeneralGallery()
    {
        InitializeGeneralSkins();
        OpenGeneralGalleryCommand = new RelayCommand(() =>
        {
            IsHelpOpen = false;
            IsHistoryOpen = false;
            IsGeneralGalleryOpen = true;
        });
        CloseGeneralGalleryCommand = new RelayCommand(() => IsGeneralGalleryOpen = false);
        SelectGeneralGalleryFactionCommand = new RelayCommand<string>(faction =>
        {
            if (GeneralGalleryFactions.Any(option => option.Id == faction)) SelectedGeneralGalleryFaction = faction;
        });
        SelectGeneralGallerySeriesCommand = new RelayCommand<string>(series =>
        {
            if (GeneralGallerySeries.Any(option => option.Id == series)) SelectedGeneralGallerySeries = series;
        });
        SelectGeneralGalleryGroupCommand = new RelayCommand<string>(group =>
        {
            if (GeneralGallerySubgroups.Any(option => option.Id == group)) SelectedGeneralGalleryGroup = group;
        });
        SelectGeneralGalleryEntryCommand = new RelayCommand<GeneralGalleryEntryViewModel>(entry =>
        {
            if (GeneralGalleryEntries.Contains(entry))
            {
                GeneralDetailsTab = "skills";
                SelectedGeneralGalleryEntry = entry;
            }
        });
        CloseGeneralGalleryDetailsCommand = new RelayCommand(() => SelectedGeneralGalleryEntry = null);
        ClearGeneralGalleryFiltersCommand = new RelayCommand(() =>
        {
            _generalGallerySearchText = string.Empty;
            _selectedGeneralGalleryFaction = "all";
            RaisePropertyChanged(nameof(GeneralGallerySearchText));
            RaisePropertyChanged(nameof(SelectedGeneralGalleryFaction));
            RefreshGeneralGallery();
        });

        foreach (var general in _contentRegistry.Generals.Values.Where(general => GeneralGalleryCatalog.IsVisible(general.Id)))
        {
            var skills = general.SkillIds.Select(_contentRegistry.GetSkill).ToArray();
            var group = GeneralGalleryCatalog.Classify(general.Id);
            var factionId = group.SeriesId == "god" ? "god" : general.FactionId ?? string.Empty;
            _allGeneralGalleryEntries.Add(new GeneralGalleryEntryViewModel
            {
                GeneralId = general.Id,
                Portrait = GetGeneralPortrait(general.Id),
                Skills = skills.Select(skill => new GeneralGallerySkill(skill.Name, GetVisibleSkillDescription(skill))).ToArray(),
                SeriesId = group.SeriesId,
                SeriesName = GeneralGallerySeries.First(option => option.Id == group.SeriesId).Name,
                GroupId = group.Id,
                GroupName = group.Title,
                Name = general.Name,
                FactionId = factionId,
                Kingdom = factionId == "god" ? "神" : FactionName(general.FactionId),
                HealthText = $"{general.BaseHp} 体力",
                HealthImages = Enumerable.Repeat($"pack://application:,,,/CardGame.Wpf;component/Assets/gallery-hp-{(factionId is "wei" or "shu" or "wu" ? factionId : "qun")}.png", Math.Clamp(general.BaseHp, 0, 12)).ToArray(),
                SkillName = string.Join(" / ", skills.Select(skill => skill.Name)),
                SkillDescription = string.Join("\n\n", skills.Select(skill => $"{skill.Name}：{GetVisibleSkillDescription(skill)}"))
            });
        }
    }

    private void RefreshGeneralGallery()
    {
        if (!IsGeneralGalleryOpen) return;
        var query = GeneralGallerySearchText.Trim();
        var filtered = query.Length > 0 || SelectedGeneralGalleryFaction != "all";
        var seriesGroups = GeneralGalleryCatalog.Groups
            .Where(group => SelectedGeneralGallerySeries == "all" || group.SeriesId == SelectedGeneralGallerySeries)
            .ToArray();
        GeneralGalleryFactionOption[] subgroups = [new("all", "全部"),
            .. seriesGroups.Where(_ => SelectedGeneralGallerySeries != "all")
                .Select(group => new GeneralGalleryFactionOption(group.Id, group.Name))];
        if (!GeneralGallerySubgroups.SequenceEqual(subgroups))
        {
            GeneralGallerySubgroups.Clear();
            foreach (var subgroup in subgroups) GeneralGallerySubgroups.Add(subgroup);
        }

        var entries = _allGeneralGalleryEntries
            .Where(entry => SelectedGeneralGallerySeries == "all" || entry.SeriesId == SelectedGeneralGallerySeries)
            .Where(entry => SelectedGeneralGalleryGroup == "all" || entry.GroupId == SelectedGeneralGalleryGroup)
            .Where(entry => SelectedGeneralGalleryFaction == "all" || entry.FactionId == SelectedGeneralGalleryFaction)
            .Where(entry => query.Length == 0 || entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.SkillName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.SkillDescription.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => GalleryFactionOrder(entry.FactionId))
            .ThenBy(entry => entry.Name, StringComparer.Ordinal).ToArray();

        GeneralGalleryEntries.Clear();
        GeneralGalleryGroups.Clear();
        foreach (var group in seriesGroups.Where(group => SelectedGeneralGalleryGroup == "all" || group.Id == SelectedGeneralGalleryGroup))
        {
            var members = entries.Where(entry => entry.GroupId == group.Id).ToArray();
            if (members.Length == 0 && (SelectedGeneralGallerySeries == "all" || filtered)) continue;
            foreach (var member in members) GeneralGalleryEntries.Add(member);
            GeneralGalleryGroups.Add(new(group.Id, group.Title, members, "当前版本暂无此分组武将"));
        }
        if (SelectedGeneralGalleryEntry is not null && !entries.Contains(SelectedGeneralGalleryEntry))
            SelectedGeneralGalleryEntry = null;
        foreach (var name in new[] { nameof(GeneralGalleryCountText), nameof(HasGeneralGallerySubgroups), nameof(HasNoGeneralGalleryResults) })
            RaisePropertyChanged(name);
    }

    private static int GalleryFactionOrder(string faction) => faction switch
    {
        "shu" => 0, "wu" => 1, "wei" => 2, "qun" => 3, "god" => 4, _ => 5
    };
}
