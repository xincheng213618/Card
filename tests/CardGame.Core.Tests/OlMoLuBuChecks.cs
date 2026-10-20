using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class OlMoLuBuChecks
{
    private const string Duoqi = "ol:mo-duoqi", Kuangmo = "ol:mo-kuangmo", Gangquan = "ol:mo-gangquan",
        Driver = "fixture:mlb-driver", Quiet = "fixture:mlb-quiet", Owner = "fixture:mlb-owner",
        Enemy = "fixture:mlb-enemy", Far = "fixture:mlb-far", Ally = "fixture:mlb-ally", Deck = "fixture:mlb-deck";
    private static ShanheStartingCard C(CardKind kind) => new(kind, Suit.Spade, 7);

    public static void NativeOpeningQiGangquanReciprocalKuangAndDeathReturn()
    {
        var s = Scene.Start(); var opening = s.E<MoLuBuOpeningStartedEvent>().Single();
        var original = s.E<MoLuBuQiMarkedEvent>();
        s.Need(opening.ActualTurn == 1 && s.E<RoundStartedEvent>().Length == 0 && s.G.State.TurnNumber == 1 && original.Length == 7 &&
            original.Select(e => e.CardId).Distinct().Count() == 7 && s.E<TurnStartedEvent>() is [{ ActorSeat: 0 }] &&
            s.E<ActualDiscardRecoveryPhaseStartedEvent>().Select(e => e.Phase.Kind).SequenceEqual([ActualDiscardRecoveryPhaseKind.Play]),
            "Duoqi starts one real extra actual turn with Play only, before the original first seat and without normal Preparation or Draw.");
        s.Need(!s.Actions().Any(a => a.PlayedCardKind == CardKind.Indulgence) && s.Actions().Any(a => a.ConversionSource?.SkillId == Gangquan && a.PlayedCardKind == CardKind.Duel),
            "The opening forbids a real delayed trick while the initial non-Qi prefix offers the complete trick-to-Duel branch.");
        s.Accept(new UseProgramSkillCommand(0, Kuangmo, "enter-and-pair", [], [1], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Need(s.E<OlMoRumoEnteredEvent>() is [{ Origin.SkillId: Kuangmo, EntryRound: 0 }] &&
            Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players.Where(p => p.Seat is 0 or 1).All(p => p.Markers?.Count(m => m.Kind == PlayerMarkerKind.OlMoKuang && m.Count == 1) == 1)),
            "The accepted opening activation issues permanent native Rumo and a public reciprocal pair without a new printed skill or a repeat entry.");
        var duel = s.Actions().First(a => a.ConversionSource?.SkillId == Gangquan && a.PlayedCardKind == CardKind.Duel && a.TargetSeats.SequenceEqual([1]));
        s.Card(duel); s.Reach(s.QiChoice); var first = s.E<MoLuBuStartedEvent>().Single(e => e.Operation == SkillProgramEffectOp.ResolveMoLuBuDuoqi && e.OriginalOwnerSeat == 1);
        s.Need(s.V(1).Hp == 6 && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 2) == 1 &&
            s.P!.Choices.All(c => c.Cards.Count == 0),
            "The first genuine reciprocal damage gains exactly one, and original-owner hidden Qi is selected through opaque canonical choices.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "qi"); s.Play();
        s.Need(s.E<MoLuBuCompletedEvent>().Count(e => e.FrameId == first.FrameId) == 1 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == s.E<CardUseDeclaredEvent>().Single(e => e.CardId == duel.CardId).ResolutionId) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == duel.CardId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "Cold restoration returns the original paid Duel and Qi gain exactly once.");
        var fire = s.Actions().Single(a => a.ConversionSource?.SkillId == Gangquan && a.PlayedCardKind == CardKind.FireSlash && a.CardId == s.V().Hand.Single(c => c.Kind == CardKind.Crossbow).Id);
        s.Need(fire.TargetSeats.SequenceEqual([1, 3]) && !s.Actions().Any(a => a.ConversionSource?.SkillId == Gangquan && a.PlayedCardKind == CardKind.Duel),
            "Using a physical Qi changes Gangquan to one equipment Fire Slash with every legal living seat neighbor, including the ally but excluding the far seat.");
        s.Card(fire); s.Reach(s.QiChoice); s.Answer(c => c.Parameters.GetValueOrDefault("verb") == "qi"); s.Play();
        var fireUse = s.E<CardUseDeclaredEvent>().Single(e => e.CardId == fire.CardId);
        s.Need(s.V(1).Hp == 5 && s.V(3).Hp == 7 && s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == fireUse.ResolutionId) == 1 &&
            s.E<MoLuBuStartedEvent>().Count(e => e.Operation == SkillProgramEffectOp.ResolveMoLuBuDuoqi && e.OriginalOwnerSeat == 1) == 1 &&
            !s.Actions().Any(a => a.ConversionSource?.SkillId == Gangquan && a.PlayedCardKind == CardKind.FireSlash),
            "All targets settle on the same native Fire Slash, the later mutual damage has no second bonus, and ordinary Slash quota and per-victim turn limits remain effective.");
        s.Accept(new UseProgramSkillCommand(0, Driver, "lethal-damage", [], [1], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("verb") == "pair") == true);
        var loot = s.E<MoLuBuStartedEvent>().Single(e => e.Operation == SkillProgramEffectOp.ResolveMoLuBuKuangmoDeath);
        var victimQi = original.Where(e => e.OriginalOwnerSeat == 1).Select(e => e.CardId).ToArray();
        s.Need(victimQi.Length == 2 && !s.V(1).IsAlive && victimQi.All(id => s.V().Hand.Any(c => c.Id == id)) &&
            s.E<MoLuBuQiMovedEvent>().Count(e => e.FrameId == loot.FrameId && e.From == CardLocation.DiscardPile && e.To == CardLocation.Hand(0)) == 1,
            "A genuine credited defeat retrieves every remaining original victim Qi from its post-death discard zone, preserving earlier obtained entities and the real killer attribution. " +
            JsonSerializer.Serialize(new { VictimQi = victimQi, LatestZones = victimQi.Select(id => s.G.CardMovements.Last(m => m.CardId == id)), LootMoves = s.E<MoLuBuQiMovedEvent>().Where(e => e.FrameId == loot.FrameId), Deaths = s.E<PlayerDiedEvent>() }));
        s.Answer(c => c.Targets.SequenceEqual([2])); s.Play();
        s.Need(s.E<MoLuBuPairChangedEvent>().Select(e => e.OpponentSeat).SequenceEqual([1, 2]) && s.E<MoLuBuCompletedEvent>().Count(e => e.FrameId == loot.FrameId) == 1,
            "Death first returns the whole original Qi roster and then mandatorily reassigns the surviving entered owner's opponent once.");
        s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.G.State.TurnNumber > opening.ActualTurn && s.P is { Kind: DecisionKind.PlayCard });
        s.Need(s.E<MoLuBuOpeningCompletedEvent>() is [{ NormalSeat: 0 }] && s.E<ActualTurnCursorReturnedEvent>().Count(e => e.ActualTurn == opening.ActualTurn) == 1 &&
            s.E<ActualDiscardRecoveryPhaseStartedEvent>().Where(e => e.Phase.TurnNumber == opening.ActualTurn).Select(e => e.Phase.Kind).SequenceEqual([ActualDiscardRecoveryPhaseKind.Play]) &&
            s.E<TurnStartedEvent>().Take(2).Select(e => e.ActorSeat).SequenceEqual([0, 0]) && s.E<RoundStartedEvent>().Last().RoundNumber == 1,
            "The extra Play drains its native phase end and actual-turn cursor exactly once, skips Discard and Ending, and returns the original normal first seat.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ol-mo-lu-bu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            string Read(string suffix, string bundle = "ol-mo-lu-bu") { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing actual OL magic bundle."); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var real = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            foreach (var id in new[] { Duoqi, Kuangmo, Gangquan }) b.AddSkill(new(id, real.Presentations[id].Name, real.Presentations[id].Description) { Program = real.Programs[id], ProgramPresentation = real.Presentations[id] });
            var derived = SkillProgramCatalog.Load(Read("rules", "ol-mo-sun-quan"), Read("presentation", "ol-mo-sun-quan"));
            b.AddSkill(new("ol:mo-rumo", derived.Presentations["ol:mo-rumo"].Name, derived.Presentations["ol:mo-rumo"].Description)
                { Program = derived.Programs["ol:mo-rumo"], ProgramPresentation = derived.Presentations["ol:mo-rumo"] });
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"activations":[{"id":"lethal-damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damageParticipants","target":"selectedTarget","amount":10}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Driver] = new { name = "原生伤害", description = "真实伤害" }, [Quiet] = new { name = "静默", description = "跳过无关出牌" } } }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "固定原生小局") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            foreach (var kind in new[] { CardKind.Indulgence, CardKind.Crossbow, CardKind.QinglongCrescentBlade }) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mlb-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            b.AddGeneral(new(Owner, "魔吕布原生主流程", "", "standard:none", "qun", 4, [Duoqi, Kuangmo, Gangquan, Driver]));
            foreach (var id in new[] { Enemy, Far, Ally }) b.AddGeneral(new(id, id, "", "standard:none", "qun", 8, [Quiet]));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 40).Select(_ => new ContentDeckPhysicalCard("fixture:mlb-card-" + CardKind.Indulgence, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "魔吕布原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Enemy, Far, Ally], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine engine, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = engine;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public IReadOnlyList<LegalAction> Actions() => G.GetHumanLegalActions();
        public bool QiChoice() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mo-lu-bu" && c.Parameters.GetValueOrDefault("verb") == "qi") == true;
        public void Card(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Reach(Func<bool> condition)
        {
            for (var i = 0; i < 150; i++)
            {
                if (condition()) return;
                Need(G.State.Winner == Winner.None, "Unexpected native terminal cutoff.");
                if (P is null) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected native Mo Lu Bu prompt: " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("The bounded Mo Lu Bu native boundary was not reached.");
        }
        public void Cold()
        {
            var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); var moves = JsonSerializer.Serialize(G.CardMovements);
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)) && moves == JsonSerializer.Serialize(G.CardMovements), "The owning Qi selection and native damage return survive a real cold restore unchanged.");
        }
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, "Rejected native " + command.GetType().Name + ": " + result.Error?.Message); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.Phase, G.State.Winner, P, Rounds = E<RoundStartedEvent>(), Hp = V().Hp })); }
        public static Scene Start()
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(83, 1, "mlb-native", Owner, 4, 0, 0, 0, 0, [], [C(CardKind.Indulgence), C(CardKind.Crossbow), C(CardKind.QinglongCrescentBlade)], [],
                [new(Enemy) { StartingHand = [C(CardKind.Indulgence)] }, new(Far) { StartingHand = [C(CardKind.Indulgence)] }], [new(Ally) { StartingHand = [C(CardKind.Indulgence), C(CardKind.Indulgence)] }], new string('d', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
                ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8, AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }
}
