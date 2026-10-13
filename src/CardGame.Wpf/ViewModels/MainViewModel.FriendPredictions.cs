using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private string? FriendPredictionText(int seat)
    {
        var forecasts = _snapshot.FriendPredictions?.Where(forecast => forecast.OwnerSeat == seat).ToArray();
        if (forecasts is not { Length: > 0 }) return null;
        return string.Join("\n", forecasts.Select(forecast =>
        {
            var remaining = forecast.Values.Skip(forecast.Index).Select(value => value switch
            {
                "basic" => "基本", "trick" => "锦囊", "equipment" => "装备", "red" => "红色", "black" => "黑色", _ => value
            });
            return $"演策 · {(forecast.Revealed ? "已公开" : "仅自己可见")} · 已验证 {forecast.Index}/{forecast.Values.Count} · 正确 {forecast.Correct}\n" +
                "剩余预测：" + string.Join(" → ", remaining);
        }));
    }

    private string? HumanFriendPredictionState() => _snapshot.Players.FirstOrDefault(player => player.IsHuman) is { } player
        ? FriendPredictionText(player.Seat) : null;
}
