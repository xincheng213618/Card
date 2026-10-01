using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class BeneficiaryStateUiChecks
{
    public static void ActualDeathGiftShieldRestores(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "identity:classic-ui-suit-shield", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:ui-shield-owner", game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        var donor = game.CreateSnapshot(0, true).Players.First(player => !player.IsHuman && player.Role == Role.Rebel).Seat;
        Accept(game, new UseProgramSkillCommand(0, "fixture:ui-shield-driver", "death", [], [donor],
            game.Revision, game.PendingDecision!.PromptId));
        ReachPlay(game);
        var shield = game.Events.Select(item => item.Payload).OfType<BeneficiarySuitShieldGrantedEvent>().Single().Shield;
        Program.Assert(!game.CreateSnapshot(0).Players[donor].IsAlive && shield.Source.OwnerSeat == donor,
            "The displayed protection must come from an actual completed native death grant.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        var seat = model.Seats.Single(item => item.Seat == shield.BeneficiarySeat);
        Program.Assert(seat.IsAlive && seat.HasDeferredPile && seat.DeferredPileText.Contains("♣保护") &&
                       seat.DeferredPileTooltip.Contains("其他角色") && seat.DeferredPileTooltip.Contains("下个本人回合开始失效"),
            "The real beneficiary's public protection and expiry text survive restore in the shared seat status badge.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "242-public-beneficiary-suit-shield.png"));
        Program.Assert(Program.Find<TextBlock>(root).Any(text => text.DataContext is SeatViewModel view &&
            view.Seat == shield.BeneficiarySeat && text.Text == seat.DeferredPileText && text.Visibility == Visibility.Visible &&
            text.ActualWidth > 0 && text.ActualHeight > 0), "The actual beneficiary badge is visibly rendered.");
        window.Content = null;
        window.Close();
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed public state fixture did not reach its actual play boundary.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted public state command.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-suit-shield", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-shield-driver","revision":1,"activations":[{"id":"death","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]}]}]}""",
                """{"schemaVersion":3,"skills":{"fixture:ui-shield-driver":{"name":"状态驱动","description":"实际死亡赠予"}}}""");
            foreach (var program in catalog.Programs) builder.AddSkill(new(program.Key, program.Key, "测试") { Program = program.Value });
            builder.AddGeneral(new("fixture:ui-shield-owner", "状态展示者", "supporter", "fixture:ui-shield-driver", "wei", 4));
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:ui-shield-{index}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "赠予者", "supporter", "classic:juexiang", "wei", 4));
            builder.AddDeck(new("fixture:ui-shield-deck", "固定实体牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 90).Select(_ => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, 2)).ToArray()
            });
            builder.AddMode(new("identity:classic-ui-suit-shield", "公开状态", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-shield-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ui-shield-owner", .. others]));
        }
    }
}
