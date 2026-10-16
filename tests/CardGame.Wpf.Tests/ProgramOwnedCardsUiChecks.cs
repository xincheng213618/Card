using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ProgramOwnedCardsUiChecks
{
    private const string SkillId = "fixture:ui-owned-cards";

    public static void PrivateSetUsesSharedChoiceSurface(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new OwnedCardsPackage());
        var game = ReachSelection(registry);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var firstPrompt = game.PendingDecision!;
        var firstCardId = firstPrompt.Choices[0].Cards.Single();
        var beforeMoves = game.CardMovements.Count;
        Program.Assert(model.IsSkillSelectionPending && model.SkillChoices.Count == firstPrompt.ValidCardIds.Count &&
                       model.SkillChoices.All(choice => choice.Description.StartsWith("选择手牌【", StringComparison.Ordinal)) &&
                       model.CurrentDecisionContext is { Title: "区域牌集合 · 选择区域牌", TargetSeat: 0 } &&
                       model.CurrentGuideTitle == firstPrompt.SkillPrompt!.Title &&
                       game.CreateSnapshot(1).PendingDecision is null,
            "A private owned-card draft must use the shared labeled choice surface without leaking to another viewer.");

        var window = new MainWindow(model);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "231-program-owned-card-set-first.png"));
        model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == firstCardId));
        Program.Assert(!model.CanConfirmSelected && game.PendingDecision!.PromptId == firstPrompt.PromptId &&
                       game.CardMovements.Count == beforeMoves,
            "Choosing from the real hand must not submit or consume the first private draft card.");
        model.ConfirmSelectedCommand.Execute(null);
        Program.Assert(game.PendingDecision!.PromptId == firstPrompt.PromptId &&
                       game.ResolutionStack.OfType<ProgramSkillFrame>().Single().OwnedCardSelection is
                           { SelectedCardIds.Count: 0 } && model.HasSelection,
            "An incomplete local draft must not answer even the first private engine choice.");
        var secondCardId = model.SkillChoices.First(choice => choice.Cards[0] != firstCardId).Cards.Single();
        model.SelectCardCommand.Execute(model.Hand.Single(card => card.Id == secondCardId));
        Program.Assert(model.Hand.Count(card => card.IsSelected) == 2 && model.CanConfirmSelected &&
                       !model.HasCenterChoices && game.CardMovements.Count == beforeMoves,
            "Both cards must remain selected together with one enabled confirmation and no central candidates.");
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "232-program-owned-card-set-second.png"));
        model.ConfirmSelectedCommand.Execute(null);
        var moved = game.CardMovements.Skip(beforeMoves)
            .Where(move => move.Reason.Value == $"skill-program.{SkillId}.MoveBoundCards").ToArray();
        Program.Assert(moved.Length == 2 && moved.Select(move => move.CardId).ToHashSet().SetEquals([firstCardId, secondCardId]) &&
                       moved.All(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile) &&
                       game.Events.Count(item => item.Payload is ProgramSkillResolvedEvent
                           { SkillId: SkillId, Completed: true }) == 1 &&
                       !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(),
            "One UI confirmation must commit the exact set once through ordinary card movement.");
        window.Content = null;
        window.Close();
    }

    public static void XiantuDockAndBatch(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new XiantuPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = XiantuPackage.ModeId,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "boundary:zhang-song", game.Revision, game.PendingDecision!.PromptId)));
        Reach(DecisionKind.PlayCard);
        var equipment = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
        Accept(game.Submit(new PlayCardCommand(0, equipment.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId)));
        Reach(DecisionKind.PlayCard);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        Reach(DecisionKind.ProgramTrigger, "boundary:xiantu");
        var store = new MemorySaveStore();
        Save();
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Program.Render(root, 1120, 740, Path.Combine(output, "xiantu-activation-dock.png"));
            var dock = (ItemsControl)window.FindName("DockSkillChoices");
            Program.Assert(model.HasDockSkillChoices && model.DockSkillChoices.Count == 3 && !model.HasCenterChoices &&
                dock.Visibility == Visibility.Visible && ((Border)window.FindName("DecisionPanel")).Visibility == Visibility.Collapsed,
                "The real boundary Xiantu offer must put both activation modes and declining below the battlefield.");
            var buttons = Program.Find<Button>(dock).ToArray();
            Program.Assert(buttons.Length == 3 && buttons.All(button => button.IsEnabled &&
                button.TranslatePoint(new Point(), root).Y > root.ActualHeight / 2),
                "All three real Xiantu choices must be enabled in the bottom action dock.");
            var skip = model.DockSkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
            var revision = game.Revision;
            model.SelectSkillChoiceCommand.Execute(skip);
            Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand skipped &&
                skipped.Choice == skip.Id, "The bottom decline must answer its exact published prompt once.");
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            var before = game.Revision;
            model.SelectSkillChoiceCommand.Execute(skip);
            Program.Assert(game.Revision == before, "A retired dock choice must not answer a reloaded Xiantu offer.");
            var one = model.DockSkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
                choice.Parameters.GetValueOrDefault("binding-id") == "gift-one-card");
            model.SelectSkillChoiceCommand.Execute(one);
            Program.Assert(model.IsOwnedCardSelectionPending && model.ChoiceSnapshot.PendingDecision!.RequiredCardCount == 1,
                "The one-card Xiantu mode must retain its one-card requirement.");
            model.SelectCardCommand.Execute(model.Hand.First());
            model.ConfirmSelectedCommand.Execute(null);
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            var two = model.DockSkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("binding-id") == "gift-two-cards");
            model.SelectSkillChoiceCommand.Execute(two);
            Program.Assert(model.IsOwnedCardSelectionPending && model.ChoiceSnapshot.PendingDecision!.RequiredCardCount == 2,
                "Choosing two-card Xiantu must expose the private remaining count.");
            Save();
            var first = model.Hand.First();
            var second = model.Hand.Skip(1).First();
            var boundary = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            model.SelectCardCommand.Execute(first);
            Program.Assert(!model.CanConfirmSelected && first.IsSelected, "One Xiantu card cannot confirm a two-card gift.");
            model.SelectCardCommand.Execute(second);
            model.SelectedPlaybackSpeed = CardGame.Wpf.Presentation.PlaybackSpeed.Fast;
            model.SelectCardCommand.Execute(model.Hand.Skip(2).First());
            Program.Assert(model.CanConfirmSelected && model.Hand.Count(card => card.IsSelected) == 2 &&
                GameCheckpointJson.Serialize(game.CreateCheckpoint()) == boundary,
                "Xiantu must keep two local selections without accepting a third or changing the command boundary.");
            model.ClearSelectionCommand.Execute(null);
            Program.Assert(!model.HasSelection && !model.CanConfirmSelected && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == boundary,
                "Cancel must clear the entire unpaid Xiantu draft.");
            model.SelectCardCommand.Execute(first);
            model.SelectCardCommand.Execute(second);
            Program.Render(root, 1120, 740, Path.Combine(output, "xiantu-two-cards-selected.png"));
            var recipient = game.State.CurrentSeat;
            var moves = game.CardMovements.Count;
            model.ConfirmSelectedCommand.Execute(null);
            var given = game.CardMovements.Skip(moves).Where(move => move.Reason.Value == "skill-program.boundary:xiantu.MoveBoundCards").ToArray();
            Program.Assert(given.Length == 2 && given.Select(move => move.CardId).ToHashSet().SetEquals([first.Id, second.Id]) &&
                given.All(move => move.To == CardLocation.Hand(recipient)) && !model.HasSelection,
                "One Xiantu confirmation must give both selected physical cards to the current phase owner.");
            var after = game.Revision;
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.Revision == after, "Repeated Xiantu confirmation must not pay again.");
            var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Program.Assert(GameCheckpointJson.Serialize(replay.CreateCheckpoint()) == GameCheckpointJson.Serialize(game.CreateCheckpoint()),
                "The combined UI gesture must retain exact command replay.");

            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            model.SelectCardCommand.Execute(first);
            Program.Assert(!model.HasSelection, "Reload must retire prior hand controls and their local selections.");
            var slot = model.HumanEquipmentSlots.Single(item => item.Card?.Id == equipment.CardId);
            root.UpdateLayout();
            var equipmentButton = Program.Find<Button>((ItemsControl)window.FindName("HumanEquipmentSlots"))
                .Single(button => ReferenceEquals(button.CommandParameter, slot) && button.Command == model.SelectOwnedCardEquipmentCommand);
            Program.Assert(equipmentButton.IsEnabled, "The real equipped-card control must route to the owned-card draft.");
            model.SelectOwnedCardEquipmentCommand.Execute(slot);
            model.SelectCardCommand.Execute(model.Hand.First());
            Program.Assert(model.CanConfirmSelected && model.HumanEquipmentSlots.Single(item => item.Card?.Id == equipment.CardId).IsSelected,
                "Xiantu must also accept a mixed hand and equipped-card selection.");
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(!game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == equipment.CardId),
                "The equipped card must be given only after confirming the complete Xiantu selection.");
        }
        finally { window.Content = null; window.Close(); }

        void Save() => store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        void Reach(DecisionKind kind, string? skill = null)
        {
            for (var step = 0; step < 128; step++)
            {
                if (game.PendingDecision is { } prompt)
                {
                    if (prompt.Kind == kind && (skill is null || prompt.SkillPrompt?.SkillId == skill)) return;
                    var decline = prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
                    Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, decline.Id, game.Revision)));
                }
                else Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            }
            throw new InvalidOperationException("The fixed Xiantu UI fixture did not reach its boundary.");
        }
    }

    private sealed class XiantuPackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-xiantu-ui";
        public PackageManifest Manifest { get; } = new("fixture:xiantu-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new($"fixture:xiantu-target-{index}", "固定目标", "supporter", "standard:none", "shu", 4));
            builder.AddDeck(new("fixture:xiantu-ui-deck", "固定献图牌库", 4, 2,
                [new ContentDeckCardCount("standard:crossbow", 80)]));
            builder.AddMode(new(ModeId, "献图界面", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:xiantu-ui-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["boundary:zhang-song", "fixture:xiantu-target-1", "fixture:xiantu-target-2", "fixture:xiantu-target-3"]));
        }
    }

    private static GameEngine ReachSelection(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = OwnedCardsPackage.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:ui-owned-cards-owner", game.Revision,
            game.PendingDecision!.PromptId)));
        for (var step = 0; step < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Program.Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "The owned-card UI fixture did not reach Play.");
        Accept(game.Submit(new UseProgramSkillCommand(0, SkillId, "select", [], [], game.Revision,
            game.PendingDecision!.PromptId)));
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true, PlayerSeat: 0 },
            "The owned-card UI fixture did not reach the private set draft.");
        return game;
    }

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted,
        result.Error?.Message ?? "Expected accepted owned-card UI command.");

    private sealed class OwnedCardsPackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-ui-owned-cards-4";
        public PackageManifest Manifest { get; } = new("fixture-ui-owned-cards", new Version(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load("""
                {"schemaVersion":62,"skills":[{"id":"fixture:ui-owned-cards","revision":1,
                "minimumRulesVersion": 171,"activations":[{"id":"select","minCards":0,"maxCards":0,
                "minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":1,
                "condition":{"kind":"always"},"effects":[
                {"op":"selectOwnedCards","target":"owner","amount":2,"zones":["hand"],"resultBind":"chosen"},
                {"op":"moveBoundCards","target":"owner","sourceBind":"chosen","destination":"discardPile"}
                ]}]}]}
                """, """
                {"schemaVersion":3,"skills":{"fixture:ui-owned-cards":{"name":"区域牌集合",
                "description":"私下选择两张自己的手牌，再统一移动。"}}}
                """);
            builder.AddSkill(new ContentSkillDefinition(SkillId, "区域牌集合", "私下选择两张自己的手牌，再统一移动。")
            { Program = catalog.Programs[SkillId], ProgramPresentation = catalog.Presentations[SkillId] });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:ui-owned-cards-owner", "牌主", "supporter",
                SkillId, "wu", BaseHp: 4));
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:ui-owned-cards-{index}", $"目标{index}",
                    "supporter", "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:ui-owned-cards-deck", "区域牌界面测试", 4, 0,
                [new ContentDeckCardCount("standard:slash", 64)]));
            builder.AddMode(new ContentModeDefinition(ModeId, "区域牌集合界面测试", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2
                },
                "fixture:ui-owned-cards-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:ui-owned-cards-owner", "fixture:ui-owned-cards-1",
                    "fixture:ui-owned-cards-2", "fixture:ui-owned-cards-3"]));
        }
    }
}
