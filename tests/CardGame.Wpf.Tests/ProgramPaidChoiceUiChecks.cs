using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ProgramPaidChoiceUiChecks
{
    public static void SharedChoiceSurfaces(string output)
    {
        ParticipantPayments(output);
        DonorControlsLordReward(output);
    }

    private static void DonorControlsLordReward(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new RewardPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Renegade, PlayerCount = 4,
            ModeId = RewardPackage.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 20
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, game.PendingDecision!.Choices[0].ContentIds[0],
            game.Revision, game.PendingDecision.PromptId)));
        Reach(game, DecisionKind.RespondSlash);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1));
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Program.Assert(prompt.SkillPrompt?.SkillId == "boundary:jijiang" && prompt.PlayerSeat == 0 &&
                       prompt.TargetSeat == 0 && prompt.SourceSeat is > 0,
            "The actual outside-turn Shu response must produce a donor-controlled Lord reward.");
        var lordSeat = prompt.SourceSeat!.Value;
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        Program.Assert(model.CurrentDecisionContext is { TargetSeat: 0, TargetLabel: "正在选择" } context &&
                       context.SourceSeat == lordSeat && context.Title.Contains("激将", StringComparison.Ordinal) &&
                       model.IsSkillSelectionPending && model.SkillChoices.Count == 2 &&
                       Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "Restoring the reward must identify the human donor as chooser and keep the AI Lord as beneficiary.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "267-program-donor-lord-reward.png"));
        var choicePanel = (ItemsControl)window.FindName("ScrollingSkillChoices");
        var buttons = Program.Find<Button>(choicePanel).Where(button =>
            button.Command == model.SelectSkillChoiceCommand && button.Visibility == Visibility.Visible &&
            button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0).ToArray();
        Program.Assert(!model.HasPinnedPublicModuleChoices && choicePanel.ActualHeight > 0 && buttons.Length == 2,
            "The human donor must have two visible, enabled reward choices through the shared WPF surface.");
        var before = game.CreateSnapshot(0).Players[lordSeat].HandCount;
        var revision = game.Revision;
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
        Program.Assert(game.Revision == revision + 1 &&
                       game.CreateSnapshot(0).Players[lordSeat].HandCount == before + 1 &&
                       game.CreateSnapshot(0).PendingDecision?.SkillPrompt?.SkillId != "boundary:jijiang" &&
                       model.SkillChoices.Count == 0 && !model.IsSkillSelectionPending,
            "The donor's real WPF command must award the Lord once and retire the old private choice.");
        window.Content = null;
        window.Close();
    }

    private static void ParticipantPayments(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new ParticipantPackage());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = ParticipantPackage.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Reach(game, DecisionKind.SelectGeneral);
        Accept(game.Submit(new SelectGeneralCommand(0, ParticipantPackage.OwnerId, game.Revision,
            game.PendingDecision!.PromptId)));
        Reach(game, DecisionKind.PlayCard);
        Accept(game.Submit(new UseProgramSkillCommand(0, "boundary:chuli", "discard-participants", [], [],
            game.Revision, game.PendingDecision!.PromptId)));
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Targets.SequenceEqual([1])));
        Answer(game, game.PendingDecision!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "distinct-faction-finish"));

        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(autoAdvance: false, seed: game.Seed, showSetup: true,
            saveStore: store, useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var ownChoice = model.SkillChoices.First(choice => choice.Cards.Count == 1);
        var ownCardId = ownChoice.Cards[0];
        Program.Assert(model.IsSkillSelectionPending && model.CurrentDecisionContext is { TargetSeat: 0 } &&
                       model.Hand.Any(card => card.Id == ownCardId) &&
                       model.SkillChoices.All(choice => choice.Cards.Count == 1),
            "Restoring a participant plan must show the actual owner's own payment choices.");

        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        model.SelectSkillChoiceCommand.Execute(ownChoice);
        Program.AdvanceToDecision(model);
        var prompt = game.CreateSnapshot(0).PendingDecision!;
        Program.Assert(prompt.SkillPrompt?.SkillId == "boundary:chuli" && model.IsSkillSelectionPending &&
                       model.SkillChoices.Count == 4 && model.SkillChoices.All(choice =>
                           choice.Cards.Count == 0 &&
                           choice.Parameters.GetValueOrDefault("card-owner-seat") == "1" &&
                           choice.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand) &&
                           !choice.Parameters.ContainsKey("card-id")) &&
                       Enumerable.Range(1, 3).All(seat => game.CreateSnapshot(seat).PendingDecision is null),
            "Other hand costs must use the shared choice surface with opaque slots and private chooser ownership.");
        Program.Render(root, 1120, 740, Path.Combine(output, "265-program-participant-private-cost.png"));
        var choicePanel = (ItemsControl)window.FindName("ScrollingSkillChoices");
        var buttons = Program.Find<Button>(choicePanel).Where(button =>
            button.Command == model.SelectSkillChoiceCommand && button.Visibility == Visibility.Visible &&
            button.IsEnabled && button.ActualWidth > 0 && button.ActualHeight > 0).ToArray();
        Program.Assert(!model.HasPinnedPublicModuleChoices && choicePanel.Items.Count == model.SkillChoices.Count &&
                       choicePanel.ActualHeight > 0 && buttons.Length == model.SkillChoices.Count,
            "Every published opaque payment slot must be a visible, enabled WPF choice.");

        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Last());
        Program.AdvanceToDecision(model);
        var costs = game.CardMovements.Where(move =>
            move.Reason.Value == "skill-program.boundary:chuli.participant-discard").ToArray();
        var rewards = game.CardMovements.Where(move =>
            move.Reason.Value == "skill-program.boundary:chuli.participant-reward").ToArray();
        Program.Assert(costs.Length == 2 && costs[0].CardId == ownCardId &&
                       costs.Select(move => move.From.OwnerSeat).SequenceEqual(new int?[] { 0, 1 }) &&
                       rewards.Length == 2 && rewards.All(move => move.Sequence > costs[^1].Sequence) &&
                       game.CreateSnapshot(0).PendingDecision?.Kind == DecisionKind.PlayCard &&
                       model.SkillChoices.Count == 0 && !model.IsSkillSelectionPending,
            "WPF payment commands must commit each physical cost once, finish both rewards, and clear the old choices.");
        Program.Render(root, 1120, 740, Path.Combine(output, "266-program-participant-paid.png"));
        window.Content = null;
        window.Close();
    }

    private static void Reach(GameEngine game, DecisionKind kind)
    {
        for (var step = 0; step < 64; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0 } prompt && prompt.Kind == kind) return;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException($"The fixed UI fixture did not reach {kind}.");
    }

    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(
        new AnswerPromptCommand(0, game.PendingDecision!.PromptId, choice.Id, game.Revision)));

    private static void Accept(CommandResult result) => Program.Assert(result.Accepted,
        result.Error?.Message ?? "The fixed UI command was rejected.");

    private sealed class ParticipantPackage : IGameContentPackage
    {
        internal const string OwnerId = "fixture:paid-choice-owner";
        internal const string ModeId = "identity:program-paid-choice-ui";
        public PackageManifest Manifest { get; } = new("fixture:program-paid-choice-ui", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new(OwnerId, "支付选择夹具", "supporter", "boundary:chuli", "qun", 3, []));
            var factions = new[] { "shu", "wu", "wei" };
            for (var index = 0; index < factions.Length; index++)
                builder.AddGeneral(new($"fixture:paid-choice-{index}", $"参与者 {index + 1}", "supporter",
                    "standard:none", factions[index], 3, []));
            builder.AddDeck(new("fixture:paid-choice-deck", "固定支付牌库", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(index =>
                    new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "实际支付选择界面", 4, 4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2
                }, "fixture:paid-choice-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [OwnerId, "fixture:paid-choice-0", "fixture:paid-choice-1", "fixture:paid-choice-2"]));
        }
    }

    private sealed class RewardPackage : IGameContentPackage
    {
        internal const string ModeId = "identity:classic-program-actor-choice-ui";
        public PackageManifest Manifest { get; } = new("fixture:program-actor-choice-ui", new(1, 0, 0), []);

        public void Register(IContentRegistryBuilder builder)
        {
            var driver = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"fixture:lb-driver","revision":1,"activations":[
                    {"id":"hurt","usesPerTurn":null,"minCards":0,"maxCards":0,"minTargets":0,
                     "maxTargets":0,"targetKind":"anyLiving","effects":[
                       {"op":"loseHp","target":"owner","amount":1}]}]}]}
                """, """
                {"schemaVersion":3,"skills":{"fixture:lb-driver":{"name":"驱动","description":"真实损失"}}}
                """);
            builder.AddSkill(new("fixture:lb-driver", "驱动", "真实损失")
            {
                Program = driver.Programs["fixture:lb-driver"]
            });
            builder.AddGeneral(new("fixture:lb-owner", "固定主公", "supporter", "standard:none", "shu", 20,
                ["boundary:jijiang", "fixture:lb-driver"]));
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new($"fixture:lb-target-{index}", "固定角色", "supporter", "standard:none",
                    "shu", 20, ["boundary:jijiang"]));
            builder.AddDeck(new("fixture:lb-deck", "固定实际响应牌库", 6, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(
                    index % 2 == 0 ? "standard:duel" : "standard:slash", Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(ModeId, "实际响应选择界面", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:lb-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:lb-owner", "fixture:lb-target-1", "fixture:lb-target-2", "fixture:lb-target-3"]));
        }
    }
}
