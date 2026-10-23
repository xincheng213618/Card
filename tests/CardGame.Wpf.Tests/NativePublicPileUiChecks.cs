using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.Presentation;
using CardGame.Wpf.ViewModels;

internal static class NativePublicPileUiChecks
{
    public static void OriginalGrainPlayRailAndResponseFaces()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        PlayRail(registry);
        ResponseFace(registry);
    }

    private static void PlayRail(ContentRegistry registry)
    {
        using var model = Restore(Create(registry), registry);
        var game = Program.Engine(model);
        var grain = Grain(model.ChoiceSnapshot.Players[0]);
        var slash = grain.Single(card => card.Kind == CardKind.Slash);
        var action = game.GetHumanLegalActions().First(a => a.CardId == slash.Id &&
            a.Kind == LegalActionKind.Slash && a.TargetSeats.Count == 1 &&
            a.NativePublicPileSource?.SkillId == "ol:shuliang" && a.ConversionSource is null);
        Program.Assert(grain.Count == 3 && grain.Select(c => CardCatalog.Get(c.Kind).CategoryName).Distinct().Count() == 3 &&
            model.Hand.Count == 0 && model.ChoiceSnapshot.Players[0].HandCount == 0 &&
            grain.All(card => model.Hand.All(hand => hand.Id != card.Id)),
            "The real initial Jiyun must produce three different categories in its public grain pile, without adding grain to Hand.");
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Layout(root);
            var choice = model.EquipmentPlayChoices.Single(c => c.Cards.SequenceEqual([slash.Id]) &&
                c.Parameters.GetValueOrDefault("native-original-public-pile") == "true");
            Program.Assert(choice.Description.Contains("原牌", StringComparison.Ordinal) &&
                !choice.Description.Contains("转化", StringComparison.Ordinal) &&
                model.EquipmentPlayChoices.All(c => c.Cards.Count == 1 && grain.Any(card => card.Id == c.Cards[0])) &&
                !model.CanConfirmSelected,
                "Ordinary Play must show an explicit original public-grain entry backed by current legal actions, before selecting any conversion skill.");
            var commands = game.AcceptedCommands.Count;
            var revision = game.Revision;
            model.SelectEquipmentPlayChoiceCommand.Execute(choice with { });
            Program.Assert(game.Revision == revision && !model.CanConfirmSelected,
                "A copied regional choice must not enter the current published selection.");
            Click(root, model.SelectEquipmentPlayChoiceCommand, choice);
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == action.TargetSeats[0]));
            Layout(root);
            Program.Assert(model.CanConfirmSelected && game.AcceptedCommands.Count == commands && game.Revision == revision &&
                game.CardMovements.All(m => m.CardId != slash.Id || m.To.Zone != CardZoneKind.Processing),
                "Selecting original grain and its real target must prepare a reversible draft without paying or submitting.");
            var moves = game.CardMovements.Count;
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.AcceptedCommands.Count == commands + 1 &&
                game.AcceptedCommands.Last() is PlayCardCommand played && played.CardId == slash.Id &&
                played.ConversionSource is null && played.PlayedCardKind == action.PlayedCardKind &&
                played.TargetSeats!.SequenceEqual(action.TargetSeats) &&
                game.CardMovements.Skip(moves).Count(m => m.CardId == slash.Id &&
                    m.From.Zone == CardZoneKind.PublicPersistentPile && m.From.OwnerSeat == 0 &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                game.Events.Any(e => e.Payload is CardUseDeclaredEvent declared && declared.CardId == slash.Id &&
                    declared.CardKind == slash.Kind && declared.SourceSeat == 0 &&
                    declared.PublicCard is { } face && face.Suit == slash.Suit && face.Rank == slash.Rank) &&
                game.Events.Count(e => e.Payload is ShuliangGrainPaidEvent paid && paid.CardId == slash.Id &&
                    paid.ProviderSeat == 0 && !paid.Played) == 1,
                "Confirmation must submit the original native kind/targets with no conversion source, preserving its printed face and paying public grain once.");
            var paidCommands = game.AcceptedCommands.Count;
            var paidMoves = game.CardMovements.Count;
            model.SelectEquipmentPlayChoiceCommand.Execute(choice);
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.AcceptedCommands.Count == paidCommands && game.CardMovements.Count == paidMoves,
                "The retired original-grain entry must not submit or pay a second time.");
        }
        finally { window.Content = null; window.Close(); }
    }

    private static void ResponseFace(ContentRegistry registry)
    {
        var game = Create(registry);
        Accept(game, new UseProgramSkillCommand(0, Fixture.Driver, "incoming-duel", [], [1],
            game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.RespondSlash);
        using var model = Restore(game, registry);
        game = Program.Engine(model);
        model.IsMotionEnabled = true;
        var slash = Grain(model.ChoiceSnapshot.Players[0]).Single(card => card.Kind == CardKind.Slash);
        var prompt = model.ChoiceSnapshot.PendingDecision!;
        var choice = model.HandResponseExtraChoices.Single(c => c.Cards.SequenceEqual([slash.Id]) &&
            c.Parameters.GetValueOrDefault("native-original-public-pile") == "true");
        Program.Assert(prompt.IncomingCard == CardKind.Duel && choice.Parameters.GetValueOrDefault("response") == "slash" &&
            !choice.Parameters.ContainsKey("conversion-skill-id") && model.IsHandResponsePending && model.Hand.Count == 0 &&
            Enumerable.Range(0, 4).All(viewer => Grain(game.CreateSnapshot(viewer).Players[0]).Any(card => card.Id == slash.Id)) &&
            game.Events.All(e => e.Payload is not ShuliangGrainPaidEvent),
            "The actual Duel must publish a native Played Slash from visible public grain, without a conversion or a fake Hand entry.");
        using (var host = new NativeResponseStatusChecks.NativeHost(model))
        {
            var root = host.Root;
            Layout(root);
            var button = BoundButton(root, model.SelectHandResponseChoiceCommand, choice);
            var presentation = Program.Find<ContentControl>(button).Select(c => c.Content)
                .OfType<PromptCardChoicePresentation>().SingleOrDefault(p => p.Face?.Id == slash.Id);
            Program.Assert(presentation is { IsCard: true, IsHidden: false, Face: not null } &&
                presentation.Face.Kind == slash.Kind && presentation.Face.Rank == slash.RankText &&
                presentation.Face.SuitGlyph == (slash.Suit switch
                { Suit.Spade => "♠", Suit.Club => "♣", Suit.Heart => "♥", Suit.Diamond => "♦", _ => string.Empty }),
                "The actual response button must render the original public grain's name, suit and rank from the viewer snapshot.");
            var commands = game.AcceptedCommands.Count;
            var moves = game.CardMovements.Count;
            var eventCursor = game.Events.Count;
            var defenderHp = game.CreateSnapshot(0).Players[1].Hp;
            Click(button);
            Program.Assert(game.AcceptedCommands.Count == commands + 1 &&
                game.AcceptedCommands.Last() is AnswerPromptCommand answer && answer.PromptId == prompt.PromptId &&
                answer.Choice == choice.Id && game.CardMovements.Skip(moves).Count(m => m.CardId == slash.Id &&
                    m.From.Zone == CardZoneKind.PublicPersistentPile && m.From.OwnerSeat == 0 &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) == 1 &&
                game.Events.Count(e => e.Payload is ShuliangGrainPaidEvent paid && paid.CardId == slash.Id &&
                    paid.ProviderSeat == 0 && paid.Played) == 1,
                "Clicking the original-grain response control must answer its exact native prompt and pay that public entity once.");
            var accepted = game.AcceptedCommands.Count;
            var paidMoves = game.CardMovements.Count;
            model.SelectHandResponseChoiceCommand.Execute(choice);
            Program.Assert(game.AcceptedCommands.Count == accepted && game.CardMovements.Count == paidMoves,
                "A retired public-grain response button must not answer or pay again.");
            // The opponent has no hand cards in this fixed native fixture. Its
            // failed Duel response must commit while the paid public face flies.
            for (var step = 0; step < 30 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            {
                var revision = game.Revision;
                Program.Assert(model.CanStepAi, "The native Duel unexpectedly needs another human choice.");
                model.StepAiCommand.Execute(null);
                Program.Assert(game.Revision > revision, "The actual Duel continuation did not commit.");
            }
            var events = game.Events.Skip(eventCursor).ToArray();
            var paidResponse = events.Single(envelope => envelope.Payload is DuelResponseEvent { UsedSlash: true, ResponderSeat: 0 });
            var failed = events.Single(envelope => envelope.Payload is DuelResponseEvent { UsedSlash: false, ResponderSeat: 1 });
            var damage = events.Single(envelope => envelope.Payload is DamageAppliedEvent { SourceSeat: 0, TargetSeat: 1 });
            var duelId = ((DuelResponseEvent)paidResponse.Payload).ResolutionId;
            var damageRequest = events.Select(envelope => envelope.Payload).OfType<DamageRequestedEvent>().Single();
            var responseCue = model.BattleCues.Single(cue => cue.Kind == BattleCueKind.Response && cue.PublicCardId == slash.Id);
            Program.Assert(duelId == ((DuelResponseEvent)failed.Payload).ResolutionId &&
                ((DamageAppliedEvent)damage.Payload).SourceCardUseResolutionId == duelId && damageRequest.ResolutionId != duelId &&
                paidResponse.Sequence < failed.Sequence && failed.Sequence < damage.Sequence &&
                game.CreateSnapshot(0).Players[1].Hp == defenderHp - 1 &&
                game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && model.EndTurnCommand.CanExecute(null) &&
                responseCue is { SourceSeat: 0, TargetSeats.Count: 0, CardKind: CardKind.Slash, PublicCard: not null, DuelOpponentSeat: 1 } &&
                responseCue.ResponseResolutionId == duelId,
                "The genuine response must reverse Duel damage, retain its public original face and resume playable state before animation ends.");
            Console.WriteLine("Native Duel public order: " + string.Join("; ", events.Where(envelope =>
                envelope.Payload is CardRespondedEvent or DuelResponseEvent or DamageRequestedEvent or DamageAppliedEvent).Select(envelope =>
                $"{envelope.Sequence}@{envelope.Revision}:{envelope.Payload}")));
            host.Clock.Advance(.01);
            var first = SampleDuel(host, responseCue.Sequence, 10);
            host.Clock.Advance(.15);
            var moving = SampleDuel(host, responseCue.Sequence, 160);
            NativeActionFeedbackChecks.VerifyDuelMotion(host, responseCue);
            host.Clock.Advance(.27);
            var arrived = SampleDuel(host, responseCue.Sequence, 430);
            host.Clock.Advance(.37);
            var finished = SampleDuel(host, responseCue.Sequence, 800);
            Program.Assert(first is { Flying: true, Pending: true, Hit: false } &&
                moving is { Flying: true, Pending: true, Hit: false },
                "The actual reversed Duel hit must wait while its paid response Slash is still flying to the public row.");
            Program.Assert(arrived is { Flying: false, Pending: false, Hit: true } &&
                finished is { Flying: false, Pending: false, Hit: false },
                "The Duel hit must become visible on the correct portrait after the public Slash arrives, then retire once.");
            DuelSchedulingBoundaries(host, events, responseCue, damage);
            using var restored = Restore(game, registry);
            var coldGame = Program.Engine(restored);
            Program.Assert(SnapshotJson.Serialize(coldGame.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)) &&
                coldGame.Events.Single(envelope => envelope.Sequence == damage.Sequence).Payload is DamageAppliedEvent coldHit &&
                coldHit.SourceCardUseResolutionId == duelId && restored.BattleCues.Count == 0,
                "Cold replay must preserve the actual card-use damage identity without replaying the completed response or hit.");
        }
    }

    private static void DuelSchedulingBoundaries(NativeResponseStatusChecks.NativeHost host, EventEnvelope[] events,
        BattleCue actualResponse, EventEnvelope actualDamage)
    {
        var view = Program.Engine(host.Model).CreateSnapshot(0);
        var responseFacts = events.Where(envelope => envelope.Payload is CardRespondedEvent or DuelResponseEvent { UsedSlash: true }).ToArray();
        var response = BattleCueProjector.Project(responseFacts, view).Single();
        var laterHit = BattleCueProjector.Project([actualDamage with { Revision = actualDamage.Revision + 1 }], view).Single();
        Program.Assert(response.ResponseResolutionId == actualResponse.ResponseResolutionId && response.TargetSeats.Count == 0 &&
            laterHit.SourceCardUseResolutionId == response.ResponseResolutionId,
            "Separate committed batches must retain exact public Duel identity without reconstructing hidden resolution state.");
        var cues = host.Model.BattleCues;
        cues.Clear(); cues.Add(response);
        host.Clock.Advance(.16); cues.Add(laterHit);
        Program.Assert(SampleDuel(host, response.Sequence, 160) is { Flying: true, Pending: true, Hit: false },
            "A later-revision hit must wait for the remaining portion of its exact response flight.");
        host.Clock.Advance(.27);
        Program.Assert(SampleDuel(host, response.Sequence, 430) is { Flying: false, Pending: false, Hit: true },
            "Cross-revision scheduling restarted a full response delay instead of waiting only for its remaining flight.");

        // Same seats and close timing cannot substitute for the owning use.
        // Use the real fact with a different public use identity, then the
        // legacy absent identity; neither may attach to this Duel response.
        foreach (var useId in new long?[] { laterHit.SourceCardUseResolutionId!.Value + 100, null })
        {
            var unrelatedFact = actualDamage with
            { Payload = ((DamageAppliedEvent)actualDamage.Payload) with { SourceCardUseResolutionId = useId } };
            var unrelated = BattleCueProjector.Project([unrelatedFact], view).Single();
            cues.Clear(); cues.Add(response); cues.Add(unrelated);
            host.Clock.Advance(.01);
            Program.Assert(SampleDuel(host, response.Sequence, 10) is { Flying: true, Pending: true, Hit: true },
                "An unrelated or legacy hit with the same participants incorrectly waited for a different Duel use.");
        }
        cues.Clear();
    }

    private static (bool Flying, bool Pending, bool Hit) SampleDuel(
        NativeResponseStatusChecks.NativeHost host, long responseSequence, int milliseconds)
    {
        host.Root.Width = 1120; host.Root.Height = 740;
        host.Window.SizeToContent = SizeToContent.WidthAndHeight;
        host.Layer.InvalidateVisual();
        Program.Render(host.Root, 1120, 740, null);
        var face = Program.Find<Border>(host.Root).Single(border => BattleFeedbackLayer.GetPlayAnchor(border) == responseSequence);
        var target = Program.Find<Button>(host.Root).Single(button => button.IsVisible && BattleFeedbackLayer.GetSeatAnchor(button) == 1);
        var targetBounds = target.TransformToVisual(host.Layer).TransformBounds(new Rect(target.RenderSize));
        var drawing = VisualTreeHelper.GetDrawing(host.Layer);
        var flying = Images(drawing).Any(image => ReferenceEquals(image.ImageSource, CardArt.Get(CardKind.Slash)));
        var pending = BattleFeedbackLayer.GetIsFlightPending(face) && ((FrameworkElement)face.Parent).Opacity < .001;
        var hit = HitOutlines(drawing, Matrix.Identity, 1).Any(bounds =>
            Math.Abs(bounds.X - targetBounds.X) < .01 && Math.Abs(bounds.Y - targetBounds.Y) < .01 &&
            Math.Abs(bounds.Width - targetBounds.Width) < .01 && Math.Abs(bounds.Height - targetBounds.Height) < .01);
        Console.WriteLine($"Native Duel at {milliseconds}ms: flying={flying}, pending={pending}, target1-hit={hit}, " +
            $"revision={Program.Engine(host.Model).Revision}, can-act={host.Model.EndTurnCommand.CanExecute(null)}.");
        return (flying, pending, hit);
    }

    private static IEnumerable<ImageDrawing> Images(Drawing? drawing)
    {
        if (drawing is ImageDrawing image) yield return image;
        if (drawing is DrawingGroup group)
            foreach (var child in group.Children) foreach (var nestedImage in Images(child)) yield return nestedImage;
    }

    private static IEnumerable<Rect> HitOutlines(Drawing? drawing, Matrix transform, double opacity)
    {
        if (opacity > .001 && drawing is GeometryDrawing
            { Geometry: RectangleGeometry rectangle, Pen.Brush: SolidColorBrush ink } && ink.Color == Color.FromRgb(255, 91, 62))
            yield return new MatrixTransform(transform).TransformBounds(rectangle.Rect);
        if (drawing is DrawingGroup group)
        {
            var nested = group.Transform?.Value ?? Matrix.Identity; nested.Append(transform);
            foreach (var child in group.Children)
                foreach (var bounds in HitOutlines(child, nested, opacity * group.Opacity)) yield return bounds;
        }
    }

    private static IReadOnlyList<CardSnapshot> Grain(PlayerSnapshot owner) =>
        owner.PublicPersistentPiles is { Count: > 0 } piles
            ? piles.Single(p => p.SourceSkillId == "ol:jiyun").Cards
            : owner.PublicPersistentPileSkillId == "ol:jiyun" ? owner.PublicPersistentPileCards ?? [] : [];

    private static GameEngine Create(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, DecisionKind.PlayCard);
        return game;
    }

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
            {
                if (prompt.Kind == kind) return;
                if (prompt.Kind == DecisionKind.ProgramTrigger && prompt.SkillPrompt?.SkillId == "ol:jiyun" &&
                    prompt.Choices.SingleOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") is { } skip)
                {
                    Accept(game, new AnswerPromptCommand(0, prompt.PromptId, skip.Id, game.Revision));
                    continue;
                }
                throw new InvalidOperationException($"Unexpected native original-grain UI prompt: {prompt.Kind}/{prompt.SkillPrompt?.SkillId}.");
            }
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException($"The actual original-grain UI fixture did not reach {kind}.");
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native original-grain UI command rejected."); }
    private static Button BoundButton(FrameworkElement root, ICommand command, object parameter) =>
        Program.Find<Button>(root).Single(button => ReferenceEquals(button.Command, command) && ReferenceEquals(button.CommandParameter, parameter));
    private static void Click(FrameworkElement root, ICommand command, object parameter) => Click(BoundButton(root, command, parameter));
    private static void Click(Button button)
    {
        Program.Assert(button.IsEnabled && button.Visibility == Visibility.Visible,
            "The actual original-grain control must be enabled and visible.");
        button.Command!.Execute(button.CommandParameter);
    }
    private static void Layout(FrameworkElement root)
    {
        root.Measure(new Size(1120, 780)); root.Arrange(new Rect(0, 0, 1120, 780)); root.UpdateLayout();
        root.Dispatcher.Invoke(() => { }, DispatcherPriority.DataBind); root.UpdateLayout();
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:native-public-grain-ui", Owner = "fixture:native-public-grain-owner",
            Driver = "fixture:native-public-grain-driver";
        public PackageManifest Manifest { get; } = new("fixture-native-public-grain-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:li-feng");
            var catalog = SkillProgramCatalog.Load($$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.RulesSchemaVersion}}}},"skills":[{"id":"fixture:native-public-grain-driver","revision":1,"activations":[
                {"id":"incoming-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving",
                "usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]}]}
                """, $$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.PresentationSchemaVersion}}}},"skills":{"fixture:native-public-grain-driver":{"name":"原生决斗","description":"令其他角色对自己使用决斗"}}}
                """);
            builder.AddSkill(new(Driver, "原生决斗", "原生决斗") { Program = catalog.Programs[Driver] });
            builder.AddGeneral(new(Owner, "原粮交互", "supporter", "ol:jiyun", "shu", 6, [Driver, "ol:shuliang"]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:native-public-grain-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "原生目标", "supporter", "standard:none", "wei", 6));
            builder.AddDeck(new("fixture:native-public-grain-deck", "真实三类开局", 0, 0, [])
            {
                PhysicalCards = [.. Enumerable.Range(0, 24).Select(i =>
                    new ContentDeckPhysicalCard("standard:slash", Suit.Spade, i % 13 + 1)),
                    new("standard:draw_two", Suit.Heart, 7), new("standard:crossbow", Suit.Club, 1)]
            });
            builder.AddMode(new(Mode, "原粮交互", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:native-public-grain-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
