using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LiuShanChecks
{
    private const string General = "classic:liu-shan";
    private const string Xiangle = "classic:xiangle";
    private const string Fangquan = "classic:fangquan";
    private const string Ruoyu = "classic:ruoyu";
    private const string Jijiang = "classic:jijiang";
    private const string Mode = "identity:classic-liu-shan-check-5";
    private const string DormantMode = "identity:classic-liu-shan-ruoyu-check-5";
    private const string LoyalistMode = "identity:classic-liu-shan-loyalist-check-5";

    public static void DefinitionAndTriggerSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "shu" } general &&
                general.SkillIds.SequenceEqual([Xiangle, Fangquan, Ruoyu]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Liu Shan must be in the current Shu roster with three HP and three skills.");
        Require(current.Skills[Xiangle].Tags.HasFlag(SkillTag.Locked) &&
                !current.Skills[Fangquan].Tags.HasFlag(SkillTag.Lord) &&
                current.Skills[Ruoyu].Tags.HasFlag(SkillTag.Awakening) &&
                current.Skills[Ruoyu].Tags.HasFlag(SkillTag.Lord),
            "Xiangle must be locked, Fangquan optional and Ruoyu the awakening lord skill.");

        var xianle = current.Skills[Xiangle].Program!;
        var nullifyTrigger = xianle.Triggers.Single();
        Require(nullifyTrigger.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects &&
                nullifyTrigger.OwnerRelation == SkillProgramCardActionOwnerRelation.Target &&
                nullifyTrigger.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
                !nullifyTrigger.Optional,
            "Xiangle must be a locked before-target-effects trigger on Slash kinds only.");
        Require(nullifyTrigger.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.ChooseOption,
            SkillProgramEffectOp.SelectAndMoveOwnedCard,
            SkillProgramEffectOp.NullifyCurrentCardEffect]),
            "Xiangle must ask the Slash user, then either take a basic card or nullify.");
        var choice = nullifyTrigger.Effects[0];
        Require(choice.Target == SkillProgramEffectTarget.Actor &&
                choice.ResultBind == "xianle-answer" &&
                choice.Options.Select(item => item.Id).SequenceEqual(["pay", "decline"]),
            "The Slash user must choose between paying a basic card and declining.");
        var pay = nullifyTrigger.Effects[1];
        Require(pay.ChooserRef!.Kind == ProgramParticipantRef.Actor &&
                pay.CardOwnerRef!.Kind == ProgramParticipantRef.Actor &&
                pay.Zones.SequenceEqual([CardZoneKind.Hand]) &&
                pay.CardCategories.SequenceEqual([SkillProgramCardCategory.Basic]) &&
                pay.Destination == SkillProgramCardDestination.DiscardPile &&
                pay.Condition is { Kind: SkillProgramConditionKind.ChoiceIs, OptionId: "pay" },
            "The payment must discard one of the user's own basic hand cards on the pay branch.");
        Require(nullifyTrigger.Effects[2].Condition is
        { Kind: SkillProgramConditionKind.ChoiceIs, OptionId: "decline" },
            "Only the decline branch may nullify the Slash.");

        var fangquan = current.Skills[Fangquan].Program!;
        Require(fangquan.BooleanStates.Single() is { Id: "fangquan-pending", InitialValue: false },
            "Fangquan must carry the public fangquan-pending state.");
        var reset = fangquan.Triggers.Single(item => item.Id == "reset-pending");
        Require(reset.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow && !reset.Optional &&
                reset.Effects.Single() is { Op: SkillProgramEffectOp.SetBooleanState, BooleanValue: false },
            "Fangquan must reset its pending flag at every owner turn start.");
        var declare = fangquan.Triggers.Single(item => item.Id == "declare-skip-play");
        Require(declare.Window == SkillProgramTriggerWindow.AfterNormalDraw && declare.Optional &&
                declare.Effects.Select(item => item.Op).SequenceEqual([
                    SkillProgramEffectOp.SkipTurnPhases,
                    SkillProgramEffectOp.SetBooleanState]) &&
                declare.Effects[0].SkippedPhases.SequenceEqual([SkillProgramTurnPhase.Play]) &&
                declare.Effects[1] is { StateId: "fangquan-pending", BooleanValue: true },
            "Declaring Fangquan must skip exactly the play phase and raise the pending flag.");
        var grant = fangquan.Triggers.Single(item => item.Id == "grant-extra-turn");
        Require(grant.Window == SkillProgramTriggerWindow.TurnEnding && grant.Optional &&
                grant.UsageScope == SkillUsageScope.Turn && grant.UsageLimit == 1 &&
                grant.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.All,
                    Children.Count: 2
                } &&
                grant.Condition.Children.Any(child => child is
                {
                    Kind: SkillProgramTriggerConditionKind.BooleanState,
                    StateId: "fangquan-pending",
                    ExpectedValue: true
                }) &&
                grant.Condition.Children.Any(child => child is
                {
                    Kind: SkillProgramTriggerConditionKind.Compare,
                    Comparison: SkillProgramComparisonOperator.GreaterThanOrEqual,
                    Left.Kind: SkillProgramTriggerValueKind.CurrentHandCount,
                    Right.Kind: SkillProgramTriggerValueKind.IntegerConstant,
                    Right.Value: 1
                }),
            "The turn-end grant must require the skip flag and at least one hand card.");
        Require(grant.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.SelectOwnedCards,
            SkillProgramEffectOp.MoveBoundCards,
            SkillProgramEffectOp.SelectTarget,
            SkillProgramEffectOp.PendExtraTurn]),
            "The grant must discard one hand card, select another player and pend their extra turn.");
        var pend = grant.Effects[3];
        Require(pend.TargetReference is { Kind: ProgramParticipantRef.SelectedTarget },
            "The pended extra turn must benefit the selected target.");

        var ruoyu = current.Skills[Ruoyu].Program!;
        var awakening = ruoyu.Triggers.Single();
        Require(awakening.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                awakening.Subject == SkillProgramTriggerSubject.Owner &&
                !awakening.Optional &&
                awakening.UsageScope == SkillUsageScope.Game &&
                awakening.UsageLimit == 1 &&
                awakening.Condition is
                {
                    Kind: SkillProgramTriggerConditionKind.Compare,
                    Comparison: SkillProgramComparisonOperator.LessThanOrEqual,
                    Left.Kind: SkillProgramTriggerValueKind.CurrentHp,
                    Right.Kind: SkillProgramTriggerValueKind.LivingPlayersMinHp
                },
            "Ruoyu must awaken once per game when the owner's HP is the living minimum (or tied).");
        Require(awakening.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.ChangeMaximumHp,
            SkillProgramEffectOp.Recover,
            SkillProgramEffectOp.GrantSkills]) &&
                awakening.Effects[0].Amount == 1 &&
                awakening.Effects[1].Amount == 1 &&
                awakening.Effects[2].SkillIds.SequenceEqual([Jijiang]),
            "Ruoyu must add one maximum HP, recover one and grant the existing classic:jijiang.");

        const string xianleTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:xianle","revision":1,
            "minimumRulesVersion": 187,
            "triggers":[{"id":"ask","window":"cardUseBeforeTargetEffects","ownerRelation":"target",
            "cardKinds":["slash","fireSlash","thunderSlash"],"optional":false,
            "effects":[
            {"op":"chooseOption","target":"actor","resultBind":"answer",
            "options":[{"id":"pay","condition":{"kind":"hasOwnedCardCategory","zones":["hand"],"cardCategories":["basic"]}},
            {"id":"decline","condition":{"kind":"always"}}]},
            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"actor"},
            "cardOwnerRef":{"kind":"actor"},"zones":["hand"],"cardCategories":["basic"],"count":1,
            "destination":"discardPile","condition":{"kind":"choiceIs","sourceBind":"answer","optionId":"pay"}},
            {"op":"nullifyCurrentCardEffect","target":"owner",
            "condition":{"kind":"choiceIs","sourceBind":"answer","optionId":"decline"}}]}]}]}
            """;
        const string xianlePresentation = """
            {"schemaVersion":3,"skills":{"fixture:xianle":{"name":"测试","description":"测试",
            "optionLabels":{"pay":"支付","decline":"拒绝"}}}}
            """;
        Require(SkillProgramCatalog.Load(xianleTemplate, xianlePresentation)
                .Programs["fixture:xianle"].Triggers.Single().Effects.Count == 3,
            "The Xianle chain must load with the actor choice, payment and conditional nullify.");
        Reject(xianleTemplate.Replace(
                "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"answer\",\"optionId\":\"decline\"}}",
                "\"condition\":{\"kind\":\"wounded\"}"),
            xianlePresentation, "the nullify only accepts always or a named-choice branch");
        Reject(xianleTemplate.Replace(
                "\"cardOwnerRef\":{\"kind\":\"actor\"}",
                "\"cardOwnerRef\":{\"kind\":\"owner\"}"),
            xianlePresentation, "category filtering requires the chooser to own the paid card");
        Reject(xianleTemplate.Replace(
                "\"destination\":\"discardPile\",\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"answer\",\"optionId\":\"pay\"}}",
                "\"destination\":\"discardPile\",\"resultBind\":\"paid\",\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"answer\",\"optionId\":\"pay\"}}"),
            xianlePresentation, "a conditioned move must not declare its own resultBind");

        const string pendTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:fangquan","revision":1,
            "minimumRulesVersion": 187,
            "triggers":[{"id":"grant","window":"turnEnding","subject":"owner","optional":true,
            "usageScope":"turn","usageLimit":1,
            "effects":[
            {"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
            {"op":"pendExtraTurn","target":"owner","targetRef":{"kind":"selectedTarget"}}]}]}]}
            """;
        const string pendPresentation = """
            {"schemaVersion":3,"skills":{"fixture:fangquan":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(pendTemplate, pendPresentation)
                .Programs["fixture:fangquan"].Triggers.Single().Effects.Count == 2,
            "A targeted extra-turn grant must load against the parameterized pendExtraTurn.");
        Reject(pendTemplate.Replace("\"targetRef\":{\"kind\":\"selectedTarget\"}",
                "\"targetRef\":{\"kind\":\"eventSource\"}"),
            pendPresentation, "the extra-turn beneficiary must be the selected target");
        Reject(pendTemplate.Replace("\"op\":\"pendExtraTurn\",\"target\":\"owner\",\"targetRef\":{\"kind\":\"selectedTarget\"}",
                "\"op\":\"pendExtraTurn\",\"target\":\"selectedTarget\""),
            pendPresentation, "an extra turn still always belongs to the skill owner frame");

        const string ruoyuTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:ruoyu","revision":1,
            "minimumRulesVersion": 187,
            "triggers":[{"id":"awakening","window":"turnStartBeforeNormalFlow","subject":"owner",
            "optional":false,"usageScope":"game","usageLimit":1,
            "condition":{"kind":"compare","left":{"kind":"currentHp"},"operator":"lessThanOrEqual",
            "right":{"kind":"livingPlayersMinHp"}},
            "effects":[{"op":"changeMaximumHp","target":"owner","amount":1},
            {"op":"recover","target":"owner","amount":1},
            {"op":"grantSkills","target":"owner","skillIds":["classic:jijiang"]}]}]}]}
            """;
        const string ruoyuPresentation = """
            {"schemaVersion":3,"skills":{"fixture:ruoyu":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(ruoyuTemplate, ruoyuPresentation)
                .Programs["fixture:ruoyu"].Triggers.Single().Effects.Count == 3,
            "The Ruoyu awakening must load with the living-minimum compare.");
        Reject(ruoyuTemplate.Replace("\"kind\":\"livingPlayersMinHp\"", "\"kind\":\"livingPlayersMinHpX\""),
            ruoyuPresentation, "the compare right side must be a registered trigger value");
    }

    public static void XianglePaidBasicCardLetsSlashResolve()
    {
        var registry = Registry(Mode, XiangleDeck());
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed, Mode);
            var outcome = DriveToXianleChoice(game);
            if (outcome is not { AttackerSeat: { } attacker } state || state.OptionId != "pay") continue;
            var handBefore = game.CreateSnapshot(attacker, true).Players[attacker].Hand
                .Select(card => card.Id).ToArray();

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            // The whole pay chain (choice, payment, damage) can commit inside one
            // advance; both sides look for the first post-choice damage event.
            DriveUntil(game, () => HasDamageAfterFirstChoice(game));
            DriveUntil(replay, () => HasDamageAfterFirstChoice(replay));
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Xianle payment branch must replay identically.");

            var payments = game.CardMovements.Where(movement =>
                movement.From.OwnerSeat == attacker &&
                movement.From.Zone == CardZoneKind.Hand &&
                movement.To == CardLocation.DiscardPile &&
                movement.Reason.Value.Contains(Xiangle, StringComparison.Ordinal)).ToArray();
            Require(payments.Length == 1,
                "Paying must discard exactly one of the attacker's basic hand cards via the skill reason.");
            Require(game.Events.Skip(FirstChoiceIndex(game) + 1)
                    .Select(item => item.Payload).OfType<CardEffectSkippedEvent>()
                    .All(item => item.TargetSeat != 0 ||
                        item.Reason != CardEffectSkipReason.SkillNullified),
                "A paid Xianle must not nullify the Slash.");
            Require(HasDamageAfterFirstChoice(game),
                "Without a dodge the paid Slash must still deal its damage to Liu Shan.");
            Require(handBefore.Contains(payments[0].CardId),
                "The paid card must come from the attacker's frozen hand.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a paid Xianle branch.");
    }

    public static void XiangleNullifiesSlashWhenActorCannotPay()
    {
        var registry = Registry(Mode, XiangleDeck());
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed, Mode);
            var outcome = DriveToXianleChoice(game);
            if (outcome is not { AttackerSeat: { } attacker } state || state.OptionId != "decline") continue;
            var hpBefore = game.CreateSnapshot(0, true).Players[0].Hp;
            var attackerHandBefore = game.CreateSnapshot(attacker, true).Players[attacker].Hand.Count;

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            // The decline chain (choice, nullification, skip event) can commit
            // inside one advance; the first public skip settles both sides.
            DriveUntil(game, () => game.Events.Select(item => item.Payload)
                .OfType<CardEffectSkippedEvent>()
                .Any(item => item.TargetSeat == 0 && item.Reason == CardEffectSkipReason.SkillNullified));
            DriveUntil(replay, () => replay.Events.Select(item => item.Payload)
                .OfType<CardEffectSkippedEvent>()
                .Any(item => item.TargetSeat == 0 && item.Reason == CardEffectSkipReason.SkillNullified));
            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Xianle decline branch must replay identically.");

            Require(game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>()
                    .Any(item => item.TargetSeat == 0 && item.SourceSeat == attacker &&
                        item.Reason == CardEffectSkipReason.SkillNullified),
                "The declined Slash must be publicly skipped for Liu Shan.");
            Require(game.Events.Skip(FirstChoiceIndex(game) + 1)
                    .Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .All(item => item.TargetSeat != 0),
                "A nullified Slash must not damage Liu Shan.");
            Require(game.CreateSnapshot(0, true).Players[0].Hp == hpBefore,
                "A nullified Slash must leave Liu Shan's HP unchanged.");
            Require(game.CardMovements.All(movement =>
                    !(movement.From.OwnerSeat == attacker &&
                      movement.Reason.Value.Contains(Xiangle, StringComparison.Ordinal))),
                "A declining attacker must not pay any card.");
            Require(game.CreateSnapshot(attacker, true).Players[attacker].Hand.Count == attackerHandBefore,
                "The declining attacker keeps their hand through the nullification.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a declined Xianle branch.");
    }

    public static void XiangleIgnoresNonSlashCards()
    {
        var registry = Registry(Mode, DuelDeck());
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed, Mode);
            var outcome = DriveToCardOnLiuShan(game, CardKind.Duel);
            if (outcome is null) continue;
            Require(game.Events.Select(item => item.Payload).All(item => item is not
                    ProgramBindingStartedEvent { SkillId: Xiangle }),
                "A Duel on Liu Shan must not open the Xianle window.");
            Require(game.Events.Select(item => item.Payload).All(item => item is not
                    ProgramOptionChosenEvent { SkillId: Xiangle }),
                "A Duel on Liu Shan must never ask for the Xianle payment.");
            for (var step = 0; step < 200 && game.State.Status != EngineStatus.Completed; step++)
                Step(game);
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Duel against Liu Shan.");
    }

    public static void FangquanSkipGrantsExtraTurnToSelectedTarget()
    {
        var registry = Registry(Mode, FangquanDeck());
        var completed = 0;
        for (var seed = 1; seed <= 60 && completed < 1; seed++)
        {
            var game = Start(registry, seed, Mode);
            if (!DriveToFangquanDeclare(game)) continue;

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            ActivateFangquan(game);
            ActivateFangquan(replay);
            // The play phase must be gone: the next seat-0 decision is the turn-end grant.
            if (!DriveToFangquanGrant(game)) continue;
            if (!DriveToFangquanGrant(replay)) continue;

            GrantExtraTurn(game, 2);
            GrantExtraTurn(replay, 2);
            DriveUntil(game, () => game.Events.Select(item => item.Payload)
                .OfType<TurnStartedEvent>().Any(item => item.ActorSeat == 2));
            DriveUntil(replay, () => replay.Events.Select(item => item.Payload)
                .OfType<TurnStartedEvent>().Any(item => item.ActorSeat == 2));
            if (game.State.Status == EngineStatus.Completed) continue;

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Fangquan extra turn must replay identically.");
            var pended = game.Events.Select(item => item.Payload).OfType<ProgramExtraTurnPendedEvent>()
                .Single(item => item.SkillId == Fangquan);
            Require(pended.Seat == 2,
                "The extra turn must be pended for the selected target seat.");
            var discard = game.CardMovements.Where(movement =>
                movement.From.OwnerSeat == 0 &&
                movement.From.Zone == CardZoneKind.Hand &&
                movement.To == CardLocation.DiscardPile &&
                movement.Reason.Value.Contains(Fangquan, StringComparison.Ordinal)).ToArray();
            Require(discard.Length == 1,
                "Fangquan must cost exactly one hand card.");
            Require(game.Events.Select(item => item.Payload)
                    .OfType<TurnStartedEvent>().Any(item => item.ActorSeat == 2),
                "The selected target must actually start the granted extra turn.");
            completed++;
        }
        Require(completed == 1, "No seeded setup completed the Fangquan extra-turn grant.");
    }

    public static void FangquanDeclineKeepsPlayPhaseAndGrantsNothing()
    {
        var registry = Registry(Mode, FangquanDeck());
        var completed = 0;
        for (var seed = 1; seed <= 60 && completed < 1; seed++)
        {
            var game = Start(registry, seed, Mode);
            if (!DriveToFangquanDeclare(game)) continue;
            DeclineFangquan(game);
            var sawPlay = false;
            for (var step = 0; step < 400 && !sawPlay && game.State.Status != EngineStatus.Completed; step++)
            {
                if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) sawPlay = true;
                if (game.PendingDecision is { } prompt && prompt.SkillPrompt?.SkillId == Fangquan)
                    throw new InvalidOperationException(
                        "A declined Fangquan must not open the turn-end grant.");
                Step(game);
            }
            if (game.State.Status == EngineStatus.Completed) continue;
            Require(sawPlay, "A declined Fangquan must keep the play phase.");
            Require(game.Events.Select(item => item.Payload)
                    .All(item => item is not ProgramExtraTurnPendedEvent { SkillId: Fangquan }),
                "A declined Fangquan must never pend an extra turn.");
            completed++;
        }
        Require(completed == 1, "No seeded setup declined the Fangquan skip.");
    }

    public static void RuoyuAwakensAtMinimumHpAndGainsJijiang()
    {
        var registry = Registry(Mode, FangquanDeck());
        var completed = 0;
        for (var seed = 1; seed <= 20 && completed < 1; seed++)
        {
            var game = Start(registry, seed, Mode);
            var opening = game.CreateSnapshot(0, true);
            var before = opening.Players[0];
            if (!DriveToFirstPlay(game)) continue;
            var awakened = game.Events.Select(item => item.Payload).OfType<SkillAwakenedEvent>()
                .SingleOrDefault(item => item.SkillId == Ruoyu);
            if (awakened is null) continue;
            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            DriveUntil(replay, () => replay.Events.Select(item => item.Payload)
                .OfType<SkillAwakenedEvent>().Any(item => item.SkillId == Ruoyu));

            Require(Events(game).SequenceEqual(Events(replay)) &&
                    State(game) == State(replay),
                "The Ruoyu awakening must replay identically.");
            Require(before.Hp == before.MaxHp &&
                    before.Hp <= opening.Players.Where(player => player.Seat != 0)
                        .Min(player => player.Hp),
                "The fixture lord must begin full and at the living minimum HP.");
            Require(awakened.PlayerSeat == 0 && awakened.MaximumHp == before.MaxHp + 1 &&
                    awakened.AcquiredSkillIds.SequenceEqual([Jijiang]),
                "Ruoyu must add one maximum HP and grant Jijiang exactly once.");
            var after = game.CreateSnapshot(0, true).Players[0];
            Require(after.MaxHp == before.MaxHp + 1 && after.Hp == after.MaxHp,
                "The awakened Liu Shan must recover to the raised maximum without overflowing.");
            Require(after.Skills!.Select(item => item.ContentId).Contains(Jijiang),
                "The awakened Liu Shan must own Jijiang.");
            Require(game.Events.Select(item => item.Payload).OfType<SkillAwakenedEvent>()
                    .Count(item => item.SkillId == Ruoyu) == 1,
                "Ruoyu must awaken exactly once.");
            Require(game.GetHumanLegalActions().Any(action =>
                    action.Kind == LegalActionKind.UseProgramSkill && action.ProgramSkillId == Jijiang),
                "The granted Jijiang must be usable in the same turn's play phase.");
            completed++;
        }
        Require(completed == 1, "No seeded setup awakened Ruoyu as the living minimum.");
    }

    public static void RuoyuStaysDormantWhenNotMinimumHp()
    {
        var registry = Registry(DormantMode, FangquanDeck(), bankBaseHp: 2);
        var completed = 0;
        for (var seed = 1; seed <= 40 && completed < 1; seed++)
        {
            var game = Start(registry, seed, DormantMode);
            if (!DriveToFirstPlay(game)) continue;
            Require(game.CreateSnapshot(0, true).Players[0].Hp >
                    game.CreateSnapshot(0, true).Players.Where(player => player.Seat != 0)
                        .Min(player => player.Hp),
                "The fixture lord must not be the living minimum on this seed.");
            Require(game.Events.Select(item => item.Payload)
                    .All(item => item is not SkillAwakenedEvent { SkillId: Ruoyu }),
                "Ruoyu must not awaken while another player's HP is strictly lower.");
            completed++;
        }
        Require(completed == 1, "No seeded setup reached play with a lower-HP opponent.");
    }

    public static void RuoyuNotOwnedByNonLord()
    {
        var registry = Registry(LoyalistMode, FangquanDeck(), loyalistLure: true);
        var game = StartLoyalist(registry, 7);
        var seat = game.CreateSnapshot(0, true).Players[0];
        Require(seat.Skills!.Select(item => item.ContentId).SequenceEqual([Xiangle, Fangquan]),
            "A non-lord Liu Shan owns Xiangle and Fangquan but not the Ruoyu lord skill.");
        Require(game.GetHumanLegalActions().All(action =>
                action.ProgramSkillId is not Ruoyu),
            "A non-lord Liu Shan must never see a Ruoyu action.");
    }

    private static (int AttackerSeat, string OptionId)? DriveToXianleChoice(GameEngine game)
    {
        for (var step = 0; step < 2400 && game.State.Status != EngineStatus.Completed; step++)
        {
            var chosen = game.Events.Select(item => item.Payload)
                .OfType<ProgramOptionChosenEvent>().FirstOrDefault(item => item.SkillId == Xiangle);
            if (chosen is not null) return (chosen.ChooserSeat, chosen.OptionId);
            Step(game);
        }
        return null;
    }

    private static int? DriveToCardOnLiuShan(GameEngine game, CardKind kind)
    {
        for (var step = 0; step < 2400 && game.State.Status != EngineStatus.Completed; step++)
        {
            var used = game.Events.Select(item => item.Payload)
                .OfType<CardUsedEvent>().FirstOrDefault(item =>
                    item.CardKind == kind && item.TargetSeat == 0 && item.SourceSeat != 0);
            if (used is not null) return used.SourceSeat;
            Step(game);
        }
        return null;
    }

    private static bool DriveToFangquanDeclare(GameEngine game)
    {
        for (var step = 0; step < 1200 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: 0,
                    SkillPrompt.SkillId: Fangquan
                } prompt &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                return true;
            Step(game);
        }
        return false;
    }

    private static bool DriveToFangquanGrant(GameEngine game)
    {
        for (var step = 0; step < 600 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: 0,
                    SkillPrompt.SkillId: Fangquan
                } prompt &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                return true;
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                throw new InvalidOperationException(
                    "A declared Fangquan must skip the owner's play phase.");
            Step(game);
        }
        return false;
    }

    private static void ActivateFangquan(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The Fangquan prompt vanished.");
        var activate = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Answer(game, activate);
    }

    private static void DeclineFangquan(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The Fangquan prompt vanished.");
        var skip = prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "skip");
        Answer(game, skip);
    }

    private static void GrantExtraTurn(GameEngine game, int targetSeat)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The Fangquan grant prompt vanished.");
        // 1. accept the optional turn-end trigger declaration.
        var activate = prompt.Choices.FirstOrDefault(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate");
        if (activate is not null)
        {
            Answer(game, activate);
            prompt = game.PendingDecision ?? throw new InvalidOperationException(
                "The Fangquan cost selection did not follow the trigger acceptance.");
        }
        // 2. pay exactly one hand card.
        var cost = prompt.Choices.FirstOrDefault(choice => choice.Cards.Count > 0);
        if (cost is not null)
        {
            Answer(game, cost);
            prompt = game.PendingDecision ?? throw new InvalidOperationException(
                "The Fangquan target selection did not follow the cost.");
        }
        // 3. pick the beneficiary.
        var target = prompt.Choices.FirstOrDefault(choice =>
            choice.Targets.SequenceEqual([targetSeat])) ?? throw new InvalidOperationException(
            $"The Fangquan grant lost the seat-{targetSeat} target choice: " +
            JsonSerializer.Serialize(prompt.Choices.Select(item => new
            {
                action = item.Parameters.GetValueOrDefault("program-action"),
                targets = item.Targets
            }).ToArray()) + ".");
        Answer(game, target);
    }

    private static bool DriveToFirstPlay(GameEngine game)
    {
        for (var step = 0; step < 1200 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return true;
            Step(game);
        }
        return false;
    }

    private static void DriveUntil(GameEngine game, Func<bool> done)
    {
        for (var step = 0; step < 1200 && !done() && game.State.Status != EngineStatus.Completed; step++)
            Step(game);
    }

    private static void Step(GameEngine game)
    {
        if (game.PendingDecision is { } prompt)
        {
            if (prompt.PlayerSeat == 0)
            {
                switch (prompt.Kind)
                {
                    case DecisionKind.PlayCard:
                        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId)));
                        return;
                    case DecisionKind.DiscardCards:
                        Accept(game.Submit(new DiscardCardsCommand(0,
                            prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                            prompt.PromptId, game.Revision)));
                        return;
                    default:
                        {
                            var choice = prompt.Choices.FirstOrDefault(item =>
                                item.Parameters.GetValueOrDefault("program-action") == "skip") ??
                                prompt.Choices.FirstOrDefault(item =>
                                    item.Parameters.GetValueOrDefault("response") is "take-damage" or "let-die") ??
                                prompt.Choices.FirstOrDefault();
                            if (choice is null)
                                throw new InvalidOperationException(
                                    $"A seat-0 prompt has no answerable choice: {prompt.Kind}.");
                            Answer(game, choice);
                            return;
                        }
                }
            }
            // AI seats resolve automatically on advance.
        }
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Liu Shan fixture did not advance.");
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Liu Shan answer failed.");
    }

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck, int bankBaseHp = 8,
        bool loyalistLure = false) =>
        ContentRegistry.Build(
            new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new LiuShanScenario(mode, deck, bankBaseHp, loyalistLure));

    private static ContentDeckRecipe XiangleDeck() => DeckCore(index => (index % 4) switch
    {
        0 => "standard:slash",
        1 => "standard:peach",
        2 => "standard:bagua",
        _ => "standard:duel"
    });

    private static ContentDeckRecipe DuelDeck() => DeckCore(index => (index % 3) switch
    {
        0 => "standard:duel",
        1 => "standard:bagua",
        _ => "standard:qinggang_sword"
    });

    private static ContentDeckRecipe FangquanDeck() => DeckCore(index => (index % 3) switch
    {
        0 => "standard:slash",
        1 => "standard:peach",
        _ => "standard:bagua"
    });

    private static ContentDeckRecipe DeckCore(Func<int, string> kindFor) =>
        new("fixture:liu-shan-deck", "刘禅测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kindFor(index), (Suit)(index % 4), index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode, Role humanRole = Role.Lord)
    {
        // Setup candidates are sampled per seed; scan forward until seat 0 is
        // offered Liu Shan, then select and return the started engine.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed + attempt,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = humanRole,
                ModeId = mode,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 40
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Liu Shan fixture did not start.");
            var choice = game.PendingDecision!;
            if (!choice.ValidContentIds.Contains(General, StringComparer.Ordinal)) continue;
            var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Liu Shan selection failed.");
            return game;
        }
        throw new InvalidOperationException(
            $"No seed from {seed} onward offered Liu Shan to seat 0 in {mode}.");
    }

    private static GameEngine StartLoyalist(ContentRegistry registry, int seed)
    {
        var game = Start(registry, seed, LoyalistMode, Role.Loyalist);
        var roles = game.CreateSnapshot(0, true).Players.ToDictionary(player => player.Seat, player => player.Role);
        Require(roles[0] != Role.Lord && roles.Values.Count(role => role == Role.Lord) == 1,
            "The loyalist fixture must seat an AI lord.");
        return game;
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Liu Shan command failed.");
    }

    private static string State(GameEngine game) =>
        JsonSerializer.Serialize(game.CreateSnapshot(0, true));

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static int FirstChoiceIndex(GameEngine game) =>
        game.Events.ToList().FindIndex(item =>
            item.Payload is ProgramOptionChosenEvent { SkillId: Xiangle });

    private static bool HasDamageAfterFirstChoice(GameEngine game) =>
        game.Events.Skip(FirstChoiceIndex(game) + 1)
            .Select(item => item.Payload).OfType<DamageAppliedEvent>()
            .Any(item => item.TargetSeat == 0);

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
            $"Expected invalid Liu Shan composition to be rejected{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class LiuShanScenario(string modeId, ContentDeckRecipe deck,
        int bankBaseHp, bool loyalistLure) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("liu-shan-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 157, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human Liu Shan; seats 1-4 are skill-less AI banks so
            // the only live skills sit on the human seat. The loyalist fixture
            // gives bank 1 an active skill so the auto-picking AI lord takes it
            // and leaves Liu Shan on the non-lord human seat.
            foreach (var index in new[] { 1, 2, 3, 4 })
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:liu-shan-bank-{index}",
                    "测试对手", "supporter",
                    loyalistLure && index == 1 ? "classic:tiaoxin" : "standard:none",
                    index == 3 ? "shu" : "qun", BaseHp: bankBaseHp));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "刘禅测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:liu-shan-bank-1",
                    "fixture:liu-shan-bank-2",
                    "fixture:liu-shan-bank-3",
                    "fixture:liu-shan-bank-4"]));
        }
    }
}
