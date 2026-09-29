using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ChenGongChecks
{
    private const string General = "classic:chen-gong";
    private const string Mingce = "classic:mingce";
    private const string Zhichi = "classic:zhichi";
    private const string MingceMode = "team:classic-chen-gong-mingce-check-4";
    private const string ZhichiMode = "identity:classic-chen-gong-zhichi-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "qun" } general &&
                general.SkillIds.SequenceEqual([Mingce, Zhichi]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Chen Gong must be in the current Qun roster with three HP.");

        var mingce = current.Skills[Mingce].Program!;
        var advise = mingce.Activations.Single();
        Require(advise.Id == "advise" &&
                advise.MinCards == 1 && advise.MaxCards == 1 &&
                advise.SourceZones.SequenceEqual([CardZoneKind.Hand]) &&
                advise.CardCategories.SequenceEqual([SkillProgramCardCategory.Equipment]) &&
                advise.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
                advise.TargetKind == SkillProgramTargetKind.OtherLiving &&
                advise.UsesPerTurn == 1 &&
                advise.Condition.Kind == SkillProgramConditionKind.OwnTurn,
            "Mingce must be a once-per-turn play activation giving one hand equipment or Slash to another player.");
        Require(advise.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.GiveSelected,
                SkillProgramEffectOp.UseDesignatedVirtualSlash,
                SkillProgramEffectOp.Draw]),
            "Mingce must give the card, offer the designated virtual Slash, then keep the fallback draw.");
        Require(advise.Effects[0].Target == SkillProgramEffectTarget.SelectedTarget &&
                advise.Effects[0].Condition.Kind == SkillProgramConditionKind.Always,
            "Mingce must give the selected card to the chosen recipient unconditionally.");
        var designated = advise.Effects[1];
        Require(designated.Target == SkillProgramEffectTarget.SelectedTarget &&
                designated.ResultBind == "designated-slash" &&
                designated.Condition.Kind == SkillProgramConditionKind.Always,
            "The designated virtual Slash must address the recipient and bind its outcome.");
        Require(advise.Effects[2] is
            {
                Target: SkillProgramEffectTarget.SelectedTarget, Amount: 1,
                Condition.Kind: SkillProgramConditionKind.ChoiceIs,
                Condition.SourceBind: "designated-slash",
                Condition.OptionId: "declined"
            },
            "The fallback draw must serve the recipient who declined or had no candidate.");

        var zhichi = current.Skills[Zhichi].Program!;
        var guard = zhichi.BooleanStates.Single();
        Require(guard.Id == "delayed-guard" && !guard.InitialValue,
            "Zhichi must track its turn-scoped guard with one boolean state.");
        var arm = zhichi.Triggers.Single(item => item.Id == "arm-after-out-of-turn-damage");
        Require(arm.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                arm.Subject == SkillProgramTriggerSubject.Owner &&
                arm.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                !arm.Optional &&
                arm.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Not,
                    Children: [{ Kind: SkillProgramTriggerConditionKind.OwnerIsTurnPlayer }]
                } &&
                arm.Effects.Single() is
                { Op: SkillProgramEffectOp.SetBooleanState, StateId: "delayed-guard", BooleanValue: true },
            "Zhichi must arm mandatorily after every out-of-turn damage the owner suffers.");
        var nullify = zhichi.Triggers.Single(item => item.Id == "nullify-targeted-card");
        Require(nullify.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects &&
                nullify.OwnerRelation == SkillProgramCardActionOwnerRelation.Target &&
                !nullify.Optional &&
                nullify.CardKinds.SequenceEqual([
                    CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash,
                    CardKind.Duel, CardKind.DrawTwo, CardKind.BarbarianAssault,
                    CardKind.ArrowBarrage, CardKind.PeachGarden, CardKind.FiveGrains,
                    CardKind.Dismantlement, CardKind.Snatch, CardKind.FireAttack,
                    CardKind.Nullification, CardKind.IronChain, CardKind.BorrowedSword]) &&
                nullify.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.BooleanState,
                    StateId: "delayed-guard",
                    ExpectedValue: true
                } &&
                nullify.Effects.Single() is { Op: SkillProgramEffectOp.NullifyCurrentCardEffect },
            "Zhichi must nullify every targeted Slash or non-delayed trick while the guard is armed.");
        var clear = zhichi.Triggers.Single(item => item.Id == "clear-at-turn-end");
        Require(clear.Window == SkillProgramTriggerWindow.TurnEnding &&
                clear.Subject == SkillProgramTriggerSubject.Owner &&
                clear.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving &&
                !clear.Optional &&
                clear.Effects.Single() is
                { Op: SkillProgramEffectOp.SetBooleanState, StateId: "delayed-guard", BooleanValue: false },
            "Zhichi must clear its guard when the current (another player's) turn ends.");

        const string adviseTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:advise","revision":1,
            "minimumRulesVersion": 191,
            "activations":[{"id":"advise","minCards":1,"maxCards":1,"sourceZones":["hand"],
            "cardCategories":["equipment"],"cardKinds":["slash","fireSlash","thunderSlash"],
            "minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,
            "condition":{"kind":"ownTurn"},"effects":[
            {"op":"giveSelected","target":"selectedTarget"},
            {"op":"useDesignatedVirtualSlash","target":"selectedTarget","resultBind":"designated"},
            {"op":"draw","target":"selectedTarget","amount":1,
            "condition":{"kind":"choiceIs","sourceBind":"designated","optionId":"declined"}}]}]}]}
            """;
        const string advisePresentation = """
            {"schemaVersion":3,"skills":{"fixture:advise":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(adviseTemplate, advisePresentation)
                .Programs["fixture:advise"].Activations.Single().Effects.Count == 3,
            "The Mingce activation composition must load with its union card filter.");
        Reject(adviseTemplate.Replace("\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"]",
                "\"cardKinds\":[]"),
            advisePresentation, "an activation card filter must not declare an empty kind list");
        Reject(adviseTemplate.Replace("\"cardCategories\":[\"equipment\"],", "\"selectedCardsSameSuit\":true,")
                .Replace("\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"],", ""),
            advisePresentation, "an activation card filter must not mix with suit-constrained selections");
        Reject(adviseTemplate.Replace(
                "{\"op\":\"useDesignatedVirtualSlash\",\"target\":\"selectedTarget\",\"resultBind\":\"designated\"}",
                "{\"op\":\"useDesignatedVirtualSlash\",\"target\":\"owner\",\"resultBind\":\"designated\"}"),
            advisePresentation, "the designated virtual Slash must address the selected recipient");
        Reject(adviseTemplate.Replace("\"resultBind\":\"designated\"", ""),
            advisePresentation, "the designated virtual Slash must bind its outcome");
        Reject(adviseTemplate.Replace(
                "{\"op\":\"useDesignatedVirtualSlash\",\"target\":\"selectedTarget\",\"resultBind\":\"designated\"}",
                "{\"op\":\"useDesignatedVirtualSlash\",\"target\":\"selectedTarget\",\"resultBind\":\"designated\",\"condition\":{\"kind\":\"faceDown\"}}"),
            advisePresentation, "the designated virtual Slash must stay unconditional");

        const string guardTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:guard","revision":1,
            "minimumRulesVersion": 191,
            "states":[{"id":"delayed-guard","initialValue":false,"visibility":"public",
            "resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],
            "triggers":[
            {"id":"arm","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage",
            "optional":false,"priority":0,
            "condition":{"kind":"not","children":[{"kind":"ownerIsTurnPlayer"}]},
            "effects":[{"op":"setBooleanState","target":"owner","stateId":"delayed-guard","value":true}]},
            {"id":"nullify","window":"cardUseBeforeTargetEffects","ownerRelation":"target",
            "cardKinds":["slash","duel"],"optional":false,"priority":0,
            "condition":{"kind":"booleanState","stateId":"delayed-guard","expectedValue":true},
            "effects":[{"op":"nullifyCurrentCardEffect","target":"owner"}]},
            {"id":"clear","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving",
            "optional":false,"priority":-100,
            "effects":[{"op":"setBooleanState","target":"owner","stateId":"delayed-guard","value":false}]}]}]}
            """;
        const string guardPresentation = """
            {"schemaVersion":3,"skills":{"fixture:guard":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(guardTemplate, guardPresentation)
                .Programs["fixture:guard"].Triggers.Count == 3,
            "The Zhichi trigger trio must load with its mandatory windows.");
        Reject(guardTemplate.Replace(
                "{\"op\":\"nullifyCurrentCardEffect\",\"target\":\"owner\"}",
                "{\"op\":\"nullifyCurrentCardEffect\",\"target\":\"selectedTarget\"}"),
            guardPresentation, "the nullification must protect the owner only");
        Reject(guardTemplate.Replace("\"damageOccurrence\":\"perDamage\",", ""),
            guardPresentation, "an after-damage trigger must declare its damage occurrence");
    }

    public static void MingceGivesCardThenDesignatedSlashOrFallbackDrawAndReplays()
    {
        var registry = Registry(SlashDuelDeck(), bankHp: 8);
        var used = 0;
        for (var seed = 1; seed <= 400 && used < 1; seed++)
        {
            MingceOutcome outcome;
            try
            {
                outcome = RunMingce(registry, seed, wantDeclined: false);
            }
            catch (SkipSeedException)
            {
                continue;
            }
            used++;
            Require(outcome.UsedSlash, "The hostile designation must end in the used-slash branch.");
            AssertMingceOutcome(outcome);
        }
        Require(used >= 1, "No seed produced the hostile designated-Slash branch.");
        var declined = 0;
        for (var seed = 1; seed <= 400 && declined < 1; seed++)
        {
            MingceOutcome outcome;
            try
            {
                outcome = RunMingce(registry, seed, wantDeclined: true);
            }
            catch (SkipSeedException)
            {
                continue;
            }
            declined++;
            Require(!outcome.UsedSlash, "The ally designation must end in the declined branch.");
            AssertMingceOutcome(outcome);
        }
        Require(declined >= 1, "No seed produced the declined designated-Slash branch.");
        Require(used >= 1 && declined >= 1,
            $"No seed pair produced both Mingce branches (used={used}, declined={declined}).");
    }

    private static void AssertMingceOutcome(MingceOutcome outcome)
    {
        var game = outcome.Game;
            var recipient = outcome.RecipientSeat;
            Require(game.CardMovements.Any(item =>
                    item.CardId == outcome.GivenCardId &&
                    item.From == CardLocation.Hand(0) &&
                    item.To == CardLocation.Hand(recipient) &&
                    item.Reason.Value.Contains(Mingce, StringComparison.Ordinal)),
                "Mingce must move the given card into the recipient's hand by the skill.");
            var choices = game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
                .Where(item => item.SkillId == Mingce).ToArray();
            Require(choices.Any(item => item.ResultBind == "designated-slash-victim") ==
                    outcome.UsedSlashOrDesignated &&
                    choices.Any(item => item.ResultBind == "designated-slash"),
                "Mingce must record the designation and the recipient's answer as choice bindings.");
            var answer = choices.Single(item => item.ResultBind == "designated-slash");
            if (outcome.UsedSlash)
            {
                Require(answer.OptionId == "used-slash" && answer.ChooserSeat == recipient,
                    "The accepting recipient must own the used-slash binding.");
                var declared = game.Events.Select(item => item.Payload).OfType<CardUseDeclaredEvent>()
                    .Single(item => item.CardKind == CardKind.Slash && item.SourceSeat == recipient);
                Require(declared.CardId == 0,
                    "The designated Slash must be a cardless virtual use by the recipient.");
                Require(game.Events.Select(item => item.Payload).OfType<CardUsedEvent>()
                        .Any(item => item.CardKind == CardKind.Slash &&
                            item.SourceSeat == recipient && item.TargetSeat == outcome.VictimSeat) &&
                    game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                        .Any(item => item.SourceSeat == recipient && item.TargetSeat == outcome.VictimSeat),
                    "The dodge-less fixture must let the virtual Slash damage the designated victim.");
            }
            else
            {
                Require(answer.OptionId == "declined" && answer.ChooserSeat == recipient,
                    "The declining recipient must own the declined binding.");
                var afterHand = game.CreateSnapshot(recipient, true).Players[recipient].Hand;
                Require(afterHand.Any(card => card.Id == outcome.GivenCardId),
                    "The given card must stay in the recipient's hand after the fallback draw.");
            }
            Require(outcome.ReplayEvents.SequenceEqual(Events(game)) &&
                    outcome.ReplayState == State(game),
                "The Mingce flow must replay identically from the paused designation prompt.");
    }

    public static void ZhichiNullifiesAfterOutOfTurnDamageUntilTurnEnd()
    {
        var registry = Registry(SlashDuelDeck(), bankHp: 3);
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            ZhichiOutcome outcome;
            try
            {
                outcome = RunZhichi(registry, seed);
            }
            catch (SkipSeedException)
            {
                continue;
            }
            completed++;
            var game = outcome.Game;
            Require(outcome.NullifyEvents.Count >= 1,
                "Zhichi must nullify a targeted card after the out-of-turn damage.");
            var nullify = outcome.NullifyEvents[0];
            Require(nullify.Payload.OwnerSeat == 0 && nullify.Payload.SourceSeat == outcome.TurnPlayerSeat &&
                    nullify.Payload.SkillId == Zhichi &&
                    nullify.Payload.CardKind is CardKind.Slash or CardKind.Duel or
                        CardKind.FireSlash or CardKind.ThunderSlash,
                "The nullified card must be the turn player's Slash or non-delayed trick at Chen Gong.");
            var damages = game.Events
                .Where(item => item.Payload is DamageAppliedEvent hit && hit.TargetSeat == 0)
                .Select(item => (item.Sequence, Hit: (DamageAppliedEvent)item.Payload))
                .ToArray();
            Require(damages.Length >= 2 &&
                    damages[0].Sequence == outcome.ArmingDamageSequence &&
                    damages[0].Hit.SourceSeat == outcome.TurnPlayerSeat &&
                    damages.Count(item => item.Sequence < outcome.LaterTurnHitSequence) == 1 &&
                    nullify.Sequence > outcome.ArmingDamageSequence,
                "Exactly one damage must precede the nullification inside the arming turn.");
            Require((outcome.LaterTurnNumber, outcome.LaterTurnSeat) !=
                    (outcome.ArmingTurnNumber, outcome.ArmingTurnSeat) &&
                    damages.Any(item => item.Sequence == outcome.LaterTurnHitSequence) &&
                    outcome.LaterTurnHitSequence > nullify.Sequence,
                "A later turn's card must damage Chen Gong again, proving the guard cleared at turn end.");
            Require(outcome.ReplayEvents.SequenceEqual(Events(game)) &&
                    outcome.ReplayState == State(game),
                "The Zhichi turn must replay identically from the checkpoint taken after the arming damage.");
        }
        Require(completed == 1, "No seeded setup resolved the Zhichi arm-nullify-clear arc.");
    }

    private sealed record MingceOutcome(
        GameEngine Game, bool UsedSlash, bool UsedSlashOrDesignated,
        int RecipientSeat, int VictimSeat, int GivenCardId,
        string[] ReplayEvents, string ReplayState);

    private sealed class ZhichiOutcome
    {
        public GameEngine Game { get; set; } = null!;
        public int TurnPlayerSeat { get; set; } = -1;
        public long ArmingDamageSequence { get; set; } = -1;
        public int ArmingTurnNumber { get; set; } = -1;
        public int ArmingTurnSeat { get; set; } = -1;
        public long LaterTurnHitSequence { get; set; } = -1;
        public int LaterTurnNumber { get; set; } = -1;
        public int LaterTurnSeat { get; set; } = -1;
        public string[] ReplayEvents { get; set; } = [];
        public string ReplayState { get; set; } = "";
        public List<(long Sequence, ProgramCardEffectNullifiedEvent Payload)> NullifyEvents { get; } = [];
    }

    private static MingceOutcome RunMingce(ContentRegistry registry, int seed, bool wantDeclined)
    {
        var game = Start(registry, seed, MingceMode);
        DriveUntil(game, stopAtDecisions: [DecisionKind.PlayCard]);
        var prompt = game.PendingDecision;
        if (prompt is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
            throw new SkipSeedException("no human play phase");
        var action = game.GetHumanLegalActions().SingleOrDefault(candidate =>
            candidate.Kind == LegalActionKind.UseProgramSkill &&
            candidate.ProgramSkillId == Mingce && candidate.ProgramActivationId == "advise");
        if (action is null || action.SelectableCardIds.Count == 0 || action.SelectableTargetSeats.Count == 0)
            throw new SkipSeedException("Mingce action unavailable");
        var recipientSeat = action.SelectableTargetSeats[0];
        var givenCardId = action.SelectableCardIds[0];
        var paused = RoundTrip(game.CreateCheckpoint());
        var replay = GameReplay.Restore(paused, registry);
        var used = game.Submit(new UseProgramSkillCommand(0, Mingce, "advise",
            [givenCardId], [recipientSeat], game.Revision, prompt.PromptId));
        Require(used.Accepted, used.Error?.Message ?? "Mingce activation failed.");
        var replayUsed = replay.Submit(new UseProgramSkillCommand(0, Mingce, "advise",
            [givenCardId], [recipientSeat], replay.Revision, replay.PendingDecision!.PromptId));
        Require(replayUsed.Accepted, replayUsed.Error?.Message ?? "Replay Mingce activation failed.");

        // Both copies now sit at the owner's designation prompt. The fixture
        // picks a candidate on the requested side of the recipient's public
        // team so the shared hostility answer is exercised deterministically.
        if (game.PendingDecision is not { PlayerSeat: 0 } designation ||
            designation.SkillPrompt?.SkillId != Mingce)
            throw new SkipSeedException("no designation candidates");
        var view = game.CreateSnapshot(0, true);
        var recipientTeam = view.Players[recipientSeat].TeamId ??
            throw new InvalidOperationException("The mingce fixture lost its public teams.");
        var wanted = designation.Choices.FirstOrDefault(choice => wantDeclined
            ? view.Players[choice.Targets[0]].TeamId == recipientTeam
            : view.Players[choice.Targets[0]].TeamId != recipientTeam);
        if (wanted is null)
            throw new SkipSeedException("no candidate on the requested side of the recipient's team");
        var victimSeat = wanted.Targets[0];
        Accept(game.Submit(new AnswerPromptCommand(0, designation.PromptId, wanted.Id, game.Revision)));
        var replayDesignation = replay.PendingDecision ??
            throw new InvalidOperationException("The replay lost the designation prompt.");
        Accept(replay.Submit(new AnswerPromptCommand(0, replayDesignation.PromptId,
            replayDesignation.Choices.Single(item => item.Id == wanted.Id).Id, replay.Revision)));
        DriveUntilSettled(game);
        DriveUntilSettled(replay);
        var events = Events(game);
        Require(events.SequenceEqual(Events(replay)) && State(game) == State(replay),
            "The parallel Mingce replay must stay identical after settling.");
        var answer = game.Events.Select(item => item.Payload).OfType<ProgramOptionChosenEvent>()
            .SingleOrDefault(item => item.SkillId == Mingce && item.ResultBind == "designated-slash");
        Require(answer is not null,
            "The designated virtual Slash must record the recipient's answer.");
        Require(answer!.ChooserSeat == recipientSeat,
            "The recipient must own the designated-slash answer binding.");
        var usedSlash = answer.OptionId == "used-slash";
        return new MingceOutcome(game, usedSlash, true,
            recipientSeat, victimSeat, givenCardId, events, State(game));
    }

    private static ZhichiOutcome RunZhichi(ContentRegistry registry, int seed)
    {
        var game = Start(registry, seed, ZhichiMode);
        DriveUntil(game, stopAtDecisions: [DecisionKind.PlayCard], budget: 400);
        if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
            throw new SkipSeedException("no human play phase");
        // End Chen Gong's first turn immediately so every later damage is
        // out-of-turn and the deck's Duels are lost without prompts.
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        DiscardEverything(game);
        var outcome = new ZhichiOutcome();
        GameCheckpoint? resume = null;
        for (var step = 0; step < 4200; step++)
        {
            if (game.State.Status == EngineStatus.Completed) break;
            var damage = game.Events
                .Where(item => item.Payload is DamageAppliedEvent hit && hit.TargetSeat == 0)
                .Select(item => (item.Sequence, Hit: (DamageAppliedEvent)item.Payload))
                .ToArray();
            var nullifies = game.Events
                .Where(item => item.Payload is ProgramCardEffectNullifiedEvent hit && hit.SkillId == Zhichi)
                .Select(item => (item.Sequence, Payload: (ProgramCardEffectNullifiedEvent)item.Payload))
                .ToList();
            if (resume is null && damage.Length > 0 && nullifies.Count == 0)
            {
                // The arming damage just happened inside an AI turn; freeze here.
                resume = game.CreateCheckpoint();
                outcome.TurnPlayerSeat = damage[^1].Hit.SourceSeat;
                outcome.ArmingDamageSequence = damage[^1].Sequence;
                var snapshot = game.CreateSnapshot(0);
                outcome.ArmingTurnNumber = snapshot.TurnNumber;
                outcome.ArmingTurnSeat = snapshot.CurrentSeat;
            }
            foreach (var nullify in nullifies)
                if (!outcome.NullifyEvents.Any(item => item.Sequence == nullify.Sequence))
                    outcome.NullifyEvents.Add(nullify);
            if (outcome.NullifyEvents.Count > 0 && damage.Length > 0 &&
                damage[^1].Sequence > outcome.NullifyEvents[0].Sequence)
            {
                var snapshot = game.CreateSnapshot(0);
                if ((snapshot.TurnNumber, snapshot.CurrentSeat) !=
                    (outcome.ArmingTurnNumber, outcome.ArmingTurnSeat))
                {
                    outcome.LaterTurnHitSequence = damage[^1].Sequence;
                    outcome.LaterTurnNumber = snapshot.TurnNumber;
                    outcome.LaterTurnSeat = snapshot.CurrentSeat;
                    break;
                }
            }
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat == 0)
            {
                switch (prompt.Kind)
                {
                    case DecisionKind.PlayCard:
                        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                        DiscardEverything(game);
                        continue;
                    case DecisionKind.DiscardCards:
                        DiscardEverything(game);
                        continue;
                    default:
                        AnswerHumanPrompt(game, prompt);
                        continue;
                }
            }
            Advance(game);
        }
        if (resume is null || outcome.NullifyEvents.Count == 0 || outcome.LaterTurnHitSequence < 0)
            throw new SkipSeedException("no arm-nullify-clear arc");
        // Replay the frozen arming checkpoint to completion and compare.
        var restored = GameReplay.Restore(RoundTrip(resume), registry);
        for (var step = 0; step < 4200; step++)
        {
            if (restored.State.Status == EngineStatus.Completed) break;
            if (restored.Events.Any(item =>
                    item.Payload is DamageAppliedEvent hit && hit.TargetSeat == 0 &&
                    item.Sequence >= outcome.LaterTurnHitSequence)) break;
            var prompt = restored.PendingDecision;
            if (prompt is null)
            {
                Advance(restored);
                continue;
            }
            if (prompt.PlayerSeat == 0)
            {
                switch (prompt.Kind)
                {
                    case DecisionKind.PlayCard:
                        Accept(restored.Submit(new EndPlayPhaseCommand(0, restored.Revision, prompt.PromptId)));
                        DiscardEverything(restored);
                        continue;
                    case DecisionKind.DiscardCards:
                        DiscardEverything(restored);
                        continue;
                    default:
                        AnswerHumanPrompt(restored, prompt);
                        continue;
                }
            }
            Advance(restored);
        }
        Require(Events(restored).SequenceEqual(Events(game)) && State(restored) == State(game),
            "The restored Zhichi arc must replay identically.");
        outcome.ReplayEvents = Events(game);
        outcome.ReplayState = State(game);
        outcome.Game = game;
        return outcome;
    }

    private static void AnswerHumanPrompt(GameEngine game, PendingDecision prompt)
    {
        PromptChoice choice = prompt.Kind switch
        {
            // Losing the Duel without prompts is the fixture's arming path.
            DecisionKind.RespondSlash => prompt.Choices.FirstOrDefault(item => item.Cards.Count == 0) ??
                prompt.Choices.First(),
            DecisionKind.RespondDodge => prompt.Choices.FirstOrDefault(item => item.Cards.Count == 0) ??
                prompt.Choices.First(),
            _ => prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                prompt.Choices.First()
        };
        Accept(game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision)));
    }

    private static void DiscardEverything(GameEngine game)
    {
        for (var guard = 0; guard < 24; guard++)
        {
            if (game.PendingDecision is not { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } prompt)
                return;
            Accept(game.Submit(new DiscardCardsCommand(0,
                prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                prompt.PromptId, game.Revision)));
        }
        throw new InvalidOperationException("Chen Gong could not finish his discard phase.");
    }

    private sealed class SkipSeedException(string reason) : InvalidOperationException(reason);

    private static void DriveUntil(
        GameEngine game,
        string[]? stopAtSkills = null,
        DecisionKind[]? stopAtDecisions = null,
        int budget = 900)
    {
        for (var step = 0; step < budget && game.State.Status != EngineStatus.Completed; step++)
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
            if (stopAtDecisions is not null && stopAtDecisions.Contains(prompt.Kind) &&
                prompt.PlayerSeat == 0)
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
                    DiscardEverything(game);
                    continue;
                default:
                    AnswerHumanPrompt(game, prompt);
                    continue;
            }
        }
        throw new SkipSeedException("drive budget exhausted");
    }

    private static void DriveUntilSettled(GameEngine game)
    {
        for (var step = 0; step < 400 && game.ResolutionStack.Count != 0; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat == 0)
            {
                AnswerHumanPrompt(game, prompt);
                continue;
            }
            Advance(game);
        }
    }

    private static ContentRegistry Registry(ContentDeckRecipe deck, int bankHp) =>
        ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new ChenGongScenario(MingceMode, ZhichiMode, deck, bankHp));

    private static ContentDeckRecipe SlashDuelDeck()
    {
        var cards = new List<ContentDeckPhysicalCard>();
        for (var index = 0; index < 180; index++)
        {
            var kind = index % 2 == 0 ? "standard:slash" : "standard:duel";
            cards.Add(new ContentDeckPhysicalCard(kind, Suit.Club, index % 13 + 1));
        }
        return new ContentDeckRecipe("fixture:chen-gong-deck", "陈宫测试牌堆", 5, 2, [])
        {
            PhysicalCards = cards.ToArray()
        };
    }

    private static GameEngine Start(ContentRegistry registry, int seed, string mode)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = mode == MingceMode ? 4 : 5,
            HumanSeat = 0,
            HumanRole = mode == MingceMode ? null : Role.Lord,
            HumanTeamId = mode == MingceMode ? "team:blue" : null,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false,
            MaxTurns = 40
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Chen Gong fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Chen Gong selection failed.");
        return game;
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Chen Gong fixture did not advance.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Chen Gong command failed.");
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
            $"Expected invalid Chen Gong composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class ChenGongScenario(
        string mingceModeId, string zhichiModeId, ContentDeckRecipe deck, int bankHp) : IGameContentPackage
    {
        private static readonly string[] PoolIds =
        [
            General,
            "fixture:chen-gong-bank-a",
            "fixture:chen-gong-bank-b",
            "fixture:chen-gong-bank-c",
            "fixture:chen-gong-bank-d"
        ];

        public PackageManifest Manifest { get; } = new("chen-gong-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 155, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human Chen Gong; the other seats are skill-less AI
            // banks. The mingce mode is a public 2v2 so the recipient's hostility
            // toward a designated victim is readable from the snapshot teams;
            // the zhichi mode is a lord-vs-rebels identity chase.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:chen-gong-bank-a", "测试对手一",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:chen-gong-bank-b", "测试对手二",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:chen-gong-bank-c", "测试对手三",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:chen-gong-bank-d", "测试对手四",
                "supporter", "standard:none", "qun", BaseHp: bankHp));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(mingceModeId, "陈宫明策测试", 4, 4,
                new Dictionary<string, int>(), deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: PoolIds, ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int>
                {
                    ["team:blue"] = 2,
                    ["team:red"] = 2
                }));
            builder.AddMode(new ContentModeDefinition(zhichiModeId, "陈宫智迟测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Rebel)] = 4
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: PoolIds));
        }
    }
}
