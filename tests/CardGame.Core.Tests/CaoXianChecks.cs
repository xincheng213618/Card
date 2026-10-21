using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class CaoXianChecks
{
    private const string Hero = "ol:cao-xian", Plain = "fixture:cx-plain", Extra = "fixture:cx-extra", Hold = "fixture:cx-hold", Deck = "fixture:cx-deck";
    public static void ActualWingsCompletedUsesRemovalAdjustmentOptionsAndCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        ShanheStartingCard[] hand = [new(CardKind.Dodge, Suit.Spade, 1), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3), new(CardKind.Dodge, Suit.Diamond, 4), new(CardKind.Dodge, Suit.Spade, 5), new(CardKind.DrawTwo, Suit.Club, 6), new(CardKind.DrawTwo, Suit.Club, 7), new(CardKind.DrawTwo, Suit.Club, 8)];
        var setup = new ShanheBattleSetup(7, 1, "cao-xian-native", Hero, 3, 0, 0, 0, 0, [Extra, Hold], hand, [new(CardKind.SilverLion, Suit.Diamond, 9)],
            [new(Plain, Hp: 12) { StartingHand = [] }, new(Plain, Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Club, 12), new(CardKind.Peach, Suit.Diamond, 13)] }],
            [new(Plain, Hp: 12) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 11)] }], new string('e', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        CaoXianReceipt? Receipt(SkillProgramEffectOp? op = null) => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.CaoXian is { } r && (op is null || r.Operation == op))?.CaoXian;
        CardLocation Position(int id) => game.CardMovements.Last(m => m.CardId == id).To;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(), Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.Hp, p.HandCount }), Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command) { try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Actual Cao Xian command rejected."); } catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); } }
        void Answer(Func<PromptChoice, bool> pick) { var p = Prompt(); Need(p?.PlayerSeat == 0, "The actual human chooser answers the current original prompt."); var choice = p!.Choices.FirstOrDefault(pick); Need(choice is not null, "Required actual Cao Xian choice missing."); Submit(new AnswerPromptCommand(0, p.PromptId, choice!.Id, game.Revision)); }
        bool Offer(string id) => Prompt() is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == id);
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal at " + line);
                if (Prompt() is not { PlayerSeat: 0 } p) { Submit(new AdvanceOneStepCommand(game.Revision)); continue; }
                if (Receipt()?.Stage == CaoXianStage.AdjustCards) { Answer(c => c.Cards is [var card] && Player(0).Hand.Single(h => h.Id == card).Kind != CardKind.DrawTwo); continue; }
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0); continue; }
                if (p.Kind == DecisionKind.DiscardCards) { Submit(new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision)); continue; }
                throw new InvalidOperationException("Unexpected actual human input at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded actual Cao Xian boundary not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Activate(string id) { Reach(() => Offer(id)); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
        void Store(params int[] ids) { Activate("ol:lingxi"); Reach(() => Receipt()?.Stage == CaoXianStage.StoreCards); foreach (var id in ids) Answer(c => c.Cards.SequenceEqual([id])); if (Receipt()?.Stage == CaoXianStage.StoreCards) Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish"); }
        void Use(int id) { var a = game.GetHumanLegalActions().FirstOrDefault(a => a.CardId == id && a.ConversionSource is null && a.Kind == LegalActionKind.DrawTwo); Need(a is not null, "The actual physical DrawTwo is admitted by ordinary native rules."); Submit(new PlayCardCommand(0, id, a!.TargetSeats, game.Revision, Prompt()!.PromptId)); }
        void Remove(params int[] ids) { Activate("ol:zhifou"); Reach(() => Receipt(SkillProgramEffectOp.ResolveCaoXianZhifou)?.Stage == CaoXianStage.RemoveCards); foreach (var id in ids) Answer(c => c.Cards.SequenceEqual([id])); if (Receipt(SkillProgramEffectOp.ResolveCaoXianZhifou)?.Stage == CaoXianStage.RemoveCards) Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish"); }
        void Option(int option, int target) { Reach(() => Receipt(SkillProgramEffectOp.ResolveCaoXianZhifou)?.Stage == CaoXianStage.OptionTarget); Answer(c => c.Parameters.GetValueOrDefault("option") == option.ToString() && c.Targets.SequenceEqual([target])); Play(); }
        Submit(new StartGameCommand()); Reach(() => Offer("ol:lingxi"));
        var original = Player(0).Hand.ToArray(); int Dodge(int rank) => original.Single(c => c.Kind == CardKind.Dodge && c.Rank == rank).Id;
        var first = Dodge(1); var second = Dodge(2); var third = Dodge(3); var fourth = Dodge(4); var fifth = Dodge(5); var lion = Player(0).Equipment.Single(c => c.Kind == CardKind.SilverLion).Id;
        var uses = original.Where(c => c.Kind == CardKind.DrawTwo).OrderBy(c => c.Rank).Select(c => c.Id).ToArray(); var gift = game.CreateSnapshot(1).Players[1].Hand.Single().Id;
        var enemyDiscard = game.CreateSnapshot(3).Players[3].Hand.Select(c => c.Id).ToArray();
        Store(lion, first, second); Play();
        Need(Player(0).PublicPersistentPileCards?.Select(c => c.Id).Order().SequenceEqual(new[] { lion, first, second }.Order()) == true && Position(lion).Zone == CardZoneKind.PublicPersistentPile && Player(0).Equipment.Count == 0, "The first actual Play deposits two real Hand entities and the original physical equipment into its public source-specific wings.");
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId)); Store(third, fourth, fifth);
        Reach(() => Offer("ol:lingxi")); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play();
        Need(Player(0).PublicPersistentPileCards?.Count == 6 && game.State.TurnNumber == 1, "The same actual turn's real inserted-Play end deposits another three; MaxHP bounds each deposit and never silently caps the total pile at three.");
        Use(uses[0]); Remove(lion); Reach(() => Prompt()?.SkillPrompt?.SkillId == Hold);
        var removal = Events<CaoXianZhifouRemovedEvent>().Single(); var frame = removal.FrameId;
        Need(removal.CardId == lion && removal.ActualTurn == 1 && removal.Source.SkillId == "ol:zhifou" && removal.Pile.SkillId == "ol:lingxi" && Position(lion) == CardLocation.OutsideGame && game.CardMovements.Single(m => m.Sequence == removal.Sequence).From == removal.Pile.Location && Events<CaoXianZhifouPaidEvent>() is [{ X: 1 }] && Receipt(SkillProgramEffectOp.ResolveCaoXianZhifou)?.Stage == CaoXianStage.RemoveChildren, "The first completed real Use pays one genuine wing directly into Outside and issues an exact scalar receipt from the actual physical callback, without inventing a preceding Discard entry.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0)); game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && Events<CaoXianZhifouRemovedEvent>().Single() == removal && Events<CaoXianZhifouPaidEvent>().Length == 1, "Cold preserves the original completed Use, source-specific pile and already-paid removal invoice while its actual movement observer is pending.");
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(() => Receipt(SkillProgramEffectOp.ResolveCaoXianZhifou)?.Stage == CaoXianStage.OptionTarget);
        var adjust1 = Events<CaoXianAdjustedEvent>().Last();
        Need(adjust1.RequiredHand == 8 && Player(0).HandCount == 8 && Events<CaoXianDrawReturnedEvent>().Any(e => e.Requested == 2 && e.Actual == 2), "After the genuine removal the four remaining wing suits drive a real two-card native Hand-adjustment draw before the original Zhifou option continues.");
        Option(1, 1); Need(Position(gift) == removal.Pile.Location && Player(1).HandCount == 0, "The selected role really places its chosen current Hand entity into Cao Xian's same wings source.");
        Use(uses[1]); Remove(first, third);
        Reach(() => Receipt(SkillProgramEffectOp.ResolveCaoXianZhifou)?.Stage == CaoXianStage.OptionTarget);
        Need(Events<CaoXianZhifouPaidEvent>().Select(e => e.X).SequenceEqual([1, 2]) && Player(0).HandCount == 6 && !Prompt()!.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "1"), "The second accepted paid invocation truly costs two wings, adjusts by native logical Hand discard to the three remaining suits and excludes the already-used option.");
        Option(2, 3); Need(enemyDiscard.All(id => Position(id) == CardLocation.DiscardPile) && Player(3).HandCount == 0, "The target's second option really discards its own two physical HE entities.");
        Use(uses[2]); Remove(second, fourth, fifth);
        Reach(() => Receipt(SkillProgramEffectOp.ResolveCaoXianZhifou)?.Stage == CaoXianStage.OptionTarget);
        Need(Events<CaoXianZhifouPaidEvent>().Select(e => e.X).SequenceEqual([1, 2, 3]) && Player(0).HandCount == 2 && Prompt()!.Choices.All(c => c.Parameters.GetValueOrDefault("option") == "3"), "The third actual completed Use costs three wings; only the actual donor's Heart remains, so real Hand adjustment leaves two and only the unspent third option remains.");
        Option(3, 2);
        Need(Player(2).Hp == 11 && Events<CaoXianOptionEvent>().Select(e => e.Option).SequenceEqual([1, 2, 3]) && Events<CaoXianZhifouRemovedEvent>().Length == 6 && Events<CaoXianZhifouPaidEvent>().Length == 3 && Events<CaoXianZhifouRemovedEvent>().All(e => e.Pile == removal.Pile && game.CardMovements.Any(m => m.CardId == e.CardId && m.Sequence == e.Sequence && m.From == e.Pile.Location && m.To == CardLocation.OutsideGame)) && uses.All(id => Events<CardUseFinishedEvent>().Count(e => e.ResolutionId == Events<CardUseDeclaredEvent>().Single(d => d.CardId == id).ResolutionId) == 1), "All three original physical Uses fully finish once, the three distinct options pay native effects, and every claimable Outside wing has its exact real source/sequence invoice.");
        Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId)); Activate("ol:lingxi"); Reach(() => Receipt()?.Stage == CaoXianStage.StoreCards);
        foreach (var id in Player(0).Hand.Select(c => c.Id).ToArray()) Answer(c => c.Cards.SequenceEqual([id]));
        if (Receipt()?.Stage == CaoXianStage.StoreCards) Answer(c => c.Parameters.GetValueOrDefault("branch") == "finish");
        Reach(() => Events<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == 1));
        Need(Player(0).HandCount == 0 && Player(0).PublicPersistentPileCards?.Count == 3 && Events<CaoXianZhifouPaidEvent>().Count(e => e.FrameId == frame) == 1 && Events<CaoXianZhifouRemovedEvent>().Count(e => e.CardId == lion) == 1, "The original actual Play ends with a genuine final optional deposit, and the Cold-resumed first cost/removal remains once-only.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-cao-xian-native", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Dodge, CardKind.DrawTwo, CardKind.SilverLion, CardKind.Peach }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:cx-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Extra}}","revision":1,"triggers":[{"id":"actual-extra","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"insertPhase","target":"owner","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
                  {"id":"{{Hold}}","revision":1,"triggers":[{"id":"actual-wing-adjustment-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["skill-program.ol:lingxi.adjust-draw"],"movementOccurrence":"perBatch","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var labels = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Extra] = new { name = "实际前置出牌", description = "准备前插入一个实际出牌阶段。" }, [Hold] = new { name = "真实知否移去后", description = "原生翼移去的真实移动子窗口暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
            var catalog = SkillProgramCatalog.Load(rules, labels); foreach (var id in new[] { Extra, Hold }) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Plain, "真实曹宪目标", "", "standard:none", "wei", 12));
            b.AddDeck(new(Deck, "固定真实闪牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("fixture:cx-" + CardKind.Dodge, Suit.Spade, 10)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "曹宪真实翼与知否流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
