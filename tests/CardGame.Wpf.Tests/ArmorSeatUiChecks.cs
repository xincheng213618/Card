using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ArmorSeatUiChecks
{
    private const string Driver = "fixture:armor-ui-driver", Mode = "identity:armor-seat-ui";
    private const string Deck = "fixture:armor-ui-deck", OpeningOne = "fixture:armor-ui-opening-one", OpeningTwo = "fixture:armor-ui-opening-two";
    private static string General(int seat) => "fixture:armor-ui-general-" + seat;

    public static void RealOpeningStacksAndDamagePaymentsRefreshEverySeat(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new()
        {
            Seed = 83, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, General(0), game.Revision, game.PendingDecision!.PromptId));
        Play(game);
        var grants = game.Events.Select(e => e.Payload).OfType<ArmorGrantedEvent>().ToArray();
        Program.Assert(game.CreateSnapshot(0).Players.All(player => player.Armor == 6) && grants.Length == 8 &&
            grants.GroupBy(grant => grant.PlayerSeat).All(group => group.Select(grant => (grant.ArmorBefore, grant.ArmorAfter)).SequenceEqual([(0, 3), (3, 6)])),
            "Each real participant must receive two genuine three-point opening grants, rather than a fabricated display-only Armor snapshot.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, 83, true, store, contentRegistry: registry)
        { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        game = Program.Engine(model);
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        try
        {
            Program.Render(root, 1440, 880, Path.Combine(output, "armor-seats-opening.png"));
            foreach (var seat in model.Seats)
            {
                var badge = Badge(seat.Seat);
                Program.Assert(seat is { Armor: 6, HasArmor: true } && badge.Visibility == Visibility.Visible && badge.ActualWidth > 0 &&
                    Program.Find<TextBlock>(badge).Single().Text == "护甲 6" && AutomationProperties.GetName(badge) == "护甲 6" &&
                    badge.ToolTip?.ToString()?.Contains("先消耗护甲") == true && seat.PortraitTooltip.Contains("护甲 6"),
                    "The shared table template must visibly label genuine Armor on every opponent and the local human portrait, with an explicit tooltip and accessible name.");
                Program.Assert(AutomationProperties.GetName(SeatButton(seat.Seat)).Contains("护甲 6"),
                    "The focusable seat button must expose Armor to a screen reader even when decorative child elements are not separate accessibility peers.");
            }
            var originalHuman = model.HumanPlayer;
            var originalOpponent = model.Seats.Single(seat => seat.Seat == 1);
            var hp = game.CreateSnapshot(0).Players.Select(player => player.Hp).ToArray();
            Hit(0, "hit-two"); Hit(1, "hit-two");
            Program.Render(root, 1440, 880, Path.Combine(output, "armor-seats-paid.png"));
            Program.Assert(ReferenceEquals(originalHuman, model.HumanPlayer) && ReferenceEquals(originalOpponent, model.Seats.Single(seat => seat.Seat == 1)) &&
                model.HumanPlayer!.Armor == 4 && originalOpponent.Armor == 4 &&
                new[] { 0, 1 }.All(seat => Badge(seat).Visibility == Visibility.Visible && Program.Find<TextBlock>(Badge(seat)).Single().Text == "护甲 4") &&
                game.CreateSnapshot(0).Players.Select(player => player.Hp).SequenceEqual(hp) &&
                game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().TakeLast(2).All(e =>
                    e is { Amount: 2, ArmorPayment: { ArmorBefore: 6, ArmorAbsorbed: 2, ArmorAfter: 4, ActualHpLost: 0 } }),
                "Real damage must update the existing human and opponent seat objects and their bound Armor digits immediately, while fully absorbed damage leaves their real HP unchanged.");
            Hit(0, "hit-four"); Hit(1, "hit-four");
            Program.Render(root, 1440, 880, null);
            Program.Assert(new[] { 0, 1 }.All(seat => model.Seats.Single(item => item.Seat == seat) is { Armor: 0, HasArmor: false } &&
                    Badge(seat).Visibility == Visibility.Collapsed && AutomationProperties.GetName(SeatButton(seat)) == model.Seats.Single(item => item.Seat == seat).Name) &&
                new[] { 2, 3 }.All(seat => Badge(seat).Visibility == Visibility.Visible && model.Seats.Single(item => item.Seat == seat).Armor == 6),
                "Spending the final four points must hide both local and opponent Armor badges and retire their accessibility value without hiding other seats' unspent Armor.");
            model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null);
            Program.Render(root, 1440, 880, null);
            Program.Assert(!model.HasSaveError && new[] { 0, 1 }.All(seat => Badge(seat).Visibility == Visibility.Collapsed) &&
                model.Seats.Where(seat => seat.Seat is 2 or 3).All(seat => seat.Armor == 6),
                "Cold desktop restoration retains exact paid Armor and does not display or reissue a completed opening grant.");
            game = Program.Engine(model);
            Program.Assert(model.HumanPlayer is { Hp: 5, MaxHp: 5, ShowNumericHealth: false } positive &&
                positive.HealthImages.Count() == 5,
                "Ordinary positive HP with a small maximum retains the original five-pip presentation.");
            Accept(game, new UseProgramSkillCommand(0, Driver, "lose-seven", [], [0], game.Revision, game.PendingDecision!.PromptId));
            for (var step = 0; step < 40 && game.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hp >= 0; step++)
            {
                Program.Assert(game.PendingDecision is null && game.State.Winner == Winner.None,
                    "The real HP-loss activation must reach its dying boundary without another invented input.");
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
            Program.Assert(game.CreateSnapshot(0).Players.Single(player => player.Seat == 0) is { Hp: -2, IsAlive: true } &&
                game.ResolutionStack.OfType<DyingFrame>().Single().VictimSeat == 0,
                "A real seven-point HP loss from five HP must expose the genuine negative dying state before rescue or death is advanced.");
            typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [game.CreateSnapshot(0)]);
            AssertNegativeHealth();
            model.SaveGameCommand.Execute(null); model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            Program.Assert(!model.HasSaveError && game.CreateSnapshot(0).Players.Single(player => player.Seat == 0).Hp == -2,
                "Cold desktop restoration preserves the same real negative HP instead of raising it to zero.");
            AssertNegativeHealth();
        }
        finally { window.Content = null; window.Close(); }

        Border Badge(int seat) => Program.Find<Border>(root).Single(border => border.Name == "SeatArmorBadge" && border.DataContext is SeatViewModel value && value.Seat == seat);
        Button SeatButton(int seat) => Program.Find<Button>(root).Single(button => button.Name == "SeatButton" && button.DataContext is SeatViewModel value && value.Seat == seat);
        void AssertNegativeHealth()
        {
            Program.Render(root, 1440, 880, null);
            var seat = model.HumanPlayer!;
            var rail = Program.Find<Border>(root).Single(border => border.Name == "SeatHealthRail" && border.DataContext is SeatViewModel value && value.Seat == 0);
            var number = Program.Find<Viewbox>(rail).Single(box => box.Name == "SeatNumericHealth");
            Program.Assert(seat is { Hp: -2, MaxHp: 5, ShowNumericHealth: true, HealthValue: "-2/5", NumericHealthText: "-2\n/\n5" } &&
                seat.HpText == "体力 -2/5" && seat.HealthPips.Length == 0 && seat.EmptyHealthPips == new string('○', 5) &&
                seat.HealthImages.Count() == 1 && seat.HealthImages.All(image => image.EndsWith("gallery-hp-empty.png", StringComparison.Ordinal)) &&
                number.Visibility == Visibility.Visible && Program.Find<TextBlock>(number).Single().Text == "-2\n/\n5" &&
                AutomationProperties.GetName(rail) == "-2/5",
                "The real small-maximum negative state must show signed digits and the same accessible value while keeping only empty, nonnegative health pips, before and after cold load.");
        }
        void Hit(int seat, string activation)
        {
            Accept(game, new UseProgramSkillCommand(0, Driver, activation, [], [seat], game.Revision, game.PendingDecision!.PromptId));
            Play(game);
            typeof(MainViewModel).GetMethod("Refresh", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(model, [game.CreateSnapshot(0)]);
        }
    }

    private static void Play(GameEngine game)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
            Program.Assert(game.PendingDecision?.PlayerSeat != 0 && game.State.Winner == Winner.None, "Unexpected human Armor UI fixture boundary.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded Armor UI fixture did not reach its native play prompt.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Program.Assert(result.Accepted, result.Error?.Message ?? "Armor UI command rejected."); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-armor-seat-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var skills = new List<object>
            {
                new { id = Driver, revision = 1, activations = new[] { ("hit-two", 2), ("hit-four", 4), ("lose-seven", 7) }.Select(pair => new
                {
                    id = pair.Item1, minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1,
                    targetKind = "anyLiving", usesPerTurn = (int?)null,
                    effects = new[] { new { op = pair.Item1 == "lose-seven" ? "loseHp" : "damage", target = "selectedTarget", amount = pair.Item2 } }
                }).ToArray() }
            };
            skills.AddRange(new[] { OpeningOne, OpeningTwo }.Select(id => (object)new
            {
                id, revision = 1, triggers = new[] { new { id = "opening", window = "gameStarting", subject = "owner", optional = false,
                    effects = new[] { new { op = "grantArmor", target = "owner", amount = 3 } } } }
            }));
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { Driver, OpeningOne, OpeningTwo }.ToDictionary(id => id, id => new { name = "真实护甲界面检查", description = "真实叠甲开局与伤害支付" }) }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 0; seat < 4; seat++) builder.AddGeneral(new(General(seat), "护甲角色" + seat, "", Driver, "qun", 4, [OpeningOne, OpeningTwo]));
            builder.AddDeck(new(Deck, "零自然摸牌", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 40).Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, index % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "护甲桌面", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                Deck, GeneralCandidateCount: 4, GeneralPoolIds: Enumerable.Range(0, 4).Select(General).ToArray()));
        }
    }
}
