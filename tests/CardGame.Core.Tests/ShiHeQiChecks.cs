using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShiHeQiChecks
{
    private const string Hero = "mobile:shi-he-qi", Shanxi = "mobile:shi-shanxi", Qizhou = "mobile:shi-qizhou",
        Driver = "fixture:shi-he-qi-driver", Watch = "fixture:shi-he-qi-watch", Quiet = "fixture:shi-he-qi-quiet", Aggressor = "fixture:shi-he-qi-aggressor",
        Spent = "fixture:shi-he-qi-spent", Peer = "fixture:shi-he-qi-peer", ActivePeer = "fixture:shi-he-qi-active-peer", Deck = "fixture:shi-he-qi-deck";

    public static void FieldEntriesNextMarkRewardNativeDuelTargetQuotaAndPaidDrawCold()
    {
        var s = Scene.Start(watchDraw: true); s.DrawChildren(cold: true); s.Play();
        var initialDraw = s.E<ShiHqDrawPaidEvent>().Single();
        s.Need(initialDraw.Count == 2 && s.V(0).Hand.Count(c => Marked(c)) == 2 && s.Reward == false,
            "The separate draw-phase native extra draw marks exactly its two actual hand entities and does not arm or consume the next-mark reward.");
        var slash = s.V(0).Hand.Single(c => c.Kind == CardKind.Slash).Id;
        s.Native(CardKind.BaguaFormation, []); s.Mark(0, slash); s.Play();
        s.Need(s.Reward && s.E<ShiHqSelectedEvent>().Single() is { RewardConsumed: false, NextReward: true } && s.E<ShiHqDrawPaidEvent>().Length == 1,
            "The first actual equipment-entry marking of Slash signs the next invocation only, without immediately paying itself.");
        var other = s.V(1).Hand.First(c => c.Kind == CardKind.Dodge).Id;
        s.Native(CardKind.Crossbow, []); s.Mark(1, other);
        s.DrawChildren(cold: true); s.Play();
        var selected = s.E<ShiHqSelectedEvent>().Last();
        s.Need(!s.Reward && selected is { RewardConsumed: true, NextReward: false } && s.E<ShiHqDrawPaidEvent>().Length == 2 &&
            s.E<ShiHqDrawPaidEvent>().All(e => e.Count == 2) && s.V(0).Hand.Count(Marked) == 5 && Marked(s.V(1).Hand.Single(c => c.Id == other)),
            "The second successful mark consumes the previous reward, marks exactly the new two native draw entities, and a non-Slash does not renew it.");
        var unmarked = s.V(0).Hand.First(c => !Marked(c));
        s.Need(s.G.GetHumanLegalActions().All(a => a.CardId != unmarked.Id || a.ConversionSource?.SkillId != Shanxi), "An unmarked entity cannot be a Shanxi material.");
        s.Duel(1); s.Play();
        s.Need(s.G.GetHumanLegalActions().All(a => a.ConversionSource?.SkillId != Shanxi || !a.TargetSeats.Contains(1)) &&
            s.G.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Shanxi && a.TargetSeats.SequenceEqual([2])),
            "The accepted native Duel spends only that actual Play target, while a different living target remains legal.");
        s.Duel(2); s.Play();
        var uses = s.E<ShiHqShanxiUsedEvent>();
        s.Need(uses.Length == 2 && uses.Select(e => e.Target).SequenceEqual([1, 2]) && uses.Select(e => e.PhaseInstance).Distinct().Count() == 1 &&
            uses.All(e => s.E<ProgramViewAsConvertedEvent>().Count(c => c.FrameId == e.UseId && c.SkillId == Shanxi && c.OutputKind == CardKind.Duel && c.PhysicalCardIds.SequenceEqual([e.CardId])) == 1),
            "Each target ledger entry belongs to one genuine marked single-material conversion and its original native Duel action.");
        s.End(); s.AtWatch(); s.ContinueWatch(); s.Offer();
        s.Need(s.G.State.CurrentSeat == 1 && s.V(0).Judgment.Any(c => c.Kind == CardKind.Indulgence) &&
            s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.Movements.Any(m => m.To == CardLocation.Judgment(0) && m.From.OwnerSeat != 0)),
            "An enemy's true delayed-trick entry to this judgment zone also offers Qizhou even when the owner lost no card in that batch.");
        s.Mark(0, s.V(0).Hand.First().Id, alreadyOffered: true); s.Replay();
    }

    public static void IssuedGlobalYingjiCleanupUsesPhysicalMoveAfterSourceLossCold()
    {
        var s = Scene.Start(watchDraw: false); s.Play();
        var foreign = s.V(1).Hand.First(c => c.Kind == CardKind.Dodge).Id;
        s.Native(CardKind.BaguaFormation, []); s.Mark(1, foreign); s.Play();
        var marked = Enumerable.Range(0, 4).SelectMany(i => s.V(i).Hand.Where(Marked).Select(c => c.Id)).Order().ToArray();
        var preserved = Enumerable.Range(0, 4).SelectMany(i => s.V(i).Hand.Where(c => !Marked(c)).Select(c => c.Id)).Order().ToArray();
        var equipment = s.V(0).Equipment.Single().Id;
        s.Accept(new UseProgramSkillCommand(0, Driver, "lose-qizhou", [], [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Need(s.V(0).Skills!.All(k => k.ContentId != Qizhou), "The real terminal fixture activation removes the source skill before the actual turn ends.");
        s.End(); s.AtWatch();
        var root = s.Root!; var receipt = root.ShiHeQi!;
        var cleanup = s.E<ShiHqCleanupIssuedEvent>().Single();
        var window = s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Batch.Id == receipt.BatchId);
        s.Need(receipt.Stage == ShiHqStage.CleanupChildren && root.PendingMovementContinuation is not null && cleanup.Count == marked.Length &&
            window.Batch.Movements.Select(m => m.CardId).Order().SequenceEqual(marked) && window.Batch.Movements.All(m => m.From.Zone == CardZoneKind.Hand && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.mobile-shi-he-qi.yingji-cleanup") &&
            s.E<NativeLogicalDiscardInvocationRecordedEvent>().All(e => e.ParentFrameId != root.Id),
            "The source-independent global obligation moves all players' actual marked hands in one batch without inventing logical discard invoices.");
        s.Need(Enumerable.Range(0, 4).SelectMany(i => s.V(i).Hand.Select(c => c.Id)).Order().SequenceEqual(preserved) && s.V(0).Equipment.Any(c => c.Id == equipment) &&
            marked.All(id => s.E<ShiHqYingjiClearedEvent>().Count(e => e.CardId == id && e.FrameId == root.Id) == 1),
            "Only the frozen marked hand entities leave, ordinary hands and equipment survive, and each paid mark clears once.");
        s.Cold(); s.ContinueWatch(); s.Reach(() => s.E<ShiHqCompletedEvent>().Any(e => e.FrameId == root.Id));
        s.Need(s.E<ShiHqCleanupIssuedEvent>().Length == 1 && s.E<ShiHqCompletedEvent>().Count(e => e.FrameId == root.Id) == 1,
            "Cold recovery resumes the original paid cleanup child without moving or clearing its entities a second time.");
        s.Replay();
    }

    private static bool Marked(CardSnapshot c) => c.SkillMarkLabel?.Contains("应机", StringComparison.Ordinal) == true;
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i)).Select(v => v.PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShiHeQi is not null);
        public bool Reward => V(0).SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == Qizhou)?.BooleanStates?.Any(s => s.StateId == "next-mark-draw" && s.Value) == true;
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Qizhou && P.Choices.Any(c => Key(c) == "activate"));
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch);
        public void ContinueWatch() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void DrawChildren(bool cold)
        {
            AtWatch(); var root = Root!; var paid = E<ShiHqDrawPaidEvent>().Single(e => e.FrameId == root.Id);
            Need(root.ShiHeQi is { Stage: ShiHqStage.DrawChildren, Draw.ActualDrawIds.Count: 2 } && paid.Count == 2 &&
                E<NativeDrawInvocationRecordedEvent>().Count(e => e.ParentFrameId == root.Id) == 1,
                "One paid native two-card draw owns its distinct one-card gain batches.");
            for (var i = 0; i < 2; i++)
            {
                AtWatch(); Need(Root?.Id == root.Id && E<ShiHqDrawPaidEvent>().Count(e => e.FrameId == root.Id) == 1, "Every gain child retains the exact paid native draw receipt.");
                if (cold && i == 0) Cold(); ContinueWatch();
            }
        }
        public void Native(CardKind kind, int[] targets)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is > 0 && a.ConversionSource is null && V(0).Hand.Any(c => c.Id == a.CardId && c.Kind == kind) && a.TargetSeats.SequenceEqual(targets));
            Need(action is not null, "The prepared original physical card has a native legal action."); Use(action!);
        }
        public void Duel(int target)
        {
            var action = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is > 0 && a.ConversionSource?.SkillId == Shanxi && a.TargetSeats.SequenceEqual([target]));
            Need(action is not null, "The selected unused target has an actual marked native Duel action."); Use(action!);
        }
        private void Use(LegalAction a) => Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource });
        public void Mark(int target, int cardId, bool alreadyOffered = false)
        {
            if (!alreadyOffered) Offer(); Answer(c => Key(c) == "activate");
            Reach(() => P?.Choices.Any(c => Key(c) == "mobile-shi-he-qi" && c.Parameters.GetValueOrDefault("branch") == "target") == true);
            Answer(c => c.Targets.SequenceEqual([target]));
            Reach(() => Root?.ShiHeQi?.Stage == ShiHqStage.Card && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "card") == true);
            var r = Root!.ShiHeQi!; var slot = r.Materials.ToList().FindIndex(c => c.CardId == cardId);
            Need(slot >= 0 && (target == 0 || P!.Choices.All(c => c.Cards.Count == 0 && !c.Parameters.Values.Contains(cardId.ToString()))), "Foreign hand choices expose opaque slots without the selected physical identity.");
            Answer(c => c.Parameters.GetValueOrDefault("slot") == slot.ToString());
        }
        public void Answer(Func<PromptChoice, bool> pick)
        { var p = P!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "The exact current choice exists."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var n = 0; n < 180; n++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0 } p)
                {
                    if (p.Choices.Any(c => Key(c) == "skip")) { Answer(c => Key(c) == "skip"); continue; }
                    if (p.Kind is DecisionKind.RespondSlash or DecisionKind.RespondDodge or DecisionKind.Nullification or DecisionKind.RescueDying)
                    { Answer(c => c.Cards.Count == 0 && c.Parameters.GetValueOrDefault("conversion-skill-id") is null); continue; }
                    throw new InvalidOperationException("Unexpected Shi He Qi boundary. " + Dump());
                }
                Need(G.State.Winner == Winner.None, "The short native scene retains a living table."); Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The short native scene reaches its exact next boundary.");
        }
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold recovery preserves the original physical marks and paid native child receipt."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(State() == before, "The command journal reproduces marks, target quota, real native uses and source-independent cleanup."); }
        public void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Shi He Qi command."); }
        public void Need(bool c, string message) { if (!c) throw new InvalidOperationException(message + " " + Dump()); }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements, Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(G.AcceptedCommands) });
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, P, Root?.ShiHeQi, Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).HandCount }) });
        public static Scene Start(bool watchDraw)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(watchDraw));
            var setup = new ShanheBattleSetup(114, 1, "shi-he-qi", Hero, 4, 0, 0, 0, 0, [Driver, Watch],
                [new(CardKind.BaguaFormation, Suit.Club, 2), new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Dodge, Suit.Heart, 3)], [],
                [new(ActivePeer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Club, 5), new(CardKind.Indulgence, Suit.Club, 6)] },
                 new(Peer, Hp: 8) { StartingHand = [new(CardKind.Dodge, Suit.Club, 8)] }, new(Peer, Hp: 8) { StartingHand = [new(CardKind.Peach, Suit.Heart, 9)] }], [], new string('a', 64));
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
    private sealed class Fixture(bool watchDraw) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shi-he-qi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var drawWatch = watchDraw ? $$"""
                ,{"id":"paid-draw-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-he-qi.yingji-draw"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}
                """ : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":30,"priority":0}],
                "triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}],
                "activations":[{"id":"lose-qizhou","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Qizhou}}"],"sourceBind":"{{Spent}}"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[
                {"id":"paid-cleanup-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.mobile-shi-he-qi.yingji-cleanup"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}{{drawWatch}}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]},{"id":"no-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Aggressor}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "原生手牌与失源", description = "保留固定手牌；主动终结替换绮胄。" },
                    [Watch] = new { name = "已支付原生子结算", description = "真实应机摸牌或清理后等待。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                    [Quiet] = new { name = "安静同桌", description = "真实回合跳过出牌。" }, [Aggressor] = new { name = "原生判定区来牌", description = "保留唯一可用的原生乐不思蜀。" } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Spent, "绮胄已失去", "已签发的全场应机清理继续。"));
            b.AddGeneral(new(Peer, "实际同桌角色", "", Quiet, "wei", 10)); b.AddGeneral(new(ActivePeer, "原生延时锦囊对手", "", Aggressor, "wei", 10));
            b.AddDeck(new(Deck, "固定实际桃牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "势贺齐原生标记决斗与清理", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer, ActivePeer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
