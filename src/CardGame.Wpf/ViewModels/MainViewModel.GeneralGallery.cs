using System.Collections.ObjectModel;
using System.Windows.Input;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly List<GeneralGalleryEntryViewModel> _allGeneralGalleryEntries = [];
    private bool _isGeneralGalleryOpen;
    private string _generalGallerySearchText = string.Empty;
    private string _selectedGeneralGalleryFaction = "all";
    private string _selectedGeneralGallerySeries = "all";

    public ObservableCollection<GeneralGalleryEntryViewModel> GeneralGalleryEntries { get; } = [];

    public IReadOnlyList<GeneralGalleryFactionOption> GeneralGalleryFactions { get; } =
    [
        new("all", "全部"),
        new("wei", "魏"),
        new("shu", "蜀"),
        new("wu", "吴"),
        new("qun", "群")
    ];

    public IReadOnlyList<GeneralGallerySeriesOption> GeneralGallerySeries { get; } =
    [
        new("all", "全部系列", "全部已注册武将"),
        new("classic", "经典标准", "经典身份局正式武将"),
        new("standard", "机制演示", "基础规则与技能演示武将"),
        new("national", "国战试验", "当前简化国战与野心家试验武将")
    ];

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
                RaisePropertyChanged(nameof(GeneralGalleryCountText));
            }
        }
    }

    public string GeneralGallerySearchText
    {
        get => _generalGallerySearchText;
        set
        {
            if (SetProperty(ref _generalGallerySearchText, value)) RefreshGeneralGallery();
        }
    }

    public string SelectedGeneralGalleryFaction
    {
        get => _selectedGeneralGalleryFaction;
        private set
        {
            if (SetProperty(ref _selectedGeneralGalleryFaction, value)) RefreshGeneralGallery();
        }
    }

    public string SelectedGeneralGallerySeries
    {
        get => _selectedGeneralGallerySeries;
        private set
        {
            if (SetProperty(ref _selectedGeneralGallerySeries, value)) RefreshGeneralGallery();
        }
    }

    public string GeneralGallerySeriesDescription =>
        GeneralGallerySeries.First(option => option.Id == SelectedGeneralGallerySeries).Description;

    public string GeneralGalleryCountText =>
        $"当前 {GeneralGalleryEntries.Count} / {_allGeneralGalleryEntries.Count} 名武将";

    public ICommand OpenGeneralGalleryCommand { get; private set; } = null!;
    public ICommand CloseGeneralGalleryCommand { get; private set; } = null!;
    public ICommand SelectGeneralGalleryFactionCommand { get; private set; } = null!;
    public ICommand SelectGeneralGallerySeriesCommand { get; private set; } = null!;

    private void InitializeGeneralGallery()
    {
        OpenGeneralGalleryCommand = new RelayCommand(() =>
        {
            IsHelpOpen = false;
            IsHistoryOpen = false;
            IsGeneralGalleryOpen = true;
        });
        CloseGeneralGalleryCommand = new RelayCommand(() => IsGeneralGalleryOpen = false);
        SelectGeneralGalleryFactionCommand = new RelayCommand<string>(faction =>
        {
            if (GeneralGalleryFactions.Any(option => option.Id == faction))
                SelectedGeneralGalleryFaction = faction!;
        });
        SelectGeneralGallerySeriesCommand = new RelayCommand<string>(series =>
        {
            if (GeneralGallerySeries.Any(option => option.Id == series))
            {
                SelectedGeneralGallerySeries = series!;
                RaisePropertyChanged(nameof(GeneralGallerySeriesDescription));
            }
        });

        foreach (var general in _contentRegistry.Generals.Values)
        {
            var skills = general.SkillIds.Select(_contentRegistry.GetSkill).ToArray();
            var seriesId = GeneralSeriesId(general.Id);
            _allGeneralGalleryEntries.Add(new GeneralGalleryEntryViewModel
            {
                GeneralId = general.Id,
                SeriesId = seriesId,
                SeriesName = GeneralGallerySeries.First(option => option.Id == seriesId).Name,
                Name = general.Name,
                FactionId = general.FactionId ?? string.Empty,
                Kingdom = FactionName(general.FactionId),
                HealthText = $"{general.BaseHp} 体力",
                SkillName = string.Join(" / ", skills.Select(skill => skill.Name)),
                SkillDescription = string.Join("\n", skills.Select(skill =>
                    $"{skill.Name}：{GetVisibleSkillDescription(skill)}"))
            });
        }
    }

    private void RefreshGeneralGallery()
    {
        var query = GeneralGallerySearchText.Trim();
        var entries = _allGeneralGalleryEntries
            .Where(entry => SelectedGeneralGallerySeries == "all" || entry.SeriesId == SelectedGeneralGallerySeries)
            .Where(entry => SelectedGeneralGalleryFaction == "all" || entry.FactionId == SelectedGeneralGalleryFaction)
            .Where(entry => query.Length == 0 ||
                entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.SkillName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                entry.SkillDescription.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(entry => entry.FactionId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal);
        GeneralGalleryEntries.Clear();
        foreach (var entry in entries) GeneralGalleryEntries.Add(entry);
        RaisePropertyChanged(nameof(GeneralGalleryCountText));
    }

    private static string GeneralSeriesId(string generalId) => generalId.Split(':', 2)[0] switch
    {
        "classic" => "classic",
        "national" => "national",
        _ => "standard"
    };
}
