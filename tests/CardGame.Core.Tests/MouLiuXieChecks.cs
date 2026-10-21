using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;
internal static class MouLiuXieChecks
{
    private const string Hero = "mou:liu-xie", Hold = "fixture:mlx-draw-hold", Quiet = "fixture:mlx-quiet",
        Rich = "fixture:mlx-rich", Poor = "fixture:mlx-poor", Equal = "fixture:mlx-equal", Deck = "fixture:mlx-deck";
    public static void NativeAllHandAdjustmentsAndOrderedRewardsCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        ShanheStartingCard[] Hand(int count) => Enumerable.Range(1, count).Select(n => new ShanheStartingCard(CardKind.Dodge, Suit.Club, n)).ToArray();
        var setup = new ShanheBattleSetup(7, 1, "mou-liu-xie-native", Hero, 10, 7, 0, 0, 0, [Hold], [], [],
            [new(Poor, Hp: 10) { StartingHand = Hand(2) }, new(Equal, Hp: 10) { StartingHand = Hand(4) }],
            [new(Rich, Hp: 10) { StartingHand = Hand(6) }], new string('b', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouLiuXieReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.MouLiuXie is not null)?.MouLiuXie;
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(),
            Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.HandCount, VisibleCards = p.Hand.Count }),
            Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command)
        {
            try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
            catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); }
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected genuine Human boundary at " + line + Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded native Liu Xie flow not reached at " + line + Diagnostic());
        }
        void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = Prompt(); Need(p?.PlayerSeat == 0, "The current published Human prompt must own the answer.");
            var choice = p!.Choices.FirstOrDefault(predicate); Need(choice is not null, "The genuine requested choice must be published.");
            Submit(new AnswerPromptCommand(0, p.PromptId, choice!.Id, game.Revision));
        }
        Submit(new StartGameCommand()); Reach(() => Prompt() is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        Need(Player(0).HandCount == 2 && Player(1).HandCount == 6 && Player(2).HandCount == 2 && Player(3).HandCount == 4,
            "The small fixed physical-deck fixture reaches mixed native Hand counts without editing runtime state.");
        Submit(new UseProgramSkillCommand(0, "mou:zhanban", "adjust-all-hands", [], [], game.Revision, Prompt()!.PromptId));
        Reach(() => Prompt() is { PlayerSeat: 0 } && Receipt()?.Stage == MouLiuXieStage.OwnerCount);
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "draw" && c.Parameters.GetValueOrDefault("value") == "2");
        Reach(() => Prompt()?.SkillPrompt?.SkillId == Hold);
        Need(Receipt() is { Stage: MouLiuXieStage.DrawChildren, TargetCount: -1, Cursor: 0, Draw.ActualDrawIds.Count: 2 } &&
            Player(0).HandCount == 4 && Events<MouLiuXieTargetCountEvent>().Length == 0 && Events<MouLiuXieAdjustedEvent>().Length == 0,
            "The original native Draw2 gain child suspends its owning Zhanban before its fixed Hand target or another player's adjustment is captured.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Events<MouLiuXieDrawReturnedEvent>() is [{ Recipient: 0, Requested: 2, Actual: 2 }],
            "A representative Cold restores the actual paid Draw2 and its exact movement child without paying twice.");
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(() => Prompt() is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        var adjustments = Events<MouLiuXieAdjustedEvent>();
        Need(Events<MouLiuXieTargetCountEvent>() is [{ Count: 4 }] && adjustments is [
                { Seat: 1, Kind: MouLiuXieAdjustmentKind.Discard, ActualCount: 2 },
                { Seat: 2, Kind: MouLiuXieAdjustmentKind.Draw, ActualCount: 2 },
                { Seat: 3, Kind: MouLiuXieAdjustmentKind.Same, ActualCount: 0 }] &&
            Player(0).HandCount == 4 && Player(1).HandCount == 7 && Player(2).HandCount == 1 && Player(3).HandCount == 4 && Player(3).Hp == 9,
            "The fixed target4 drives native discard2/draw2/no-change in SeatOrder, followed by Draw3/discard3/actual owner-sourced Damage1; the initial owner draw receives no peer reward.");
        var payloads = game.Events.Select(e => e.Payload).ToArray();
        Need(Array.FindLastIndex(payloads, e => e is MouLiuXieAdjustedEvent) < Array.FindIndex(payloads, e => e is MouLiuXieDrawReturnedEvent { Recipient: 1, Requested: 3 }) &&
            Events<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ExecutorSeat == 1 && e.ActualCount == 2 && e.HandCount == 2) &&
            Events<NativeLogicalDiscardInvocationRecordedEvent>().Any(e => e.ExecutorSeat == 2 && e.ActualCount == 3 && e.HandCount == 3) &&
            Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 3 && e.Amount == 1) &&
            Events<MouLiuXieDrawReturnedEvent>().Count(e => e.Recipient == 0 && e.Requested == 2) == 1 &&
            !game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "mou:zhanban"),
            "Every native adjustment completes before the first follow-up, each discard has a primary invocation receipt, and this actual Play's debit prevents reuse.");
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId));
        Reach(() => Events<ProgramBindingResolvedEvent>().Any(e => e.SkillId == "mou:chensheng" && e.Activated && e.Completed));
        var ended = Events<TurnEndedEvent>();
        var chensheng = Events<ProgramBindingResolvedEvent>().Where(e => e.SkillId == "mou:chensheng" && e.Activated && e.Completed).ToArray();
        var finalFacts = game.Events.Select(e => e.Payload).ToArray();
        Need(ended.Select(e => e.ActorSeat).SequenceEqual([0, 1, 2]) && chensheng is [{ OwnerSeat: 0, Window: SkillProgramTriggerWindow.AfterTurnEnded }] &&
            Array.FindLastIndex(finalFacts, e => e is TurnEndedEvent { ActorSeat: 2 }) < Array.FindIndex(finalFacts, e => e == chensheng[0]) && Player(0).HandCount == 5 &&
            Player(1).HandCount > Player(0).HandCount && Player(1).HandCount > Player(2).HandCount,
            "Chensheng correctly skips the uniquely largest Hand player's genuine turn end, then really draws1 at another actual ending when neither owner nor turn owner uniquely leads.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-liu-xie", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            var dodge = CardCatalog.Get(CardKind.Dodge);
            builder.AddCard(new("fixture:mlx-dodge", dodge.DisplayName, dodge.CategoryName, dodge.Description, CardKind.Dodge));
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,
                "effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                {"id":"{{Hold}}","revision":1,"triggers":[{"id":"real-owner-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],
                "movementOccurrence":"perBatch","movementReasons":["skill-program.mou:zhanban.draw"],"optional":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-hold","options":[{"id":"continue"}]}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "正常摸牌后跳过出牌阶段" },
                    [Hold] = new { name = "真实前置摸牌子窗", description = "实际获得斩绊前置摸牌后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续真实斩绊" } } } }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            foreach (var id in new[] { Rich, Poor, Equal }) builder.AddGeneral(new(id, "真实同行", "supporter", Quiet, "wei", 10));
            builder.AddDeck(new(Deck, "固定真实闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 60).Select(_ => new ContentDeckPhysicalCard("fixture:mlx-dodge", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "谋刘协实际主流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Rich, Poor, Equal], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
