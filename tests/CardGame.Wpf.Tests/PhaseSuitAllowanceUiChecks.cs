using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PhaseSuitAllowanceUiChecks
{
    public static void PaidAllowanceSurvivesSourceLossAndExpires(string output)
    {
        var (game, registry) = Start(false);
        Activate(game, "classic:chenglue", "alternate", [], []);
        using var payment = Restore(game, registry);
        for (var index = 0; index < 2; index++)
        {
            var choice = payment.SkillChoices.First(candidate => candidate.Cards.Count == 1);
            if (index == 0) ClickChoice(payment, choice, output, "261-phase-suit-private-payment.png");
            else payment.SelectSkillChoiceCommand.Execute(choice);
        }
        Reach(payment, prompt => prompt.Kind == DecisionKind.PlayCard);
        game = Program.Engine(payment);
        var suits = game.CreateSnapshot(0).Players[0].IssuedPlayPhaseSuitUseAllowances!.Single().Suits;
        var glyphs = suits.Select(Glyph).ToArray();
        using var issued = Restore(game, registry);
        AssertBadge(issued, glyphs);
        Render(issued, output, "262-phase-suit-issued-allowance.png", "距离/次数不限");

        Activate(Program.Engine(issued), "fixture:suit-ui-driver", "lose-source", [], []);
        Reach(Program.Engine(issued), prompt => prompt.Kind == DecisionKind.PlayCard);
        using var lost = Restore(Program.Engine(issued), registry);
        Program.Assert(Program.Engine(lost).CreateSnapshot(0).Players[0].Skills!.All(skill => skill.ContentId != "classic:chenglue"),
            "The source is actually removed, rather than only hidden by the UI.");
        AssertBadge(lost, glyphs);
        Render(lost, output, "263-phase-suit-after-source-loss.png", "距离/次数不限");
        var previousTurn = Program.Engine(lost).CreateSnapshot(0).TurnNumber;
        lost.EndTurnCommand.Execute(null);
        Reach(lost, prompt => prompt.Kind == DecisionKind.PlayCard &&
            Program.Engine(lost).CreateSnapshot(0).TurnNumber > previousTurn);
        Program.Assert(!lost.HumanSummary.Contains("距离/次数不限") &&
            lost.Seats.All(seat => !seat.DeferredPileText.Contains("距离/次数不限")),
            "The paid badge disappears on the next actual Play without retaining a stale source or phase.");
    }

    public static void MultipleCompletedCostsRestoreFaceOrder(string output)
    {
        var (game, registry) = Start(true);
        var ids = game.CreateSnapshot(0).Players[0].Hand.Take(2).Select(card => card.Id).ToArray();
        Activate(game, "fixture:suit-ui-driver", "multi", ids, [1]);
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:shicai");
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Accept(game, new AnswerPromptCommand(0, prompt.PromptId,
            prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate").Id, game.Revision));
        Reach(game, pending => pending.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "alternating-suit-top"));
        using var model = Restore(game, registry);
        Program.Assert(model.PublicRevealedCards.Select(card => card.Id).ToHashSet().SetEquals(ids),
            "Both completed physical costs are public selectable faces after restoring the native prompt.");
        ClickFace(model, ids[1], output, "264-completed-cost-public-order.png");
        using var resumed = Restore(Program.Engine(model), registry);
        Program.Assert(resumed.PublicRevealedCards.Single().Id == ids[0] &&
            resumed.SelectRevealedCardCommand.CanExecute(resumed.PublicRevealedCards.Single()),
            "Restoring after the first actual click offers only the unpaid remaining entity.");
        ClickFace(resumed, ids[0], output, "265-completed-cost-remaining-face.png");
        Reach(resumed, pending => pending.Kind == DecisionKind.PlayCard);
        var finished = Program.Engine(resumed);
        var top = finished.CreateCardZoneDiagnostics().Where(card => card.Location == CardLocation.DrawPile)
            .OrderByDescending(card => card.ZoneIndex).Take(2).Select(card => card.CardId).ToArray();
        Program.Assert(top.SequenceEqual([ids[1], ids[0]]) &&
            finished.CardMovements.Count(movement => movement.Reason.Value == "skill-program.completed-top.place") == 2,
            "The real face controls preserve user order and move each completed cost once.");
    }

    private static void AssertBadge(MainViewModel model, string[] glyphs)
    {
        var seat = model.Seats.Single(candidate => candidate.Seat == 0);
        Program.Assert(model.HumanSummary.Contains("距离/次数不限") &&
            seat.DeferredPileText.Contains("距离/次数不限") && glyphs.All(glyph => model.HumanSummary.Contains(glyph)),
            "The human summary and generic seat badge display the actual paid suit policy.");
        Program.Assert(seat.DeferredPileTooltip.Contains("其他使用条件仍适用") &&
            seat.DeferredPileTooltip.Contains("结束出牌阶段后解除"),
            "The public tooltip states the scope and actual expiry of the allowance.");
    }

    private static string Glyph(Suit suit) => suit switch
    {
        Suit.Spade => "♠", Suit.Heart => "♥", Suit.Club => "♣", Suit.Diamond => "♦", _ => string.Empty
    };

    private static (GameEngine, ContentRegistry) Start(bool completed)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(completed));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-suit-ui",
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, prompt => prompt.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:suit-ui-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        return (game, registry);
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true, contentRegistry: registry)
            { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void Activate(GameEngine game, string skill, string activation, int[] cards, int[] targets) =>
        Accept(game, new UseProgramSkillCommand(0, skill, activation, cards, targets, game.Revision, game.PendingDecision!.PromptId));

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted actual UI command.");
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 200; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed suit UI fixture did not reach its actual boundary.");
    }

    private static void Reach(MainViewModel model, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 200; step++)
        {
            if (Program.Engine(model).CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Program.Assert(model.CanStepAi, "The restored suit parent must remain resumable.");
            model.StepAiCommand.Execute(null);
        }
        throw new InvalidOperationException("The restored suit UI did not reach its actual boundary.");
    }

    private static void ClickChoice(MainViewModel model, PromptChoice choice, string output, string file) =>
        Render(model, output, file, null, root =>
        {
            var button = Program.Find<Button>(root).First(candidate => candidate.Command == model.SelectSkillChoiceCommand &&
                candidate.CommandParameter is PromptChoice parameter && parameter.Id == choice.Id && candidate.ActualWidth > 0);
            Program.Assert(button.IsEnabled, "A genuine private cost choice renders as an enabled shared control.");
            button.Command!.Execute(button.CommandParameter);
        });

    private static void ClickFace(MainViewModel model, int cardId, string output, string file) =>
        Render(model, output, file, null, root =>
        {
            var panel = Program.Find<ItemsControl>(root).Single(control => control.Name == "PublicRevealedCards");
            var button = Program.Find<Button>(panel).Single(candidate => candidate.CommandParameter is CardViewModel card && card.Id == cardId);
            Program.Assert(button.IsEnabled && button.ActualWidth > 0, "The exact public cost has an enabled native face control.");
            button.Command!.Execute(button.CommandParameter);
        });

    private static void Render(MainViewModel model, string output, string file, string? text, Action<FrameworkElement>? action = null)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, file));
        if (text is not null) Program.Assert(Program.Find<TextBlock>(root).Any(block => block.Text.Contains(text) &&
            block.Visibility == Visibility.Visible && block.ActualWidth > 0), "The native public policy text must render.");
        action?.Invoke(root);
        window.Content = null;
        window.Close();
    }

    private sealed class Fixture(bool completed) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-suit-ui", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:suit-ui-driver","revision":1,
                "viewAs":[{"id":"multi","inputKinds":[],"inputSuits":[],"inputCount":2,"outputKind":"slash","forPlay":true,"forResponse":false}],
                "activations":[{"id":"lose-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                "effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:chenglue"],"sourceBind":"standard:none"}]},
                {"id":"multi","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                "effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"multi","outputKind":"slash"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:suit-ui-driver":{"name":"真实动作","description":"固定 UI 行为"}}}""");
            builder.AddSkill(new("fixture:suit-ui-driver", "真实动作", "固定 UI 行为") { Program = catalog.Programs["fixture:suit-ui-driver"] });
            builder.AddGeneral(new("fixture:suit-ui-owner", "授权者", "supporter", "classic:chenglue", "qun", 20,
                completed ? ["classic:shicai", "classic:cunmu", "fixture:suit-ui-driver"] : ["classic:cunmu", "fixture:suit-ui-driver"]));
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:suit-ui-target-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "目标", "supporter", "standard:none", "wu", 20));
            builder.AddDeck(new("fixture:suit-ui-deck", "固定真实实体", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 100).Select(index => new ContentDeckPhysicalCard("standard:crossbow",
                    index % 2 == 0 ? Suit.Spade : Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-suit-ui", "花色授权界面", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:suit-ui-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:suit-ui-owner", .. others]));
        }
    }
}
