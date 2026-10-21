using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WushuangSkillsBChecks
{
    private const string Mode = "identity:classic-wushuang-b", Driver = "fixture:wushuang-b-driver", Human = "fixture:wushuang-b-human";
    private static string Skill(int id) => $"wushuang:skill-{id}";

    public static void WeiyiYinglueAndZhendanNativeRules()
    {
        var (g, registry) = Create([3289, 3290, 3331], CardKind.DrawTwo);
        Play(g);
        Activate(g, Driver, "damage", [1]);
        Reach(g, p => p.SkillPrompt?.SkillId == Skill(3289));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "health"));
        Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("value") == "lose");
        Play(g);
        Require(g.CreateSnapshot(0).Players[1].Hp == 6, "Weiyi loses HP after actual damage to a higher-HP target.");
        Activate(g, Driver, "damage", [1]); Play(g);
        Require(Facts<WushuangWeiyiUsedEvent>(g).Count() == 1, "Each recipient can receive Weiyi only once per game.");
        Activate(g, Skill(3331), "draw-next-handlimit", [0]); Play(g);
        Require(Facts<WushuangYinglueArmedEvent>(g).Single().Amount == -2, "Yinglue schedules its printed next-discard penalty on a self target.");
        Activate(g, Skill(3331), "lose-hp-next-draw", [0]); Play(g);
        Require(Facts<WushuangYinglueArmedEvent>(g).Count() == 2, "Yinglue's two choices each have an independent once-per-phase allowance.");
        var slash = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Skill(3290) && a.Kind == LegalActionKind.Slash);
        Use(g, slash); Play(g);
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.ConversionSource?.SkillId == Skill(3290)),
            "Zhendan cannot reuse the Slash name in the same actual round, including elemental variants.");
        Activate(g, Driver, "self-damage", []); Play(g);
        Require(Facts<WushuangZhendanDisabledEvent>(g).Count() == 1 && !g.GetHumanLegalActions().Any(a => a.ConversionSource?.SkillId == Skill(3290)),
            "Receiving damage draws once for the actual round turn count and disables all Zhendan conversion directions.");
        End(g);
        Reach(g, p => p.Kind == DecisionKind.DiscardCards || g.State.TurnNumber > 1);
        Require(Facts<WushuangYinglueAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Phase == TurnPhase.Discard && e.Amount == -2),
            "Yinglue's next discard penalty applies in this turn's actual discard phase.");
        Play(g);
        Require(Facts<WushuangYinglueAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Phase == TurnPhase.Draw && e.Amount == 2),
            "Yinglue draw adjustment applies to the next actual Draw phase, even though normal Draw omits PhaseChangedEvent.");
        Cold(g, registry);
    }

    public static void LiantaoAndXiaojieNativeUsesReplay()
    {
        var (duels, duelRegistry) = Create([3301], CardKind.DrawTwo, initialHp: 5, otherHp: 30);
        Reach(duels, p => p.SkillPrompt?.SkillId == Skill(3301));
        Answer(duels, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(duels, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "target"));
        Cold(duels, duelRegistry); Answer(duels, c => c.Targets.SequenceEqual([1])); Play(duels);
        var uses = Facts<WushuangBNativeIssuedEvent>(duels).ToArray();
        Require(uses.Length > 1 && uses.All(e => e.Return.OutputKind == CardKind.Duel) &&
            Facts<WushuangBNativeReturnedEvent>(duels).Count() == uses.Length,
            "Liantao issues and completes each matching physical hand card as a separate native Duel.");
        Require(duels.CardMovements.Count(m => m.Reason.Value == "skill-program.wushuang-b.draw" && m.To == CardLocation.Hand(0)) == uses.Length,
            "Liantao draws exactly the damage it actually dealt to the chosen target.");
        Cold(duels, duelRegistry);

        var (echo, echoRegistry) = Create([3333], CardKind.DrawTwo, initialHp: 8, attackOther: true);
        Play(echo); Use(echo, echo.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo)); Play(echo); End(echo);
        Reach(echo, p => p.SkillPrompt?.SkillId == Skill(3333));
        Answer(echo, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(echo, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "trick")); Cold(echo, echoRegistry);
        Answer(echo, c => c.Parameters.GetValueOrDefault("value") != "draw");
        Play(echo);
        Require(Facts<WushuangXiaojieAcceptedEvent>(echo).Count() == 1 && Facts<WushuangBNativeIssuedEvent>(echo).Single().Return.OutputKind == CardKind.DrawTwo,
            "Xiaojie declines response to its incoming Slash and uses the owner's last ordinary trick after that Slash completes.");
        Cold(echo, echoRegistry);
    }

    public static void BianyuIdentityAndChunlaoAnyHpLoss()
    {
        var (cardless, cardlessRegistry) = Create([3316, 3218], CardKind.DrawTwo, initialHp: 8);
        Play(cardless);
        Require(cardless.GetHumanLegalActions().Any(a => a.CardId == 0 && a.ConversionSource?.SkillId == "ol:weijing"),
            "Bianyu ignores the synthetic zero-card material of a real Weijing Slash without looking up card zero in the zone store.");
        Use(cardless, cardless.GetHumanLegalActions().First(a => a.CardId == 0 && a.ConversionSource?.SkillId == "ol:weijing"));
        Play(cardless); Cold(cardless, cardlessRegistry);
        Require(Facts<CardActionAcceptedEvent>(cardless).Any(e => e.Action.EffectiveKind == CardKind.Slash && e.Action.PhysicalCards.Count == 0),
            "The genuine material-free Slash commits and restores beside Bianyu without inventing a physical card zero.");
        var (marks, markRegistry) = Create([3316], CardKind.DrawTwo, initialHp: 8, attackOther: true);
        Play(marks); End(marks);
        Reach(marks, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "mark"));
        Answer(marks, c => c.Parameters.GetValueOrDefault("value") != "done");
        Answer(marks, c => c.Parameters.GetValueOrDefault("value") == "done"); Play(marks);
        Require(Facts<WushuangBianyuMarkedEvent>(marks).Any(e => e.TargetSeat == 0), "Incoming Slash damage can mark the damaged owner's hand entity.");
        var markedId = Facts<WushuangBianyuMarkedEvent>(marks).Last(e => e.TargetSeat == 0).CardId;
        Require(marks.GetHumanLegalActions().Any(a => a.CardId == markedId && a.Kind == LegalActionKind.Slash),
            "A Bianyu mark changes that exact physical hand entity into a native Slash."); Cold(marks, markRegistry);
        Use(marks, marks.GetHumanLegalActions().First(a => a.CardId == markedId && a.Kind == LegalActionKind.Slash));
        Reach(marks, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "mark"));
        Answer(marks, c => c.Parameters.GetValueOrDefault("value") == "done"); Play(marks);
        Require(marks.CreateCardZoneDiagnostics().Single(z => z.CardId == markedId).Location == CardLocation.DiscardPile,
            "Using the transformed Slash moves the original physical entity exactly once.");

        var (wine, wineRegistry) = Create([3336], CardKind.Slash);
        Play(wine); Activate(wine, Driver, "discard-one", []); Play(wine);
        Require(wine.CreateSnapshot(0).Players[0].ChunlaoCount == 1, "The current Chunlao version collects its actual discarded Slash.");
        Activate(wine, Driver, "other-hp-loss", [1]);
        Reach(wine, p => p.SkillPrompt?.SkillId == Skill(3336));
        Answer(wine, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(wine, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "chun"));
        Cold(wine, wineRegistry); Answer(wine, c => c.Cards.Count == 1); Answer(wine, c => c.Parameters.GetValueOrDefault("value") == "done"); Play(wine);
        Require(wine.CreateSnapshot(0).Players[0].ChunlaoCount == 0,
            "Another character losing HP offers Chunlao and returns its selected public Chun to its owner's hand.");
        Cold(wine, wineRegistry);
    }

    public static void DinganSecondNameAndLargestHandDiscard()
    {
        var (g, registry) = Create([3335], CardKind.DrawTwo);
        Play(g); Use(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo)); Play(g);
        Use(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "draw-target"));
        Answer(g, c => c.Targets.SequenceEqual([1]));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "punish")); Cold(g, registry);
        var before = g.CreateSnapshot(1).Players[1].Hand.Count;
        Answer(g, c => c.Parameters.GetValueOrDefault("value") == "discard-1"); Play(g);
        Require(before > 1 && g.CreateSnapshot(1).Players[1].Hand.Count == 0 && Facts<WushuangDinganDrawnEvent>(g).Count() == 1,
            "Dingan's second same-name use draws for a non-target once, then the largest-hand other discards its largest same-name group.");
        Cold(g, registry);
    }

    public static void OfficialJiushiActiveAlcoholAndDamageFace()
    {
        var (g, registry) = Create([3269], CardKind.DrawTwo, initialHp: 6);
        Play(g); Activate(g, Skill(3269), "flip-use-alcohol", []); Play(g);
        Require(g.CreateSnapshot(0).Players[0].IsFaceDown && Facts<CardUseDeclaredEvent>(g).Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.Alcohol),
            "Official Jiushi pays the face-down cost and issues an actual native Alcohol use.");
        Cold(g, registry); Activate(g, Driver, "self-damage", []);
        Reach(g, p => p.SkillPrompt?.SkillId == Skill(3269));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play(g);
        Require(!g.CreateSnapshot(0).Players[0].IsFaceDown, "Being face down at the actual damage start permits the post-damage face-up option.");
        Cold(g, registry);

        var (dying, dyingRegistry) = Create([3269], CardKind.DrawTwo, initialHp: 1);
        Play(dying); Activate(dying, Driver, "self-damage", []);
        Reach(dying, p => p.SkillPrompt?.SkillId == Skill(3269) && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "flip"));
        Cold(dying, dyingRegistry); Answer(dying, c => c.Parameters.GetValueOrDefault("option-id") == "flip"); Play(dying);
        Require(dying.CreateSnapshot(0).Players[0].Hp == 1 && dying.CreateSnapshot(0).Players[0].IsFaceDown,
            "A face-up victim flipping down for dying Alcohol must not also receive the face-up after-damage benefit.");
        Cold(dying, dyingRegistry);
    }

    public static void OfficialGuzhengAndDuwuVersionDifferences()
    {
        var (g, registry) = Create([3139], CardKind.DrawTwo, otherHp: 1);
        Reach(g, p => p.SkillPrompt?.SkillId == Skill(3139));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "guzheng-return"));
        Cold(g, registry); var target = g.ResolutionStack.OfType<ProgramSkillFrame>().Last().WushuangB!.TargetSeat;
        var selected = P(g)!.Choices[0].Cards.Single(); Answer(g, c => c.Cards.SequenceEqual([selected]));
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("step") == "guzheng-claim"));
        Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("value") == "pass");
        Reach(g, p => p.SkillPrompt?.SkillId == Skill(3139) || p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(g.CreateCardZoneDiagnostics().Single(z => z.CardId == selected).Location == CardLocation.Hand(target) &&
            !g.CardMovements.Any(m => m.Reason.Value == "skill-program.wushuang-guzheng.claim"),
            "Official Guzheng returns a real discard entity and permits declining the remainder.");

        var (duwu, duwuRegistry) = Create([3062], CardKind.DrawTwo, otherHp: 1);
        Play(duwu); var hp = duwu.CreateSnapshot(0).Players[0].Hp;
        Activate(duwu, Skill(3062), "target-hp-cost", [1]);
        Reach(duwu, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "dynamic-discard-damage"));
        Cold(duwu, duwuRegistry); Answer(duwu, c => c.Cards.Count == 1); Play(duwu);
        Require(!duwu.CreateSnapshot(0).Players[1].IsAlive && duwu.CreateSnapshot(0).Players[0].Hp == hp - 1 &&
            Facts<DynamicDiscardDamagePenaltyEvent>(duwu).Count() == 1 &&
            !duwu.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill(3062)),
            "Official Duwu loses HP and disables after its exact victim enters dying, including an unsaved fatal target.");
        Cold(duwu, duwuRegistry);
    }

    public static void OfficialFuqiNonTargetResponseAndGuanchaoTrueUse()
    {
        var (g, registry) = Create([3100, 3076], CardKind.Duel, initialHp: 8);
        Reach(g, p => p.SkillPrompt?.SkillId == Skill(3076));
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, p => p.Choices.Any(c => c.Parameters.ContainsKey("ascending")));
        Answer(g, c => c.Parameters.GetValueOrDefault("ascending") == "true"); Play(g);
        var before = Facts<ProgramGuanchaoRankDrawEvent>(g).Count();
        Use(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Duel && a.TargetSeats.SequenceEqual([2])));
        Reach(g, p => p.Kind == DecisionKind.Nullification && p.PlayerSeat == 0 && p.Choices.Any(c => c.Cards.Count == 1));
        Cold(g, registry); Answer(g, c => c.Cards.Count == 1); Play(g);
        Require(Facts<ProgramGuanchaoRankDrawEvent>(g).Skip(before).Count(e => e.Drew) >= 2,
            "Official Guanchao counts the owner's physical trick and its true Nullification response use in increasing rank order.");
        Require(Facts<NullificationRequestedEvent>(g).Where(e => e.SourceSeat == 0 && e.ChainDepth == 0).Any(e => e.ResponderSeat == 2) &&
            !Facts<NullificationRequestedEvent>(g).Where(e => e.SourceSeat == 0 && e.ChainDepth is 0 or 2).Any(e => e.ResponderSeat is 1 or 3),
            "Official Fuqi suppresses adjacent non-target Nullification providers while the distance-two target can respond.");
        Cold(g, registry);
    }

    public static void OfficialQiangzhiDingcuoAndLonghunRules()
    {
        var (qiangzhi, qiangzhiRegistry) = Create([3075], CardKind.DrawTwo, initialHp: 8);
        Reach(qiangzhi, p => p.SkillPrompt?.SkillId == Skill(3075));
        Answer(qiangzhi, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(qiangzhi, p => p.Choices.Any(c => c.Targets.SequenceEqual([1]))); Answer(qiangzhi, c => c.Targets.SequenceEqual([1])); Play(qiangzhi);
        var hand = qiangzhi.CreateSnapshot(0).Players[0].Hand.Count;
        Use(qiangzhi, qiangzhi.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo));
        Reach(qiangzhi, p => p.SkillPrompt?.SkillId == Skill(3075)); Cold(qiangzhi, qiangzhiRegistry);
        Answer(qiangzhi, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play(qiangzhi);
        Require(qiangzhi.CreateSnapshot(0).Players[0].Hand.Count == hand + 2,
            "Official Qiangzhi records the revealed category and offers its extra draw during the chosen Play phase.");

        var (dingcuo, dingcuoRegistry) = Create([3291], CardKind.DrawTwo, initialHp: 6);
        Play(dingcuo); hand = dingcuo.CreateSnapshot(0).Players[0].Hand.Count;
        Activate(dingcuo, Driver, "self-damage", []); Reach(dingcuo, p => p.SkillPrompt?.SkillId == Skill(3291));
        Cold(dingcuo, dingcuoRegistry); Answer(dingcuo, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Play(dingcuo);
        Require(dingcuo.CreateSnapshot(0).Players[0].Hand.Count == hand + 2, "A self-inflicted actual damage offers Dingcuo once as damage taken.");
        Activate(dingcuo, Driver, "self-damage", []); Play(dingcuo);
        Require(Facts<ProgramBindingStartedEvent>(dingcuo).Count(e => e.SkillId == Skill(3291)) == 1,
            "Dingcuo's shared turn allowance prevents both duplicate caused/taken dispatch and a second self-damage draw."); Cold(dingcuo, dingcuoRegistry);

        var (longhun, longhunRegistry) = Create([3023], CardKind.Dodge, initialHp: 8, attackOther: true);
        Reach(longhun, p => p.Kind == DecisionKind.RespondDodge && p.Choices.Any(c => c.Cards.Count == 2 && c.Parameters.GetValueOrDefault("conversion-skill-id") == Skill(3023)));
        var turnOwner = longhun.State.CurrentSeat;
        Answer(longhun, c => c.Cards.Count == 2 && c.Parameters.GetValueOrDefault("conversion-skill-id") == Skill(3023));
        Reach(longhun, p => p.SkillPrompt?.SkillId == Skill(3023)); Cold(longhun, longhunRegistry);
        Require(!P(longhun)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") is "activate" or "skip"),
            "Official Longhun's two-black-card actual use has a compulsory discard branch."); Play(longhun);
        Require(longhun.CardMovements.Count(m => m.From == CardLocation.Hand(turnOwner) && m.To == CardLocation.DiscardPile && m.Reason.Value.Contains(Skill(3023), StringComparison.Ordinal)) == 1,
            "The true two-club Dodge use discards one actual entity from the current turn owner."); Cold(longhun, longhunRegistry);
    }

    public static void OfficialZengouFirstDodgeMultiTargetAndDyingCost()
    {
        foreach (var dying in new[] { false, true })
        {
            var (g, registry) = Create(dying ? [3287, 3269] : [3287], CardKind.Slash, initialHp: dying ? 1 : 8);
            Play(g);
            Use(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1, 3])));
            Reach(g, p => p.SkillPrompt?.SkillId == Skill(3287));
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "wushuang-zengou"));
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Last(); var paid = root.WushuangZengou!;
            var attack = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == paid.AttackFrameId);
            Require(attack.TargetSeats.Count == 2 && attack.CardAttack!.RequiredDodgeResponses == 2 && paid.DodgeCursor == 0,
                "Official Zengou pauses the very first true Dodge use of a double-Dodge, multi-target Slash.");
            var dodge = paid.Cards.Single(); Cold(g, registry);
            if (dying)
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("cost") == "hp");
                Reach(g, p => p.SkillPrompt?.SkillId == Skill(3269) && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "flip"));
                Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "flip");
            }
            else Answer(g, c => c.Cards.Count == 1);
            Play(g);
            Require(Facts<WushuangZengouPaidEvent>(g).Count() == 1 && Facts<WushuangZengouInvalidatedEvent>(g).Count() == 1 &&
                Facts<WushuangZengouGainedCardEvent>(g).Single().CardId == dodge &&
                g.CreateCardZoneDiagnostics().Single(z => z.CardId == dodge).Location == CardLocation.Hand(0) &&
                g.CreateSnapshot(0).Players[1].Hp == 7,
                "One exact paid response is canceled and its actual Dodge entity is obtained, including after genuine Dying child recovery.");
            Require(!Facts<RequiredResponseProgressEvent>(g).Any(e => e.ResponderSeat == 1 && e.ResponseCount > 0),
                "The nullified first Dodge is not counted as a successful response or deferred until full Slash dodge.");
            Cold(g, registry);
        }
    }

    private static IEnumerable<T> Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(c); Require(r.Accepted, r.Error?.Message ?? "B skill command rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, (p.Choices.FirstOrDefault(select) ?? throw new InvalidOperationException(JsonSerializer.Serialize(p))).Id, g.Revision)); }
    private static void Use(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Activate(GameEngine g, string skill, string activation, int[] targets) => Accept(g, new UseProgramSkillCommand(0, skill, activation,
        activation == "discard-one" ? [g.CreateSnapshot(0).Players[0].Hand[0].Id] : [], targets, g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> done)
    { for (var i = 0; i < 220; i++) { if (P(g) is { } p && done(p)) return; Step(g); } throw new InvalidOperationException("Wushuang B boundary missing: " + JsonSerializer.Serialize(P(g))); }
    private static void Step(GameEngine g)
    {
        if (P(g) is { Kind: DecisionKind.DiscardCards } d) Accept(g, new DiscardCardsCommand(d.PlayerSeat, d.ValidCardIds.Take(d.RequiredCardCount).ToArray(), d.PromptId, g.Revision));
        else if (P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) End(g);
        else if (P(g) is { } p && p.PlayerSeat == 0 && p.Choices.Count > 0)
        {
            var c = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die") ?? p.Choices[0];
            Accept(g, new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision));
        }
        else { if (g.State.Status == EngineStatus.Completed) throw new InvalidOperationException("Completed: " + string.Join("\n",g.Events.TakeLast(35).Select(e=>e.Payload.GetType().Name+":"+JsonSerializer.Serialize(e.Payload,e.Payload.GetType())))); Accept(g, new AdvanceOneStepCommand(g.Revision)); }
    }
    private static void Cold(GameEngine g, ContentRegistry r)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        Require(JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements) &&
            Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(restored.CreateSnapshot(s))),
            "B skills cold-restore their owning frames, hidden prompts and exact physical movements.");
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static (GameEngine, ContentRegistry) Create(int[] skills, CardKind cards, int initialHp = 3, int otherHp = 8, bool attackOther = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(skills, cards, initialHp, otherHp, attackOther));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 17, ModeId = Mode, PlayerCount = 4, HumanSeat = 0,
            HumanRole = null, HumanTeamId = "player", UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, Human, g.Revision, P(g)!.PromptId)); return (g, r);
    }
    private sealed class Fixture(int[] skills, CardKind cards, int initialHp, int otherHp, bool attackOther) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-wushuang-b", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var asm = typeof(StandardContentPackage).Assembly;
            string Read(string suffix) { using var stream = asm.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wushuang-skills-b." + suffix + ".json")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
            var prod = SkillProgramCatalog.Load(Read("rules"), Read("presentation"));
            using (var auditStream = asm.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wushuang-skills-b-audit.rules.json")!)
            using (var auditPresentation = asm.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wushuang-skills-b-audit.presentation.json")!)
            using (var auditRulesReader = new StreamReader(auditStream))
            using (var auditPresentationReader = new StreamReader(auditPresentation))
            {
                var audit = SkillProgramCatalog.Load(auditRulesReader.ReadToEnd(), auditPresentationReader.ReadToEnd());
                foreach (var (id, program) in audit.Programs) b.AddSkill(new(id, audit.Presentations[id].Name, audit.Presentations[id].Description) { Program = program, ProgramPresentation = audit.Presentations[id] });
            }
            foreach (var (id, program) in prod.Programs) b.AddSkill(new(id, prod.Presentations[id].Name, prod.Presentations[id].Description) { Program = program, ProgramPresentation = prod.Presentations[id] });
            if (skills.Contains(3218)) b.AddSkill(StandardWushuangContentRegistry.Create().Skills["ol:weijing"]);
            var fixtures = SkillProgramCatalog.Load(Rules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString()),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Driver] = new { name = "行为驱动", description = "有实体牌和原生命令的技能规则夹具" },
                        ["fixture:wushuang-b-quiet"] = new { name = "固定对手", description = "跳过出牌" },
                        ["fixture:wushuang-b-attack"] = new { name = "对手原生杀", description = "出牌阶段对唯一对手使用杀" },
                        ["fixture:wushuang-b-twin"] = new { name = "双闪多目标夹具", description = "双目标杀各需两闪" } } }));
            foreach (var (id, program) in fixtures.Programs) b.AddSkill(new(id, id, id) { Program = program, ProgramPresentation = fixtures.Presentations[id] });
            b.AddGeneral(new(Human, "无双B组主将", "", Driver, "qun", 8, skills.Select(id => id == 3218 ? "ol:weijing" : Skill(id)).Concat(skills.Contains(3287) ? ["fixture:wushuang-b-twin"] : Array.Empty<string>()).ToArray()) { InitialHp = initialHp });
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:wushuang-b-other-{i}", "固定对手", "", "fixture:wushuang-b-quiet", "qun", otherHp, attackOther ? ["fixture:wushuang-b-attack"] : []));
            var cardId = cards == CardKind.DrawTwo ? "standard:draw_two" : cards == CardKind.Duel ? "standard:duel" : cards == CardKind.Dodge ? "standard:dodge" : "standard:slash";
            b.AddDeck(new("fixture:wushuang-b-deck", "同色原生实体", 4, 2, [])
                { PhysicalCards = Enumerable.Range(0, 240).Select(i => new ContentDeckPhysicalCard(skills.Contains(3287) ? i % 3 == 0 ? "standard:slash" : i % 3 == 1 ? "standard:dodge" : "standard:draw_two" : cards == CardKind.Duel && i % 2 == 1 ? "standard:nullification" : cardId, cards == CardKind.Dodge ? Suit.Club : Suit.Heart, cards == CardKind.Duel ? i % 2 == 1 ? 9 : 2 : 7)).ToArray() });
            b.AddMode(new(Mode, "官方技能B组夹具", 4, 4, new Dictionary<string, int>(), "fixture:wushuang-b-deck", 2,
                [Human, "fixture:wushuang-b-other-1", "fixture:wushuang-b-other-2", "fixture:wushuang-b-other-3"], ContentModeKind.Team, new Dictionary<string, int> { ["player"] = 1, ["other"] = 3 }));
        }
    }
    private const string Rules = """
    {"schemaVersion":$SCHEMA$,"skills":[
    {"id":"fixture:wushuang-b-twin","revision":1,"modifiers":[{"id":"extra-target","query":"cardTargetCount","operation":"add","value":1,"priority":0,"cardKinds":["slash","fireSlash","thunderSlash"]}],"cardPolicies":[{"id":"two-dodges","kind":"minimumResponseCount","cardKinds":["slash","fireSlash","thunderSlash"],"requiredCardKinds":["dodge"],"value":2}]},
    {"id":"fixture:wushuang-b-driver","revision":1,"activations":[
      {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
      {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
      {"id":"other-hp-loss","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
      {"id":"discard-one","minCards":1,"maxCards":1,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]}]},
    {"id":"fixture:wushuang-b-quiet","revision":1,"triggers":[{"id":"skip","window":"afterNormalDraw","subject":"owner","priority":-100,"optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
    {"id":"fixture:wushuang-b-attack","revision":1,"triggers":[{"id":"slash","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","targetRestriction":"distanceUnlimitedAgainstTarget","useCardActionWindows":true}]}]}
    ]}
    """;
}




