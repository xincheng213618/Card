using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenSimaYiChecks
{
    private const string General = "classic:shen-sima-yi";
    private const string Renjie = "classic:renjie";
    private const string Baiyin = "classic:baiyin";
    private const string Lianpo = "classic:lianpo";
    private const string Mode = "identity:classic-shen-sima-yi-check-5";
    private const string KillMode = "identity:classic-shen-sima-yi-kill-5";
    private const string DiscardReason = "rule.hand-limit-discard";

    public static void DefinitionAndKillWindowSchema()
    {
        var registry = StandardContentRegistry.CreateWithClassicGenerals();
        Require(registry.Generals[General] is { BaseHp: 4, FactionId: "god" } general &&
                general.SkillIds.SequenceEqual([Renjie, Baiyin]) &&
                registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                registry.Skills[Renjie].Tags.HasFlag(SkillTag.Locked) &&
                registry.Skills[Baiyin].Tags.HasFlag(SkillTag.Awakening),
            "God Sima Yi must join the classic roster with the locked Renjie and the Baiyin awakening.");

        var renjie = registry.Skills[Renjie].Program!;
        var damage = renjie.Triggers.Single(item => item.Id == "damage-ren");
        Require(damage.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                damage.Subject == SkillProgramTriggerSubject.Owner &&
                damage.DamageOccurrence == SkillProgramDamageOccurrence.PerDamagePoint &&
                !damage.Optional &&
                damage.Effects.Single() is { Op: SkillProgramEffectOp.ChangeAttributedMarker } gained &&
                gained.TargetReference!.Kind == ProgramParticipantRef.Owner &&
                gained.Marker == PlayerMarkerKind.Ren && gained.Amount == 1,
            "Renjie must gain one Ren marker on the owner for every damage point suffered.");
        var discard = renjie.Triggers.Single(item => item.Id == "discard-ren");
        Require(discard.Window == SkillProgramTriggerWindow.CardsMoved &&
                discard.Subject == SkillProgramTriggerSubject.Owner &&
                discard.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                discard.MovementOccurrence == SkillProgramMovementOccurrence.PerCard &&
                discard.MovementReasons.SequenceEqual([DiscardReason]) &&
                discard.Effects.Single() is { Op: SkillProgramEffectOp.ChangeAttributedMarker } counted &&
                counted.Marker == PlayerMarkerKind.Ren && counted.Amount == 1,
            "Renjie must count only hand-limit discards, one Ren marker per discarded hand card.");

        var baiyin = registry.Skills[Baiyin].Program!;
        var awakening = baiyin.Triggers.Single();
        Require(awakening.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                awakening.UsageScope == SkillUsageScope.Game && awakening.UsageLimit == 1 &&
                awakening.Condition.Kind == SkillProgramTriggerConditionKind.Compare &&
                awakening.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.ChangeMaximumHp,
                    SkillProgramEffectOp.GrantSkills]) &&
                awakening.Effects.Single(item => item.Op == SkillProgramEffectOp.GrantSkills)
                    .SkillIds.SequenceEqual([Lianpo]),
            "Baiyin must awaken once per game at the owner's turn start, losing one maximum HP and granting Lianpo.");

        var lianpo = registry.Skills[Lianpo].Program!;
        var kill = lianpo.Triggers.Single();
        Require(kill.Window == SkillProgramTriggerWindow.CharacterDied &&
                kill.Optional &&
                kill.Condition.Kind == SkillProgramTriggerConditionKind.DeathKillerIsOwner &&
                kill.Effects.Single().Op == SkillProgramEffectOp.PendExtraTurn,
            "Lianpo must observe deaths the owner caused and offer one extra turn.");

        const string presentation = """
            {"schemaVersion":3,"skills":{"fixture:renjie":{"name":"忍戒","description":"测试"}}}
            """;
        const string damageSkill = """
            {"schemaVersion":62,"skills":[{"id":"fixture:renjie","revision":1,
            "minimumRulesVersion": 177,
            "triggers":[{"id":"watch","window":"afterDamageApplied","subject":"owner",
            "damageOccurrence":"perDamagePoint","optional":false,
            "condition":{"kind":"compare","left":{"kind":"ownerAttributedMarkerCount","marker":"ren"},
            "operator":"greaterThanOrEqual","right":{"kind":"integerConstant","value":1}},
            "effects":[{"op":"changeAttributedMarker","target":"owner",
            "targetRef":{"kind":"owner"},"marker":"ren","amount":1}]}]}]}
            """;
        Require(SkillProgramCatalog.Load(damageSkill, presentation).Programs["fixture:renjie"]
                .Triggers.Single().Effects.Count == 1,
            "An owner-attributed Ren marker composition must be independently definable.");
        Reject(damageSkill.Replace("\"kind\":\"ownerAttributedMarkerCount\",\"marker\":\"ren\"",
                "\"kind\":\"ownerAttributedMarkerCount\""),
            presentation, "ownerAttributedMarkerCount requires its marker");
        Reject(damageSkill.Replace("\"target\":\"owner\"", "\"target\":\"selectedTarget\""),
            presentation, "owner-referenced markers require the owner target");

        const string killSkill = """
            {"schemaVersion":62,"skills":[{"id":"fixture:lianpo","revision":1,
            "minimumRulesVersion": 177,
            "triggers":[{"id":"watch","window":"characterDied","subject":"owner",
            "optional":true,
            "condition":{"kind":"deathKillerIsOwner"},
            "effects":[{"op":"pendExtraTurn","target":"owner"}]}]}]}
            """;
        const string killPresentation = """
            {"schemaVersion":3,"skills":{"fixture:lianpo":{"name":"连破","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(killSkill, killPresentation).Programs["fixture:lianpo"]
                .Triggers.Single().Effects.Single().Op == SkillProgramEffectOp.PendExtraTurn,
            "A killer-side extra-turn composition must be independently definable.");
        Reject(killSkill.Replace("\"window\":\"characterDied\"", "\"window\":\"afterDamageApplied\""),
            killPresentation, "deathKillerIsOwner requires a characterDied trigger");
        Reject(killSkill.Replace("\"op\":\"pendExtraTurn\",\"target\":\"owner\"",
                "\"op\":\"pendExtraTurn\",\"target\":\"selectedTarget\""),
            killPresentation, "an extra turn always belongs to the owner");
    }

    public static void DamageAndDiscardBothGrantRenMarkers()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 150 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            DriveUntil(game, () => TurnsStarted(game) >= 2, DriveMode.BankDuels);
            var banked = RenMarkers(game);
            var bankedDamage = DamageOnShen(game);
            if (banked < 1 || game.CreateSnapshot(0, true).Players[0].Hp < 1) continue;
            var damageSeen = 0;
            DriveUntil(game, () =>
                (damageSeen = DamageOnShen(game) - bankedDamage) >= 1 &&
                RenMarkers(game) >= banked + damageSeen);
            Require(DamageOnShen(game) - bankedDamage >= 1,
                "The AI turns must damage Shen Sima Yi at least once.");
            Require(RenMarkers(game) == banked + (DamageOnShen(game) - bankedDamage),
                $"Every damage point suffered must add exactly one Ren marker: banked={banked}, " +
                $"damage={DamageOnShen(game) - bankedDamage}, markers={RenMarkers(game)}.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a damaging AI turn for the Ren marker test.");
    }

    public static void HandLimitDiscardsGrantRenMarkers()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 60 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision,
                game.PendingDecision!.PromptId)));
            for (var step = 0; step < 24 &&
                 game.PendingDecision is not { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 }; step++)
            {
                if (game.PendingDecision is null) Advance(game);
            }
            if (game.PendingDecision is not { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } discard) continue;
            var required = discard.RequiredCardCount;
            if (required < 1) continue;
            Accept(game.Submit(new DiscardCardsCommand(0,
                discard.ValidCardIds.Take(required).ToArray(), discard.PromptId, game.Revision)));
            var changes = game.Events.Select(item => item.Payload)
                .OfType<PlayerMarkerChangedEvent>()
                .Where(item => item.PlayerSeat == 0 && item.Marker == PlayerMarkerKind.Ren).ToList();
            Require(changes.Count == required && changes.All(item => item.Delta == 1) &&
                    RenMarkers(game) == required,
                "The hand-limit discard must add exactly one Ren marker per discarded card.");
            var movements = game.CardMovements
                .Where(item => item.Reason.Value == DiscardReason &&
                    item.From == CardLocation.Hand(0)).ToList();
            Require(movements.Count >= required,
                "The counted discards must be ordinary hand-limit discard movements.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a hand-limit discard for the Ren marker test.");
    }

    public static void AwakeningAtFourMarkersGrantsLianpo()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 150 && completed < 1; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            DriveUntil(game, () => TurnsStarted(game) >= 2, DriveMode.BankDuels);
            DriveUntil(game, () => IsAwakened(game));
            if (!IsAwakened(game)) continue;
            var awakened = game.Events.Select(item => item.Payload)
                .OfType<SkillAwakenedEvent>()
                .Single(item => item.SkillId == Baiyin);
            Require(awakened.PlayerSeat == 0 && awakened.MaximumHp == 4 &&
                    awakened.AcquiredSkillIds.SequenceEqual([Lianpo]),
                "Baiyin must reduce the maximum HP from five to four and grant Lianpo.");
            Require(game.CreateSnapshot(0, true).Players[0].Skills!
                        .Any(item => item.ContentId == Lianpo) &&
                    RenMarkers(game) >= 4,
                "The awakened Shen Sima Yi must own Lianpo with at least four Ren markers.");
            completed++;
        }
        Require(completed == 1, "No seeded setup reached four Ren markers before a Shen Sima Yi turn.");
    }

    public static void KillGrantsExactlyOneExtraTurnAndReplays()
    {
        var registry = KillRegistry();
        var completed = 0;
        for (var seed = 1; seed <= 150 && completed < 1; seed++)
        {
            var game = Start(registry, seed, KillMode);
            ReachPlay(game);
            if (!DriveToAwakenedPlay(game)) continue;
            var killTurn = game.CreateSnapshot(0, true).TurnNumber;
            var victim = FirstLivingVictim(game, preferred: -1);
            if (victim < 0 || HumanSlashOn(game, victim) is not { } slash) continue;
            Play(game, slash);
            DriveUntil(game, () => false, DriveMode.Pass, stopAtSkills: [Lianpo]);
            if (game.State.Status == EngineStatus.Completed) continue;
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt ||
                prompt.SkillPrompt?.SkillId != Lianpo) continue;
            var checkpoint = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(checkpoint, registry);
            Activate(game, Lianpo);
            Activate(replay, Lianpo);
            DriveUntil(game, () => TurnsStarted(game) >= killTurn + 2);
            DriveUntil(replay, () => TurnsStarted(replay) >= killTurn + 2);
            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The kill, Lianpo activation and extra turn must replay exactly from the checkpoint.");
            var started = game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>().ToList();
            var killIndex = started.FindIndex(item => item.TurnNumber == killTurn);
            Require(killIndex >= 0 && killIndex + 2 < started.Count &&
                    started[killIndex + 1] is { ActorSeat: 0, TurnNumber: var extraNumber } &&
                    extraNumber == killTurn + 1 &&
                    started[killIndex + 2] is { ActorSeat: var nextSeat, TurnNumber: var nextNumber } &&
                    nextSeat != 0 && nextNumber == killTurn + 2,
                $"The kill must yield exactly one extra turn: killTurn={killTurn}, status={game.State.Status}, " +
                "turns=[" + string.Join(",", started.Select(item => $"{item.TurnNumber}:{item.ActorSeat}")) + "].");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced an activated Lianpo extra turn.");
    }

    public static void DeclinedKillKeepsNormalRotation()
    {
        var registry = KillRegistry();
        var completed = 0;
        for (var seed = 1; seed <= 150 && completed < 1; seed++)
        {
            var game = Start(registry, seed, KillMode);
            ReachPlay(game);
            if (!DriveToAwakenedPlay(game)) continue;
            var killTurn = game.CreateSnapshot(0, true).TurnNumber;
            var victim = FirstLivingVictim(game, preferred: -1);
            if (victim < 0 || HumanSlashOn(game, victim) is not { } slash) continue;
            Play(game, slash);
            DriveUntil(game, () => false, DriveMode.Pass, stopAtSkills: [Lianpo]);
            if (game.State.Status == EngineStatus.Completed) continue;
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt ||
                prompt.SkillPrompt?.SkillId != Lianpo) continue;
            var skip = prompt.Choices.Single(item =>
                item.Parameters.GetValueOrDefault("program-action") == "skip");
            Answer(game, skip);
            DriveUntil(game, () => TurnsStarted(game) >= killTurn + 1);
            var started = game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>().ToList();
            var killIndex = started.FindIndex(item => item.TurnNumber == killTurn);
            Require(killIndex >= 0 && killIndex + 1 < started.Count &&
                    started[killIndex + 1] is { ActorSeat: var nextSeat, TurnNumber: var nextNumber } &&
                    nextSeat != 0 && nextNumber == killTurn + 1,
                $"Declining must keep the rotation: killTurn={killTurn}, status={game.State.Status}, " +
                "turns=[" + string.Join(",", started.Select(item => $"{item.TurnNumber}:{item.ActorSeat}")) + "].");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a declined Lianpo prompt.");
    }

    public static void ExtraTurnKillChainsAnotherExtraTurn()
    {
        var registry = KillRegistry();
        var completed = 0;
        for (var seed = 1; seed <= 500 && completed < 1; seed++)
        {
            var game = Start(registry, seed, KillMode);
            ReachPlay(game);
            if (!DriveToAwakenedPlay(game)) continue;
            var killTurn = game.CreateSnapshot(0, true).TurnNumber;
            // First kill: equip the crossbow and bring a wounded victim down.
            var crossbow = game.CreateSnapshot(0, true).Players[0].Hand
                .FirstOrDefault(item => item.Kind == CardKind.Crossbow);
            var firstVictim = WoundedVictim(game, preferred: -1);
            if (crossbow is null || firstVictim < 0) continue;
            var equip = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Equip && action.CardId == crossbow.Id);
            if (equip is null || HumanSlashOn(game, firstVictim) is null) continue;
            Play(game, equip);
            DriveUntil(game, () => AtHumanPlay(game));
            SlashUntilPrompt(game, firstVictim);
            if (game.State.Status == EngineStatus.Completed) continue;
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt ||
                prompt.SkillPrompt?.SkillId != Lianpo) continue;
            Activate(game, Lianpo);
            // The kill only pends the extra turn; drive through the turn end into it.
            DriveUntil(game, () => TurnsStarted(game) >= killTurn + 1 && AtHumanPlay(game));
            var extraTurn = game.CreateSnapshot(0, true).TurnNumber;
            Require(extraTurn == killTurn + 1, "The first extra turn must follow the killing turn.");
            var secondVictim = WoundedVictim(game, preferred: -1);
            if (secondVictim < 0 || HumanSlashOn(game, secondVictim) is null) continue;
            SlashUntilPrompt(game, secondVictim);
            if (game.State.Status == EngineStatus.Completed) continue;
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger } followUpPrompt ||
                followUpPrompt.SkillPrompt?.SkillId != Lianpo) continue;
            Activate(game, followUpPrompt.SkillPrompt!.SkillId);
            DriveUntil(game, () => TurnsStarted(game) >= killTurn + 3);
            var started = game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>().ToList();
            var killIndex = started.FindIndex(item => item.TurnNumber == killTurn);
            Require(killIndex >= 0 && killIndex + 3 < started.Count &&
                    started[killIndex + 1] is { ActorSeat: 0, TurnNumber: var firstExtra } &&
                    firstExtra == killTurn + 1 &&
                    started[killIndex + 2] is { ActorSeat: 0, TurnNumber: var secondExtra } &&
                    secondExtra == killTurn + 2 &&
                    started[killIndex + 3] is { ActorSeat: var afterSeat, TurnNumber: var afterNumber } &&
                    afterSeat != 0 && afterNumber == killTurn + 3,
                $"A kill inside the extra turn must chain into one more extra turn: killTurn={killTurn}, " +
                "turns=[" + string.Join(",", started.Select(item => $"{item.TurnNumber}:{item.ActorSeat}")) + "].");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a chained extra-turn kill.");
    }

    private enum DriveMode { Pass, BankDuels }

    private static bool IsAwakened(GameEngine game) =>
        game.CreateSnapshot(0, true).Players[0].MaxHp == 4;

    private static bool AtHumanPlay(GameEngine game) =>
        game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };

    private static int RenMarkers(GameEngine game) =>
        game.CreateSnapshot(0, true).Players[0].Markers?
            .Where(item => item.Kind == PlayerMarkerKind.Ren).Sum(item => item.Count) ?? 0;

    private static int DamageOnShen(GameEngine game) =>
        game.Events.Select(item => item.Payload)
            .OfType<DamageAppliedEvent>().Where(item => item.TargetSeat == 0)
            .Sum(item => item.Amount);

    private static int TurnsStarted(GameEngine game) =>
        game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>().Count();

    private static int FirstLivingVictim(GameEngine game, int preferred)
    {
        var snapshot = game.CreateSnapshot(0, true);
        if (preferred >= 0 && snapshot.Players[preferred].Hp == 1 &&
            snapshot.Players[preferred].MaxHp <= 4) return preferred;
        // Victims one slash away from death; 1 and 4 sit next to the lord in a full ring.
        foreach (var seat in new[] { 1, 4, 2, 3 })
            if (seat != preferred && snapshot.Players[seat].Hp == 1 &&
                snapshot.Players[seat].MaxHp <= 4) return seat;
        return -1;
    }

    private static LegalAction? HumanSlashOn(GameEngine game, int victimSeat)
    {
        var snapshot = game.CreateSnapshot(0, true);
        return game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash && action.TargetSeat == victimSeat &&
            snapshot.Players[0].Hand.Any(card => card.Id == action.CardId));
    }

    private static int WoundedVictim(GameEngine game, int preferred)
    {
        var snapshot = game.CreateSnapshot(0, true);
        if (preferred >= 0 && snapshot.Players[preferred].Hp > 0 &&
            snapshot.Players[preferred].MaxHp <= 4) return preferred;
        foreach (var seat in new[] { 1, 4, 2, 3 })
            if (seat != preferred && snapshot.Players[seat].Hp > 0 &&
                snapshot.Players[seat].MaxHp <= 4) return seat;
        return -1;
    }

    private static void SlashUntilPrompt(GameEngine game, int targetSeat)
    {
        // The equipped crossbow lifts the once-per-turn limit, so a full-HP
        // four-card victim still falls inside one turn.
        for (var slashIndex = 0; slashIndex < 5; slashIndex++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger }) return;
            if (HumanSlashOn(game, targetSeat) is not { } slash) return;
            Play(game, slash);
            DriveUntil(game, () => AtHumanPlay(game), stopAtSkills: [Lianpo]);
            if (game.State.Status == EngineStatus.Completed) return;
        }
    }

    private static bool DriveToAwakenedPlay(GameEngine game)
    {
        DriveUntil(game, () => TurnsStarted(game) >= 2, DriveMode.BankDuels);
        DriveUntil(game, () => IsAwakened(game));
        if (!IsAwakened(game)) return false;
        DriveUntil(game, () => AtHumanPlay(game));
        return AtHumanPlay(game);
    }

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        DriveMode mode = DriveMode.Pass,
        string[]? stopAtSkills = null,
        int budget = 500)
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
                case DecisionKind.PlayCard when mode == DriveMode.BankDuels:
                    var snapshot = game.CreateSnapshot(0, true);
                    var bankTarget = snapshot.Players
                        .Where(item => item.Seat != 0 && item.Hp > 0)
                        .OrderByDescending(item => item.MaxHp).ThenBy(item => item.Seat)
                        .First().Seat;
                    var actions = game.GetHumanLegalActions();
                    var duel = actions.FirstOrDefault(action =>
                        action.Kind == LegalActionKind.Duel && action.TargetSeat == bankTarget);
                    if (duel is not null && snapshot.Players[0].Hp > 2)
                    {
                        Play(game, duel);
                    }
                    else
                    {
                        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                    }
                    continue;
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
                        item.Parameters.GetValueOrDefault("response") is "take-damage" or "let-die") ??
                        prompt.Choices.FirstOrDefault(item =>
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

    private static ContentRegistry KillRegistry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new KillScenario());

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
        Require(result.Accepted, result.Error?.Message ?? "Shen Sima Yi skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, int seed, string mode = Mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 10
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Shen Sima Yi fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Shen Sima Yi selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            if (game.PendingDecision is { Kind: DecisionKind.SelectFaction, PlayerSeat: 0 } faction)
            {
                var wei = faction.Choices.Single(item =>
                    item.Parameters.GetValueOrDefault("faction-id") == "wei");
                Answer(game, wei);
                continue;
            }
            Advance(game);
        }
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard,
            "Shen Sima Yi fixture did not reach Play.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Shen Sima Yi fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Shen Sima Yi card action failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Shen Sima Yi command failed.");
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
            $"Expected invalid Shen Sima Yi composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shen-sima-yi-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 147, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human lord; seats 1 and 4 sit next to him and are
            // one-hit victims, seats 2 and 3 are healthy duel banks.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-victim-a", "测试受击一",
                "supporter", "standard:none", "qun", BaseHp: 1));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-bystander-b", "测试目标二",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-bystander-c", "测试目标三",
                "supporter", "standard:none", "qun", BaseHp: 8));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-victim-d", "测试受击四",
                "supporter", "standard:none", "qun", BaseHp: 1));
            var cards = Enumerable.Range(0, 180).Select(index => index % 2 == 0
                    ? "standard:slash"
                    : "standard:duel")
                .Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1))
                .ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:shen-deck", "神司马懿测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "神司马懿测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, "fixture:shen-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:shen-victim-a",
                    "fixture:shen-bystander-b",
                    "fixture:shen-bystander-c",
                    "fixture:shen-victim-d"]));
        }
    }

    /// <summary>
    /// Kill-window fixture: three one-HP victims and one healthy duel bank so the
    /// Lianpo chain finds living adjacent targets after the AI turns.
    /// </summary>
    private sealed class KillScenario() : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shen-sima-yi-kill-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 147, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-kill-victim-a", "测试受击一",
                "supporter", "standard:none", "qun", BaseHp: 2));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-kill-victim-b", "测试受击二",
                "supporter", "standard:none", "qun", BaseHp: 2));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-kill-victim-c", "测试受击三",
                "supporter", "standard:none", "qun", BaseHp: 2));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:shen-kill-bystander", "测试目标",
                "supporter", "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index => (index % 3) switch
                {
                    2 => "standard:crossbow",
                    _ => "standard:slash"
                })
                .Select((kind, index) => new ContentDeckPhysicalCard(kind, (Suit)(index % 4), index % 13 + 1))
                .ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:shen-kill-deck", "神司马击杀测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(KillMode, "神司马懿击杀测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 2
                }, "fixture:shen-kill-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:shen-kill-victim-a",
                    "fixture:shen-kill-victim-b",
                    "fixture:shen-kill-victim-c",
                    "fixture:shen-kill-bystander"]));
        }
    }
}
