using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;

internal static class AnniversaryBaiLingYunChecks
{
    private const string Hero = "anniversary:bai-ling-yun", Linghui = "anniversary:linghui", Xiace = "anniversary:xiace", Yuxin = "anniversary:yuxin",
        Peer = "fixture:ann-bly-peer", Driver = "fixture:ann-bly-driver", Quiet = "fixture:ann-bly-quiet", NoDraw = "fixture:ann-bly-no-draw",
        UseHold = "fixture:ann-bly-use", GainHold = "fixture:ann-bly-gain", Deck = "fixture:ann-bly-deck";
    public static void PrivateTopNativeUseSuppressionAndLimitedRecoveryResumeCold()
    {
        var s = Scene.Start(); s.Play();
        s.Use("incoming", [2]); s.Offer(Xiace); s.Activate();
        s.Reach(() => s.P?.Choices.Any(c => c.Targets.SequenceEqual([2])) == true); s.Answer(c => c.Targets.SequenceEqual([2])); s.Play();
        s.Need(s.V(0).Hp == 2 && s.E<CurrentTurnNonLockedSkillSuppressionIssuedEvent>() is [{ Suppression.TargetSeat: 2 }],
            "Actual incoming damage grants one real current-turn nonlocked suppression to the selected other participant.");
        var slash = s.V(0).Hand!.Single(c => c.Kind == CardKind.Slash).Id;
        var cost = s.V(0).Hand!.Single(c => c.Kind == CardKind.Dodge).Id;
        s.Card(slash, LegalActionKind.Slash, 3); s.Offer(Xiace); s.Activate();
        s.Reach(() => s.P?.Choices.Any(c => c.Cards.SequenceEqual([cost])) == true); s.Answer(c => c.Cards.SequenceEqual([cost])); s.Play();
        s.Need(s.V(0).Hp == 3 && s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == cost && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) == 1 &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Xiace && e.BindingId == "incoming-suppression") == 1 &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Xiace && e.BindingId == "outgoing-recovery") == 1,
            "The independent outgoing item really discards its original cost, then recovers once, after a normal counted hand Slash.");
        s.Use("fatal", [1]); s.Offer(Yuxin); s.Activate();
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([1])) == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "select-target" && c.Targets.SequenceEqual([1])); s.Play();
        s.Need(s.V(1).Hp == 3 && s.E<PlayerDyingEvent>().Any(e => e.VictimSeat == 1) &&
            s.E<RecoveryAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 5 && e.RemainingHp == 3) == 1 &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Yuxin) == 1 &&
            s.E<ProgramBindingStartedEvent>().Count(e => e.SkillId == Xiace && e.BindingId == "outgoing-recovery") == 1,
            "Yuxin uses a genuine dying-entry recovery-to-current-owner-HP, while the already-spent outgoing Xiace item never fires twice in this actual turn.");

        s.End(); s.Offer(Linghui); s.Activate(); s.Menu();
        var root = s.Root!; var viewed = root.AnniversaryBly!.Viewed.ToArray(); var lease = new CardLocation(CardZoneKind.PrivatePersistentPile, 0, $"linghui:{root.Id}");
        var hand = s.V(0).HandCount;
        s.Need(viewed.Length == 3 && viewed.All(id => s.Zone(id) == lease) &&
            s.G.CardMovements.Count(m => viewed.Contains(m.CardId) && m.From == CardLocation.DrawPile && m.To == lease) == 3 &&
            s.G.CardMovements.All(m => !viewed.Contains(m.CardId) || m.To != CardLocation.Hand(0)) &&
            !s.E<CardsRevealedEvent>().SelectMany(e => e.Cards).Any(c => viewed.Contains(c.Id)),
            "Linghui privately retains three actual top entities in its own named viewing area; none is falsely gained or publicly revealed.");
        var chosen = s.P!.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("index") != "skip");
        s.Need(chosen is not null, "The exact private DrawTwo entity must have a current legal native Use at the actual ending boundary.");
        var used = chosen!.Cards.Single(); s.Answer(c => c.Id == chosen.Id); s.Reach(() => s.P?.SkillPrompt?.SkillId == UseHold);
        var use = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.AnniversaryBlyUse?.FrameId == root.Id);
        s.Need(use.CardId == used && use.CardKind == CardKind.DrawTwo && use.TargetSeats.SequenceEqual([0]) &&
            use.Action is { Type: CardActionType.Use, PhysicalCards: [{ CardId: var material, From: var from }] } && material == used && from == CardLocation.Processing &&
            viewed.Where(id => id != used).All(id => s.Zone(id) == lease),
            "A selected private entity enters the genuine native DrawTwo pipeline with its implicit self target, retaining the other two outside the draw pile.");
        var beforeDraw = s.G.CardMovements.Last().Sequence; var eventsBeforeDraw = s.G.Events.Count;
        s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Reach(() => s.P?.SkillPrompt?.SkillId == GainHold);
        root = s.Root!; var receipt = root.AnniversaryBly!; var gained = receipt.GainId!.Value;
        var drawFacts = s.G.Events.Skip(eventsBeforeDraw).Select(e => e.Payload).OfType<CardsDrawnEvent>().ToArray();
        var nativeDrawIds = s.G.CardMovements.Where(m => m.Sequence > beforeDraw && m.Sequence <= receipt.Before && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw).Select(m => m.CardId).ToArray();
        s.Need(receipt is { Stage: AnniversaryBlyStage.GainChildren, Returned: true } && s.V(0).HandCount == hand + 3 &&
            drawFacts is [{ PlayerSeat: 0, Count: 2 }] && nativeDrawIds.Length == 2 && !nativeDrawIds.Intersect(viewed).Any() &&
            viewed.Contains(gained) && gained != used && s.Zone(gained) == CardLocation.Hand(0) &&
            s.Zone(used) == CardLocation.DiscardPile && viewed.Where(id => id != used && id != gained).All(id => s.Zone(id) == CardLocation.DrawPile) &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == use.Id && e.CardId == used && e.CardKind == CardKind.DrawTwo) == 1,
            "DrawTwo really draws two new cards beneath the private reserve; exactly one remaining viewed entity is gained and the final entity returns to the top after the whole native use.");
        s.Cold(); s.Skip(); s.Reach(() => s.Root is null && s.E<AnniversaryBlyGainCompletedEvent>().Length == 1);
        s.Play();
        s.Need(s.E<AnniversaryBlyStartedEvent>().Length == 1 && s.E<AnniversaryBlyNativeReturnedEvent>().Length == 1 &&
            s.E<CurrentTurnNonLockedSkillSuppressionsExpiredEvent>().Length == 1,
            "Cold does not repay a completed use/gain and no foreign ending without a dying event qualifies; suppression expires at the real turn boundary.");
        s.End(); s.Offer(Linghui); s.Activate(); s.Menu();
        var skipRoot = s.Root!; var skipViewed = skipRoot.AnniversaryBly!.Viewed.ToArray();
        s.Answer(c => c.Parameters.GetValueOrDefault("index") == "skip"); s.Reach(() => s.P?.SkillPrompt?.SkillId == GainHold);
        var skipReceipt = s.Root!.AnniversaryBly!; var skipGain = skipReceipt.GainId!.Value;
        s.Need(s.E<AnniversaryBlyNativeIssuedEvent>().Length == 1 && skipViewed.Contains(skipGain) &&
            s.G.CardMovements.Where(m => m.Sequence > skipReceipt.Before && m.Sequence <= skipReceipt.After && m.Reason.Value == "skill.anniversary.linghui.return" && skipViewed.Contains(m.CardId)).Select(m => m.CardId)
                .SequenceEqual(skipViewed.Where(id => id != skipGain).Reverse()),
            "Declining the optional use still gains one under the adopted detailed rules; the untouched remainder returns in original top order and creates no fake native use.");
        s.Skip(); s.Reach(() => s.Root is null && s.E<AnniversaryBlyGainCompletedEvent>().Length == 2);
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-bly", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, Hero); var catalog = SkillProgramCatalog.Load(Rules, Presentation);
            foreach (var (id, p) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "固定原生角色", "", Quiet, "wei", 6));
            builder.AddDeck(new(Deck, "固定无中实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:draw_two", Suit.Heart, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "柏灵筠真实私看与恢复", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Presentation => JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
        {
            [NoDraw] = new { name = "不补普通摸牌", description = "固定短局" }, [Quiet] = new { name = "跳过其他人出牌", description = "固定短局" },
            [Driver] = new { name = "真实伤害驱动", description = "明确来源和原生濒死" }, [GainHold] = new { name = "原生取得暂停", description = "已移动的私有实体" },
            [UseHold] = new { name = "原生实体使用暂停", description = "真实无中使用", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
        } });
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Driver}}","revision":1,"activations":[
            {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
            {"id":"fatal","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]}
          ]},
          {"id":"{{UseHold}}","revision":1,"triggers":[{"id":"native-use","window":"cardUseBeforeTargetEffects","ownerRelation":"actor","cardKinds":["drawTwo"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"native-use","options":[{"id":"continue"}]}]}]},
          {"id":"{{GainHold}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill.anniversary.linghui.gain"],"optional":true,"isSkillInvocation":false,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
        ]}
        """;
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryBly is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Use(string activation, int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets, G.Revision, P!.PromptId));
        public void Card(int id, LegalActionKind kind, int target)
        { var a = G.GetHumanLegalActions().Single(a => a.CardId == id && a.Kind == kind && a.TargetSeats.SequenceEqual([target])); Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources }); }
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Offer(string skill) => Reach(() => P?.SkillPrompt?.SkillId == skill && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Menu() => Reach(() => Root?.AnniversaryBly?.Stage == AnniversaryBlyStage.Choice && P?.Choices.Any(c => c.Parameters.GetValueOrDefault("index") == "skip") == true);
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing a real current Bai Ling Yun choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var step = 0; step < 150; step++)
            {
                if (ready()) return;
                if (P is { Kind: DecisionKind.SelectFaction }) Answer(_ => true);
                else if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == GainHold) Skip();
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Bai Ling Yun boundary. " + Dump());
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Bai Ling Yun did not reach its bounded actual native boundary.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(); var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))), "Cold retains private viewed entities/order, once-issued native Use, paid random gain and its exact native movement child.");
        }
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Bai Ling Yun command."); }
        public void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " " + Dump()); }
        private string Dump() => JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Receipt = Root?.AnniversaryBly, Hp = Enumerable.Range(0, 4).Select(i => V(i).Hp),
            Starts = E<AnniversaryBlyStartedEvent>(), Issued = E<AnniversaryBlyNativeIssuedEvent>(), Returned = E<AnniversaryBlyNativeReturnedEvent>(), Gain = E<AnniversaryBlyGainCompletedEvent>(), Recovery = E<RecoveryAppliedEvent>() });
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(59, 1, "anniversary-bly", Hero, 3, 0, 0, 0, 0, [NoDraw, Driver, UseHold, GainHold],
                [new(CardKind.Slash, Suit.Club, 7), new(CardKind.Dodge, Suit.Spade, 9)], [],
                [new(Peer, Hp: 6, ExtraSkillIds: [NoDraw]) { StartingHand = [] }, new(Peer, Hp: 6, ExtraSkillIds: [NoDraw]) { StartingHand = [] }],
                [new(Peer, Hp: 2, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('e', 64));
            var game = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var scene = new Scene(game, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }
}
