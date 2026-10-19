using System.Collections;
using System.Runtime.CompilerServices;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class LeCaoZhiChecks
{
    private const string Fule = "le:fule", Wenlan = "le:wenlan", Driver = "fixture:lcz-driver", Quiet = "fixture:lcz-quiet",
        Conditional = "fixture:lcz-conditional", Watch = "fixture:lcz-watch", Request = "fixture:lcz-request", Swap = "fixture:lcz-actor", Owner = "fixture:lcz-owner",
        Ally = "fixture:lcz-ally", EnemyA = "fixture:lcz-enemy-a", EnemyB = "fixture:lcz-enemy-b", Deck = "fixture:lcz-deck";
    private const string ObtainReason = "skill-program.le-cao-zhi.wenlan.obtain";
    private static ShanheStartingCard C(CardKind kind, int rank = 1) => new(kind, Suit.Spade, rank);
    private static bool Fu(CardSnapshot card) => card.SkillMarkLabel?.Contains("赋", StringComparison.Ordinal) == true;

    public static void OpeningFuEntitiesModeAliasesHandLimitAndPrivacyCold()
    {
        var s = Scene.Start(new("opening") { Hand = [C(CardKind.Crossbow), C(CardKind.Crossbow, 2), C(CardKind.Crossbow, 3), C(CardKind.Crossbow, 4)], Alias = CardKind.Slash });
        var initial = s.V().Hand.Select(c => c.Id).ToArray();
        s.Need(s.V() is { Hp: 3, MaxHp: 3 } && s.E<FuleInitializedEvent>().Single().EntityCount == 4 &&
            initial.All(id => s.Name(id) is { PrintedKind: CardKind.Crossbow, AliasKind: CardKind.Slash, Revision: > 0 }) && s.V().Hand.All(Fu),
            "The actual four opening entities receive one Fu and the only legal recipe-based alternate non-equipment name, with the original equipment name preserved."); s.Private(); s.Cold();
        var original = s.Name(initial[0]); s.Use("give", [1], [initial[0]]); s.Play();
        s.Need(s.V(1).Hand.Single().Id == original.CardId && Fu(s.V(1).Hand.Single()) && s.Name(original.CardId) == original,
            "A real transfer retains the exact entity's permanent mark and names rather than assigning the recipient a fresh alias.");
        s.Take(1, original.CardId); s.Need(s.Name(original.CardId) == original && s.E<FuEntityAssignedEvent>().Count(e => e.CardId == original.CardId) == 1,
            "Reobtaining the actual entity restores its own exemption without replaying random assignment.");
        s.Use("draw-four"); s.Play(); s.Need(s.V().Hand.Count(Fu) == 4 && s.V().Hand.Count(c => !Fu(c)) == 4,
            "Later real draws do not inherit opening marks or private aliases."); var turn = s.G.State.TurnNumber; s.End(); s.Reach(() => s.G.State.TurnNumber > turn && s.P is { Kind: DecisionKind.PlayCard });
        s.Need(initial.All(id => s.V().Hand.Any(c => c.Id == id && Fu(c))) && s.V().Hand.Count(c => !Fu(c)) == 3 &&
            !s.G.CardMovements.Any(m => m.TurnNumber == turn && initial.Contains(m.CardId) && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile),
            "The native discard phase exempts exactly the real Fu Hand entities and retires one ordinary over-limit card."); s.Cold();

        var lost = Scene.Start(new("lost-exemption") { Hand = [C(CardKind.Crossbow), C(CardKind.Crossbow), C(CardKind.Crossbow), C(CardKind.Crossbow)], Alias = CardKind.Slash, ConditionalSources = true });
        var lossIds = lost.V().Hand.Select(c => c.Id).ToArray(); lost.Use("lose-two"); lost.Play();
        lost.Need(!lost.Has(Fule) && lost.V().Hand.All(Fu) && !lost.G.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Fule),
            "Real source loss preserves the physical Fu labels and names but removes alternate-name Use rights.");
        var lossTurn = lost.G.State.TurnNumber; lost.End(); lost.Reach(() => lost.G.State.TurnNumber > lossTurn && lost.P is { Kind: DecisionKind.PlayCard });
        lost.Need(lost.V().Hand.Count == 1 && lost.V().Hand.All(Fu) && lost.G.CardMovements.Count(m => m.TurnNumber == lossTurn && lossIds.Contains(m.CardId) && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 3,
            "Without an active Fule source, the native discard phase no longer exempts those still-marked original Hand entities."); lost.Cold();
        ActivityPoolIsPureAndPreservesNextNativeDraft();
        ReservedFuleParserExceptionsRejectTransplantsAndMetadata();
    }

    private static void ReservedFuleParserExceptionsRejectTransplantsAndMetadata()
    {
        static string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.le-cao-zhi." + suffix + ".json")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        void Reject(Action<JsonNode, JsonNode, JsonNode> mutate, string label)
        {
            var rules = JsonNode.Parse(Read("rules"))!; var presentation = JsonNode.Parse(Read("presentation"))!;
            var fule = rules["skills"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == Fule)!; mutate(rules, presentation, fule);
            var rejected = false; try { _ = SkillProgramCatalog.Load(rules.ToJsonString(), presentation.ToJsonString()); } catch (InvalidOperationException) { rejected = true; }
            if (!rejected) throw new InvalidOperationException("The new reserved Fule parser exception accepted invalid " + label + ".");
        }
        Reject((_, presentation, fule) => { const string copy = "fixture:copied-fule"; fule["id"] = copy; var labels = presentation["skills"]!.AsObject(); var label = labels[Fule]!.DeepClone(); labels.Remove(Fule); labels[copy] = label; }, "transplanted source with its complete reserved bindings and operation");
        Reject((_, _, fule) => fule["viewAs"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "fu-alias-lightning")!["distanceUnlimited"] = true, "Lightning distance metadata");
        Reject((_, _, fule) => fule["viewAs"]!.AsArray().Single(n => n!["id"]!.GetValue<string>() == "fu-alias-lightning")!["damageBonus"] = 1, "Lightning damage metadata");
        Reject((_, _, fule) => { var views = fule["viewAs"]!.AsArray(); views.RemoveAt(views.Count - 1); }, "incomplete native name family");
    }

    private static void ActivityPoolIsPureAndPreservesNextNativeDraft()
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(includeWenheActivities: true);
        GameEngine New() => GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 8, HumanSeat = 0, HumanRole = null, ModeId = "activity:wenhe-2019",
            WenheAspirationRank = 4, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 4 }, registry);
        var queried = New(); var reference = New();
        var query = typeof(GameEngine).GetMethod("LeCaoZhiModeNamePool", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing real Fu activity pool query.");
        var randomField = typeof(GameEngine).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing native random diagnostics.");
        long RandomState(GameEngine game) { var random = randomField.GetValue(game)!; return Convert.ToInt64(random.GetType().GetProperty("State")!.GetValue(random), System.Globalization.CultureInfo.InvariantCulture); }
        var before = RandomState(queried); var revision = queried.Revision; var events = queried.Events.Count; var movement = queried.CardMovements.Count;
        var names = (CardKind[])query.Invoke(queried, null)!; var second = (CardKind[])query.Invoke(queried, null)!;
        if (!names.SequenceEqual(second) || names.Distinct().Count() != names.Length || names.Any(EquipmentCatalog.IsEquipment) ||
            !new[] { CardKind.BeamSwap, CardKind.HandRedistribution, CardKind.StarShift, CardKind.WenheChaos }.All(names.Contains) ||
            new[] { CardKind.Nullification, CardKind.Indulgence, CardKind.PeachGarden, CardKind.SupplyShortage }.Any(names.Contains) ||
            RandomState(queried) != before || queried.Revision != revision || queried.Events.Count != events || queried.CardMovements.Count != movement)
            throw new InvalidOperationException("A real Wenhe pool must project red/black Nullification independently, remove its delayed names, and remain a query without RNG, event or movement effects.");
        string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 8).Select(v => SnapshotJson.Serialize(game.CreateSnapshot(v))),
            Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Random = RandomState(game) });
        foreach (var game in new[] { queried, reference })
        {
            var result = game.Submit(new StartGameCommand()); if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message);
            for (var i = 0; game.CreateSnapshot(0).PendingDecision?.Kind != DecisionKind.SelectGeneral && i < 20; i++)
            { result = game.Submit(new AdvanceOneStepCommand(game.Revision)); if (!result.Accepted) throw new InvalidOperationException(result.Error?.Message); }
            if (game.CreateSnapshot(0).PendingDecision is not { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0, IsPrivate: true }) throw new InvalidOperationException("The existing small Wenhe setup did not reach its human native draft.");
        }
        if (State(queried) != State(reference)) throw new InvalidOperationException("Pure activity-name queries changed the next real same-seed private general draft or accepted command/event prefix.");
    }

    public static void AlternateNativeNamesRealMaterialsDelayedAndResponsesCold()
    {
        foreach (var kind in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash, CardKind.Peach, CardKind.Alcohol,
            CardKind.DrawTwo, CardKind.Duel, CardKind.Snatch, CardKind.Dismantlement, CardKind.FireAttack, CardKind.BorrowedSword,
            CardKind.BarbarianAssault, CardKind.ArrowBarrage, CardKind.PeachGarden, CardKind.FiveGrains, CardKind.IronChain,
            CardKind.Indulgence, CardKind.SupplyShortage, CardKind.Lightning })
        {
            var s = Scene.Start(new("alias-" + kind) { Hand = kind == CardKind.FireAttack ? [C(CardKind.Crossbow), C(CardKind.Slash)] : [C(CardKind.Crossbow)],
                Alias = kind, PeerHand = (kind is CardKind.Snatch or CardKind.Dismantlement or CardKind.FireAttack or CardKind.BorrowedSword) ? new[] { C(CardKind.Slash) } : Array.Empty<ShanheStartingCard>() });
            var id = s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
            s.Need(s.Name(id) is { PrintedKind: CardKind.Crossbow } name && name.AliasKind == kind,
                "The fixed mode recipe forces its sole non-equipment alias by real assignment, without seed search or alias injection.");
            if (kind is CardKind.Peach or CardKind.PeachGarden) { s.Use("wound"); s.Play(); }
            if (kind == CardKind.BorrowedSword) { s.Use("arm-peer", [1]); s.Play(); s.Need(s.V(1).Equipment.Any(c => c.Kind == CardKind.Crossbow), "A real deck-equipment Use arms the actual Borrowed Sword participant before its native forced Slash."); }
            s.AliasUse(id, kind); s.Play(); var declared = s.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 0 && e.CardId == id);
            s.Need(declared.CardKind == kind && s.G.CardMovements.Count(m => m.CardId == id && m.CardKind == CardKind.Crossbow && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == declared.ResolutionId && e.CardId == id && e.CardKind == kind) == 1,
                "The published Fule plan issues one native effective-name Use, pays the real Crossbow entity once and finishes that exact native Use once.");
            if (kind is CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning)
            {
                var target = kind == CardKind.Lightning ? 0 : 1;
                s.Need(s.V(target).Judgment.Any(c => c.Id == id) && s.Name(id).AliasKind == kind,
                    "A delayed alternate name persists on the same paid original Judgment entity, without creating a neutral clone.");
                s.Cold(); s.End();
                if (kind == CardKind.Lightning)
                {
                    s.Reach(() => s.E<LightningResolvedEvent>().Any(e => e.CardId == id));
                    s.Need(s.E<LightningResolvedEvent>().Single(e => e.CardId == id) is { Hit: false, NextTargetSeat: 1 } && s.V(1).Judgment.Any(c => c.Id == id),
                        "The real rank-one Spade Lightning misses and transfers the same positive physical Fu entity to the next Judgment zone.");
                }
                else
                {
                    s.Reach(() => s.E<DelayedCardResolvedEvent>().Any(e => e.CardId == id));
                    s.Need(s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Judgment(target) && m.To == CardLocation.DiscardPile) == 1,
                        "The original delayed native judgment retires its same physical material exactly once.");
                }
            }
            else s.Need(s.PaidOnce(id), "An ordinary alias Use retires its original physical material once through Processing.");
            if (kind == CardKind.FireAttack) s.Need(s.E<FireAttackResolvedEvent>().Single().CausedDamage &&
                s.G.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.FireAttackDiscard) == 1,
                "FireAttack pays another genuine matching-suit Hand entity rather than treating the original Crossbow as an extra discard."); s.Cold();
        }

        var dodge = Scene.Start(new("alias-dodge") { Hand = [C(CardKind.Crossbow)], Alias = CardKind.Dodge }); var dodgeId = dodge.V().Hand.Single().Id;
        dodge.IncomingSlash(); dodge.RespondAlias(dodgeId); dodge.Play();
        var accepted = dodge.E<CardActionAcceptedEvent>().Single(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Dodge).Action;
        var attack = dodge.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 1 && e.CardKind == CardKind.Slash);
        dodge.Need(accepted.PhysicalCards is [{ CardId: var paid, CardKind: CardKind.Crossbow }] && paid == dodgeId && dodge.PaidOnce(dodgeId) &&
            dodge.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == attack.ResolutionId) == 1,
            "A true own defensive Dodge uses the assigned name with one real physical cost and returns through its exact incoming Slash."); dodge.Cold();

        var counter = Scene.Start(new("alias-null") { Hand = [C(CardKind.Dismantlement), C(CardKind.Crossbow)], PeerHand = [C(CardKind.Slash)], Alias = CardKind.Nullification });
        var nullId = counter.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id; var trick = counter.V().Hand.Single(c => c.Kind == CardKind.Dismantlement).Id;
        counter.NativeOriginal(trick); counter.Reach(() => counter.P is { Kind: DecisionKind.Nullification }); counter.RespondAlias(nullId); counter.Play();
        counter.Need(counter.PaidOnce(nullId) && counter.PaidOnce(trick) && counter.V(1).HandCount == 1 &&
            counter.E<CardActionAcceptedEvent>().Count(e => e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Nullification && e.Action.PhysicalCards.Single().CardId == nullId) == 1,
            "An alternate Nullification is a true paid native counterspell; its originally declared parent is nullified and returns once."); counter.Cold();

        var pure = Scene.Start(new("alias-pure-played") { Hand = [C(CardKind.Crossbow), C(CardKind.Slash)], Alias = CardKind.Slash }); var forbidden = pure.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        var printedSlash = pure.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        pure.Use("duel", [1]); pure.Reach(() => pure.P is { Kind: DecisionKind.RespondSlash });
        pure.Need(!pure.P!.Choices.Any(c => c.Cards.Contains(forbidden)) && pure.V().Hand.Any(c => c.Id == forbidden) && pure.P.Choices.Any(c => c.Cards.SequenceEqual([printedSlash]) && !c.Parameters.ContainsKey("conversion-skill-id")),
            "A real printed Slash keeps the native Duel response observable, while the printed Crossbow's alias Slash remains unavailable for pure Played payment.");
        pure.Private(); pure.RejectPrompt(); pure.Cold(); pure.Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage"); pure.Play();
        pure.Need(!pure.G.CardMovements.Any(m => m.CardId == forbidden && m.From == CardLocation.Hand(0)), "A rejected alias Played never spends or moves its original entity."); pure.Cold();

        var request = Scene.Start(new("true-slash-request") { Alias = CardKind.Slash, RequestFromPeer = true }); var requestIds = request.V().Hand.Select(c => c.Id).ToArray();
        request.Use("give", [1], [requestIds[0]]); request.Reach(() => request.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "assisted-physical-slash" && c.Parameters.GetValueOrDefault("request-option") == "target") == true);
        request.Private(); request.Cold(); request.Answer(c => c.Parameters.GetValueOrDefault("request-option") == "target" && c.Targets.SequenceEqual([1]));
        request.Reach(() => request.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("conversion-skill-id") == Fule && c.Cards.SequenceEqual([requestIds[1]])) == true);
        request.RespondAlias(requestIds[1]); request.Play(); var requested = request.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 0 && e.CardId == requestIds[1] && e.CardKind == CardKind.Slash);
        request.Need(request.PaidOnce(requestIds[1]) && request.Entries() is [{ ActionType: CardActionType.Use, EffectiveKind: CardKind.Slash, Capture.MaterialCount: 1, Capture.FuEntity: not null }] &&
            request.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == requested.ResolutionId) == 1,
            "A genuine distinct requester's native Slash Use accepts the current Fu alias, pays its own actual entity and returns once; this is distinct from a prohibited pure Played Slash."); request.Cold();

        var forced = Scene.Start(new("borrowed-owner-fu-slash") { Alias = CardKind.Slash, Equipment = [C(CardKind.Crossbow)], IncomingBorrowed = true });
        var forcedId = forced.V().Hand.First(c => c.Kind == CardKind.Crossbow).Id; var weaponId = forced.V().Equipment.Single().Id;
        var borrowedId = forced.V(2).Hand.Single(c => c.Kind == CardKind.BorrowedSword).Id;
        forced.End(); forced.Reach(() => forced.P is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash"));
        var borrowed = forced.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.BorrowedSword);
        var forcedChoice = forced.P!.Choices.Single(c => c.Cards.SequenceEqual([forcedId]) && c.Parameters.GetValueOrDefault("response") == "borrowed-sword-slash" &&
            c.Parameters.GetValueOrDefault("conversion-skill-id") == Fule && c.Parameters.GetValueOrDefault("response-card-kind") == nameof(CardKind.Slash));
        forced.Need(borrowed.CardId == borrowedId && borrowed.SourceSeat == 2 && borrowed.TargetSeats is [0, var forcedTarget] &&
            forcedChoice.Targets.SequenceEqual([forcedTarget]) && !forced.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == borrowed.Id),
            "The exact native Borrowed Sword frontier publishes the weapon owner's current Fu Slash alias as a true forced Use.");
        forced.Private(); forced.RejectPrompt(); forced.Cold(); forced.Answer(c => c.Id == forcedChoice.Id);
        forced.Reach(() => forced.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == borrowed.Id));
        var forcedUse = forced.E<CardUseDeclaredEvent>().Single(e => e.SourceSeat == 0 && e.CardId == forcedId && e.CardKind == CardKind.Slash);
        forced.Need(forced.PaidOnce(forcedId) &&
            forced.G.CardMovements.Count(m => m.CardId == borrowedId && m.From == CardLocation.Hand(2) && m.To == CardLocation.Processing) == 1 &&
            forced.G.CardMovements.Count(m => m.CardId == borrowedId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 && forced.V().Equipment.Any(c => c.Id == weaponId) &&
            !forced.G.CardMovements.Any(m => m.CardId == weaponId && m.From == CardLocation.Equipment(0)) && forced.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == forcedUse.ResolutionId) == 1 &&
            forced.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == borrowed.Id) == 1 &&
            forced.E<BorrowedSwordResolvedEvent>().Single(e => e.ResolutionId == borrowed.Id) is { UsedSlash: true, WeaponOwnerSeat: 0, TransferredWeaponCardId: null } &&
            forced.Entries() is [{ ActionType: CardActionType.Use, EffectiveKind: CardKind.Slash, Capture.MaterialCount: 1, Capture.FuEntity.CardId: var forcedMaterial }] && forcedMaterial == forcedId,
            "The native Borrowed Sword Fu Slash frontier pays the actual marked Hand entity once, preserves the weapon and returns both real parent and child Uses once."); forced.Cold();

        var limited = Scene.Start(new("native-limits") { Alias = CardKind.Slash }); var limitedIds = limited.V().Hand.Select(c => c.Id).ToArray();
        limited.Need(!limited.G.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Fule && a.PlayedCardKind == CardKind.Slash && a.TargetSeats.Contains(2)),
            "An alternate Slash retains its real distance restriction before any payment."); limited.AliasUse(limitedIds[0], CardKind.Slash); limited.Play();
        limited.Need(!limited.G.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Fule && a.PlayedCardKind == CardKind.Slash) && limited.V().Hand.Any(c => c.Id == limitedIds[1]),
            "A second Fu entity cannot bypass the ordinary one-Slash quota by using its alternate name."); limited.Cold();
    }


    public static void FixedAcceptedPairsNestedPlayedCrossTurnAndSourceCold()
    {
        var nested = Scene.Start(new("nested-pairs") { Hand = [C(CardKind.Dismantlement), C(CardKind.Nullification), C(CardKind.Crossbow), C(CardKind.Crossbow)], PeerHand = [C(CardKind.Slash)], Alias = CardKind.Slash, GainChild = true });
        var trick = nested.V().Hand.Single(c => c.Kind == CardKind.Dismantlement).Id; var counter = nested.V().Hand.Single(c => c.Kind == CardKind.Nullification).Id;
        nested.NativeOriginal(trick); nested.Reach(() => nested.P is { Kind: DecisionKind.Nullification }); nested.RespondOriginal(counter); nested.Reach(nested.Hold);
        var entries = nested.Entries(); var firstPair = nested.E<WenlanStartedEvent>().Single();
        var original = nested.E<CardUseDeclaredEvent>().Single(e => e.CardId == trick);
        nested.Need(entries is [{ Ordinal: 1, EffectiveKind: CardKind.Dismantlement }, { Ordinal: 2, EffectiveKind: CardKind.Nullification }] &&
            firstPair.Qualification.FirstEntryId == entries[0].EntryId && firstPair.Qualification.SecondEntryId == entries[1].EntryId &&
            firstPair.Qualification.ActionId == entries[1].ActionId && firstPair.Qualification.Window == SkillProgramTriggerWindow.CardUseCompleted &&
            !nested.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == original.ResolutionId) && nested.G.ResolutionStack.OfType<CardUseFrame>().Any(f => f.Id == original.ResolutionId),
            "The nested second accepted counterspell owns fixed pair (1,2) when it completes, before the first accepted native parent finishes; completion order cannot reverse that pair.");
        nested.Private(); nested.Cold(); nested.Continue(); nested.Play();
        nested.Need(nested.E<WenlanCompletedEvent>().Single().SecondEntryId == entries[1].EntryId && nested.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == original.ResolutionId) == 1 &&
            nested.PaidOnce(trick) && nested.PaidOnce(counter), "The original parent returns once after the pair's real gain child without paying either material or pairing the first action again.");
        var next = nested.V().Hand.Where(c => c.Kind == CardKind.Crossbow).Take(2).Select(c => c.Id).ToArray(); nested.NativeOriginal(next[0]); nested.Play(); nested.NativeOriginal(next[1]); nested.Play();
        var all = nested.Entries(); var completed = nested.E<WenlanCompletedEvent>();
        nested.Need(all.Select(e => e.Ordinal).SequenceEqual([1, 2, 3, 4]) && completed.Length == 2 &&
            completed[0].FirstEntryId == all[0].EntryId && completed[0].SecondEntryId == all[1].EntryId &&
            completed[1].FirstEntryId == all[2].EntryId && completed[1].SecondEntryId == all[3].EntryId,
            "Subsequent real equipment Uses form only (3,4), never overlapping rolling (2,3) or replaying the nested pair."); nested.Cold();

        var cross = Scene.Start(new("cross-turn-source") { ConditionalSources = true, Alias = CardKind.Slash });
        var ids = cross.V().Hand.Select(c => c.Id).ToArray(); var frozen = cross.Name(ids[0]); cross.NativeOriginal(ids[0]); cross.Play(); var turn = cross.G.State.TurnNumber;
        cross.Need(cross.Entries() is [{ Ordinal: 1 }] && cross.E<WenlanStartedEvent>().Length == 0, "The unmatched first real Use waits without inventing a pair.");
        cross.End(); cross.Reach(() => cross.G.State.TurnNumber > turn && cross.P is { Kind: DecisionKind.PlayCard });
        cross.Use("lose-two"); cross.Play(); cross.Need(!cross.Has(Fule) && !cross.Has(Wenlan), "A genuine HP loss removes both conditional sources between accepted slots.");
        cross.Use("recover-two"); cross.Play(); cross.NativeOriginal(ids[1]); cross.Play();
        var retained = cross.Entries();
        cross.Need(retained.Length == 2 && retained[0].Capture.FuEntity == frozen && cross.E<WenlanCompletedEvent>().Single().FirstEntryId == retained[0].EntryId &&
            cross.E<WenlanCompletedEvent>().Single().SecondEntryId == retained[1].EntryId && cross.E<FuleInitializedEvent>().Length == 1,
            "A real turn transition and actual source loss/regrant preserve the unmatched accepted first Fu and its frozen name set without refreshing opening marks."); cross.Cold();

        var actor = Scene.Start(new("actual-actor-replacement") { Hand = [C(CardKind.Slash), C(CardKind.Crossbow)], Alias = CardKind.Duel, ActorReplacement = true });
        var provided = actor.V().Hand.Single(c => c.Kind == CardKind.Slash).Id; var untouched = actor.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id; var namesBefore = actor.Name(provided);
        actor.NativeOriginal(provided); actor.Reach(() => actor.P?.SkillPrompt?.SkillId == Swap && actor.P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        var oldEntry = actor.Entries().Single(); actor.Private(); actor.Cold(); actor.Answer(c => c.Targets.SequenceEqual([3])); actor.Play();
        var moved = actor.E<WenlanActionReassignedEvent>().Single(); var newEntry = actor.E<WenlanActionAcceptedEvent>().Single(e => e.OwnerSeat == 3);
        actor.Need(moved is { PreviousActorSeat: 0, ActorSeat: 3 } && moved.RetiredEntryId == oldEntry.EntryId && moved.NewEntryId == newEntry.EntryId &&
            newEntry is { Ordinal: 1, ProviderSeat: 0, ActionType: CardActionType.Use } && newEntry.ActionId == oldEntry.ActionId && newEntry.Capture.FuEntity == namesBefore &&
            newEntry.Capture.Source!.Source.OwnerSeat == 3 && actor.Name(provided) == namesBefore && actor.PaidOnce(provided) &&
            actor.E<ProgramCardUseActorReplacedEvent>().Count(e => e.CardUseFrameId == moved.CardUseFrameId && e.PreviousActorSeat == 0 && e.ActorSeat == 3 && e.ProviderSeat == 0) == 1 &&
            actor.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == moved.CardUseFrameId && e.CardId == provided) == 1,
            "One real native actor replacement retires the unused original owner credit and appends the same Action/material/name snapshot to the new sourced actor, retaining physical provider zero.");
        actor.NativeOriginal(untouched); actor.Play(); var activeOwner = actor.E<WenlanActionAcceptedEvent>().Where(e => e.OwnerSeat == 0 && e.EntryId != moved.RetiredEntryId).ToArray();
        actor.Need(activeOwner is [{ Ordinal: 1, EffectiveKind: CardKind.Crossbow }] && actor.E<WenlanStartedEvent>().Length == 0 &&
            actor.E<WenlanActionAcceptedEvent>().Count(e => e.EntryId == oldEntry.EntryId) == 1,
            "The next real original-owner Use begins its unpaired tail again without consuming the reassigned card or erasing its immutable accepted history."); actor.Cold();

        var played = Scene.Start(new("played-second") { Hand = [C(CardKind.Crossbow), C(CardKind.Slash)], Alias = CardKind.Duel });
        var equipment = played.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id; var slash = played.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        played.NativeOriginal(equipment); played.Play(); played.Use("duel", [1]); played.Reach(() => played.P is { Kind: DecisionKind.RespondSlash }); played.RespondOriginal(slash); played.Play();
        var playedEntries = played.Entries(); var playedPair = played.E<WenlanStartedEvent>().Single();
        played.Need(playedEntries is [{ Ordinal: 1, ActionType: CardActionType.Use }, { Ordinal: 2, ActionType: CardActionType.Response, EffectiveKind: CardKind.Slash }] &&
            playedEntries[1].Capture is { MaterialCount: 1, FuEntity.CardId: var originalSlash } && originalSlash == slash &&
            playedPair.Qualification.Window == SkillProgramTriggerWindow.CardResponseCompleted && playedPair.Qualification.SecondEntryId == playedEntries[1].EntryId &&
            played.PaidOnce(slash) && played.E<CardResponseCompletedEvent>().Count(e => e.ActionId == playedEntries[1].ActionId && e.OriginalContinuation == ProgramCardContinuation.DuelSlash) == 1 &&
            played.E<WenlanCompletedEvent>().Length == 1, "One actual pure Played printed Slash counts as Wenlan's second accepted card and uses its true response-completion parent once."); played.Cold();

        var direct = Scene.Start(new("direct-program-duel") { Hand = [C(CardKind.Crossbow), C(CardKind.Crossbow), C(CardKind.Crossbow)], Alias = CardKind.Slash });
        var directIds = direct.V().Hand.Select(c => c.Id).ToArray(); direct.NativeOriginal(directIds[0]); direct.Play(); direct.Use("legacy-duel", [0, 1], [directIds[1]]); direct.Reach(direct.HandChoice);
        var directEntry = direct.Entries().Last(); var native = direct.E<WenlanDirectDuelAcceptedEvent>().Single();
        direct.Need(directEntry is { Ordinal: 2, ActionId: 0, EffectiveKind: CardKind.Duel, Capture.MaterialCount: 0, Capture.FuEntity: null } &&
            directEntry.EntryId == native.EntryId && directEntry.NativeFrameId == native.ProducerFrameId && directEntry.ProgramInstructionIndex == native.InstructionIndex &&
            direct.E<WenlanDirectDuelReturnedEvent>().Single() is { DamageApplied: true, FinalTargetSeat: 1 } && direct.V(1).Hp == 11 &&
            direct.E<DuelResponseEvent>().Count(e => e.ResolutionId == native.ProducerFrameId && e.ResponderSeat == 1 && !e.UsedSlash) == 1 &&
            !direct.E<CardUseDeclaredEvent>().Any(e => e.CardKind == CardKind.Duel) && !direct.E<CardActionAcceptedEvent>().Any(e => e.Action.EffectiveKind == CardKind.Duel),
            "A real ProgramSkillFrame direct Duel occupies one native instruction slot and reaches Wenlan only after complete actual damage, without manufacturing a CardUse or accepted Action.");
        direct.AssertHandChoice(); direct.Confirm(); direct.Play();
        direct.Need(direct.E<WenlanDirectDuelResumedEvent>().Single() is { WenlanCompleted: true } && direct.E<WenlanCompletedEvent>().Single().SecondEntryId == native.EntryId &&
            direct.G.CardMovements.Count(m => m.CardId == directIds[1] && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.fixture:lcz-driver.DiscardSelected") == 1 &&
            direct.E<ProgramSkillStartedEvent>().Count(e => e.FrameId == native.ProducerFrameId && e.OwnerSeat == 0 && e.SkillId == Driver && e.ActivationId == "legacy-duel") == 1 &&
            direct.E<ProgramSkillResolvedEvent>().Count(e => e.FrameId == native.ProducerFrameId && e.OwnerSeat == 0 && e.SkillId == Driver && e.ActivationId == "legacy-duel" && e.Completed) == 1 &&
            !direct.G.ResolutionStack.Any(f => f.Id == native.ProducerFrameId),
            "Cold private selection returns once to the real direct-Duel producer, preserving its one already-paid skill discard."); direct.Cold();

        foreach (var multiple in new[] { false, true })
        {
            var material = Scene.Start(new(multiple ? "multi-material-slot" : "zero-material-slot") { Hand = multiple ? [C(CardKind.Crossbow), C(CardKind.Crossbow), C(CardKind.Crossbow), C(CardKind.Crossbow)] : [C(CardKind.Crossbow), C(CardKind.Crossbow)],
                Equipment = multiple ? [C(CardKind.ZhangbaSerpentSpear)] : [], Alias = CardKind.Slash, OpeningUse = !multiple });
            if (multiple)
            {
                var pair = material.V().Hand.Take(2).Select(c => c.Id).ToArray(); material.Accept(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [1], material.G.Revision, material.P!.PromptId)); material.Play();
                material.Need(pair.All(material.PaidOnce), "The native multi-material Slash pays its two true marked entities once.");
            }
            var final = material.V().Hand.First(c => c.Kind == CardKind.Crossbow).Id; material.NativeOriginal(final); material.Reach(material.HandChoice);
            var ledger = material.Entries();
            material.Need(ledger.Length == 2 && ledger[0].Capture.MaterialCount == (multiple ? 2 : 0) && ledger[0].Capture.FuEntity is null &&
                ledger[1].Capture is { MaterialCount: 1, FuEntity: not null } && !material.E<WenlanStartedEvent>().Single().CommonName,
                "A zero-material or multi-material true Use occupies one accepted action slot but is not one Fu even when all actual materials are marked.");
            material.Private(); material.Cold(); material.Confirm(); material.Play(); material.Need(material.E<WenlanCompletedEvent>().Single() is { CommonName: false, Count: 0 }, "The real non-Fu pair can return through its zero-selection alternative once."); material.Cold();
        }
    }

    public static void FullNameUnionActualObtainsShortageAndGainSourceCold()
    {
        foreach (var mode in new[] { "complete", "missing", "empty", "gain-source-loss" })
        {
            var s = Scene.Start(new("union-" + mode) { Hand = [C(CardKind.Crossbow), C(CardKind.Duel)],
                Stock = mode == "complete" || mode == "gain-source-loss" ? [.. Enumerable.Repeat(CardKind.Crossbow, 44), CardKind.Slash, CardKind.Slash, CardKind.Duel, CardKind.Duel] :
                    [.. Enumerable.Repeat(CardKind.Crossbow, 47), CardKind.Slash],
                ConditionalSources = mode == "gain-source-loss", GainChild = mode == "gain-source-loss" });
            var first = s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id; var second = s.V().Hand.Single(c => c.Kind == CardKind.Duel).Id;
            var names = s.Names(first).Concat(s.Names(second)).Distinct().Order().ToArray();
            s.Need(names.SequenceEqual(new[] { CardKind.Slash, CardKind.Duel, CardKind.Crossbow }.Order()) &&
                s.Names(first).Intersect(s.Names(second)).Any(), "The real random assignments always yield a common name and the full three-name union, not merely the used effective names.");
            if (mode == "empty") { s.Use("drain-zero"); s.Play(); var pool = (CardKind[])typeof(GameEngine).GetMethod("LeCaoZhiModeNamePool", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(s.G, null)!;
                s.Need(s.G.CreateSnapshot(0).DrawPileCount == 0 && pool.SequenceEqual([CardKind.Slash]), "The real bounded Draw20+20+8 exhausts the live deck while its immutable selected recipe still permits the exact alternate name."); }
            s.NativeOriginal(first); s.Play(); s.NativeOriginal(second);
            if (mode == "gain-source-loss")
            {
                s.Reach(s.Hold); var r = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.LeCaoZhiWenlan is not null).LeCaoZhiWenlan!;
                s.Need(r is { Stage: WenlanStage.ObtainChildren, ObtainedIds.Count: 3 } && r.ObtainedIds.All(id => s.V().Hand.Any(c => c.Id == id && Fu(c))) && s.E<WenlanCompletedEvent>().Length == 0,
                    "Every actual union obtain is already Fu, with a real alias assignment, before its genuine CardsGained child can remove the source."); s.Immutable(r.ObtainPlan); s.Immutable(r.ObtainedIds); s.Private(); s.Cold(); s.Continue();
            }
            s.Play(); var started = s.E<WenlanStartedEvent>().Single(); var plan = s.E<WenlanNamedObtainPlannedEvent>().Where(e => e.FrameId == started.FrameId).ToArray();
            var gained = s.E<WenlanEntityObtainedEvent>().Where(e => e.FrameId == started.FrameId).ToArray(); var count = mode == "empty" ? 0 : mode == "missing" ? 2 : 3;
            s.Need(started.CommonName && plan.Select(p => p.Kind).SequenceEqual(names) && plan.Select(p => p.Kind).Distinct().Count() == 3 &&
                gained.Length == count && s.Obtains().Length == count && gained.All(e => s.G.CardMovements.Count(m => m.Sequence == e.Sequence && m.CardId == e.CardId &&
                    m.CardKind == e.PrintedKind && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == ObtainReason) == 1) &&
                s.E<FuEntityAssignedEvent>().Where(e => e.Origin == FuMarkOrigin.WenlanObtain).Select(e => e.CardId).ToHashSet().SetEquals(gained.Select(e => e.CardId)) &&
                s.E<WenlanCompletedEvent>().Single() is { CommonName: true, Count: var actual } && actual == count,
                "The common branch searches every distinct complete name once, obtains only each available original deck entity, marks only actual gains and completes once.");
            if (mode == "missing") s.Need(plan.Single(p => p.Kind == CardKind.Duel).CardId is null, "An absent union name is skipped without substituting a different name.");
            if (mode == "empty") s.Need(plan.All(p => p.CardId is null) && s.G.CreateSnapshot(0).DrawPileCount == 0 &&
                !s.G.CardMovements.Any(m => m.From == CardLocation.DiscardPile && m.To == CardLocation.DrawPile), "Empty obtains neither fabricate cards nor reshuffle the two actually used Fu originals.");
            if (mode == "gain-source-loss")
            {
                s.Need(s.V().Hp == 1 && !s.Has(Fule) && !s.Has(Wenlan), "The real gain child removes both sources after its paid original obtains.");
                s.Use("recover-two"); s.Play(); s.Need(s.Has(Fule) && s.E<FuleInitializedEvent>().Length == 1 && s.E<WenlanCompletedEvent>().Length == 1 && s.Obtains().Length == 3,
                    "Real regrant neither reinitializes entities nor repeats a paid union obtain or its accepted pair.");
            }
            s.Cold();
        }
    }

    public static void PrivateHandMarkReplacementZeroSelectionAndReturnCold()
    {
        var s = Scene.Start(new("private-rewrite") { Hand = [C(CardKind.Crossbow)] }); var marked = s.V().Hand.Single().Id; var old = s.Name(marked);
        s.Use("draw-eight"); s.Play(); var ordinary = s.V().Hand.Where(c => !Fu(c) && c.Kind == CardKind.Crossbow).Take(4).Select(c => c.Id).ToArray();
        s.Need(ordinary.Length == 4, "The fixed recipe provides four real ordinary Crossbows after a bounded Draw8, independent of its shuffle.");
        s.NativeOriginal(ordinary[0]); s.Play(); s.NativeOriginal(ordinary[1]); s.Reach(s.HandChoice);
        var zero = s.P!; var before = s.G.CardMovements.Count; var count = s.E<FuEntityAssignedEvent>().Length; s.AssertHandChoice(); s.Confirm(); s.Play(); s.RejectOld(zero);
        s.Need(s.E<WenlanHandConfirmedEvent>().Single().Count == 0 && s.E<WenlanCompletedEvent>().Single() is { CommonName: false, Count: 0 } &&
            s.G.CardMovements.Count == before && s.E<FuEntityAssignedEvent>().Length == count && s.Name(marked) == old,
            "Confirming zero actual Hand selections returns without moving cards, consuming RNG aliases or inventing a mandatory change.");
        s.NativeOriginal(ordinary[2]); s.Play(); s.NativeOriginal(ordinary[3]); s.Reach(s.HandChoice);
        var newly = s.V().Hand.First(c => !Fu(c)).Id; var unselected = s.V().Hand.Where(c => c.Id != marked && c.Id != newly).Select(c => c.Id).ToArray();
        var previous = s.E<FuEntityAssignedEvent>().Length; var moves = s.G.CardMovements.Count; s.AssertHandChoice();
        s.Toggle(marked); s.Toggle(newly); s.Toggle(newly); s.Toggle(newly);
        var r = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.LeCaoZhiWenlan is not null).LeCaoZhiWenlan!;
        s.Need(r.SelectedIds.ToHashSet().SetEquals([marked, newly]) && s.Name(marked) == old && !Fu(s.V().Hand.Single(c => c.Id == newly)),
            "Private toggle selections remain on their owning frame and do not assign names before a real confirmation."); s.Immutable(r.SelectedIds); s.Private(); s.Cold();
        var prompt = s.P!; s.Confirm(); s.Play(); s.RejectOld(prompt);
        var rewrite = s.E<FuEntityAssignedEvent>().Skip(previous).ToArray();
        s.Need(rewrite.Length == 2 && rewrite.All(e => e.Origin == FuMarkOrigin.WenlanHandRewrite && e.Changed) && rewrite.Select(e => e.CardId).ToHashSet().SetEquals([marked, newly]) &&
            s.Name(marked).AliasKind != old.AliasKind && s.Name(marked).PrintedKind == old.PrintedKind && s.Name(marked).Revision > old.Revision &&
            s.Name(newly).AliasKind != s.Name(newly).PrintedKind && s.V().Hand.Count(Fu) == 2 && s.G.CardMovements.Count == moves &&
            !rewrite.Any(e => unselected.Contains(e.CardId)) && s.E<WenlanCompletedEvent>().Last() is { CommonName: false, Count: 2 },
            "One actual mixed confirmation marks the ordinary card, changes the old Fu alias by excluding its prior name, preserves printed entities and moves no Hand material."); s.Cold();

        var ox = Scene.Start(new("ox-handlike") { Hand = [C(CardKind.Crossbow)], Equipment = [C(CardKind.WoodenOx)] }); var grain = ox.V().Hand.Single().Id; var grainName = ox.Name(grain);
        ox.Accept(new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain], [], ox.G.Revision, ox.P!.PromptId)); ox.Play(); ox.Use("draw-eight"); ox.Play();
        var ordinaryGear = ox.V().Hand.Where(c => c.Kind == CardKind.Crossbow && !Fu(c)).Take(2).Select(c => c.Id).ToArray();
        ox.NativeOriginal(ordinaryGear[0]); ox.Play(); ox.NativeOriginal(ordinaryGear[1]); ox.Reach(ox.HandChoice); ox.AssertHandChoice();
        ox.Need(ox.P!.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "toggle" && c.Cards.SequenceEqual([grain])) && ox.V().WoodenOxGrain!.Single().Id == grain,
            "A currently usable real Wooden Ox grain is an actual private handlike Wenlan candidate, without broad deck authorization.");
        ox.Toggle(grain); ox.Cold(); ox.Confirm(); ox.Play(); var changed = ox.Name(grain);
        ox.Need(changed.PrintedKind == grainName.PrintedKind && changed.AliasKind is not null && changed.AliasKind != grainName.AliasKind && ox.V().WoodenOxGrain!.Single().Id == grain,
            "The real handlike selection replaces only the stored entity's alternate name and leaves the physical grain in its native pile.");
        ox.AliasUse(grain, changed.AliasKind!.Value); ox.Play();
        ox.Need((ox.V().WoodenOxGrain?.Count ?? 0) == 0 && ox.G.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.Processing) == 1 &&
            ox.G.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1 && ox.Entries().Last().Capture.FuEntity == changed,
            "A real Fule alias Use consumes the actual current Ox grain once, freezes that Fu's rewritten names and returns through the native Use."); ox.Cold();
    }

    private sealed record Spec(string Name)
    {
        public ShanheStartingCard[] Hand { get; init; } = [C(CardKind.Crossbow), C(CardKind.Crossbow)];
        public ShanheStartingCard[] Equipment { get; init; } = [];
        public ShanheStartingCard[] PeerHand { get; init; } = [];
        public CardKind? Alias { get; init; }
        public CardKind[]? Stock { get; init; }
        public bool ConditionalSources { get; init; }
        public bool GainChild { get; init; }
        public bool OpeningUse { get; init; }
        public bool RequestFromPeer { get; init; }
        public bool ActorReplacement { get; init; }
        public bool IncomingBorrowed { get; init; }
        public CardKind[] Recipe => Stock ?? (Alias is { } kind ? [.. Enumerable.Repeat(CardKind.Crossbow, 47), kind] :
            [.. Enumerable.Repeat(CardKind.Crossbow, 44), CardKind.Slash, CardKind.Slash, CardKind.Duel, CardKind.Duel]);
    }

    private sealed class Fixture(Spec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-le-cao-zhi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.le-cao-zhi." + suffix + ".json") ?? throw new InvalidOperationException("Missing complete real Le Cao Zhi bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var production = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var (id, program) in production.Programs) b.AddSkill(new(id, production.Presentations[id].Name, production.Presentations[id].Description) { Program = program, ProgramPresentation = production.Presentations[id] });
            foreach (var kind in spec.Recipe.Concat(spec.Hand.Select(c => c.Kind)).Concat(spec.Equipment.Select(c => c.Kind)).Concat(spec.PeerHand.Select(c => c.Kind)).Concat(spec.IncomingBorrowed ? [CardKind.BorrowedSword] : Array.Empty<CardKind>()).Distinct())
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:lcz-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static JsonObject A(string id, string effects, int targets = 0, int cards = 0, int? maximum = null) => new()
            { ["id"] = id, ["minCards"] = cards, ["maxCards"] = maximum ?? cards, ["minTargets"] = targets, ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            static string Draw(int n) => "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":" + n + "}";
            const string capture = "{\"op\":\"captureSelectedCards\",\"target\":\"owner\",\"resultBind\":\"paid\"}";
            var acts = new JsonArray(A("draw-four", Draw(4)), A("draw-eight", Draw(8)), A("drain-zero", Draw(20) + "," + Draw(20) + "," + Draw(8)),
                A("give", capture + ",{\"op\":\"moveBoundCards\",\"target\":\"owner\",\"sourceBind\":\"paid\",\"destination\":\"selectedTargetHand\"}", 1, 1, 4),
                A("take", "{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"ownerHand\",\"awaitMovementTriggers\":true}", 1),
                A("wound", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("lose-two", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}"),
                A("recover-two", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":2}"), A("incoming", "{\"op\":\"offerVirtualSlashOrDraw\",\"target\":\"selectedTarget\"}", 1));
            acts.Add(A("arm-peer", "{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"real-weapon\"}", 1));
            acts.Add(A("legacy-duel", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1},{\"op\":\"startVirtualDuel\",\"target\":\"owner\"}", 2, 1));
            var duel = A("duel", "{\"op\":\"useSelectedActorDuel\",\"target\":\"owner\"}", 1); duel["usesPerPhase"] = 2; duel["targetKind"] = "otherLiving"; acts.Add(duel);
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (spec.OpeningUse) defs[0]!["triggers"] = N("[{\"id\":\"real-zero-material-use\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"useVirtualOrdinaryTrick\",\"target\":\"owner\",\"actorRef\":{\"kind\":\"owner\"},\"targetRef\":{\"kind\":\"owner\"},\"outputKind\":\"arrowBarrage\"}]}]");
            if (spec.ConditionalSources) defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-fule\",\"skillId\":\"" + Fule + "\",\"minimumHp\":2},{\"id\":\"healthy-wenlan\",\"skillId\":\"" + Wenlan + "\",\"minimumHp\":2}]}"));
            if (spec.RequestFromPeer) defs.Add(N("{\"id\":\"" + Request + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-request-after-gift\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"skill-program.fixture:lcz-driver.MoveBoundCards\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"currentTurnPlayer\"},{\"op\":\"requestSlashAgainstChosenTarget\",\"target\":\"selectedTarget\",\"chooserRef\":{\"kind\":\"selectedTarget\"},\"resultBind\":\"real-use\"}]}]}"));
            if (spec.ActorReplacement) defs.Add(N("{\"id\":\"" + Swap + "\",\"revision\":1,\"triggers\":[{\"id\":\"actual-replacement\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":false,\"usageScope\":\"game\",\"usageLimit\":1,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"replaceCurrentCardUseActor\",\"target\":\"selectedTarget\"}]}]}"));
            if (spec.GainChild) defs.Add(N("{\"id\":\"" + Watch + "\",\"revision\":1,\"triggers\":[{\"id\":\"paid-union-gain\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + ObtainReason + "\"],\"movementOccurrence\":\"perBatch\",\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(), description = "乐曹植固定真实交互夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(binding => binding!["effects"]!.AsArray())
                    .SelectMany(effect => effect!["options"]?.AsArray() ?? new JsonArray()).Select(option => option!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => id) });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var own = new List<string> { Driver }; if (spec.ConditionalSources) own.Add(Conditional); else own.AddRange([Fule, Wenlan]); if (spec.GainChild) own.Add(Watch); if (spec.ActorReplacement) own.Add(Swap);
            b.AddGeneral(new(Owner, "乐曹植真实本人", "", "standard:none", "wei", 3, own));
            foreach (var peer in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(peer, "实际其他角色", "", "standard:none", "qun", 12, spec.IncomingBorrowed && peer == EnemyA ? Array.Empty<string>() : spec.RequestFromPeer && peer == Ally ? [Quiet, Request] : spec.ActorReplacement && peer == EnemyB ? [Quiet, Wenlan] : [Quiet]));
            b.AddDeck(new(Deck, "固定四十八张实体", 0, 0, []) { PhysicalCards = spec.Recipe.Select(kind => new ContentDeckPhysicalCard("fixture:lcz-card-" + kind, Suit.Spade, 1)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "乐曹植真实小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }

    private sealed class Scene(Spec spec, GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public WenlanActionAcceptedEvent[] Entries() => E<WenlanActionAcceptedEvent>().Where(e => e.OwnerSeat == 0).OrderBy(e => e.Ordinal).ToArray();
        public bool Has(string skill) => V().Skills?.Any(s => s.ContentId == skill) == true;
        public FuEntityNameSnapshot Name(int id) { var e = E<FuEntityAssignedEvent>().Last(e => e.CardId == id); return new(id, e.PrintedKind, e.AliasKind, e.Revision); }
        public CardKind[] Names(int id) { var n = Name(id); return n.AliasKind is { } alias ? new[] { n.PrintedKind, alias }.Distinct().ToArray() : [n.PrintedKind]; }
        public bool IsAction(PromptChoice c, string action) => c.Parameters.GetValueOrDefault("program-action") == action;
        public bool HandChoice() => P?.Choices.Any(c => IsAction(c, "le-cao-zhi-wenlan-hand")) == true;
        public bool Hold() => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public int[] Ordinary() => V().Hand.Where(c => !Fu(c)).Select(c => c.Id).ToArray();
        public CardMovementRecord[] Obtains() => G.CardMovements.Where(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason.Value == ObtainReason).ToArray();
        public bool PaidOnce(int id) => G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1;
        public void Use(string id, int[]? targets = null, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
        private LegalAction Plan(int id, CardKind? alias)
        {
            var plans = G.GetHumanLegalActions().Where(a => a.CardId == id && a.Kind != LegalActionKind.Recast &&
                (alias is null ? a.ConversionSource is null : a.ConversionSource?.SkillId == Fule && a.PlayedCardKind == alias)).ToArray();
            var action = plans.FirstOrDefault(a => a.TargetSeats.SequenceEqual([1])) ?? plans.FirstOrDefault(a => a.TargetSeats.Count == 0) ?? plans.FirstOrDefault();
            Need(action is not null, "The real published native name and target plan must exist for entity " + id + "/" + alias); return action!;
        }
        public void NativeOriginal(int id) => Native(Plan(id, null));
        public void AliasUse(int id, CardKind kind) { Private(); Cold(); var a = Plan(id, kind); Reject(new PlayCardCommand(0, id, a.TargetSeats, G.Revision - 1, P!.PromptId, kind) { ConversionSource = a.ConversionSource }); Native(a); }
        public void Native(LegalAction action) => Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
        public void Toggle(int id) => Answer(c => IsAction(c, "le-cao-zhi-wenlan-hand") && c.Parameters.GetValueOrDefault("verb") == "toggle" && c.Cards.SequenceEqual([id]));
        public void Confirm() => Answer(c => IsAction(c, "le-cao-zhi-wenlan-hand") && c.Parameters.GetValueOrDefault("verb") == "confirm");
        public void AssertHandChoice()
        {
            var frame = G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.LeCaoZhiWenlan is not null); var receipt = frame.LeCaoZhiWenlan!;
            Need(receipt is { Stage: WenlanStage.SelectingHand, CommonName: false } && receipt.HandIds.ToHashSet().SetEquals(V().Hand.Concat(V().WoodenOxGrain ?? []).Select(c => c.Id)) &&
                P!.Choices.Where(c => c.Parameters.GetValueOrDefault("verb") == "toggle").All(c => c.Cards.Count == 1 && receipt.HandIds.Contains(c.Cards[0]) && c.Targets.Count == 0) &&
                P.Choices.Count(c => c.Parameters.GetValueOrDefault("verb") == "confirm" && c.Cards.Count == 0 && c.Targets.Count == 0) == 1,
                "The real private Wenlan selection freezes only its current actual Hand and publishes one canonical toggle per entity plus a zero-card confirm.");
            Private(); Immutable(receipt.HandIds); Immutable(receipt.SelectedIds); Immutable(P!.Choices); foreach (var c in P.Choices) Immutable(c.Cards); RejectPrompt(); Cold();
        }
        public void IncomingSlash() { Use("incoming", [1]); Reach(() => P?.Choices.Any(c => IsAction(c, "virtual-slash-offer") && c.Targets.SequenceEqual([0])) == true); Answer(c => IsAction(c, "virtual-slash-offer") && c.Targets.SequenceEqual([0])); Reach(() => P is { Kind: DecisionKind.RespondDodge }); }
        public void RespondAlias(int id) { Private(); RejectPrompt(); Cold(); Answer(c => c.Cards.SequenceEqual([id]) && c.Parameters.GetValueOrDefault("conversion-skill-id") == Fule); }
        public void RespondOriginal(int id) { Private(); Cold(); Answer(c => c.Cards.SequenceEqual([id]) && !c.Parameters.ContainsKey("conversion-skill-id") && c.Parameters.GetValueOrDefault("response") is not ("pass" or "take-damage" or "let-die")); }
        public void Take(int seat, int id) { var index = V(seat).Hand.ToList().FindIndex(c => c.Id == id); Use("take", [seat]); Reach(() => P?.Choices.Any(c => c.Parameters.ContainsKey("slot-index")) == true); Private(); Cold(); Answer(c => c.Parameters.GetValueOrDefault("slot-index") == index.ToString(System.Globalization.CultureInfo.InvariantCulture)); Play(); }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "Choose an actually published owner option."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 220; step++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "The bounded fixture ended before boundary " + line); var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (HandChoice()) Confirm();
                else if (Hold()) Continue();
                else if (p.Choices.Any(c => IsAction(c, "activate"))) Answer(c => IsAction(c, "activate"));
                else if (p.Choices.Any(c => IsAction(c, "skip"))) Answer(c => IsAction(c, "skip"));
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (p.Kind == DecisionKind.SelectTargetCard) Answer(c => c.Parameters.ContainsKey("slot-index") || c.Cards.Count == 1);
                else if (p.Kind is DecisionKind.SelectHarvestCard or DecisionKind.FireAttackReveal or DecisionKind.FireAttackDiscard) Answer(c => c.Cards.Count == 1);
                else throw new InvalidOperationException("Unexpected boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Missing bounded boundary " + line + " " + Diagnostic());
        }
        public void Private() => Need(P is { PlayerSeat: 0, IsPrivate: true } && Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0 && (p.WoodenOxGrain?.Count ?? 0) == 0)), "Only the chooser sees private choices and every foreign Hand or Ox grain stays opaque.");
        public void Immutable<T>(IReadOnlyList<T> list) { if (list is IList mutable && mutable.Count > 0) { var before = JsonSerializer.Serialize(list); var rejected = false; try { mutable[0] = mutable[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected && JsonSerializer.Serialize(list) == before, "Prepared nested collections reject observer mutation."); } }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay preserves all private views, exact owning pair/name frames, native invoices, RNG and accepted commands."); }
        public void Reject(GameCommand command) { var before = State(); Need(!G.Submit(command).Accepted && State() == before, "A stale, forged or foreign command cannot alter real marks, pair slots, payments, RNG or history."); }
        public void RejectPrompt() { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-le-cao-zhi"), G.Revision)); }
        public void RejectOld(PendingDecision p) => Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision));
        public void Accept(GameCommand command) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, "Rejected " + command.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).MaxHp, V(i).Armor, V(i).IsAlive, V(i).HandCount }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Assigned = E<FuEntityAssignedEvent>().TakeLast(8),
            Accepted = E<CardActionAcceptedEvent>().TakeLast(4), Declared = E<CardUseDeclaredEvent>().TakeLast(4), Finished = E<CardUseFinishedEvent>().TakeLast(4),
            Wenlan = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("Wenlan", StringComparison.Ordinal)).TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            Bindings = E<ProgramBindingResolvedEvent>().Where(e => e.SkillId is Fule or Wenlan).TakeLast(5) });
        public static Scene Start(Spec spec)
        {
            ContentRegistry registry; try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec)); } catch (Exception e) { throw new InvalidOperationException(spec.Name + " fixture: " + e.Message, e); }
            var setup = new ShanheBattleSetup(91, 1, "lcz-" + spec.Name, Owner, 3, 0, 0, 0, 0, [], spec.Hand, spec.Equipment,
                [new(EnemyA) { StartingHand = spec.IncomingBorrowed ? [C(CardKind.BorrowedSword)] : [] }, new(EnemyB) { StartingHand = [] }], [new(Ally) { StartingHand = spec.PeerHand }], new string('f', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var scene = new Scene(spec, game, registry); scene.Accept(new StartGameCommand()); scene.Play(); return scene;
        }
    }
}
