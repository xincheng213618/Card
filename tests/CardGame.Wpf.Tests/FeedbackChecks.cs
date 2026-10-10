using System.IO;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;
using CardGame.Wpf.Audio;
using System.Text.Json;
using static Program;

internal static class FeedbackChecks
{
    public static void CardFlights(string output)
    {
        var root = new Canvas { Background = Brushes.DarkSlateGray, Width = 1040, Height = 300 };
        var cues = new ObservableCollection<BattleCue>();
        var clock = new FrameClock();
        var layer = new BattleFeedbackLayer { Width = 1040, Height = 300, Cues = cues, AnchorRoot = root, Clock = clock };
        var kinds = new CardKind?[] { CardKind.Slash, CardKind.ThunderSlash, CardKind.IronChain,
            CardKind.GeneralWeapon, CardKind.ScarletBloodSword, null, CardKind.Dodge };
        for (var index = 0; index < kinds.Length; index++)
        {
            var seat = new Border { Width = 54, Height = 35, Background = Brushes.Black,
                Child = new TextBlock { Text = $"座位 {index + 1}", Foreground = Brushes.Wheat } };
            Canvas.SetLeft(seat, 50 + 140 * index); Canvas.SetTop(seat, 15);
            BattleFeedbackLayer.SetSeatAnchor(seat, index); root.Children.Add(seat);
            var name = kinds[index] is { } kind ? CardCatalog.Get(kind).DisplayName : "未知牌";
            var settled = new ContentControl
            {
                Content = new TablePlayViewModel(index + 1, name, "公开动作")
                    { Kind = kinds[index], ActionLabel = index == 6 ? "打出" : kinds[index] == CardKind.IronChain ? "重铸" : "使用" },
                ContentTemplate = (DataTemplate)Application.Current.FindResource("TablePlayTemplate")
            };
            Canvas.SetLeft(settled, 35 + 140 * index); Canvas.SetTop(settled, 150);
            root.Children.Add(settled);
            cues.Add(new BattleCue(index + 1, index == 6 ? BattleCueKind.Response : BattleCueKind.Card, index, [], name, "公开动作")
                { CardKind = kinds[index], Detail = kinds[index] == CardKind.IronChain ? "重铸" : null });
        }
        root.Children.Add(layer);
        clock.Advance(.12);
        Render(root, 1040, 300, Path.Combine(output, "card-flight-moving.png"));
        clock.Advance(.33);
        layer.InvalidateVisual();
        Render(root, 1040, 300, Path.Combine(output, "card-flight-landed.png"));
        var images = Images(VisualTreeHelper.GetDrawing(layer)).ToArray();
        Assert(images.Length == kinds.Count(kind => kind is not null) &&
               kinds.Where(kind => kind is not null).All(kind => images.Any(image => ReferenceEquals(image.ImageSource, CardArt.Get(kind)))),
            "Flying cards did not draw the cached declared faces or an unknown face exposed unrelated artwork.");
        var names = Find<TextBlock>(root).Where(text => text.Name == "PlayRuntimeCardName" && text.Visibility == Visibility.Visible).ToArray();
        Assert(names.Select(text => text.Text).ToHashSet().SetEquals(new[]
                { CardCatalog.Get(CardKind.GeneralWeapon).DisplayName, CardCatalog.Get(CardKind.ScarletBloodSword).DisplayName }),
            "The settled dynamic weapon illustrations lost their names or other faces gained duplicate labels.");
        var anchors = Find<Border>(root).Where(border => BattleFeedbackLayer.GetPlayAnchor(border) >= 0).ToArray();
        Assert(anchors.All(anchor => anchor.ActualWidth == 86 && anchor.ActualHeight == 120) && !layer.IsHitTestVisible,
            "The flight changed the settled card size or blocked table input.");
        var imageBounds = ImageBounds(VisualTreeHelper.GetDrawing(layer), Matrix.Identity).ToArray();
        foreach (var anchor in anchors)
        {
            var play = (TablePlayViewModel)anchor.DataContext;
            if (play.Artwork is null) continue;
            var expected = anchor.TransformToVisual(layer).TransformBounds(new Rect(1, 1, anchor.ActualWidth - 2, anchor.ActualHeight - 2));
            var actual = imageBounds.Single(image => ReferenceEquals(image.Image.ImageSource, play.Artwork)).Bounds;
            Assert(Math.Abs(actual.X - expected.X) < .1 && Math.Abs(actual.Y - expected.Y) < .1 &&
                   Math.Abs(actual.Width - expected.Width) < .1 && Math.Abs(actual.Height - expected.Height) < .1,
                "A flying face landed at a different position or size than its settled public card.");
        }
        layer.IsPromptVisible = true;
        Render(root, 1040, 300, Path.Combine(output, "card-flight-prompt-safe.png"));
        Assert(Images(VisualTreeHelper.GetDrawing(layer)).Count() == kinds.Count(kind => kind is not null),
            "A hand response prompt suppressed card flights into the visible public row.");
        foreach (var settled in root.Children.OfType<ContentControl>()) settled.Visibility = Visibility.Collapsed;
        layer.InvalidateVisual();
        Render(root, 1040, 300, Path.Combine(output, "card-flight-choice-safe.png"));
        Assert(!Images(VisualTreeHelper.GetDrawing(layer)).Any(), "A flying face without a visible landing covered the current human decision.");
        foreach (var settled in root.Children.OfType<ContentControl>()) settled.Visibility = Visibility.Visible;
        layer.InvalidateVisual();
        layer.IsPromptVisible = false;
        var onRender = typeof(BattleFeedbackLayer).GetMethod("OnRender", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var visual = new DrawingVisual();
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < 120; frame++)
        {
            using var drawing = visual.RenderOpen();
            onRender.Invoke(layer, [drawing]);
        }
        Console.WriteLine($"120 seven-card drawing preparations: {System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds:F1} ms; {GC.GetAllocatedBytesForCurrentThread() - allocated:N0} bytes (not screen FPS).");
        cues.Clear();
    }

    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Images(child)) yield return nested;
    }

    private static IEnumerable<(ImageDrawing Image, Rect Bounds)> ImageBounds(Drawing? drawing, Matrix transform)
    {
        if (drawing is ImageDrawing image) yield return (image, new MatrixTransform(transform).TransformBounds(image.Rect));
        if (drawing is DrawingGroup group)
        {
            var nestedTransform = group.Transform?.Value ?? Matrix.Identity;
            nestedTransform.Append(transform);
            foreach (var child in group.Children)
                foreach (var nested in ImageBounds(child, nestedTransform)) yield return nested;
        }
    }

    public static void InquiryLifetime(string output)
    {
        CheckAnimationBoundary();
        using var vm = new MainViewModel(false, 17, false, new MemorySaveStore(),
            historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
            { IsMotionEnabled = true, IsSoundEnabled = false };
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var clock = new FrameClock();
        layer.Clock = clock;
        var delivered = new List<BattleCue>();
        vm.BattleCues.CollectionChanged += (_, args) =>
        {
            if (args.NewItems is not null) delivered.AddRange(args.NewItems.OfType<BattleCue>());
        };
        try
        {
            for (var step = 0; step < 120 && !vm.HasGameOver; step++)
            {
                clock.Advance(2);
                layer.InvalidateVisual();
                root.Measure(new Size(1120, 740));
                root.Arrange(new Rect(0, 0, 1120, 740));
                root.UpdateLayout();
                new RenderTargetBitmap(1120, 740, 96, 96, PixelFormats.Pbgra32).Render(root);
                delivered.Clear();
                Step(vm);
                if (!vm.CanStepAi || !delivered.Any(cue => cue.IsInquiry)) continue;

                clock.Advance(.15);
                Render(root, 1120, 740, Path.Combine(output, "feedback-inquiry-before-answer.png"));
                foreach (var card in delivered.Where(cue => cue.Kind == BattleCueKind.Card))
                    Assert(Images(VisualTreeHelper.GetDrawing(layer)).Any(image => ReferenceEquals(image.ImageSource, CardArt.Get(card.CardKind))),
                        "A real committed card use flew as a placeholder instead of its declared face.");
                var retained = delivered.Count(cue => !cue.IsInquiry);
                var inquiries = delivered.Count(cue => cue.IsInquiry);
                Assert(layer.ActiveEffectCount == delivered.Count, "The current public inquiry did not reach the bound animation layer.");
                var before = Engine(vm).Revision;
                delivered.Clear();
                Step(vm);
                clock.Advance(.15);
                Render(root, 1120, 740, Path.Combine(output, "feedback-inquiry-after-answer.png"));
                Console.WriteLine($"Inquiry lifetime: revision {before} -> {Engine(vm).Revision}, expired inquiries={inquiries}, expected active={retained + delivered.Count}, actual active={layer.ActiveEffectCount}.");
                Assert(Engine(vm).Revision > before && layer.ActiveEffectCount == retained + delivered.Count,
                    "An accepted continuation kept the previous waiting-for-response animation over the new feedback.");
                return;
            }
            Assert(false, "The fixed match did not reach an automatic response inquiry.");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void CheckAnimationBoundary()
    {
        var cues = new ObservableCollection<BattleCue>();
        var layer = new BattleFeedbackLayer { Cues = cues, CommittedRevision = 10, Clock = new FrameClock() };
        layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
        var inquiry = new BattleCue(1, BattleCueKind.ResponseWindow, 1, [0], "等待闪响应", "响应者")
            { IsInquiry = true, Revision = 10 };
        try
        {
            cues.Add(inquiry);
            layer.CommittedRevision = 10;
            Assert(layer.ActiveEffectCount == 1 && layer.IsFrameTimerRunning,
                "An unchanged command boundary hid the current inquiry.");
            layer.CommittedRevision = 11;
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && cues.Count == 1,
                "A continuation without a new cue kept drawing an expired inquiry or deleted public history.");
            cues.Add(inquiry with { Sequence = 2 });
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning,
                "Late delivery restarted an inquiry from an earlier command.");
            cues.Add(inquiry with { Sequence = 3, Revision = 12 });
            layer.CommittedRevision = 12;
            Assert(layer.ActiveEffectCount == 1,
                "A delayed revision binding removed the new command's inquiry.");
            cues.Add(new BattleCue(4, BattleCueKind.Response, 1, [], "打出闪", "响应者") { Revision = 12 });
            layer.CommittedRevision = 13;
            Assert(layer.ActiveEffectCount == 1, "Retiring an inquiry removed its actual response feedback.");
            layer.MotionEnabled = false;
            layer.MotionEnabled = true;
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning,
                "Re-enabling animations replayed old inquiry history.");
        }
        finally { layer.RaiseEvent(new RoutedEventArgs(FrameworkElement.UnloadedEvent)); }
    }

    public static void PublicProjection()
    {
        using var vm = new MainViewModel(false, 721019, false, new MemorySaveStore());
        var view = Engine(vm).CreateSnapshot(0);
        var sequence = 0L;
        EventEnvelope Envelope(IGameEvent payload) => new(new EventId(++sequence), null, sequence, 1, "feedback-check", payload);
        var privateEvents = new[]
        {
            Envelope(new GeneralSelectionRequestedEvent(1, ["private-general"])),
            Envelope(new GeneralSelectedEvent(1, "private-general")),
            Envelope(new CardMovedEvent(9876, CardKind.Peach, CardLocation.DrawPile,
                new CardLocation(CardZoneKind.Hand, 1), CardMoveReasons.Draw))
        };
        Assert(BattleCueProjector.Project(privateEvents, view).Count == 0, "Hidden setup or hand movements leaked into feedback.");
        int[] targets = [1, 1, -1, 99];
        var publicEvents = new[]
        {
            Envelope(new CardUseDeclaredEvent(7, 2468, CardKind.Slash, 0)),
            Envelope(new TargetsConfirmedEvent(7, targets)),
            Envelope(new CardRespondedEvent(1357, 1, 0, CardKind.Slash)),
            Envelope(new DuelResponseEvent(7, 1, true, 1357, CardKind.Slash)),
            Envelope(new DamageAppliedEvent(0, 1, 2, 1, DamageNature.Fire)),
            Envelope(new RecoveryAppliedEvent(2, 1, 1, 2)),
            Envelope(new DamageAppliedEvent(0, 1, 0, 2))
        };
        var cues = BattleCueProjector.Project(publicEvents, view);
        targets[0] = 4;
        Assert(cues.Count == 4 && cues.Select(cue => cue.Sequence).Distinct().Count() == 4, "Public response duplicated or zero damage rendered.");
        Assert(cues.All(cue => cue.Revision == 1), "Public feedback lost its committed command revision.");
        Assert(cues[0].Label == "杀" && cues[0].TargetSeats.SequenceEqual([1]), "Declared card or detached public targets differ.");
        Assert(cues[0].CardKind == CardKind.Slash && cues[1].CardKind == CardKind.Slash && cues.Skip(2).All(cue => cue.CardKind is null),
            "Use and response artwork must come from their public effective kinds, never from private card IDs.");
        var recastCue = BattleCueProjector.Project([Envelope(new CardRecastEvent(0, 300, CardKind.IronChain, 1))], view).Single();
        Assert(recastCue is { CardKind: CardKind.IronChain, Detail: "重铸" }, "Recast lost its public card kind or action label.");
        Assert(cues[1].Label == "打出杀" && cues[2].Label == "−2" && cues[2].Nature == DamageNature.Fire && cues[3].Label == "+1", "Response, damage, or recovery feedback differs from committed events.");
        var sourceFreeEvents = new[] { Envelope(new DamageAppliedEvent(1, 1, 1, 0) { SourceLess = true }) };
        Assert(BattleCueProjector.Project(sourceFreeEvents, view).Single() is
            { Kind: BattleCueKind.Damage, SourceSeat: -1, TargetSeats: [1] },
            "Source-free damage feedback falsely attributes its internal continuation seat.");
        var sourceFreeReport = MatchSummary.Create(view with { Status = EngineStatus.Completed }, sourceFreeEvents)!;
        Assert(sourceFreeReport.Players.Single(player => player.Seat == 1) is { DamageDealt: 0, DamageTaken: 1 },
            "Source-free debt damage must count as received damage without crediting a damage dealer.");
        var virtualResponses = BattleCueProjector.Project(
            [Envelope(new CardRespondedEvent(-1, 1, 0, CardKind.Dodge)),
             Envelope(new CardRespondedEvent(-1, 2, 0, CardKind.Dodge))], view);
        Assert(virtualResponses.Count == 2 && virtualResponses.All(cue => cue.Label == "打出闪"),
            "Distinct virtual responses must not be collapsed by their shared nonphysical card sentinel.");
        var zeroResponses = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardRespondedEvent(0, 1, 0, CardKind.Dodge)),
             Envelope(new GroupResponseEvent(30, CardKind.ArrowBarrage, CardKind.Dodge, 1, true, 0)),
             Envelope(new CardRespondedEvent(0, 2, 0, CardKind.Dodge)),
             Envelope(new GroupResponseEvent(30, CardKind.ArrowBarrage, CardKind.Dodge, 2, true, 0))], view);
        Assert(zeroResponses.Count == 2 && zeroResponses.Select(cue => cue.SourceSeat).SequenceEqual([1, 2]),
            "The zero virtual-card sentinel collapsed distinct players or duplicated their group results.");
        var unknownResponse = BattleCueProjector.ProjectCardPlays([Envelope(new CardRespondedEvent(9876, 1, 0))], view).Single();
        Assert(unknownResponse.CardKind is null && unknownResponse.Label == "打出响应牌",
            "A legacy response without a public kind inferred a hidden physical face.");
        var counterspellCards = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardRespondedEvent(46, 1, 0, CardKind.Nullification)),
             Envelope(new NullificationRespondedEvent(21, 45, CardKind.Duel, 1, 46, true, 1))], view);
        Assert(counterspellCards.Count == 1 && counterspellCards[0].CardKind == CardKind.Nullification,
            "The committed counterspell and its chain result produced two public cards.");
        var rescueCards = BattleCueProjector.ProjectCardPlays(
            [Envelope(new CardUseDeclaredEvent(31, 99, CardKind.Peach, 1)),
             Envelope(new DyingResponseEvent(32, 1, true, 99) { UsedPeachPhysicalCardKind = CardKind.Dodge })], view);
        Assert(rescueCards.Count == 1 && rescueCards[0].CardKind == CardKind.Peach && rescueCards[0].CardActionLabel == "使用",
            "Converted rescue duplicated its declared use or displayed its physical material as the effective card.");
        var armorCues = BattleCueProjector.Project(
        [
            Envelope(new ArmorEffectAppliedEvent(8, CardKind.RenwangShield, 0, 1, CardKind.Slash)),
            Envelope(new JudgmentResolvedEvent(
                9,
                8,
                1,
                JudgmentReasons.BaguaDefense,
                42,
                CardKind.Dodge,
                Suit.Heart,
                Rank: 7,
                Succeeded: true))
        ], view);
        Assert(
            armorCues.Count == 2 &&
            armorCues[0].Label.Contains("仁王盾") &&
            armorCues[0].Label.Contains("无效") &&
            armorCues[1].Kind == BattleCueKind.Judgment &&
            armorCues[1].Label == "八卦阵 · ♥7" &&
            armorCues[1].Detail == "红色 · 视为打出闪",
            "Formal armor outcomes were not projected as public battle feedback.");
        var judgmentCues = BattleCueProjector.Project(
        [
            Envelope(new JudgmentRequestedEvent(10, 7, 1, JudgmentReasons.Indulgence, CardKind.Indulgence)),
            Envelope(new JudgmentReplacementResolvedEvent(11, 10, 1, 0, JudgmentReasons.Indulgence, true,
                40, 41, CardKind.Peach, Suit.Heart, 12)),
            Envelope(new JudgmentResolvedEvent(10, 7, 1, JudgmentReasons.Indulgence, 41, CardKind.Peach,
                Suit.Heart, 12, true)),
            Envelope(new JudgmentResolvedEvent(12, 8, 1, JudgmentReasons.Lightning, 43, CardKind.Slash,
                Suit.Spade, 5, true))
        ], view);
        Assert(judgmentCues.Count == 4 &&
               judgmentCues[0] is { Kind: BattleCueKind.Judgment, Label: "乐不思蜀 · 判定中" } &&
               judgmentCues[1] is { SourceSeat: 0, TargetSeats: [1], Label: "鬼才改判 · ♥Q", Detail: "最终判定牌已替换" } &&
               judgmentCues[2] is { Label: "乐不思蜀 · ♥Q", Detail: "红桃 · 不跳过出牌阶段" } &&
               judgmentCues[3] is { Label: "闪电 · ♠5", Detail: "黑桃 2–9 · 命中" },
            "Judgment lifecycle, replacement, card face, or rule outcome was not projected exactly.");
        var responseCues = BattleCueProjector.Project(
        [
            Envelope(new ResponseRequestedEvent(0, 1, CardKind.Slash, CardKind.Dodge)),
            Envelope(new RequiredResponseProgressEvent(20, 0, 1, CardKind.Slash, CardKind.Dodge, 1, 2)),
            Envelope(new NullificationRequestedEvent(21, 45, CardKind.Duel, 0, 1, false, 0)),
            Envelope(new NullificationRespondedEvent(21, 45, CardKind.Duel, 1, 46, true, 1)),
            Envelope(new NullificationRequestedEvent(21, 45, CardKind.Duel, 0, 2, true, 1)),
            Envelope(new NullificationResolvedEvent(21, 45, CardKind.Duel, true, 1))
        ], view);
        Assert(responseCues.Count == 6 &&
               responseCues[0] is { Kind: BattleCueKind.ResponseWindow, SourceSeat: 1, TargetSeats: [0], Label: "等待闪响应", Detail: "响应【杀】" } &&
               responseCues[1] is { Label: "连续响应 1 / 2", Detail: "已打出【闪】" } &&
               responseCues[2] is { Label: "无懈可击询问中 · 第 1 层", Detail: "当前锦囊生效中 · 可令其失效" } &&
               responseCues[3] is { Kind: BattleCueKind.Response, Label: "打出无懈可击 · 第 1 层", Detail: "锦囊暂时失效" } &&
               responseCues[4] is { Label: "无懈可击询问中 · 第 2 层", Detail: "当前锦囊已失效 · 可反制恢复" } &&
               responseCues[5] is { SourceSeat: -1, Label: "决斗 · 已失效", Detail: "无懈链共 1 次响应" },
            "Response request, locked progress, or layered Nullification state was not projected exactly.");
        Assert(responseCues.Select(cue => cue.IsInquiry).SequenceEqual(new[] { true, false, true, false, true, false }),
            "Response progress, a played response or the final outcome was treated as an unanswered inquiry.");
        var iFieldView = view with
        {
            Players = view.Players.Select(player => player.Seat == 0
                ? player with
                {
                    Skills = [new GeneralSkillDefinition("I力场", "")
                    { ContentId = "classic:i-field" }]
                }
                : player).ToArray()
        };
        var iFieldCue = BattleCueProjector.Project(
            [Envelope(new WuyanDamagePreventedEvent(22, 1, 0, CardKind.Duel, 1, 0,
                "classic:i-field"))], iFieldView).Single();
        Assert(iFieldCue.Label == "I力场 · 锦囊伤害已防止",
            "Incoming trick prevention must display the skill that actually prevented damage.");
    }

    public static void PublicCardTable(string output)
    {
        var fixture = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, arrowBarrage: true);
        using var vm = fixture.Model;
        vm.IsMotionEnabled = true;
        var window = new MainWindow(vm);
        var root = (FrameworkElement)window.Content;
        var layer = (BattleFeedbackLayer)window.FindName("BattleFeedback");
        var clock = new FrameClock();
        layer.Clock = clock;
        try
        {
            Render(root, 1120, 740, Path.Combine(output, "public-card-table-arrow-before.png"));
            var viewport = (Viewbox)window.FindName("TablePlaysViewport");
            var prompt = (Border)window.FindName("DecisionPanel");
            Assert(vm.AreTablePlaysVisible && viewport.Visibility == Visibility.Visible &&
                   vm.RecentPlays[0] is { Kind: CardKind.ArrowBarrage, ActionLabel: "使用", SourceSeat: 1 },
                "A real pending Arrow Barrage hid its declared public use from the center.");
            var promptBounds = prompt.TransformToVisual(root).TransformBounds(new Rect(prompt.RenderSize));
            var cardBounds = Find<Border>(root).Where(border => BattleFeedbackLayer.GetPlayAnchor(border) >= 0)
                .Select(border => border.TransformToVisual(root).TransformBounds(new Rect(border.RenderSize))).ToArray();
            Console.WriteLine($"Public row bounds: prompt={promptBounds}; cards={string.Join("; ", cardBounds.Select(bounds => bounds.ToString()))}.");
            Assert(cardBounds.Length == vm.RecentPlays.Count && cardBounds.All(bounds => bounds.Top >= promptBounds.Bottom && bounds.Height > 80),
                "The public card row overlapped the hand response description or disappeared in the small window.");
            var card = vm.Hand.First(item => item.Kind == CardKind.Dodge && item.IsPlayable);
            var before = Engine(vm).Revision;
            vm.SelectCardCommand.Execute(card);
            Assert(Engine(vm).Revision == before, "Selecting a response card started a public play before commit.");
            vm.ConfirmSelectedCommand.Execute(null);
            Assert(Engine(vm).Revision == before + 1 && vm.RecentPlays.Last() is
                    { Kind: CardKind.Dodge, ActionLabel: "打出", SourceSeat: 0 } response && response.PublicCardId == card.Id &&
                   vm.RecentPlays.Count(play => play.PublicCardId == card.Id) == 1 && vm.RecentPlays.Count <= 5,
                "A committed physical Dodge lost its actor, duplicated its group result or displaced the group use.");
            clock.Advance(.12);
            Render(root, 1120, 740, Path.Combine(output, "public-card-table-dodge-moving.png"));
            Assert(Find<Border>(root).Where(border => BattleFeedbackLayer.GetPlayAnchor(border) >= 0)
                    .All(border => border.TransformToVisual(root).TransformBounds(new Rect(border.RenderSize)).Height > 80),
                "Returning to AI playback shrank the public uses and responses below readable card size.");
            var moving = ImageBounds(VisualTreeHelper.GetDrawing(layer), Matrix.Identity)
                .Single(image => ReferenceEquals(image.Image.ImageSource, CardArt.Get(CardKind.Dodge))).Bounds;
            clock.Advance(.43);
            layer.InvalidateVisual();
            Render(root, 1120, 740, Path.Combine(output, "public-card-table-dodge-landed.png"));
            var landed = ImageBounds(VisualTreeHelper.GetDrawing(layer), Matrix.Identity)
                .Single(image => ReferenceEquals(image.Image.ImageSource, CardArt.Get(CardKind.Dodge))).Bounds;
            Assert((new Point(moving.X, moving.Y) - new Point(landed.X, landed.Y)).Length > 10,
                "The actual committed Dodge had a badge but no movement from its responder to the public row.");
            var captions = Find<TextBlock>(root).Where(text => text.Name == "PlayActorCaption").Select(text => text.Text).ToArray();
            Assert(captions.Contains(vm.RecentPlays[0].ActorName + "使用") && captions.Contains(vm.RecentPlays.Last().ActorName + "打出"),
                "Public use and response faces did not display their actor/action captions.");
            vm.IsMotionEnabled = false;
            Render(root, 1440, 880, Path.Combine(output, "public-card-table-motion-off.png"));
            Assert(layer.ActiveEffectCount == 0 && !layer.IsFrameTimerRunning && vm.RecentPlays.Any(play => play.Kind == CardKind.Dodge),
                "Disabling animation removed the settled response card or kept its frame timer running.");
            var row = vm.RecentPlays.Select(play => (play.PublicCardId, play.Kind, play.SourceSeat, play.Caption)).ToArray();
            vm.SaveGameCommand.Execute(null);
            vm.LoadManualGameCommand.Execute(null);
            Assert(!vm.HasSaveError && layer.ActiveEffectCount == 0 && row.SequenceEqual(
                    vm.RecentPlays.Select(play => (play.PublicCardId, play.Kind, play.SourceSeat, play.Caption))),
                "Loading the same boundary lost the public response row or replayed old animations.");
            StableHandResponseFixture.RecordAndReplay(Engine(vm), fixture.Registry, output, "public-card-table-after");
        }
        finally { window.Content = null; window.Close(); }
    }


    private static void Play(MainViewModel vm, LegalAction action)
    {
        vm.SelectCardCommand.Execute(vm.Hand.Single(card => card.Id == action.CardId));
        if (action.TargetSeat is { } seat && !vm.Seats.Single(player => player.Seat == seat).IsSelectedTarget)
            vm.SelectTargetCommand.Execute(vm.Seats.Single(player => player.Seat == seat));
        Assert(vm.CanConfirmSelected, "Feedback fixture selected an unusable card.");
        vm.ConfirmSelectedCommand.Execute(null);
    }

    private static void Step(MainViewModel vm)
    {
        if (vm.IsGeneralSelectionPending) vm.SelectGeneralChoiceCommand.Execute(vm.GeneralChoices[0]);
        else if (vm.IsDiscardSelectionPending) ResolveDiscard(vm);
        else if (vm.CanStepAi) vm.StepAiCommand.Execute(null);
        else if (vm.CanEndTurn)
        {
            var peach = Engine(vm).GetHumanLegalActions().FirstOrDefault(action => action.CardId is { } id &&
                vm.Hand.Single(card => card.Id == id).Name == "桃" && action.TargetSeat == 0);
            if (peach is not null) Play(vm, peach); else vm.EndTurnCommand.Execute(null);
        }
        else if (vm.CanDeclineResponse) vm.DeclineResponseCommand.Execute(null);
        else
        {
            var choices = new (IEnumerable<PromptChoice>, ICommand)[]
            {
                (vm.ResponseChoices, vm.SelectResponseChoiceCommand), (vm.DyingChoices, vm.SelectDyingChoiceCommand),
                (vm.HarvestChoices, vm.SelectHarvestChoiceCommand), (vm.TargetCardChoices, vm.SelectTargetCardChoiceCommand),
                (vm.FireAttackChoices, vm.SelectFireAttackChoiceCommand),
                (vm.NullificationChoices, vm.SelectNullificationChoiceCommand), (vm.SkillChoices, vm.SelectSkillChoiceCommand)
            };
            var available = choices.FirstOrDefault(pair => pair.Item1.Any());
            Assert(available.Item1 is not null, $"Feedback fixture has no continuation at {Engine(vm).State.Status}.");
            available.Item2.Execute(available.Item1!.First());
        }
        Assert(!vm.PromptText.Contains("未执行"), vm.PromptText);
    }

    private sealed class FrameClock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(double seconds) => _ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
