using System.Text.Json;
using System.Text.Json.Nodes;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryCaoCaoChecks
{
    private const string General = "boundary:cao-cao";
    private const string Jianxiong = "boundary:jianxiong";
    private const string Mode = "identity:classic-boundary-cao-cao-check-5";

    public static void ContentAndSchemaBoundary()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        var general = registry.Generals[General];
        Require(general.Name == "界曹操" && general.FactionId == "wei" && general.BaseHp == 4 &&
                general.PortraitKey == "boundary_cao_cao" &&
                general.SkillIds.SequenceEqual([Jianxiong, "boundary:hujia"]) &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                registry.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2014 boundary Cao Cao must be an independent Wei 4-HP official identity general.");
        var trigger = registry.Skills[Jianxiong].Program!.Triggers.Single();
        Require(trigger.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                trigger.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage && trigger.Optional &&
                trigger.Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.ChooseOption, SkillProgramEffectOp.Draw,
                    SkillProgramEffectOp.ClaimDamageCards]) &&
                trigger.Effects[0].Options.Single(option => option.Id == "claim").Condition.Kind ==
                    SkillProgramConditionKind.HasClaimableDamageCards &&
                trigger.Effects[2].Condition.CanEvaluateWithoutProgramFrame() == false &&
                trigger.Effects[0].Condition.CanEvaluateWithoutProgramFrame() &&
                registry.Skills["boundary:hujia"].Id == "boundary:hujia" &&
                registry.Skills["boundary:hujia"].Program?.CardPolicies.Count > 0,
            "Jianxiong must choose draw or currently claimable damage cards once per hit; FactionDefense must reuse the lord kind.");
        var owner = new PlayerSkillContext(0, 3, 5, 2, TurnPhase.Play);
        var effects = trigger.Effects.ToArray();
        var cardlessAi = ProgramCompositionAi.Estimate(effects, owner,
            publicContext: new ProgramAiPublicContext(5, HasClaimableDamageCards: false));
        var claimableAi = ProgramCompositionAi.Estimate(effects, owner,
            publicContext: new ProgramAiPublicContext(5, HasClaimableDamageCards: true));
        Require(cardlessAi.Hint.OwnerDraw == 1 && claimableAi.Hint.OwnerDraw == 0 &&
                claimableAi.Score > 0,
            "Shared AI must distinguish only public claimability, without reading hidden hands.");

        var assembly = typeof(StandardClassicGeneralPackage).Assembly;
        string ReadEmbedded(string suffix)
        {
            using var stream = assembly.GetManifestResourceStream(
                "CardGame.Content.Standard.SkillPrograms.boundary-cao-cao." + suffix + ".json")!;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        var rules = ReadEmbedded("rules");
        var presentation = ReadEmbedded("presentation");
        var oldSchema = JsonNode.Parse(rules)!;
        oldSchema["schemaVersion"] = 53;
        Reject(oldSchema.ToJsonString(), presentation, "Schema 53 must reject hasClaimableDamageCards.");
        var wrongWindow = JsonNode.Parse(rules)!;
        wrongWindow["skills"]![0]!["triggers"]![0]!["window"] = "turnEnding";
        wrongWindow["skills"]![0]!["triggers"]![0]!.AsObject().Remove("damageOccurrence");
        Reject(wrongWindow.ToJsonString(), presentation, "Only afterDamageApplied may expose claimable damage cards.");
        var wrongTarget = JsonNode.Parse(rules)!;
        wrongTarget["skills"]![0]!["triggers"]![0]!["effects"]![0]!["target"] = "selectedTarget";
        Reject(wrongTarget.ToJsonString(), presentation, "Claimable damage cards must not leak through a non-owner choice.");
    }

    public static void PhysicalDamageDrawClaimAndReplay()
    {
        var registry = Registry();
        var game = FindOffer(registry, requireClaim: true);
        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var before = game.CreateSnapshot(0, true).Players[0].HandCount;
        var skipped = GameReplay.Restore(checkpoint, registry);
        Answer(skipped, "skip");
        Require(skipped.CreateSnapshot(0, true).Players[0].HandCount == before,
            "Declining Jianxiong must grant no card.");

        var drawn = GameReplay.Restore(checkpoint, registry);
        Answer(drawn, "activate");
        var choice = Prompt(drawn);
        Require(choice.IsPrivate && choice.Choices.Select(item => item.Parameters.GetValueOrDefault("option-id"))
                .Order(StringComparer.Ordinal).SequenceEqual(["claim", "draw"]),
            "An unclaimed physical damage card must offer both private options.");
        var drawPaused = RoundTrip(drawn.CreateCheckpoint());
        AnswerOption(drawn, "draw");
        Require(drawn.CreateSnapshot(0, true).Players[0].HandCount == before + 1 &&
                drawn.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                    .Any(item => item.SkillId == Jianxiong && item.OptionId == "draw"),
            "Draw branch must add exactly one card.");
        AssertReplay(drawn, registry);

        var claimed = GameReplay.Restore(drawPaused, registry);
        var physical = claimed.CreateCardZoneDiagnostics()
            .Where(item => item.Location == CardLocation.Processing).Select(item => item.CardId).ToHashSet();
        Require(physical.Count > 0, "Physical damage card must be in Processing while Jianxiong is pending.");
        AnswerOption(claimed, "claim");
        Require(claimed.Events.Select(item => item.Payload).OfType<ProgramDamageCardsClaimedEvent>()
                    .Any(item => item.SkillId == Jianxiong && item.CardIds.All(physical.Contains) && item.CardIds.Count > 0) &&
                claimed.CreateCardZoneDiagnostics().Any(item => physical.Contains(item.CardId) &&
                    item.Location == CardLocation.Hand(0)),
            "Claim branch must move this damage's physical card from Processing into Cao Cao's hand.");
        AssertReplay(claimed, registry);

        var preempted = GameReplay.Restore(drawPaused, registry);
        var store = (CardZoneStore)typeof(GameEngine)
            .GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(preempted)!;
        foreach (var card in store.CardsAt(CardLocation.Processing).ToArray())
            typeof(GameEngine).GetMethod("MoveCard", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(preempted, [card, CardLocation.Processing, CardLocation.Hand(1),
                    new CardMoveReason("fixture.earlier-claim")]);
        var stale = Prompt(preempted).Choices.Single(item =>
            item.Parameters.GetValueOrDefault("option-id") == "claim");
        var stableState = State(preempted);
        var stableEvents = Events(preempted);
        var staleResult = preempted.Submit(new AnswerPromptCommand(0, Prompt(preempted).PromptId,
            stale.Id, preempted.Revision));
        Require(!staleResult.Accepted && State(preempted) == stableState &&
                Events(preempted).SequenceEqual(stableEvents) &&
                Prompt(preempted).Choices.Any(item => item.Id == stale.Id),
            "A competing earlier claim must make the stale option reject atomically at submission.");
    }

    private static void Reject(string rules, string presentation, string message)
    {
        try { SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException(message);
    }

    private static ContentRegistry Registry(bool lightningOnly = false, bool hujiaDeck = false) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario(lightningOnly, hujiaDeck));

    private static GameEngine Start(ContentRegistry registry, int seed, Role role = Role.Lord)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, ModeId = Mode, HumanSeat = 0,
            HumanRole = role, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Boundary Cao Cao fixture failed to start.");
        var setup = Prompt(game);
        Require(setup.ValidContentIds.Contains(General) &&
                game.Submit(new SelectGeneralCommand(0, General, game.Revision, setup.PromptId)).Accepted,
            "Boundary Cao Cao was not selectable.");
        return game;
    }

    private static GameEngine FindOffer(ContentRegistry registry, bool requireClaim)
    {
        string? firstFailure = null;
        for (var seed = 1; seed <= 128; seed++)
        {
            var game = Start(registry, seed);
            for (var step = 0; step < 1200 && game.State.Winner == Winner.None; step++)
            {
                if (game.PendingDecision?.SkillPrompt?.SkillId == Jianxiong)
                {
                    if (!requireClaim) return game;
                    var probe = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                    Answer(probe, "activate");
                    if (Prompt(probe).Choices.Any(item => item.Parameters.GetValueOrDefault("option-id") == "claim"))
                        return game;
                }
                try { if (!Step(game)) break; }
                catch (Exception error)
                {
                    firstFailure ??= $"seed={seed}, step={step}, prompt={game.PendingDecision?.Kind}: {error}";
                    break;
                }
            }
        }
        throw new InvalidOperationException("No bounded real-damage boundary Cao Cao offer was found. " + firstFailure);
    }

    private static bool Step(GameEngine game)
    {
        var prompt = game.PendingDecision;
        if (prompt is null || prompt.PlayerSeat != 0)
            return game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted;
        if (prompt.Kind == DecisionKind.PlayCard)
            return game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)).Accepted;
        if (prompt.Kind == DecisionKind.DiscardCards)
            return game.Submit(new DiscardCardsCommand(0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision)).Accepted;
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "skip" ||
            item.Parameters.GetValueOrDefault("response") == "take-damage" ||
            item.Cards.Count == 0) ?? prompt.Choices.LastOrDefault();
        return choice is not null && game.Submit(new AnswerPromptCommand(0, prompt.PromptId,
            choice.Id, game.Revision)).Accepted;
    }

    private static void Answer(GameEngine game, string action)
    {
        var prompt = Prompt(game);
        var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("program-action") == action);
        Require(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision)).Accepted,
            $"Jianxiong {action} failed.");
    }

    private static void AnswerOption(GameEngine game, string option)
    {
        var prompt = Prompt(game);
        var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("option-id") == option);
        var result = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"Jianxiong option {option} failed.");
    }

    private static PendingDecision Prompt(GameEngine game) => game.PendingDecision ??
        throw new InvalidOperationException("Boundary Cao Cao lost the expected prompt.");
    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void AssertReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            "Jianxiong checkpoint/replay state or events diverged.");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(bool lightningOnly, bool hujiaDeck) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("boundary-cao-cao-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddDeck(new ContentDeckRecipe("fixture:boundary-cao-cao-deck", "Cao Cao Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(lightningOnly ? "standard:lightning" :
                        hujiaDeck ? index % 4 == 0 ? "standard:dodge" : "standard:slash" :
                        index % 9 == 0 ? "standard:duel" : "standard:slash",
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition(Mode, "2014 Cao Cao", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:boundary-cao-cao-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General, "classic:xiahou-dun", "classic:guan-yu",
                    "classic:zhang-fei", "classic:sun-quan"]));
        }
    }

    private sealed class FrameGateScenario(SkillProgram program, SkillPresentation presentation) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("bound-claim-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new ContentSkillDefinition("fixture:bound-claim", "绑定领取", "通用伤害后绑定验证")
            {
                Program = program, ProgramPresentation = presentation,
                ExecutionForms = SkillExecutionForm.Trigger, ActionForms = SkillActionForm.None
            });
            builder.AddGeneral(new ContentGeneralDefinition("fixture:bound-claim-owner", "绑定领取者",
                "supporter", "fixture:bound-claim", "wei", BaseHp: 4));
            builder.AddDeck(new ContentDeckRecipe("fixture:bound-claim-deck", "Claim Deck", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard("standard:slash", (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            builder.AddMode(new ContentModeDefinition("identity:classic-bound-claim-check-5", "Bound Claim", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                }, "fixture:bound-claim-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: ["fixture:bound-claim-owner", "classic:xiahou-dun", "classic:guan-yu",
                    "classic:zhang-fei", "classic:sun-quan"]));
        }
    }
}
