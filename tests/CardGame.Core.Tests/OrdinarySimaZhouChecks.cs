using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OrdinarySimaZhouChecks
{
    private const string Caiwang = "ol:caiwang", Najiang = "ol:najiang", Driver = "fixture:sima-zhou-driver";
    private const string Response = "fixture:sima-zhou-response", Gain = "fixture:sima-zhou-gain";
    private const string Suppress = "fixture:sima-zhou-suppress", Mode = "identity:classic-sima-zhou-focused";
    private enum Scenario { Duplicate, Different, Colorless, Reverse, Duel, Nullification, Judgment, Equipment, Paid, Lease, Ai }

    public static void SameColorPairDiscardsOneRealHandCardAndDeduplicatesGrants()
    {
        var (g, registry) = Create(Scenario.Duplicate);
        var sources = ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(g)!)[0].SkillGrants.Grants.Where(grant => grant.IsEnabled && grant.SkillId == Caiwang).ToArray();
        Require(sources.Length == 2 && sources.Select(s => s.GrantId).Distinct().Count() == 2 && sources.Select(s => s.SkillInstanceId).Distinct().Count() == 2 &&
            sources.Any(s => s.SourceId == CharacterState.PrimarySkillSource) && sources.Any(s => s.SourceId == "acquired:fixture:sima-zhou-acquire"),
            "The legal native setup owns two genuinely distinct printed/acquired Caiwang grants and instances, even though adding a second source does not emit a new effective-skill acquisition. " + Diagnostic(g));
        BeginOwnSlash(g); Reach(g, p => Offer(p));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Slash);
        var action = use.Action!; var response = E<CardActionAcceptedEvent>(g).Single(e => e.Action.Type == CardActionType.Response).Action;
        var pair = E<PairedColorResponseLinkedEvent>(g).Single(e => e.ResponseActionId == response.ActionId);
        Require(pair is { ResponseSeat: 1, PairedSeat: 0, ResponseIsRed: false, PairedIsRed: false } &&
            pair.PairedActionId == action.ActionId && pair.RootCardUseFrameId == use.Id && pair.RootActionId == action.ActionId &&
            response is { EffectiveKind: CardKind.Dodge, ProviderSeat: 1 } && response.ParentActionId == action.ActionId &&
            response.PhysicalCards is [{ From: var from }] && from == CardLocation.Hand(1),
            "A genuine native Dodge Use answers exactly the physical Slash Use; the accepted pair freezes both effective black colors and native identities.");
        Answer(g, Activate); Reach(g, Disposition); var frame = PairFrame(g); var receipt = frame.PairedColorDisposition!;
        Require(receipt is { Obtain: false, CounterpartSeat: 1, Stage: PairedColorDispositionStage.ChoosingCard } && receipt.Pair == pair &&
            receipt.Source.SkillId == Caiwang && receipt.Source.OwnerSeat == 0 && receipt.GameplayHash == registry.GetSkill(Caiwang).Program!.GameplayHash,
            "The optional observer owns the exact issued pair and selects a real counterpart Hand card for discard.");
        Blind(g); g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        Play(g); Once(g, frame.Id, false);
        var paid = E<PairedColorDispositionPaidEvent>(g).Single(e => e.FrameId == frame.Id);
        Require(paid.From == CardLocation.Hand(1) && paid.To == CardLocation.DiscardPile && !paid.IsGeneralWeapon &&
            g.CardMovements.Count(m => m.CardId == paid.CardId && m.From == paid.From && m.To == paid.To &&
                m.Sequence > paid.SequenceBefore && m.Sequence <= paid.SequenceAfter && m.Reason.Value == "skill-program.paired-color-response.discard") == 1 &&
            E<PairedColorDispositionStartedEvent>(g).Count(e => e.ResponseActionId == response.ActionId && e.Source.OwnerSeat == 0) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1,
            "Duplicate same-skill grants cannot duplicate this response opportunity, its real one-card payment or the original native Slash return.");
        VerifyOpaqueHandAiIgnoresHiddenFaces();
    }

    public static void DifferentAndColorlessPairsNeverOfferDisposition()
    {
        foreach (var scenario in new[] { Scenario.Different, Scenario.Colorless })
        {
            var (g, registry) = Create(scenario); BeginOwnSlash(g); Play(g);
            var pair = E<PairedColorResponseLinkedEvent>(g).Single();
            Require(scenario == Scenario.Different ? pair.PairedIsRed == true && pair.ResponseIsRed == false :
                    pair.PairedIsRed is null && pair.ResponseIsRed is null,
                "The fixed real payment produces precisely the intended different-color or two-colorless native pair.");
            Require(E<CardResponseCompletedEvent>(g).Length == 1 && E<PairedColorDispositionStartedEvent>(g).Length == 0 &&
                E<PairedColorDispositionPaidEvent>(g).Length == 0 && E<CardUseFinishedEvent>(g).Length == 1,
                "A completed genuine response with different effective colors, or no colored match, cannot offer or pay Caiwang.");
            _ = Cold(g, registry);
        }
    }

    public static void ReverseDodgeAndDuelPairsRetainTheRealUsedCard()
    {
        var (g, registry) = Create(Scenario.Reverse); Use(g, "request", [1]);
        Reach(g, p => p.PlayerSeat == 1 && Has(p, "request-slash"));
        var issued = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash").Cards.Single();
        Accept(g, new AdvanceOneStepCommand(g.Revision));
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } && p.Choices.Any(CaiwangDodge));
        Require(V(g, 0).HandCount == 1, "The reverse direction has one genuine Hand entity, so the full formal last-Hand Dodge route is eligible.");
        var material = P(g)!.Choices.First(CaiwangDodge).Cards.Single(); Private(g); g = Cold(g, registry); Answer(g, CaiwangDodge);
        Reach(g, Offer); Answer(g, Activate); Reach(g, Disposition);
        var frame = PairFrame(g); var pair = frame.PairedColorDisposition!.Pair;
        var incoming = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Use).Action;
        var response = E<CardActionAcceptedEvent>(g).Single(e => e.Action.ActorSeat == 0 && e.Action.Type == CardActionType.Response).Action;
        Require(pair is { ResponseSeat: 0, PairedSeat: 1, ResponseIsRed: false, PairedIsRed: false } &&
            pair.PairedActionId == incoming.ActionId && pair.ResponseActionId == response.ActionId &&
            incoming.PhysicalCards.Single().CardId == issued && response.PhysicalCards.Single().CardId == material &&
            response.ConversionChain.Single().SkillId == Caiwang && frame.PairedColorDisposition.CounterpartSeat == 1,
            "The responding skill owner also receives exactly one opportunity, tied to its actual formal last-Hand Dodge and the other actor's original Slash Use.");
        Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); Play(g); Once(g, frame.Id, false);
        Require(g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == pair.RootCardUseFrameId) == 1,
            "The true converted Dodge pays one original entity and resumes the forced physical Slash once.");

        (g, registry) = Create(Scenario.Duel); Use(g, "hurt-peer", [1]); Play(g); Convert(g, "duel", [1]);
        Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondSlash, IncomingCard: CardKind.Duel });
        var duel = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Duel); var duelAction = duel.Action!;
        Require(duelAction.EffectiveIsRed == false && V(g, 0).Skills!.Any(s => s.Id == "fixture:sima-zhou-red"),
            "The native committed child changes the owner to red only after the original black Duel appearance was captured.");
        var commands = g.AcceptedCommands.Count; Accept(g, new AdvanceOneStepCommand(g.Revision));
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash, IncomingCard: CardKind.Duel });
        Require(g.AcceptedCommands.Skip(commands).All(c => c is AdvanceOneStepCommand), "The other actor's genuine Duel response is chosen by native AI commands only.");
        Require(E<PairedColorResponseLinkedEvent>(g).Length == 1,
            "The native completed pure Duel response must retain exactly one original-Use pair. " + Diagnostic(g));
        var first = E<PairedColorResponseLinkedEvent>(g).Single();
        Require(first is { PairedIsRed: false, ResponseIsRed: true, PairedKind: CardKind.Duel } && first.PairedActionId == duelAction.ActionId,
            "The first red pure Slash response is compared to the original black Duel Use, so it offers no same-color opportunity.");
        Private(g); g = Cold(g, registry); Answer(g, c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "slash");
        Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondSlash, IncomingCard: CardKind.Duel });
        var pairs = E<PairedColorResponseLinkedEvent>(g);
        Require(pairs.Length == 2 && pairs.All(p => p.RootCardUseFrameId == duel.Id && p.PairedActionId == duelAction.ActionId && p.PairedKind == CardKind.Duel && p.PairedIsRed == false) &&
            pairs[1] is { ResponseSeat: 0, PairedSeat: 0, ResponseIsRed: true } &&
            pairs[1].PairedActionId != pairs[0].ResponseActionId && E<PairedColorDispositionStartedEvent>(g).Length == 0,
            "The owner's later red pure Slash still pairs to its own original black Duel, never to the preceding red Respond Slash and never to another actor's fictitious Use.");
        Play(g); Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == duel.Id) == 1, "The exact original Duel returns once after the real response cursor continues.");

        (g, registry) = Create(Scenario.Nullification); Convert(g, "duel", [1]);
        Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.Nullification });
        var trick = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Duel);
        Require(trick.Action!.EffectiveIsRed == false && V(g, 0).Skills!.Any(s => s.Id == "fixture:sima-zhou-red"),
            "The real original black trick was captured before the committed child made the later owner's Nullification red.");
        Accept(g, new AdvanceOneStepCommand(g.Revision));
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.Nullification } && p.Choices.Any(c => c.Cards.Count == 1));
        var firstCounter = E<CardActionAcceptedEvent>(g).Single(e => e.Action.EffectiveKind == CardKind.Nullification).Action;
        var firstLink = E<PairedColorResponseLinkedEvent>(g).Single();
        Require(firstCounter.ActorSeat == 1 && firstCounter.EffectiveIsRed == true && firstLink.PairedActionId == trick.Action.ActionId &&
            firstLink is { PairedIsRed: false, ResponseIsRed: true } && E<PairedColorDispositionStartedEvent>(g).Length == 0,
            "The native AI's first red counterspell answers the original black trick and does not offer a false same-color disposition. " + Diagnostic(g));
        g = Cold(g, registry); Answer(g, c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") == "nullification");
        Reach(g, Offer); Answer(g, Activate); Reach(g, Disposition); var counterFrame = PairFrame(g); var secondLink = counterFrame.PairedColorDisposition!.Pair;
        var secondCounter = E<CardActionAcceptedEvent>(g).Single(e => e.Action.EffectiveKind == CardKind.Nullification && e.Action.ActorSeat == 0).Action;
        Require(secondLink is { ResponseSeat: 0, PairedSeat: 1, ResponseIsRed: true, PairedIsRed: true, PairedKind: CardKind.Nullification } &&
            secondLink.ResponseActionId == secondCounter.ActionId && secondLink.PairedActionId == firstCounter.ActionId &&
            secondLink.PairedActionId != trick.Action.ActionId && secondLink.RootActionId == trick.Action.ActionId &&
            secondLink.RootCardUseFrameId == trick.Id && secondCounter.ParentActionId == trick.Action.ActionId &&
            E<CardResponseCompletedEvent>(g).Any(e => e.ActionId == firstCounter.ActionId && e.OriginalContinuation == ProgramCardContinuation.NullificationResponse),
            "A second red Nullification is itself a real Use directly answering the first red Nullification; its native chain stays rooted at the black trick without misusing that root as the color pair.");
        Blind(g); g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        Play(g); Once(g, counterFrame.Id, false);
        Require(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == trick.Id) == 1 &&
            E<PairedColorDispositionStartedEvent>(g).Count(e => e.ResponseActionId == secondCounter.ActionId) == 1,
            "The accepted direct-counterspell opportunity and exact original trick both finish once after the native remaining counterspell chain drains.");
    }

    public static void CounterpartEquipmentIsLegalButJudgmentAloneIsNot()
    {
        var (g, registry) = Create(Scenario.Judgment);
        var delay = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.SequenceEqual([1]));
        SubmitPlay(g, delay); Play(g); Require(V(g, 1).Judgment.Count == 1 && V(g, 1).HandCount == 1, "A real physical delayed trick occupies the counterpart's Judgment while its only Hand response remains available.");
        BeginOwnSlash(g); Play(g);
        Require(V(g, 1).HandCount == 0 && V(g, 1).Equipment.Count == 0 && V(g, 1).Judgment.Count == 1 &&
            E<PairedColorResponseLinkedEvent>(g).Single() is { ResponseIsRed: false, PairedIsRed: false } &&
            E<PairedColorDispositionStartedEvent>(g).Length == 0 &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == delay.CardId).Location == CardLocation.Judgment(1),
            "A genuine matching pair cannot offer Caiwang when the counterpart only has Judgment; its last-Judgment conversion route does not expand disposition areas.");
        _ = Cold(g, registry);

        (g, registry) = Create(Scenario.Equipment); var equipped = V(g, 0).Hand[0].Id;
        Use(g, "equip-peer", [1], [equipped]); Play(g); BeginOwnSlash(g); Reach(g, Offer); Answer(g, Activate); Reach(g, Disposition);
        var frame = PairFrame(g); var exact = P(g)!.Choices.Single(c => c.Cards.SequenceEqual([equipped]));
        Require(exact.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Equipment) && V(g, 1).Equipment.Any(c => c.Id == equipped),
            "The same native picker publishes the exact visible counterpart Equipment entity alongside opaque Hand slots.");
        Private(g); g = Cold(g, registry); Answer(g, c => c.Id == exact.Id); Play(g); Once(g, frame.Id, false);
        Require(E<PairedColorDispositionPaidEvent>(g).Single(e => e.FrameId == frame.Id) is { From: var from, To: var to, CardId: var id } &&
            from == CardLocation.Equipment(1) && to == CardLocation.DiscardPile && id == equipped,
            "Caiwang actually removes the selected real Equipment entity once through the native movement return.");
    }

    public static void PaidAcquisitionSurvivesSourceLossAndNativeChildColdReturn()
    {
        var (g, registry) = Create(Scenario.Paid); Use(g, "lose-hp"); Play(g); Use(g, "damage", [1]); Play(g);
        Require(V(g, 0).Hp == 4 && E<DamageCounterpartAcquisitionGrantedEvent>(g).Single().CounterpartSeat == 1 && V(g, 0).Skills!.Any(s => s.Id == Caiwang),
            "The real acquired Caiwang source is qualified after actual HP loss, and actual positive damage records the exact counterpart.");
        BeginOwnSlash(g); Reach(g, Offer); Answer(g, Activate); Reach(g, Disposition); var frame = PairFrame(g); var rootId = frame.Id;
        Require(frame.PairedColorDisposition is { Obtain: true, CounterpartSeat: 1 }, "Only the recorded opponent changes this issued disposition into acquisition.");
        Blind(g); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand));
        Reach(g, p => p.SkillPrompt?.SkillId == Gain && IsContinue(p));
        var paid = E<PairedColorDispositionPaidEvent>(g).Single(e => e.FrameId == rootId);
        var owning = PairFrame(g); var receipt = owning.PairedColorDisposition!;
        Require(receipt is { Stage: PairedColorDispositionStage.MovementChildren, BatchId: > 0 } &&
            receipt.PaidCardId == paid.CardId && receipt.BatchId == paid.BatchId && paid.From == CardLocation.Hand(1) && paid.To == CardLocation.Hand(0) &&
            owning.PendingMovementContinuation?.SubjectSeat == 1 &&
            g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.Id == paid.BatchId && w.Batch.ParentFrameId == rootId && w.Batch.AwaitingProgramFrameId == rootId) &&
            E<PairedColorDispositionCompletedEvent>(g).Length == 0,
            "The original paired-color frame owns the exact already-paid Hand acquisition and its native recipient gain child before completion.");
        Private(g); g = Cold(g, registry); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Suppress && IsContinue(p));
        Require(E<SkillsAcquiredEvent>(g).Any(e => e.PlayerSeat == 0 && e.SourceSkillId == Gain && e.SkillIds.Contains(Suppress)) &&
            V(g, 0).Skills!.All(s => s.Id != Caiwang) && PairFrame(g).Id == rootId && PairFrame(g).PairedColorDisposition!.PaidCardId == paid.CardId &&
            g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Window == SkillProgramTriggerWindow.SkillsChanged),
            "A real nested grant suppresses the acquired source, and the paid invoice remains on its original frame through the exact SkillsChanged child.");
        Private(g); g = Cold(g, registry); Continue(g); Play(g); Once(g, rootId, true);
        Require(E<PairedColorDispositionPaidEvent>(g).Count(e => e.FrameId == rootId) == 1 &&
            g.CardMovements.Count(m => m.CardId == paid.CardId && m.From == paid.From && m.To == paid.To) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == receipt.Pair.RootCardUseFrameId) == 1,
            "Paid source loss neither repeats acquisition nor loses the native response/Slash return after cold restoration.");
    }

    public static void DamageCounterpartsPersistAcrossSourceRegrantAndExpireAtOwnerTurnStart()
    {
        var (g, registry) = Create(Scenario.Lease); Use(g, "lose-hp"); Play(g); Use(g, "self-damage"); Play(g);
        Require(E<ProgramSkillHpLostEvent>(g).Length > 0 && E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 0) &&
            E<DamageCounterpartAcquisitionGrantedEvent>(g).Length == 0,
            "Native HP loss and true self-damage cannot create a counterpart direction.");
        Use(g, "damage", [1]); Play(g); Use(g, "opponent-duel", [1]);
        Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash, IncomingCard: CardKind.Duel });
        Answer(g, c => c.Cards.Count == 0); Play(g);
        var leases = E<DamageCounterpartAcquisitionGrantedEvent>(g);
        Require(leases.Length == 2 && leases.All(e => e.Source.OwnerSeat == 0 && e.Source.SkillId == Najiang && e.CounterpartSeat == 1 &&
                e.TargetSkillId == Caiwang && e.StateId == "caiwang" && e.Amount == 1 && e.ActualTurnOwnerSeat == 0) &&
            leases.Select(e => (e.DamageSourceSeat, e.DamageTargetSeat)).ToHashSet().SetEquals([(0, 1), (1, 0)]) &&
            leases.Select(e => e.DamageFrameId).Distinct().Count() == 2,
            "Actual damage dealt to and received from the same other actor independently records its two exact native damage identities, not points or HP-loss events.");
        Use(g, "remove"); Play(g); Use(g, "regain"); Play(g);
        Require(E<ProgramOwnerSkillsReplacedEvent>(g).Any(e => e.SkillId == Driver && e.LostSkillIds.Contains(Caiwang) && e.LostSkillIds.Contains(Najiang)) &&
            V(g, 0).Skills!.Any(s => s.Id == Caiwang) && V(g, 0).Skills!.All(s => s.Id != Najiang),
            "A legal final activation physically retires both original skills; a genuine grant returns only Caiwang before any response.");
        BeginOwnSlash(g); Reach(g, Offer); Answer(g, Activate); Reach(g, Disposition); var first = PairFrame(g);
        Require(first.PairedColorDisposition is { Obtain: true, CounterpartSeat: 1 }, "The existing exact-opponent direction survives physical loss and regrant without requiring the original Najiang instance.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); Play(g); Once(g, first.Id, true);
        var turn = g.CreateSnapshot(0).TurnNumber; Use(g, "extra"); Play(g); End(g);
        Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && g.CreateSnapshot(0).TurnNumber > turn);
        Require(E<TurnStartedEvent>(g).Last().ActorSeat == 0 && E<DamageCounterpartAcquisitionGrantedEvent>(g).Length == 2,
            "The issued native extra turn reaches the owner's next actual TurnStarted without refreshing the removed damage-recording source.");
        BeginOwnSlash(g); Reach(g, Offer); Answer(g, Activate); Reach(g, Disposition); var next = PairFrame(g);
        Require(next.PairedColorDisposition is { Obtain: false, CounterpartSeat: 1 }, "Exactly the next actual owner turn expires the former acquisition direction; the same opponent now incurs ordinary discard.");
        Private(g); g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)); Play(g); Once(g, next.Id, false);
    }

    private static void BeginOwnSlash(GameEngine g)
    {
        Convert(g, "slash", [1]); Reach(g, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        var count = g.AcceptedCommands.Count; Accept(g, new AdvanceOneStepCommand(g.Revision));
        Require(g.AcceptedCommands.Skip(count).All(c => c is AdvanceOneStepCommand), "The real NPC defense is selected only through the native Advance command.");
    }
    private static void VerifyOpaqueHandAiIgnoresHiddenFaces()
    {
        var (first, _) = Create(Scenario.Ai); var material = V(first, 0).Hand[0].Id;
        var hidden = V(first, 0).Hand.Skip(1).Select(c => c.Id).ToHashSet();
        var (second, _) = Create(Scenario.Ai, hidden);
        Require(V(first, 0).Hand.Select(c => c.Id).SequenceEqual(V(second, 0).Hand.Select(c => c.Id)) &&
            V(first, 0).Hand.Where(c => hidden.Contains(c.Id)).All(c => c.Kind == CardKind.Slash) &&
            V(second, 0).Hand.Where(c => hidden.Contains(c.Id)).All(c => c.Kind == CardKind.Crossbow) &&
            V(first, 1).Hand.SequenceEqual(V(second, 1).Hand),
            "Two independently created legal fixed-seed decks change only the test author's own unplayed Hand faces; the NPC's own entities, colors and all public Hand counts stay identical.");
        var selected = new List<int>();
        foreach (var game in new[] { first, second })
        {
            SubmitPlay(game, game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.ConversionSource.BindingId == "slash" && a.CardId == material && a.TargetSeats.SequenceEqual([1])));
            Reach(game, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
            var commandStart = game.AcceptedCommands.Count; Accept(game, new AdvanceOneStepCommand(game.Revision));
            Reach(game, p => p.PlayerSeat == 1 && p.SkillPrompt?.SkillId == Caiwang && p.Choices.Any(Activate));
            Require(game.CreateSnapshot(1).Players[0].Hand.Count == 0 && game.CreateSnapshot(1).Players[0].HandCount == hidden.Count,
                "The actual AI actor receives the same public counterpart count and no hidden Hand faces at the optional opportunity.");
            Accept(game, new AdvanceOneStepCommand(game.Revision)); Reach(game, p => p.PlayerSeat == 1 && Disposition(p));
            Require(P(game)!.Choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)),
                "The actual AI's legal payment menu contains only opaque Hand slots.");
            Accept(game, new AdvanceOneStepCommand(game.Revision)); Play(game);
            var paid = E<PairedColorDispositionPaidEvent>(game).Single(); selected.Add(paid.CardId);
            Require(game.AcceptedCommands.Skip(commandStart).All(c => c is AdvanceOneStepCommand) &&
                E<PairedColorDispositionStartedEvent>(game).Single().Source.OwnerSeat == 1,
                "The actual NPC decides to invoke and selects its opaque payment using only native Advance commands.");
            Once(game, paid.FrameId, false);
        }
        Require(selected.Count == 2 && selected[0] == selected[1] && hidden.Contains(selected[0]),
            "Swapping only the hidden counterpart Hand faces cannot change the actual AI's decision to invoke or its chosen opaque slot.");
    }
    private static void Once(GameEngine g, long id, bool obtain) => Require(
        E<PairedColorDispositionStartedEvent>(g).Count(e => e.FrameId == id && e.Obtain == obtain) == 1 &&
        E<PairedColorDispositionPaidEvent>(g).Count(e => e.FrameId == id) == 1 &&
        E<PairedColorDispositionCompletedEvent>(g).Count(e => e.FrameId == id && e.Obtain == obtain && e.Paid) == 1 &&
        E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == id && e.Completed) == 1 && !g.ResolutionStack.Any(f => f.Id == id),
        "The exact paired-color owning binding pays and returns once, with its accepted branch preserved.");
    private static bool Activate(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "activate";
    private static bool Offer(PendingDecision p) => p.PlayerSeat == 0 && p.SkillPrompt?.SkillId == Caiwang && p.Choices.Any(Activate);
    private static bool Has(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Disposition(PendingDecision p) => Has(p, "paired-color-disposition");
    private static bool CaiwangDodge(PromptChoice c) => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("conversion-skill-id") == Caiwang &&
        c.Parameters.GetValueOrDefault("conversion-binding-id") == "last-hand-dodge";
    private static bool IsContinue(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static ProgramSkillFrame PairFrame(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.PairedColorDisposition is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(r.Accepted, r.Error?.Message ?? "A real Sima Zhou fixture command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Use(GameEngine g, string id, int[]? targets = null, int[]? cards = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Convert(GameEngine g, string id, int[] targets) => SubmitPlay(g, g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver && a.ConversionSource.BindingId == id && a.TargetSeats.SequenceEqual(targets)));
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource });
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var i = 0; i < 140; i++) { var p = P(g); if (p is not null && stop(p)) return;
            Require(g.CreateSnapshot(0).Status != EngineStatus.Completed, "The fixed Sima Zhou boundary was not reached before completion: " + Diagnostic(g)); Step(g); }
        throw new InvalidOperationException("The fixed Sima Zhou boundary was not reached: " + Diagnostic(g));
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p is not null && IsContinue(p)) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification }) Answer(g, c => c.Cards.Count == 0);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Frozen<T>(IReadOnlyList<T> list) => Require(list is System.Collections.IList { IsReadOnly: true }, "Every exposed nested prompt collection is read-only.");
    private static void Private(GameEngine g)
    {
        var p = P(g)!; Require(p.IsPrivate, "The current actual native chooser receives a private decision.");
        foreach (var viewer in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat)) Require(g.CreateSnapshot(viewer).PendingDecision is null && g.CreateSnapshot(viewer).Players[p.PlayerSeat].Hand.Count == 0,
            "Foreign prepared views expose neither the private prompt nor hidden Hand identities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats); Frozen(p.ValidContentIds);
        foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); Frozen(c.ContentIds); Require(c.Parameters is System.Collections.IDictionary { IsReadOnly: true }, "Nested choice parameters are frozen."); }
        var before = State(g); Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before,
            "An unpublished choice cannot pay a card, alter a recorded pair or advance a native parent.");
        Require(!g.Submit(new AnswerPromptCommand((p.PlayerSeat + 1) % 4, p.PromptId, p.Choices[0].Id, g.Revision)).Accepted && State(g) == before,
            "Another actor cannot answer the owner's actual published private choice or change its native invoice.");
    }
    private static void Blind(GameEngine g)
    {
        Private(g); var p = P(g)!; var hand = p.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand)).ToArray();
        Require(hand.Length > 0 && hand.All(c => c.Cards.Count == 0 && c.Parameters.ContainsKey("slot-index") && !c.Parameters.ContainsKey("card-id")) &&
            g.CreateSnapshot(p.PlayerSeat).Players[PairFrame(g).PairedColorDisposition!.CounterpartSeat].Hand.Count == 0,
            "Counterpart Hand is represented only by opaque native slots, never private entity IDs or card appearance.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new {
        g.CreateSnapshot(0).Status, g.CreateSnapshot(0).TurnNumber, g.CreateSnapshot(0).CurrentSeat, g.CreateSnapshot(0).Phase,
        Players = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).GeneralId, V(g, s).Hp, V(g, s).MaxHp, V(g, s).HandCount, V(g, s).IsAlive,
            Skills = V(g, s).Skills?.Select(skill => skill.Id).ToArray() }).ToArray(),
        Prompt = P(g) is { } p ? new { p.Kind, p.PlayerSeat, p.IncomingCard, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(4).Select(c => c.Parameters).ToArray() } : null,
        Frames = g.ResolutionStack.Select(f => new { f.Id, Type = f.GetType().Name }).ToArray(),
        CompletionStarted = E<CardResponseCompletionStartedEvent>(g).Length, CompletionFinished = E<CardResponseCompletedEvent>(g).Length,
        Uses = g.ResolutionStack.OfType<CardUseFrame>().Select(u => new { u.Id, u.CardKind, ActionId = u.Action?.ActionId }).ToArray(),
        Actions = E<CardActionAcceptedEvent>(g).TakeLast(4).Select(e => new { e.Action.ActionId, e.Action.ParentActionId, e.Action.ActorSeat, e.Action.ProviderSeat,
            e.Action.Type, e.Action.EffectiveKind, e.Action.EffectiveSuit, e.Action.EffectiveIsRed }).ToArray(),
        Pairs = E<PairedColorResponseLinkedEvent>(g).TakeLast(4).ToArray(),
        Paid = E<PairedColorDispositionPaidEvent>(g).TakeLast(2).ToArray(), Completed = E<PairedColorDispositionCompletedEvent>(g).TakeLast(2).ToArray() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry) { var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all four prepared views, frozen response colors, original native parents, paid invoices and stable counterpart directions."); return copy; }
    private static (GameEngine, ContentRegistry) Create(Scenario scenario, IReadOnlySet<int>? hiddenSwapIds = null)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario, hiddenSwapIds));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, "fixture:sima-zhou-owner", g.Revision, P(g)!.PromptId)); Play(g);
        Require(V(g, 1).GeneralId == "fixture:sima-zhou-peer-1" && V(g, 0).Hp == 5 && V(g, 0).MaxHp == 5,
            "Native fixed selection weights preserve peer1 and actual ordinary Jin4 plus Lord1 HP. " + JsonSerializer.Serialize(new { Scenario = scenario.ToString(), Players = g.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Role, p.Hp, p.MaxHp }).ToArray() }));
        return (g, registry);
    }
    private sealed class Fixture(Scenario scenario, IReadOnlySet<int>? hiddenSwapIds) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:ordinary-sima-zhou", "1.0.0", "完整才望纳降原生边界");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rr = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-sima-zhou.rules.json")!);
            using var pp = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-sima-zhou.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rr.ReadToEnd(), pp.ReadToEnd());
            foreach (var (id, p) in formal.Programs) b.AddSkill(new(id, formal.Presentations[id].Name, formal.Presentations[id].Description) { Program = p, ProgramPresentation = formal.Presentations[id] });
            var rules = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:sima-zhou-driver","revision":1,"viewAs":[
                {"id":"slash","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"slash","forPlay":true,"forResponse":false,"allowSameKind":true},
                {"id":"duel","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],
                "modifiers":[{"id":"real-slash-budget","query":"slashLimit","operation":"add","value":10,"priority":0},{"id":"real-hand-limit","query":"handLimit","operation":"add","value":20,"priority":0}],
                "activations":[
                  {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWhoseAttackRangeIncludesOwner","usesPerTurn":null,"effects":[{"op":"requestSlashByTarget","target":"selectedTarget","resultBind":"answer"}]},
                  {"id":"equip-peer","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"equipment"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"equipment"}]},
                  {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                  {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                  {"id":"lose-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                  {"id":"hurt-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]},
                  {"id":"opponent-duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]},
                  {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:caiwang","ol:najiang"],"sourceBind":"fixture:sima-zhou-inert"}]},
                  {"id":"regain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:caiwang"]}]},
                  {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
              {"id":"fixture:sima-zhou-response","revision":1,"viewAs":[{"id":"native-dodge","inputKinds":[],"inputSuits":[],"sourceZones":["hand"],"outputKind":"dodge","forPlay":false,"forResponse":true,"allowSameKind":true}]},
              {"id":"fixture:sima-zhou-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":6,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:sima-zhou-peer-initial","revision":1,"modifiers":[{"id":"initial","query":"initialHandSize","operation":"add","value":6,"priority":0,"condition":{"kind":"always"}}]},
              {"id":"fixture:sima-zhou-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"fixture:sima-zhou-red","revision":1,"cardPolicies":[{"id":"effective-red","kind":"rewriteSuit","inputSuit":"spade","outputSuit":"heart"}]},
              {"id":"fixture:sima-zhou-gain","revision":1,"triggers":[{"id":"paid-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.paired-color-response.obtain"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]},{"op":"grantSkills","target":"owner","skillIds":["fixture:sima-zhou-suppress"]}]}]},
              {"id":"fixture:sima-zhou-suppress","revision":1,"triggers":[{"id":"paid-qualification-child","window":"skillsChanged","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"condition":{"kind":"compare","left":{"kind":"currentHp"},"operator":"equal","right":{"kind":"integerConstant","value":4}},"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:sima-zhou-acquire","revision":1,"triggers":[{"id":"true-source","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"grantSkills","target":"owner","skillIds":["ol:caiwang"]}]}]},
              {"id":"fixture:sima-zhou-committed","revision":1,"triggers":[{"id":"after-black-duel","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["duel"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"grantSkills","target":"owner","skillIds":["fixture:sima-zhou-red"]}]}]},
              {"id":"fixture:sima-zhou-inert","revision":1,"triggers":[{"id":"inert","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"draw","target":"owner","amount":1}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion; var skills = rules["skills"]!.AsArray();
            JsonNode Skill(string id) => skills.Single(n => n!["id"]!.GetValue<string>() == id)!;
            if (scenario == Scenario.Reverse) Skill("fixture:sima-zhou-initial")["modifiers"]![0]!["value"] = 1;
            if (scenario == Scenario.Judgment) Skill("fixture:sima-zhou-peer-initial")["modifiers"]![0]!["value"] = 1;
            var labels = skills.ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var p = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实固定小实体原生行为驱动" };
                if (id is Gain or Suppress) p["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" }; return (object)p;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "原生边界") { Program = p, ProgramPresentation = catalog.Presentations[id],
                SuppressionRule = id == Suppress ? new(4) : null });
            b.AddSkill(new("fixture:sima-zhou-first", "唯一首个其他角色", "真实选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:sima-zhou-peer", "其余角色", "真实选将权重") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            var owner = new List<string> { "fixture:sima-zhou-initial" };
            if (scenario is Scenario.Paid or Scenario.Lease) { owner.Add("fixture:sima-zhou-acquire"); owner.Add(Najiang); }
            else if (scenario != Scenario.Ai) owner.Add(Caiwang);
            if (scenario == Scenario.Duplicate) owner.Add("fixture:sima-zhou-acquire");
            if (scenario == Scenario.Different) owner.Add("fixture:sima-zhou-red");
            if (scenario is Scenario.Duel or Scenario.Nullification) owner.Add("fixture:sima-zhou-committed");
            if (scenario == Scenario.Paid) owner.Add(Gain);
            b.AddGeneral(new("fixture:sima-zhou-owner", "完整正式司马伷能力", "supporter", Driver, "jin", 4, owner, GeneralGender.Male));
            for (var seat = 1; seat < 4; seat++)
            {
                var peer = new List<string> { "fixture:sima-zhou-peer-initial", "fixture:sima-zhou-quiet", Response };
                if ((scenario is Scenario.Duel or Scenario.Nullification) && seat == 1) peer.Add("fixture:sima-zhou-red");
                if (scenario == Scenario.Nullification && seat > 1) peer.Remove("fixture:sima-zhou-peer-initial");
                if (scenario == Scenario.Ai && seat == 1) peer.Add(Caiwang);
                b.AddGeneral(new($"fixture:sima-zhou-peer-{seat}", "真实其他存活角色", "supporter", seat == 1 ? "fixture:sima-zhou-first" : "fixture:sima-zhou-peer", "qun", 4, peer));
            }
            var card = scenario switch { Scenario.Judgment => "standard:indulgence", Scenario.Equipment => "standard:crossbow", Scenario.Nullification => "standard:nullification", _ => "standard:slash" };
            b.AddDeck(new("fixture:sima-zhou-deck", "固定单种真实实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 40).Select(i =>
                new ContentDeckPhysicalCard(hiddenSwapIds?.Contains(i + 1) == true ? "standard:crossbow" : card, scenario == Scenario.Colorless ? Suit.None : Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "完整才望纳降共享机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:sima-zhou-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:sima-zhou-owner", "fixture:sima-zhou-peer-1", "fixture:sima-zhou-peer-2", "fixture:sima-zhou-peer-3"]));
        }
    }
}
