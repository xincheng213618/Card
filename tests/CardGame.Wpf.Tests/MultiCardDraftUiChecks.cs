using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class MultiCardDraftUiChecks
{
    public static void ActiveDiscardSurvivesReleaseAndClock(string output)
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new Fixture()]);
        var game = GameEngine.CreateStandard(StandardWushuangContentRegistry.CreateOptions(31) with
            { UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand());
        ReachSetup(game);
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        var store = Store(game);
        using var model = new MainViewModel(false, 31, true, new MemorySaveStore(), contentRegistry: registry,
            wushuangSaveStore: store, shanheSaveStore: new MemoryShanheCampaignSaveStore())
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadWushuangManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        DriveToPlay(model);
        game = Program.Engine(model);
        model.SelectCardCommand.Execute(model.Hand.First(card => card.Kind == CardKind.Crossbow));
        model.ConfirmSelectedCommand.Execute(null);
        DriveToPlay(model);
        var equipmentId = model.HumanEquipmentSlots.Single(slot => slot.Card?.Kind == CardKind.Crossbow).Card!.Id;
        var cards = model.Hand.Take(3).Select(card => card.Id).ToArray();
        Program.Assert(cards.Length == 3, "The native Zhiheng scene needs three independent hand materials plus an equipped weapon.");
        using var host = new NativeResponseStatusChecks.NativeHost(model);
        Layout(host.Root);
        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == "classic:zhiheng"));
        var prompt = game.PendingDecision!.PromptId;
        var moves = game.CardMovements.Count;
        var skillUses = game.AcceptedCommands.OfType<UseProgramSkillCommand>().Count();
        PressRelease(host, cards[0]); Tick([cards[0]], false);
        PressRelease(host, cards[1]); Tick([cards[0], cards[1]], false);
        model.SelectActiveEquipmentCommand.Execute(model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId));
        Tick([cards[0], cards[1]], true);
        PressRelease(host, cards[0]); Tick([cards[1]], true);
        PressRelease(host, cards[2]); Tick([cards[1], cards[2]], true);
        Program.Assert(!QuickConfirm(host.Window, model.Hand.Single(card => card.Id == cards[2])) &&
            game.CardMovements.Count == moves && game.AcceptedCommands.OfType<UseProgramSkillCommand>().Count() == skillUses,
            "Double-click confirmation must remain disabled for a complete active multi-card draft.");
        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == Fixture.OwnedSkill));
        Tick([cards[1], cards[2]], true);
        Program.Assert(model.SelectionArea.ConfirmText == "发动制衡", "Clicking another skill must not transfer or pay Zhiheng's material draft.");
        model.CancelActionCommand.Execute(null);
        Program.Assert(!model.HasSelection && model.Hand.All(card => !card.IsSelected) &&
            model.HumanEquipmentSlots.All(slot => !slot.IsSelected) && game.CardMovements.Count == moves &&
            game.AcceptedCommands.OfType<UseProgramSkillCommand>().Count() == skillUses,
            "Cancel must clear the whole mixed draft without paying any selected entity.");
        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == "classic:zhiheng"));
        PressRelease(host, cards[0]); PressRelease(host, cards[2]);
        model.SelectActiveEquipmentCommand.Execute(model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId));
        Tick([cards[0], cards[2]], true);
        Program.Render(host.Root, 1120, 740, Path.Combine(output, "active-zhiheng-persistent-multiselect.png"));
        Click((Button)host.Window.FindName("PlayCardButton"));
        var paid = game.AcceptedCommands.OfType<UseProgramSkillCommand>().Skip(skillUses).Single();
        var expected = new[] { cards[0], cards[2], equipmentId };
        Program.Assert(paid.SkillId == "classic:zhiheng" && paid.CardIds.ToHashSet().SetEquals(expected) &&
            expected.All(id => game.CardMovements.Skip(moves).Count(move => move.CardId == id && move.To == CardLocation.DiscardPile) == 1) &&
            !model.HasSelection, "One real confirmation must discard every exact selected hand/equipment entity once.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Program.Assert(SnapshotJson.Serialize(restored.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)),
            "The actual mixed Zhiheng payment and clock commands must restore through the ordinary journal.");

        void Tick(int[] selected, bool weapon)
        {
            var revision = game.Revision;
            Field("_wushuangClockCommittedMilliseconds").SetValue(model, -5000L);
            Method("OnWushuangClockTick").Invoke(model, [null, EventArgs.Empty]);
            Layout(host.Root);
            Program.Assert(game.Revision > revision && game.PendingDecision?.PromptId == prompt &&
                game.CardMovements.Count == moves && game.AcceptedCommands.OfType<UseProgramSkillCommand>().Count() == skillUses &&
                model.Hand.Where(card => card.IsSelected).Select(card => card.Id).ToHashSet().SetEquals(selected) &&
                model.HumanEquipmentSlots.Single(slot => slot.Card?.Id == equipmentId).IsSelected == weapon && model.CanConfirmSelected,
                "A committed native clock refresh must retain the unpaid active hand/equipment set, including its reversals.");
            AssertHandInk(host, selected);
        }
    }

    public static void MultiCardPromptsRequireExplicitConfirmation(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(timed: false));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Fixture.PlainMode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Accept(game, new StartGameCommand()); ReachSetup(game);
        Accept(game, new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; game.PendingDecision?.Kind != DecisionKind.PlayCard && step < 40; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The fixed four-player owned scene must reach its actual human Play boundary before opening the selector.");
        Accept(game, new UseProgramSkillCommand(0, Fixture.OwnedSkill, "select", [], [], game.Revision, game.PendingDecision!.PromptId));
        using (var model = Restore(game, registry))
        using (var host = new NativeResponseStatusChecks.NativeHost(model))
        {
            Layout(host.Root);
            Program.Assert(model.IsOwnedCardSelectionPending && !model.IsCardPaymentSelectionPending,
                "The owned regression must use the actual native two-card selector.");
            VerifyExplicit(host, model.Hand.Where(card => card.IsPlayable).Take(2).Select(card => card.Id).ToArray(), "owned");
        }
        var witness = StoneAxeScenario.FindHumanTrigger(seed: 1);
        using (var model = Restore(witness.Game, witness.Game.ContentRegistry))
        using (var host = new NativeResponseStatusChecks.NativeHost(model))
        {
            Layout(host.Root);
            Program.Assert(model.IsCardPaymentSelectionPending && !model.IsOwnedCardSelectionPending,
                "The payment regression must reach the real Stone Axe multi-card payment.");
            var handIds = model.Hand.Select(card => card.Id).ToHashSet();
            var choice = model.SkillChoices.First(item => item.Cards.Count == 2 && item.Cards.All(handIds.Contains));
            VerifyExplicit(host, choice.Cards.ToArray(), "payment");
        }

        void VerifyExplicit(NativeResponseStatusChecks.NativeHost host, int[] ids, string kind)
        {
            var model = host.Model;
            var game = Program.Engine(model);
            Program.Assert(ids.Length == 2, "A multi-card confirmation regression needs two real hand entities.");
            foreach (var id in ids) PressRelease(host, id);
            var revision = game.Revision;
            var commands = game.AcceptedCommands.Count;
            var moves = game.CardMovements.Count;
            var prompt = game.PendingDecision!.PromptId;
            Program.Assert(model.CanConfirmSelected, "The exact native multi-card set must first enable primary confirmation.");
            Program.Assert(!QuickConfirm(host.Window, model.Hand.Single(card => card.Id == ids[1])) &&
                game.Revision == revision && game.AcceptedCommands.Count == commands && game.CardMovements.Count == moves &&
                game.PendingDecision?.PromptId == prompt && model.Hand.Count(card => card.IsSelected) == 2,
                "The shared double-click handler must leave a complete multi-card prompt unpaid and editable.");
            PressRelease(host, ids[1]);
            Program.Assert(!model.CanConfirmSelected && model.Hand.Single(card => card.Id == ids[0]).IsSelected &&
                !model.Hand.Single(card => card.Id == ids[1]).IsSelected && game.Revision == revision,
                "A second ordinary press must reverse only that selected entity instead of paying the full set.");
            PressRelease(host, ids[1]);
            Program.Render(host.Root, 1120, 740, Path.Combine(output, $"multi-card-{kind}-explicit-confirmation.png"));
            Click((Button)host.Window.FindName("PlayCardButton"));
            Program.Assert(game.AcceptedCommands.Count > commands &&
                ids.All(id => game.CardMovements.Skip(moves).Count(move => move.CardId == id && move.To == CardLocation.DiscardPile) == 1),
                "Primary confirmation must still submit the original native answers and pay both exact cards once.");
        }
    }

    private static void PressRelease(NativeResponseStatusChecks.NativeHost host, int id)
    {
        Layout(host.Root);
        var card = host.Model.Hand.Single(item => item.Id == id);
        var items = (ItemsControl)host.Window.FindName("HandCards");
        var panel = Program.Find<HandPanel>(items).Single();
        var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
        var point = container.TranslatePoint(new Point(8, 80), panel);
        Program.Assert(typeof(HandPanel).GetMethod("PressCardAt", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, [point]) is true,
            "The real hand panel must accept its current physical card's press.");
        Program.Find<Button>(container).Single().RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
    }
    private static void AssertHandInk(NativeResponseStatusChecks.NativeHost host, int[] selected)
    {
        var items = (ItemsControl)host.Window.FindName("HandCards");
        foreach (var card in host.Model.Hand)
        {
            var container = (FrameworkElement)items.ItemContainerGenerator.ContainerFromItem(card);
            var surface = Program.Find<CardLiftSurface>(container).Single();
            var check = Program.Find<Border>(container).Single(border => border.Name == "SelectedCheck");
            var outline = Program.Find<Border>(container).Single(border => border.Name == "CardSelectedOutline");
            var expected = selected.Contains(card.Id);
            Program.Assert(surface.IsSelected == expected && check.Visibility == (expected ? Visibility.Visible : Visibility.Collapsed) &&
                outline.Opacity == (expected ? 1 : 0) && (!expected || surface.RenderTransform.Value.OffsetY == -18),
                "Released selections must retain their lift, check and outline after the clock refresh.");
        }
    }
    private static bool QuickConfirm(MainWindow window, object item) =>
        (bool)typeof(MainWindow).GetMethod("TryQuickConfirm", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [item])!;
    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var model = new MainViewModel(false, game.Seed, true, Store(game), contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }
    private static MemorySaveStore Store(GameEngine game)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        return store;
    }
    private static void ReachSetup(GameEngine game)
    { for (var step = 0; game.PendingDecision?.Kind != DecisionKind.SelectGeneral && step < 40; step++) Accept(game, new AdvanceOneStepCommand(game.Revision)); }
    private static void DriveToPlay(MainViewModel model)
    {
        for (var step = 0; step < 100; step++)
        {
            var game = Program.Engine(model);
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            var prompt = game.CreateSnapshot(0).PendingDecision;
            GameCommand command = prompt is { } own
                ? new AnswerPromptCommand(0, own.PromptId, (own.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip") ?? own.Choices.First()).Id, game.Revision)
                : new AdvanceOneStepCommand(game.Revision);
            var result = (CommandResult)Method("SubmitCommand").Invoke(model, [command])!;
            Program.Assert(result.Accepted, result.Error?.Message ?? "The fixed native timed scene did not reach Play.");
        }
        throw new InvalidOperationException("The fixed timed multi-card scene missed its Play boundary.");
    }
    private static MethodInfo Method(string name) => typeof(MainViewModel).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static FieldInfo Field(string name) => typeof(MainViewModel).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native multi-card fixture rejected its command."); }
    private static void Click(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);
    private static void Layout(FrameworkElement root)
    { root.Width = 1120; root.Height = 740; root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout(); Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle); }

    private sealed class Fixture(bool timed = true) : IGameContentPackage
    {
        internal const string Owner = "fixture:multi-card-draft-owner";
        internal const string OwnedSkill = "fixture:multi-card-draft-owned";
        internal const string PlainMode = "identity:multi-card-draft";
        public PackageManifest Manifest { get; } = new("fixture-multi-card-draft", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            if (timed)
                foreach (var skill in StandardWushuangContentRegistry.Create().Skills.Values.Where(skill =>
                    skill.Id.StartsWith("wushuang:", StringComparison.Ordinal) || skill.Id.StartsWith("shanhe:", StringComparison.Ordinal) ||
                    skill.Id.StartsWith("hulao:", StringComparison.Ordinal) || skill.Id.StartsWith("shenwu:", StringComparison.Ordinal) ||
                    skill.Id.StartsWith("ghost:", StringComparison.Ordinal))) builder.AddSkill(skill);
            else CharacterFixtureContent.Register(builder, "classic:sun-quan");
            var catalog = SkillProgramCatalog.Load($$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.RulesSchemaVersion}}}},"skills":[{"id":"{{{{OwnedSkill}}}}","revision":1,
                 "activations":[{"id":"select","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                 "targetKind":"anyLiving","usesPerTurn":1,"effects":[
                  {"op":"selectOwnedCards","target":"owner","amount":2,"zones":["hand"],"resultBind":"chosen"},
                  {"op":"moveBoundCards","target":"owner","sourceBind":"chosen","destination":"discardPile"}]}]}]}
                """, $$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.PresentationSchemaVersion}}}},"skills":{"{{{{OwnedSkill}}}}":{"name":"区域选择","description":"选择两张真实手牌后一次弃置。"}}}
                """);
            builder.AddSkill(new(OwnedSkill, "区域选择", "选择两张真实手牌后一次弃置。")
                { Program = catalog.Programs[OwnedSkill], ProgramPresentation = catalog.Presentations[OwnedSkill] });
            builder.AddGeneral(new(Owner, "制衡选择", "supporter", "classic:zhiheng", "wu", BaseHp: 20, AdditionalSkillIds: [OwnedSkill]));
            var peers = timed ? new[] { "fixture:multi-card-draft-ai" } : Enumerable.Range(1, 3)
                .Select(index => $"fixture:multi-card-draft-ai-{index}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "对手", "supporter", "standard:none", "wei", BaseHp: 20));
            builder.AddDeck(new("fixture:multi-card-draft-deck", "多选实际牌堆", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 160).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Heart, 7)).ToArray() });
            builder.AddMode(timed
                ? new(WushuangMode.ModeId, "多选计时场景", 2, 2, new Dictionary<string, int>(), "fixture:multi-card-draft-deck", 2,
                    [Owner, "fixture:multi-card-draft-ai"], ContentModeKind.Team,
                    new Dictionary<string, int> { [WushuangMode.PlayerTeamId] = 1, [WushuangMode.AiTeamId] = 1 })
                : new(PlainMode, "多选区域场景", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                    "fixture:multi-card-draft-deck", 4, [Owner, .. peers]));
        }
    }
}
