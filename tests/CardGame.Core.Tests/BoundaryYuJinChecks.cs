using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryYuJinChecks
{
    private const string Prep = "boundary:zhenjun-current", Compared = "boundary:yizhong-current";
    private const string Driver = "fixture:yj-driver", Observer = "fixture:yj-observer", Gain = "fixture:yj-gain",
        Entry = "fixture:yj-entry", Completed = "fixture:yj-completed", Conversion = "fixture:yj-conversion", RoleChange = "fixture:yj-role";
    private const string Mode = "identity:classic-yj-fixture", TargetDiscard = "skill-program.preparation-discard.target.ResolvePrepDiscardOrEnding",
        OwnerDiscard = "skill-program.preparation-discard.owner.ResolvePrepDiscardOrEnding", EndingDraw = "program.preparation-discard.ending-draw";
    private static readonly Lazy<ContentRegistry> RegisteredPrograms = new(() => ContentRegistry.Build(new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage()));

    public static void ActualPreparationOpaqueDiscardCostAndZeroEquipmentCount()
    {
        var (g, r) = Create(); Reach(g, p => Offer(p, Prep)); Reject(g); g = Restore(g, r); Activate(g);
        Reach(g, p => Step(p, "target")); Answer(g, c => c.Targets.SequenceEqual([1]));
        var frozen = E<PrepDiscardTargetFrozenEvent>(g).Single();
        Require(frozen.HandCount == 4 && frozen.Hp == 1 && frozen.RequestedCount == 3 && frozen.RequiredCount == 3,
            "Actual Prep freezes X from the selected living target's public Hand/HP and real available discard count.");
        SelectDiscard(g); Reach(g, p => p.SkillPrompt?.SkillId == Observer);
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PrepDiscard?.TargetPayment is not null);
        var targetPaid = root.PrepDiscard!.TargetPayment!;
        Require(targetPaid.ActualCount == 3 && targetPaid.NonEquipmentCount == 3 &&
            targetPaid.From.All(l => l == CardLocation.Hand(1)) &&
            g.CreateSnapshot(0).Players[1].Hand.Count == 0 && g.CreateSnapshot(1).Players[1].HandCount == 1,
            "Three opaque foreign Hand slots are really discarded; the observer sees no surviving hidden identity.");
        var readonlyCards = (IList<int>)targetPaid.CardIds; var blocked = false;
        try { readonlyCards[0] = -1; } catch (NotSupportedException) { blocked = true; }
        Require(blocked && !JsonSerializer.Serialize(E<PrepDiscardPaidEvent>(g).Single()).Contains("CardIds", StringComparison.Ordinal),
            "Trusted payment collections are frozen and new public facts contain no private selected card collection.");
        Reject(g); g = Restore(g, r); Continue(g); Reach(g, p => Step(p, "pay")); AnswerStep(g, "pay");
        SelectDiscard(g); Reach(g, p => p.SkillPrompt?.SkillId == Observer);
        var ownerPaid = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PrepDiscard?.OwnerPayment is not null).PrepDiscard!.OwnerPayment!;
        Require(ownerPaid.PayerSeat == 0 && ownerPaid.ActualCount == targetPaid.NonEquipmentCount &&
            E<PrepDiscardPaidEvent>(g).Count() == 2, "The selected pay branch really discards precisely N owner cards after target children return.");
        g = Restore(g, r); Continue(g); Play(g);
        Require(targetPaid.CardIds.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == TargetDiscard) == 1) &&
            ownerPaid.CardIds.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == OwnerDiscard) == 1) &&
            !E<PrepDiscardEndingIssuedEvent>(g).Any(), "Cold continuation pays both actual costs once and creates no deferred benefit for the paid branch.");

        var (equipment, er) = Create(deck: "defensive_horse"); Reach(equipment, p => Offer(p, Prep)); Activate(equipment);
        Reach(equipment, p => Step(p, "target")); Answer(equipment, c => c.Targets.SequenceEqual([0]));
        SelectDiscard(equipment); Reach(equipment, p => p.SkillPrompt?.SkillId == Observer);
        var equipmentPaid = E<PrepDiscardPaidEvent>(equipment).Single();
        Require(equipmentPaid.ActualCount == 1 && equipmentPaid.NonEquipmentCount == 0 &&
            equipment.CardMovements.Any(m => m.Reason.Value == TargetDiscard && m.From == CardLocation.Hand(0) &&
                m.To == CardLocation.DiscardPile && EquipmentCatalog.IsEquipment(m.CardKind)),
            "The owner can be the target, and printed equipment in Hand contributes zero to N.");
        equipment = Restore(equipment, er); Continue(equipment); Reach(equipment, p => Step(p, "pay")); AnswerStep(equipment, "pay"); Play(equipment);
        Require(!equipment.CardMovements.Any(m => m.Reason.Value == OwnerDiscard) &&
            !E<PrepDiscardEndingIssuedEvent>(equipment).Any(), "N=0 completes the empty cost without manufacturing a movement or future draw.");

        var (decline, dr) = Create(); Reach(decline, p => Offer(p, Prep)); decline = Restore(decline, dr); Skip(decline); Play(decline);
        Require(!E<PrepDiscardTargetFrozenEvent>(decline).Any() && !E<PrepDiscardPaidEvent>(decline).Any(),
            "Declining the original optional actual Prep opportunity selects no target and pays nothing.");
    }

    public static void SameActualTurnEndingPromiseSourceReplacementAndRealRescue()
    {
        var (g, r) = Create(); Promise(g, 1); var promise = E<PrepDiscardEndingIssuedEvent>(g).Single().Promise;
        Play(g); End(g); Reach(g, p => p.SkillPrompt?.SkillId == Observer);
        var drawn = E<PrepDiscardEndingDrawnEvent>(g).Single();
        Require(drawn.PromiseId == promise.Id && drawn.TargetSeat == 1 && drawn.RequestedCount == 3 && drawn.ActualCount == 3 &&
            E<PrepDiscardEndingConsumedEvent>(g).Single().Applied &&
            g.CardMovements.Count(m => m.Sequence > drawn.SequenceBefore && m.Sequence <= drawn.SequenceAfter &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) && m.Reason.Value == EndingDraw) == 3,
            "The exact original actual-turn Ending pays the original target a real draw of frozen N.");
        Reject(g); g = Restore(g, r); Continue(g);
        Until(g, () => !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PrepDiscardEndingDraw is not null));
        Require(E<PrepDiscardEndingDrawnEvent>(g).Count(e => e.PromiseId == promise.Id) == 1 &&
            E<PrepDiscardEndingConsumedEvent>(g).Count(e => e.Id == promise.Id) == 1,
            "The restored movement children return once without reissuing the promise or repeating actual draw.");

        var (lost, lr) = Create(); Promise(lost, 1); Play(lost);
        var original = E<PrepDiscardEndingIssuedEvent>(lost).Single().Promise;
        Use(lost, "lose-prep", [], []); Play(lost); Use(lost, "reacquire-prep", [], []); Play(lost);
        lost = Restore(lost, lr); End(lost);
        Until(lost, () => E<PrepDiscardEndingConsumedEvent>(lost).Any(e => e.Id == original.Id));
        Require(!E<PrepDiscardEndingConsumedEvent>(lost).Single(e => e.Id == original.Id).Applied &&
            !E<PrepDiscardEndingDrawnEvent>(lost).Any(e => e.PromiseId == original.Id) &&
            lost.CardMovements.Count(m => m.Reason.Value == TargetDiscard) == 3,
            "A legally lost/reacquired source is a new instance and cannot inherit or re-pay the original Ending promise.");

        var (zero, zr) = Create(deck: "defensive_horse"); Promise(zero, 0); Play(zero); End(zero);
        Until(zero, () => E<PrepDiscardEndingConsumedEvent>(zero).Any() &&
            !zero.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PrepDiscardEndingDraw is not null)); zero = Restore(zero, zr);
        Require(E<PrepDiscardEndingConsumedEvent>(zero).Single().Applied && !E<PrepDiscardEndingDrawnEvent>(zero).Any() &&
            !zero.CardMovements.Any(m => m.Reason.Value == EndingDraw), "A promised zero consumes only its original promise, without a fake draw fact or movement batch.");

        var (rescue, rr) = Create(deck: "peach", gainDying: true); Promise(rescue, 0); Play(rescue); End(rescue);
        Reach(rescue, p => p.SkillPrompt?.SkillId == Gain); Continue(rescue);
        Reach(rescue, p => p.SkillPrompt?.SkillId == Entry); rescue = Restore(rescue, rr); Continue(rescue);
        Reach(rescue, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RescueDying);
        Answer(rescue, c => c.Parameters.GetValueOrDefault("response") == "peach");
        Reach(rescue, p => p.SkillPrompt?.SkillId == Entry && rescue.ResolutionStack.OfType<ProgramSkillFrame>().Any(f =>
            f.SkillId == Entry && f.WindowContext?.Window == SkillProgramTriggerWindow.CardUseCompleted));
        Require(rescue.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PrepDiscardEndingDraw is not null) &&
            E<CardActionAcceptedEvent>(rescue).Any(e => e.Action.Type == CardActionType.Response &&
                e.Action.EffectiveKind == CardKind.Peach && e.Action.PhysicalCards.Count == 1),
            "The promised real draw owns the gain→HP-loss→Dying→physical Peach completed child chain.");
        rescue = Restore(rescue, rr); Continue(rescue);
        Until(rescue, () => !rescue.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.PrepDiscardEndingDraw is not null));
        Require(rescue.CreateSnapshot(0).Players[0].Hp > 0 && E<PrepDiscardEndingDrawnEvent>(rescue).Count() == 1 &&
            E<PrepDiscardPaidEvent>(rescue).Count() == 1, "The genuinely restored engine completes rescue and returns to Ending without repeated cost or draw.");
    }

    public static void FrozenBlackSlashHpHandArmorAndMixedPhysicalPayments()
    {
        var (g, r) = Create(armor: true); SkipPrep(g); Play(g); Use(g, "gear", [], [1]); Play(g);
        Require(g.CreateSnapshot(0).Players[1].Equipment.Any(c => c.Kind == CardKind.BaguaFormation), "A real equipment producer installs Bagua on the actual target.");
        var hp = g.CreateSnapshot(0).Players[1].Hp;
        var materials = g.CreateSnapshot(0).Players[0].Hand.Where(c => c.Kind == CardKind.Dodge).Take(2).Select(c => c.Id).ToArray();
        Use(g, "double-slash", materials, [1]); Reach(g, p => p.SkillPrompt?.SkillId == Completed);
        var outgoing = E<ComparedBlackSlashAppliedEvent>(g).Single().Receipt;
        var action = E<CardActionAcceptedEvent>(g).Select(e => e.Action).Single(a => a.ActionId == outgoing.ActionId);
        Require(outgoing.ActorSeat == 0 && outgoing.ProviderSeat == 0 && outgoing.TargetSeat == 1 && outgoing.CannotRespond &&
            !outgoing.Ineffective && outgoing.TargetHandCount <= outgoing.ActorHandCount && action.EffectiveIsRed == false &&
            action.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(materials.Order()) &&
            g.CreateSnapshot(0).Players[1].Hp == hp - 1 &&
            !E<JudgmentResolvedEvent>(g).Any(e => e.TargetSeat == 1 && e.Reason == JudgmentReasons.BaguaDefense) &&
            !E<CardActionAcceptedEvent>(g).Any(e => e.Action.ParentActionId == outgoing.ActionId && e.Action.EffectiveKind == CardKind.Dodge),
            "A true paid black multi-material Slash compares current public Hand counts and prohibits actual Dodge/Bagua response.");
        g = Restore(g, r); Continue(g); Play(g);
        Require(materials.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1) &&
            !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Cold completion retains one original cost/cleanup and does not increase ordinary Slash quota.");

        var (armor, ar) = Create(armor: true, armorId: "standard:renwang_shield"); SkipPrep(armor); Play(armor);
        Use(armor, "gear", [], [1]); Play(armor); var armorHp = armor.CreateSnapshot(0).Players[1].Hp;
        PlaySlash(armor, 1); Reach(armor, p => p.SkillPrompt?.SkillId == Completed);
        Require(E<ComparedBlackSlashAppliedEvent>(armor).Single().Receipt.CannotRespond &&
            armor.CreateSnapshot(0).Players[1].Hp == armorHp, "Prohibiting Dodge response preserves the real Renwang black-Slash armor immunity.");
        armor = Restore(armor, ar); Continue(armor); Play(armor);

        foreach (var lowerHp in new[] { false, true })
        {
            var (incoming, ir) = Create(deck: "slash", lowerPeerHp: lowerHp); SkipPrep(incoming); Play(incoming);
            var originalHp = incoming.CreateSnapshot(0).Players[0].Hp;
            Use(incoming, "request-slash", [], [1]);
            Until(incoming, () => E<ComparedBlackSlashAppliedEvent>(incoming).Any());
            var receipt = E<ComparedBlackSlashAppliedEvent>(incoming).Single().Receipt;
            Require(receipt.ActorSeat == 1 && receipt.ProviderSeat == 1 && receipt.TargetSeat == 0 &&
                receipt.Ineffective == !lowerHp && receipt.Ineffective == (receipt.ActorHp >= receipt.TargetHp),
                "The requested native actor truly uses its own physical Slash; incoming immunity compares the actual user HP.");
            incoming = Restore(incoming, ir);
            if (lowerHp) { Reach(incoming, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RespondDodge); Pass(incoming); }
            Play(incoming);
            Require(incoming.CreateSnapshot(0).Players[0].Hp == originalHp - (lowerHp ? 1 : 0) &&
                E<CardUseFinishedEvent>(incoming).Count(e => e.ResolutionId == receipt.CardUseFrameId) == 1,
                "The restored black Slash either finishes ineffective or follows the normal response/damage path once.");
        }

        var (replaced, pr) = Create(replaceActor: true); SkipPrep(replaced); Play(replaced);
        PlaySlash(replaced, 3);
        Reach(replaced, p => p.SkillPrompt?.SkillId == RoleChange && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Answer(replaced, c => c.Targets.SequenceEqual([1])); Until(replaced, () => E<ComparedBlackSlashAppliedEvent>(replaced).Any());
        var currentActor = E<ComparedBlackSlashAppliedEvent>(replaced).Single().Receipt;
        Require(E<ProgramCardUseActorReplacedEvent>(replaced).Single().ActorSeat == 1 &&
            currentActor is { ActorSeat: 1, ProviderSeat: 0, TargetSeat: 3, Ineffective: true, ActorPolicySource: null } &&
            currentActor.ActorHp >= currentActor.TargetHp && replaced.CreateSnapshot(0).Players[0].Hp < currentActor.TargetHp,
            "Same-use actor replacement keeps its original black appearance/provider, but HP and policy qualification use the true replacement actor.");
        replaced = Restore(replaced, pr); Play(replaced);
        Require(E<CardUseFinishedEvent>(replaced).Count(e => e.ResolutionId == currentActor.CardUseFrameId) == 1,
            "The replaced original Slash completes once after cold restoration without provider-based immunity.");

        var (mixed, mr) = Create(mixed: true); SkipPrep(mixed); Play(mixed); Use(mixed, "draw-materials", [], []); Play(mixed);
        var hand = mixed.CreateSnapshot(0).Players[0].Hand;
        var red = hand.First(c => c.Suit is Suit.Heart or Suit.Diamond).Id;
        var black = hand.First(c => c.Suit is Suit.Spade or Suit.Club).Id;
        Use(mixed, "double-slash", [red, black], [1]); Reach(mixed, p => p.SkillPrompt?.SkillId == Completed);
        var mixedUse = E<CardActionAcceptedEvent>(mixed).Select(e => e.Action).Last(a => a.Type == CardActionType.Use && a.EffectiveKind == CardKind.Slash);
        Require(mixedUse.PhysicalCards.Count == 2 && mixedUse.EffectiveIsRed is null &&
            !E<ComparedBlackSlashAppliedEvent>(mixed).Any(e => e.Receipt.ActionId == mixedUse.ActionId),
            "A real red/black multi-material payment is colorless and does not acquire a black-Slash conclusion.");
        mixed = Restore(mixed, mr); Continue(mixed); Play(mixed);
        Require(new[] { red, black }.All(id => mixed.CardMovements.Count(m => m.CardId == id && m.To == CardLocation.Processing) == 1),
            "The restored mixed use retains both exact physical payments without inventing a source color.");
    }

    public static void NativePreparationComparisonAndStrictStandaloneContracts()
    {
        var (native, nr) = Create(native: true);
        // StartGame may already complete a native match; inspect its full committed
        // history first, and never submit an Advance after completion.
        for (var i = 0; i < 140 && native.State.Status != EngineStatus.Completed &&
            (!E<PrepDiscardPaidEvent>(native).Any() || !E<ComparedBlackSlashAppliedEvent>(native).Any()); i++)
            Accept(native, new AdvanceOneStepCommand(native.Revision));
        var paid = E<PrepDiscardPaidEvent>(native).FirstOrDefault(e => !e.OwnerCost);
        var comparison = E<ComparedBlackSlashAppliedEvent>(native).FirstOrDefault()?.Receipt;
        Require(paid is not null && paid.ActualCount > 0 && comparison is not null && comparison.ActorSeat == 0 &&
            comparison.CannotRespond && E<CardActionAcceptedEvent>(native).Any(e => e.Action.ActionId == comparison.ActionId &&
                e.Action.Type == CardActionType.Use && e.Action.PhysicalCards.Count == 1),
            "An actual native bot selects real Prep payment and uses a paid black conversion Slash through the comparison path.");
        native = Restore(native, nr);
        Require(E<PrepDiscardPaidEvent>(native).Count(e => e.ProgramFrameId == paid!.ProgramFrameId && !e.OwnerCost) == 1,
            "Native command replay retains its original payment instead of regenerating a hidden card selection.");
        if (native.State.Status != EngineStatus.Completed) Accept(native, new AdvanceOneStepCommand(native.Revision));

        var prep = "{\"id\":\"p\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"resolvePrepDiscardOrEnding\",\"target\":\"owner\",\"stateId\":\"end\",\"zones\":[\"hand\",\"equipment\"]}]}";
        var ending = "{\"id\":\"end\",\"window\":\"turnEnding\",\"subject\":\"owner\",\"optional\":false,\"turnOwnerScope\":\"paidPrepDiscardEnding\",\"effects\":[{\"op\":\"drawPrepDiscardEnding\",\"target\":\"owner\"}]}";
        foreach (var trigger in new[] { prep.Replace("turnStartBeforeNormalFlow", "drawPhaseStarting", StringComparison.Ordinal),
            prep.Replace("\"optional\":true", "\"optional\":false", StringComparison.Ordinal),
            prep.Replace("[\"hand\",\"equipment\"]", "[\"hand\"]", StringComparison.Ordinal),
            ending.Replace("paidPrepDiscardEnding", "own", StringComparison.Ordinal),
            ending.Replace("\"optional\":false", "\"optional\":true", StringComparison.Ordinal) })
        {
            var failed = false; try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion +
                ",\"skills\":[{\"id\":\"fixture:bad-prep\",\"revision\":1,\"triggers\":[" + trigger + "]}]}",
                "{\"schemaVersion\":3,\"skills\":{\"fixture:bad-prep\":{\"name\":\"bad\",\"description\":\"bad\"}}}"); }
            catch (InvalidOperationException) { failed = true; }
            Require(failed, "Strict composition refuses alternate phase, optionality, payment region or unearned Ending.");
        }
        foreach (var kind in new[] { "duel", "dodge" })
        {
            var failed = false; try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion +
                ",\"skills\":[{\"id\":\"fixture:bad-black\",\"revision\":1,\"cardPolicies\":[{\"id\":\"bad\",\"kind\":\"nullifyBlackSlashByCurrentHp\",\"cardKinds\":[\"" + kind + "\"]}]}]}",
                "{\"schemaVersion\":3,\"skills\":{\"fixture:bad-black\":{\"name\":\"bad\",\"description\":\"bad\"}}}"); }
            catch (InvalidOperationException) { failed = true; }
            Require(failed, "The new comparison policy cannot silently become a non-Slash policy.");
        }
    }

    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Offer(PendingDecision p, string skill) => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == skill &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool Step(PendingDecision p, string step) => p.SkillPrompt?.SkillId == Prep && p.Choices.Any(c => c.Parameters.GetValueOrDefault("prep-discard-step") == step);
    private static void AnswerStep(GameEngine g, string step) => Answer(g, c => c.Parameters.GetValueOrDefault("prep-discard-step") == step);
    private static void SelectDiscard(GameEngine g)
    {
        Reach(g, p => Step(p, "card"));
        for (var i = 0; i < 64 && !P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("prep-discard-step") == "finish"); i++)
        {
            var choices = P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("prep-discard-step") == "card").ToArray();
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PrepDiscard is not null);
            if (root.PrepDiscard!.Stage == PrepDiscardStage.SelectingTargetCards && root.PrepDiscard.TargetSeat != 0)
                Require(choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).All(c => c.Cards.Count == 0),
                    "Foreign Hand choices are opaque original-region slots rather than public hidden entities.");
            Answer(g, c => c.Id == choices[0].Id);
        }
        AnswerStep(g, "finish");
    }
    private static void Promise(GameEngine g, int target)
    { Reach(g, p => Offer(p, Prep)); Activate(g); Reach(g, p => Step(p, "target")); Answer(g, c => c.Targets.SequenceEqual([target]));
        SelectDiscard(g); Reach(g, p => Step(p, "ending")); AnswerStep(g, "ending"); }
    private static void SkipPrep(GameEngine g) { Reach(g, p => Offer(p, Prep)); Skip(g); }
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Use(GameEngine g, string activation, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards, targets, g.Revision, P(g)!.PromptId));
    private static void PlaySlash(GameEngine g, int target)
    { var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind)
            { ConversionSource = action.ConversionSource }); }
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Pass(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> done) => Until(g, () => P(g) is { } p && done(p));
    private static void Until(GameEngine g, Func<bool> done)
    { for (var i = 0; i < 160; i++) { if (done()) return; Require(g.State.Status != EngineStatus.Completed, "Match completed before its fixed boundary."); Advance(g); }
        throw new InvalidOperationException("Fixed Yu Jin boundary not reached: " + JsonSerializer.Serialize(P(g))); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "peach"))
            Answer(g, c => c.Parameters.GetValueOrDefault("response") == "peach");
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Pass(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
            Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Real command rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Restore(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(g) == State(restored), "Four private views, exact original payments/receipts and children survive real JSON restore."); return restored; }
    private static void Reject(GameEngine g)
    { var p = P(g)!; var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && before == State(g),
        "Rejected input leaves every private view, card zone, real cost and accepted command unchanged."); }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException(text); }

    private static (GameEngine, ContentRegistry) Create(string deck = "dodge", bool armor = false, bool gainDying = false,
        bool lowerPeerHp = false, bool mixed = false, bool native = false, string armorId = "standard:bagua", bool replaceActor = false)
    {
        // Reuse the official registered definitions but load no unrelated color
        // capability. This independently exercises both new appearance gates.
        var registry = ContentRegistry.Build(new StandardContentPackage(), new ComparedPrograms(),
            new Fixture(deck, armor, gainDying, lowerPeerHp, mixed, native, armorId, replaceActor));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, ModeId = Mode,
            HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord, UseInteractiveSetup = true,
            UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = native ? 3 : 8 }, registry);
        Accept(g, new StartGameCommand());
        if (!native) { Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
            Accept(g, new SelectGeneralCommand(0, "fixture:yj-owner", g.Revision, P(g)!.PromptId)); }
        return (g, registry);
    }
    private sealed class ComparedPrograms : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-current-prep-compared-programs", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        { b.AddSkill(RegisteredPrograms.Value.GetSkill(Prep)); b.AddSkill(RegisteredPrograms.Value.GetSkill(Compared)); }
    }
    private sealed class Fixture(string deck, bool armor, bool gainDying, bool lowerPeerHp, bool mixed, bool native, string armorId, bool replaceActor) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-yu-jin", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var gainTail = gainDying ? ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}" : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
            {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":80,"priority":0}],
              "viewAs":[{"id":"two-slash","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand","equipment"],"extendedUse":true,"allowSameKind":true,"sameSuit":false,"outputKind":"slash","forPlay":true,"forResponse":false}],
              "activations":[
                {"id":"gear","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                {"id":"double-slash","minCards":2,"maxCards":2,"sourceZones":["hand","equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-slash","outputKind":"slash"}]},
                {"id":"request-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"requested"}]},
                {"id":"lose-prep","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"none","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Prep}}"],"sourceBind":"standard:none"}]},
                {"id":"reacquire-prep","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"none","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Prep}}"]}]},
                {"id":"draw-materials","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"none","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":20}]}
              ]},
            {"id":"{{Conversion}}","revision":1,"viewAs":[{"id":"single-slash","inputKinds":["dodge"],"inputSuits":["spade"],"sameSuit":false,"outputKind":"slash","forPlay":true,"forResponse":false}]},
            {"id":"{{Observer}}","revision":1,"triggers":[
              {"id":"paid-discard","window":"cardsMoved","subject":"any","sourceZones":["hand","equipment"],"destinationZones":["discardPile"],"movementReasons":["{{TargetDiscard}}","{{OwnerDiscard}}"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid-seen","options":[{"id":"continue"}]}]},
              {"id":"promised-real-draw","window":"cardsMoved","subject":"any","sourceZones":["drawPile"],"destinationZones":["hand"],"movementReasons":["{{EndingDraw}}"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-seen","options":[{"id":"continue"}]}]}
            ]},
            {"id":"{{Gain}}","revision":1,"triggers":[{"id":"real-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{EndingDraw}}"],"movementOccurrence":"perSourceOwner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}{{gainTail}}]}]},
            {"id":"{{Entry}}","revision":1,"triggers":[
              {"id":"dying-entry","window":"dyingEntering","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"entry-seen","options":[{"id":"continue"}]}]},
              {"id":"peach-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["peach"],"includeResponseUses":true,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"peach-seen","options":[{"id":"continue"}]}]}
            ]},
            {"id":"{{RoleChange}}","revision":1,"triggers":[{"id":"actual-actor-replacement","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]},
            {"id":"{{Completed}}","revision":1,"triggers":[{"id":"slash-completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"slash-seen","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object> {
                [Driver] = new { name = "真实成熟驱动", description = "真实装备、成本、用牌及来源替换" },
                [Conversion] = new { name = "真实单实体转换", description = "黑色闪当杀" },
                [RoleChange] = new { name = "真实使用者替换", description = "成熟目标窗口替换实际使用者" },
                [Observer] = Options("真实付款与收益子窗"), [Gain] = Options("真实收益与濒死"),
                [Entry] = Options("真实救援子链"), [Completed] = Options("真实用牌完成")
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = labels }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "真实通用夹具") { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            foreach (var owner in new[] { true, false }) b.AddSkill(new(owner ? "fixture:yj-owner-weight" : "fixture:yj-peer-weight", "原生固定选将", "真实角色权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => (role == Role.Lord) == owner ? 10000d : -10000d) });
            var ownSkills = new List<string> { "fixture:yj-owner-weight", Compared, Conversion };
            if (!native) ownSkills.AddRange([Driver, gainDying ? Gain : Observer, Completed]);
            if (gainDying) ownSkills.Add(Entry);
            if (replaceActor) ownSkills.Add(RoleChange);
            b.AddGeneral(new("fixture:yj-owner", "当前于禁真实来源", "supporter", Prep, "wei", 6, ownSkills.ToArray()) { InitialHp = gainDying ? 1 : 4 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:yj-peer-{i}", "真实对手", "supporter", "fixture:yj-peer-weight", "shu", 8,
                replaceActor && i == 3 ? [Compared] : [])
                { InitialHp = replaceActor ? (i == 3 ? 5 : null) : i == 1 && (deck != "slash" || lowerPeerHp) ? 1 : null });
            b.AddDeck(new("fixture:yj-deck", "固定真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(
                armor && i >= 64 ? armorId : "standard:" + deck, mixed && i >= 40 ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "当前于禁真实成本与目标响应", 4, 4, new Dictionary<string,int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:yj-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:yj-owner", "fixture:yj-peer-1", "fixture:yj-peer-2", "fixture:yj-peer-3"]));
        }
        private static object Options(string name) => new { name, description = "真实命令子链", optionLabels = new Dictionary<string,string> { ["continue"] = "继续" } };
    }
}
