using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangZhaoZhangHongChecks
{
    private const string General = "classic:zhang-zhao-zhang-hong";
    private const string Zhijian = "classic:zhijian";
    private const string Guzheng = "classic:guzheng";
    private const string Mode = "identity:zhang-zhao-zhang-hong-check-5";
    private const string WealthyMode = "identity:zhang-zhao-zhang-hong-wealthy-check-5";

    public static void DefinitionAndSkillSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        Require(current.Generals[General] is { BaseHp: 3, FactionId: "wu" } general &&
                general.SkillIds.SequenceEqual([Zhijian, Guzheng]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General),
            "2011 Zhang Zhao & Zhang Hong must be in the current Wu roster with three HP and two skills.");

        var zhijian = current.Skills[Zhijian].Program!;
        var gift = zhijian.Activations.Single();
        Require(gift.Id == "gift-equipment" && gift.MinCards == 0 && gift.MaxCards == 0 &&
                gift.MinTargets == 1 && gift.MaxTargets == 1 &&
                gift.TargetKind == SkillProgramTargetKind.OtherLiving &&
                gift.UsesPerTurn is null && gift.TargetRequiresEmptyEquipmentSlot,
            "Zhijian must be an unlimited play-phase activation asking one other living target with a free matching slot.");
        Require(gift.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.SelectAndMoveOwnedCard,
            SkillProgramEffectOp.Draw]),
            "Zhijian must move one hand equipment card and then draw one card.");
        var move = gift.Effects[0];
        Require(move.Zones.SequenceEqual([CardZoneKind.Hand]) &&
                move.CardCategories.SequenceEqual([SkillProgramCardCategory.Equipment]) &&
                move.Amount == 1 &&
                move.Destination == SkillProgramCardDestination.SelectedTargetEquipment &&
                move.TargetReference is { Kind: ProgramParticipantRef.SelectedTarget },
            "The gift must place one own hand equipment card into the selected target's equipment area.");

        var guzheng = current.Skills[Guzheng].Program!;
        var claim = guzheng.Triggers.Single();
        Require(claim.Id == "claim-discarded-hand" &&
                claim.Window == SkillProgramTriggerWindow.TurnEnding &&
                claim.Subject == SkillProgramTriggerSubject.Owner &&
                claim.TurnOwnerScope == SkillProgramTurnOwnerScope.OtherLiving &&
                claim.Optional && claim.UsageScope is null,
            "Guzheng must be an unlimited optional trigger at another living player's turn ending.");
        Require(claim.Condition is
        {
            Kind: SkillProgramTriggerConditionKind.Compare,
            Comparison: SkillProgramComparisonOperator.GreaterThan,
            Left.Kind: SkillProgramTriggerValueKind.TurnOwnerDiscardPhaseHandDiscardCount,
            Right.Kind: SkillProgramTriggerValueKind.IntegerConstant,
            Right.Value: 0
        },
            "Guzheng must require the turn owner to have discarded at least one hand card this phase.");
        Require(claim.Effects.Select(item => item.Op).SequenceEqual([
                SkillProgramEffectOp.BindDiscardPhaseDiscards,
            SkillProgramEffectOp.SelectTarget,
            SkillProgramEffectOp.SelectCardSubset,
            SkillProgramEffectOp.MoveBoundCards,
            SkillProgramEffectOp.ChooseOption,
            SkillProgramEffectOp.MoveBoundCards]),
            "Guzheng must bind the phase pool, bind the ending turn owner, send one card back and then offer the rest.");
        Require(claim.Effects[0].ResultBind == "guzheng-pool",
            "The bound pool must feed both the return and the optional rest.");
        Require(claim.Effects[1].TargetKind == SkillProgramTargetKind.EventSource,
            "The return destination must be the ending turn's owner.");
        Require(claim.Effects[2] is
        {
            SourceBind: "guzheng-pool", ResultBind: "guzheng-return",
            MinimumCards: 1, MaximumCards: 1
        },
            "Exactly one pool card must be chosen to return to the turn owner.");
        Require(claim.Effects[3].Destination == SkillProgramCardDestination.SelectedTargetHand,
            "The returned card must join the turn owner's hand.");
        Require(claim.Effects[4].Options.Select(item => item.Id).SequenceEqual(["take-rest", "decline"]),
            "The owner must choose between claiming the remaining cards and declining.");
        Require(claim.Effects[5] is
        {
            SourceBind: "guzheng-pool", ExceptBind: "guzheng-return",
            Destination: SkillProgramCardDestination.OwnerHand
        } rest &&
                rest.Condition is
                {
                    Kind: SkillProgramConditionKind.ChoiceIs,
                    SourceBind: "guzheng-rest-answer",
                    OptionId: "take-rest"
                },
            "Only the take-rest branch may move the remaining pool cards into the owner's hand.");

        const string zhijianTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:zhijian","revision":1,"minimumRulesVersion":188,
            "activations":[{"id":"gift","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
            "targetKind":"otherLiving","usesPerTurn":null,"targetRequiresEmptyEquipmentSlot":true,
            "effects":[
            {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},
            "cardOwnerRef":{"kind":"owner"},"zones":["hand"],"cardCategories":["equipment"],"count":1,
            "destination":"selectedTargetEquipment","targetRef":{"kind":"selectedTarget"}},
            {"op":"draw","target":"owner","amount":1}]}]}]}
            """;
        const string zhijianPresentation = """
            {"schemaVersion":3,"skills":{"fixture:zhijian":{"name":"测试","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(zhijianTemplate, zhijianPresentation)
                .Programs["fixture:zhijian"].Activations.Single().Effects.Count == 2,
            "The Zhijian gift chain must load with the empty-slot activation filter.");
        Reject(zhijianTemplate.Replace("\"maxTargets\":1", "\"maxTargets\":2"),
            zhijianPresentation, "the empty-slot filter requires exactly one target");
        Reject(zhijianTemplate.Replace("\"zones\":[\"hand\"]", "\"zones\":[\"hand\",\"equipment\"]"),
            zhijianPresentation, "an equipment gift must come from the hand only");
        Reject(zhijianTemplate.Replace("\"cardCategories\":[\"equipment\"]", "\"cardCategories\":[\"basic\"]"),
            zhijianPresentation, "only equipment cards may be gifted into an equipment area");
        Reject(zhijianTemplate.Replace("\"destination\":\"selectedTargetEquipment\",\"targetRef\":{\"kind\":\"selectedTarget\"}",
                "\"destination\":\"selectedTargetEquipment\""),
            zhijianPresentation, "a selected-target equipment destination requires the target reference");

        const string guzhengTemplate = """
            {"schemaVersion":62,"skills":[{"id":"fixture:guzheng","revision":1,"minimumRulesVersion":188,
            "triggers":[{"id":"claim","window":"turnEnding","subject":"owner","turnOwnerScope":"otherLiving",
            "optional":true,
            "condition":{"kind":"compare","left":{"kind":"turnOwnerDiscardPhaseHandDiscardCount"},
            "operator":"greaterThan","right":{"kind":"integerConstant","value":0}},
            "effects":[
            {"op":"bindDiscardPhaseDiscards","target":"owner","resultBind":"pool"},
            {"op":"selectTarget","target":"owner","targetKind":"eventSource"},
            {"op":"selectCardSubset","target":"owner","sourceBind":"pool","resultBind":"returned",
            "minimumCards":1,"maximumCards":1,"maximumRankSum":208,"aiOrder":"mostCardsThenRankSum"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"returned","destination":"selectedTargetHand"},
            {"op":"chooseOption","target":"owner","resultBind":"rest-answer",
            "options":[{"id":"take-rest","condition":{"kind":"always"}},{"id":"decline","condition":{"kind":"always"}}]},
            {"op":"moveBoundCards","target":"owner","sourceBind":"pool","exceptBind":"returned",
            "destination":"ownerHand",
            "condition":{"kind":"choiceIs","sourceBind":"rest-answer","optionId":"take-rest"}}]}]}]}
            """;
        const string guzhengPresentation = """
            {"schemaVersion":3,"skills":{"fixture:guzheng":{"name":"测试","description":"测试",
            "optionLabels":{"take-rest":"获得其余的牌","decline":"不获得"}}}}
            """;
        Require(SkillProgramCatalog.Load(guzhengTemplate, guzhengPresentation)
                .Programs["fixture:guzheng"].Triggers.Single().Effects.Count == 6,
            "The Guzheng two-stage claim must load against the discard-phase ledger binding.");
        Reject(guzhengTemplate.Replace(
                "\"condition\":{\"kind\":\"choiceIs\",\"sourceBind\":\"rest-answer\",\"optionId\":\"take-rest\"}",
                "\"condition\":{\"kind\":\"wounded\"}"),
            guzhengPresentation, "the rest gain must be a named-choice branch");
        Reject(guzhengTemplate.Replace("\"resultBind\":\"pool\"}", "\"resultBind\":\"pool\",\"x\":1}"),
            guzhengPresentation, "unknown node keys must be rejected");
        Reject(guzhengTemplate.Replace(
                "{\"op\":\"bindDiscardPhaseDiscards\",\"target\":\"owner\",\"resultBind\":\"pool\"}",
                "{\"op\":\"bindDiscardPhaseDiscards\",\"target\":\"selectedTarget\",\"resultBind\":\"pool\"}"),
            guzhengPresentation, "the discard-phase pool always belongs to the ending turn owner");
    }

    public static void ZhijianGiftsEquipmentIntoEmptySlotAndDraws()
    {
        var registry = Registry(Mode, ZhijianDeck());
        var completed = 0;
        for (var seed = 1; seed <= 400 && completed < 1; seed++)
        {
            var game = Start(registry, seed, Mode);
            if (!DriveToFirstPlay(game)) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == Zhijian);
            if (action is null) continue;
            var before = game.CreateSnapshot(0, revealAll: true);
            Require(before.Players[0].Hand.Any(card => EquipmentCatalog.IsEquipment(card.Kind)),
                "An offered Zhijian action implies at least one hand equipment card.");
            var target = before.Players.First(player => player.IsAlive && player.Seat != 0).Seat;
            Require(action.SelectableTargetSeats.Contains(target) && !action.SelectableTargetSeats.Contains(0),
                "Zhijian must offer living others with free slots and never the owner.");

            Accept(game.Submit(new UseProgramSkillCommand(0, Zhijian, "gift-equipment", [], [target],
                game.Revision, game.PendingDecision!.PromptId)));
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } payment ||
                payment.Choices.All(choice => choice.Cards.Count == 0))
                continue;
            Require(payment.Choices.SelectMany(choice => choice.Cards).All(cardId =>
                    before.Players[0].Hand.Any(card => card.Id == cardId &&
                        EquipmentCatalog.IsEquipment(card.Kind))),
                "Only own hand equipment cards may be offered as the gift.");
            var giftCard = payment.Choices.First(choice => choice.Cards.Count > 0).Cards[0];

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            Accept(game, payment.Choices.First(choice => choice.Cards.Contains(giftCard)));
            Accept(replay, replay.PendingDecision!.Choices.First(choice => choice.Cards.Contains(giftCard)));

            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Zhijian gift must replay identically.");
            var movements = game.CardMovements.Where(item => item.CardId == giftCard).ToArray();
            Require(movements.Any(item => item.From == CardLocation.Hand(0) &&
                    item.To == new CardLocation(CardZoneKind.Equipment, target)),
                "The gifted equipment must move from the owner's hand to the target's equipment area.");
            Require(game.CreateSnapshot(0, revealAll: true).Players[target].Equipment
                    .Any(card => card.Id == giftCard),
                "The target must publicly hold the gifted equipment.");
            Require(game.Events.Select(item => item.Payload).OfType<EquipmentChangedEvent>()
                    .Any(item => item.PlayerSeat == target && item.CardId == giftCard &&
                        item.ReplacedCardId is null),
                "The empty-slot gift must publish an equipment-changed event without a replacement.");
            var after = game.CreateSnapshot(0, revealAll: true);
            Require(after.Players[0].Hand.Count == before.Players[0].Hand.Count &&
                    after.Players[0].Hand.Any(card =>
                        !before.Players[0].Hand.Any(before1 => before1.Id == card.Id)),
                "Zhijian must draw exactly one replacement card after the gift.");
            Require(game.CardMovements.Any(item => item.From.Zone == CardZoneKind.DrawPile &&
                    item.To == CardLocation.Hand(0) &&
                    item.Reason.Value.Contains(Zhijian, StringComparison.Ordinal)),
                "The draw must be attributed to the Zhijian skill reason.");
            completed++;
        }
        Require(completed == 1, "No seeded setup produced a Zhijian equipment gift.");
    }

    public static void ZhijianSkipsOccupiedSlotsAndUnequippedHands()
    {
        var registry = Registry(Mode, ZhijianDeck());
        var occupied = 0;
        var barren = 0;
        for (var seed = 1; seed <= 400 && (occupied < 1 || barren < 1); seed++)
        {
            var game = Start(registry, seed, Mode);
            if (!DriveToFirstPlay(game)) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == Zhijian);
            if (action is null)
            {
                Require(game.CreateSnapshot(0, revealAll: true).Players[0].Hand
                        .Any(card => EquipmentCatalog.IsEquipment(card.Kind)) == false,
                    "Without hand equipment Zhijian must stay unavailable while every slot is free.");
                barren++;
                continue;
            }
            if (occupied > 0) continue;
            var target = game.CreateSnapshot(0, revealAll: true).Players
                .First(player => player.IsAlive && player.Seat != 0).Seat;
            Accept(game.Submit(new UseProgramSkillCommand(0, Zhijian, "gift-equipment", [], [target],
                game.Revision, game.PendingDecision!.PromptId)));
            if (game.PendingDecision is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } payment ||
                payment.Choices.All(choice => choice.Cards.Count == 0))
                continue;
            Accept(game, payment.Choices.First(choice => choice.Cards.Count > 0));
            if (!AwaitPlayPrompt(game)) continue;
            var second = game.GetHumanLegalActions().FirstOrDefault(item =>
                item.Kind == LegalActionKind.UseProgramSkill && item.ProgramSkillId == Zhijian);
            Require(second is not null && second.SelectableTargetSeats.Any(seat => seat != target) &&
                    !second.SelectableTargetSeats.Contains(target),
                "A gifted slot must drop the recipient while other free-slot targets remain.");
            occupied++;
        }
        Require(occupied == 1, "No seeded setup produced a second Zhijian against an occupied slot.");
        Require(barren == 1, "No seeded setup produced an equipment-free Zhijian hand.");
    }

    public static void GuzhengReturnsOneCardAndMayTakeTheRest()
    {
        RunGuzhengClaim("take-rest");
    }

    public static void GuzhengDeclineKeepsTheRestInDiscardPile()
    {
        RunGuzhengClaim("decline");
    }

    public static void GuzhengStaysSilentForOwnTurnAndEmptyPools()
    {
        var registry = Registry(WealthyMode, GuzhengDeck(), bankBaseHp: 8);
        var game = Start(registry, 1, WealthyMode);
        // Drive seat 0's whole first turn (its own end-phase discards must not
        // open the window) plus seat 1's first turn (an uninjured bank sheds no
        // hand cards, so the phase pool stays empty). The window ends when
        // seat 2's turn starts.
        for (var step = 0; step < 1200; step++)
        {
            if (game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>()
                    .Any(item => item.ActorSeat == 2)) break;
            if (game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    SkillPrompt.SkillId: Guzheng
                })
                throw new InvalidOperationException(
                    "Guzheng must stay silent for its own turn ending and for empty discard-phase pools.");
            Step(game);
        }
        Require(game.Events.Select(item => item.Payload).OfType<TurnStartedEvent>()
                .Any(item => item.ActorSeat == 2),
            "The fixture must reach the second bank's turn.");
        Require(game.Events.Select(item => item.Payload).All(item =>
                item is not ProgramBindingStartedEvent { SkillId: Guzheng }),
            "No Guzheng window may open without another turn owner's phase discards.");
    }

    private static void RunGuzhengClaim(string optionId)
    {
        var registry = Registry(Mode, GuzhengDeck());
        var completed = 0;
        for (var seed = 1; seed <= 120 && completed < 1; seed++)
        {
            var game = Start(registry, seed, Mode);
            if (!DriveToGuzhengPrompt(game)) continue;
            var turnOwner = game.Events.Select(item => item.Payload)
                .OfType<TurnStartedEvent>().Last().ActorSeat;
            Require(turnOwner != 0,
                "Guzheng must only open at another living player's turn ending.");

            var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
            AcceptTrigger(game);
            AcceptTrigger(replay);
            Accept(game, AwaitTargetChoice(game, turnOwner));
            Accept(replay, AwaitTargetChoice(replay, turnOwner));
            var selection = AwaitPoolChoice(game);
            var selectionReplay = AwaitPoolChoice(replay);
            Require(selection.Cards.SequenceEqual(selectionReplay.Cards) && selection.Cards.Count == 1,
                "The Guzheng return choice must offer exactly the frozen pool cards on both sides.");
            var pool = selection.Cards.ToArray();
            Require(pool.All(cardId => game.CardMovements.Any(item =>
                    item.CardId == cardId && item.From == new CardLocation(CardZoneKind.Hand, turnOwner) &&
                    item.To == CardLocation.DiscardPile)),
                "Every offered card must be a hand card the turn owner discarded this phase.");

            Accept(game, selection);
            Accept(replay, selectionReplay);
            Accept(game, AwaitOptionChoice(game, optionId));
            Accept(replay, AwaitOptionChoice(replay, optionId));

            Require(Events(game).SequenceEqual(Events(replay)) && State(game) == State(replay),
                "The Guzheng claim must replay identically through the two-stage decision.");
            var movements = game.CardMovements.Where(item => pool.Contains(item.CardId) &&
                    item.From == CardLocation.DiscardPile && item.To.Zone == CardZoneKind.Hand &&
                    item.Reason.Value.Contains(Guzheng, StringComparison.Ordinal)).ToArray();
            Require(movements.Count(item => item.To == new CardLocation(CardZoneKind.Hand, turnOwner)) == 1,
                "Guzheng must return exactly one chosen card to the turn owner's hand.");
            var returned = movements.Single(item => item.To == new CardLocation(CardZoneKind.Hand, turnOwner))
                .CardId;
            var rest = movements.Where(item => item.To == CardLocation.Hand(0))
                .Select(item => item.CardId).Order().ToArray();
            if (optionId == "take-rest")
            {
                Require(rest.SequenceEqual(pool.Where(cardId => cardId != returned).Order()),
                    "The take-rest branch must move every remaining pool card into the owner's hand.");
            }
            else
            {
                Require(rest.Length == 0,
                    "The decline branch must leave every remaining pool card in the discard pile.");
            }
            completed++;
        }
        Require(completed == 1, $"No seeded setup completed the Guzheng {optionId} branch.");
    }

    private static void AcceptTrigger(GameEngine game)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException(
            "The Guzheng trigger prompt vanished.");
        Accept(game, prompt.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("program-action") == "activate"));
    }

    private static PromptChoice AwaitTargetChoice(GameEngine game, int turnOwner)
    {
        for (var step = 0; step < 600 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { } prompt)
            {
                if (prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "select-target" &&
                        choice.Targets.SequenceEqual([turnOwner])) is { } choice)
                    return choice;
                throw new InvalidOperationException(
                    "The Guzheng target selection was displaced by: " + Describe(prompt));
            }
            Advance(game);
        }
        throw new InvalidOperationException("The Guzheng target selection never appeared.");
    }

    private static PromptChoice AwaitPoolChoice(GameEngine game)
    {
        for (var step = 0; step < 600 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { } prompt)
            {
                if (prompt.Choices.FirstOrDefault(choice => choice.Cards.Count > 0) is { } choice)
                    return choice;
                throw new InvalidOperationException(
                    "The Guzheng pool selection was displaced by: " + Describe(prompt));
            }
            Advance(game);
        }
        throw new InvalidOperationException("The Guzheng pool selection never appeared.");
    }

    private static PromptChoice AwaitOptionChoice(GameEngine game, string optionId)
    {
        for (var step = 0; step < 600 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { } prompt)
            {
                if (prompt.Choices.FirstOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("option-id") == optionId) is { } choice)
                    return choice;
                throw new InvalidOperationException(
                    "The Guzheng " + optionId + " prompt was displaced by: " + Describe(prompt));
            }
            Advance(game);
        }
        throw new InvalidOperationException("The Guzheng " + optionId + " prompt never appeared.");
    }

    private static string Describe(PendingDecision prompt) =>
        prompt.Kind + " / seat " + prompt.PlayerSeat + " / " +
        string.Join(',', prompt.Choices.Select(item =>
            item.Parameters.GetValueOrDefault("program-action") ??
            item.Parameters.GetValueOrDefault("option-id") ?? "?"));

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhang Zhao Zhang Hong fixture did not advance.");
    }

    private static bool DriveToGuzhengPrompt(GameEngine game)
    {
        for (var step = 0; step < 2400 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is
                {
                    Kind: DecisionKind.ProgramTrigger,
                    PlayerSeat: 0,
                    SkillPrompt.SkillId: Guzheng
                } prompt &&
                prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"))
                return true;
            Step(game);
        }
        return false;
    }

    private static bool AwaitPlayPrompt(GameEngine game)
    {
        for (var step = 0; step < 600 && game.State.Status != EngineStatus.Completed; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return true;
            if (game.PendingDecision is { PlayerSeat: 0 } prompt)
                throw new InvalidOperationException(
                    "The play phase was displaced by: " + Describe(prompt));
            Advance(game);
        }
        return false;
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
                            Accept(game, choice);
                            return;
                        }
                }
            }
            // AI seats resolve automatically on advance.
        }
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhang Zhao Zhang Hong fixture did not advance.");
    }

    private static void Accept(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
            choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Zhang Zhao Zhang Hong answer failed.");
    }

    private static void Accept(CommandResult result)
    {
        Require(result.Accepted, result.Error?.Message ?? "Zhang Zhao Zhang Hong command failed.");
    }

    private static ContentRegistry Registry(string mode, ContentDeckRecipe deck, int bankBaseHp = 8) =>
        ContentRegistry.Build(
            new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
            new ZhangZhaoZhangHongScenario(mode, deck, bankBaseHp));

    private static ContentDeckRecipe ZhijianDeck() => DeckCore(index => (index % 3) switch
    {
        0 => "standard:bagua",
        1 => "standard:peach",
        _ => "standard:slash"
    });

    private static ContentDeckRecipe GuzhengDeck() => DeckCore(index => (index % 3) switch
    {
        0 => "standard:slash",
        1 => "standard:peach",
        _ => "standard:bagua"
    });

    private static ContentDeckRecipe DeckCore(Func<int, string> kindFor) =>
        new("fixture:zhang-zhao-zhang-hong-deck", "张昭张纮测试牌堆", 5, 2, [])
        {
            PhysicalCards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard(kindFor(index), (Suit)(index % 4), index % 13 + 1)).ToArray()
        };

    private static GameEngine Start(ContentRegistry registry, int seed, string mode)
    {
        // Setup candidates are sampled per seed; scan forward until seat 0 is
        // offered Zhang Zhao & Zhang Hong, then select and return the started engine.
        for (var attempt = 0; attempt < 200; attempt++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed + attempt,
                PlayerCount = 5,
                HumanSeat = 0,
                HumanRole = Role.Lord,
                ModeId = mode,
                UseInteractiveSetup = true,
                UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false,
                MaxTurns = 40
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted,
                "Zhang Zhao Zhang Hong fixture did not start.");
            var choice = game.PendingDecision!;
            if (!choice.ValidContentIds.Contains(General, StringComparer.Ordinal)) continue;
            var selected = game.Submit(new SelectGeneralCommand(0, General, game.Revision, choice.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Zhang Zhao Zhang Hong selection failed.");
            return game;
        }
        throw new InvalidOperationException(
            $"No seed from {seed} onward offered Zhang Zhao & Zhang Hong to seat 0 in {mode}.");
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
            $"Expected invalid Zhang Zhao Zhang Hong composition to be rejected" +
            $"{(because.Length == 0 ? "" : $": {because}")}.");
    }

    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class ZhangZhaoZhangHongScenario(string modeId, ContentDeckRecipe deck, int bankBaseHp)
        : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("zhang-zhao-zhang-hong-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 158, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            // Seat 0 is the human Zhang Zhao & Zhang Hong; seats 1-4 are
            // skill-less AI banks so the only live skills sit on the human seat.
            foreach (var index in new[] { 1, 2, 3, 4 })
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:zzh-bank-{index}",
                    "测试对手", "supporter", "standard:none",
                    index == 3 ? "shu" : "qun", BaseHp: bankBaseHp));
            builder.AddDeck(deck);
            builder.AddMode(new ContentModeDefinition(modeId, "张昭张纮测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2,
                    [nameof(Role.Renegade)] = 1
                }, deck.Id, GeneralCandidateCount: 5,
                GeneralPoolIds: [General,
                    "fixture:zzh-bank-1",
                    "fixture:zzh-bank-2",
                    "fixture:zzh-bank-3",
                    "fixture:zzh-bank-4"]));
        }
    }
}
