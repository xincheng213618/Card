using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryFengXiChecks
{
    private const string Hero = "anniversary:feng-xi", Yusui = "anniversary:yusui", Boyan = "anniversary:boyan",
        Peer = "fixture:feng-xi-peer", NoDraw = "fixture:feng-xi-no-draw", Quiet = "fixture:feng-xi-quiet",
        Shield = "fixture:feng-xi-sole-target", Hold = "fixture:feng-xi-draw-hold", PaidHold = "fixture:feng-xi-paid-hold", Deck = "fixture:feng-xi-deck";

    public static void NativeRefillAndBothBlackTargetBranchesResumeCold()
    {
        var s = Scene.Start(); s.Play();
        s.Accept(new UseProgramSkillCommand(0, Boyan, "fill-and-seal-hand", [], [1], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold);
        var refill = s.Root!;
        s.Need(s.P!.PlayerSeat == 1 && refill.AnniversaryFengXi is { Stage: AnniversaryFengXiStage.Drawing, Target: 1, Amount: 3 } &&
            s.V(1).MaxHp == 6 && s.V(1).HandCount == 2 &&
            s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(f => f.Context.ParentFrameId == refill.Id && f.Context.RecipientSeat == 1 && f.Context.RequestedCount == 3) &&
            !s.E<HandCardColorRestrictionGrantedEvent>().Any(e => e.Restriction.ParentFrameId == refill.Id),
            "Boyan issues three real draws to reach five total Hand cards, then waits for the native draw child before sealing that Hand.");
        s.Cold(); s.Play();
        s.Need(s.V(1).HandCount == 5 && s.E<AnniversaryFengXiDrawReturnedEvent>().Count(e => e.FrameId == refill.Id && e.Target == 1 && e.Requested == 3 && e.Actual == 3) == 1 &&
            s.E<HandCardColorRestrictionGrantedEvent>().Count(e => e.Restriction.ParentFrameId == refill.Id && e.Restriction.AffectedSeat == 1) == 2,
            "Cold resumes the original refill once, then grants both color prohibitions after its native draw returns.");

        var duel = s.Card(CardKind.Duel, 1); s.Play();
        s.Need(s.V(1).HandCount == 5 && s.V(1).Hp == 5 &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceCardUseResolutionId == duel && e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            !s.E<CardActionAcceptedEvent>().Any(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Slash),
            "Five real Hand Slashes remain unavailable even as pure played responses to Duel; the native Duel really deals its damage.");

        s.End(); s.YusuiOffer(); s.Activate(); s.Menu(AnniversaryFengXiStage.Choice);
        var hand = s.Root!; var handUse = hand.AnniversaryFengXi!.UseId!.Value;
        s.Need(s.V(0).Hp == 2 && hand.AnniversaryFengXi.Target == 3 && s.V(3).HandCount == 5 && s.V(0).HandCount == 3 &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.FrameId == hand.Id && e.SkillId == Yusui && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            !s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == handUse),
            "Yusui pays the real one-HP cost before choosing, while preserving the black Slash and its actual foreign user.");
        s.Cold(); s.Branch("hand"); s.Menu(AnniversaryFengXiStage.DiscardChoice);
        s.Need(s.P!.PlayerSeat == 3 && s.Root!.AnniversaryFengXi is { Target: 3, Amount: 2 } &&
            s.P.RequiredCardCount == 2 && s.P.MinimumCardCount == 2 &&
            s.P.Choices.All(c => c.Cards.Count == 1 && s.Zone(c.Cards[0]) == CardLocation.Hand(3) &&
                c.Parameters.GetValueOrDefault("owned-selection-role") == "pick"),
            "The actual user privately receives the complete two-card owned draft for the excess Hand cards, rather than singleton immediate payment or Feng Xi choosing foreign cards.");
        var beforeDiscard = s.G.CardMovements.Count;
        var firstDiscard = s.P.Choices[0].Cards.Single(); var stage = s.P.Choices[0].Parameters["owned-selection-stage"];
        s.AnswerDiscardParticipant(c => c.Cards.SequenceEqual([firstDiscard]));
        s.Need(s.P is { PlayerSeat: 3, RequiredCardCount: 1, MinimumCardCount: 1 } && s.Root!.Id == hand.Id &&
            s.Root.AnniversaryFengXi!.Cards.SequenceEqual([firstDiscard]) && s.G.CardMovements.Count == beforeDiscard &&
            s.P.Choices.All(c => c.Parameters.GetValueOrDefault("owned-selection-selected") == firstDiscard.ToString() &&
                c.Parameters.GetValueOrDefault("owned-selection-stage") == stage),
            "The native first pick stays unpaid on the original Yusui frame, remains highlighted and reduces the same group's remaining bound before cold recovery.");
        s.Cold(); var secondDiscard = s.P!.Choices[0].Cards.Single(); s.AnswerDiscardParticipant(c => c.Cards.SequenceEqual([secondDiscard]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == PaidHold);
        s.Need(s.Root!.Id == hand.Id && s.Root.AnniversaryFengXi!.Stage == AnniversaryFengXiStage.MovementChildren &&
            s.G.CardMovements.Skip(beforeDiscard).Where(m => m.Reason.Value == "skill.anniversary.yusui.discard")
                .Select(m => m.CardId).Order().SequenceEqual(new[] { firstDiscard, secondDiscard }.Order()),
            "The complete original two-card group pays once before its real discard-observer child.");
        s.Cold(); s.AnswerDiscardParticipant(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.V(3).HandCount == s.V(0).HandCount && s.E<AnniversaryFengXiPaidEvent>().Count(e => e.FrameId == hand.Id && e.Target == 3 && e.Count == 2) == 1 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Count(e => e.ParentFrameId == hand.Id && e.ExecutorSeat == 3 && e.ActualCount == 2 && e.HandCount == 2) == 1,
            "The real foreign payment is one native logical discard, after which the original Slash proceeds to its native Dodge response.");
        s.Dodge(); s.Play();
        s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == handUse) == 1 && s.V(0).Hp == 2 && !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 0),
            "The paid branch and cold choice never duplicate HP loss or replace the original Slash; a real Dodge prevents its damage.");

        s.Card(CardKind.Peach, 0); s.Play(); s.End(); s.YusuiOffer(); s.Activate(); s.Menu(AnniversaryFengXiStage.Choice);
        var health = s.Root!; var healthUse = health.AnniversaryFengXi!.UseId!.Value;
        s.Need(health.Id != hand.Id && s.G.State.TurnNumber > hand.AnniversaryFengXi.Turn && s.V(0).Hp == 2 && s.V(3).Hp == 6,
            "The next actual turn grants a fresh once-per-turn Yusui opportunity after a genuine Peach restores Feng Xi.");
        s.Branch("hp"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        s.Need(s.V(3).Hp == 2 && s.E<AnniversaryFengXiHealthIssuedEvent>().Single(e => e.FrameId == health.Id) is { Target: 3, OwnerHp: 2, TargetHp: 6, Amount: 4 } &&
            s.E<ProgramSkillHpLostEvent>().Count(e => e.FrameId == health.Id && e.TargetSeat == 3 && e.Amount == 4) == 1 &&
            !s.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 3),
            "The second branch really loses the user's excess four HP, with the comparison frozen after Feng Xi's cost; it is neither damage nor a direct HP assignment.");
        s.Dodge(); s.Play();
        s.Need(s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == healthUse) == 1 &&
            s.E<AnniversaryFengXiStartedEvent>().Count(e => e.Source.SkillId == Yusui) == 2 && s.V(0).Hp == 2 && s.V(3).Hp == 2 &&
            s.E<AnniversaryFengXiDrawReturnedEvent>().Count(e => e.FrameId == refill.Id) == 1,
            "Both independent native Slash uses finish once; the prior draw invoice and turn-limited activation do not leak into the new turn.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-feng-xi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero); var c = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            b.AddGeneral(new(Peer, "固定六体力角色", "", NoDraw, "wei", 6, [PaidHold]));
            b.AddDeck(new(Deck, "固定原生杀", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "冯熙真实补牌与原用牌者选择", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new[] { NoDraw, Quiet, Shield, Hold, PaidHold }.ToDictionary(id => id, id => new { name = id, description = "短流程真实原生子窗口",
            optionLabels = id == PaidHold ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) });
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Shield}}","revision":1,"cardPolicies":[{"id":"only-human-is-a-legal-slash-target","kind":"prohibitTarget","cardKinds":["slash","fireSlash","thunderSlash"]}]},
          {"id":"{{Hold}}","revision":1,"triggers":[{"id":"hold","window":"nativeDrawStarting","subject":"owner","optional":true,"isSkillInvocation":false,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
          {"id":"{{PaidHold}}","revision":1,"triggers":[{"id":"paid-group","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.anniversary.yusui.discard"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
        ]}
        """;
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryFengXi is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void YusuiOffer() => Reach(() => P?.SkillPrompt?.SkillId == Yusui && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Branch(string branch) => Answer(c => c.Parameters.GetValueOrDefault("branch") == branch);
        public void Menu(AnniversaryFengXiStage stage) => Reach(() => Root?.AnniversaryFengXi?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-feng-xi") == true);
        public void Dodge() => Answer(c => c.Parameters.GetValueOrDefault("response") == "dodge" && c.Cards.Count == 1);
        public long Card(CardKind kind, int target)
        {
            var cardId = V(0).Hand!.First(c => c.Kind == kind).Id;
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId == cardId && a.TargetSeats.SequenceEqual([target]) &&
                a.Kind == (kind == CardKind.Peach ? LegalActionKind.Peach : LegalActionKind.Duel));
            Need(action is not null, "The real exact Hand card and target must be a legal native action.");
            Accept(new PlayCardCommand(0, action!.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)
                { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources });
            return E<CardUseDeclaredEvent>().Last(e => e.CardId == cardId && e.SourceSeat == 0).ResolutionId;
        }
        public void Answer(Func<PromptChoice, bool> pick)
        {
            var p = P!; Need(V(p.PlayerSeat).IsHuman, "Only the actual human may submit this answer.");
            var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "Missing the exact current Feng Xi response.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void AnswerDiscardParticipant(Func<PromptChoice, bool> pick)
        {
            var p = P!; Need(Root?.AnniversaryFengXi is { } r && p.PlayerSeat == r.Target &&
                (r.Stage == AnniversaryFengXiStage.DiscardChoice && p.SkillPrompt?.SkillId == Yusui ||
                 r.Stage == AnniversaryFengXiStage.MovementChildren && p.SkillPrompt?.SkillId == PaidHold),
                "Only the exact original user may answer this real unpaid discard or its paid child.");
            var choice = p.Choices.FirstOrDefault(pick); Need(choice is not null, "Missing the current user's exact discard continuation.");
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice!.Id, G.Revision));
        }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 160; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Feng Xi boundary. " + Dump());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Feng Xi missed a bounded original-frame continuation.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) &&
                frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold retains the actual choice, one-time payment and native child without reissuing either.");
        }
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Feng Xi command."); }
        public void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryFengXi, Players = Enumerable.Range(0, 4).Select(i => new { Seat = i, V(i).Hp, V(i).HandCount }),
            Started = E<AnniversaryFengXiStartedEvent>(), Paid = E<AnniversaryFengXiPaidEvent>(), Draw = E<AnniversaryFengXiDrawReturnedEvent>(), Loss = E<ProgramSkillHpLostEvent>(), Damage = E<DamageAppliedEvent>() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(67, 1, "feng-xi-two-native-branches", Hero, 3, 0, 0, 0, 0, [NoDraw],
                [new(CardKind.Duel, Suit.Spade, 1), new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.Dodge, Suit.Heart, 8), new(CardKind.Peach, Suit.Heart, 3)], [],
                [new(Peer, ExtraSkillIds: [Quiet]) { StartingHand = [] },
                 new(Peer) { StartingHand = Enumerable.Range(0, 6).Select(_ => new ShanheStartingCard(CardKind.Slash, Suit.Club, 7)).ToArray() }],
                [new(Peer, ExtraSkillIds: [Quiet, Shield, Hold]) { StartingHand = [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Slash, Suit.Club, 8)] }], new string('f', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
