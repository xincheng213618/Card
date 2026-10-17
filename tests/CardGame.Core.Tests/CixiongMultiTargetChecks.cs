using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CixiongMultiTargetChecks
{
    private const string Mode = "identity:classic-cixiong-regression";
    private const string Owner = "fixture:cixiong-owner", Driver = "fixture:cixiong-driver", Quiet = "fixture:cixiong-quiet";

    public static void StagedChoicesKeepTheCurrentNativeSlashTargetAndColdReplay()
    {
        foreach (var count in new[] { 1, 3 })
        foreach (var activate in new[] { false, true })
        {
            var (game, registry) = Start();
            var targets = Enumerable.Range(1, count).ToArray();
            var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash &&
                a.TargetSeats.SequenceEqual(targets) && a.ConversionSource is null);
            var cardId = action.CardId!.Value;
            Accept(game, new PlayCardCommand(0, cardId, targets, game.Revision, Prompt(game)!.PromptId,
                action.PlayedCardKind));
            var useId = game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == cardId).Id;
            var sourceChoices = new List<int>();
            var targetChoices = new List<int>();
            for (var step = 0; step < 100 && !IsPlay(game); step++)
            {
                var prompt = Prompt(game);
                if (prompt is { Kind: DecisionKind.CixiongDoubleSwords })
                {
                    var use = game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == useId);
                    Need(use.TargetSeats.SequenceEqual(targets) && use.TargetSeats[use.TargetIndex] == prompt.TargetSeat &&
                        use.CardAttack?.TargetSeat == prompt.TargetSeat && use.Step == ResolutionFrameStep.Declared,
                        "Both weapon stages retain the accepted target list and its current native attack cursor.");
                    Need(Enumerable.Range(0, 4).Where(s => s != prompt.PlayerSeat)
                            .All(s => game.CreateSnapshot(s).PendingDecision is null),
                        "Only the actual responder sees either staged weapon choice.");
                    Cold(game, registry);
                    if (prompt.PlayerSeat == 0)
                    {
                        sourceChoices.Add(prompt.TargetSeat!.Value);
                        if (count == 3 && activate && sourceChoices.Count == 1)
                            RejectForeignTargetCursors(game, registry, use);
                        var choice = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("action") ==
                            (activate ? "cixiong-use" : "cixiong-skip"));
                        Accept(game, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
                        continue;
                    }
                    targetChoices.Add(prompt.TargetSeat!.Value);
                }
                Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
            var effects = game.Events.Select(e => e.Payload).OfType<CixiongDoubleSwordsResolvedEvent>()
                .Where(e => e.ResolutionId == useId).ToArray();
            Need(IsPlay(game) && sourceChoices.SequenceEqual(targets) &&
                targetChoices.SequenceEqual(activate ? targets : []) &&
                effects.Select(e => e.TargetSeat).SequenceEqual(targets) && effects.All(e => e.Activated == activate),
                "Each target receives exactly one independent activation and, if accepted, its own resource choice.");
            Need(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>()
                    .Where(e => e.SourceSeat == 0).Select(e => e.TargetSeat).SequenceEqual(targets) &&
                game.Events.Select(e => e.Payload).OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1 &&
                game.CardMovements.Count(m => m.CardId == cardId && m.From == CardLocation.Processing &&
                    m.To == CardLocation.DiscardPile) == 1,
                "All native target attacks return before the physical Slash completes and leaves Processing once.");
            Cold(game, registry);
        }
    }

    private static void RejectForeignTargetCursors(GameEngine game, ContentRegistry registry, CardUseFrame original)
    {
        foreach (var index in new[] { -1, original.TargetSeats.Count, 1 })
        {
            var invalid = GameReplay.Restore(game.CreateCheckpoint(), registry);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var stack = (FrameStore)typeof(GameEngine).GetField("_resolutionStack", flags)!.GetValue(invalid)!;
            var use = stack.OfType<CardUseFrame>().Single(u => u.Id == original.Id);
            stack.Replace(use with { TargetIndex = index });
            var rejected = false;
            try { typeof(GameEngine).GetMethod("AssertCoreInvariants", flags)!.Invoke(invalid, null); }
            catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
            { rejected = true; }
            Need(rejected, "An out-of-range cursor or another accepted target cannot own the pending weapon choice.");
        }
    }

    private static (GameEngine, ContentRegistry) Start()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new StandardHulaoActivityPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand());
        Until(game, () => Prompt(game)?.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, Prompt(game)!.PromptId));
        Until(game, () => IsPlay(game));
        var weapon = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip &&
            game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == a.CardId && c.Kind == CardKind.CixiongDoubleSwords));
        Accept(game, new PlayCardCommand(0, weapon.CardId!.Value, weapon.TargetSeats, game.Revision,
            Prompt(game)!.PromptId, weapon.PlayedCardKind));
        Until(game, () => IsPlay(game));
        return (game, registry);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("cixiong-native-target-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"small-fixed-opening","query":"initialHandSize","operation":"add","value":20,"priority":0}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = Driver, description = "Native weapon fixture." } } }));
            builder.AddSkill(new(Driver, Driver, "Native weapon fixture.")
                { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            builder.AddSkill(new(Quiet, Quiet, "No triggered behavior."));
            builder.AddGeneral(new(Owner, "Native source", "", Driver, "shu", 12, ["hulao:shenji"]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:cixiong-peer-{i}").ToArray();
            foreach (var peer in peers)
                builder.AddGeneral(new(peer, peer, "", Quiet, "wei", 12, Gender: GeneralGender.Female));
            const string deck = "fixture:cixiong-deck";
            builder.AddDeck(new(deck, "Physical swords and fire Slash", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(
                i % 2 == 0 ? "classic:cixiong-double-swords" : "standard:fire_slash", Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "Native multi-target weapon regression", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, deck, 4, [Owner, .. peers]));
        }
    }

    private static bool IsPlay(GameEngine game) => Prompt(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Until(GameEngine game, Func<bool> ready)
    {
        for (var step = 0; step < 60; step++)
        { if (ready()) return; Accept(game, new AdvanceOneStepCommand(game.Revision)); }
        throw new InvalidOperationException("The bounded native fixture did not reach its expected prompt.");
    }
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(game.CreateSnapshot(s)) ==
            SnapshotJson.Serialize(restored.CreateSnapshot(s))), "All private player views survive exact cold replay.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
