using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouDiaoChanMobileChecks
{
    public static void NativeRingAndEndingDraw()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-diao-chan", Owner, 8, 0, 0, 0, 0, [],
            [new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Dodge, Suit.Spade, 8)], [],
            [new(EnemyA, Hp: 8), new(EnemyB, Hp: 8)], [new(Ally, Hp: 8)], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        void Need(bool value, string reason) { if (!value) throw new InvalidOperationException(reason); }
        void Accept(GameCommand command) { var r = g.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Real command rejected."); }
        void Reach(Func<bool> done)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return;
                Need(g.State.Winner == Winner.None, "The fixed real game ended before the ring returned.");
                if (P() is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected Human prompt: " + p.Prompt);
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Mou Diao Chan native flow did not return;turn=" + g.State.TurnNumber + ";phase=" + g.State.Phase + ";frames=" +
                string.Join(';', g.ResolutionStack.Select(f => f.GetType().Name + "/" + f.Id + (f is ProgramSkillFrame p ? "/" + p.InstructionIndex + "/" + JsonSerializer.Serialize(p.MouDiaoChanMobile) : ""))));
        }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Answer(Func<PromptChoice, bool> predicate)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only real Human choices are supplied."); Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
        string B(PromptChoice choice) => choice.Parameters.GetValueOrDefault("branch", "");
        Accept(new StartGameCommand()); Play();
        var costs = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        Need(costs.Length == 2 && registry.Generals[g.CreateSnapshot(0).Players[1].GeneralId].Gender == GeneralGender.Female,
            "The fixed native roster includes a female participant and exactly two original HE costs.");
        Accept(new UseProgramSkillCommand(0, Lijian, "ring-duels", [], [], g.Revision, P()!.PromptId));
        Reach(() => P()?.Choices.Any(c => B(c) == "target") == true);
        foreach (var seat in new[] { 1, 2, 3 }) Answer(c => B(c) == "target" && c.Targets.SequenceEqual([seat]));
        Answer(c => B(c) == "targets-done");
        foreach (var id in costs) Answer(c => B(c) == "card" && c.Cards.SequenceEqual([id]));
        var before = SnapshotJson.Serialize(g.CreateSnapshot(0));
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(g.CreateSnapshot(0)) && E<MouDiaoChanMobileCostEvent>().Length == 0,
            "One pending full three-participant ring restores its exact private costs without paying them early.");
        Answer(c => B(c) == "pay"); Play();
        var issued = E<MouDiaoChanMobileDuelIssuedEvent>();
        Need(E<MouDiaoChanMobileRosterEvent>().Select(e => e.Seat).SequenceEqual([1, 2, 3]) && issued.Length == 3 &&
            issued.Select(e => e.Origin.ActorSeat).SequenceEqual([1, 2, 3]) && issued.Select(e => e.Origin.TargetSeat).SequenceEqual([3, 1, 2]) &&
            E<MouDiaoChanMobileDuelReturnedEvent>().Select(e => e.UseId).SequenceEqual(issued.Select(e => e.UseId)) &&
            issued.All(e => E<CardUseDeclaredEvent>().Count(d => d.ResolutionId == e.UseId && d.CardId == 0 && d.CardKind == CardKind.Duel && d.SourceSeat == e.Origin.ActorSeat) == 1 &&
                E<CardUseFinishedEvent>().Count(d => d.ResolutionId == e.UseId && d.CardId == 0 && d.CardKind == CardKind.Duel) == 1),
            "All three selected participants, including the female role, each use and completely finish one genuine zero-material Duel at the nearest counterclockwise selected other.");
        Need(E<MouDiaoChanMobileCostEvent>().Length == 2 && costs.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1) &&
            E<DamageAppliedEvent>().Length == 3 && E<DamageAppliedEvent>().Select(e => e.TargetSeat).Distinct().Count() == 3 &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Lijian),
            "The whole ring costs exactly N-1 original cards once, deals three native damage instances and spends one actual Play allowance.");
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => E<MouDiaoChanMobileDrawReturnedEvent>().Length == 1);
        Need(E<MouDiaoChanMobileBiyueEvent>() is [{ DamagedRoles: 3, RequestedDraw: 4 }] && E<MouDiaoChanMobileDrawReturnedEvent>() is [{ Requested: 4, Actual: 4 }],
            "The actual mandatory Ending counts distinct damaged roles and returns the native capped Draw4.");
        Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } && g.State.TurnNumber > 1);
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => E<MouDiaoChanMobileDrawReturnedEvent>().Length == 2);
        Need(E<MouDiaoChanMobileBiyueEvent>().Last() is { DamagedRoles: 0, RequestedDraw: 1 } && E<MouDiaoChanMobileDrawReturnedEvent>().Last() is { Requested: 1, Actual: 1 },
            "A later quiet actual turn draws one; Biyue does not reuse last turn's damage or count presently wounded roles.");
    }
    private const string Lijian = "mou-mobile:lijian", Biyue = "mou-mobile:biyue", Quiet = "fixture:mdc-quiet",
        Owner = "fixture:mdc-owner", Ally = "fixture:mdc-ally", EnemyA = "fixture:mdc-enemy-a", EnemyB = "fixture:mdc-enemy-b", Deck = "fixture:mdc-deck";
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-diao-chan-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:diao-chan-mobile");
            var card = CardCatalog.Get(CardKind.Dodge);
            b.AddCard(new("fixture:mdc-dodge", card.DisplayName, card.CategoryName, card.Description, CardKind.Dodge));
            var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new[] {
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } }
            } });
            var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Quiet] = new { name = "Wait", description = "Wait through real native turns" } } });
            var programs = SkillProgramCatalog.Load(rules, presentation);
            b.AddSkill(new(Quiet, "Wait", "Wait") { Program = programs.Programs[Quiet], ProgramPresentation = programs.Presentations[Quiet] });
            b.AddGeneral(new(Owner, "Mou Diao Chan", "supporter", Lijian, "qun", 8, [Biyue], Gender: GeneralGender.Female));
            b.AddGeneral(new(Ally, "Female participant", "supporter", "standard:none", "shu", 8, [Quiet], Gender: GeneralGender.Female));
            foreach (var id in new[] { EnemyA, EnemyB }) b.AddGeneral(new(id, "Other participant", "supporter", "standard:none", "wei", 8, [Quiet]));
            b.AddDeck(new(Deck, "Small fixed deck", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("fixture:mdc-dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "Mou Diao Chan native ring", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
