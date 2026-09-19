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

    public ObservableCollection<GeneralGalleryEntryViewModel> GeneralGalleryEntries { get; } = [];

    public IReadOnlyList<GeneralGalleryFactionOption> GeneralGalleryFactions { get; } =
    [
        new("all", "全部"),
        new("wei", "魏"),
        new("shu", "蜀"),
        new("wu", "吴"),
        new("qun", "群")
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

    public string GeneralGalleryCountText =>
        $"当前 {GeneralGalleryEntries.Count} / {_allGeneralGalleryEntries.Count} 名武将";

    public ICommand OpenGeneralGalleryCommand { get; private set; } = null!;
    public ICommand CloseGeneralGalleryCommand { get; private set; } = null!;
    public ICommand SelectGeneralGalleryFactionCommand { get; private set; } = null!;

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

        var mode = _contentRegistry.Modes.GetValueOrDefault("identity:classic-8") ??
            _contentRegistry.Modes.GetValueOrDefault("identity:standard-8");
        var ids = mode?.GeneralPoolIds ?? _contentRegistry.Generals.Keys.ToArray();
        foreach (var id in ids.Distinct(StringComparer.Ordinal))
        {
            if (!_contentRegistry.Generals.TryGetValue(id, out var general)) continue;
            var skills = general.SkillIds.Select(_contentRegistry.GetSkill).ToArray();
            _allGeneralGalleryEntries.Add(new GeneralGalleryEntryViewModel
            {
                GeneralId = general.Id,
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
}
