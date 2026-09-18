using CardGame.Core;

namespace CardGame.Wpf.Presentation;

/// <summary>UI context projected solely from the viewer's own prompt and visible players.</summary>
public sealed record DecisionContext(string Title, string Description, int? SourceSeat, int? TargetSeat, string TargetLabel)
{
    public static DecisionContext? From(GameSnapshot? snapshot)
    {
        if (snapshot?.PendingDecision is not { } prompt || prompt.PlayerSeat != snapshot.HumanSeat ||
            prompt.Kind is DecisionKind.PlayCard or DecisionKind.SelectGeneral or DecisionKind.DiscardCards) return null;
        string Name(int? seat) => snapshot.Players.FirstOrDefault(player => player.Seat == seat) is { } player
            ? player.IsHuman ? $"你（{player.GeneralName}）" : $"{player.Seat + 1}号位 {player.GeneralName}"
            : "未指定角色";
        var description = prompt.Prompt;
        foreach (var player in snapshot.Players.Where(player => !player.IsHuman).OrderByDescending(player => player.Name.Length))
            if (!string.IsNullOrEmpty(player.Name)) description = description.Replace(player.Name, Name(player.Seat), StringComparison.Ordinal);
        var source = prompt.SourceSeat;
        var target = prompt.TargetSeat;
        var card = prompt.IncomingCard is { } kind ? $"【{CardCatalog.Get(kind).DisplayName}】" : "技能";
        string title;
        var targetLabel = "受影响";
        switch (prompt.Kind)
        {
            case DecisionKind.RespondDodge:
            case DecisionKind.RespondSlash:
                target ??= prompt.PlayerSeat;
                title = prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") is "hujia-request" or "hujia-dodge" or "hujia-bagua")
                    ? $"响应护驾 · {Name(target)} · {card}"
                    : prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") is "jijiang-request" or "jijiang-slash")
                        ? $"响应激将 · {Name(target)} · {card}"
                    : $"{Name(source)} → {Name(target)} · {card}";
                break;
            case DecisionKind.RescueDying:
                // This prompt's SourceSeat names the dying player, not the damage source.
                target ??= source;
                source = null;
                var victim = snapshot.Players.FirstOrDefault(player => player.Seat == target);
                title = $"救援 {Name(target)}" + (victim is null ? string.Empty : $" · 体力 {victim.Hp}/{victim.MaxHp}");
                targetLabel = "等待救援";
                break;
            case DecisionKind.Nullification:
                if (prompt.IncomingCard == CardKind.DrawTwo) target ??= source;
                var scope = prompt.IncomingCard is CardKind.ArrowBarrage or CardKind.BarbarianAssault or CardKind.PeachGarden or CardKind.FiveGrains
                    ? "群体效果" : "效果响应";
                title = target is null ? $"{Name(source)} · {card} · {scope}" : $"{Name(source)} → {Name(target)} · {card}";
                break;
            case DecisionKind.FireAttackReveal:
            case DecisionKind.FireAttackDiscard:
            case DecisionKind.SelectTargetCard:
                title = $"{Name(source)} → {Name(target)} · {card}";
                break;
            case DecisionKind.SelectHarvestCard:
                target = prompt.PlayerSeat;
                title = $"{Name(source)} · {card} · 轮到你选牌";
                targetLabel = "正在选牌";
                break;
            default:
                title = $"当前技能选择 · {Name(prompt.PlayerSeat)}";
                break;
        }
        return new(title, description, source, target, targetLabel);
    }
}
