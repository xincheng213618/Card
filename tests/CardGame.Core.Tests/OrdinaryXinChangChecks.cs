using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryXinChangChecks
{
    private const string Canmou = "ol:canmou", Congjian = "ol:congjian";
    private const string Driver = "fixture:xin-chang-driver", Complete = "fixture:xin-chang-complete";
    private const string Gain = "fixture:xin-chang-gain", Acquire = "fixture:xin-chang-acquire";
    private const string Suppress = "fixture:xin-chang-suppress", Owner = "fixture:xin-chang-owner";
    private const string PrintedLoss = "fixture:xin-chang-printed-loss", Reacquire = "fixture:xin-chang-reacquire";
    private const string Mode = "identity:classic-xin-chang-shared";
    private const string RewardReason = "skill-program.joined-trick.damage-reward";
    private enum Scenario { Hand, HandDrawTwo, Snatch, BorrowedSword, Damage, SourceLoss, Reacquired, DrawTwo, HpTie, Multiple, Counterspell }

    public static void LargestHandTrickAdditionUsesActualActorAndRejectsTies()
    {
        var (g, registry) = Start(Scenario.Hand);
        var action = Action(g, LegalActionKind.IronChain, [1, 3]);
        var use = BeginHumanAddition(ref g, registry, action);
        Require(use.Action?.EffectiveSuit == Suit.Spade && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([2])) &&
            P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => !c.Targets.Contains(1) && !c.Targets.Contains(3)),
            "The real physical Iron Chain freezes its effective Spade suit, and the unique actual user's hand lead permits one legal new target while excluding both original targets.");
        AddHuman(ref g, registry, use, [2]); Reach(g, IsComplete); g = Cold(g, registry);
        Require(E<IronChainStateChangedEvent>(g).Where(e => e.ResolutionId == use.Id).Select(e => e.TargetSeat).SequenceEqual([1, 3, 2]),
            "The original two targets and the new target each receive one native Iron Chain effect in order.");
        Reach(g, IsPlay); AssertFinished(g, use.Id, action.CardId!.Value, CardKind.IronChain); _ = Cold(g, registry);

        (g, registry) = Start(Scenario.Hand);
        Use(g, "tie-hand", [1]); Reach(g, IsPlay);
        action = Action(g, LegalActionKind.IronChain, [1, 3]); Play(g, action); Reach(g, IsComplete);
        var tied = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == action.CardId);
        Require(V(g, 0).HandCount == V(g, 1).HandCount && E<ProgramBindingStartedEvent>(g).All(e => e.SkillId != Canmou) &&
            E<UniqueLeaderTrickQualificationEvent>(g).Single(e => e.CardUseFrameId == tied.Id).UniqueLargestHandSeat is null,
            "The real physical payment creates a maximum-hand tie at designation, so no Canmou activation is issued.");
        Reach(g, IsPlay); AssertFinished(g, tied.Id, action.CardId!.Value, CardKind.IronChain);
        Use(g, "boost-hand", [2]); Reach(g, IsPlay); Use(g, "foreign-duel", [2]);
        Reach(g, p => Offer(p, Canmou, 0));
        Require(V(g, 2).HandCount > Enumerable.Range(0, 4).Where(s => s != 2).Max(s => V(g, s).HandCount),
            "A real foreign actor, rather than the skill owner, owns the unique hand lead.");
        var foreign = BeginHumanAddition(ref g, registry);
        Require(foreign.Action is { ActorSeat: 2, Type: CardActionType.Use } && foreign.TargetSeats.SequenceEqual([0]) && foreign.PhysicalCardIds!.Count == 0,
            "The mature selected-actor Duel supplies a genuine foreign zero-material Use with its exact original target.");
        AddHuman(ref g, registry, foreign, [3]); Reach(g, IsComplete); g = Cold(g, registry);
        Require(E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 2 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 2 && e.TargetSeat == 3 && e.Amount == 1) == 1,
            "The foreign Duel resolves both real targets under the original actor without creating physical material.");
        Reach(g, IsPlay); Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == foreign.Id && e.CardKind == CardKind.Duel) == 1,
            "The exact foreign Duel finishes once after both native target effects.");

        (g, registry) = Start(Scenario.Snatch);
        action = Action(g, LegalActionKind.Snatch, [1]); use = BeginHumanAddition(ref g, registry, action);
        Require(use.Action?.EffectiveSuit == Suit.Spade && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([3])) &&
            !P(g)!.Choices.Any(c => c.Targets.Contains(0) || c.Targets.Contains(1) || c.Targets.Contains(2)),
            "The real physical Snatch freezes its effective Spade suit, while Canmou keeps ordinary distance and self/duplicate restrictions: distant seat two cannot be added.");
        AddHuman(ref g, registry, use, [3]); Reach(g, IsComplete); g = Cold(g, registry);
        Require(E<TargetCardSelectionRequestedEvent>(g).Where(e => e.ResolutionId == use.Id).Select(e => e.TargetSeat).SequenceEqual([1, 3]),
            "Each genuinely legal Snatch target owns its separate opaque native card choice.");
        Reach(g, IsPlay); AssertFinished(g, use.Id, action.CardId!.Value, CardKind.Snatch);
        action = Action(g, LegalActionKind.Snatch, [1]); use = BeginHumanAddition(ref g, registry, action);
        Answer(g, c => c.Targets.Count == 0); g = Cold(g, registry); Reach(g, IsComplete);
        var declined = E<UniqueLeaderTrickTargetResolvedEvent>(g).Single(e => e.CardUseFrameId == use.Id);
        var declinedUse = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == use.Id);
        Require(E<ProgramCardUseTargetAddedEvent>(g).All(e => e.CardUseFrameId != use.Id) &&
            declined.AddedTargetSeats.Count == 0 && declined.OriginalTargetSeats.SequenceEqual([1]) &&
            declined.ResultTargetSeats.SequenceEqual([1]) && declinedUse.TargetSeats.SequenceEqual([1]) &&
            declinedUse.Action!.TargetSeats.SequenceEqual([1]),
            "An ordinary declined addition records exactly one unchanged-target audit result, emits no target-added event and leaves the real final action unchanged.");
        Frozen(declined.OriginalTargetSeats); Frozen(declined.AddedTargetSeats); Frozen(declined.ResultTargetSeats);
        Reach(g, IsPlay); AssertFinished(g, use.Id, action.CardId!.Value, CardKind.Snatch);

        (g, registry) = Start(Scenario.HandDrawTwo);
        var handBefore = V(g, 0).HandCount;
        action = Action(g, LegalActionKind.DrawTwo); use = BeginHumanAddition(ref g, registry, action);
        Require(use.TargetSeats.SequenceEqual([0]) &&
            E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == use.Action!.ActionId).Action.TargetSeats.Count == 0,
            "The real accepted DrawTwo retains its implicit self target while only the new target-selection carrier normalizes it.");
        Answer(g, c => c.Targets.Count == 0); g = Cold(g, registry); Reach(g, IsComplete); g = Cold(g, registry);
        var completedUse = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == use.Id);
        var normalized = E<UniqueLeaderTrickTargetResolvedEvent>(g).Single(e => e.CardUseFrameId == use.Id);
        var exactUseProof = typeof(GameEngine).GetMethod("HasExactAcceptedActualHandGainUse", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The retained actual-use proof helper is missing.");
        Require(completedUse.Action!.TargetSeats.SequenceEqual([0]) && normalized.OriginalTargetSeats.SequenceEqual([0]) &&
            normalized.AddedTargetSeats.Count == 0 && normalized.ResultTargetSeats.SequenceEqual([0]) &&
            E<ProgramCardUseTargetAddedEvent>(g).All(e => e.CardUseFrameId != use.Id) &&
            exactUseProof.Invoke(g, [completedUse, completedUse.Action]) is true && V(g, 0).HandCount == handBefore + 1,
            "A cold-restored declined self DrawTwo remains a proven accepted actual use, draws only its native two cards and emits no invented target addition.");
        Frozen(normalized.OriginalTargetSeats); Frozen(normalized.AddedTargetSeats); Frozen(normalized.ResultTargetSeats);
        Reach(g, IsPlay); AssertFinished(g, use.Id, action.CardId!.Value, CardKind.DrawTwo); _ = Cold(g, registry);
    }

    public static void LargestHandBorrowedSwordAppendsOneCompleteLegalPair()
    {
        var (g, registry) = Start(Scenario.BorrowedSword);
        foreach (var seat in new[] { 1, 3 }) { Use(g, "equip", [seat]); Reach(g, IsPlay); }
        Use(g, "draw-three", []); Reach(g, IsPlay);
        var weapons = new[] { 1, 3 }.ToDictionary(s => s, s => V(g, s).Equipment.Single().Id);
        var action = Action(g, LegalActionKind.BorrowedSword, [1, 2]);
        var use = BeginHumanAddition(ref g, registry, action);
        Require(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([3, 2])) && P(g)!.Choices.Any(c => c.Targets.SequenceEqual([3, 0])) &&
            P(g)!.Choices.Where(c => c.Targets.Count > 0).All(c => c.Targets.Count == 2 && c.Targets[0] != 1),
            "A legal Borrowed Sword addition is one complete new weapon-holder/victim pair; only the holder must be distinct.");
        AddHuman(ref g, registry, use, [3, 2]); Reach(g, IsComplete); g = Cold(g, registry);
        Require(g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == use.Id).TargetSeats.SequenceEqual([1, 2, 3, 2]) &&
            weapons.All(pair => g.CardMovements.Count(m => m.CardId == pair.Value && m.From == CardLocation.Equipment(pair.Key) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.BorrowedSwordGive) == 1) &&
            weapons.All(pair => g.CardMovements.Count(m => m.CardId == pair.Value && m.From == CardLocation.Processing &&
                m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.BorrowedSwordGive) == 1),
            "Both genuinely Slash-less weapon holders pay their own exact native weapon transfer once: " + Diagnostic(g));
        Reach(g, IsPlay); AssertFinished(g, use.Id, action.CardId!.Value, CardKind.BorrowedSword); _ = Cold(g, registry);
    }

    public static void JoinedTrickDamageRewardOwnsExactUseAndSurvivesSourceLoss()
    {
        foreach (var scenario in new[] { Scenario.Damage, Scenario.SourceLoss, Scenario.Reacquired })
        {
            var (g, registry) = Start(scenario);
            var beforeHand = V(g, 1).HandCount;
            Use(g, "foreign-duel", [2]);
            var use = NativeJoin(ref g, registry, CardKind.Duel);
            var frozenSource = use.JoinedTrickDamageBenefits!.Single().Source;
            Require(use.Action!.ActorSeat == 2 && use.TargetSeats.SequenceEqual([0, 1]),
                "The native AI adds itself to the real foreign actor's Duel against the unique maximum-HP third party.");
            if (scenario == Scenario.Reacquired)
            {
                Reach(g, p => p?.SkillPrompt?.SkillId == Reacquire && p.PlayerSeat == 1);
                var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == Reacquire);
                var loss = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.OrderedPrintedSkillLoss is not null);
                var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Id == child.WindowContext!.ParentFrameId);
                Require(loss.OrderedPrintedSkillLoss!.Lease.LostSkillIds.SequenceEqual([Congjian]) &&
                    E<OrderedPrintedSkillGrantRemovedEvent>(g).Count(e => e.FrameId == loss.Id && e.Grant.SkillId == Congjian &&
                        e.Grant.SkillInstanceId == frozenSource.SkillInstanceId) == 1 &&
                    V(g, 1).Skills!.All(s => s.Id != Congjian) && movement.Batch.ParentFrameId == loss.Id &&
                    movement.Batch.Movements is [var draw] && draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(1) &&
                    draw.Reason.Value == $"skill-program.{PrintedLoss}.ordered-printed-skill-loss.draw",
                    "A real ordered printed-skill loss removes the exact issued A instance, then its native one-card Draw owns the separate reacquisition child.");
                Frozen(loss.OrderedPrintedSkillLoss.Lease.LostSkillIds); Frozen(loss.OrderedPrintedSkillLoss.Lease.RemovedGrants);
                Private(g); g = Cold(g, registry); Step(g);
            }
            Reach(g, IsComplete); g = Cold(g, registry);
            Require(E<CompletedUndamagedUseDamageRecordedEvent>(g).Count(e => e.CardUseFrameId == use.Id && e.CardActionId == use.Action.ActionId &&
                    e.TargetSeat == 1 && e.Amount == 1) == 1 &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 2 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
                V(g, 1).Hp == 5 && (scenario != Scenario.SourceLoss || V(g, 1).Skills!.All(s => s.Id != Congjian)),
                "Exactly this native use caused the joined owner's real damage, and the source-loss variant has already lost its active qualification.");
            if (scenario == Scenario.Reacquired)
                Require(E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 1 && e.SourceSkillId == Reacquire && e.SkillIds.Contains(Congjian)) == 1 &&
                    V(g, 1).SkillRuntimeStates!.Single(s => s.SkillId == Congjian).IsAcquired &&
                    frozenSource.SkillInstanceId != $"acquired:{Reacquire}:{Congjian}",
                    "The native grant restores a genuinely different active B source before completion, while the owed benefit still belongs to removed A.");
            if (scenario is Scenario.Damage or Scenario.Reacquired)
            {
                Continue(g); Reach(g, p => p?.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 1);
                var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.JoinedTrickDamageRewardReceipt is not null);
                var receipt = parent.JoinedTrickDamageRewardReceipt!;
                var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == Gain);
                var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Id == child.WindowContext!.ParentFrameId);
                var issued = E<JoinedTrickDamageRewardDrawIssuedEvent>(g).Single(e => e.ProgramFrameId == parent.Id);
                Require(parent.OwnerSeat == 1 && parent.SkillId == Congjian && receipt.FrozenDrawCount == 2 && receipt.DrawActual == 2 &&
                    receipt.Benefit.CardUseFrameId == use.Id && receipt.Benefit.ActionId == use.Action.ActionId &&
                    issued.Benefit == receipt.Benefit && issued.FrozenDrawCount == 2 && issued.DrawActual == 2 &&
                    issued.MovementSequenceBefore == receipt.MovementSequenceBefore && issued.MovementSequenceAfter == receipt.MovementSequenceAfter &&
                    movement.Batch.ParentFrameId == parent.Id && movement.Batch.OriginOwnerSeat == 1 && movement.Batch.OriginSkillId == Congjian &&
                    movement.Batch.OriginSkillInstanceId == parent.SkillInstanceId && movement.Batch.Movements is [var draw] &&
                    draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(1) && draw.Reason.Value == RewardReason,
                    "The two-card reward freezes one original receipt and suspends on a real one-entity native draw child with its exact parent and source.");
                Frozen(movement.Batch.Movements); Private(g); g = Cold(g, registry);
            }
            Reach(g, IsPlay);
            Require(V(g, 1).HandCount == beforeHand + (scenario == Scenario.Reacquired ? 3 : 2) &&
                g.CardMovements.Count(m => m.To == CardLocation.Hand(1) && m.From == CardLocation.DrawPile && m.Reason.Value == RewardReason) == 2 &&
                E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Congjian && e.Window == SkillProgramTriggerWindow.CardUseCompleted) == 1 &&
                E<ProgramBindingStartedEvent>(g).Single(e => e.SkillId == Congjian && e.Window == SkillProgramTriggerWindow.CardUseCompleted).SkillInstanceId == frozenSource.SkillInstanceId &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Congjian && e.Window == SkillProgramTriggerWindow.CardUseCompleted && e.Completed) == 1 &&
                E<JoinedTrickDamageRewardDrawIssuedEvent>(g).Count(e => e.Benefit.CardUseFrameId == use.Id && e.Benefit.ActionId == use.Action.ActionId &&
                    e.Benefit.Source.OwnerSeat == 1 && e.FrozenDrawCount == 2 && e.DrawActual == 2) == 1 &&
                E<JoinedTrickDamageRewardResolvedEvent>(g).Count(e => e.Benefit.CardUseFrameId == use.Id && e.Benefit.ActionId == use.Action.ActionId &&
                    e.Benefit.Source.OwnerSeat == 1 && e.FrozenDrawCount == 2 && e.DrawActual == 2) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id && e.CardKind == CardKind.Duel) == 1,
                "Paid native draw children and source loss return one exact two-entity reward and the original Duel once.");
            if (scenario is Scenario.Damage or Scenario.Reacquired)
                Require(E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Gain) == 2,
                    "Both physical reward entities own their separate retained native draw-child batch.");
            _ = Cold(g, registry);
        }
    }

    public static void JoinedTrickQualificationsAndNullificationCannotBorrowOtherDamage()
    {
        RejectUnsupportedContracts();
        var (g, registry) = Start(Scenario.DrawTwo);
        var action = Action(g, LegalActionKind.DrawTwo); Play(g, action);
        var use = NativeJoin(ref g, registry, CardKind.DrawTwo);
        Reach(g, IsComplete); g = Cold(g, registry);
        Require(use.TargetSeats.SequenceEqual([0, 1]) && V(g, 1).HandCount == 6 &&
            E<CompletedUndamagedUseDamageRecordedEvent>(g).All(e => e.CardUseFrameId != use.Id) &&
            !g.CardMovements.Any(m => m.Reason.Value == RewardReason),
            "A real beneficial self DrawTwo accepts the other owner's native join and resolves both targets without inventing a damage reward.");
        Reach(g, IsPlay); AssertFinished(g, use.Id, action.CardId!.Value, CardKind.DrawTwo);

        (g, registry) = Start(Scenario.HpTie);
        Require(Enumerable.Range(0, 4).Select(s => V(g, s).Hp).Distinct().Count() == 1,
            "The negative fixture begins with a real public HP tie, including the Lord's ordinary HP bonus.");
        action = Action(g, LegalActionKind.DrawTwo); Play(g, action); Reach(g, IsComplete);
        Require(E<ProgramBindingStartedEvent>(g).All(e => e.SkillId != Congjian),
            "A tied maximum-HP target does not produce a Congjian join or consume an activation.");
        Reach(g, IsPlay); _ = Cold(g, registry);

        (g, registry) = Start(Scenario.Multiple);
        action = Action(g, LegalActionKind.IronChain, [0, 2]); Play(g, action); Reach(g, IsComplete);
        Require(E<ProgramBindingStartedEvent>(g).All(e => e.SkillId != Congjian),
            "Two actual original targets exclude Congjian even when one is the unique highest-HP character.");
        Reach(g, IsPlay); action = Action(g, LegalActionKind.Recast); Play(g, action); Reach(g, IsPlay);
        Require(E<CardRecastEvent>(g).Any(e => e.ActorSeat == 0 && e.CardId == action.CardId) &&
            E<ProgramBindingStartedEvent>(g).All(e => e.SkillId != Congjian),
            "A real targetless recast does not become an ordinary-trick designated-target use.");

        (g, registry) = Start(Scenario.Counterspell);
        Use(g, "damage", [1]); Reach(g, IsPlay);
        Require(E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1,
            "The joined owner has genuine earlier damage that cannot be borrowed by a later use's reward.");
        Use(g, "foreign-duel", [2]); use = NativeJoin(ref g, registry, CardKind.Duel);
        Reach(g, p => p is { Kind: DecisionKind.Nullification, PlayerSeat: 0, TargetSeat: 1 });
        var nullification = P(g)!.Choices.First(c => c.Cards.Count == 1);
        var material = nullification.Cards.Single(); g = Cold(g, registry);
        Answer(g, c => c.Id == nullification.Id); Reach(g, IsComplete); g = Cold(g, registry);
        Require(E<NullificationRespondedEvent>(g).Count(e => e.ResolutionId == use.Id && e.ResponderSeat == 0 &&
                    e.NullificationCardId == material && e.EffectNullified) == 1 &&
            E<NullificationResolvedEvent>(g).Any(e => e.ResolutionId == use.Id && e.EffectNullified) &&
            E<CompletedUndamagedUseDamageRecordedEvent>(g).All(e => e.CardUseFrameId != use.Id || e.TargetSeat != 1),
            "A real published unrespondable Nullification pays once and prevents this use's joined-target damage, independently of earlier real damage.");
        Reach(g, IsPlay);
        Require(!g.CardMovements.Any(m => m.Reason.Value == RewardReason) &&
            E<ProgramBindingStartedEvent>(g).All(e => e.SkillId != Congjian || e.Window != SkillProgramTriggerWindow.CardUseCompleted),
            "No completion reward is issued for a nullified joined target or for another action's damage history.");
        _ = Cold(g, registry);
    }

    private static void RejectUnsupportedContracts()
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        using var rr = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-xin-chang.rules.json")!);
        using var pp = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-xin-chang.presentation.json")!);
        var rules = rr.ReadToEnd(); var presentation = pp.ReadToEnd();
        void RejectShape(string label, string skill, int index, Action<JsonObject> change)
        {
            var root = JsonNode.Parse(rules)!.AsObject();
            var trigger = root["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == skill)!["triggers"]![index]!.AsObject();
            change(trigger);
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The shared unique-leader contracts reject " + label);
        }
        RejectShape("a non-finalized addition", Canmou, 0, t => t["window"] = "cardUseCommitted");
        RejectShape("an actor-only relation replacing its observer", Canmou, 0, t => t["ownerRelation"] = "actor");
        RejectShape("pure response inclusion", Congjian, 0, t => t["includeResponseUses"] = true);
        RejectShape("a forged configurable reward amount", Congjian, 1, t => t["effects"]![0]!["amount"] = 2);
        var missingJoin = JsonNode.Parse(rules)!.AsObject();
        missingJoin["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Congjian)!["triggers"]!.AsArray().RemoveAt(0);
        var missingRejected = false;
        try { _ = SkillProgramCatalog.Load(missingJoin.ToJsonString(), presentation); }
        catch (InvalidOperationException) { missingRejected = true; }
        Require(missingRejected, "A same-skill completion reward cannot exist without its exact finalized join producer.");
    }

    private static CardUseFrame BeginHumanAddition(ref GameEngine g, ContentRegistry registry, LegalAction? action = null)
    {
        if (action is not null) { Play(g, action); Reach(g, p => Offer(p, Canmou, 0)); }
        Private(g); g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, IsAddition);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.UniqueLeaderTrickTargetDraft is not null);
        var draft = parent.UniqueLeaderTrickTargetDraft!;
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == draft.CardUseFrameId);
        Require(parent.OwnerSeat == 0 && parent.SkillId == Canmou && draft.ActionId == use.Action!.ActionId &&
            draft.InstructionIndex == parent.InstructionIndex && draft.GameplayHash == parent.GameplayHash &&
            draft.Source == new CardConversionSource(Canmou, parent.TriggerId!, 0, parent.SkillInstanceId) &&
            draft.OriginalTargetSeats.SequenceEqual(use.TargetSeats) && draft.Choices.Count == P(g)!.Choices.Count,
            "One exact original use/action, instruction, source instance and gameplay hash own the public addition draft.");
        var offered = E<UniqueLeaderTrickTargetOfferedEvent>(g).Single(e => e.ProgramFrameId == parent.Id);
        Require(offered.CardUseFrameId == use.Id && offered.ActionId == use.Action!.ActionId && offered.Source == draft.Source &&
            offered.GameplayHash == draft.GameplayHash && offered.InstructionIndex == parent.InstructionIndex &&
            offered.Operation == SkillProgramEffectOp.OfferUniqueLargestHandTrickTargetAddition && offered.OriginalTargetSeats.SequenceEqual(use.TargetSeats),
            "One committed offer freezes the exact program, source, operation and original native target list.");
        Frozen(offered.OriginalTargetSeats); Frozen(offered.CandidateTargetSeats);
        Frozen(draft.OriginalTargetSeats); Frozen(draft.Choices); Frozen(P(g)!.Choices);
        foreach (var choice in P(g)!.Choices) { Frozen(choice.Targets); Require(choice.Cards.Count == 0, "Public target choices expose no foreign Hand identities."); }
        Reject(g, new AnswerPromptCommand(1, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, new ChoiceId("xin-chang:not-published"), g.Revision));
        g = Cold(g, registry); return use;
    }

    private static void AddHuman(ref GameEngine g, ContentRegistry registry, CardUseFrame use, IReadOnlyList<int> targets)
    {
        var old = P(g)!; var oldRevision = g.Revision; var choice = old.Choices.Single(c => c.Targets.SequenceEqual(targets));
        var retained = g.CreateSnapshot(2); var retainedJson = SnapshotJson.Serialize(retained);
        Answer(g, c => c.Id == choice.Id);
        var changed = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == use.Id);
        var settled = E<UniqueLeaderTrickTargetResolvedEvent>(g).Single(e => e.CardUseFrameId == use.Id);
        Require(changed.Action!.ActionId == use.Action!.ActionId && changed.Action.EffectiveSuit == use.Action.EffectiveSuit &&
            changed.TargetSeats.SequenceEqual(use.TargetSeats.Concat(targets)) &&
            changed.Action.TargetSeats.SequenceEqual(changed.TargetSeats) && settled.ActionId == use.Action.ActionId &&
            settled.OriginalTargetSeats.SequenceEqual(use.TargetSeats) && settled.AddedTargetSeats.SequenceEqual(targets) &&
            settled.ResultTargetSeats.SequenceEqual(changed.TargetSeats), "One published choice changes only the exact native use's ordered target list.");
        Frozen(settled.OriginalTargetSeats); Frozen(settled.AddedTargetSeats); Frozen(settled.ResultTargetSeats);
        Require(SnapshotJson.Serialize(retained) == retainedJson, "An already prepared private viewer snapshot cannot be changed by an accepted target addition.");
        Frozen(changed.TargetSeats); Reject(g, new AnswerPromptCommand(0, old.PromptId, choice.Id, oldRevision)); g = Cold(g, registry);
    }

    private static CardUseFrame NativeJoin(ref GameEngine g, ContentRegistry registry, CardKind kind)
    {
        Reach(g, p => Offer(p, Congjian, 1));
        var publicPlayers = g.CreateSnapshot(0).Players;
        Require(publicPlayers.Single(p => p.Seat == 0).Hp > publicPlayers.Where(p => p.Seat != 0).Max(p => p.Hp),
            "The original target is publicly the unique highest-HP other character.");
        Private(g); g = Cold(g, registry);
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
        Step(g); Reach(g, p => IsAddition(p) && p!.PlayerSeat == 1);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.UniqueLeaderTrickTargetDraft is not null);
        var draft = parent.UniqueLeaderTrickTargetDraft!;
        Require(parent.OwnerSeat == 1 && parent.SkillId == Congjian && draft.OriginalTargetSeats.SequenceEqual([0]) &&
            P(g)!.Choices.Any(c => c.Targets.SequenceEqual([1])) && P(g)!.Choices.All(c => c.Cards.Count == 0),
            "The real non-human owner can only join itself to the exact single original target, without private Hand leakage.");
        Frozen(draft.OriginalTargetSeats); Frozen(draft.Choices); g = Cold(g, registry); Step(g);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == draft.CardUseFrameId);
        Require(use.CardKind == kind && use.Action!.ActionId == draft.ActionId && use.TargetSeats.SequenceEqual([0, 1]) &&
            use.JoinedTrickDamageBenefits is { Count: 1 }, "One native AI choice appends itself and freezes exactly one same-use damage benefit: " + Diagnostic(g));
        var benefit = use.JoinedTrickDamageBenefits!.Single();
        var qualified = E<UniqueLeaderTrickQualificationEvent>(g).Single(e => e.CardUseFrameId == use.Id);
        var resolved = E<UniqueLeaderTrickTargetResolvedEvent>(g).Single(e => e.ProgramFrameId == parent.Id);
        Require(benefit.ProgramFrameId == parent.Id && benefit.CardUseFrameId == use.Id && benefit.ActionId == use.Action!.ActionId &&
            benefit.Source == draft.Source && benefit.GameplayHash == draft.GameplayHash &&
            E<JoinedTrickDamageBenefitIssuedEvent>(g).Count(e => e.Benefit == benefit) == 1 &&
            qualified.ActionId == use.Action.ActionId && qualified.UniqueLargestHpSeat == 0 &&
            qualified.OriginalPrimaryTargetCount == 1 && qualified.OriginalSinglePrimaryTargetSeat == 0 &&
            resolved.CardUseFrameId == use.Id && resolved.ActionId == use.Action.ActionId &&
            resolved.OriginalTargetSeats.SequenceEqual([0]) && resolved.AddedTargetSeats.SequenceEqual([1]) && resolved.ResultTargetSeats.SequenceEqual([0, 1]),
            "The accepted join freezes original unique-HP qualification, the exact same-use source and one immutable benefit rather than a general future damage reward.");
        Frozen(resolved.OriginalTargetSeats); Frozen(resolved.AddedTargetSeats); Frozen(resolved.ResultTargetSeats);
        Frozen(use.JoinedTrickDamageBenefits!); g = Cold(g, registry); return use;
    }

    private static (GameEngine, ContentRegistry) Start(Scenario scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, IsPlay);
        if (scenario is Scenario.Hand or Scenario.HandDrawTwo or Scenario.Snatch)
        {
            Use(g, "draw-three", []); Reach(g, IsPlay);
        }
        Require(V(g, 1).GeneralId == "fixture:xin-chang-peer-1", "Verified Seed31 and ordinary selection weights fix the native observer at seat one.");
        Private(g); return (g, registry);
    }

    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool IsPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool IsComplete(PendingDecision? p) => p?.SkillPrompt?.SkillId == Complete && p.PlayerSeat == 0;
    private static bool Offer(PendingDecision? p, string skill, int owner) => p?.SkillPrompt?.SkillId == skill && p.PlayerSeat == owner &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool IsAddition(PendingDecision? p) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "unique-leader-trick-target") == true;
    private static LegalAction Action(GameEngine g, LegalActionKind kind, IReadOnlyList<int>? targets = null) =>
        g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == kind && (targets is null || a.TargetSeats.SequenceEqual(targets))) ??
        throw new InvalidOperationException("Missing actual legal " + kind + " " + JsonSerializer.Serialize(targets) + ": " + Diagnostic(g));
    private static void Play(GameEngine g, LegalAction a)
    {
        if (a.Kind == LegalActionKind.Recast) Accept(g, new RecastCardCommand(0, a.CardId!.Value, g.Revision, P(g)!.PromptId) { ConversionSource = a.ConversionSource });
        else Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    private static void Use(GameEngine g, string activation, IReadOnlyList<int> targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    {
        var p = P(g)!; Require(p.PlayerSeat == 0, "Only the genuine human chooser may submit AnswerPrompt.");
        var choice = p.Choices.FirstOrDefault(match) ?? throw new InvalidOperationException("Missing published native choice: " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> ready)
    {
        for (var i = 0; i < 360; i++)
        {
            if (ready(P(g))) return;
            Require(!IsPlay(P(g)), "The actual Xin Chang operation returned to human Play before its required boundary: " + Diagnostic(g));
            Step(g);
        }
        throw new InvalidOperationException("The bounded native Xin Chang fixture missed its boundary: " + Diagnostic(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not { PlayerSeat: 0 }) { Accept(g, new AdvanceOneStepCommand(g.Revision)); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) { Continue(g); return; }
        if (p.Kind is DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash)
        { Answer(g, c => c.Cards.Count == 0); return; }
        if (p.Kind == DecisionKind.SelectTargetCard) { Answer(g, _ => true); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
        { Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); return; }
        throw new InvalidOperationException("Unexpected genuine human Xin Chang boundary: " + Diagnostic(g));
    }
    private static void AssertFinished(GameEngine g, long useId, int card, CardKind kind)
    {
        var finish = kind == CardKind.IronChain ? CardMoveReasons.IronChainFinished : CardMoveReasons.UseFinished;
        Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId && e.CardId == card && e.CardKind == kind) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            g.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == finish) == 1 &&
            !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == useId), "One original material pays once, finishes once and leaves no suspended native use.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "A real Xin Chang command was rejected.");
    }
    private static void Reject(GameEngine g, GameCommand command)
    {
        var before = State(g); Require(!g.Submit(command).Accepted && State(g) == before,
            "A wrong actor, forged choice or stale response must reject atomically without changing the original use.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), g.CardMovements,
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves exact native use/action, frozen targets, private views, paid material and typed child returns."); return copy;
    }
    private static void Private(GameEngine g)
    {
        foreach (var viewer in Enumerable.Range(0, 4)) foreach (var player in g.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer))
            Require(player.Hand.Count == 0, "No public target prompt or prepared viewer snapshot exposes a foreign Hand entity.");
    }
    private static void Frozen<T>(IReadOnlyList<T> list)
    {
        if (list is not IList<T> writable) return;
        var rejected = false;
        try { if (writable.Count > 0) writable[0] = writable[0]; else writable.Add(default!); } catch (NotSupportedException) { rejected = true; }
        Require(rejected, "Prepared targets, choices, benefits and native movement collections are immutable.");
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        Prompt = P(g), Players = g.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.MaxHp, p.HandCount, p.Equipment, p.Skills }),
        Hand = V(g, 0).Hand, Legal = g.GetHumanLegalActions(),
        Frames = g.ResolutionStack.Select(f => new { f.Id, f.Kind, Type = f.GetType().Name }),
        Movements = g.CardMovements.TakeLast(12),
        Facts = g.Events.TakeLast(12).Select(e => new { e.Sequence, Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) })
    });
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-xin-chang", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            if (scenario == Scenario.BorrowedSword)
                b.AddCard(StandardContentRegistry.CreateWithClassicGenerals().GetCard("classic:borrowed-sword"));
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rr = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-xin-chang.rules.json")!);
            using var pp = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-xin-chang.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rr.ReadToEnd(), pp.ReadToEnd());
            foreach (var (id, program) in formal.Programs) b.AddSkill(new(id, formal.Presentations[id].Name, formal.Presentations[id].Description)
                { Program = program, ProgramPresentation = formal.Presentations[id] });
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:xin-chang-driver","revision":1,"activations":[
                {"id":"tie-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]},
                {"id":"boost-hand","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":3}]},
                {"id":"draw-three","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]},
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"foreign-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}]},
              {"id":"fixture:xin-chang-complete","revision":1,"triggers":[{"id":"native-completion","window":"cardUseCompleted","ownerRelation":"observer","singleActionInstance":true,"cardCategories":["instantTrick"],"priority":100,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:xin-chang-gain","revision":1,"triggers":[{"id":"native-reward-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.joined-trick.damage-reward"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:xin-chang-acquire","revision":1,"triggers":[{"id":"real-source","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:congjian"]}]}]},
              {"id":"fixture:xin-chang-unrespondable","revision":1,"cardPolicies":[{"id":"true-counterspell","kind":"unrespondableNullification","cardKinds":["nullification"]}]},
              {"id":"fixture:xin-chang-printed-loss","revision":1,"triggers":[{"id":"real-printed-loss","window":"afterDamageApplied","subject":"any","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"loseFirstPrintedSkillsUntilTurnEndAndDraw","target":"owner"}]}]},
              {"id":"fixture:xin-chang-reacquire","revision":1,"triggers":[{"id":"native-regain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fixture:xin-chang-printed-loss.ordered-printed-skill-loss.draw"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["ol:congjian"]}]}]}]}
            """)!.AsObject();
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var skills = rules["skills"]!.AsArray();
            var labels = skills.ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var d = new Dictionary<string, object> { ["name"] = id, ["description"] = "小实体原生指定目标和完成收益" };
                if (id is Complete or Gain or Reacquire) d["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)d;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实共享机制夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Suppress, "HP5真实失源", "已冻结原用牌权益") { SuppressionRule = new(5) });
            b.AddSkill(new("fixture:xin-chang-first", "固定首位", "已验证Seed31真实选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:xin-chang-peer", "固定其他角色", "已验证Seed31真实选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            var hand = scenario is Scenario.Hand or Scenario.HandDrawTwo or Scenario.Snatch or Scenario.BorrowedSword;
            var own = new List<string> { Complete };
            if (hand) own.Add(Canmou);
            if (scenario == Scenario.Counterspell) own.Add("fixture:xin-chang-unrespondable");
            b.AddGeneral(new(Owner, "真实锦囊拥有者", "supporter", Driver, "jin", scenario == Scenario.HpTie ? 5 : 8, own));
            for (var index = 1; index < 4; index++)
            {
                var extra = new List<string>();
                if (index == 1 && !hand)
                {
                    extra.Add(Gain);
                    if (scenario == Scenario.Reacquired) { extra.Add("fixture:xin-chang-first"); extra.Add(PrintedLoss); extra.Add(Reacquire); }
                    else extra.Add(Acquire);
                    if (scenario == Scenario.SourceLoss) extra.Add(Suppress);
                }
                var primary = index == 1 ? scenario == Scenario.Reacquired ? Congjian : "fixture:xin-chang-first" : "fixture:xin-chang-peer";
                b.AddGeneral(new($"fixture:xin-chang-peer-{index}", "原生其他角色", "supporter", primary, "wei", 6, extra));
            }
            var cards = scenario switch
            {
                Scenario.BorrowedSword => new[] { "standard:crossbow", "classic:borrowed-sword", "standard:dodge" },
                Scenario.Snatch => new[] { "standard:snatch" },
                Scenario.Damage or Scenario.SourceLoss or Scenario.Reacquired => new[] { "standard:duel" },
                Scenario.HandDrawTwo or Scenario.DrawTwo or Scenario.HpTie => new[] { "standard:draw_two" },
                Scenario.Counterspell => new[] { "standard:nullification" },
                _ => new[] { "standard:iron_chain" }
            };
            // The compound-pair fixture is deliberately Slash-less and counterspell-free.
            b.AddDeck(new("fixture:xin-chang-deck", "固定真实小牌库", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(cards[i % cards.Length], Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "真实唯一领先者锦囊扩展", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:xin-chang-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, "fixture:xin-chang-peer-1", "fixture:xin-chang-peer-2", "fixture:xin-chang-peer-3"]));
        }
    }
}
