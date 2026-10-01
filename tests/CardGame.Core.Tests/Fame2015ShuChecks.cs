using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2015ShuChecks
{
    public static void AlternativeBasicCostSourceUseAndDyingBoundaries()
    {
        var authorityRejected = false;
        try
        {
            SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:alternative-source","revision":1,
             "viewAs":[{"id":"top-cost","inputKinds":[],"inputSuits":[],"sourceZones":["authority"],"outputKind":"slash",
               "forPlay":true,"forResponse":false,"useOnly":true,"costDestination":"drawPileTop"}]}]}
            """, """{"schemaVersion":3,"skills":{"fixture:alternative-source":{"name":"来源边界","description":"实体来源"}}}""");
        }
        catch (InvalidOperationException error) when (error.Message.Contains("alternative-cost viewAs requires")) { authorityRejected = true; }
        Require(authorityRejected, "A top-deck cost helper must reject unsupported authority sources before creating a game.");
        var registry = Registry(new Fixture("classic:huomo", ["standard:slash", "standard:crossbow", "standard:duel"]));
        GameEngine? game = null;
        // Fixed seeds reach the same native and converted source boundary without a search.
        foreach (var seed in new[] { 1 })
        {
            var candidate = Create(registry, seed); ReachPlay(candidate);
            if (candidate.GetHumanLegalActions().Any(item => item.Kind == LegalActionKind.Slash && item.ConversionSource is null) &&
                candidate.GetHumanLegalActions().Any(item => item.ConversionSource?.SkillId == "classic:huomo"))
                game = candidate;
        }
        Require(game is not null, "The fixture must expose native and converted basic-card sources together.");
        var native = game!.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash && item.ConversionSource is null);
        Require(game.Submit(new PlayCardCommand(0, native.CardId!.Value, native.TargetSeats, game.Revision, Prompt(game)!.PromptId)).Accepted, "A native Slash must enter the ordinary use ledger.");
        Settle(game);
        Require(game.GetHumanLegalActions().All(item => item.ConversionSource?.SkillId != "classic:huomo" || item.PlayedCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)), "A native Slash must block every converted Slash name in this turn.");
        Equal(game, Restore(game, registry));

        registry = Registry(new Fixture("classic:huomo", ["standard:crossbow"]));
        game = Create(registry); ReachPlay(game);
        var alcohol = game.GetHumanLegalActions().First(action => action.PlayedCardKind == CardKind.Alcohol && action.ConversionSource?.SkillId == "classic:huomo");
        var alcoholCost = alcohol.CardId!.Value; var alcoholRestore = Restore(game, registry);
        foreach (var branch in new[] { game, alcoholRestore })
        {
            var command = new PlayCardCommand(0, alcoholCost, alcohol.TargetSeats, branch.Revision, Prompt(branch)!.PromptId, alcohol.PlayedCardKind)
                { ConversionSource = alcohol.ConversionSource };
            Require(branch.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()).Accepted, "A play-phase Huomo Alcohol use must survive command JSON.");
            Settle(branch);
        }
        Equal(game, alcoholRestore); Equal(game, Restore(game, registry));
        Require(game.CreateSnapshot(0, true).Players[0].HasAlcoholEffect &&
            game.CardMovements.Count(move => move.CardId == alcoholCost && move.From == CardLocation.Hand(0) && move.To == CardLocation.DrawPile) == 1 &&
            game.CardMovements.All(move => move.CardId != alcoholCost || move.To != CardLocation.Processing && move.To != CardLocation.DiscardPile) &&
            game.GetHumanLegalActions().All(action => action.PlayedCardKind != CardKind.Alcohol || action.ConversionSource?.SkillId != "classic:huomo"),
            "Actual play-phase Alcohol must grant its wine effect, preserve its top-deck cost and consume the same-turn Alcohol-use name.");

        registry = Registry(new Fixture("classic:huomo", ["standard:slash", "standard:crossbow", "standard:duel"], targetHp: 1));
        game = null;
        foreach (var seed in new[] { 1 })
        {
            var candidate = Create(registry, seed); ReachPlay(candidate); var snapshot = candidate.CreateSnapshot(0, true);
            if (snapshot.Players[1].Role == Role.Rebel && snapshot.Players[0].Hand.Any(card => card.Kind == CardKind.Slash) && snapshot.Players[1].Hand.Any(card => card.Kind == CardKind.Slash) &&
                candidate.GetHumanLegalActions().Any(item => item.Kind == LegalActionKind.Duel && item.TargetSeat == 1) &&
                candidate.GetHumanLegalActions().Any(item => item.ConversionSource?.SkillId == "classic:huomo"))
                game = candidate;
        }
        Require(game is not null, "A Duel fixture must expose an ordinary Slash response alongside the use-only cost.");
        var duel = game!.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Duel && item.TargetSeat == 1);
        Require(game.Submit(new PlayCardCommand(0, duel.CardId!.Value, [1], game.Revision, Prompt(game)!.PromptId)).Accepted, "A native Duel must open actual response windows.");
        Reach(game, prompt => prompt.Kind == DecisionKind.RespondSlash && prompt.PlayerSeat == 0);
        Require(Prompt(game)!.Choices.All(choice => choice.Parameters.GetValueOrDefault("conversion-skill-id") != "classic:huomo"), "A use-only conversion cannot be offered for a Duel's Play/Respond Slash.");
        Atomic(game, new AnswerPromptCommand(0, Prompt(game)!.PromptId, new ChoiceId("huomo-forged-response"), game.Revision));
        var responseRestore = Restore(game, registry);
        Both(game, responseRestore, choice => choice.Cards.Count == 0); Settle(game); Settle(responseRestore); Equal(game, responseRestore);

        foreach (var output in new[] { CardKind.Peach, CardKind.Alcohol })
        {
            registry = Registry(new Fixture("classic:huomo", ["standard:crossbow"], boundaryDriver: true));
            game = Create(registry); ReachPlay(game);
            while (game.CreateSnapshot(0, true).Players[0].Hp > 1)
            { Activate(game, "fixture:fame2015-health", "lose", [], []); Settle(game); }
            Activate(game, "fixture:fame2015-health", "lose", [], []);
            Reach(game, prompt => prompt.Kind == DecisionKind.RescueDying && prompt.PlayerSeat == 0);
            var choices = Prompt(game)!.Choices;
            var selected = choices.First(choice => choice.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:huomo" && choice.Parameters.GetValueOrDefault("response") == output.ToString().ToLowerInvariant());
            var cost = selected.Cards.Single(); var restored = Restore(game, registry);
            Both(game, restored, choice => choice.Id == selected.Id); Settle(game); Settle(restored); Equal(game, restored);
            Require(game.CreateSnapshot(0, true).Players[0].Hp == 1 && game.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Hand(0) && move.To == CardLocation.DrawPile) == 1 && !game.CardMovements.Any(move => move.CardId == cost && move.To == CardLocation.DiscardPile), "A rescue Use must recover one HP while preserving the real nonbasic cost on draw-pile top.");
            Require(game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Any(item => item.Action.Type == CardActionType.Use && item.Action.EffectiveKind == output && item.Action.ConversionChain.Any(source => source.SkillId == "classic:huomo")), "Dying rescue must retain its explicit use-only source and Use classification.");
        }

        // These are actual incoming attacks, not an artificial response context:
        // the AI equips a Crossbow and uses two Slashes in the same turn.
        foreach (var converted in new[] { true, false })
        {
            registry = Registry(new Fixture("classic:huomo",
                ["standard:slash", "standard:slash", "standard:crossbow", "standard:dodge"], responseDraw: true));
            game = null;
            foreach (var seed in new[] { 3 })
            {
                var candidate = Create(registry, seed);
                if (FindDodge(candidate, prompt =>
                {
                    var full = candidate.CreateSnapshot(0, true); var attacker = full.Players[full.CurrentSeat];
                    return prompt.IncomingCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash &&
                        prompt.Choices.Any(IsHuomo) && prompt.Choices.Any(IsNativeDodge) &&
                        attacker.Equipment.Any(card => card.Kind == CardKind.Crossbow) && attacker.Hand.Any(card => card.Kind == CardKind.Slash) &&
                        full.Players[0].Hand.Any(card => card.Kind == CardKind.Dodge);
                })) game = candidate;
            }
            Require(game is not null, "A real repeated Slash fixture must expose native and Huomo Dodge before a second same-turn attack.");
            var g = game!; var prompt = Prompt(g)!; var turn = g.CreateSnapshot(0, true).TurnNumber;
            var selected = prompt.Choices.First(converted ? IsHuomo : IsNativeDodge); var cost = selected.Cards.Single();
            Require(g.CreateSnapshot(2).Players[0].Hand.Count == 0 &&
                (g.CreateSnapshot(2).PendingDecision is null || g.CreateSnapshot(2).PendingDecision!.Choices.All(choice => !choice.Cards.Contains(cost))),
                "An observer must not see the responder's private physical cost before it is accepted.");
            Atomic(g, new AnswerPromptCommand(0, prompt.PromptId, new ChoiceId(selected.Id.Value + ".forged-source-owner-1"), g.Revision));
            var restored = Restore(g, registry); Both(g, restored, choice => choice.Id == selected.Id);
            Require(Prompt(g)?.SkillPrompt?.SkillId == "fixture:fame2015-response-draw", "Accepted Dodge must pause at a real optional response observer.");
            if (converted)
                Require(g.CardMovements.Count(move => move.CardId == cost && move.From == CardLocation.Hand(0) && move.To == CardLocation.DrawPile) == 1 &&
                    g.CreateSnapshot(0, true).Players[0].Hand.All(card => card.Id != cost), "Huomo must place its actual black nonbasic card on the draw pile before response triggers.");
            Equal(g, restored); var pausedRestore = Restore(g, registry); Equal(g, pausedRestore);
            Both(g, restored, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Answer(pausedRestore, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Settle(g); Settle(restored); Settle(pausedRestore); Equal(g, restored); Equal(g, pausedRestore);
            if (converted)
            {
                Require(g.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Id == cost) &&
                    g.CardMovements.Any(move => move.CardId == cost && move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0)) &&
                    g.CardMovements.All(move => move.CardId != cost || move.To != CardLocation.Processing && move.To != CardLocation.DiscardPile),
                    "A nested draw must receive the actual top cost; accepted-response cleanup must not discard or reprocess it.");
                Require(g.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Any(item =>
                    item.Action.EffectiveKind == CardKind.Dodge && item.Action.ActorSeat == 0 && item.Action.ProviderSeat == 0 &&
                    item.Action.ConversionChain.Any(source => source.SkillId == "classic:huomo") &&
                    item.Action.PhysicalCards.Any(physical => physical.CardId == cost && physical.From == CardLocation.Hand(0))),
                    "The typed Dodge response must freeze its Huomo source and original physical provenance.");
            }
            Require(FindDodge(g, prompt => prompt.IncomingCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash, turn),
                "The real Crossbow attacker must open a second Dodge window in this same turn.");
            Require(g.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Kind == CardKind.Crossbow) && Prompt(g)!.Choices.All(choice => !IsHuomo(choice)),
                "Both native and converted used Dodge must block Huomo Dodge again this turn despite an available black nonbasic cost.");
            Equal(g, Restore(g, registry));
        }

        // Arrow Barrage and a Hujia provider play Dodge. Neither is a Dodge use.
        foreach (var assistance in new[] { false, true })
        {
            var responseCandidates = new List<string>();
            registry = Registry(new Fixture("classic:huomo", assistance
                ? ["standard:slash", "standard:slash", "standard:crossbow", "standard:dodge"]
                : ["standard:arrow_barrage", "standard:crossbow", "standard:dodge"], factionDefense: assistance, responseTarget: true));
            game = null;
            // The provider branch needs seed 49; the Arrow Barrage branch uses seed 1.
            foreach (var seed in new[] { assistance ? 49 : 1 })
            {
                var candidate = Create(registry, seed, assistance ? Role.Loyalist : Role.Lord);
                if (FindDodge(candidate, prompt =>
                {
                    var full = candidate.CreateSnapshot(0, true); var attacker = full.Players[full.CurrentSeat];
                    responseCandidates.Add($"{seed}/{prompt.IncomingCard}:seat{full.CurrentSeat},role{attacker.Role},black{full.Players[0].Hand.Count(card => card.Kind == CardKind.Crossbow)},slash{attacker.Hand.Count(card => card.Kind == CardKind.Slash)},bow{attacker.Hand.Concat(attacker.Equipment).Count(card => card.Kind == CardKind.Crossbow)}");
                    return full.CurrentSeat is 1 or 3 && full.Players[0].Hand.Any(card => card.Kind == CardKind.Crossbow) &&
                        (assistance ? full.CurrentSeat == 1 && full.Players[2].Role == Role.Lord && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "faction-defense-dodge")
                        : attacker.Role == Role.Rebel &&
                          prompt.IncomingCard == CardKind.ArrowBarrage && prompt.Choices.Any(IsNativeDodge));
                })) game = candidate;
            }
            Require(game is not null, $"A real Arrow Barrage or Hujia provider must open a physical Dodge play window. assistance={assistance}, candidates={string.Join(";", responseCandidates.Take(24))}");
            var g = game!; var prompt = Prompt(g)!; var turn = g.CreateSnapshot(0, true).TurnNumber;
            Require(prompt.Choices.All(choice => !IsHuomo(choice)) && g.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Kind == CardKind.Crossbow),
                "Huomo must reject played Dodge despite an available black nonbasic cost.");
            Atomic(g, new AnswerPromptCommand(0, prompt.PromptId, new ChoiceId("black-nonbasic-dodge.forged-use"), g.Revision));
            var nativeChoice = prompt.Choices.First(choice => choice.Cards.Count == 1 && !IsHuomo(choice));
            var restored = Restore(g, registry); Both(g, restored, choice => choice.Id == nativeChoice.Id);
            foreach (var branch in new[] { g, restored })
                Require(FindDodge(branch, prompt => prompt.IncomingCard is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash &&
                    prompt.Choices.All(choice => choice.Parameters.GetValueOrDefault("response") != "faction-defense-decline"), turn),
                    $"After playing Dodge, the same turn must reach the provider's own actual Slash defense. assistance={assistance}, oldTurn={turn}, turn={branch.CreateSnapshot(0, true).TurnNumber}, seat={branch.CreateSnapshot(0, true).CurrentSeat}, pending={Prompt(branch)?.Kind}");
            Equal(g, restored);
            Require(Prompt(g)!.Choices.Any(IsHuomo), "Played Arrow/Hujia Dodge must not consume this turn's unused Dodge-use name.");
            Equal(g, Restore(g, registry));
        }

        static bool IsHuomo(PromptChoice choice) => choice.Parameters.GetValueOrDefault("conversion-skill-id") == "classic:huomo";
        static bool IsNativeDodge(PromptChoice choice) => choice.Cards.Count == 1 && !IsHuomo(choice);
        static bool FindDodge(GameEngine candidate, Func<PendingDecision, bool> predicate, int? sameTurn = null)
        {
            for (var step = 0; step < 256; step++)
            {
                var snapshot = candidate.CreateSnapshot(0, true);
                if (snapshot.Winner != Winner.None || sameTurn is { } turn && snapshot.TurnNumber != turn) return false;
                var pending = Prompt(candidate);
                if (pending is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } && predicate(pending)) return true;
                if (pending is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
                    Require(candidate.Submit(new EndPlayPhaseCommand(0, candidate.Revision, pending.PromptId)).Accepted, "The fixture must end its own phase while waiting for an actual incoming attack.");
                else if (pending is { PlayerSeat: 0 })
                {
                    if (pending.Kind == DecisionKind.RespondDodge && (pending.IncomingCard == CardKind.ArrowBarrage ||
                        pending.Choices.Any(choice => choice.Parameters.GetValueOrDefault("response") == "faction-defense-dodge")) && pending.Choices.Any(IsNativeDodge))
                    { Answer(candidate, IsNativeDodge); continue; }
                    var decline = pending.Choices.FirstOrDefault(choice => choice.Cards.Count == 0 &&
                        (choice.Parameters.GetValueOrDefault("response") is "pass" or "decline" or "take-damage" or "faction-defense-decline" ||
                         choice.Parameters.GetValueOrDefault("program-action") == "decline"));
                    if (decline is null) return false;
                    Answer(candidate, choice => choice.Id == decline.Id);
                }
                else Require(candidate.Submit(new AdvanceOneStepCommand(candidate.Revision)).Accepted, "A real AI attack must advance normally.");
            }
            return false;
        }
    }
    public static void IdentityOneHpBoundaryAndRealSlashReplay()
    {
        var registry = Registry(new Fixture("classic:shizhi", ["standard:dodge"], boundaryDriver: true));
        var game = Create(registry); ReachPlay(game);
        while (game.CreateSnapshot(0, true).Players[0].Hp > 2)
        { Activate(game, "fixture:fame2015-health", "lose", [], []); Settle(game); }
        Require(game.GetHumanLegalActions().All(action => action.Kind != LegalActionKind.Slash), "At two HP, physical Dodge must keep its native identity.");
        Activate(game, "fixture:fame2015-health", "lose", [], []); Settle(game);
        var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash && item.ConversionSource?.SkillId == "classic:shizhi");
        var physical = game.CreateSnapshot(0, true).Players[0].Hand.Single(card => card.Id == action.CardId);
        Require(physical.Kind == CardKind.Dodge && game.CreateSnapshot(0, true).Players[0].Hp == 1, "Exactly one HP must turn a physical Dodge into a legal Slash.");
        var restored = Restore(game, registry);
        foreach (var branch in new[] { game, restored })
        {
            Require(branch.Submit(new PlayCardCommand(0, physical.Id, action.TargetSeats, branch.Revision, Prompt(branch)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource }).Accepted, "A one-HP identity Slash must pay its actual Dodge.");
            Settle(branch);
        }
        Equal(game, restored);
        Require(game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Any(item => item.Action.Type == CardActionType.Use && item.Action.EffectiveKind == CardKind.Slash && item.Action.PhysicalCards.Any(cost => cost.CardId == physical.Id && cost.CardKind == CardKind.Dodge)), "Typed accepted use must retain effective Slash and physical Dodge separately.");
        Activate(game, "fixture:fame2015-health", "recover", [], []); Settle(game);
        Require(game.CreateSnapshot(0, true).Players[0].Hp == 2 && game.GetHumanLegalActions().All(item => item.ConversionSource?.SkillId != "classic:shizhi"), "Recovery above one HP must immediately restore all remaining Dodge identities.");
    }

    public static void EqualHandsOutsideTurnDrawsAndReplay()
    {
        var registry = Registry(new Fixture("classic:qiaoshi", ["standard:dodge"])); var game = Create(registry); ReachPlay(game);
        Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)).Accepted, "The skill owner must finish its own turn.");
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:qiaoshi");
        var target = Prompt(game)!.TargetSeat!.Value; var before = game.CreateSnapshot(0, true);
        Require(target != 0 && before.Players[0].Hand.Count == before.Players[target].Hand.Count, "The trigger must belong to another character's ending with exactly equal hand counts.");
        var restored = Restore(game, registry); Both(game, restored, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        if (Prompt(game) is { Kind: DecisionKind.ProgramTrigger } select && select.Choices.Any(choice => choice.Targets.Contains(target))) Both(game, restored, choice => choice.Targets.Contains(target));
        Equal(game, restored);
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before.Players[0].Hand.Count + 1 && game.CreateSnapshot(0, true).Players[target].Hand.Count == before.Players[target].Hand.Count + 1, "Outside-turn equal hands must draw exactly one for each participant.");
    }
    public static void AllHandDuelDrawLimitPaymentAndReplay()
    {
        foreach (var failure in new[] { false, true })
        {
            var registry = Registry(new Fixture("classic:zhanjue", failure ? ["standard:slash"] : ["standard:dodge"], targetHp: failure ? 1 : 10));
            var game = Create(registry); ReachPlay(game);
            var firstHand = game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).Order().ToArray();
            Atomic(game, new UseProgramSkillCommand(0, "classic:zhanjue", "all-hand-duel", firstHand[..^1], [], game.Revision, Prompt(game)!.PromptId));
            var count = failure ? 1 : 2;
            for (var use = 0; use < count; use++)
            {
                var hand = game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).Order().ToArray();
                Activate(game, "classic:zhanjue", "all-hand-duel", hand, []);
                Require(Prompt(game)!.Choices.All(choice => choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.Duel)), "A fixed all-hand conversion must expose only Duel.");
                var restored = Restore(game, registry);
                Both(game, restored, choice => choice.Targets.SequenceEqual(new[] { 1 })); Settle(game); Settle(restored); Equal(game, restored);
                foreach (var card in hand)
                    Require(game.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1,
                        "All-hand Duel must pay every selected physical owner card exactly once.");
                Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == (failure ? 2 : 1), $"Successful Duel draws one owner card; suffering its actual damage draws two. failure={failure}, use={use}, hand={game.CreateSnapshot(0, true).Players[0].Hand.Count}, damage={System.Text.Json.JsonSerializer.Serialize(game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().ToArray())}");
                if (!failure && use == 0) Require(game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:zhanjue"), "One actual owner draw must leave a second Duel legal.");
            }
            Require(game.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:zhanjue"), "Two actual participant draws must disable all-hand Duel for the phase.");
            Require(game.Events.Any(item => item.Payload is ProgramBooleanStateChangedEvent { StateId: "zhanjue-exhausted", Value: true }), "The threshold must publish its typed public state.");
        }
        {
            var registry = Registry(new Fixture("classic:zhanjue", ["standard:dodge"], actorReplacement: true));
            var game = Create(registry); ReachPlay(game);
            Activate(game, "classic:zhanjue", "all-hand-duel", game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToArray(), []);
            Answer(game, choice => choice.Targets.SequenceEqual(new[] { 1 }));
            Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:zenhui" && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Answer(game, choice => choice.Targets.SequenceEqual(new[] { 2 }));
            Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "become-user");
            Require(game.ResolutionStack.OfType<CardUseFrame>().Single().Action is { ProviderSeat: 0 } action && action.ConversionChain.Any(source => source.OwnerSeat == 0 && source.SkillId == "classic:zhanjue"), "Actor replacement must retain the original all-hand conversion owner and physical provider in its frozen use.");
            var restored = Restore(game, registry); Both(game, restored, choice => choice.Cards.Count == 1);
            Require(game.Events.Any(item => item.Payload is ProgramCardUseActorReplacedEvent { ActorSeat: 2, ProviderSeat: 0 }), "The typed replacement must retain its new actor and original physical provider.");
            Settle(game); Settle(restored); Equal(game, restored);
            Require(game.CardMovements.Count(move => move.To == CardLocation.Hand(0) && move.Reason.Value == "skill-program.classic:zhanjue.completed-participant-draw") == 1 &&
                game.CardMovements.All(move => move.To != CardLocation.Hand(2) || move.Reason.Value != "skill-program.classic:zhanjue.completed-participant-draw"), "A replaced Duel actor must not receive the original skill owner's completion draw.");
        }
        {
            var registry = Registry(new Fixture("classic:zhanjue", ["standard:dodge"], targetHp: 1));
            var game = Create(registry); ReachPlay(game);
            Activate(game, "classic:zhanjue", "all-hand-duel", game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToArray(), []);
            var restored = Restore(game, registry); Both(game, restored, choice => choice.Targets.SequenceEqual(new[] { 1 }));
            Settle(game); Settle(restored); Equal(game, restored);
            Require(!game.CreateSnapshot(0, true).Players[1].IsAlive && game.CardMovements.Count(move => move.To == CardLocation.Hand(0) && move.Reason.Value == "skill-program.classic:zhanjue.completed-participant-draw") == 1 &&
                game.Events.All(item => item.Payload is not ProgramBooleanStateChangedEvent { StateId: "zhanjue-exhausted", Value: true }), "A killed Duel target must leave the original owner completion draw intact; native kill rewards must not consume its two-draw limit.");
        }
    }

    public static void SlashRecastThresholdEndRewardAndReplay()
    {
        var registry = Registry(new Fixture("classic:yanyu", ["standard:slash", "standard:fire_slash", "standard:thunder_slash"]));
        var game = Create(registry); ReachPlay(game);
        for (var index = 0; index < 2; index++)
        {
            var card = game.CreateSnapshot(0, true).Players[0].Hand[0]; var before = game.CreateSnapshot(0, true).Players[0].Hand.Count;
            Activate(game, "classic:yanyu", "recast-slash", [card.Id], []); Settle(game);
            Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == before && game.Events.Any(item => item.Payload is CardRecastEvent recast && recast.CardId == card.Id), "Slash recast must consume its actual card and draw exactly one replacement.");
            Require(game.CardMovements.Any(move => move.CardId == card.Id && move.Reason == CardMoveReasons.RecastDiscard), "Recast must retain the recast movement reason rather than an ordinary discard.");
        }
        Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)).Accepted, "Play phase must end through a real command.");
        Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:yanyu"); Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Require(Prompt(game)!.Choices.All(choice => choice.Targets.All(seat => registry.Generals[game.CreateSnapshot(0, true).Players[seat].GeneralId].Gender == GeneralGender.Male)), "End reward must select a male character.");
        var seat = Prompt(game)!.Choices.First().Targets.Single(); var beforeReward = game.CreateSnapshot(0, true).Players[seat].Hand.Count;
        var restored = Restore(game, registry); Both(game, restored, choice => choice.Targets.Contains(seat)); Equal(game, restored);
        Require(game.CreateSnapshot(0, true).Players[seat].Hand.Count == beforeReward + 2, "Two recast Slashes must unlock exactly two reward cards at play ending.");
    }

    public static void PairedRevealAllFourBranchesPrivacyAndReplay()
    {
        foreach (var ownerSlash in new[] { false, true }) foreach (var targetDodge in new[] { false, true })
        {
            var registry = Registry(new Fixture("classic:wurong", ["standard:slash", "standard:dodge", "standard:peach"]));
            GameEngine? game = null; int ownerCard = 0; int targetCard = 0;
            for (var seed = 1; seed <= 64 && game is null; seed++)
            {
                var candidate = Create(registry, seed); ReachPlay(candidate); var full = candidate.CreateSnapshot(0, true);
                var own = full.Players[0].Hand.FirstOrDefault(card => (card.Kind == CardKind.Slash) == ownerSlash);
                var theirs = full.Players[1].Hand.FirstOrDefault(card => (card.Kind == CardKind.Dodge) == targetDodge);
                if (own is null || theirs is null) continue; game = candidate; ownerCard = own.Id; targetCard = theirs.Id;
            }
            Require(game is not null, "The bounded paired-reveal deck must provide its physical branch cards.");
            var g = game!; var before = g.CreateSnapshot(0, true); var beforeEvents = g.Events.Count;
            Activate(g, "classic:wurong", "paired-hand-reveal", [ownerCard], [1]);
            Require(g.Events.Skip(beforeEvents).All(item => item.Payload is not ProgramCardsRevealedEvent), "The first privately committed card must stay unrevealed until both choices are committed.");
            Require(g.CreateSnapshot(2).PendingDecision is null && Prompt(g)!.Choices.All(choice => !choice.Cards.Contains(ownerCard)), "A paired reveal must neither expose the responder's hand to observers nor reveal the owner's committed card to the target.");
            Atomic(g, new AnswerPromptCommand(1, Prompt(g)!.PromptId, new ChoiceId("paired-hand-reveal." + ownerCard), g.Revision));
            var restored = Restore(g, registry); Both(g, restored, choice => choice.Cards.Contains(targetCard));
            if (!ownerSlash && targetDodge)
            {
                Require(Prompt(g)!.Choices.All(choice => !choice.Cards.Any(id => before.Players[1].Hand.Any(card => card.Id == id))), "Obtaining a hand card must retain opaque target slots.");
                Both(g, restored, choice => true);
            }
            Settle(g); Settle(restored); Equal(g, restored);
            var reveal = g.Events.Skip(beforeEvents).Select(item => item.Payload).OfType<ProgramCardsRevealedEvent>().Single();
            Require(reveal.Cards.Select(card => card.Id).SequenceEqual(new[] { ownerCard, targetCard }), "The typed reveal must publish both physical cards together.");
            var damage = ownerSlash && !targetDodge; var obtain = !ownerSlash && targetDodge;
            Require(g.CardMovements.Count(move => move.CardId == ownerCard && move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile) == (damage || obtain ? 1 : 0), "Only the matching branch may discard the owner's shown card.");
            Require(g.CreateSnapshot(0, true).Players[1].Hp == before.Players[1].Hp - (damage ? 1 : 0), "Only Slash versus non-Dodge causes real damage.");
            Require(g.CreateSnapshot(0, true).Players[0].Hand.Count == before.Players[0].Hand.Count - (damage ? 1 : 0), "The obtain branch exchanges its shown cost for exactly one target card.");
            Require(g.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:wurong"), "The reveal must consume its once-per-phase activation even when neither branch matches.");
        }
    }

    public static void FactionRequestCostDeclineRewardAndReplay()
    {
        foreach (var decline in new[] { false, true })
        {
            var registry = Registry(new Fixture("classic:qinwang", decline ? ["standard:dodge"] : ["standard:slash"], allShu: true)); var game = Create(registry); ReachPlay(game);
            Activate(game, "classic:qinwang", "request-shu-slash", [], [1]);
            Require(Prompt(game)!.PlayerSeat == 0 && Prompt(game)!.Choices.All(choice => choice.Parameters.GetValueOrDefault("response") == "faction-request-cost"), "The lord must pay a real card before asking any provider.");
            var paid = Prompt(game)!.Choices.First().Cards.Single(); var beforeHand = game.CreateSnapshot(0, true).Players[0].Hand.Count;
            Atomic(game, new AnswerPromptCommand(0, Prompt(game)!.PromptId, new ChoiceId("faction-request-cost.invalid"), game.Revision));
            var restored = Restore(game, registry); Both(game, restored, choice => choice.Cards.Contains(paid));
            Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == beforeHand - 1, "The requester's hand cost must be spent once before provider consent.");
            if (decline)
            {
                Settle(game); Settle(restored); Equal(game, restored);
                Require(game.CardMovements.All(move => move.Reason.Value != "program.faction-request.provider-reward"), "Unsuccessful requests must not reward any provider.");
            }
            else
            {
                var providerHands = game.CreateSnapshot(0, true).Players.Select(player => player.Hand.Count).ToArray();
                Settle(game); Settle(restored); Equal(game, restored);
                var accepted = game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Last(item => item.Action.ActorSeat == 0 && item.Action.ProviderSeat != 0).Action;
                var provider = accepted.ProviderSeat; var physical = accepted.PhysicalCards.Single().CardId;
                Require(game.CreateSnapshot(provider, true).Players[provider].Hand.Count == providerHands[provider], "A provider must pay one real Slash and receive one reward card.");
                Require(game.CardMovements.Count(move => move.CardId == physical && move.From == CardLocation.Hand(provider) && move.To == CardLocation.Processing) == 1, "Faction assistance must pay the actual provider's card once.");
                Require(game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>().Any(item => item.Action.ActorSeat == 0 && item.Action.ProviderSeat == provider), "The typed use must retain its lord actor and physical provider separately.");
            }
        }
    }

    private static ContentRegistry Registry(Fixture fixture) => ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), fixture);
    private static GameEngine Create(ContentRegistry registry, int seed = 31, Role humanRole = Role.Lord)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = humanRole, PlayerCount = 4,
            ModeId = "identity:classic-fame2015-shu", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 40 }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture must start.");
        Reach(game, prompt => prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.SelectGeneral);
        Require(game.Submit(new SelectGeneralCommand(0, "fixture:fame2015-shu-owner", game.Revision, Prompt(game)!.PromptId)).Accepted, "Fixture owner must be selected."); return game;
    }
    private static void Activate(GameEngine game, string skill, string activation, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
        Require(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([new UseProgramSkillCommand(0, skill, activation, cards, targets, game.Revision, Prompt(game)!.PromptId)])).Single()).Accepted, "A published generic activation must survive command JSON and be accepted.");
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void ReachPlay(GameEngine game) => Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    { for (var step = 0; step < 256; step++) { if (Prompt(game) is { } prompt && condition(prompt)) return; Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Fixture advancement must be accepted."); } throw new InvalidOperationException($"Fixture failed to reach its required boundary. pending={Prompt(game)?.Kind}/{Prompt(game)?.PlayerSeat}, duel={System.Text.Json.JsonSerializer.Serialize(game.Events.Select(item => item.Payload).OfType<DuelResponseEvent>().ToArray())}"); }
    private static void Settle(GameEngine game)
    {
        for (var step = 0; step < 256 && game.ResolutionStack.Count > 0; step++)
            if (Prompt(game) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "pass");
            else if (Prompt(game) is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "decline" || choice.Parameters.GetValueOrDefault("response") == "faction-slash-decline");
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "A physical skill resolution must advance.");
        Require(game.ResolutionStack.Count == 0, "The physical resolution must leave no paused frame.");
        if (game.CreateSnapshot(0, true) is { CurrentSeat: 0, Phase: TurnPhase.Play } && Prompt(game) is null) ReachPlay(game);
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Prompt(game)!; var choice = prompt.Choices.First(predicate); Require(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)])).Single()).Accepted, "A published physical choice must survive command JSON."); }
    private static void Both(GameEngine a, GameEngine b, Func<PromptChoice, bool> predicate) { var id = Prompt(a)!.Choices.First(predicate).Id; Answer(a, choice => choice.Id == id); Answer(b, choice => choice.Id == id); }
    private static GameEngine Restore(GameEngine game, ContentRegistry registry) => GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
    private static void Atomic(GameEngine game, GameCommand command)
    { var before = GameCheckpointJson.Serialize(game.CreateCheckpoint()); var moves = game.CardMovements.Count; Require(!game.Submit(command).Accepted && before == GameCheckpointJson.Serialize(game.CreateCheckpoint()) && moves == game.CardMovements.Count, "Illegal physical input must reject atomically without moving cards or consuming a phase allowance."); }
    private static void Equal(GameEngine a, GameEngine b) => Require(SnapshotJson.Serialize(a.CreateSnapshot(0, true)) == SnapshotJson.Serialize(b.CreateSnapshot(0, true)) && a.CardMovements.SequenceEqual(b.CardMovements) &&
        a.Events.Select(item => System.Text.Json.JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(b.Events.Select(item => System.Text.Json.JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))), "Checkpoint replay must reproduce typed events, snapshots and the physical ledger.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(string skill, IReadOnlyList<string> cards, bool allShu = false, bool boundaryDriver = false, int targetHp = 10, bool actorReplacement = false, bool responseDraw = false, bool factionDefense = false, int ownerHp = 10, bool responseTarget = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-fame2015-shu", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (boundaryDriver)
            {
                var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:fame2015-health","revision":1,"activations":[
                  {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                  {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]}
                ]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:fame2015-health":{"name":"体力边界","description":"真实体力变化"}}}""");
                builder.AddSkill(new("fixture:fame2015-health", "体力边界", "真实体力变化") { Program = catalog.Programs["fixture:fame2015-health"] });
            }
            if (responseDraw)
            {
                var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:fame2015-response-draw","revision":1,
                 "triggers":[{"id":"draw-after-dodge","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["dodge"],
                   "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:fame2015-response-draw":{"name":"响应摸牌","description":"响应后摸真实牌堆顶"}}}""");
                builder.AddSkill(new("fixture:fame2015-response-draw", "响应摸牌", "响应后摸真实牌堆顶") { Program = catalog.Programs["fixture:fame2015-response-draw"] });
            }
            if (responseTarget)
            {
                var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:fame2015-response-target","revision":1,
                 "triggers":[{"id":"also-defend-this-slash","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],
                  "condition":{"kind":"compare","left":{"kind":"cardUseDesignatedTargetCount"},"operator":"equal","right":{"kind":"integerConstant","value":1}},
                  "optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"addCurrentCardUseTarget","target":"selectedTarget"}]},
                 {"id":"slash-after-arrow","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["arrowBarrage"],"optional":false,
                  "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
                   {"op":"useVirtualSlash","target":"selectedTarget"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:fame2015-response-target":{"name":"追加响应目标","description":"同一张杀先护驾提供再本人防御"}}}""");
                builder.AddSkill(new("fixture:fame2015-response-target", "追加响应目标", "同一张杀先护驾提供再本人防御")
                {
                    Program = catalog.Programs["fixture:fame2015-response-target"],
                    SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = 100 }
                });
            }
            builder.AddGeneral(new("fixture:fame2015-shu-owner", "测试蜀将", "supporter", skill, factionDefense ? "wei" : "shu", BaseHp: ownerHp, AdditionalSkillIds: boundaryDriver ? ["fixture:fame2015-health"] : actorReplacement ? ["classic:zenhui"] : responseDraw ? ["fixture:fame2015-response-draw"] : [], Gender: skill == "classic:yanyu" ? GeneralGender.Female : GeneralGender.Male));
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:fame2015-shu-target-{index}", $"目标{index}", "supporter", factionDefense ? "classic:hujia" : "standard:none", allShu ? "shu" : "wei", BaseHp: targetHp, AdditionalSkillIds: responseTarget ? ["fixture:fame2015-response-target"] : [], Gender: GeneralGender.Male));
            builder.AddDeck(new("fixture:fame2015-shu-deck", "测试牌堆", 8, 0, []) { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(cards[index % cards.Count], Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-fame2015-shu", "蜀将机制测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:fame2015-shu-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:fame2015-shu-owner", "fixture:fame2015-shu-target-1", "fixture:fame2015-shu-target-2", "fixture:fame2015-shu-target-3"]));
        }
    }
}
