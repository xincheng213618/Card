using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinaryShiBaoChecks
{
    private const string Skill = "ol:zhuosheng", Driver = "fixture:shi-bao-driver";
    private const string Owner = "fixture:shi-bao-owner", Complete = "fixture:shi-bao-complete";
    private const string Gain = "fixture:shi-bao-gain", Inert = "fixture:shi-bao-inert";
    private const string Mode = "identity:classic-shi-bao-shared", DrawReason = "skill-program.round-gained.equipment-draw";
    private enum Scenario { Slash, Alcohol, IronChain, DrawTwo, BorrowedSword, Equipment }

    public static void BasicUsesRequireEveryCurrentRoundMaterialAndRespectSourceQualification()
    {
        var (g, registry) = Start(Scenario.Slash);
        var old = V(g, 0).Hand.Select(c => c.Id).ToArray();
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
            "Initial dealing is not a current-round acquisition: ordinary old Slash cannot reach the opposite seat.");
        var gained = Draw(g);
        for (var index = 0; index < 2; index++)
        {
            var action = Action(g, LegalActionKind.Slash, gained[index], [2]);
            Play(g, action); Reach(g, IsComplete);
            var use = CurrentUse(g, action.CardId!.Value); AssertQualification(g, use, [gained[index]]);
            g = Cold(g, registry); Reach(g, IsPlay); AssertFinished(g, use, [gained[index]]);
        }
        Require(E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) == 2 &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == old[0] && a.TargetSeats.SequenceEqual([1])),
            "Two actually gained Slashes ignore range and do not debit the ordinary Slash allowance; an old adjacent Slash is still available.");
        Play(g, Action(g, LegalActionKind.Slash, old[0], [1])); Reach(g, IsComplete);
        var ordinary = CurrentUse(g, old[0]);
        Require(ordinary.RoundGainedUseQualifications is null, "The real old adjacent Slash spends its ordinary quota without acquiring a source receipt.");
        Reach(g, IsPlay); AssertFinished(g, ordinary, [old[0]]);
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == old[1]),
            "After one actual ordinary Slash, another old material obeys the spent ordinary quota.");
        Reject(g, new PlayCardCommand(0, old[1], [1], g.Revision, P(g)!.PromptId));
        ReturnThroughForeignHand(ref g, registry, old[1]);
        var returned = Action(g, LegalActionKind.Slash, old[1], [2]); Play(g, returned); Reach(g, IsComplete);
        var returnedUse = CurrentUse(g, old[1]); AssertQualification(g, returnedUse, [old[1]]);
        Require(returnedUse.RoundGainedUseQualifications!.Single().MaterialGains.Single().OriginalSource == CardLocation.Hand(1),
            "A real foreign-hand gift back, rather than the printed card kind or original dealing, supplies the new acquisition.");
        g = Cold(g, registry); Reach(g, IsPlay); AssertFinished(g, returnedUse, [old[1]]);

        foreach (var allNew in new[] { false, true })
        {
            (g, registry) = Start(Scenario.Slash); old = V(g, 0).Hand.Select(c => c.Id).ToArray(); gained = Draw(g);
            var materials = allNew ? gained.Take(2).ToArray() : new[] { old[0], gained[0] };
            Use(g, "pair-slash", materials, [allNew ? 2 : 1]); Reach(g, IsComplete);
            var pairCandidates = g.ResolutionStack.OfType<CardUseFrame>().Where(f => f.PhysicalCardIds is { Count: 2 } ids &&
                ids.Distinct().Count() == 2 && ids.Order().SequenceEqual(materials.Order())).ToArray();
            Require(pairCandidates.Length == 1, "The actual two-material native carrier must own exactly the submitted entities: " + Diagnostic(g));
            var pair = pairCandidates.Single(); var frozenMaterials = pair.PhysicalCardIds!;
            Require(pair.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0 } && pair.CardKind == CardKind.Slash &&
                pair.Action.PhysicalCards.Select(c => c.CardId).SequenceEqual(frozenMaterials),
                "The mature two-card activation freezes both submitted physical claims in its canonical native order.");
            if (allNew) AssertQualification(g, pair, frozenMaterials);
            else Require(pair.RoundGainedUseQualifications is null && E<RoundGainedUseQualifiedEvent>(g).All(e => e.Qualification.CardUseFrameId != pair.Id),
                "A mixed old/new material set is wholly ineligible; one recent card cannot qualify the other material.");
            g = Cold(g, registry); Reach(g, IsPlay); AssertFinished(g, pair, materials);
        }

        (g, registry) = Start(Scenario.Slash);
        Use(g, "zero-slash", [], [2]); Reach(g, IsComplete);
        var zero = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == 0);
        Require(zero.Action is { Type: CardActionType.Use, ActorSeat: 0, PhysicalCards.Count: 0 } &&
            zero.PhysicalCardIds!.Count == 0 && zero.RoundGainedUseQualifications is null &&
            E<RoundGainedUseQualifiedEvent>(g).All(e => e.Qualification.CardUseFrameId != zero.Id),
            "A genuine zero-material native Slash remains a Use, but has no round-gained material qualification.");
        g = Cold(g, registry); Reach(g, IsPlay);
        Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == zero.Id && e.CardKind == CardKind.Slash) == 1,
            "The real zero-material Slash completes once without invented payments or an enhanced-use receipt.");

        (g, registry) = Start(Scenario.Alcohol); old = V(g, 0).Hand.Select(c => c.Id).ToArray(); gained = Draw(g);
        Play(g, Action(g, LegalActionKind.Alcohol, gained[0])); Reach(g, IsComplete);
        var wine = CurrentUse(g, gained[0]); AssertQualification(g, wine, [gained[0]]);
        g = Cold(g, registry); Reach(g, IsPlay); AssertFinished(g, wine, [gained[0]]);
        Play(g, Action(g, LegalActionKind.Slash, gained[1], [2], "as-slash")); Reach(g, IsComplete);
        var consuming = CurrentUse(g, gained[1]); AssertQualification(g, consuming, [gained[1]]);
        Reach(g, IsPlay); AssertFinished(g, consuming, [gained[1]]);
        Require(!V(g, 0).HasAlcoholEffect, "A real subsequent Slash consumes the first Alcohol effect before another Wine is attempted.");
        Play(g, Action(g, LegalActionKind.Alcohol, old[0])); Reach(g, IsComplete);
        var ordinaryWine = CurrentUse(g, old[0]);
        Require(ordinaryWine.RoundGainedUseQualifications is null, "The old Wine remains available after the qualified Wine, proving the qualified use did not debit the ordinary Alcohol quota.");
        Reach(g, IsPlay); AssertFinished(g, ordinaryWine, [old[0]]);
        Play(g, Action(g, LegalActionKind.Slash, old[1], [1], "as-slash")); Reach(g, IsComplete);
        var ordinaryConsuming = CurrentUse(g, old[1]);
        Require(ordinaryConsuming.RoundGainedUseQualifications is null, "The old material's real Slash uses the ordinary allowance and consumes the ordinary Wine effect.");
        Reach(g, IsPlay); AssertFinished(g, ordinaryConsuming, [old[1]]);
        Require(!V(g, 0).HasAlcoholEffect && !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol && a.CardId == old[2]),
            "The ordinary Wine quota is now genuinely spent, with no pending Wine effect masking the limit check.");
        Play(g, Action(g, LegalActionKind.Alcohol, gained[2])); Reach(g, IsComplete);
        var secondWine = CurrentUse(g, gained[2]); AssertQualification(g, secondWine, [gained[2]]);
        g = Cold(g, registry); Reach(g, IsPlay); AssertFinished(g, secondWine, [gained[2]]);
        Require(E<AlcoholAppliedEvent>(g).Count(e => e.SourceSeat == 0) == 3,
            "Qualified Wine, ordinary Wine and another qualified Wine each apply once: the first enhanced use did not debit quota and the last bypasses the spent ordinary quota.");
    }

    public static void OrdinaryTrickTargetAdditionRemovalAndImplicitSelfFinishOnce()
    {
        AssertLoaderBoundaries();
        foreach (var branch in new[] { "add", "remove", "decline" })
        {
            var (g, registry) = Start(Scenario.IronChain); var material = Draw(g)[0];
            var original = branch == "add" ? new[] { 1, 3 } : new[] { 1 };
            var use = BeginAdjustment(ref g, registry, Action(g, LegalActionKind.IronChain, material, original));
            var expected = branch == "add" ? new[] { 1, 3, 2 } : branch == "remove" ? [] : original;
            Adjust(ref g, registry, use, branch, branch == "add" ? [2] : branch == "remove" ? [1] : []);
            Reach(g, IsComplete); g = Cold(g, registry);
            Require(E<IronChainStateChangedEvent>(g).Where(e => e.ResolutionId == use.Id).Select(e => e.TargetSeat).SequenceEqual(expected),
                "Exactly the final target list receives native Iron Chain effects, including a genuine removal down to zero.");
            Reach(g, IsPlay); AssertFinished(g, use, [material]);
            Require(E<RoundGainedTrickTargetResolvedEvent>(g).Count(e => e.Qualification.CardUseFrameId == use.Id) == 1,
                "One optional adjustment has one exact audit result and cannot repeat its material or target tail.");
        }
        foreach (var branch in new[] { "add", "remove", "decline" })
        {
            var (g, registry) = Start(Scenario.DrawTwo); var material = Draw(g)[0];
            var before = Enumerable.Range(0, 4).Select(s => V(g, s).HandCount).ToArray();
            var use = BeginAdjustment(ref g, registry, Action(g, LegalActionKind.DrawTwo, material));
            Require(E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == use.Action!.ActionId).Action.TargetSeats.Count == 0 &&
                g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.RoundGainedTrickTargetDraft is not null)
                    .RoundGainedTrickTargetDraft!.OriginalTargetSeats.SequenceEqual([0]),
                "A real accepted DrawTwo keeps its implicit target while the typed adjustment exposes exactly its native self target.");
            Adjust(ref g, registry, use, branch, branch == "add" ? [2] : branch == "remove" ? [0] : []);
            Reach(g, IsComplete); g = Cold(g, registry);
            Require(V(g, 0).HandCount == before[0] - 1 + (branch == "remove" ? 0 : 2) &&
                V(g, 2).HandCount == before[2] + (branch == "add" ? 2 : 0),
                "Self DrawTwo removal draws zero, decline draws only for self, and addition resolves two real entities for each final recipient.");
            Reach(g, IsPlay); AssertFinished(g, use, [material]); _ = Cold(g, registry);
        }
        {
            var (g, registry) = Start(Scenario.IronChain); var old = V(g, 0).Hand[0].Id;
            Play(g, Action(g, LegalActionKind.IronChain, old, [1])); Reach(g, IsComplete);
            var use = CurrentUse(g, old);
            Require(use.RoundGainedUseQualifications is null && E<RoundGainedTrickTargetOfferedEvent>(g).Length == 0,
                "An old physical trick does not gain a target-selection offer merely because the actor owns Zhuosheng.");
            Reach(g, IsPlay); AssertFinished(g, use, [old]);
            var material = Draw(g)[0]; var before = E<RoundGainedTrickTargetOfferedEvent>(g).Length;
            Play(g, Action(g, LegalActionKind.Recast, material)); Reach(g, IsPlay);
            Require(E<RoundGainedTrickTargetOfferedEvent>(g).Length == before && g.CreateCardZoneDiagnostics().Single(c => c.CardId == material).Location == CardLocation.DiscardPile,
                "A true Iron Chain recast has no actual ordinary-trick target adjustment, even when the card was gained this round.");
            _ = Cold(g, registry);
        }
    }

    public static void BorrowedSwordAdjustsWholePairsWithoutSplittingNativePayments()
    {
        foreach (var branch in new[] { "add", "remove" })
        {
            var (g, registry) = Start(Scenario.BorrowedSword);
            foreach (var seat in new[] { 1, 3 }) { Use(g, "equip-peer", [], [seat]); Reach(g, IsPlay); }
            var weapons = new[] { 1, 3 }.ToDictionary(s => s, s => V(g, s).Equipment.Single().Id);
            var material = Draw(g)[0];
            var use = BeginAdjustment(ref g, registry, Action(g, LegalActionKind.BorrowedSword, material, [1, 2], "as-borrowed"));
            Require(P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") != "decline").All(c => c.Targets.Count == 2) &&
                P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "add" && c.Targets.SequenceEqual([3, 2])) &&
                !P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "add" && c.Targets[0] == 1),
                "Borrowed Sword additions and removals publish complete ordered pairs; a new holder may reuse the original victim, but not the original holder.");
            Adjust(ref g, registry, use, branch, branch == "add" ? [3, 2] : [1, 2]);
            Reach(g, IsComplete); g = Cold(g, registry);
            var actual = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == use.Id);
            Require(actual.TargetSeats.SequenceEqual(branch == "add" ? new[] { 1, 2, 3, 2 } : []) &&
                weapons.All(pair => g.CardMovements.Count(m => m.CardId == pair.Value && m.From == CardLocation.Equipment(pair.Key) &&
                    m.To == CardLocation.Processing && m.Reason == CardMoveReasons.BorrowedSwordGive) == (branch == "add" ? 1 : 0)) &&
                weapons.All(pair => g.CardMovements.Count(m => m.CardId == pair.Value && m.From == CardLocation.Processing &&
                    m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.BorrowedSwordGive) == (branch == "add" ? 1 : 0)),
                "The Slash-less/counterspell-free fixed deck makes both retained pairs pay their real weapons once; removing the only pair pays neither: " + Diagnostic(g));
            Reach(g, IsPlay); AssertFinished(g, use, [material]); _ = Cold(g, registry);
        }
    }

    public static void EquipmentDrawnEntitiesRemainExcludedAcrossReturnAndRegrantUntilNewGain()
    {
        var (g, registry) = Start(Scenario.Equipment); var gained = Draw(g); var material = gained[0];
        Play(g, Action(g, LegalActionKind.Equip, material)); Reach(g, IsSkillOffer);
        var use = CurrentUse(g, material); AssertQualification(g, use, [material]);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, IsGain);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.RoundGainedEquipmentDrawReceipt is not null);
        var receipt = parent.RoundGainedEquipmentDrawReceipt!;
        var child = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == Gain);
        var movement = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(f => f.Id == child.WindowContext!.ParentFrameId);
        var issued = E<RoundGainedEquipmentDrawIssuedEvent>(g).Single(e => e.ProgramFrameId == parent.Id);
        Require(receipt.InstructionIndex == 1 && receipt.DrawActual == 1 && receipt.DrawnCardIds.Count == 1 &&
            receipt.Qualification.CardUseFrameId == use.Id && issued.Qualification.ActionId == use.Action!.ActionId &&
            issued.DrawnCardIds.SequenceEqual(receipt.DrawnCardIds) && issued.MovementSequenceBefore == receipt.MovementSequenceBefore &&
            issued.MovementSequenceAfter == receipt.MovementSequenceAfter && issued.DrawRoundNumber == receipt.DrawRoundNumber &&
            movement.Batch.ParentFrameId == parent.Id && movement.Batch.OriginOwnerSeat == 0 && movement.Batch.OriginSkillId == Skill &&
            movement.Batch.Movements is [var draw] && draw.CardId == receipt.DrawnCardIds.Single() &&
            draw.From == CardLocation.DrawPile && draw.To == CardLocation.Hand(0) && draw.Reason.Value == DrawReason &&
            draw.Sequence > receipt.MovementSequenceBefore && draw.Sequence <= receipt.MovementSequenceAfter,
            "The actual equipment reward issues its exact one-entity exclusion before the native gain child, with the original qualified use and typed parent intact.");
        Frozen(receipt.DrawnCardIds); Frozen(issued.DrawnCardIds); FreezeQualification(issued.Qualification); Frozen(movement.Batch.Movements);
        var marked = receipt.DrawnCardIds.Single(); var retainedView = g.CreateSnapshot(0); var retained = SnapshotJson.Serialize(retainedView);
        Private(g); g = Cold(g, registry); Reach(g, IsComplete); g = Cold(g, registry); Reach(g, IsPlay);
        AssertEquipmentFinished(g, use, material);
        Require(E<RoundGainedEquipmentDrawResolvedEvent>(g).Count(e => e.ProgramFrameId == parent.Id && e.DrawActual == 1) == 1 &&
            E<RoundGainedEquipmentDrawIssuedEvent>(g).Count(e => e.Qualification.CardUseFrameId == use.Id) == 1 && SnapshotJson.Serialize(retainedView) == retained,
            "The reward and native equipment use return exactly once after their real child, with one frozen issued draw.");
        AssertExcluded(g, marked);
        Require(g.GetHumanLegalActions().Any(a => a.CardId == gained[1] && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
            "Another ordinary current-round acquisition still has the same basic range bonus; the exclusion is attached only to the actual skill-drawn entity.");
        ReturnThroughForeignHand(ref g, registry, marked); AssertExcluded(g, marked);
        var previousInstance = use.RoundGainedUseQualifications!.Single().Source.SkillInstanceId;
        Use(g, "remove", [], []); Reach(g, IsPlay);
        Require(V(g, 0).Skills!.All(s => s.Id != Skill), "The real terminal source-replacement instruction removes the original Zhuosheng source.");
        Use(g, "regain", [], []); Reach(g, IsPlay); AssertExcluded(g, marked);
        Require(V(g, 0).SkillRuntimeStates!.Single(s => s.SkillId == Skill).IsAcquired,
            "The legal reacquisition creates a genuine acquired source; it does not erase the owner/skill/policy/round entity exclusion.");
        var round = E<RoundStartedEvent>(g).Last().RoundNumber;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Reach(g, p => IsPlay(p) && E<RoundStartedEvent>(g).Last().RoundNumber > round);
        Require(V(g, 0).Hand.Any(c => c.Id == marked), "The quiet native peer turns retain the exact marked entity into the next real round.");
        AssertExcluded(g, marked);
        ReturnThroughForeignHand(ref g, registry, marked);
        Require(g.GetHumanLegalActions().Any(a => a.CardId == marked && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
            "After the next real round starts, a new true hand acquisition qualifies the entity; merely carrying it into that round did not.");
        Play(g, Action(g, LegalActionKind.Equip, marked)); Reach(g, IsSkillOffer);
        var later = CurrentUse(g, marked); AssertQualification(g, later, [marked]);
        Require(later.RoundGainedUseQualifications!.Single().RoundNumber > receipt.Qualification.RoundNumber &&
            later.RoundGainedUseQualifications!.Single().Source.SkillInstanceId != previousInstance,
            "The independently reacquired source freezes a new-round receipt without borrowing the earlier instance or earlier round.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, IsGain);
        Private(g); g = Cold(g, registry); Reach(g, IsComplete); Reach(g, IsPlay); AssertEquipmentFinished(g, later, marked);
        Require(E<RoundGainedEquipmentDrawIssuedEvent>(g).Length == 2 && E<RoundGainedEquipmentDrawResolvedEvent>(g).Length == 2 &&
            g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == DrawReason) == 2,
            "The two independently eligible actual equipment uses issue two actual draws and resolve twice, with no repeated draw across either cold return.");
        _ = Cold(g, registry);
    }

    private static void AssertExcluded(GameEngine g, int id) => Require(!g.GetHumanLegalActions().Any(a =>
        a.CardId == id && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([2])),
        "An excluded or not-yet-reacquired material cannot obtain the opposite-seat Slash bonus: " + Diagnostic(g));

    private static void AssertLoaderBoundaries()
    {
        var assembly = typeof(StandardContentPackage).Assembly;
        using var rr = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-shi-bao.rules.json")!);
        using var pp = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-shi-bao.presentation.json")!);
        var rules = rr.ReadToEnd(); var presentation = pp.ReadToEnd();
        foreach (var mutation in new Action<JsonNode>[]
        {
            n => n["skills"]![0]!["triggers"]![0]!["window"] = "cardUseCompleted",
            n => n["skills"]![0]!["triggers"]![0]!["includeResponseUses"] = true,
            n => n["skills"]![0]!["triggers"]![1]!["effects"]![0]!["amount"] = 1,
            n => n["skills"]![0]!["cardPolicies"] = new JsonArray()
        })
        {
            var changed = JsonNode.Parse(rules)!; mutation(changed); var rejected = false;
            try { _ = SkillProgramCatalog.Load(changed.ToJsonString(), presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Wrong windows, pure-response use, invented draw amounts and an unpaired round-source policy must reject at the shared loader boundary.");
        }
    }

    private static CardUseFrame BeginAdjustment(ref GameEngine g, ContentRegistry registry, LegalAction action)
    {
        Play(g, action); Reach(g, IsSkillOffer);
        var use = CurrentUse(g, action.CardId!.Value); AssertQualification(g, use, [action.CardId.Value]);
        Private(g); g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, IsAdjustment);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.RoundGainedTrickTargetDraft is not null);
        var draft = parent.RoundGainedTrickTargetDraft!; var offered = E<RoundGainedTrickTargetOfferedEvent>(g).Single(e => e.ProgramFrameId == parent.Id);
        Require(draft.CardUseFrameId == use.Id && draft.ActionId == use.Action!.ActionId && draft.InstructionIndex == 1 &&
            offered.Qualification.CardUseFrameId == use.Id && offered.OriginalTargetSeats.SequenceEqual(draft.OriginalTargetSeats) &&
            offered.Choices.Select(c => c.Id).SequenceEqual(P(g)!.Choices.Select(c => c.Id)) && P(g)!.Choices.All(c => c.Cards.Count == 0),
            "The public target choice retains its exact frozen material qualification, original native use and published choices.");
        Frozen(draft.OriginalTargetSeats); FreezeChoices(draft.Choices); Frozen(offered.OriginalTargetSeats); FreezeChoices(offered.Choices);
        FreezeQualification(offered.Qualification); g = Cold(g, registry); return use;
    }

    private static void Adjust(ref GameEngine g, ContentRegistry registry, CardUseFrame use, string branch, IReadOnlyList<int> targets)
    {
        var p = P(g)!; var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("branch") == branch && c.Targets.SequenceEqual(targets))
            ?? throw new InvalidOperationException("Missing real round-gained target choice: " + Diagnostic(g));
        Reject(g, new AnswerPromptCommand(1, p.PromptId, choice.Id, g.Revision));
        Reject(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision - 1));
        Reject(g, new AnswerPromptCommand(0, p.PromptId, new ChoiceId("forged-round-gained-target"), g.Revision));
        var snapshot = g.CreateSnapshot(0); var frozen = SnapshotJson.Serialize(snapshot);
        Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision));
        Require(SnapshotJson.Serialize(snapshot) == frozen, "An already prepared private snapshot cannot be changed by resolving its public target choice.");
        var parentId = E<RoundGainedTrickTargetOfferedEvent>(g).Single(e => e.Qualification.CardUseFrameId == use.Id).ProgramFrameId;
        var resolved = E<RoundGainedTrickTargetResolvedEvent>(g).Single(e => e.ProgramFrameId == parentId);
        Require(resolved.Qualification.CardUseFrameId == use.Id && resolved.Qualification.ActionId == use.Action!.ActionId &&
            (branch == "add" ? resolved.AddedTargetSeats.SequenceEqual(targets) && resolved.RemovedTargetSeats.Count == 0 :
             branch == "remove" ? resolved.RemovedTargetSeats.SequenceEqual(targets) && resolved.AddedTargetSeats.Count == 0 :
             resolved.AddedTargetSeats.Count == 0 && resolved.RemovedTargetSeats.Count == 0),
            "The actual one-choice result freezes exactly the selected add/remove/decline branch for the original action.");
        Frozen(resolved.OriginalTargetSeats); Frozen(resolved.AddedTargetSeats); Frozen(resolved.RemovedTargetSeats); Frozen(resolved.ResultTargetSeats);
        FreezeQualification(resolved.Qualification); Private(g); g = Cold(g, registry);
    }

    private static void AssertQualification(GameEngine g, CardUseFrame use, IReadOnlyList<int> ids)
    {
        Require(use.RoundGainedUseQualifications is { Count: 1 }, "Exactly one formal skill qualifies the actual use: " + Diagnostic(g));
        var q = use.RoundGainedUseQualifications!.Single();
        Require(q.Source.SkillId == Skill && q.Source.OwnerSeat == 0 && q.PolicyId == "zhuosheng" && q.Source.BindingId == q.PolicyId &&
            q.CardUseFrameId == use.Id && q.ActionId == use.Action!.ActionId && q.ActorSeat == 0 && q.EffectiveKind == use.CardKind &&
            q.MaterialGains.Select(m => m.CardId).SequenceEqual(ids) && use.Action.PhysicalCards.Select(m => m.CardId).SequenceEqual(ids) &&
            q.RoundNumber == E<RoundStartedEvent>(g).Last().RoundNumber && q.MaterialGains.All(m => m.MovementSequence > q.RoundStartMovementSequence &&
                g.CardMovements.Any(actual => actual.CardId == m.CardId && actual.Sequence == m.MovementSequence && actual.To == CardLocation.Hand(0))) &&
            E<RoundGainedUseQualifiedEvent>(g).Count(e => e.Qualification.CardUseFrameId == use.Id) == 1,
            "All and only the physical materials have exact current-round acquisition sequences, original source, actor and owning use proof.");
        Frozen(use.RoundGainedUseQualifications!); FreezeQualification(q);
        FreezeQualification(E<RoundGainedUseQualifiedEvent>(g).Single(e => e.Qualification.CardUseFrameId == use.Id).Qualification);
    }

    private static void ReturnThroughForeignHand(ref GameEngine g, ContentRegistry registry, int id)
    {
        Require(V(g, 1).HandCount == 0, "The fixed recipient has an actually empty Hand before the one-card transfer.");
        Use(g, "give", [id], [1]); Reach(g, IsPlay);
        Require(V(g, 1).HandCount == 1 && g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)),
            "The exact physical entity first leaves the owner through a real native gift.");
        Use(g, "take", [], [1]); Reach(g, p => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        Require(P(g) is { PlayerSeat: 0, IsPrivate: true } && P(g)!.Choices is [var choice] && choice.Cards.Count == 0,
            "The genuine foreign-hand selection publishes one opaque slot, never the foreign physical identity.");
        Private(g); g = Cold(g, registry); Answer(g, _ => true); Reach(g, IsPlay);
        Require(V(g, 1).HandCount == 0 && V(g, 0).Hand.Any(c => c.Id == id) &&
            g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)),
            "The same entity returns by a real selected-owner payment and actual hand-gain movement.");
    }

    private static int[] Draw(GameEngine g)
    {
        var before = g.CardMovements.LastOrDefault()?.Sequence ?? 0; Use(g, "draw-four", [], []); Reach(g, IsPlay);
        var ids = g.CardMovements.Where(m => m.Sequence > before && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0)).Select(m => m.CardId).ToArray();
        Require(ids.Length == 4 && ids.Distinct().Count() == 4, "One real driver draw acquires four distinct physical hand entities during the actual round."); return ids;
    }

    private static (GameEngine, ContentRegistry) Start(Scenario scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 4,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, IsPlay);
        Require(V(g, 0).HandCount == 4 && Enumerable.Range(1, 3).All(s => V(g, s).HandCount == 0) && E<RoundStartedEvent>(g).Length > 0,
            "Seed31 and the explicit owner-only initial-hand modifier provide four old materials, three empty native peers and a real initial round.");
        Private(g); return (g, registry);
    }
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool IsPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static bool IsComplete(PendingDecision? p) => p?.SkillPrompt?.SkillId == Complete && p.PlayerSeat == 0;
    private static bool IsGain(PendingDecision? p) => p?.SkillPrompt?.SkillId == Gain && p.PlayerSeat == 0;
    private static bool IsSkillOffer(PendingDecision? p) => p?.SkillPrompt?.SkillId == Skill && p.PlayerSeat == 0 && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool IsAdjustment(PendingDecision? p) => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "round-gained-trick-target") == true;
    private static CardUseFrame CurrentUse(GameEngine g, int card) => g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == card);
    private static LegalAction Action(GameEngine g, LegalActionKind kind, int card, IReadOnlyList<int>? targets = null, string? binding = null) =>
        g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == kind && a.CardId == card && (targets is null || a.TargetSeats.SequenceEqual(targets)) &&
            (binding is null || a.ConversionSource?.BindingId == binding)) ?? throw new InvalidOperationException("Missing real action " + kind + ": " + Diagnostic(g));
    private static void Play(GameEngine g, LegalAction a)
    {
        if (a.Kind == LegalActionKind.Recast) Accept(g, new RecastCardCommand(0, a.CardId!.Value, g.Revision, P(g)!.PromptId) { ConversionSource = a.ConversionSource });
        else Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
            { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    private static void Use(GameEngine g, string activation, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards, targets, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    {
        var p = P(g)!; Require(p.PlayerSeat == 0, "Only the real human chooser submits a prompt answer.");
        var choice = p.Choices.FirstOrDefault(match) ?? throw new InvalidOperationException("Missing published choice: " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> ready)
    {
        for (var i = 0; i < 300; i++)
        {
            if (ready(P(g))) return;
            Require(!IsPlay(P(g)), "The real Shi Bao operation returned to human Play before its required boundary: " + Diagnostic(g)); Step(g);
        }
        throw new InvalidOperationException("The bounded native fixture missed its required boundary: " + Diagnostic(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is not { PlayerSeat: 0 }) { Accept(g, new AdvanceOneStepCommand(g.Revision)); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) { Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); return; }
        if (p.Kind is DecisionKind.Nullification or DecisionKind.RespondDodge or DecisionKind.RespondSlash) { Answer(g, c => c.Cards.Count == 0); return; }
        if (p.Kind == DecisionKind.SelectTargetCard) { Answer(g, _ => true); return; }
        if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); return; }
        throw new InvalidOperationException("Unexpected real human boundary: " + Diagnostic(g));
    }
    private static void AssertFinished(GameEngine g, CardUseFrame use, IReadOnlyList<int> ids)
    {
        var cancellation = E<RoundGainedTrickTargetResolvedEvent>(g).SingleOrDefault(e => e.Qualification.CardUseFrameId == use.Id &&
            e.Qualification.ActionId == use.Action!.ActionId && e.Source.SkillId == Skill && e.Source.OwnerSeat == 0 &&
            e.AddedTargetSeats.Count == 0 && e.RemovedTargetSeats.Count == 1 &&
            e.RemovedTargetSeats.SequenceEqual(e.OriginalTargetSeats) && e.ResultTargetSeats.Count == 0);
        var finish = use.CardKind == CardKind.IronChain && cancellation is null ? CardMoveReasons.IronChainFinished : CardMoveReasons.UseFinished;
        Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id && e.CardKind == use.CardKind) == 1 &&
            ids.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == finish) == 1) &&
            !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id), "Each original material pays once, finishes once and leaves no pending native use: " + Diagnostic(g));
    }
    private static void AssertEquipmentFinished(GameEngine g, CardUseFrame use, int id) => Require(
        E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id && e.CardKind == CardKind.SilverLion) == 1 &&
        g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.EquipmentUse) == 1 &&
        g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.Equipment(0) && m.Reason == CardMoveReasons.EquipmentEnter) == 1 &&
        !g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == use.Id), "The original equipment entity pays and enters its real slot once, then finishes exactly once.");
    private static void Accept(GameEngine g, GameCommand c)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "A real Shi Bao command was rejected."); }
    private static void Reject(GameEngine g, GameCommand c)
    { var before = State(g); Require(!g.Submit(c).Accepted && State(g) == before, "Invalid actor, stale choice and illegal material commands reject atomically."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), g.CardMovements, Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Require(State(copy) == State(g), "Cold replay retains all material gains, target branches, private views and paid child returns."); return copy; }
    private static void Private(GameEngine g)
    { foreach (var viewer in Enumerable.Range(0, 4)) foreach (var p in g.CreateSnapshot(viewer).Players.Where(p => p.Seat != viewer)) Require(p.Hand.Count == 0, "No prepared player view exposes another player's private hand."); }
    private static void FreezeQualification(RoundGainedUseQualification q) => Frozen(q.MaterialGains);
    private static void FreezeChoices(IReadOnlyList<PromptChoice> choices)
    { Frozen(choices); foreach (var c in choices) { Frozen(c.Cards); Frozen(c.Targets); if (c.Parameters is IDictionary<string, string> d) { var rejected = false; try { d["mutation"] = "invalid"; } catch (NotSupportedException) { rejected = true; } Require(rejected, "Nested published choice parameters are immutable."); } } }
    private static void Frozen<T>(IReadOnlyList<T> list)
    { if (list is not IList<T> writable) return; var rejected = false; try { if (writable.Count > 0) writable[0] = writable[0]; else writable.Add(default!); } catch (NotSupportedException) { rejected = true; } Require(rejected, "Published nested collections must be immutable."); }
    private static string Diagnostic(GameEngine g)
    {
        var prompt = P(g);
        return JsonSerializer.Serialize(new
        {
            Prompt = prompt is null ? null : new { prompt.Kind, prompt.PlayerSeat, Skill = prompt.SkillPrompt?.SkillId,
                Choices = prompt.Choices.Take(12).Select(c => new { c.Id, Action = c.Parameters.GetValueOrDefault("program-action"), Branch = c.Parameters.GetValueOrDefault("branch"), c.Cards, c.Targets }) },
            Players = g.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.HandCount, Equipment = p.Equipment.Select(c => c.Id) }),
            Hand = V(g, 0).Hand.Select(c => new { c.Id, c.Kind }),
            Legal = g.GetHumanLegalActions().Take(16).Select(a => new { a.Kind, a.CardId, a.TargetSeats, a.ConversionSource }),
            Frames = g.ResolutionStack.TakeLast(10).Select(f => new { f.Id, Type = f.GetType().Name, Use = f is CardUseFrame u ?
                new { u.CardId, u.CardKind, u.TargetSeats, u.PhysicalCardIds, ActionId = u.Action?.ActionId, ActionMaterials = u.Action?.PhysicalCards.Select(c => c.CardId) } : null }),
            Movements = g.CardMovements.TakeLast(8), Facts = g.Events.TakeLast(6).Select(e => new { Type = e.Payload.GetType().Name, Payload = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) })
        });
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(Scenario scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ordinary-shi-bao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            if (scenario is Scenario.BorrowedSword or Scenario.Equipment)
                b.AddCard(StandardContentRegistry.CreateWithClassicGenerals().GetCard(scenario == Scenario.Equipment ? "classic:silver-lion" : "classic:borrowed-sword"));
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rr = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-shi-bao.rules.json")!);
            using var pp = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-shi-bao.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rr.ReadToEnd(), pp.ReadToEnd());
            foreach (var (id, program) in formal.Programs) b.AddSkill(new(id, formal.Presentations[id].Name, formal.Presentations[id].Description) { Program = program, ProgramPresentation = formal.Presentations[id] });
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:shi-bao-driver","revision":1,"viewAs":[
                {"id":"as-slash","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"allowSameKind":true,"outputKind":"slash","forPlay":true,"forResponse":false},
                {"id":"two-slash","inputKinds":[],"inputSuits":[],"inputCount":2,"sourceZones":["hand"],"allowSameKind":true,"outputKind":"slash","forPlay":true,"forResponse":false},
                {"id":"as-borrowed","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"borrowedSword","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],
                "activations":[
                  {"id":"draw-four","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":4}]},
                  {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]},
                  {"id":"take","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","awaitMovementTriggers":true}]},
                  {"id":"pair-slash","minCards":2,"maxCards":2,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useSelectedCardsAs","target":"selectedTarget","sourceBind":"two-slash","outputKind":"slash"}]},
                  {"id":"zero-slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"}]},
                  {"id":"equip-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
                  {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:zhuosheng"],"sourceBind":"fixture:shi-bao-inert"}]},
                  {"id":"regain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:zhuosheng"]}]}]},
              {"id":"fixture:shi-bao-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":4,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:shi-bao-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:shi-bao-complete","revision":1,"triggers":[{"id":"actual-completion","window":"cardUseCompleted","ownerRelation":"actor","singleActionInstance":true,"cardCategories":["basic","instantTrick","equipment"],"priority":100,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:shi-bao-gain","revision":1,"triggers":[{"id":"actual-reward-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.round-gained.equipment-draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var skills = rules["skills"]!.AsArray();
            if (scenario != Scenario.BorrowedSword) skills[0]!["viewAs"]!.AsArray().RemoveAt(2);
            var labels = skills.ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            { var id = n!["id"]!.GetValue<string>(); var label = new Dictionary<string, object> { ["name"] = id, ["description"] = "固定小牌库的真实获牌和原生返回" };
                if (id is Complete or Gain) label["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" }; return (object)label; });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实获牌共享机制") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Inert, "替换来源", "无运行能力"));
            b.AddGeneral(new(Owner, "真实擢升拥有者", "supporter", Driver, "jin", 9, [Skill, Complete, Gain, "fixture:shi-bao-initial"]));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:shi-bao-peer-{i}", "原生空手角色", "supporter", "fixture:shi-bao-quiet", "wei", 9));
            var card = scenario switch { Scenario.Slash => "standard:slash", Scenario.Alcohol => "standard:alcohol", Scenario.IronChain => "standard:iron_chain",
                Scenario.DrawTwo => "standard:draw_two", Scenario.BorrowedSword => "standard:crossbow", _ => "classic:silver-lion" };
            // Uniform equipment is deliberately Slash-less and counterspell-free; no seed search or zone injection is needed.
            b.AddDeck(new("fixture:shi-bao-deck", "真实固定小牌库", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard(card, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "本轮获牌的真实使用", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:shi-bao-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, "fixture:shi-bao-peer-1", "fixture:shi-bao-peer-2", "fixture:shi-bao-peer-3"]));
        }
    }
}
