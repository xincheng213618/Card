using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouZhangXiuAnniversaryChecks
{
    private const string Hero = "mou:zhang-xiu", Plain = "fixture:mzxa-plain", Driver = "fixture:mzxa-driver", Hold = "fixture:mzxa-hold", Deck = "fixture:mzxa-deck";
    public static void ActualFuxiBranchesHaoyiEntitiesLightningAndCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        ShanheStartingCard[] hand = [new(CardKind.Slash, Suit.Spade, 1), new(CardKind.Slash, Suit.Club, 2), new(CardKind.Duel, Suit.Diamond, 3), new(CardKind.Lightning, Suit.Spade, 4), new(CardKind.Peach, Suit.Heart, 5), new(CardKind.Dodge, Suit.Club, 6)];
        var setup = new ShanheBattleSetup(7, 1, "mou-zhang-xiu-anniversary-native", Hero, 4, 0, 0, 0, 0, [Driver, Hold], hand, [],
            [new(Plain, Hp: 16) { StartingHand = [] }, new(Plain, Hp: 16) { StartingHand = [] }],
            [new(Plain, Hp: 16) { StartingHand = Enumerable.Range(0, 16).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i % 13 + 1)).ToArray() }], new string('b', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouZhangXiuAnniversaryReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouZhangXiuAnniversary is not null)?.MouZhangXiuAnniversary;
        CardLocation Position(int id) => game.CardMovements.Last(m => m.CardId == id).To;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(), Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.HandCount }), Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command) { try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); } catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); } }
        void Answer(Func<PromptChoice, bool> pick) { var p = Prompt(); Need(p?.PlayerSeat == 0, "Only the actual human chooser is answered."); var c = p!.Choices.FirstOrDefault(pick); Need(c is not null, "Required actual Zhang Xiu choice missing."); Submit(new AnswerPromptCommand(0, p.PromptId, c!.Id, game.Revision)); }
        bool Offer(string id) => Prompt() is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == id);
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 200; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal at " + line);
                if (Prompt() is not { PlayerSeat: 0 } p) { Submit(new AdvanceOneStepCommand(game.Revision)); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0); continue; }
                if (p.Kind == DecisionKind.DiscardCards) { Submit(new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision)); continue; }
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                throw new InvalidOperationException("Unexpected actual human input at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded actual Zhang Xiu boundary not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Native(int card, int? target = null)
        {
            var a = game.GetHumanLegalActions().FirstOrDefault(a => a.CardId == card && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && (target is null || a.TargetSeats.SequenceEqual([target.Value]))); Need(a is not null, "Required physical native Use is legal.");
            Submit(new PlayCardCommand(0, card, a!.TargetSeats, game.Revision, Prompt()!.PromptId, a.PlayedCardKind, a.TargetCardId)); Play();
        }
        void Discard(int card) { Submit(new UseProgramSkillCommand(0, Driver, "discard", [card], [], game.Revision, Prompt()!.PromptId)); Play(); }
        void Activate(string id) { Reach(() => Offer(id)); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); }
        void EndPlay() => Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId));
        Submit(new StartGameCommand()); Play();
        var initial = Player(0).Hand.ToArray(); var used = initial.Single(c => c.Kind == CardKind.Slash && c.Rank == 1).Id; var dropped = initial.Single(c => c.Kind == CardKind.Slash && c.Rank == 2).Id; var duel = initial.Single(c => c.Kind == CardKind.Duel).Id; var lightning = initial.Single(c => c.Kind == CardKind.Lightning).Id; var preexisting = initial.Single(c => c.Kind == CardKind.Dodge && c.Rank == 6).Id;
        Native(used, 3); Discard(dropped); Discard(duel); Native(lightning);
        Need(Events<MouZhangXiuAnniversaryDamageEvent>().Any(e => e.CardId == used && e.Turn == 1 && e.Amount == 1) && Position(lightning) == CardLocation.Judgment(0), "The first physical Slash really deals native damage, while the physical Lightning is truly placed for the next owner's Judgment.");
        EndPlay(); Activate("mou:haoyi"); Reach(() => Prompt()?.SkillPrompt?.SkillId == Hold);
        var original = Receipt(); var frame = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouZhangXiuAnniversary is not null).Id;
        Need(original is { Stage: MouZhangXiuAnniversaryStage.ObtainChildren } && original.Gained.Order().SequenceEqual(new[] { dropped, duel }.Order()) && original.Cards.Select(c => c.Id).Order().SequenceEqual(original.Gained.Order()) && Position(used) == CardLocation.DiscardPile && Position(preexisting) == CardLocation.Hand(0), "Haoyi actually obtains only this turn's undamaging damage-card entities and freezes exactly the paid incoming cards before its child observer.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0));
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == frame && f.MouZhangXiuAnniversary?.Gained.Order().SequenceEqual(new[] { dropped, duel }.Order()) == true), "Cold preserves the actually paid incoming entities and original Haoyi distribution owner frame.");
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Reach(() => Receipt()?.Stage == MouZhangXiuAnniversaryStage.DistributionCard);
        Need(Prompt()!.Choices.Where(c => c.Cards.Count > 0).SelectMany(c => c.Cards).Order().SequenceEqual(new[] { dropped, duel }.Order()) && !Prompt()!.Choices.Any(c => c.Cards.Contains(preexisting)), "Distribution offers the actual acquired subset, never an equally sized selection from preexisting Hand or equipment.");
        Answer(c => c.Cards.SequenceEqual([duel])); Answer(c => c.Targets.SequenceEqual([1])); Reach(() => Receipt()?.Stage == MouZhangXiuAnniversaryStage.DistributionCard); Answer(c => c.Parameters.GetValueOrDefault("branch") == "stop");
        Reach(() => Events<TurnEndedEvent>().Any(e => e.TurnNumber == 1 && e.ActorSeat == 0));
        Need(Position(duel) == CardLocation.Hand(1) && Position(dropped) == CardLocation.Hand(0) && Events<MouZhangXiuAnniversaryMovedEvent>().Count(e => e.FrameId == frame && e.Stage == MouZhangXiuAnniversaryStage.ObtainChildren) == 2 && Events<MouZhangXiuAnniversaryMovedEvent>().Count(e => e.FrameId == frame && e.Stage == MouZhangXiuAnniversaryStage.DistributionChildren) == 1, "The restored claim pays once; native partial distribution gives the chosen entity and retains the remainder.");
        Activate("mou:fuxi"); Reach(() => Receipt()?.Stage == MouZhangXiuAnniversaryStage.Branch); Need(Receipt()?.Target == 1 && game.State.TurnNumber == 2, "Fuxi belongs to the actual other player's Play and accepts the largest current Hand.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "give"); Answer(c => c.Cards.SequenceEqual([preexisting]));
        Reach(() => Events<MouZhangXiuAnniversaryDrawReturnedEvent>().Length == 1);
        Need(Position(preexisting) == CardLocation.Hand(1) && Events<MouZhangXiuAnniversaryDrawReturnedEvent>() is [{ Actual: 2, Replacement: 0 }] && Events<MouZhangXiuAnniversaryMovedEvent>().Any(e => e.Stage == MouZhangXiuAnniversaryStage.GiveChildren && e.CardId == preexisting && e.From == CardLocation.Hand(0) && e.To == CardLocation.Hand(1)), "Fuxi's give branch transfers the chosen real HE entity before the genuine native two-card draw.");
        Play(); Need(game.State.TurnNumber == 5 && Events<LightningResolvedEvent>().Any(e => e.CardId == lightning && e.Hit) && Events<MouZhangXiuAnniversaryDamageEvent>().Any(e => e.CardId == lightning && e.Turn == 5 && e.Delayed && e.Amount == 3) && Position(lightning) == CardLocation.DiscardPile, "The same physical Lightning actually hits during the next real Judgment and its own delayed native damage invoice is recorded before entering Discard.");
        Discard(dropped); EndPlay(); Activate("mou:haoyi"); Reach(() => Receipt()?.Stage == MouZhangXiuAnniversaryStage.DistributionCard);
        Need(Receipt()!.Gained.SequenceEqual([dropped]) && !Receipt()!.Cards.Any(c => c.Id == lightning) && Position(lightning) == CardLocation.DiscardPile, "This actual turn's damaging Lightning remains in Discard and is excluded while the newly discarded undamaging Slash is genuinely reacquired.");
        Answer(c => c.Parameters.GetValueOrDefault("branch") == "stop");
        Activate("mou:fuxi"); Reach(() => Receipt()?.Stage == MouZhangXiuAnniversaryStage.Branch); Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard"); Answer(c => c.Parameters.GetValueOrDefault("branch") == "hand");
        Reach(() => Events<MouZhangXiuAnniversarySlashReturnedEvent>().Length == 1);
        var issued = Events<MouZhangXiuAnniversarySlashIssuedEvent>().Single();
        Need(issued.Origin.Target == 1 && Events<MouZhangXiuAnniversaryMovedEvent>().Count(e => e.Stage == MouZhangXiuAnniversaryStage.DiscardChildren) == 1 && Events<CardUseDeclaredEvent>().Any(e => e.ResolutionId == issued.Origin.UseId && e.SourceSeat == 0 && e.CardId == 0 && e.CardKind == CardKind.Slash) && Events<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.Origin.UseId) == 1 && !Events<CardUseDebitRecordedEvent>().Any(e => e.Debit.CardActionId == issued.Origin.ActionId) && Events<CardActionAcceptedEvent>().Any(e => e.Action.Type == CardActionType.Response && e.Action.ActorSeat == 1 && e.Action.EffectiveKind == CardKind.Dodge), "After a real opposing-Hand discard, Fuxi issues and fully returns one genuine zero-material Slash; the defender really responds and this exact forced Use preserves the actor's ordinary Slash allowance.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-zhang-xiu-anniversary", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in new[] { CardKind.Slash, CardKind.Duel, CardKind.Lightning, CardKind.Peach, CardKind.Dodge }) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mzxa-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"room","query":"handLimit","operation":"add","value":20,"priority":0,"condition":{"kind":"always"} }],"activations":[{"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}]},
                  {"id":"{{Hold}}","revision":1,"triggers":[{"id":"actual-paid-claim","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mou:haoyi.claim"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var labels = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Driver] = new { name = "真实弃牌", description = "只提供原生逻辑弃置与手牌空间。" }, [Hold] = new { name = "实际豪义获得", description = "原生获得后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
            var catalog = SkillProgramCatalog.Load(rules, labels); foreach (var id in new[] { Driver, Hold }) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Plain, "真实张绣目标", "", "standard:none", "wei", 16));
            b.AddDeck(new(Deck, "固定闪电命中牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("fixture:mzxa-" + CardKind.Dodge, Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋张绣真实原生流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
