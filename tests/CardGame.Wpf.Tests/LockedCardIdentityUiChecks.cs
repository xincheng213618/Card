using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class LockedCardIdentityUiChecks
{
    public static void DirectPlayAndJieyingHandBadge()
    {
        foreach (var (general, skill) in new[]
        {
            ("ol:shen-liu-bei", "ol:longnu"), ("classic:shen-guan-yu", "classic:wushen")
        })
        foreach (var secondPlay in general == "classic:shen-guan-yu" ? new[] { false } : new[] { false, true })
        foreach (var jigong in general == "ol:shen-liu-bei" && secondPlay ? new[] { false, true } : new[] { false })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(general, skill, jigong));
            var prepared = GameEngine.CreateStandard(new GameOptions
            {
                Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            Accept(prepared, new StartGameCommand());
            Accept(prepared, new SelectGeneralCommand(0, Fixture.Owner, prepared.Revision, prepared.CreateSnapshot(0).PendingDecision!.PromptId));
            ReachPlay(prepared);
            if (secondPlay)
            {
                Accept(prepared, new EndPlayPhaseCommand(0, prepared.Revision, prepared.CreateSnapshot(0).PendingDecision!.PromptId));
                ReachPlay(prepared);
            }
            var store = new MemorySaveStore();
            store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow, false, prepared.CreateCheckpoint()));
            using var model = new MainViewModel(false, 31, true, store, contentRegistry: registry,
                historyStore: new MemoryMatchHistoryStore(), preferencesStore: new MemoryPlayerPreferencesStore())
                { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            var game = Program.Engine(model);
            var expected = general == "classic:shen-guan-yu" ? CardKind.Slash : secondPlay ? CardKind.ThunderSlash : CardKind.FireSlash;
            var action = game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == skill &&
                a.PlayedCardKind == expected && a.TargetSeats.Count == 1);
            var card = model.Hand.Single(c => c.Id == action.CardId);
            var name = CardCatalog.Get(expected).DisplayName;
            var unchanged = Boundary(game);
            var checkpoint = game.CreateCheckpoint();
            var window = new MainWindow(model);
            var root = (FrameworkElement)window.Content;
            try
            {
                root.Measure(new Size(1120, 740)); root.Arrange(new Rect(0, 0, 1120, 740)); root.UpdateLayout();
                if (general != "classic:shen-guan-yu")
                {
                    var player = game.CreateSnapshot(0).Players[0];
                    var seat = model.Seats.Single(s => s.IsHuman);
                    var limit = jigong ? 2 : player.Hp + 2;
                    Program.Assert(player.IsChained && player.HandLimit == limit &&
                        (!jigong || player.ActiveEffects?.Any(e => e.SkillId == "classic:jigong") == true) &&
                        seat.HandBadgeText == $"{player.HandCount}/{limit}" &&
                        Program.Find<TextBlock>(root).Any(t => t.Name == "SeatHandCountText" &&
                            ReferenceEquals(t.DataContext, seat) && t.Text == seat.HandBadgeText),
                        $"{general}: Jieying adds two to the actual base, including zero damage after Jigong; actual {seat.HandBadgeText}.");
                }
                Program.Assert(card.IdentityKind == expected && !model.HumanSkillCards.Single(s => s.ContentId == skill).IsAvailable,
                    "A locked card identity must already apply without an optional skill selection.");
                model.SelectCardCommand.Execute(card);
                model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == action.TargetSeats[0]));
                Program.Assert(model.CanConfirmSelected && model.CanPlaySelected && !model.HasAlternateSlash &&
                    model.PlayButtonText == $"使用 {name}" && model.SelectedCardText.Contains(name, StringComparison.Ordinal) &&
                    model.ActionHint.Contains(name, StringComparison.Ordinal) && !model.HasPublicTargetChoices && Boundary(game) == unchanged,
                    $"{general}/{expected}: physical Snatch must draft its forced identity and legal targets without a skill click or payment.");
                const BindingFlags access = BindingFlags.Instance | BindingFlags.NonPublic;
                var draft = typeof(MainViewModel).GetMethod("CapturePlayCardAimDraft", access)!.Invoke(model, [card]);
                model.SelectCardCommand.Execute(card);
                Program.Assert(draft is not null && (bool)typeof(MainViewModel).GetMethod("RestorePlayCardAimDraft", access)!.Invoke(model, [draft])! && model.CanConfirmSelected,
                    "Pointer aim restoration must preserve the locked source and target.");
                model.ClearSelectionCommand.Execute(null);
                Program.Assert(!model.HasSelection && Boundary(game) == unchanged && card.IdentityKind == expected,
                    "Cancel clears only the draft; a locked identity remains active.");
                model.SelectCardCommand.Execute(card);
                model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == action.TargetSeats[0]));
                var reference = GameReplay.Restore(checkpoint, registry);
                var command = new PlayCardCommand(0, card.Id, action.TargetSeats, game.Revision,
                    game.CreateSnapshot(0).PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)
                { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources };
                Accept(reference, command);
                var button = (Button)window.FindName("PlayCardButton");
                Program.Assert(button.IsEnabled && button.Command.CanExecute(button.CommandParameter), "Real confirm control must be enabled.");
                button.Command.Execute(button.CommandParameter);
                Program.Assert(Boundary(game) == Boundary(reference) &&
                    CommandJson.Serialize([game.AcceptedCommands.Last()]) == CommandJson.Serialize([command]),
                    "Direct confirmation must submit the exact published locked identity and physical card once.");
            }
            finally { window.Content = null; window.Close(); }
        }
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 100; step++)
        {
            var prompt = game.CreateSnapshot(0).PendingDecision;
            if (prompt is { Kind: DecisionKind.PlayCard }) return;
            if (prompt is not null)
                Accept(game, new AnswerPromptCommand(0, prompt.PromptId,
                    (prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "activate") ?? prompt.Choices[0]).Id, game.Revision));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The small locked-identity fixture missed its native Play phase.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Locked-identity command rejected.");
    }
    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray()
    });

    private sealed class Fixture(string general, string skill, bool jigong) : IGameContentPackage
    {
        internal const string Mode = "identity:locked-card-ui", Owner = "fixture:locked-card-owner";
        public PackageManifest Manifest { get; } = new("fixture-locked-card-ui", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, general);
            if (jigong) CharacterFixtureContent.Register(builder, "classic:guo-tu-feng-ji");
            const string initial = "fixture:locked-card-initial";
            var catalog = SkillProgramCatalog.Load($$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.RulesSchemaVersion}}}},"skills":[{"id":"{{{{initial}}}}","revision":1,
                "modifiers":[{"id":"four-cards","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]}]}
                """, $$$$"""
                {"schemaVersion":{{{{SkillProgramCatalog.PresentationSchemaVersion}}}},"skills":{"{{{{initial}}}}":{"name":"固定起手","description":"四张真实起手牌"}}}
                """);
            builder.AddSkill(new(initial, "固定起手", "四张真实起手牌") { Program = catalog.Programs[initial] });
            builder.AddGeneral(new(Owner, "锁定牌名持有者", "supporter", skill, "shu", 2,
                general == "classic:shen-guan-yu" ? [initial] :
                    jigong ? [initial, skill.Split(':')[0] + ":shen-liu-bei-jieying", "classic:jigong"] :
                    [initial, skill.Split(':')[0] + ":shen-liu-bei-jieying"]));
            var peers = Enumerable.Range(1, 3).Select(s => $"fixture:locked-card-peer-{s}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "空手对手", "supporter", "standard:none", "wei", 6));
            const string deck = "fixture:locked-card-deck";
            builder.AddDeck(new(deck, "真实顺手牵羊牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 60).Select(i => new ContentDeckPhysicalCard("standard:snatch", Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "锁定牌名实际四席", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
