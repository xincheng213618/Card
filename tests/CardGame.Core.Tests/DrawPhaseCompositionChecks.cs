using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DrawPhaseCompositionChecks
{
    private const int Human = 0;

    public static void AdditiveAdjustmentDrawAndDamageGrantComposeAndReplay()
    {
        var registry = Registry();
        var skipped = Start(registry, "fixture:draw-a");
        ReachPrompt(skipped, "fixture:draw-a");
        var beforeSkip = Hand(skipped);
        Require(skipped.CreateSnapshot(1).PendingDecision is null, "Draw-phase activation must remain private.");
        Answer(skipped, "skip");
        Require(Hand(skipped) == beforeSkip + 2 && skipped.State.Phase == TurnPhase.Play,
            "Skipping additive composition must perform exactly one ordinary two-card draw.");

        var used = Start(registry, "fixture:draw-a");
        ReachPrompt(used, "fixture:draw-a");
        var before = Hand(used);
        var copy = GameReplay.Restore(RoundTrip(used.CreateCheckpoint()), registry);
        Answer(used, "activate"); Answer(copy, "activate");
        var grant = used.Events.Select(e => e.Payload).OfType<CardDamageModifierGrantedEvent>().Single();
        Require(Hand(used) == before + 2 &&
                used.CardMovements.Count(m => m.Reason.Value == "skill-program.fixture:draw-a.Draw") == 1 &&
                grant.Modifier.Amount == 1 && grant.Modifier.CardKinds.Order().SequenceEqual(
                    new[] { CardKind.Slash, CardKind.Duel }.Order()) &&
                State(used) == State(copy) && Events(used).SequenceEqual(Events(copy)),
            "Additive draw, adjusted normal draw, damage grant and replay must each resolve once.");
        if (used.PendingDecision is null)
            Require(used.Submit(new AdvanceCommand(used.Revision)).Accepted,
                "Could not advance the composed program to its play prompt.");
        var play = used.PendingDecision!;
        Require(used.Submit(new EndPlayPhaseCommand(Human, used.Revision, play.PromptId)).Accepted,
            "Could not end the composed turn.");
        for (var step = 0; step < 16 && used.Events.All(e => e.Payload is not TurnCardUseEffectsExpiredEvent); step++)
            Require(used.Submit(new AdvanceOneStepCommand(used.Revision)).Accepted, "Could not reach turn-effect expiry.");
        Require(used.Events.Select(e => e.Payload).OfType<TurnCardUseEffectsExpiredEvent>()
                .Any(e => e.GrantSequences.Contains(grant.Modifier.GrantSequence)),
            "The composed damage modifier must expire with its granting turn.");
    }

    public static void RevealPartitionRecoveryAndExtraDrawConserveCardsAndReplay()
    {
        var registry = Registry();
        var game = Start(registry, "fixture:draw-b");
        ReachPrompt(game, "fixture:draw-b");
        var beforeHand = Hand(game);
        var beforePlayer = game.CreateSnapshot(Human, true).Players[Human];
        Require(beforePlayer.Hp < beforePlayer.MaxHp,
            "The replacement fixture must enter its draw phase wounded through its lifecycle program.");
        var beforeZones = game.CardMovements.Count;
        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Answer(game, "activate"); Answer(replay, "activate");
        var revealed = game.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>()
            .Single(e => e.SkillId == "fixture:draw-b");
        var hearts = revealed.Cards.Where(c => c.Suit == Suit.Heart).Select(c => c.Id).ToHashSet();
        var nonHearts = revealed.Cards.Where(c => c.Suit != Suit.Heart).Select(c => c.Id).ToHashSet();
        var movements = game.CardMovements.Skip(beforeZones).ToArray();
        var afterPlayer = game.CreateSnapshot(Human, true).Players[Human];
        Require(revealed.Cards.Count == 4 && hearts.Count > 0 && nonHearts.Count > 0 &&
                game.CreateSnapshot(1).PublicRevealedCards.Count == 0 &&
                game.ResolutionStack.Count == 0 && game.State.Phase == TurnPhase.Play &&
                hearts.All(id => movements.Count(m => m.CardId == id && m.To == CardLocation.Hand(Human)) == 1) &&
                nonHearts.All(id => movements.Count(m => m.CardId == id && m.To == CardLocation.DiscardPile) == 1) &&
                movements.Count(m => revealed.Cards.Any(c => c.Id == m.CardId) &&
                    (m.To == CardLocation.Hand(Human) || m.To == CardLocation.DiscardPile)) == 4 &&
                movements.Count(m => m.Reason.Value == "skill-program.fixture:draw-b.Draw") == 1 &&
                Hand(game) == beforeHand + 1 + hearts.Count &&
                afterPlayer.Hp == Math.Min(beforePlayer.MaxHp, beforePlayer.Hp + hearts.Count) &&
                State(game) == State(replay) && Events(game).SequenceEqual(Events(replay)),
            $"Replacement must explicitly partition all revealed cards, draw once, clean public reveal state and replay " +
            $"(revealed={revealed.Cards.Count}, hearts={hearts.Count}, nonHearts={nonHearts.Count}, " +
            $"suits={string.Join(',', revealed.Cards.Select(card => card.Suit))}, " +
            $"phase={game.State.Phase}, frames={game.ResolutionStack.Count}, handDelta={Hand(game) - beforeHand}, " +
            $"expectedHandDelta={1 + hearts.Count}, hp={afterPlayer.Hp}, expectedHp=" +
            $"{Math.Min(beforePlayer.MaxHp, beforePlayer.Hp + hearts.Count)}, stateMatch={State(game) == State(replay)}, " +
            $"eventsMatch={Events(game).SequenceEqual(Events(replay))}).");

        var skipped = Start(registry, "fixture:draw-b"); ReachPrompt(skipped, "fixture:draw-b");
        var hand = Hand(skipped); Answer(skipped, "skip");
        Require(Hand(skipped) == hand + 2 && skipped.Events.All(e => e.Payload is not ProgramCardsRevealedEvent),
            "Skipping reveal replacement must perform only the ordinary draw.");
    }


    public static void ValidatorRejectsUnsafeGraphs()
    {
        Reject("missing", """[{"op":"filterBoundCards","target":"owner","sourceBind":"missing","resultBind":"x","suits":["heart"]}]""");
        Reject("resource operations must be always", """[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public","condition":{"kind":"wounded"}},{"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"discardPile"}]""");
        Reject("already have moved", """[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"r","destination":"discardPile"},{"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"x","suits":["heart"]}]""");
        Reject("cards may be moved more than once", """[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public"},{"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"h","suits":["heart"]},{"op":"moveBoundCards","target":"owner","sourceBind":"h","destination":"ownerHand"},{"op":"moveBoundCards","target":"owner","sourceBind":"h","destination":"discardPile"},{"op":"moveBoundCards","target":"owner","sourceBind":"r","exceptBind":"h","destination":"discardPile"}]""");
        Reject("not fully consumed", """[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"r","visibility":"public"},{"op":"filterBoundCards","target":"owner","sourceBind":"r","resultBind":"h","suits":["heart"]},{"op":"moveBoundCards","target":"owner","sourceBind":"h","destination":"ownerHand"}]""");
        Reject("same source root", """[{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"a","visibility":"public"},{"op":"revealTopCards","target":"owner","amount":2,"resultBind":"b","visibility":"public"},{"op":"moveBoundCards","target":"owner","sourceBind":"a","exceptBind":"b","destination":"discardPile"},{"op":"moveBoundCards","target":"owner","sourceBind":"a","destination":"ownerHand"},{"op":"moveBoundCards","target":"owner","sourceBind":"b","destination":"discardPile"}]""");
        Reject("replacement draw program cannot adjust", """[{"op":"adjustNormalDraw","target":"owner","amount":-1}]""");
        Reject("only once", """[{"op":"selectTargets","target":"owner","targetKind":"otherLivingWithHand","minimumTargets":1,"maximumTargets":2,"targetAiOrder":"hostileThenHandCount"},{"op":"takeRandomHandCardFromSelectedTargets","target":"owner","amount":1},{"op":"draw","target":"owner","amount":1},{"op":"takeRandomHandCardFromSelectedTargets","target":"owner","amount":1}]""");
        RejectRules("requires context DrawPlan", """{"schemaVersion":62,"skills":[{"id":"fixture:invalid","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"cardIdentities":[],"triggers":[{"id":"x","window":"playEnding","subject":"owner","optional":true,"priority":0,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-1}]}]}]}""");
    }

    private static void Reject(string expected, string effects)
    {
        var rules = $$"""{"schemaVersion":62,"skills":[{"id":"fixture:invalid","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"cardIdentities":[],"triggers":[{"id":"x","window":"drawPhaseStarting","subject":"owner","optional":true,"priority":0,"drawPhaseMode":"replacement","effects":{{effects}}}]}]}""";
        RejectRules(expected, rules);
    }

    private static void RejectRules(string expected, string rules)
    {
        try { _ = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:invalid":{"name":"Invalid","description":"Fixture"}}}"""); }
        catch (InvalidOperationException e) when (e.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected validation failure containing '{expected}'.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(new StandardContentPackage(), new Package());
    private static GameEngine Start(ContentRegistry registry, string skill)
    {
        var seed = skill == "fixture:draw-b" ? 1 : 922;
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, PlayerCount = 5, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Package.Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture start failed.");
        var p = game.PendingDecision!; Require(p.ValidContentIds.Contains("fixture:owner-" + skill[^1]), "Owner not offered.");
        Require(game.Submit(new SelectGeneralCommand(0, "fixture:owner-" + skill[^1], game.Revision, p.PromptId)).Accepted, "Selection failed.");
        return game;
    }
    private static void ReachPrompt(GameEngine game, string skill)
    {
        var r = game.Submit(new AdvanceCommand(game.Revision));
        Require(r.Accepted && game.PendingDecision?.SkillPrompt?.SkillId == skill, r.Error?.Message ?? "Prompt not reached.");
    }
    private static PendingDecision RequirePrompt(GameEngine game, string skill) =>
        game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: var id } p && id == skill
            ? p : throw new InvalidOperationException($"Expected {skill} prompt.");
    private static void Answer(GameEngine game, string action) => Submit(game,
        game.PendingDecision!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == action));
    private static void Submit(GameEngine game, PromptChoice choice)
    {
        var p = game.PendingDecision!;
        var r = game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice.Id, game.Revision));
        Require(r.Accepted, r.Error?.Message ?? "Answer rejected.");
    }
    private static int Hand(GameEngine g) => g.CreateSnapshot(0, true).Players[0].HandCount;
    private static GameCheckpoint RoundTrip(GameCheckpoint c) => GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(c));
    private static string State(GameEngine g) => SnapshotJson.Serialize(g.CreateSnapshot(0, true));
    private static string[] Events(GameEngine g) => g.Events.Select(e => $"{e.Sequence}|{e.Payload.GetType().Name}|{JsonSerializer.Serialize(e.Payload, e.Payload.GetType())}").ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Package : IGameContentPackage
    {
        public const string Mode = "identity:draw-composition-5";
        public PackageManifest Manifest { get; } = new("draw-composition", new Version(1,0,0));
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var id in new[] { "fixture:draw-a", "fixture:draw-b", "fixture:draw-c" })
                b.AddSkill(new(id, id, "Fixture") { Program = catalog.Programs[id], ExecutionForms = SkillExecutionForm.Trigger });
            b.AddGeneral(new("fixture:owner-a", "A", "supporter", "fixture:draw-a", "wei", BaseHp:4));
            b.AddGeneral(new("fixture:owner-b", "B", "supporter", "fixture:draw-b", "wei", BaseHp:4));
            b.AddGeneral(new("fixture:owner-c", "C", "supporter", "fixture:draw-c", "wei", BaseHp:4));
            for (var i=0;i<2;i++) b.AddGeneral(new($"fixture:target-{i}", "Target", "supporter", "standard:none", "shu", BaseHp:6));
            b.AddDeck(new("fixture:deck", "Deck", 4, 2, []) { PhysicalCards = Enumerable.Range(0,160).Select(i => new ContentDeckPhysicalCard(i%3==0?"standard:peach":"standard:slash",(Suit)(i%4),i%13+1)).ToArray() });
            b.AddMode(new(Mode,"Mode",5,5,new Dictionary<string,int>{{nameof(Role.Lord),1},{nameof(Role.Loyalist),1},{nameof(Role.Rebel),2},{nameof(Role.Renegade),1}},"fixture:deck",GeneralCandidateCount:5,GeneralPoolIds:["fixture:owner-a","fixture:owner-b","fixture:owner-c","fixture:target-0","fixture:target-1"]));
        }
    }

    private const string Rules = """
    {"schemaVersion":62,"skills":[
    {"id":"fixture:draw-a","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"cardIdentities":[],"triggers":[{"id":"a","window":"drawPhaseStarting","subject":"owner","optional":true,"priority":0,"drawPhaseMode":"additive","effects":[{"op":"draw","target":"owner","amount":1},{"op":"grantTurnCardDamageModifier","target":"owner","amount":1,"cardKinds":["slash","duel"]},{"op":"adjustNormalDraw","target":"owner","amount":-1}]}]},
    {"id":"fixture:draw-b","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"cardIdentities":[],"triggers":[{"id":"wound","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"priority":0,"effects":[{"op":"loseHp","target":"owner","amount":2}]},{"id":"b","window":"drawPhaseStarting","subject":"owner","optional":true,"priority":0,"drawPhaseMode":"replacement","effects":[{"op":"draw","target":"owner","amount":1},{"op":"revealTopCards","target":"owner","amount":4,"resultBind":"revealed","visibility":"public"},{"op":"filterBoundCards","target":"owner","sourceBind":"revealed","resultBind":"hearts","suits":["heart"]},{"op":"moveBoundCards","target":"owner","sourceBind":"hearts","destination":"ownerHand"},{"op":"moveBoundCards","target":"owner","sourceBind":"revealed","exceptBind":"hearts","destination":"discardPile"},{"op":"recover","target":"owner","numberExpression":"boundCardCount","sourceBind":"hearts"}]}]},
    {"id":"fixture:draw-c","revision":1,"minimumRulesVersion": 171,"modifiers":[],"viewAs":[],"activations":[],"contributions":[],"cardIdentities":[],"triggers":[{"id":"c","window":"drawPhaseStarting","subject":"owner","optional":true,"priority":0,"drawPhaseMode":"replacement","effects":[{"op":"draw","target":"owner","amount":1},{"op":"selectTargets","target":"owner","targetKind":"otherLivingWithHand","minimumTargets":1,"maximumTargets":2,"targetAiOrder":"hostileThenHandCount"},{"op":"takeRandomHandCardFromSelectedTargets","target":"owner","amount":1}]}]}]}
    """;
    private const string Presentation = """{"schemaVersion":3,"skills":{"fixture:draw-a":{"name":"A","description":"Fixture"},"fixture:draw-b":{"name":"B","description":"Fixture"},"fixture:draw-c":{"name":"C","description":"Fixture"}}}""";
}
