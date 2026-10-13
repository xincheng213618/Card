using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static partial class SoleTargetProtectionChecks
{
    private const string Skill = "ol:zhenwei", Driver = "fixture:sole-driver", TargetPause = "fixture:sole-target-pause";
    private const string GainPause = "fixture:sole-return-pause", SourceLoss = "fixture:sole-cost-loss", Replacement = "fixture:sole-replacement";
    private const string Mode = "identity:classic-sole-protection";

    public static void SubstitutionChangesActualSlashAndBlackTrickTarget()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.Duel, CardKind.Snatch, CardKind.DrawTwo, CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Lightning })
        {
            var (g, registry) = Start(kind, substitute: true);
            var protector = Protector(g); var action = FindAction(g, kind, protector);
            Play(g, action); Reach(g, p => p?.SkillPrompt?.SkillId == TargetPause && p.PlayerSeat == protector);
            var use = g.ResolutionStack.OfType<CardUseFrame>().SingleOrDefault(u => u.CardId == action.CardId) ??
                throw new InvalidOperationException("Expected live substituted use for " + kind + ": " + JsonSerializer.Serialize(new
                { action.CardId, stack = g.ResolutionStack, facts = g.Events.Select(e => e.Payload).OfType<SoleTargetProtectionPaidEvent>(), prompt = P(g) }));
            var replacement = Facts<SoleTargetReplacedEvent>(g).Single(e => e.CardUseFrameId == use.Id);
            Require(use.Action is { } actual && actual.ActionId == replacement.ActionId && actual.ActorSeat == 0 && actual.ProviderSeat == 0 &&
                actual.TargetSeats.SequenceEqual([protector]) && use.TargetSeats.SequenceEqual([protector]) &&
                (use.CardAttack is null || use.CardAttack.TargetSeat == protector) &&
                Facts<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == TargetPause && e.OwnerSeat == protector) == 1 &&
                !Facts<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == TargetPause && e.OwnerSeat == replacement.PreviousTargetSeat),
                "Substitution retains one real use and retargets native resolution plus new-target confirmation without executing removed target hooks.");
            g = Cold(g, registry); Reach(g, IsPlay);
            Require(Facts<SoleTargetProtectionPaidEvent>(g).Count(e => e.Receipt.CardUseFrameId == use.Id) == 1 &&
                Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                !Facts<SoleTargetQuarantineIssuedEvent>(g).Any(e => e.Receipt.CardUseFrameId == use.Id),
                "Cost/draw child cold returns neither repeat payment nor switch the selected substitution branch.");
            if (kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning)
                Require(g.CreateCardZoneDiagnostics().Single(z => z.CardId == action.CardId).Location == CardLocation.Judgment(protector),
                    "Black delayed placement uses the replacement receiver's real judgment zone.");
            if (kind == CardKind.Snatch) Require(Facts<TargetCardSelectionRequestedEvent>(g).Any(e => e.ResolutionId == use.Id && e.TargetSeat == protector),
                "Native opaque Snatch card selection belongs to the replacement target.");
            Cold(g, registry);
        }
        var (chain, chainRegistry) = Start(CardKind.Duel, substitute: true, chain: true);
        var original = chain.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:sole-peer-1").Seat;
        var high = chain.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:sole-high-protector").Seat;
        var action2 = chain.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual([original]));
        Play(chain, action2); Reach(chain, p => p?.SkillPrompt?.SkillId == TargetPause && p.PlayerSeat == high);
        var replacements = Facts<SoleTargetReplacedEvent>(chain);
        Require(replacements.Length == 2 && replacements[0].PreviousTargetSeat == original && replacements[0].NewTargetSeat == Protector(chain) &&
            replacements[1].PreviousTargetSeat == Protector(chain) && replacements[1].NewTargetSeat == high &&
            Facts<SoleTargetProtectionPaidEvent>(chain).Select(e => e.Receipt.Source).Distinct().Count() == 2 &&
            chain.ResolutionStack.OfType<CardUseFrame>().Single().TargetSeats.SequenceEqual([high]),
            "A distinct second protection source can protect the replacement while each exact issuing source pays once.");
        chain = Cold(chain, chainRegistry); Reach(chain, IsPlay);
        Require(Facts<CardUseFinishedEvent>(chain).Count(e => e.ResolutionId == replacements[0].CardUseFrameId) == 1 &&
            Facts<SoleTargetProtectionPaidEvent>(chain).Length == 2,
            "Two-source cold continuations finish one actual use without re-offering either paid source."); Cold(chain, chainRegistry);
        var (redirect, redirectRegistry) = Start(CardKind.Slash, substitute: true, liuli: true);
        var receiver = Protector(redirect); var slash = FindAction(redirect, CardKind.Slash, receiver); var frozenRedirects = 0;
        redirect.EventCommitted += e =>
        {
            if (e.Payload is SoleTargetAfterProtectionRedirectedEvent fact)
            { Frozen(fact.BeforeTargets); Frozen(fact.AfterTargets); frozenRedirects++; }
        };
        Play(redirect, slash);
        Reach(redirect, p => p?.SkillPrompt?.SkillId == "boundary:lihuo" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(redirect, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(redirect, p => p?.SkillPrompt?.SkillId == "boundary:lihuo" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "convert"));
        Answer(redirect, c => c.Parameters.GetValueOrDefault("branch") == "convert");
        Reach(redirect, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        var redirectWindow = redirect.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.SoleTargetRedirectReturn is not null);
        Frozen(redirectWindow.SoleTargetRedirectReturn!.FinalizedCursor.Candidates);
        Require(redirectWindow.SoleTargetRedirectReturn!.FinalizedCursor.CandidateIndex > 0 &&
            Facts<SoleTargetProtectionPaidEvent>(redirect).Length == 1 && Facts<SoleTargetReplacedEvent>(redirect).Single().NewTargetSeat == receiver,
            "The replacement receiver starts its real Liuli window after the saved finalized cursor has advanced past the once-paid Zhenwei source.");
        redirect = Cold(redirect, redirectRegistry);
        AnswerPublished(redirect, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(redirect, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.Choices.Any(c => c.Targets.Count == 1));
        AnswerPublished(redirect, c => c.Targets is [var target] && target != 0 && target != receiver);
        Reach(redirect, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.Choices.Any(c => c.Cards.Count == 1));
        redirect = Cold(redirect, redirectRegistry);
        redirect.EventCommitted += e =>
        {
            if (e.Payload is SoleTargetAfterProtectionRedirectedEvent fact)
            { Frozen(fact.BeforeTargets); Frozen(fact.AfterTargets); frozenRedirects++; }
        };
        AnswerPublished(redirect, c => c.Cards.Count == 1);
        Reach(redirect, p => p?.SkillPrompt?.SkillId == TargetPause && p.PlayerSeat != receiver);
        var secondRedirect = Facts<SoleTargetAfterProtectionRedirectedEvent>(redirect).Single();
        var finalUse = redirect.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == secondRedirect.CardUseFrameId);
        Require(secondRedirect.PreviousTargetSeat == receiver && secondRedirect.NewTargetSeat != receiver && secondRedirect.NewTargetSeat != 0 &&
            secondRedirect.Source.SkillId == "classic:liuli" && finalUse.CardKind == CardKind.FireSlash && finalUse.CurrentSlashFirePolicy is { Converted: true, ExtraTargetSeat: null } &&
            finalUse.TargetSeats.SequenceEqual([secondRedirect.NewTargetSeat]) &&
            finalUse.Action!.TargetSeats.SequenceEqual(finalUse.TargetSeats) && finalUse.CardAttack!.TargetSeat == secondRedirect.NewTargetSeat &&
            (finalUse.PreparedTargetAttacks is null || finalUse.PreparedTargetAttacks.All(a => a.TargetSeat == secondRedirect.NewTargetSeat)) && frozenRedirects == 1 &&
            !Facts<ProgramBindingStartedEvent>(redirect).Any(e => e.SkillId == TargetPause && e.OwnerSeat == receiver),
            "Real Liuli performs a second target change and restores finalization against its actual receiver, skipping removed target hooks and freezing its mutation proof.");
        redirect = Cold(redirect, redirectRegistry); Reach(redirect, IsPlay);
        Require(Facts<SoleTargetProtectionPaidEvent>(redirect).Length == 1 &&
            Facts<ProgramBindingStartedEvent>(redirect).Count(e => e.SkillId == "classic:liuli") == 1 &&
            Facts<CardUseFinishedEvent>(redirect).Count(e => e.ResolutionId == finalUse.Id) == 1 &&
            redirect.CardMovements.Count(m => m.From.OwnerSeat == receiver && m.To == CardLocation.DiscardPile &&
                m.Reason.Value == "skill-program.classic:liuli.SelectAndMoveOwnedCard") == 1,
            "Cold Liuli and finalized returns retain one actual use and neither reopen Zhenwei nor pay the real redirect fee twice.");
        Cold(redirect, redirectRegistry);
    }

    public static void ReplacementLiuliReentersCompletedTargetEpisodeAfterColdRecovery()
    {
        var (g, registry) = Start(CardKind.Slash, substitute: true, liuli: true, liuliLoop: true);
        var a = Protector(g);
        var b = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:sole-peer-1").Seat;
        var last = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:sole-peer-2").Seat;
        var slash = g.GetHumanLegalActions().First(c => c.Kind == LegalActionKind.Slash && c.TargetSeats.SequenceEqual([b]));
        Play(g, slash);
        Reach(g, p => p?.SkillPrompt?.SkillId == "boundary:lihuo" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p?.SkillPrompt?.SkillId == "boundary:lihuo" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "convert"));
        Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "convert");
        Reach(g, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.PlayerSeat == b);
        AnswerPublished(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        var costs = new List<(int Owner, int Card)>();
        Redirect(a, b, expectedPaidEpisodes: 0);
        Redirect(b, a, expectedPaidEpisodes: 1);
        Redirect(a, last, expectedPaidEpisodes: 2);
        Reach(g, p => p?.SkillPrompt?.SkillId == TargetPause && p.PlayerSeat == last);
        var mutations = Facts<SoleTargetAfterProtectionRedirectedEvent>(g);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == mutations[0].CardUseFrameId);
        Require(mutations.Length == 3 && mutations.Select(e => e.PreviousTargetSeat).SequenceEqual([a, b, a]) &&
            mutations.Select(e => e.NewTargetSeat).SequenceEqual([b, a, last]) &&
            mutations.All(e => e.TargetIndex == 0 && e.CardUseFrameId == use.Id && e.ActionId == use.Action!.ActionId) &&
            mutations.Select(e => e.ProgramFrameId).Distinct().Count() == 3 &&
            use.TargetSeats.SequenceEqual([last]) && use.Action!.TargetSeats.SequenceEqual(use.TargetSeats) && use.CardAttack!.TargetSeat == last &&
            Facts<SoleTargetProtectionPaidEvent>(g).Length == 1 &&
            !Facts<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == TargetPause && (e.OwnerSeat == a || e.OwnerSeat == b)),
            "Three genuine becoming-target episodes use separate Liuli programs on one exact use/slot, and restore finalization for its actual last receiver without re-offering paid Zhenwei.");
        VerifyExactProtectedRedirectProofs(g, use, mutations);
        g = Cold(g, registry); Reach(g, IsPlay);
        Require(costs.Count == 3 && costs.Count(c => c.Owner == a) == 2 && costs.Count(c => c.Owner == b) == 1 &&
            costs.All(c => g.CardMovements.Count(m => m.CardId == c.Card && m.From == CardLocation.Hand(c.Owner) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.classic:liuli.SelectAndMoveOwnedCard") == 1) &&
            Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 && Facts<SoleTargetProtectionPaidEvent>(g).Length == 1,
            "Cold recovery pays A twice and B once for three distinct real Liuli episodes, with one Zhenwei cost and one whole-use finish.");
        Cold(g, registry);

        void Redirect(int owner, int destination, int expectedPaidEpisodes)
        {
            Reach(g, p => p?.SkillPrompt?.SkillId == TargetPause || IsPlay(p) ||
                p?.SkillPrompt?.SkillId == "classic:liuli" && p.PlayerSeat == owner &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            Require(P(g)?.SkillPrompt?.SkillId == "classic:liuli" && P(g)!.PlayerSeat == owner &&
                P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"),
                "A new becoming-target episode must re-offer Liuli even when the same exact source finished an earlier episode; completed redirects: " + expectedPaidEpisodes);
            var window = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.SoleTargetRedirectReturn is not null);
            Require(Facts<SoleTargetAfterProtectionRedirectedEvent>(g).Length == expectedPaidEpisodes &&
                window.Candidates[window.CandidateIndex].OwnerSeat == owner &&
                window.CandidateIndex >= window.SoleTargetRedirectReturn!.TargetEpisodeCandidateStartIndex &&
                window.Candidates[window.CandidateIndex].FrozenContext!.TargetSeat == owner &&
                window.SoleTargetRedirectReturn!.Request.TargetIndex == 0 &&
                g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == window.ParentFrameId).CardAttack!.TargetSeat == owner,
                "The published Liuli prompt belongs to the current real episode and active native slot.");
            g = Cold(g, registry); AnswerPublished(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.PlayerSeat == owner && p.Choices.Any(c => c.Targets.SequenceEqual([destination])));
            AnswerPublished(g, c => c.Targets.SequenceEqual([destination]));
            Reach(g, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.PlayerSeat == owner && p.Choices.Any(c => c.Cards.Count == 1));
            var card = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); costs.Add((owner, card));
            g = Cold(g, registry); AnswerPublished(g, c => c.Cards.SequenceEqual([card]));
        }
    }

    public static void BlackDelayedQuarantineFinishesCommittedUseAndReturnsAfterActualTurn()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.Duel, CardKind.DrawTwo, CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Lightning })
        {
            var (g, registry) = Start(kind);
            var action = FindAction(g, kind, Protector(g)); var frozen = 0;
            g.EventCommitted += e =>
            {
                if (e.Payload is SoleTargetProtectionPaidEvent paid) { Frozen(paid.Receipt.OriginalTargets); Frozen(paid.Receipt.Materials); frozen++; }
                if (e.Payload is SoleTargetQuarantineIssuedEvent issued) { Frozen(issued.Receipt.TargetSeats); Frozen(issued.Receipt.Materials); frozen++; }
                if (e.Payload is SoleTargetQuarantineSettledEvent settled) { Frozen(settled.CardIds); frozen++; }
            };
            string? firstIssued = null;
            g.EventCommitted += e => { if (e.Payload is SoleTargetQuarantineIssuedEvent issued) firstIssued = JsonSerializer.Serialize(issued); };
            Play(g, action); Reach(g, IsPlay);
            var r = Facts<SoleTargetQuarantineIssuedEvent>(g).SingleOrDefault()?.Receipt ??
                throw new InvalidOperationException("Expected issued quarantine for " + kind + ": " + JsonSerializer.Serialize(new
                { action.CardId, stack = g.ResolutionStack, paid = Facts<SoleTargetProtectionPaidEvent>(g), bindings = Facts<ProgramBindingResolvedEvent>(g) }));
            Require(r.ActorSeat == 0 && r.ProviderSeat == 0 && r.ActualTurnNumber == g.State.TurnNumber &&
                r.Materials.Select(m => m.CardId).SequenceEqual([action.CardId!.Value]) &&
                g.CreateCardZoneDiagnostics().Single(z => z.CardId == action.CardId).Location == CardLocation.OutsideGame &&
                Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == r.CardUseFrameId) == 1 &&
                !Facts<DamageAppliedEvent>(g).Any(e => e.TargetSeat == r.TargetSeats[0]) &&
                !Facts<SoleTargetQuarantineSettledEvent>(g).Any() && firstIssued == JsonSerializer.Serialize(new SoleTargetQuarantineIssuedEvent(r)) &&
                g.ObserverFailures.Count == 0,
                "An immutable actual material receipt isolates the paid use once, ends its effects, and stays outstanding through the play phase.");
            var relevant = g.Events.Where(e => e.Payload is CardActionAcceptedEvent a && a.Action.ActionId == r.ActionId ||
                e.Payload is ProgramBindingStartedEvent c && c.SkillId == "classic:benxi" && c.Window == SkillProgramTriggerWindow.CardUseCommitted ||
                e.Payload is CardUseFinishedEvent f && f.ResolutionId == r.CardUseFrameId).ToArray();
            Require(relevant.First(e => e.Payload is ProgramBindingStartedEvent).Sequence < relevant.First(e => e.Payload is CardActionAcceptedEvent).Sequence &&
                relevant.First(e => e.Payload is CardActionAcceptedEvent).Sequence < relevant.First(e => e.Payload is CardUseFinishedEvent).Sequence,
                "Actual commitment precedes protection finalization and real use completion.");
            Require(Facts<ProgramBindingResolvedEvent>(g).Any(e => e.SkillId == "classic:benxi" && e.BindingId == "use-shortens-turn-distance" && e.Completed),
                "An isolated black delayed Use still resolves the ordinary committed-use observer Benxi.");
            g = Cold(g, registry);
            g.EventCommitted += e => { if (e.Payload is SoleTargetQuarantineSettledEvent settled) Frozen(settled.CardIds); };
            End(g);
            Reach(g, p => p?.SkillPrompt?.SkillId == GainPause);
            var cursor = g.ResolutionStack.OfType<DeferredTurnEndFrame>().Single().SoleTargetQuarantineReturns!;
            Require(cursor.Index == 1 && cursor.MovementChildFrameId is not null &&
                g.CreateCardZoneDiagnostics().Single(z => z.CardId == action.CardId).Location == CardLocation.Hand(0) &&
                Facts<SoleTargetQuarantineSettledEvent>(g).Count(e => e.CardUseFrameId == r.CardUseFrameId) == 1 &&
                Facts<TurnEndedEvent>(g).Any(e => e.TurnNumber == r.ActualTurnNumber && e.ActorSeat == r.ActualTurnOwnerSeat),
                "Actual TurnEnded returns the same entity to its true user, with real gain children owned by DeferredTurnEnd.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Require(g.CardMovements.Count(m => m.CardId == action.CardId && m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(0)) == 1,
                "The actual end gain child cold return cannot duplicate the settled entity.");
            Cold(g, registry);
        }
        var (multi, multiRegistry) = Start(CardKind.Dodge, multi: true);
        Accept(multi, new UseProgramSkillCommand(0, Driver, "equip", [], [0], multi.Revision, P(multi)!.PromptId)); Reach(multi, IsPlay);
        if (multi.CreateSnapshot(0).Players[0].Hand.Count(c => c.Kind == CardKind.Dodge) < 2)
        { Accept(multi, new UseProgramSkillCommand(0, Driver, "draw", [], [], multi.Revision, P(multi)!.PromptId)); Reach(multi, IsPlay); }
        var target = multi.CreateSnapshot(0).Players.First(p => p.Seat != 0 && p.Seat != Protector(multi)).Seat;
        var pair = multi.CreateSnapshot(0).Players[0].Hand.Where(c => c.Kind == CardKind.Dodge).Take(2).Select(c => c.Id).ToArray();
        Accept(multi, new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [target], multi.Revision, P(multi)!.PromptId)); Reach(multi, IsPlay);
        var two = Facts<SoleTargetQuarantineIssuedEvent>(multi).Single().Receipt;
        Require(two.Materials.Select(m => m.CardId).SequenceEqual(pair) && pair.All(id => multi.CreateCardZoneDiagnostics().Single(z => z.CardId == id).Location == CardLocation.OutsideGame),
            "A true Zhangba Slash quarantines both real materials of one actual use.");
        multi = Cold(multi, multiRegistry); End(multi); Reach(multi, p => p?.SkillPrompt?.SkillId == GainPause); multi = Cold(multi, multiRegistry);
        Answer(multi, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Require(pair.All(id => multi.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(0)) == 1),
            "Both exact Zhangba materials return once through one actual end gain batch."); Cold(multi, multiRegistry);
    }

    public static void PaidProtectionSurvivesActualSourceLossWithoutRepayment()
    {
        var (g, registry) = Start(CardKind.SupplyShortage, sourceLoss: true);
        var protector = Protector(g); var action = FindAction(g, CardKind.SupplyShortage, protector);
        Play(g, action); Reach(g, IsPlay);
        var receipt = Facts<SoleTargetProtectionPaidEvent>(g).Single().Receipt;
        Require(Facts<ProgramOwnerSkillsReplacedEvent>(g).Any(e => e.OwnerSeat == protector) &&
            Facts<SoleTargetQuarantineIssuedEvent>(g).Count(e => e.Receipt.CardUseFrameId == receipt.CardUseFrameId) == 1 &&
            g.CardMovements.Count(m => m.CardId == receipt.Cost!.CardId && m.Reason.Value == "skill-program.sole-target-protection.cost") == 1,
            "A real discarded-cost dying/recovery replacement removes Zhenwei, while its paid receipt completes quarantine once: " +
                JsonSerializer.Serialize(new { receipt, replaced = Facts<ProgramOwnerSkillsReplacedEvent>(g),
                    issued = Facts<SoleTargetQuarantineIssuedEvent>(g), moves = g.CardMovements.Where(m => m.CardId == receipt.Cost!.CardId) }));
        g = Cold(g, registry); End(g); Reach(g, p => p?.SkillPrompt?.SkillId == GainPause); g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Require(Facts<SoleTargetQuarantineSettledEvent>(g).Count(e => e.CardUseFrameId == receipt.CardUseFrameId) == 1,
            "Source loss does not revoke the true user's already issued turn-end return."); Cold(g, registry);
        var (foreign, foreignRegistry) = Start(CardKind.Duel, ownSkill: true, foreignActor: true);
        var actor = Protector(foreign); var realAction = FindAction(foreign, CardKind.Duel, actor);
        Play(foreign, realAction); Reach(foreign, p => p?.SkillPrompt?.SkillId == "fixture:sole-actor-swap" && p.Choices.Any(c => c.Targets.Count == 1));
        Answer(foreign, c => c.Targets.SequenceEqual([actor]));
        Reach(foreign, p => p?.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Answer(foreign, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(foreign, p => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost") == true);
        foreign = Cold(foreign, foreignRegistry); Answer(foreign, c => c.Cards.Count == 1);
        Reach(foreign, p => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "quarantine") == true);
        Answer(foreign, c => c.Parameters.GetValueOrDefault("branch") == "quarantine"); Reach(foreign, IsPlay);
        var changed = Facts<SoleTargetQuarantineIssuedEvent>(foreign).Single().Receipt;
        Require(changed.ActorSeat == actor && changed.ProviderSeat == 0 && changed.Source.OwnerSeat == 0 && changed.Materials.Single().From == CardLocation.Hand(0),
            "Real role replacement preserves user/provider/material roles; the provider can protect a different user's sole target.");
        foreign = Cold(foreign, foreignRegistry); End(foreign); Reach(foreign, p => p?.SkillPrompt?.SkillId == GainPause && p.PlayerSeat == actor);
        Require(Facts<SoleTargetQuarantineSettledEvent>(foreign).Single().Destination == CardLocation.Hand(actor) &&
            foreign.CreateCardZoneDiagnostics().Single(z => z.CardId == realAction.CardId).Location == CardLocation.Hand(actor),
            "Actual end returns a provided entity to the true changed user, rather than its original provider/protector.");
        Cold(foreign, foreignRegistry);
    }

    public static void EligibilityRejectsRedTrickAndOwnUseAndPreservesPrivateCost()
    {
        foreach (var red in new[] { true, false })
        {
            var (g, registry) = Start(CardKind.Duel, red: red, ownSkill: !red);
            var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.Count == 1);
            Play(g, action); Reach(g, IsPlay);
            Require(!Facts<SoleTargetProtectionPaidEvent>(g).Any(), "Red tricks and the owner's own real use cannot pay Zhenwei."); Cold(g, registry);
        }
        var (privateGame, privateRegistry) = Start(CardKind.Slash, substitute: true);
        Play(privateGame, FindAction(privateGame, CardKind.Slash, Protector(privateGame)));
        Reach(privateGame, p => p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "sole-target-protection" && c.Parameters.GetValueOrDefault("branch") == "cost") == true);
        var p = P(privateGame)!;
        Require(p.IsPrivate && Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat).All(s => privateGame.CreateSnapshot(s).PendingDecision is null),
            "Only the actual protector sees real HE payment identities.");
        var state = GameCheckpointJson.Serialize(privateGame.CreateCheckpoint());
        Require(!privateGame.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, privateGame.Revision)).Accepted &&
            state == GameCheckpointJson.Serialize(privateGame.CreateCheckpoint()), "A foreign command rejects without payment or cursor mutation.");
        Cold(privateGame, privateRegistry);
        var (zero, zeroRegistry) = Start(CardKind.Dodge, zero: true);
        var target = zero.CreateSnapshot(0).Players.First(p => p.Seat != 0 && p.Seat != Protector(zero)).Seat;
        Answer(zero, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(zero, p => p?.SkillPrompt?.SkillId == "fixture:sole-legacy" && p.Choices.Any(c => c.Targets.Count == 1));
        Answer(zero, c => c.Targets.SequenceEqual([target])); Reach(zero, IsPlay);
        var receipt = Facts<SoleTargetQuarantineIssuedEvent>(zero).Single().Receipt;
        Require(receipt.Materials.Count == 0 && Facts<CardUseDeclaredEvent>(zero).Any(e => e.ResolutionId == receipt.CardUseFrameId && e.CardId == 0) &&
            Facts<CardUseFinishedEvent>(zero).Count(e => e.ResolutionId == receipt.CardUseFrameId) == 1,
            "An existing Action-null UseVirtualCard producer becomes visible to protection through the new catalog's exact native Use windows, with no invented physical materials.");
        zero = Cold(zero, zeroRegistry); End(zero); Reach(zero, _ => zero.State.CurrentSeat != 0);
        Require(Facts<SoleTargetQuarantineSettledEvent>(zero).Single(e => e.CardUseFrameId == receipt.CardUseFrameId).CardIds.Count == 0,
            "Zero-entity quarantine closes at actual turn end without manufacturing a gain or a card."); Cold(zero, zeroRegistry);
        LegacyZeroSlashWithCurrentEnhancementKeepsExactPrimaryReturn();
    }

    private static void LegacyZeroSlashWithCurrentEnhancementKeepsExactPrimaryReturn()
    {
        var (g, registry) = Start(CardKind.Dodge, zero: true, fullBenxi: true);
        var protector = Protector(g);
        var peers = g.CreateSnapshot(0).Players.Where(p => p.Seat != 0 && p.Seat != protector).Select(p => p.Seat).ToArray();
        var target = peers[0]; var extra = peers[1];
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p?.SkillPrompt?.SkillId == "fixture:sole-legacy" && p.Choices.Any(c => c.Targets.SequenceEqual([target])));
        Answer(g, c => c.Targets.SequenceEqual([target]));
        Reach(g, p => p?.SkillPrompt?.SkillId == "classic:benxi" && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("enhancement-option") == nameof(CurrentCardEnhancement.ExtraTarget)));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == 0 && u.CardKind == CardKind.Slash);
        var useId = use.Id; var parentId = use.CardAttack!.ProgramSkillCardUseFrameId!.Value;
        Require(use.ProgramUseCommitted && use.PhysicalCardIds is { Count: 0 } && use.TargetSeats.SequenceEqual([target]) &&
            use.Action is { PhysicalCards.Count: 0, ConversionChain.Count: 1 } &&
            Facts<TargetsConfirmedEvent>(g).Single(e => e.ResolutionId == useId).TargetSeats.SequenceEqual([target]),
            "A real legacy zero-material Slash exposes its exact issuer and initial singleton to mature Benxi's committed window.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("enhancement-option") == nameof(CurrentCardEnhancement.ExtraTarget));
        Reach(g, p => p?.SkillPrompt?.SkillId == "classic:benxi" && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("enhancement-option") == "target" && c.Targets.SequenceEqual([extra])));
        Answer(g, c => c.Parameters.GetValueOrDefault("enhancement-option") == "target" && c.Targets.SequenceEqual([extra]));
        Reach(g, p => p?.SkillPrompt?.SkillId == "classic:benxi" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("enhancement-option") == "finish"));
        use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == useId);
        Require(use.TargetSeats.SequenceEqual([target, extra]) && use.Action!.TargetSeats.SequenceEqual([target, extra]) &&
            Facts<CurrentCardEnhancedEvent>(g).Single(e => e.CardUseFrameId == useId && e.Enhancement == CurrentCardEnhancement.ExtraTarget)
                is { ExtraTargetSeat: var added, OwnerSeat: 0 } && added == extra,
            "The actual mature ExtraTarget choice extends the original owning use without changing its zero physical cost.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("enhancement-option") == "finish");
        Reach(g, p => p?.SkillPrompt?.SkillId == TargetPause);
        use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == useId);
        var parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
        Require(use.ProgramUseAccepted && use.TargetSeats.SequenceEqual([target, extra]) && parent.SelectedTargetSeats.SequenceEqual([target]) &&
            Facts<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == use.Action!.ActionId).Action.TargetSeats.SequenceEqual([target, extra]) &&
            !Facts<SoleTargetProtectionPaidEvent>(g).Any(e => e.Receipt.CardUseFrameId == useId) &&
            !Facts<SoleTargetQuarantineIssuedEvent>(g).Any(e => e.Receipt.CardUseFrameId == useId),
            "The true finalized two-target use correctly disqualifies sole-target protection while retaining the original producer selection.");
        var method = typeof(GameEngine).GetMethod("TryGetCurrentEnhancementVirtualPrimaryReturn",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var issuer = use.Action!.ConversionChain.Single();
        var before = GameCheckpointJson.Serialize(g.CreateCheckpoint());
        Require(Proven(use, parent) &&
            !Proven(use with { CardAttack = use.CardAttack! with { ProgramSkillCardUseFrameId = parentId + 1000 } }, parent) &&
            !Proven(use with { Id = useId + 1000 }, parent) &&
            !Proven(use with { TargetSeats = [extra, target] }, parent) &&
            !Proven(use with { TargetSeats = [target, target] }, parent) &&
            !Proven(use with { TargetSeats = [target, protector] }, parent) &&
            !Proven(use with { Enhancements = CurrentCardEnhancement.None }, parent) &&
            !Proven(WithChain([]), parent) && !Proven(WithChain([issuer, issuer]), parent) &&
            !Proven(WithChain([issuer with { BindingId = "foreign-binding" }]), parent) &&
            !Proven(use, parent with { SkillInstanceId = "foreign-producer" }) &&
            before == GameCheckpointJson.Serialize(g.CreateCheckpoint()),
            "The exact primary proof rejects borrowed parents and uses, swapped or duplicated targets, an unproved later tail, absent enhancement issuance and foreign instances without mutation.");
        g = Cold(g, registry);
        Reach(g, p => p?.SkillPrompt?.SkillId == "fixture:sole-legacy" && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("result-bind") == "legacy-return"));
        parent = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
        Require(parent.SelectedTargetSeats.SequenceEqual([target]) && !g.ResolutionStack.Any(f => f.Id == useId) &&
            Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 &&
            Facts<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == TargetPause && (e.OwnerSeat == target || e.OwnerSeat == extra)) == 2 &&
            !Facts<SoleTargetProtectionPaidEvent>(g).Any(e => e.Receipt.CardUseFrameId == useId) && g.CardMovements.All(m => m.CardId != 0),
            "Both real response cursors finish once and return one primary to the next actual instruction, with no protection cost or synthetic card movement.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Reach(g, IsPlay);
        Require(Facts<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == "fixture:sole-legacy" && e.Completed) == 1 &&
            !g.ResolutionStack.Any(f => f.Id == parentId || f.Id == useId),
            "The cold-restored typed whole-use return resumes and completes its original legacy binding once."); Cold(g, registry);

        bool Proven(CardUseFrame candidate, ProgramSkillFrame producer)
        {
            object?[] arguments = [candidate, producer, null];
            return (bool)method.Invoke(g, arguments)! && arguments[2] is IReadOnlyList<int> primary && primary.SequenceEqual([target]);
        }
        CardUseFrame WithChain(IReadOnlyList<CardConversionSource> chain)
        {
            var action = use.Action!;
            return use with { Action = new CardActionContext(action.ActionId, action.ParentActionId, action.Type,
                action.ActorSeat, action.ProviderSeat, action.RequesterSeat, action.ResponderSeat, action.OpponentSeat,
                action.EffectiveKind, action.TargetSeats, action.PhysicalCards, chain, action.DesignatedTargetSeats,
                action.EffectiveSuit, action.EffectiveRank, action.EffectiveIsRed, action.FactionOrigin) };
        }
    }

    public static void LegacyCurrentEnhancementPrimaryRedirectReturnsExactSingleton() => LegacyCurrentEnhancementRedirectReturns(1);
    public static void LegacyCurrentEnhancementTailRedirectReturnsExactSingleton() => LegacyCurrentEnhancementRedirectReturns(2);
    public static void LegacyCurrentEnhancementDuplicateTailUsesActualCursor() => LegacyCurrentEnhancementRedirectReturns(3);

    private static void LegacyCurrentEnhancementRedirectReturns(int redirectCase)
    {
        var (g, registry) = Start(CardKind.Dodge, zero: true, fullBenxi: true, benxiRedirectCase: redirectCase);
        var target = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:sole-peer-1").Seat;
        var extra = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:sole-peer-2").Seat;
        var protector = Protector(g); var firstRedirect = redirectCase is 1 or 3; var lastRedirect = redirectCase is 2 or 3;
        var costs = new List<(int Owner, int Card)>(); var frozenAccepted = 0; var frozenRedirects = 0; var skippedInitialExtra = 0;
        var expectedRedirects = new List<(long Frame, long Window, CardConversionSource Source, string Hash,
            int Instruction, int Cursor, int Previous, int Next, IReadOnlyList<int> Before, bool Accepted)>();
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p?.SkillPrompt?.SkillId == "fixture:sole-legacy" && p.Choices.Any(c => c.Targets.SequenceEqual([target])));
        Answer(g, c => c.Targets.SequenceEqual([target]));
        Reach(g, p => p?.SkillPrompt?.SkillId == "classic:benxi" && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("enhancement-option") == nameof(CurrentCardEnhancement.ExtraTarget)));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == 0 && u.CardKind == CardKind.Slash);
        var useId = use.Id; var actionId = use.Action!.ActionId; var parentId = use.CardAttack!.ProgramSkillCardUseFrameId!.Value;
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("enhancement-option") == nameof(CurrentCardEnhancement.ExtraTarget));
        Reach(g, p => p?.SkillPrompt?.SkillId == "classic:benxi" && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("enhancement-option") == "target" && c.Targets.SequenceEqual([extra])));
        Answer(g, c => c.Parameters.GetValueOrDefault("enhancement-option") == "target" && c.Targets.SequenceEqual([extra]));
        Reach(g, p => p?.SkillPrompt?.SkillId == "classic:benxi" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("enhancement-option") == "finish"));
        g = Cold(g, registry); Observe(); Answer(g, c => c.Parameters.GetValueOrDefault("enhancement-option") == "finish");
        if (firstRedirect)
        {
            RedirectPublished(target, extra, 0, accepted: false);
            // Choose a real optional prompt if the new receiver is offered one;
            // do not manufacture a second redirect window for that same cursor.
            Reach(g, p => p?.SkillPrompt?.SkillId == TargetPause ||
                p?.SkillPrompt?.SkillId == "classic:liuli" && p.PlayerSeat == extra && Current().TargetIndex == 0);
            if (P(g)?.SkillPrompt?.SkillId == "classic:liuli")
            {
                g = Cold(g, registry); Observe();
                AnswerPublished(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); skippedInitialExtra++;
            }
        }
        Reach(g, p => p?.SkillPrompt?.SkillId == TargetPause);
        var acceptedTargets = firstRedirect ? new[] { extra, extra } : new[] { target, extra };
        use = Current();
        Require(use.ProgramUseAccepted && use.TargetSeats.SequenceEqual(acceptedTargets) &&
            use.Action!.TargetSeats.SequenceEqual(acceptedTargets) &&
            Facts<CardActionAcceptedEvent>(g).Single(e => e.Action.ActionId == actionId).Action.TargetSeats.SequenceEqual(acceptedTargets) &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId).SelectedTargetSeats.SequenceEqual([target]) &&
            !Facts<SoleTargetProtectionPaidEvent>(g).Any(e => e.Receipt.CardUseFrameId == useId),
            "The genuine pre-response redirect may finalize a repeated actual pair, while the protected-source participant remains ineligible by real HP.");
        g = Cold(g, registry); Observe();
        if (lastRedirect) RedirectPublished(extra, protector, 1, accepted: true);
        var actualTargets = lastRedirect ? new[] { firstRedirect ? extra : target, protector } : acceptedTargets;
        if (lastRedirect)
        {
            Reach(g, _ => Current().TargetIndex == 1 && Current().CardAttack!.TargetSeat == protector);
            use = Current();
            Require(use.TargetSeats.SequenceEqual(actualTargets) && use.Action!.TargetSeats.SequenceEqual(acceptedTargets),
                "The later published tail redirect changes only its real index-one cursor, retaining the earlier finalized action and untouched primary.");
        }
        Require(frozenAccepted == 1 && costs.Count == (redirectCase == 3 ? 2 : 1) &&
            costs.All(cost => g.CardMovements.Count(m => m.CardId == cost.Card && m.From == CardLocation.Hand(cost.Owner) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.classic:liuli.SelectAndMoveOwnedCard") == 1) &&
            !Facts<SoleTargetProtectionPaidEvent>(g).Any(e => e.Receipt.CardUseFrameId == useId),
            "Every explicitly chosen native Liuli branch pays its one genuine hand cost and the one accepted event exposes frozen nested arrays.");
        var history = g.Events.Select(e => e.Payload).ToArray();
        var redirects = history.OfType<CurrentEnhancementSlashRedirectedEvent>().Where(e => e.CardUseFrameId == useId).ToArray();
        var acceptance = history.OfType<CardActionAcceptedEvent>().Single(e => e.Action.ActionId == actionId);
        var addition = history.OfType<CurrentCardEnhancedEvent>().Single(e => e.CardUseFrameId == useId && e.Enhancement == CurrentCardEnhancement.ExtraTarget);
        Require(redirects.Length == expectedRedirects.Count && frozenRedirects == expectedRedirects.Count,
            "Every actual native redirect publishes and freezes exactly one typed enhancement fact, including across cold-restored cost choices.");
        var reader = typeof(GameEngine).GetMethod("IsCurrentEnhancementSlashRedirectFact",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var unchanged = GameCheckpointJson.Serialize(g.CreateCheckpoint());
        for (var i = 0; i < redirects.Length; i++)
        {
            var fact = redirects[i]; var expected = expectedRedirects[i];
            Require(fact.ProgramFrameId == expected.Frame && fact.WindowFrameId == expected.Window && fact.ActionId == actionId &&
                fact.Source == expected.Source && fact.Source.SkillId == "classic:liuli" && fact.GameplayHash == expected.Hash &&
                fact.InstructionIndex == expected.Instruction && fact.TargetIndex == expected.Cursor &&
                fact.PreviousTargetSeat == expected.Previous && fact.NewTargetSeat == expected.Next &&
                fact.BeforeTargets.SequenceEqual(expected.Before) &&
                fact.AfterTargets.SequenceEqual(expected.Before.Select((seat, cursor) => cursor == expected.Cursor ? expected.Next : seat)) &&
                Array.IndexOf(history, addition) < Array.IndexOf(history, fact) &&
                (expected.Accepted ? Array.IndexOf(history, acceptance) < Array.IndexOf(history, fact) : Array.IndexOf(history, fact) < Array.IndexOf(history, acceptance)),
                "The typed redirect retains its real native window, source, paused instruction and actual cursor, ordered on the correct side of finalized acceptance.");
            var alternate = g.CreateSnapshot(0).Players.First(p => p.IsAlive && p.Seat != 0 &&
                p.Seat != fact.PreviousTargetSeat && p.Seat != fact.NewTargetSeat).Seat;
            Require(Proven(fact) && !Proven(fact with { WindowFrameId = fact.WindowFrameId + 1000 }) &&
                !Proven(fact with { NewTargetSeat = alternate,
                    AfterTargets = Array.AsReadOnly(fact.BeforeTargets.Select((seat, cursor) => cursor == fact.TargetIndex ? alternate : seat).ToArray()) }),
                "An existing keyed fact cannot lend its window or history to a different valid redirect destination and matching forged after-target sequence.");
        }
        Require(unchanged == GameCheckpointJson.Serialize(g.CreateCheckpoint()),
            "Typed redirect proof and both forged-fact rejections leave the complete game checkpoint unchanged.");
        g = Cold(g, registry); Observe();
        Reach(g, p => p?.SkillPrompt?.SkillId == "fixture:sole-legacy" && p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "legacy-return"));
        var returned = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == parentId);
        Require(returned.SelectedTargetSeats.SequenceEqual([firstRedirect ? extra : target]) &&
            Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 &&
            Facts<ResponseRequestedEvent>(g).Count(e => e.SourceSeat == 0 && e.IncomingCard == CardKind.Slash) == 2 &&
            Facts<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == "classic:liuli") == costs.Count + skippedInitialExtra &&
            Facts<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == "classic:liuli" && e.Completed) == costs.Count &&
            !Facts<SoleTargetQuarantineIssuedEvent>(g).Any(e => e.Receipt.CardUseFrameId == useId) &&
            !g.ResolutionStack.Any(f => f.Id == useId) && g.CardMovements.All(m => m.CardId != 0),
            "Both native actual response cursors complete once and the exact legacy program receives one primary, with no quarantine shortcut or invented physical entity.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Reach(g, IsPlay);
        Require(Facts<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == "fixture:sole-legacy" && e.Completed) == 1 &&
            !g.ResolutionStack.Any(f => f.Id == parentId || f.Id == useId),
            "Cold restoration resumes the actual next instruction and original binding once after the native redirect completion."); Cold(g, registry);

        CardUseFrame Current() => g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == useId);
        void Observe() => g.EventCommitted += e =>
        {
            if (e.Payload is CardActionAcceptedEvent fact && fact.Action.ActionId == actionId)
            { Frozen(fact.Action.TargetSeats); Frozen(fact.Action.PhysicalCards); Frozen(fact.Action.ConversionChain); frozenAccepted++; }
            if (e.Payload is CurrentEnhancementSlashRedirectedEvent redirected && redirected.CardUseFrameId == useId)
            { Frozen(redirected.BeforeTargets); Frozen(redirected.AfterTargets); frozenRedirects++; }
        };
        bool Proven(CurrentEnhancementSlashRedirectedEvent fact)
        {
            object?[] arguments = [Current(), fact, fact.BeforeTargets, history];
            return (bool)reader.Invoke(g, arguments)!;
        }
        void RedirectPublished(int owner, int destination, int cursor, bool accepted)
        {
            Reach(g, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.PlayerSeat == owner &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            var pending = Current();
            Require(pending.TargetIndex == cursor && pending.CardAttack!.TargetSeat == owner && pending.ProgramUseAccepted == accepted,
                "The chosen published Liuli activation belongs to its exact live native target cursor and acceptance stage.");
            g = Cold(g, registry); Observe(); AnswerPublished(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.PlayerSeat == owner && p.Choices.Any(c => c.Targets.SequenceEqual([destination])));
            g = Cold(g, registry); Observe(); AnswerPublished(g, c => c.Targets.SequenceEqual([destination]));
            Reach(g, p => p?.SkillPrompt?.SkillId == "classic:liuli" && p.PlayerSeat == owner && p.Choices.Any(c => c.Cards.Count == 1));
            var card = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single(); costs.Add((owner, card));
            var source = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SkillId == "classic:liuli" && f.OwnerSeat == owner);
            var window = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == source.WindowContext!.ParentFrameId);
            expectedRedirects.Add((source.Id, window.Id,
                new(source.SkillId, source.TriggerId ?? source.ActivationId!, source.OwnerSeat, source.SkillInstanceId), source.GameplayHash,
                source.InstructionIndex + 1, cursor, owner, destination, Array.AsReadOnly(Current().TargetSeats.ToArray()), accepted));
            g = Cold(g, registry); Observe(); AnswerPublished(g, c => c.Cards.SequenceEqual([card]));
        }
    }

    private static (GameEngine, ContentRegistry) Start(CardKind kind, bool substitute = false, bool sourceLoss = false, bool red = false, bool ownSkill = false, bool multi = false, bool chain = false, bool foreignActor = false, bool zero = false, bool liuli = false, bool fullBenxi = false, int benxiRedirectCase = 0, bool liuliLoop = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(kind, substitute, sourceLoss, red, ownSkill, multi, chain, foreignActor, zero, liuli, fullBenxi, benxiRedirectCase, liuliLoop));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, "fixture:sole-owner", g.Revision, P(g)!.PromptId));
        Reach(g, zero ? p => p?.SkillPrompt?.SkillId == "fixture:sole-legacy" : IsPlay); return (g, registry);
    }
    private static int Protector(GameEngine g) => g.CreateSnapshot(0).Players.Single(p => p.GeneralId == "fixture:sole-protector").Seat;
    private static LegalAction FindAction(GameEngine g, CardKind kind, int protector) => g.GetHumanLegalActions().First(a =>
        a.Kind == Enum.Parse<LegalActionKind>(kind.ToString()) && (kind is CardKind.DrawTwo or CardKind.Lightning ||
            a.TargetSeats is [var target] && target != protector && target != 0));
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s, false).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision? p) => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Play(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId,
        a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Require(p.PlayerSeat == 0, "Only true human choices use Answer commands."); Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    // This AI fixture exposes a real responder-owned prompt. Choose its native
    // branch explicitly so AI utility ties cannot skip the behavior under test.
    private static void AnswerPublished(GameEngine g, Func<PromptChoice, bool> pick)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Reach(GameEngine g, Func<PendingDecision?, bool> stop)
    {
        for (var i = 0; i < 200; i++)
        {
            var p = P(g); if (stop(p)) return;
            if (g.State.Winner != Winner.None)
                throw new InvalidOperationException("The sole-protection fixture ended before its expected native prompt: " +
                    JsonSerializer.Serialize(new { stack = g.ResolutionStack, events = g.Events.Where(e =>
                        e.Payload is SoleTargetReplacedEvent or SoleTargetAfterProtectionRedirectedEvent or
                            SoleTargetRedirectWindowIssuedEvent or ProgramBindingStartedEvent)
                        .Select(e => new { Type = e.Payload.GetType().Name, Value = JsonSerializer.Serialize(e.Payload, e.Payload.GetType()) }) }));
            if (p is { PlayerSeat: 0 })
            {
                if (p.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                else if (IsPlay(p)) End(g);
                else
                {
                    var choice = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == "continue") ??
                        p.Choices.FirstOrDefault(c => c.Cards.Count == 0 && c.Targets.Count == 0) ?? p.Choices[0];
                    Accept(g, new AnswerPromptCommand(0, p.PromptId, choice.Id, g.Revision));
                }
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Sole protection exceeded bounded native prompts: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "Rejected real fixture command."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s, false)) == SnapshotJson.Serialize(copy.CreateSnapshot(s, false))) &&
            JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) &&
            g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(copy.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(copy.CardMovements), "Every view, owning receipt, immutable fact and real movement replays cold identically."); return copy;
    }
    private static void Frozen<T>(IReadOnlyList<T> values)
    { Require(values is IList<T> { IsReadOnly: true }, "Committed nested collections must be immutable."); if (values.Count == 0) return; try { ((IList<T>)values)[0] = values[0]; } catch (NotSupportedException) { return; } throw new InvalidOperationException("Observer changed a committed nested collection."); }
    private static void Require(bool yes, string message) { if (!yes) throw new InvalidOperationException(message); }
    private static string Resource(string bundle, string kind)
    { var asm = typeof(StandardContentPackage).Assembly; using var r = new StreamReader(asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n => n.EndsWith(bundle + "." + kind + ".json", StringComparison.Ordinal)))!); return r.ReadToEnd(); }

    private sealed class Fixture(CardKind kind, bool substitute, bool sourceLoss, bool red, bool ownSkill, bool multi, bool chain, bool foreignActor, bool zero, bool liuli, bool fullBenxi, int benxiRedirectCase, bool liuliLoop) : IGameContentPackage
    {
        private static readonly Lazy<SkillProgramCatalog> WenPin = new(() => LoadCatalog("ol-wen-pin"));
        private static readonly Lazy<SkillProgramCatalog> TrimmedBenxi = new(() => LoadBenxi(trim: true));
        private static readonly Lazy<SkillProgramCatalog> FullBenxi = new(() => LoadBenxi(trim: false));
        private static readonly Lazy<SkillProgramCatalog> Liuli = new(() => LoadCatalog("passive-response-rules"));
        private static readonly Lazy<SkillProgramCatalog> Lihuo = new(() => LoadCatalog("boundary-cheng-pu"));
        private static readonly Dictionary<(string Id, string Body, bool ContinueLabel), SkillProgramCatalog> FixturePrograms = [];

        private static SkillProgramCatalog LoadCatalog(string bundle) =>
            SkillProgramCatalog.Load(Resource(bundle, "rules"), Resource(bundle, "presentation"));

        private static SkillProgramCatalog LoadBenxi(bool trim)
        {
            var rules = Resource("classic-wu-yi", "rules"); var presentation = Resource("classic-wu-yi", "presentation");
            if (trim)
            {
                // The sole-target fixture keeps only the distance observer.
                // Complete enhancement fixtures load an independent catalog.
                var parsedRules = JsonNode.Parse(rules)!;
                var triggers = parsedRules["skills"]![0]!["triggers"]!.AsArray();
                for (var i = triggers.Count - 1; i >= 0; i--)
                    if (triggers[i]!["id"]!.GetValue<string>() != "use-shortens-turn-distance") triggers.RemoveAt(i);
                rules = parsedRules.ToJsonString();
                var parsedPresentation = JsonNode.Parse(presentation)!;
                parsedPresentation["skills"]!["classic:benxi"]!["triggerChoices"]!.AsObject().Remove("choose-current-card-enhancements");
                presentation = parsedPresentation.ToJsonString();
            }
            return SkillProgramCatalog.Load(rules, presentation);
        }

        public PackageManifest Manifest { get; } = new("fixture-sole-protection", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            foreach (var c in new[] { WenPin.Value, fullBenxi ? FullBenxi.Value : TrimmedBenxi.Value })
            {
                foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            }
            if (liuli)
            {
                var catalog = Liuli.Value;
                var id = "classic:liuli";
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
                var fire = Lihuo.Value;
                var fireId = "boundary:lihuo";
                b.AddSkill(new(fireId, fire.Presentations[fireId].Name, fire.Presentations[fireId].Description)
                    { Program = fire.Programs[fireId], ProgramPresentation = fire.Presentations[fireId] });
            }
            if (benxiRedirectCase != 0 && !liuli)
            {
                var catalog = Liuli.Value;
                var id = "classic:liuli";
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            }
            void Add(string id, string body)
            {
                lock (FixturePrograms)
                {
                    var key = (id, body, id == TargetPause || id == GainPause || id == "fixture:sole-legacy" && fullBenxi);
                    if (FixturePrograms.TryGetValue(key, out var existing))
                    {
                        b.AddSkill(new(id, id, id) { Program = existing.Programs[id], ProgramPresentation = existing.Presentations[id] });
                        return;
                    }
                    var c = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{id}}","revision":1,{{body}}}]}""",
                        JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
                        { [id] = new { name = id, description = id, optionLabels = id != TargetPause && id != GainPause && (id != "fixture:sole-legacy" || !fullBenxi)
                            ? new Dictionary<string, string>()
                            : new Dictionary<string, string> { ["continue"] = "继续" } } } }));
                    b.AddSkill(new(id, id, id) { Program = c.Programs[id], ProgramPresentation = c.Presentations[id] });
                    FixturePrograms.Add(key, c);
                }
            }
            if (benxiRedirectCase != 0 || liuliLoop) Add("fixture:sole-redirect-range", """
            "modifiers":[{"id":"published-liuli-target-range","query":"attackRange","operation":"add","value":1,"priority":0}]
            """);
            Add(Driver, """
            "activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
            {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"gear"}]},
            {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":3}]}]
            """);
            Add(TargetPause, """
            "triggers":[{"id":"actual-new-target","window":"cardUseTargetsFinalized","ownerRelation":"target","priority":-100,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]
            """);
            Add(GainPause, """
            "triggers":[{"id":"real-return-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.sole-target-protection.return"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]
            """);
            if (multi) b.AddCard(StandardContentRegistry.CreateWithClassicGenerals().Cards["classic:zhangba-serpent-spear"]);
            if (chain) Add("fixture:sole-high", """
            "triggers":[{"id":"high-protection","window":"cardUseTargetsFinalized","ownerRelation":"observer","priority":-50,"optional":true,"effects":[{"op":"protectOtherSoleCardTarget","target":"owner"}]}]
            """);
            if (foreignActor) Add("fixture:sole-actor-swap", """
            "triggers":[{"id":"actual-user-replacement","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["duel"],"priority":1000,"optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"replaceCurrentCardUseActor","target":"selectedTarget"}]}]
            """);
            if (zero)
            {
                var returned = fullBenxi ? ",{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"legacy-return\",\"options\":[{\"id\":\"continue\"}]}" : "";
                Add("fixture:sole-legacy", $$"""
                "triggers":[{"id":"legal-legacy-slash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingVirtualSlashTarget"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget"}{{returned}}]}]
                """);
            }
            if (sourceLoss)
            {
                Add(SourceLoss, """
                "triggers":[{"id":"actual-cost-kills-source","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementDiscardOnly":true,"movementOccurrence":"perBatch","movementReasons":["skill-program.sole-target-protection.cost"],"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]
                """);
                Add(Replacement, """
                "triggers":[{"id":"replace-lost-source","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":3,"clampToMaxHp":true},{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:zhenwei"],"sourceBind":"fixture:sole-spent"}]}]
                """);
                b.AddSkill(new("fixture:sole-spent", "Spent", "Actual source removed after cost."));
            }
            b.AddSkill(new("fixture:sole-peer", "Peer", "Fixed remaining generals") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -10000d : 10000d) });
            b.AddGeneral(new("fixture:sole-owner", "Actual user", "supporter", Driver, "wei", 1, [GainPause, "classic:benxi",
                .. (liuli ? new[] { "boundary:lihuo" } : []), .. (liuliLoop ? new[] { "fixture:sole-redirect-range" } : []), .. (ownSkill ? new[] { Skill } : []),
                .. (foreignActor ? new[] { "fixture:sole-actor-swap" } : []), .. (zero ? new[] { "fixture:sole-legacy" } : [])]));
            b.AddGeneral(new("fixture:sole-protector", "Actual protector", "supporter", "fixture:sole-peer", "wei", substitute ? 12 : 3,
                [.. (ownSkill ? Array.Empty<string>() : new[] { Skill }), TargetPause, GainPause,
                    .. (liuli ? new[] { "classic:liuli" } : []), .. (liuliLoop ? new[] { "fixture:sole-redirect-range" } : []), .. (sourceLoss ? new[] { SourceLoss, Replacement } : [])]));
            b.AddGeneral(new("fixture:sole-peer-1", "Original target", "supporter", "fixture:sole-peer", "wei", benxiRedirectCase == 0 ? 1 : 4,
                [TargetPause, .. (benxiRedirectCase is 1 or 3 || liuliLoop ? new[] { "classic:liuli", "fixture:sole-redirect-range" } : [])]));
            if (chain) b.AddGeneral(new("fixture:sole-high-protector", "Second protector", "supporter", "fixture:sole-peer", "wei", 20, ["fixture:sole-high", TargetPause]));
            else b.AddGeneral(new("fixture:sole-peer-2", "Original target", "supporter", "fixture:sole-peer", "wei", benxiRedirectCase == 0 ? 1 : 4,
                [TargetPause, .. (benxiRedirectCase is 2 or 3 ? new[] { "classic:liuli", "fixture:sole-redirect-range" } : [])]));
            var definition = kind switch { CardKind.Dodge => "standard:dodge", CardKind.Slash => "standard:slash", CardKind.Duel => "standard:duel", CardKind.Snatch => "standard:snatch",
                CardKind.DrawTwo => "standard:draw_two", CardKind.Indulgence => "standard:indulgence", CardKind.SupplyShortage => "standard:supply_shortage", CardKind.Lightning => "standard:lightning", _ => throw new InvalidOperationException() };
            b.AddDeck(new("fixture:sole-deck", "Small fixed real deck", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(multi && i % 10 == 0 ? "classic:zhangba-serpent-spear" : definition, red ? Suit.Heart : Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "Sole protection", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:sole-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:sole-owner", "fixture:sole-protector", "fixture:sole-peer-1", chain ? "fixture:sole-high-protector" : "fixture:sole-peer-2"]));
        }
    }
}
