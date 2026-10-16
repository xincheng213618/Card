using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class SkillRailUiChecks
{
    private const string Shared = "fixture:rail-exchange";
    private const string Grant = "fixture:rail-grant";
    private const string Gained = "fixture:rail-gained";
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    public static void SharedAndAcquiredSkills(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var original = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = Fixture.ModeId, UseInteractiveSetup = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(original, new StartGameCommand());
        ReachPlay(original);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(GameSaveFile.CurrentFormatVersion, DateTimeOffset.UtcNow,
            false, original.CreateCheckpoint()));
        using var model = new MainViewModel(false, 31, true, store, contentRegistry: registry)
            { IsMotionEnabled = false, IsSoundEnabled = false, IsMusicEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var game = Program.Engine(model);
        var window = new MainWindow(model);
        var root = (FrameworkElement)window.Content;
        try
        {
            var retired = model.HumanSkillCards.Last(skill => skill.ContentId == Shared && skill.SkillOwnerSeat is not null);
            model.LoadManualGameCommand.Execute(null);
            game = Program.Engine(model);
            var loadedRevision = game.Revision;
            model.ActivateHumanSkillCommand.Execute(retired);
            Program.Assert(!model.IsActiveSkillSelectionPending && game.Revision == loadedRevision,
                "An equal-looking rail button from a previous engine must retire on reload.");
            var foreign = model.HumanSkillCards.Where(skill => skill.ContentId == Shared && skill.SkillOwnerSeat is not null).ToArray();
            Program.Assert(foreign.Length == 3 && foreign.All(skill => skill.IsAvailable) &&
                foreign.Select(skill => skill.SkillOwnerSeat).Distinct().Count() == 3 &&
                model.HumanSkillCards.Single(skill => skill.ContentId == Shared && skill.SkillOwnerSeat is null).IsAvailable &&
                model.SupplementalActiveSkillActions.Count == 0,
                "Owned and foreign entries of one skill must coexist in the recipient rail without duplicate supplemental buttons.");
            var selected = foreign.Last();
            var action = model.HumanActiveSkillActions.Single(candidate => candidate.ProgramSkillId == Shared &&
                candidate.ProgramSkillOwnerSeat == selected.SkillOwnerSeat);
            var revision = game.Revision;
            model.ActivateHumanSkillCommand.Execute(selected with { });
            Program.Assert(!model.IsActiveSkillSelectionPending && game.Revision == revision,
                "A copied rail item must not open a foreign skill draft.");
            Program.Render(root, 1440, 860, Path.Combine(output, "skill-rail-shared.png"));
            var rail = (ItemsControl)window.FindName("HumanSkillCards");
            var button = Program.Find<Button>(rail).Single(candidate => ReferenceEquals(candidate.CommandParameter, selected));
            Program.Assert(button.IsEnabled && selected.SourceText.Contains($"{selected.SkillOwnerSeat + 1:00} 号位"),
                "The actual rail control must show an enabled foreign entry with its source seat.");
            button.Command!.Execute(button.CommandParameter);
            var card = model.Hand.First(candidate => action.SelectableCardIds.Contains(candidate.Id));
            model.SelectCardCommand.Execute(card);
            model.SelectTargetCommand.Execute(model.Seats.Single(seat => seat.Seat == selected.SkillOwnerSeat));
            Program.Assert(model.CanConfirmSelected && game.Revision == revision,
                "The foreign rail button must prepare its own exact card and owner without paying early.");
            model.ConfirmSelectedCommand.Execute(null);
            Program.Assert(game.AcceptedCommands.Last() is UseProgramSkillCommand command &&
                command.SkillId == Shared && command.SkillOwnerSeat == selected.SkillOwnerSeat &&
                command.CardIds.SequenceEqual([card.Id]) && command.TargetSeats.SequenceEqual([selected.SkillOwnerSeat!.Value]),
                "The rail must submit the selected foreign owner, rather than the owned or first foreign action.");
            ReachPlay(game);
            Refresh(model, game.CreateSnapshot(0));
            var exhausted = model.HumanSkillCards.Single(skill => skill.ContentId == Shared && skill.SkillOwnerSeat == selected.SkillOwnerSeat);
            Program.Assert(!exhausted.IsAvailable && model.HumanSkillCards.Count(skill => skill.ContentId == Shared) == 4 &&
                model.HumanSkillCards.Where(skill => skill.ContentId == Shared && skill.SkillOwnerSeat != selected.SkillOwnerSeat)
                    .All(skill => skill.IsAvailable),
                "An exhausted shared entry must remain grey while other owners and the owned skill retain their separate quotas.");
            revision = game.Revision;
            model.ActivateHumanSkillCommand.Execute(selected);
            model.ActivateHumanSkillCommand.Execute(exhausted);
            Program.Assert(!model.IsActiveSkillSelectionPending && game.Revision == revision,
                "Retired and unavailable rail buttons must not reuse another owner's legal action.");

            model.ActivateHumanSkillCommand.Execute(model.HumanSkillCards.Single(skill => skill.ContentId == Grant));
            ReachPlay(game);
            var grantedView = game.CreateSnapshot(0);
            Refresh(model, grantedView);
            var gained = model.HumanSkillCards.Single(skill => skill.ContentId == Gained);
            Program.Assert(gained.IsAvailable && gained.SourceText.Contains("获得技能", StringComparison.Ordinal) &&
                !gained.SourceText.Contains("觉醒", StringComparison.Ordinal) &&
                model.HumanSkillColumns == 2 && model.SupplementalActiveSkillActions.Count == 0,
                "A real phase-granted skill must join the same rail and use a general acquisition label.");
            Program.Render(root, 1120, 740, Path.Combine(output, "skill-rail-acquired.png"));
            Program.Assert(Program.Find<Button>(rail).Any(candidate => ReferenceEquals(candidate.CommandParameter, gained) && candidate.IsEnabled),
                "The acquired skill must render as an enabled button inside the actual skill rail.");

            // Use a permitted acquired-skill view to exercise the national slot
            // presentation independently of the identity fixture's rule flow.
            var national = grantedView with
            {
                ModeKind = ContentModeKind.NationalWarLite,
                Players = grantedView.Players.Select(player => player.IsHuman ? player with
                { IsGeneralPublic = false } : player).ToArray()
            };
            Refresh(model, national);
            Program.Assert(model.HumanSkillCards.Single(skill => skill.ContentId == Gained) is
                { IsAvailable: true, IsDisabled: false } &&
                model.HumanSkillCards.Single(skill => skill.ContentId == Grant).IsDisabled,
                "National presentation must retain acquired skills independently of a hidden printed general slot.");
            Refresh(model, grantedView);
            gained = model.HumanSkillCards.Single(skill => skill.ContentId == Gained);
            model.ActivateHumanSkillCommand.Execute(gained);
            Program.Assert(game.AcceptedCommands.Last() is UseProgramSkillCommand { SkillId: Gained, SkillOwnerSeat: null },
                "An acquired skill must activate as the recipient's own skill.");
            ReachPlay(game);
            Refresh(model, game.CreateSnapshot(0));
            Program.Assert(!model.HumanSkillCards.Single(skill => skill.ContentId == Gained).IsAvailable,
                "A used acquired skill must remain in the rail with its current disabled state.");
            model.EndTurnCommand.Execute(null);
            for (var step = 0; step < 20 && game.CreateSnapshot(0).Phase == TurnPhase.Play; step++)
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            Refresh(model, game.CreateSnapshot(0));
            Program.Assert(model.HumanSkillCards.All(skill => skill.ContentId != Gained),
                "An expired phase skill must leave the recipient rail immediately.");
            var boundary = SnapshotJson.Serialize(game.CreateSnapshot(0));
            model.ActivateHumanSkillCommand.Execute(gained);
            Program.Assert(SnapshotJson.Serialize(game.CreateSnapshot(0)) == boundary,
                "A retired acquired skill button must not act after expiry.");
        }
        finally
        {
            window.Content = null;
            window.Close();
        }
    }

    private static void Refresh(MainViewModel model, GameSnapshot snapshot) =>
        typeof(MainViewModel).GetMethod("Refresh", Flags)!.Invoke(model, [snapshot]);

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80 && game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        Program.Assert(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The fixed skill rail fixture must reach its human play decision.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected a fixture command to be accepted.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string ModeId = "identity:skill-rail";
        public PackageManifest Manifest { get; } = new("fixture-skill-rail", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = JsonNode.Parse("""
                {"skills":[
                  {"id":"fixture:rail-exchange","revision":1,"activations":[{"id":"exchange","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"sourceZones":["hand","equipment"],"effects":[{"op":"exchangeOwnedCardThroughDeckEnd","target":"owner"}]}]},
                  {"id":"fixture:rail-grant","revision":1,"activations":[{"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"grantPlayPhaseSkills","target":"owner","skillIds":["fixture:rail-gained"]}]}]},
                  {"id":"fixture:rail-gained","revision":1,"activations":[{"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
                ]}
                """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), """
                {"schemaVersion":3,"skills":{
                  "fixture:rail-exchange":{"name":"问卦入口","description":"其他角色也可交给拥有者一张牌。"},
                  "fixture:rail-grant":{"name":"授技","description":"获得技能直到阶段结束。"},
                  "fixture:rail-gained":{"name":"获得技能","description":"出牌阶段限一次，摸一张牌。"}
                }}
                """);
            foreach (var (id, program) in catalog.Programs)
            {
                var display = catalog.Presentations[id];
                builder.AddSkill(new(id, display.Name, display.Description)
                { Program = program, ProgramPresentation = display, ActionForms = SkillActionForm.Active });
            }
            var ids = Enumerable.Range(0, 4).Select(index => $"fixture:rail-{index}").ToArray();
            foreach (var (id, index) in ids.Select((id, index) => (id, index)))
                builder.AddGeneral(new(id, $"技能角色{index}", "supporter", Shared, "qun", 8, [Grant]));
            builder.AddDeck(new("fixture:rail-deck", "技能栏固定牌堆", 4, 2, [new("standard:slash", 80)]));
            builder.AddMode(new(ModeId, "技能栏", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:rail-deck", GeneralCandidateCount: 4, GeneralPoolIds: ids));
        }
    }
}
