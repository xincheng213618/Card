using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryCaoChongChecks
{
    private const string Chengxiang = "boundary:chengxiang-current";
    private const string Renxin = "boundary:renxin-current";
    private const string Bonus = "next-reveal-bonus";
    private const string Driver = "fixture:cc-driver";
    private const string Gain = "fixture:cc-gain";
    private const string Hp = "fixture:cc-hp";
    private const string Cost = "fixture:cc-cost";
    private const string Flip = "fixture:cc-flip";
    private const string Mode = "identity:classic-boundary-cao-chong-fixture";
    private const string GainReason = "skill-program.boundary:chengxiang-current.rank-bonus-obtain";
    private const string CostReason = "skill-program.boundary:renxin-current.SelectAndMoveOwnedCard";

    public static void PerPointBonusConsumptionPublicPoolAndColdReplay()
    {
        var (game, registry) = Create(); ReachPlay(game); Use(game, "heal", []); ReachPlay(game);
        Require(game.State.Players[0].Hp == 4 && game.State.Players[0].MaxHp == 4,
            "The real classic identity Lord uses the three-HP source's actual four-HP maximum.");
        Use(game, "incoming", [1]); BeginWeigh(game, registry, 4);
        var first = ChooseThirteenAndPauseGain(game, registry);
        Require(Started(game, Chengxiang) == 1 && game.State.Players[0].Hp == 2,
            "The first paid gain still owns the first point of actual two-point damage.");
        Continue(game); Reach(game, p => Activation(p, Chengxiang, "weigh-each-damage-point"));
        Require(Started(game, Chengxiang) == 1 && HasBonus(game),
            "The second damage point publishes its own optional activation; publishing it does not consume the reserved next-use bonus.");
        Replay(game, registry); Skip(game); ReachPlay(game);
        Require(Reveals(game).Select(e => e.Cards.Count).SequenceEqual([4]) && HasBonus(game),
            "Declining the second point preserves the exact instance's bonus and reveals no extra pool.");

        Use(game, "heal", []); ReachPlay(game); Use(game, "incoming-one", [1]); BeginWeigh(game, registry, 5);
        var emptyPool = Reveals(game).Last();
        Require(!HasBonus(game), "The accepted next reveal consumes its bonus before the actual subset choice.");
        Replay(game, registry); Answer(game, c => c.Cards.Count == 0); ReachPlay(game);
        Require(!HasBonus(game) && emptyPool.Cards.All(c => game.CardMovements.Count(m => m.CardId == c.Id &&
            m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1) &&
            game.Events.Select(e => e.Payload).OfType<ProgramCardSubsetSelectedEvent>().Last(e => e.SkillId == Chengxiang) is { RankSum: 0, CardIds.Count: 0 },
            "The legal empty selection arms no replacement bonus and discards all five actual revealed entities exactly once.");
        Replay(game, registry);

        Use(game, "heal", []); ReachPlay(game); Use(game, "incoming-one", [1]); BeginWeigh(game, registry, 4);
        var third = ChooseThirteenAndPauseGain(game, registry); Continue(game); ReachPlay(game);
        Use(game, "heal", []); ReachPlay(game); Use(game, "incoming-one", [1]); BeginWeigh(game, registry, 5);
        var fourth = ChooseThirteenAndPauseGain(game, registry); Continue(game); ReachPlay(game);
        Require(Reveals(game).Select(e => e.Cards.Count).SequenceEqual([4, 5, 4, 5]) && HasBonus(game) &&
            Started(game, Chengxiang) == 4 && new[] { first, third, fourth }.Distinct().Count() == 3 &&
            new[] { first, third, fourth }.All(id => GainMoves(game, id) == 1) &&
            game.Events.Select(e => e.Payload).OfType<ProgramCardSubsetSelectedEvent>().Where(e => e.SkillId == Chengxiang)
                .Select(e => e.RankSum).SequenceEqual([13, 0, 13, 13]) &&
            game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Where(e => e.SourceSeat == 1 && e.TargetSeat == 0)
                .Select(e => e.Amount).SequenceEqual([2, 1, 1, 1]) && game.CreateSnapshot(0).ProcessingCardCount == 0,
            "Real successive activations consume and re-arm a single next-use bonus, retain per-point damage, and never repeat a gained entity after child returns.");
        foreach (var pool in Reveals(game))
        foreach (var card in pool.Cards)
            Require(game.CardMovements.Count(m => m.CardId == card.Id && m.From == CardLocation.Processing &&
                (m.To == CardLocation.Hand(0) || m.To == CardLocation.DiscardPile)) == 1,
                "Every actual revealed entity leaves Processing exactly once through its selected gain or remainder discard.");
        Replay(game, registry);

        // A native AI uses the same shared ops and actual rank-thirteen deck.
        Use(game, "hurt-two", [1]);
        for (var point = 0; point < 2; point++)
        {
            Reach(game, p => p.PlayerSeat == 1 && Activation(p, Chengxiang, "weigh-each-damage-point"));
            Replay(game, registry); Accept(game, new AdvanceOneStepCommand(game.Revision));
            Reach(game, p => p.PlayerSeat == 1 && Action(p, "select-subset"));
            var pool = game.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Last(e => e.SkillId == Chengxiang && e.OwnerSeat == 1);
            Require(pool.Cards.Count == (point == 0 ? 4 : 5) && !HasBonus(game, 1),
                "Native AI actually activates each damage point and consumes its own reserved bonus on the next reveal.");
            foreach (var viewer in new[] { 0, 2, 3 }) Require(game.CreateSnapshot(viewer).PendingDecision is null,
                "The AI's subset choice stays private even though its actual reveal pool is public.");
            Replay(game, registry); Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        ReachPlay(game);
        var aiPools = game.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Where(e => e.SkillId == Chengxiang && e.OwnerSeat == 1).ToArray();
        Require(aiPools.Select(e => e.Cards.Count).SequenceEqual([4, 5]) && HasBonus(game, 1) &&
            game.Events.Select(e => e.Payload).OfType<ProgramCardSubsetSelectedEvent>().Where(e => aiPools.Any(pool => pool.FrameId == e.FrameId))
                .Select(e => e.RankSum).SequenceEqual([13, 13]) &&
            game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Last(e => e.SourceSeat == 0 && e.TargetSeat == 1).Amount == 2,
            "Native AI chooses actual legal thirteen-point subsets twice without a host-injected receipt or repeated payment.");
        Replay(game, registry);
    }

    public static void EquipmentPaymentBeforeTurnOverAndExactDyingRecovery()
    {
        var (game, registry) = Create(equipment: true); ReachPlay(game);
        var armor = game.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.SilverLion).Id;
        Accept(game, new PlayCardCommand(0, armor, [], game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        Require(game.State.Players[0].Hp == 2 && game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == armor),
            "The real injured rescuer equips its actual Silver Lion before the victim enters dying.");
        Require(game.State.Players[1].Hp == 1, "The actual other victim begins at one HP before existing Jueqing replaces the lethal three-point damage with HP loss.");
        Use(game, "hurt-other", [1]); Reach(game, p => Activation(p, Renxin, "equip-for-other-dying"));
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == 1);
        var entry = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.ResumeDyingFrameId == dying.Id);
        Require(dying.Continuation == DyingContinuationKind.AttackHpLoss &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == dying.ParentFrameId && f.SkillId == Driver && f.ActivationId == "hurt-other") &&
            game.Events.Select(e => e.Payload).OfType<DamageReplacedWithHpLossEvent>().Single(e => e.ParentResolutionId == dying.ParentFrameId) is
                { PolicyOwnerSeat: 0, TargetSeat: 1, Amount: 3, RemainingHp: -2 } &&
            game.State.Players[1].Hp == -2 &&
            game.State.Players[1].IsAlive && entry.OwnerSeat == 1 && entry.Window == SkillProgramTriggerWindow.DyingEntering &&
            entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry,
            $"The optional rescue belongs to the exact other victim's actual DyingEntering token after existing Jueqing replaces three real damage with three HP loss; continuation={dying.Continuation}, victimHP={game.State.Players[1].Hp}, entryOwner={entry.OwnerSeat}, window={entry.Window}, return={entry.Continuation}, parent={dying.ParentFrameId}.");
        Replay(game, registry); Activate(game, Renxin, "equip-for-other-dying");
        Reach(game, p => Action(p, "select-and-move-owned-card"));
        Require(Prompt(game)!.Choices.Any(c => c.Cards.SequenceEqual([armor])) && Prompt(game)!.Choices.All(c =>
            c.Cards.Count == 1 && (game.CreateSnapshot(0).Players[0].Hand.Concat(game.CreateSnapshot(0).Players[0].Equipment))
                .Any(card => card.Id == c.Cards[0] && EquipmentCatalog.IsEquipment(card.Kind))),
            "The real payment offers only currently owned HE equipment entities, including the equipped armor.");
        Replay(game, registry); Reject(game); Answer(game, c => c.Cards.SequenceEqual([armor]));
        Reach(game, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 0);
        var paid = RescueFrame(game); AssertDyingOwner(game, paid, dying.Id, entry.Id);
        Require(paid.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0, CoverageResultBind: null } &&
            paid.CardSetBindings.Single(b => b.Name == "renxin-cost").CardIds.SequenceEqual([armor]) &&
            game.State.Players[0].Hp == 3 && !game.State.Players[0].IsFaceDown && game.State.Players[1].Hp == -2 &&
            CostMoves(game, armor) == 1 && game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f =>
                f.ResumeFrameId == paid.Id && f.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                f.Change.ParentFrameId == paid.Id && f.Change.TargetSeat == 0),
            "Silver Lion's actual one-HP recovery pauses beneath the paid instruction before turnover or victim recovery.");
        Replay(game, registry); Reject(game); Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Cost);
        paid = RescueFrame(game); AssertDyingOwner(game, paid, dying.Id, entry.Id);
        var costWindow = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.ParentFrameId == paid.Id);
        Require(costWindow.ResumeProgramFrameId is null && costWindow.Batch.AwaitingProgramFrameId == paid.Id &&
            costWindow.Batch.Movements is [var cost] && cost.CardId == armor && cost.From == CardLocation.Equipment(0) &&
            cost.To == CardLocation.DiscardPile && cost.Reason.Value == CostReason &&
            !game.State.Players[0].IsFaceDown && game.State.Players[1].Hp == -2,
            "The exact equipment payment movement child returns to the same paid source before either later effect.");
        Replay(game, registry); Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Flip);
        paid = RescueFrame(game); AssertDyingOwner(game, paid, dying.Id, entry.Id);
        Require(game.State.Players[0].IsFaceDown && game.State.Players[1].Hp == -2 && CostMoves(game, armor) == 1 &&
            game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f =>
                f.Window == SkillProgramTriggerWindow.CharacterTurnedOver && f.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                f.CharacterStateContinuation == CharacterStateContinuation.Program && f.ResumeProgramFrameId == paid.Id),
            "The real turnover owns a typed program-return child while the original other victim remains at minus two HP.");
        Replay(game, registry); Reject(game); Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 1);
        paid = RescueFrame(game); AssertDyingOwner(game, paid, dying.Id, entry.Id);
        Require(game.State.Players[1].Hp == 1 && game.State.Players[0].Hp == 3 &&
            game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.ResumeFrameId == paid.Id &&
                f.Continuation == PostEventContinuation.Program && f.Change.ParentFrameId == paid.Id && f.Change.TargetSeat == 1) &&
            game.Events.Select(e => e.Payload).OfType<RecoveryAppliedEvent>().Single(e => e.SourceSeat == 0 && e.TargetSeat == 1) is { Amount: 3, RemainingHp: 1 },
            "The recovery producer heals the frozen exact victim to one, rather than healing one point or another seat.");
        Replay(game, registry); Reject(game); Continue(game); ReachPlay(game);
        Require(CostMoves(game, armor) == 1 && game.State.Players[0].IsFaceDown && game.State.Players[1].IsAlive &&
            game.State.Players[1].Hp == 1 && Started(game, Renxin) == 1 &&
            game.Events.Select(e => e.Payload).OfType<SilverLionRemovedRecoveryEvent>().Count(e => e.PlayerSeat == 0 && e.RecoveredAmount == 1) == 1 &&
            game.Events.Select(e => e.Payload).OfType<CharacterStateChangedEvent>().Count(e => e.Change.ParentFrameId == paid.Id &&
                e.Change.TargetSeat == 0 && e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver) == 1 &&
            game.Events.Select(e => e.Payload).OfType<DyingResolvedEvent>().Single(e => e.ResolutionId == dying.Id) is { VictimSeat: 1, Survived: true } &&
            game.Events.Select(e => e.Payload).OfType<DamageReplacedWithHpLossEvent>().Single(e => e.ParentResolutionId == dying.ParentFrameId) is
                { PolicyOwnerSeat: 0, TargetSeat: 1, Amount: 3, RemainingHp: -2 } &&
            !game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1) &&
            !game.ResolutionStack.OfType<DyingFrame>().Any() && !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Renxin) &&
            game.CreateSnapshot(0).ProcessingCardCount == 0,
            "All real cost, HP, movement and turnover children return once; rescue preserves the actual Jueqing replacement and completes its precise negative-HP Dying token.");
        Replay(game, registry);
        PhysicalSlashDyingRecovery();
    }

    private static void BeginWeigh(GameEngine game, ContentRegistry registry, int count)
    {
        Reach(game, p => Activation(p, Chengxiang, "weigh-each-damage-point"));
        Activate(game, Chengxiang, "weigh-each-damage-point"); Reach(game, p => Action(p, "select-subset"));
        var revealed = Reveals(game).Last(); var frame = WeighFrame(game);
        Require(revealed.FrameId == frame.Id && revealed.OwnerSeat == 0 && revealed.Cards.Count == count &&
            revealed.Cards.All(c => c.Rank == 13) && Prompt(game)!.Choices.All(c => c.Cards.Count <= 1) &&
            Prompt(game)!.Choices.Any(c => c.Cards.Count == 0) && Prompt(game)!.Choices.Any(c => c.Cards.Count == 1),
            "The actual public top pool and legal arbitrary subsets enforce the real rank-sum cap of thirteen.");
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var view = game.CreateSnapshot(viewer);
            Require(view.PublicRevealedCards.Select(c => c.Id).Order().SequenceEqual(revealed.Cards.Select(c => c.Id).Order()),
                "Every player sees the same actual revealed pool.");
            if (viewer != 0) Require(view.PendingDecision is null && view.Players[0].Hand.Count == 0,
                "Public revelation does not expose the owner's private hand or private subset decision.");
        }
        FreezePreparedCollections(game, revealed); Replay(game, registry); Reject(game);
    }

    private static void PhysicalSlashDyingRecovery()
    {
        var (game, registry) = Create(equipment: true, physicalSlash: true); ReachPlay(game);
        // Eight total entities: four Slash and four armor. One initial entity
        // per seat plus the actual four-card draw gives this owner five entities,
        // so both categories are guaranteed without a seed/hand search.
        Use(game, "draw-four", []); ReachPlay(game);
        var hand = game.CreateSnapshot(0).Players[0].Hand;
        Require(hand.Count == 5 && hand.Any(c => c.Kind == CardKind.Slash) && hand.Any(c => c.Kind == CardKind.SilverLion) &&
            game.State.Players[0].Hp == 2 && game.State.Players[1].Hp == 1,
            "The tiny real deck guarantees a physical Slash and an equipment cost while preserving the actual formal initial HP.");
        var armor = hand.First(c => c.Kind == CardKind.SilverLion).Id;
        var slash = hand.First(c => c.Kind == CardKind.Slash).Id;
        Accept(game, new PlayCardCommand(0, armor, [], game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        Accept(game, new PlayCardCommand(0, slash, [1], game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => Activation(p, Renxin, "equip-for-other-dying"));
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == slash && f.CardKind == CardKind.Slash);
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == 1);
        var damage = game.ResolutionStack.OfType<DamageFrame>().Single(f => f.Id == dying.ParentFrameId);
        var entry = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.ResumeDyingFrameId == dying.Id);
        Require(dying.Continuation == DyingContinuationKind.Damage && damage.ParentFrameId == use.Id &&
            damage.SourceSeat == 0 && damage.TargetSeat == 1 && damage.Amount == 1 && game.State.Players[1].Hp == 0,
            "The original Dying token is owned by actual physical Slash damage, retaining its paid CardUse frame.");
        Replay(game, registry); Activate(game, Renxin, "equip-for-other-dying");
        Reach(game, p => Action(p, "select-and-move-owned-card")); Answer(game, c => c.Cards.SequenceEqual([armor]));
        Reach(game, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 0);
        var paid = RescueFrame(game); AssertDyingOwner(game, paid, dying.Id, entry.Id);
        Require(game.State.Players[0].Hp == 3 && !game.State.Players[0].IsFaceDown && game.State.Players[1].Hp == 0 &&
            CostMoves(game, armor) == 1 && game.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id),
            "The physical attack remains retained during the rescuer's one-time actual Silver Lion recovery.");
        Replay(game, registry); Continue(game); Reach(game, p => p.SkillPrompt?.SkillId == Cost);
        paid = RescueFrame(game); AssertDyingOwner(game, paid, dying.Id, entry.Id);
        Require(game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.Batch.ParentFrameId == paid.Id &&
            f.Batch.AwaitingProgramFrameId == paid.Id && f.ResumeProgramFrameId is null),
            "The physical attack's rescue waits for the exact paid equipment movement return.");
        Replay(game, registry); Continue(game); Reach(game, p => p.SkillPrompt?.SkillId == Flip);
        paid = RescueFrame(game); AssertDyingOwner(game, paid, dying.Id, entry.Id);
        Require(game.State.Players[0].IsFaceDown && game.State.Players[1].Hp == 0 &&
            game.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id && f.CardId == slash) &&
            game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(f =>
                f.Window == SkillProgramTriggerWindow.CharacterTurnedOver && f.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                f.CharacterStateContinuation == CharacterStateContinuation.Program && f.ResumeProgramFrameId == paid.Id),
            "The actual turned-over observer retains both the exact rescue program and original paid physical attack.");
        Replay(game, registry); Reject(game); Continue(game);
        Reach(game, p => p.SkillPrompt?.SkillId == Hp && p.PlayerSeat == 1);
        paid = RescueFrame(game); AssertDyingOwner(game, paid, dying.Id, entry.Id);
        Require(game.State.Players[1].Hp == 1 && game.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id) &&
            game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.ResumeFrameId == paid.Id &&
                f.Change.ParentFrameId == paid.Id && f.Change.TargetSeat == 1 && f.Continuation == PostEventContinuation.Program) &&
            game.Events.Select(e => e.Payload).OfType<RecoveryAppliedEvent>().Single(e => e.SourceSeat == 0 && e.TargetSeat == 1) is { Amount: 1, RemainingHp: 1 },
            "The physical attack remains owned while actual victim recovery pauses on its precise program return.");
        Replay(game, registry); Reject(game); Continue(game); ReachPlay(game);
        Require(CostMoves(game, armor) == 1 && Started(game, Renxin) == 1 && game.State.Players[1].IsAlive &&
            game.State.Players[1].Hp == 1 && game.State.Players[0].IsFaceDown &&
            game.Events.Select(e => e.Payload).OfType<DyingResolvedEvent>().Single(e => e.ResolutionId == dying.Id).Survived &&
            game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Single(e => e.SourceSeat == 0 && e.TargetSeat == 1).Amount == 1 &&
            game.CardMovements.Count(m => m.CardId == slash && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            game.CardMovements.Count(m => m.CardId == slash && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 &&
            !game.ResolutionStack.OfType<DyingFrame>().Any() && !game.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id) &&
            game.CreateSnapshot(0).ProcessingCardCount == 0,
            "The original physical Slash, equipment payment and Dying each finish once after typed turnover and HP children return.");
        Replay(game, registry);
    }

    private static int ChooseThirteenAndPauseGain(GameEngine game, ContentRegistry registry)
    {
        var selected = Prompt(game)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(game, c => c.Cards.SequenceEqual([selected])); Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        var paid = WeighFrame(game);
        var context = paid.WindowContext!;
        var damage = game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single(f => f.Id == context.ParentFrameId);
        var movement = game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Batch.ParentFrameId == paid.Id);
        var candidate = damage.Candidates[damage.CandidateIndex];
        Require(paid.InstructionIndex == 3 && paid.PendingMovementContinuation is { SubjectSeat: 0, BeforeCount: 0, CoverageResultBind: null } &&
            paid.CardSetBindings.Single(b => b.Name == "selected").CardIds.SequenceEqual([selected]) &&
            context.Window == SkillProgramTriggerWindow.AfterDamageApplied && damage.TriggerWindow == context.Window &&
            damage.TargetSeat == 0 && damage.SourceSeat == 1 && candidate.OwnerSeat == paid.OwnerSeat &&
            candidate.ProgramId == paid.SkillId && candidate.ProgramTriggerId == paid.TriggerId &&
            candidate.SkillInstanceId == paid.SkillInstanceId && candidate.GameplayHash == paid.GameplayHash &&
            movement.ResumeProgramFrameId is null && movement.Batch.AwaitingProgramFrameId is null &&
            movement.Batch.ParentFrameId == paid.Id && movement.Batch.OriginSkillId == paid.SkillId &&
            movement.Batch.OriginSkillInstanceId == paid.SkillInstanceId && movement.Batch.OriginOwnerSeat == 0 &&
            movement.Batch.Movements is [var gain] && gain.CardId == selected && gain.From == CardLocation.Processing &&
            gain.To == CardLocation.Hand(0) && gain.Reason.Value == GainReason &&
            game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == selected) && GainMoves(game, selected) == 1 && HasBonus(game),
            "The real atomic thirteen-point gain freezes its next-use state before a child, retaining its exact damage cursor, source instance and paid movement ledger.");
        FreezePreparedCollections(game, Reveals(game).Last()); Replay(game, registry); Reject(game); return selected;
    }

    private static void AssertDyingOwner(GameEngine game, ProgramSkillFrame paid, long dyingId, long entryId)
    {
        var entry = game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.Id == entryId);
        var candidate = entry.Candidates[entry.CandidateIndex];
        Require(game.ResolutionStack.OfType<DyingFrame>().Any(f => f.Id == dyingId && f.VictimSeat == 1) &&
            entry.ResumeDyingFrameId == dyingId && entry.Window == SkillProgramTriggerWindow.DyingEntering &&
            entry.Continuation == ProgramLifecycleContinuation.ResumeDyingEntry &&
            paid.OwnerSeat == 0 && paid.WindowContext is { Window: SkillProgramTriggerWindow.DyingEntering, TargetSeat: 1 } context &&
            context.ParentFrameId == entryId && candidate.OwnerSeat == paid.OwnerSeat && candidate.SkillId == paid.SkillId &&
            candidate.BindingId == paid.TriggerId && candidate.SkillInstanceId == paid.SkillInstanceId && candidate.GameplayHash == paid.GameplayHash,
            "Each paused rescue child retains the original exact other-victim entry and current owner/binding/instance/gameplay-hash candidate.");
    }

    private static void FreezePreparedCollections(GameEngine game, ProgramCardsRevealedEvent revealed)
    {
        var before = State(game); Frozen(revealed.Cards);
        var selected = game.Events.Select(e => e.Payload).OfType<ProgramCardSubsetSelectedEvent>().LastOrDefault(e => e.FrameId == revealed.FrameId);
        if (selected is not null) Frozen(selected.CardIds);
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var view = game.CreateSnapshot(viewer); Frozen(view.PublicRevealedCards);
            var states = view.Players[0].SkillRuntimeStates!; Frozen(states);
            Frozen(states.Single(s => s.SkillId == Chengxiang).BooleanStates!);
            if (view.PendingDecision is { } decision)
            { Frozen(decision.Choices); foreach (var choice in decision.Choices) { Frozen(choice.Cards); Frozen(choice.Targets); } }
        }
        Require(State(game) == before, "Prepared public pools, committed subset payloads and nested view collections reject mutation without changing a command prefix.");
    }

    private static void Frozen<T>(IReadOnlyList<T> values)
    {
        if (values is not IList<T> list) return;
        Require(list.IsReadOnly, "An exposed built-in collection is immutable.");
        if (list.Count == 0) return;
        try { list[0] = list[0]; }
        catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A prepared or committed collection accepted mutation.");
    }

    private static ProgramSkillFrame WeighFrame(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chengxiang);
    private static ProgramSkillFrame RescueFrame(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Renxin);
    private static ProgramCardsRevealedEvent[] Reveals(GameEngine g) => g.Events.Select(e => e.Payload).OfType<ProgramCardsRevealedEvent>().Where(e => e.SkillId == Chengxiang && e.OwnerSeat == 0).ToArray();
    private static int Started(GameEngine g, string skill) => g.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == skill && e.OwnerSeat == 0);
    private static int GainMoves(GameEngine g, int id) => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason.Value == GainReason);
    private static int CostMoves(GameEngine g, int id) => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == CostReason);
    private static bool HasBonus(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players[seat].SkillRuntimeStates!.Single(s => s.SkillId == Chengxiang).BooleanStates!.Single(s => s.StateId == Bonus).Value;
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Activation(PendingDecision p, string skill, string binding) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Activate(GameEngine g, string skill, string binding) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, IReadOnlyList<int> targets) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets, g.Revision, Prompt(g)!.PromptId));
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 180; step++) { var p = Prompt(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed Cao Chong fixture did not reach the real boundary: " + JsonSerializer.Serialize(Prompt(g)));
    }
    private static void Advance(GameEngine g)
    {
        var p = Prompt(g);
        if (p?.SkillPrompt?.SkillId is Gain or Hp or Cost or Flip) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Skip(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine g)
    { var before = State(g); var p = Prompt(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before, "An unpublished choice changes no paid state or private view."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine g, ContentRegistry registry) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry)), "The actual command prefix cold-restores all four views, exact typed frames, movement ledger and events.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(bool equipment = false, bool physicalSlash = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(equipment, physicalSlash));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:cc-owner", game.Revision, Prompt(game)!.PromptId)); return (game, registry);
    }

    private sealed class Fixture(bool equipment, bool physicalSlash) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-cao-chong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"activations":[
                  {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]},
                  {"id":"incoming-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                  {"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":4,"clampToMaxHp":true}]},
                  {"id":"hurt-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                  {"id":"hurt-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":3}]},
                  {"id":"draw-four","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":4}]}]},
                 {"id":"fixture:cc-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                 {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gained","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{GainReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Cost}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand","equipment"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{CostReason}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"cost-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Flip}}","revision":1,"triggers":[{"id":"turned","window":"characterTurnedOver","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"flip-seen","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实伤害驱动", description = "实际伤害和恢复" }, ["fixture:cc-quiet"] = new { name = "安静实际回合", description = "固定普通杀额度" }, [Gain] = Observer("真实获牌"), [Hp] = Observer("真实回复"), [Cost] = Observer("真实付款"), [Flip] = Observer("真实翻面") } }));
            foreach (var id in new[] { Driver, "fixture:cc-quiet", Gain, Hp, Cost, Flip }) builder.AddSkill(new(id, id, "公共能力真实边界夹具") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:cc-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:cc-owner", "当前界曹冲机制", "supporter", equipment ? Renxin : Chengxiang, "wei", 3,
                equipment ? physicalSlash ? [Driver, Hp, Cost, Flip] : [Driver, Hp, Cost, Flip, "classic:jueqing"] : [Driver, Gain]) { InitialHp = equipment ? 1 : 3 });
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:cc-target-{i}", "固定实际来源", "supporter", "fixture:cc-selection", "wu", equipment ? 3 : 8, equipment ? ["fixture:cc-quiet", Hp] : ["fixture:cc-quiet", Chengxiang]) { InitialHp = equipment ? 1 : null });
            builder.AddDeck(new("fixture:cc-deck", "固定点数真实实体", physicalSlash ? 1 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, physicalSlash ? 8 : 100).Select(i => new ContentDeckPhysicalCard(physicalSlash ? i % 2 == 0 ? "standard:slash" : "classic:silver-lion" : equipment ? "classic:silver-lion" : "standard:slash", equipment ? Suit.Heart : Suit.Spade, 13)).ToArray() });
            builder.AddMode(new(Mode, "当前界曹冲机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:cc-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:cc-owner", "fixture:cc-target-1", "fixture:cc-target-2", "fixture:cc-target-3"]));
        }
        private static object Observer(string name) => new { name, description = "暂停实际子结算", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
