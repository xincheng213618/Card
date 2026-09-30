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
        for (var seed = 1; seed <= 64 && game is null; seed++)
        {
            var candidate = Create(registry, seed); ReachPlay(candidate);
            if (candidate.GetHumanLegalActions().Any(item => item.Kind == LegalActionKind.Slash && item.ConversionSource is null) &&
                candidate.GetHumanLegalActions().Any(item => item.ConversionSource?.SkillId == "classic:huomo")) game = candidate;
        }
        Require(game is not null, "The fixture must expose native and converted basic-card sources together.");
        var native = game!.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Slash && item.ConversionSource is null);
        Require(game.Submit(new PlayCardCommand(0, native.CardId!.Value, native.TargetSeats, game.Revision, Prompt(game)!.PromptId)).Accepted, "A native Slash must enter the ordinary use ledger.");
        Settle(game);
        Require(game.GetHumanLegalActions().All(item => item.ConversionSource?.SkillId != "classic:huomo" || item.PlayedCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash)), "A native Slash must block every converted Slash name in this turn.");
        Equal(game, Restore(game, registry));

        registry = Registry(new Fixture("classic:huomo", ["standard:slash", "standard:crossbow", "standard:duel"], targetHp: 1));
        game = null;
        for (var seed = 1; seed <= 64 && game is null; seed++)
        {
            var candidate = Create(registry, seed); ReachPlay(candidate); var snapshot = candidate.CreateSnapshot(0, true);
            if (snapshot.Players[1].Role == Role.Rebel && snapshot.Players[0].Hand.Any(card => card.Kind == CardKind.Slash) && snapshot.Players[1].Hand.Any(card => card.Kind == CardKind.Slash) &&
                candidate.GetHumanLegalActions().Any(item => item.Kind == LegalActionKind.Duel && item.TargetSeat == 1) &&
                candidate.GetHumanLegalActions().Any(item => item.ConversionSource?.SkillId == "classic:huomo")) game = candidate;
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
    private static GameEngine Create(ContentRegistry registry, int seed = 31)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = "identity:classic-fame2015-shu", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 40 }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture must start.");
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
    private sealed class Fixture(string skill, IReadOnlyList<string> cards, bool allShu = false, bool boundaryDriver = false, int targetHp = 10, bool actorReplacement = false) : IGameContentPackage
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
            builder.AddGeneral(new("fixture:fame2015-shu-owner", "测试蜀将", "supporter", skill, "shu", BaseHp: 10, AdditionalSkillIds: boundaryDriver ? ["fixture:fame2015-health"] : actorReplacement ? ["classic:zenhui"] : [], Gender: skill == "classic:yanyu" ? GeneralGender.Female : GeneralGender.Male));
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:fame2015-shu-target-{index}", $"目标{index}", "supporter", "standard:none", allShu ? "shu" : "wei", BaseHp: targetHp, Gender: GeneralGender.Male));
            builder.AddDeck(new("fixture:fame2015-shu-deck", "测试牌堆", 8, 0, []) { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(cards[index % cards.Count], Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-fame2015-shu", "蜀将机制测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:fame2015-shu-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:fame2015-shu-owner", "fixture:fame2015-shu-target-1", "fixture:fame2015-shu-target-2", "fixture:fame2015-shu-target-3"]));
        }
    }
}
