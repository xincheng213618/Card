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

public enum BattleEffectPreviewKind { Slash, Dodge, FireSlash, ThunderSlash, Damage, FireDamage, ThunderDamage, Recovery, Skill, Trick, HitSequence, DodgeSequence, DamageRecovery, FireChain, ThunderChain, RequiredDodgeFirst, RequiredDodgeSecond, StoneAxeContinuation, QinglongPursuit, JudgmentReveal, Draw }

/// <summary>A presentation-only rehearsal using the exact effect layer used by the live table.</summary>
public sealed class BattleEffectsPreview : Grid, IDisposable
{
    private readonly ObservableCollection<BattleCue> _cues = [];
    private readonly DispatcherTimer _cycle = new() { Interval = TimeSpan.FromSeconds(2.3) };
    private readonly Dictionary<BattleEffectPreviewKind, Button> _buttons = [];
    private readonly Dictionary<int, HealthMeter> _health = [];
    private readonly Dictionary<int, (TextBlock Label, string Name)> _healthLabels = [];
    private readonly TextBlock _caption;
    private readonly CheckBox _auto;
    private readonly Border _relayPortrait;
    private bool _disposed;
    private static readonly (BattleEffectPreviewKind Kind, string Name, string Description)[] Examples =
    [
        (BattleEffectPreviewKind.Slash, "杀", "公开牌飞向桌面，刀光沿攻击方向到达目标边缘。"),
        (BattleEffectPreviewKind.Dodge, "闪", "打出【闪】后，侧移残影与开放弧线短促掠过。"),
        (BattleEffectPreviewKind.FireSlash, "火杀", "赤色刀光拖着火焰飞向目标，伤害结算后才爆发。"),
        (BattleEffectPreviewKind.ThunderSlash, "雷杀", "紫色刀光与电弧飞向目标，落雷由实际伤害触发。"),
        (BattleEffectPreviewKind.Damage, "受伤", "冲击光圈、震动描边与伤害数字。"),
        (BattleEffectPreviewKind.FireDamage, "火焰伤害", "火焰升腾，红色伤害数字浮起。"),
        (BattleEffectPreviewKind.ThunderDamage, "雷电伤害", "落雷与电弧，紫色伤害数字浮起。"),
        (BattleEffectPreviewKind.Recovery, "回复", "绿色光圈展开，回复符号向上飘散。"),
        (BattleEffectPreviewKind.Skill, "技能发动", "金色光环与技能名在发动者身上展开。"),
        (BattleEffectPreviewKind.Trick, "锦囊", "目标亮起蓝色光圈，公开使用牌飞向桌面。"),
        (BattleEffectPreviewKind.HitSequence, "出杀 → 命中", "出牌、刀光到达、伤害冲击与飘字依次衔接。"),
        (BattleEffectPreviewKind.DodgeSequence, "出杀 → 打出闪", "出牌与攻击到达后打出【闪】，侧移残影标记本次响应。"),
        (BattleEffectPreviewKind.DamageRecovery, "伤后回复", "连续结算时，伤害与回复数字分别浮起，血条显示最新体力。"),
        (BattleEffectPreviewKind.FireChain, "火焰连环", "命中后，火焰从受伤武将传到下一目标；到达后衔接伤害与飘字。"),
        (BattleEffectPreviewKind.ThunderChain, "雷电连环", "命中后，电弧从受伤武将传到下一目标；到达后衔接伤害与飘字。"),
        (BattleEffectPreviewKind.RequiredDodgeFirst, "连续两闪 · 1/2", "已打出第一张【闪】，刻度显示 1/2；仍需继续响应。"),
        (BattleEffectPreviewKind.RequiredDodgeSecond, "连续两闪 · 2/2", "已打出第二张【闪】，刻度显示 2/2；伤害结果由后续结算决定。"),
        (BattleEffectPreviewKind.StoneAxeContinuation, "贯石斧 · 破闪", "发动贯石斧，斧光指向原目标，再显示已结算的伤害。"),
        (BattleEffectPreviewKind.QinglongPursuit, "青龙偃月刀 · 追击", "武器在发动者身上亮起金光，随后展示追加的一张【杀】。"),
        (BattleEffectPreviewKind.JudgmentReveal, "判定翻牌", "判定牌翻开后，显示判定花色与公开结果；金色笔触确认揭示。"),
        (BattleEffectPreviewKind.Draw, "摸牌", "牌背从牌堆飞向摸牌武将，只显示本次实际摸牌数量。")
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
        _relayPortrait = AddSeat(2, "zhao-yun", "赵云", 365, 35);
        _relayPortrait.Visibility = Visibility.Collapsed;
        var center = new Border { Width = 92, Height = 130, BorderBrush = new SolidColorBrush(Color.FromArgb(65, 193, 168, 118)),
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5) };
        BattleFeedbackLayer.SetPlayAnchor(center, 1);
        Place(center, 393, 287);
        var responseLanding = new Border { Width = 92, Height = 130, IsHitTestVisible = false };
        BattleFeedbackLayer.SetPlayAnchor(responseLanding, 2);
        Place(responseLanding, 495, 287);
        var drawPile = new Grid { Name = "PreviewDrawPileAnchor", Width = 46, Height = 64, IsHitTestVisible = false };
        for (var index = 2; index >= 0; index--)
            drawPile.Children.Add(new Border
            {
                Width = 42, Height = 60, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(index * 2, index * 2, 0, 0), CornerRadius = new CornerRadius(4),
                BorderThickness = new Thickness(1.5), BorderBrush = Brushes.DarkGoldenrod,
                Background = new SolidColorBrush(Color.FromRgb(48, 39, 29)),
                Child = index == 0 ? new TextBlock { Text = "牌", FontFamily = new("KaiTi"), FontSize = 24,
                    Foreground = Brushes.Wheat, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center } : null
            });
        Place(drawPile, 298, 218);
        FeedbackLayer = new BattleFeedbackLayer { Width = 880, Height = 470, Cues = _cues, AnchorRoot = Scene,
            CenterAnchor = center, DrawPileAnchor = drawPile };
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
        var chain = kind is BattleEffectPreviewKind.FireChain or BattleEffectPreviewKind.ThunderChain;
        _relayPortrait.Visibility = chain ? Visibility.Visible : Visibility.Collapsed;
        var example = Examples.Single(example => example.Kind == kind);
        _caption.Text = example.Description;
        foreach (var (effect, button) in _buttons)
            button.BorderBrush = effect == kind ? Brushes.Goldenrod : Brushes.DarkOliveGreen;
        _cues.Clear();
        foreach (var seat in _health.Keys) SetHealth(seat, 4);
        BattleCue cue = kind switch
        {
            BattleEffectPreviewKind.Draw => new(1, BattleCueKind.Draw, 1, [1], "摸牌 +2", "曹操") { DrawCount = 2 },
            BattleEffectPreviewKind.JudgmentReveal => new(1, BattleCueKind.Judgment, 1, [1], "乐不思蜀 · ♣8", "曹操",
                Detail: "非红桃 · 跳过出牌阶段")
                {
                    JudgmentStage = JudgmentCueStage.Resolved, JudgmentResolutionId = 1, JudgmentTitle = "乐不思蜀",
                    JudgmentCard = new(1, CardKind.Dodge, Suit.Club, 8, "闪", "8")
                },
            BattleEffectPreviewKind.StoneAxeContinuation or BattleEffectPreviewKind.QinglongPursuit =>
                new(1, BattleCueKind.Weapon, 0, [1], example.Name, "关羽")
                {
                    WeaponKind = kind == BattleEffectPreviewKind.StoneAxeContinuation ? CardKind.StoneAxe : CardKind.QinglongCrescentBlade,
                    WeaponResolutionId = 1, IsWeaponActivated = true
                },
            BattleEffectPreviewKind.Dodge or BattleEffectPreviewKind.RequiredDodgeFirst or BattleEffectPreviewKind.RequiredDodgeSecond =>
                new(1, BattleCueKind.Response, 1, [], "打出闪", "曹操") { CardKind = CardKind.Dodge },
            BattleEffectPreviewKind.Skill => new(1, BattleCueKind.Skill, 0, [0], "武圣", "关羽") { SkillId = "wusheng" },
            BattleEffectPreviewKind.Recovery => new(1, BattleCueKind.Recovery, 0, [1], "+1", "曹操"),
            BattleEffectPreviewKind.DamageRecovery => new(1, BattleCueKind.Damage, 0, [1], "−2", "曹操"),
            BattleEffectPreviewKind.Damage or BattleEffectPreviewKind.FireDamage or BattleEffectPreviewKind.ThunderDamage =>
                new(1, BattleCueKind.Damage, 0, [1], "−1", "曹操", kind == BattleEffectPreviewKind.FireDamage ? DamageNature.Fire :
                    kind == BattleEffectPreviewKind.ThunderDamage ? DamageNature.Thunder : DamageNature.Normal),
            _ => new(1, BattleCueKind.Card, 0, [1], example.Name, "关羽") { CardKind = kind switch
                { BattleEffectPreviewKind.FireSlash or BattleEffectPreviewKind.FireChain => CardKind.FireSlash,
                    BattleEffectPreviewKind.ThunderSlash or BattleEffectPreviewKind.ThunderChain => CardKind.ThunderSlash,
                    BattleEffectPreviewKind.Trick => CardKind.Duel, _ => CardKind.Slash } }
        };
        if (cue.Kind is BattleCueKind.Damage or BattleCueKind.Recovery)
        {
            SetHealth(1, 3);
            cue = cue with { HpDelta = kind == BattleEffectPreviewKind.DamageRecovery ? -2 : cue.Kind == BattleCueKind.Damage ? -1 : 1,
                RemainingHp = kind == BattleEffectPreviewKind.DamageRecovery ? 2 : 3, MaximumHp = 4 };
        }
        _cues.Add(cue);
        if (kind == BattleEffectPreviewKind.StoneAxeContinuation)
        {
            SetHealth(1, 3);
            _cues.Add(new(2, BattleCueKind.Damage, 0, [1], "−1", "曹操") { HpDelta = -1, RemainingHp = 3, MaximumHp = 4 });
        }
        else if (kind == BattleEffectPreviewKind.QinglongPursuit)
            _cues.Add(new(2, BattleCueKind.Card, 0, [1], "杀", "关羽") { CardKind = CardKind.Slash });
        else if (kind is BattleEffectPreviewKind.RequiredDodgeFirst or BattleEffectPreviewKind.RequiredDodgeSecond)
        {
            // Separate replay buttons rehearse each public response stage. They
            // do not fabricate two committed commands in one animation frame.
            var count = kind == BattleEffectPreviewKind.RequiredDodgeFirst ? 1 : 2;
            _cues.Add(new(2, BattleCueKind.ResponseWindow, 1, [0], $"连续响应 {count} / 2", "曹操", Detail: "已打出【闪】")
            {
                ResponseCount = count, RequiredResponseCount = 2, RequiredResponseCardKind = CardKind.Dodge,
                ResponseResolutionId = 1
            });
        }
        else if (chain)
        {
            var nature = kind == BattleEffectPreviewKind.FireChain ? DamageNature.Fire : DamageNature.Thunder;
            SetHealth(1, 3); SetHealth(2, 3);
            _cues.Add(new(2, BattleCueKind.Damage, 0, [1], "−1", "曹操", nature) { HpDelta = -1, RemainingHp = 3, MaximumHp = 4 });
            _cues.Add(new(3, BattleCueKind.ChainPropagation, 0, [2], "连环传导", "曹操", nature) { OriginSeat = 1 });
            _cues.Add(new(4, BattleCueKind.Damage, 0, [2], "−1", "赵云", nature) { HpDelta = -1, RemainingHp = 3, MaximumHp = 4 });
        }
        else if (kind == BattleEffectPreviewKind.HitSequence)
        {
            SetHealth(1, 3);
            _cues.Add(new(2, BattleCueKind.Damage, 0, [1], "−1", "曹操") { HpDelta = -1, RemainingHp = 3, MaximumHp = 4 });
        }
        else if (kind == BattleEffectPreviewKind.DodgeSequence)
            _cues.Add(new(2, BattleCueKind.Response, 1, [], "打出闪", "曹操") { CardKind = CardKind.Dodge });
        else if (kind == BattleEffectPreviewKind.DamageRecovery)
            _cues.Add(new(2, BattleCueKind.Recovery, 1, [1], "+1", "曹操") { HpDelta = 1, RemainingHp = 3, MaximumHp = 4 });
    }

    private Border AddSeat(int seat, string generalId, string name, double x, double y)
    {
        var portrait = new Border { Width = 146, Height = 170, BorderBrush = new SolidColorBrush(Color.FromRgb(158, 132, 78)),
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Background = GeneralArt.GetPortrait(generalId) };
        BattleFeedbackLayer.SetSeatAnchor(portrait, seat);
        var label = new TextBlock { Text = name + "  4/4", Foreground = Brushes.Wheat, FontSize = 16, FontFamily = new("KaiTi") };
        var meter = new HealthMeter { Value = 4, Maximum = 4, Height = 7, Margin = new Thickness(0, 4, 0, 0) };
        BattleFeedbackLayer.SetHealthAnchor(meter, seat);
        _health[seat] = meter;
        _healthLabels[seat] = (label, name);
        var healthTitle = new StackPanel();
        healthTitle.Children.Add(label); healthTitle.Children.Add(meter);
        var title = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = new SolidColorBrush(Color.FromArgb(225, 18, 27, 29)), Padding = new Thickness(8, 5, 8, 5),
            Child = healthTitle };
        portrait.Child = title;
        Place(portrait, x, y);
        return portrait;
    }

    private void SetHealth(int seat, int hp)
    {
        _health[seat].Value = hp;
        var (label, name) = _healthLabels[seat];
        label.Text = $"{name}  {hp}/{_health[seat].Maximum}";
    }

    private void Place(UIElement element, double x, double y) { Canvas.SetLeft(element, x); Canvas.SetTop(element, y); Scene.Children.Add(element); }
    public void Dispose() { if (_disposed) return; _disposed = true; _cycle.Stop(); _cues.Clear(); }
}
