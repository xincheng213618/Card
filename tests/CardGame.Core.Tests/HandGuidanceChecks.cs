using CardGame.Content.Standard;
using CardGame.Core;

internal static class HandGuidanceChecks
{
    public static void ReadOnlyAndPrivate()
    {
        var seen = new HashSet<HandGuidanceReason>();
        foreach (var seed in Enumerable.Range(1, 48))
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, AiPolicyVersion = 2, AdvanceAfterHumanCommands = false }, StandardContentRegistry.Create());
            Require(game.Submit(new StartGameCommand()).Accepted, "Game did not start.");
            var state = SnapshotJson.Serialize(game.CreateSnapshot(0, true));
            var checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            var eventCount = game.Events.Count;
            var ownHand = game.State.Players.Single(player => player.IsHuman).Hand.Select(card => card.Id).Order().ToArray();
            var playable = game.GetHumanLegalActions().Where(action => action.CardId is not null).Select(action => action.CardId!.Value).ToHashSet();
            var hints = game.GetHumanHandGuidance();
            Require(hints.Select(hint => hint.CardId).Order().SequenceEqual(ownHand), "Guidance must contain exactly the local human's hand.");
            Require(hints.All(hint => hint.CanPlay == playable.Contains(hint.CardId)), "Guidance disagrees with published legal actions.");
            foreach (var hint in hints) { seen.Add(hint.Reason); Require(!string.IsNullOrWhiteSpace(hint.Message), "A hand card has no explanation."); }
            for (var read = 0; read < 10; read++) Require(game.GetHumanHandGuidance().SequenceEqual(hints), "Repeated reads changed guidance.");
            Require(game.Events.Count == eventCount && GameCheckpointJson.Serialize(game.CreateCheckpoint()) == checkpoint && SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == state, "A guidance query mutated the game.");
        }
        Require(new[] { HandGuidanceReason.Playable, HandGuidanceReason.ConversionOnly, HandGuidanceReason.HealthFull, HandGuidanceReason.ResponseOnly }.All(seen.Contains), "Fixtures missed common hand restrictions or skill conversions.");
        var aiOnly = GameEngine.CreateStandard(new GameOptions { HumanSeat = -1, HumanRole = null }, StandardContentRegistry.Create());
        Require(aiOnly.GetHumanHandGuidance().Count == 0, "Human guidance must not expose an AI hand.");
    }

    public static void AfterUsingCards()
    {
        var standard = StandardContentRegistry.Create();
        string Id(CardKind kind) => standard.Cards.Values.Single(card => card.LegacyKind == kind).Id;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new SyntheticPackage("guidance", builder =>
            builder.AddDeck(new ContentDeckRecipe("guidance:deck", "手牌限制说明场景", 6, 2,
                [new(Id(CardKind.Alcohol), 12), new(Id(CardKind.Slash), 20), new(Id(CardKind.Dodge), 10), new(Id(CardKind.Peach), 10)]))));
        GameEngine? selected = null;
        for (var seed = 1; seed <= 128 && selected is null; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5, AiPolicyVersion = 2, DeckId = "guidance:deck", AdvanceAfterHumanCommands = false }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Controlled game failed.");
            var human = game.State.Players.Single(player => player.IsHuman);
            if (human.GeneralId is { } generalId &&
                !registry.Generals[generalId].SkillIds.Contains("standard:paoxiao", StringComparer.Ordinal) &&
                human.Hand.Count(card => card.Kind == CardKind.Alcohol) >= 2 &&
                human.Hand.Count(card => card.Kind == CardKind.Slash) >= 2) selected = game;
        }
        Require(selected is not null, "Controlled hand was not found.");
        var match = selected!;
        Play(match, match.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Alcohol));
        ResolveToPlay(match);
        Require(match.GetHumanHandGuidance().Any(hint => hint.Reason == HandGuidanceReason.AlcoholAlreadyActive && !hint.CanPlay), "Unused second wine needs an active-effect explanation.");
        Play(match, match.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash));
        ResolveToPlay(match);
        Require(match.GetHumanHandGuidance().Any(hint => hint.Reason == HandGuidanceReason.SlashLimitReached && !hint.CanPlay), "Second Slash needs a turn-limit explanation.");
        var replay = GameReplay.Restore(match.CreateCheckpoint(), registry);
        Require(replay.GetHumanHandGuidance().SequenceEqual(match.GetHumanHandGuidance()), "Guidance changed after checkpoint restore.");
    }

    public static void PendingDecisions()
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 721019, HumanSeat = 0, HumanRole = Role.Lord, UseInteractiveSetup = true, AiPolicyVersion = 2, AdvanceAfterHumanCommands = false }, StandardContentRegistry.Create());
        Require(game.Submit(new StartGameCommand()).Accepted, "Interactive game failed.");
        var sawResponse = false;
        var sawDiscard = false;
        for (var step = 0; step < 6000 && game.State.Status != EngineStatus.Completed && !(sawResponse && sawDiscard); step++)
        {
            var prompt = game.PendingDecision;
            var hints = game.GetHumanHandGuidance();
            if (prompt?.Kind == DecisionKind.DiscardCards)
            {
                sawDiscard = true;
                Require(hints.Count > 0 && hints.All(hint => hint.Reason == HandGuidanceReason.SelectDiscard && !hint.CanPlay), "Discard guidance must describe selection, not playing a card.");
            }
            else if (prompt is not null && prompt.Kind is not (DecisionKind.PlayCard or DecisionKind.SelectGeneral) && hints.Count > 0)
            {
                sawResponse = true;
                Require(hints.All(hint => hint.Reason == HandGuidanceReason.ResolveCurrentPrompt && !hint.CanPlay), "Private response must direct the player to its published choices.");
            }
            else if (prompt is null) Require(hints.All(hint => hint.Reason == HandGuidanceReason.WaitingForTurn), "Another player's prompt must not leak into human guidance.");
            GameCommand command = prompt?.Kind switch
            {
                null => new AdvanceOneStepCommand(game.Revision),
                DecisionKind.SelectGeneral => new SelectGeneralCommand(0, prompt.ValidContentIds[0], game.Revision, prompt.PromptId),
                DecisionKind.PlayCard => new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                DecisionKind.DiscardCards => new DiscardCardsCommand(0, prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(), prompt.PromptId, game.Revision),
                _ => new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision)
            };
            Require(game.Submit(command).Accepted, "Actual response walkthrough failed.");
        }
        Require(sawResponse && sawDiscard, "Walkthrough missed a real discard or response boundary.");
    }

    private static void Play(GameEngine game, LegalAction action) => Require(game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)).Accepted, "Legal card was rejected.");
    private static void ResolveToPlay(GameEngine game)
    {
        for (var step = 0; step < 256 && game.PendingDecision?.Kind != DecisionKind.PlayCard && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            GameCommand command = prompt is null ? new AdvanceOneStepCommand(game.Revision) : new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision);
            Require(game.Submit(command).Accepted, "Resolution did not continue.");
        }
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Fixture did not return to human play.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
