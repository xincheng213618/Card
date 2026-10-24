using System.Reflection;
using System.Windows;
using System.Windows.Media;
using CardGame.Core;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Presentation;
using static Program;

internal static class ColdSlashUiChecks
{
    public static void PhysicalResponseIncomingAndSlashEffects(string output)
    {
        var (responseModel, _) = StableHandResponseFixture.Create(DecisionKind.RespondSlash, output, coldSlash: true);
        using (responseModel)
        {
            var game = Engine(responseModel);
            var prompt = game.PendingDecision!;
            var card = responseModel.Hand.First(c => c.IsPlayable && c.Kind == CardKind.ColdSlash);
            var choice = prompt.Choices.Single(c => c.Cards.SequenceEqual([card.Id]));
            var before = game.Events.Count;
            responseModel.SelectCardCommand.Execute(card);
            Assert(responseModel.PlayButtonText == "打出杀" && responseModel.CanConfirmSelected &&
                choice.Parameters.GetValueOrDefault("conversion-skill-id") is null,
                "A printed Cold Slash must select the native Slash response, without a conversion caption or source.");
            responseModel.ConfirmSelectedCommand.Execute(null);
            Assert(game.Revision == prompt.Revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                answer.PromptId == prompt.PromptId && answer.Choice == choice.Id &&
                game.Events.Skip(before).Any(e => e.Payload is CardRespondedEvent paid &&
                    paid.CardId == card.Id && paid.ResponderSeat == 0 && paid.EffectiveCardKind == CardKind.Slash &&
                    paid.PublicCard is { Kind: CardKind.ColdSlash }) &&
                game.CardMovements.Any(m => m.CardId == card.Id && m.From == CardLocation.Hand(0) &&
                    m.To == CardLocation.Processing && m.CardKind == CardKind.ColdSlash),
                "Confirming a Cold Slash response must pay the selected physical entity through the published native choice exactly once.");
        }

        var (incomingModel, _) = StableHandResponseFixture.Create(DecisionKind.RespondDodge, output, coldSlash: true);
        using (incomingModel)
        {
            var game = Engine(incomingModel);
            Assert(game.PendingDecision is { Kind: DecisionKind.RespondDodge, IncomingCard: CardKind.ColdSlash, PlayerSeat: 0 } &&
                incomingModel.CurrentDecisionContext?.Title.Contains("【冰杀】") == true &&
                incomingModel.EventStack.Any(line => line.Contains("UseCard(ColdSlash)")) &&
                incomingModel.EventStack.Any(line => line.Contains("AskForResponse(Dodge)")),
                "An actual incoming Cold Slash must retain its printed name and the native Dodge response context.");
            var cue = BattleCueProjector.Project(game.Events, game.CreateSnapshot(0)).Single(c =>
                c is { Kind: BattleCueKind.Card, CardKind: CardKind.ColdSlash, SourceSeat: 1 });
            Assert(cue.TargetSeats.SequenceEqual([0]) && cue.PublicCard is { Kind: CardKind.ColdSlash },
                "The Cold Slash effect witness must come from the genuine public physical declaration.");
            var first = Paint(cue, .18);
            var later = Paint(cue, .32);
            Assert(Shapes(first).Any(IsSlashBlade) && Shapes(later).Any(IsSlashBlade) && first.Bounds != later.Bounds,
                "A Cold Slash must enter the moving Slash blade effect instead of the generic card aura.");
        }
    }

    private static DrawingGroup Paint(BattleCue cue, double progress)
    {
        var source = new Rect(40, 40, 100, 150);
        var seats = new Dictionary<int, Rect> { [1] = source, [0] = new(600, 250, 100, 150) };
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
            typeof(BattleFeedbackLayer).Assembly.GetType("CardGame.Wpf.Controls.BattleEffectPainter")!
                .GetMethod("Draw", BindingFlags.Public | BindingFlags.Static)!
                .Invoke(null, [context, cue, progress, source, seats]);
        return visual.Drawing;
    }

    private static IEnumerable<GeometryDrawing> Shapes(Drawing drawing)
    {
        if (drawing is GeometryDrawing shape) yield return shape;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children)
                foreach (var nested in Shapes(child)) yield return nested;
    }

    private static bool IsSlashBlade(GeometryDrawing shape) => shape is
        { Geometry: StreamGeometry, Brush: SolidColorBrush fill, Pen: null } &&
        fill.Color == Color.FromRgb(255, 203, 100);
}
