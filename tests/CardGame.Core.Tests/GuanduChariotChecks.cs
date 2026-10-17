using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using static GuanduCardChecks;

internal static class GuanduChariotChecks
{
    private const string ModeId = "fixture:guandu-chariot", Driver = "fixture:guandu-chariot-driver",
        Watcher = "fixture:guandu-chariot-watcher", QuietSkill = "fixture:guandu-chariot-quiet",
        Wusheng = "sp:guan-yu-wusheng";

    public static void RedChariotWushengResponseKeepsSingleDestroyedInvoiceAndTypedCompletion()
    {
        var (g, r) = StartChariot(); var chariot = Chariot(g);
        Accept(g, new UseProgramSkillCommand(0, Driver, "jianji", [], [7], g.Revision, P(g)!.PromptId));
        Until(g, () => P(g)?.Kind == DecisionKind.RespondSlash);
        var duel = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Duel);
        Need(duel.SourceSeat == 7 && duel.TargetSeats.SequenceEqual([0]) && duel.CardId > 0,
            "The enemy really uses its exact drawn physical Duel against SP Guan Yu, rather than issuing a synthetic response request.");
        Need(P(g)!.Choices.Any(c => c.Cards.Contains(chariot) &&
            c.Parameters.GetValueOrDefault("conversion-skill-id") == Wusheng),
            "The real red equipped chariot is offered by SP Guan Yu's registered equipment-enabled Wusheng response.");
        Cold(g, r);
        Answer(g, c => c.Cards.Contains(chariot) && c.Parameters.GetValueOrDefault("conversion-skill-id") == Wusheng);
        Until(g, () => IsWatcher(g, "accepted"));
        var action = E<CardActionAcceptedEvent>(g).Single(e => e.Action.Type == CardActionType.Response &&
            e.Action.PhysicalCards.Any(c => c.CardId == chariot)).Action;
        var invoice = E<GuanduChariotResponsePaidEvent>(g).Single(e => e.ActionId == action.ActionId);
        Need(action.EffectiveKind == CardKind.Slash && action.ActorSeat == 0 && action.ProviderSeat == 0 &&
            action.PhysicalCards is [{ CardId: var id, CardKind: CardKind.ThunderChariot, From: var from }] && id == chariot && from == CardLocation.Equipment(0) &&
            action.ConversionChain.Single().SkillId == Wusheng && invoice.ParentFrameId == duel.Id &&
            invoice.Continuation == ProgramCardContinuation.DuelSlash && invoice.BatchId > 0,
            "The accepted native Slash response freezes the real equipment material, Wusheng source and exact Duel-parent destruction invoice.");
        DestroyedOnce(g, chariot, invoice.MovementSequence); Cold(g, r); Continue(g);
        Until(g, () => IsWatcher(g, "completed"));
        var completion = g.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.ResponseCompletion?.ActionId == action.ActionId);
        Need(completion.ResponseCompletion is { CostsDrained: true, OriginalContinuation: ProgramCardContinuation.DuelSlash } receipt &&
            receipt.ParentFrameId == duel.Id && receipt.NativeCosts is [var cost] && cost.Sequence == invoice.MovementSequence &&
            cost.From == CardLocation.Equipment(0) && cost.To == CardLocation.OutsideGame &&
            E<CardResponseCompletedEvent>(g).Count(e => e.ActionId == action.ActionId && e.ParentFrameId == duel.Id && e.EffectiveKind == CardKind.Slash) == 1,
            "The typed response completion drains the one actual destruction as its whole material payment before the original Duel cursor resumes.");
        Cold(g, r); Continue(g); Play(g);
        Need(E<DuelResponseEvent>(g).Count(e => e.ResolutionId == duel.Id && e.ResponderSeat == 0 && e.UsedSlash && e.SlashCardId == chariot) == 1 &&
            E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == duel.Id) == 1 &&
            E<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == Watcher && e.Activated && e.Completed) == 2,
            "Both real response child programs complete once and return to the original Duel without charging the destroyed entity again.");
        DestroyedOnce(g, chariot, invoice.MovementSequence); Cold(g, r);
    }

    public static void RedChariotWushengUseAndRealDiscardPhaseDestroyOnce()
    {
        {
            var (g, r) = StartChariot(); var chariot = Chariot(g);
            var targetHp = V(g, 7).Hp;
            Need(targetHp > V(g, 0).Hp, "The actual Underdog event fixture begins with the enemy above the SP Guan Yu commander's health.");
            var slash = g.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Slash && a.CardId == chariot &&
                a.TargetSeats.SequenceEqual([7]) && a.ConversionSource?.SkillId == Wusheng);
            Act(g, slash); Until(g, () => IsWatcher(g, "committed"));
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Slash);
            Need(use.Action!.PhysicalCards is [{ CardId: var id, CardKind: CardKind.ThunderChariot, From: var from }] &&
                id == chariot && from == CardLocation.Equipment(0),
                "A normal Wusheng Slash also retains its actual equipped red chariot as its native accepted material.");
            DestroyedOnce(g, chariot); Cold(g, r); Continue(g); Play(g);
            Need(E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == use.Id) == 1 &&
                E<DamageAppliedEvent>(g).Where(e => e.SourceSeat == 0 && e.TargetSeat == 7).ToArray() is [{ Amount: 2 }] &&
                V(g, 7).Hp == targetHp - 2,
                "A genuine equipment-to-Slash use finishes once and applies the actual Underdog bonus once after the paid chariot has been destroyed.");
            DestroyedOnce(g, chariot); Cold(g, r);
        }
        {
            var (g, r) = StartChariot(); var chariot = Chariot(g); var turn = g.State.TurnNumber;
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Until(g, () => P(g)?.SkillPrompt?.SkillId == Driver && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "discard"));
            Need(g.State.Phase == TurnPhase.Discard && g.CreateCardZoneDiagnostics().Single(c => c.CardId == chariot).Location == CardLocation.Equipment(0),
                "The real discard-phase instruction pauses before discarding its owned equipment, with the physical chariot still present.");
            Cold(g, r); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "discard");
            Until(g, () => E<TurnEndedEvent>(g).Any(e => e.TurnNumber == turn));
            var owned = E<ProgramOwnedZoneCardsDiscardedEvent>(g).Where(e => e.SkillId == Driver).ToArray();
            var invocations = E<NativeBuZhiDiscardInvocationRecordedEvent>(g).Where(e => e.Source.SkillId == Driver).ToArray();
            var hand = g.CardMovements.Where(move => move.From == CardLocation.Hand(0) && move.To == CardLocation.DiscardPile &&
                move.Reason == CardMoveReasons.HandLimitDiscard && move.TurnNumber == turn).ToArray();
            var diagnostic = $"owned=[{string.Join(',', owned.Select(e => e.CardCount))}], " +
                $"native=[{string.Join(',', invocations.Select(e => $"{e.ActualCount}/{e.ZoneMask}"))}], " +
                $"handMoves=[{string.Join(',', hand.Select(move => move.CardId))}], finalHand={V(g, 0).HandCount}, hp={V(g, 0).Hp}.";
            Need(owned is [{ CardCount: 1 }],
                "The actual owned-equipment discard instruction must report exactly one real discarded chariot. " + diagnostic);
            Need(invocations is [{ ActualCount: 1, ZoneMask: 2 }],
                "The active native aggregation producer must preserve one exact equipped-entity discard invoice. " + diagnostic);
            Need(hand is [{ CardKind: CardKind.Duel }] && V(g, 0).HandCount == V(g, 0).Hp,
                "The same genuine discard phase must also discard exactly one hand card to its actual hand limit. " + diagnostic);
            DestroyedOnce(g, chariot); Cold(g, r);
        }
    }

    private static bool IsWatcher(GameEngine g, string binding) => P(g)?.SkillPrompt?.SkillId == Watcher &&
        g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Watcher && f.TriggerId == binding);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static int Chariot(GameEngine g)
    {
        var made = E<GuanduChariotCreatedEvent>(g).Single(e => e.OwnerSeat == 0);
        Need(g.CreateCardZoneDiagnostics().Single(c => c.CardId == made.CardId) is { CardKind: CardKind.ThunderChariot, Location: var location } &&
            location == CardLocation.Equipment(0) && V(g, 0).Equipment.Single(c => c.Id == made.CardId).Suit == Suit.Diamond,
            "The native mandatory Polu trigger actually creates and equips the red Diamond-nine chariot before SP Guan Yu's first play phase.");
        return made.CardId;
    }
    private static void DestroyedOnce(GameEngine g, int card, int? expectedSequence = null)
    {
        var moves = g.CardMovements.Where(m => m.CardId == card && m.From == CardLocation.Equipment(0)).ToArray();
        Need(moves is [var destroyed] && destroyed.To == CardLocation.OutsideGame &&
            (expectedSequence is null || destroyed.Sequence == expectedSequence) &&
            g.CardMovements.All(m => m.CardId != card || m.To != CardLocation.Processing && m.To != CardLocation.DiscardPile) &&
            E<GuanduChariotDestroyedEvent>(g).Count(e => e.CardId == card && e.MovementSequence == destroyed.Sequence) == 1 &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.OutsideGame,
            "The physical chariot departs its equipment slot directly to Outside exactly once, with one matching destruction fact and no fabricated Processing or discard movement.");
    }

    private static (GameEngine, ContentRegistry) StartChariot()
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardGuanduActivityPackage(), new Fixture());
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:cao", HumanGuanduRole = GuanduSeatRole.Commander, ModeId = ModeId,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, r);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, P(g)!.Choices.First().ContentIds.Single(), g.Revision, P(g)!.PromptId)); Play(g);
        return (g, r);
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-guandu-chariot", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[{"id":"jianji","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":1,"effects":[{"op":"drawAndOfferJianjiExactCard","target":"selectedTarget"}]}],
                "triggers":[{"id":"discard-equipment","window":"discardPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"discard","options":[{"id":"discard"}]},{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment"]}]}]},
              {"id":"{{{Watcher}}}","revision":1,"triggers":[
                {"id":"accepted","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]},
                {"id":"completed","window":"cardResponseCompleted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]},
                {"id":"committed","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]},
              {"id":"{{{QuietSkill}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Watcher, QuietSkill }.ToDictionary(id => id, id => new { name = id,
                    description = "Native red chariot response and destruction fixture.", optionLabels = id == Driver
                        ? new Dictionary<string, string> { ["discard"] = "弃置" }
                        : id == Watcher ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Actual native chariot fixture.")
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var pools = new Dictionary<string, IReadOnlyList<string>>();
            foreach (var army in new[] { "cao", "yuan" })
            {
                var generals = Enumerable.Range(0, 9).Select(i => $"fixture:guandu-chariot-{army}-{i}").ToArray();
                foreach (var id in generals)
                {
                    var human = army == "cao" && id == generals[0];
                    b.AddGeneral(new(id, human ? "SP关羽" : id, human ? "guan_yu" : "", human ? Wusheng : QuietSkill,
                        army == "cao" ? "wei" : "qun", human ? 4 : 8, human ? ["sp:danji", Driver, Watcher, "guandu:polu"] : []));
                }
                pools["team:" + army] = generals;
            }
            const string deck = "fixture:guandu-chariot-deck";
            b.AddDeck(new(deck, deck, 4, 2, []) { PhysicalCards = Enumerable.Range(0, 120)
                .Select(i => new ContentDeckPhysicalCard("standard:duel", Suit.Heart, i % 13 + 1)).ToArray() });
            var layout = new[] { "team:cao", "team:cao", "team:cao", "team:cao", "team:yuan", "team:yuan", "team:yuan", "team:yuan" };
            b.AddMode(new(ModeId, ModeId, 8, 8, new Dictionary<string, int>(), deck, 6, pools.Values.SelectMany(p => p).ToArray(),
                ContentModeKind.Team, new Dictionary<string, int> { ["team:cao"] = 4, ["team:yuan"] = 4 })
            { GuanduActivity = new(pools, new Dictionary<string, string> { ["team:cao"] = pools["team:cao"][0], ["team:yuan"] = pools["team:yuan"][0] },
                [GuanduBattleEvent.Underdog], Enumerable.Range(0, 5).Select(_ => (IReadOnlyList<string>)layout).ToArray(), PlayerTier: 1) });
        }
    }
}
