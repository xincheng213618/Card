using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouDianWeiChecks
{
    private const string Hero = "mou:dian-wei", Quiet = "fixture:mdw-quiet", Low = "fixture:mdw-low", Mixed = "fixture:mdw-mixed", Uninvolved = "fixture:mdw-uninvolved", Deck = "fixture:mdw-deck";
    public static void NativeThreeContestsMultiTargetSlashAndActualRecoveryDebtCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-dian-wei-native", Hero, 1, 0, 0, 0, 0, [], [new(CardKind.Dodge, Suit.Spade, 13)], [],
            [new(Mixed, Hp: 10) { StartingHand = [new(CardKind.Slash, Suit.Club, 3), new(CardKind.Slash, Suit.Club, 1)] }, new(Uninvolved, Hp: 10) { StartingHand = [] }],
            [new(Low, Hp: 10) { StartingHand = [new(CardKind.Slash, Suit.Club, 1), new(CardKind.Slash, Suit.Club, 1), new(CardKind.Slash, Suit.Club, 1)] }], new string('d', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        PlayerSnapshot Player(int s) => game.CreateSnapshot(0).Players[s];
        MouDianWeiReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.MouDianWei is not null)?.MouDianWei;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(),
            Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.MaxHp, p.HandCount }),
            Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command)
        {
            try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
            catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); }
        }
        void Answer(Func<PromptChoice, bool> match)
        {
            var p = Prompt(); Need(p?.PlayerSeat == 0, "Only the actual published Human choice is answered.");
            var c = p!.Choices.FirstOrDefault(match); Need(c is not null, "The expected exact native choice is published."); Submit(new AnswerPromptCommand(0, p.PromptId, c!.Id, game.Revision));
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Human boundary at " + line + Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded native Dian Wei flow not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Opponent(int seat, int index)
        {
            Reach(() => Receipt() is { Stage: MouDianWeiStage.Opponent } r && r.Index == index && Prompt()?.PlayerSeat == 0);
            Answer(c => c.Targets.SequenceEqual([seat]));
        }
        void SourceCard(int rank)
        {
            Reach(() => game.ResolutionStack.LastOrDefault() is PindianFrame && Prompt()?.PlayerSeat == 0);
            Answer(c => c.Cards is [var id] && Player(0).Hand.Any(card => card.Id == id && card.Rank == rank));
        }
        Submit(new StartGameCommand()); Play();
        Need(Player(0).Hp == 5 && Player(0).MaxHp == 5 && Player(0).HandCount == 2 && Events<MouDianWeiDebtIssuedEvent>() is [{ ActualRecovered: 4 }] &&
            Events<RecoveryAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 4),
            "A real Shanhe wounded entry recovers its actual four HP through native Kangyong and records only that recovery debt; the normal Draw1 remains native.");
        Submit(new UseProgramSkillCommand(0, "mou:kuangzhan", "draw-and-contest", [], [], game.Revision, Prompt()!.PromptId));
        Reach(() => Receipt() is { Stage: MouDianWeiStage.Opponent, X: 3 } && Prompt()?.PlayerSeat == 0);
        Need(Events<MouDianWeiDrawReturnedEvent>() is [{ Requested: 3, ActualDrawn: 3, ReplacementGained: 0, X: 3 }] && Player(0).HandCount == 5,
            "Kuangzhan obtains three real native draw entities to reach MaxHp5, freezing three actual contests.");
        Opponent(1, 0); SourceCard(13);
        Reach(() => Receipt() is { Stage: MouDianWeiStage.Opponent, Index: 1 } && Prompt()?.PlayerSeat == 0);
        Need(Player(1).Hp == 9 && Player(2).Hp == 10 && Player(3).Hp == 10 && Events<MouDianWeiSlashIssuedEvent>() is [{ Actor: 0, Target: 1 }],
            "The first real win attacks the participating loser only; neither a yet-uninvolved future opponent nor an empty-Hand nonparticipant becomes a target.");
        Opponent(2, 1);
        Reach(() => game.ResolutionStack.LastOrDefault() is PindianFrame && Prompt()?.PlayerSeat == 0);
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Receipt() is { Index: 1, Stage: MouDianWeiStage.Pindian, X: 3 } &&
            Events<MouDianWeiDrawReturnedEvent>().Length == 1 && Events<MouDianWeiSlashReturnedEvent>().Length == 1,
            "One representative Cold restores the next native Pindian below its own paid Draw/contest/Slash parent without repeating the first payment.");
        SourceCard(2); Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge });
        Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        Opponent(2, 2); SourceCard(2); Play();
        var comparisons = Events<PindianResultDeterminedEvent>(); var uses = Events<MouDianWeiSlashIssuedEvent>();
        Need(comparisons.Length == 3 && comparisons.Select(e => e.Result.SourceWon).SequenceEqual([true, false, true]) &&
            uses.Length == 4 && uses.Where(e => e.Index == 2).Select(e => e.Target).SequenceEqual([1, 2]) && uses.Where(e => e.Index == 2).Select(e => e.UseId).Distinct().Count() == 1 &&
            uses.Any(e => e.Index == 1 && e.Actor == 2 && e.Target == 0) && Player(0).Hp == 4 && Player(1).Hp == 8 && Player(2).Hp == 9 && Player(3).Hp == 10 &&
            Events<MouDianWeiSlashReturnedEvent>().Length == 3 && Events<MouDianWeiDrawReturnedEvent>().Length == 1 && !game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "mou:kuangzhan"),
            "Three actual comparisons include the real counter-Slash; the last win issues one native two-target Slash including an earlier winner who now lost, excludes nonparticipants, and preserves the actual phase debit.");
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId)); Reach(() => Events<MouDianWeiDebtPaidEvent>().Length == 1);
        var history = game.Events.Select(e => e.Payload).ToArray();
        Need(Events<MouDianWeiDebtPaidEvent>() is [{ Debt: 4, ActualLost: 3 }] && Player(0).Hp == 1 &&
            Events<ProgramSkillHpLostEvent>().Any(e => e.SkillId == "mou:kangyong" && e.TargetSeat == 0 && e.Amount == 3) &&
            Array.FindIndex(history, e => e is TurnEndedEvent { ActorSeat: 0 }) < Array.FindIndex(history, e => e is MouDianWeiDebtPaidEvent),
            "Only the genuine full-turn end repays native recovered HP, capping the actual loss at three to preserve one HP.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-dian-wei", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Slash }) { var c = CardCatalog.Get(kind); builder.AddCard(new("fixture:mdw-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[
                {"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "正常摸牌后跳过出牌阶段" } } }));
            builder.AddSkill(new(Quiet, "安静同行", "正常摸牌后跳过出牌阶段") { Program = catalog.Programs[Quiet], ProgramPresentation = catalog.Presentations[Quiet] });
            foreach (var id in new[] { Low, Mixed, Uninvolved }) builder.AddGeneral(new(id, "真实同行", "supporter", Quiet, "wei", 10));
            builder.AddDeck(new(Deck, "固定真实低点闪牌堆", 0, 1, []) { PhysicalCards = Enumerable.Range(0, 60).Select(_ => new ContentDeckPhysicalCard("fixture:mdw-" + CardKind.Dodge, Suit.Club, 2)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "谋典韦真实主流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Low, Mixed, Uninvolved], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
