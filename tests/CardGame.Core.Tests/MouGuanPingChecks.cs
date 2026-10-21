using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class MouGuanPingChecks
{
    private const string Hero = "mou:guan-ping", Plain = "fixture:mgp-plain", Deck = "fixture:mgp-deck";
    public static void NativeWholeColorAfterOrdinarySlashOptionsRepeatAndCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-guan-ping-native", Hero, 4, 0, 0, 0, 0, [],
            [new(CardKind.Slash, Suit.Spade, 3), new(CardKind.Slash, Suit.Spade, 4), new(CardKind.DrawTwo, Suit.Spade, 5), new(CardKind.Crossbow, Suit.Club, 6),
             new(CardKind.Dodge, Suit.Heart, 7), new(CardKind.IronChain, Suit.Diamond, 8), new(CardKind.ZhangbaSerpentSpear, Suit.Heart, 9)], [],
            [new(Plain, Hp: 20) { StartingHand = [] }, new(Plain, Hp: 20) { StartingHand = [] }], [new(Plain, Hp: 20) { StartingHand = [] }], new string('e', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouGuanPingReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.MouGuanPing is not null)?.MouGuanPing;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(),
            Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.HandCount }),
            Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(25).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool ok, string why) { if (!ok) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command)
        {
            try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
            catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); }
        }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 120; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected actual Human boundary at " + line + Diagnostic());
                Submit(new AdvanceOneStepCommand(game.Revision));
            }
            throw new InvalidOperationException("Bounded native Wuwei flow not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Choice(int option)
        {
            Reach(() => Receipt()?.Stage == MouGuanPingStage.Choosing && Prompt()?.PlayerSeat == 0);
            var prompt = Prompt()!; var chosen = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("option") == option.ToString());
            Submit(new AnswerPromptCommand(0, prompt.PromptId, chosen.Id, game.Revision));
        }
        void Wuwei(bool red)
        {
            var ids = Player(0).Hand.Where(c => red ? c.Suit is Suit.Heart or Suit.Diamond : c.Suit is Suit.Spade or Suit.Club).Select(c => c.Id).Order().ToArray();
            Need(ids.Length > 0 && game.GetHumanLegalActions().Any(a => a.SkillId == "mou:wuwei"), "Actual Wuwei activation must remain legal with current whole-color entities and available skill quota.");
            Submit(new UseProgramSkillCommand(0, "mou:wuwei", "color-hand-slash", ids, [2], game.Revision, Prompt()!.PromptId));
            Reach(() => Receipt()?.Stage == MouGuanPingStage.Choosing && Prompt()?.PlayerSeat == 0);
            var use = Receipt()!.Use;
            Need(game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == use.UseId).Action is { } a && a.PhysicalCards.Select(c => c.CardId).Order().SequenceEqual(ids) &&
                a.PhysicalCards.All(c => c.From == CardLocation.Hand(0)), "The original native Use freezes exactly every chosen-color Hand entity as its real payment.");
        }
        Submit(new StartGameCommand()); Play();
        var ordinary = Player(0).Hand.First(c => c.Kind == CardKind.Slash).Id;
        var ordinaryAction = game.GetHumanLegalActions().First(a => a.CardId == ordinary && a.Kind == LegalActionKind.Slash && a.ConversionSource is null && a.TargetSeats.SequenceEqual([3]));
        Submit(new PlayCardCommand(0, ordinary, ordinaryAction.TargetSeats, game.Revision, Prompt()!.PromptId)); Play();
        Need(!game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null), "The ordinary native Slash allowance is truly spent before Wuwei.");
        Wuwei(true);
        var first = Receipt()!.Use; var redIds = game.ResolutionStack.OfType<CardUseFrame>().Single(u => u.Id == first.UseId).Action!.PhysicalCards.Select(c => c.CardId).ToArray();
        Need(first.TypeCount == 3 && !first.Extra && redIds.Length == 3, "Different red suits retain their three real basic/trick/equipment material types.");
        Choice(1); Reach(() => Receipt()?.Cursor == 1 && Receipt()?.Stage == MouGuanPingStage.Choosing && Prompt()?.PlayerSeat == 0);
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Events<MouGuanPingDrawReturnedEvent>().Length == 1,
            "Cold at the paid original Use option cursor preserves its real materials and completed native draw without duplicate payment or draw.");
        Choice(2); Choice(3); Play();
        Need(Player(2).Hp == 18 && Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 2) && Events<CardUseFinishedEvent>().Count(e => e.ResolutionId == first.UseId) == 1 &&
            Events<CurrentTurnNonLockedSkillSuppressionIssuedEvent>().Any(e => e.Suppression.TargetSeat == 2) && Events<MouGuanPingCompletedEvent>().Any(e => e.UseId == first.UseId && e.Mask == 7 && e.Count == 3),
            "All three distinct native options suppress the actual target for this turn, draw once, grant the real additional skill use and add one damage only to the original out-of-range Slash.");
        Need(!game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.ConversionSource is null), "The source-specific Wuwei exemption does not restore ordinary Slash quota.");
        Wuwei(false); var second = Receipt()!.Use;
        Need(second.Extra && second.TypeCount == 3, "The added activation pays the new current complete black Hand and consumes one actual-turn extra-use token.");
        Choice(1); Choice(1); Choice(3); Play();
        Need(Events<MouGuanPingOptionEvent>().Where(e => e.UseId == second.UseId).Select(e => e.Option).SequenceEqual([1, 1, 3]) &&
            Events<MouGuanPingCompletedEvent>().Single(e => e.UseId == second.UseId).Mask == 5 && Player(2).Hp == 17 && Events<DamageAppliedEvent>().Last().Amount == 1 && Events<CardUseFinishedEvent>().Count(e => e.ResolutionId == second.UseId) == 1,
            "The user-selected repeated draw item executes twice within the frozen type budget and does not earn the all-three-distinct damage bonus.");
        Wuwei(false); var third = Receipt()!.Use;
        Need(third.Extra && third.TypeCount == 1, "The second added activation sees only its newly drawn current basic materials.");
        Choice(1); Play();
        var uses = Events<MouGuanPingUseIssuedEvent>().Select(e => e.Receipt).ToArray();
        Need(uses.Length == 3 && uses.All(u => !Events<CardUseDebitRecordedEvent>().Any(e => e.Debit.CardActionId == u.ActionId)) &&
            Player(0).HandCount == 1 && !game.GetHumanLegalActions().Any(a => a.SkillId == "mou:wuwei") && Events<MouGuanPingDrawReturnedEvent>().Length == 4,
            "Only the genuinely earned Wuwei uses run, all are exempt from ordinary Slash debit, and the native draw returns occur once per chosen draw item.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-guan-ping", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Slash, CardKind.Dodge, CardKind.DrawTwo, CardKind.Crossbow, CardKind.IronChain, CardKind.ZhangbaSerpentSpear })
            { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mgp-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            b.AddGeneral(new(Plain, "真实武威目标", "", "standard:none", "wei", 20));
            b.AddDeck(new(Deck, "固定真实黑闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("fixture:mgp-" + CardKind.Dodge, Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋关平真实主流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
