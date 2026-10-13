using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class PublicCardIdentityChecks
{
    public static void PublicFaces(string output)
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        var view = Engine(vm).CreateSnapshot(0);
        var physical = new CardSnapshot(41, CardKind.Crossbow, Suit.Heart, 12, "诸葛连弩", "Q");
        long sequence = 0;
        EventEnvelope Event(IGameEvent payload) => new(new(++sequence), null, sequence, 1, "public-face", payload);
        BattleCue Project(IGameEvent payload) => BattleCueProjector.ProjectCardPlays([Event(payload)], view).Single();
        var converted = Project(new CardRespondedEvent(41, 0, 1, CardKind.Dodge) { PublicCard = physical });
        Assert(converted.CardKind == CardKind.Dodge && converted.PublicCard == physical,
            "The converted public face replaced its announced kind with the printed material or lost its metadata.");
        var recast = Project(new CardRecastEvent(0, 41, CardKind.IronChain, 1) { PublicCard = physical });
        Assert(recast.CardKind == CardKind.IronChain && recast.PublicCard == physical && recast.CardActionLabel == "重铸",
            "Recast discarded its actual public suit/rank or changed its effective kind.");
        foreach (var id in new[] { -1, 0, 42 })
            Assert(Project(new CardUseDeclaredEvent(9, id, CardKind.Slash, 0) { PublicCard = physical }).PublicCard is null,
                "A virtual or mismatched play inherited another physical card's suit/rank.");
        var legacy = Project(new CardRespondedEvent(41, 0, 1, CardKind.Dodge));
        Assert(legacy.PublicCard is null, "An older public event inferred metadata from a private hand or its ID.");
        var oldJson = "{\"ResolutionId\":1,\"CardId\":41,\"CardKind\":0,\"SourceSeat\":0,\"IgnoresArmor\":false}";
        Assert(JsonSerializer.Deserialize<CardUseDeclaredEvent>(oldJson)?.PublicCard is null &&
            !JsonSerializer.Serialize(new CardUseDeclaredEvent(1, 41, CardKind.Slash, 0)).Contains("PublicCard"),
            "The optional face payload changed an older event's JSON shape when metadata is absent.");

        var root = new Canvas { Width = 900, Height = 430, Background = Brushes.DarkSlateGray };
        var cues = new ObservableCollection<BattleCue>(); var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Width = 900, Height = 430, Cues = cues, Clock = clock, AnchorRoot = root };
        var cases = new (Suit Suit, int Rank, string RankText)[] { (Suit.Spade, 10, "10"), (Suit.Heart, 11, "J"), (Suit.Club, 12, "Q"), (Suit.Diamond, 13, "K") };
        for (var index = 0; index < cases.Length; index++)
        {
            var face = new CardSnapshot(100 + index, CardKind.Crossbow, cases[index].Suit, cases[index].Rank, "诸葛连弩", cases[index].RankText);
            var play = new TablePlayViewModel(100 + index, "闪", "公开转换") { Kind = CardKind.Dodge, PublicCardId = face.Id, PublicCard = face };
            var control = new ContentControl { Content = play, ContentTemplate = (DataTemplate)Application.Current.FindResource("TablePlayTemplate") };
            Canvas.SetLeft(control, 100 + index * 170); Canvas.SetTop(control, 220); root.Children.Add(control);
            var seat = new Border { Width = 65, Height = 90, Background = Brushes.Black };
            BattleFeedbackLayer.SetSeatAnchor(seat, index); Canvas.SetLeft(seat, 90 + index * 170); Canvas.SetTop(seat, 20); root.Children.Add(seat);
            cues.Add(new(100 + index, BattleCueKind.Response, index, [], "闪", "公开转换") { CardKind = CardKind.Dodge, PublicCardId = face.Id, PublicCard = face });
        }
        root.Children.Add(layer); clock.Advance(.24);
        Render(root, 900, 430, Path.Combine(output, "card-identity-four-suits-moving.png"));
        var images = Images(VisualTreeHelper.GetDrawing(layer)).ToArray();
        foreach (var control in root.Children.OfType<ContentControl>())
        {
            var play = (TablePlayViewModel)control.Content;
            Assert(images.Count(image => ReferenceEquals(image.ImageSource, play.RankArtwork)) == 1 &&
                images.Count(image => ReferenceEquals(image.ImageSource, play.SuitArtwork)) == 1,
                "A moving converted face lost a committed rank/suit or used the wrong red/black assets.");
        }
        Assert(images.Count(image => ReferenceEquals(image.ImageSource, CardArt.Get(CardKind.Dodge))) == 4 &&
            !images.Any(image => ReferenceEquals(image.ImageSource, CardArt.Get(CardKind.Crossbow))),
            "The physical material replaced a converted public Dodge illustration.");
        clock.Advance(.3); layer.InvalidateVisual();
        Render(root, 900, 430, Path.Combine(output, "card-identity-four-suits-landed.png"));
        Assert(!Images(VisualTreeHelper.GetDrawing(layer)).Any(), "Rank/suit overlays continued drawing after the public face handoff.");
        foreach (var control in root.Children.OfType<ContentControl>())
        {
            var play = (TablePlayViewModel)control.Content;
            Assert(Find<TextBlock>(control).Single(text => text.Name == "CardRank").Text == play.Rank &&
                Find<TextBlock>(control).Single(text => text.Name == "CardSuit").Text == play.SuitGlyph,
                "The shared static corner template changed the public card's rank or suit.");
        }
        cues.Clear();
        var unknown = new TablePlayViewModel(900, "未公开牌面", "旧记录") { Kind = CardKind.Dodge, PublicCardId = 41 };
        var old = new ContentControl { Content = unknown, ContentTemplate = (DataTemplate)Application.Current.FindResource("TablePlayTemplate") };
        root.Children.Clear(); root.Children.Add(old);
        Render(root, 900, 430, Path.Combine(output, "card-identity-legacy-no-metadata.png"));
        Assert(!Find<Image>(old).Any(image => ReferenceEquals(image.Source, CardArt.GetSuit("♥"))) &&
            Find<TextBlock>(old).Single(text => text.Name == "CardRank").Text == string.Empty &&
            Find<TextBlock>(old).Single(text => text.Name == "CardSuit").Text == string.Empty,
            "A legacy or concealed card fabricated metadata from its public ID.");
        Console.WriteLine("Public card identities: four suit colors, 10/J/Q/K, effective conversion artwork, recast, virtual/mismatched IDs and absent legacy metadata passed.");
    }

    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var nested in Images(child)) yield return nested;
    }
    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
