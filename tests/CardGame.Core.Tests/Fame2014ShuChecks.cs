using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2014ShuChecks
{
    public static void AiCurrentEnhancementAndCompletedGiftReplay()
    {
        var registry = Registry(new Fixture("standard:none", CardKind.Slash, Suit.Heart, aiOwners: true));
        var game = Create(registry); Reach(game, p => p.Kind == DecisionKind.PlayCard);
        Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId)).Accepted, "Human turn must end through its published command.");
        Reach(game, p => p.Kind == DecisionKind.ProgramTrigger && p.PlayerSeat != 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-card-enhancement"));
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        for (var step = 0; step < 512; step++)
        {
            if (Prompt(game) is { PlayerSeat: 0 } prompt)
            {
                if (prompt.Kind == DecisionKind.PlayCard) break;
                var choice = prompt.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") == "pass" || c.Parameters.GetValueOrDefault("gift-option") == "decline");
                Require(choice is not null, "Human response must expose its ordinary decline."); Both(game, restored, c => c.Id == choice!.Id);
            }
            else
            {
                Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted && restored.Submit(new AdvanceOneStepCommand(restored.Revision)).Accepted,
                    "AI must resolve only its published legal choices through normal advancement.");
            }
            if (game.ResolutionStack.Count == 0 && game.Events.Any(e => e.Payload is CompletedCardGiftedEvent gift && gift.OwnerSeat != 0))
            {
                Require(game.Events.Any(e => e.Payload is CurrentCardEnhancedEvent enhancement && enhancement.OwnerSeat != 0), "An AI owner's actual use must apply current-card enhancements.");
                Equal(game, restored); return;
            }
        }
        throw new InvalidOperationException("The AI must complete a real enhanced Slash and physical gift without an unsupported paused instruction.");
    }

    public static void BenxiBorrowedSwordCompoundTargetsAndReplay()
    {
        var registry = Registry(new Fixture("classic:benxi", CardKind.BorrowedSword, Suit.Spade, equipmentDriver: true));
        for (var seed = 1; seed <= 64; seed++)
        {
            var game = Create(registry, seed, mode: "identity:classic-fame2014-shu"); Reach(game, p => p.Kind == DecisionKind.PlayCard);
            var weapons = game.CreateSnapshot(0, true).Players[0].Hand.Where(c => c.Kind == CardKind.QinggangSword).Take(2).ToArray();
            if (weapons.Length != 2 || game.CreateSnapshot(0, true).Players[0].Hand.All(c => c.Kind != CardKind.BorrowedSword)) continue;
            for (var index = 0; index < 2; index++)
            {
                Require(game.Submit(new UseProgramSkillCommand(0, "fixture:shu-equipment-driver", "equip", [], [index + 1], game.Revision, Prompt(game)!.PromptId)).Accepted, "A real holder weapon must be equipped.");
                Answer(game, Prompt(game)!.Choices.Single(c => c.Cards.Contains(weapons[index].Id))); Reach(game, p => p.Kind == DecisionKind.PlayCard);
            }
            var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.BorrowedSword && a.TargetSeats.SequenceEqual(new[] { 1, 0 }));
            Play(game, action); Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-card-enhancement"));
            Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("enhancement-option") == "ExtraTarget"));
            AssertAtomicRejection(game);
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Both(game, restored, c => c.Targets.SequenceEqual(new[] { 2, 3 }));
            var use = game.ResolutionStack.OfType<CardUseFrame>().Last();
            Require(use.TargetSeats.SequenceEqual(new[] { 1, 0, 2, 3 }) && use.Action!.EffectiveDesignatedTargetSeats.SequenceEqual(new[] { 1, 2 }), "Borrowed Sword must preserve separate holder/victim pairs and exactly two designated holders.");
            Both(game, restored, c => c.Parameters.GetValueOrDefault("enhancement-option") == "finish"); Settle(game); Settle(restored);
            Require(game.Events.Count(e => e.Payload is BorrowedSwordResolvedEvent b && b.SourceSeat == 0) == 2, "Both real Borrowed Sword holder/victim pairs must settle.");
            Require(game.CardMovements.Count(m => m.CardId == action.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(m => m.CardId == action.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1, "Two Borrowed Sword pairs must consume one physical trick exactly once.");
            Equal(game, restored); return;
        }
        throw new InvalidOperationException("No bounded two-weapon Borrowed Sword fixture was available.");
    }

    public static void BenxiUncancelableTrickPaysPhysicalNullification()
    {
        var registry = Registry(new Fixture("classic:benxi", CardKind.Duel, Suit.Spade, mixedNullification: true));
        for (var seed = 1; seed <= 32; seed++)
        {
            var game = Create(registry, seed); Reach(game, p => p.Kind == DecisionKind.PlayCard);
            if (game.CreateSnapshot(0, true).Players[0].Hand.All(card => card.Kind != CardKind.Nullification)) continue;
            var action = game.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual(new[] { 1 }));
            if (action is null) continue;
            Play(game, action); Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-card-enhancement"));
            Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("enhancement-option") == "Uncancelable"));
            Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("enhancement-option") == "finish"));
            var prompt = Reach(game, p => p.Kind == DecisionKind.Nullification && p.PlayerSeat == 0 && p.Choices.Any(c => c.Cards.Count == 1));
            var choice = prompt.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "nullification"); var paid = choice.Cards.Single();
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Both(game, restored, c => c.Id == choice.Id); Settle(game); Settle(restored);
            Require(game.CardMovements.Any(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) &&
                game.CardMovements.Any(m => m.CardId == paid && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile),
                "A real Nullification must still be paid and finish in discard against an uncancelable trick.");
            Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1),
                "The actual Duel must still damage its target after real Nullification responses.");
            Equal(game, restored); return;
        }
        throw new InvalidOperationException("No bounded physical Nullification and Duel payment fixture was found.");
    }

    public static void BenxiIgnoreArmorBypassesPhysicalSilverLionForDuel()
    {
        foreach (var ignoreArmor in new[] { false, true })
        {
            var registry = Registry(new Fixture("classic:benxi", CardKind.Duel, Suit.Spade, equipmentDriver: true));
            GameEngine? game = null;
            for (var seed = 1; seed <= 32 && game is null; seed++)
            {
                var candidate = Create(registry, seed, mode: "identity:classic-fame2014-shu"); Reach(candidate, p => p.Kind == DecisionKind.PlayCard);
                if (candidate.CreateSnapshot(0, true).Players[0].Hand.Any(card => card.Kind == CardKind.SilverLion) &&
                    candidate.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Duel)) game = candidate;
            }
            Require(game is not null, "A bounded Duel and real Silver Lion fixture must be available.");
            var before = game!.CreateSnapshot(0, true); var armor = before.Players[0].Hand.First(card => card.Kind == CardKind.SilverLion).Id;
            Require(game.Submit(new UseProgramSkillCommand(0, "fixture:shu-equipment-driver", "equip", [], [1], game.Revision, Prompt(game)!.PromptId)).Accepted,
                "The actual armor equip operation must be accepted.");
            Answer(game, Prompt(game)!.Choices.Single(choice => choice.Cards.Contains(armor)));
            Reach(game, p => p.Kind == DecisionKind.PlayCard);
            Require(game.CreateSnapshot(0, true).Players[1].Equipment.Any(card => card.Id == armor && card.Kind == CardKind.SilverLion),
                "The physical Silver Lion must actually be equipped on the Duel target.");
            Require(game.Submit(new UseProgramSkillCommand(0, "fixture:shu-equipment-driver", "boost", [], [], game.Revision, Prompt(game)!.PromptId)).Accepted,
                "The generic two-damage Duel grant must be accepted.");
            Reach(game, p => p.Kind == DecisionKind.PlayCard);
            var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual(new[] { 1 }));
            Play(game, action); Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-card-enhancement"));
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            if (ignoreArmor) Both(game, restored, c => c.Parameters.GetValueOrDefault("enhancement-option") == "IgnoreArmor");
            Both(game, restored, c => c.Parameters.GetValueOrDefault("enhancement-option") == "finish");
            Settle(game); Settle(restored);
            var damage = game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Single(e => e.SourceSeat == 0 && e.TargetSeat == 1);
            Require(damage.Amount == (ignoreArmor ? 2 : 1), "A real Silver Lion must cap the ordinary Duel at one damage and be bypassed by current-card IgnoreArmor.");
            Require(game.CreateSnapshot(0, true).Players[1].Equipment.Any(card => card.Id == armor), "Ignoring armor must retain the target's physical equipment.");
            Equal(game, restored);
        }
    }

    public static void BenxiUncancelableSlashConsumesDodgeAndDealsDamage()
    {
        var registry = Registry(new Fixture("classic:benxi", CardKind.Slash, Suit.Spade, mixedDodge: true));
        for (var seed = 1; seed <= 64; seed++)
        {
            var game = Create(registry, seed); Reach(game, p => p.Kind == DecisionKind.PlayCard);
            var action = game.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual(new[] { 1 }));
            if (action is null || game.CreateSnapshot(0, true).Players[1].Hand.All(c => c.Kind != CardKind.Dodge)) continue;
            Play(game, action); Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-card-enhancement"));
            Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("enhancement-option") == "Uncancelable"));
            Answer(game, Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("enhancement-option") == "IgnoreArmor"));
            Settle(game);
            var response = game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Select(e => e.Action)
                .FirstOrDefault(a => a.Type == CardActionType.Response && a.EffectiveKind == CardKind.Dodge && a.ResponderSeat == 1);
            Require(response is not null && response.PhysicalCards.Count > 0 && response.PhysicalCards.All(c =>
                game.CardMovements.Any(m => m.CardId == c.CardId && m.From == CardLocation.Hand(1))),
                "Uncancelable Slash must allow and consume the target's real Dodge response.");
            Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1),
                "An actual Dodge response must not cancel the selected uncancelable Slash.");
            return;
        }
        throw new InvalidOperationException("No bounded real Dodge fixture was available for an uncancelable Slash.");
    }

    public static void BenxiCurrentEnhancementsAndReplay()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.Duel, CardKind.DrawTwo, CardKind.IronChain })
        {
            var registry = Registry(new Fixture("classic:benxi", kind, Suit.Spade));
            var game = Create(registry); Reach(game, p => p.Kind == DecisionKind.PlayCard);
            Require(game.GetCombatDistance(0, 2) == 2, "Distance must begin with ordinary geometry.");
            var card = game.CreateSnapshot(0, true).Players[0].Hand.First(c => c.Kind == kind);
            var before = game.CreateSnapshot(0, true);
            var action = game.GetHumanLegalActions().First(a => a.CardId == card.Id && a.Kind != LegalActionKind.Recast && a.TargetSeats.Count <= 1);
            Play(game, action);
            PendingDecision prompt;
            try { prompt = Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "current-card-enhancement")); }
            catch (InvalidOperationException error) { throw new InvalidOperationException($"Enhancement boundary failed for {kind}: {error.Message}", error); }
            Require(game.GetCombatDistance(0, 2) == 1, "The same use must reduce distance before enhancement eligibility is tested.");
            Require(prompt.Choices.Count(c => c.Parameters.GetValueOrDefault("enhancement-option") != "finish") == 4,
                "Current ordinary tricks and Slashes must offer all four enhancement options.");
            AssertAtomicRejection(game);
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Both(game, restored, p => p.Parameters.GetValueOrDefault("enhancement-option") == "ExtraTarget");
            Both(game, restored, p => p.Targets.Count == 1);
            Require(Prompt(game)!.Choices.All(c => c.Parameters.GetValueOrDefault("enhancement-option") != "ExtraTarget"), "Selections must not repeat an option.");
            Both(game, restored, p => p.Parameters.GetValueOrDefault("enhancement-option") == "DrawAfterDamage");
            Settle(game); Settle(restored);
            Require(game.Events.Select(e => e.Payload).OfType<CurrentCardEnhancedEvent>().Count(e => e.OwnerSeat == 0) == 2,
                "A card must apply exactly two different selected enhancements.");
            Require(game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count(e => e.CardId == card.Id) == 1,
                "The enhanced card must be declared once, even when it gains a second target.");
            Require(game.CardMovements.Count(m => m.CardId == card.Id && m.To == CardLocation.Processing) == 1,
                "Adding a target must pay the actual card only once.");
            if (kind is CardKind.Slash or CardKind.Duel)
            {
                Require(game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Where(e => e.SourceSeat == 0).Select(e => e.TargetSeat).Distinct().Count() == 2,
                    "An enhanced damaging card must resolve both physical targets.");
                Require(game.CreateSnapshot(0, true).Players[0].HandCount == before.Players[0].HandCount - 1 + 2,
                    "Each of the card's two actual damage instances must draw exactly one physical card.");
            }
            else if (kind == CardKind.DrawTwo)
            {
                var target = game.Events.Select(e => e.Payload).OfType<CurrentCardEnhancedEvent>().Single(e => e.ExtraTargetSeat is not null).ExtraTargetSeat!.Value;
                var after = game.CreateSnapshot(0, true);
                Require(after.Players[0].HandCount == before.Players[0].HandCount + 1 && after.Players[target].HandCount == before.Players[target].HandCount + 2,
                    "The same physical Draw Two must draw two cards for its implicit self target and its actual additional target.");
            }
            else if (kind == CardKind.IronChain)
                Require(game.CreateSnapshot(0, true).Players.Count(p => p.IsChained) == 2 && game.CreateSnapshot(0, true).Players[0].HandCount == before.Players[0].HandCount - 1,
                    "The enhanced Iron Chain must chain both targets without a damage-only draw.");
            Equal(game, restored);
        }
    }

    public static void ZhongyongPhysicalGiftsAndRedSlashReplay()
    {
        foreach (var suit in new[] { Suit.Spade, Suit.Heart })
        {
            var registry = Registry(new Fixture("classic:zhongyong", CardKind.Slash, suit));
            var game = Create(registry); Reach(game, p => p.Kind == DecisionKind.PlayCard);
            var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual(new[] { 1 }));
            var physical = action.CardId!.Value; Play(game, action);
            var gift = Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "completed-card-gift"));
            Require(gift.Choices.Where(c => c.Parameters.GetValueOrDefault("gift-option") == "give").All(c => !c.Targets.Contains(0)), "A completed gift cannot select its owner.");
            Require(gift.Choices.Any(c => c.Targets.Contains(1) && c.Cards.Contains(physical)), "Current official wording permits gifting to the original target.");
            AssertAtomicRejection(game);
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Both(game, restored, c => c.Targets.Contains(1) && c.Cards.Contains(physical));
            Require(game.CreateSnapshot(0, true).Players[1].Hand.Any(c => c.Id == physical), "The settled Slash must actually enter the recipient's hand.");
            if (suit == Suit.Heart)
            {
                Require(Prompt(game) is { PlayerSeat: 1 } && Prompt(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("gift-option") == "slash"),
                    $"The recipient must choose a real paid Slash after a red gift. Pending={Prompt(game)?.PlayerSeat}/{Prompt(game)?.Kind}; gift={System.Text.Json.JsonSerializer.Serialize(game.Events.Select(e => e.Payload).OfType<CompletedCardGiftedEvent>().Last())}; handSuit={game.CreateSnapshot(0, true).Players[1].Hand.Single(c => c.Id == physical).Suit}");
                Require(Prompt(game)!.Choices.SelectMany(c => c.Targets).All(seat => seat != 0 && seat != 1),
                    "The owner's range excludes the owner, and a recipient cannot Slash itself.");
                var choice = Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("gift-option") == "slash" && c.Cards.Contains(physical) && c.Targets.Contains(3));
                Both(game, restored, c => c.Id == choice.Id);
                Settle(game); Settle(restored);
                var responseUse = game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Select(e => e.Action)
                    .Single(a => a.Type == CardActionType.Use && a.ActorSeat == 1 && a.PhysicalCards.Any(c => c.CardId == physical));
                Require(responseUse.ProviderSeat == 1 && responseUse.EffectiveDesignatedTargetSeats.SequenceEqual(new[] { 3 }),
                    "The recipient must own and pay the Slash while using the gift owner's range.");
                Require(game.GetCombatDistance(1, 3) == 2 && game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 1 && e.TargetSeat == 3),
                    "A real recipient Slash must reach a target outside its own attack range.");
                Require(game.CardMovements.Count(m => m.CardId == physical && m.To == CardLocation.Processing) == 2,
                    "Gift reuse must consume the same physical Slash once for each actual use.");
            }
            else Settle(game);
            Settle(restored); Equal(game, restored);
            Require(game.Events.Select(e => e.Payload).OfType<CompletedCardGiftedEvent>().Count() == 1, "The completed Slash gift must occur exactly once.");
        }
    }

    private static ContentRegistry Registry(IGameContentPackage fixture) => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), fixture);
    public static void ZhongyongRespondedDodgePhysicalProvenance()
    {
        var registry = Registry(new Fixture("classic:zhongyong", CardKind.Slash, Suit.Spade, mixedDodge: true));
        GameEngine? game = null; PromptChoice? gift = null;
        for (var seed = 1; seed <= 64 && gift is null; seed++)
        {
            var candidate = Create(registry, seed); Reach(candidate, p => p.Kind == DecisionKind.PlayCard);
            var action = candidate.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual(new[] { 1 }));
            if (action is null) continue;
            Play(candidate, action);
            var prompt = Reach(candidate, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "completed-card-gift"), forceDodge: true);
            var responses = candidate.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Select(e => e.Action)
                .Where(a => a.Type == CardActionType.Response && a.EffectiveKind == CardKind.Dodge && a.ResponderSeat == 1).ToArray();
            gift = prompt.Choices.FirstOrDefault(c => c.Targets.Contains(2) && responses.Any(a => a.PhysicalCards.Select(cost => cost.CardId).SequenceEqual(c.Cards)));
            if (gift is not null) game = candidate;
        }
        Require(game is not null && gift is not null, "A bounded real target Dodge response must provide a transferable physical payment.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game!.CreateCheckpoint())), registry);
        var physical = gift!.Cards.Single(); Both(game, restored, c => c.Id == gift.Id); Settle(game); Settle(restored);
        Require(game.CreateSnapshot(0, true).Players[2].Hand.Any(c => c.Id == physical && c.Kind == CardKind.Dodge),
            "The actual target's consumed Dodge must enter the chosen recipient's hand.");
        Require(game.CardMovements.Any(m => m.CardId == physical && m.From == CardLocation.Hand(1)) &&
            game.CardMovements.Any(m => m.CardId == physical && m.To == CardLocation.Hand(2)), "The response ledger must preserve the responding target's physical card movement.");
        Equal(game, restored);
    }

    public static void ZhongyongFactionActorAndProviderPhysicalReplay()
    {
        var registry = Registry(new Fixture("classic:zhongyong", CardKind.Slash, Suit.Heart, factionRecipients: true));
        var game = Create(registry, role: Role.Rebel); Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var lord = game.CreateSnapshot(0, true).Players.Single(p => p.Role == Role.Lord).Seat;
        var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash);
        Play(game, action); Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "completed-card-gift"));
        Answer(game, Prompt(game)!.Choices.First(c => c.Targets.Contains(lord) && c.Cards.Contains(action.CardId!.Value)));
        var request = Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("gift-option") == "faction");
        Answer(game, request);
        Reach(game, p => p.Kind == DecisionKind.RespondSlash && p.PlayerSeat == 0);
        var physical = Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash").Cards.Single();
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Both(game, restored, c => c.Cards.Contains(physical) && c.Parameters.GetValueOrDefault("response") == "faction-slash-slash");
        Settle(game); Settle(restored);
        Require(game.Events.Select(e => e.Payload).OfType<CardActionAcceptedEvent>().Select(e => e.Action).Any(a =>
            a.Type == CardActionType.Use && a.ActorSeat == lord && a.ProviderSeat == 0 && a.PhysicalCards.Any(c => c.CardId == physical)),
            "The gift recipient must remain the actual Slash actor while a different faction provider pays its real card.");
        Require(game.CardMovements.Any(m => m.CardId == physical && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing),
            "Faction follow-up must consume the physical provider's Slash.");
        Equal(game, restored);
    }

    private static GameEngine Create(ContentRegistry registry, int seed = 31, Role role = Role.Lord, string mode = "fixture:fame2014-shu")
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = role, PlayerCount = 4,
            ModeId = mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 80 }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture did not start.");
        Require(game.Submit(new SelectGeneralCommand(0, "fixture:shu-owner", game.Revision, Prompt(game)!.PromptId)).Accepted, "Fixture general selection failed.");
        return game;
    }
    private static void Play(GameEngine game, LegalAction action) => Require(game.Submit(new PlayCardCommand(0, action.CardId!.Value,
        action.TargetSeats, game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource }).Accepted, "Real card use failed.");
    private static PendingDecision Reach(GameEngine game, Func<PendingDecision, bool> condition, bool forceDodge = false)
    {
        for (var step = 0; step < 256; step++)
        {
            if (Prompt(game) is { } p && condition(p)) return p;
            if (forceDodge && Prompt(game) is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 } dodge && dodge.Choices.Any(c => c.Cards.Count > 0))
            { Answer(game, dodge.Choices.First(c => c.Cards.Count > 0)); continue; }
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Fixture failed to advance.");
        }
        throw new InvalidOperationException("Fixture did not reach its required boundary.");
    }
    private static void Settle(GameEngine game)
    {
        for (var step = 0; step < 256 && game.ResolutionStack.Count > 0; step++)
            if (Prompt(game) is { Kind: DecisionKind.ProgramTrigger } p)
                Answer(game, p.Choices.First(c => c.Parameters.GetValueOrDefault("gift-option") == "decline" || c.Parameters.GetValueOrDefault("enhancement-option") == "finish"));
            else if (Prompt(game) is { Kind: DecisionKind.RespondDodge, PlayerSeat: 0 } dodge && dodge.Choices.Any(c => c.Cards.Count > 0))
                Answer(game, dodge.Choices.First(c => c.Cards.Count > 0));
            else if (Prompt(game) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 } nullification)
                Answer(game, nullification.Choices.First(c => c.Parameters.GetValueOrDefault("response") == "pass"));
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Physical resolution failed to settle.");
        Require(game.ResolutionStack.Count == 0, "Physical card use retained an unresolved frame.");
    }
    private static void Both(GameEngine a, GameEngine b, Func<PromptChoice, bool> condition)
    { var choice = Prompt(a)!.Choices.First(condition); Answer(a, choice); Answer(b, Prompt(b)!.Choices.Single(c => c.Id == choice.Id)); }
    private static void Answer(GameEngine game, PromptChoice choice)
    { var command = new AnswerPromptCommand(Prompt(game)!.PlayerSeat, Prompt(game)!.PromptId, choice.Id, game.Revision);
      Require(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()).Accepted, "A published choice failed its command JSON round trip."); }
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4)
        .Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void AssertAtomicRejection(GameEngine game)
    {
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint()); var moves = game.CardMovements.Count;
        Require(!game.Submit(new AnswerPromptCommand(Prompt(game)!.PlayerSeat, Prompt(game)!.PromptId, new ChoiceId("illegal.fixture"), game.Revision)).Accepted,
            "A forged choice must be rejected.");
        Require(GameCheckpointJson.Serialize(game.CreateCheckpoint()) == before && game.CardMovements.Count == moves, "Rejected choices must not consume state or move physical cards.");
    }
    private static void Equal(GameEngine a, GameEngine b) => Require(SnapshotJson.Serialize(a.CreateSnapshot(0, true)) == SnapshotJson.Serialize(b.CreateSnapshot(0, true)) &&
        a.CardMovements.SequenceEqual(b.CardMovements) &&
        a.Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(
            b.Events.Select(e => System.Text.Json.JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))),
        "Checkpoint replay must preserve typed events, physical movements and final snapshots.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(string skill, CardKind kind, Suit suit, bool mixedDodge = false, bool factionRecipients = false,
        bool mixedNullification = false, bool equipmentDriver = false, bool aiOwners = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-fame2014-shu", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (equipmentDriver)
            {
                var driver = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:shu-equipment-driver","revision":1,"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[
                {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,
                "cardCategories":["equipment"],"destination":"selectedTargetEquipment","targetRef":{"kind":"selectedTarget"}}]},
                {"id":"boost","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":1,"effects":[
                {"op":"grantTurnCardDamageModifier","target":"owner","amount":1,"cardKinds":["duel"],"expires":"currentTurnEnd","sourceScope":"damageSource"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:shu-equipment-driver":{"name":"装备测试","description":"真实装备和伤害修改"}}}""");
                builder.AddSkill(new("fixture:shu-equipment-driver", "装备测试", "真实装备和伤害修改") { Program = driver.Programs["fixture:shu-equipment-driver"] });
            }
            builder.AddGeneral(new("fixture:shu-owner", "测试蜀将", "supporter", skill, "shu", BaseHp: 10,
                AdditionalSkillIds: equipmentDriver ? ["fixture:shu-equipment-driver"] : []));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:shu-target-{seat}", $"目标{seat}", "supporter", factionRecipients ? "classic:jijiang" : aiOwners ? "classic:benxi" : "standard:none", factionRecipients ? "shu" : "wei", BaseHp: 10, AdditionalSkillIds: aiOwners ? ["classic:zhongyong"] : []));
            var cardId = kind switch { CardKind.Slash => "standard:slash", CardKind.Duel => "standard:duel", CardKind.DrawTwo => "standard:draw_two", CardKind.BorrowedSword => "classic:borrowed-sword", _ => "standard:iron_chain" };
            builder.AddDeck(new("fixture:shu-deck", "测试牌堆", 8, 0, [])
            { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(index % 2 == 0 ?
                mixedDodge ? "standard:dodge" : mixedNullification ? "standard:nullification" : equipmentDriver ? kind == CardKind.BorrowedSword ? "standard:qinggang_sword" : "classic:silver-lion" : cardId : cardId,
                suit, index % 13 + 1)).ToArray() });
            foreach (var modeId in new[] { "fixture:fame2014-shu", "identity:classic-fame2014-shu" }) builder.AddMode(new(modeId, "蜀将测试", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:shu-deck", GeneralCandidateCount: 4,
            GeneralPoolIds: ["fixture:shu-owner", "fixture:shu-target-1", "fixture:shu-target-2", "fixture:shu-target-3"]));
        }
    }
}


