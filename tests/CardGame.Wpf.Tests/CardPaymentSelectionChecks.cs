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

internal static class CardPaymentSelectionChecks
{
    private const string Skill = "fixture:card-payment-ui";
    public static void SharedPayments(string output)
    {
        foreach (var scenario in new[] { "equipment", "optional-equipment", "range", "optional-pair", "dynamic" })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord,
                PlayerCount = 4, ModeId = Fixture.Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false }, registry);
            Accept(game.Submit(new StartGameCommand()));
            Accept(game.Submit(new SelectGeneralCommand(0, "fixture:payment-owner", game.Revision, game.PendingDecision!.PromptId)));
            ReachPlay(game);
            var equip = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Equip);
            Accept(game.Submit(new PlayCardCommand(0, equip.CardId!.Value, [], game.Revision, game.PendingDecision!.PromptId)));
            ReachPlay(game);
            var target = scenario == "dynamic" ? game.GetHumanLegalActions().First(action => action.ProgramSkillId == Skill).SelectableTargetSeats.Take(1).ToArray() : [];
            Accept(game.Submit(new UseProgramSkillCommand(0, Skill, "pay", [], target, game.Revision, game.PendingDecision!.PromptId)));
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            using var model = new MainViewModel(false, 17, true, store, contentRegistry: registry)
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            game = Engine(model);
            var window = new MainWindow(model);
            try
            {
                var root = (FrameworkElement)window.Content;
                Render(root, 1120, 740, Path.Combine(output, $"payment-{scenario}-empty.png"));
                var confirm = (Button)window.FindName("PlayCardButton");
                Assert(!confirm.IsEnabled && !model.HasCenterChoices && model.CanActFromHand,
                    $"{scenario}: costs must use a disabled bottom confirmation without duplicate central cards.");
                var boundary = GameCheckpointJson.Serialize(game.CreateCheckpoint());
                var moves = game.CardMovements.Count;
                var count = scenario.EndsWith("equipment", StringComparison.Ordinal) ? 1 : 2;
                var equipment = model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equip.CardId);
                if (count == 1)
                {
                    var button = Find<Button>((ItemsControl)window.FindName("HumanEquipmentSlots")).Single(button =>
                        ReferenceEquals(button.CommandParameter, equipment) && button.Command == model.SelectEquipmentMaterialCommand);
                    Click(button);
                    Assert(model.CanConfirmSelected && model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equip.CardId).IsSelected,
                        "An equipment-only cost must be selectable through the actual equipped-card control.");
                    if (scenario == "equipment") VerifyAmbiguousPayment(model);
                }
                else
                {
                    model.SelectCardCommand.Execute(model.Hand[0]);
                    Assert(!confirm.IsEnabled && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == boundary,
                        "A partial pair or variable cost must leave confirmation disabled and the engine unchanged.");
                    model.ConfirmSelectedCommand.Execute(null);
                    model.SelectCardCommand.Execute(model.Hand[1]);
                    Assert(confirm.IsEnabled && model.Hand.Count(card => card.IsSelected) == 2,
                        "Reaching the exact count or variable lower bound must enable confirmation.");
                    model.StartTutorialCommand.Execute(null); model.ExitTutorialCommand.Execute(null);
                    Assert(model.CanConfirmSelected && model.Hand.Count(card => card.IsSelected) == 2,
                        "Returning from practice must retain the uncommitted multi-card payment.");
                }
                Assert(GameCheckpointJson.Serialize(game.CreateCheckpoint()) == boundary && game.CardMovements.Count == moves,
                    "Selection must not answer any prompt or move a payment card before confirmation.");
                Render(root, 1120, 740, Path.Combine(output, $"payment-{scenario}-selected.png"));
                model.ClearSelectionCommand.Execute(null);
                Assert(!confirm.IsEnabled && !model.HasSelection && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == boundary,
                    "Cancelling must clear the payment draft without consuming cards.");
                if (count == 1) model.SelectEquipmentMaterialCommand.Execute(model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equip.CardId));
                else { model.SelectCardCommand.Execute(model.Hand[0]); model.SelectCardCommand.Execute(model.Hand[1]); }
                Click(confirm);
                var paid = game.CardMovements.Skip(moves).Where(move => move.To == CardLocation.DiscardPile &&
                    move.From.OwnerSeat == 0 && move.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment).ToArray();
                Assert(paid.Length == count && !model.HasSelection,
                    $"{scenario}: one confirmation must discard exactly the chosen {count} cards, including a below-maximum variable set.");
                var after = GameCheckpointJson.Serialize(game.CreateCheckpoint());
                model.ConfirmSelectedCommand.Execute(null);
                Assert(GameCheckpointJson.Serialize(game.CreateCheckpoint()) == after, "Repeated confirmation must not pay again.");
                var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(after), registry);
                Assert(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(restored.CreateSnapshot(seat)) == SnapshotJson.Serialize(game.CreateSnapshot(seat))),
                    "Confirmed payments must retain every viewer's replay boundary.");
                if (scenario.StartsWith("optional", StringComparison.Ordinal))
                {
                    model.LoadManualGameCommand.Execute(null); game = Engine(model);
                    model.SelectCardCommand.Execute(model.Hand[0]);
                    model.SelectEquipmentMaterialCommand.Execute(equipment);
                    var beforeDecline = game.CardMovements.Count;
                    Click((Button)window.FindName("CancelActionButton"));
                    Assert(game.CardMovements.Count == beforeDecline && !model.HasSelection,
                        "Declining optional payment must retain all locally selected materials.");
                }
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 64 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        Assert(game.PendingDecision?.Kind == DecisionKind.PlayCard, "The fixed payment fixture must reach Play.");
    }

    private static void VerifyAmbiguousPayment(MainViewModel model)
    {
        var game = Engine(model);
        var snapshot = game.CreateSnapshot(0);
        var native = snapshot.PendingDecision!.Choices.Single();
        var variant = native with { Id = new("fixture:payment-alternative"), Description = "同一张牌的另一支付效果" };
        var refresh = typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        try
        {
            refresh.Invoke(model, [snapshot with { PendingDecision = snapshot.PendingDecision with { Choices = [native, variant] } }]);
            Assert(!model.CanConfirmSelected && model.HandResponseExtraChoices.Count == 2,
                "An ambiguous material must require choosing its exact effect without paying either automatically.");
            model.SelectHandResponseChoiceCommand.Execute(variant with { });
            Assert(!model.CanConfirmSelected, "A copied payment-effect parameter must not choose a variant.");
            model.SelectHandResponseChoiceCommand.Execute(model.HandResponseExtraChoices.Single(choice => choice.Id == variant.Id));
            Assert(model.CanConfirmSelected && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == before,
                "Choosing an effect must only enable final confirmation and preserve the native boundary.");
        }
        finally { refresh.Invoke(model, [snapshot]); }
    }
    private static void Accept(CommandResult result) => Assert(result.Accepted, result.Error?.Message ?? "Rejected payment fixture command.");
    private static void Click(Button button)
    {
        Assert(button.IsEnabled, "The intended actual payment control must be enabled.");
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    }

    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public const string Mode = "identity:payment-ui-4";
        public PackageManifest Manifest { get; } = new("fixture-payment-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var selection = scenario == "equipment"
                ? """{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["equipment"],"count":1,"destination":"discardPile"}"""
                : scenario == "dynamic"
                    ? """{"op":"discardTargetHpCardsAndDamage","target":"selectedTarget"}"""
                : scenario == "optional-equipment"
                    ? """{"op":"selectOwnedCards","target":"owner","zones":["equipment"],"amount":1,"allowDecline":true,"resultBind":"chosen"}"""
                : scenario == "range"
                    ? """{"op":"selectOwnedCards","target":"owner","zones":["hand","equipment"],"minimumCards":2,"maximumCards":3,"resultBind":"chosen"}"""
                    : """{"op":"selectOwnedCards","target":"owner","zones":["hand","equipment"],"amount":2,"allowDecline":true,"resultBind":"chosen"}""";
            var move = scenario is "equipment" or "dynamic" ? "" :
                """,{"op":"moveBoundCards","target":"owner","sourceBind":"chosen","destination":"discardPile"}""";
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Skill}}","revision":1,"activations":[{"id":"pay","minCards":0,"maxCards":0,"minTargets":{{(scenario == "dynamic" ? 1 : 0)}},"maxTargets":{{(scenario == "dynamic" ? 1 : 0)}},"targetKind":"{{(scenario == "dynamic" ? "otherLivingInAttackRange" : "otherLiving")}}","usesPerTurn":1,"effects":[{{selection}}{{move}}]}]}]}""",
                System.Text.Json.JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Skill] = new { name = "弃牌支付", description = "选齐合法手牌或装备后确认支付。" } } }));
            builder.AddSkill(new(Skill, "弃牌支付", "选齐合法费用后确认。") { Program = catalog.Programs[Skill], ProgramPresentation = catalog.Presentations[Skill] });
            builder.AddGeneral(new("fixture:payment-owner", "支付者", "supporter", Skill, "shu", 4));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:payment-target-{seat}", "固定目标", "supporter", "standard:none", "shu", scenario == "dynamic" ? 2 : 4));
            builder.AddDeck(new("fixture:payment-deck", "支付固定牌库", 4, 2, [new("standard:crossbow", 64)]));
            builder.AddMode(new(Mode, "支付界面", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:payment-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:payment-owner", "fixture:payment-target-1", "fixture:payment-target-2", "fixture:payment-target-3"]));
        }
    }
}
