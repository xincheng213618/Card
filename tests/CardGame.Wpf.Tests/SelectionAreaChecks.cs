using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class SelectionAreaChecks
{
    public static void NativeDraftsAndUsageLimits(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(new StartGameCommand());
        Accept(new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        Resume();
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            var crossbow = model.Hand.First(card => card.Kind == CardKind.Crossbow);
            model.SelectCardCommand.Execute(crossbow);
            ClickConfirm("使用 诸葛连弩", "equip"); ResumeAndRefresh();

            var before = Boundary();
            Begin("classic:gongxin");
            AssertAction("发动攻心", false, "gongxin-empty");
            Program.Assert(Boundary() == before, "Entering Gongxin must only prepare its target draft.");
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == 0));
            AssertAction("发动攻心", false, "gongxin-invalid");
            model.SelectTargetCommand.Execute(model.Seats.First(seat => seat.IsLegalTarget));
            AssertAction("发动攻心", true, "gongxin-target");
            Click((Button)window.FindName("CancelActionButton"));
            Program.Assert(!model.HasSelection && Boundary() == before, "Cancel must discard the skill draft without paying its once-per-phase use.");
            Begin("classic:gongxin");
            var target = model.Seats.First(seat => seat.IsLegalTarget);
            model.SelectTargetCommand.Execute(target);
            ClickConfirm("发动攻心", "gongxin-confirm");
            var use = game.AcceptedCommands.OfType<UseProgramSkillCommand>().Last();
            Program.Assert(use.SkillId == "classic:gongxin" && use.TargetSeats.SequenceEqual([target.Seat]),
                "The common confirm button must submit the actual Gongxin target.");
            ResumeAndRefresh();
            AssertUnavailable("classic:gongxin");

            Begin("classic:zhiheng");
            AssertAction("发动制衡", false, "zhiheng-empty");
            var equipment = model.HumanEquipmentSlots.Single(slot => slot.Card?.Kind == CardKind.Crossbow);
            var hand = model.Hand.First();
            model.SelectActiveEquipmentCommand.Execute(equipment);
            model.SelectCardCommand.Execute(hand);
            AssertAction("发动制衡", true, "zhiheng-hand-equipment");
            ClickConfirm("发动制衡", "zhiheng-confirm");
            use = game.AcceptedCommands.OfType<UseProgramSkillCommand>().Last();
            Program.Assert(use.SkillId == "classic:zhiheng" && use.CardIds.Order().SequenceEqual(new[] { equipment.Card!.Id, hand.Id }.Order()),
                "Zhiheng must submit the selected hand card and equipment together through the same confirmation.");
            ResumeAndRefresh();
            AssertUnavailable("classic:zhiheng");

            var hp = game.CreateSnapshot(0).Players[0].Hp;
            for (var repeat = 0; repeat < 2; repeat++)
            {
                var revision = game.Revision;
                Begin("classic:kujin");
                Program.Assert(game.Revision == revision + 1, "Each click on a repeatable skill with no selections must commit once.");
                ResumeAndRefresh();
            }
            Program.Assert(game.CreateSnapshot(0).Players[0].Hp == hp - 2 &&
                game.AcceptedCommands.OfType<UseProgramSkillCommand>().Count(command => command.SkillId == "classic:kujin") == 2,
                "The common area must retain the native distinction between repeatable and limited skills.");

            var slash = model.Hand.First(card => card.Kind == CardKind.Slash && card.IsPlayable);
            model.SelectCardCommand.Execute(slash);
            AssertAction("使用 杀", false, "slash-empty");
            target = model.Seats.First(seat => seat.IsLegalTarget);
            model.SelectTargetCommand.Execute(target);
            ClickConfirm("使用 杀", "slash-target");
            Program.Assert(game.AcceptedCommands.Last() is PlayCardCommand played && played.CardId == slash.Id && played.TargetSeats.SequenceEqual([target.Seat]),
                "Ordinary Slash must use the same confirmation with its exact physical card and selected target.");
        }
        finally { window.Content = null; window.Close(); }
        SingleRecipientStillNeedsSelection();
        NullificationUsesTheSameArea(output);

        void Begin(string id) => model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == id));
        void AssertUnavailable(string id)
        {
            var revision = game.Revision;
            var chip = model.HumanSkillCards.Single(skill => skill.ContentId == id);
            Program.Assert(!chip.IsAvailable && model.HumanActiveSkillActions.All(action => action.ProgramSkillId != id),
                "A paid once-per-phase skill must disappear from the native legal action set.");
            model.ActivateHumanSkillCommand.Execute(chip);
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.Revision == revision && !model.IsActiveSkillSelectionPending, "Unavailable skill input must not reopen or pay its old draft.");
        }
        void AssertAction(string text, bool enabled, string stage)
        {
            Program.Render(root, 1120, 780, Path.Combine(output, $"selection-area-{stage}.png"));
            var confirm = (Button)window.FindName("PlayCardButton");
            Program.Assert(Equals(confirm.Content, text) && confirm.IsEnabled == enabled &&
                model.SelectionArea.ConfirmText == text && model.SelectionArea.CanConfirm == enabled &&
                ReferenceEquals(confirm.Command, model.SelectionArea.ConfirmCommand),
                $"The actual selection area must show '{text}' with enabled={enabled}.");
        }
        void ClickConfirm(string text, string stage)
        {
            AssertAction(text, true, stage);
            var count = game.AcceptedCommands.Count;
            Click((Button)window.FindName("PlayCardButton"));
            Program.Assert(game.AcceptedCommands.Count == count + 1, "A real confirmation click must commit exactly once.");
        }
        void ResumeAndRefresh()
        {
            Resume();
            typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [game.CreateSnapshot(0)]);
        }
        void Resume()
        {
            for (var step = 0; step < 80; step++)
            {
                var prompt = game.CreateSnapshot(0).PendingDecision;
                if (prompt?.Kind == DecisionKind.PlayCard) return;
                if (prompt is { PlayerSeat: 0 }) Accept(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision));
                else Accept(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("The native selection fixture did not resume play.");
        }
        void Accept(GameCommand command)
        { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
        string Boundary() => $"{game.Revision}:{game.AcceptedCommands.Count}:{game.CardMovements.Count}:{game.Events.Count}";
    }

    private static void Click(Button button) => typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, null);

    private static void NullificationUsesTheSameArea(string output)
    {
        var (model, _) = StableHandResponseFixture.Create(DecisionKind.Nullification, output);
        using (model)
        {
            model.IsSoundEnabled = model.IsMusicEnabled = false;
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                var game = Program.Engine(model);
                var revision = game.Revision;
                var prompt = model.ChoiceSnapshot.PendingDecision!;
                var card = model.Hand.First(item => item.IsPlayable);
                Program.Assert(model.SelectionArea.ConfirmText == "确认无懈" && !model.SelectionArea.CanConfirm &&
                    model.SelectionArea.CancelText == "放 弃" && model.SelectionArea.CanCancel,
                    "The shared area must preserve Nullification's unselected response and independent decline.");
                model.SelectCardCommand.Execute(card);
                var choice = prompt.Choices.Single(candidate => candidate.Cards.SequenceEqual([card.Id]));
                Program.Render(root, 1120, 780, Path.Combine(output, "selection-area-nullification.png"));
                var confirm = (Button)window.FindName("PlayCardButton");
                Program.Assert(Equals(confirm.Content, "确认无懈") && confirm.IsEnabled &&
                    model.SelectionArea.Hint.Contains(choice.Description, StringComparison.Ordinal) && game.Revision == revision,
                    "Selecting Nullification must keep its original effect wording and remain a reversible draft.");
                Click(confirm);
                Program.Assert(game.Revision == revision + 1 && game.AcceptedCommands.Last() is AnswerPromptCommand answer &&
                    answer.PromptId == prompt.PromptId && answer.Choice == choice.Id,
                    "The common confirmation must submit the original Nullification response exactly once.");
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static void SingleRecipientStillNeedsSelection()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(oneRecipient: true));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false }, registry);
        Accept(new StartGameCommand());
        Accept(new SelectGeneralCommand(0, Fixture.Owner, game.Revision, game.PendingDecision!.PromptId));
        for (var step = 0; step < 32 && game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Accept(new AdvanceOneStepCommand(game.Revision));
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        game = Program.Engine(model);
        var revision = game.Revision;
        model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == "classic:gongxin"));
        Program.Assert(model.SelectionArea.ConfirmText == "发动攻心" && !model.SelectionArea.CanConfirm &&
            model.Seats.Count(seat => seat.IsLegalTarget) == 1 && model.Seats.All(seat => !seat.IsSelectedTarget) && game.Revision == revision,
            "A target-only skill must start gray even when the opponent is its sole legal recipient.");
        model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.IsLegalTarget));
        Program.Assert(model.SelectionArea.CanConfirm && game.Revision == revision,
            "The unique recipient becomes an executable draft only after the explicit portrait click.");
        void Accept(GameCommand command)
        { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    }

    private sealed class Fixture(bool oneRecipient = false) : IGameContentPackage
    {
        public const string Mode = "identity:selection-area";
        public const string Owner = "fixture:selection-area";
        public PackageManifest Manifest { get; } = new("fixture-selection-area", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "classic:sun-quan", "classic:shen-lu-meng", "classic:huang-gai");
            const string initial = "fixture:selection-area-initial";
            if (oneRecipient)
            {
                var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new
                { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new[] { new { id = initial, revision = 1,
                    modifiers = new[] { new { id = "four", query = "initialHandSize", operation = "add", value = 4,
                        priority = 0, condition = new { kind = "always" } } } } } }),
                    JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                        skills = new Dictionary<string, object> { [initial] = new { name = "起手", description = "真实起手四张" } } }));
                builder.AddSkill(new(initial, "起手", "真实起手四张") { Program = catalog.Programs[initial], ProgramPresentation = catalog.Presentations[initial] });
            }
            builder.AddGeneral(new(Owner, "选择区", "supporter", "classic:gongxin", "wei", 12,
                oneRecipient ? ["classic:zhiheng", "classic:kujin", initial] : ["classic:zhiheng", "classic:kujin"]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:selection-area-{i}").ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
                builder.AddGeneral(new(id, "目标", "supporter", oneRecipient && index == 0 ? initial : "standard:none", "wei", 12));
            builder.AddDeck(new("fixture:selection-area-deck", "真实选择牌堆", oneRecipient ? 0 : 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 128).Select(i => new ContentDeckPhysicalCard(i % 2 == 0 ? "standard:slash" : "standard:crossbow", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(Mode, "选择区", 4, 4, new Dictionary<string, int>
                { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:selection-area-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
