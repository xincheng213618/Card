using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2011CommandChecks
{
    public static void AssistedSlashAndTurnImmunitySurviveCheckpoints()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "fixture:command", UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Start failed.");
        for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        Require(game.Submit(new UseProgramSkillCommand(0, "classic:mingce", "offer-command", [], [1], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Mingce activation failed.");
        for (var i = 0; i < 25 && game.PendingDecision?.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option")?.StartsWith("target-", StringComparison.Ordinal) == true) != true; i++) Advance(game);
        Require(game.PendingDecision?.PlayerSeat == 0, "Skill owner must choose target.");
        Require(game.PendingDecision!.Choices.Any(c => c.Parameters.GetValueOrDefault("offer-option")?.StartsWith("target-", StringComparison.Ordinal) == true), "Owner target prompt was not reached.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        var target = game.PendingDecision!.Choices.First(c => c.Parameters.GetValueOrDefault("offer-option")?.StartsWith("target-", StringComparison.Ordinal) == true);
        Require(game.Submit(new AnswerPromptCommand(0, game.PendingDecision.PromptId, target.Id, game.Revision)).Accepted, "Owner target answer failed.");
        Require(restored.Submit(new AnswerPromptCommand(0, restored.PendingDecision!.PromptId, target.Id, restored.Revision)).Accepted, "Restored owner answer failed.");
        for (var i = 0; i < 80 && game.ResolutionStack.Count != 0; i++) Advance(game);
        for (var i = 0; i < 80 && restored.ResolutionStack.Count != 0; i++) Advance(restored);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(restored.CreateSnapshot(0, true)), "Assisted Slash checkpoint continuation differs.");
        Require(game.Events.Any(e => e.Payload is CardUseDeclaredEvent declared && declared.SourceSeat == 1), "Recipient must be card user.");
        Require(game.Events.Any(e => e.Payload is DamageAppliedEvent), "Virtual Slash must resolve damage.");
        Require(!game.Events.Any(e => e.Payload is TurnRuleModifierGrantedEvent granted && granted.Modifier.Query == SkillRuleQuery.CardEffectImmunity && granted.Modifier.Source.OwnerSeat == 0), "Own-turn damage must not grant immunity.");
        Require(!game.Submit(new UseProgramSkillCommand(0, "classic:mingce", "offer-command", [], [1], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Mingce must be once per phase.");
        Require(game.Submit(new UseProgramSkillCommand(0, "fixture:command-driver", "damage", [], [1], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Damage driver failed.");
        for (var i = 0; i < 80 && game.ResolutionStack.Count != 0; i++) Advance(game);
        Require(game.Events.Any(e => e.Payload is TurnRuleModifierGrantedEvent granted && granted.Modifier.Query == SkillRuleQuery.CardEffectImmunity), "Outside-turn damage must grant immunity.");
        var immune = GameReplay.Restore(game.CreateCheckpoint(), registry);
        foreach (var current in new[] { game, immune })
        {
            Require(current.Submit(new UseProgramSkillCommand(0, "fixture:command-driver", "offer", [], [0], current.Revision, current.PendingDecision!.PromptId)).Accepted, "Offer driver failed.");
            for (var i = 0; i < 80 && current.ResolutionStack.Count != 0; i++) Advance(current);
            Require(current.Events.Any(e => e.Payload is CardEffectSkippedEvent skipped && skipped.TargetSeat == 1), "Slash effect must be skipped by immunity.");
        }
        for (var i = 0; i < 20 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        var beforeDraw = game.CreateSnapshot(0, true).Players[0].Hand.Count;
        Require(game.Submit(new UseProgramSkillCommand(0, "fixture:command-driver", "no-target", [], [0], game.Revision, game.PendingDecision!.PromptId)).Accepted, "No-target activation failed.");
        for (var i = 0; i < 80 && game.ResolutionStack.Count != 0; i++) Advance(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == beforeDraw + 1, "No legal Slash target must draw automatically.");
        for (var i = 0; i < 20 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        var immunityGrants = game.Events.Select(e => e.Payload).OfType<TurnRuleModifierGrantedEvent>().Where(e => e.Modifier.Query == SkillRuleQuery.CardEffectImmunity).Select(e => e.Modifier.GrantSequence).ToArray();
        Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)).Accepted, "End play failed.");
        for (var i = 0; i < 80 && !game.Events.Any(e => e.Payload is TurnCardUseEffectsExpiredEvent expired && immunityGrants.All(expired.GrantSequences.Contains)); i++) Advance(game);
        Require(game.Events.Any(e => e.Payload is TurnCardUseEffectsExpiredEvent expired && immunityGrants.All(expired.GrantSequences.Contains)), "Immunity must expire at the same turn's end.");
        AssertCommandReplay(game, registry);
        OrdinaryAndDelayedTricks(registry);
    }
    private static void AssertCommandReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Replay(game.CreateCheckpoint().Options, CommandJson.Deserialize(CommandJson.Serialize(game.AcceptedCommands)), registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)), "Serialized accepted command journal must reproduce the final state.");
    }
    private static void OrdinaryAndDelayedTricks(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "fixture:command-tricks", UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Trick fixture start failed.");
        for (var i = 0; i < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; i++) Advance(game);
        void Skill(string activation, int seat)
        {
            Require(game.Submit(new UseProgramSkillCommand(0, "fixture:command-driver", activation, [], [seat], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Trick fixture health change failed.");
            for (var i = 0; i < 80 && (game.ResolutionStack.Count != 0 || game.PendingDecision?.Kind != DecisionKind.PlayCard); i++) Advance(game);
        }
        Skill("damage", 1);
        Skill("wound", 2);
        var before = game.CreateSnapshot(0, true);
        var garden = before.Players[0].Hand.First(card => card.Kind == CardKind.PeachGarden);
        Require(game.Submit(new PlayCardCommand(0, garden.Id, [], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Peach Garden use failed.");
        for (var i = 0; i < 80 && (game.ResolutionStack.Count != 0 || game.PendingDecision?.Kind != DecisionKind.PlayCard); i++) Advance(game);
        var after = game.CreateSnapshot(0, true);
        Require(after.Players[1].Hp == before.Players[1].Hp, "Zhichi must also nullify beneficial ordinary tricks.");
        Require(after.Players[2].Hp == before.Players[2].Hp + 1, "The same group trick must still heal an unprotected target.");
        Require(game.Events.Any(e => e.Payload is CardEffectSkippedEvent skipped && skipped.TargetSeat == 1 && skipped.CardKind == CardKind.PeachGarden), "Ordinary trick immunity must publish an actual skipped target effect.");
        var delayed = after.Players[0].Hand.First(card => card.Kind == CardKind.Indulgence);
        Require(game.Submit(new PlayCardCommand(0, delayed.Id, [1], game.Revision, game.PendingDecision!.PromptId)).Accepted, "Delayed trick use failed.");
        for (var i = 0; i < 80 && (game.ResolutionStack.Count != 0 || game.PendingDecision?.Kind != DecisionKind.PlayCard); i++) Advance(game);
        Require(game.CreateSnapshot(0, true).Players[1].Judgment.Any(card => card.Id == delayed.Id), "Zhichi must not block placement of a delayed trick.");
        Require(!game.Events.Any(e => e.Payload is CardEffectSkippedEvent skipped && skipped.TargetSeat == 1 && skipped.CardKind == CardKind.Indulgence), "Delayed trick must not publish an immunity skip.");
        AssertCommandReplay(game, registry);
    }
    private static void Advance(GameEngine game)
    {
        var prompt = game.PendingDecision;
        var command = prompt is null || prompt.Kind == DecisionKind.PlayCard || prompt.PlayerSeat != 0 ? (GameCommand)new AdvanceCommand(game.Revision) : new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First().Id, game.Revision);
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "Continuation failed.");
    }
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("command-fixture", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(Rules, """{"schemaVersion":3,"skills":{"fixture:command-driver":{"name":"Driver","description":"Test"}}}""");
            builder.AddSkill(new ContentSkillDefinition("fixture:command-driver", "Driver", "Test") { Program = catalog.Programs["fixture:command-driver"] });
            var generals = Enumerable.Range(0, 4).Select(i => $"fixture:command-{i}").ToArray();
            foreach (var id in generals) builder.AddGeneral(new ContentGeneralDefinition(id, "Test", "supporter", "classic:mingce", BaseHp: 6, AdditionalSkillIds: ["classic:zhichi", "fixture:command-driver"]));
            builder.AddDeck(new ContentDeckRecipe("fixture:command-deck", "Fixture", 4, 2, [new ContentDeckCardCount("standard:slash", 100)]));
            builder.AddMode(new ContentModeDefinition("fixture:command", "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:command-deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
            builder.AddDeck(new ContentDeckRecipe("fixture:command-trick-deck", "Tricks", 10, 2, [new ContentDeckCardCount("standard:peach_garden", 50), new ContentDeckCardCount("standard:indulgence", 50)]));
            builder.AddMode(new ContentModeDefinition("fixture:command-tricks", "Tricks", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:command-trick-deck", GeneralCandidateCount: 1, GeneralPoolIds: generals));
        }
    }
    private static readonly string Rules = $$"""
    {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:command-driver","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
    {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
    {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
    {"id":"offer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]},
    {"id":"no-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["slash"],"actionTypes":["use"]},{"op":"offerVirtualSlashOrDraw","target":"selectedTarget"}]}]}]}
    """;
}
