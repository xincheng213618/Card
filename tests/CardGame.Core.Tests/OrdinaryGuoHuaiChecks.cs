using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryGuoHuaiChecks
{
    private const string Zhefu = "ol:zhefu", Yidu = "ol:yidu", Driver = "fixture:guo-huai-driver";
    private const string Loss = "fixture:guo-huai-loss", DamageLoss = "fixture:guo-huai-damage-loss";
    private const string Child = "fixture:guo-huai-discard-child", Suppress = "fixture:guo-huai-suppress", Mode = "fixture:guo-huai-mode";
    private const string ActiveRequest = "fixture:guo-huai-active-request";

    public static void OutsideTurnActualUseAndOwnTurnExclusion()
    {
        var (g, r) = Create(CardKind.FireSlash);
        Use(g, "request", [1]);
        Reach(g, p => p.PlayerSeat == 1 && Has(p, "request-slash"));
        var material = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash").Cards.Single();
        Answer(g, c => c.Cards.SequenceEqual([material]));
        Reach(g, p => Offer(p, Zhefu, 1));
        var use = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Use).Action;
        Require(use.EffectiveKind == CardKind.FireSlash && use.PhysicalCards is [{ CardId: var id }] && id == material &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 1 && e.TargetSeat == 0) &&
            !E<SameNameHandStartedEvent>(g).Any(),
            "A foreign actual-turn forced physical Fire Slash really resolves before its optional completed-Use demand is accepted.");
        Answer(g, Activate); Reach(g, p => Demand(p, "target"));
        var started = E<SameNameHandStartedEvent>(g).Single();
        Require(started.ActionId == use.ActionId && started.Source.SkillId == Zhefu && started.Source.OwnerSeat == 1 &&
            started.ActualTurnOwnerSeat == 0 && started.EffectiveKind == CardKind.FireSlash && started.NormalizedName == CardKind.Slash,
            "The complete use freezes its real effective name, source and actual foreign turn.");
        Private(g); g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => Demand(p, "discard"));
        var cost = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(g, c => c.Cards.SequenceEqual([cost])); Play(g);
        OnceDemand(g, started.FrameId, discarded: true);
        Require(g.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1,
            "The target actually discards one same-name Hand entity once through the native movement boundary.");

        var (own, _) = Create(CardKind.Slash);
        Convert(own, "slash", [1]); Play(own);
        Require(E<CardActionAcceptedEvent>(own).Any(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Use) &&
            !E<SameNameHandStartedEvent>(own).Any(e => e.Source.OwnerSeat == 0),
            "The same formal Zhefu owned by the current actor does not trigger during its own actual turn.");
    }

    public static void PureResponsesCompleteOnTheirNativeParents()
    {
        foreach (var direction in new[] { CardKind.Duel, CardKind.ArrowBarrage })
        {
            var (g, r) = Create(direction == CardKind.Duel ? CardKind.Slash : CardKind.Dodge);
            if (direction == CardKind.Duel)
            {
                Use(g, "hurt-target", [1]); Play(g);
                Require(V(g, 1).Hp == 2 && V(g, 1).MaxHp == 4 && E<ProgramSkillHpLostEvent>(g).Any(e => e.SkillId == Driver && e.TargetSeat == 1),
                    "The real target pays native HP loss2 before Duel; its legal AI response score now prefers the available Slash over passing.");
                Convert(g, "duel", [1]);
            }
            else Arrows(g);
            Reach(g, p => p.PlayerSeat == 1 && p.Kind == (direction == CardKind.Duel ? DecisionKind.RespondSlash : DecisionKind.RespondDodge));
            var p = P(g)!; var published = p.Choices.Where(c => c.Cards.Count == 1).Select(c => c.Cards.Single()).ToHashSet();
            var parent = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == direction);
            var parentId = parent.Id; var actionId = parent.Action!.ActionId;
            Require(!E<SameNameHandStartedEvent>(g).Any(), "Accepting the incoming card alone cannot pre-fire a completed response.");
            Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, q => Offer(q, Zhefu, 1));
            var response = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Response).Action;
            Require(response.ParentActionId == actionId && published.Contains(response.PhysicalCards.Single().CardId) && response.PhysicalCards.Single().From == CardLocation.Hand(1) &&
                response.EffectiveKind == (direction == CardKind.Duel ? CardKind.Slash : CardKind.Dodge),
                "The actual supplied Slash or group Dodge retains its native parent and remains a Response, without pretending it was a basic-card Use.");
            Answer(g, Activate); Reach(g, q => Demand(q, "target"));
            var started = E<SameNameHandStartedEvent>(g).Single();
            Require(started.ActionId == response.ActionId && started.OriginalParentFrameId == parentId,
                "The pure response completion demand owns precisely the original Duel or group-card parent.");
            g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, q => Demand(q, "discard"));
            Answer(g, c => c.Cards.Count == 1); Play(g); OnceDemand(g, started.FrameId, discarded: true);
            Require(E<SameNameHandStartedEvent>(g).Count(e => e.ActionId == response.ActionId) == 1,
                "The native response cursor resumes once, and the same completed pure response cannot issue a second demand.");
        }
    }

    public static void ResponseUsesCompleteOnlyOnce()
    {
        foreach (var direction in new[] { CardKind.Dodge, CardKind.Nullification })
        {
            var (g, r) = Create(direction);
            Convert(g, direction == CardKind.Dodge ? "slash" : "duel", [1]);
            Reach(g, p => p.PlayerSeat == 1 && p.Kind == (direction == CardKind.Dodge ? DecisionKind.RespondDodge : DecisionKind.Nullification));
            var published = P(g)!.Choices.Where(c => c.Cards.Count == 1).Select(c => c.Cards.Single()).ToHashSet();
            Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => Offer(p, Zhefu, 1));
            var response = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActorSeat == 1 && e.Action.EffectiveKind == direction).Action;
            Require(response is { Type: CardActionType.Response, RequesterSeat: null, ResponderSeat: 1, ProviderSeat: 1 } &&
                response.PhysicalCards is [{ From: var from, CardId: var actualCard }] && from == CardLocation.Hand(1) && published.Contains(actualCard),
                "The genuine own Slash defense or counterspell Use preserves its real Response action and provider.");
            Answer(g, Activate); Reach(g, p => Demand(p, "target"));
            var started = E<SameNameHandStartedEvent>(g).Single();
            Private(g); g = Cold(g, r); Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, p => Demand(p, "discard"));
            Answer(g, c => c.Cards.Count == 1); Play(g); OnceDemand(g, started.FrameId, discarded: true);
            Require(E<SameNameHandStartedEvent>(g).Count(e => e.ActionId == response.ActionId) == 1 &&
                !E<SameNameHandStartedEvent>(g).Any(e => e.Source.OwnerSeat == 0),
                "A response that is also a genuine Use triggers Zhefu once; the actor's own-turn card still does not trigger it.");
        }
    }

    public static void PaidDiscardAndNativeDamageDyingSurviveSourceLoss()
    {
        var (g, registry) = Create(CardKind.Dodge, sourceLoss: true);
        Require(V(g, 1).Skills!.Any(s => s.Id == Zhefu) && E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 1 && e.SourceSkillId == "fixture:guo-huai-initial-zhefu" && e.SkillIds.Contains(Zhefu)) == 1,
            "The source-loss fixture genuinely acquires its original Zhefu grant before any use or payment, rather than relying on a printed skill display.");
        Convert(g, "slash", [1]); Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => Offer(p, Zhefu, 1)); Answer(g, Activate);
        Reach(g, p => Demand(p, "target")); Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, p => Demand(p, "discard"));
        var cost = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); Answer(g, c => c.Cards.SequenceEqual([cost]));
        Reach(g, p => p.SkillPrompt?.SkillId == Loss && ContinueChoice(p));
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SameNameHandDiscardOrDamage is not null);
        var receipt = root.SameNameHandDiscardOrDamage!; var id = root.Id;
        var paid = E<SameNameHandDiscardPaidEvent>(g).Single();
        Require(receipt.Stage == SameNameHandStage.DiscardChildren && receipt.PaidMaterial?.CardId == cost && paid.FrameId == id &&
            g.CardMovements.Count(m => m.CardId == cost && m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter && m.To == CardLocation.DiscardPile) == 1 &&
            !E<SameNameHandCompletedEvent>(g).Any(),
            "The real same-name payment is committed before its native movement observer pauses; the owning receipt cannot re-pay it.");
        Private(g); g = Cold(g, registry); Continue(g); Play(g);
        Require(V(g, 1).Hp == 4 && E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 1 && e.SourceSkillId == Loss && e.SkillIds.Contains(Suppress)) &&
            !V(g, 1).Skills!.Any(s => s.Id == Zhefu), "The actual nested acquisition suppresses the accepted source's effective qualification.");
        OnceDemand(g, id, discarded: true);
        Require(E<SameNameHandDiscardPaidEvent>(g).Count(e => e.FrameId == id) == 1, "Paid source loss still returns the original completed discard exactly once.");

        var (dying, r) = Create(CardKind.Dodge, damageLoss: true);
        Require(V(dying, 1).Skills!.Any(s => s.Id == Zhefu) && E<SkillsAcquiredEvent>(dying).Count(e => e.PlayerSeat == 1 && e.SourceSkillId == "fixture:guo-huai-initial-zhefu" && e.SkillIds.Contains(Zhefu)) == 1,
            "The genuine acquired Zhefu instance will lose effective qualification during its issued damage child.");
        Use(dying, "hurt-self"); Play(dying); Require(V(dying, 0).Hp == 1, "The real Lord HP5 pays actual HP loss4 before the incoming demand.");
        Convert(dying, "slash", [1]); Reach(dying, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        Accept(dying, new AdvanceOneStepCommand(dying.Revision)); Reach(dying, p => Offer(p, Zhefu, 1)); Answer(dying, Activate);
        Reach(dying, p => Demand(p, "target")); Answer(dying, c => c.Targets.SequenceEqual([0])); Reach(dying, p => Demand(p, "damage"));
        Answer(dying, c => c.Parameters.GetValueOrDefault("step") == "damage");
        Reach(dying, p => p.SkillPrompt?.SkillId == DamageLoss && ContinueChoice(p));
        var damage = E<SameNameHandDamageIssuedEvent>(dying).Single();
        var damageOwner = dying.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == damage.FrameId);
        var beforeDamage = dying.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single();
        Require(damage is { SourceSeat: 1, TargetSeat: 0, Amount: 1 } && V(dying, 0).Hp == 1 &&
            damageOwner.SameNameHandDiscardOrDamage is { Stage: SameNameHandStage.DamageIssued, DamageIssued: true } &&
            damageOwner.AttackAttempt is { SourceSeat: 1, TargetSeat: 0, DamageAmount: 1, Nature: DamageNature.Normal, DamageWasApplied: false } &&
            beforeDamage.ParentFrameId == damage.FrameId && beforeDamage is { SourceSeat: 1, TargetSeat: 0, Amount: 1, Nature: DamageNature.Normal } &&
            dying.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == DamageLoss).WindowContext is
                { Window: SkillProgramTriggerWindow.BeforeDamageApplied, ParentFrameId: var beforeId } && beforeId == beforeDamage.Id &&
            !E<DamageAppliedEvent>(dying).Any() && !E<PlayerDyingEvent>(dying).Any(),
            "Declining the same-name discard issues the real attributed normal damage attempt; its exact native before-damage child pauses before HP loss.");
        Private(dying); dying = Cold(dying, r); Continue(dying);
        Reach(dying, p => p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        var dyingFrame = dying.ResolutionStack.OfType<DyingFrame>().Single();
        var damageFrame = dying.ResolutionStack.OfType<DamageFrame>().Single(f => f.Id == dyingFrame.ParentFrameId);
        Require(V(dying, 0).Hp == 0 && E<DamageAppliedEvent>(dying).Any(e => e is
                { SourceSeat: 1, TargetSeat: 0, Amount: 1, RemainingHp: 0, Nature: DamageNature.Normal }) &&
            damageFrame.ParentFrameId == damage.FrameId && damageFrame is { SourceSeat: 1, TargetSeat: 0, Amount: 1, Nature: DamageNature.Normal } &&
            dyingFrame is { VictimSeat: 0, KillerSeat: 1, Continuation: DyingContinuationKind.Damage } && dyingFrame.ResponderSeat == 0 &&
            E<DamageRequestedEvent>(dying).Count(e => e.ResolutionId == damageFrame.Id && e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1 && e.Nature == DamageNature.Normal) == 1 &&
            E<SkillsAcquiredEvent>(dying).Any(e => e.PlayerSeat == 1 && e.SourceSkillId == DamageLoss && e.SkillIds.Contains(Suppress)) &&
            !V(dying, 1).Skills!.Any(s => s.Id == Zhefu) && !E<SameNameHandCompletedEvent>(dying).Any(),
            "The damage source really loses qualification while its issued native damage still owes the actual Dying return.");
        var peach = P(dying)!.Choices.First(c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "peach" &&
            c.Parameters.GetValueOrDefault("conversion-skill-id") == Driver && c.Parameters.GetValueOrDefault("conversion-binding-id") == "peach").Cards.Single();
        Require(V(dying, 0).Hand.Any(c => c.Id == peach && c.Kind == CardKind.Dodge), "The actual self-rescue prompt publishes the Driver's legal real Hand Dodge-to-Peach source.");
        dying = Cold(dying, r); Answer(dying, c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "peach"); Play(dying);
        Require(V(dying, 0).IsAlive && V(dying, 0).Hp == 1 && !dying.ResolutionStack.OfType<DyingFrame>().Any(),
            "A real converted Peach rescues the original native Dying victim.");
        Require(E<DyingResponseEvent>(dying).Count(e => e.ResolutionId == dyingFrame.Id && e.ResponderSeat == 0 && e.UsedPeach && e.PeachCardId == peach && e.UsedPeachPhysicalCardKind == CardKind.Dodge) == 1,
            "The exact original Dying receives one real converted-entity Peach response after cold restore.");
        OnceDemand(dying, damage.FrameId, discarded: false);
        Require(E<SameNameHandDamageIssuedEvent>(dying).Count(e => e.FrameId == damage.FrameId) == 1,
            "Cold damage and Dying returns complete the original paid attempt once despite source loss.");
    }

    public static void UndamagedGroupTargetRevealSameColorAndColdReturn()
    {
        var (g, registry) = Create(CardKind.Dodge, yidu: true, emptySecond: true);
        Require(V(g, 2).HandCount == 0 && V(g, 1).HandCount == 5 && V(g, 3).HandCount == 4,
            "Legal initial-deal modifiers leave only target2 without a Dodge; no NPC response is manually forged.");
        Arrows(g); RespondGroup(g); Reach(g, p => Offer(p, Yidu, 0));
        var use = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.ArrowBarrage).Action;
        var damage = E<CompletedUndamagedUseDamageRecordedEvent>(g).Where(e => e.CardActionId == use.ActionId).ToArray();
        Require(damage is [{ TargetSeat: 2, Amount: 1 }] && E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 2),
            "The independent whole-use ledger records only the actually damaged group target, not its successfully defended siblings.");
        Answer(g, Activate); Reach(g, p => Reveal(p, "target"));
        var root = RevealRoot(g); var receipt = root.CompletedUndamagedTargetReveal!; var id = root.Id;
        Require(receipt.FinalTargets.ToHashSet().SetEquals([1, 2, 3]) && receipt.EligibleTargets.ToHashSet().SetEquals([1, 3]) &&
            P(g)!.Choices.All(c => !c.Targets.Contains(2)),
            "A multi-target damage trick offers every eligible undamaged target and excludes the target hurt by this exact use.");
        Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => Reveal(p, "slot"));
        root = RevealRoot(g); receipt = root.CompletedUndamagedTargetReveal!;
        var invoice = receipt.TargetHand.Take(3).ToArray();
        Require(invoice.Length == 3 && invoice.All(m => g.CreateCardZoneDiagnostics().Single(z => z.CardId == m.CardId).Location == CardLocation.Hand(1)),
            "Three frozen material slots identify true current target Hand entities after its real Dodge cost.");
        Blind(g); g = Cold(g, registry);
        foreach (var m in invoice) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "slot" && c.Parameters.GetValueOrDefault("slot-index") == m.Slot.ToString());
        Reach(g, p => p.SkillPrompt?.SkillId == Child && ContinueChoice(p));
        root = RevealRoot(g); receipt = root.CompletedUndamagedTargetReveal!;
        var shown = E<ProgramCardsRevealedEvent>(g).Single(e => e.FrameId == id);
        var movement = E<CompletedUndamagedTargetRevealMovementIssuedEvent>(g).Single();
        Require(receipt is { Stage: CompletedUndamagedTargetRevealStage.MovementChildren, SameColor: true, MovementIssued: true } &&
            receipt.RevealedMaterials.Select(m => m.CardId).SequenceEqual(invoice.Select(m => m.CardId)) && receipt.PaidMaterials.Count == 3 &&
            shown.Bind == "completed-undamaged-target-reveal" && shown.Cards.Select(c => c.Id).SequenceEqual(invoice.Select(m => m.CardId)) &&
            movement.FrameId == id && movement.ActualCount == 3 && g.CardMovements.Count(m => m.Sequence > movement.SequenceBefore && m.Sequence <= movement.SequenceAfter &&
                m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile) == 3 && !E<CompletedUndamagedTargetRevealCompletedEvent>(g).Any(),
            "The exact public reveal pays one genuine same-color three-card discard before its own native movement child returns.");
        Frozen(shown.Cards); Frozen(receipt.TargetHand); Frozen(receipt.SelectedSlots); Frozen(receipt.RevealedMaterials); Frozen(receipt.PaidMaterials);
        PublicReveal(g, shown.Cards, hiddenOwner: 1, receipt.TargetHand.Where(m => !invoice.Any(i => i.CardId == m.CardId)).Select(m => m.CardId).ToArray());
        Private(g); g = Cold(g, registry);
        PublicReveal(g, shown.Cards, hiddenOwner: 1, receipt.TargetHand.Where(m => !invoice.Any(i => i.CardId == m.CardId)).Select(m => m.CardId).ToArray());
        Continue(g); Play(g);
        Require(E<CompletedUndamagedTargetRevealCompletedEvent>(g).Single() == new CompletedUndamagedTargetRevealCompletedEvent(id, 3, 3) &&
            E<CompletedUndamagedTargetRevealMovementIssuedEvent>(g).Length == 1 && !g.ResolutionStack.Any(f => f.Id == id),
            "The native discard child cold-restores and completes exactly once without substituting or repeating the revealed entities.");
    }

    public static void OptionalZeroMixedAndTargetEffectiveColorReveals()
    {
        foreach (var selectedCount in new[] { 0, 1, 2 })
        {
            var mixed = selectedCount == 2;
            var (g, r) = Create(CardKind.Dodge, yidu: true, mixed: mixed);
            Convert(g, "slash", [1]); Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
            Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => Offer(p, Yidu, 0)); Answer(g, Activate);
            Reach(g, p => Reveal(p, "target")); Answer(g, c => c.Targets.SequenceEqual([1])); Reach(g, p => Reveal(p, "slot"));
            var root = RevealRoot(g); var invoice = root.CompletedUndamagedTargetReveal!.TargetHand; var id = root.Id;
            var hand = V(g, 1).Hand.ToDictionary(c => c.Id);
            if (mixed) Require(hand.Count == 21 && hand.Values.Select(c => c.Suit is Suit.Heart or Suit.Diamond).Distinct().Count() == 2,
                "The target's own legal view proves both real colors remain after its native Dodge; the source still receives only opaque slots.");
            var slots = mixed ? invoice.GroupBy(m => hand[m.CardId].Suit is Suit.Heart or Suit.Diamond).Select(group => group.First()).ToArray() : invoice.Take(selectedCount).ToArray();
            Require(slots.Length == selectedCount, "The fixed mixed fixture truly supplies two different target colors, without changing seed or assertions.");
            Blind(g); if (selectedCount == 0) g = Cold(g, r);
            foreach (var slot in slots) Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "slot" && c.Parameters.GetValueOrDefault("slot-index") == slot.Slot.ToString());
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish");
            if (selectedCount == 1) { Reach(g, p => p.SkillPrompt?.SkillId == Child && ContinueChoice(p)); Continue(g); }
            Play(g);
            Require(E<CompletedUndamagedTargetRevealCompletedEvent>(g).Single() == new CompletedUndamagedTargetRevealCompletedEvent(id, selectedCount, selectedCount == 1 ? 1 : 0) &&
                E<ProgramCardsRevealedEvent>(g).Count(e => e.FrameId == id) == (selectedCount == 0 ? 0 : 1) &&
                E<CompletedUndamagedTargetRevealMovementIssuedEvent>(g).Count(e => e.FrameId == id) == (selectedCount == 1 ? 1 : 0),
                "The same optional skill accepts zero with no empty reveal/payment, one same-color card with one real discard, and two mixed-color cards with no discard.");
            if (mixed) Require(slots.All(s => g.CreateCardZoneDiagnostics().Single(z => z.CardId == s.CardId).Location == CardLocation.Hand(1)),
                "Every mixed-color shown entity remains in the original target Hand.");
        }

        var (effective, _) = Create(CardKind.Dodge, yidu: true, mixed: true, targetRed: true);
        Convert(effective, "slash", [1]); Reach(effective, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        Accept(effective, new AdvanceOneStepCommand(effective.Revision)); Reach(effective, p => Offer(p, Yidu, 0)); Answer(effective, Activate);
        Reach(effective, p => Reveal(p, "target")); Answer(effective, c => c.Targets.SequenceEqual([1])); Reach(effective, p => Reveal(p, "slot"));
        var owning = RevealRoot(effective); var effectiveHand = V(effective, 1).Hand.ToDictionary(c => c.Id);
        var selected = owning.CompletedUndamagedTargetReveal!.TargetHand.GroupBy(m => effectiveHand[m.CardId].Suit is Suit.Heart or Suit.Diamond).Select(group => group.First()).ToArray(); var owningId = owning.Id;
        Require(selected.Length == 2, "The fixed effective-color fixture starts from two different printed colors before the target's rewrite unifies them.");
        foreach (var slot in selected) Answer(effective, c => c.Parameters.GetValueOrDefault("branch") == "slot" && c.Parameters.GetValueOrDefault("slot-index") == slot.Slot.ToString());
        Answer(effective, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Reach(effective, p => p.SkillPrompt?.SkillId == Child && ContinueChoice(p));
        Require(RevealRoot(effective).CompletedUndamagedTargetReveal!.RevealedMaterials.All(m => m.EffectiveColor == CardColor.Red),
            "Color comparison freezes the target's actual rewritten appearance, rather than the source's or raw printed suit.");
        Continue(effective); Play(effective);
        Require(E<CompletedUndamagedTargetRevealCompletedEvent>(effective).Single() == new CompletedUndamagedTargetRevealCompletedEvent(owningId, 2, 2),
            "Both target-effective red entities are really discarded by the complete native skill.");
    }

    public static void FactionProvidersOwnTheirCompletedResponseExactlyOnce()
    {
        foreach (var required in new[] { CardKind.Dodge, CardKind.Slash })
        foreach (var lordHasZhefu in new[] { false, true })
        {
            var (g, registry) = Create(required, providerDirection: required, lordHasZhefu: lordHasZhefu);
            var lord = g.CreateSnapshot(0).Players.Single(p => p.Role == Role.Lord).Seat;
            var provider = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:guo-huai-peer-1").Seat;
            Require(lord != 0 && provider != lord && provider != 0 && V(g, lord).HandCount == 0 && V(g, provider).HandCount == 4 &&
                V(g, provider).Skills!.Any(s => s.Id == Zhefu) && V(g, lord).Skills!.Any(s => s.Id == Zhefu) == lordHasZhefu,
                "Real role-weighted selection preserves an outside-turn native Lord and a distinct qualified provider; only the provider owns Zhefu in the first branch.");
            if (required == CardKind.Dodge) Arrows(g); else Convert(g, "duel", [lord]);
            var responseKind = required == CardKind.Dodge ? DecisionKind.RespondDodge : DecisionKind.RespondSlash;
            var requestMarker = required == CardKind.Dodge ? "faction-defense-request" : "faction-slash-request";
            var supplyMarker = required == CardKind.Dodge ? "faction-defense-dodge" : "faction-slash-slash";
            Reach(g, p => p.PlayerSeat == lord && p.Kind == responseKind && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == requestMarker));
            var parent = g.ResolutionStack.OfType<CardUseFrame>().Last(f => f.CardKind == (required == CardKind.Dodge ? CardKind.ArrowBarrage : CardKind.Duel));
            var parentId = parent.Id; var parentAction = parent.Action!.ActionId;
            Accept(g, new AdvanceOneStepCommand(g.Revision));
            Reach(g, p => p.PlayerSeat == provider && p.Kind == responseKind && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == supplyMarker));
            var published = P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("response") == supplyMarker).SelectMany(c => c.Cards).ToHashSet();
            g = Cold(g, registry);
            Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => Offer(p, Zhefu, provider));
            var completion = E<CardResponseCompletedEvent>(g).Single(e => e.NativeActorSeat == lord && e.ProviderSeat == provider && e.ParentFrameId == parentId);
            var native = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == completion.ActionId).Action;
            Require(completion.ActorSeat == provider && completion.EffectiveKind == required && completion.ActualTurnOwnerSeat == 0 &&
                native.ActorSeat == lord && native.ProviderSeat == provider && native.RequesterSeat == lord && native.ResponderSeat == lord &&
                native.Type == CardActionType.Response && native.ParentActionId == parentAction &&
                native.PhysicalCards is [{ CardId: var card, From: var from }] && published.Contains(card) && from == CardLocation.Hand(provider) &&
                E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == completion.ActionId) == 1,
                "Native assistance retains the original requester action and real provider material; the completed-response actor belongs to the supplier without creating a second accepted action.");
            Answer(g, Activate); Reach(g, p => Demand(p, "target"));
            var started = E<SameNameHandStartedEvent>(g).Single(e => e.ActionId == completion.ActionId);
            Require(started.Source.OwnerSeat == provider && started.OriginalParentFrameId == parentId && started.NormalizedName == required,
                "The exact provider source, effective name and original incoming parent own the issued Zhefu demand.");
            Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, p => Demand(p, "discard"));
            Answer(g, c => c.Cards.Count == 1); Play(g); OnceDemand(g, started.FrameId, discarded: true);
            Require(E<SameNameHandStartedEvent>(g).Count(e => e.ActionId == completion.ActionId) == 1 &&
                !E<SameNameHandStartedEvent>(g).Any(e => e.ActionId == completion.ActionId && e.Source.OwnerSeat == lord),
                "The supplied physical response triggers once for its provider, including when the foreign-turn Lord also owns Zhefu; it cannot be attributed to both roles.");
        }
    }

    public static void ActiveFactionSupplyCompletesBeforePrincipalUseWithoutFakeResponse()
    {
        foreach (var lordHasZhefu in new[] { false, true })
        {
            var (g, registry) = Create(CardKind.Slash, providerDirection: CardKind.Slash, lordHasZhefu: lordHasZhefu, activeSupply: true);
            var lord = g.CreateSnapshot(0).Players.Single(p => p.Role == Role.Lord).Seat;
            var provider = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:guo-huai-peer-1").Seat;
            var originalHp = V(g, 0).Hp;
            Require(V(g, lord).HandCount == 0 && V(g, provider).HandCount == 4 && g.CreateSnapshot(0).CurrentSeat == 0,
                "The real empty-Hand Lord and distinct provider are both outside the current human0 actual turn.");
            Use(g, "assisted", [lord]);
            Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") == "target"));
            Answer(g, c => c.Targets.SequenceEqual([0]));
            Reach(g, p => p.PlayerSeat == lord && p.Choices.Any(c => c.Parameters.GetValueOrDefault("request-option") == "faction"));
            Answer(g, c => c.Parameters.GetValueOrDefault("request-option") == "faction");
            Reach(g, p => p.PlayerSeat == provider && p.Kind == DecisionKind.RespondSlash && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash"));
            var published = P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("response") == "faction-slash-slash").SelectMany(c => c.Cards).ToHashSet();
            Accept(g, new AdvanceOneStepCommand(g.Revision)); Reach(g, p => Offer(p, Zhefu, provider));
            var completion = E<CardSupplyCompletedEvent>(g).Single();
            var native = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == completion.ParentFrameId);
            var accepted = native.Action!;
            var declared = E<CardUseDeclaredEvent>(g).Single(e => e.ResolutionId == native.Id);
            Require(completion is { EffectiveKind: CardKind.Slash, ActualTurnOwnerSeat: 0 } && completion.RequesterSeat == lord && completion.ProviderSeat == provider &&
                accepted.Type == CardActionType.Use && accepted.ActorSeat == lord && accepted.RequesterSeat == lord && accepted.ProviderSeat == provider &&
                accepted.PhysicalCards is [{ CardId: var material, From: var from }] && from == CardLocation.Hand(provider) && published.Contains(material) &&
                accepted.ActionId == completion.ActionId && declared.SourceSeat == lord && declared.CardKind == CardKind.Slash && declared.CardId == material && V(g, 0).Hp == originalHp &&
                !E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == lord && e.TargetSeat == 0) &&
                !E<CardResponseCompletedEvent>(g).Any(e => e.ActionId == completion.ActionId) &&
                E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == completion.ActionId) == 0,
                "Active supply finishes its real provider payment on the declared native typed Use before the principal Slash effect and its later accepted boundary, without a fabricated Response.");
            Answer(g, Activate); Reach(g, p => Demand(p, "target"));
            var supplyDemand = E<SameNameHandStartedEvent>(g).Single(e => e.ActionId == completion.ActionId);
            Require(supplyDemand.Source.OwnerSeat == provider && supplyDemand.OriginalParentFrameId == native.Id &&
                !E<SameNameHandStartedEvent>(g).Any(e => e.Source.OwnerSeat == lord), "Only the provider owns the pre-effect supply-completion demand.");
            Private(g); g = Cold(g, registry); Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, p => Demand(p, "discard"));
            Answer(g, c => c.Cards.Count == 1);
            if (lordHasZhefu)
            {
                Reach(g, p => Offer(p, Zhefu, lord));
                Require(V(g, 0).Hp == originalHp - 1 && E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == lord && e.TargetSeat == 0) == 1 &&
                    E<SameNameHandCompletedEvent>(g).Count(e => e.FrameId == supplyDemand.FrameId) == 1,
                    "The paid provider demand returns once before the principal's genuine Slash damage and separate ordinary Use-completed opportunity.");
                Answer(g, Activate); Reach(g, p => Demand(p, "target"));
                var principal = E<SameNameHandStartedEvent>(g).Single(e => e.ActionId == completion.ActionId && e.Source.OwnerSeat == lord);
                Answer(g, c => c.Targets.SequenceEqual([0])); Reach(g, p => Demand(p, "discard")); Answer(g, c => c.Cards.Count == 1); Play(g);
                OnceDemand(g, principal.FrameId, discarded: true);
            }
            else Play(g);
            OnceDemand(g, supplyDemand.FrameId, discarded: true);
            Require(E<SameNameHandStartedEvent>(g).Count(e => e.ActionId == completion.ActionId && e.Source.OwnerSeat == provider) == 1 &&
                E<SameNameHandStartedEvent>(g).Count(e => e.ActionId == completion.ActionId && e.Source.OwnerSeat == lord) == (lordHasZhefu ? 1 : 0) &&
                E<CardSupplyCompletedEvent>(g).Count(e => e.ActionId == completion.ActionId) == 1 &&
                E<CardActionAcceptedEvent>(g).Where(e => e.Action.ActionId == completion.ActionId).ToArray() is [{ Action.Type: CardActionType.Use } finalAccepted] &&
                finalAccepted.Action.ActorSeat == lord && finalAccepted.Action.ProviderSeat == provider && finalAccepted.Action.PhysicalCards.SequenceEqual(accepted.PhysicalCards) &&
                !E<CardResponseCompletedEvent>(g).Any(e => e.ActionId == completion.ActionId) &&
                E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == lord && e.TargetSeat == 0) == 1 &&
                !g.ResolutionStack.Any(f => f.Id == completion.ParentFrameId),
                "Provider played-card and principal used-card completions each follow their own foreign-turn qualification once, then the exact native Slash returns normally.");
        }
    }

    private static void PublicReveal(GameEngine g, IReadOnlyList<CardSnapshot> shown, int hiddenOwner, IReadOnlyList<int> hidden)
    {
        Require(hidden.Count > 0 && hidden.All(id => g.CreateCardZoneDiagnostics().Single(z => z.CardId == id).Location == CardLocation.Hand(hiddenOwner)),
            "At least one original unselected target-Hand entity remains genuinely hidden during the paid reveal child.");
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var view = g.CreateSnapshot(viewer); Frozen(view.PublicRevealedCards);
            Require(view.PublicRevealedCards.SequenceEqual(shown) && view.PublicRevealedCards.All(c => !hidden.Contains(c.Id)) &&
                (viewer == hiddenOwner || view.Players[hiddenOwner].Hand.Count == 0),
                "Every viewer sees precisely the public revealed entities with their frozen effective Suit; no unselected target-Hand identity is projected.");
        }
    }

    private static void OnceDemand(GameEngine g, long id, bool discarded) => Require(
        E<SameNameHandCompletedEvent>(g).Single(e => e.FrameId == id) is var done && done.Discarded == discarded && done.DamageIssued != discarded &&
        E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1 && !g.ResolutionStack.Any(f => f.Id == id),
        "The exact demand owning program returns and completes once with precisely its paid branch.");
    private static void RespondGroup(GameEngine g)
    {
        for (var n = 0; n < 30; n++)
        {
            var p = P(g); if (p is not null && Offer(p, Yidu, 0)) return;
            Step(g);
        }
        throw new InvalidOperationException("The real group responses did not finish: " + State(g));
    }
    private static void Arrows(GameEngine g) => Use(g, "arrows", cards: V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray());
    private static void Convert(GameEngine g, string binding, int[] targets)
    {
        var action = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.ConversionSource.BindingId == binding && a.TargetSeats.SequenceEqual(targets));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind) { ConversionSource = action.ConversionSource });
    }
    private static bool Activate(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "activate";
    private static bool Offer(PendingDecision p, string skill, int owner) => p.PlayerSeat == owner && p.SkillPrompt?.SkillId == skill && p.Choices.Any(Activate);
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Demand(PendingDecision p, string step) => Has(p, "same-name-hand") && p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == step);
    private static bool Reveal(PendingDecision p, string branch) => Has(p, "completed-undamaged-target-reveal") && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch);
    private static bool ContinueChoice(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static ProgramSkillFrame RevealRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.CompletedUndamagedTargetReveal is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "A real Guo Huai command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var n = 0; n < 120; n++) { var p = P(g); if (p is not null && stop(p)) return; Step(g); }
        throw new InvalidOperationException("The fixed actual Guo Huai boundary was not reached: " + State(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) throw new InvalidOperationException("An expected nested boundary already returned to Play: " + State(g));
        if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Cards.Count == 0 && (c.Parameters.GetValueOrDefault("response") is "pass" or "let-die" || c.Parameters.GetValueOrDefault("action") == "pass"));
        else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Frozen<T>(IReadOnlyList<T> items) => Require(items is System.Collections.IList { IsReadOnly: true }, "The exposed nested collection is frozen.");
    private static void Private(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The published owner or payer decision is private.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(viewer).PendingDecision is null &&
            g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0, "Another viewer receives neither the private prompt nor foreign Hand identities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats); Frozen(p.ValidContentIds);
        foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); Frozen(c.ContentIds); Require(c.Parameters is System.Collections.IDictionary { IsReadOnly: true }, "Every choice parameter dictionary is frozen."); }
        var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("not-published"), g.Revision)).Accepted && State(g) == before,
            "An unpublished decision cannot change a source, reveal private cards or pay any cost.");
    }
    private static void Blind(GameEngine g)
    {
        Private(g); var p = P(g)!;
        Require(p.ValidCardIds.Count == 0 && p.Choices.All(c => c.Cards.Count == 0 && c.Parameters.Keys.All(k => k is "program-action" or "frame-id" or "branch" or "slot-index")) &&
            g.CreateSnapshot(0).Players[1].Hand.Count == 0,
            "Before public reveal the source sees only frozen opaque target-Hand slots, without card identity, name or color.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all four prepared views, frozen choices, owning typed receipts, native children, events and physical card provenance."); return copy; }

    private static (GameEngine, ContentRegistry) Create(CardKind kind, bool yidu = false, bool sourceLoss = false, bool damageLoss = false, bool mixed = false, bool targetRed = false, bool emptySecond = false, CardKind? providerDirection = null, bool lordHasZhefu = false, bool activeSupply = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, yidu, sourceLoss, damageLoss, mixed, targetRed, emptySecond, providerDirection, lordHasZhefu, activeSupply));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = providerDirection is null ? Role.Lord : Role.Renegade, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, "fixture:guo-huai-owner", g.Revision, P(g)!.PromptId));
        Play(g);
        Require(providerDirection is not null || V(g, 1).GeneralId == "fixture:guo-huai-peer-1", "Native role-weighted selection places the unique response skill owner at seat1.");
        return (g, registry);
    }
    private sealed class Fixture(CardKind kind, bool yidu, bool sourceLoss, bool damageLoss, bool mixed, bool targetRed, bool emptySecond, CardKind? providerDirection, bool lordHasZhefu, bool activeSupply) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:ordinary-guo-huai", "1.0.0", "完整哲妇遗毒的真实原生边界");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.rules.json")!);
            using var labels = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rules.ReadToEnd(), labels.ReadToEnd());
            foreach (var id in formal.Programs.Keys) b.AddSkill(new(id, formal.Presentations[id].Name, formal.Presentations[id].Description) { Program = formal.Programs[id], ProgramPresentation = formal.Presentations[id] });
            var fixture = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"viewAs":[
                {"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
                {"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true},
                {"id":"draw","inputKinds":[],"inputSuits":[],"outputKind":"drawTwo","forPlay":true,"forResponse":false,"singleCardTrickUse":true},
                {"id":"peach","inputKinds":[],"inputSuits":[],"outputKind":"peach","forPlay":false,"forResponse":true},
                {"id":"arrows","inputKinds":[],"inputSuits":[],"inputCount":2,"sameSuit":true,"outputKind":"arrowBarrage","forPlay":true,"forResponse":false}],
                "activations":[
                  {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWhoseAttackRangeIncludesOwner","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"}]},
                  {"id":"arrows","minCards":2,"maxCards":2,"sourceZones":["hand"],"selectedCardsSameSuit":true,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"owner","sourceBind":"arrows","outputKind":"arrowBarrage"}]},
                  {"id":"hurt-target","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                  {"id":"hurt-self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]}]},
              {"id":"{{{Loss}}}","revision":1,"triggers":[{"id":"paid-source-loss","window":"discardPileReceived","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementDiscardOnly":true,"discardOwnerScope":"other","movementReasons":["skill-program.ol:zhefu.same-name-hand.discard"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{{Suppress}}}"]}]}]},
              {"id":"{{{DamageLoss}}}","revision":1,"triggers":[{"id":"issued-source-loss","window":"beforeDamageApplied","subject":"damageSource","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["{{{Suppress}}}"]}]}]},
              {"id":"{{{Child}}}","revision":1,"triggers":[{"id":"discard-child","window":"discardPileReceived","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementDiscardOnly":true,"discardOwnerScope":"own","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:guo-huai-red","revision":1,"cardPolicies":[{"id":"red-target","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]},
              {"id":"fixture:guo-huai-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            if (sourceLoss || damageLoss) fixture["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:guo-huai-initial-zhefu","revision":1,"triggers":[{"id":"acquire-source","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:zhefu"]}]}]}""")!);
            if (emptySecond || providerDirection is not null) fixture["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:guo-huai-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]}""")!);
            if (providerDirection is { } required) fixture["skills"]!.AsArray().Add(JsonNode.Parse(JsonSerializer.Serialize(new {
                id = "fixture:guo-huai-faction", revision = 1, cardPolicies = new[] { new {
                    id = "native-assistance", kind = "factionResponseRequest", requiredCardKinds = new[] { required == CardKind.Dodge ? "dodge" : "slash" }, factionId = "wei", ownerRole = "lord" } }
            }))!);
            if (activeSupply)
            {
                fixture["skills"]!.AsArray().Add(JsonNode.Parse(JsonSerializer.Serialize(new {
                    id = ActiveRequest, revision = 1, modifiers = new[] { new { id = "provided-range", query = "attackRange", operation = "set", value = 3, priority = 0 } }
                }))!);
                fixture["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Driver)!["activations"]!.AsArray().Add(JsonNode.Parse("""{"id":"assisted","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"answer"}]}""")!);
            }
            // Twenty red and twenty black entities: twenty-two genuinely dealt target cards
            // leave both colors after its one native Dodge, independently of shuffle and AI cost.
            if (mixed) fixture["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:guo-huai-mixed-initial","revision":1,"modifiers":[{"id":"guaranteed-colors","query":"initialHandSize","operation":"add","value":18,"priority":0,"condition":{"kind":"always"}}]}""")!);
            if (emptySecond) fixture["skills"]!.AsArray().Add(JsonNode.Parse("""{"id":"fixture:guo-huai-extra-initial","revision":1,"modifiers":[{"id":"one-hidden","query":"initialHandSize","operation":"add","value":1,"priority":0,"condition":{"kind":"always"}}]}""")!);
            var presentation = fixture["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var p = new Dictionary<string, object> { ["name"] = id, ["description"] = "固定小实体原生行为驱动" };
                if (id is Loss or DamageLoss or Child) p["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)p;
            });
            var catalog = SkillProgramCatalog.Load(fixture.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实原生夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Suppress, "实际HP4资格抑制", "不修改隐藏状态") { SuppressionRule = new(4) });
            b.AddSkill(new("fixture:guo-huai-first-selection", "唯一首个AI响应拥有者", "原生选将排序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:guo-huai-peer-selection", "其余真实候选", "原生选将排序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            if (emptySecond) b.AddSkill(new("fixture:guo-huai-second-selection", "唯一空手次个目标", "原生选将排序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000d) });
            var ownerSkills = new List<string>(yidu ? new[] { Yidu, Zhefu } : new[] { Zhefu });
            if (emptySecond || providerDirection is not null) ownerSkills.Add("fixture:guo-huai-initial");
            b.AddGeneral(new("fixture:guo-huai-owner", "真实发动者", "supporter", Driver, "jin", 4, ownerSkills));
            var first = new List<string> { "fixture:guo-huai-quiet" };
            if (emptySecond || providerDirection is not null) first.Add("fixture:guo-huai-initial");
            if (emptySecond) first.Add("fixture:guo-huai-extra-initial");
            if (mixed) first.Add("fixture:guo-huai-mixed-initial");
            if (yidu) first.Add(Child); else first.Add(sourceLoss || damageLoss ? "fixture:guo-huai-initial-zhefu" : Zhefu);
            if (sourceLoss) first.Add(Loss); if (damageLoss) first.Add(DamageLoss); if (targetRed) first.Add("fixture:guo-huai-red");
            b.AddGeneral(new("fixture:guo-huai-peer-1", "唯一响应拥有者或展示目标", "supporter", "fixture:guo-huai-first-selection", providerDirection is null ? "qun" : "wei", 4, first));
            if (providerDirection is not null)
            {
                b.AddSkill(new("fixture:guo-huai-lord-selection", "唯一真实主公", "原生角色权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -10000d) });
                var lordSkills = new List<string> { "fixture:guo-huai-faction", "fixture:guo-huai-quiet" };
                if (lordHasZhefu) lordSkills.Add(Zhefu);
                if (activeSupply) lordSkills.Add(ActiveRequest);
                b.AddGeneral(new("fixture:guo-huai-peer-2", "真实护驾或激将主公", "supporter", "fixture:guo-huai-lord-selection", "wei", 4, lordSkills));
                b.AddGeneral(new("fixture:guo-huai-peer-3", "其他存活者", "supporter", "fixture:guo-huai-peer-selection", "qun", 4,
                    ["fixture:guo-huai-quiet", "fixture:guo-huai-initial"]));
            }
            else for (var seat = 2; seat < 4; seat++) b.AddGeneral(new($"fixture:guo-huai-peer-{seat}", "普通其他目标", "supporter", emptySecond && seat == 2 ? "fixture:guo-huai-second-selection" : "fixture:guo-huai-peer-selection", "qun", 4,
                emptySecond && seat == 3 ? ["fixture:guo-huai-quiet", "fixture:guo-huai-initial"] : ["fixture:guo-huai-quiet"]));
            var card = kind switch { CardKind.Slash => "standard:slash", CardKind.FireSlash => "standard:fire_slash", CardKind.Dodge => "standard:dodge", CardKind.Nullification => "standard:nullification", _ => throw new InvalidOperationException() };
            b.AddDeck(new("fixture:guo-huai-deck", "固定小实体牌堆", emptySecond || providerDirection is not null ? 0 : 4, 0, []) { PhysicalCards = Enumerable.Range(0, 40).Select(i =>
                new ContentDeckPhysicalCard(card, mixed && i % 2 == 1 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "完整郭槐共享机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:guo-huai-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:guo-huai-owner", "fixture:guo-huai-peer-1", "fixture:guo-huai-peer-2", "fixture:guo-huai-peer-3"]));
        }
    }
}
