using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PublicPileInteractionUiChecks
{
    public static void StoreSetsAndInspectPortraitPiles(string output)
    {
        foreach (var skill in new[] { "classic:kongsheng", "ol:yinbing" })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(skill));
            var initial = Start(registry, skill);
            using var model = Restore(initial, registry);
            var game = Program.Engine(model);
            var prompt = model.ChoiceSnapshot.PendingDecision!;
            var candidates = prompt.Choices.Where(choice => choice.Cards.Count == 1).SelectMany(choice => choice.Cards).ToArray();
            var equipment = model.HumanEquipmentSlots.FirstOrDefault(slot => slot.Card is not null);
            var ids = skill == "classic:kongsheng" ? candidates
                : new[] { candidates.First(id => model.Hand.Any(card => card.Id == id)), equipment!.Card!.Id };
            var revision = game.Revision;
            var moves = game.CardMovements.Count;
            Program.Assert(candidates.Length >= 3 && model.IsOwnedCardSelectionPending && model.CanConfirmSelected &&
                prompt.MinimumCardCount == 0 && prompt.RequiredCardCount == candidates.Length &&
                !model.HasCenterChoices && !model.HasTargetCardRegions && model.UngroupedCenterSkillChoices.Count == 0,
                "Both real public-pile stores must offer a zero-to-all local hand/equipment draft without central card buttons.");

            Toggle(ids[0]); Toggle(ids[1]); Toggle(ids[0]);
            Program.Assert(model.Hand.Count(card => card.IsSelected) + model.HumanEquipmentSlots.Count(slot => slot.IsSelected) == 1,
                "Clicking a selected card again must remove only that card from the unpaid draft.");
            model.ClearSelectionCommand.Execute(null);
            Program.Assert(!model.HasSelection && game.Revision == revision && game.CardMovements.Count == moves,
                "Clearing the selection must leave the real prompt and all cards untouched.");
            foreach (var id in ids) Toggle(id);
            Program.Assert(model.CanConfirmSelected && game.Revision == revision && game.CardMovements.Count == moves &&
                model.Hand.Count(card => card.IsSelected) + model.HumanEquipmentSlots.Count(slot => slot.IsSelected) == ids.Length,
                "All chosen hand and equipment cards must stay selected until one confirmation.");

            var window = new MainWindow(model);
            try
            {
                var root = (FrameworkElement)window.Content;
                var label = skill.Replace(':', '-');
                Program.Render(root, 1120, 740, Path.Combine(output, $"pile-{label}-selection.png"));
                model.ConfirmSelectedCommand.Execute(null);
                var paid = game.CardMovements.Skip(moves).Where(move => ids.Contains(move.CardId) &&
                    move.To.Zone == CardZoneKind.PublicPersistentPile).ToArray();
                Program.Assert(paid.Length == ids.Length && paid.Select(move => move.CardId).ToHashSet().SetEquals(ids) &&
                    model.ChoiceSnapshot.Players[0].PublicPersistentPileCount == ids.Length && !model.IsOwnedCardSelectionPending,
                    "One confirmation must finish even an all-card draft and store the exact set once.");
                if (skill == "classic:kongsheng")
                {
                    Reach(game, p => p.SkillPrompt?.SkillId == "classic:liangyin");
                    Refresh();
                    Program.Render(root, 1120, 740, Path.Combine(output, "pile-liangyin-dock.png"));
                    Program.Assert(model.HasDockSkillChoices && model.DockSkillChoices.Count == 2 &&
                        model.UngroupedCenterSkillChoices.Count == 0 && !model.HasCenterChoices &&
                        !model.HasPinnedPublicModuleChoices &&
                        ((FrameworkElement)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed,
                        "Liangyin with a stored pile must show its activation choices only in the lower dock.");
                    model.SelectSkillChoiceCommand.Execute(model.DockSkillChoices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "skip"));
                    Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
                    Refresh();
                    Program.Assert(!model.HasCenterChoices && model.Hand.All(card => !ids.Contains(card.Id)),
                        "Stored Kongsheng cards must remain on the general without occupying the center during Play.");
                }
                Program.Render(root, 1120, 740, Path.Combine(output, $"pile-{label}-table.png"));
                var status = model.HumanPlayer!.StatusBadges.Single(item => item.CanInspectPile);
                var badge = Program.Find<Button>((ItemsControl)window.FindName("HumanStatusBadges"))
                    .Single(button => ReferenceEquals(button.CommandParameter, status));
                Program.Assert(badge.Visibility == Visibility.Visible && badge.IsEnabled && badge.ActualWidth > 0,
                    "The general's existing pile badge must provide a visible inspection button.");
                revision = game.Revision;
                model.OpenPublicPileCommand.Execute(status with { });
                Program.Assert(!model.IsReserveDialogOpen, "A copied stale badge must not open a pile viewer.");
                model.OpenPublicPileCommand.Execute(status);
                Program.Assert(model.IsReserveDialogOpen && !model.IsReserveExchange &&
                    model.ReserveDialogStars.Select(card => card.Card.Id).ToHashSet().SetEquals(ids) &&
                    model.ReserveDialogBacks.Count == 0 && model.ReserveDialogHint.Contains("不计入手牌") &&
                    model.ReserveDialogStars.All(card => !model.MoveReserveCardCommand.CanExecute(card)) && game.Revision == revision,
                    "Clicking the pile badge must inspect its public faces without moving cards or entering an exchange.");
                Program.Render(root, 1120, 740, Path.Combine(output, $"pile-{label}-viewer.png"));
                model.CloseReserveDialogCommand.Execute(null);
            }
            finally { window.Content = null; window.Close(); }

            using var empty = Restore(initial, registry);
            var emptyGame = Program.Engine(empty);
            moves = emptyGame.CardMovements.Count;
            empty.ConfirmSelectedCommand.Execute(null);
            Program.Assert(!empty.IsOwnedCardSelectionPending && emptyGame.CardMovements.Count == moves &&
                empty.ChoiceSnapshot.Players[0].PublicPersistentPileCount == 0,
                "Confirming an empty optional set must finish without storing or moving a card.");

            void Toggle(int id)
            {
                if (model.Hand.FirstOrDefault(card => card.Id == id) is { } hand) model.SelectCardCommand.Execute(hand);
                else model.SelectEquipmentMaterialCommand.Execute(model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == id));
            }
            void Refresh() => typeof(MainViewModel).GetMethod("Refresh",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(model, [game.CreateSnapshot(0)]);
        }
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static GameEngine Start(ContentRegistry registry, string skill)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        if (skill == "ol:yinbing")
        {
            Reach(game, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
            var equipment = game.CreateSnapshot(0).Players[0].Hand.First(card => EquipmentCatalog.IsEquipment(card.Kind));
            Accept(game, new PlayCardCommand(0, equipment.Id, [], game.Revision, game.PendingDecision!.PromptId));
        }
        Reach(game, p => p.SkillPrompt?.SkillId == skill &&
            p.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        return game;
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> stop)
    {
        for (var step = 0; step < 128; step++)
        {
            var prompt = game.CreateSnapshot(0).PendingDecision;
            if (prompt is not null && stop(prompt)) return;
            if (prompt is { Kind: DecisionKind.PlayCard })
                Accept(game, new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
            else if (prompt is not null)
                Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The public-pile fixture did not reach the expected prompt.");
    }

    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Public-pile command rejected.");
    }

    private sealed class Fixture(string skill) : IGameContentPackage
    {
        internal const string Mode = "identity:pile-ui", Owner = "fixture:pile-ui";
        public PackageManifest Manifest { get; } = new("fixture:pile-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, skill == "classic:kongsheng" ? "classic:zhou-fei" : "ol:zu-mao");
            builder.AddGeneral(new(Owner, "牌堆测试", "supporter", skill, "wu", 20,
                skill == "classic:kongsheng" ? ["classic:liangyin"] : []));
            for (var seat = 1; seat < 4; seat++)
                builder.AddGeneral(new($"{Owner}-{seat}", "对手", "supporter", "standard:none", "wei", 20));
            builder.AddDeck(new("fixture:pile-ui-deck", "牌堆交互", 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(index => new ContentDeckPhysicalCard(
                    index % 3 == 0 ? "standard:crossbow" : "standard:draw_two", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "牌堆交互", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:pile-ui-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, $"{Owner}-1", $"{Owner}-2", $"{Owner}-3"]));
        }
    }
}
