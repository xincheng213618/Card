using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouCaoHongAnniversaryChecks
{
    private const string Hero = "mou:cao-hong", Quiet = "fixture:mch-anniv-quiet", Ally = "fixture:mch-anniv-ally", EnemyA = "fixture:mch-anniv-enemy-a", EnemyB = "fixture:mch-anniv-enemy-b", Deck = "fixture:mch-anniv-deck";
    public static void NativeRangeDebtAndBilateralDistanceHandExchangeCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-cao-hong-anniversary-native", Hero, 10, 6, 0, 0, 0, [],
            [new(CardKind.IronChain, Suit.Club, 7), new(CardKind.Slash, Suit.Spade, 8)],
            [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.BaguaFormation, Suit.Spade, 2)],
            [new(EnemyA, Hp: 10) { StartingHand = [new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Club, 4)] }, new(EnemyB, Hp: 10) { StartingHand = [new(CardKind.Dodge, Suit.Club, 5), new(CardKind.Dodge, Suit.Club, 6)] }],
            [new(Ally, Hp: 10) { StartingHand = [new(CardKind.Dodge, Suit.Club, 7), new(CardKind.Dodge, Suit.Club, 8)] }], new string('a', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        PlayerSnapshot Player(int s) => game.CreateSnapshot(0).Players[s];
        MouCaoHongReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.MouCaoHong is not null)?.MouCaoHong;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(),
            Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.HandCount, p.Equipment }),
            Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command)
        {
            try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
            catch (Exception e) { throw new InvalidOperationException(e.Message + Diagnostic(), e); }
        }
        void Answer(Func<PromptChoice, bool> match)
        {
            var p = Prompt(); Need(p?.PlayerSeat == 0, "Only the actual published Human choice is answered."); var c = p!.Choices.FirstOrDefault(match);
            Need(c is not null, "The exact native choice is published."); Submit(new AnswerPromptCommand(0, p.PromptId, c!.Id, game.Revision));
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 160; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Human boundary at " + line + Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded native Cao Hong anniversary flow not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Use(CardKind kind, int target)
        {
            var id = Player(0).Hand.Single(c => c.Kind == kind).Id;
            var action = game.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual([target]));
            Need(action is not null, "The real physical card has its canonical native target action."); Submit(new PlayCardCommand(0, id, action!.TargetSeats, game.Revision, Prompt()!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        void Xianju()
        {
            Submit(new UseProgramSkillCommand(0, "mou:xianju", "obtain-in-range", [], [], game.Revision, Prompt()!.PromptId));
            foreach (var target in new[] { 1, 3 })
            {
                Reach(() => Receipt() is { Stage: MouCaoHongStage.ObtainCard } r && r.Target == target && Prompt()?.PlayerSeat == 0);
                Need(Prompt()!.ValidCardIds.Count == 0 && Prompt()!.Choices is [{ Cards.Count: 0 }] && Prompt()!.Choices[0].Parameters.GetValueOrDefault("branch") == "hand",
                    "An opposing hidden Hand offers only the actual anonymous random-obtain entry, never an opponent entity ID or face.");
                Answer(c => c.Parameters.GetValueOrDefault("branch") == "hand");
            }
            Play(); Need(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "mou:xianju"), "Xianju spends the actual current Play allowance.");
        }
        void Add(int id) => Answer(c => c.Parameters.GetValueOrDefault("branch") == "add" && c.Cards.SequenceEqual([id]));
        Submit(new StartGameCommand()); Play();
        Need(game.GetCombatDistance(0, 2) == 2 && game.GetCombatDistance(2, 0) == 2, "The actual four-seat native ring initially leaves the opposite seat out of range.");
        var firstTurn = game.State.TurnNumber; var ownHand = Player(0).HandCount; Xianju();
        Need(Player(0).HandCount == ownHand + 2 && Player(1).HandCount == 1 && Player(2).HandCount == 2 && Player(3).HandCount == 1 &&
            Events<MouCaoHongHandChosenEvent>().Select(e => e.Holder).SequenceEqual([1, 3]) && Events<MouCaoHongMovementPaidEvent>().Length == 2,
            "The native activation takes one genuine Hand entity from each frozen in-range other seat and leaves the opposite seat untouched.");
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId));
        Reach(() => Receipt() is { Stage: MouCaoHongStage.DiscardCards, Requested: 1 } && Prompt()?.PlayerSeat == 0);
        var armor = Player(0).Equipment.Single(c => c.Kind == CardKind.BaguaFormation).Id; Add(armor); Answer(c => c.Parameters.GetValueOrDefault("branch") == "done"); Play();
        Need(game.State.TurnNumber > firstTurn && Events<TurnEndedEvent>().Any(e => e.TurnNumber == firstTurn && e.ActorSeat == 0) &&
            Events<MouCaoHongDebtConsumedEvent>() is [{ Required: 1 }] && Events<MouCaoHongSegmentCompletedEvent>().Any(e => e.Stage == MouCaoHongStage.DiscardChildren && e.Requested == 1 && e.Actual == 1) &&
            Events<MouCaoHongMovementPaidEvent>().Any(e => e.CardId == armor && e.From == CardLocation.Equipment(0) && e.To == CardLocation.DiscardPile),
            "The original Play really ends only after its one outside-seat cost pays native logical discard from Equipment; NPCs alone skip their Play phases.");
        Xianju();
        var targetHand = Player(2).HandCount; var secondTurn = game.State.TurnNumber;
        Use(CardKind.IronChain, 2); Reach(() => Receipt()?.Stage == MouCaoHongStage.ExchangeTarget && Prompt()?.PlayerSeat == 0);
        Need(game.GetCombatDistance(0, 2) == 1 && game.GetCombatDistance(2, 0) == 1 && Events<MouCaoHongDistanceIssuedEvent>().Any(e => e.Turn == secondTurn && e.Target == 2),
            "The true native targeted Use issues this actual turn's bilateral fixed-distance proof before testing the all-others condition.");
        Answer(c => c.Targets.SequenceEqual([2])); Reach(() => Receipt() is { Stage: MouCaoHongStage.ReturnCards } && Prompt()?.PlayerSeat == 0);
        Need(Receipt()?.Requested == targetHand && Player(2).HandCount == 0 && Events<MouCaoHongSegmentCompletedEvent>().Any(e => e.Stage == MouCaoHongStage.ExchangeTakeChildren && e.Actual == targetHand),
            "Yingjia obtains every real original Hand entity first and freezes the actual acquired count, rather than its requested or net Hand difference, for the return.");
        var crossbow = Player(0).Equipment.Single(c => c.Kind == CardKind.Crossbow).Id; Add(crossbow); Add(Player(0).Hand.First(c => c.Kind == CardKind.Dodge).Id);
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0)); var recipientBefore = SnapshotJson.Serialize(game.CreateSnapshot(2)); var taken = Events<MouCaoHongMovementPaidEvent>().Length;
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && recipientBefore == SnapshotJson.Serialize(game.CreateSnapshot(2)) && Receipt() is { Stage: MouCaoHongStage.ReturnCards, Selected.Count: 2 } &&
            Events<MouCaoHongMovementPaidEvent>().Length == taken && Player(0).Equipment.Any(c => c.Id == crossbow) && Player(2).HandCount == 0,
            "One representative Cold retains private partial HE return choices and the original completed-Use parent without repeating the already paid whole-Hand obtain or exposing the uncommitted give.");
        while (Receipt() is { Stage: MouCaoHongStage.ReturnCards } r && r.Selected.Count < r.Requested)
            Add(Player(0).Hand.First(c => c.Kind == CardKind.Dodge && !r.Selected.Contains(c.Id)).Id);
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "done"); Play();
        Need(Player(2).HandCount == targetHand && Player(0).Equipment.All(c => c.Id != crossbow) &&
            Events<MouCaoHongMovementPaidEvent>().Any(e => e.CardId == crossbow && e.From == CardLocation.Equipment(0) && e.To == CardLocation.Hand(2)) &&
            Events<MouCaoHongSegmentCompletedEvent>().Any(e => e.Stage == MouCaoHongStage.ReturnChildren && e.Requested == targetHand && e.Actual == targetHand) &&
            Events<NativeCardsGivenEvent>().Any(e => e.Giver == 0 && e.Recipient == 2 && e.Count == targetHand),
            "The exact real return includes the selected Equipment entity and delivers the acquired count once; its physical callback also seals a genuine native Give for shared observers.");
        Use(CardKind.Slash, 3); Reach(() => Receipt()?.Stage == MouCaoHongStage.ExchangeTarget && Prompt()?.PlayerSeat == 0);
        Need(Prompt()!.Choices.All(c => !c.Targets.Contains(2)) && Events<MouCaoHongExchangeIssuedEvent>().Count(e => e.Owner == 0 && e.Turn == secondTurn && e.Target == 2) == 1,
            "A later real other-target Use may test Yingjia again, while the exact exchange target already spent this actual turn cannot be chosen twice.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline"); Play();
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId)); Reach(() => Events<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == secondTurn));
        Need(Events<MouCaoHongDebtConsumedEvent>().Select(e => e.Required).SequenceEqual([1, 0]) && game.GetCombatDistance(0, 2) == 2 && game.GetCombatDistance(2, 0) == 2 &&
            Events<MouCaoHongSegmentCompletedEvent>().Any(e => e.Stage == MouCaoHongStage.DiscardChildren && e.Requested == 0 && e.Actual == 0),
            "The second Play-ending cost queries the current now-complete range instead of the original outside count; both fixed-distance directions expire at the actual TurnEnded fact.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-cao-hong-anniversary", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero);
            CharacterFixtureContent.Register(builder, "mou:huang-quan");
            foreach (var kind in new[] { CardKind.Dodge, CardKind.Slash, CardKind.IronChain, CardKind.Crossbow, CardKind.BaguaFormation }) { var c = CardCatalog.Get(kind); builder.AddCard(new("fixture:mch-anniv-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Quiet] = new { name = "安静同行", description = "正常摸牌后跳过出牌" } } }));
            builder.AddSkill(new(Quiet, "安静同行", "正常摸牌后跳过出牌") { Program = catalog.Programs[Quiet], ProgramPresentation = catalog.Presentations[Quiet] });
            foreach (var id in new[] { Ally, EnemyA, EnemyB }) builder.AddGeneral(new(id, "真实同行", "supporter", Quiet, "wei", 10));
            builder.AddDeck(new(Deck, "固定真实闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 70).Select(_ => new ContentDeckPhysicalCard("fixture:mch-anniv-" + CardKind.Dodge, Suit.Club, 2)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "谋曹洪十周年真实主流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
