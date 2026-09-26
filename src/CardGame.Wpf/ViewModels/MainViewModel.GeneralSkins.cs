using System.Windows.Input;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private readonly Dictionary<string, string> _generalSkinPreferences = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GeneralPortraitViewModel> _generalPortraits = new(StringComparer.Ordinal);
    private string _generalDetailsTab = "skills";

    public string GeneralDetailsTab
    {
        get => _generalDetailsTab;
        private set
        {
            if (!SetProperty(ref _generalDetailsTab, value)) return;
            RaisePropertyChanged(nameof(IsGeneralSkillsTab));
            RaisePropertyChanged(nameof(IsGeneralSkinsTab));
            RaisePropertyChanged(nameof(IsGeneralVoicesTab));
        }
    }
    public bool IsGeneralSkillsTab => GeneralDetailsTab == "skills";
    public bool IsGeneralSkinsTab => GeneralDetailsTab == "skins";
    public ICommand SelectGeneralDetailsTabCommand { get; private set; } = null!;
    public ICommand SelectGeneralSkinCommand { get; private set; } = null!;
    public ICommand PreviousGeneralDetailsCommand { get; private set; } = null!;
    public ICommand NextGeneralDetailsCommand { get; private set; } = null!;

    private GeneralPortraitViewModel GetGeneralPortrait(string id)
    {
        var key = GeneralArt.NormalizeKey(id);
        if (!_generalPortraits.TryGetValue(key, out var portrait))
            _generalPortraits[key] = portrait = new(id, _generalSkinPreferences.GetValueOrDefault(key));
        return portrait;
    }

    private void InitializeGeneralSkins()
    {
        InitializeGeneralVoices();
        SelectGeneralDetailsTabCommand = new RelayCommand<string>(tab =>
        {
            if (tab is "skills" or "skins" or "voices") GeneralDetailsTab = tab;
        });
        SelectGeneralSkinCommand = new RelayCommand<GeneralSkin>(skin =>
        {
            if (SelectedGeneralGalleryEntry is not { } entry || !entry.Skins.Contains(skin)) return;
            var key = GeneralArt.NormalizeKey(entry.GeneralId);
            if (skin.Id == GeneralArt.GetSkin(entry.GeneralId)?.Id) _generalSkinPreferences.Remove(key);
            else _generalSkinPreferences[key] = skin.Id;
            GetGeneralPortrait(entry.GeneralId).SelectSkin(skin.Id);
            RefreshGeneralVoices();
            QueuePreferencesSave();
        });
        PreviousGeneralDetailsCommand = new RelayCommand(() => MoveGeneralDetails(-1));
        NextGeneralDetailsCommand = new RelayCommand(() => MoveGeneralDetails(1));
    }

    private void MoveGeneralDetails(int direction)
    {
        if (SelectedGeneralGalleryEntry is not { } entry || GeneralGalleryEntries.Count == 0) return;
        var index = GeneralGalleryEntries.IndexOf(entry);
        SelectedGeneralGalleryEntry = GeneralGalleryEntries[(index + direction + GeneralGalleryEntries.Count) % GeneralGalleryEntries.Count];
    }

    private void ApplyGeneralSkinPreferences(IReadOnlyDictionary<string, string>? preferences)
    {
        _generalSkinPreferences.Clear();
        if (preferences is not null)
            foreach (var (key, id) in preferences)
                if (GeneralArt.GetSkins(key).Any(skin => skin.Id == id)) _generalSkinPreferences[key] = id;
        foreach (var (key, portrait) in _generalPortraits)
            portrait.SelectSkin(_generalSkinPreferences.GetValueOrDefault(key));
    }
}
