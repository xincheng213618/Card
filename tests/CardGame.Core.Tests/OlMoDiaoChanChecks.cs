using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class OlMoDiaoChanChecks
{
    public static void NativeCohortAndAffectedUseCompletion()
    {
        QingshiCohort();
        HuanhuoUses();
    }

    private static void QingshiCohort()
    {
        var s = Scene.Start(qingshi: true);
        s.Reach(() => s.Branch("enter")); s.Answer(c => B(c) == "enter"); s.Play();
        var cohort = s.E<MoDiaoChanCohortStartedEvent>().Single();
        var granted = s.E<MoDiaoChanCardGrantedEvent>().Where(e => e.CohortId == cohort.CohortId).ToArray();
        Need(granted.Length == 4 && granted.Select(e => e.RecipientSeat).ToHashSet().SetEquals([0, 1, 2, 3]) &&
            granted.All(e => e.Kind == CardKind.Duel && s.G.CardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(e.RecipientSeat))),
            "Entering Qingshi obtains four distinct real single-target damage entities from the fixed draw pile.");
        var own = granted.Single(e => e.RecipientSeat == 0).CardId;
        s.Physical(own, 2); s.Play();
        Need(s.E<MoDiaoChanDiscardClaimedEvent>().All(e => e.CardId != own) &&
            s.G.CardMovements.Any(m => m.CardId == own && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile),
            "The owner's genuine marked Duel Use finishes in discard and is not recaptured as a non-Use payment.");
        s.End();
        s.Reach(() => s.Branch("card"));
        var allyCard = granted.Single(e => e.RecipientSeat == 1).CardId;
        Need(s.G.ResolutionStack.OfType<CardUseFrame>().Any(use => use.SourceSeat == 1 && use.Action?.PhysicalCards.Any(c => c.CardId == allyCard) == true),
            "The real ally AI uses its actual granted Duel before the owner chooses redirection.");
        var cost = s.G.CreateSnapshot(0).Players[0].Hand.First(c => c.Kind == CardKind.Dodge).Id;
        s.Answer(c => B(c) == "card" && c.Cards.SequenceEqual([cost]));
        s.Answer(c => B(c) == "confirm"); s.Reach(() => s.Branch("redirect"));
        var original = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MoDiaoChan is { Stage: MoDiaoChanStage.RedirectChoice }).MoDiaoChan!;
        var target = original.PreviousTarget == 2 ? 3 : 2;
        Need(s.P!.Choices.Any(c => B(c) == "redirect" && c.Targets.SequenceEqual([target])), "The actual new target is offered by the native unique-target window.");
        var view = SnapshotJson.Serialize(s.G.CreateSnapshot(0));
        s.G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(s.G.CreateCheckpoint())), s.Registry);
        Need(view == SnapshotJson.Serialize(s.G.CreateSnapshot(0)), "The paid redirect keeps its original Use, physical material and owning receipt through one cold recovery.");
        s.Answer(c => B(c) == "redirect" && c.Targets.SequenceEqual([target]));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && s.G.State.TurnNumber > 1);
        var redirect = s.E<MoDiaoChanRetargetedEvent>().Single();
        Need(redirect.ActorSeat == 1 && redirect.ProviderSeat == 1 && redirect.PreviousTarget == original.PreviousTarget && redirect.NewTarget == target &&
            s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == redirect.ActionId && e.Action.ActorSeat == 1 && e.Action.PhysicalCards.Any(c => c.CardId == allyCard)) &&
            s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 1 && e.TargetSeat == target && e.Amount == 1) &&
            s.G.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
            s.E<MoDiaoChanStartedEvent>().Where(e => e.Op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage).All(e =>
                s.E<MoDiaoChanDrawReturnedEvent>().Count(d => d.FrameId == e.FrameId && d.Requested == 1 && d.Actual == 1) == 1) &&
            s.E<MoDiaoChanStartedEvent>().Count(e => e.Op == SkillProgramEffectOp.ResolveMoDiaoChanQingshiDamage) == 2,
            "One paid redirect preserves the original ally Use and provider, changes its unique victim, and both genuine marked damage instances return native Draw1 rewards.");
        Need(s.E<MoDiaoChanCohortStartedEvent>().Length == 1, "The first later preparation cannot refresh while another original recipient still holds a cohort card.");
        foreach (var recipient in new[] { 2, 3 })
        {
            var card = granted.Single(e => e.RecipientSeat == recipient).CardId;
            Need(s.G.CreateSnapshot(recipient).Players[recipient].Hand.Single().Id == card, "The quiet real recipient still holds its exact original cohort entity.");
            s.Driver("discard-other", [recipient]);
            bool IsPublishedHiddenSlot(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card" &&
                c.Parameters.GetValueOrDefault("card-owner-seat") == recipient.ToString(System.Globalization.CultureInfo.InvariantCulture) &&
                c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand) && c.Parameters.GetValueOrDefault("slot-index") == "0";
            s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(IsPublishedHiddenSlot));
            Need(s.P!.Choices.Where(IsPublishedHiddenSlot).Single().Cards.Count == 0,
                "The real foreign Hand payment is offered as a private slot, without revealing an entity id.");
            s.Answer(IsPublishedHiddenSlot); s.Play();
            Need(s.E<MoDiaoChanDiscardClaimedEvent>().Count(e => e.CardId == card) == 1 &&
                s.G.CardMovements.Any(m => m.CardId == card && m.From == CardLocation.Hand(recipient) && m.To == CardLocation.DiscardPile) &&
                s.G.CardMovements.Any(m => m.CardId == card && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(0)),
                "A genuine non-Use foreign discard is recaptured once into the original Qingshi owner's real hand.");
        }
        Need(s.E<MoDiaoChanCohortStartedEvent>().Length == 1, "Leaving all original hands does not refresh the cohort immediately during Play.");
        s.End(); s.Reach(() => s.E<MoDiaoChanCohortStartedEvent>().Length == 2); s.Play();
        var refreshed = s.E<MoDiaoChanCohortStartedEvent>().Last();
        Need(refreshed.ActualTurn > cohort.ActualTurn && s.E<MoDiaoChanQingshiEnteredEvent>().Length == 1 &&
            s.E<MoDiaoChanCardGrantedEvent>().Count(e => e.CohortId == refreshed.CohortId) == 4,
            "The next real preparation automatically grants a complete new cohort without a second Rumo entry.");
    }

    private static void HuanhuoUses()
    {
        var s = Scene.Start(qingshi: false); s.Play();
        var promise = s.E<MoDiaoChanPromiseEvent>().First(e => e.TargetSeat == 0);
        Need(s.E<TurnStartedEvent>().Skip(promise.AfterTurnStartOrdinal).All(e => e.ActorSeat != 0) && s.P!.ForcedHandSelection is null,
            "RoundStarting follows this real TurnStarted; its promise affects the next actual human turn, not the already-started one.");
        s.End(); s.Play();
        Need(s.E<TurnStartedEvent>().Skip(promise.AfterTurnStartOrdinal).First(e => e.ActorSeat == 0).TurnNumber == s.G.State.TurnNumber,
            "Quiet real NPC turns return to the exact next TurnStarted qualified by the accepted promise.");
        var hint = s.P!.ForcedHandSelection;
        var actions = s.G.GetHumanLegalActions();
        Need(actions.Any(a => a.CardId == 0 && a.ConversionSource?.SkillId == DriverId && a.PlayedCardKind == CardKind.Slash),
            "The real zero-material draw-funded Slash offer remains queryable alongside Huanhuo's ordinary hand lock. " +
            $"hp={s.G.State.Players[0].Hp}; hand={string.Join(",", s.G.CreateSnapshot(0).Players[0].Hand.Select(c => $"{c.Id}/{c.Kind}/{c.Suit}"))}; actions={string.Join(";", actions.Select(a => $"{a.Kind}/{a.CardId}/{a.PlayedCardKind}/{a.ConversionSource?.SkillId}"))}");
        Need(hint is { SkillId: Huanhuo, RemainingUses: 2 } && s.G.CreateSnapshot(0).Players[0].Hand.Single(c => c.Id == hint.CardId).Kind == CardKind.DrawTwo,
            "The real enemy AI's paid round-start choice affects the next actual human turn and locks its only usable hand card.");
        s.Physical(hint!.CardId); s.Play();
        Need(s.E<MoDiaoChanUseClaimedEvent>() is [{ Claim.Ordinal: 1, Claim.Kind: CardKind.DrawTwo }] &&
            s.E<MoDiaoChanRandomDiscardEvent>() is [{ ActorSeat: 0, CardId: not null }] && s.E<MoDiaoChanUseSettledEvent>().Length == 1,
            "A real physical DrawTwo finishes and pays exactly one genuine random HE discard through its original use completion.");
        s.Driver("legacy-duel", [0, 3]); s.Play();
        var issued = s.E<MoDiaoChanLegacyDuelIssuedEvent>().Single().Receipt;
        var returned = s.E<MoDiaoChanLegacyDuelReturnedEvent>().Single();
        var claims = s.E<MoDiaoChanUseClaimedEvent>().Select(e => e.Claim).ToArray();
        Need(issued.ActorSeat == 0 && issued.TargetSeat == 3 && returned.EntryId == issued.EntryId && returned.ProducerFrameId == issued.ProducerFrameId &&
            returned.DamageApplied && returned.FinalTarget == 3 && s.E<MoDiaoChanLegacyDuelResumedEvent>().Count(e => e.EntryId == issued.EntryId) == 1 &&
            claims.Length == 2 && claims[1].Ordinal == 2 && claims[1].Kind == CardKind.Duel && claims[1].ActionId == -issued.EntryId &&
            s.E<MoDiaoChanRandomDiscardEvent>().Count(e => e.ActorSeat == 0 && e.CardId is not null) == 2 &&
            s.E<MoDiaoChanUseSettledEvent>().Length == 2 && s.P!.ForcedHandSelection is null &&
            s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 3 && e.Amount == 1),
            "The real legacy direct Duel fully returns to its exact producer, settles the second affected use once, pays its random discard, then releases the two-use lock.");
        foreach (var paid in s.E<MoDiaoChanRandomDiscardEvent>())
            Need(s.E<MoDiaoChanCostEvent>().Count(e => e.FrameId == paid.FrameId && e.CardId == paid.CardId &&
                e.From.OwnerSeat == 0 && e.To == CardLocation.DiscardPile && s.G.CardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == e.CardId && m.From == e.From && m.To == e.To)) == 1,
                "Each random choice is backed by exactly one original physical HE payment, rather than an invented completion or duplicate discard.");
    }

    private static string B(PromptChoice c) => c.Parameters.GetValueOrDefault("branch", "");
    private static void Need(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
    private const string Huanhuo = "ol:mo-huanhuo", Qingshi = "ol:mo-qingshi", DriverId = "fixture:modc-driver", Quiet = "fixture:modc-quiet",
        Owner = "fixture:modc-owner", Ally = "fixture:modc-ally", EnemyA = "fixture:modc-enemy-a", EnemyB = "fixture:modc-enemy-b", Deck = "fixture:modc-deck";
    private static ShanheStartingCard C(CardKind kind, Suit suit = Suit.Club) => new(kind, suit, 7);

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        internal GameEngine G = game;
        internal ContentRegistry Registry = registry;
        internal PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        internal T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        internal bool Branch(string b) => P is { PlayerSeat: 0 } p && p.Choices.Any(c => B(c) == b);
        internal void Accept(GameCommand c) { var r = G.Submit(c); Need(r.Accepted, r.Error?.Message ?? "Real command rejected."); }
        internal void Answer(Func<PromptChoice, bool> match)
        { var p = P!; Need(p.PlayerSeat == 0, "Only real human choices are submitted."); Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(match).Id, G.Revision)); }
        internal void Reach(Func<bool> done)
        {
            for (var step = 0; step < 240; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The fixed real game ended before the tested native return.");
                if (P is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected human prompt: " + p.Prompt + ";choices=" + string.Join(';', p.Choices.Select(c => c.Id)));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Magic Diao Chan bounded native flow was not reached;turn=" + G.State.TurnNumber + ";phase=" + G.State.Phase + ";frames=" +
                string.Join(';', G.ResolutionStack.Select(f => f.GetType().Name + "/" + f.Id + (f is ProgramSkillFrame p ? "/" + p.InstructionIndex + "/" + p.MoDiaoChan?.Stage + "/" + p.MoDiaoChanLegacyDuel : ""))));
        }
        internal void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        internal void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        internal void Driver(string id, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, DriverId, id, [], targets, G.Revision, P!.PromptId));
        internal void Physical(int id, int? target = null)
        { var a = G.GetHumanLegalActions().First(a => a.CardId == id && (target is null || a.TargetSeats.SequenceEqual([target.Value]))); Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource }); }
        internal static Scene Start(bool qingshi)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(qingshi));
            var hand = qingshi ? new[] { C(CardKind.Dodge), C(CardKind.Dodge, Suit.Spade) } :
                new[] { C(CardKind.DrawTwo), C(CardKind.Dodge), C(CardKind.Dodge, Suit.Spade), C(CardKind.Dodge) };
            var setup = new ShanheBattleSetup(7, 1, "magic-diao-chan", Owner, qingshi ? 8 : 4, 0, 0, 0, 0, [], hand, [],
                [new(EnemyA, Hp: 8) { StartingHand = qingshi ? [] : [C(CardKind.Dodge), C(CardKind.Dodge, Suit.Spade)] }, new(EnemyB, Hp: 8)],
                [new(Ally, Hp: 8)], new string('a', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture(bool qingshi) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-mo-diao-chan", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:mo-diao-chan", "ol:mo-sun-quan");
            foreach (var registeredKind in new[] { CardKind.Duel, CardKind.Dodge, CardKind.DrawTwo })
            { var card = CardCatalog.Get(registeredKind); b.AddCard(new("fixture:modc-card-" + registeredKind, card.DisplayName, card.CategoryName, card.Description, registeredKind)); }
            var activations = new object[] {
                new { id = "discard-other", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                    effects = new object[] { new { op = "selectAndMoveOwnedCard", target = "owner", chooserRef = new { kind = "owner" }, cardOwnerRef = new { kind = "selectedTarget" }, zones = new[] { "hand" }, count = 1, destination = "discardPile", awaitMovementTriggers = true } } },
                new { id = "legacy-duel", minCards = 0, maxCards = 0, minTargets = 2, maxTargets = 2, targetKind = "anyLiving", usesPerTurn = (int?)null,
                    effects = new object[] { new { op = "startVirtualDuel", target = "owner" } } }
            };
            var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new object[] {
                new { id = DriverId, revision = 1, activations, viewAs = qingshi ? Array.Empty<object>() : new object[] {
                    new { id = "zero-slash-query", inputCount = 0, sourceZones = Array.Empty<string>(), inputKinds = Array.Empty<string>(), inputSuits = Array.Empty<string>(),
                        outputKind = "slash", forPlay = true, forResponse = false, useOnly = true,
                        drawFundedDistinctBasic = new { methodLedgerId = "fixture:modc-zero" } }
                } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
            } });
            var labels = new Dictionary<string, string> { [DriverId] = "Real native driver", [Quiet] = "Wait" };
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = labels.ToDictionary(p => p.Key, p => new { name = p.Value, description = p.Value }) }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, labels[id], labels[id]) { Program = program, ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Owner, "Real owner", "supporter", "standard:none", "qun", 8, qingshi ? [Qingshi, DriverId] : [DriverId]));
            b.AddGeneral(new(Ally, "Ally", "supporter", "standard:none", "shu", 8, qingshi ? [] : [Quiet]));
            b.AddGeneral(new(EnemyA, "Enemy A", "supporter", "standard:none", "wei", 8, qingshi ? [Quiet] : [Huanhuo, Quiet]));
            b.AddGeneral(new(EnemyB, "Enemy B", "supporter", "standard:none", "wei", 8, [Quiet]));
            var deckKind = qingshi ? CardKind.Duel : CardKind.Dodge;
            b.AddDeck(new(Deck, "Fixed small deck", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 40)
                .Select(_ => new ContentDeckPhysicalCard("fixture:modc-card-" + deckKind, Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "Magic Diao Chan native fixture", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
