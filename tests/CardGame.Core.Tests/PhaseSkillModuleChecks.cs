using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PhaseSkillModuleChecks
{
    public static void IndependentModulesSharePromptReplayAndContinuation()
    {
        var registry = Registry();
        var game = Create(registry);
        var play = game.PendingDecision!;
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
        var first = Prompt(game, "fixture:module-a");
        Require(first.Kind == DecisionKind.SkillModule, "New content must not require a legacy decision enum.");
        Require(game.CreateSnapshot(1).PendingDecision is null, "Another player must not see a private module prompt.");
        var revision = game.Revision;
        Require(!game.Submit(new AnswerPromptCommand(1, first.PromptId, first.Choices[0].Id, revision)).Accepted,
            "A non-owner cannot answer the module prompt.");
        Require(!game.Submit(new AnswerPromptCommand(0, first.PromptId, new ChoiceId("forged"), revision)).Accepted &&
                game.Revision == revision, "A forged choice must not change revision or consume the activation.");

        var checkpoint = GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint()));
        var restored = GameReplay.Restore(checkpoint, registry);
        Answer(game, activate: false);
        Answer(restored, activate: false);
        var second = Prompt(game, "fixture:module-b");
        Require(!game.Submit(new AnswerPromptCommand(0, first.PromptId, first.Choices[0].Id, game.Revision)).Accepted,
            "A stale first prompt cannot answer the second activation.");
        var hand = game.CreateSnapshot(0).Players[0].HandCount;
        Answer(game, activate: true);
        Answer(restored, activate: true);
        Require(game.PendingDecision is null && game.State.Phase == TurnPhase.Discard &&
                game.CreateSnapshot(0).Players[0].HandCount == hand + 2,
            "The second module must draw exactly once and resume the original phase boundary.");
        var results = game.Events.Select(item => item.Payload).OfType<SkillModuleResolvedEvent>().ToArray();
        Require(results.Length == 2 && !results[0].Used && results[1].Used,
            "Skip and use must each finish one distinct skill invocation.");
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) &&
                Events(game).SequenceEqual(Events(restored)), "Paused module replay must preserve state and event order.");
    }

    public static void AiUsesTheSameModuleDecisions()
    {
        var game = Create(Registry());
        var play = game.PendingDecision!;
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, play.PromptId)));
        Answer(game, activate: false);
        Answer(game, activate: false);
        for (var step = 0; step < 256; step++)
        {
            var results = game.Events.Select(item => item.Payload).OfType<SkillModuleResolvedEvent>()
                .Where(item => item.OwnerSeat != 0).ToArray();
            if (results.Length >= 2)
            {
                Require(results[0] is { SkillId: "fixture:module-a", Used: false } &&
                        results[1] is { SkillId: "fixture:module-b", Used: true },
                    "AI must consume the same published skip/use options without per-skill AI branches.");
                return;
            }
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The fixed module scenario never reached an AI activation.");
    }

    public static void ModuleBindingParticipatesInFingerprint()
    {
        var first = Registry(revision: 1);
        var second = Registry(revision: 2);
        Require(first.ContentHash != second.ContentHash, "A module semantics revision must change the content fingerprint.");
        var game = Create(first);
        try { GameReplay.Restore(game.CreateCheckpoint(), second); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("A save cannot silently change its module implementation revision.");
    }

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();

    private static ContentRegistry Registry(int revision = 1) => ContentRegistry.Build(
        new StandardContentPackage(), new ScenarioPackage(revision));

    private static GameEngine Create(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 670021, PlayerCount = 6, ModeId = ScenarioPackage.ModeId,
            HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20
        }, registry);
        Accept(game.Submit(new StartGameCommand()));
        var prompt = game.PendingDecision!;
        Accept(game.Submit(new SelectGeneralCommand(0, prompt.ValidContentIds[0], game.Revision, prompt.PromptId)));
        for (var step = 0; step < 32; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return game;
            Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
        }
        throw new InvalidOperationException("The fixed module scenario did not reach Play.");
    }

    private static PendingDecision Prompt(GameEngine game, string skillId) =>
        game.PendingDecision is { SkillPrompt: { } presentation } prompt && presentation.SkillId == skillId
            ? prompt : throw new InvalidOperationException($"Expected module prompt {skillId}.");

    private static void Answer(GameEngine game, bool activate)
    {
        var prompt = game.PendingDecision!;
        Accept(game.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[activate ? 0 : 1].Id, game.Revision)));
    }

    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Command rejected.");
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class DrawModule(string id, int count, bool preferUse, int revision) : IPhaseSkillModule
    {
        public string SkillId => id;
        public int Revision => revision;
        public PhaseSkillWindow Window => PhaseSkillWindow.PlayEnding;
        public SkillActivationPlan CreatePlan(PhaseSkillContext context) => new(
            new(id, id, "Optional draw", "Use or skip"), "Optional draw",
            new(new ChoiceId(id + ".use"), "Use", [], [], new Dictionary<string, string>()),
            new(new ChoiceId(id + ".skip"), "Skip", [], [], new Dictionary<string, string>()),
            [new DrawSkillCards(count, new CardMoveReason("fixture.module.draw"))], preferUse);
    }

    private sealed class ScenarioPackage(int revision) : IGameContentPackage
    {
        public const string ModeId = "fixture:phase-modules";
        public PackageManifest Manifest { get; } = new("phase-module-scenario", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(new("fixture:module-a", "Module A", "Optional draw")
            { PhaseSkill = new DrawModule("fixture:module-a", 1, false, revision) });
            builder.AddSkill(new("fixture:module-b", "Module B", "Optional draw")
            { PhaseSkill = new DrawModule("fixture:module-b", 2, true, revision) });
            var ids = Enumerable.Range(0, 6).Select(index => $"fixture:module-general-{index}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new(id, "Module General", "supporter", "fixture:module-a", "wei",
                    AdditionalSkillIds: ["fixture:module-b"]));
            builder.AddDeck(new("fixture:module-deck", "Fixed module deck", 4, 2,
                [new ContentDeckCardCount("standard:crossbow", 96)]));
            builder.AddMode(new(ModeId, "Module scenario", 6, 6,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 3, [nameof(Role.Renegade)] = 1
                }, "fixture:module-deck", GeneralCandidateCount: 6, GeneralPoolIds: ids));
        }
    }
}
