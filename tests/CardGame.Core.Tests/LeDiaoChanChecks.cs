using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

namespace CardGame.Core.Tests;

internal static class LeDiaoChanChecks
{
    private const string Tb = "le:tanban", Diou = "le:diou", Driver = "fixture:ldc-driver", Conv = "fixture:ldc-conversion";
    private const string Quiet = "fixture:ldc-quiet", Watch = "fixture:ldc-watch", Conditional = "fixture:ldc-conditional", Neutral = "fixture:ldc-neutral";
    private const string Owner = "fixture:ldc-owner", Mode = "identity:classic-le-diao-chan", DrawReason = "skill-program.le-diao-chan.diou.draw";
    private const string Liyu = "boundary:liyu-current", PeerSlash = "fixture:ldc-peer-slash";
    private static bool Marked(CardSnapshot c) => c.SkillMarkLabel?.Contains("檀板", StringComparison.Ordinal) == true;

    public static void TanbanFrozenOpeningWholeHandInversionAndActualLimitCold()
    {
        var s = Scene.Start("opening", holdOpening: true);
        var initial = s.G.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Single(f => f.TanbanInitialEntities is not null).TanbanInitialEntities!;
        s.Need(initial is IList { IsReadOnly: true } && initial.Count(e => e.OwnerSeat == 0) == 4 &&
            initial.Where(e => e.OwnerSeat == 0).Select(e => e.CardId).ToHashSet().SetEquals(s.V().Hand.Select(c => c.Id)) && !s.E<TanbanInitializedEvent>().Any(),
            "The game-start mother freezes all four final opening Hand entities before an earlier observer returns or any mark is committed.");
        s.Private(); s.RejectPrompt(); s.Cold(); s.Continue(); s.Play();
        var init = s.E<TanbanInitializedEvent>().Single(); var original = s.V().Hand.Where(Marked).Select(c => c.Id).ToArray();
        s.Need(init is { EntityCount: 4, Source.OwnerSeat: 0 } && original.Length == 4 && s.V().Hand.Count == 6 &&
            s.E<TanbanEntityMarkChangedEvent>().Where(e => e.FrameId == init.FrameId).All(e => !e.Before && e.Marked &&
                s.G.CardMovements.Count(m => m.Sequence == e.EntrySequence && m.CardId == e.CardId && m.From == CardLocation.DrawPile &&
                    m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.InitialDeal) == 1),
            "Only genuine opening invoices receive the permanent entity mark; declining inversion leaves the normal Draw2 unmarked.");
        s.Use("draw-four"); s.Play(); var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(original.All(id => s.V().Hand.Any(c => c.Id == id && Marked(c))) && s.V().Hand.Count(c => !Marked(c)) == s.V().Hp + 2 &&
            !s.G.CardMovements.Any(m => m.TurnNumber == turn && original.Contains(m.CardId) && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "The real discard keeps all four marked originals and only an ordinary HP quota of unmarked cards, followed by the next real Draw2.");
        var moved = original[0]; s.Use("give", [moved], [1]); s.Play();
        s.Need(Marked(s.V(1).Hand.Single(c => c.Id == moved)), "A real transfer preserves the global physical entity mark in a foreign Hand.");
        s.End(); s.Reach(() => s.G.State.CurrentSeat == 1 && s.V(1).HandLimit is not null);
        s.Need(s.V(1).HandLimit == s.V(1).Hp, "A holder without Tanban receives no Hand-limit exclusion from the label alone."); s.Play();
        s.Take(1, moved); s.Play(); s.Need(Marked(s.V().Hand.Single(c => c.Id == moved)), "The same returned original retains its binary mark.");
        s.Use("lose-tanban"); s.Play(); s.Need(s.V().Hand.Any(Marked) && !s.Has(Tb), "Actual source loss removes the capability while retaining physical marks.");
        s.Use("grant-tanban"); s.Play(); s.Need(s.Has(Tb) && s.E<TanbanInitializedEvent>().Length == 1, "Regrant restores the capability without reinitializing later cards."); s.Cold();

        s = Scene.Start("swap", holdSwap: true); var before = s.V().Hand.ToDictionary(c => c.Id, Marked); var old = s.P!;
        s.Need(before.Count == 6 && before.Values.Count(v => v) == 4, "The actual own DrawPhaseEnded opportunity has a mixed six-card Hand.");
        var movementCount = s.G.CardMovements.Count; s.Private(); s.RejectPrompt(); s.Cold(); s.Activate(); s.Play(); s.RejectOld(old);
        var swap = s.E<TanbanHandMarksSwappedEvent>().Single();
        s.Need(swap is { HolderSeat: 0, EntityCount: 6 } && s.V().Hand.All(c => Marked(c) == !before[c.Id]) &&
            s.E<TanbanEntityMarkChangedEvent>().Count(e => e.FrameId == swap.FrameId && e.Before != e.Marked) == 6 && s.G.CardMovements.Count == movementCount,
            "One accepted whole-Hand inversion flips both current cohorts atomically without any card movement or debit."); s.Cold();
    }

    public static void DiouRetainedShowNativeBasicTrickDelayedAndEquipmentCold()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.Peach, CardKind.Alcohol, CardKind.DrawTwo, CardKind.Duel, CardKind.FireAttack, CardKind.BorrowedSword,
            CardKind.ArrowBarrage, CardKind.PeachGarden, CardKind.FiveGrains, CardKind.IronChain, CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Lightning, CardKind.SilverLion, CardKind.WoodenOx })
        {
            var s = Scene.Start("family-" + kind, kind); if (kind is CardKind.Peach or CardKind.PeachGarden) { s.Use("wound"); s.Play(); }
            int? weapon = null; int? borrowedMaterial = null;
            if (kind == CardKind.BorrowedSword)
            {
                s.Use("arm-peer", targets: [1]); s.Play();
                s.Need(s.V(1).Equipment.Count == 1, "The fixed seed equips a real draw-pile weapon on the designated native Borrowed Sword user."); weapon = s.V(1).Equipment.Single().Id;
            }
            var originalShown = s.V().Hand.First(c => !Marked(c) && (kind != CardKind.BorrowedSword || c.Kind == kind)); var shown = originalShown.Id; var material = s.Study(); s.ReachDiou();
            s.Need(s.E<CardUseFinishedEvent>().Any(e => e.CardId == material && e.CardKind == CardKind.DrawTwo),
                "The optional child begins after the genuine marked conversion's complete native DrawTwo Use.");
            s.Private(); s.RejectPrompt(); var old = s.P!; if (kind is CardKind.Alcohol or CardKind.Indulgence or CardKind.SilverLion) s.Cold();
            s.ShowDiou(shown); s.RejectOld(old); s.Reach(() => s.DiouBranch("use"));
            s.Need(s.P!.Choices.Where(c => s.IsDiou(c, "use")).All(c => c.Cards.Count == 0) && s.V().Hand.Any(c => c.Id == shown && !Marked(c)),
                "The original show stays in its actual Hand and the native-use selector contains no physical payment.");
            if (kind == CardKind.BorrowedSword)
            {
                s.Answer(c => s.IsDiou(c, "use") && c.Targets.SequenceEqual([1, 0]));
                s.Reach(() => s.P is { PlayerSeat: 1, Kind: DecisionKind.RespondSlash } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash" && c.Cards.Count == 1));
                s.Cold(); s.Accept(new AdvanceOneStepCommand(s.G.Revision));
                var child = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.SourceSeat == 1 && u.CardKind == CardKind.Slash);
                s.Need(child.Action is { PhysicalCards.Count: 1 } && child.Action.PhysicalCards.Single().From == CardLocation.Hand(1) &&
                    child.Action.ParentActionId == s.E<DiouNativeIssuedEvent>().Single().ActionId && s.V(1).Equipment.Any(c => c.Id == weapon),
                    "The real AI pays its own one-entity Slash under the original zero-material Borrowed Sword, keeping its real weapon.");
                borrowedMaterial = child.Action!.PhysicalCards.Single().CardId; s.Cold();
            }
            else s.UseDiou();
            s.Reach(() => s.Hold("inspect-use"));
            s.Need(s.E<DiouShownEvent>().Length == 1 && s.E<DiouNativeIssuedEvent>().Length == 1, "The actual completed native child has one exact public show and issuance.");
            var show = s.E<DiouShownEvent>().Single(); var issued = s.E<DiouNativeIssuedEvent>().Single();
            var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == issued.UseId); var action = native.Action!;
            s.Need(action is { Type: CardActionType.Use } && action.ActionId == issued.ActionId && action.PhysicalCards.Count == 0 && native.PhysicalCardIds is { Count: 0 } &&
                action.PhysicalCards is IList { IsReadOnly: true } && action.TargetSeats is IList { IsReadOnly: true } && action.ConversionChain is IList { IsReadOnly: true } &&
                !s.E<DiouNativeReturnedEvent>().Any(e => e.FrameId == issued.FrameId) && !s.E<DiouDrawReturnedEvent>().Any(),
                "The genuine still-live completed Use owns a frozen zero-material action; a response-only AcceptedEvent is not fabricated.");
            if (kind is CardKind.Alcohol or CardKind.SilverLion) s.Cold(); s.Continue(); s.Play();
            s.Need(show.CardId == shown && show.OutputKind == kind && show.FirstShow && show.SameName == (kind == CardKind.DrawTwo) &&
                action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0 } && action.EffectiveKind == kind && action.PhysicalCards.Count == 0 &&
                action.ConversionChain.Any(c => c.SkillId == Diou && c.OwnerSeat == 0) &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardKind == kind) == 1 &&
                s.E<DiouNativeReturnedEvent>().Count(e => e.FrameId == issued.FrameId && e.UseId == issued.UseId) == 1 &&
                s.E<DiouDrawReturnedEvent>().Single() is { RequestedCount: 2, ActualCount: 2 } &&
                s.V().Hand.Any(c => c.Id == shown && !Marked(c) && c.Suit == originalShown.Suit && c.Rank == originalShown.Rank && c.RankText == originalShown.RankText) &&
                !s.G.CardMovements.Any(m => m.CardId == shown && m.From == CardLocation.Hand(0)) &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
                "Each native family uses a real zero-material same-name action, finishes once, earns Draw2 and retains the exact publicly shown entity.");
            if (kind == CardKind.BorrowedSword)
                s.Need(s.E<BorrowedSwordResolvedEvent>().Count(e => e.ResolutionId == issued.UseId && e is { WeaponOwnerSeat: 1, SlashTargetSeat: 0, UsedSlash: true, TransferredWeaponCardId: null } && e.SlashCardId == borrowedMaterial) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == borrowedMaterial && m.From == CardLocation.Hand(1) && m.To == CardLocation.Processing) == 1 && s.V(1).Equipment.Any(c => c.Id == weapon),
                    "The real material-backed child Slash returns once to Borrowed Sword without falling back to a weapon transfer or consuming the retained original show.");
            if (kind == CardKind.FireAttack)
            {
                var fire = s.E<FireAttackResolvedEvent>().Single();
                s.Need(fire.ResolutionId == issued.UseId && fire.SourceSeat == 0 && fire.TargetSeat == 1 && fire.CausedDamage &&
                    fire.MatchingDiscardCardId is { } paid && paid != shown &&
                    s.G.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.FireAttackDiscard) == 1 &&
                    s.G.CardMovements.Count(m => m.CardId == paid && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.FireAttackDiscardFinished) == 1,
                    "The genuine FireAttack reveals the target's real Hand face and pays another actual same-suit Hand entity through its two native discard invoices, retaining the original Diou show.");
            }
            if (kind is CardKind.SilverLion or CardKind.WoodenOx)
                s.Need(issued.CarrierId != shown && s.V().Equipment.Any(c => c.Id == issued.CarrierId && c.Kind == kind && c.Suit == Suit.None && c.Rank == 0 &&
                    c.SkillMarkLabel?.Contains("低讴·虚拟", StringComparison.Ordinal) == true && c.RankText == "—"),
                    "Native equipment installs an independent neutral carrier while the displayed original remains in Hand.");
            if (kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning)
                s.Need(issued.CarrierId != shown && Enumerable.Range(0, 4).SelectMany(i => s.V(i).Judgment).Any(c => c.Id == issued.CarrierId && c.Kind == kind && c.Suit == Suit.None && c.Rank == 0 &&
                    c.SkillMarkLabel?.Contains("低讴·虚拟", StringComparison.Ordinal) == true && c.RankText == "—"),
                    "A delayed Use retains a separate neutral carrier in the actual judgment area after placement children return.");
        }
        var mixed = Scene.Start("mixed-spear", CardKind.ZhangbaSerpentSpear);
        var spear = mixed.V().Hand.First(Marked).Id; mixed.Native(mixed.Actions().First(a => a.CardId == spear && a.Kind == LegalActionKind.Equip)); mixed.Play();
        var pair = new[] { mixed.V().Hand.First(Marked).Id, mixed.V().Hand.First(c => !Marked(c)).Id };
        var spare = mixed.V().Hand.First(c => !Marked(c) && c.Id != pair[1]).Id;
        mixed.Accept(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [1], mixed.G.Revision, mixed.P!.PromptId));
        mixed.ReachDiou(); mixed.Need(mixed.E<DiouStartedEvent>().Single().Claim is { MaterialCount: 2, TanbanMaterialCount: 1 },
            "The actual native Zhangba Use qualifies on one marked original within its genuine mixed two-entity payment.");
        mixed.Private(); mixed.Cold(); mixed.ShowDiou(spare); mixed.Reach(() => mixed.DiouBranch("use")); mixed.UseDiou(); mixed.Play();
        mixed.Need(pair.All(id => mixed.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1) &&
            mixed.E<DiouNativeIssuedEvent>().Length == 1 && mixed.E<DiouStartedEvent>().Length == 1 && mixed.E<DiouCompletedEvent>().Length == 1 && mixed.V().Hand.Any(c => c.Id == spare),
            "The native mixed-material Slash pays both real entities once; its zero-material clone does not recursively count as a marked Use.");
        var sequential = Scene.Start("adjusted-duel", CardKind.Duel); var duelShow = sequential.V().Hand.First(c => !Marked(c)).Id;
        _ = sequential.Study(); sequential.ReachDiou(); sequential.ShowDiou(duelShow); sequential.Reach(() => sequential.DiouBranch("use"));
        sequential.Answer(c => sequential.IsDiou(c, "use") && c.Targets.SequenceEqual([1, 2]));
        sequential.Reach(() => sequential.G.ResolutionStack.OfType<CardUseFrame>().Any(u => u.DiouOrigin is not null && u.CardKind == CardKind.Duel && u.SequentialTrick is not null && u.TargetIndex == 1));
        var duelUse = sequential.E<DiouNativeIssuedEvent>().Single(); sequential.Cold(); sequential.Play();
        sequential.Need(new[] { 1, 2 }.All(seat => sequential.E<DuelResponseEvent>().Count(e => e.ResolutionId == duelUse.UseId && e.ResponderSeat == seat && !e.UsedSlash) == 1 &&
                sequential.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == seat && e.Amount == 1) == 1) &&
            sequential.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == duelUse.UseId && e.CardKind == CardKind.Duel) == 1 &&
            sequential.E<DiouNativeReturnedEvent>().Count(e => e.UseId == duelUse.UseId) == 1 && sequential.E<DiouDrawReturnedEvent>().Single().ActualCount == 2 &&
            sequential.V().Hand.Any(c => c.Id == duelShow),
            "A genuine next-card target grant resolves both ordered Duel targets through one neutral whole Use, then returns and earns Draw2 once after its second resumable cursor.");
    }

    public static void DiouTrueResponsePlayedExclusionAndActualTurnShowOrSameNameCold()
    {
        foreach (var scene in new[] { "response-dodge", "response-null", "response-rescue" })
        {
            var s = Scene.Start(scene); var shown = s.V().Hand.First(c => !Marked(c)).Id; long? rescueUse = null, rescueDying = null;
            if (scene == "response-dodge")
            {
                s.Use("incoming", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true);
                s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
            }
            else if (scene == "response-null") { s.Use("duel", targets: [1]); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification }); }
            else
            {
                s.Use("near-dying"); s.Play(); s.Need(s.V().Hp == 1, "The real preparation leaves the participant alive at one HP.");
                s.Use("incoming", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0])) == true);
                s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "virtual-slash-offer" && c.Targets.SequenceEqual([0]));
                s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
                s.Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage");
                s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
                s.Need(s.G.ResolutionStack.OfType<DamageFrame>().Any() && s.G.ResolutionStack.OfType<CardUseFrame>().Any(u => u.SourceSeat == 1 && u.CardKind == CardKind.Slash),
                    "The genuine rescue owns the incoming Slash-to-Damage-to-Dying ancestry that must survive its nested zero-material child.");
                rescueUse = s.G.ResolutionStack.OfType<CardUseFrame>().Single(u => u.SourceSeat == 1 && u.CardKind == CardKind.Slash).Id;
                rescueDying = s.G.ResolutionStack.OfType<DyingFrame>().Single().Id;
            }
            var material = s.Respond(); s.ReachDiou(); s.Private(); s.Cold(); s.ShowDiou(shown); s.Reach(() => s.DiouBranch("use")); s.UseDiou(); s.Play();
            var claim = s.E<DiouStartedEvent>().Single().Claim;
            s.Need(claim.EffectiveKind == (scene == "response-dodge" ? CardKind.Dodge : scene == "response-null" ? CardKind.Nullification : CardKind.Peach) &&
                claim.MaterialCount == 1 && claim.TanbanMaterialCount == 1 && s.E<DiouCompletedEvent>().Single() is { Shown: true, Used: true, Reward: true } &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
                "Each genuine true response freezes the marked original before payment and offers one complete Diou child after its real native return.");
            if (scene == "response-rescue") s.Need(s.E<DyingResolvedEvent>().Count(e => e.ResolutionId == rescueDying && e.Survived) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == rescueUse && e.CardKind == CardKind.Slash) == 1 &&
                s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 1,
                "The original genuine Slash and its exact dying parent finish once after the paid rescue and nested Diou Slash return, without replaying damage.");
        }
        var played = Scene.Start("played-duel"); played.Use("duel", targets: [1]); played.Reach(() => played.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        var paid = played.Respond(); played.Play();
        played.Need(played.E<CardRespondedEvent>().Any(e => e.ResponderSeat == 0 && e.CardId == paid) && !played.E<DiouStartedEvent>().Any() && !played.E<DiouUseClaimedEvent>().Any(),
            "A native Duel's pure Played Slash pays its real marked original without a true-Use claim, offer or reward."); played.Cold();
        foreach (var scene in new[] { "ledger-first", "ledger-shown", "ledger-same" })
        {
            var s = Scene.Start(scene, scene == "ledger-same" ? CardKind.DrawTwo : CardKind.Slash); var shown = s.V().Hand.First(c => !Marked(c)).Id;
            if (scene != "ledger-first") s.Show(shown);
            _ = s.Study(); s.ReachDiou(); s.ShowDiou(shown); s.Reach(() => s.DiouBranch("use")); s.UseDiou(); s.Play();
            var proof = s.E<DiouShownEvent>().Single(); var reward = scene != "ledger-shown";
            s.Need(proof.FirstShow == (scene == "ledger-first") && proof.SameName == (scene == "ledger-same") &&
                s.E<DiouDrawReturnedEvent>().Length == (reward ? 1 : 0) && s.E<DiouCompletedEvent>().Single().Reward == reward &&
                s.G.CardMovements.Count(m => m.Reason.Value == DrawReason && m.To == CardLocation.Hand(0)) == (reward ? 2 : 0),
                "Draw2 is first public Hand show of this entity this actual turn OR matching the trigger's effective name; another skill's earlier real show counts.");
            if (scene == "ledger-first")
            {
                var turn = s.G.State.TurnNumber; s.End(); s.Reach(() => s.G.State.TurnNumber > turn && s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
                _ = s.Study(); s.ReachDiou(); s.ShowDiou(shown); s.Reach(() => s.DiouBranch("use")); s.UseDiou(); s.Play();
                s.Need(s.E<DiouShownEvent>().Length == 2 && s.E<DiouShownEvent>().Last().FirstShow && s.E<DiouDrawReturnedEvent>().Length == 2,
                    "A real turn change restores first-show status for the same retained original instead of imposing a permanent entity gate."); s.Cold();
            }
        }
        foreach (var kind in new[] { CardKind.Dodge, CardKind.Nullification })
        {
            var s = Scene.Start("no-use-" + kind, kind); var shown = s.V().Hand.First(c => !Marked(c)).Id; _ = s.Study(); s.ReachDiou(); s.ShowDiou(shown); s.Play();
            s.Need(s.E<DiouShownEvent>().Single() is { FirstShow: true, SameName: false } && !s.E<DiouNativeIssuedEvent>().Any() &&
                s.E<DiouCompletedEvent>().Single() is { Shown: true, Used: false, Reward: true } && s.E<DiouDrawReturnedEvent>().Single().ActualCount == 2 && s.V().Hand.Any(c => c.Id == shown),
                "A legal public nonmarked show with no legal proactive Dodge/Null Use keeps its earned Draw2 without a forged native action or physical debit.");
        }
    }

    public static void DiouPaidNativeChildrenSourceLossDeathAndCarrierRetirementCold()
    {
        foreach (var scene in new[] { "draw-loss", "draw-death", "equipment-lion", "equipment-ox" })
        {
            var kind = scene == "equipment-lion" ? CardKind.SilverLion : scene == "equipment-ox" ? CardKind.WoodenOx : CardKind.DrawTwo;
            var s = Scene.Start(scene, kind); int? previous = null; int? grain = null;
            if (scene.StartsWith("equipment-", StringComparison.Ordinal))
            {
                previous = s.V().Hand.First(Marked).Id; s.Native(s.Actions().First(a => a.CardId == previous && a.Kind == LegalActionKind.Equip)); s.Play();
                if (scene == "equipment-lion") { s.Use("wound"); s.Play(); } else { grain = s.V().Hand.First(Marked).Id; s.Use("store-grain", [grain.Value]); s.Play(); }
            }
            var shown = s.V().Hand.First(c => !Marked(c)).Id; var material = s.Study(); s.ReachDiou(); s.ShowDiou(shown); s.Reach(() => s.DiouBranch("use")); s.UseDiou();
            s.Reach(() => s.Hold(scene == "equipment-lion" ? "lion-child" : scene == "equipment-ox" ? "ox-child" : "draw-child"));
            var start = s.E<DiouStartedEvent>().Last();
            s.Need(!s.E<DiouCompletedEvent>().Any(e => e.FrameId == start.FrameId) && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == start.FrameId && f.Diou is not null),
                "An actual native replacement or Draw child suspends the owning paid Diou frame with its replayable receipt.");
            s.Private(); s.RejectPrompt(); s.Cold(); s.Continue();
            if (scene == "draw-death") { s.Reach(() => s.Hold("death-entry")); s.Cold(); s.Continue(); s.Reach(() => !s.V().IsAlive); s.Reach(() => s.E<DiouCompletedEvent>().Any(e => e.FrameId == start.FrameId)); }
            else s.Play();
            s.Need(s.E<DiouCompletedEvent>().Count(e => e.FrameId == start.FrameId && e.Shown && e.Used) == 1 &&
                s.G.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.E<DiouNativeIssuedEvent>().Count(e => e.FrameId == start.FrameId) == 1 && s.E<DiouNativeReturnedEvent>().Count(e => e.FrameId == start.FrameId) == 1,
                "Cold return drains the already-issued real children once, without repaying the original or issuing another clone.");
            if (scene.StartsWith("draw-", StringComparison.Ordinal))
                s.Need(s.G.CardMovements.Count(m => m.Reason.Value == DrawReason && m.To == CardLocation.Hand(0)) == 2 &&
                    (scene == "draw-death" ? !s.V().IsAlive && s.G.State.Winner == Winner.None : !s.Has(Diou)),
                    "The paid native Draw2 returns once even when its gain child removes the source or causes a nonterminal death.");
            else
            {
                var clone = s.E<DiouNativeIssuedEvent>().Last(); s.Need(!s.Has(Diou) && s.V().Hand.Any(c => c.Id == shown) && s.V().Equipment.Any(c => c.Id == clone.CarrierId) &&
                    s.G.CardMovements.Count(m => m.CardId == previous && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1 &&
                    (grain is null || s.G.CardMovements.Count(m => m.CardId == grain && m.From.Zone == CardZoneKind.WoodenOxGrain && m.To == CardLocation.DiscardPile) == 1),
                    "Native replacement really removes the old physical gear and genuine Ox grain, while the independent clone and shown original survive source loss.");
                if (scene == "equipment-lion")
                {
                    s.Native(s.Actions().First(a => a.CardId == shown && a.Kind == LegalActionKind.Equip)); s.Play();
                    s.Need(s.V().Equipment.Any(c => c.Id == shown && c.Kind == CardKind.SilverLion), "A genuine physical equipment Use replaces the earlier independent clone.");
                }
                else
                {
                    s.Use("move-gear", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
                    s.Answer(c => c.Cards.SequenceEqual([clone.CarrierId])); s.Play();
                    s.Need(s.V(1).Equipment.Any(c => c.Id == clone.CarrierId), "A real corresponding-zone field transfer preserves the same equipment carrier at its new holder.");
                    var snatch = s.Actions().First(a => a.ConversionSource?.SkillId == Conv && a.PlayedCardKind == CardKind.Snatch && a.TargetSeats.SequenceEqual([1]) && a.TargetCardId == clone.CarrierId);
                    s.Native(snatch); s.Play();
                    s.Need(s.E<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.Snatch),
                        "A genuine physical view-as Snatch targets the exact public carrier in another holder's equipment area.");
                }
                s.Need(Enumerable.Range(0, 4).All(i => !s.V(i).Hand.Any(c => c.Id == clone.CarrierId) && !s.V(i).Equipment.Any(c => c.Id == clone.CarrierId)) &&
                    s.G.CardMovements.Any(m => m.CardId == clone.CarrierId && m.To.Zone == CardZoneKind.OutsideGame),
                    "Real replacement or native acquisition retires the carrier outside the game instead of manufacturing a reusable physical Hand card."); s.Cold();
            }
        }
        foreach (var kind in new[] { CardKind.Indulgence, CardKind.Lightning })
        {
            var s = Scene.Start("retire-" + kind, kind); var shown = s.V().Hand.First(c => !Marked(c)).Id;
            _ = s.Study(); s.ReachDiou(); s.ShowDiou(shown); s.Reach(() => s.DiouBranch("use")); s.UseDiou(); s.Play();
            var carrier = s.E<DiouNativeIssuedEvent>().Single().CarrierId; s.Cold(); s.End();
            if (kind == CardKind.Indulgence)
            {
                s.Reach(() => s.E<DelayedCardResolvedEvent>().Any(e => e.CardId == carrier));
                s.Need(s.G.CardMovements.Count(m => m.CardId == carrier && m.From == CardLocation.Judgment(1) && m.To == CardLocation.OutsideGame) == 1,
                    "The original delayed judgment finishes and retires its neutral Indulgence carrier once, without inserting it into ordinary Discard.");
            }
            else
            {
                s.Reach(() => s.E<LightningResolvedEvent>().Any(e => e.CardId == carrier && !e.Hit && e.NextTargetSeat == 1));
                s.Need(s.V(1).Judgment.Any(c => c.Id == carrier) && !s.E<DiouCarrierDestroyedEvent>().Any(e => e.CardId == carrier),
                    "A real failed Lightning judgment transfers the same still-valid persistent carrier to the next judgment area.");
                s.Play(); s.Use("take-judgment", targets: [1]); s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
                s.Answer(c => c.Cards.SequenceEqual([carrier])); s.Play();
                s.Need(s.G.CardMovements.Count(m => m.CardId == carrier && m.From == CardLocation.Judgment(1) && m.To == CardLocation.OutsideGame) == 1,
                    "Attempted acquisition after a genuine judgment-area transfer destroys the same carrier instead of turning it into a Hand entity.");
            }
            s.Need(!s.G.CardMovements.Any(m => m.CardId == carrier && m.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.DrawPile or CardZoneKind.Hand) &&
                s.E<DiouCarrierDestroyedEvent>().Count(e => e.CardId == carrier) == 1,
                "A destroyed persistent clone cannot contaminate Discard, Hand or a later shuffled draw pile."); s.Cold();
        }
        var liyu = Scene.Start("liyu-judgment", CardKind.Indulgence); var shownJudge = liyu.V().Hand.First(c => !Marked(c)).Id;
        _ = liyu.Study(); liyu.ReachDiou(); liyu.ShowDiou(shownJudge); liyu.Reach(() => liyu.DiouBranch("use")); liyu.UseDiou(); liyu.Play();
        var judgmentCarrier = liyu.E<DiouNativeIssuedEvent>().Single().CarrierId; var handBefore = liyu.V().HandCount; var victimBefore = liyu.V(1).HandCount;
        liyu.Native(liyu.Actions().First(a => a.ConversionSource?.SkillId == Conv && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.SequenceEqual([1]) &&
            a.CardId is { } id && id != shownJudge && liyu.V().Hand.Any(c => c.Id == id && !Marked(c))));
        liyu.Reach(() => liyu.P?.SkillPrompt?.SkillId == Liyu && liyu.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        liyu.Activate(); liyu.Reach(() => liyu.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "damage-target-obtain") == true);
        liyu.Need(liyu.P!.Choices.Any(c => c.Cards.SequenceEqual([judgmentCarrier]) && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Judgment)),
            "A real Liyu damage child offers the public virtual judgment entity as a legal native Obtain choice.");
        liyu.Private(); liyu.RejectPrompt(); liyu.Cold(); liyu.Answer(c => c.Cards.SequenceEqual([judgmentCarrier])); liyu.Play();
        liyu.Need(liyu.G.CardMovements.Count(m => m.CardId == judgmentCarrier && m.From == CardLocation.Judgment(1) && m.To == CardLocation.OutsideGame &&
                m.Reason.Value == "skill-program.damage-target-obtain.obtain") == 1 &&
            liyu.V().HandCount == handBefore - 1 && liyu.V().Hand.Any(c => c.Id == shownJudge) && liyu.V(1).HandCount == victimBefore &&
            !liyu.G.CardMovements.Any(m => m.Reason.Value == "skill-program.damage-target-obtain.benefit-draw") &&
            !liyu.E<CardUseDeclaredEvent>().Any(e => e.CardKind == CardKind.Duel) && liyu.E<DiouCarrierDestroyedEvent>().Single().From == CardLocation.Judgment(1),
            "Liyu's real Judgment-to-Outside destruction has actual obtain count zero, grants no category benefit and returns its original Slash without a false receipt or Duel."); liyu.Cold();
    }

    // Fixed small real games; no mutation of engine state, seed searches or complete-match simulation.
    private sealed class Fixture(string scene, CardKind kind) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-diao-chan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var s = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.le-diao-chan." + suffix + ".json") ?? throw new InvalidOperationException("Missing real le-diao-chan bundle"); using var r = new StreamReader(s); return r.ReadToEnd(); }
            var real = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in new[] { Tb, Diou }) b.AddSkill(new(id, real.Presentations[id].Name, real.Presentations[id].Description) { Program = real.Programs[id], ProgramPresentation = real.Presentations[id] });
            if (scene == "liyu-judgment")
            {
                string ReadLiyu(string suffix) { using var s = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-lu-bu." + suffix + ".json") ?? throw new InvalidOperationException("Missing real Liyu bundle"); using var r = new StreamReader(s); return r.ReadToEnd(); }
                var actual = SkillProgramCatalog.Load(ReadLiyu("rules"), ReadLiyu("presentation"));
                b.AddSkill(new(Liyu, actual.Presentations[Liyu].Name, actual.Presentations[Liyu].Description) { Program = actual.Programs[Liyu], ProgramPresentation = actual.Presentations[Liyu] });
            }
            b.AddSkill(new(Neutral, "普通替代技能", "真实失去来源的中性定义"));
            static JsonNode N(string value) => JsonNode.Parse(value)!;
            static JsonObject A(string id, string effects, int min = 0, int max = 0, int targets = 0) => new() { ["id"] = id, ["minCards"] = min, ["maxCards"] = max,
                ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["sourceZones"] = N("[\"hand\"]"), ["effects"] = N("[" + effects + "]") };
            static string Pause(string id) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held-" + id + "\",\"options\":[{\"id\":\"" + id + "\"}]}";
            const string capture = "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"selected\"}";
            var acts = new JsonArray(A("draw-four", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":4}"),
                A("show", "{\"op\":\"selectOwnedCards\",\"target\":\"owner\",\"zones\":[\"hand\"],\"minimumCards\":1,\"maximumCards\":1,\"resultBind\":\"selected\"},{\"op\":\"revealBoundCards\",\"target\":\"owner\",\"sourceBind\":\"selected\"}"),
                A("give", capture + ",{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"selected\",\"destination\":\"selectedTargetHand\"}", 1, 1, 1),
                A("give-many", capture + ",{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"selected\",\"destination\":\"selectedTargetHand\"}", 1, 20, 1),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1),
                A("lose-tanban", "{\"op\":\"loseOwnerSkillsAndGrant\",\"target\":\"owner\",\"skillIds\":[\"" + Tb + "\"],\"sourceBind\":\"" + Neutral + "\"}"),
                A("grant-tanban", "{\"op\":\"grantSkills\",\"target\":\"owner\",\"skillIds\":[\"" + Tb + "\"]}"),
                A("wound", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("near-dying", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":12}"),
                A("incoming", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", targets: 1),
                A("arm-peer", "{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"real-weapon\"}", targets: 1),
                A("store-grain", capture + ",{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"selected\",\"destination\":\"ownerPersistentZone\",\"destinationZone\":\"woodenOxGrain\"}", 1, 1),
                A("take-gear", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}"));
            acts.Add(A("take-judgment", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"judgment\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", targets: 1));
            acts.Add(A("move-gear", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"targetRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"equipment\"],\"count\":1,\"destination\":\"selectedTargetCorrespondingZone\",\"awaitMovementTriggers\":true}", targets: 1));
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", targets: 1); duel["targetKind"] = "otherLiving"; duel["usesPerPhase"] = 2; acts.Add(duel);
            var driver = new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts };
            if (scene != "opening") driver["modifiers"] = N("[{\"id\":\"small-real-inventory\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":40,\"priority\":0},{\"id\":\"legal-native-slash-stock\",\"query\":\"slashLimit\",\"operation\":\"add\",\"value\":4,\"priority\":0}]");
            var defs = new JsonArray(driver, N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            defs.Add(N("{\"id\":\"" + Conv + "\",\"revision\":1,\"viewAs\":[{\"id\":\"real-study\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"drawTwo\",\"forPlay\":true,\"forResponse\":false,\"singleCardTrickUse\":true},{\"id\":\"real-dodge\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"dodge\",\"forPlay\":false,\"forResponse\":true},{\"id\":\"real-null\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"nullification\",\"forPlay\":false,\"forResponse\":true},{\"id\":\"real-rescue\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"peach\",\"forPlay\":false,\"forResponse\":true,\"extendedUse\":true}]}"));
            if (scene == "equipment-ox") defs.Last()!["viewAs"]!.AsArray().Add(N("{\"id\":\"real-carrier-snatch\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"snatch\",\"forPlay\":true,\"forResponse\":false,\"singleCardTrickUse\":true}"));
            if (scene == "liyu-judgment") defs.Last()!["viewAs"]!.AsArray().Add(N("{\"id\":\"real-liyu-slash\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false}"));
            if (kind == CardKind.BorrowedSword) defs.Add(N("{\"id\":\"" + PeerSlash + "\",\"revision\":1,\"viewAs\":[{\"id\":\"native-forced-slash\",\"inputKinds\":[],\"inputSuits\":[],\"sourceZones\":[\"hand\"],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false}]}"));
            var own = new List<string> { Driver, Conv, Tb };
            if (scene == "liyu-judgment") own.Add(Liyu);
            if (scene is "draw-loss" or "equipment-ox" or "equipment-lion")
            { defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"exact-health-diou\",\"skillId\":\"" + Diou + "\"," + (scene == "equipment-lion" ? "\"maximumHp\":12" : "\"minimumHp\":13") + "}]}")); own.Add(Conditional); }
            else if (scene is not ("opening" or "swap")) own.Add(Diou);
            var watch = new JsonArray();
            if (scene.StartsWith("family-", StringComparison.Ordinal)) watch.Add(N("{\"id\":\"inspect-real-completed-use\",\"window\":\"cardUseCompleted\",\"ownerRelation\":\"actor\",\"includeResponseUses\":true,\"optional\":false,\"effects\":[" + Pause("inspect-use") + "]}"));
            if (scene == "adjusted-duel") watch.Add(N("{\"id\":\"real-completed-study-grant\",\"window\":\"cardUseCompleted\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"drawTwo\"],\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"grantNextCardTargetAdjustment\",\"target\":\"owner\"}]}"));
            if (scene == "opening") watch.Add(N("{\"id\":\"before-initial-mark\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"priority\":1000,\"effects\":[" + Pause("opening") + "]}"));
            if (scene.StartsWith("draw-", StringComparison.Ordinal)) watch.Add(N("{\"id\":\"real-diou-draw-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + DrawReason + "\"],\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + Pause("draw-child") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "draw-death" ? 20 : 1) + "}]}"));
            if (scene == "draw-death") watch.Add(N("{\"id\":\"real-death-entry\",\"window\":\"dyingEntering\",\"subject\":\"owner\",\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[" + Pause("death-entry") + "]}"));
            if (scene == "equipment-lion") watch.Add(N("{\"id\":\"real-lion-heal-child\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":false,\"effects\":[" + Pause("lion-child") + "]}"));
            if (scene == "equipment-ox") watch.Add(N("{\"id\":\"real-ox-spill-child\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"woodenOxGrain\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"equipment.wooden-ox.grain-discard\"],\"optional\":false,\"effects\":[" + Pause("ox-child") + ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}"));
            if (watch.Count > 0) { defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watch }); own.Add(Watch); }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "乐貂蝉固定小局真实实体与原生孩子",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(t => t!["effects"]!.AsArray()).SelectMany(e => e!["options"]?.AsArray() ?? new JsonArray()).Select(o => o!["id"]!.GetValue<string>()).Distinct().ToDictionary(x => x, x => x) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            const string choose = "fixture:ldc-owner-choice"; b.AddSkill(new(choose, "固定本人", "真实选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            b.AddGeneral(new(Owner, "乐貂蝉真实本人", "supporter", choose, "qun", scene == "opening" ? 3 : 12, own));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:ldc-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var pick = "fixture:ldc-peer-pick-" + i; b.AddSkill(new(pick, "固定他人", "真实选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (4 - i)) });
                var skills = new List<string> { Quiet }; if (i == 1 && kind == CardKind.BorrowedSword) skills.Add(PeerSlash);
                b.AddGeneral(new(peers[i - 1], "实际其他角色", "supporter", pick, "wei", 12, skills));
            }
            const string entity = "fixture:ldc-original"; var def = CardCatalog.Get(kind); b.AddCard(new(entity, def.DisplayName, def.CategoryName, "固定原始牌面", kind));
            if (kind == CardKind.BorrowedSword) { var gear = CardCatalog.Get(CardKind.Crossbow); b.AddCard(new("fixture:ldc-real-weapon", gear.DisplayName, gear.CategoryName, "真实持刀武器", CardKind.Crossbow)); }
            b.AddDeck(new("fixture:ldc-deck", "四十八真实原实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(i => new ContentDeckPhysicalCard(kind == CardKind.BorrowedSword && i >= 44 ? "fixture:ldc-real-weapon" : entity, kind == CardKind.Lightning ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            var roles = scene == "draw-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 } : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 };
            b.AddMode(new(Mode, "乐貂蝉原生行为小局", 4, 4, roles, "fixture:ldc-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }

    private sealed class Scene(string scene, GameEngine game, ContentRegistry registry)
    {
        private int? _retainedShown;
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Has(string id) => V().Skills?.Any(k => k.ContentId == id) == true;
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool IsDiou(PromptChoice c, string branch) => c.Parameters.GetValueOrDefault("program-action") == "le-diou" && c.Parameters.GetValueOrDefault("branch") == branch;
        public bool DiouBranch(string branch) => P is { PlayerSeat: 0 } p && p.Choices.Any(c => IsDiou(c, branch));
        public bool TanbanOffer() => P?.SkillPrompt?.SkillId == Tb && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public bool Hold(string id) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
        public void Use(string id, int[]? cards = null, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        public void Native(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public int Study()
        {
            var a = Actions().FirstOrDefault(a => (a.Kind == LegalActionKind.DrawTwo && a.ConversionSource is null || a.ConversionSource?.SkillId == Conv && a.PlayedCardKind == CardKind.DrawTwo) &&
                a.CardId is { } id && V().Hand.Any(c => c.Id == id && Marked(c)));
            Need(a is not null, "An original marked Hand card must have a real natural or converted DrawTwo Use before its child."); Native(a!); return a!.CardId!.Value;
        }
        public void ReachDiou()
        {
            Reach(() => DiouBranch("show") || P?.SkillPrompt?.SkillId == Diou && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
            if (!DiouBranch("show")) Activate(); Reach(() => DiouBranch("show"));
        }
        public void ShowDiou(int id) { _retainedShown = id; Answer(c => IsDiou(c, "show") && c.Cards.SequenceEqual([id])); }
        public void UseDiou()
        {
            var plans = P!.Choices.Where(c => IsDiou(c, "use")).ToArray(); Need(plans.Length > 0, "At least one real published native plan is required.");
            var selected = plans.FirstOrDefault(c => c.Targets.Contains(1)) ?? plans.FirstOrDefault(c => c.Targets.Count == 0 || c.Targets.SequenceEqual([0])) ?? plans[0];
            Answer(c => c.Id == selected.Id);
        }
        public void Show(int id) { Use("show"); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards") == true); Answer(c => c.Cards.SequenceEqual([id])); Play(); }
        public int Respond()
        {
            var c = P!.Choices.First(c => c.Cards.Count == 1 && V().Hand.Any(card => card.Id == c.Cards[0] && Marked(card)) && c.Parameters.GetValueOrDefault("response") is not ("pass" or "take-damage" or "let-die"));
            var id = c.Cards[0]; Private(); RejectPrompt(); Answer(choice => choice.Id == c.Id); return id;
        }
        public void Take(int seat, int id)
        {
            var slot = V(seat).Hand.Select((c, i) => (c.Id, i)).Single(c => c.Id == id).i; Use("take", targets: [seat]);
            Reach(() => P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); Need(P!.Choices.All(c => c.Cards.Count == 0), "Foreign Hand acquisition exposes opaque slots only.");
            Answer(c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.ContainsKey("option-id"));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match)
        {
            var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "The real chooser answers exactly one published choice.");
            Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
        }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The fixture ended before native boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.SkillPrompt?.SkillId == Watch) Continue();
                else if (p.Kind == DecisionKind.SelectHarvestCard) { if (scene == "family-FiveGrains") Cold(); Answer(c => c.Cards.Count == 1); }
                else if (p.Kind == DecisionKind.FireAttackReveal) Answer(c => c.Cards.Count == 1);
                else if (p.Kind == DecisionKind.FireAttackDiscard) { Cold(); Answer(c => c.Cards.Count == 1 && c.Cards[0] != _retainedShown); }
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (p.Choices.Any(c => IsDiou(c, "cancel"))) Answer(c => IsDiou(c, "cancel"));
                else if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected native boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded native boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)),
            "Only the actual chooser receives its private canonical choices and all foreign Hands remain hidden.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay preserves prepared views, original marks, claims, carriers, paid children and accepted commands."); }
        public void Reject(GameCommand c) { var before = State(); Need(!G.Submit(c).Accepted && State() == before, "A stale, forged or foreign command cannot change any view, claim, payment or history."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-le-diao-chan"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public void Accept(GameCommand c)
        {
            try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, "Rejected " + c.GetType().Name + ": " + r.Error?.Code + "/" + r.Error?.Message); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " cause=" + e.Message + " " + Diagnostic(), e); }
        }
        public void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { Scene = scene, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner,
            Prompt = P is { } p ? new { p.Kind, p.PlayerSeat, p.PromptId, p.RequiredCardKind, Choices = p.Choices.Take(5) } : null,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).MaxHp, V(i).IsAlive, V(i).HandCount, V(i).HandLimit }),
            Frames = G.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step, Diou = (f as ProgramSkillFrame)?.Diou,
                Native = f is CardUseFrame u ? new { u.CardId, u.SourceSeat, u.TargetSeats, u.PhysicalCardIds, u.Action, u.DiouClaims } : null }),
            OwnHand = V().Hand.Select(c => new { c.Id, c.Kind, c.SkillMarkLabel }),
            Started = E<DiouStartedEvent>().TakeLast(2), Shown = E<DiouShownEvent>().TakeLast(2), Issued = E<DiouNativeIssuedEvent>().TakeLast(2), Completed = E<DiouCompletedEvent>().TakeLast(2) });
        public static Scene Start(string scene, CardKind kind = CardKind.Slash, bool holdOpening = false, bool holdSwap = false)
        {
            ContentRegistry registry; try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene, kind)); }
            catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture=" + e.Message, e); }
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "draw-death" ? Role.Renegade : Role.Lord,
                ModeId = Mode, UseInteractiveSetup = true, ConfirmOpeningHand = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(scene, g, registry); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
            s.Accept(new SelectGeneralCommand(0, Owner, g.Revision, s.P!.PromptId)); if (holdOpening) s.Reach(() => s.Hold("opening")); else if (holdSwap) s.Reach(s.TanbanOffer); else s.Play(); return s;
        }
    }
}
