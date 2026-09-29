using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DongZhuoChecks
{
    private const string General = "classic:dong-zhuo";
    private const string Jiuchi = "classic:jiuchi";
    private const string Roulin = "classic:roulin";
    private const string Benghuai = "classic:benghuai";
    private const string Baonve = "classic:baonve";
    private const string FemaleBank = "fixture:dong-zhuo-bank-female";
    private const string JiuchiMode = "identity:classic-dong-zhuo-jiuchi-check-5";
    private const string RoulinMode = "identity:classic-dong-zhuo-roulin-check-5";
    private const string BaonveMode = "identity:classic-dong-zhuo-baonve-check-5";
    private const string BaonveNegativeMode = "identity:classic-dong-zhuo-negative-check-5";
    private const string BenghuaiQuietMode = "identity:classic-dong-zhuo-quiet-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 8, FactionId: "qun" } general &&
                general.SkillIds.SequenceEqual([Jiuchi, Roulin, Benghuai, Baonve]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Dong Zhuo must be in the current Qun roster with eight HP.");

        // The Liu Shan batch shares these primitives; the numeric values are the
        // merge contract and must not drift between the two batches.
        Require((int)SkillProgramNumberExpression.LivingPlayersMinHp == 14,
            "livingPlayersMinHp must stay number expression 14 for the cross-batch convention.");
        Require((int)SkillProgramTriggerValueKind.LivingPlayersMinHp == 25,
            "livingPlayersMinHp must stay trigger value 25 for the cross-batch convention.");
        Require((int)SkillProgramTriggerConditionKind.DamageSourceIsOwner == 25 &&
                (int)SkillProgramTriggerConditionKind.DamageSourceFactionIs == 26,
            "The damage-source trigger conditions must keep values 25 and 26.");
        Require((int)SkillProgramTargetKind.EventSource == 25,
            "The eventSource selectTarget kind must stay 25.");
        Require((int)SkillProgramConditionKind.EventTargetGenderIs == 30 &&
                (int)SkillProgramConditionKind.EventSourceGenderIs == 31,
            "The participant gender conditions must keep values 30 and 31.");
        Require((int)SkillProgramCardPolicyKind.MinimumResponseCountAsTarget == 17,
            "minimumResponseCountAsTarget must stay card policy 17.");

        var jiuchi = current.Skills[Jiuchi].Program!;
        var conversion = jiuchi.ViewAs.Single();
        Require(conversion.Id == "spade-hand-as-alcohol" &&
                conversion.InputKinds.Count == 0 &&
                conversion.InputSuits.SequenceEqual([Suit.Spade]) &&
                conversion.OutputKind == CardKind.Alcohol &&
                conversion.ForPlay && conversion.ForResponse &&
                conversion.InputCount == 1 &&
                conversion.SourceZones.SequenceEqual([CardZoneKind.Hand]),
            "Jiuchi must convert one spade hand card into Alcohol for play and dying rescue.");
        Require(jiuchi.Activations.Count == 0 && jiuchi.Triggers.Count == 0 &&
                jiuchi.CardPolicies.Count == 0,
            "Jiuchi must stay a pure viewAs skill.");

        var roulin = current.Skills[Roulin].Program!;
        var attackerSide = roulin.CardPolicies.Single(policy =>
            policy.Kind == SkillProgramCardPolicyKind.MinimumResponseCount);
        Require(attackerSide.Id == "female-target-needs-two-dodges" &&
                attackerSide.Value == 2 &&
                attackerSide.CardKinds.SequenceEqual(
                    [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
                attackerSide.RequiredCardKinds.SequenceEqual([CardKind.Dodge]) &&
                attackerSide.Condition is
                {
                    Kind: SkillProgramConditionKind.EventTargetGenderIs,
                    Gender: GeneralGender.Female
                },
            "Roulin must demand two dodges when the slash target is female.");
        var defenderSide = roulin.CardPolicies.Single(policy =>
            policy.Kind == SkillProgramCardPolicyKind.MinimumResponseCountAsTarget);
        Require(defenderSide.Id == "female-source-slash-needs-two-dodges" &&
                defenderSide.Value == 2 &&
                defenderSide.CardKinds.SequenceEqual(
                    [CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
                defenderSide.RequiredCardKinds.SequenceEqual([CardKind.Dodge]) &&
                defenderSide.Condition is
                {
                    Kind: SkillProgramConditionKind.EventSourceGenderIs,
                    Gender: GeneralGender.Female
                },
            "Roulin must demand two dodges when a female slashes Dong Zhuo.");

        var benghuai = current.Skills[Benghuai].Program!;
        var decay = benghuai.Triggers.Single();
        Require(decay.Id == "benghuai-decay" &&
                decay.Window == SkillProgramTriggerWindow.TurnEnding &&
                decay.Subject == SkillProgramTriggerSubject.Owner &&
                !decay.Optional &&
                decay.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Compare,
                    Comparison: SkillProgramComparisonOperator.GreaterThan,
                    Left.Kind: SkillProgramTriggerValueKind.CurrentHp,
                    Right.Kind: SkillProgramTriggerValueKind.LivingPlayersMinHp
                },
            "Benghuai must be a locked end-phase decay that fires only when someone has lower HP.");
        Require(decay.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.ChooseOption,
            SkillProgramEffectOp.LoseHp,
            SkillProgramEffectOp.ChangeMaximumHp]),
            "Benghuai must offer losing one HP or reducing one maximum HP.");
        Require(decay.Effects[0].Target == SkillProgramEffectTarget.Owner &&
                decay.Effects[0].ResultBind == "benghuai-choice" &&
                decay.Effects[0].Options.Select(item => item.Id).SequenceEqual(
                    ["lose-hp", "reduce-max-hp"]),
            "Benghuai must present exactly the two official options.");
        Require(decay.Effects[1] is { Amount: 1 } loseHp &&
                loseHp.Condition is
                {
                    Kind: SkillProgramConditionKind.ChoiceIs,
                    SourceBind: "benghuai-choice",
                    OptionId: "lose-hp"
                } &&
                decay.Effects[2] is { Amount: -1 } reduceMaxHp &&
                reduceMaxHp.Condition is
                {
                    Kind: SkillProgramConditionKind.ChoiceIs,
                    SourceBind: "benghuai-choice",
                    OptionId: "reduce-max-hp"
                },
            "Each Benghuai option must gate exactly its own branch.");

        var baonve = current.Skills[Baonve].Program!;
        var prompt = baonve.Triggers.Single(item => item.Id == "baonve-judge-prompt");
        Require(prompt.Window == SkillProgramTriggerWindow.AfterDamageApplied &&
                prompt.Subject == SkillProgramTriggerSubject.Any &&
                !prompt.Optional &&
                prompt.DamageOccurrence == SkillProgramDamageOccurrence.PerDamage &&
                ConditionHas(prompt.Condition, SkillProgramTriggerConditionKind.DamageSourceIsOwner) &&
                ConditionHas(prompt.Condition, SkillProgramTriggerConditionKind.DamageSourceFactionIs),
            "Baonve must ask after damage from another qun character.");
        Require(FindCondition(prompt.Condition, SkillProgramTriggerConditionKind.DamageSourceFactionIs)!
                .Factions.SequenceEqual(["qun"]),
            "The Baonve faction gate must list exactly the qun faction.");
        Require(prompt.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.ChooseOption,
            SkillProgramEffectOp.SelectTarget,
            SkillProgramEffectOp.StartJudgment,
            SkillProgramEffectOp.MoveBoundCards]),
            "Baonve must let the damage source choose, then judge that source.");
        var ask = prompt.Effects[0];
        Require(ask.ChooserRef is { Kind: ProgramParticipantRef.EventSource } &&
                ask.ResultBind == "baonve-choice" &&
                ask.Options.Select(item => item.Id).SequenceEqual(["judge", "no-judge"]),
            "Baonve must let the non-holder damage source decide whether to judge.");
        Require(prompt.Effects[1].TargetKind == SkillProgramTargetKind.EventSource,
            "Baonve must select the damage source as the judged player.");
        var judgment = prompt.Effects[2];
        Require(judgment.Target == SkillProgramEffectTarget.SelectedTarget &&
                judgment.JudgmentReason == "skill.classic.baonve" &&
                judgment.ResultBind == "baonve-judgment" &&
                judgment.SourceRef is { Kind: ProgramParticipantRef.EventSource } &&
                judgment.Condition is
                {
                    Kind: SkillProgramConditionKind.ChoiceIs,
                    SourceBind: "baonve-choice",
                    OptionId: "judge"
                },
            "The Baonve judgment must run on the judge branch with the source as its source.");
        var cleanup = prompt.Effects[3];
        Require(cleanup.SourceBind == "baonve-judgment" &&
                cleanup.Destination == SkillProgramCardDestination.DiscardPile &&
                cleanup.Condition is
                {
                    Kind: SkillProgramConditionKind.ChoiceIs,
                    SourceBind: "baonve-choice",
                    OptionId: "judge"
                },
            "The Baonve judgment card must be discarded on the judge branch.");
        var spade = baonve.Triggers.Single(item => item.Id == "baonve-spade-recover");
        Require(spade.Window == SkillProgramTriggerWindow.JudgmentFinalized &&
                !spade.Optional &&
                spade.Subject == SkillProgramTriggerSubject.Any &&
                spade.JudgmentReasons.SequenceEqual(["skill.classic.baonve"]) &&
                spade.Suits.SequenceEqual([Suit.Spade]) &&
                spade.Effects.Single() is { Op: SkillProgramEffectOp.Recover, Amount: 1 } recover &&
                recover.Target == SkillProgramEffectTarget.Owner,
            "A spade Baonve judgment must recover the owner by one.");

        Require(current.Skills[Jiuchi].Tags == SkillTag.None &&
                current.Skills[Roulin].Tags == SkillTag.Locked &&
                current.Skills[Benghuai].Tags == SkillTag.Locked &&
                current.Skills[Baonve].Tags == SkillTag.Lord,
            "The Dong Zhuo skill tags must match the official locked and lord skills.");

        const string roulinTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:roulin","revision":1,
            "minimumRulesVersion": 186,
            "cardPolicies":[{"id":"p","kind":"minimumResponseCount",
            "cardKinds":["slash"],"requiredCardKinds":["dodge"],"value":2,
            "condition":{"kind":"eventTargetGenderIs","gender":"female"}}]}]}
            """;
        const string roulinPresentation = """
            {"schemaVersion":3,"skills":{"fixture:roulin":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(roulinTemplate, roulinPresentation)
                .Programs["fixture:roulin"].CardPolicies.Count == 1,
            "A gendered minimum response count policy must be definable.");
        Reject(roulinTemplate.Replace(",\"gender\":\"female\"", ""),
            roulinPresentation, "a participant gender condition without a gender");
        Reject(roulinTemplate.Replace("\"requiredCardKinds\":[\"dodge\"],", ""),
            roulinPresentation, "a minimum response count policy without required card kinds");

        const string baonveTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:baonve","revision":1,
            "minimumRulesVersion": 186,
            "triggers":[{"id":"t","window":"afterDamageApplied","subject":"any",
            "damageOccurrence":"perDamage","optional":false,
            "condition":{"kind":"all","children":[
            {"kind":"not","children":[{"kind":"damageSourceIsOwner"}]},
            {"kind":"damageSourceFactionIs","factions":["qun"]}]},
            "effects":[
            {"op":"chooseOption","target":"owner","chooserRef":{"kind":"eventSource"},
            "resultBind":"choice","options":[{"id":"judge","condition":{"kind":"always"}},
            {"id":"no-judge","condition":{"kind":"always"}}]},
            {"op":"selectTarget","target":"owner","targetKind":"eventSource"},
            {"op":"startJudgment","target":"selectedTarget","judgmentReason":"fixture.baonve",
            "resultBind":"judgment","visibility":"public","sourceRef":{"kind":"eventSource"},
            "condition":{"kind":"choiceIs","sourceBind":"choice","optionId":"judge"}},
            {"op":"moveBoundCards","target":"owner","sourceBind":"judgment",
            "destination":"discardPile"}]}]}]}
            """;
        const string baonvePresentation = """
            {"schemaVersion":3,"skills":{"fixture:baonve":{"name":"测试","description":"测试",
            "optionLabels":{"judge":"判定","no-judge":"不判定"}}}}
            """;
        Require(SkillProgramCatalog.Load(baonveTemplate, baonvePresentation)
                .Programs["fixture:baonve"].Triggers.Count == 1,
            "A non-holder-gated conditional judgment must be definable.");
        Reject(baonveTemplate.Replace("\"window\":\"afterDamageApplied\"", "\"window\":\"drawPhase\""),
            baonvePresentation, "a damage-source faction gate outside the damage window");
        Reject(baonveTemplate.Replace(",\"factions\":[\"qun\"]", ""),
            baonvePresentation, "a faction gate without factions");
        Reject(baonveTemplate.Replace(
                "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"choice\",\"optionId\":\"judge\"}",
                "\"condition\":{\"kind\":\"wounded\"}"),
            baonvePresentation, "a judgment gated by an arbitrary condition");

        const string decayTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:decay","revision":1,
            "minimumRulesVersion": 186,
            "triggers":[{"id":"t","window":"turnEnding","subject":"owner","optional":false,
            "condition":{"kind":"compare","left":{"kind":"currentHp"},"operator":"greaterThan",
            "right":{"kind":"livingPlayersMinHp"}},
            "effects":[{"op":"chooseOption","target":"owner","resultBind":"c",
            "options":[{"id":"a","condition":{"kind":"always"}}]}]}]}]}
            """;
        const string decayPresentation = """
            {"schemaVersion":3,"skills":{"fixture:decay":{"name":"测试","description":"测试",
            "optionLabels":{"a":"甲"}}}}
            """;
        Require(SkillProgramCatalog.Load(decayTemplate, decayPresentation)
                .Programs["fixture:decay"].Triggers.Count == 1,
            "A livingPlayersMinHp decay gate must be definable.");
    }

    public static void JiuchiPlaysSpadeHandCardAsAlcoholAndReplays()
    {
        var registry = Registry(JiuchiMode, PlayDeck());
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, JiuchiMode, 12);
            if (!DriveToFirstPlay(game)) continue;
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var spade = hand.FirstOrDefault(card => card.Suit == Suit.Spade);
            var heart = hand.FirstOrDefault(card => card.Suit == Suit.Heart);
            if (spade is null || heart is null) continue;

            var actions = game.GetHumanLegalActions()
                .Where(item => item.Kind == LegalActionKind.Alcohol).ToArray();
            if (actions.Length == 0) continue;
            Require(actions.All(item => item.PlayedCardKind == CardKind.Alcohol &&
                    hand.First(card => card.Id == item.CardId).Suit == Suit.Spade),
                "Jiuchi must offer Alcohol conversions for spade hand cards only.");
            Require(!actions.Any(item => item.CardId == heart.Id),
                "A non-spade hand card must not be playable as Alcohol.");
            var action = actions.First(item => item.CardId == spade.Id);

            var paused = RoundTrip(game.CreateCheckpoint());
            var replay = GameReplay.Restore(paused, registry);
            Play(game, action);
            Play(replay, action);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Jiuchi alcohol play must replay identically from the paused checkpoint.");
            Require(game.CreateSnapshot(0, true).Players[0].HasAlcoholEffect,
                "The converted Alcohol must grant the damage bonus effect.");
            Require(game.Events.Select(item => item.Payload)
                    .OfType<AlcoholAppliedEvent>()
                    .Any(item => item.SourceSeat == 0),
                "The spade card must resolve as a real Alcohol.");
            Require(game.CardMovements.Any(movement =>
                    movement.CardId == spade.Id &&
                    movement.From == CardLocation.Hand(0) &&
                    movement.To == CardLocation.Processing &&
                    movement.Reason == CardMoveReasons.Use),
                "The spade hand card must be paid as the Alcohol's physical card.");
            var followUp = game.GetHumanLegalActions()
                .Where(item => item.Kind == LegalActionKind.Alcohol).ToArray();
            Require(followUp.Length == 0,
                "The once-per-turn play alcohol limit must block a second Jiuchi use.");
            completed++;
        }
        Require(completed == 1, "No seeded setup played Jiuchi Alcohol in the play phase.");
    }

    public static void JiuchiRescuesDyingOwnerWithSpadeHandCard()
    {
        var registry = Registry(JiuchiMode, DyingDeck());
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed, JiuchiMode, 40);
            if (!DriveUntilSelfDying(game, out var dyingPrompt)) continue;
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            var spadeIds = hand.Where(card => card.Suit == Suit.Spade)
                .Select(card => card.Id).ToHashSet();
            var alcoholChoice = dyingPrompt.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("response") == "alcohol" &&
                choice.Cards.Count == 1 && spadeIds.Contains(choice.Cards[0]));
            if (alcoholChoice is null)
            {
                LetDie(game, dyingPrompt);
                continue;
            }

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Answer(game, alcoholChoice);
            Answer(replay, dyingPrompt.Choices.Single(choice => choice.Id.Value == alcoholChoice.Id.Value));
            DriveUntilSettled(game);
            DriveUntilSettled(replay);
            if (game.State.Status == EngineStatus.Completed) continue;
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Jiuchi dying rescue must replay identically from the paused prompt.");
            var after = game.CreateSnapshot(0, true).Players[0];
            Require(after.Hp == 1 && after.IsAlive,
                "The spade hand Alcohol must rescue the dying Dong Zhuo to one HP.");
            Require(game.CardMovements.Any(movement =>
                    movement.CardId == alcoholChoice.Cards[0] &&
                    movement.From == CardLocation.Hand(0) &&
                    movement.Reason == CardMoveReasons.Use),
                "The rescue must pay the spade hand card as a real Alcohol.");
            Require(!game.Events.Select(item => item.Payload).OfType<CardUsedEvent>()
                    .Any(item => item.CardKind == CardKind.Peach && item.SourceSeat == 0),
                "The rescue must not consume a Peach.");
            completed++;
        }
        Require(completed == 1, "No seeded setup rescued dying Dong Zhuo with Jiuchi.");
    }

    public static void RoulinDemandsTwoDodgesFromFemaleTarget()
    {
        var registry = Registry(RoulinMode, RoulinDeck(), includeFemaleBank: true);
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed, RoulinMode, 12);
            if (!DriveToFirstPlay(game)) continue;
            var players = game.CreateSnapshot(0, true).Players;
            var femaleSeat = players.Where(player => player.GeneralId == FemaleBank)
                .Select(player => (int?)player.Seat).SingleOrDefault();
            if (femaleSeat is not { } seat || seat is not (1 or 4)) continue;
            if (players[seat].Hand.Count(card => card.Kind == CardKind.Dodge) != 1) continue;
            var slash = players[0].Hand.FirstOrDefault(card => IsSlash(card.Kind));
            if (slash is null) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Slash && item.CardId == slash.Id &&
                item.TargetSeats!.Contains(seat));
            if (action is null) continue;

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Play(game, action);
            Play(replay, action);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Roulin slash must replay identically from the paused checkpoint.");
            var progress = game.Events.Select(item => item.Payload)
                .OfType<RequiredResponseProgressEvent>()
                .SingleOrDefault(item => item.ResponderSeat == seat);
            if (progress is null) continue;
            Require(progress.RequiredResponseCount == 2 && progress.ResponseCount == 1,
                "Roulin must require two dodges from the female target.");
            var damage = game.Events.Select(item => item.Payload)
                .OfType<DamageAppliedEvent>()
                .Any(item => item.TargetSeat == seat && item.SourceSeat == 0);
            Require(damage, "One dodge cannot stop a Roulin slash against a female target.");
            completed++;
        }
        Require(completed == 1, "No seeded setup slashed a female target under Roulin.");
    }

    public static void RoulinSparesMaleTargetWithOneDodge()
    {
        var registry = Registry(RoulinMode, RoulinDeck(), includeFemaleBank: true);
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed, RoulinMode, 12);
            if (!DriveToFirstPlay(game)) continue;
            var players = game.CreateSnapshot(0, true).Players;
            var maleSeat = players.Where(player => player.Seat is 1 or 4 &&
                    player.GeneralId != FemaleBank && player.Seat != 0)
                .Select(player => player.Seat).FirstOrDefault();
            if (maleSeat == 0) continue;
            if (players[maleSeat].Hand.Count(card => card.Kind == CardKind.Dodge) != 1) continue;
            var slash = players[0].Hand.FirstOrDefault(card => IsSlash(card.Kind));
            if (slash is null) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Slash && item.CardId == slash.Id &&
                item.TargetSeats!.Contains(maleSeat));
            if (action is null) continue;

            Play(game, action);
            DriveUntilSettled(game);
            Require(!game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.TargetSeat == maleSeat && item.SourceSeat == 0),
                "A male target must stop the slash with a single dodge.");
            var dodgePlayed = game.CardMovements.Any(movement =>
                movement.CardKind == CardKind.Dodge &&
                movement.From.OwnerSeat == maleSeat &&
                movement.From.Zone == CardZoneKind.Hand);
            Require(dodgePlayed, "The male target's single dodge must be paid.");
            Require(!game.Events.Select(item => item.Payload)
                    .OfType<RequiredResponseProgressEvent>()
                    .Any(item => item.ResponderSeat == maleSeat),
                "A male target must never be asked for a second dodge.");
            completed++;
        }
        Require(completed == 1, "No seeded setup slashed a male target for the control case.");
    }

    public static void RoulinDemandsTwoDodgesWhenFemaleSlashesOwner()
    {
        var registry = Registry(RoulinMode, RoulinDeck(), includeFemaleBank: true);
        var survived = 0;
        var damaged = 0;
        for (var seed = 1; seed <= 600 && (survived < 1 || damaged < 1); seed++)
        {
            var game = Start(registry, seed, RoulinMode, 12);
            if (!DriveToFirstPlay(game)) continue;
            var players = game.CreateSnapshot(0, true).Players;
            var femaleSeat = players.Where(player => player.GeneralId == FemaleBank)
                .Select(player => (int?)player.Seat).SingleOrDefault();
            if (femaleSeat is not { } seat || seat is not (1 or 4)) continue;
            if (!players[seat].Hand.Any(card => IsSlash(card.Kind))) continue;
            if (players[0].Hand.All(card => card.Kind != CardKind.Dodge)) continue;

            EndPlay(game);
            var asked = 0;
            var answered = 0;
            DamageAppliedEvent? damage = null;
            var dodgeChoicesAtFirstRequest = 0;
            var processed = game.Events.Count;
            for (var step = 0; step < 600; step++)
            {
                Observe(game.Events, processed, seat, ref asked, ref damage, ref processed);
                if (damage is not null && asked >= 1 || asked >= 2 && answered >= 2)
                {
                    break;
                }
                var prompt = game.PendingDecision;
                if (prompt is null)
                {
                    Advance(game);
                    continue;
                }
                if (prompt.PlayerSeat != 0)
                {
                    Advance(game);
                    continue;
                }
                if (prompt.Kind == DecisionKind.RespondDodge)
                {
                    var dodgeChoice = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "dodge");
                    if (dodgeChoice is not null)
                    {
                        answered++;
                        if (asked == 1 && answered == 1)
                            dodgeChoicesAtFirstRequest = prompt.Choices.Count(choice =>
                                choice.Parameters.GetValueOrDefault("response") == "dodge");
                    }
                    Answer(game, dodgeChoice ?? prompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "take-damage"));
                    continue;
                }
                if (prompt.Kind == DecisionKind.PlayCard)
                {
                    break;
                }
                GenericAnswer(game, prompt);
            }
            if (asked == 0 || dodgeChoicesAtFirstRequest == 0) continue;
            var progresses = game.Events.Select(item => item.Payload)
                .OfType<RequiredResponseProgressEvent>()
                .Where(item => item.ResponderSeat == 0).ToArray();
            if (dodgeChoicesAtFirstRequest >= 2)
            {
                if (survived > 0) continue;
                Require(asked == 2 && damage is null && answered == 2 &&
                        progresses.Length == 2 &&
                        progresses.All(item => item.RequiredResponseCount == 2) &&
                        progresses[^1].ResponseCount == 2,
                    "Two held dodges must fully answer a female slash under Roulin.");
                survived++;
            }
            else
            {
                if (damaged > 0) continue;
                Require(asked == 1 && damage is { } applied && applied.SourceSeat == seat &&
                        answered == 1 &&
                        progresses.Length == 1 &&
                        progresses[0].RequiredResponseCount == 2 &&
                        progresses[0].ResponseCount == 1,
                    "One held dodge must leave the owner damaged by the female slash " +
                    "that demanded a second dodge.");
                damaged++;
            }
        }
        Require(survived == 1 && damaged == 1,
            $"No seeded setups resolved both female-slash Roulin branches. survived={survived} damaged={damaged}");
    }

    public static void BenghuaiStaysSilentWhileNobodyIsLower()
    {
        // Identity gives the lord +1 maximum HP, so the quiet fixture banks hold
        // nine HP too and nobody sits below Dong Zhuo at his first end phase.
        var registry = Registry(BenghuaiQuietMode, PlayDeck(), bankHp: 9);
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, BenghuaiQuietMode, 12);
            if (!DriveToFirstPlay(game)) continue;
            var players = game.CreateSnapshot(0, true).Players;
            if (players.Any(player => player.IsAlive && player.Hp < player.MaxHp)) continue;
            Require(players.All(player => player.Hp == 9),
                "The quiet fixture must start everyone at nine HP.");
            EndPlay(game);
            var sawForeignTurn = false;
            for (var step = 0; step < 200 && !sawForeignTurn; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is null)
                {
                    Advance(game);
                    continue;
                }
                if (prompt.PlayerSeat != 0)
                {
                    sawForeignTurn = true;
                    break;
                }
                Require(!(prompt.Kind == DecisionKind.ProgramTrigger &&
                        prompt.SkillPrompt?.SkillId == Benghuai),
                    "Benghuai must not fire while nobody has lower HP. HP: " +
                    string.Join(",", game.CreateSnapshot(0, true).Players.Select(player =>
                        $"{player.Seat}:{player.Hp}/{player.MaxHp}")) +
                    $" pending={prompt.Prompt} choices={prompt.Choices.Count}");
                if (prompt.Kind == DecisionKind.PlayCard)
                {
                    sawForeignTurn = true;
                    break;
                }
                GenericAnswer(game, prompt);
            }
            if (!sawForeignTurn) continue;
            Require(!game.Events.Select(item => item.Payload)
                    .OfType<ProgramOptionChosenEvent>()
                    .Any(item => item.SkillId == Benghuai),
                "Benghuai must stay silent until someone has lower HP.");
            completed++;
        }
        Require(completed == 1, "No seeded setup observed a quiet Benghuai end phase.");
    }

    public static void BenghuaiLoseHpBranchReplays()
    {
        BenghuaiBranch("lose-hp", (before, after) =>
            after.Hp == before.Hp - 1 && after.MaxHp == before.MaxHp);
    }

    public static void BenghuaiReduceMaximumHpBranchReplays()
    {
        BenghuaiBranch("reduce-max-hp", (before, after) =>
            after.MaxHp == before.MaxHp - 1 && after.Hp == after.MaxHp &&
            before.Hp == before.MaxHp);
    }

    private static void BenghuaiBranch(string optionId,
        Func<PlayerSnapshot, PlayerSnapshot, bool> verify)
    {
        var registry = Registry(JiuchiMode, PlayDeck());
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed, JiuchiMode, 12);
            if (!DriveToFirstPlay(game)) continue;
            var players = game.CreateSnapshot(0, true).Players;
            var slash = players[0].Hand.FirstOrDefault(card => IsSlash(card.Kind));
            if (slash is null) continue;
            var target = new[] { 1, 4 }.FirstOrDefault(candidate =>
                players[candidate].Hand.All(card => card.Kind != CardKind.Dodge));
            if (target == 0) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.Slash && item.CardId == slash.Id &&
                item.TargetSeats!.Contains(target));
            if (action is null) continue;
            Play(game, action);
            DriveUntilSettled(game);
            if (!game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .Any(item => item.TargetSeat == target && item.SourceSeat == 0)) continue;
            var before = game.CreateSnapshot(0, true).Players[0];

            EndPlay(game);
            var prompt = AwaitBenghuaiPrompt(game);
            if (prompt is null) continue;
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            AnswerOption(game, optionId);
            AnswerOption(replay, optionId);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);
            if (game.State.Status == EngineStatus.Completed) continue;
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Benghuai decay must replay identically from the paused prompt.");
            var chosen = game.Events.Select(item => item.Payload)
                .OfType<ProgramOptionChosenEvent>()
                .Single(item => item.SkillId == Benghuai);
            Require(chosen.OptionId == optionId && chosen.ChooserSeat == 0 &&
                    chosen.ResultBind == "benghuai-choice",
                "The Benghuai choice must be recorded on the declared branch.");
            var after = game.CreateSnapshot(0, true).Players[0];
            Require(verify(before, after),
                $"The {optionId} branch must change only its own stat at the end phase.");
            completed++;
        }
        Require(completed == 1, $"No seeded setup resolved the Benghuai {optionId} branch.");
    }

    public static void BaonveJudgesQunDamageAndRecoversOnSpade()
    {
        var registry = Registry(BaonveMode, BaonveDeck());
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed, BaonveMode, 20);
            if (!DriveUntilBaonveChoice(game)) continue;
            var choice = game.Events.Select(item => item.Payload)
                .OfType<ProgramOptionChosenEvent>()
                .Last(item => item.SkillId == Baonve);
            Require(choice.OptionId == "judge" &&
                    choice.ResultBind == "baonve-choice" &&
                    choice.OwnerSeat == 0 && choice.ChooserSeat != 0,
                "The qun damage source must choose to judge for Baonve.");
            var before = game.CreateSnapshot(0, true).Players[0];
            Require(before.Hp < before.MaxHp,
                "Benghuai decay must have wounded Dong Zhuo before the spade recovery.");

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            DriveUntilSettled(game);
            DriveUntilSettled(replay);
            if (game.State.Status == EngineStatus.Completed) continue;
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Baonve judgment must replay identically from the paused choice.");
            Require(game.CardMovements.Any(movement =>
                    movement.Reason.Value.Contains(Baonve, StringComparison.Ordinal) &&
                    movement.From.Zone == CardZoneKind.Processing &&
                    movement.To == CardLocation.DiscardPile),
                "The Baonve judgment card must be discarded after the judgment.");
            var after = game.CreateSnapshot(0, true).Players[0];
            if (after.Hp != Math.Min(before.Hp + 1, before.MaxHp)) continue;
            completed++;
        }
        Require(completed == 1, "No seeded setup resolved a spade Baonve recovery.");
    }

    public static void BaonveIgnoresOwnerAndNonQunSources()
    {
        var registry = Registry(BaonveNegativeMode, BaonveDeck(), weiBanks: true);
        var completed = 0;
        for (var seed = 1; seed <= 250 && completed < 1; seed++)
        {
            var game = Start(registry, seed, BaonveNegativeMode, 60);
            DriveForBaonveNegative(game);
            var events = game.Events.Select(item => item.Payload).ToArray();
            Require(events.OfType<ProgramOptionChosenEvent>()
                    .All(item => item.SkillId != Baonve),
                "Baonve must never ask about owner or non-qun damage.");
            Require(events.OfType<DamageAppliedEvent>()
                    .Any(item => item.SourceSeat == 0),
                $"The negative scenario must include owner damage (status={game.State.Status}).");
            Require(events.OfType<DamageAppliedEvent>()
                    .Any(item => item.SourceSeat != 0),
                "The negative scenario must include non-owner damage.");
            completed++;
        }
        Require(completed == 1, "No seeded setup exercised the Baonve exclusions.");
    }

    private static void DriveForBaonveNegative(GameEngine game)
    {
        var myDamage = false;
        var foreignDamage = false;
        var processed = 0;
        for (var step = 0; step < 2400 && game.State.Status != EngineStatus.Completed &&
            !(myDamage && foreignDamage); step++)
        {
            ObserveDamage(game.Events, ref processed, ref myDamage, ref foreignDamage);
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    var slashAction = game.GetHumanLegalActions().FirstOrDefault(item =>
                        item.Kind == LegalActionKind.Slash);
                    if (slashAction is not null && slashAction.TargetSeats is { Count: > 0 })
                        Play(game, slashAction);
                    else
                        EndPlay(game);
                    break;
                case DecisionKind.RespondDodge:
                    var dodgeChoice = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "dodge");
                    Answer(game, dodgeChoice ?? prompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "take-damage"));
                    break;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    break;
                case DecisionKind.RescueDying:
                    var rescue = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") is "peach" or "alcohol") ??
                        prompt.Choices.Single(choice =>
                            choice.Parameters.GetValueOrDefault("response") == "let-die");
                    Answer(game, rescue);
                    break;
                default:
                    GenericAnswer(game, prompt);
                    break;
            }
        }
    }

    private static void ObserveDamage(IReadOnlyList<EventEnvelope> events,
        ref int processed, ref bool myDamage, ref bool foreignDamage)
    {
        for (var index = processed; index < events.Count; index++)
        {
            if (events[index].Payload is not DamageAppliedEvent damage) continue;
            if (damage.SourceSeat == 0) myDamage = true;
            else foreignDamage = true;
        }
        processed = events.Count;
    }

    private static void Observe(IReadOnlyList<EventEnvelope> events, int processed, int femaleSeat,
        ref int asked, ref DamageAppliedEvent? damage, ref int updated)
    {
        for (var index = processed; index < events.Count; index++)
        {
            if (events[index].Payload is ResponseRequestedEvent request &&
                request.SourceSeat == femaleSeat && request.TargetSeat == 0)
                asked++;
            if (events[index].Payload is DamageAppliedEvent applied &&
                applied.SourceSeat == femaleSeat && applied.TargetSeat == 0)
                damage = applied;
        }
        updated = events.Count;
    }

    private static bool DriveUntilSelfDying(GameEngine game, out PendingDecision dyingPrompt)
    {
        dyingPrompt = null!;
        for (var step = 0; step < 2000 && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.Kind == DecisionKind.RescueDying && prompt.PlayerSeat == 0 &&
                prompt.TargetSeat == 0)
            {
                dyingPrompt = prompt;
                return true;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    EndPlay(game);
                    break;
                case DecisionKind.RescueDying:
                    LetDie(game, prompt);
                    break;
                case DecisionKind.RespondDodge:
                    var pass = prompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "take-damage");
                    Answer(game, pass);
                    break;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    break;
                default:
                    if (prompt.Kind == DecisionKind.ProgramTrigger &&
                        prompt.SkillPrompt?.SkillId == Benghuai)
                    {
                        AnswerOption(game, "lose-hp");
                        break;
                    }
                    GenericAnswer(game, prompt);
                    break;
            }
        }
        return false;
    }

    private static bool DriveUntilBaonveChoice(GameEngine game)
    {
        var processed = 0;
        for (var step = 0; step < 1600 && game.State.Status != EngineStatus.Completed; step++)
        {
            for (var index = processed; index < game.Events.Count; index++)
            {
                processed++;
                if (game.Events[index].Payload is ProgramOptionChosenEvent chosen &&
                    chosen.SkillId == Baonve)
                {
                    return true;
                }
            }
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat != 0)
            {
                Advance(game);
                continue;
            }
            switch (prompt.Kind)
            {
                case DecisionKind.PlayCard:
                    EndPlay(game);
                    break;
                case DecisionKind.RespondDodge:
                    var dodgeChoice = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "dodge");
                    Answer(game, dodgeChoice ?? prompt.Choices.Single(choice =>
                        choice.Parameters.GetValueOrDefault("response") == "take-damage"));
                    break;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    break;
                case DecisionKind.RescueDying:
                    var rescue = prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("response") is "peach" or "alcohol") ??
                        prompt.Choices.Single(choice =>
                            choice.Parameters.GetValueOrDefault("response") == "let-die");
                    Answer(game, rescue);
                    break;
                default:
                    if (prompt.Kind == DecisionKind.ProgramTrigger &&
                        prompt.SkillPrompt?.SkillId == Benghuai)
                    {
                        AnswerOption(game, "lose-hp");
                        break;
                    }
                    GenericAnswer(game, prompt);
                    break;
            }
        }
        return false;
    }

    private static PendingDecision? AwaitBenghuaiPrompt(GameEngine game)
    {
        for (var step = 0; step < 200; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
            }
            if (prompt.PlayerSeat != 0) return null;
            if (prompt.Kind == DecisionKind.ProgramTrigger &&
                prompt.SkillPrompt?.SkillId == Benghuai) return prompt;
            if (prompt.Kind == DecisionKind.PlayCard) return null;
            GenericAnswer(game, prompt);
        }
        return null;
    }

    private static void AnswerOption(GameEngine game, string optionId)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The option prompt vanished.");
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("option-id") == optionId) ??
            throw new InvalidOperationException($"The prompt lost its {optionId} option.");
        Answer(game, choice);
    }

    private static void GenericAnswer(GameEngine game, PendingDecision prompt)
    {
        var choice = prompt.Choices.FirstOrDefault(item =>
            item.Parameters.GetValueOrDefault("program-action") == "skip") ??
            prompt.Choices.FirstOrDefault() ??
            throw new InvalidOperationException("A prompt ran out of choices.");
        Answer(game, choice);
    }

    private static void LetDie(GameEngine game, PendingDecision prompt)
    {
        var choice = prompt.Choices.Single(item =>
            item.Parameters.GetValueOrDefault("response") == "let-die");
        Answer(game, choice);
    }

    private static bool DriveToFirstPlay(GameEngine game)
    {
        DriveUntil(game, () => false, stopAtDecisions: [DecisionKind.PlayCard]);
        return game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    }

    private static void DriveUntilSettled(GameEngine game) =>
        DriveUntil(game, () => game.ResolutionStack.Count == 0 &&
            (game.State.Status == EngineStatus.Completed || game.PendingDecision is not null));

    private static void DriveUntil(
        GameEngine game,
        Func<bool> done,
        DecisionKind[]? stopAtDecisions = null,
        int budget = 900)
    {
        for (var step = 0; step < budget && !done() && game.State.Status != EngineStatus.Completed; step++)
        {
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                Advance(game);
                continue;
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
                    EndPlay(game);
                    break;
                case DecisionKind.DiscardCards:
                    Accept(game.Submit(new DiscardCardsCommand(0,
                        prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                        prompt.PromptId, game.Revision)));
                    break;
                default:
                    GenericAnswer(game, prompt);
                    break;
            }
        }
    }

    private static bool ConditionHas(SkillProgramTriggerCondition condition,
        SkillProgramTriggerConditionKind kind) =>
        condition.Kind == kind ||
        (condition.Children?.Any(child => ConditionHas(child, kind)) ?? false);

    private static SkillProgramTriggerCondition FindCondition(
        SkillProgramTriggerCondition condition, SkillProgramTriggerConditionKind kind) =>
        condition.Kind == kind
            ? condition
            : condition.Children?.Select(child => FindCondition(child, kind))
                .FirstOrDefault(item => item is not null)!;

    private static bool IsSlash(CardKind kind) =>
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck,
        bool includeFemaleBank = false, bool weiBanks = false, int bankHp = 8) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario(mode, deck, includeFemaleBank, weiBanks, bankHp));

    private static ContentDeckRecipe PlayDeck() =>
        Deck("fixture:dong-zhuo-deck-play", index => (index % 4) switch
        {
            1 => "standard:dodge",
            2 => "standard:peach",
            _ => "standard:slash"
        });

    private static ContentDeckRecipe DyingDeck() =>
        Deck("fixture:dong-zhuo-deck-dying", index => (index % 5) switch
        {
            2 => "standard:dodge",
            4 => "standard:peach",
            _ => "standard:slash"
        });

    private static ContentDeckRecipe RoulinDeck() =>
        Deck("fixture:dong-zhuo-deck-roulin", index => (index % 4) switch
        {
            0 or 1 => "standard:slash",
            2 => "standard:dodge",
            _ => "standard:peach"
        });

    private static ContentDeckRecipe BaonveDeck() =>
        Deck("fixture:dong-zhuo-deck-baonve", index => (index % 3) switch
        {
            1 => "standard:dodge",
            _ => "standard:slash"
        });

    private static ContentDeckRecipe Deck(string id, Func<int, string> kindFor) =>
        new(id, "董卓测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kindFor(index), (Suit)(index % 4), index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode, int maxTurns)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 5,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            ModeId = mode,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            MaxTurns = maxTurns
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Dong Zhuo fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Dong Zhuo selection failed.");
        return game;
    }

    private static void EndPlay(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The play phase decision vanished.");
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Dong Zhuo fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource });
        Require(result.Accepted, result.Error?.Message ?? "Dong Zhuo card action failed.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Dong Zhuo answer failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Dong Zhuo command failed.");
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
            $"Expected invalid Dong Zhuo composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(string modeId, ContentDeckRecipe deck,
        bool includeFemaleBank, bool weiBanks, int bankHp) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new($"dong-zhuo-check-{modeId}",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 156, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            var faction = weiBanks ? "wei" : "qun";
            // Seat 0 is the human Dong Zhuo lord; the banks are skill-less so the
            // only live skills are the four Dong Zhuo skills on the human seat.
            builder.AddGeneral(new ContentGeneralDefinition("fixture:dong-zhuo-bank-a",
                "测试对手一", "supporter", "standard:none", faction, BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:dong-zhuo-bank-b",
                "测试对手二", "supporter", "standard:none", faction, BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:dong-zhuo-bank-c",
                "测试对手三", "supporter", "standard:none", faction, BaseHp: bankHp));
            builder.AddGeneral(new ContentGeneralDefinition("fixture:dong-zhuo-bank-d",
                "测试对手四", "supporter", "standard:none", faction, BaseHp: bankHp));
            var pool = new List<string>
            {
                General,
                "fixture:dong-zhuo-bank-a",
                "fixture:dong-zhuo-bank-b",
                "fixture:dong-zhuo-bank-c",
                "fixture:dong-zhuo-bank-d"
            };
            if (includeFemaleBank)
            {
                builder.AddGeneral(new ContentGeneralDefinition(FemaleBank, "测试女将",
                    "supporter", "standard:none", "qun", BaseHp: bankHp,
                    Gender: GeneralGender.Female));
                pool[^1] = FemaleBank;
            }
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "董卓测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5, GeneralPoolIds: [.. pool]));
        }
    }
}
