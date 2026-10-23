using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryHeYanChecks
{
    private const string Hero = "anniversary:he-yan", Yachai = "anniversary:yachai", Qingtan = "anniversary:qingtan", Peer = "fixture:he-yan-peer",
        NoDraw = "fixture:he-yan-no-draw", Quiet = "fixture:he-yan-quiet", Driver = "fixture:he-yan-driver", DrawHold = "fixture:he-yan-draw-hold", GainHold = "fixture:he-yan-gain-hold", Deck = "fixture:he-yan-deck";

    public static void SourcedChoicesAndSealedSuitHandsResumeCold()
    {
        var s = Scene.Start(false); s.Play();
        s.Use("self"); s.Offer(); s.Activate(); s.Menu(AnniversaryHeYanStage.SourceChoice);
        s.Need(s.P!.PlayerSeat == 0 && s.Root!.AnniversaryHeYan!.SourceSeat == 0 && s.P.Choices.Count == 3, "Actual self damage may designate its own source; no other-character restriction is invented.");
        s.Branch("discard"); s.Menu(AnniversaryHeYanStage.DiscardChoice);
        var discardFrame = s.Root!.Id; var required = s.Root.AnniversaryHeYan!.RequiredDiscard;
        s.Need(required == 3 && s.V(0).HandCount == 5, "Five real Hand cards require exactly three, rounded upward from the frozen branch count.");
        for (var i = 0; i < required; i++) s.Answer(c => c.Cards is [var id] && s.V(0).Hand!.Single(h => h.Id == id).Suit == Suit.Spade);
        s.Play();
        s.Need(s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == discardFrame && e.ExecutorSeat == 0 && e.ActualCount == 3 && e.HandCount == 3) == 1 &&
            s.V(0).HandCount == 2, "The accepted first branch pays one real logical discard of three original Hand entities.");
        s.Use("heal"); s.Play(); s.Use("self"); s.Offer(); s.Activate(); s.Menu(AnniversaryHeYanStage.SourceChoice); s.Branch("ban");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == DrawHold);
        var banFrame = s.Root!.Id;
        s.Need(s.Root.AnniversaryHeYan is { Stage: AnniversaryHeYanStage.Drawing, DrawAmount: 2 } && s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.ParentFrameId == banFrame && f.Context.RecipientSeat == 0 && f.Context.RequestedCount == 2),
            "The second branch issues its ban before the original two-card native draw request suspends.");
        s.Cold(); s.Skip(); s.Play();
        s.Need(s.E<AnniversaryHeYanHandUseBannedEvent>().Count(e => e.FrameId == banFrame && e.AffectedSeat == 0) == 1 &&
            s.E<AnniversaryHeYanDrawReturnedEvent>().Count(e => e.FrameId == banFrame && e.TargetSeat == 0 && e.Requested == 2 && e.Actual == 2) == 1 &&
            !s.G.GetHumanLegalActions().Any(a => a.CardId is not null && a.Kind is not (LegalActionKind.Recast or LegalActionKind.UseProgramSkill or LegalActionKind.UseEquipmentEffect)),
            "Cold does not repeat the ban or native draw; current Hand cards cannot be used while skill actions remain legal.");

        s.Use("duel", [1]); s.Reach(() => s.P is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        var played = s.P!.Choices.FirstOrDefault(c => c.Cards is [var id] && s.V(0).Hand!.Single(h => h.Id == id).Suit == Suit.Club);
        s.Need(played is not null, "The same ban still permits a real Hand Slash to be played in response to Duel.");
        var playedId = played!.Cards.Single(); s.Answer(c => c.Id == played.Id); s.Play();
        s.Need(s.E<CardActionAcceptedEvent>().Count(e => e.Action is { ActorSeat: 0, Type: CardActionType.Response, EffectiveKind: CardKind.Slash } a && a.PhysicalCards.Any(c => c.CardId == playedId)) == 1 &&
            s.Zone(playedId) == CardLocation.DiscardPile, "The legal pure response pays and completes once, rather than granting permission to use a Hand card.");

        s.Qingtan(); s.Menu(AnniversaryHeYanStage.SealedChoice);
        var qFrame = s.Root!.Id; var plans = s.Root.AnniversaryHeYan!.Plans;
        s.Need(plans.Select(p => p.Seat).SequenceEqual([0, 1, 2]) && !s.E<ProgramCardsRevealedEvent>().Any(e => e.FrameId == qFrame), "Only living characters with Hand cards participate, and no commitment is publicly shown early.");
        s.Answer(c => c.Cards is [var id] && s.V(0).Hand!.Single(h => h.Id == id).Suit == Suit.Spade);
        s.Menu(AnniversaryHeYanStage.SuitChoice);
        var shown = s.Root!.AnniversaryHeYan!.Plans.Select(p => (p.Seat, Card: p.Cards.Single(c => c.Id == p.SelectedId))).ToArray();
        var matches = shown.Where(p => p.Card.Suit == Suit.Spade).Select(p => p.Seat).ToArray(); var rest = shown.Where(p => p.Card.Suit != Suit.Spade).Select(p => p.Card.Id).ToArray();
        s.Need(s.E<AnniversaryHeYanChoiceCommittedEvent>().Count(e => e.FrameId == qFrame) == plans.Count &&
            s.E<ProgramCardsRevealedEvent>().Where(e => e.FrameId == qFrame).SelectMany(e => e.Cards).Select(c => c.Id).Order().SequenceEqual(shown.Select(p => p.Card.Id).Order()) && matches.SequenceEqual([0, 1]),
            "All private commitments precede the complete simultaneous reveal, with each actual holder retained.");
        var selfCard = shown.Single(p => p.Seat == 0).Card.Id;
        s.Branch("suit-" + (int)Suit.Spade); s.Reach(() => s.P?.SkillPrompt?.SkillId == GainHold);
        s.Need(s.Root!.Id == qFrame && s.Root.AnniversaryHeYan is { Stage: AnniversaryHeYanStage.MovementChildren, ChosenSuit: Suit.Spade } &&
            s.V(0).Hand!.Any(c => c.Id == shown.Single(p => p.Seat == 1).Card.Id) && s.G.CardMovements.All(m => m.CardId != selfCard || m.Reason.Value != "skill.anniversary-he-yan.qingtan-obtain"),
            "A true gain child occurs after the foreign matching card moves; the owner's matching Hand card is retained without a fake self transfer.");
        s.Cold(); s.Skip(); s.Play();
        s.Need(s.E<AnniversaryHeYanDrawReturnedEvent>().Where(e => e.FrameId == qFrame).Select(e => (e.TargetSeat, e.Requested, e.Actual)).SequenceEqual(matches.Select(seat => (seat, 1, 1))) &&
            rest.All(id => s.Zone(id) == CardLocation.DiscardPile) && s.E<AnniversaryHeYanPaidEvent>().Count(e => e.FrameId == qFrame && e.Kind == "qingtan-obtain") == 1 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == qFrame && e.Reason.Value == "skill.anniversary-he-yan.qingtan-rest" && e.ActualCount == rest.Length) == 1,
            "Both matching revealers receive independent native draws exactly once; only the other displayed entities are then logically discarded.");
        s.Use("incoming", [3]); s.Offer(); s.Activate(); s.Menu(AnniversaryHeYanStage.SourceChoice);
        s.Need(s.Root!.AnniversaryHeYan!.SourceSeat == 3 && s.P!.PlayerSeat == 3 && s.P.Choices is [var only] && only.Parameters.GetValueOrDefault("branch") == "ban",
            "The official empty-source FAQ excludes discard and reveal/give, even though the second branch remains valid.");
        s.Play(); s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId)); s.Play();
        s.Use("refill"); s.Play();
        var unbanned = s.G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is not null && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
        s.Need(s.G.State.TurnNumber > 1 && unbanned is not null, "The Hand-use prohibition expires at the real actual-turn boundary, permitting the newly drawn physical Slash.");
        s.Accept(new PlayCardCommand(0, unbanned!.CardId!.Value, unbanned.TargetSeats, s.G.Revision, s.P!.PromptId, unbanned.PlayedCardKind, unbanned.TargetCardId)
            { ConversionSource = unbanned.ConversionSource, AdditionalConversionSources = unbanned.AdditionalConversionSources }); s.Play();
        s.Need(s.E<CardUseFinishedEvent>().Count(e => e.CardId == unbanned.CardId && e.CardKind == CardKind.Slash) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == unbanned.CardId && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "The newly permitted Hand card actually pays and completes a native use once after the ban expires.");
        s.Qingtan(); s.Menu(AnniversaryHeYanStage.SealedChoice); s.Answer(c => c.Cards.Count == 1); s.Menu(AnniversaryHeYanStage.SuitChoice);
        var decline = s.Root!; var untouched = decline.AnniversaryHeYan!.Plans.Select(p => (p.Seat, Id: p.SelectedId!.Value)).ToArray(); s.Branch("decline"); s.Play();
        s.Need(untouched.All(p => s.Zone(p.Id) == CardLocation.Hand(p.Seat)) && !s.E<AnniversaryHeYanPaidEvent>().Any(e => e.FrameId == decline.Id) && !s.E<AnniversaryHeYanDrawReturnedEvent>().Any(e => e.FrameId == decline.Id),
            "Declining Qingtan's optional obtain does not discard the other shown cards or grant reward draws.");

        var foreign = Scene.Start(true); foreign.Play(); foreign.Use("hit", [1]); foreign.Menu(AnniversaryHeYanStage.SourceChoice);
        foreign.Need(foreign.Root!.OwnerSeat == 1 && foreign.P!.PlayerSeat == 0 && foreign.P.Choices.Count == 3, "A different He Yan delegates the actual decision to the living damage source, rather than making it for that player.");
        foreign.Branch("reveal"); foreign.Menu(AnniversaryHeYanStage.SuitChoice);
        var giftRoot = foreign.Root!; var giftIds = giftRoot.AnniversaryHeYan!.Plans[0].Cards.Where(c => c.Suit == Suit.Heart).Select(c => c.Id).ToArray();
        foreign.Need(foreign.P!.PlayerSeat == 0 && giftIds.Length == 2 && foreign.P.Choices.Select(c => c.Parameters["branch"]).ToHashSet().SetEquals(["suit-" + (int)Suit.Heart, "suit-" + (int)Suit.Spade]), "The source chooses only a suit actually present in its complete revealed Hand.");
        foreign.Branch("suit-" + (int)Suit.Heart); foreign.Play();
        foreign.Need(giftIds.All(id => foreign.Zone(id) == CardLocation.Hand(1)) && foreign.V(0).HandCount == 1 &&
            foreign.E<AnniversaryHeYanPaidEvent>().Count(e => e.FrameId == giftRoot.Id && e.Kind == "yachai-give" && e.Executor == 0 && e.Count == 2) == 1,
            "The actual source gives every real card of its chosen suit to He Yan, with no random substitute or partial transfer.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-he-yan", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero); var c = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "固定安静角色", "", NoDraw, "wei", 6));
            b.AddDeck(new(Deck, "固定原生杀", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "何晏真实委托与全体密选", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new[] { NoDraw, Quiet, Driver, DrawHold, GainHold }.ToDictionary(id => id, id => new { name = id, description = "短流程真实原生暂停" }) });
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
         {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
         {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
         {"id":"{{Driver}}","revision":1,"activations":[
          {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
          {"id":"heal","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":2}]},
          {"id":"refill","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},
          {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
          {"id":"hit","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
          {"id":"duel","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"usesPerPhase":2,"effects":[{"op":"useSelectedActorDuel","target":"owner"}]}
         ]},
         {"id":"{{DrawHold}}","revision":1,"triggers":[{"id":"hold","window":"nativeDrawStarting","subject":"owner","optional":true,"isSkillInvocation":false,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
         {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"hold","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.anniversary-he-yan.qingtan-obtain"],"optional":true,"isSkillInvocation":false,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
        ]}
        """;
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryHeYan is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Use(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Qingtan() => Accept(new UseProgramSkillCommand(0, AnniversaryHeYanChecks.Qingtan, "simultaneous-suit-hands", [], [], G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Yachai && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Branch(string id) => Answer(c => c.Parameters.GetValueOrDefault("branch") == id);
        public void Menu(AnniversaryHeYanStage stage) => Reach(() => Root?.AnniversaryHeYan?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-he-yan") == true);
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing the exact current He Yan decision."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 120; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId is DrawHold or GainHold && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Skip();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected He Yan boundary. " + Dump());
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "He Yan missed a bounded original-frame continuation.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(); var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold retains sealed choices, original payment and typed child without issuing it twice.");
        }
        public void Accept(GameCommand c) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected He Yan command."); }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryHeYan, Hand = Enumerable.Range(0, 4).Select(i => V(i).HandCount),
            Starts = E<AnniversaryHeYanStartedEvent>(), Paid = E<AnniversaryHeYanPaidEvent>(), Draws = E<AnniversaryHeYanDrawReturnedEvent>(), Damage = E<DamageAppliedEvent>() });
        public static Scene Start(bool foreign)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard[] hand = foreign ? [new(CardKind.Peach, Suit.Heart, 3), new(CardKind.Slash, Suit.Heart, 8), new(CardKind.Dodge, Suit.Spade, 9)] :
                [new(CardKind.Slash, Suit.Spade, 2), new(CardKind.Slash, Suit.Spade, 3), new(CardKind.Slash, Suit.Spade, 4), new(CardKind.Slash, Suit.Spade, 5), new(CardKind.Peach, Suit.Heart, 3)];
            var ally = foreign ? new ShanheCombatantDefinition(Hero, ExtraSkillIds: [NoDraw, Quiet]) { StartingHand = [] } :
                new ShanheCombatantDefinition(Peer, ExtraSkillIds: [Quiet]) { StartingHand = [new(CardKind.Dodge, Suit.Spade, 7)] };
            var setup = new ShanheBattleSetup(73, 1, foreign ? "he-yan-source" : "he-yan-three-choices", foreign ? Peer : Hero, foreign ? 6 : 3, 0, 0, 0, 0,
                foreign ? [Driver, DrawHold, GainHold] : [NoDraw, Driver, DrawHold, GainHold], hand, [],
                [new(Peer, ExtraSkillIds: [Quiet]) { StartingHand = [new(CardKind.Peach, Suit.Heart, 7)] }, new(Peer, ExtraSkillIds: [Quiet]) { StartingHand = [] }], [ally], new string('f', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
