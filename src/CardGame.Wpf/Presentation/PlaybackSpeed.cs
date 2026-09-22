namespace CardGame.Wpf.Presentation;

public sealed record PlaybackSpeed(string Id, string Name, int IntervalMilliseconds)
{
    public static PlaybackSpeed Normal { get; } = new("normal", "标准", 350);
    public static PlaybackSpeed Fast { get; } = new("fast", "快速", 100);
    public static IReadOnlyList<PlaybackSpeed> All { get; } = Array.AsReadOnly(new[]
    {
        new PlaybackSpeed("slow", "从容", 1200), Normal, Fast
    });
    public static PlaybackSpeed? Find(string id) => All.SingleOrDefault(speed => speed.Id == id);
}
