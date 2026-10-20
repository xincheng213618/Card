using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class WuLuKangDixianChecks
{
    private const string Draw = "skill-program.wumiao:dixian.draw", Obtain = "skill-program.wumiao:dixian.obtain";
    private static ShanheStartingCard C(CardKind kind, int rank = 7, Suit suit = Suit.Spade) => WuLuKangScene.C(kind, rank, suit);
    private static void Finished(WuLuKangScene s, long id) => s.Reach(() => s.E<WuLuKangDixianCompletedEvent>().Any(e => e.FrameId == id));
    private static void Spent(WuLuKangScene s)
    {
        s.Need(!s.Actions().Any(a => a.ProgramSkillId == WuLuKangScene.Dixian), "The real game-limited activation is already consumed.");
        s.Reject(new UseProgramSkillCommand(0, WuLuKangScene.Dixian, "declare-rank", [], [], s.G.Revision, s.P!.PromptId));
    }
    public static void SuccessfulGuessEffectiveRankNativeQuotaAndLifetimeCold()
    {
        var s = WuLuKangScene.Start(new("success-rank-six-permanent") { Dixian = true, ConditionalSources = true, PauseUse = true, WatchDraw = true,
            Hand = [C(CardKind.ZhangbaSerpentSpear), C(CardKind.Slash, 7), C(CardKind.Slash, 6), C(CardKind.Slash, 6), C(CardKind.Slash, 6), C(CardKind.Slash, 7),
                C(CardKind.Alcohol, 7), C(CardKind.Alcohol, 6), C(CardKind.Alcohol, 6), C(CardKind.Alcohol, 7), C(CardKind.Peach, 1), C(CardKind.IronChain, 2), C(CardKind.Dodge, 4), C(CardKind.Dodge, 4)],
            Stock = Enumerable.Range(0, 18).Select(_ => C(CardKind.Dodge, 7)).ToArray() });
        s.Native(CardKind.Slash, [1], 7); s.Play(); s.Native(CardKind.Alcohol, [], 7); s.Play();
        s.Need(!s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.Contains(2)) &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Alcohol), "Ordinary Slash and Alcohol quotas are actually spent before the limited skill.");
        s.BeginDixian(); var started = s.E<WuLuKangDixianStartedEvent>().Single();
        s.Need(s.P!.Choices.Count == 13 && s.P.Choices.All(c => c.Cards.Count == 0 && c.Targets.Count == 0) &&
            s.P.Choices.Select(c => c.Parameters.GetValueOrDefault("rank")).ToHashSet().SetEquals(Enumerable.Range(1, 13).Select(i => i.ToString())),
            "A genuine accepted limited skill privately publishes exactly thirteen rank declarations without revealing stock identities.");
        s.Private(); s.RejectPrompt(); s.Cold(); s.ChooseRank(6); s.Reach(() => s.Hold());
        s.Need(s.E<WuLuKangDixianRankChosenEvent>().Single() is { Rank: 6, AllAtLeast: true, StockCount: 18 } &&
            s.E<WuLuKangDixianDrawReturnedEvent>().Single() is { RequestedCount: 6, ActualCount: 6, ReplacementCount: 0 } &&
            s.E<WuLuKangDixianGrantedEvent>().Length == 0 && s.G.CardMovements.Count(m => m.Reason.Value == Draw && m.To == CardLocation.Hand(0)) == 6,
            "The entire accepted stock predicate is frozen; all six real draws and their gain child precede the permanent grant.");
        s.Private(); s.Cold(); s.Continue(); Finished(s, started.FrameId); s.Play();
        var grant = s.E<WuLuKangDixianGrantedEvent>().Single().Grant;
        s.Need(grant.Rank == 6 && grant.Source.OwnerSeat == 0 && grant.ProgramFrameId == started.FrameId, "One exact rank-six permanent grant is issued only after real draw descendants return."); Spent(s);
        foreach (var expected in new[] { 1, 2, 3 })
        {
            var card = s.V().Hand.First(c => c.Kind == CardKind.Slash && c.Rank == 6).Id;
            s.Native(CardKind.Slash, [2], 6); s.Reach(() => s.Hold());
            var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.CardId == card);
            s.Need(use.Action is { Type: CardActionType.Use, EffectiveRank: 6 } && use.WuLuKangDixianUse is { Applies: true, EffectiveRank: 6 } receipt && receipt.Grant == grant &&
                s.G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
                "Each equality-bound real Slash uses its original paid entity and frozen effective rank, reaches distance two and exceeds only the ordinary count quota.");
            s.Private(); s.Cold(); s.Continue(); s.Play();
            s.Need(s.E<WuLuKangDixianUseAdmittedEvent>().Count(e => e.Receipt.Applies && e.Receipt.Kind == CardKind.Slash && e.Receipt.EffectiveRank == 6) == expected,
                "Each actual rank-six Use receives one admission fact, never a synthetic free action or duplicated return.");
            if (expected < 3) { s.Native(CardKind.Alcohol, [], 6); s.Play(); }
        }
        s.Need(!s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId is { } id && s.V().Hand.Any(c => c.Id == id && c.Rank == 7)),
            "A printed/effective seven above the bound cannot borrow unlimited Slash usage even at a near target.");
        s.Need(!s.Actions().Any(a => a.Kind == LegalActionKind.Alcohol && a.CardId is { } id && s.V().Hand.Any(c => c.Id == id && c.Rank == 7)),
            "Two eligible Alcohol Uses bypass their count without refunding the already spent ordinary Alcohol quota to high ranks.");
        s.Use("lose-one"); s.Play(); s.Need(!s.Has(WuLuKangScene.Dixian), "A genuine HP change removes the original conditional source.");
        s.Need(Enumerable.Range(0, 4).All(v => s.G.CreateSnapshot(v).Players[0].ActiveEffects?.Any(e => e.SkillId == WuLuKangScene.Dixian && e.SourceSeat == 0 &&
            e.Expiry == ActivePlayerEffectExpiry.RuleCleared && e.Description.Contains("6", StringComparison.Ordinal)) == true),
            "The issued permanent rank bound remains public to every safe viewer after its original skill source is lost.");
        s.Native(CardKind.ZhangbaSerpentSpear, []); s.Play();
        var pair = s.V().Hand.Where(c => c.Rank is 1 or 2).Take(2).Select(c => c.Id).ToArray();
        s.Need(pair.Length == 2, "The original two exact low-rank materials remain available for native Zhangba.");
        s.Accept(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [2], s.G.Revision, s.P!.PromptId)); s.Reach(() => s.Hold());
        var spear = s.G.ResolutionStack.OfType<CardUseFrame>().Last();
        s.Need(spear.Action is { PhysicalCards.Count: 2, EffectiveRank: 3 } && spear.WuLuKangDixianUse is { Applies: true, EffectiveRank: 3 },
            "A genuine two-material Zhangba uses its actual effective rank sum three, retains both real payments and preserves the issued privilege after source loss."); s.Cold(); s.Continue(); s.Play();
        var tooHighPair = s.V().Hand.Where(c => c.Kind == CardKind.Dodge && c.Rank == 4).Take(2).Select(c => c.Id).ToArray();
        s.Reject(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, tooHighPair, [2], s.G.Revision, s.P!.PromptId));
        s.Use("recover-one"); s.Play(); s.Need(s.Has(WuLuKangScene.Dixian) && s.E<WuLuKangDixianGrantedEvent>().Length == 1, "Real source regrant retains one issued rank bound without resetting the limited skill."); Spent(s); s.Cold();

        var replaced = WuLuKangScene.Start(new("success-real-draw-replacement") { Dixian = true, Zenrun = true, Stock = Enumerable.Range(0, 8).Select(_ => C(CardKind.Dodge, 7)).ToArray() });
        replaced.BeginDixian(); replaced.ChooseRank(1); replaced.Reach(() => replaced.Optional("ol:zenrun")); replaced.Private(); replaced.Cold(); replaced.Activate();
        replaced.Reach(() => replaced.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "target") == true); replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "target" && c.Targets.SequenceEqual([2]));
        replaced.Reach(() => replaced.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "pick") == true); replaced.Private();
        replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "pick" && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); replaced.Answer(c => c.Parameters.GetValueOrDefault("branch") == "take");
        Finished(replaced, replaced.E<WuLuKangDixianStartedEvent>().Single().FrameId); replaced.Play();
        replaced.Need(replaced.E<WuLuKangDixianDrawReturnedEvent>().Single() is { RequestedCount: 1, ActualCount: 0, ReplacementCount: 1 } && replaced.E<WuLuKangDixianGrantedEvent>().Single().Grant.Rank == 1 &&
            replaced.G.CardMovements.All(m => m.Reason.Value != Draw), "An actual native replacement is not falsely recorded as drawing the minimum/top entity, and requested Draw1 still completes the permanent grant."); replaced.Cold();

        foreach (var childLoss in new[] { 1, 4 })
        {
            var paid = WuLuKangScene.Start(new("successful-draw-child-source-" + childLoss) { Dixian = true, ConditionalSources = true, WatchDraw = true, DrawChildLoss = childLoss,
                Stock = Enumerable.Range(0, 4).Select(_ => C(CardKind.Dodge, 7)).ToArray() });
            paid.BeginDixian(); paid.ChooseRank(1); paid.Reach(() => paid.Hold()); var parent = paid.E<WuLuKangDixianStartedEvent>().Single().FrameId;
            paid.Need(paid.E<WuLuKangDixianDrawReturnedEvent>().Single() is { RequestedCount: 1, ActualCount: 1 } && paid.E<WuLuKangDixianGrantedEvent>().Length == 0,
                "The accepted successful branch holds its actual returned draw while the genuine gain child is still unresolved.");
            paid.Private(); paid.Cold(); paid.Continue(); Finished(paid, parent);
            paid.Need(paid.E<WuLuKangDixianDrawReturnedEvent>().Length == 1 && paid.E<WuLuKangDixianCompletedEvent>().Count(e => e.FrameId == parent && e.Granted == (childLoss == 1)) == 1 &&
                paid.E<WuLuKangDixianGrantedEvent>().Length == (childLoss == 1 ? 1 : 0) && !paid.Has(WuLuKangScene.Dixian),
                "A paid draw child that removes the live source still grants once for its living owner, while actual nonterminal owner death stops the permanent grant and preserves the once-returned draw.");
            if (childLoss == 1) { paid.Play(); Spent(paid); } else paid.Need(!paid.V().IsAlive && paid.V(1).IsAlive && paid.G.State.Winner == Winner.None, "The success death branch is an actual nonterminal native death, not a terminal shortcut."); paid.Cold();
        }

        var empty = WuLuKangScene.Start(new("empty-stock-vacuous-success") { Dixian = true, Stock = [C(CardKind.Dodge, 7)] }); empty.Use("draw20"); empty.Play();
        empty.Need(empty.Stock().Length == 0, "A real native draw exhausts the one-card stock before the declaration."); empty.BeginDixian(); empty.ChooseRank(13); Finished(empty, empty.E<WuLuKangDixianStartedEvent>().Single().FrameId); empty.Play();
        empty.Need(empty.E<WuLuKangDixianRankChosenEvent>().Single() is { Rank: 13, AllAtLeast: true, StockCount: 0 } &&
            empty.E<WuLuKangDixianDrawReturnedEvent>().Single() is { RequestedCount: 13, ActualCount: 0 } && empty.E<WuLuKangDixianGrantedEvent>().Single().Grant.Rank == 13,
            "An empty current stock freezes vacuous success before its genuine short requested Draw13, rather than inventing thirteen actual cards."); Spent(empty); empty.Cold();
    }

    public static void FailedGuessExactKRegionsNativeChildrenAndCold()
    {
        foreach (var scenario in new[] { "lion-recovery", "paid-source-loss", "paid-source-death" })
        {
            var conditional = scenario == "paid-source-loss"; var loss = conditional ? 1 : scenario.EndsWith("death", StringComparison.Ordinal) ? 4 : 0;
            var s = WuLuKangScene.Start(new("failed-exact-all-k-" + scenario) { Dixian = true, ConditionalSources = conditional, OwnerHp = scenario == "lion-recovery" ? 3 : 4,
                Equipment = [C(CardKind.SilverLion, 13)], Hand = [C(CardKind.Indulgence, 13), C(CardKind.Slash, 13), C(CardKind.Peach, 13)],
                Peers = [[C(CardKind.Dodge, 13)], [C(CardKind.Dodge, 13)], [C(CardKind.Dodge, 2)]],
                Stock = [C(CardKind.IronChain, 7), C(CardKind.Dodge, 1), C(CardKind.Indulgence, 13), C(CardKind.Indulgence, 13)], WatchReasons = [Obtain], ChildLoss = loss });
            s.Native(CardKind.Indulgence, [2]); s.Play(); var discarded = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id; s.Use("discard", cards: [discarded]); s.Play();
            var ownHeld = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id; var foreignHeld = s.V(1).Hand.Single().Id;
            var expected = s.Stock().Where(c => s.StockRank(c) == 13).Select(c => c.CardId).Concat(s.V().Equipment.Where(c => c.Rank == 13).Select(c => c.Id))
                .Concat(s.V(2).Judgment.Where(c => c.Rank == 13).Select(c => c.Id)).Append(discarded).ToHashSet();
            var nonKStock = s.Stock().Where(c => s.StockRank(c) != 13).Select(c => c.CardId).ToArray();
            s.Need(s.StockRank(s.Stock().First()) > 1 && s.Stock().Any(c => s.StockRank(c) == 1), "The fixed failed-stock witness hides its lower one beneath a higher top; top-only guessing would choose the wrong branch."); s.BeginDixian(); s.Private(); s.RejectPrompt(); s.Cold(); s.ChooseRank(13); s.Reach(() => s.Hold());
            var started = s.E<WuLuKangDixianStartedEvent>().Single(); var roster = s.E<WuLuKangDixianMaterialFrozenEvent>().OrderBy(e => e.Index).ToArray();
            s.Need(s.E<WuLuKangDixianRankChosenEvent>().Single() is { Rank: 13, AllAtLeast: false } && roster.Length == expected.Count && roster.Select(e => e.Material.CardId).ToHashSet().SetEquals(expected) &&
                roster.All(e => e.Material.PrintedRank == 13 && e.Material.From.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.DrawPile or CardZoneKind.DiscardPile) &&
                !roster.Any(e => e.Material.CardId == ownHeld || e.Material.CardId == foreignHeld),
                "The failed accepted branch freezes all and only actual printed Kings in Equipment/Judgment/current stock/Discard, excluding own and foreign Hands.");
            s.Need(s.E<WuLuKangDixianEntityMovedEvent>().Length >= 1 && s.E<WuLuKangDixianCompletedEvent>().Length == 0 &&
                (scenario != "lion-recovery" || s.V().Hp == 4 && s.E<RecoveryAppliedEvent>().Any(e => e.Amount == 1)),
                "A genuine first equipment departure and SilverLion recovery occur before moving the next frozen entity or finishing its accepted parent.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); Finished(s, started.FrameId);
            if (scenario != "paid-source-death")
            {
                s.Play(); var moves = s.E<WuLuKangDixianEntityMovedEvent>();
                s.Need(moves.Length == expected.Count && moves.Select(e => e.CardId).ToHashSet().SetEquals(expected) && moves.All(e =>
                    s.G.CardMovements.Count(m => m.CardId == e.CardId && m.From == e.From && m.To == e.To && m.Sequence == e.Sequence && m.Reason.Value == Obtain) == 1) &&
                    expected.All(id => s.Location(id) == CardLocation.Hand(0)) && s.Location(ownHeld) == CardLocation.Hand(0) && s.Location(foreignHeld) == CardLocation.Hand(1) &&
                    s.Stock().Where(c => nonKStock.Contains(c.CardId)).Select(c => c.CardId).SequenceEqual(nonKStock),
                    "Every frozen King moves through its exact native zone receipt once while unrelated stock order and hidden held Hands remain intact.");
                s.Need(!conditional || !s.Has(WuLuKangScene.Dixian) && s.V().Hp == 3, "Source loss in an already-paid acquisition child cannot rescan, cancel or replay the remaining exact live-owner roster.");
            }
            else s.Need(!s.V().IsAlive && s.V(1).IsAlive && s.G.State.Winner == Winner.None && s.E<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 &&
                s.E<WuLuKangDixianEntityMovedEvent>().All(e => s.G.CardMovements.Any(m => m.CardId == e.CardId && m.Sequence == e.Sequence)) &&
                !s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == started.FrameId), "Actual nonterminal owner death retains prior paid facts and stops the uncollected roster without inventing a dead player's new Hand.");
            s.Need(s.E<WuLuKangDixianGrantedEvent>().Length == 0 && s.E<WuLuKangDixianDrawReturnedEvent>().Length == 0 &&
                s.E<WuLuKangDixianCompletedEvent>().Count(e => e.FrameId == started.FrameId && !e.AllAtLeast && !e.Granted) == 1, "Failure obtains physical Kings without a fake Draw, success grant or duplicate completion."); s.Cold();
        }
        var none = WuLuKangScene.Start(new("failure-no-eligible-kings") { Dixian = true, Hand = [C(CardKind.Peach, 13)], Equipment = [C(CardKind.WoodenOx, 7)], Stock = [C(CardKind.Dodge, 1), C(CardKind.IronChain, 7)] });
        var grain = none.V().Hand.Single().Id; none.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], none.G.Revision, none.P!.PromptId)); none.Play();
        none.Need(none.Location(grain) == CardLocation.WoodenOxGrain(0), "One real printed King is held in native WoodenOx grain before the failed declaration.");
        none.BeginDixian(); none.ChooseRank(13); Finished(none, none.E<WuLuKangDixianStartedEvent>().Single().FrameId); none.Play();
        none.Need(none.E<WuLuKangDixianCompletedEvent>().Single() is { AllAtLeast: false, ActualGained: 0, Granted: false } && none.E<WuLuKangDixianEntityMovedEvent>().Length == 0 && none.Location(grain) == CardLocation.WoodenOxGrain(0),
            "An actually empty eligible roster completes naturally without inventing a King's location, transfer or permanent privilege."); Spent(none); none.Cold();
    }
}
