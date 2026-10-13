using System.Globalization;
using CardGame.Core;

namespace CardGame.Wpf.Presentation;

/// <summary>UI context projected solely from the viewer's own prompt and visible players.</summary>
public sealed record DecisionContext(string Title, string Description, int? SourceSeat, int? TargetSeat, string TargetLabel)
{
    public string ResponseStatusTitle { get; init; } = string.Empty;
    public bool HasResponseStatus => ResponseStatusTitle.Length > 0;

    public static DecisionContext? From(GameSnapshot? snapshot)
    {
        if (snapshot?.PendingDecision is not { } prompt || prompt.PlayerSeat != snapshot.HumanSeat ||
            prompt.Kind is DecisionKind.PlayCard or DecisionKind.SelectGeneral or DecisionKind.DiscardCards) return null;
        var lordSeat = snapshot.Players.FirstOrDefault(player => player.Role == Role.Lord)?.Seat ?? 0;
        int Number(int seat) => snapshot.ModeKind == ContentModeKind.Identity
            ? (seat - lordSeat + snapshot.Players.Count) % snapshot.Players.Count + 1 : seat + 1;
        string Name(int? seat) => snapshot.Players.FirstOrDefault(player => player.Seat == seat) is { } player
            ? player.IsHuman ? $"你（{player.GeneralName}）" : $"{Number(player.Seat)}号位 {player.GeneralName}"
            : "未指定角色";
        var description = prompt.Prompt;
        foreach (var player in snapshot.Players.Where(player => !player.IsHuman).OrderByDescending(player => player.Name.Length))
            if (!string.IsNullOrEmpty(player.Name)) description = description.Replace(player.Name, Name(player.Seat), StringComparison.Ordinal);
        var source = prompt.SourceSeat;
        var target = prompt.TargetSeat;
        var card = prompt.IncomingCard is { } kind ? $"【{CardCatalog.Get(kind).DisplayName}】" : "技能";
        string title;
        var targetLabel = "受影响";
        if (prompt.SkillPrompt is { } skillPrompt)
        {
            target ??= prompt.PlayerSeat;
            title = skillPrompt.Title;
            targetLabel = "正在选择";
        }
        else switch (prompt.Kind)
        {
            case DecisionKind.SelectFaction:
                title = "选择本局势力";
                target = prompt.PlayerSeat;
                targetLabel = "正在选择";
                break;
            case DecisionKind.RespondDodge:
            case DecisionKind.RespondSlash:
                target ??= prompt.PlayerSeat;
                title = prompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("response") is "faction-defense-request" or "faction-defense-dodge" or "faction-defense-bagua")
                    ? $"响应护驾 · {Name(target)} · {card}"
                    : prompt.Choices.Any(choice =>
                        choice.Parameters.GetValueOrDefault("response") is "faction-slash-request" or "faction-slash-slash")
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
        return new(title, description, source, target, targetLabel) { ResponseStatusTitle = ResponseTitle(prompt) };
    }

    private static string ResponseTitle(PendingDecision prompt)
    {
        if (prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash)
        {
            if (!IsResponseCardPrompt(prompt)) return string.Empty;
            var required = prompt.RequiredCardKind ?? (prompt.Kind == DecisionKind.RespondDodge ? CardKind.Dodge : CardKind.Slash);
            return required is CardKind.Dodge or CardKind.Slash ? $"等待你打出【{CardCatalog.Get(required).DisplayName}】" : string.Empty;
        }
        if (prompt.Kind == DecisionKind.Nullification)
        {
            // The public prompt supplies the chain depth. Do not infer the current
            // trick's validity from parity, card identities or a previous animation.
            var responses = prompt.Choices.Where(choice => choice.Parameters.GetValueOrDefault("response") == "nullification").ToArray();
            var depths = responses.Select(choice => int.TryParse(choice.Parameters.GetValueOrDefault("chain-depth"),
                NumberStyles.None, CultureInfo.InvariantCulture, out var depth) && depth is >= 0 and < int.MaxValue ? (int?)depth : null)
                .Distinct().Take(2).ToArray();
            return depths is [int chainDepth] ? $"等待你的无懈 · 第{chainDepth + 1}层" : "等待你的无懈";
        }
        return prompt.Kind switch
        {
            DecisionKind.RescueDying => "等待你的救援",
            DecisionKind.FireAttackReveal => "等待你展示手牌",
            DecisionKind.FireAttackDiscard => "等待你弃牌 · 火攻",
            _ => string.Empty
        };
    }

    internal static bool IsResponseCardPrompt(PendingDecision prompt) =>
        (prompt.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash) && !prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("declaration-answer") is "pass" or "challenge" ||
            choice.Parameters.GetValueOrDefault("response") == "faction-request-cost");
}
