using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5XuRongChecks
{
    private const string Driver = "fixture:yaodao-driver", Watch = "fixture:yaodao-paid-gain", Rescue = "fixture:yaodao-entry-rescue";
    private const string Deck = "fixture:yaodao-deck", Weapon = StandardShanheS5XuRongPackage.YaodaoChitongSkillId;

    public static void WeaponNativeEquipRejudgeClaimLegalRandomTargetAndRedOutcomeCold()
    {
        var s = Scene.Start(weapon: true);
        var blade = s.V().Hand.Single(c => c.Kind == CardKind.YaodaoChitong).Id;
        Need(s.V().Equipment.Count == 0 && !s.V().Skills!.Any(k => k.ContentId == Weapon),
            "The fixed weapon is initially an extra real Hand entity; merely holding it does not grant its effects.");
        s.Native(blade, []); s.Play();
        Need(s.V().Equipment.Any(c => c.Id == blade) && s.V().Skills!.Any(k => k.ContentId == Weapon) &&
            s.E<CardUseDeclaredEvent>().Any(e => e.CardId == blade && e.CardKind == CardKind.YaodaoChitong) &&
            s.G.CardMovements.Count(m => m.CardId == blade && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardId == blade) == 1,
            "Only a completed native equipment Use installs the actual Yaodao and grants its source-specific locked program.");
        var replacement = s.V().Hand.Single(c => c.Kind == CardKind.Dodge && c.Suit == Suit.Club).Id;
        var blackSlash = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Spade).Id;
        s.Native(blackSlash, [2]); s.Until(() => s.P?.Kind == DecisionKind.ProgramJudgmentReplacement);
        Need(s.G.ResolutionStack.OfType<JudgmentFrame>().Single() is { TargetSeat: 0, Reason: "shanhe.yaodao-chitong" } &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.YaodaoChitong is not null).YaodaoChitong is
                { WeaponCardId: var id, OriginalTargetSeat: 2, Resolved: false } && id == blade,
            "The real Slash's target window freezes the installed weapon and original target before opening native retrial.");
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([replacement])); s.Until(() => s.P?.Kind == DecisionKind.ProgramJudgmentTrigger);
        s.Answer(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate");
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        Need(s.E<ProgramJudgmentCardClaimedEvent>().Single() is { CardId: var claimed, OwnerSeat: 0 } && claimed == replacement &&
            s.Zone(replacement) == CardLocation.Hand(0) && !s.E<YaodaoChitongResolvedEvent>().Any(),
            "Native Tiandu owns the final retrial entity while its real gain child is pending; the weapon has not prematurely chosen a new target.");
        s.Cold(); s.Continue(); s.Play();
        var resolved = s.E<YaodaoChitongResolvedEvent>().Single();
        Need(resolved is { OriginalTargetSeat: 2, NewTargetSeat: 3, FinalSuit: Suit.Club, CandidateCount: 1 } &&
            s.E<YaodaoChitongResolvedEvent>().Length == 1 &&
            s.E<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 3, Amount: 2 }] &&
            s.V(2).Hp == 8 && s.V(1).Hp == 8 && s.V(3).Hp == 6 &&
            s.E<TargetsConfirmedEvent>().Single(e => e.ResolutionId == resolved.CardUseId).TargetSeats.SequenceEqual([2]) &&
            s.E<CardActionAcceptedEvent>().Single(e => e.Action.ActionId == resolved.ActionId).Action.TargetSeats.SequenceEqual([3]) &&
            s.E<CardUseFinishedEvent>().Count(e => e.CardId == blackSlash) == 1 &&
            s.Zone(replacement) == CardLocation.Hand(0) &&
            s.G.CardMovements.Count(m => m.CardId == replacement && m.From.Zone == CardZoneKind.Judgment && m.To == CardLocation.Hand(0)) == 1,
            "Final black judgment chooses once among legal targets: the source, original target and genuinely empty-Hand Kongcheng holder are excluded; the real new recipient takes black-Slash damage plus one, and claimed result cleanup does not discard Tiandu's card.");
        s.Cold();

        var redSlash = s.V().Hand.Single(c => c.Kind == CardKind.Slash && c.Suit == Suit.Heart).Id;
        s.Native(redSlash, [2]); s.Until(() => s.P?.Kind == DecisionKind.ProgramJudgmentReplacement);
        s.Answer(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
        s.Until(() => s.P?.Kind == DecisionKind.ProgramJudgmentTrigger);
        s.Answer(c => c.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-skip"); s.Play();
        var red = s.E<YaodaoChitongResolvedEvent>().Last();
        Need(red is { OriginalTargetSeat: 2, NewTargetSeat: null, FinalSuit: Suit.Heart, CandidateCount: 0 } &&
            s.E<YaodaoChitongResolvedEvent>().Length == 2 && s.V(2).Hp == 7 &&
            s.E<DamageAppliedEvent>().Last() is { TargetSeat: 2, Amount: 1 } &&
            s.E<JudgmentRequestedEvent>().Count(e => e.Reason == "shanhe.yaodao-chitong") == 2,
            "A red judgment preserves the actual target and spends no random draw; a red Slash receives no black-only damage bonus.");
        s.Cold();
    }

    public static void ShajueFrozenNegativeEntryGatesBothPoolAndCausalClaimCold()
    {
        foreach (var overkill in new[] { false, true })
        {
            var s = Scene.Start(weapon: false);
            s.Accept(new UseProgramSkillCommand(0, "ol:xionghuo", "transfer-one-marker", [], [1], s.G.Revision, s.P!.PromptId)); s.Play();
            Need(s.Baoli(0) == 2, "A real Xionghuo transfer makes room for a visible actual Shajue replenishment.");
            if (overkill) { s.Native(s.V().Hand.Single(c => c.Kind == CardKind.Alcohol).Id, []); s.Play(); }
            var slash = s.V().Hand.Single(c => c.Kind == CardKind.Slash).Id;
            s.Native(slash, [2]);
            if (overkill)
            {
                s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
                var root = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.XuRong?.Stage == "claim-native");
                var history = s.G.Events.ToList();
                var recovered = history.FindIndex(e => e.Payload is RecoveryAppliedEvent
                    { SourceSeat: 0, TargetSeat: 2, Amount: 9, RemainingHp: 8 });
                var replenished = history.FindIndex(e => e.Payload is XionghuoPoolChangedEvent
                    { Source.SkillId: StandardShanheS5XuRongPackage.ShajueId, Kind: "dying" });
                Need(root.SkillId == StandardShanheS5XuRongPackage.ShajueId &&
                    root.WindowContext?.XuRongDying is { HpAtEntry: -1, RescueCardsRequired: 2 } &&
                    s.V(2).Hp == 8 && s.Baoli(0) == 3 && s.Zone(slash) == CardLocation.Hand(0) &&
                    recovered >= 0 && replenished > recovered &&
                    s.E<XuRongCausalClaimIssuedEvent>().Single().Receipt is { HpAtEntry: -1, RescueCardsRequired: 2 } receipt &&
                    receipt.CardIds.SequenceEqual([slash]),
                    "A higher-priority genuine rescue does not erase the frozen negative entry: S5 Shajue gains exactly one Baoli and the precise lethal native Slash before its paid gain child returns. " +
                    JsonSerializer.Serialize(new
                    {
                        RootSkillMatches = root.SkillId == StandardShanheS5XuRongPackage.ShajueId,
                        NegativeEntryMatches = root.WindowContext?.XuRongDying is { HpAtEntry: -1, RescueCardsRequired: 2 },
                        RescuedHpMatches = s.V(2).Hp == 8, PoolMatches = s.Baoli(0) == 3,
                        RecoveryBeforeReplenishment = recovered >= 0 && replenished > recovered,
                        OriginalEntityInHand = s.Zone(slash) == CardLocation.Hand(0), root.SkillId,
                        Proof = root.WindowContext?.XuRongDying, TargetHp = s.V(2).Hp, TargetMaxHp = s.V(2).MaxHp,
                        Baoli = s.Baoli(0), CausalClaims = s.E<XuRongCausalClaimIssuedEvent>(),
                        Claimed = s.E<XuRongCardsPaidEvent>().Where(e => e.FrameId == root.Id),
                        ActualDamage = s.E<DamageAppliedEvent>(), Recovery = s.E<RecoveryAppliedEvent>(),
                        SlashMoves = s.G.CardMovements.Where(m => m.CardId == slash).TakeLast(5)
                    }));
                s.Cold(); s.Continue();
            }
            s.Play();
            Need(s.E<XionghuoPoolChangedEvent>().Count(e => e.Source.SkillId == StandardShanheS5XuRongPackage.ShajueId && e.Kind == "dying") == (overkill ? 1 : 0) &&
                s.Baoli(0) == (overkill ? 3 : 2) &&
                s.E<XionghuoPoolChangedEvent>().Count(e => e.Source.SkillId == "ol:shajue" && e.Kind == "dying") == 1 &&
                s.Zone(slash) == (overkill ? CardLocation.Hand(0) : CardLocation.DiscardPile) &&
                s.E<CardUseFinishedEvent>().Count(e => e.CardId == slash) == 1,
                "At zero HP the S5 binding does neither operation, while ordinary OL still replenishes on every dying entry; at negative HP the restored S5 claim survives original native use completion without double payment.");
            s.Cold();
        }
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static string Peer(int seat) => "fixture:yaodao-peer-" + seat;
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Baoli(int seat) => V(seat).Markers?.FirstOrDefault(m => m.Kind == PlayerMarkerKind.Baoli)?.Count ?? 0;
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.ToString() ?? "Rejected S5 native Xu Rong command."); }
        public void Answer(Func<PromptChoice, bool> choose) { var p = P!; var c = p.Choices.FirstOrDefault(choose); Need(c is not null, "Missing real choice: " + JsonSerializer.Serialize(p)); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Native(int id, int[] targets) => Accept(new PlayCardCommand(0, id, targets, G.Revision, P!.PromptId));
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Until(Func<bool> done)
        {
            for (var step = 0; step < 160; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small native Xu Rong fixture ended before its intended boundary.");
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Xu Rong boundary: " + JsonSerializer.Serialize(new { P, Frames = G.ResolutionStack.Select(f => f.GetType().Name), Recent = G.Events.TakeLast(4).Select(e => e.Payload.GetType().Name) }));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Native Xu Rong boundary missing: " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))).ToArray(), Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Zones = g.CreateCardZoneDiagnostics() });
            var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State(G) == State(restored), "Cold JSON replay preserves every view, native judgment/retrial/claim, exact Slash target, paid random result and causal dying receipt."); G = restored;
        }
        public static Scene Start(bool weapon)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheS5XuRongPackage(), new Fixture());
            ShanheStartingCard[] cards = weapon
                ? [new(CardKind.YaodaoChitong, Suit.Spade, 6), new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Heart, 7), new(CardKind.Dodge, Suit.Club, 8)]
                : [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Alcohol, Suit.Heart, 9)];
            var setup = new ShanheBattleSetup(37, 1, "native-xurong", StandardShanheS5XuRongPackage.XurongId, 4, 0, 0, 0, 0,
                weapon ? [Driver, Watch, "boundary:guidao", "classic:tiandu"] : [Driver, Watch, Rescue], cards, [],
                [new(Peer(2), Hp: weapon ? 8 : 1) { StartingHand = [] },
                 new(Peer(3), ExtraSkillIds: weapon ? [] : ["ol:shajue"]) { StartingHand = [] }],
                [new(Peer(1), ExtraSkillIds: weapon ? ["classic:kongcheng"] : []) { StartingHand = [] }], new string('f', 64));
            var g = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); scene.Play(); return scene;
        }
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-xurong", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"reach","query":"attackRange","operation":"set","value":3,"priority":0},{"id":"quota","query":"slashLimit","operation":"add","value":2,"priority":0}]},
             {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","optional":false,
              "movementReasons":["skill-program.classic:tiandu.claimJudgmentCard","program.shajue2023.claim"],
              "effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]},
             {"id":"{{Rescue}}","revision":1,"triggers":[{"id":"early-real-rescue","window":"dyingEntering","subject":"any","optional":false,"priority":1000,
              "effects":[{"op":"recoverOtherDyingVictimTo","target":"owner","amount":8}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
            { [Driver] = new { name = "原生范围次数", description = "实际原生杀" }, [Rescue] = new { name = "真实先行救援", description = "濒死先行真实回复" },
              [Watch] = new { name = "真实获牌孩子", description = "在原牌实付后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "原生新规则检查") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat), "实际机制目标", "supporter", "standard:none", "wei", 8));
            b.AddDeck(new(Deck, "小真实牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:peach", Suit.Heart, 3)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "原生妖刀与杀绝", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5XuRongPackage.XurongId, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
