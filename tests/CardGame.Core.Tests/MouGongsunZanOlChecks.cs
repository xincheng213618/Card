using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouGongsunZanOlChecks
{
    private const string Hero = "mou:gongsun-zan-ol", Baojing = "mou-ol:baojing", Driver = "fixture:mgz-driver", Quiet = "fixture:mgz-quiet", Range = "fixture:mgz-range";
    public static void EqualRangeNativeSlashAndNextActualPlayExpiryCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.V().Hp == 4 && s.G.GetAttackRange(0) == 4 && s.V().Equipment.Any(c => c.Kind == CardKind.QilinBow) && s.G.GetAttackRange(2) == 4,
            "Jiaodi's final current-HP range is four despite the real range-five Qilin Bow; the other participant has an ordinary native range-four Set.");
        s.Baojing(1); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "increase"); s.Play();
        s.Need(s.G.GetAttackRange(1) == 2 && s.V(1).ActiveEffects?.Any(e => e.SkillId == Baojing) == true && !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == Baojing),
            "Baojing genuinely grants another character +1, publicly projects the issued duration, and consumes the actual Play once.");
        var original = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
        var action = s.G.GetHumanLegalActions().Single(a => a.CardId == original && a.TargetSeats.SequenceEqual([2]));
        s.Accept(new PlayCardCommand(0, original, action.TargetSeats, s.G.Revision, s.P!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        s.Reach(() => s.Branch("obtain"));
        var started = s.E<MouGongsunZanStartedEvent>().Single();
        s.Need(started is { OwnerRange: 4, TargetRange: 4, OriginalTarget: 2 } && s.E<MouGongsunZanDamageGrantedEvent>().Single() is { Before: 1, After: 2 },
            "The original native sole-target designation freezes equal public ranges and first grants +1 to this whole Slash.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "obtain" && c.Parameters.GetValueOrDefault("slot-index") == "0");
        s.Reach(() => s.Branch("discard")); s.Cold();
        s.Need(s.E<MouGongsunZanMovedEvent>().Count(e => e.FrameId == started.FrameId && e.Purpose == "obtain" && e.From == CardLocation.Hand(2) && e.To == CardLocation.Hand(0)) == 1,
            "Jiaodi actually obtains one original concealed Hand entity before continuing; Cold retains that paid movement once.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard" && c.Parameters.GetValueOrDefault("source-zone") == "Hand" && c.Parameters.GetValueOrDefault("slot-index") == "0");
        s.Reach(() => s.Branch("extra-target"));
        s.Need(s.P!.Choices.All(c => c.Parameters.GetValueOrDefault("branch") == "extra-target") && s.P.Choices.Any(c => c.Targets.SequenceEqual([3])),
            "The equal comparison also really discards the target's remaining region card, then requires one legal extra target without an invented refusal.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "extra-target" && c.Targets.SequenceEqual([3])); s.Play();
        s.Need(s.E<MouGongsunZanExtraTargetEvent>().Single() is { TargetSeat: 3 } && s.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == started.UseId && e.CardId == original) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == started.UseId) == 1 && s.E<MouGongsunZanCompletedEvent>().Count(e => e.FrameId == started.FrameId) == 1 &&
            s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat is 2 or 3 && e.Amount == 2) == 2 && s.V(2).Hp == 10 && s.V(3).Hp == 10 &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ExecutorSeat == 0 && e.SubjectSeat == 2 && e.ActualCount == 1 && e.Reason.Value == "skill-program.mou-ol:jiaodi.discard"),
            "The original physical Slash deals two to both native targets and finishes once. Actual obtain, logical discard and extra-target choice do not forge another Use or collapse equal-range branches.");

        s.End(); s.Reach(() => s.IsPlay() && s.E<ProgramPhaseScheduledEvent>().Any(e => e.SkillId == "ol:dianjun" && e.OwnerSeat == 0 && e.Phase == TurnPhase.Play && e.Started));
        s.Need(s.G.State.TurnNumber == 1 && s.V().Hp == 3 && s.G.GetAttackRange(0) == 3 && s.G.GetAttackRange(1) == 1 && s.V(1).ActiveEffects?.Any(e => e.SkillId == Baojing) != true,
            "Real Dianjun damages the owner and inserts a later Play after raw TurnEnded in the same actual turn: HP immediately sets range three and that actual Play start expires Baojing.");
        s.Baojing(2); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "decrease"); s.Play();
        s.Need(s.G.GetAttackRange(2) == 3 && s.E<MouGongsunZanBaojingGrantedEvent>().Select(e => e.Amount).SequenceEqual([1, -1]),
            "A new real Play has a new Baojing budget, and its negative grant changes the native range-four recipient to three.");
        s.Accept(new UseProgramSkillCommand(0, Driver, "lose-three", [], [], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying });
        s.Need(s.V().Hp == 0 && s.G.GetAttackRange(0) == 0, "The final HP override permits actual zero during native dying instead of the ordinary attack-range lower bound one.");
        var peach = s.V().Hand.Single(c => c.Kind == CardKind.Peach).Id;
        s.Answer(c => c.Cards.Contains(peach)); s.Play();
        s.Need(s.V().Hp == 1 && s.G.GetAttackRange(0) == 1, "A real self-rescue immediately restores the HP-derived range without changing the printed range-five weapon.");
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int i = 0) => G.CreateSnapshot(i).Players[i];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool IsPlay() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
        public bool Branch(string branch) => P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == branch);
        public void Play() => Reach(IsPlay);
        public void Baojing(int target) => Accept(new UseProgramSkillCommand(0, MouGongsunZanOlChecks.Baojing, "adjust-range", [], [target], G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var c = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && c is not null, "Only an actual published human choice is submitted."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision)); }
        public void Accept(GameCommand command) { try { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Rejected native command."); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var n = 0; n < 220; n++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected terminal cutoff at " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else throw new InvalidOperationException("Unexpected real prompt at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded real Gongsun Zan flow not reached at " + line + Diagnostic());
        }
        public void Cold() { var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "The accepted original Slash range comparisons and already-obtained entity survive Cold without another payment."); }
        public void Need(bool result, string message) { if (!result) throw new InvalidOperationException(message + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Own = V(), Actions = IsPlay() ? G.GetHumanLegalActions().ToArray() : [],
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouGongsunZan", StringComparison.Ordinal) || e.Payload is DamageAppliedEvent or TurnEndedEvent or ProgramPhaseScheduledEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start()
        {
            var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(7, 1, "mougsz-real", Hero, 4, 0, 0, 0, 0, [Driver, "ol:dianjun"], [new(CardKind.Slash, Suit.Heart, 7), new(CardKind.Peach, Suit.Heart, 7)], [new(CardKind.QilinBow, Suit.Club, 7)],
                [new("fixture:mgz-enemy-a", Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Peach, Suit.Heart, 7)] }, new("fixture:mgz-enemy-b", Hp: 12) { StartingHand = [] }],
                [new("fixture:mgz-ally", Hp: 12) { StartingHand = [] }], new string('d', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, r);
            var s = new Scene(g, r); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-gongsun-zan-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, [Hero, "ol:zhang-yi"]);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Slash, CardKind.Peach, CardKind.QilinBow }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mgz-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var defs = new object[] {
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Range, revision = 1, modifiers = new[] { new { id = "range-four", query = "attackRange", operation = "set", value = 4, priority = 10 } } },
                new { id = Driver, revision = 1, activations = new[] { new { id = "lose-three", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "loseHp", target = "owner", amount = 3 } } } } }
            };
            var labels = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "原生跳过出牌阶段" }, [Range] = new { name = "四尺范围", description = "原生设置攻击范围为四" }, [Driver] = new { name = "实际流失", description = "实际失去三点体力", activationLabels = new Dictionary<string, string> { ["lose-three"] = "实际流失三点体力" } } };
            var cata = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in cata.Programs) b.AddSkill(new(id, cata.Presentations[id].Name, cata.Presentations[id].Description) { Program = p, ProgramPresentation = cata.Presentations[id] });
            b.AddGeneral(new("fixture:mgz-ally", "同行", "", "standard:none", "shu", 12, [Quiet]));
            b.AddGeneral(new("fixture:mgz-enemy-a", "原生范围四", "", "standard:none", "wei", 12, [Quiet, Range]));
            b.AddGeneral(new("fixture:mgz-enemy-b", "同行", "", "standard:none", "wei", 12, [Quiet]));
            b.AddDeck(new("fixture:mgz-deck", "固定真实补牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("fixture:mgz-card-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋公孙瓒完整真实主流", 4, 4, new Dictionary<string, int>(), "fixture:mgz-deck", GeneralPoolIds: [Hero, "fixture:mgz-ally", "fixture:mgz-enemy-a", "fixture:mgz-enemy-b"], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
