using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class AnniversaryWuFuChecks
{
    private const string Hero = "anniversary:wu-fu", Zhonge = "anniversary:zhonge", Juekai = "anniversary:juekai", Peer = "fixture:wu-fu-peer",
        NoDraw = "fixture:wu-fu-no-draw", Quiet = "fixture:wu-fu-quiet", Driver = "fixture:wu-fu-driver", Watch = "fixture:wu-fu-native-watch";

    public static void CommittedPairNativeSlashesExactResetAndHandUseCold()
    {
        var s = Scene.Start(); s.Play();
        var initial = s.V().HandCount;
        s.Zhonge(); s.Stage(AnniversaryWuFuStage.AdjustDiscard);
        s.Need(initial == 5 && s.Root!.AnniversaryWuFu!.Count == 2, "Zhonge first requests the actual excess over current MaxHp.");
        s.Answer(c => s.Kind(c.Cards.Single()) == CardKind.Peach);
        s.Answer(c => s.Kind(c.Cards.Single()) == CardKind.Dodge);
        s.Stage(AnniversaryWuFuStage.Target);
        var first = s.Root!.Id;
        s.Need(s.V().HandCount == 3 && s.E<NativeLogicalDiscardInvocationRecordedEvent>().Single(e => e.ParentFrameId == first) is { ExecutorSeat: 0, ActualCount: 2 },
            "The complete two-card adjustment is a real native logical discard, before choosing the peer.");
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Stage(AnniversaryWuFuStage.Commit);
        s.Need(s.P!.IsPrivate && s.G.CreateSnapshot(2).PendingDecision is null && s.E<AnniversaryWuFuVoteRevealedEvent>().Length == 0,
            "The first participant's real pending commitment is private and no public vote has been released.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "slash");
        s.HoldNative();
        var votes = s.E<AnniversaryWuFuVoteRevealedEvent>().Where(e => e.FrameId == first).ToArray();
        var draws = s.E<AnniversaryWuFuDrawReturnedEvent>().Where(e => e.FrameId == first).ToArray();
        var native = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.AnniversaryWuFuSlash is not null);
        s.Need(votes is [{ Seat: 0, Draw: false }, { Seat: 1, Draw: true }] && draws.Select(e => e.Recipient).SequenceEqual([0, 0, 1]) && draws.All(e => e.Requested == 1 && e.Actual == 1) &&
            native is { CardId: 0, CardKind: CardKind.Slash, PhysicalCardIds.Count: 0, Action: { ActorSeat: 0, ProviderSeat: 0, TargetSeats: [1], ConversionChain.Count: 0 } } &&
            s.Root!.AnniversaryWuFu is { Cursor: 3, Stage: AnniversaryWuFuStage.NativeUse, Returned: false },
            "Different commitments execute numbered Draw first, twice for Wu Fu and once for the peer, then issue the original zero-material native Slash child.");
        var useId = native.Id; var beforeDraws = s.E<AnniversaryWuFuDrawReturnedEvent>().Length;
        s.Cold(); s.Continue(); s.Play();
        var uses = s.E<AnniversaryWuFuSlashIssuedEvent>().Where(e => e.Origin.FrameId == first).Select(e => e.Origin).ToArray();
        s.Need(uses.Select(u => u.Actor).SequenceEqual([0, 0, 1]) && uses.Select(u => u.Target).SequenceEqual([1, 1, 0]) &&
            s.E<AnniversaryWuFuDrawReturnedEvent>().Length == beforeDraws && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId) == 1 &&
            uses.All(u => s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == u.UseId && e.CardId == 0 && e.SourceSeat == u.Actor) == 1 &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == u.UseId) == 1 && s.E<AnniversaryWuFuSlashReturnedEvent>().Count(e => e.UseId == u.UseId && e.ActionId == u.ActionId) == 1) &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 2 && s.V().Hp == 2 &&
            !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Zhonge),
            "Cold returns each of the three ordered whole native Slashes once, preserves true damage and never repeats paid draws or refreshes the once-per-phase activation.");

        s.Juekai(); s.Play();
        var arm = s.E<AnniversaryWuFuJuekaiEvent>().Single();
        s.Need(arm.PreviousZhongeUses == 1 && s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Zhonge) &&
            !s.G.GetHumanLegalActions().Any(a => a.CardId is { } id && s.V().Hand!.Any(c => c.Id == id) && a.Kind != LegalActionKind.Recast) &&
            s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Recast && a.CardId is { } id && s.Kind(id) == CardKind.IronChain) &&
            new[] { 0, 1 }.All(seat => s.E<AnniversaryWuFuPhaseGainEvent>().Any(e => e.Phase == arm.Phase && e.Recipient == seat)),
            "Juekai resets exactly Zhonge's phase usage and restricts both actual earlier recipients; real Hand uses disappear while recast remains legal.");
        var hpBeforeRestrictedSlashes = s.V().Hp;
        var retainedDodge = s.V().Hand!.Single(c => c.Kind == CardKind.Dodge).Id;
        s.Zhonge(); s.Stage(AnniversaryWuFuStage.AdjustDiscard);
        s.Answer(c => s.Kind(c.Cards.Single()) == CardKind.Slash); s.Answer(c => s.Kind(c.Cards.Single()) == CardKind.Slash);
        s.Stage(AnniversaryWuFuStage.Target); var second = s.Root!.Id;
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Stage(AnniversaryWuFuStage.Commit); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "slash");
        s.Play();
        var incoming = s.E<AnniversaryWuFuSlashIssuedEvent>().Single(e => e.Origin.FrameId == second && e.Origin.Actor == 1).Origin;
        s.Need(s.V().Hp == hpBeforeRestrictedSlashes - 1 && s.V().Hand!.Any(c => c.Id == retainedDodge) &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceCardUseResolutionId == incoming.UseId && e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            !s.E<CardActionAcceptedEvent>().Any(e => e.Action is { ActorSeat: 0, EffectiveKind: CardKind.Dodge }) &&
            s.E<AnniversaryWuFuSlashReturnedEvent>().Count(e => e.FrameId == second) == 3 && s.E<AnniversaryWuFuJuekaiEvent>().Length == 1 &&
            !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId is Zhonge or Juekai),
            "Hand restrictions permit actual adjustment costs and zero-material mutual Uses, but the native Slash cannot use the retained Hand Dodge: it deals real damage without refunding either activation.");
        var played = new List<int>();
        var hpBeforeDuel = s.V().Hp;
        s.Driver("pure-duel", [1, 0]);
        for (var i = 0; i < 6; i++)
        {
            s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash or DecisionKind.PlayCard });
            if (s.P!.Kind == DecisionKind.PlayCard) break;
            var choice = s.P.Choices.First(c => c.Cards is [var id] && s.Kind(id) == CardKind.Slash);
            played.Add(choice.Cards.Single()); s.Answer(c => c.Id == choice.Id);
        }
        s.Play();
        s.Need(played.Count > 0 && s.V().Hp == hpBeforeDuel && played.All(id =>
            s.E<CardActionAcceptedEvent>().Count(e => e.Action is { ActorSeat: 0, Type: CardActionType.Response, EffectiveKind: CardKind.Slash } a && a.PhysicalCards.Any(c => c.CardId == id)) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To != m.From) == 1 &&
            s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.DiscardPile) &&
            !s.G.GetHumanLegalActions().Any(a => a.CardId is { } id && s.V().Hand!.Any(c => c.Id == id) && a.Kind != LegalActionKind.Recast),
            "In the same armed actual phase, a real native Duel still accepts and pays pure Played Hand Slashes while ordinary Hand Uses remain prohibited.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Driver("draw-one"); s.Play();
        s.Need(s.G.State.TurnNumber > turn && s.E<ActualPhaseEndedEvent>().Any(e => e.Phase == arm.Phase) &&
            s.G.GetHumanLegalActions().Any(a => a.CardId is { } id && s.Kind(id) == CardKind.Slash && a.Kind != LegalActionKind.Recast) &&
            s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Zhonge) && !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Juekai),
            "The exact original Play boundary expires Juekai; a later phase's newly gained Hand can be used and the game-limited activation remains spent.");
    }

    public static void LimitedBeforeZhongeSameChoiceAndLaterRecipient()
    {
        var s = Scene.Start(shortHand: true); s.Play(); s.Juekai(); s.Play();
        var arm = s.E<AnniversaryWuFuJuekaiEvent>().Single();
        s.Need(arm.PreviousZhongeUses == 0 && !s.E<AnniversaryWuFuStartedEvent>().Any() &&
            s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Zhonge) && s.G.GetHumanLegalActions().Any(a => a.CardId is { } id && s.Kind(id) == CardKind.Slash),
            "The limited skill may precede the first Zhonge and does not ban a role that has not actually gained this phase.");
        s.Zhonge(); s.Stage(AnniversaryWuFuStage.Target); var root = s.Root!.Id;
        s.Need(s.V().HandCount == 3 && s.E<AnniversaryWuFuAdjustedEvent>().Single(e => e.FrameId == root) is { Requested: 2, Draw: true },
            "A short actual Hand is adjusted through its original native two-card draw before peer selection.");
        s.Answer(c => c.Targets.SequenceEqual([1])); s.Stage(AnniversaryWuFuStage.Commit); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "draw"); s.Play();
        s.Need(s.E<AnniversaryWuFuDrawReturnedEvent>().Where(e => e.FrameId == root).Select(e => e.Recipient).SequenceEqual([0, 1]) &&
            !s.E<AnniversaryWuFuSlashIssuedEvent>().Any() && s.E<AnniversaryWuFuPhaseGainEvent>().Any(e => e.Phase == arm.Phase && e.Recipient == 1) &&
            !s.G.GetHumanLegalActions().Any(a => a.CardId is { } id && s.V().Hand!.Any(c => c.Id == id) && a.Kind != LegalActionKind.Recast),
            "Same draw commitments execute the item once for each participant; recipients acquired after arming immediately join the exact-phase Hand-use restriction.");
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryWuFu is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardKind Kind(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).CardKind;
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Stage(AnniversaryWuFuStage stage) => Reach(() => Root?.AnniversaryWuFu?.Stage == stage && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "anniversary-wu-fu") == true);
        public void HoldNative() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Zhonge() => Accept(new UseProgramSkillCommand(0, AnniversaryWuFuChecks.Zhonge, "adjust-and-commit", [], [], G.Revision, P!.PromptId));
        public void Juekai() => Accept(new UseProgramSkillCommand(0, AnniversaryWuFuChecks.Juekai, "reset-and-restrict", [], [], G.Revision, P!.PromptId));
        public void Driver(string id, IReadOnlyList<int>? targets = null) => Accept(new UseProgramSkillCommand(0, AnniversaryWuFuChecks.Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "A real current human choice is required."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 160; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                    if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                    { Answer(c => c.Cards.Count == 0); continue; }
                    throw new InvalidOperationException("Unexpected real Wu Fu prompt at " + line + Dump());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Bounded Wu Fu flow did not reach line " + line + Dump());
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold preserves the original private commitments, phase arm, paid draws and native use identity.");
        }
        public void Accept(GameCommand c)
        { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected actual Wu Fu command."); }
        public void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P, Root = Root?.AnniversaryWuFu,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }), Uses = E<AnniversaryWuFuSlashIssuedEvent>(), Returned = E<AnniversaryWuFuSlashReturnedEvent>(), Arms = E<AnniversaryWuFuJuekaiEvent>() });
        public static Scene Start(bool shortHand = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            ShanheStartingCard C(CardKind kind, int rank) => new(kind, Suit.Heart, rank);
            IReadOnlyList<ShanheStartingCard> hand = shortHand ? [C(CardKind.Slash, 2)] : [C(CardKind.Slash, 2), C(CardKind.Dodge, 3), C(CardKind.IronChain, 4), C(CardKind.Peach, 5), C(CardKind.Dodge, 6)];
            var setup = new ShanheBattleSetup(17, 1, "wu-fu-actual-native", Hero, 3, 0, 0, 0, 0, [NoDraw, AnniversaryWuFuChecks.Driver, Watch], hand, [],
                [new(Peer, Hp: 12) { StartingHand = [] }, new(Peer, Hp: 12) { StartingHand = [] }], [new(Peer, Hp: 12) { StartingHand = [] }], new string('a', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-wu-fu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"none","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"draw-one","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},{"id":"pure-duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"native-use","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue-native","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>();
            foreach (var id in new[] { NoDraw, Quiet, Driver }) names[id] = new { name = "伍孚固定原生输入", description = "固定短局及原生摸牌" };
            names[Watch] = new { name = "伍孚原生暂停", description = "真实用牌目标效果前暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "固定同桌", "supporter", NoDraw, "wei", 12, AdditionalSkillIds: [Quiet]));
            const string deck = "fixture:wu-fu-slash-deck";
            b.AddDeck(new(deck, "固定原生杀", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "伍孚真实密选与阶段限制", 4, 4, new Dictionary<string, int>(), deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
