using System.Windows;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.Presentation;

namespace CardGame.Wpf.Controls;

public sealed partial class BattleFeedbackLayer
{
    public static readonly DependencyProperty PublicChoiceAnchorProperty = DependencyProperty.RegisterAttached(
        "PublicChoiceAnchor", typeof(int), typeof(BattleFeedbackLayer), new PropertyMetadata(-1));
    public static int GetPublicChoiceAnchor(DependencyObject element) => (int)element.GetValue(PublicChoiceAnchorProperty);
    public static void SetPublicChoiceAnchor(DependencyObject element, int value) => element.SetValue(PublicChoiceAnchorProperty, value);
    public static readonly DependencyProperty IsPublicChoiceAreaProperty = DependencyProperty.RegisterAttached(
        "IsPublicChoiceArea", typeof(bool), typeof(BattleFeedbackLayer), new PropertyMetadata(false));
    public static bool GetIsPublicChoiceArea(DependencyObject element) => (bool)element.GetValue(IsPublicChoiceAreaProperty);
    public static void SetIsPublicChoiceArea(DependencyObject element, bool value) => element.SetValue(IsPublicChoiceAreaProperty, value);
    private readonly Dictionary<int, Rect> _publicChoiceOrigins = [];

    private void CachePublicChoiceOrigins()
    {
        if (CenterAnchor is null || !MotionEnabled || !IsPromptVisible) return;
        Visit(CenterAnchor);
        while (_publicChoiceOrigins.Count > 64) _publicChoiceOrigins.Remove(_publicChoiceOrigins.Keys.First());
        void Visit(DependencyObject element)
        {
            if (element is UIElement { Visibility: not Visibility.Visible }) return;
            if (element is FrameworkElement choice && GetPublicChoiceAnchor(choice) is var id && id > 0)
            {
                var bounds = Bounds(choice);
                if (!bounds.IsEmpty) _publicChoiceOrigins[id] = bounds;
                return;
            }
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) Visit(VisualTreeHelper.GetChild(element, i));
        }
    }
    public static readonly DependencyProperty DiscardPileAnchorProperty = DependencyProperty.Register(
        nameof(DiscardPileAnchor), typeof(FrameworkElement), typeof(BattleFeedbackLayer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public FrameworkElement? DiscardPileAnchor
    {
        get => (FrameworkElement?)GetValue(DiscardPileAnchorProperty);
        set => SetValue(DiscardPileAnchorProperty, value);
    }

    private static bool IsTransfer(BattleCue cue) => cue.Kind is BattleCueKind.CardTransfer or BattleCueKind.CardDiscard;

    private void DrawCardTransfer(DrawingContext dc, TimedCue item, double elapsed,
        IReadOnlyDictionary<int, Rect> seats, FrameGeometry frame)
    {
        if (elapsed >= DrawArrivalTime + .16) return;
        var cue = item.Cue;
        var discard = cue.Kind == BattleCueKind.CardDiscard;
        var donor = seats.GetValueOrDefault(cue.SourceSeat, Rect.Empty);
        var arena = frame.Bounds(CenterAnchor);
        // A public choice starts in the shared revealed-card area. A hidden
        // hand transfer starts beside its owner and never consults a card ID.
        var launch = !item.PublicChoiceOrigin.IsEmpty ? Center(item.PublicChoiceOrigin) : donor.IsEmpty
            ? arena.IsEmpty ? new Point(ActualWidth * .45, ActualHeight * .4) : Center(arena)
            : new Point(Center(donor).X > ActualWidth / 2 ? donor.Left - 36 : donor.Right + 36,
                donor.Top + donor.Height * .4);
        Point destination;
        if (discard)
        {
            var pile = frame.Bounds(DiscardPileAnchor);
            if (pile.IsEmpty) pile = frame.Bounds(DrawPileAnchor);
            if (pile.IsEmpty) return;
            destination = new Point(pile.Left - 48, pile.Top - 24);
        }
        else
        {
            if (cue.TargetSeats.Count == 0 || !seats.TryGetValue(cue.TargetSeats[0], out var receiver)) return;
            var hand = cue.TargetSeats[0] == HandOwnerSeat ? frame.Bounds(HandRoot) : Rect.Empty;
            destination = hand.IsEmpty
                ? new Point(Center(receiver).X > ActualWidth / 2 ? receiver.Left - 14 : receiver.Right + 14, Center(receiver).Y)
                : new Point(hand.Left + hand.Width / 2, hand.Top + HandPanel.ArrivalEntryInset);
        }
        // Use measured button and glyph ink, as for played-card routes. Full
        // TextBlock layout boxes can include large areas with no visible ink.
        // Portraits are protected whole; the hand is the arrival destination.
        var obstacles = FlightCommandBounds(ActionBar).Concat(FlightCommandBounds(SupplementalActions))
            .Concat(FlightCommandBounds(CenterAnchor, excludePublicChoices: !item.PublicChoiceOrigin.IsEmpty)).Concat(seats.Values)
            .Append(frame.VisibleBounds(Sidebar)).Append(frame.VisibleBounds(PhaseHud))
            // Public picks can fly over the remaining pool as it reflows after
            // removal. Other commands, response captions and portraits remain protected.
            .Where(bounds => !bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0 &&
                (item.PublicChoiceOrigin.IsEmpty || !bounds.IntersectsWith(item.PublicChoiceOrigin))).ToArray();
        var path = item.GetTransferPath(launch, destination, obstacles, frame.FlightViewport);
        var accent = Frozen(discard ? Color.FromRgb(233, 148, 110) : Color.FromRgb(121, 218, 197));
        // Preserve command/readout visibility while leaving the hand-entry
        // corridor open. The layer has no input hit area.
        Geometry clip = new RectangleGeometry(frame.FlightViewport);
        foreach (var area in obstacles)
            if (!area.IsEmpty) clip = new CombinedGeometry(GeometryCombineMode.Exclude, clip, new RectangleGeometry(area));
        dc.PushClip(clip);
        // Stagger along the same validated route instead of offsetting a fan
        // into nearby controls. Every back arrives at the shared handoff time.
        // Larger gifts keep a bounded trail; the cue caption reports the full count.
        for (var index = Math.Clamp(cue.TransferCount, 1, 3) - 1; index >= 0; index--)
        {
            var start = index * .04;
            if (elapsed <= start) continue;
            var move = Math.Clamp((elapsed - start) / (DrawArrivalTime - start), 0, 1);
            var eased = 1 - Math.Pow(1 - move, 2);
            var point = path.PositionAt(move);
            var scale = (1 - .6 * eased) * path.ScaleAt(eased);
            var alpha = Math.Min(1, (elapsed - start) / .035) * Math.Clamp((DrawArrivalTime + .16 - elapsed) / .16, 0, 1);
            DrawTransferFace(dc, item, point, scale, move, alpha, accent, discard);
        }
        dc.Pop();
    }

    private static void DrawTransferFace(DrawingContext dc, TimedCue item, Point point, double scale,
        double move, double alpha, Brush accent, bool discard)
    {
        var cue = item.Cue;
        dc.PushOpacity(alpha);
        dc.PushTransform(new RotateTransform((discard ? -10 : 8) * Math.Sin(Math.PI * move), point.X, point.Y));
        dc.PushTransform(new ScaleTransform(scale, scale, point.X, point.Y));
        var body = new Rect(point.X - 23, point.Y - 32, 46, 64);
        dc.DrawRoundedRectangle(Frozen(Color.FromRgb(31, 43, 43)), new Pen(accent, 1.5), body, 4, 4);
        var inner = body; inner.Inflate(-5, -5);
        if (cue.PublicCardId is > 0 && cue.CardKind is not null && item.Artwork is { } artwork)
        {
            dc.DrawImage(artwork, inner);
        }
        else
        {
            dc.DrawRoundedRectangle(null, new Pen(accent, .8), inner, 2, 2);
            var diamond = new StreamGeometry();
            using (var ornament = diamond.Open())
            {
                ornament.BeginFigure(point + new Vector(0, -12), false, true);
                ornament.LineTo(point + new Vector(9, 0), true, false);
                ornament.LineTo(point + new Vector(0, 12), true, false);
                ornament.LineTo(point + new Vector(-9, 0), true, false);
            }
            diamond.Freeze();
            dc.DrawGeometry(null, new Pen(accent, 1), diamond);
        }
        dc.Pop(); dc.Pop(); dc.Pop();
    }
}
