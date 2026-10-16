using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;
using static Program;

internal static class EquipmentSkillInteractionChecks
{
    public static void SpearStartsFromEquipment(string output)
    {
        using var model = CreateModel("ol:du-yu");
        var game = Engine(model);
        var window = new MainWindow(model);
        try
        {
            var root = (FrameworkElement)window.Content;
            Render(root, 1120, 740, Path.Combine(output, "spear-idle.png"));
            Assert(model.SupplementalActiveSkillActions.All(action => action.EquipmentKind != CardKind.ZhangbaSerpentSpear),
                "An equipped Spear must activate from its own slot without a duplicate supplemental button.");
            var slot = model.HumanEquipmentSlots.Single(item => item.Card is not null);
            var before = Boundary(game);
            model.SelectEquipmentMaterialCommand.Execute(slot with { });
            Assert(!model.IsActiveSkillSelectionPending && Boundary(game) == before, "A copied equipment parameter must retire.");
            Click(EquipmentButton(window, model, slot));
            Assert(model.IsActiveSkillSelectionPending && model.ActionHint.Contains("丈八蛇矛") &&
                model.HumanEquipmentSlots.Single(item => item.Card is not null).IsSelected && Boundary(game) == before,
                "Clicking the actual Spear must start its own unpaid card/target draft even alongside Sanchen.");
            Click(EquipmentButton(window, model, model.HumanEquipmentSlots.Single(item => item.Card is not null)));
            Assert(!model.HasSelection && Boundary(game) == before, "Clicking the selected Spear again must cancel its unpaid draft.");
            Click(EquipmentButton(window, model, model.HumanEquipmentSlots.Single(item => item.Card is not null)));
            var cards = model.Hand.Take(2).ToArray();
            foreach (var card in cards) model.SelectCardCommand.Execute(card);
            Assert(!model.CanConfirmSelected && Boundary(game) == before, "Two Spear materials still require a legal target.");
            model.SelectTargetCommand.Execute(model.Seats.First(seat => seat.IsLegalTarget));
            Assert(model.CanConfirmSelected && cards.All(card => card.IsSelected) && Boundary(game) == before,
                "The Spear must retain two selected cards plus its target without paying early.");
            Render(root, 1120, 740, Path.Combine(output, "spear-ready.png"));
            Click((Button)window.FindName("PlayCardButton"));
            Assert(game.AcceptedCommands.Last() is UseEquipmentEffectCommand command &&
                command.EquipmentKind == CardKind.ZhangbaSerpentSpear && command.CardIds.ToHashSet().SetEquals(cards.Select(card => card.Id)),
                "The shared confirmation must submit the exact Spear materials once.");
            var after = Boundary(game);
            model.ConfirmSelectedCommand.Execute(null);
            Assert(Boundary(game) == after, "A repeated Spear confirmation must not pay twice.");
        }
        finally { window.Content = null; window.Close(); }
    }

    public static void SanchenAndZhihengMixZones(string output)
    {
        foreach (var general in new[] { "ol:du-yu", "classic:sun-quan" })
        {
            using var model = CreateModel(general);
            var game = Engine(model);
            var window = new MainWindow(model);
            try
            {
                var skillId = general == "ol:du-yu" ? "ol:sanchen" : "classic:zhiheng";
                var skill = model.HumanSkillCards.Single(item => item.ContentId == skillId);
                if (general == "ol:du-yu") Assert(skill.ProgressText == "0/3", "Sanchen must show its initial game progress.");
                else Assert(skill.ProgressText == "0/1", "Ordinary phase-limited skills must also show their quota.");
                model.ActivateHumanSkillCommand.Execute(skill);
                if (general == "ol:du-yu")
                {
                    model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.IsHuman));
                    model.ConfirmSelectedCommand.Execute(null);
                    Assert(model.IsOwnedCardSelectionPending && model.ChoiceSnapshot.PendingDecision!.RequiredCardCount == 3,
                        "The real Sanchen discard must collect three cards before confirming.");
                }
                var root = (FrameworkElement)window.Content;
                Render(root, 1120, 740, Path.Combine(output, $"{skillId.Replace(':', '-')}-empty.png"));
                var before = Boundary(game);
                if (general == "ol:du-yu")
                {
                    var candidate = model.SkillChoices.First(choice => choice.Cards.Count == 1);
                    model.SelectSkillChoiceCommand.Execute(candidate);
                    Assert(model.HasSelection && Boundary(game) == before,
                        "Even a still-published central card candidate must only toggle the local unpaid draft.");
                    model.SelectSkillChoiceCommand.Execute(candidate);
                    Assert(!model.HasSelection && Boundary(game) == before, "The same candidate must toggle off without answering.");
                }
                var slot = model.HumanEquipmentSlots.Single(item => item.Card is not null);
                Click(EquipmentButton(window, model, slot));
                var cards = model.Hand.Take(general == "ol:du-yu" ? 2 : 1).ToArray();
                foreach (var card in cards) model.SelectCardCommand.Execute(card);
                Assert(model.HumanEquipmentSlots.Single(item => item.Card is not null).IsSelected && cards.All(card => card.IsSelected) &&
                    model.CanConfirmSelected && Boundary(game) == before,
                    "The real skill must hold mixed hand/equipment selections until final confirmation.");
                if (general == "ol:du-yu") Assert(!model.HasCenterChoices, "Sanchen must not expose one-at-a-time discard buttons.");
                model.ClearSelectionCommand.Execute(null);
                Assert(!model.HasSelection && Boundary(game) == before, "Cancel must preserve every unpaid mixed-zone card.");
                if (general == "classic:sun-quan")
                    model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(item => item.ContentId == skillId));
                Click(EquipmentButton(window, model, model.HumanEquipmentSlots.Single(item => item.Card is not null)));
                foreach (var card in cards) model.SelectCardCommand.Execute(card);
                Render(root, 1120, 740, Path.Combine(output, $"{skillId.Replace(':', '-')}-ready.png"));
                var moves = game.CardMovements.Count;
                Click((Button)window.FindName("PlayCardButton"));
                var ids = cards.Select(card => card.Id).Append(slot.Card!.Id).ToHashSet();
                Assert(game.CardMovements.Skip(moves).Count(move => ids.Contains(move.CardId) && move.To == CardLocation.DiscardPile) == ids.Count &&
                    !game.CreateSnapshot(0).Players[0].Equipment.Any(card => card.Id == slot.Card.Id),
                    "One confirmation must discard exactly the selected hand and equipment cards.");
                if (general == "classic:sun-quan") Assert(model.HumanSkillCards.Single(item => item.ContentId == skillId).ProgressText == "1/1",
                    "Zhiheng must display its exhausted phase quota after payment.");
                if (general == "ol:du-yu")
                {
                    Assert(model.HumanSkillCards.Single(item => item.ContentId == skillId).ProgressText == "1/3",
                        "A real accepted Sanchen activation must advance the visible counter.");
                    model.SaveGameCommand.Execute(null);
                    model.LoadManualGameCommand.Execute(null);
                    game = Engine(model);
                    Assert(!model.HasSaveError && model.HumanSkillCards.Single(item => item.ContentId == skillId).ProgressText == "1/3",
                        "Loading must reconstruct the game activation count.");
                    for (var count = 2; count <= 3; count++)
                    {
                        NextTurn(game);
                        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(item => item.ContentId == skillId));
                        model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.IsHuman));
                        model.ConfirmSelectedCommand.Execute(null);
                        Assert(model.HumanSkillCards.Single(item => item.ContentId == skillId).ProgressText == $"{count}/3",
                            "Every real activation must count once even while its discard is pending.");
                        foreach (var card in model.Hand.Take(3).ToArray()) model.SelectCardCommand.Execute(card);
                        model.ConfirmSelectedCommand.Execute(null);
                        model.SaveGameCommand.Execute(null);
                        model.LoadManualGameCommand.Execute(null);
                        game = Engine(model);
                        Assert(!model.HasSaveError && model.HumanSkillCards.Single(item => item.ContentId == skillId).ProgressText == $"{count}/3",
                            "Progress must survive saving and loading at every count.");
                    }
                    Render(root, 1120, 740, Path.Combine(output, "sanchen-three-activations.png"));
                    NextTurn(game);
                    Assert(model.HumanSkillCards.Any(item => item.ContentId == "ol:pozhu") &&
                        model.HumanSkillCards.Single(item => item.ContentId == skillId).ProgressText == "3/3" &&
                        model.HumanSkillCards.Single(item => item.ContentId == "ol:zhaotao").StateText == "已觉醒",
                        "Reaching three real activations must display readiness and awaken on the next actual turn.");
                }
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static MainViewModel CreateModel(string general)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(general));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 24
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, general, game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
        var equipment = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
        Accept(game.Submit(new PlayCardCommand(0, equipment.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, 17, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            $"The fixed equipment fixture must reach Play: {game.State.Status}, {game.PendingDecision?.Kind}, {game.PendingDecision?.Prompt}.");
    }
    private static void NextTurn(GameEngine game)
    {
        ReachPlay(game);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        ReachPlay(game);
    }
    private static string Boundary(GameEngine game) => GameCheckpointJson.Serialize(game.CreateCheckpoint());
    private static void Accept(CommandResult result) => Assert(result.Accepted, result.Error?.Message ?? "Rejected fixture command.");
    private static Button EquipmentButton(MainWindow window, MainViewModel model, EquipmentSlotViewModel slot)
    {
        ((FrameworkElement)window.Content).UpdateLayout();
        return Find<Button>((ItemsControl)window.FindName("HumanEquipmentSlots")).Single(button =>
            ReferenceEquals(button.CommandParameter, slot) && button.Command == model.SelectEquipmentMaterialCommand);
    }
    private static void Click(Button button)
    {
        Assert(button.IsEnabled && button.Visibility == Visibility.Visible, "The actual selected control must be enabled and visible.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }

    private sealed class Fixture(string general) : IGameContentPackage
    {
        internal const string Mode = "identity:classic-equipment-ui-4";
        public PackageManifest Manifest { get; } = new("fixture-equipment-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, general);
            builder.AddCard(new("fixture:spear", "丈八蛇矛", "装备牌", "装备交互测试", CardKind.ZhangbaSerpentSpear));
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:equipment-idle",
                "revision":1,"triggers":[{"id":"skip-ai-play","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """, $$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.PresentationSchemaVersion}}}},"skills":{"fixture:equipment-idle":{"name":"固定轮转","description":"跳过测试对手的出牌阶段。"}}}
                """);
            builder.AddSkill(new("fixture:equipment-idle", "固定轮转", "跳过测试对手的出牌阶段。")
                { Program = catalog.Programs["fixture:equipment-idle"], ProgramPresentation = catalog.Presentations["fixture:equipment-idle"] });
            for (var index = 1; index < 4; index++)
                builder.AddGeneral(new($"fixture:equipment-target-{index}", "固定目标", "supporter", "fixture:equipment-idle", "shu", 4));
            builder.AddDeck(new("fixture:equipment-deck", "装备交互固定牌库", 4, 2, [new("fixture:spear", 80)]));
            builder.AddMode(new(Mode, "装备交互", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:equipment-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [general, "fixture:equipment-target-1", "fixture:equipment-target-2", "fixture:equipment-target-3"]));
        }
    }
}
