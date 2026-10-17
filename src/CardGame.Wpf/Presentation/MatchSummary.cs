using CardGame.Core;
using System.Text.Json.Serialization;

namespace CardGame.Wpf.Presentation;

public sealed record PlayerMatchResult(int Seat, string GeneralName, string Camp, bool IsHuman, bool IsAlive,
    int CardsUsed, int Responses, int DamageDealt, int DamageTaken, int Recovery, int RescueCards, int Defeats)
{
    public string? SecondaryGeneralName { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? SurvivalRank { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TeamRank { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TeamTreasures { get; init; }
    [JsonIgnore] public string RankOrCamp => TeamRank is { } teamRank ? $"{Camp}·{teamRank}" : SurvivalRank is { } rank ? $"第{rank}名" : Camp;
    public string PlayerLabel => $"{Seat + 1:00}  {GeneralName}" + (SecondaryGeneralName is null ? "" : $" / {SecondaryGeneralName}") + (IsHuman ? " · 你" : string.Empty);
    public string StatusLabel => IsAlive ? "存活" : "阵亡";
}

/// <summary>End-of-match aggregates. Never carries hands, card identifiers or private choices.</summary>
public sealed record MatchSummary(int TurnCount, IReadOnlyList<PlayerMatchResult> Players)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsSurvival { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DragonBoatTeamSnapshot>? DragonBoatTeams { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DragonBoatRounds { get; init; }
    [JsonIgnore] public bool IsDragonBoat => DragonBoatTeams is not null;
    [JsonIgnore] public string CampColumnHeader => IsDragonBoat ? "船/名" : IsSurvival ? "名次" : "阵营";
    public string DurationText => DragonBoatRounds is { } rounds
        ? $"{Players.Count} 人竞舟 · {rounds} 个实际轮次 · {TurnCount} 个行动回合"
        : $"{Players.Count} 人对局 · {TurnCount} 个行动回合";
    [JsonIgnore] public string DragonBoatRankingText => DragonBoatTeams is not { } teams ? string.Empty :
        string.Join("\n", teams.OrderBy(team => team.Rank).Select(team =>
            $"第{team.Rank}名  {DragonBoatTeamName(team.TeamId)}  {team.Treasures}/12 枚" + (team.TangZiVictory ? " · 唐咨终局优先" : string.Empty)));
    public string HumanPerformance => Players.SingleOrDefault(player => player.IsHuman) is { } human
        ? (human.TeamRank is { } teamRank ? $"同船第{teamRank}名 · 至宝{human.TeamTreasures}枚 · " :
            human.SurvivalRank is { } rank ? $"你获得第{rank}名 · " : string.Empty) + $"你本局用牌 {human.CardsUsed} 次 · 打出响应 {human.Responses} 次 · 救援用牌 {human.RescueCards} 张"
        : "本局没有本地玩家";

    public static MatchSummary? Create(GameSnapshot snapshot, IEnumerable<EventEnvelope> events)
    {
        if (snapshot.Status != EngineStatus.Completed) return null;
        var totals = snapshot.Players.ToDictionary(player => player.Seat, _ => new Totals());
        var rounds = 0;
        foreach (var envelope in events)
        {
            switch (envelope.Payload)
            {
                case RoundStartedEvent round:
                    rounds = round.RoundNumber;
                    break;
                case CardUseDeclaredEvent use:
                    if (totals.TryGetValue(use.SourceSeat, out var user)) user.CardsUsed++;
                    break;
                case CardRespondedEvent response:
                    if (totals.TryGetValue(response.ResponderSeat, out var responder)) responder.Responses++;
                    break;
                case DamageAppliedEvent damage:
                    if (!damage.SourceLess && totals.TryGetValue(damage.SourceSeat, out var source)) source.DamageDealt += damage.Amount;
                    if (totals.TryGetValue(damage.TargetSeat, out var target)) target.DamageTaken += damage.Amount;
                    break;
                case RecoveryAppliedEvent recovery:
                    if (totals.TryGetValue(recovery.SourceSeat, out var healer)) healer.Recovery += recovery.Amount;
                    break;
                case DyingResponseEvent rescue:
                    if (totals.TryGetValue(rescue.ResponderSeat, out var rescuer))
                        rescuer.RescueCards += (rescue.UsedPeach ? 1 : 0) + (rescue.UsedAlcohol ? 1 : 0);
                    break;
                case PlayerDiedEvent { KillerSeat: { } killer } death when killer != death.VictimSeat:
                    if (totals.TryGetValue(killer, out var victor)) victor.Defeats++;
                    break;
            }
        }
        var survival = snapshot.ModeKind == ContentModeKind.Survival;
        var dragonBoat = snapshot.DragonBoatStandings;
        return new(snapshot.TurnNumber, snapshot.Players.OrderBy(player => dragonBoat is not null
                ? dragonBoat.Single(team => team.TeamId == player.TeamId).Rank : survival ? player.SurvivalRank ?? int.MaxValue : player.Seat)
            .ThenBy(player => player.Seat).Select(player =>
        {
            var total = totals[player.Seat];
            var teamResult = dragonBoat?.Single(team => team.TeamId == player.TeamId);
            var camp = teamResult is not null ? DragonBoatTeamName(teamResult.TeamId)[..1] : survival ? "独立" : snapshot.ModeKind == ContentModeKind.NationalWarLite ? player.FactionId switch { "wei" => "魏", "shu" => "蜀", "ambitious" => "野心家", _ => "未明势力" } : player.TeamId switch
            {
                "team:blue" => "青队",
                "team:red" => "赤队",
                _ => player.Role switch { Role.Lord => "主公", Role.Loyalist => "忠臣", Role.Rebel => "反贼", Role.Renegade => "内奸", _ => "未知" }
            };
            return new PlayerMatchResult(player.Seat, player.GeneralName, camp, player.IsHuman, player.IsAlive,
                total.CardsUsed, total.Responses, total.DamageDealt, total.DamageTaken, total.Recovery, total.RescueCards, total.Defeats)
            { SecondaryGeneralName = player.SecondaryGeneralName, SurvivalRank = player.SurvivalRank,
                TeamRank = teamResult?.Rank, TeamTreasures = teamResult?.Treasures };
        }).ToArray()) { IsSurvival = survival, DragonBoatTeams = dragonBoat, DragonBoatRounds = dragonBoat is null ? null : rounds };
    }

    private static string DragonBoatTeamName(string id) => id switch
    { "team:wei" => "魏船", "team:shu" => "蜀船", "team:wu" => "吴船", "team:qun" => "群船", _ => id };

    private sealed class Totals
    {
        public int CardsUsed, Responses, DamageDealt, DamageTaken, Recovery, RescueCards, Defeats;
    }
}
