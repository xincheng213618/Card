using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class DeferredHandAlignmentUiChecks
{
    public static void NativeRecipientDraftRestores(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-haozhao-fixture", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(game, new UseProgramSkillCommand(0, "fixture:driver", "grow-owner", [], [],
            game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.PlayerSeat == 0 && prompt.SkillPrompt?.SkillId == "classic:zhengu" &&
            game.ResolutionStack.FirstOrDefault() is DeferredTurnEndFrame);
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        var draft = frame.OwnedCardSelection!;
        Program.Assert(frame.OwnerSeat != 0 && draft.CardOwnerSeat == 0 && draft.RequiredCount > 1 &&
            Enumerable.Range(1, 3).All(viewer => game.CreateSnapshot(viewer).PendingDecision is null),
            "The native AI source must suspend for the real human recipient's private hand draft.");

        using var model = Restore(game, registry);
        game = Program.Engine(model);
        var human = model.Seats.Single(seat => seat.Seat == 0);
        Program.Assert(model.HumanSummary.Contains("待对齐") && human.DeferredPileText.Contains("待对齐") &&
            human.DeferredPileTooltip.Contains($"第 {frame.OwnerSeat + 1} 席") &&
            human.DeferredPileTooltip.Contains("下次实际回合结束时") &&
            human.DeferredPileTooltip.Contains("摸牌至多到五张"),
            "A real remaining next-turn item restores its public source and timing without private hand counts.");
        Program.Assert(model.IsSkillSelectionPending && model.SkillChoices.Count == draft.CandidateCardIds.Count &&
            model.SkillChoices.All(choice => choice.Cards.Count == 1 && draft.CandidateCardIds.Contains(choice.Cards[0])),
            "The shared choice surface offers only the actual recipient's published private candidates.");
        Render(model, output, "246-deferred-hand-alignment-private-recipient.png");

        var beforeMoves = game.CardMovements.Count;
        var chosen = new HashSet<int>();
        var first = model.SkillChoices.First(choice => choice.Cards.Count == 1);
        chosen.Add(first.Cards[0]);
        model.SelectSkillChoiceCommand.Execute(first);
        Program.Assert(game.CardMovements.Count == beforeMoves &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Single(child => child.Id == frame.Id)
                .OwnedCardSelection is { SelectedCardIds.Count: 1 },
            "The first real UI answer drafts one entity and does not move any entity yet.");

        using var partial = Restore(game, registry);
        game = Program.Engine(partial);
        Program.Assert(partial.IsSkillSelectionPending && partial.SkillChoices.All(choice =>
            !choice.Cards.Any(chosen.Contains)), "A restored draft conceals already selected entities from remaining choices.");
        for (var step = 0; step < 64 && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(child => child.Id == frame.Id); step++)
        {
            if (partial.IsSkillSelectionPending && game.PendingDecision is { PlayerSeat: 0 })
            {
                var choice = partial.SkillChoices.First(item => item.Cards.Count == 1);
                chosen.Add(choice.Cards[0]);
                partial.SelectSkillChoiceCommand.Execute(choice);
            }
            else
            {
                Program.Assert(partial.CanStepAi, "The actual suspended due chain must remain resumable.");
                partial.StepAiCommand.Execute(null);
            }
        }

        var moved = game.CardMovements.Skip(beforeMoves)
            .Where(move => move.Reason.Value == "skill-program.deferred-hand.discard").ToArray();
        Program.Assert(!game.ResolutionStack.OfType<ProgramSkillFrame>().Any(child => child.Id == frame.Id) &&
            chosen.Count == draft.RequiredCount && moved.Length == draft.RequiredCount &&
            moved.Select(move => move.CardId).ToHashSet().SetEquals(chosen) &&
            moved.All(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile),
            "The restored shared UI pays the complete exact entity set once and resumes the genuine due parent.");
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed native alignment fixture did not reach its UI boundary.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted native alignment command.");
    }

    private static void Render(MainViewModel model, string output, string file)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, file));
        Program.Assert(Program.Find<TextBlock>(root).Any(text => text.DataContext is SeatViewModel { Seat: 0 } &&
            text.Text.Contains("待对齐") && text.Visibility == Visibility.Visible &&
            text.ActualWidth > 0 && text.ActualHeight > 0), "The actual human recipient's pending due badge is rendered.");
        window.Content = null;
        window.Close();
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-hand-alignment", new Version(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:driver","revision":1,
                "activations":[{"id":"grow-owner","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                "targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:driver":{"name":"真实手数驱动","description":"实体抽牌"}}}""");
            builder.AddSkill(new("fixture:driver", "真实手数驱动", "实体抽牌")
            { Program = catalog.Programs["fixture:driver"], ProgramPresentation = catalog.Presentations["fixture:driver"] });
            builder.AddGeneral(new("fixture:owner", "对齐", "supporter", "fixture:driver", "wei", 12));
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:target-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "郝昭", "hao_zhao", "standard:none", "wei", 12,
                AdditionalSkillIds: ["classic:zhengu"]));
            builder.AddDeck(new("fixture:alignment-deck", "真实实体", 3, 2, [])
            { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Heart, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-haozhao-fixture", "对齐", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:alignment-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:owner", .. others]));
        }
    }
}
