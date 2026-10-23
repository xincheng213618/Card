using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class AnniversaryMangYaChangChecks
{
    private const string Hero = "anniversary:mang-ya-chang", Skill = "anniversary:jiedao", Driver = "fixture:ann-jiedao-driver", Watch = "fixture:ann-jiedao-watch", Transfer = "fixture:ann-jiedao-transfer", Deck = "fixture:ann-jiedao-deck";
    private const string CostReason = "skill-program.anniversary:jiedao.discard";
    public static void FirstSourceChoiceNativeLionCapSurvivorDiscardAndDeathCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var g = GameEngine.CreateStandard(Options(), registry); Accept(g, new StartGameCommand()); Play(g);
        Need(V(g).Hp == 2 && V(g).MaxHp == 4, "The fixed real scene starts wounded by exactly two, without injecting damage or Jiedao grants.");
        var lion = V(g).Hand.Single(c => c.Kind == CardKind.SilverLion).Id;
        Accept(g, new UseProgramSkillCommand(0, Driver, "equip-peer", [lion], [3], g.Revision, P(g)!.PromptId)); Play(g);
        Need(Zone(g, lion) == CardLocation.Equipment(3) && V(g, 3).Equipment.Any(c => c.Id == lion && c.Kind == CardKind.SilverLion),
            "The real Silver Lion entity enters the actual victim's armor slot through the existing native equipment placement.");
        var turn = g.State.TurnNumber;
        Native(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual([3])));
        Offer(g); var source = g.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single(f => f.AnniversaryJiedaoSource is not null).AnniversaryJiedaoSource!;
        Need(source.MaximumBonus == 2 && source.Source.OwnerSeat == 0 && source.Target == 3 && source.OriginalAmount == 1 && V(g, 3).Hp == 8,
            "The source-only window freezes its exact original first damage and lost HP before the native finalizer or any target HP payment.");
        Answer(g, c => Key(c) == "activate"); Menu(g, AnniversaryJiedaoStage.BonusChoice); Answer(g, c => Option(c) == "2");
        Menu(g, AnniversaryJiedaoStage.DiscardChoice);
        Need(V(g, 3).Hp == 8 && V(g).Hp == 1 && Root(g)!.WindowContext?.TargetSeat == 0 && Root(g)!.AnniversaryJiedao is { Required: 2, Grant.Bonus: 2 } &&
            E<ProgramDamageTransferredEvent>(g).Any(e => e.ResolutionId == source.AttackId && e.OwnerSeat == 3 && e.TargetSeat == 0) &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1) &&
            E<SilverLionDamageCappedEvent>(g).Any(e => e.ResolutionId == source.AttackId && e.TargetSeat == 3 && e.BaseAmount == 3 && e.ModifiedAmount == 1),
            "Choosing +2 enters the native source modifier and real Silver Lion caps it to one; a genuine bound-card redirect then changes this same damage's actual victim to the living source, whose exact damage return still requires the chosen two-card cost.");
        var discard = V(g).Hand.Where(c => c.Kind == CardKind.Dodge).Take(2).Select(c => c.Id).ToArray();
        Need(discard.Length == 2, "The fixed real opening retains two genuine discard materials.");
        foreach (var id in discard) Answer(g, c => Option(c) == "pick" && c.Cards.SequenceEqual([id]));
        Answer(g, c => Option(c) == "finish"); Reach(g, p => p.SkillPrompt?.SkillId == Watch);
        var paid = Root(g)!; var invoice = E<NativeLogicalDiscardInvocationRecordedEvent>(g).Single(e => e.ParentFrameId == paid.Id && e.Reason.Value == CostReason);
        Need(paid.AnniversaryJiedao is { Stage: AnniversaryJiedaoStage.DiscardChildren, Required: 2 } && paid.PendingMovementContinuation is not null &&
            invoice.ActualCount == 2 && discard.All(id => Zone(g, id) == CardLocation.DiscardPile) &&
            g.CardMovements.Count(m => discard.Contains(m.CardId) && m.Reason.Value == CostReason) == 2,
            "The actual two-card logical discard is fully paid while its original damage-return frame still owns the real movement child.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(E<AnniversaryJiedaoCostPaidEvent>(g).Length == 1 && E<AnniversaryJiedaoCostBoundEvent>(g).Length == 1 &&
            g.CardMovements.Count(m => m.Reason.Value == CostReason) == 2 && V(g, 3).Hp == 8 && V(g).Hp == 1,
            "The paid child cold return resumes native damage without paying twice or reapplying its source increase.");
        Native(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([3]))); Play(g);
        Need(g.State.TurnNumber == turn && V(g, 3).Hp == 7 && E<AnniversaryJiedaoWindowIssuedEvent>(g).Length == 1 &&
            E<AnniversaryJiedaoChosenEvent>(g).Length == 1 && E<AnniversaryJiedaoCostPaidEvent>(g).Length == 1,
            "The second actual sourced damage in the same real turn never receives another Jiedao opportunity.");
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Need(g.State.TurnNumber > turn && V(g, 2).IsAlive && V(g, 2).Hp == 1, "The same short scene reaches a genuine next actual own turn and keeps its one-HP opponent alive until the real Duel.");
        Native(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual([2]))); Offer(g);
        Answer(g, c => Key(c) == "activate"); Menu(g, AnniversaryJiedaoStage.BonusChoice); Answer(g, c => Option(c) == "2"); Play(g);
        Need(!V(g, 2).IsAlive && E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 2 && e.KillerSeat == 0) &&
            E<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 3) &&
            E<AnniversaryJiedaoChosenEvent>(g).Length == 2 && E<AnniversaryJiedaoCostBoundEvent>(g).Length == 1 && E<AnniversaryJiedaoCostPaidEvent>(g).Length == 1 &&
            g.CardMovements.Count(m => m.Reason.Value == CostReason) == 2,
            "A real new turn reopens the first-damage choice; original Duel damage really kills its victim after native dying, so no posthumous discard obligation is issued.");
        Need(State(g) == State(GameReplay.Replay(Options(), g.AcceptedCommands, registry)), "The whole short command flow replays its original source window, native armor cap, paid discard cold return, first-damage boundary and actual death exemption exactly.");
    }
    private static GameOptions Options()
    {
        var setup = new ShanheBattleSetup(7, 1, "anniversary-jiedao", Hero, 2, 0, 0, 0, 0, [Driver, Watch],
            [new(CardKind.SilverLion, Suit.Club, 1), new(CardKind.Duel, Suit.Club, 1), new(CardKind.Slash, Suit.Heart, 7),
             new(CardKind.Duel, Suit.Spade, 1), new(CardKind.Dodge, Suit.Heart, 2), new(CardKind.Dodge, Suit.Club, 3)], [],
            [new(Peer(2), Hp: 1) { StartingHand = [] }, new(Peer(3), ExtraSkillIds: [Transfer]) { StartingHand = [new(CardKind.Dodge, Suit.Heart, 9)] }], [new(Peer(1)) { StartingHand = [] }], new string('a', 64));
        return new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 };
    }
    private static string Peer(int s) => "fixture:ann-jiedao-peer-" + s;
    private static PlayerSnapshot V(GameEngine g, int seat = 0) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static ProgramSkillFrame? Root(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.AnniversaryJiedao is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static CardLocation Zone(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static string? Key(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action");
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option");
    private static void Need(bool b, string message) { if (!b) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected native Jiedao command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; var c = p.Choices.FirstOrDefault(pick); Need(c is not null, "Missing exact Jiedao choice: " + JsonSerializer.Serialize(p)); Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Native(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Offer(GameEngine g) => Reach(g, p => p.SkillPrompt?.SkillId == Skill && p.Choices.Any(c => Key(c) == "activate"));
    private static void Menu(GameEngine g, AnniversaryJiedaoStage stage) => Reach(g, p => p.SkillPrompt?.SkillId == Skill && Root(g)?.AnniversaryJiedao?.Stage == stage);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> ready)
    {
        for (var i = 0; i < 150; i++)
        {
            if (P(g) is { } p)
            {
                if (ready(p)) return;
                if (p.SkillPrompt?.SkillId == Watch) { Continue(g); continue; }
                if (p.Kind == DecisionKind.RescueDying && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "let-die")) { Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die"); continue; }
                if (p.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Jiedao boundary: " + JsonSerializer.Serialize(new { p, g.State.TurnNumber, Hp = Enumerable.Range(0, 4).Select(s => V(g, s).Hp), Receipt = Root(g)?.AnniversaryJiedao, Chosen = E<AnniversaryJiedaoChosenEvent>(g), Paid = E<AnniversaryJiedaoCostPaidEvent>(g) }));
            }
            Need(g.State.Winner == Winner.None, "The bounded Jiedao scene retains another living enemy and its genuine native boundary."); Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Jiedao missed its bounded real native boundary.");
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry r) { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Need(State(g) == State(restored), "Cold restore preserves all viewers, exact original damage, chosen bonus, paid logical discard, native child and its owner frame."); return restored; }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Zones = g.CreateCardZoneDiagnostics(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-anniversary-jiedao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            b.AddCard(new("fixture:ann-jiedao-lion", "白银狮子", "装备牌", CardCatalog.Get(CardKind.SilverLion).Description, CardKind.SilverLion));
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"native-range","query":"attackRange","operation":"set","value":4,"priority":0},{"id":"native-quota","query":"slashLimit","operation":"add","value":20,"priority":0},{"id":"preserve-materials","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[{"id":"equip-peer","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"lion"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"lion"}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-logical-discard","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{CostReason}}"],"optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Transfer}}","revision":1,"triggers":[{"id":"real-transfer","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"eventSource"},{"op":"selectOwnedCards","target":"owner","zones":["hand"],"minimumCards":1,"maximumCards":1,"resultBind":"cost"},{"op":"moveBoundCards","target":"owner","sourceBind":"cost","destination":"discardPile"},{"op":"redirectCurrentDamage","target":"selectedTarget","sourceBind":"cost","drawLostHpAfterDamage":false}]}]}]}
            """;
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Driver] = new { name = "真实装备及原生杀", description = "只复用原生装备移动、范围和次数。" }, [Watch] = new { name = "真实弃牌子窗", description = "原弃牌已支付后暂停。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
              [Transfer] = new { name = "真实弃牌转移", description = "已有的一次性实际受伤转移，以真实手牌支付后转移给原来源。" } } }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id] });
            for (var s = 1; s < 4; s++) b.AddGeneral(new(Peer(s), "固定真实同桌", "", Watch, "wei", 8));
            b.AddDeck(new(Deck, "固定只有闪的真实牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Diamond, 2 + i % 5)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "截刀来源增伤与实际后付", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
