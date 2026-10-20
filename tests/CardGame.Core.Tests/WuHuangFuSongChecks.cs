using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class WuHuangFuSongChecks
{
    private static ShanheStartingCard C(CardKind kind, int rank) => new(kind, Suit.Spade, rank);

    public static void ChaozhenRealMinimumFirstTurnDyingRecoveryAndCold()
    {
        var s = Scene.Start("public-field-ace", [C(CardKind.Peach, 7)], [C(CardKind.StoneAxe, 1)]);
        s.Need(s.V().Hp == 1 && s.V().MaxHp == 4 && s.Registry.Generals[Scene.Hero].InitialHp == 1,
            "The complete real general starts at one of four HP, without replacing his definition with a full-health fixture.");
        s.Offer(Scene.Chaozhen); s.Activate(); s.Reach(s.OwnChoice); s.Private(); s.RejectPrompt(); s.Cold();
        var axe = s.V().Equipment.Single().Id;
        s.Answer(c => c.Parameters.GetValueOrDefault("selection") == "field" && c.Cards.SequenceEqual([axe])); s.Play();
        var obtained = s.E<WuHuangFuSongMovedEvent>().Single();
        s.Need(obtained is { Rank: 1 } && obtained.CardId == axe && obtained.From == CardLocation.Equipment(0) && obtained.To == CardLocation.Hand(0) &&
            s.G.CardMovements.Count(m => m.Sequence == obtained.Sequence && m.CardId == axe && m.Reason.Value == "skill-program.wumiao:chaozhen.obtain") == 1 &&
            s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1 && s.V().Hp == 2 &&
            s.E<WuHuangFuSongChaozhenDisabledEvent>().Length == 1 && s.E<WuHuangFuSongDrawReturnedEvent>().Length == 0,
            "The public minimum is really obtained from Equipment and then natively recovers one; obtaining is not fabricated as a draw.");
        var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
        s.Use("loss-two"); s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }); s.Private(); s.Cold();
        s.Need(s.V().Hp == 0 && s.E<WuHuangFuSongStartedEvent>().Length == 1,
            "The Ace disables Chaozhen in a later genuine Dying entry in the same actual turn.");
        s.Answer(c => c.Cards.SequenceEqual([peach]) && c.Parameters.GetValueOrDefault("response") == "peach"); s.Play();
        s.Need(s.V().Hp == 1 && s.E<DyingResolvedEvent>().Any(e => e.VictimSeat == 0 && e.Survived) &&
            s.E<WuHuangFuSongMovedEvent>().Length == 1 && s.E<WuHuangFuSongCompletedEvent>().Count(e => e.FrameId == obtained.FrameId) == 1,
            "The once-paid obtain survives native Dying/Peach children without being issued again.");

        var dying = Scene.Start("first-turn-dying-deck-ace", [], [], aceDeck: true);
        dying.Play(); dying.Use("loss-one"); dying.Offer(Scene.Chaozhen); dying.Activate(); dying.Reach(dying.OwnChoice);
        dying.Need(dying.V().Hp == 0 && dying.E<WuHuangFuSongStartedEvent>().Single().Operation == SkillProgramEffectOp.ResolveWuHuangFuSongChaozhen &&
            dying.P!.Choices.All(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("selection") == "deck"),
            "The first actual turn's native Dying entry can offer a concealed deck minimum without exposing its card face.");
        dying.Private(); dying.Cold(); dying.Answer(c => c.Parameters.GetValueOrDefault("selection") == "deck"); dying.Play();
        dying.Need(dying.E<WuHuangFuSongMovedEvent>().Single() is { Rank: 1, From: var from, To: var to } && from == CardLocation.DrawPile && to == CardLocation.Hand(0) &&
            dying.E<RecoveryAppliedEvent>().Single(e => e.TargetSeat == 0).Amount == 1 && dying.V().Hp == 1 &&
            dying.E<DyingResolvedEvent>().Any(e => e.VictimSeat == 0 && e.Survived) && dying.E<WuHuangFuSongDrawReturnedEvent>().Length == 0,
            "A real deck-Ace obtain and native recovery save the owner on the first turn, retaining their exact typed parent through cold replay.");
    }

    public static void LianjieActualDrawPrivilegesJiangxianDamageAndEndLossCold()
    {
        var s = Scene.Start("lianjie-jiangxian-main-flow", [C(CardKind.RenwangShield, 1), C(CardKind.Slash, 7)], [], watchDraw: true);
        s.Play(); var armor = s.V().Hand.Single(c => c.Kind == CardKind.RenwangShield).Id;
        s.Native(armor); s.Offer(Scene.Lianjie);
        var admitted = s.E<WuHuangFuSongUseAdmittedEvent>().Last().Receipt;
        s.Need(admitted is { ActorSeat: 0, ProviderSeat: 0, EffectiveRank: 1, HandUse: true, MaterialCount: 1 },
            "An isolated complete hero bundle freezes the real Hand equipment Use's rank without relying on another character's rank capability.");
        s.Activate(); s.Reach(s.OwnChoice); s.Answer(c => c.Targets.SequenceEqual([1])); s.Reach(() => s.OwnChoice() && s.P!.Choices.Any(c => c.Parameters.ContainsKey("slot-index")));
        s.Private(); s.Cold(); s.Answer(c => c.Parameters.ContainsKey("slot-index")); s.Reach(s.Hold);
        var paid = s.E<WuHuangFuSongBottomPaidEvent>().Single(); var draw = s.E<WuHuangFuSongDrawReturnedEvent>().Single();
        var marks = s.E<WuHuangFuSongLianjieEntityEvent>();
        s.Need(paid is { OwnerSeat: 0, TargetSeat: 1, Rank: 3 } && s.Location(paid.CardId) == CardLocation.DrawPile &&
            s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == paid.CardId).ZoneIndex == 0 &&
            draw is { Requested: 3, Actual: 3, Replacement: 0 } && marks.Length == 3 && marks.All(e => e.FrameId == draw.FrameId &&
                s.G.CardMovements.Any(m => m.Sequence == e.DrawSequence && m.CardId == e.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0))) &&
            marks.All(e => s.V().Hand.Single(c => c.Id == e.CardId).SkillMarkLabel?.Contains("连捷", StringComparison.Ordinal) == true) &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == draw.FrameId).WuHuangFuSong is { Stage: WuHuangFuSongStage.DrawChildren },
            "The victim's actual minimum goes to the real bottom; three real draws are marked before their native gain child can use them.");
        s.Private(); s.Cold(); s.Continue(); s.Play();
        var ordinary = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.Rank == 7).Id;
        s.Native(ordinary, 3); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.Amount == 1) == 1 &&
            s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0) == 1,
            "One genuine unmarked Slash damages an adjacent target and spends the ordinary quota before the issued privilege is needed.");
        s.Accept(new UseProgramSkillCommand(0, Scene.Jiangxian, "arm-turn", [], [], s.G.Revision, s.P!.PromptId)); s.Play();
        var marked = marks[0].CardId;
        s.Need(s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId == marked && a.TargetSeats.Contains(2)),
            "The actual Lianjie entity remains natively legal against the distance-two target after the ordinary Slash quota is spent.");
        s.Native(marked, 2); s.Play();
        s.Need(s.E<DamageAppliedEvent>().Single(e => e.SourceSeat == 0 && e.TargetSeat == 2).Amount == 2 &&
            s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.ActorSeat == 0) == 1 &&
            s.E<WuHuangFuSongUseAdmittedEvent>().Any(e => e.Receipt is { LianjieMaterials: true, HandUse: true, ActorSeat: 0, ProviderSeat: 0 }) &&
            s.E<WuHuangFuSongJiangxianArmedEvent>().Length == 1,
            "Jiangxian adds the one prior actual damage occurrence; the marked real Use bypasses distance/quota without spending another ordinary use.");
        var turn = s.G.State.TurnNumber;
        s.End(); s.Reach(() => s.OwnChoice() && s.P!.Choices.Any(c => c.Parameters.ContainsKey("lose-skill"))); s.Private(); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("lose-skill") == Scene.Lianjie);
        s.Reach(() => s.E<WuHuangFuSongJiangxianSettledEvent>().Length == 1); s.Play();
        var remaining = marks.Where(e => e.CardId != marked).Select(e => e.CardId).ToArray();
        s.Need(s.G.State.TurnNumber != turn && !s.Has(Scene.Lianjie) && s.Has(Scene.Chaozhen) &&
            s.E<WuHuangFuSongSkillRemovedEvent>().Any(e => e.SkillId == Scene.Lianjie) &&
            s.E<ActualTurnCursorReturnedEvent>().Any(e => e.ActualTurn == turn && e.OwnerSeat == 0) &&
            !s.Actions().Any(a => a.ProgramSkillId == Scene.Jiangxian) &&
            !s.Actions().Any(a => a.Kind == LegalActionKind.Slash && a.CardId is { } id && remaining.Contains(id) && a.TargetSeats.Contains(2)),
            "The paid turn-end choice really removes Lianjie, game-limited Jiangxian stays spent, and unspent entities lose their distance privilege at actual-turn return.");
    }

    private sealed class Scene(string name, GameEngine game, ContentRegistry registry)
    {
        public const string Hero = "wumiao:huangfu-song", Chaozhen = "wumiao:chaozhen", Lianjie = "wumiao:lianjie", Jiangxian = "wumiao:jiangxian";
        private const string Driver = "fixture:wuhf-driver", Quiet = "fixture:wuhf-quiet", Watch = "fixture:wuhf-watch", Ally = "fixture:wuhf-ally", EnemyA = "fixture:wuhf-enemy-a", EnemyB = "fixture:wuhf-enemy-b", Deck = "fixture:wuhf-deck";
        public GameEngine G { get; private set; } = game;
        public ContentRegistry Registry => registry;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public LegalAction[] Actions() => G.GetHumanLegalActions().ToArray();
        public bool Has(string skill) => V().Skills?.Any(s => s.ContentId == skill) == true;
        public CardLocation Location(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public bool OwnChoice() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wu-huangfu-song") == true;
        public bool Hold() => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Offer(string skill) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Use(string id) => Accept(new UseProgramSkillCommand(0, Driver, id, [], [], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Native(int card, params int[] targets) => Accept(new PlayCardCommand(0, card, targets, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(p.PlayerSeat == 0 && c is not null, "Only a real published human choice may be answered."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 180; n++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff while reaching " + line);
                var p = P;
                if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (Hold()) Continue();
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected native prompt at " + line + " " + Diagnostic());
            }
            throw new InvalidOperationException("Bounded fixture could not reach " + line + " " + Diagnostic());
        }
        public void Private() => Need(P!.IsPrivate && Enumerable.Range(1, 3).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(p => p.Seat != v).All(p => p.Hand.Count == 0)), "The human choice is private and foreign Hands remain opaque.");
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Zones = G.CreateCardZoneDiagnostics(), G.CardMovements,
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold replay retains safe views, exact frame receipts, movement and commands."); }
        public void RejectPrompt()
        { var p = P!; Reject(new AnswerPromptCommand(1, p.PromptId, p.Choices[0].Id, G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-wuhf"), G.Revision)); Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1)); }
        private void Reject(GameCommand c) { var before = State(); Need(!G.Submit(c).Accepted && before == State(), "An invalid actor, unpublished choice or stale revision cannot mutate the command session."); }
        public void Accept(GameCommand c)
        { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected command"); } catch (Exception e) { throw new InvalidOperationException(name + ": " + e.Message + " " + Diagnostic(), e); } }
        public void Need(bool value, string why) { if (!value) throw new InvalidOperationException(why + " " + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P,
            Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).MaxHp, V(i).HandCount, V(i).IsAlive }),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("WuHuangFuSong", StringComparison.Ordinal)).TakeLast(15).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Moves = G.CardMovements.TakeLast(12) });
        public static Scene Start(string name, ShanheStartingCard[] hand, ShanheStartingCard[] equipment, bool aceDeck = false, bool watchDraw = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(aceDeck, watchDraw));
            var setup = new ShanheBattleSetup(7, 1, "wuhf-" + name, Hero, 1, 0, 0, 0, 0, watchDraw ? [Driver, Watch] : [Driver], hand, equipment,
                [new(EnemyA, Hp: 12) { StartingHand = [] }, new(EnemyB, Hp: 12) { StartingHand = [] }],
                [new(Ally, Hp: 12) { StartingHand = [C(CardKind.Dodge, 3), C(CardKind.Dodge, 9)] }], new string('b', 64));
            var options = new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 };
            var s = new Scene(name, GameEngine.CreateStandard(options, registry), registry); s.Accept(new StartGameCommand()); s.Reach(() => s.P is { PlayerSeat: 0 }); return s;
        }
        private sealed class Fixture(bool aceDeck, bool watchDraw) : IGameContentPackage
        {
            public PackageManifest Manifest { get; } = new("fixture-wu-huangfu-song-native", new(1, 0, 0));
            public void Register(IContentRegistryBuilder b)
            {
                CharacterFixtureContent.Register(b, Hero);
                foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.Peach, CardKind.StoneAxe, CardKind.RenwangShield })
                { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:wuhf-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
                var definitions = new List<object>
                {
                    new { id = Driver, revision = 1, activations = new[] { Activation("loss-one", 1), Activation("loss-two", 2) },
                        modifiers = new[] { new { id = "keep", query = "handLimit", operation = "add", value = 30, priority = 0 } } },
                    new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                        effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
                };
                if (watchDraw) definitions.Add(new { id = Watch, revision = 1, triggers = new[] { new { id = "actual-draw-child", window = "cardsGained", subject = "owner",
                    destinationZones = new[] { "hand" }, movementOccurrence = "perBatch", movementReasons = new[] { "skill-program.wumiao:lianjie.draw" }, usageScope = "game", usageLimit = 1, priority = 1000, optional = false,
                    effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "held", options = new[] { new { id = "continue" } } } } } } });
                var labels = definitions.Select(d => JsonSerializer.SerializeToElement(d).GetProperty("id").GetString()!).ToDictionary(id => id,
                    id => new { name = id, description = "武皇甫嵩原生主流程检查", optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
                var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                    JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
                foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, labels[id].description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
                foreach (var id in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(id, "真实其他角色", "", "standard:none", "wu", 12, [Quiet]));
                b.AddDeck(new(Deck, "固定真实小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:wuhf-card-" + (aceDeck ? CardKind.Dodge : CardKind.Slash), Suit.Spade, aceDeck ? 1 : 2)).ToArray() });
                b.AddMode(new(ShanheBattleSetup.BattleModeId, "武皇甫嵩原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                    GeneralPoolIds: [Hero, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                    TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            }
            private static object Activation(string id, int amount) => new { id, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0,
                targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "loseHp", target = "owner", amount } } };
        }
    }
}
