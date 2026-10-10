using System.Windows;
using CardGame.Wpf.Controls;

namespace CardGame.Wpf;

public sealed class BattleEffectsPreviewWindow : Window
{
    public BattleEffectsPreviewWindow()
    {
        Title = "三国卡牌 · 战场特效预览";
        Width = 1020; Height = 760; MinWidth = 760; MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var preview = new BattleEffectsPreview();
        Content = preview;
        Closed += (_, _) => preview.Dispose();
    }
}
