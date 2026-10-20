using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouMengHuoMobileChecks
{
    public static void NativeAttributionChargePhaseAndPaidRecipientCold()
    {
        var s = Scene.Start(); s.Play();
        var original = s.V().Hand.Where(c => c.Kind == CardKind.BarbarianAssault).Select(c => c.Id).ToArray();
        s.Need(original.Length == 2 && s.Charge() == 0, "Current mobile Zaiqi starts at zero of seven, with two real initial Barbarian entities.");
        s.Barbarian(original[0]); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.Amount == 1) == 3 && s.E<MouMengHuoChargeGainedEvent>() is [{ Before: 0, After: 1 }] &&
            s.Charge() == 1 && s.E<MouMengHuoAttributionIssuedEvent>().Length == 0,
            "The native own Barbarian damages three actual targets, gains only one charge in that actual turn, and does not replace its own actor/provider.");
        s.Need(s.V().Hand.Any(c => c.Id == original[1]) && !s.Actions().Any(a => a.Kind == LegalActionKind.BarbarianAssault),
            "A second real Barbarian remains in Hand but native action queries prohibit another Use in this exact Play.");
        s.Reject(new PlayCardCommand(0, original[1], [1, 2, 3], s.G.Revision, s.P!.PromptId));
        s.End(); s.Reach(s.Selecting); s.Choose("toggle", target: 0); s.Choose("confirm"); s.Reach(() => s.Recipient(0)); s.Choose("draw");
        s.Reach(() => s.G.State.TurnNumber >= 5 && s.IsPlay());
        var attributed = s.E<MouMengHuoAttributionIssuedEvent>().Single().Attribution;
        var used = s.E<GroupCardUsedEvent>().Single(e => e.ResolutionId == attributed.UseId);
        s.Need(attributed.OriginalActorSeat == 1 && attributed.Source.OwnerSeat == 0 && used.SourceSeat == 1 &&
            !used.TargetSeats.Contains(0) && s.E<HuoshouAttributedEvent>().Any(e => e.ResolutionId == attributed.UseId) &&
            s.E<MouMengHuoChargeGainedEvent>().Length == 2 && s.Charge() == 1 && s.V().Hp == 4,
            "The genuine friendly NPC independently uses its real Barbarian against both enemies. Huoshou immunity excludes owner zero, while the same native Use keeps actor one and attributes real damage to owner zero, earning only one charge in the foreign turn.");
        var obtained = s.E<MouMengHuoObtainEvent>().Single();
        s.Need(obtained.From == CardLocation.DiscardPile && obtained.To == CardLocation.Hand(0) &&
            s.G.CardMovements.Any(m => m.CardId == obtained.CardId && m.To == CardLocation.DiscardPile && m.CardKind == CardKind.BarbarianAssault && m.Sequence < obtained.Sequence),
            "The next actual Play start randomly obtains a real previously discarded Barbarian, without generating a substitute entity.");
        s.Driver("lose-two"); s.Play(); s.Barbarian(s.V().Hand.First(c => c.Kind == CardKind.BarbarianAssault).Id); s.Play();
        s.Need(s.V().Hp == 2 && s.Charge() == 2 && s.E<MouMengHuoChargeGainedEvent>().Length == 3 &&
            s.E<MouMengHuoBarbarianUsedEvent>().Select(e => e.Phase).Distinct().Count() == 3,
            "A fresh own actual Play resets only its native use quota; genuine caused damage adds one charge, while real HP Loss never creates charge.");
        s.End(); s.Reach(s.Selecting); s.Choose("toggle", target: 0); s.Choose("toggle", target: 1); s.Choose("confirm");
        s.Reach(() => s.Recipient(0)); s.Choose("discard", card: s.V().Equipment.Single(c => c.Kind == CardKind.SilverLion).Id);
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Scene.Watch);
        var frame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouMengHuo is { Obtain: false });
        s.Need(frame.MouMengHuo is { Stage: MouMengHuoStage.DiscardChildren, Cursor: 0, Payment: not null, Accepted: true } && s.Charge() == 0 && s.V().Hp == 3 &&
            s.E<SilverLionRemovedRecoveryEvent>().Any(e => e.PlayerSeat == 0 && e.Reason.Value == "skill-program.mou-mobile:zaiqi.discard" && e.RecoveredAmount == 1 && e.RemainingHp == 3),
            "The whole recipient count is debited once. The real first recipient equipment discard has already drained SilverLion recovery to three and pauses before Zaiqi's separate native recovery.");
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        s.Reach(() => s.E<MouMengHuoCompletedEvent>().Count(e => !e.Obtain && e.Accepted) == 2);
        var spent = s.E<MouMengHuoChargeSpentEvent>().Last(); var payments = s.E<MouMengHuoDiscardPaidEvent>();
        s.Need(spent is { Count: 2, Before: 2, After: 0 } && payments is [{ RecipientSeat: 0 }] &&
            payments[0].Payment.PrintedKind == CardKind.SilverLion && payments[0].Payment.From == CardLocation.Equipment(0) &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.InvocationId == payments[0].Payment.InvocationId && e.ActualCount == 1 && e.HandCount == 0) &&
            s.E<MouMengHuoRecoveryIssuedEvent>() is [{ SourceSeat: 0, TargetSeat: 0, Requested: 1 }] && s.V().Hp == 4 &&
            s.E<MouMengHuoDrawReturnedEvent>().Length == 2 && s.E<MouMengHuoDrawReturnedEvent>().All(e => e.Requested == 1 && e.Actual == 1) &&
            s.E<MouMengHuoRecipientDecidedEvent>().TakeLast(2).Select(e => (e.RecipientSeat, e.Branch)).SequenceEqual(new[] { (0, "discard"), (1, "draw") }) &&
            s.E<MouMengHuoCompletedEvent>().Last(e => !e.Obtain).Recipients == 2,
            "Cold actual equipment-child return pays once and separately recovers with Zaiqi, then the genuine ally NPC independently chooses Draw1 after the owner is full. The original ordered queue completes once.");
    }
    private static ShanheStartingCard C(CardKind k, int rank = 7) => new(k, Suit.Spade, rank);
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public const string Hero = "mou:meng-huo-mobile", Watch = "fixture:moumh-watch";
        private const string DriverId = "fixture:moumh-driver", Quiet = "fixture:moumh-quiet", Ally = "fixture:moumh-ally", EnemyA = "fixture:moumh-enemy-a", EnemyB = "fixture:moumh-enemy-b", Deck = "fixture:moumh-deck";
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int i = 0) => G.CreateSnapshot(i).Players[i];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Charge() => V().Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.MouMengHuoCharge)?.Count ?? 0;
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool IsPlay() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
        public bool Selecting() => P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-meng-huo-zaiqi" && c.Parameters.GetValueOrDefault("branch") == "toggle");
        public bool Recipient(int seat) => P?.PlayerSeat == seat && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-meng-huo-zaiqi" && c.Parameters.GetValueOrDefault("branch") == "draw");
        public void Play() => Reach(IsPlay);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Barbarian(int card)
        { var a = Actions().First(a => a.Kind == LegalActionKind.BarbarianAssault && a.CardId == card); Accept(new PlayCardCommand(0, card, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource }); }
        public void Driver(string id) => Accept(new UseProgramSkillCommand(0, DriverId, id, [], [], G.Revision, P!.PromptId));
        public void Choose(string branch, int? target = null, int? card = null) => Answer(c => c.Parameters.GetValueOrDefault("branch") == branch && (target is null || c.Targets.SequenceEqual([target.Value])) && (card is null || c.Cards.SequenceEqual([card.Value])));
        public void Answer(Func<PromptChoice, bool> match)
        { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only the real human-owned published choice is answered."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected native terminal cutoff at " + line);
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected native human child at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded real fixture did not reach " + line + Diagnostic());
        }
        public void Accept(GameCommand c)
        { try { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Command rejected"); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        public void Reject(GameCommand c)
        { var before = State(); var r = G.Submit(c); Need(!r.Accepted && before == State(), "A prohibited second native Barbarian must reject without changing committed state."); }
        public void Private() => Need(G.CreateSnapshot(1).PendingDecision is null || G.CreateSnapshot(1).PendingDecision!.Choices.Count == 0,
            "Another viewer cannot receive the human's private recipient/child options.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Zones = G.CreateCardZoneDiagnostics(), Moves = G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restore preserves the actual owned charge/payment queue, private prompt and native child."); }
        public void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount, V(i).Markers }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouMengHuo", StringComparison.Ordinal)).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start()
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(7, 1, "moumh-native", Hero, 4, 0, 0, 0, 0, [DriverId, Watch], [C(CardKind.BarbarianAssault, 3), C(CardKind.BarbarianAssault, 4), C(CardKind.Dodge, 5), C(CardKind.Dodge, 6)], [C(CardKind.SilverLion, 9)],
                [new(EnemyA, Hp: 12) { StartingHand = [] }, new(EnemyB, Hp: 12) { StartingHand = [] }], [new(Ally, Hp: 12) { StartingHand = [C(CardKind.BarbarianAssault, 8)] }], new string('b', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); return s;
        }
        private sealed class Fixture : IGameContentPackage
        {
            public PackageManifest Manifest { get; } = new("fixture-mou-meng-huo-native", new(1, 0, 0));
            public void Register(IContentRegistryBuilder b)
            {
                CharacterFixtureContent.Register(b, Hero);
                foreach (var kind in new[] { CardKind.BarbarianAssault, CardKind.Dodge, CardKind.SilverLion }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:moumh-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
                var defs = new object[] {
                    new { id = DriverId, revision = 1, activations = new[] { new { id = "lose-two", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "loseHp", target = "owner", amount = 2 } } } } },
                    new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                    new { id = Watch, revision = 1, triggers = new[] { new { id = "real-recipient-discard", window = "cardsMoved", subject = "owner", sourceZones = new[] { "equipment" }, movementReasons = new[] { "skill-program.mou-mobile:zaiqi.discard" }, movementOccurrence = "perBatch", optional = false,
                        effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "hold", options = new[] { new { id = "continue" } } } } } } }
                };
                var labels = new[] { DriverId, Quiet, Watch }.ToDictionary(id => id, id => new { name = id, description = "谋孟获真实原生小局", optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续实际弃牌后回复" } : new Dictionary<string, string>() });
                var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
                foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, labels[id].description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
                b.AddGeneral(new(Ally, "真实同盟", "", "standard:none", "shu", 12, []));
                foreach (var id in new[] { EnemyA, EnemyB }) b.AddGeneral(new(id, "真实敌方", "", "standard:none", "wei", 12, [Quiet]));
                b.AddDeck(new(Deck, "固定原生库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:moumh-card-Dodge", Suit.Club, 7)).ToArray() });
                b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋孟获原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                    TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            }
        }
    }
}
