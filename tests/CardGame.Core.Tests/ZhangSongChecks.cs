using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhangSongChecks
{
    private const string ClassicGeneral = "classic:zhang-song";
    private const string BoundaryGeneral = "boundary:zhang-song";
    private const string ClassicQiangzhi = "classic:qiangzhi";
    private const string BoundaryQiangzhi = "boundary:qiangzhi";
    private const string ClassicXiantu = "classic:xiantu";
    private const string BoundaryXiantu = "boundary:xiantu";

    public static void ClassicQiangzhiRevealsThenDrawsOnMatchingCategory()
    {
        var registry = Registry();
        var completed = 0;
        var declinedDraws = 0;
        for (var seed = 1; seed <= 320 && (completed < 4 || declinedDraws < 1); seed++)
        {
            var game = Start(registry, seed, ClassicGeneral);
            if (!AdvanceToOwnPrompt(game, ClassicQiangzhi, out var prompt) ||
                prompt.SkillPrompt?.SkillId != ClassicQiangzhi)
                continue;

            // Decline the reveal first: no card becomes visible and no flag is set.
            if (declinedDraws < 1)
            {
                var skip = prompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip");
                if (!Answer(game, skip.Id).Accepted) continue;
                Require(AdvanceToPlayDecision(game),
                    $"Declining Qiangzhi must return to the play decision (seed {seed}, " +
                    $"pending={DescribePending(game)}).");
                Require(!BooleanState(game, 0, ClassicQiangzhi, "shown-basic") &&
                        !BooleanState(game, 0, ClassicQiangzhi, "shown-trick") &&
                        !BooleanState(game, 0, ClassicQiangzhi, "shown-equipment"),
                    "A declined reveal must leave every shown flag false.");
                declinedDraws++;
                if (!RunToEndOfTurn(game)) continue;
                continue;
            }

            var activate = prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            if (!Answer(game, activate.Id).Accepted) continue;
            var targetPrompt = game.PendingDecision;
            if (targetPrompt is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } ||
                targetPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") != "select-target"))
                continue;
            var targetSeat = targetPrompt.Choices
                .Select(choice => choice.Targets.FirstOrDefault())
                .OfType<int>()
                .OrderByDescending(seat => game.CreateSnapshot(0, true).Players[seat].HandCount)
                .FirstOrDefault();
            var pick = targetPrompt.Choices.Single(choice => choice.Targets.Contains(targetSeat));
            if (!Answer(game, pick.Id).Accepted) continue;
            if (!AdvanceToPlayDecision(game)) continue;

            var revealed = game.Events.Select(item => item.Payload)
                .OfType<ProgramCardsRevealedEvent>()
                .Where(item => item.SkillId == ClassicQiangzhi && item.OwnerSeat == 0)
                .LastOrDefault();
            if (revealed is not { Cards.Count: 1 }) continue;
            var category = CategoryOf(revealed.Cards[0].Kind);
            Require(game.CreateSnapshot(0, true).Players[targetSeat].Hand
                        .Any(card => card.Id == revealed.Cards[0].Id),
                "The revealed card must stay in the holder's hand.");
            Require(game.CreateSnapshot(0, true).Players[0].Hand.All(card => card.Id != revealed.Cards[0].Id),
                "The revealed card must not move to Zhang Song's hand.");
            Require(BooleanState(game, 0, ClassicQiangzhi, StateIdForCategory(category)) &&
                    CategoryIds().Where(id => id != StateIdForCategory(category))
                        .All(id => !BooleanState(game, 0, ClassicQiangzhi, id)),
                "Exactly the revealed card's category flag must be set after the reveal.");

            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) continue;
            var kinds = HandAndEquipmentKinds(game, 0);
            // Only Slash and trick uses open the before-target-effects window, so
            // equipment and Peach reveals cannot exercise the draw clause.
            var matching = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.CardId is { } cardId && kinds.TryGetValue(cardId, out var kind) &&
                CategoryOf(kind) == category &&
                (kind == CardKind.Slash || category == SkillProgramCardCategory.Trick));
            if (matching is null) continue;
            var movementsBefore = game.CardMovements.Count;
            var handBefore = game.CreateSnapshot(0, true).Players[0].HandCount;
            if (!Play(game, matching)) continue;
            var drawPrompt = DriveToSkillPrompt(game, ClassicQiangzhi);
            Require(drawPrompt is not null,
                $"Using a {category} card after the reveal must offer the Qiangzhi draw (seed {seed}, " +
                $"pending={DescribePending(game)}).");
            if (drawPrompt is null) continue;
            var drawActivate = drawPrompt!.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Require(Answer(game, drawActivate.Id).Accepted,
                "Accepting the Qiangzhi draw failed.");
            var playedKind = kinds[matching.CardId!.Value];
            var playedSelfGain = playedKind switch
            {
                CardKind.DrawTwo => 2,
                CardKind.Snatch => 1,
                _ => 0
            };
            Require(game.CreateSnapshot(0, true).Players[0].HandCount ==
                    handBefore - 1 + playedSelfGain + 1,
                $"Qiangzhi must net one card back after playing the matching card " +
                $"(seed {seed}, kind={playedKind}, before={handBefore}, " +
                $"after={game.CreateSnapshot(0, true).Players[0].HandCount}, gain={playedSelfGain}).");
            Require(game.CardMovements.Skip(movementsBefore).Count(move =>
                        move.Reason.Value == $"skill-program.{ClassicQiangzhi}.Draw" &&
                        move.To == CardLocation.Hand(0)) == 1,
                "Qiangzhi's draw must move exactly one card into Zhang Song's hand.");
            completed++;
        }
        Require(completed >= 2 && declinedDraws >= 1,
            $"The classic Qiangzhi fixtures must cover reveal/draw and decline (completed={completed}, declined={declinedDraws}).");
    }

    public static void BoundaryQiangzhiViewsHandAndChoosesReveal()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 320 && completed < 3; seed++)
        {
            var game = Start(registry, seed, BoundaryGeneral);
            if (!AdvanceToOwnPrompt(game, BoundaryQiangzhi, out var prompt) ||
                prompt.SkillPrompt?.SkillId != BoundaryQiangzhi)
                continue;
            var activate = prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate");
            if (!Answer(game, activate.Id).Accepted) continue;
            var targetPrompt = game.PendingDecision;
            if (targetPrompt is not { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } ||
                targetPrompt.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") != "select-target"))
                continue;
            var targetSeat = targetPrompt.Choices
                .Select(choice => choice.Targets.FirstOrDefault())
                .OfType<int>()
                .OrderByDescending(seat => game.CreateSnapshot(0, true).Players[seat].HandCount)
                .FirstOrDefault();
            if (!Answer(game, targetPrompt.Choices.Single(choice => choice.Targets.Contains(targetSeat)).Id).Accepted)
                continue;

            var draft = game.PendingDecision;
            if (draft is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true, PlayerSeat: 0 } reveal ||
                reveal.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") != "reveal-target-hand-card"))
                continue;
            var holderHand = game.CreateSnapshot(0, true).Players[targetSeat].Hand;
            Require(reveal.Choices.Count == holderHand.Count &&
                    reveal.Choices.All(choice => choice.Cards.Count == 1 &&
                        holderHand.Any(card => card.Id == choice.Cards[0])),
                "The boundary reveal draft must offer exactly the target's hand cards.");

            // Prefer a candidate whose category Zhang Song can actually use through
            // the before-target-effects window (Slash or a trick).
            var kinds = HandAndEquipmentKinds(game, 0);
            var pick = reveal.Choices.FirstOrDefault(choice =>
            {
                var category = CategoryOf(holderHand
                    .First(card => card.Id == choice.Cards[0]).Kind);
                return category != SkillProgramCardCategory.Equipment &&
                    game.GetHumanLegalActions().Any(action =>
                        action.CardId is { } cardId && kinds.TryGetValue(cardId, out var kind) &&
                        CategoryOf(kind) == category &&
                        (kind == CardKind.Slash || category == SkillProgramCardCategory.Trick));
            }) ?? reveal.Choices.FirstOrDefault();
            if (pick is null) continue;
            var pickedCategory = CategoryOf(holderHand.First(card => card.Id == pick.Cards[0]).Kind);

            var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), Registry());
            Require(State(restored) == State(game) && Events(restored).SequenceEqual(Events(game)),
                $"The boundary reveal draft must restore with the same pending choice (seed {seed}).");

            if (!Answer(game, pick.Id).Accepted) continue;
            if (!AdvanceToPlayDecision(game)) continue;
            var revealed = game.Events.Select(item => item.Payload)
                .OfType<ProgramCardsRevealedEvent>()
                .Where(item => item.SkillId == BoundaryQiangzhi && item.OwnerSeat == 0)
                .LastOrDefault();
            if (revealed is not { Cards.Count: 1 } || revealed.Cards[0].Id != pick.Cards[0]) continue;
            Require(CategoryOf(revealed.Cards[0].Kind) == pickedCategory,
                "The revealed card must be exactly the chosen candidate.");
            Require(BooleanState(game, 0, BoundaryQiangzhi, StateIdForCategory(pickedCategory)),
                "The boundary reveal must set the matching category flag.");

            if (game.PendingDecision is not { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) continue;
            var handBefore = game.CreateSnapshot(0, true).Players[0].HandCount;
            var matching = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.CardId is { } cardId && kinds.TryGetValue(cardId, out var kind) &&
                CategoryOf(kind) == pickedCategory &&
                (kind == CardKind.Slash || pickedCategory == SkillProgramCardCategory.Trick));
            if (matching is null) continue;
            var movementsBefore = game.CardMovements.Count;
            if (!Play(game, matching)) continue;
            var drawPrompt = DriveToSkillPrompt(game, BoundaryQiangzhi);
            if (drawPrompt is null) continue;
            Require(Answer(game, drawPrompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "activate").Id).Accepted,
                "Accepting the boundary Qiangzhi draw failed.");
            var playedSelfGain = kinds[matching.CardId!.Value] switch
            {
                CardKind.DrawTwo => 2,
                CardKind.Snatch => 1,
                _ => 0
            };
            Require(game.CreateSnapshot(0, true).Players[0].HandCount ==
                    handBefore - 1 + playedSelfGain + 1 &&
                    game.CardMovements.Skip(movementsBefore).Count(move =>
                        move.Reason.Value == $"skill-program.{BoundaryQiangzhi}.Draw" &&
                        move.To == CardLocation.Hand(0)) == 1,
                $"Boundary Qiangzhi must draw exactly one card for the matching use " +
                $"(seed {seed}, kind={kinds[matching.CardId!.Value]}, before={handBefore}, " +
                $"after={game.CreateSnapshot(0, true).Players[0].HandCount}, gain={playedSelfGain}).");
            completed++;
        }
        Require(completed >= 2,
            $"The boundary Qiangzhi fixtures must cover viewing and choosing the reveal (completed={completed}).");
    }

    public static void ClassicXiantuGiftsTwoAndPenalizesWithoutKill()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 96 && completed < 3; seed++)
        {
            var game = Start(registry, seed, ClassicGeneral);
            var gifts = 0;
            var eventsBefore = game.Events.Count;
            for (var phase = 0; phase < 2; phase++)
            {
                if (!RunToXiantuPrompt(game, ClassicXiantu, out var prompt)) break;
                var phaseOwner = game.CreateSnapshot(0, true).CurrentSeat;
                var movesBefore = game.CardMovements.Count;
                var activate = prompt.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "activate");
                if (!Answer(game, activate.Id).Accepted) break;
                CompleteGiftSelection(game);
                Require(BooleanState(game, 0, ClassicXiantu, "gifted"),
                    "The gifted flag must be set while the gifted phase runs.");
                Require(game.CardMovements.Skip(movesBefore).Count(move =>
                            move.Reason.Value == $"skill-program.{ClassicXiantu}.MoveBoundCards" &&
                            move.To == CardLocation.Hand(phaseOwner)) == 2 &&
                        game.CardMovements.Skip(movesBefore).Count(move =>
                            move.Reason.Value == $"skill-program.{ClassicXiantu}.Draw" &&
                            move.To == CardLocation.Hand(0)) == 2,
                    "Classic Xiantu must draw two cards and hand both to the phase owner.");
                gifts++;
                if (!RunToEndOfTurn(game)) break;
            }
            if (gifts < 2) continue;
            var hpLost = game.Events.Skip(eventsBefore).Select(item => item.Payload)
                .OfType<ProgramSkillHpLostEvent>()
                .Where(item => item.SkillId == ClassicXiantu && item.TargetSeat == 0)
                .ToArray();
            Require(hpLost.Length == 2 && hpLost.All(item => item.Amount == 1) &&
                    hpLost[1].RemainingHp < hpLost[0].RemainingHp &&
                    game.CreateSnapshot(0, true).Players[0].IsAlive,
                $"Classic Xiantu must cost one HP after each killless gifted phase (seed {seed}, " +
                $"events={DescribeHpLost(hpLost)}).");
            Require(!BooleanState(game, 0, ClassicXiantu, "gifted"),
                "The gifted flag must be cleared again at the turn end.");
            completed++;
        }
        Require(completed >= 2,
            $"The classic Xiantu fixtures must cover gift and penalty (completed={completed}).");
    }

    public static void BoundaryXiantuChoosesGiftAmountAndPenalty()
    {
        var registry = Registry();
        var completed = 0;
        for (var seed = 1; seed <= 96 && completed < 2; seed++)
        {
            var game = Start(registry, seed, BoundaryGeneral);
            var gifts = 0;
            var eventsBefore = game.Events.Count;
            var labels = new[] { "摸一张牌并交给该角色一张牌", "摸两张牌并交给该角色两张牌" };
            var giftedPhases = new List<(int Branch, int DamageDealt)>();
            for (var phase = 0; phase < 2; phase++)
            {
                if (!RunToXiantuPrompt(game, BoundaryXiantu, out var prompt)) break;
                var phaseOwner = game.CreateSnapshot(0, true).CurrentSeat;
                var movesBefore = game.CardMovements.Count;
                var phaseEventsBefore = game.Events.Count;
                Require(prompt.Choices.Count(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
                            choice.Parameters.GetValueOrDefault("choice-group") == "xiantu-choice") == 2 &&
                        prompt.Choices.Any(choice =>
                            choice.Parameters.GetValueOrDefault("program-action") == "skip"),
                    $"The boundary Xiantu group must expose both branches and a skip (seed {seed}).");
                Require(labels.All(label => prompt.Choices.Any(choice => choice.Description == label)),
                    $"Both Xiantu branches must carry their presentation labels (seed {seed}).");
                var branch = prompt.Choices.Single(choice => choice.Description == labels[phase]);
                if (!Answer(game, branch.Id).Accepted) break;
                CompleteGiftSelection(game);
                var expected = phase == 0 ? 1 : 2;
                Require(BooleanState(game, 0, BoundaryXiantu, phase == 0 ? "gifted-1" : "gifted-2") &&
                        !BooleanState(game, 0, BoundaryXiantu, phase == 0 ? "gifted-2" : "gifted-1"),
                    "Exactly the chosen branch's gifted flag must be set.");
                Require(game.CardMovements.Skip(movesBefore).Count(move =>
                            move.Reason.Value == $"skill-program.{BoundaryXiantu}.MoveBoundCards" &&
                            move.To == CardLocation.Hand(phaseOwner)) == expected &&
                        game.CardMovements.Skip(movesBefore).Count(move =>
                            move.Reason.Value == $"skill-program.{BoundaryXiantu}.Draw" &&
                            move.To == CardLocation.Hand(0)) == expected,
                    $"Boundary Xiantu branch {phase + 1} must draw and hand {expected} card(s) (seed {seed}).");
                gifts++;
                if (!RunToEndOfTurn(game)) break;
                giftedPhases.Add((phase, game.Events.Skip(phaseEventsBefore)
                    .Select(item => item.Payload)
                    .OfType<DamageAppliedEvent>()
                    .Where(damage => damage.SourceSeat == phaseOwner)
                    .Sum(damage => damage.Amount)));
            }
            if (giftedPhases.Count < 2) continue;
            var hpLost = game.Events.Skip(eventsBefore).Select(item => item.Payload)
                .OfType<ProgramSkillHpLostEvent>()
                .Where(item => item.SkillId == BoundaryXiantu && item.TargetSeat == 0)
                .ToArray();
            var expectedPenalties = giftedPhases.Count(record =>
                record.DamageDealt < (record.Branch == 0 ? 1 : 2));
            Require(hpLost.Length == expectedPenalties && hpLost.All(item => item.Amount == 1) &&
                    hpLost.Select(item => item.RemainingHp).SequenceEqual(
                        hpLost.Select(item => item.RemainingHp).OrderByDescending(item => item)) &&
                    game.CreateSnapshot(0, true).Players[0].IsAlive,
                $"Boundary Xiantu penalties must follow the branch damage limits (seed {seed}, " +
                $"phases={string.Join(";", giftedPhases.Select(item => $"branch{item.Branch}dmg{item.DamageDealt}"))}, " +
                $"events={DescribeHpLost(hpLost)}, gifts={gifts}, status={game.State.Status}, " +
                $"pending={DescribePending(game)}).");
            Require(!BooleanState(game, 0, BoundaryXiantu, "gifted-1") &&
                    !BooleanState(game, 0, BoundaryXiantu, "gifted-2"),
                "Both gifted flags must be cleared again at the turn end.");
            completed++;
        }
        Require(completed >= 2,
            $"The boundary Xiantu fixtures must cover both branches (completed={completed}).");
    }

    public static void EquipmentUsesReplaceAndResumeExactlyOnce()
    {
        foreach (var general in new[] { ClassicGeneral, BoundaryGeneral })
        {
            var skill = general == ClassicGeneral ? ClassicQiangzhi : BoundaryQiangzhi;
            var (game, registry) = FindEquipmentReveal(general, requirePair: true);
            var pair = game.CreateSnapshot(0, true).Players[0].Hand.Where(card => EquipmentCatalog.IsEquipment(card.Kind))
                .GroupBy(card => EquipmentCatalog.Get(card.Kind).Slot).First(group => group.Count() >= 2).Take(2).ToArray();
            for (var i = 0; i < pair.Length; i++)
            {
                Require(AdvanceToPlayDecision(game), "The equipment use must return to the play decision.");
                var action = game.GetHumanLegalActions().First(item => item.Kind == LegalActionKind.Equip && item.CardId == pair[i].Id);
                var before = game.CardMovements.Count;
                Require(Play(game, action), "The equipment use must be legal.");
                Require(game.PendingDecision?.SkillPrompt?.SkillId == skill &&
                        game.CreateSnapshot(0, true).Players[0].Equipment.Any(card => card.Id == pair[i].Id),
                    "A matching equipment use must offer Qiangzhi after the equipment enters its slot.");
                Require(game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>()
                        .Any(frame => frame.Continuation == ProgramCardContinuation.CommittedSimpleCard),
                    "Both current Qiangzhi texts trigger at use commitment, before the use completes.");
                var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                Require(State(game) == State(restored) && Events(game).SequenceEqual(Events(restored)),
                    "An equipment Qiangzhi prompt must restore exactly.");
                foreach (var branch in new[] { game, restored })
                {
                    var choice = branch.PendingDecision!.Choices.Single(item =>
                        item.Parameters.GetValueOrDefault("program-action") == (i == 0 ? "activate" : "skip"));
                    Require(Answer(branch, choice.Id).Accepted, "An equipment draw or decline must resume.");
                    Require(AdvanceToPlayDecision(branch), "The resumed equipment use must finish.");
                }
                Require(State(game) == State(restored) && Events(game).SequenceEqual(Events(restored)) &&
                        game.CardMovements.Skip(before).Count(move => move.Reason.Value == $"skill-program.{skill}.Draw") == (i == 0 ? 1 : 0),
                    "Equipment Qiangzhi must draw once, or zero after declining, including after restore.");
            }
            Require(game.CreateCardZoneDiagnostics().Single(card => card.CardId == pair[0].Id).Location == CardLocation.DiscardPile &&
                    game.CreateSnapshot(0, true).Players[0].Equipment.Any(card => card.Id == pair[1].Id),
                "Replacing equipment must discard the old physical card and retain the new one.");
        }
    }

    public static void CategoryMismatchAndPhaseLifetime()
    {
        foreach (var general in new[] { ClassicGeneral, BoundaryGeneral })
        {
            var skill = general == ClassicGeneral ? ClassicQiangzhi : BoundaryQiangzhi;
            var (game, _) = FindEquipmentReveal(general, requirePair: false);
            var mismatch = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash);
            Require(Play(game, mismatch), "The different-category card must remain playable.");
            Require(game.PendingDecision?.SkillPrompt?.SkillId != skill, "A category mismatch must not offer Qiangzhi.");
            Require(AdvanceToPlayDecision(game), "The different-category use must finish.");
            Require(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)).Accepted,
                "The play phase must end.");
            Require(BooleanState(game, 0, skill, "shown-equipment") == (general == ClassicGeneral),
                "Boundary Qiangzhi expires at play end; classic Qiangzhi remains for the rest of its own turn.");
            Require(RunToEndOfTurn(game) && !BooleanState(game, 0, skill, "shown-equipment"),
                "Neither Qiangzhi flag may leak into another player's turn.");
        }
    }

    public static void XiantuSelectsExistingCardsAndPenalizesBeforeDiscard()
    {
        foreach (var general in new[] { ClassicGeneral, BoundaryGeneral })
        {
            var skill = general == ClassicGeneral ? ClassicXiantu : BoundaryXiantu;
            var verified = false;
            var registry = Registry();
            for (var seed = 1; seed <= 32 && !verified; seed++)
            {
                var game = Start(registry, seed, general);
                if (!RunToXiantuPrompt(game, skill, out var prompt)) continue;
                var oldHand = game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToArray();
                if (oldHand.Length < 2) continue;
                var start = game.Events.Count;
                var phaseOwner = game.State.CurrentSeat;
                var activate = prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate" &&
                    (general == ClassicGeneral || choice.Parameters.GetValueOrDefault("binding-id") == "gift-two-cards"));
                Require(Answer(game, activate.Id).Accepted, "Xiantu must draw before choosing the gift.");
                Require(game.PendingDecision is { IsPrivate: true }, "The owner's gift selection must be private.");
                var paused = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
                foreach (var branch in new[] { game, paused })
                {
                    foreach (var id in oldHand.Take(2))
                        Require(Answer(branch, branch.PendingDecision!.Choices.Single(choice => choice.Cards.Contains(id)).Id).Accepted,
                            "Xiantu must allow handing over cards owned before its draw.");
                    Require(branch.CreateSnapshot(0, true).Players[phaseOwner].Hand.Any(card => card.Id == oldHand[0]) &&
                            branch.CreateSnapshot(0, true).Players[phaseOwner].Hand.Any(card => card.Id == oldHand[1]),
                        "The selected original cards must reach the phase owner.");
                    Require(RunToEndOfTurn(branch), "The gifted phase must finish.");
                }
                Require(State(game) == State(paused) && Events(game).SequenceEqual(Events(paused)),
                    "A partially resolved gift must replay exactly.");
                var events = game.Events.Skip(start).ToArray();
                var penalty = Array.FindIndex(events, item => item.Payload is ProgramSkillHpLostEvent loss && loss.SkillId == skill);
                if (penalty < 0) continue;
                var discard = Array.FindIndex(events, item => item.Payload is PhaseChangedEvent phase && phase.Phase == TurnPhase.Discard);
                Require(discard > penalty, "Xiantu's HP penalty must resolve before the recipient enters discard.");
                verified = true;
            }
            Require(verified, "Both Xiantu variants need a real gift-selection and phase-end penalty fixture.");
        }
    }

    private static (GameEngine Game, ContentRegistry Registry) FindEquipmentReveal(string general, bool requirePair)
    {
        var registry = Registry();
        var skill = general == ClassicGeneral ? ClassicQiangzhi : BoundaryQiangzhi;
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = Start(registry, seed, general);
            if (!AdvanceToOwnPrompt(game, skill, out var prompt)) continue;
            var hand = game.CreateSnapshot(0, true).Players[0].Hand;
            if (requirePair && !hand.Where(card => EquipmentCatalog.IsEquipment(card.Kind))
                    .GroupBy(card => EquipmentCatalog.Get(card.Kind).Slot).Any(group => group.Count() >= 2)) continue;
            Require(Answer(game, prompt.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate").Id).Accepted,
                "The equipment reveal must activate.");
            var target = game.PendingDecision!.Choices.FirstOrDefault(choice => choice.Targets.Count == 1 &&
                game.CreateSnapshot(0, true).Players[choice.Targets[0]].Hand.Any(card => EquipmentCatalog.IsEquipment(card.Kind)));
            if (target is null) continue;
            Require(Answer(game, target.Id).Accepted, "The equipment holder must be selectable.");
            if (general == BoundaryGeneral)
            {
                var equipmentIds = game.CreateSnapshot(0, true).Players[target.Targets[0]].Hand
                    .Where(card => EquipmentCatalog.IsEquipment(card.Kind)).Select(card => card.Id).ToHashSet();
                Require(Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Any(equipmentIds.Contains)).Id).Accepted,
                    "Boundary Qiangzhi must select the exact equipment card.");
            }
            if (!AdvanceToPlayDecision(game) || !BooleanState(game, 0, skill, "shown-equipment")) continue;
            if (!requirePair && !game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash)) continue;
            return (game, registry);
        }
        throw new InvalidOperationException("No bounded equipment-reveal fixture was found.");
    }

    private static void CompleteGiftSelection(GameEngine game)
    {
        for (var i = 0; i < 2 && game.PendingDecision is { } prompt &&
             prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"); i++)
            Require(Answer(game, prompt.Choices.First(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "select-owned-cards").Id).Accepted,
                "Xiantu's exact gift selection must resume.");
    }

    private static string StateIdForCategory(SkillProgramCardCategory category) => category switch
    {
        SkillProgramCardCategory.Basic => "shown-basic",
        SkillProgramCardCategory.Trick => "shown-trick",
        _ => "shown-equipment"
    };

    private static string DescribePending(GameEngine game) => game.PendingDecision switch
    {
        null => "<null>",
        { Kind: var kind, PlayerSeat: var seat, SkillPrompt: { SkillId: var skillId } } decision =>
            $"{kind} seat={seat} skill={skillId} choices=[{string.Join(",", decision.Choices.Select(choice =>
                choice.Parameters.GetValueOrDefault("program-action") ?? "?"))}]",
        { Kind: var kind, PlayerSeat: var seat } => $"{kind} seat={seat}"
    };

    private static string DescribeHpLost(ProgramSkillHpLostEvent[] events) =>
        string.Join(",", events.Select(item => $"seat{item.TargetSeat}=-{item.Amount}rem{item.RemainingHp}"));

    private static SkillProgramCardCategory IndexToCategory(int index) =>
        (SkillProgramCardCategory)index;

    private static IEnumerable<string> CategoryIds() =>
        ["shown-basic", "shown-trick", "shown-equipment"];

    private static SkillProgramCardCategory CategoryOf(CardKind kind) =>
        EquipmentCatalog.IsEquipment(kind)
            ? SkillProgramCardCategory.Equipment
            : CardCatalog.Get(kind).CategoryName switch
            {
                "基本牌" => SkillProgramCardCategory.Basic,
                "锦囊牌" => SkillProgramCardCategory.Trick,
                _ => throw new InvalidOperationException($"Card kind '{kind}' has no supported category.")
            };

    private static bool BooleanState(GameEngine game, int seat, string skillId, string stateId)
    {
        var states = game.CreateSnapshot(0, true).Players[seat].SkillRuntimeStates;
        return states is not null && states.Where(state => state.SkillId == skillId)
            .SelectMany(state => state.BooleanStates ?? [])
            .Any(state => state.StateId == stateId && state.Value);
    }

    private static Dictionary<int, CardKind> HandAndEquipmentKinds(GameEngine game, int seat) =>
        game.CreateSnapshot(0, true).Players[seat].Hand
            .Concat(game.CreateSnapshot(0, true).Players[seat].Equipment)
            .ToDictionary(card => card.Id, card => card.Kind);

    /// <summary>Advances until one of Zhang Song's own play-phase prompts for
    /// <paramref name="skillId"/> is pending, skipping the reveal offer when a
    /// later skill is requested; returns false when the play decision arrives.</summary>
    private static bool AdvanceToOwnPrompt(GameEngine game, string skillId, [NotNullWhen(true)] out PendingDecision? prompt)
    {
        prompt = null;
        for (var step = 0; step < 400; step++)
        {
            var pending = game.PendingDecision;
            if (pending is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger &&
                trigger.SkillPrompt?.SkillId is not null &&
                IsZhangSongSkill(trigger.SkillPrompt.SkillId) &&
                trigger.SkillPrompt.SkillId == skillId)
            {
                prompt = pending;
                return true;
            }
            if (pending is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return false;
            if (pending is null)
            {
                if (game.State.Status == EngineStatus.Completed ||
                    !game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            if (!AutoAnswer(game, pending)) return false;
        }
        return false;
    }

    /// <summary>Advances through Zhang Song's own turn (declining his own triggers)
    /// until a Xiantu prompt is pending during another player's play phase.</summary>
    private static bool RunToXiantuPrompt(GameEngine game, string skillId, [NotNullWhen(true)] out PendingDecision? prompt)
    {
        prompt = null;
        for (var step = 0; step < 600; step++)
        {
            var pending = game.PendingDecision;
            if (pending is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger &&
                trigger.SkillPrompt?.SkillId == skillId)
            {
                prompt = pending;
                return true;
            }
            if (pending is null)
            {
                if (game.State.Status == EngineStatus.Completed ||
                    !game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            if (!AutoAnswer(game, pending)) return false;
        }
        return false;
    }

    /// <summary>Drives the engine until a program prompt for <paramref name="skillId"/>
    /// is pending, answering intervening follow-up choices; null when it never appears.</summary>
    private static PendingDecision? DriveToSkillPrompt(GameEngine game, string skillId)
    {
        for (var step = 0; step < 60; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger &&
                trigger.SkillPrompt?.SkillId == skillId)
                return trigger;
            var pending = game.PendingDecision;
            if (pending is null)
            {
                if (game.State.Status == EngineStatus.Completed ||
                    !game.Submit(new AdvanceCommand(game.Revision)).Accepted) return null;
                continue;
            }
            if (!AutoAnswer(game, pending)) return null;
        }
        return null;
    }

    private static bool IsZhangSongSkill(string skillId) =>
        skillId is ClassicQiangzhi or BoundaryQiangzhi or ClassicXiantu or BoundaryXiantu;

    /// <summary>Drives the engine (AdvanceCommand while idle) until Zhang Song's
    /// play decision is pending; every human answer can leave the engine idle.</summary>
    private static bool AdvanceToPlayDecision(GameEngine game)
    {
        for (var step = 0; step < 200; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return true;
            var pending = game.PendingDecision;
            if (pending is null)
            {
                if (game.State.Status == EngineStatus.Completed ||
                    !game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            if (!AutoAnswer(game, pending)) return false;
        }
        return false;
    }

    /// <summary>Answers any prompt that is not the one the caller waits for:
    /// Zhang Song's own program triggers are skipped, his play phase is ended,
    /// discards are paid and other prompts take their first choice.</summary>
    private static bool AutoAnswer(GameEngine game, PendingDecision pending)
    {
        if (pending is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger &&
            trigger.SkillPrompt?.SkillId is not null && IsZhangSongSkill(trigger.SkillPrompt.SkillId))
        {
            var choice = trigger.Choices.FirstOrDefault(choice =>
                choice.Parameters.GetValueOrDefault("program-action") == "skip") ?? trigger.Choices.First();
            return Answer(game, choice.Id).Accepted;
        }
        GameCommand command = pending.Kind switch
        {
            DecisionKind.PlayCard when pending.PlayerSeat == 0 =>
                new EndPlayPhaseCommand(0, game.Revision, pending.PromptId),
            DecisionKind.DiscardCards => new DiscardCardsCommand(pending.PlayerSeat,
                pending.ValidCardIds.Take(pending.RequiredCardCount).ToArray(),
                pending.PromptId, game.Revision),
            _ => new AnswerPromptCommand(pending.PlayerSeat, pending.PromptId,
                pending.Choices.First().Id, game.Revision)
        };
        return game.Submit(command).Accepted;
    }

    private static bool RunToEndOfTurn(GameEngine game)
    {
        var startTurn = game.CreateSnapshot(0, true).TurnNumber;
        for (var step = 0; step < 200; step++)
        {
            if (game.CreateSnapshot(0, true).TurnNumber > startTurn) return true;
            var prompt = game.PendingDecision;
            if (prompt is null)
            {
                if (game.State.Status == EngineStatus.Completed ||
                    !game.Submit(new AdvanceCommand(game.Revision)).Accepted) return false;
                continue;
            }
            if (prompt is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } trigger &&
                trigger.SkillPrompt?.SkillId is not null && IsZhangSongSkill(trigger.SkillPrompt.SkillId))
            {
                var skip = trigger.Choices.FirstOrDefault(choice =>
                    choice.Parameters.GetValueOrDefault("program-action") == "skip");
                if (skip is null || !Answer(game, skip.Id).Accepted) return false;
                continue;
            }
            GameCommand command = prompt.Kind switch
            {
                DecisionKind.PlayCard when prompt.PlayerSeat == 0 =>
                    new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId),
                DecisionKind.DiscardCards => new DiscardCardsCommand(prompt.PlayerSeat,
                    prompt.ValidCardIds.Take(prompt.RequiredCardCount).ToArray(),
                    prompt.PromptId, game.Revision),
                _ => new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                    prompt.Choices.First().Id, game.Revision)
            };
            if (!game.Submit(command).Accepted) return false;
        }
        return false;
    }

    private static bool Play(GameEngine game, LegalAction action)
    {
        if (game.PendingDecision is not { Kind: DecisionKind.PlayCard } prompt) return false;
        return game.Submit(new PlayCardCommand(0, action.CardId!.Value,
            action.TargetSeats, game.Revision, prompt.PromptId, action.PlayedCardKind)).Accepted;
    }

    private static CommandResult Answer(GameEngine game, ChoiceId choiceId)
    {
        var prompt = game.PendingDecision!;
        return game.Submit(new AnswerPromptCommand(0, prompt.PromptId, choiceId, game.Revision));
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Scenario());

    private static GameEngine Start(ContentRegistry registry, int seed, string generalId)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Scenario.ModeId, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Zhang Song fixture did not start.");
        var setup = game.PendingDecision!;
        Require(setup.ValidContentIds.Contains(generalId) &&
                game.Submit(new SelectGeneralCommand(0, generalId, game.Revision, setup.PromptId)).Accepted,
            $"{generalId} was not selectable in the fixture.");
        return game;
    }

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));
    private static string State(GameEngine game) => SnapshotJson.Serialize(game.CreateSnapshot(0, true));
    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}").ToArray();
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class Scenario : IGameContentPackage
    {
        internal const string ModeId = "identity:zhang-song-check-5";
        public PackageManifest Manifest { get; } = new("zhang-song-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 145, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var ids = Enumerable.Range(1, 4).Select(i => $"fixture:zhang-song-target-{i}").ToArray();
            foreach (var id in ids)
                builder.AddGeneral(new ContentGeneralDefinition(id, "测试目标", "supporter",
                    "standard:none", "qun", BaseHp: 5));
            // Every category appears often, no alcohol exists, and the 5-HP
            // targets keep every fixture alive through the Xiantu penalties.
            var cardIds = new[]
            {
                "standard:slash", "standard:dodge", "standard:draw_two", "standard:slash",
                "standard:dodge", "standard:crossbow", "standard:draw_two", "standard:snatch",
                "standard:dismantlement", "standard:peach", "standard:offensive_horse", "standard:dodge"
            };
            builder.AddDeck(new ContentDeckRecipe("fixture:zhang-song-deck", "张松测试牌堆", 5, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(index =>
                    new ContentDeckPhysicalCard(cardIds[index % cardIds.Length],
                        (Suit)(index % 4), index % 13 + 1)).ToArray()
            });
            var roles = new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
            };
            builder.AddMode(new ContentModeDefinition(ModeId, "张松测试", 5, 5, roles,
                "fixture:zhang-song-deck", GeneralCandidateCount: 6,
                GeneralPoolIds: [ClassicGeneral, BoundaryGeneral, .. ids]));
        }
    }
}
