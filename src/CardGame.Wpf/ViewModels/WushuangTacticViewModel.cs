using System.Windows.Media;
using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

/// <summary>A passive table badge, separate from activatable general skills.</summary>
public sealed record WushuangTacticViewModel(int Id)
{
    private WushuangTactic Tactic => WushuangTactics.ById[Id];
    public string Name => Tactic.Name;
    public string Tooltip => $"战法 · {Name}\n{Tactic.Description}";
    public ImageSource? Icon => ShanheArt.Get(Id);
    public ImageSource Frame => WushuangArt.Get($"rogue_zhanfa_bg_{Tactic.Quality}.png");
    public string Foreground => Tactic.Quality switch
    {
        2 => "#DEC5F1", 3 => "#F5D38B", 4 => "#FFAF87", _ => "#E6D1AE"
    };
}
