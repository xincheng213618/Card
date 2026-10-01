using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PublicStateUiChecks
{
    public static void PublicRuleStatesAndComparedHandsRestore(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new RuleStateFixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 1, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "identity:classic-ui-rule-state-4", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:ui-rule-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:caishi");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "limit");
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var source = game.CreateSnapshot(0).Players[0].Hand.First();
        Accept(game, new UseProgramSkillCommand(0, "classic:zhongjian", "compare", [source.Id], [1],
            game.Revision, game.PendingDecision!.PromptId));
        var draft = game.ResolutionStack.OfType<ProgramSkillFrame>().Last().HandComparisonDraft!;
        for (var count = 0; count < draft.Required; count++)
            Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "reveal");
        var actualCards = game.CreateSnapshot(0).PublicRevealedCards;
        Program.Assert(actualCards.Count == draft.Required + 1 && Enumerable.Range(0, 4).All(seat =>
            game.CreateSnapshot(seat).PublicRevealedCards.Select(card => card.Id)
                .SequenceEqual(actualCards.Select(card => card.Id))),
            "Only actual shown hand entities become public while comparison reward is pending.");
        using (var model = RestoreModel(game, registry))
        {
            var quota = model.HumanSkillCards.Single(skill => skill.ContentId == "classic:zhongjian");
            var limit = model.HumanSkillCards.Single(skill => skill.ContentId == "classic:caishi");
            Program.Assert(quota.StateText.Contains("本阶段已发动 1/2 次") &&
                           limit.StateText.Contains("手牌上限调整 +1") &&
                           model.PublicRevealedCards.Select(card => card.Id).SequenceEqual(actualCards.Select(card => card.Id)) &&
                           model.PublicRevealedCards.All(card => !model.SelectRevealedCardCommand.CanExecute(card)),
                "Restored public quota, persistent hand limit and shown faces use the generic UI state contract.");
            Program.Assert(model.HasPinnedPublicModuleChoices,
                "Small non-card reward choices remain above the shown hand faces.");
            RenderStateWindow(model, output, "240-public-rule-state-and-compared-hands.png", assertPinnedChoices: true);
            model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(choice =>
                choice.Parameters.GetValueOrDefault("branch") == "draw"));
            Program.Assert(model.PublicRevealedCards.Count == 0,
                "The actual restored reward button submits its current choice and finishes the public comparison.");
        }
        Answer(game, choice => choice.Parameters.GetValueOrDefault("branch") == "draw");
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Accept(game, new UseProgramSkillCommand(0, "fixture:ui-rule-driver", "hurt", [], [],
            game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.PlayerSeat == 0 && prompt.SkillPrompt?.SkillId == "classic:caishi");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "recover");
        Reach(game, prompt => prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.PlayCard);
        using var recoveredModel = RestoreModel(game, registry);
        var recoveredState = recoveredModel.HumanSkillCards.Single(skill => skill.ContentId == "classic:caishi");
        Program.Assert(recoveredState.StateText.Contains("手牌上限调整 +1") &&
                       recoveredState.StateText.Contains("本回合不能对自己用牌") &&
                       recoveredState.Tooltip.Contains(recoveredState.StateText),
            "The actual new-turn self prohibition and previous persistent contribution survive restore and appear together.");
        RenderStateWindow(recoveredModel, output, "241-public-self-target-prohibition.png");
    }

    private static MainViewModel RestoreModel(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void RenderStateWindow(MainViewModel model, string output, string filename, bool assertPinnedChoices = false)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        Program.Render((FrameworkElement)window.Content, 1120, 740, Path.Combine(output, filename));
        if (assertPinnedChoices)
        {
            var panel = Program.Find<ItemsControl>((FrameworkElement)window.Content)
                .Single(control => control.Name == "PinnedPublicModuleChoices");
            Program.Assert(panel.Visibility == Visibility.Visible && Program.Find<Button>(panel).Count(button =>
                button.Command == model.SelectSkillChoiceCommand && button.IsEnabled &&
                button.ActualWidth > 0 && button.ActualHeight > 0) == model.SkillChoices.Count,
                "Every actual reward choice has a visible enabled button above the public card viewport.");
        }
        window.Content = null;
        window.Close();
    }

    public static void AlternatingStateAndPersistentPileRestore(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 1, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "identity:classic-ui-public-state-4", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 2 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:ui-state-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:fumian");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "targets");
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var action = game.GetHumanLegalActions().First(candidate => candidate.Kind == LegalActionKind.DrawTwo && candidate.ProgramActivationId is null);
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:bizhuan");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        var owner = game.CreateSnapshot(0).Players[0];
        var pileCards = owner.PublicPersistentPileCards ?? throw new InvalidOperationException("Expected the real public pile.");
        Program.Assert(owner.PublicPersistentPileCount == 1 && pileCards.Count == 1 &&
                       Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat).Players[0].PublicPersistentPileCards!
                           .Select(card => card.Id).SequenceEqual(pileCards.Select(card => card.Id))),
            "The real completed black-Spade use must create a persistent public owned pile visible to every observer.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var skill = model.HumanSkillCards.Single(item => item.ContentId == "classic:fumian");
        var pile = model.DeferredPublicPiles.Single(item => item.OwnerSeat == 0);
        var actual = pileCards.Single();
        var face = pile.Cards.Single();
        Program.Assert(skill.StateText.Contains("下次选择：摸牌 +2／额外目标 +1") && skill.Tooltip.Contains(skill.StateText) &&
                       pile.Title.Contains("状态测试者 · 书 1张") && face.Id == actual.Id && face.Kind == actual.Kind &&
                       face.Rank == actual.RankText && face.SuitGlyph == "♠" && face.PublicCardLabel == "公开牌" &&
                       !model.SelectRevealedCardCommand.CanExecute(face) && model.HasCenterChoices &&
                       model.Seats.Single(seat => seat.Seat == 0).DeferredPileText.Contains("书 ×1") &&
                       model.HumanSummary.Contains("书 1"),
            "Actual restored alternating values and persistent pile ownership/faces must use shared skill state, seat badge and summary.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "239-public-pile-and-choice-cycle.png"));
        var panel = Program.Find<ItemsControl>(root).Single(control => control.Name == "DeferredPublicPiles");
        var button = Program.Find<Button>(panel).Single(button => button.Command == model.SelectRevealedCardCommand);
        Program.Assert(!button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0,
            "The public persistent pile must render through the shared read-only physical card surface.");
        window.Content = null;
        window.Close();
    }

    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision));
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The deterministic UI state fixture did not reach its actual rule boundary.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected accepted fixture command.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-public-state", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new("fixture:ui-state-owner", "状态测试者", "supporter", "classic:fumian", "wei", 9,
                AdditionalSkillIds: ["classic:bizhuan", "classic:tongbo"]));
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:ui-state-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "旁观者", "supporter", "standard:none", "wu", 9));
            builder.AddDeck(new("fixture:ui-state-deck", "状态测试实体牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard("standard:draw_two", Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-ui-public-state-4", "公开状态界面测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-state-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ui-state-owner", .. others]));
        }
    }

    private sealed class RuleStateFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-rule-state", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-rule-driver","revision":1,
                "activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                "targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:ui-rule-driver":{"name":"体力准备","description":"界面检查"}}}""");
            builder.AddSkill(new("fixture:ui-rule-driver", "体力准备", "界面检查")
            { Program = catalog.Programs["fixture:ui-rule-driver"] });
            builder.AddGeneral(new("fixture:ui-rule-owner", "辛宪英", "xin_xianying", "classic:zhongjian", "wei", 3,
                AdditionalSkillIds: ["classic:caishi", "fixture:ui-rule-driver"], Gender: GeneralGender.Female));
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:ui-rule-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "旁观者", "supporter", "standard:none", "wu", 3));
            builder.AddDeck(new("fixture:ui-rule-deck", "状态检查实体牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Spade, 5)).ToArray() });
            builder.AddMode(new("identity:classic-ui-rule-state-4", "公开状态界面检查", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-rule-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ui-rule-owner", .. others]));
        }
    }
}
