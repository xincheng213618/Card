using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouJiangWeiAnniversaryChecks
{
    private const string Hero = "mou:jiang-wei", Plain = "fixture:mjw-plain", Quiet = "fixture:mjw-quiet", Deck = "fixture:mjw-deck";
    public static void TrueTrickYinEmptyHeCausalDyingFuzhanColdAndModifiedBoundaries()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-jiang-wei-native", Hero, 3, 0, 0, 0, 0, [],
            [new(CardKind.DrawTwo, Suit.Heart, 7), new(CardKind.DrawTwo, Suit.Club, 8), new(CardKind.DrawTwo, Suit.Diamond, 9), new(CardKind.DrawTwo, Suit.Spade, 10)], [],
            [new(Plain, Hp: 20) { StartingHand = [] }, new(Plain, Hp: 20) { StartingHand = [] }],
            [new(Plain, Hp: 20) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 5)] }], new string('f', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouJiangWeiAnniversaryReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouJiangWeiAnniversary is not null)?.MouJiangWeiAnniversary;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(),
            Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.MaxHp, p.HandCount }),
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
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Human input at " + line + Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded native Juemou flow not reached at " + line + Diagnostic());
        }
        void Answer(Func<PromptChoice, bool> match)
        {
            var p = Prompt(); Need(p?.PlayerSeat == 0, "Only the actual published Human input is answered.");
            var choice = p!.Choices.FirstOrDefault(match); Need(choice is not null, "Required current Juemou choice missing.");
            Submit(new AnswerPromptCommand(0, p.PromptId, choice!.Id, game.Revision));
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        bool Offer(string skill) => Prompt() is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill);
        void Activate(string skill) { Reach(() => Offer(skill)); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill); }
        void Trick()
        {
            var id = Player(0).Hand.First(c => c.Kind == CardKind.DrawTwo).Id;
            var action = game.GetHumanLegalActions().Single(a => a.CardId == id && a.Kind == LegalActionKind.DrawTwo && a.ConversionSource is null);
            Submit(new PlayCardCommand(0, id, action.TargetSeats, game.Revision, Prompt()!.PromptId)); Activate("mou:juemou");
        }
        void Pair(int victim, int source)
        {
            Reach(() => Receipt()?.Stage == MouJiangWeiAnniversaryStage.Victim && Prompt()?.PlayerSeat == 0); Answer(c => c.Targets.SequenceEqual([victim]));
            Answer(c => c.Targets.SequenceEqual([source]));
        }
        Submit(new StartGameCommand());
        Reach(() => Receipt()?.Stage == MouJiangWeiAnniversaryStage.Initial && Prompt()?.PlayerSeat == 0); Answer(c => c.Parameters.GetValueOrDefault("branch") == "yang"); Play();
        Need(Player(0).Hp == 3 && Player(0).MaxHp == 4, "The genuine 3/4 initial health and explicit GameStarting Yang choice are retained.");
        Trick(); Play();
        Need(Player(0).Hp == 2 && Events<MouJiangWeiAnniversaryDrawReturnedEvent>() is [{ Requested: 2 }] && Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1),
            "A real Trick Use accepts Yang, applies actual self damage and draws current missing HP only after its native damage children return.");
        Need(Player(3).HandCount == 0, "The next real Yin holder has no HE payment before the pairing.");
        Trick(); Pair(0, 3); Play();
        Need(Player(0).Hp == 1 && Events<MouJiangWeiAnniversaryPairEvent>().Any(e => e.Victim == 0 && e.Source == 3) && Events<MouJiangWeiAnniversaryDiscardedEvent>().Length == 0 &&
            Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 3 && e.TargetSeat == 0 && e.Amount == 1),
            "The explicit user decision permits an empty HE holder: no discard is fabricated and the holder still causes its real damage to the first selected role.");
        Trick(); Reach(() => Offer("mou:fuzhan"));
        var restored = Events<MouJiangWeiAnniversaryStartedEvent>().Single(e => e.Operation == SkillProgramEffectOp.RestoreMouJiangWeiJuemou);
        Need(restored.CauseFrameId > 0 && restored.DyingId > 0 && Events<PlayerDyingEvent>().Any(e => e.ResolutionId == restored.DyingId && e.VictimSeat == 0) &&
            Events<DyingResolvedEvent>().Any(e => e.ResolutionId == restored.DyingId && e.VictimSeat == 0 && e.Survived) && Player(0).Hp == 1 &&
            Events<MouJiangWeiAnniversaryRecoveryIssuedEvent>().Single(e => e.DyingId == restored.DyingId) is { Amount: 1, TargetHp: 1 },
            "Only the owner's actual Juemou damage creates this causal Dying receipt and native recover-to-one before the real survived-Dying return.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Events<MouJiangWeiAnniversaryStartedEvent>().Count(e => e.Operation == SkillProgramEffectOp.RestoreMouJiangWeiJuemou) == 1,
            "Cold at the actual Fuzhan exit offer retains the still-paused original Yang damage and completed causal rescue without repeating either.");
        Activate("mou:fuzhan"); Play();
        Need(Player(0).Hp == 4 && Events<ProgramBooleanStateChangedEvent>().Any(e => e.OwnerSeat == 0 && e.SkillId == "mou:juemou" && e.StateId == "modified" && e.Value) &&
            Events<MouJiangWeiAnniversaryDrawReturnedEvent>().Length == 1 && Events<SkillUsageConsumedEvent>().Count(e => e.SkillId == "mou:fuzhan" && e.Scope == SkillUsageScope.Game) == 1,
            "Fuzhan really recovers to current maximum then modifies Juemou once; the original Yang draw sees the newly full HP after the nested rescue and draws zero.");
        Need(Player(1).HandCount == 1, "The next real foreign discard has one private Hand entity to pay.");
        Trick(); Pair(2, 1); Play();
        Need(Events<MouJiangWeiAnniversaryDiscardedEvent>() is [{ Chooser: 2, Holder: 1, From.Zone: CardZoneKind.Hand }] && Player(1).HandCount == 0 && Player(2).Hp == 19 &&
            Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 1 && e.TargetSeat == 2 && e.Amount == 1),
            "Yin's first selected role genuinely discards the second role's hidden Hand entity, then takes that second role's native damage; no private Hand face is exposed for the choice.");
        var endedTurn = game.State.TurnNumber;
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId)); Activate("mou:juemou");
        Reach(() => Offer("mou:juemou") && game.State.TurnNumber > endedTurn);
        Need(Events<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == endedTurn) &&
            Events<MouJiangWeiAnniversaryStartedEvent>().Any(e => e.Source.BindingId == "modified-ending" && e.Turn == endedTurn) && Events<MouJiangWeiAnniversaryDrawReturnedEvent>().Length == 2 && Player(0).Hp == 3,
            "The modified Ending branch runs after the genuine native TurnEnded fact, and the next actual owner turn offers the added beginning branch.");
        Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play();
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-jiang-wei", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.DrawTwo, CardKind.Dodge }) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mjw-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Quiet] = new { name = "真实安静同行", description = "正常摸牌后跳过出牌阶段。" } } }));
            b.AddSkill(new(Quiet, "真实安静同行", "正常摸牌后跳过出牌阶段。") { Program = catalog.Programs[Quiet], ProgramPresentation = catalog.Presentations[Quiet] });
            b.AddGeneral(new(Plain, "真实绝谋同行", "", "standard:none", "wei", 20, [Quiet]));
            b.AddDeck(new(Deck, "固定真实黑闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("fixture:mjw-" + CardKind.Dodge, Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋姜维真实主流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
