using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class HandReflowChecks
{
    public static void EqualCountReplacement(string output)
    {
        var clock = new FrameClock();
        var panel = new HandPanel { Width = 336, Height = 178, Clock = clock };
        var root = new Grid { Width = 360, Height = 210, Background = Brushes.DarkSlateGray };
        root.Children.Add(panel);
        var first = Card("A", Brushes.Wheat);
        var second = Card("B", Brushes.LightSteelBlue);
        var third = Card("C", Brushes.PaleGoldenrod);
        var fourth = Card("D", Brushes.PaleGreen);
        var fifth = Card("E", Brushes.Thistle);
        foreach (var card in new[] { first, second, third }) panel.Children.Add(card);
        Capture();
        var beforeSecond = X(second);
        var beforeThird = X(third);

        // One committed refresh removes a paid card then adds a drawn card
        // before WPF arranges. The count is unchanged, but B and C must reflow.
        panel.Children.Remove(first);
        panel.Children.Add(fourth);
        var movie = RecordMotion ? new GifBitmapEncoder() : null;
        AddFrame(Capture());
        Assert(Near(X(second), beforeSecond) && Near(X(third), beforeThird),
            "Equal-count replacement snapped surviving cards to their new slots before reflow.");
        clock.Advance(.04);
        AddFrame(Capture());
        Assert(X(second) > 0 && X(second) < beforeSecond && X(third) > 112 && X(third) < beforeThird,
            "Equal-count survivors did not animate toward their new contiguous slots.");

        var interruptedThird = X(third);
        panel.Children.Remove(second);
        panel.Children.Add(fifth);
        Capture();
        Assert(Near(X(third), interruptedThird),
            "A second replacement jumped back to the old layout instead of continuing the visible pose.");
        for (var step = 0; step < 5; step++)
        {
            clock.Advance(.04);
            AddFrame(Capture());
        }
        Assert(Near(X(third), 0) && Near(X(fourth), 112) && Near(X(fifth), 224),
            "Surviving cards did not settle into contiguous slots after replacement.");

        // The same surviving faces also retain their visible positions when
        // the player explicitly changes their order.
        panel.Children.Remove(fifth);
        panel.Children.Insert(0, fifth);
        Capture();
        Assert(Near(X(fifth), 224) && Near(X(third), 0) && Near(X(fourth), 112),
            "An explicit sort snapped surviving faces to their new order.");
        clock.Advance(.04);
        Capture();
        Assert(X(fifth) > 0 && X(fifth) < 224 && X(third) > 0 && X(third) < 112 &&
            X(fourth) > 112 && X(fourth) < 224,
            "Sorted faces must move between their old visible positions and their new slots.");
        clock.Advance(.16);
        Capture();
        Assert(Near(X(fifth), 0) && Near(X(third), 112) && Near(X(fourth), 224),
            "Sorted faces did not settle into their new contiguous order.");
        foreach (CardLiftSurface card in panel.Children) card.MotionEnabled = false;
        panel.Children.Remove(fifth);
        panel.Children.Add(second);
        Capture();
        Assert(Near(X(third), 0) && Near(X(fourth), 112) && Near(X(second), 224),
            "Disabled motion retained a replacement offset.");
        if (movie is not null)
        {
            var path = Path.Combine(output, "hand-equal-count-reflow.gif");
            using (var file = File.Create(path)) movie.Save(file);
            BattleEffectsChecks.SetGifTiming(path, 4);
        }
        root.Children.Clear();
        Console.WriteLine("Hand replacement: equal count, interrupted visible pose, settled slots, sort and motion-off passed.");
        NativeSort(output);

        CardLiftSurface Card(string label, Brush color)
        {
            var card = new CardLiftSurface { DataContext = new object(), Width = 112, Height = 178 };
            card.Children.Add(new Border
            {
                Background = color, CornerRadius = new CornerRadius(5), BorderBrush = Brushes.Goldenrod,
                BorderThickness = new Thickness(2), Child = new TextBlock
                {
                    Text = label, FontSize = 36, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            });
            return card;
        }
        RenderTargetBitmap Capture()
        {
            foreach (UIElement child in panel.Children) child.InvalidateVisual();
            root.Measure(new Size(360, 210));
            root.Arrange(new Rect(0, 0, 360, 210));
            root.UpdateLayout();
            var bitmap = new RenderTargetBitmap(360, 210, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(root);
            return bitmap;
        }
        double X(CardLiftSurface card) => card.TransformToAncestor(panel).Transform(new Point()).X;
        static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .1;
        void AddFrame(BitmapSource frame)
        {
            if (movie is null) return;
            var metadata = new BitmapMetadata("gif");
            metadata.SetQuery("/grctlext/Delay", (ushort)4);
            metadata.SetQuery("/grctlext/Disposal", (byte)2);
            if (movie.Frames.Count == 0)
            {
                metadata.SetQuery("/appext/application", System.Text.Encoding.ASCII.GetBytes("NETSCAPE2.0"));
                metadata.SetQuery("/appext/data", new byte[] { 3, 1, 0, 0, 0 });
            }
            movie.Frames.Add(BitmapFrame.Create(frame, null, metadata, null));
        }
    }

    private static void NativeSort(string output)
    {
        using var model = new MainViewModel(false, 721019, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = true, IsSoundEnabled = false, IsMusicEnabled = false };
        StartLordFixture(model);
        model.SelectGeneralChoiceCommand.Execute(model.GeneralChoices[0]);
        for (var step = 0; step < 80 && model.CanStepAi; step++) model.StepAiCommand.Execute(null);
        var game = Engine(model);
        Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && model.Hand.Count > 1,
            "The fixed native sort fixture must reach its first human Play boundary.");
        var original = model.Hand.ToArray();
        var expected = original.OrderBy(card => card.KindLabel).ThenBy(card => card.Name)
            .ThenBy(card => card.SuitGlyph).ThenBy(card => card.Rank).ToArray();
        Assert(!original.SequenceEqual(expected) && original.All(card => card.Id > 0) &&
            original.Select(card => card.Id).Order().SequenceEqual(game.CreateSnapshot(0).Players[0].Hand.Select(card => card.Id).Order()),
            "The native sort fixture must use a nontrivial permutation of the engine's real hand, without injected cards.");
        var selected = original.First(card => card.IsPlayable);
        model.SelectCardCommand.Execute(selected);
        Assert(selected.IsSelected, "The real hand must retain a selected card draft through sorting.");
        model.BattleCues.Clear();
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        host.Capture(output, "hand-native-sort-before", 1120);
        var panel = Find<HandPanel>(host.Root).Single(candidate => candidate.IsVisible);
        panel.Clock = host.Clock;
        panel.InvalidateArrange(); host.Root.UpdateLayout();
        host.Clock.Advance(.2);
        RenderFrame();
        var surfaces = Find<CardLiftSurface>(panel).ToDictionary(surface => ((CardViewModel)surface.DataContext).Id);
        Assert(surfaces.Count == original.Length && surfaces.Values.All(surface =>
            surface.IsLoaded && ReferenceEquals(surface.Clock, host.Clock) && surface.OpacityMask is null),
            "The native sort must start from loaded, settled real hand surfaces on one frame clock.");
        var before = surfaces.ToDictionary(pair => pair.Key, pair => X(pair.Value));
        var commands = game.AcceptedCommands.Count;
        var views = Enumerable.Range(0, game.CreateSnapshot(0).Players.Count)
            .Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray();
        var choice = model.ChoiceSnapshot;
        var sort = (Button)host.Window.FindName("SortHandButton");
        Assert(sort.IsVisible && sort.IsEnabled && ReferenceEquals(sort.Command, model.SortHandCommand),
            "The native sort button must publish the real SortHandCommand.");
        var clicks = 0;
        sort.Click += (_, _) => clicks++;
        ClickSort(); RenderFrame();
        var destinations = surfaces.ToDictionary(pair => pair.Key,
            pair => X(pair.Value) - pair.Value.RenderTransform.Value.OffsetX * panel.CardScale);
        var moved = original.Where(card => Math.Abs(before[card.Id] - destinations[card.Id]) > .1).ToArray();
        Assert(moved.Length > 1 && clicks == 1 && model.Hand.SequenceEqual(expected),
            "The actual native sort click must reorder the original CardViewModels.");
        Sample(0);
        host.Clock.Advance(.04); Sample(40);
        var interrupted = surfaces.ToDictionary(pair => pair.Key, pair => X(pair.Value));
        ClickSort(); RenderFrame();
        Assert(clicks == 2 && surfaces.All(pair => Near(X(pair.Value), interrupted[pair.Key])),
            "A repeated sort of the already ordered hand restarted or snapped its current visible motion.");
        host.Clock.Advance(.06); Sample(100);
        host.Clock.Advance(.1); Sample(200);
        Assert(game.AcceptedCommands.Count == commands && ReferenceEquals(choice, model.ChoiceSnapshot) &&
            Enumerable.Range(0, views.Length).All(seat => views[seat] == SnapshotJson.Serialize(game.CreateSnapshot(seat))) &&
            selected.IsSelected && model.Hand.Count(card => card.IsSelected) == 1 && model.EndTurnCommand.CanExecute(null),
            "Sorting must preserve the selected draft, live next command and every player-safe engine view without submitting a command.");
        Console.WriteLine($"Native hand sort seed721019: real IDs {string.Join(',', original.Select(card => card.Id))} -> " +
            $"{string.Join(',', model.Hand.Select(card => card.Id))}; {moved.Length} moving faces, stable VM/surface identities, 0/40/100/200ms, repeated-sort continuity, zero game commands.");

        void ClickSort() => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(sort, null);
        void RenderFrame()
        {
            foreach (var surface in Find<CardLiftSurface>(panel)) surface.InvalidateVisual();
            Render(host.Root, 1120, 740, null);
        }
        void Sample(int milliseconds)
        {
            RenderFrame();
            Assert(model.Hand.SequenceEqual(expected) && original.All(card => ReferenceEquals(card, model.Hand.Single(item => item.Id == card.Id))) &&
                Find<CardLiftSurface>(panel).All(surface => ReferenceEquals(surface, surfaces[((CardViewModel)surface.DataContext).Id])) &&
                surfaces.Values.All(surface => surface.IsLoaded && surface.OpacityMask is null),
                $"At {milliseconds}ms sorting replaced or reintroduced an existing CardViewModel or native surface.");
            foreach (var card in moved)
            {
                var x = X(surfaces[card.Id]); var start = before[card.Id]; var finish = destinations[card.Id];
                Assert(milliseconds == 0 ? Near(x, start) : milliseconds == 200 ? Near(x, finish) :
                    x > Math.Min(start, finish) + .01 && x < Math.Max(start, finish) - .01,
                    $"Native sorted card {card.Id} at {milliseconds}ms must traverse its old and new slots: {start:F2} -> {x:F2} -> {finish:F2}.");
            }
            host.Capture(output, $"hand-native-sort-{milliseconds}ms", 1120);
        }
        double X(CardLiftSurface surface) => surface.TransformToAncestor(panel).Transform(new Point()).X;
        static bool Near(double actual, double expected) => Math.Abs(actual - expected) < .1;
    }

    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
