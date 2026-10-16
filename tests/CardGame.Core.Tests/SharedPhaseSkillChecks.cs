using CardGame.Core;
using static SharedPhaseSkillScenario;

internal static class SharedPhaseSkillChecks
{
    public static void OwnAndForeignChoicesReplayAndExpire()
    {
        foreach (var real in new[] { false, true })
        {
            var registry = Registry(real);
            var skillId = real ? "ol:bolan" : Shared;
            var own = Start(registry, ownerHuman: true);
            Reach(own, prompt => prompt?.SkillPrompt?.SkillId == skillId);
            Answer(own, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            var offered = own.PendingDecision!;
            Need(offered.PlayerSeat == 0 && offered.Choices.Count == 3 && offered.Choices.All(choice => choice.Parameters.ContainsKey("skill-id")),
                "Own phase start must offer three distinct skill IDs after accepting its optional trigger.");
            var beforeChoice = Cold(own, registry);
            Need(beforeChoice.PendingDecision!.Choices.Select(choice => choice.Id).SequenceEqual(offered.Choices.Select(choice => choice.Id)),
                "Cold restore must retain the sampled candidate order.");
            Answer(own, _ => true);
            Play(own);
            var ownGrant = Events<ProgramPlayPhaseSkillsGrantedEvent>(own).Single();
            Need(ownGrant.RecipientSeat == 0 && Events<ProgramSkillHpLostEvent>(own).Length == 0, "Own phase offer must not pay foreign HP loss.");
            End(own);
            Reach(own, _ => own.State.Phase != TurnPhase.Play);
            Need(!Has(own, ownGrant.Grants.Single().SkillId), "The own phase grant must expire at its actual boundary.");

            var game = Start(registry);
            Play(game);
            var action = game.GetHumanLegalActions().Last(candidate => candidate.ProgramSkillId == skillId);
            var hp = game.CreateSnapshot(0).Players[0].Hp;
            var snapshot = SnapshotJson.Serialize(game.CreateSnapshot(0));
            var invalid = game.Submit(new UseProgramSkillCommand(0, skillId, action.ProgramActivationId!, [], [], game.Revision, game.PendingDecision!.PromptId));
            Need(!invalid.Accepted && SnapshotJson.Serialize(game.CreateSnapshot(0)) == snapshot, "Shared activation must reject an omitted source owner atomically.");
            UseShared(game, action);
            Reach(game, decision => decision?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "random-phase-skill") == true);
            var prompt = Decision(game)!;
            Need(prompt.PlayerSeat == action.ProgramSkillOwnerSeat && prompt.Choices.Count == 3 &&
                prompt.Choices.Select(choice => choice.Parameters["skill-id"]).Distinct().Count() == 3 &&
                game.CreateSnapshot(0).PendingDecision is null && game.CreateSnapshot(0).Players[0].Hp == hp - 1,
                "The payer loses one HP, while only the skill owner receives the distinct-three choice.");
            invalid = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices[0].Id, game.Revision));
            Need(!invalid.Accepted, "The payer must not answer the owner's skill choice.");
            var restored = Cold(game, registry);
            Need(Decision(restored)!.Choices.Select(choice => choice.Id).SequenceEqual(prompt.Choices.Select(choice => choice.Id)),
                "A foreign selection checkpoint must not resample or repay.");
            Play(restored);
            var grant = Events<ProgramPlayPhaseSkillsGrantedEvent>(restored).Single();
            Need(grant.RecipientSeat == 0 && grant.Source.OwnerSeat == action.ProgramSkillOwnerSeat && Has(restored, grant.Grants.Single().SkillId) &&
                Events<ProgramSkillHpLostEvent>(restored).Length == 1 && restored.CreateSnapshot(0).Players[0].Hp == hp - 1 &&
                restored.GetHumanLegalActions().All(candidate => candidate.ProgramSkillId != skillId || candidate.ProgramSkillOwnerSeat != action.ProgramSkillOwnerSeat),
                "The foreign grant and phase quota must return to the payer once, preserving the source owner.");
            var frozenOffer = Events<ProgramRandomPhaseSkillsOfferedEvent>(restored).Single();
            Need(frozenOffer.SkillIds is ICollection<string> { IsReadOnly: true }, "Committed candidate collections must be frozen.");
            if (!real)
            {
                var gainedAction = restored.GetHumanLegalActions().Single(candidate => candidate.ProgramSkillId == grant.Grants.Single().SkillId);
                UseShared(restored, gainedAction);
                Play(restored);
                Need(restored.GetHumanLegalActions().All(candidate => candidate.ProgramSkillId != gainedAction.ProgramSkillId), "A granted skill must use its native phase quota.");
            }
            End(restored);
            Reach(restored, _ => restored.State.Phase != TurnPhase.Play);
            Need(!Has(restored, grant.Grants.Single().SkillId), "Foreign phase grants must expire without waiting for turn end.");
            _ = Cold(restored, registry);
            if (real)
            {
                var pool = registry.GetSkill(skillId).Program!.Activations.Single().Effects.Last().SkillIds;
                Need(pool.Count == 18 && pool.All(id => registry.GetSkill(id).Program is not null), "Bolan must register the explicit complete announced pool.");
            }
        }
    }

    public static void PaidDyingAndExtraPhaseSources()
    {
        var registry = Registry(initialHp: 1);
        var game = Start(registry);
        Play(game);
        UseShared(game, game.GetHumanLegalActions().Single(action => action.ProgramSkillId == "fixture:shared-health-driver"));
        Play(game);
        UseShared(game, game.GetHumanLegalActions().First(action => action.ProgramSkillId == Shared));
        Reach(game, decision => decision is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 });
        Need(Events<ProgramSkillHpLostEvent>(game).Count(item => item.SkillId == Shared) == 1 && game.PendingDecision?.PlayerSeat == 0,
            "A one-HP payer must enter its real dying response before skills are offered.");
        game = Cold(game, registry);
        Answer(game, choice => choice.Cards.Count == 1);
        Play(game);
        Need(Events<ProgramSkillHpLostEvent>(game).Count(item => item.SkillId == Shared) == 1 && Events<ProgramPlayPhaseSkillsGrantedEvent>(game).Length == 1,
            "Resuming rescue must not pay HP twice or grant before the child returns.");

        registry = Registry(initialHp: 1, rescue: false);
        game = Start(registry);
        Play(game);
        UseShared(game, game.GetHumanLegalActions().Single(action => action.ProgramSkillId == "fixture:shared-health-driver"));
        Play(game);
        UseShared(game, game.GetHumanLegalActions().First(action => action.ProgramSkillId == Shared));
        Reach(game, _ => game.State.Winner != Winner.None);
        Need(Events<ProgramSkillHpLostEvent>(game).Count(item => item.SkillId == Shared) == 1 &&
            Events<ProgramPlayPhaseSkillsGrantedEvent>(game).Length == 0 && Events<ProgramRandomPhaseSkillsOfferedEvent>(game).Length == 0,
            "Death from the real HP payment must finish its child without offering or granting a skill to the dead actor.");

        registry = Registry(permanent: true, extraPhase: true);
        game = Start(registry);
        Play(game);
        var action = game.GetHumanLegalActions().First(candidate => candidate.ProgramSkillId == Shared);
        UseShared(game, action);
        Play(game);
        var first = Events<ProgramPlayPhaseSkillsGrantedEvent>(game).Single();
        End(game);
        Reach(game, prompt => prompt?.Kind == DecisionKind.PlayCard || prompt?.SkillPrompt?.SkillId == "fixture:shared-extra-phase");
        if (game.PendingDecision?.Kind != DecisionKind.PlayCard) Answer(game, _ => true);
        Play(game);
        Need(game.State.TurnNumber == first.Expiry.TurnNumber && Has(game, Draw) &&
            game.GetHumanLegalActions().Any(candidate => candidate.ProgramSkillId == Shared && candidate.ProgramSkillOwnerSeat == action.ProgramSkillOwnerSeat) &&
            Events<ProgramPlayPhaseSkillGrantExpiredEvent>(game).Any(expired => expired.Grant.GrantId == first.Grants.Single().GrantId),
            "An inserted Play phase must expire the previous phase's source, preserve permanent skills and open a new foreign quota.");
        UseShared(game, game.GetHumanLegalActions().First(candidate => candidate.ProgramSkillId == Shared && candidate.ProgramSkillOwnerSeat == action.ProgramSkillOwnerSeat));
        Play(game);
        Need(Events<ProgramPlayPhaseSkillsGrantedEvent>(game).Select(item => item.Expiry.PhaseInstanceId).Distinct().Count() == 2,
            "Repeated same-turn activations must issue different exact phase identities.");
        _ = Cold(game, registry);
    }

    public static void OffTurnOwnerChoiceAndConfigurationBoundaries()
    {
        var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:shared-contract","revision":1,"activations":[{"id":"share","otherCharactersOnly":true,"minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"loseHp","target":"activator","amount":1},{"op":"offerRandomPlayPhaseSkill","target":"activator","amount":3,"skillIds":["fixture:a","fixture:b","fixture:c"]}]}]}]}
            """;
        const string presentation = """{"schemaVersion":3,"skills":{"fixture:shared-contract":{"name":"共享契约","description":"边界检查"}}}""";
        _ = SkillProgramCatalog.Load(rules, presentation);
        foreach (var invalid in new[]
        {
            rules.Replace("\"maxCards\":0", "\"maxCards\":1"),
            rules.Replace("\"otherCharactersOnly\":true", "\"otherCharactersOnly\":false"),
            rules.Replace("\"usesPerTurn\":null", "\"usesPerTurn\":1"),
            rules.Replace("\"otherCharactersOnly\":true", "\"otherCharactersOnly\":true,\"usageGroup\":\"other-quota\""),
            rules.Replace("\"amount\":3", "\"amount\":4"),
            rules.Replace("\"target\":\"activator\"", "\"target\":\"owner\"")
        })
        {
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(invalid, presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "Shared configuration must reject unsupported input, owner costs, quotas and oversized random offers.");
        }
        var registry = Registry();
        var game = Start(registry, ownerHuman: true);
        Reach(game, prompt => prompt?.SkillPrompt?.SkillId == Shared);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        Play(game);
        End(game);
        Reach(game, prompt => prompt is { PlayerSeat: 0 } && prompt.Choices.Any(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "random-phase-skill"));
        var invocation = Events<ProgramSharedActivationStartedEvent>(game).Last();
        Need(invocation.ActorSeat != 0 && invocation.Source.OwnerSeat == 0 && game.State.CurrentSeat == invocation.ActorSeat,
            "The real AI actor must invoke another owner's skill and pause for that owner's off-turn choice.");
        game = Cold(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("skill-id") == Draw);
        var grant = Events<ProgramPlayPhaseSkillsGrantedEvent>(game).Last();
        Need(grant.RecipientSeat == invocation.ActorSeat && grant.Source.OwnerSeat == 0 &&
            game.CreateSnapshot(0).Players[invocation.ActorSeat].Skills!.Any(skill => skill.ContentId == Draw),
            "The human owner chooses, while the actual off-turn AI payer receives the selected skill.");
    }

    private static void UseShared(GameEngine game, LegalAction action) => Accept(game,
        new UseProgramSkillCommand(0, action.ProgramSkillId!, action.ProgramActivationId!, [], [], game.Revision, game.PendingDecision!.PromptId)
        { SkillOwnerSeat = action.ProgramSkillOwnerSeat });
    private static void End(GameEngine game) => Accept(game, new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId));
    private static bool Has(GameEngine game, string id) => game.CreateSnapshot(0).Players[0].Skills?.Any(skill => skill.ContentId == id) == true;
    private static T[] Events<T>(GameEngine game) where T : IGameEvent => game.Events.Select(item => item.Payload).OfType<T>().ToArray();
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(SnapshotJson.Serialize(cold.CreateSnapshot(0)) == SnapshotJson.Serialize(game.CreateSnapshot(0)), "Shared skill cold replay must match the permitted view exactly.");
        return cold;
    }
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
