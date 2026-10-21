using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MouSimaShiChecks
{
    private const string Hero = "mou:sima-shi", Plain = "fixture:mss-plain", Driver = "fixture:mss-driver", Quiet = "fixture:mss-quiet", Hold = "fixture:mss-hold", Deck = "fixture:mss-deck";
    private static readonly CardKind[] Kinds = [CardKind.Slash, CardKind.Dodge, CardKind.DrawTwo, CardKind.Alcohol, CardKind.SilverLion, CardKind.Peach, CardKind.Slash, CardKind.Dodge, CardKind.IronChain, CardKind.Duel, CardKind.Snatch, CardKind.Crossbow, CardKind.SupplyShortage];
    public static void ActualMarksUsesDiscardClaimPhaseTailDeathShuffleAndCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var setup = new ShanheBattleSetup(7, 1, "mou-sima-shi-native", Hero, 3, 0, 0, 0, 0, [Driver, Hold], [new(CardKind.Slash, Suit.Spade, 1)], [new(CardKind.Crossbow, Suit.Club, 12)],
            [new(Plain, Hp: 16) { StartingHand = [] }, new(Plain, Hp: 16) { StartingHand = Enumerable.Range(0, 16).Select(i => new ShanheStartingCard(CardKind.Dodge, Suit.Club, i % 13 + 1)).ToArray() }],
            [new(Plain, Hp: 16) { StartingHand = [] }], new string('a', 64));
        var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 9 }, registry);
        PendingDecision? Prompt() => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        PlayerSnapshot Player(int seat) => game.CreateSnapshot(0).Players[seat];
        T[] Events<T>() => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
        MouSimaShiReceipt? Receipt() => game.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.MouSimaShi is not null)?.MouSimaShi;
        CardLocation Position(int card) => game.CardMovements.Last(m => m.CardId == card).To;
        string Diagnostic() => JsonSerializer.Serialize(new { game.State.TurnNumber, game.State.CurrentSeat, game.State.Phase, Prompt = Prompt(), Receipt = Receipt(), Players = game.CreateSnapshot(0).Players.Select(p => new { p.Seat, p.GeneralId, p.Hp, p.MaxHp, p.HandCount }), Death = new { Bindings = Events<ProgramBindingResolvedEvent>().Where(e => e.Window == SkillProgramTriggerWindow.OwnerDied), Started = Events<MouSimaShiStartedEvent>().Where(e => e.Operation == SkillProgramEffectOp.ReturnMouSimaShiChenlve), Settled = Events<MouSimaShiDeathSettledEvent>(), Returns = Events<MouSimaShiMovedEvent>().Where(e => e.Operation == SkillProgramEffectOp.ReturnMouSimaShiChenlve), Shuffles = Events<MouSimaShiShuffledEvent>() }, Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(30).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
        void Need(bool yes, string why) { if (!yes) throw new InvalidOperationException(why + Diagnostic()); }
        void Submit(GameCommand command) { try { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Actual native command rejected."); } catch (Exception ex) { throw new InvalidOperationException(ex.Message + Diagnostic(), ex); } }
        void Answer(Func<PromptChoice, bool> pick) { var p = Prompt(); Need(p?.PlayerSeat == 0, "Answer only the actual human chooser."); var c = p!.Choices.FirstOrDefault(pick); Need(c is not null, "Required native Sima Shi choice missing."); Submit(new AnswerPromptCommand(0, p.PromptId, c!.Id, game.Revision)); }
        void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
        {
            for (var i = 0; i < 240; i++)
            {
                if (done()) return; Need(game.State.Winner == Winner.None, "Unexpected terminal result at " + line);
                if (Prompt() is not { PlayerSeat: 0 } p) { Submit(new AdvanceOneStepCommand(game.Revision)); continue; }
                if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying) { Answer(c => c.Cards.Count == 0); continue; }
                if (p.Kind == DecisionKind.DiscardCards) { var marked = Events<MouSimaShiMarkedEvent>().Select(e => e.CardId).ToHashSet(); Submit(new DiscardCardsCommand(0, p.ValidCardIds.OrderBy(marked.Contains).Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision)); continue; }
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) { Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); continue; }
                throw new InvalidOperationException("Unexpected actual Human input at " + line + Diagnostic());
            }
            throw new InvalidOperationException("Bounded actual Sima Shi boundary not reached at " + line + Diagnostic());
        }
        void Play() => Reach(() => Prompt() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void DriverAction(string id, int? card = null, int? target = null) { Submit(new UseProgramSkillCommand(0, Driver, id, card is { } c ? [c] : [], target is { } t ? [t] : [], game.Revision, Prompt()!.PromptId)); Play(); }
        void Native(int card, int? target = null)
        {
            var a = game.GetHumanLegalActions().FirstOrDefault(a => a.CardId == card && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && (target is null || a.TargetSeats.SequenceEqual([target.Value])));
            Need(a is not null, "Required real native use is legal."); Submit(new PlayCardCommand(0, card, a!.TargetSeats, game.Revision, Prompt()!.PromptId, a.PlayedCardKind, a.TargetCardId));
        }
        Submit(new StartGameCommand()); Play();
        var marks = Events<MouSimaShiMarkedEvent>();
        Need(marks.Length == 13 && marks.Select(e => e.Rank).Order().SequenceEqual(Enumerable.Range(1, 13)) && marks.Select(e => e.CardId).Distinct().Count() == 13 && marks.All(e => e.RandomBefore != e.RandomAfter) && Events<MouSimaShiInitializedEvent>().Length == 1, "Sanshi marks one actual random remaining-Deck entity at each printed rank, once at real GameStarting.");
        var ordinary = Player(0).Hand.Single(c => !marks.Any(m => m.CardId == c.Id) && c.Kind == CardKind.Slash).Id;
        Native(ordinary, 3); Reach(() => Prompt() is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == "mou:zhenrao")); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(() => Receipt()?.Stage == MouSimaShiStage.Choosing); Answer(c => c.Targets.SequenceEqual([3])); Play();
        Need(Events<MouSimaShiTargetEvent>() is [{ Owner: 0, Target: 3, Turn: 1 }] && Events<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 3 && e.Amount == 1), "Zhenrao compares actual hand counts and deals genuine native damage to the selected actual target.");
        DriverAction("stock-twenty"); DriverAction("stock-four");
        Need(marks.All(m => Player(0).Hand.Any(c => c.Id == m.CardId)), "The fixed actual 26-card deck has been drawn without a search or synthetic marker injection.");
        var used = marks.First(m => m.Rank == 1).CardId; var dropped = marks.First(m => m.Rank == 2).CardId;
        var hp = Player(3).Hp; var responses = Events<CardActionAcceptedEvent>().Count(e => e.Action.ActorSeat == 3 && e.Action.Type == CardActionType.Response);
        Native(used, 3); Play();
        Need(Player(3).Hp == hp - 1 && Events<CardActionAcceptedEvent>().Count(e => e.Action.ActorSeat == 3 && e.Action.Type == CardActionType.Response) == responses && Events<IssuedCardNoResponseEvent>().Any(e => e.Effect.Source.SkillId == "mou:sanshi") && Events<MouSimaShiTargetEvent>().Length == 1, "A real marked Slash cannot be responded to despite the defender's real Dodges, and the same peer cannot reuse Zhenrao this actual turn.");
        DriverAction("discard", dropped); Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId)); Reach(() => Events<TurnEndedEvent>().Any(e => e.TurnNumber == 1 && e.ActorSeat == 0));
        Need(Position(dropped) == CardLocation.Hand(0) && Position(used) != CardLocation.Hand(0) && Events<MouSimaShiMovedEvent>().Count(e => e.Operation == SkillProgramEffectOp.RecoverMouSimaShiSanshi && e.CardId == dropped) == 1 && !Events<MouSimaShiMovedEvent>().Any(e => e.Operation == SkillProgramEffectOp.RecoverMouSimaShiSanshi && e.CardId == used), "The ordinary logical discard returns at actual TurnEnding while the owner's actual used material remains excluded.");
        Play(); Need(game.State.TurnNumber == 5, "The next original owner Play follows the three real NPC turns.");
        var armor = marks.Single(m => m.Rank == 5).CardId; var delayed = marks.Single(m => m.Rank == 13).CardId; var toDeck = marks.Single(m => m.Rank == 3).CardId; var toOther = marks.Single(m => m.Rank == 10).CardId;
        Native(armor); Play(); Native(delayed, 3); Play(); DriverAction("top", toDeck); DriverAction("give", toOther, 3); DriverAction("discard", dropped);
        var expected = marks.Select(m => m.CardId).Where(id => Position(id) != CardLocation.Hand(0) && Position(id).Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.DrawPile or CardZoneKind.DiscardPile).Order().ToArray();
        var excluded = marks.Select(m => m.CardId).Except(expected).Order().ToArray();
        Need(expected.Contains(armor) && expected.Contains(delayed) && expected.Contains(toDeck) && expected.Contains(toOther) && expected.Contains(dropped) && excluded.Length > 0, "The actual claim begins with field, Deck, Discard, other-Hand sources plus marked entities already in the owner's Hand that must stay outside the frozen claim.");
        Submit(new UseProgramSkillCommand(0, "mou:chenlve", "claim-dead-soldiers", [], [], game.Revision, Prompt()!.PromptId)); Reach(() => Prompt()?.SkillPrompt?.SkillId == Hold);
        var issued = Events<MouSimaShiClaimIssuedEvent>().Single(); var frozen = Receipt();
        Need(frozen is { Stage: MouSimaShiStage.MovementChildren, Index: 1 } && frozen.Cards.Select(c => c.Id).Order().SequenceEqual(expected) && Events<MouSimaShiMovedEvent>().Count(e => e.FrameId == issued.FrameId && e.Operation == SkillProgramEffectOp.ResolveMouSimaShiChenlve) == 1, "The native claim's first actually paid entity has suspended its exact owner frame before later claim movements.");
        var before = SnapshotJson.Serialize(game.CreateSnapshot(0)); var frozenId = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouSimaShi is not null).Id;
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(before == SnapshotJson.Serialize(game.CreateSnapshot(0)) && game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == frozenId && f.MouSimaShi?.Index == 1), "Cold keeps the already paid first claim, original actual-Play key and frozen remaining physical cursor unchanged.");
        Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play();
        var claims = Events<MouSimaShiMovedEvent>().Where(e => e.FrameId == issued.FrameId && e.Operation == SkillProgramEffectOp.ResolveMouSimaShiChenlve && e.To == CardLocation.Hand(0)).ToArray();
        Need(claims.Select(e => e.CardId).Order().SequenceEqual(expected) && claims.Select(e => e.CardId).Distinct().Count() == claims.Length && expected.All(id => Position(id) == CardLocation.Hand(0)), "The cold-returned native claim obtains each originally eligible entity exactly once and preserves preexisting own Hand exclusions.");
        DriverAction("top", toDeck); DriverAction("give", toOther, 3); DriverAction("discard", dropped); Native(armor); Play(); Native(delayed, 3); Play();
        DriverAction("lose-chenlve"); Need(!Player(0).Skills!.Any(s => s.ContentId == "mou:chenlve") && Events<ProgramOwnerSkillsReplacedEvent>().Any(e => e.OwnerSeat == 0 && e.SkillId == Driver && e.LostSkillIds.Contains("mou:chenlve")), "The retained native skill-loss operation truly removes Chenlve before its already accepted original-Play quarantine and later death return.");
        DriverAction("arm-death"); Submit(new EndPlayPhaseCommand(0, game.Revision, Prompt()!.PromptId));
        Reach(() => Events<MouSimaShiShuffledEvent>().Length == 1);
        var removals = Events<MouSimaShiRemovedEvent>().Where(e => e.DebtId == issued.FrameId).ToArray(); var returns = Events<MouSimaShiMovedEvent>().Where(e => e.Operation == SkillProgramEffectOp.ReturnMouSimaShiChenlve && e.DebtId == issued.FrameId).ToArray();
        Need(removals.Select(e => e.CardId).Order().SequenceEqual(expected) && returns.Select(e => e.CardId).Order().SequenceEqual(expected) && returns.All(e => e.From == CardLocation.OutsideGame && e.To == CardLocation.DrawPile && e.Sequence > removals.Single(m => m.CardId == e.CardId).Sequence) && excluded.All(id => !removals.Any(e => e.CardId == id)), "At the original actual Play tail the exact obtained entities leave their current Deck/Hand/Equipment/Judgment/Discard regions, while preexisting own Hand marks are not exiled; only the same actual quarantined entities return on real owner death.");
        Need(Player(0).Hp <= 0 && Events<MouSimaShiPhaseSettledEvent>().Count(e => e.DebtId == issued.FrameId) == 1 && Events<MouSimaShiDeathSettledEvent>().Count(e => e.DebtId == issued.FrameId) == 1 && Events<MouSimaShiShuffledEvent>() is [{ RandomBefore: var rngBefore, RandomAfter: var rngAfter }] && rngBefore != rngAfter && returns.All(e => Position(e.CardId) == CardLocation.DrawPile), "Actual death returns the exile once, then changes the real native shuffle RNG; phase-tail settlement precedes the genuine owner death rather than assigning HP or manufacturing a shuffle.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-sima-shi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            foreach (var kind in Kinds.Distinct()) { var c = CardCatalog.Get(kind); b.AddCard(new("fixture:mss-" + kind, c.DisplayName, c.CategoryName, c.Description, kind)); }
            var rules = $$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"states":[{"id":"death","initialValue":false,"visibility":"public","resetScope":"game","reacquirePolicy":"preserveUntilGameEnd"}],"modifiers":[{"id":"hand-room","query":"handLimit","operation":"add","value":20,"priority":0,"condition":{"kind":"always"} }],"activations":[
                    {"id":"stock-twenty","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                    {"id":"stock-four","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":4}]},
                    {"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                    {"id":"top","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"top"},{"op":"moveBoundCards","target":"owner","sourceBind":"top","destination":"drawPileTop","awaitMovementTriggers":true}]},
                    {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gift"},{"op":"moveBoundCards","target":"owner","sourceBind":"gift","destination":"selectedTargetHand"}]},
                    {"id":"arm-death","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setBooleanState","target":"owner","stateId":"death","value":true}]},
                    {"id":"lose-chenlve","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["mou:chenlve"],"sourceBind":"standard:none"}]}
                  ],"triggers":[{"id":"real-owner-death-after-play-tail","window":"discardPhaseStarting","subject":"owner","condition":{"kind":"booleanState","stateId":"death","expectedValue":true},"optional":false,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]},
                  {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                  {"id":"{{Hold}}","revision":1,"triggers":[{"id":"first-real-claim","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.mou:sima-shi.ResolveMouSimaShiChenlve"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"continue","options":[{"id":"continue"}]}]}]}
                ]}
                """;
            var labels = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> { [Driver] = new { name = "真实实体与死亡", description = "原生摸牌、弃置、置顶、交给及出牌尾后实际死亡。" }, [Quiet] = new { name = "安静同行", description = "只为NPC跳过出牌阶段。" }, [Hold] = new { name = "真实沉略获得", description = "本次第一张真实沉略获得后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } });
            var catalog = SkillProgramCatalog.Load(rules, labels); foreach (var id in new[] { Driver, Quiet, Hold }) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = catalog.Programs[id], ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Plain, "真实司马师目标", "", "standard:none", "wei", 16, [Quiet]));
            b.AddDeck(new(Deck, "固定各点数双实体", 0, 2, []) { PhysicalCards = Kinds.SelectMany((kind, i) => Enumerable.Range(0, 2).Select(_ => new ContentDeckPhysicalCard("fixture:mss-" + kind, i % 2 == 0 ? Suit.Spade : Suit.Club, i + 1))).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋司马师原生实体流程", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Plain], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
