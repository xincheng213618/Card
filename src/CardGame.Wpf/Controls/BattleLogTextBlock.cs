using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CardGame.Wpf.ViewModels;

namespace CardGame.Wpf.Controls;

public sealed class BattleLogTextBlock : TextBlock
{
    private static readonly Brush ParticipantBrush = FrozenBrush(0xE5, 0xC8, 0x71);
    private static readonly Brush TitleBrush = FrozenBrush(0xD9, 0xCB, 0x91);
    private static readonly Brush SkillBrush = FrozenBrush(0xE0, 0xAF, 0x65);
    private static readonly Brush DamageBrush = FrozenBrush(0xE4, 0x96, 0x7C);
    private static readonly Brush RecoveryBrush = FrozenBrush(0x97, 0xC5, 0x99);
    private const string Tokens = @"(?<title>【[^】]*】)|(?<damage>\d+\s*点(?:火焰|雷电|普通)?伤害|(?:失去|流失)\s*\d+\s*点体力|濒死|阵亡|死亡)|(?<recovery>(?:回复|恢复)\s*\d+\s*点体力)";

    public static readonly DependencyProperty EntryProperty = DependencyProperty.Register(
        nameof(Entry), typeof(BattleLogEntryViewModel), typeof(BattleLogTextBlock),
        new PropertyMetadata(null, (owner, _) => ((BattleLogTextBlock)owner).RefreshInlines()));

    public BattleLogEntryViewModel? Entry
    {
        get => (BattleLogEntryViewModel?)GetValue(EntryProperty);
        set => SetValue(EntryProperty, value);
    }

    private void RefreshInlines()
    {
        Inlines.Clear();
        if (Entry is not { } entry) return;
        var pattern = Tokens;
        if (entry.ParticipantNames.Count > 0)
            pattern += "|(?<participant>" + string.Join('|', entry.ParticipantNames
                .OrderByDescending(name => name.Length).Select(Regex.Escape)) + ")";
        var position = 0;
        foreach (Match match in Regex.Matches(entry.Message, pattern, RegexOptions.CultureInvariant))
        {
            if (match.Index > position) Inlines.Add(new Run(entry.Message[position..match.Index]));
            var brush = match.Groups["participant"].Success ? ParticipantBrush :
                match.Groups["damage"].Success ? DamageBrush :
                match.Groups["recovery"].Success ? RecoveryBrush :
                entry.Type is "SkillTriggered" or "SkillSkipped" or "SkillDraw" or "EquipmentEffect" or "ArmorEffect" ? SkillBrush : TitleBrush;
            Inlines.Add(new Run(match.Value) { Foreground = brush });
            position = match.Index + match.Length;
        }
        if (position < entry.Message.Length) Inlines.Add(new Run(entry.Message[position..]));
    }

    private static Brush FrozenBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}
