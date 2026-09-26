using CardGame.Content.Standard;
using CardGame.Core;

internal static class SkillProgramJudgmentTriggerChecks
{
    internal static void Definitions()
    {
        var catalog = SkillProgramCatalog.Load(ValidV3, Presentation);
        var program = catalog.Programs["judgment-test:reward"];
        var trigger = program.Triggers.Single();
        Require(program.RuntimeVersion == "skill-program-v60" && program.MinimumRulesVersion == 170 &&
                trigger.Window == SkillProgramTriggerWindow.JudgmentFinalized &&
                trigger.SourceSkillId is null && trigger.SourceViewAsId is null &&
                trigger.Subject == SkillProgramTriggerSubject.Owner && trigger.Optional &&
                trigger.Suits.SequenceEqual([Suit.Club]) &&
                trigger.MinimumRank == 1 && trigger.MaximumRank == 13 &&
                trigger.ExcludedReasons.SequenceEqual([JudgmentReasons.Leiji]) &&
                trigger.Effects.Select(effect => effect.Op)
                    .SequenceEqual([SkillProgramEffectOp.Recover, SkillProgramEffectOp.Draw]),
            "The shared executor must retain typed final-judgment filters and effects.");
        RequireThrows<NotSupportedException>(() =>
            ((ICollection<Suit>)trigger.Suits).Add(Suit.Spade));
        RequireThrows<NotSupportedException>(() =>
            ((ICollection<string>)trigger.ExcludedReasons).Clear());

        AssertReject(ValidV3.Replace("\"schemaVersion\":60", "\"schemaVersion\":57", StringComparison.Ordinal),
            "schema version");
        AssertReject(ValidV3.Replace("\"suits\":[\"club\"]", "\"suits\":[]", StringComparison.Ordinal),
            "at least one final suit");
        AssertReject(ValidV3.Replace("\"minimumRank\":1", "\"minimumRank\":14", StringComparison.Ordinal),
            "rank bounds");
        AssertReject(ValidV3.Replace("\"target\":\"owner\"", "\"target\":\"opponent\"", StringComparison.Ordinal),
            "opponent");
        AssertReject(ValidV3.Replace("\"op\":\"recover\"", "\"op\":\"obtainOpponentHandCard\"", StringComparison.Ordinal),
            "obtainOpponentHandCard");
        AssertReject(ValidV3.Replace("\"subject\":\"owner\"", "\"sourceSkillId\":\"x\",\"subject\":\"owner\"", StringComparison.Ordinal),
            "does not accept card-conversion source fields");
        AssertReject(ValidV3.Replace("\"excludedReasons\":[\"skill.leiji\"]",
                "\"excludedReasons\":[\"skill.leiji\",\"skill.leiji\"]", StringComparison.Ordinal),
            "duplicate");
    }

    internal static void WindowAndReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new JudgmentFixturePackage());
        var game = FindProgramBoundary(registry);
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("Final judgment trigger prompt was lost.");
        var frame = game.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().Single();
        var judgment = game.Events.Select(item => item.Payload).OfType<JudgmentResolvedEvent>()
            .Single(item => item.ResolutionId == frame.Judgment.JudgmentFrameId);
        var replacements = game.Events.Select(item => item.Payload)
            .OfType<JudgmentReplacementResolvedEvent>()
            .Where(item => item.JudgmentResolutionId == frame.Judgment.JudgmentFrameId && item.Used)
            .ToArray();
        Require(replacements.Length == 1,
            $"The fixture expected one used replacement for judgment {frame.Judgment.JudgmentFrameId}, got {replacements.Length}.");
        var replacement = replacements[0];
        var before = game.CreateSnapshot(0, revealAll: true).Players[0];
        Require(prompt is { Kind: DecisionKind.ProgramJudgmentTrigger, PlayerSeat: 0, IsPrivate: true } &&
                prompt.Choices.Count == 2 && prompt.Choices.All(choice => choice.Cards.Count == 0) &&
                frame.Judgment is { SubjectSeat: 0, Reason: JudgmentReasons.Ganglie, Suit: Suit.Club, Rank: 5 } &&
                judgment.Suit == Suit.Club && replacement.NewCardId == judgment.CardId &&
                replacement.NewSuit == judgment.Suit && before.Hp == before.MaxHp - 1 &&
                game.CreateCardZoneDiagnostics().Single(card => card.CardId == judgment.CardId).Location ==
                    CardLocation.Judgment(0),
            "The final replacement result must be frozen before judgment-card cleanup.");
        Require(game.CreateSnapshot(1, revealAll: false).PendingDecision is null,
            "Other seats must not receive the owner's optional judgment trigger prompt.");
        var paused = game.CreateCheckpoint();

        var skipped = GameReplay.Restore(paused, registry);
        var skippedBefore = skipped.CreateSnapshot(0, revealAll: true).Players[0];
        Answer(skipped, "program-judgment-trigger-skip");
        var skippedAfter = skipped.CreateSnapshot(0, revealAll: true).Players[0];
        Require(skippedAfter.Hp == skippedBefore.Hp && skippedAfter.HandCount == skippedBefore.HandCount &&
                skipped.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>()
                    .Single() is { SkillId: "judgment-test:reward", Activated: false } &&
                skipped.ResolutionStack.All(item => item is not ProgramJudgmentTriggerWindowFrame),
            "Skipping must preserve HP and hand count while resuming the parent Ganglie judgment.");

        var activated = GameReplay.Restore(paused, registry);
        var activatedBefore = activated.CreateSnapshot(0, revealAll: true).Players[0];
        Answer(activated, "program-judgment-trigger-activate");
        var activatedAfter = activated.CreateSnapshot(0, revealAll: true).Players[0];
        Require(activatedAfter.Hp == activatedAfter.MaxHp &&
                activatedAfter.HandCount == activatedBefore.HandCount + 1 &&
                activated.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                    .Count(item => item.SkillId == "judgment-test:reward" &&
                        item.Window == SkillProgramTriggerWindow.JudgmentFinalized) == 1 &&
                activated.Events.Select(item => item.Payload).OfType<ProgramBindingResolvedEvent>()
                    .Count(item => item.SkillId == "judgment-test:reward" && item.Completed) == 1 &&
                activated.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>()
                    .Single() is
                    {
                        SkillId: "judgment-test:reward",
                        TriggerId: "after-club-judgment",
                        OwnerSeat: 0,
                        Activated: true
                    } &&
                activated.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>()
                    .All(item => item.SkillId != "judgment-test:excluded") &&
                activated.CreateCardZoneDiagnostics().Single(card => card.CardId == judgment.CardId).Location ==
                    CardLocation.DiscardPile,
            "Activation must recover, draw and then resume ordinary judgment cleanup exactly once.");
        var replay = GameReplay.Restore(activated.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(activated.CreateSnapshot(0, revealAll: true)) &&
                replay.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>().Count() == 1,
            "A completed final-judgment trigger must replay without repeating its effects.");
    }

    internal static void FrozenInstanceCannotTransferToAnotherGrant()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new JudgmentFixturePackage());
        var game = FindProgramBoundary(registry);
        var window = game.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().Single();
        var candidate = window.Candidates[window.CandidateIndex];
        var owner = ((IReadOnlyList<CharacterState>)(typeof(GameEngine)
            .GetField("_players", System.Reflection.BindingFlags.NonPublic |
                                  System.Reflection.BindingFlags.Instance)!
            .GetValue(game) ?? throw new InvalidOperationException("The judgment fixture has no players.")))
            [candidate.OwnerSeat];
        const string alternateInstanceId = "fixture:alternate-judgment-instance";
        owner.SkillGrants.Grant(new SkillGrant(
            "fixture:alternate-judgment-grant", candidate.SkillId, alternateInstanceId, "acquired:test"));
        foreach (var grant in owner.SkillGrants.Grants.Where(grant =>
                     grant.SkillId == candidate.SkillId &&
                     grant.SkillInstanceId == candidate.SkillInstanceId).ToArray())
            owner.SkillGrants.SetEnabled(grant.GrantId, false);
        Require(owner.SkillGrants.Grants.Any(grant =>
                grant.SkillId == candidate.SkillId && grant.SkillInstanceId == alternateInstanceId &&
                grant.IsEnabled) &&
                owner.SkillGrants.Grants.All(grant =>
                    grant.SkillId != candidate.SkillId ||
                    grant.SkillInstanceId != candidate.SkillInstanceId || !grant.IsEnabled),
            "The frozen judgment candidate must outlive its original grant while another instance stays active.");

        Answer(game, "program-judgment-trigger-activate");
        Require(game.Events.Select(item => item.Payload).OfType<ProgramBindingStartedEvent>()
                    .All(item => item.SkillId != candidate.SkillId ||
                                 item.Window != SkillProgramTriggerWindow.JudgmentFinalized) &&
                game.Events.Select(item => item.Payload).OfType<ProgramJudgmentTriggerResolvedEvent>()
                    .Single(item => item.SkillId == candidate.SkillId &&
                                    item.TriggerId == candidate.TriggerId) is { Activated: false } &&
                game.ResolutionStack.All(item => item is not ProgramJudgmentTriggerWindowFrame),
            "A revoked frozen instance must skip its opportunity rather than transfer it to another grant.");
    }

    private static GameEngine FindProgramBoundary(ContentRegistry registry)
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed,
                PlayerCount = 5,
                ModeId = "identity:classic-program-judgment-5",
                HumanSeat = 0,
                HumanRole = Role.Rebel,
                UseInteractiveSetup = false,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                AiPolicyVersion = 2,
                MaxTurns = 20
            }, registry);
            if (!game.Submit(new StartGameCommand()).Accepted) continue;
            for (var step = 0; step < 320 && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.ProgramJudgmentTrigger, PlayerSeat: 0 })
                {
                    var programFrame = game.ResolutionStack
                        .OfType<ProgramJudgmentTriggerWindowFrame>().Single();
                    var replaced = game.Events.Select(item => item.Payload)
                        .OfType<JudgmentReplacementResolvedEvent>()
                        .Any(item => item.JudgmentResolutionId == programFrame.Judgment.JudgmentFrameId && item.Used);
                    if (replaced) return game;
                    Answer(game, "program-judgment-trigger-skip");
                    continue;
                }
                if (game.PendingDecision is { Kind: DecisionKind.Ganglie, PlayerSeat: 0 } ganglie)
                {
                    var invoke = ganglie.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "ganglie");
                    var accepted = game.Submit(new AnswerPromptCommand(
                        0, ganglie.PromptId, invoke.Id, game.Revision));
                    Require(accepted.Accepted, accepted.Error?.Message ?? "Ganglie judgment was rejected.");
                    continue;
                }
                if (game.PendingDecision is { Kind: DecisionKind.Guicai, PlayerSeat: 0 } guicai)
                {
                    var replacement = guicai.Choices.FirstOrDefault(choice => choice.Cards.Count == 1) ??
                        throw new InvalidOperationException("The Guicai fixture has no replacement card.");
                    var accepted = game.Submit(new AnswerPromptCommand(
                        0, guicai.PromptId, replacement.Id, game.Revision));
                    Require(accepted.Accepted, accepted.Error?.Message ?? "Guicai replacement was rejected.");
                    continue;
                }
                if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
                {
                    var ended = game.Submit(new EndPlayPhaseCommand(
                        0, game.Revision, game.PendingDecision.PromptId));
                    Require(ended.Accepted, ended.Error?.Message ?? "Fixture could not end the play phase.");
                    continue;
                }
                if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } dodge)
                {
                    var decline = dodge.Choices.Single(choice => choice.Cards.Count == 0);
                    var answered = game.Submit(new AnswerPromptCommand(
                        0, dodge.PromptId, decline.Id, game.Revision));
                    Require(answered.Accepted, answered.Error?.Message ?? "Fixture could not decline Dodge.");
                    continue;
                }
                if (game.PendingDecision is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } dying)
                {
                    var decline = dying.Choices.Single(choice => choice.Cards.Count == 0);
                    var answered = game.Submit(new AnswerPromptCommand(
                        0, dying.PromptId, decline.Id, game.Revision));
                    Require(answered.Accepted, answered.Error?.Message ?? "Fixture could not decline rescue.");
                    continue;
                }
                var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
                if (!advanced.Accepted) break;
            }
        }
        throw new InvalidOperationException("No bounded final-judgment program boundary was found.");
    }

    private static void Answer(GameEngine game, string action)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("Program prompt was lost.");
        var choice = prompt.Choices.Single(item => item.Parameters.GetValueOrDefault("action") == action);
        var accepted = game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choice.Id, game.Revision));
        Require(accepted.Accepted, accepted.Error?.Message ?? $"Judgment action '{action}' was rejected.");
    }

    private sealed class JudgmentFixturePackage : IGameContentPackage
    {
        private static readonly string[] GeneralIds =
            Enumerable.Range(0, 5).Select(index => $"judgment-test:general-{index}").ToArray();

        public PackageManifest Manifest { get; } = new(
            "program-judgment-fixture", new Version(1, 0, 0),
            [new PackageDependency("standard", new Version(1, 11, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(ValidV3, Presentation);
            foreach (var program in catalog.Programs.Values)
            {
                var text = catalog.Presentations[program.Id];
                builder.AddSkill(new ContentSkillDefinition(program.Id, text.Name, text.Description)
                    { Program = program });
            }
            foreach (var id in GeneralIds)
                builder.AddGeneral(new ContentGeneralDefinition(
                id, "判定测试", "zhang_jiao", "standard:ganglie", "qun", BaseHp: 4,
                    AdditionalSkillIds: ["standard:guicai", "judgment-test:reward", "judgment-test:excluded"]));
            builder.AddDeck(new ContentDeckRecipe(
                "judgment-test:club-slash-deck", "梅花杀判定夹具", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 60)
                    .Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 5))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                "identity:classic-program-judgment-5", "判定程序夹具", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: "judgment-test:club-slash-deck",
                GeneralCandidateCount: 1,
                GeneralPoolIds: GeneralIds));
        }
    }

    private static void AssertReject(string rules, string expected)
    {
        try { _ = SkillProgramCatalog.Load(rules, Presentation); }
        catch (InvalidOperationException exception) when (
            exception.Message.Contains(expected, StringComparison.OrdinalIgnoreCase)) { return; }
        throw new InvalidOperationException($"Expected rejection containing '{expected}'.");
    }

    private static void RequireThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private const string ValidV3 = """
        {"schemaVersion":60,"skills":[
          {"id":"judgment-test:reward","revision":1,"triggers":[{
            "id":"after-club-judgment","window":"judgmentFinalized","subject":"owner",
            "suits":["club"],"minimumRank":1,"maximumRank":13,"excludedReasons":["skill.leiji"],
            "optional":true,"effects":[
              {"op":"recover","target":"owner","amount":2},
              {"op":"draw","target":"owner","amount":1}
            ]
          }]},
          {"id":"judgment-test:excluded","revision":1,"triggers":[{
            "id":"excluded-ganglie","window":"judgmentFinalized","subject":"owner",
            "suits":["club"],"minimumRank":1,"maximumRank":13,"excludedReasons":["skill.ganglie"],
            "optional":false,"effects":[{"op":"draw","target":"owner","amount":20}]
          }]}
        ]}
        """;

    private const string Presentation = """
        {"schemaVersion":3,"skills":{
          "judgment-test:reward":{"name":"判定奖赏","description":"梅花判定后回复并摸牌。"},
          "judgment-test:excluded":{"name":"排除原因","description":"不响应刚烈判定。"}
        }}
        """;
}
