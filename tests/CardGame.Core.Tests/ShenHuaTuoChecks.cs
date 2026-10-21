using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class ShenHuaTuoChecks
{
    private const string Hero = "anniversary:shen-hua-tuo", Driver = "fixture:shen-ht-driver", Pulse = "fixture:shen-ht-pulse",
        Watch = "fixture:shen-ht-hp-watch", DrawWatch = "fixture:shen-ht-draw-watch", Quiet = "fixture:shen-ht-quiet", A = "fixture:shen-ht-a",
        B = "fixture:shen-ht-b", C = "fixture:shen-ht-c", Enemy = "fixture:shen-ht-enemy", Deck = "fixture:shen-ht-deck";
    public static void LuxinInterruptsNextTrueInvocationAndJingyuCountsEachNameOnce()
    {
        var s = Scene.Start(fragile: true); s.Play(); var gift = s.V(0).Hand.First(c => c.Kind == CardKind.Slash).Id;
        s.Use("anniversary-god:luxin", "give-and-filter", [gift], [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "discard") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("option") == "discard"); s.Play();
        s.Need(s.E<ShenHuaTuoLuxinArmedEvent>() is [{ TargetSeat: 1, HpDelta: -1 }] &&
            s.G.CardMovements.Any(m => m.CardId == gift && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) &&
            s.G.CardMovements.Count(m => m.From == CardLocation.Hand(1) && m.Reason.Value == "skill.shen-hua-tuo.luxin.random-discard") == 1,
            "Luxin gives a genuine hand entity and random-discards one actual same-name hand card before arming its next invocation.");
        s.Use(Driver, "touch", [], []); s.Play(); s.Use(Driver, "touch", [], []); s.Play();
        s.Need(s.E<SkillInvocationStartedEvent>().Count(e => e.SkillKey == Driver) == 2 &&
            s.E<ShenHuaTuoJingyuReservedEvent>().Count(e => e.SkillName == "发动基准") == 1 &&
            s.E<ShenHuaTuoJingyuReservedEvent>().All(e => e.SkillName != "无正常摸牌"),
            "Two accepted active uses invoke the same skill twice, Jingyu draws for its name only once, and explicit maintenance never invokes.");
        var hp = s.V(1).Hp; s.End();
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Pulse);
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Reach(() => s.P is { Kind: DecisionKind.RescueDying, PlayerSeat: 0 });
        s.Need(s.V(1).Hp == 0 && s.G.ResolutionStack.OfType<DyingFrame>().Any(f => f.Continuation == DyingContinuationKind.SkillInvocation && f.VictimSeat == 1) &&
            s.G.ResolutionStack.OfType<SkillInvocationFrame>().Any(f => f.SkillKey == Pulse && f.DyingFrameId is not null),
            "Luxin's actual HP loss enters an invocation-owned dying frame while the accepted pulse remains suspended.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("response") == "peach");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Watch);
        s.Need(s.V(1).Hp == hp && s.E<ShenHuaTuoLuxinConsumedEvent>() is [{ TargetSeat: 1, HpDelta: -1 }] &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Pulse && f.InstructionIndex == 0) &&
            s.G.ResolutionStack.OfType<SkillInvocationFrame>().Any(f => f.SkillKey == Pulse && f.Stage == SkillInvocationStage.Children),
            "The real Peach rescue returns to Luxin's HP observers before instruction zero of the original skill.");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        s.Reach(() => s.E<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Pulse && e.Activated && e.Completed));
        s.Need(s.E<SkillInvocationHpPaidEvent>().Count(e => e.TargetSeat == 1 && e.Delta == -1 && e.Before == 1 && e.After == 0) == 1 && s.V(1).Hp == hp &&
            s.E<RecoveryAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1),
            "The typed HP-child return resumes the original skill exactly once after cold restoration.");
    }
    public static void HuandaoRestoresThenSamplesGeneralAndSkillBeforeRecipientLoss()
    {
        var s = Scene.Start(); s.Play(); s.Use(Driver, "prepare", [], [1]); s.Play();
        s.Need(s.V(1).IsFaceDown && s.V(1).IsChained, "The fixture changes real face and chain state through native program effects.");
        s.Use("anniversary-god:huandao", "restore-and-exchange", [], [1]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "accept") == true);
        s.Need(!s.V(1).IsFaceDown && !s.V(1).IsChained && s.P!.PlayerSeat == 1 && s.G.CreateSnapshot(0).PendingDecision is null,
            "Huandao first really restores the target; only its recipient receives the private optional acquisition decision.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "accept");
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "lose:" + Pulse) == true);
        var r = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "anniversary-god:huandao").ShenHuaTuo!;
        var rng = new DeterministicRandom(unchecked((int)r.RandomBefore));
        s.Need(r.GeneralCandidates.SequenceEqual(new[] { A, B, C }) && r.SelectedGeneralId == r.GeneralCandidates[rng.Next(r.GeneralCandidates.Count)] &&
            rng.State == r.RandomAfterGeneral && r.SelectedSkillId == r.SkillCandidates[rng.Next(r.SkillCandidates.Count)] && rng.State == r.RandomAfterSkill &&
            s.V(1).Skills!.Any(k => k.ContentId == r.SelectedSkillId) && s.P!.PlayerSeat == 1,
            "The frozen sampler picks a same-person general first and one of its skills second, grants it before the recipient loses another skill.");
        var generalResult = s.E<ShenHuaTuoRandomGeneralEvent>().Single();
        var skillResult = s.E<ShenHuaTuoRandomSkillEvent>().Single();
        var publicResults = new[] { System.Text.Json.JsonSerializer.SerializeToElement(generalResult), System.Text.Json.JsonSerializer.SerializeToElement(skillResult) };
        s.Need(generalResult.TargetSeat == 1 && generalResult.GeneralId == r.SelectedGeneralId && skillResult.FrameId == generalResult.FrameId &&
            skillResult.TargetSeat == 1 && skillResult.GeneralId == r.SelectedGeneralId && skillResult.SkillId == r.SelectedSkillId &&
            publicResults.All(result => !result.TryGetProperty("Before", out _) && !result.TryGetProperty("After", out _)),
            "Public sampling facts disclose only the sampled results; the two exact RNG transitions remain on the trusted owning frame.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option") == "lose:" + Pulse); s.Play();
        s.Need(!s.V(1).Skills!.Any(k => k.ContentId == Pulse) && s.V(1).Skills!.Any(k => k.ContentId == r.SelectedSkillId) &&
            s.E<ShenHuaTuoSkillGrantedEvent>().Length == 1 && s.E<ShenHuaTuoSkillRevokedEvent>().Any(e => e.SkillId == Pulse) &&
            !s.G.GetHumanLegalActions().Any(a => a.ProgramSkillId == "anniversary-god:huandao"),
            "The chosen old skill is really removed, the sampled permanent grant survives, and the true game usage is spent once.");
        var duplicate = Scene.Start(onlyOwned: true); duplicate.Play(); duplicate.Use("anniversary-god:huandao", "restore-and-exchange", [], [1]);
        duplicate.Reach(() => duplicate.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "accept") == true);
        duplicate.Answer(c => c.Parameters.GetValueOrDefault("option") == "accept"); duplicate.Play();
        duplicate.Need(duplicate.E<ShenHuaTuoRandomGeneralEvent>() is [{ GeneralId: A }] && duplicate.E<ShenHuaTuoSkillUnchangedEvent>().Length == 1 &&
            duplicate.E<ShenHuaTuoSkillGrantedEvent>().Length == 0 && duplicate.E<ShenHuaTuoSkillRevokedEvent>().Length == 0,
            "An already-owned result stays in the literal general/printed-skill sampler; it adds no duplicate grant, never rerolls and causes no unrelated skill loss.");
    }
    public static void ActualConvertedMaterialsAndNativeEquipmentCold()
    {
        var s = Scene.Start(equipment: true); s.Play(); var armor = s.V(0).Hand.Single(c => c.Kind == CardKind.BaguaFormation).Id;
        s.Use(Driver, "arm-target", [armor], [2]); s.Play();
        s.Need(s.V(2).Equipment.Any(c => c.Id == armor && c.Kind == CardKind.BaguaFormation), "A real captured equipment entity is placed into the defender's armor slot.");
        var pair = s.V(0).Hand.Where(c => c.Kind == CardKind.Slash).Take(2).Select(c => c.Id).ToArray();
        s.Accept(new UseEquipmentEffectCommand(0, CardKind.ZhangbaSerpentSpear, pair, [2], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == DrawWatch && s.G.ResolutionStack.OfType<SkillInvocationFrame>()
            .Any(f => f.ReturnKind == SkillInvocationReturnKind.NativeEquipment && f.SkillName == CardCatalog.Get(CardKind.BaguaFormation).DisplayName));
        s.Need(s.E<ZhangbaSerpentSpearConvertedEvent>().Any(e => e.UserSeat == 0 && e.PhysicalCardIds.SequenceEqual(pair)) &&
            s.E<SkillInvocationStartedEvent>().Count(e => e.SkillName == CardCatalog.Get(CardKind.ZhangbaSerpentSpear).DisplayName) == 1,
            "Only the actual accepted two-material Spear conversion enters the invocation boundary.");
        s.Need(s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any() &&
            s.E<NativeEquipmentInvocationIssuedEvent>().Count(e => e.Kind == NativeEquipmentInvocationKind.Bagua) == 1 &&
            !s.E<JudgmentRequestedEvent>().Any(e => e.Reason == JudgmentReasons.BaguaDefense),
            "The accepted native armor choice owns a frozen receipt; Jingyu's real pre-draw child suspends its judgment before payment.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        foreach (var kind in new[] { CardKind.ZhangbaSerpentSpear, CardKind.BaguaFormation })
        {
            var name = CardCatalog.Get(kind).DisplayName;
            var reserved = s.E<ShenHuaTuoJingyuReservedEvent>().Where(e => e.SkillName == name).ToArray();
            s.Need(reserved.Length == 1 && s.E<SkillInvocationDrawPaidEvent>().Count(e => e.FrameId == reserved[0].InvocationFrameId && e.Count == 1) == 1,
                "Each genuine equipment skill name pays Jingyu exactly once in the same round, including after cold return.");
        }
        s.Need(s.E<NativeEquipmentInvocationReturnedEvent>().Count(e => e.Kind == NativeEquipmentInvocationKind.Bagua) == 1 &&
            s.E<JudgmentRequestedEvent>().Count(e => e.Reason == JudgmentReasons.BaguaDefense) == 1 &&
            s.E<JudgmentResolvedEvent>().Count(e => e.Reason == JudgmentReasons.BaguaDefense) == 1 &&
            pair.All(id => s.G.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
                s.G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location == CardLocation.DiscardPile),
            "The original armor judgment resumes once and both accepted Spear materials settle once through the real native use.");
    }

    private sealed class Fixture(bool onlyOwned = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shen-hua-tuo", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            b.AddCard(new("classic:zhangba-serpent-spear", "丈八蛇矛", "装备牌", "装备至武器槽；攻击范围3，你可以将两张手牌当一张杀使用或打出。", CardKind.ZhangbaSerpentSpear));
            var json = $$"""
              {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
                  {"id":"touch","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":1}]},
                  {"id":"prepare","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"turnOver","target":"selectedTarget"},{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                  {"id":"arm-target","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"cost"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"cost"}]}]},
                {"id":"fixture:shen-ht-no-draw","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
                {"id":"{{Pulse}}","revision":1,"triggers":[{"id":"pulse","window":"drawPhaseStarting","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                {"id":"{{DrawWatch}}","revision":1,"triggers":[{"id":"pre-draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
                {"id":"{{Watch}}","revision":1,"triggers":[{"id":"watch","window":"afterHpLost","subject":"owner","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}]}
              """;
            var labels = new Dictionary<string, object> { [Driver] = new { name = "发动基准", description = "真实发动与复原小夹具" }, [Pulse] = new { name = "真实脉动", description = "实际摸牌发动" },
                [Watch] = new { name = "失体力观察", description = "实际体力变化子窗口" }, [DrawWatch] = new { name = "摸牌前暂停", description = "真实摸牌请求子窗口" }, [Quiet] = new { name = "安静同行", description = "跳过无关出牌" }, ["fixture:shen-ht-no-draw"] = new { name = "无正常摸牌", description = "固定手牌的内部维护" } };
            var catalog = SkillProgramCatalog.Load(json, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            foreach (var id in new[] { "fixture:shen-ht-gain-one", "fixture:shen-ht-gain-two", "fixture:shen-ht-gain-three" }) b.AddSkill(new(id, id, "寰道人物池技能"));
            b.AddGeneral(new(A, "同名甲", "", Quiet, "wei", 12, [Pulse, Watch]) { CharacterId = "character:shen-ht-family", VariantId = "fixture-a", RulesetId = "fixture" });
            b.AddGeneral(new(B, "同名乙", "", "fixture:shen-ht-gain-one", "wei", 12, ["fixture:shen-ht-gain-two"]) { CharacterId = onlyOwned ? "character:shen-ht-other" : "character:shen-ht-family", VariantId = "fixture-b", RulesetId = "fixture" });
            b.AddGeneral(new(C, "同名丙", "", "fixture:shen-ht-gain-three", "wei", 12) { CharacterId = onlyOwned ? "character:shen-ht-other" : "character:shen-ht-family", VariantId = "fixture-c", RulesetId = "fixture" });
            b.AddGeneral(new(Enemy, "安静角色", "", Quiet, "wei", 12));
            b.AddDeck(new(Deck, "固定牌堆", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "神华佗原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, A, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use(string skill, string activation, IReadOnlyList<int> cards, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, skill, activation, cards, targets, G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 120; i++)
            {
                if (done()) return;
                if (P?.SkillPrompt?.SkillId == DrawWatch) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (P is null || P.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
                else throw new InvalidOperationException("Unexpected Shen Hua Tuo boundary " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Bounded Shen Hua Tuo occurrence not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "Cold restore preserves the actual invocation source and recipient-only choice.");
        }
        public void Accept(GameCommand c)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected command."); }
        public void Need(bool ok, string reason)
        { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.CurrentSeat, G.State.TurnNumber, G.State.Phase, P, Facts = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool onlyOwned = false, bool equipment = false, bool fragile = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(onlyOwned));
            var extra = new List<string> { Driver, "fixture:shen-ht-no-draw" }; if (equipment) extra.Add(DrawWatch);
            var hand = new List<ShanheStartingCard> { new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Heart, 8) };
            if (equipment) hand.Add(new(CardKind.BaguaFormation, Suit.Club, 2));
            if (fragile) hand.Add(new(CardKind.Peach, Suit.Heart, 3));
            var setup = new ShanheBattleSetup(37, 1, "shen-ht-native", Hero, 3, 0, 0, 0, 0, extra,
                hand, equipment ? [new(CardKind.ZhangbaSerpentSpear, Suit.Spade, 12)] : [],
                [new(Enemy) { StartingHand = [] }, new(Enemy) { StartingHand = [] }],
                [new(A, Hp: fragile ? 1 : null, ExtraSkillIds: ["fixture:shen-ht-no-draw"]) { StartingHand = [new(CardKind.Slash, Suit.Club, 4)] }], new string('e', 64)) { DefaultGodFactionId = "wu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
