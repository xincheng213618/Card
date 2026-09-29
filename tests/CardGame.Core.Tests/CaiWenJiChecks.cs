using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CaiWenJiChecks
{
    private const string General = "classic:cai-wen-ji";
    private const string Beige = "classic:beige";
    private const string Duanchang = "classic:duanchang";
    private const string Mode = "identity:classic-cai-wen-ji-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "qun" } general &&
                general.SkillIds.SequenceEqual([Beige, Duanchang]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Cai Wenji must be in the current Qun roster with three HP.");

        var beige = current.Skills[Beige].Program!;
        var prompt = beige.Triggers.Single(item => item.Id == "slash-damage-prompt");
        Require(prompt.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                prompt.Subject == SkillProgramTriggerSubject.Any &&
                prompt.Optional &&
                prompt.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                ConditionHas(prompt.Condition, SkillProgramTriggerConditionKind.DamageCardIsSlash) &&
                ConditionHas(prompt.Condition, SkillProgramTriggerConditionKind.Compare),
            "Beige must offer an optional prompt after any surviving slash damage.");
        Require(prompt.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.SelectOwnedCards,
            SkillProgramEffectOp.MoveBoundCards,
            SkillProgramEffectOp.SelectTarget,
            SkillProgramEffectOp.StartJudgment,
            SkillProgramEffectOp.MoveBoundCards]),
            "Beige must discard one cost card, target the victim and judge them.");
        var judgment = prompt.Effects.Single(item => item.Op == SkillProgramEffectOp.StartJudgment);
        Require(judgment.Target == SkillProgramEffectTarget.SelectedTarget &&
                judgment.SourceRef is { Kind: ProgramParticipantRef.EventSource },
            "The Beige judgment must judge the victim and carry the damage source as its source.");

        var branches = beige.Triggers
            .Where(item => item.Window == SkillProgramTriggerWindow.JudgmentFinalized).ToArray();
        Require(branches.Length == 4 &&
                branches.Select(item => item.Suits.Single()).Order().SequenceEqual(
                    [Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond]) &&
                branches.All(item => !item.Optional &&
                    item.JudgmentSource is null &&
                    item.JudgmentReasons.SequenceEqual(["skill.classic.beige"])),
            "The four Beige branches must partition the four suits on the scoped judgment reason.");
        Require(branches.Single(item => item.Suits.Single() == Suit.Heart).Effects
                .Single(item => item.Op == SkillProgramEffectOp.Recover).TargetReference is
        { Kind: ProgramParticipantRef.EventTarget },
            "The heart branch must recover the judged victim.");
        Require(branches.Single(item => item.Suits.Single() == Suit.Diamond).Effects
                .Single(item => item.Op == SkillProgramEffectOp.Draw).TargetReference is
        { Kind: ProgramParticipantRef.EventTarget },
            "The diamond branch must let the judged victim draw.");
        var club = branches.Single(item => item.Suits.Single() == Suit.Club).Effects
            .Where(item => item.Op == SkillProgramEffectOp.ChooseOwnCardDiscard).ToArray();
        Require(club.Length == 2 && club.All(item =>
                item.TargetReference is null &&
                item.ChooserRef is { Kind: ProgramParticipantRef.EventSource } &&
                item.Zones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment])),
            "The club branch must make the damage source discard two own chosen cards.");
        var spade = branches.Single(item => item.Suits.Single() == Suit.Spade).Effects.Single();
        Require(spade.Op == SkillProgramEffectOp.TurnOver &&
                spade.TargetReference is { Kind: ProgramParticipantRef.EventSource },
            "The spade branch must turn the damage source over.");

        var duanchang = current.Skills[Duanchang].Program!;
        Require(duanchang.Triggers.Single() is
        { Window: SkillProgramTriggerWindow.OwnerDied, Optional: false },
            "Duanchang must stay a mandatory ownerDied trigger.");

        const string slashTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:slash","revision":1,
            "minimumRulesVersion": 182,
            "triggers":[{"id":"t","window":"afterDamageApplied","subject":"any",
            "damageOccurrence":"perDamage","optional":true,
            "condition":{"kind":"damageCardIsSlash"},
            "effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string slashPresentation = """
            {"schemaVersion":3,"skills":{"fixture:slash":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(slashTemplate, slashPresentation)
                .Programs["fixture:slash"].Triggers.Single().Effects.Count == 1,
            "A damageCardIsSlash condition must be definable on afterDamageApplied triggers.");
        Reject(slashTemplate.Replace("\"window\":\"afterDamageApplied\"", "\"window\":\"cardUseCommitted\""),
            slashPresentation, "damageCardIsSlash outside an afterDamageApplied trigger");

        const string sourceJudgmentTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:judge","revision":1,
            "minimumRulesVersion": 182,
            "triggers":[{"id":"t","window":"afterDamageApplied","subject":"any",
            "damageOccurrence":"perDamage","optional":false,
            "effects":[{"op":"selectTarget","target":"owner","targetKind":"eventTarget"},
            {"op":"startJudgment","target":"selectedTarget","judgmentReason":"fixture.test",
            "resultBind":"fixture-bind","visibility":"public","sourceRef":{"kind":"eventSource"}},
            {"op":"moveBoundCards","target":"owner","sourceBind":"fixture-bind","destination":"discardPile"}]}]}]}
            """;
        const string sourceJudgmentPresentation = """
            {"schemaVersion":3,"skills":{"fixture:judge":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(sourceJudgmentTemplate, sourceJudgmentPresentation)
                .Programs["fixture:judge"].Triggers.Single().Effects.Count == 3,
            "startJudgment must accept an eventSource sourceRef.");
        Reject(sourceJudgmentTemplate.Replace("\"kind\":\"eventSource\"", "\"kind\":\"resultSource\""),
            sourceJudgmentPresentation, "startJudgment sourceRef outside event participants");

        const string ownDiscardTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:discard","revision":1,
            "minimumRulesVersion": 182,
            "triggers":[{"id":"t","window":"judgmentFinalized","subject":"any",
            "judgmentReasons":["fixture.test"],"suits":["club"],
            "minimumRank":1,"maximumRank":13,"excludedReasons":[],
            "optional":false,
            "effects":[{"op":"chooseOwnCardDiscard","target":"owner",
            "chooserRef":{"kind":"eventSource"},"zones":["hand","equipment"]}]}]}]}
            """;
        const string ownDiscardPresentation = """
            {"schemaVersion":3,"skills":{"fixture:discard":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(ownDiscardTemplate, ownDiscardPresentation)
                .Programs["fixture:discard"].Triggers.Single().Effects.Count == 1,
            "chooseOwnCardDiscard must accept an eventSource chooser with own zones.");
        Reject(ownDiscardTemplate.Replace("\"zones\":[\"hand\",\"equipment\"]", "\"zones\":[\"judgment\"]"),
            ownDiscardPresentation, "chooseOwnCardDiscard on the judgment zone");

        const string sourceTurnOverTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:flip","revision":1,
            "minimumRulesVersion": 182,
            "triggers":[{"id":"t","window":"judgmentFinalized","subject":"any",
            "judgmentReasons":["fixture.test"],"suits":["club"],
            "minimumRank":1,"maximumRank":13,"excludedReasons":[],
            "optional":false,
            "effects":[{"op":"turnOver","target":"owner","targetRef":{"kind":"eventSource"}}]}]}]}
            """;
        const string sourceTurnOverPresentation = """
            {"schemaVersion":3,"skills":{"fixture:flip":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(sourceTurnOverTemplate, sourceTurnOverPresentation)
                .Programs["fixture:flip"].Triggers.Single().Effects.Count == 1,
            "turnOver must accept an eventSource targetRef.");
        Reject(sourceTurnOverTemplate.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            sourceTurnOverPresentation, "turnOver targetRef requires an owner placeholder");
    }

    public static void BeigeResolvesOneBranchPerJudgmentAndReplays()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            try
            {
                if (DriveUntilBeigePrompt(game) is null) continue;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"[seed {seed} prompt-phase] {ex}");
            }
            var damage = game.Events.Select(item => item.Payload)
                .OfType<DamageAppliedEvent>().LastOrDefault();
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Activate(game, Beige);
            Activate(replay, Beige);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            DriveUntil(replay, () => replay.ResolutionStack.Count == 0);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Beige resolution must replay identically from the paused checkpoint.");
            var movements = game.CardMovements
                .Where(item => item.Reason.Value.Contains(Beige, StringComparison.Ordinal)).ToArray();
            var costDiscards = movements.Count(item => item.To == CardLocation.DiscardPile &&
                item.From.Zone == CardZoneKind.Hand &&
                item.Reason.Value.EndsWith("MoveBoundCards", StringComparison.Ordinal));
            var judgmentCleanup = movements.Count(item => item.To == CardLocation.DiscardPile &&
                item.From.Zone == CardZoneKind.Processing);
            if (costDiscards != 1 || judgmentCleanup != 1)
                throw new InvalidOperationException(
                    $"[dbg seed {seed}] beige movements: " + string.Join(" | ", game.CardMovements
                        .Select(item => $"{item.CardKind}#{item.CardId}:{item.From.Zone}({item.From.OwnerSeat})->" +
                            $"{item.To.Zone}({item.To.OwnerSeat}):{item.Reason.Value}")
                        .TakeLast(14)));
            var victimHpGain = damage is { } applied
                ? game.CreateSnapshot(0, true).Players[applied.TargetSeat].Hp - applied.RemainingHp
                : 0;
            var victimDraws = movements.Count(item => item.To.Zone == CardZoneKind.Hand &&
                item.From.Zone == CardZoneKind.DrawPile);
            var sourceDiscards = movements.Count(item =>
                item.Reason.Value.Contains("ChooseOwnCardDiscard", StringComparison.Ordinal));
            var flipped = game.CreateSnapshot(0, true).Players
                .Where(item => item.IsFaceDown).Select(item => item.Seat)
                .Where(item => item != 0).Count();
            var branchCount = (victimHpGain > 0 ? 1 : 0) + (victimDraws > 0 ? 1 : 0) +
                (sourceDiscards > 0 ? 1 : 0) + (flipped > 0 ? 1 : 0);
            Require(branchCount == 1,
                "Exactly one Beige branch must resolve per judgment.");
            Require(victimDraws == 0 && sourceDiscards == 0 ||
                    victimDraws == 2 && sourceDiscards == 0 ||
                    victimDraws == 0 && sourceDiscards is 1 or 2,
                "The diamond branch must draw exactly two cards and the club branch discards up to two.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved a Beige judgment.");
    }

    public static void ClubBranchMakesTheSourceDiscardTwoOwnCards()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (DriveUntilBeigePrompt(game) is null) continue;
            var damageSource = game.Events.Select(item => item.Payload)
                .OfType<DamageAppliedEvent>()
                .LastOrDefault()?.SourceSeat;
            Activate(game, Beige);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            var discards = game.CardMovements.Where(item =>
                    item.Reason.Value.Contains("ChooseOwnCardDiscard", StringComparison.Ordinal) &&
                    item.To == CardLocation.DiscardPile).ToArray();
            if (discards.Length == 0) continue;
            Require(discards.Length == 2 &&
                    damageSource is { } source && discards.All(item =>
                        (item.From.Zone == CardZoneKind.Hand || item.From.Zone == CardZoneKind.Equipment) &&
                        item.From.OwnerSeat == source),
                "The club branch must make the damage source discard exactly two of their own cards.");
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the Beige club branch.");
    }

    public static void SpadeBranchTurnsTheSourceOver()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            if (DriveUntilBeigePrompt(game) is null) continue;
            var damageSource = game.Events.Select(item => item.Payload)
                .OfType<DamageAppliedEvent>()
                .LastOrDefault()?.SourceSeat;
            Activate(game, Beige);
            DriveUntil(game, () => game.ResolutionStack.Count == 0);
            if (damageSource is not { } source ||
                !game.CreateSnapshot(0, true).Players[source].IsFaceDown)
                continue;
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved the Beige spade branch.");
    }

    private static bool ConditionHas(SkillProgramTriggerCondition condition,
        SkillProgramTriggerConditionKind kind) =>
        condition.Kind == kind || (condition.Children?.Any(child => ConditionHas(child, kind)) ?? false);

    private static PendingDecision? DriveUntilBeigePrompt(GameEngine game)
    {
        DriveUntil(game, () => false, stopAtSkills: [Beige]);
        return game.State.Status == EngineStatus.Completed ? null :
            IsProgramPrompt(game, Beige) ? game.PendingDecision : null;
    }

    private static bool IsProgramPrompt(GameEngine game, string skillId) =>
        game.PendingDecision is
        {
            Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0
        } prompt && prompt.SkillPrompt?.SkillId == skillId;

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        string[]? stopAtSkills = null,
        int budget = 800)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId is { } skillId &&
                stopAtSkills is not null && stopAtSkills.Contains(skillId))
            {
                return;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    continue;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    continue;
                default:
                    var choice = prompt.Choices.FirstOrDefault(item =>
                        item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        prompt.Choices.First();
                    Answer(game, choice);
                    continue;
            }
        }
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario());

    private static void Activate(GameEngine game, string skillId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            $"No pending decision to activate {skillId}.");
        Require(prompt.Kind == DecisionKind.ProgramTrigger,
            $"The {skillId} prompt must be a program trigger.");
        var activate = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate") ??
            throw new InvalidOperationException($"The {skillId} prompt lost its activate choice.");
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            activate.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? $"{skillId} activation failed.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Cai Wenji skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, int seed)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = Mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Cai Wenji fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Cai Wenji selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Cai Wenji fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Cai Wenji command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Reject(string rules, string presentation, string because = "")
    {
        try
        {
            _ = SkillProgramCatalog.Load(rules, presentation);
        }
        catch (InvalidOperationException)
        {
            return;
        }
        throw new InvalidOperationException(
            $"Expected invalid Cai Wenji composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("cai-wen-ji-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 152, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1-4 are skill-less AI banks so the
            // only live skills are Beige and Duanchang, and every AI slash damage
            // can offer the Beige prompt to the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cai-wen-ji-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cai-wen-ji-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cai-wen-ji-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:cai-wen-ji-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index => (index % 4) switch
            {
                1 => "standard:dodge",
                2 => "standard:peach",
                _ => "standard:slash"
            }).Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:cai-wen-ji-deck", "蔡文姬测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "蔡文姬测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:cai-wen-ji-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:cai-wen-ji-bank-a",
                    "fixture:cai-wen-ji-bank-b",
                    "fixture:cai-wen-ji-bank-c",
                    "fixture:cai-wen-ji-bank-d"]));
        }
    }
}
