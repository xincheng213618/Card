using System.IO;
using System.Windows;
using System.Windows.Controls;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class ConfiguredConversionsUiChecks
{
    public static void TierRestoresWithActualUpgrade(string output)
    {
        var (game, registry) = Create(conversion: true);
        var store = new MemorySaveStore();
        using var model = new MainViewModel(false, game.Seed, true, store,
            useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        for (var tier = 0; tier <= 2; tier++)
        {
            store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
            model.LoadManualGameCommand.Execute(null);
            Program.Assert(!model.HasSaveError, model.SaveStatus);
            var skill = model.HumanSkillCards.Single(item => item.ContentId == "classic:jiaozhao");
            Program.Assert(skill.StateText.Contains($"当前第 {tier + 1} 级 · 已修改 {tier} 次") &&
                           skill.Tooltip.Contains(skill.StateText),
                "The actual persisted conversion tier must be visible in the shared skill card and tooltip.");
            Program.Render(root, 1120, 740, Path.Combine(output, $"236-conversion-tier-{tier + 1}.png"));
            if (tier == 2) break;
            Damage(game, 0);
            Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:danxin");
            Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "upgrade");
            Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        }
        window.Content = null;
        window.Close();
    }

    public static void PrivateOfferRestoresThroughSharedFaces(string output)
    {
        var (game, registry) = Create(conversion: false);
        Damage(game, 1);
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:huisheng");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        for (var index = 0; index < 2; index++) Answer(game, choice => choice.Cards.Count == 1);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        var cards = game.CreateSnapshot(0).PrivateRevealedCards!.ToArray();
        Program.Assert(cards.Length == 2 && Enumerable.Range(1, 3).All(seat =>
                           game.CreateSnapshot(seat).PrivateRevealedCards is null) &&
                       Enumerable.Range(0, 4).All(seat => game.CreateSnapshot(seat).PublicRevealedCards.Count == 0),
            "Only the actual damage source privately sees the offered physical faces.");
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store,
            useExpandedContent: true, contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError && model.HasPrivatelyViewedCards &&
                       model.PrivateRevealTitle == "贿生 · 仅你可见" &&
                       cards.All(card => model.PrivatelyViewedCards.Any(face => face.Id == card.Id &&
                           face.Kind == card.Kind && face.Rank == card.RankText && face.PublicCardLabel == "仅你可见")),
            "A restored private damage offer uses the existing private card surface with exact real faces.");
        var window = new MainWindow(model);
        window.ApplyTemplate();
        var root = (FrameworkElement)window.Content;
        Program.Render(root, 1120, 740, Path.Combine(output, "237-private-damage-offer.png"));
        var panel = Program.Find<ItemsControl>(root).Single(items => items.Name == "PrivatelyViewedCards");
        var faceButtons = Program.Find<Button>(panel).Where(button => button.Command == model.SelectRevealedCardCommand).ToArray();
        Program.Assert(faceButtons.Length == 2 && faceButtons.All(button => !button.IsEnabled && button.ActualWidth > 0),
            "Offered faces render as read-only cards; the shared choice controls submit the answer.");
        var chosen = model.SkillChoices.First(choice => choice.Parameters.GetValueOrDefault("option") == "gain");
        model.SelectSkillChoiceCommand.Execute(chosen);
        Program.Assert(!model.HasPrivatelyViewedCards && Program.Engine(model).CardMovements.Any(move =>
                           move.CardId == chosen.Cards.Single() && move.To == CardLocation.Hand(0)),
            "The shared gain choice commits the real offered card and retires the private view.");
        window.Content = null;
        window.Close();
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(bool conversion)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(conversion));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, HumanSeat = 0, HumanRole = Role.Lord,
            PlayerCount = 4, ModeId = "identity:ui-configured", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:ui-configured-owner", game.Revision, Pending(game)!.PromptId));
        Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard);
        return (game, registry);
    }

    private static PendingDecision? Pending(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void Damage(GameEngine game, int target) => Accept(game,
        new UseProgramSkillCommand(0, "fixture:ui-configured-damage", "damage", [], [target], game.Revision, Pending(game)!.PromptId));
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = Pending(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision));
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var index = 0; index < 80; index++)
        {
            if (Pending(game) is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The deterministic UI fixture did not reach the required rule boundary.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected accepted fixture command.");
    }

    private sealed class Fixture(bool conversion) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ui-configured", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:ui-configured-damage","revision":1,
                "activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
                "targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:ui-configured-damage":{"name":"试验伤害","description":"制造真实伤害边界"}}}""");
            builder.AddSkill(new("fixture:ui-configured-damage", "试验伤害", "制造真实伤害边界")
                { Program = catalog.Programs["fixture:ui-configured-damage"] });
            builder.AddGeneral(new("fixture:ui-configured-owner", "测试者", "supporter", "fixture:ui-configured-damage", "wei", 9,
                AdditionalSkillIds: conversion ? ["classic:jiaozhao", "classic:danxin"] : []));
            var others = Enumerable.Range(1, 3).Select(index => $"fixture:ui-configured-{index}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "对手", "supporter",
                conversion ? "standard:none" : "classic:huisheng", "wu", 9));
            builder.AddDeck(new("fixture:ui-configured-deck", "测试实体牌", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(
                index % 2 == 0 ? "standard:dodge" : "standard:crossbow", (Suit)(index % 4), index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:ui-configured", "转换界面测试", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:ui-configured-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ui-configured-owner", .. others]));
        }
    }
}
