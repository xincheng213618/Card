using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhuHuanChecks
{
    private const string GeneralId = "classic:zhu-huan";
    private const string SkillId = "classic:youdi";
    private const string SyntheticSkillId = "fixture:kind-test";
    private const string SyntheticGeneralId = "fixture:kind-owner";
    private const string RulesResource = "CardGame.Content.Standard.SkillPrograms.classic-zhu-huan.rules.json";
    private const string PresentationResource = "CardGame.Content.Standard.SkillPrograms.classic-zhu-huan.presentation.json";

    public static void DefinitionAndReusableKindCondition()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var general = registry.Generals[GeneralId];
        var skill = registry.Skills[SkillId];
        Require(general is { Name: "朱桓", FactionId: "wu", BaseHp: 4, PortraitKey: "zhu_huan" } &&
                general.SkillIds.SequenceEqual([SkillId]) &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(GeneralId) &&
                registry.Modes["identity:classic-8"].GeneralPoolIds!.Contains(GeneralId) &&
                skill.Program is { RuntimeVersion: SkillProgramCatalog.RuntimeVersion, MinimumRulesVersion: 172 } &&
                skill.Program.Triggers.Single().Window == SkillProgramTriggerWindow.TurnEnding &&
                (int)SkillProgramConditionKind.BoundCardsMatchKinds == 20,
            "Zhu Huan must be a formal Wu/Fame IV general using the reusable schema-54 condition.");
        var rules = Resource(RulesResource);
        var presentation = Resource(PresentationResource);
        Reject(rules.Replace("\"sourceBind\": \"discarded-card\",", "", StringComparison.Ordinal),
            presentation, "sourceBind");
        Reject(rules.Replace("\"sourceBind\": \"discarded-card\"", "\"sourceBind\": \"future-card\"", StringComparison.Ordinal),
            presentation, "unknown card binding");
        Reject(rules.Replace("\"slash\", \"fireSlash\", \"thunderSlash\"",
                "\"slash\", \"slash\"", StringComparison.Ordinal),
            presentation, "duplicate");
        Reject(rules.Replace("\"slash\", \"fireSlash\", \"thunderSlash\"",
                "", StringComparison.Ordinal),
            presentation, "nonempty");
        Reject(rules.Replace("\"slash\", \"fireSlash\", \"thunderSlash\"",
                "\"not-a-kind\"", StringComparison.Ordinal),
            presentation, "not-a-kind");
        Reject(rules.Replace("\"cardKinds\": [\"slash\", \"fireSlash\", \"thunderSlash\"]",
                "\"cardCategories\": [\"basic\"]", StringComparison.Ordinal),
            presentation, "cardCategories");
        // A second skill ID must compile the same condition and produce the opposite branch,
        // proving neither parsing nor execution keys off Zhu Huan's name.
        Require(SyntheticCatalog().Programs[SyntheticSkillId].Triggers.Single().Effects.Count == 3,
            "The generic bound-kind condition must parse for an unrelated skill ID.");
    }

    public static void SlashVariantsStopReturnAndNonSlashTransfersWithReplay()
    {
        foreach (var (cardId, kind) in new[]
                 {
                     ("standard:slash", CardKind.Slash),
                     ("standard:fire_slash", CardKind.FireSlash),
                     ("standard:thunder_slash", CardKind.ThunderSlash),
                     ("standard:dodge", CardKind.Dodge)
                 })
        {
            var (game, registry) = Create(cardId, initialHand: 1, drawPerTurn: 0);
            ReachYoudi(game);
            AnswerAction(game, "activate");
            AnswerTarget(game, 1);
            var chooser = RequirePrompt(game, 1);
            Require(chooser.IsPrivate && chooser.Choices.Count == 1 &&
                    chooser.Choices[0].Cards.Count == 0 &&
                    chooser.Choices[0].Parameters.GetValueOrDefault("card-owner-seat") == "0" &&
                    game.CreateSnapshot(0).PendingDecision is null,
                "The chosen opponent alone must see one opaque Zhu Huan hand slot, with no decline.");
            var beforeIllegal = State(game);
            var beforeIllegalMoves = game.CardMovements.Count;
            var wrongResponder = game.Submit(new AnswerPromptCommand(0, chooser.PromptId,
                chooser.Choices[0].Id, game.Revision));
            var forgedChoice = game.Submit(new AnswerPromptCommand(1, chooser.PromptId,
                new ChoiceId("forged"), game.Revision));
            Require(!wrongResponder.Accepted && !forgedChoice.Accepted && State(game) == beforeIllegal &&
                    game.CardMovements.Count == beforeIllegalMoves,
                "Wrong responders and forged private card choices must reject without advancing Youdi.");
            var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            RunPastAiDiscard(game);
            RunPastAiDiscard(paused);
            var discarded = game.CardMovements.Last(move =>
                move.Reason.Value == $"skill-program.{SkillId}.SelectAndMoveOwnedCard" &&
                move.To == CardLocation.DiscardPile);
            Require(discarded.CardKind == kind, "The physical discarded kind must retain its identity.");
            if (kind == CardKind.Dodge)
            {
                var taking = RequirePrompt(game, 0);
                Require(taking.IsPrivate && taking.Choices.Count == 1 &&
                        taking.Choices[0].Cards.Count == 0 &&
                        taking.Choices[0].Parameters.GetValueOrDefault("card-owner-seat") == "1",
                    "A non-Slash discard must let Zhu Huan take one opaque target hand card.");
                Require(State(paused) == State(game), "The discard-choice checkpoint lost the return prompt.");
                var takePaused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                Answer(game, taking.Choices.Single());
                Answer(paused, RequirePrompt(paused, 0).Choices.Single());
                Answer(takePaused, RequirePrompt(takePaused, 0).Choices.Single());
                Require(game.CardMovements.Count(move =>
                            move.Reason.Value == $"skill-program.{SkillId}.SelectAndMoveOwnedCard" &&
                            move.To == CardLocation.Hand(0)) == 1 &&
                        State(takePaused) == State(game) && Events(takePaused).SequenceEqual(Events(game)),
                    "A non-Slash discard must take exactly one card and replay both private pauses.");
            }
            else
            {
                Require(game.PendingDecision?.SkillPrompt?.SkillId != SkillId &&
                        !game.CardMovements.Any(move =>
                            move.Reason.Value == $"skill-program.{SkillId}.SelectAndMoveOwnedCard" &&
                            move.To == CardLocation.Hand(0)),
                    "All three physical Slash variants must suppress the return transfer.");
            }
            Require(State(paused) == State(game) && Events(paused).SequenceEqual(Events(game)),
                "The opponent's private discard checkpoint diverged.");
        }
    }

    private static (GameEngine Game, ContentRegistry Registry) Create(
        string physicalCardId, int initialHand, int drawPerTurn, bool synthetic = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new Scenario(physicalCardId, initialHand, drawPerTurn, synthetic));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = Scenario.ModeId, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, synthetic ? SyntheticGeneralId : GeneralId, game.Revision,
            game.PendingDecision!.PromptId)));
        for (var i = 0; i < 128 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture did not reach Zhu Huan's play phase.");
        return (game, registry);
    }

    private static void ReachYoudi(GameEngine game)
    {
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var i = 0; i < 64 && game.PendingDecision?.SkillPrompt?.SkillId != SkillId; i++) Advance(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == SkillId,
            $"Fixture did not reach Youdi (pending={game.PendingDecision?.Kind}, turn={game.State.TurnNumber}).");
    }
    private static void RunPastAiDiscard(GameEngine game)
    {
        for (var i = 0; i < 24 &&
                (InternalPrompt(game)?.PlayerSeat == 1 ||
                 game.PendingDecision is null && game.ResolutionStack.OfType<ProgramSkillFrame>().Any());
             i++) Advance(game);
    }
    private static PendingDecision? InternalPrompt(GameEngine game) =>
        (PendingDecision?)typeof(GameEngine).GetField("_pendingDecision",
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(game);
    private static PendingDecision RequirePrompt(GameEngine game, int seat) =>
        (game.PendingDecision ?? InternalPrompt(game)) is { Kind: DecisionKind.ProgramTrigger } prompt &&
        prompt.PlayerSeat == seat
            ? prompt : throw new InvalidOperationException(
                $"Expected program prompt for seat {seat}, got {InternalPrompt(game)?.Kind}/{InternalPrompt(game)?.PlayerSeat}; " +
                $"status={game.State.Status}, turn={game.State.TurnNumber}, frames={string.Join(',', game.ResolutionStack.Select(frame => frame.Kind))}, " +
                $"moves={string.Join(';', game.CardMovements.TakeLast(4).Select(move => move.Reason.Value + ':' + move.CardKind + ':' + move.To))}.");
    private static void AnswerAction(GameEngine game, string action) =>
        Answer(game, game.PendingDecision!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == action));
    private static void AnswerTarget(GameEngine game, int seat) =>
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Targets.SequenceEqual([seat])));
    private static void Answer(GameEngine game, PromptChoice choice) =>
        Accept(game.Submit(new AnswerPromptCommand(game.PendingDecision!.PlayerSeat,
            game.PendingDecision.PromptId, choice.Id, game.Revision)));
    private static void Advance(GameEngine game) =>
        Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true));
    private static IReadOnlyList<string> Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static string Resource(string name)
    {
        using var stream = typeof(StandardClassicGeneralPackage).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Missing resource {name}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    private static SkillProgramCatalog SyntheticCatalog() =>
        SkillProgramCatalog.Load(
            Resource(RulesResource).Replace(SkillId, SyntheticSkillId, StringComparison.Ordinal)
                .Replace("\"kind\": \"not\"", "\"kind\": \"all\"", StringComparison.Ordinal),
            Resource(PresentationResource).Replace(SkillId, SyntheticSkillId, StringComparison.Ordinal));
    private static void Reject(string rules, string presentation, string fragment)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException error) when (error.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase))
        { return; }
        throw new InvalidOperationException($"Expected definition rejection containing '{fragment}'.");
    }
    private static void Accept(CommandResult result) =>
        Require(result.Accepted, result.Error?.Message ?? "Rejected command.");
    private static void Require(bool valid, string message)
    {
        if (!valid) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(string physicalCardId, int initialHand, int drawPerTurn,
        bool synthetic) : IGameContentPackage
    {
        public const string ModeId = "identity:classic-zhu-huan-check-4";
        public PackageManifest Manifest { get; } =
            new("zhu-huan-scenario", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (synthetic)
            {
                var catalog = SyntheticCatalog();
                var presentation = catalog.Presentations[SyntheticSkillId];
                builder.AddSkill(new ContentSkillDefinition(
                    SyntheticSkillId, presentation.Name, presentation.Description)
                {
                    Program = catalog.Programs[SyntheticSkillId],
                    ProgramPresentation = presentation
                });
                builder.AddGeneral(new ContentGeneralDefinition(
                    SyntheticGeneralId, "绑定牌种测试", "kind_test", SyntheticSkillId, "wu", BaseHp: 4));
            }
            var targets = new[] { "fixture:zhu-huan-1", "fixture:zhu-huan-2", "fixture:zhu-huan-3" };
            foreach (var id in targets)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    "standard:none", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:zhu-huan-deck", "朱桓测试牌堆",
                initialHand, drawPerTurn, [])
            {
                PhysicalCards = Enumerable.Range(0, 80)
                    .Select(index => new ContentDeckPhysicalCard(physicalCardId,
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(ModeId, "朱桓场景", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:zhu-huan-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [synthetic ? SyntheticGeneralId : GeneralId, .. targets]));
        }
    }
}
