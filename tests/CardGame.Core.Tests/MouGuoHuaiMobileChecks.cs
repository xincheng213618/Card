using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouGuoHuaiMobileChecks
{
    private const string Hero = "mou:guo-huai-mobile", Skill = "mou-mobile:jingce", Watch = "fixture:mough-watch";
    public static void OrderedRealPredictionsAndNextActualStartNoneCold()
    {
        var s = Scene.Start(48); s.Play();
        var initial = s.E<MouGuoHuaiStoredEvent>().OrderBy(e => e.Index).ToArray();
        s.Need(initial.Length == 3 && initial.All(e => e.From == CardLocation.DrawPile && e.To.Zone == CardZoneKind.PublicPersistentPile && e.To.OwnerSeat == 0) &&
            s.V().Markers?.Single(m => m.Kind == PlayerMarkerKind.MouGuoHuaiJingce).Count == 3 && Enumerable.Range(0, 4).All(viewer => s.G.CreateSnapshot(viewer).Players[0].PublicPersistentPileCount == 3),
            "The real raw own-start stores the actual top three entities in a public Jingce pile before normal Draw; the public marker describes those three originals.");
        s.End(); s.Reach(() => s.Branch("card"));
        var selected = new[] { initial[2].CardId, initial[0].CardId, initial[1].CardId };
        for (var i = 0; i < selected.Length; i++)
        {
            s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "card" && c.Cards.SequenceEqual([selected[i]]));
            if (i == 1) s.Cold();
            s.Answer(c => i == 0 ? c.Parameters.GetValueOrDefault("branch") == "recipient" && c.Targets.SequenceEqual([2]) : c.Parameters.GetValueOrDefault("branch") == "none");
            if (i < 2) s.Reach(() => s.Branch("card"));
        }
        s.Reach(s.IsEndWitness);
        var predictions = s.E<MouGuoHuaiPredictionIssuedEvent>().OrderBy(e => e.Ordinal).ToArray();
        var pile = s.G.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).OrderByDescending(c => c.ZoneIndex).Select(c => c.CardId).ToArray();
        s.Need(predictions.Select(p => p.CardId).SequenceEqual(selected) && predictions.Select(p => p.Ordinal).SequenceEqual([1, 2, 3]) &&
            new[] { pile[2], pile[5], pile[8] }.SequenceEqual(selected) && s.E<MouGuoHuaiPredictionPlacedEvent>().All(e => !e.AtBottom && e.ActualPosition == e.Ordinal * 3) && s.V().PublicPersistentPileCount == 0,
            "The human chooses which actual Jingce entity is first, second and third. Those chosen originals truly occupy the third, sixth and ninth stock positions; prediction did not keep an imposed original top-three order.");
        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Reach(() => s.G.State.TurnNumber == 5 && s.IsPlay());
        var first = predictions[0]; var second = predictions[1]; var third = predictions[2];
        s.Need(s.E<MouGuoHuaiPredictionVerifiedEvent>().Count(e => e.PredictionId == first.PredictionId && e.Success && e.GainingSeat == 2) == 1 &&
            s.E<MouGuoHuaiDrawReturnedEvent>().Count(e => e.PredictionId == first.PredictionId && e.Requested == 1 && e.Actual == 1) == 1,
            "The real second opposing turn first obtains the third-position original. Its true CardsGained invoice verifies the recipient once and issues a real Draw1 to the predictor, who is a different actor from the gainer.");
        s.Need(s.E<MouGuoHuaiPredictionVerifiedEvent>().Count(e => e.PredictionId == second.PredictionId && !e.Success && e.GainingSeat == 3) == 1 && s.E<MouGuoHuaiRewardIssuedEvent>().All(e => e.PredictionId != second.PredictionId),
            "The second none prediction fails immediately when the third opposing turn really obtains its original entity, and cannot collect a false reward at the next start.");
        var due = s.E<MouGuoHuaiPredictionDueEvent>().Single(e => e.PredictionId == third.PredictionId);
        var reward = s.E<MouGuoHuaiDrawReturnedEvent>().Single(e => e.PredictionId == third.PredictionId);
        var noneFrame = s.E<MouGuoHuaiStartedEvent>().Single(e => e.FrameId == reward.FrameId);
        s.Need(due is { ActualTurn: 5, OwnerSeat: 0, NoneSuccess: true } && reward is { Requested: 3, Actual: 3 } && noneFrame.Op == SkillProgramEffectOp.ResolveMouGuoHuaiJingceStart &&
            s.E<MouGuoHuaiPredictionConsumedEvent>().Count(e => e.PredictionId == third.PredictionId && e.Reason == "none-at-next-start") == 1 &&
            s.E<MouGuoHuaiPredictionVerifiedEvent>().All(e => e.PredictionId != third.PredictionId) && s.E<MouGuoHuaiStoredEvent>().Count(e => e.FrameId == noneFrame.FrameId) == 3,
            "The user-selected none rule freezes success at the next actual own TurnStarted and really draws three once, even when that Draw itself obtains the old predicted original. It consumes the old record before storing three new Jingce entities.");
        var all = s.G.Events.Select(e => e.Payload).ToArray();
        s.Need(Array.FindIndex(all, e => e is MouGuoHuaiDrawReturnedEvent draw && draw.PredictionId == third.PredictionId) < Array.FindIndex(all, e => e is MouGuoHuaiStoredEvent stored && stored.FrameId == noneFrame.FrameId),
            "The next-start none reward and all its native children precede storing the new top three.");

        var shortStock = Scene.Start(7); shortStock.Play(); var shortCards = shortStock.E<MouGuoHuaiStoredEvent>().OrderBy(e => e.Index).Select(e => e.CardId).ToArray();
        shortStock.Need(shortStock.G.CreateCardZoneDiagnostics().Count(c => c.Location == CardLocation.DrawPile) == 2, "The real short-stock witness has exactly two stock entities before returning any Jingce original.");
        shortStock.End(); shortStock.Reach(() => shortStock.Branch("card"));
        foreach (var id in shortCards)
        {
            shortStock.Answer(c => c.Parameters.GetValueOrDefault("branch") == "card" && c.Cards.SequenceEqual([id]));
            shortStock.Answer(c => c.Parameters.GetValueOrDefault("branch") == "none");
            if (id != shortCards[^1]) shortStock.Reach(() => shortStock.Branch("card"));
        }
        shortStock.Reach(shortStock.IsEndWitness);
        var shortPile = shortStock.G.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).OrderByDescending(c => c.ZoneIndex).Select(c => c.CardId).ToArray();
        shortStock.Need(shortPile.Length == 5 && shortPile.TakeLast(3).SequenceEqual(shortCards) && shortStock.E<MouGuoHuaiPredictionPlacedEvent>().Select(e => e.AtBottom).SequenceEqual([false, true, true]),
            "With two real stock cards, only the first return reaches position three. The later sixth and ninth positions are unavailable at their individual steps, so their actual originals go to the bottom in prediction order.");
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public bool Branch(string b) => P is { PlayerSeat: 0 } && P.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == b);
        public bool IsPlay() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard };
        public bool IsEndWitness() => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(IsPlay);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> match) { var p = P!; var choice = p.Choices.FirstOrDefault(match); Need(p.PlayerSeat == 0 && choice is not null, "Only a published real human choice is submitted."); Accept(new AnswerPromptCommand(0, p.PromptId, choice!.Id, G.Revision)); }
        public void Accept(GameCommand command) { try { var r = G.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Command rejected."); } catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); } }
        public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return; Need(G.State.Winner == Winner.None, "Unexpected native cutoff at " + line);
                if (P is not { PlayerSeat: 0 } p) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (p.Kind == DecisionKind.PlayCard) End();
                else throw new InvalidOperationException("Unexpected human prompt at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded real Jingce flow not reached at " + line + Diagnostic());
        }
        public void Cold() { var before = SnapshotJson.Serialize(G.CreateSnapshot(0)); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == SnapshotJson.Serialize(G.CreateSnapshot(0)), "The actual chosen prediction order and pending recipient survive Cold without reselecting or duplicating a reward."); }
        public void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message + Diagnostic()); }
        private string Diagnostic() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, Prompt = P, Stock = G.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).OrderByDescending(c => c.ZoneIndex), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouGuoHuai", StringComparison.Ordinal) || e.Payload is TurnStartedEvent).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        public static Scene Start(int stock)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(stock));
            var setup = new ShanheBattleSetup(7, 1, "mough-real", Hero, 4, 0, 0, 0, 0, [Watch], [], [],
                [new("fixture:mough-enemy-a", Hp: 12) { StartingHand = [] }, new("fixture:mough-enemy-b", Hp: 12) { StartingHand = [] }], [new("fixture:mough-ally", Hp: 12) { StartingHand = [] }], new string('c', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(int stock) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-guo-huai-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero); var card = CardCatalog.Get(CardKind.Dodge); b.AddCard(new("fixture:mough-dodge", card.DisplayName, card.CategoryName, card.Description, card.Kind));
            var defs = new object[] {
                new { id = "fixture:mough-quiet", revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false, effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "after-prediction", window = "afterTurnEnded", subject = "owner", turnOwnerScope = "own", priority = -1000, optional = false, effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "witness", options = new[] { new { id = "continue" } } } } } } }
            };
            var labels = new Dictionary<string, object> { ["fixture:mough-quiet"] = new { name = "安静同行", description = "实际跳过同行出牌阶段" }, [Watch] = new { name = "真实结束窗口", description = "观察真实预测后的牌堆位置", optionLabels = new Dictionary<string, string> { ["continue"] = "继续原生回合" } } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "原生精策小局") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            foreach (var id in new[] { "fixture:mough-ally", "fixture:mough-enemy-a", "fixture:mough-enemy-b" }) b.AddGeneral(new(id, "同行", "", "standard:none", id.EndsWith("ally", StringComparison.Ordinal) ? "shu" : "wei", 12, ["fixture:mough-quiet"]));
            b.AddDeck(new("fixture:mough-deck", "固定精策库存", 0, 2, []) { PhysicalCards = Enumerable.Range(0, stock).Select(_ => new ContentDeckPhysicalCard("fixture:mough-dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "精策真实预测小局", 4, 4, new Dictionary<string, int>(), "fixture:mough-deck", GeneralPoolIds: [Hero, "fixture:mough-ally", "fixture:mough-enemy-a", "fixture:mough-enemy-b"], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
