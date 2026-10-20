using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;
internal static class WuZhugeLiangChecks
{
    private static ShanheStartingCard C(CardKind k, int rank) => new(k, Suit.Spade, rank);
    public static void CompleteOpeningHpOrderingQingshiBranchesAndCopiedUseCold()
    {
        var s = Scene.Start("three-skills-main-flow", [C(CardKind.Slash, 5), C(CardKind.Slash, 6), C(CardKind.DrawTwo, 5), C(CardKind.DrawTwo, 6), C(CardKind.IronChain, 5), C(CardKind.IronChain, 6)]);
        s.Need(s.R.Generals[Scene.Hero] is { BaseHp: 7, InitialHp: 4 } && s.V().HandCount == 7 &&
            s.E<WuZhugeLiangDrawReturnedEvent>().Single() is { Requested: 1, Actual: 1, Replacement: 0 } &&
            s.E<WuZhugeLiangHpSetEvent>().Single() is { Before: 4, After: 7, SevenCount: 47 } && s.V().Hp == 7 &&
            s.E<RecoveryAppliedEvent>().Length == 0 && s.E<ProgramSkillHpLostEvent>().Length == 0,
            "The real 4/7 general draws only to seven and natively sets HP, without fabricating Recovery or HP Loss.");
        s.Need(s.P is { Kind: DecisionKind.ProgramTopReorder, IsPrivate: true } && (s.G.CreateSnapshot(0).PrivateRevealedCards?.Count ?? 0) == 7 &&
            Enumerable.Range(1, 3).All(v => (s.G.CreateSnapshot(v).PrivateRevealedCards?.Count ?? 0) == 0),
            "Jincui's seven real top cards use the existing private top/bottom ordering UI.");
        s.Cold(); var top = s.P!.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "top"); s.Answer(c => c.Id == top.Id);
        s.Answer(c => c.Parameters.GetValueOrDefault("action") == "finish-top"); s.Order(); s.Play();
        s.Native(s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.Rank == 5).Id, 3); s.Qingshi();
        s.Private(); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "damage" && c.Targets.SequenceEqual([3])); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Single(e => e.SourceSeat == 0 && e.TargetSeat == 3).Amount == 2 &&
            s.E<WuZhugeLiangQingshiAcceptedEvent>().Single().Name == CardKind.Slash,
            "The same-name true Slash can choose one actual target for exactly one additional native damage.");
        s.Native(s.V().Hand.Single(c => c.Kind == CardKind.DrawTwo && c.Rank == 5).Id); s.Qingshi();
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "others"); s.Answer(c => c.Targets.SequenceEqual([1])); s.Answer(c => c.Targets.SequenceEqual([2]));
        var othersFrame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id; s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish"); s.Play();
        s.Need(s.E<WuZhugeLiangDrawReturnedEvent>().Where(e => e.FrameId == othersFrame).Select(e => e.RecipientSeat).SequenceEqual([1, 2]) &&
            s.E<WuZhugeLiangDrawReturnedEvent>().Where(e => e.FrameId == othersFrame).All(e => e.Requested == 1 && e.Actual == 1) &&
            s.E<WuZhugeLiangQingshiDisabledEvent>().Length == 0,
            "The arbitrary other-recipient branch serially completes both real Draw1 requests without disabling Qingshi.");
        var original = s.V().Hand.Single(c => c.Kind == CardKind.DrawTwo && c.Rank == 6).Id;
        s.Copy(original); s.Play(); var copy = s.E<WuZhugeLiangCopyCreatedEvent>().Single();
        s.Need(copy.OriginalCardId == original && copy.CardId != original && copy is { Kind: CardKind.DrawTwo, Suit: Suit.Spade, Rank: 6 } &&
            s.Location(original) == CardLocation.Hand(0) && s.Location(copy.CardId) == CardLocation.Hand(0),
            "Zhizhe leaves the selected original intact and creates one new real entity with identical printed kind, suit and rank.");
        s.Native(copy.CardId); s.Play(); var returned = s.E<WuZhugeLiangCopyReturnedEvent>().Single();
        s.Need(returned.CardId == copy.CardId && s.Location(copy.CardId) == CardLocation.Hand(0) &&
            s.G.CardMovements.Any(m => m.Sequence == returned.ReturnSequence && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.UseFinished) &&
            !s.G.CardMovements.Any(m => m.CardId == copy.CardId && m.To == CardLocation.DiscardPile) &&
            !s.Actions().Any(a => a.CardId == copy.CardId && a.Kind != LegalActionKind.Recast) &&
            s.E<WuZhugeLiangQingshiAcceptedEvent>().Count(e => e.Name == CardKind.DrawTwo) == 1,
            "The copied real Use is replaced directly back to Hand, its physical entity is blocked for this turn, and the same-name Qingshi budget is not refreshed.");
        s.Reject(new PlayCardCommand(0, copy.CardId, [], s.G.Revision, s.P!.PromptId));
        s.Native(s.V().Hand.Single(c => c.Kind == CardKind.IronChain && c.Rank == 5).Id, 0); s.Qingshi();
        var drawFrame = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Last().Id;
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "draw-three"); s.Play();
        s.Need(s.E<WuZhugeLiangDrawReturnedEvent>().Single(e => e.FrameId == drawFrame) is { RecipientSeat: 0, Requested: 3, Actual: 3 } &&
            s.E<WuZhugeLiangQingshiDisabledEvent>().Single().FrameId == drawFrame && s.E<CardRecastEvent>().Length == 0,
            "The third branch really draws three, then disables only Qingshi for the actual turn; the targeted Iron Chain was a Use, not a recast.");
        var turn = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.G.State.TurnNumber != turn && s.Actions().Any(a => a.CardId == copy.CardId && a.Kind == LegalActionKind.DrawTwo) &&
            !s.Actions().Any(a => a.ProgramSkillId == Scene.Zhizhe), "A later actual turn releases the copied entity while the game-limited copying activation stays spent.");
    }

    public static void CopiedPurePlayedNativeDuelReturnAndTurnExpiryCold()
    {
        var s = Scene.Start("copied-real-played", [C(CardKind.Slash, 5), C(CardKind.Slash, 6), C(CardKind.Slash, 8), C(CardKind.Slash, 9), C(CardKind.IronChain, 5), C(CardKind.Peach, 9)], foreignDuel: true);
        s.Order(); s.Play(); var original = s.V().Hand.First(c => c.Kind == CardKind.Slash && c.Rank == 5).Id;
        foreach (var id in s.V().Hand.Where(c => c.Id != original).Select(c => c.Id).ToArray()) { s.Discard(id); s.Play(); }
        s.Copy(original); s.Play(); var copy = s.E<WuZhugeLiangCopyCreatedEvent>().Single(); s.End();
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RespondSlash });
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([copy.CardId])) && s.G.State.CurrentSeat == 2 && s.V().HandCount == 2,
            "The real NPC independently uses its genuine Duel against the only legal target; the copied Slash is a genuine published Played response.");
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([copy.CardId]) && c.Parameters.GetValueOrDefault("response") == "slash");
        s.Reach(() => s.E<WuZhugeLiangCopyReturnedEvent>().Length == 1);
        var returned = s.E<WuZhugeLiangCopyReturnedEvent>().Single();
        s.Need(returned.Reason == CardMoveReasons.ResponseFinished && s.Location(copy.CardId) == CardLocation.Hand(0) &&
            s.E<CardActionAcceptedEvent>().Any(e => e.Action.Type == CardActionType.Response && e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Any(c => c.CardId == copy.CardId)) &&
            s.E<WuZhugeLiangQingshiAcceptedEvent>().Length == 0 && s.V().Hand.Single(c => c.Id == copy.CardId).SkillMarkLabel?.Contains("禁用", StringComparison.Ordinal) == true,
            "A pure Played Slash returns under the real native response-finish invoice, is visibly blocked this turn, and never masquerades as a Qingshi true Use.");
        s.Play(); s.Need(s.G.State.TurnNumber != returned.ActualTurn && s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == copy.CardId),
            "The same actual entity becomes usable again after the foreign actual turn returns.");
    }

    private sealed class Scene(string name, GameEngine game, ContentRegistry registry)
    {
        public const string Hero = "wumiao:zhuge-liang", QingshiSkill = "wumiao:qingshi", Zhizhe = "wumiao:zhizhe";
        private const string Driver = "fixture:wuzgl-driver", Quiet = "fixture:wuzgl-quiet", BanDuel = "fixture:wuzgl-ban-duel", Ally = "fixture:wuzgl-ally", EnemyA = "fixture:wuzgl-enemy-a", EnemyB = "fixture:wuzgl-enemy-b", Deck = "fixture:wuzgl-deck";
        public GameEngine G { get; private set; } = game;
        public ContentRegistry R => registry;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public CardLocation Location(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Native(int id, params int[] targets) => Accept(new PlayCardCommand(0, id, targets, G.Revision, P!.PromptId));
        public void Copy(int id) => Accept(new UseProgramSkillCommand(0, Zhizhe, "copy-hand", [id], [], G.Revision, P!.PromptId));
        public void Discard(int id) => Accept(new UseProgramSkillCommand(0, Driver, "discard-one", [id], [], G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Qingshi()
        { Reach(() => P?.SkillPrompt?.SkillId == QingshiSkill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate")); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wu-zhuge-liang") == true); }
        public void Order()
        { while (P is { Kind: DecisionKind.ProgramTopReorder }) Answer(c => c.Cards.Count == 1); }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "Only a published human choice is answered."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 180; n++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff reaching " + line);
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Kind == DecisionKind.ProgramTopReorder) Order();
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected human boundary " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Bounded native fixture could not reach " + line + " " + Diagnostic());
        }
        public void Private() => Need(P!.IsPrivate && Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null), "The actual chooser alone sees the private choice.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Zones = G.CreateCardZoneDiagnostics(), G.CardMovements,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay retains the real top reorder or response frame and safe snapshots."); }
        public void Reject(GameCommand c) { var before = State(); Need(!G.Submit(c).Accepted && before == State(), "The already-returned entity cannot be used again in its blocked actual turn."); }
        public void Accept(GameCommand c) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected command"); } catch (Exception e) { throw new InvalidOperationException(name + ": " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).MaxHp, V(i).HandCount }), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("WuZhugeLiang", StringComparison.Ordinal)).TakeLast(20).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Moves = G.CardMovements.TakeLast(10) });
        public static Scene Start(string name, ShanheStartingCard[] hand, bool foreignDuel = false)
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(foreignDuel));
            var setup = new ShanheBattleSetup(7, 1, "wuzgl-" + name, Hero, 4, 0, 0, 0, 0, [Driver], hand, [],
                [new(EnemyA, Hp: 12) { StartingHand = foreignDuel ? [C(CardKind.Duel, 8)] : [] }, new(EnemyB, Hp: 12) { StartingHand = [] }],
                [new(Ally, Hp: 12) { StartingHand = [] }], new string('d', 64));
            var options = new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 };
            var s = new Scene(name, GameEngine.CreateStandard(options, r), r); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0 }); return s;
        }
        private sealed class Fixture(bool foreignDuel) : IGameContentPackage
        {
            public PackageManifest Manifest { get; } = new("fixture-wu-zhuge-liang-native", new(1, 0, 0));
            public void Register(IContentRegistryBuilder b)
            {
                CharacterFixtureContent.Register(b, Hero);
                foreach (var kind in new[] { CardKind.Slash, CardKind.Duel, CardKind.DrawTwo, CardKind.IronChain, CardKind.Peach })
                { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:wuzgl-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
                var defs = new object[] {
                    new { id = Driver, revision = 1, activations = new[] { new { id = "discard-one", minCards = 1, maxCards = 1, sourceZones = new[] { "hand" }, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                        effects = new[] { new { op = "discardSelected", target = "owner", amount = 1 } } } }, modifiers = new[] { new { id = "keep", query = "handLimit", operation = "add", value = 30, priority = 0 } } },
                    new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                    new { id = BanDuel, revision = 1, cardPolicies = new[] { new { id = "not-a-duel-target", kind = "prohibitTarget", cardKinds = new[] { "duel" } } } }
                };
                var labels = new[] { Driver, Quiet, BanDuel }.ToDictionary(id => id, id => new { name = id, description = "武诸葛亮真实主流程夹具" });
                var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
                foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, labels[id].description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
                b.AddGeneral(new(Ally, "真实同行", "", "standard:none", "shu", 12, foreignDuel ? [Quiet, BanDuel] : [Quiet]));
                b.AddGeneral(new(EnemyA, "真实敌军", "", "standard:none", "wei", 12, foreignDuel ? [] : [Quiet]));
                b.AddGeneral(new(EnemyB, "真实敌军", "", "standard:none", "wei", 12, foreignDuel ? [Quiet, BanDuel] : [Quiet]));
                b.AddDeck(new(Deck, "固定七点实体库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:wuzgl-card-Slash", Suit.Spade, 7)).ToArray() });
                b.AddMode(new(ShanheBattleSetup.BattleModeId, "武诸葛亮原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, EnemyA, EnemyB],
                    ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            }
        }
    }
}
