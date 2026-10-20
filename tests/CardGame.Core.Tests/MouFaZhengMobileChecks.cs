using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouFaZhengMobileChecks
{
    public static void NativeMarksRandomGainAndEnyuan()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-fa-zheng", Owner, 8, 0, 0, 0, 0, [],
            Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Dodge, Suit.Club, 7)).ToArray(), [],
            [new(EnemyA, Hp: 8), new(EnemyB, Hp: 8)], [new(Ally, Hp: 8)], new string('a', 64));
        var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        PendingDecision? P() => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] E<T>() => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
        void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason); }
        void Accept(GameCommand command) { var r = g.Submit(command); Need(r.Accepted, r.Error?.Message ?? "Native command rejected."); }
        void Reach(Func<bool> done)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                Need(g.State.Winner == Winner.None, "The small native game ended before Enyuan returned.");
                if (P() is { PlayerSeat: 0 } p) throw new InvalidOperationException("Unexpected real Human prompt: " + p.Prompt);
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Fa Zheng did not return;turn=" + g.State.TurnNumber + ";phase=" + g.State.Phase + ";frames=" + string.Join(';', g.ResolutionStack.Select(f =>
                f.GetType().Name + '/' + f.Id + (f is ProgramSkillFrame p ? "/" + p.InstructionIndex + "/" + JsonSerializer.Serialize(p.MouFaZheng) : ""))));
        }
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Answer(Func<PromptChoice, bool> match)
        { var p = P()!; Need(p.PlayerSeat == 0, "Only real Human choices are supplied."); Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
        string Branch(PromptChoice c) => c.Parameters.GetValueOrDefault("branch", "");
        void Gift(int target)
        {
            Accept(new UseProgramSkillCommand(0, Xuanhuo, "give-xuan", [], [], g.Revision, P()!.PromptId));
            Reach(() => P()?.Choices.Any(c => Branch(c) == "target") == true);
            Answer(c => c.Targets.SequenceEqual([target]));
            Reach(() => P()?.Choices.Any(c => Branch(c) == "card") == true);
        }
        void Drive(string id, int? target = null)
        { Accept(new UseProgramSkillCommand(0, Driver, id, [], target is { } seat ? [seat] : [], g.Revision, P()!.PromptId)); Play(); }
        Accept(new StartGameCommand()); Play(); Gift(2);
        var before = SnapshotJson.Serialize(g.CreateSnapshot(0));
        g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(g.CreateSnapshot(0)) && E<MouFaZhengMarkEvent>().Length == 0,
            "One representative exact HE gift restores before payment and before issuing a mark.");
        Answer(c => Branch(c) == "card"); Play();
        var mark = E<MouFaZhengMarkEvent>().Single();
        Need(mark.TargetSeat == 2 && mark.Source.OwnerSeat == 0 && E<MouFaZhengObtainedEvent>().Length == 0 &&
            g.CreateSnapshot(0).Players[2].Markers?.Single(m => m.Kind == PlayerMarkerKind.MouFaZhengXuan).Count == 1,
            "The genuine gift issues one attributed public mark; its own gain does not retroactively extract a card.");
        for (var i = 0; i < 6; i++) Drive("draw", 2);
        Need(E<MouFaZhengObtainedEvent>().Length == 5 && E<MouFaZhengObtainedEvent>().All(e => e.MarkId == mark.MarkId && e.Actual == 1) &&
            E<MouFaZhengActualGainEvent>().Count(e => e.MarkId == mark.MarkId) == 5 &&
            E<MouFaZhengMovementPaidEvent>().Where(e => e.Purpose == "take").All(e => g.CardMovements.Any(m => m.Sequence == e.Sequence &&
                m.From == CardLocation.Hand(2) && m.To == CardLocation.Hand(0))),
            "Six actual off-Draw native gain batches cause precisely five random real Hand transfers, with a separate actual received ledger. " +
            "qualified=[" + string.Join(';', E<MouFaZhengGainQualifiedEvent>().Select(e => $"batch{e.BatchId}/mark{e.MarkId}/seat{e.TargetSeat}/index{e.MovementIndex}/seq{e.GainSequence}")) +
            "];started=[" + string.Join(';', E<MouFaZhengStartedEvent>().Select(e => $"{e.FrameId}/{e.Op}/parent{e.ParentId}")) +
            "];obtained=[" + string.Join(';', E<MouFaZhengObtainedEvent>().Select(e => $"mark{e.MarkId}/actual{e.Actual}/seq{e.Sequence}")) +
            "];received=[" + string.Join(';', E<MouFaZhengActualGainEvent>().Select(e => $"mark{e.MarkId}/batch{e.BatchId}/seq{e.Sequence}")) +
            "];moves=[" + string.Join(';', g.CardMovements.Where(m => m.Sequence > mark.GiftSequence).Select(m => $"{m.Sequence}/{m.From}->{m.To}/{m.Reason.Value}")) + "]");
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => P()?.Choices.Any(c => Branch(c) == "card") == true);
        Need(E<MouFaZhengRemovedEvent>() is [{ MarkId: var removed, Obtained: 5 }] && removed == mark.MarkId,
            "Preparation removes the qualified mark before the mandatory native return-three payment.");
        for (var i = 0; i < 3; i++)
        { Answer(c => Branch(c) == "card"); if (i < 2) Reach(() => P()?.Choices.Any(c => Branch(c) == "card") == true); }
        Play();
        Need(E<MouFaZhengMovementPaidEvent>().Count(e => e.Purpose == "repay" && e.TargetSeat == 2 && e.MarkId == mark.MarkId) == 3 &&
            E<MouFaZhengLossReturnedEvent>().Length == 0, "The high branch returns three real HE entities, rather than requiring three as an entry condition or applying the loss branch.");
        Gift(3); Answer(c => Branch(c) == "card"); Play();
        var low = E<MouFaZhengMarkEvent>().Last(); Drive("wound");
        Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => E<MouFaZhengRemovedEvent>().Any(e => e.MarkId == low.MarkId));
        Play();
        Need(E<MouFaZhengLossReturnedEvent>() is [{ TargetSeat: 3, Amount: 1, HpBefore: 8, HpAfter: 7 }] &&
            E<MouFaZhengRecoveryReturnedEvent>() is [{ OwnerSeat: 0 }] && g.CreateSnapshot(0).Players[0].Hp == 8 &&
            (g.CreateSnapshot(0).Players[3].Markers ?? []).All(m => m.Kind != PlayerMarkerKind.MouFaZhengXuan) &&
            E<RecoveryAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1),
            "The low branch fully returns native LoseHp1, then native Recover1, then removes its original mark and returns Preparation.");
    }
    private const string Xuanhuo = "mou-mobile:xuanhuo", Enyuan = "mou-mobile:enyuan", Driver = "fixture:mfz-driver", Quiet = "fixture:mfz-quiet",
        Owner = "fixture:mfz-owner", Ally = "fixture:mfz-ally", EnemyA = "fixture:mfz-enemy-a", EnemyB = "fixture:mfz-enemy-b", Deck = "fixture:mfz-deck";
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-fa-zheng-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "mou:fa-zheng-mobile");
            var c = CardCatalog.Get(CardKind.Dodge); b.AddCard(new("fixture:mfz-dodge", c.DisplayName, c.CategoryName, c.Description, CardKind.Dodge));
            var rules = "{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[" +
                "{\"id\":\"" + Driver + "\",\"revision\":1,\"activations\":[" +
                "{\"id\":\"draw\",\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"draw\",\"target\":\"selectedTarget\",\"amount\":1}]}," +
                "{\"id\":\"wound\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]}," +
                "{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}]}";
            var labels = new Dictionary<string, string> { [Driver] = "Native small-flow driver", [Quiet] = "Wait" };
            var programs = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = labels.ToDictionary(e => e.Key, e => new { name = e.Value, description = e.Value }) }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, labels[id], labels[id]) { Program = program, ProgramPresentation = programs.Presentations[id] });
            b.AddGeneral(new(Owner, "Mou Fa Zheng", "supporter", Xuanhuo, "shu", 8, [Enyuan, Driver]));
            foreach (var id in new[] { Ally, EnemyA, EnemyB }) b.AddGeneral(new(id, "Native marked peer", "supporter", "standard:none", "shu", 8, [Quiet]));
            b.AddDeck(new(Deck, "Fixed small native draw stock", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 28).Select(_ => new ContentDeckPhysicalCard("fixture:mfz-dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "Fa Zheng native mark game", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
