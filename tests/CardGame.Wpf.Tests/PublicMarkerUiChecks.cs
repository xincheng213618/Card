using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class PublicMarkerUiChecks
{
    public static void ActualMarkersRestoreAndTransfer(string output)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-ui-marker", UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:ui-marker-owner", game.Revision,
            game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.PlayerSeat == 0 && prompt.SkillPrompt?.SkillId == "classic:yili");
        Program.Assert(game.CreateSnapshot(0).Players[0].Markers is [{ Name: "橘", Count: 3 }],
            "The displayed marks must be created by the actual native game-start trigger.");
        using var model = Restore(game, registry);
        game = Program.Engine(model);
        Program.Assert(model.HumanSummary.Contains("橘 ×3") &&
            model.Seats.Single(seat => seat.Seat == 0).DeferredPileText.Contains("橘 ×3"),
            "Actual public marker names and counts must survive restore in both shared status surfaces.");
        Choose(model, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, prompt => prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("pay") == "marker"));
        var payment = model.SkillChoices.Single(choice => choice.Parameters.GetValueOrDefault("pay") == "marker");
        Program.Assert(payment.Description.Contains("橘") && !payment.Description.Contains("暴怒"),
            "The shared marker-payment label must use the actual configured public marker name.");
        model.SelectSkillChoiceCommand.Execute(payment);
        Choose(model, choice => choice.Targets.SequenceEqual([1]));
        Reach(game, prompt => prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.PlayCard);
        using (var transferred = Restore(game, registry))
        {
            var recipient = transferred.Seats.Single(seat => seat.Seat == 1);
            Program.Assert(transferred.HumanSummary.Contains("橘 ×2") &&
                recipient.DeferredPileText.Contains("橘 ×1") && recipient.DeferredPileTooltip.Contains("橘 ×1"),
                "The restored public source and recipient badges reflect an actual payment and transfer.");
            Render(transferred, output, "245-public-markers-after-transfer.png", 1, "橘 ×1");
        }
        var hp = game.CreateSnapshot(0).Players[1].Hp;
        Accept(game, new UseProgramSkillCommand(0, "fixture:ui-marker-driver", "damage", [], [1],
            game.Revision, game.PendingDecision!.PromptId));
        Reach(game, prompt => prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.PlayCard);
        Program.Assert(game.CreateSnapshot(0).Players[1].Hp == hp &&
            game.CreateSnapshot(0).Players[1].Markers is null or { Count: 0 },
            "A genuine prevented damage consumes the recipient mark before checking its updated UI.");
        using var consumed = Restore(game, registry);
        var cleared = consumed.Seats.Single(seat => seat.Seat == 1);
        Program.Assert(!cleared.DeferredPileText.Contains("橘") && !cleared.DeferredPileTooltip.Contains("橘") &&
            consumed.HumanSummary.Contains("橘 ×2"),
            "Only the consumed recipient marker disappears after actual damage and checkpoint restoration.");
    }

    private static MainViewModel Restore(GameEngine game, ContentRegistry registry)
    {
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);
        return model;
    }
    private static void Choose(MainViewModel model, Func<PromptChoice, bool> predicate) =>
        model.SelectSkillChoiceCommand.Execute(model.SkillChoices.Single(predicate));

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed real marker fixture did not reach its UI boundary.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected an accepted marker fixture command.");
    }
    private static void Render(MainViewModel model, string output, string file, int seat, string badge)
    {
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, file));
        Program.Assert(Program.Find<TextBlock>(root).Any(text => text.DataContext is SeatViewModel view &&
            view.Seat == seat && text.Text.Contains(badge) && text.Visibility == Visibility.Visible &&
            text.ActualWidth > 0 && text.ActualHeight > 0), "The actual recipient marker badge is rendered.");
        window.Content = null;
        window.Close();
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-marker", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-marker-driver","revision":1,
                "activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
                "targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:ui-marker-driver":{"name":"试验伤害","description":"形成实际标记消耗状态"}}}""");
            builder.AddSkill(new("fixture:ui-marker-driver", "试验伤害", "形成实际标记消耗状态")
                { Program = catalog.Programs["fixture:ui-marker-driver"] });
            builder.AddGeneral(new("fixture:ui-marker-owner", "陆绩", "lu_ji", "classic:huaiju", "wu", 3,
                AdditionalSkillIds: ["classic:yili", "classic:zhenglun", "fixture:ui-marker-driver"]));
            var others = Enumerable.Range(1, 3).Select(seat => $"fixture:ui-marker-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "旁观者", "supporter", "standard:none", "wei", 3));
            builder.AddDeck(new("fixture:ui-marker-deck", "实际标记界面牌堆", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(index => new ContentDeckPhysicalCard("standard:crossbow", Suit.Club, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-ui-marker", "公开标记界面检查", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-marker-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ui-marker-owner", .. others]));
        }
    }
}
