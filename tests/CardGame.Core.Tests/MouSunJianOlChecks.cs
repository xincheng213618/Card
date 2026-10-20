using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouSunJianOlChecks
{
    private const string Hero = "mou:sun-jian-ol", Driver = "fixture:msj-driver", Quiet = "fixture:msj-quiet", Ally = "fixture:msj-ally", EnemyA = "fixture:msj-enemy-a", EnemyB = "fixture:msj-enemy-b", Deck = "fixture:msj-deck";
    public static void FirstHpAndIndependentHulie()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-sun-jian", Hero, 4, 0, 0, 0, 0, [Driver],
            [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Duel, Suit.Club, 7), new(CardKind.Duel, Suit.Spade, 8),
             new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Diamond, 4), new(CardKind.Dodge, Suit.Spade, 5)], [],
            [new(EnemyA, Hp: 8) { StartingHand = [] }, new(EnemyB, Hp: 1) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 7)] }], [new(Ally, Hp: 8) { StartingHand = [] }], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(i => g.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        string Diagnostic() => JsonSerializer.Serialize(new { g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase, Prompt = P(),
            Owner = g.CreateSnapshot(0).Players[0], Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = g.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouSunJianOl", StringComparison.Ordinal) || e.Payload is DamageAppliedEvent or CardUseFinishedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string message) { if (!ok) throw new InvalidOperationException(message + Diagnostic()); }
        void Accept(GameCommand command) { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
        bool Action(string action) => P() is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
        void Answer(Func<PromptChoice, bool> choose)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only actual human choices are submitted."); var c = p.Choices.FirstOrDefault(choose); Need(c is not null, "Missing published human choice."); Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, g.Revision)); }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return; Need(g.State.Winner == Winner.None, "Native game ended early at " + line);
                if (P() is not { PlayerSeat: 0 } p) { Accept(new AdvanceOneStepCommand(g.Revision)); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("response") is not "bagua"); continue; }
                throw new InvalidOperationException("Unexpected human choice at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded native Mou Sun Jian flow not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Drive(string id) => Accept(new UseProgramSkillCommand(0, Driver, id, [], [], g.Revision, P()!.PromptId));
        void ChooseYipo(string branch, bool cold = false)
        {
            Reach(() => Action("mou-sun-jian-yipo"));
            if (branch == "decline") { Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline"); Play(); return; }
            Answer(c => c.Targets.SequenceEqual([0]));
            if (cold)
            {
                var view = SnapshotJson.Serialize(g.CreateSnapshot(0)); var count = E<MouSunJianOlHpFirstReachedEvent>().Length;
                g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
                Need(view == SnapshotJson.Serialize(g.CreateSnapshot(0)) && E<MouSunJianOlHpFirstReachedEvent>().Length == count && E<MouSunJianOlYipoChosenEvent>().Length == 0,
                    "One representative pending Yipo restores its actual HP parent and spent first occurrence without drawing early.");
            }
            Answer(c => c.Parameters.GetValueOrDefault("branch") == branch);
            Reach(() => Action("mou-sun-jian-yipo") && P()!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "card"));
            while (!P()!.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "confirm"))
                Answer(c => c.Parameters.GetValueOrDefault("branch") == "card" && g.CreateSnapshot(0).Players[0].Hand.Any(h => h.Id == c.Cards.Single() && h.Kind == CardKind.Dodge));
            Answer(c => c.Parameters.GetValueOrDefault("branch") == "confirm"); Play();
        }
        void Physical(LegalActionKind kind, int target)
        {
            var a = g.GetHumanLegalActions().First(a => a.Kind == kind && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P()!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        }
        Accept(new StartGameCommand()); Play();
        Need(g.CreateSnapshot(0).Players[0] is { Hp: 4, MaxHp: 5 } && E<MouSunJianOlHpFirstReachedEvent>().Length == 0, "Official initial4/Max5 is assigned without spending first reached4.");
        Drive("damage"); ChooseYipo("draw-many", cold: true);
        Need(E<MouSunJianOlHpFirstReachedEvent>() is [{ Hp: 3 }] && E<MouSunJianOlYipoChosenEvent>() is [{ TargetSeat: 0, X: 2, Draw: 2, Discard: 1 }] && E<MouSunJianOlYipoDrawEvent>() is [{ Requested: 2, Actual: 2 }] && E<MouSunJianOlYipoDiscardEvent>() is [{ Count: 1 }],
            "A genuine native damage reaches3, freezes lostHP2 and really draws2 before the recipient discards1.");
        Drive("lose"); ChooseYipo("discard-many");
        Need(E<MouSunJianOlHpFirstReachedEvent>().Select(e => e.Hp).SequenceEqual([3, 2]) && E<MouSunJianOlYipoChosenEvent>().Last() is { TargetSeat: 0, X: 3, Draw: 1, Discard: 3 } && E<MouSunJianOlYipoDiscardEvent>().Last().Count == 3 &&
            E<NativeLogicalDiscardInvocationRecordedEvent>().Where(e => e.ExecutorSeat == 0 && e.Reason.Value == "skill-program.mou-ol:yipo.discard").Sum(e => e.ActualCount) == 4,
            "The independent native HP loss reaches2; the other real branch draws1 then forces exactly three real self-chosen HE discards.");
        Drive("recover"); Play(); Drive("recover"); ChooseYipo("decline");
        Drive("lose"); Play(); Drive("recover"); Play();
        Need(E<MouSunJianOlHpFirstReachedEvent>().Select(e => e.Hp).SequenceEqual([3, 2, 4]) && E<MouSunJianOlYipoStartedEvent>().Length == 3 && E<MouSunJianOlYipoChosenEvent>().Length == 2,
            "A later true recovery to4 qualifies despite initial assignment; declining consumes first4 and repeated3/4 changes do not reoffer.");
        Physical(LegalActionKind.Slash, 3); Reach(() => Action("mou-sun-jian-hulie")); Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline");
        Reach(() => Action("mou-sun-jian-hulie"));
        Need(E<MouSunJianOlHulieUsedEvent>().Length == 0 && g.ResolutionStack.OfType<ProgramSkillFrame>().Last().MouSunJianOlHulie?.Stage == MouSunJianOlHulieStage.CounterChoice,
            "A real NPC Dodge avoids the original Slash; the independent counter is offered although the original bonus was declined.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "accept"); Play();
        var counter = E<MouSunJianOlCounterIssuedEvent>().Single().Origin;
        Need(counter.ActorSeat == 3 && counter.TargetSeat == 0 && E<MouSunJianOlCounterReturnedEvent>().Count(e => e.UseId == counter.UseId) == 1 &&
            E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == counter.UseId && e.SourceSeat == 3 && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 && E<DamageAppliedEvent>().Any(e => e.SourceSeat == 3 && e.TargetSeat == 0 && e.Amount == 1),
            "The original target really uses a zero-material Slash, the human naturally passes its response and the native whole Use returns once to its original parent.");
        Physical(LegalActionKind.Duel, 2); Reach(() => Action("mou-sun-jian-hulie")); Answer(c => c.Parameters.GetValueOrDefault("branch") == "accept"); Play();
        var boost = E<MouSunJianOlHulieUsedEvent>().Single(e => !e.Counter);
        Need(E<MouSunJianOlHulieUsedEvent>().Count(e => e.Counter) == 1 && E<MouSunJianOlUseDamageEvent>().Single(e => e.UseId == boost.OriginalUseId).Amount == 2 && g.CreateSnapshot(0).Players[2].Hp == 6,
            "After the separately spent counter, the still-unused bonus affects a genuine native Duel for2 in the same actual turn.");
        Physical(LegalActionKind.Duel, 2); Play();
        Need(E<MouSunJianOlHulieUsedEvent>().Length == 2 && g.CreateSnapshot(0).Players[2].Hp == 5, "The two independent actual-turn options do not refresh on another real Use.");
        Drive("clamp"); ChooseYipo("decline");
        Need(g.CreateSnapshot(0).Players[0] is { Hp: 1, MaxHp: 1 } && E<MouSunJianOlHpFirstReachedEvent>().Last().Hp == 1 && E<MaximumHpChangedEvent>().Last().HpBeforeClamp == 3,
            "A genuine maximum-HP clamp preserves its actual before receipt and records the newly reached positive HP1.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-sun-jian-ol", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Slash, CardKind.Duel }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:msj-card-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var defs = new object[] {
                new { id = Driver, revision = 1, activations = new object[] {
                    new { id = "damage", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "damage", target = "owner", amount = 1 } } },
                    new { id = "lose", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "loseHp", target = "owner", amount = 1 } } },
                    new { id = "recover", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "recover", target = "owner", amount = 1 } } },
                    new { id = "clamp", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { new { op = "changeMaximumHp", target = "owner", amount = -4 } } }
                } },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "实际体力驱动", description = "真实伤害、流失、恢复及上限夹取", activationLabels = new Dictionary<string, string> { ["damage"] = "真实伤害1", ["lose"] = "真实失去1体力", ["recover"] = "真实回复1体力", ["clamp"] = "真实减少上限4" } }, [Quiet] = new { name = "安静同行", description = "跳过真实出牌阶段" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            foreach (var peer in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(peer, "真实同行", "supporter", Quiet, peer == Ally ? "wu" : "wei", 8));
            b.AddDeck(new(Deck, "固定小牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("fixture:msj-card-Dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋孙坚原生主流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
