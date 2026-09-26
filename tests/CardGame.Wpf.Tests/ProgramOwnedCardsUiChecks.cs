using System.IO;
using System.Windows;
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
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(choice => choice.Cards.Contains(firstCardId)));
        var secondPrompt = game.PendingDecision!;
        Program.Assert(secondPrompt.SkillPrompt?.SkillId == SkillId &&
                       secondPrompt.Choices.All(choice => !choice.Cards.Contains(firstCardId)) &&
                       game.CardMovements.Count == beforeMoves &&
                       game.ResolutionStack.OfType<ProgramSkillFrame>().Single().OwnedCardSelection is
                           { SelectedCardIds.Count: 1 },
            "The first shared UI choice must update only the private draft and must not move a card.");
        Program.Render((FrameworkElement)window.Content, 1120, 740,
            Path.Combine(output, "232-program-owned-card-set-second.png"));

        var secondCardId = model.SkillChoices[0].Cards.Single();
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices[0]);
        var moved = game.CardMovements.Skip(beforeMoves)
            .Where(move => move.Reason.Value == $"skill-program.{SkillId}.MoveBoundCards").ToArray();
        Program.Assert(moved.Length == 2 && moved.Select(move => move.CardId).ToHashSet().SetEquals([firstCardId, secondCardId]) &&
                       moved.All(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile) &&
                       game.Events.Count(item => item.Payload is ProgramSkillResolvedEvent
                           { SkillId: SkillId, Completed: true }) == 1 &&
                       !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(),
            "The second shared UI choice must commit the exact set once through ordinary card movement.");
        window.Content = null;
        window.Close();
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
                {"schemaVersion":61,"skills":[{"id":"fixture:ui-owned-cards","revision":1,
                "minimumRulesVersion":170,"activations":[{"id":"select","minCards":0,"maxCards":0,
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
