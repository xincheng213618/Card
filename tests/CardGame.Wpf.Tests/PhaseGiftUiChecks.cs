using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PhaseGiftUiChecks
{
    public static void IssuedPhaseBanRestores(string output)
    {
        var registry = Registry(new BanFixture());
        var game = Start(registry, 17, Role.Lord, "identity:classic-chendao-fixture", "fixture:owner");
        Accept(game, new UseProgramSkillCommand(0, "fixture:ui-grow", "grow", [], [],
            game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        using var model = Restore(game, registry);
        var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(2));
        Play(model, slash);
        Reach(model, p => p.SkillPrompt?.SkillId == "classic:wanglie");
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Reach(model, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        game = Program.Engine(model);
        Program.Assert(game.CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is { Count: 1 },
            "A real first distant Slash commits the phase prohibition through shared skill controls.");

        using var restored = Restore(game, registry);
        game = Program.Engine(restored);
        var human = restored.Seats.Single(s => s.Seat == 0);
        Program.Assert(restored.HumanSummary.Contains("本阶段不能使用牌") &&
            human.DeferredPileText.Contains("本阶段不能使用牌") && human.DeferredPileTooltip.Contains("打出牌不受此限制") &&
            restored.ActionHint.Contains("结束出牌") && restored.CanEndTurn && restored.CanUseActiveSkill &&
            restored.Hand.Count > 0 && restored.Hand.All(c => !c.IsPlayable),
            "The restored public restriction explains its actual scope, leaves active skills and phase ending available, and disables real hand uses.");
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        restored.SelectCardCommand.Execute(restored.Hand[0]);
        Program.Assert(!restored.HasSelection && !restored.CanConfirmSelected && before == GameCheckpointJson.Serialize(game.CreateCheckpoint()),
            "Clicking a prohibited real hand entity neither selects it nor changes the match.");
        Render(restored, output, "249-issued-play-phase-use-ban.png", "本阶段不能使用牌");
        restored.EndTurnCommand.Execute(null);
        Program.Assert(Program.Engine(restored).CreateSnapshot(0).Players[0].IssuedPlayPhaseUseProhibitions is null &&
            !restored.HumanSummary.Contains("本阶段不能使用牌"), "The actual phase end removes the expired public restriction.");
    }

    public static void ProviderAndLordChoicesRestore(string output)
    {
        var registry = Registry(new GiftFixture());
        var game = Start(registry, 31, Role.Loyalist, "identity:classic-budget-gift-check");
        using var model = Restore(game, registry);
        var slash = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash);
        Play(model, slash);
        Reach(model, p => HasGiftChoice(p));
        game = Program.Engine(model);
        var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        var draft = frame.CompletedFactionGiftDraft!;
        Program.Assert(game.PendingDecision is { PlayerSeat: 0, IsPrivate: true } && frame.OwnerSeat != 0 &&
            draft.ProviderSeat == 0 && draft.Stage == "offer" && draft.CardIds.Contains(slash.CardId!.Value) &&
            Enumerable.Range(1, 3).All(s => game.CreateSnapshot(s).PendingDecision is null),
            "The native human provider chooses the genuine completed Slash cost while the distinct Lord owns the source.");
        using var restored = Restore(game, registry);
        game = Program.Engine(restored);
        Program.Assert(restored.IsSkillSelectionPending && restored.SkillChoices.Count == 2 &&
            restored.CurrentDecisionContext is { TargetSeat: 0, TargetLabel: "正在选择" } context && context.SourceSeat == frame.OwnerSeat &&
            restored.SkillChoices.Single(c => c.Parameters.GetValueOrDefault("mode") == "give").Cards.SequenceEqual(draft.CardIds),
            "A restored shared gift prompt marks the actual chooser and retains exact completed costs without naming the provider as the skill owner.");
        Render(restored, output, "250-completed-slash-provider-gift.png", "交出本次【杀】");
        restored.SelectSkillChoiceCommand.Execute(restored.SkillChoices.Single(c => c.Parameters.GetValueOrDefault("mode") == "give"));
        Reach(restored, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        game = Program.Engine(restored);
        Program.Assert(game.CardMovements.Count(m => m.CardId == slash.CardId && m.Reason.Value == "skill-program.faction-cost-gift.obtain" &&
            m.To == CardLocation.Hand(frame.OwnerSeat)) == 1 &&
            game.CardMovements.Count(m => m.Reason.Value == "skill-program.faction-cost-gift.reward") == 1,
            "The provider's actual UI choice transfers its one entity once before the native AI Lord rewards it.");

        game = Start(registry, 31, Role.Lord, "identity:classic-budget-gift-check");
        using var lord = Restore(game, registry);
        lord.EndTurnCommand.Execute(null);
        Reach(lord, p => HasGiftChoice(p));
        game = Program.Engine(lord);
        frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Last();
        draft = frame.CompletedFactionGiftDraft!;
        Program.Assert(frame.OwnerSeat == 0 && draft is { Stage: "reward" } && draft.ProviderSeat != 0 &&
            game.Events.Any(e => e.Payload is CompletedFactionCostGiftedEvent),
            "A native AI provider independently gifts its real Slash before the human Lord's reward choice.");
        using var restoredLord = Restore(game, registry);
        game = Program.Engine(restoredLord);
        Program.Assert(restoredLord.SkillChoices.Count == 2 && restoredLord.SkillChoices.All(c => c.Cards.Count == 0) &&
            restoredLord.SkillChoices.Single(c => c.Parameters.GetValueOrDefault("mode") == "reward").Targets.SequenceEqual([draft.ProviderSeat]),
            "Restoring the Lord's reward prompt neither repeats the completed gift nor offers its already moved cost.");
        Render(restoredLord, output, "251-human-lord-completed-gift-reward.png", "不奖励");
        restoredLord.SelectSkillChoiceCommand.Execute(restoredLord.SkillChoices.Single(c => c.Parameters.GetValueOrDefault("mode") == "decline"));
        Program.Assert(!Program.Engine(restoredLord).CardMovements.Any(m => m.Reason.Value == "skill-program.faction-cost-gift.reward"),
            "The human Lord can decline the reward through shared controls without reversing the completed gift.");
    }

    private static bool HasGiftChoice(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "faction-cost-gift");
    private static ContentRegistry Registry(IGameContentPackage fixture) => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), fixture);

    private static GameEngine Start(ContentRegistry registry, int seed, Role role, string mode, string? general = null)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 4, HumanSeat = 0, HumanRole = role,
            ModeId = mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, general ?? game.PendingDecision!.ValidContentIds[0], game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        return game;
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void Play(MainViewModel model, LegalAction action)
    {
        model.SelectCardCommand.Execute(model.Hand.Single(c => c.Id == action.CardId));
        if (action.TargetSeat is { } target && !model.Seats.Single(s => s.Seat == target).IsSelectedTarget)
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == target));
        Program.Assert(model.CanConfirmSelected, "The actual card remains playable through shared target and confirmation controls.");
        model.ConfirmSelectedCommand.Execute(null);
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> done)
    {
        for (var step = 0; step < 140; step++)
        {
            var prompt = game.CreateSnapshot(0).PendingDecision;
            if (prompt is { } p && done(p)) return;
            if (prompt is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying or DecisionKind.Nullification })
            {
                var pass = prompt.Choices.First(choice => choice.Cards.Count == 0);
                Accept(game, new AnswerPromptCommand(0, prompt.PromptId, pass.Id, game.Revision));
            }
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed native UI fixture did not reach its required boundary.");
    }

    private static void Reach(MainViewModel model, Func<PendingDecision, bool> done)
    {
        for (var step = 0; step < 140; step++)
        {
            if (Program.Engine(model).CreateSnapshot(0).PendingDecision is { } p && done(p)) return;
            if (model.IsHandResponsePending && model.ResponseChoices.Any(c => c.Cards.Count == 0))
                model.SelectResponseChoiceCommand.Execute(model.ResponseChoices.First(c => c.Cards.Count == 0));
            else
            {
                Program.Assert(model.CanStepAi, "The native shared UI parent must remain resumable.");
                model.StepAiCommand.Execute(null);
            }
        }
        throw new InvalidOperationException("The actual shared UI controls did not reach the required boundary.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted native UI fixture command.");
    }

    private static void Render(MainViewModel model, string output, string file, string text)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, file));
        Program.Assert(Program.Find<TextBlock>(root).Any(t => t.Text.Contains(text) && t.Visibility == Visibility.Visible &&
            t.ActualWidth > 0 && t.ActualHeight > 0), "The actual native restriction or choice label must render.");
        window.Content = null;
        window.Close();
    }

    private sealed class BanFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-phase-ban", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-grow","revision":1,"activations":[{"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:ui-grow":{"name":"摸牌","description":"固定实体夹具"}}}""");
            b.AddSkill(new("fixture:ui-grow", "摸牌", "固定实体夹具") { Program = catalog.Programs["fixture:ui-grow"] });
            b.AddGeneral(new("fixture:owner", "机制将", "supporter", "classic:wanglie", "shu", 12, ["fixture:ui-grow"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:target-{i}", "目标", "supporter", "standard:none", "wei", 12));
            b.AddDeck(new("fixture:ui-ban-deck", "固定实体", 3, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                new ContentDeckPhysicalCard(i % 3 == 0 ? "standard:slash" : i % 3 == 1 ? "standard:dodge" : "standard:nullification", Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-chendao-fixture", "阶段使用", 4, 4, Roles(), "fixture:ui-ban-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:owner", "fixture:target-1", "fixture:target-2", "fixture:target-3"]));
        }
    }

    private sealed class GiftFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-faction-gift", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            for (var i = 0; i < 4; i++) b.AddGeneral(new(i == 0 ? "fixture:sl-owner" : $"fixture:sl-{i}", "机制将", "supporter", "classic:lijun", "wu", 10));
            b.AddDeck(new("fixture:sl-deck", "固定实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i =>
                new ContentDeckPhysicalCard(i % 2 == 0 ? "standard:slash" : "standard:dodge", (Suit)(i % 4), i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-budget-gift-check", "赠牌选择", 4, 4, Roles(), "fixture:sl-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:sl-owner", "fixture:sl-1", "fixture:sl-2", "fixture:sl-3"]));
        }
    }

    private static Dictionary<string, int> Roles() => new() { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 };
}
