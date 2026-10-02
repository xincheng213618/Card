using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryMaChaoChecks
{
    private static ContentRegistry Classic => ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());

    public static void SuitsPaymentDeclinePrivacyReplay()
    {
        foreach (var suit in new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond })
        foreach (var pay in new[] { false, true })
        {
            var (game, registry) = Start(suit);
            Slash(game, 1);
            Activate(game);
            Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
            var prompt = Prompt(game)!;
            var draft = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SlashSuitDiscard is not null).SlashSuitDiscard!;
            var skill = registry.GetSkill("boundary:tieqi");
            Require(skill.Name == "铁骑", "The registered skill definition exposes its actual Tieqi display name.");
            var judgmentLog = game.Log.Single(entry => entry.Type == "Judgment" && entry.ActorSeat == 0 && entry.TargetSeat == 0);
            Require(judgmentLog.Message.Contains($"发动【{skill.Name}】判定：最终为【", StringComparison.Ordinal) &&
                !judgmentLog.Message.Contains("【boundary:tieqi】", StringComparison.Ordinal),
                "The owning real Tieqi judgment log uses the content skill display name rather than its implementation reason.");
            Require(prompt.PlayerSeat == 1 && prompt.IsPrivate && draft.FinalSuit == suit, "Each real final suit belongs to the target's private HE choice.");
            Require(Enumerable.Range(0, 4).Where(seat => seat != 1).All(seat => game.CreateSnapshot(seat).PendingDecision?.Choices.Count is null or 0), "Other viewers cannot inspect target cost cards.");
            Require(!game.CreateSnapshot(1).Players[1].Skills!.Any(skill => skill.ContentId == "classic:qingguo") && game.CreateSnapshot(1).Players[1].Skills!.Any(skill => skill.ContentId == "fixture:mc-locked"), "Same actual turn suppression disables nonlocked skill and keeps locked skill.");
            Replay(game, registry);
            RejectChoice(game, new("slash-suit-discard.invalid"));
            Answer(game, c => pay ? c.Cards.Count == 1 : c.Cards.Count == 0);
            Settle(game);
            var resolved = game.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Single();
            Require(resolved.JudgmentSuit == suit && resolved.PaymentCompleted == pay, "Only a real matching-suit payment clears exact target cancellation restriction.");
            Require(game.Events.Select(e => e.Payload).OfType<JudgmentRequestedEvent>().Count(e => e.Reason == "boundary:tieqi") == 1, "Final target judges exactly once.");
            if (pay)
                Require(game.CardMovements.Count(m => m.CardId == resolved.PaidCardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile) == 1, "True HE payment commits once.");
            Require(game.CreateSnapshot(1).Players[1].Hp == (pay ? 4 : 3), "Paid Dodge cancels while a refused payment retains damage after real Dodge.");
            Replay(game, registry);
        }
    }

    public static void TieqiPaoxiaoTrueDodgeAndNextUseReplay()
    {
        var (declined, declinedRegistry) = Start(Suit.Heart, "paoxiao");
        Slash(declined, 1); Activate(declined);
        Reach(declined, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        Replay(declined, declinedRegistry);
        Answer(declined, c => c.Cards.Count == 0); Settle(declined);
        Require(declined.Events.Select(e => e.Payload).OfType<CardRespondedEvent>().Count(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) == 1 &&
            declined.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Single().PaymentCompleted == false &&
            !declined.Events.Select(e => e.Payload).OfType<NextSlashDamageReservedEvent>().Any() && declined.CreateSnapshot(1).Players[1].Hp == 3,
            "Tieqi declined true suit cost: a real full Dodge responds but cannot cancel or issue Paoxiao reserve.");
        Replay(declined, declinedRegistry);

        var (paid, paidRegistry) = Start(Suit.Heart, "paoxiao");
        Slash(paid, 1); Activate(paid);
        Reach(paid, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        var payment = Prompt(paid)!.Choices.First(c => c.Cards.Count == 1 &&
            paid.CreateSnapshot(1, true).Players[1].Hand.Single(h => h.Id == c.Cards[0]).Kind == CardKind.Slash);
        Replay(paid, paidRegistry); Answer(paid, c => c.Id == payment.Id); Settle(paid);
        var receipt = paid.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Single();
        var reserve = paid.Events.Select(e => e.Payload).OfType<NextSlashDamageReservedEvent>().Single();
        Require(receipt.PaymentCompleted && receipt.JudgmentSuit == Suit.Heart && receipt.PaidCardId == payment.Cards.Single() &&
            paid.CardMovements.Count(m => m.CardId == receipt.PaidCardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile) == 1 &&
            paid.Events.Select(e => e.Payload).OfType<CardRespondedEvent>().Count(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) == 1 &&
            reserve.OwnerSeat == 0 && reserve.TargetSeat == 1 && reserve.CardUseFrameId == receipt.CardUseFrameId && reserve.Amount == 1 &&
            paid.CreateSnapshot(1).Players[1].Hp == 4,
            "Completed true same-suit HE movement lets full real Dodge cancel and issue exactly one actual-actor reserve.");
        Replay(paid, paidRegistry);
        paid = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(paid.CreateCheckpoint())), paidRegistry);
        Slash(paid, 1);
        var use = paid.ResolutionStack.OfType<CardUseFrame>().Single();
        var consumed = paid.Events.Select(e => e.Payload).OfType<NextSlashDamageConsumedEvent>().Single();
        Require(use.Action is { Type: CardActionType.Use, ActorSeat: 0 } && use.NextSlashDamage == 1 &&
            use.Id != receipt.CardUseFrameId && consumed.CardUseFrameId == use.Id && consumed.OwnerSeat == 0 && consumed.Amount == 1,
            "The next genuine Slash Use after cold restore freezes and consumes the issued reserve once before Tieqi response.");
        Replay(paid, paidRegistry);
        Answer(paid, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Settle(paid);
        Require(paid.Events.Select(e => e.Payload).OfType<NextSlashDamageConsumedEvent>().Count() == 1,
            "Completing response after a frozen next Use cannot consume its reserve twice.");
        Replay(paid, paidRegistry);
    }

    public static void EquipmentChildAndArmorImmunity()
    {
        var (game, registry) = Start(Suit.Heart, "equipment");
        Driver(game, "equip", 1); Settle(game);
        Driver(game, "wound", 1); Settle(game);
        var armor = game.CreateSnapshot(1, true).Players[1].Equipment.Single();
        Require(armor.Kind == CardKind.SilverLion && game.CreateSnapshot(1).Players[1].Hp == 3, "Fixture equips and wounds through real commands.");
        Slash(game, 1); Activate(game);
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        Answer(game, c => c.Cards.SequenceEqual([armor.Id]));
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:mc-locked");
        Require(game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SlashSuitDiscard is { Stage: ProgramSlashSuitDiscardStage.PaymentMovement }), "Equipment payment owns movement child and does not resume Slash early.");
        Require(game.ResolutionStack.OfType<CardUseFrame>().Single().CardAttack!.JudgmentSuitDodgeRestriction is not null && !game.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Any(), "Exact Dodge cancellation restriction remains until movement children complete.");
        Replay(game, registry); Activate(game); Settle(game);
        Require(game.CardMovements.Count(m => m.CardId == armor.Id && m.From == CardLocation.Equipment(1) && m.To == CardLocation.DiscardPile) == 1, "Equipment actual payment cannot repeat after child return.");
        Require(game.Events.Select(e => e.Payload).OfType<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == 1 && e.RecoveredAmount == 1) && game.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Single().PaymentCompleted, "Silver Lion actual recovery and movement children precede paid cancellation release.");
        Replay(game, registry);

        var (immune, immuneRegistry) = Start(Suit.Spade, "renwang");
        Driver(immune, "equip", 1); Settle(immune);
        Slash(immune, 1); Activate(immune);
        Reach(immune, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        Answer(immune, c => c.Cards.Count == 0); Settle(immune);
        Require(immune.CreateSnapshot(1).Players[1].Hp == 4 && immune.Events.Select(e => e.Payload).OfType<ArmorEffectAppliedEvent>().Any(e => e.ArmorCard == CardKind.RenwangShield), "Tieqi cancellation restriction preserves Renwang black Slash immunity.");
        Replay(immune, immuneRegistry);
    }

    public static void FinalJudgmentReplacementAndClaim()
    {
        var (game, registry) = Start(Suit.Heart, "replacement");
        Slash(game, 1); Activate(game);
        Reach(game, p => p.Kind == DecisionKind.ProgramJudgmentReplacement);
        var prompt = Prompt(game)!;
        var original = game.ResolutionStack.OfType<JudgmentFrame>().Single().CardId;
        var originalSuit = game.ResolutionStack.OfType<JudgmentFrame>().Single().Suit;
        var replacement = prompt.Choices.First(c => c.Cards.Count == 1 && game.CreateSnapshot(0, true).Players[0].Hand.Single(h => h.Id == c.Cards[0]).Suit != originalSuit);
        Answer(game, c => c.Id == replacement.Id);
        Reach(game, p => p.Kind == DecisionKind.ProgramJudgmentTrigger);
        Replay(game, registry); Activate(game);
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        var result = game.Events.Select(e => e.Payload).OfType<JudgmentResolvedEvent>().Single(e => e.Reason == "boundary:tieqi");
        var draft = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SlashSuitDiscard is not null).SlashSuitDiscard!;
        Require(result.CardId == replacement.Cards.Single() && result.CardId != original && draft.FinalSuit == result.Suit, "Replacement result, not first printed suit, controls target payment.");
        Require(game.CreateCardZoneDiagnostics().Single(c => c.CardId == result.CardId).Location == CardLocation.Hand(0), "Real finalized judgment claim retains its entity instead of being discarded by Tieqi cleanup.");
        Answer(game, c => c.Cards.Count == 0); Settle(game); Replay(game, registry);
    }

    public static void FinalTargetsMultipleAndOutsideTurn()
    {
        var (redirect, rr) = Start(Suit.Heart, "redirect");
        var slash = redirect.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        Accept(redirect, new PlayCardCommand(0, slash.CardId!.Value, [1], redirect.Revision, Prompt(redirect)!.PromptId));
        Reach(redirect, p => p.SkillPrompt?.SkillId == "classic:liuli"); Activate(redirect);
        Answer(redirect, c => c.Targets.SequenceEqual([2])); Answer(redirect, c => c.Cards.Count == 1);
        Reach(redirect, p => p.SkillPrompt?.SkillId == "boundary:tieqi");
        Require(!redirect.Events.Select(e => e.Payload).OfType<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Any(), "Liuli fully completes before final-target Tieqi is offered.");
        Activate(redirect); Reach(redirect, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        Require(Prompt(redirect)!.PlayerSeat == 2 && redirect.Events.Select(e => e.Payload).OfType<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Single().Suppression.TargetSeat == 2, "Redirection freezes only actual final target.");
        Answer(redirect, c => c.Cards.Count == 0); Settle(redirect); Replay(redirect, rr);

        var (multi, mr) = Start(Suit.Heart, "multi");
        slash = multi.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        Accept(multi, new PlayCardCommand(0, slash.CardId!.Value, [1], multi.Revision, Prompt(multi)!.PromptId));
        Reach(multi, p => p.SkillPrompt?.SkillId == "fixture:mc-enhance"); Activate(multi);
        Answer(multi, c => c.Parameters.GetValueOrDefault("enhancement-option") == "ExtraTarget");
        Answer(multi, c => c.Targets.SequenceEqual([2]));
        Reach(multi, p => p.SkillPrompt?.SkillId == "boundary:tieqi"); Activate(multi);
        Reach(multi, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        Answer(multi, c => c.Cards.Count == 1);
        Reach(multi, p => p.SkillPrompt?.SkillId == "boundary:tieqi"); Activate(multi);
        Reach(multi, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        Require(Prompt(multi)!.PlayerSeat == 2 && multi.ResolutionStack.OfType<CardUseFrame>().Single().CardAttack!.JudgmentSuitDodgeRestriction?.TargetSeat == 2, "First-target payment never unlocks the second target.");
        Replay(multi, mr); Answer(multi, c => c.Cards.Count == 0); Settle(multi);
        var payments = multi.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().ToArray();
        Require(payments.Length == 2 && payments[0].TargetSeat == 1 && payments[0].PaymentCompleted && payments[1].TargetSeat == 2 && !payments[1].PaymentCompleted &&
            payments.Select(e => e.CardUseFrameId).Distinct().Count() == 1 && multi.Events.Select(e => e.Payload).OfType<JudgmentRequestedEvent>().Count(e => e.Reason == "boundary:tieqi") == 2, "Same multi-use judges and pays once independently for each target.");
        Replay(multi, mr);

        var (outside, or) = Start(Suit.Heart, "outside");
        Driver(outside, "request", 1);
        Answer(outside, c => c.Targets.SequenceEqual([2])); Answer(outside, c => c.Cards.Count == 1);
        Reach(outside, p => p.SkillPrompt?.SkillId == "boundary:tieqi"); Activate(outside);
        Reach(outside, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        var issued = outside.Events.Select(e => e.Payload).OfType<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Single().Suppression;
        Require(issued.Source.OwnerSeat == 1 && issued.TurnOwnerSeat == 0 && outside.CreateSnapshot(0).CurrentSeat == 0,
            "A genuine supplied Slash judges and suppresses until requester actual turn end.");
        Answer(outside, c => c.Cards.Count == 0); Settle(outside); Replay(outside, or);
        ExpireTurn(outside); Require(outside.Events.Select(e => e.Payload).OfType<CurrentTurnNonLockedSkillSuppressionsExpiredEvent>().Any(e => e.TurnOwnerSeat == 0 && e.TurnNumber == issued.TurnNumber), "Out-of-turn issuer does not defer expiry to its own next turn.");
        Replay(outside, or);
    }

    public static void MissingSuitLockedDefenseAndSourceLoss()
    {
        var (missing, nr) = Start(Suit.Spade, "missing"); Slash(missing, 1); Activate(missing); Settle(missing);
        var result = missing.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Single();
        Require(result.JudgmentSuit == Suit.Spade && !result.PaymentCompleted && result.PaidCardId is null && missing.CreateSnapshot(1).Players[1].Hp == 3,
            "Locked Hongyan changes target HE effective suits, so no printed-Spade card can fake Spade payment.");
        Replay(missing, nr);
        foreach (var pay in new[] { false, true })
        {
            var (defense, dr) = Start(Suit.Heart, "bazhen"); var defenseHp = defense.CreateSnapshot(0).Players[0].Hp; Driver(defense, "request", 1);
            Answer(defense, c => c.Targets.SequenceEqual([0])); Answer(defense, c => c.Cards.Count == 1);
            Reach(defense, p => p.SkillPrompt?.SkillId == "boundary:tieqi"); Activate(defense);
            Reach(defense, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
            Answer(defense, c => pay ? c.Cards.Count == 1 : c.Cards.Count == 0);
            Reach(defense, p => p.Kind == DecisionKind.RespondDodge); Answer(defense, c => c.Parameters.GetValueOrDefault("response") == "bagua"); Settle(defense);
            Require(defense.Events.Select(e => e.Payload).OfType<JudgmentRequestedEvent>().Any(e => e.Reason == JudgmentReasons.BaguaDefense) &&
                defense.CreateSnapshot(0).Players[0].Hp == defenseHp - (pay ? 0 : 1), "Locked Bazhen keeps real red-judgment Dodge; only actual payment permits cancellation.");
            Replay(defense, dr);
        }
        var (lost, lr) = Start(Suit.Heart); Slash(lost, 1); Activate(lost);
        Reach(lost, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        Answer(lost, c => c.Cards.Count == 0); Settle(lost);
        Accept(lost, new UseProgramSkillCommand(0, "fixture:mc-driver", "lose-source", [], [], lost.Revision, Prompt(lost)!.PromptId)); Settle(lost);
        Require(!lost.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == "boundary:tieqi") &&
            !lost.CreateSnapshot(1).Players[1].Skills!.Any(s => s.ContentId == "classic:qingguo"), "Losing actual issuing source cannot revoke already-issued turn suppression.");
        Replay(lost, lr); ExpireTurn(lost);
        Require(lost.CreateSnapshot(1).Players[1].Skills!.Any(s => s.ContentId == "classic:qingguo"), "Actual turn end restores qualification after source loss."); Replay(lost, lr);
    }

    public static void JudgmentDyingTargetLossAndLegacy()
    {
        var (game, registry) = Start(Suit.Heart, "death"); Slash(game, 1); Activate(game);
        Reach(game, p => p.Kind == DecisionKind.ProgramJudgmentTrigger); Replay(game, registry); Activate(game);
        for (var step = 0; step < 80 && Prompt(game)?.Kind != DecisionKind.PlayCard; step++)
            if (Prompt(game) is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 }) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(Prompt(game)?.Kind == DecisionKind.PlayCard && !game.CreateSnapshot(1).Players[1].IsAlive &&
            game.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Single() is { PaymentCompleted: false, PaidCardId: null } &&
            game.ResolutionStack.All(frame => frame is not (JudgmentFrame or ProgramSkillFrame)), "True judgment finalized HP/dying/death children return once and a dead target receives no phantom cost prompt.");
        Replay(game, registry);

        var (legacy, lr) = Start(Suit.Spade, "legacy");
        var action = legacy.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        Accept(legacy, new PlayCardCommand(0, action.CardId!.Value, [1], legacy.Revision, Prompt(legacy)!.PromptId));
        Reach(legacy, p => p.SkillPrompt?.SkillId == "classic:tieqi"); Activate(legacy); Settle(legacy);
        Require(!legacy.Events.Select(e => e.Payload).OfType<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Any() &&
            !legacy.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Any() && legacy.CreateSnapshot(1).Players[1].Hp == 4,
            "Classic black-result Tieqi preserves old Dodge cancellation and issues no new capability facts.");
        Require(!JsonSerializer.Serialize(new CardAttackState()).Contains("JudgmentSuitDodgeRestriction") &&
            !JsonSerializer.Serialize(new ProgramSkillFrame(1, 0, "old", "old", "hash", 0, [], [])).Contains("SlashSuitDiscard"), "No new capability preserves old canonical null omission.");
        Replay(legacy, lr);
    }

    private static void ExpireTurn(GameEngine game)
    {
        var turn = game.CreateSnapshot(0).TurnNumber;
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        for (var step = 0; step < 40 && game.CreateSnapshot(0).TurnNumber == turn; step++)
            if (Prompt(game) is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } discard)
                Accept(game, new DiscardCardsCommand(0, game.CreateSnapshot(0).Players[0].Hand.Take(discard.RequiredCardCount).Select(c => c.Id).ToArray(), discard.PromptId, game.Revision));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        Require(game.CreateSnapshot(0).TurnNumber > turn, "Bounded real turn ending reached.");
    }

    private static (GameEngine, ContentRegistry) Start(Suit suit, string scenario = "normal")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(suit, scenario));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = "identity:classic-mc", UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:mc-owner", game.Revision, Prompt(game)!.PromptId));
        Settle(game); return (game, registry);
    }
    private static void Slash(GameEngine game, int target)
    {
        var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, [target], game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind));
        Reach(game, p => p.SkillPrompt?.SkillId == "boundary:tieqi");
    }
    private static void Driver(GameEngine game, string id, int target) => Accept(game,
        new UseProgramSkillCommand(0, "fixture:mc-driver", id, [], [target], game.Revision, Prompt(game)!.PromptId));
    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision ?? Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null && p.Choices.Count > 0);
    private static void Activate(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate" || c.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> select)
    {
        var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, game.Revision));
    }
    private static void Settle(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> goal)
    {
        for (var step = 0; step < 100; step++)
        {
            if (Prompt(game) is { } prompt && goal(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Expected bounded command boundary, found " + Prompt(game)?.Kind);
    }
    private static void RejectChoice(GameEngine game, ChoiceId choice)
    {
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint()); var p = Prompt(game)!;
        Require(!game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice, game.Revision)).Accepted &&
            GameCheckpointJson.Serialize(game.CreateCheckpoint()) == before, "Illegal suit choice is atomically rejected.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Rejected real command.");
    }
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack), "Four viewer cold replay preserves exact typed stack.");
        Require(game.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 80, "Every physical entity is conserved.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(Suit suit, string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mc", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            typeof(StandardClassicGeneralPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryMaChaoContent")!
                .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
            foreach (var id in new[] { "classic:mashu", "classic:qingguo" }) builder.AddSkill(Classic.GetSkill(id));
            if (scenario == "paoxiao") builder.AddSkill(Classic.GetSkill("boundary:paoxiao"));
            if (scenario == "replacement") builder.AddSkill(Classic.GetSkill("classic:guicai"));
            if (scenario == "redirect") builder.AddSkill(Classic.GetSkill("classic:liuli"));
            if (scenario == "missing") builder.AddSkill(Classic.GetSkill("classic:hongyan"));
            if (scenario == "bazhen") builder.AddSkill(Classic.GetSkill("classic:bazhen"));
            if (scenario == "legacy") builder.AddSkill(Classic.GetSkill("classic:tieqi"));
            if (scenario == "equipment") builder.AddCard(Classic.GetCard("classic:silver-lion"));

            var rules = """
                {"schemaVersion":62,"skills":[
                {"id":"fixture:mc-driver","revision":1,"activations":[
                  {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"requested"}]},
                  {"id":"lose-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:tieqi"],"sourceBind":"fixture:mc-locked"}]},
                  {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                  {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
                {"id":"fixture:mc-locked","revision":1,"triggers":[{"id":"equipment-leave","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                {"id":"fixture:mc-claim","revision":1,"triggers":[{"id":"claim","window":"judgmentFinalized","subject":"owner","minimumRank":1,"maximumRank":13,"excludedReasons":[],"suits":["spade","heart","club","diamond"],"optional":true,"effects":[{"op":"claimJudgmentCard","target":"owner"}]}]}]}
                """;
            var programs = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:mc-driver":{"name":"真实链","description":"真实链"},"fixture:mc-locked":{"name":"锁定子链","description":"锁定子链"},"fixture:mc-claim":{"name":"取判定牌","description":"取判定牌"}}}""");
            foreach (var id in programs.Programs.Keys) builder.AddSkill(new(id, id, id)
            { Program = programs.Programs[id], Tags = id == "fixture:mc-locked" ? SkillTag.Locked : SkillTag.None });
            if (scenario == "death")
            {
                var death = SkillProgramCatalog.Load("""{"schemaVersion":62,"skills":[{"id":"fixture:mc-death","revision":1,"triggers":[{"id":"judgment-hp-child","window":"judgmentFinalized","subject":"any","minimumRank":1,"maximumRank":13,"excludedReasons":[],"suits":["spade","heart","club","diamond"],"optional":true,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:mc-death":{"name":"实际濒死子链","description":"实际濒死子链"}}}""");
                builder.AddSkill(new("fixture:mc-death", "实际濒死子链", "实际濒死子链") { Program = death.Programs["fixture:mc-death"], Tags = SkillTag.Locked });
            }
            if (scenario == "multi")
            {
                var enhance = SkillProgramCatalog.Load("""{"schemaVersion":62,"skills":[{"id":"fixture:mc-enhance","revision":1,"triggers":[{"id":"extra","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":true,"effects":[{"op":"applyCurrentCardEnhancements","target":"owner","amount":1}]}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:mc-enhance":{"name":"实际多目标","description":"实际多目标"}}}""");
                builder.AddSkill(new("fixture:mc-enhance", "实际多目标", "实际多目标") { Program = enhance.Programs["fixture:mc-enhance"] });
            }
            builder.AddSkill(new("fixture:mc-marker", "固定", "固定") { SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = -1000 } });
            builder.AddGeneral(new("fixture:mc-owner", "测试马超", "supporter", scenario == "legacy" ? "classic:tieqi" : "boundary:tieqi", "shu", 4,
                new[] { "classic:mashu", "fixture:mc-driver", "fixture:mc-marker" }.Concat(scenario == "paoxiao" ? ["boundary:paoxiao"] : Array.Empty<string>()).Concat(scenario == "replacement" ? ["classic:guicai", "fixture:mc-claim"] : Array.Empty<string>()).Concat(scenario == "multi" ? ["fixture:mc-enhance"] : scenario == "bazhen" ? ["classic:bazhen"] : Array.Empty<string>()).ToArray()));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:mc-{seat}", "目标" + seat, "supporter", "classic:qingguo", "wei", 4,
                new[] { "fixture:mc-locked" }.Concat(scenario == "redirect" && seat == 1 ? ["classic:liuli"] :
                    scenario == "death" && seat == 1 ? ["fixture:mc-death"] : scenario == "missing" ? ["classic:hongyan"] : scenario == "bazhen" ? ["classic:bazhen", "boundary:tieqi"] : scenario == "outside" ? ["boundary:tieqi"] : Array.Empty<string>()).ToArray()));
            var armorId = scenario == "equipment" ? "classic:silver-lion" : "standard:renwang_shield";
            builder.AddDeck(new("fixture:mc-deck", "固定", scenario == "replacement" ? 8 : 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(
                scenario == "bazhen" ? "standard:slash" : scenario is "equipment" or "renwang" && index % 6 == 0 ? armorId : index % 2 == 0 ? "standard:slash" : "standard:dodge",
                scenario == "replacement" ? (index % 2 == 0 ? Suit.Heart : Suit.Spade) : suit, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-mc", "固定", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:mc-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:mc-owner", "fixture:mc-1", "fixture:mc-2", "fixture:mc-3"]));
        }
    }
}
