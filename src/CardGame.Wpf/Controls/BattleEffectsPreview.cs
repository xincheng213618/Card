using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

public enum BattleEffectPreviewKind { Slash, Dodge, FireSlash, ThunderSlash, Damage, FireDamage, ThunderDamage, Recovery, Skill, Trick }

/// <summary>A presentation-only rehearsal using the exact effect layer used by the live table.</summary>
public sealed class BattleEffectsPreview : Grid, IDisposable
{
    private readonly ObservableCollection<BattleCue> _cues = [];
    private readonly DispatcherTimer _cycle = new() { Interval = TimeSpan.FromSeconds(2.3) };
    private readonly Dictionary<BattleEffectPreviewKind, Button> _buttons = [];
    private readonly TextBlock _caption;
    private readonly CheckBox _auto;
    private bool _disposed;
    private static readonly (BattleEffectPreviewKind Kind, string Name, string Description)[] Examples =
    [
        (BattleEffectPreviewKind.Slash, "杀", "刀光沿攻击方向飞出，在目标处划过。"),
        (BattleEffectPreviewKind.Dodge, "闪", "残影向两侧散开，蓝色护盾亮起。"),
        (BattleEffectPreviewKind.FireSlash, "火杀", "赤色刀光与火焰围绕目标爆发。"),
        (BattleEffectPreviewKind.ThunderSlash, "雷杀", "紫色刀光与电弧照亮目标。"),
        (BattleEffectPreviewKind.Damage, "受伤", "冲击光圈、震动描边与伤害数字。"),
        (BattleEffectPreviewKind.FireDamage, "火焰伤害", "火焰升腾，红色伤害数字浮起。"),
        (BattleEffectPreviewKind.ThunderDamage, "雷电伤害", "落雷与电弧，紫色伤害数字浮起。"),
        (BattleEffectPreviewKind.Recovery, "回复", "绿色光圈展开，回复符号向上飘散。"),
        (BattleEffectPreviewKind.Skill, "技能发动", "金色光环与技能名在发动者身上展开。"),
        (BattleEffectPreviewKind.Trick, "锦囊", "目标亮起蓝色光圈，公开使用牌飞向桌面。")
    ];

    public Canvas Scene { get; }
    public BattleFeedbackLayer FeedbackLayer { get; }
    public BattleEffectPreviewKind SelectedEffect { get; private set; }

    public BattleEffectsPreview()
    {
        Background = new SolidColorBrush(Color.FromRgb(13, 23, 27));
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel { Margin = new Thickness(24, 18, 24, 8) };
        heading.Children.Add(new TextBlock { Text = "战场特效预览", FontFamily = new("KaiTi"), FontSize = 30,
            Foreground = new SolidColorBrush(Color.FromRgb(237, 210, 151)) });
        heading.Children.Add(new TextBlock { Text = "点击下方按钮重播，也可以自动轮播。", Foreground = Brushes.DarkSeaGreen, Margin = new Thickness(0, 5, 0, 0) });
        Children.Add(heading);
        Scene = new Canvas { Width = 880, Height = 470, ClipToBounds = true,
            Background = new RadialGradientBrush(Color.FromRgb(44, 62, 58), Color.FromRgb(16, 28, 32)) };
        var table = new Ellipse { Width = 650, Height = 300, Stroke = new SolidColorBrush(Color.FromArgb(100, 171, 144, 91)), StrokeThickness = 1 };
        Place(table, 115, 85);
        var tableName = new TextBlock { Text = "三  国  ·  对  决", FontFamily = new("KaiTi"), FontSize = 32,
            Foreground = new SolidColorBrush(Color.FromArgb(60, 209, 186, 130)) };
        Place(tableName, 328, 155);
        AddSeat(0, "guan-yu", "关羽", 75, 250);
        AddSeat(1, "cao-cao", "曹操", 655, 45);
        var center = new Border { Width = 92, Height = 130, BorderBrush = new SolidColorBrush(Color.FromArgb(65, 193, 168, 118)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5) };
        BattleFeedbackLayer.SetPlayAnchor(center, 1);
        Place(center, 393, 287);
        FeedbackLayer = new BattleFeedbackLayer { Width = 880, Height = 470, Cues = _cues, AnchorRoot = Scene, CenterAnchor = center };
        Scene.Children.Add(FeedbackLayer);
        var viewbox = new Viewbox { Child = Scene, Stretch = Stretch.Uniform, Margin = new Thickness(18, 4, 18, 0) };
        SetRow(viewbox, 1); Children.Add(viewbox);

        var controls = new StackPanel { Margin = new Thickness(24, 12, 24, 18) };
        _caption = new TextBlock { FontSize = 14, Foreground = Brushes.Wheat, Margin = new Thickness(0, 0, 0, 12) };
        controls.Children.Add(_caption);
        var buttons = new WrapPanel();
        foreach (var example in Examples)
        {
            var button = new Button { Content = example.Name, Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(0, 0, 7, 7), Foreground = Brushes.Wheat, BorderThickness = new Thickness(1),
                BorderBrush = Brushes.DarkOliveGreen, Background = new SolidColorBrush(Color.FromRgb(29, 44, 46)) };
            button.Click += (_, _) => { Play(example.Kind); if (_cycle.IsEnabled) { _cycle.Stop(); _cycle.Start(); } };
            _buttons.Add(example.Kind, button);
            buttons.Children.Add(button);
        }
        controls.Children.Add(buttons);
        _auto = new CheckBox { Content = "自动轮播", IsChecked = true, Foreground = Brushes.DarkSeaGreen, Margin = new Thickness(0, 5, 0, 0) };
        _auto.Checked += (_, _) => { if (IsLoaded && !_disposed) _cycle.Start(); };
        _auto.Unchecked += (_, _) => _cycle.Stop();
        controls.Children.Add(_auto);
        SetRow(controls, 2); Children.Add(controls);
        _cycle.Tick += (_, _) => Play((BattleEffectPreviewKind)(((int)SelectedEffect + 1) % Examples.Length));
        Loaded += (_, _) => { if (!_disposed) { Play(SelectedEffect); if (_auto.IsChecked == true) _cycle.Start(); } };
        Unloaded += (_, _) => { _cycle.Stop(); _cues.Clear(); };
        Play(BattleEffectPreviewKind.Slash);
    }

    public void Play(BattleEffectPreviewKind kind)
    {
        if (_disposed) return;
        SelectedEffect = kind;
        var example = Examples.Single(example => example.Kind == kind);
        _caption.Text = example.Description;
        foreach (var (effect, button) in _buttons)
            button.BorderBrush = effect == kind ? Brushes.Goldenrod : Brushes.DarkOliveGreen;
        _cues.Clear();
        BattleCue cue = kind switch
        {
            BattleEffectPreviewKind.Dodge => new(1, BattleCueKind.Response, 1, [], "打出闪", "曹操") { CardKind = CardKind.Dodge },
            BattleEffectPreviewKind.Skill => new(1, BattleCueKind.Skill, 0, [0], "武圣", "关羽") { SkillId = "wusheng" },
            BattleEffectPreviewKind.Recovery => new(1, BattleCueKind.Recovery, 0, [1], "+1", "曹操"),
            BattleEffectPreviewKind.Damage or BattleEffectPreviewKind.FireDamage or BattleEffectPreviewKind.ThunderDamage =>
                new(1, BattleCueKind.Damage, 0, [1], "−1", "曹操", kind == BattleEffectPreviewKind.FireDamage ? DamageNature.Fire :
                    kind == BattleEffectPreviewKind.ThunderDamage ? DamageNature.Thunder : DamageNature.Normal),
            _ => new(1, BattleCueKind.Card, 0, [1], example.Name, "关羽") { CardKind = kind switch
                { BattleEffectPreviewKind.FireSlash => CardKind.FireSlash, BattleEffectPreviewKind.ThunderSlash => CardKind.ThunderSlash,
                    BattleEffectPreviewKind.Trick => CardKind.Duel, _ => CardKind.Slash } }
        };
        _cues.Add(cue);
    }

    private void AddSeat(int seat, string generalId, string name, double x, double y)
    {
        var portrait = new Border { Width = 146, Height = 170, BorderBrush = new SolidColorBrush(Color.FromRgb(158, 132, 78)),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Background = GeneralArt.GetPortrait(generalId) };
        BattleFeedbackLayer.SetSeatAnchor(portrait, seat);
        var title = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(225, 18, 27, 29)), Padding = new Thickness(8, 5, 8, 5),
            Child = new TextBlock { Text = name + "  ♥ ♥ ♥ ♥", Foreground = Brushes.Wheat, FontSize = 16, FontFamily = new("KaiTi") } };
        portrait.Child = title;
        Place(portrait, x, y);
    }

    private void Place(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); Scene.Children.Add(element); }
    public void Dispose() { if (_disposed) return; _disposed = true; _cycle.Stop(); _cues.Clear(); }
}
