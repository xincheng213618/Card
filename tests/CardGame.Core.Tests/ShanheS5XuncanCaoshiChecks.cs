using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5XuncanCaoshiChecks
{
    private const string Driver = "fixture:s5-xuncan-driver", Quiet = "fixture:s5-xuncan-quiet", Watch = "fixture:s5-xuncan-gain";
    private const string Girl = "fixture:s5-xuncan-girl", Peer = "fixture:s5-xuncan-peer", Deck = "fixture:s5-xuncan-deck";

    public static void YunshenBothNativeColdDirectionsAndLivingFenchaiRecoverCold()
    {
        foreach (var reverse in new[] { false, true })
        {
            var s = Scene.Start(slashProbe: !reverse); s.Play();
            if (!reverse) Need(s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
                "An actual physical Slash is legal before Yunshen spends the ordinary Play quota.");
            s.BeginYunshen();
            s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
            Need(s.V(1).Hp == 3 && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == StandardShanheS5XuncanCaoshiPackage.YunshenId && f.InstructionIndex == 2),
                "The real recovery is paid and its own child remains on the accepted original Yunshen frame.");
            s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "shanhe-s5-yunshen") == true);
            Need(s.V(1).Hp == 3 && s.E<ShanheS5FenchaiBoundEvent>().Single() is { OwnerSeat: 0, TargetSeat: 1 },
                "The actual selected female recovers before the direction choice and is Fenchai's first printed-skill target.");
            s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("actor-seat") == (reverse ? "1" : "0"));
            s.Offer(StandardShanheS5XuncanCaoshiPackage.ShangshenId);
            var issued = s.E<ShanheS5YunshenIssuedEvent>().Single();
            var target = reverse ? 0 : 1;
            Need(issued.ActorSeat == (reverse ? 1 : 0) && issued.TargetSeat == target &&
                s.E<DamageAppliedEvent>().Single() is { Amount: 1, Nature: DamageNature.Cold } damage &&
                damage.SourceSeat == issued.ActorSeat && damage.TargetSeat == target && damage.SourceCardUseResolutionId == issued.UseId,
                "Each direction is an actual colorless native Cold Slash with its own actor, source use and one real Cold damage segment.");
            s.Cold(); s.Invoke(true);
            s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
            var childPaid = s.E<ShanheS5XuncanDrawPaidEvent>().Single();
            Need(s.V(target).HandCount == 4 && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f =>
                    f.SkillId == StandardShanheS5XuncanCaoshiPackage.ShangshenId && f.Id == childPaid.FrameId && f.NativeDraw is null) &&
                childPaid is { Requested: 4, Actual: 4 } && childPaid.TargetSeat == target &&
                s.G.CardMovements.Count(m => m.Sequence > childPaid.Before && m.Sequence <= childPaid.After &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(target) && m.Reason.Value == "shanhe.s5.shangshen.refill") == 4 &&
                s.E<JudgmentResolvedEvent>().Single(e => e.Reason == "shanhe.s5.shangshen.lightning") is { Suit: Suit.Heart, Rank: 6 } &&
                !s.E<DamageAppliedEvent>().Any(e => e.SourceLess),
                "Living Fenchai overrides the actual Spade-six judgment to Heart; refill entities are already paid while their real gain child is paused.");
            s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
            Need(s.E<ShanheS5XuncanDrawPaidEvent>().Single() is { Requested: 4, Actual: 4 } paid && paid.TargetSeat == target &&
                s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == issued.UseId && e.CardKind == CardKind.ColdSlash) == 1 &&
                s.E<ShanheS5FirstElementalDamageEvent>().Length == 1,
                "Native draw and the issued Cold Slash each return once to their original frames without a second first-elemental offer.");
            Need(s.E<CardUseDebitRecordedEvent>().Count(e => e.Debit.CardActionId == issued.ActionId) == (reverse ? 0 : 1) &&
                s.E<CardUseDebitRecordedEvent>().Length == (reverse ? 0 : 1) &&
                (reverse || s.V().Hand.Any(c => c.Kind == CardKind.Slash) && !s.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash)),
                "The actual owner-to-target use spends exactly one ordinary Slash quota, disables the retained physical Slash, and cold recovery never repeats it; the opposite actor never debits Xuncan.");
            s.Cold();
        }

        var spent = Scene.Start(slashProbe: true); spent.Play(); spent.PhysicalSlash(1); spent.Play();
        Need(spent.E<CardUseDebitRecordedEvent>().Length == 1, "The first real physical Slash pays the one ordinary Play quota.");
        spent.BeginYunshen(); spent.Until(() => spent.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "shanhe-s5-yunshen") == true);
        Need(spent.P!.Choices.Count == 1 && spent.P.Choices.Single().Parameters.GetValueOrDefault("actor-seat") == "1",
            "With Xuncan's ordinary quota already spent, only the legal outside-turn reverse direction is offered after the real recovery.");
        spent.Cold(); spent.Answer(c => c.Parameters.GetValueOrDefault("actor-seat") == "1");
        spent.Offer(StandardShanheS5XuncanCaoshiPackage.ShangshenId); spent.Invoke(false); spent.Play();
        Need(spent.E<ShanheS5YunshenIssuedEvent>().Single() is { ActorSeat: 1, TargetSeat: 0 } && spent.E<CardUseDebitRecordedEvent>().Length == 1,
            "The selected role's real reverse Cold Slash resolves while preserving the one existing owner debit.");
        spent.Cold();
    }

    public static void ShangshenFirstOccurrenceDeclineAndDeadFenchaiLightningRecoverCold()
    {
        var s = Scene.Start(); s.Play(); s.BeginDriver("fire", [2]);
        s.Offer(StandardShanheS5XuncanCaoshiPackage.ShangshenId); s.Invoke(false); s.Play();
        s.BeginDriver("fire", [2]); s.Play();
        Need(s.E<DamageAppliedEvent>().Length == 2 && s.E<ShanheS5FirstElementalDamageEvent>().Length == 1 &&
            !s.E<JudgmentResolvedEvent>().Any(e => e.Reason == "shanhe.s5.shangshen.lightning"),
            "Declining the first real elemental segment does not allow activation on the second segment of the same turn.");
        s.Cold();

        s = Scene.Start(); s.Play(); s.BeginYunshen();
        s.Until(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "shanhe-s5-yunshen") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("actor-seat") == "0");
        s.Offer(StandardShanheS5XuncanCaoshiPackage.ShangshenId); s.Invoke(false); s.Play();
        s.BeginDriver("kill", [1]); s.Play();
        Need(!s.V(1).IsAlive && s.E<ShanheS5FenchaiBoundEvent>().Single().TargetSeat == 1,
            "The original bound female dies through real damage and dying; Fenchai retains that exact target.");
        var turn = s.E<TurnStartedEvent>().Last().TurnNumber;
        s.EndPlay(); s.Until(() => s.P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && s.E<TurnStartedEvent>().Last().TurnNumber > turn);
        var hp = s.V().Hp; s.BeginDriver("fire", [2]); s.Offer(StandardShanheS5XuncanCaoshiPackage.ShangshenId); s.Cold(); s.Invoke(true);
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        Need(s.E<JudgmentResolvedEvent>().Single(e => e.Reason == "shanhe.s5.shangshen.lightning") is { Suit: Suit.Spade, Rank: 6 } &&
            s.E<DamageAppliedEvent>().Last() is { TargetSeat: 0, Amount: 3, Nature: DamageNature.Thunder, SourceLess: true } &&
            s.V().Hp == hp - 3 && s.V(2).HandCount == 4,
            "Dead Fenchai yields the real Spade-six Lightning result, three source-less Thunder damage, then refills the original damaged recipient.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        Need(s.E<ShanheS5XuncanDrawPaidEvent>().Single() is { TargetSeat: 2, Requested: 4, Actual: 4 } &&
            s.E<ShanheS5FirstElementalDamageEvent>().Length == 2,
            "Nested Lightning cannot become another first-elemental opportunity and the original refill returns once.");
        s.Cold();
    }

    public static void CaoshiPreparationLeijieNativeJudgmentGuidaoAndDrawRecoverCold()
    {
        foreach (var replace in new[] { false, true })
        {
            var s = Scene.Start(caoshi: true); s.Offer(StandardShanheS5XuncanCaoshiPackage.LeijieId); s.Invoke(true);
            s.Until(() => s.P?.Choices.Any(c => c.Targets.SequenceEqual([2])) == true); s.Answer(c => c.Targets.SequenceEqual([2]));
            s.Until(() => s.P is { Kind: DecisionKind.ProgramJudgmentReplacement, PlayerSeat: 0 });
            Need(s.G.ResolutionStack.OfType<JudgmentFrame>().Single() is { TargetSeat: 2, Reason: "shanhe.s5.leijie" },
                "Caoshi's real preparation selects a recipient to judge, rather than using the ordinary Play-phase two-Slash skill.");
            var material = s.V().Hand.Single().Id; s.Cold();
            s.Answer(c => replace ? c.Cards.SequenceEqual([material]) : c.Parameters.GetValueOrDefault("action") == "program-judgment-replace-skip");
            s.Play();
            var result = s.E<JudgmentResolvedEvent>().Single(e => e.Reason == "shanhe.s5.leijie");
            Need(!s.E<CardUseDeclaredEvent>().Any(e => e.CardKind is CardKind.Slash or CardKind.ThunderSlash),
                "Leijie creates no invented Slash, Slash response or Slash quota payment.");
            if (replace)
                Need(result is { Suit: Suit.Spade, Rank: 6 } && s.E<DamageAppliedEvent>().Single() is
                        { SourceSeat: 0, TargetSeat: 2, Amount: 2, Nature: DamageNature.Thunder, SourceLess: false } &&
                    s.E<ProgramJudgmentReplacementResolvedEvent>().Single(e => e.SkillId == "boundary:guidao") is
                        { Activated: true, DrawnCards: 1 } && s.V(2).HandCount == 0,
                    "The exact source-883 Guidao replaces with a real black Spade-six, draws its native one-card reward and Leijie causes two genuine Thunder damage.");
            else
                Need(result is { Suit: Suit.Club, Rank: 6 } && !s.E<DamageAppliedEvent>().Any() &&
                    s.E<ShanheS5XuncanDrawPaidEvent>().Single() is { TargetSeat: 2, Requested: 2, Actual: 2 } && s.V(2).HandCount == 2,
                    "A non-hit native preparation judgment draws two actual cards for that judged recipient.");
            s.Cold();
        }
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, G.PlayerCount).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand c) { var r = G.Submit(c); Need(r.Accepted, r.Error?.ToString() ?? "The actual S5 Xuncan/Caoshi command failed."); }
        public void Answer(Func<PromptChoice, bool> pick) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, G.Revision)); }
        public void Invoke(bool yes) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == (yes ? "activate" : "skip"));
        public void Offer(string skill) => Until(() => P is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == skill);
        public void Play() => Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void BeginDriver(string id, int[] target) => Accept(new UseProgramSkillCommand(0, Driver, id, [], target, G.Revision, P!.PromptId));
        public void BeginYunshen() => Accept(new UseProgramSkillCommand(0, StandardShanheS5XuncanCaoshiPackage.YunshenId, "heal-and-cold-slash", [], [1], G.Revision, P!.PromptId));
        public void PhysicalSlash(int target)
        {
            var action = G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind));
        }
        public void Until(Func<bool> done)
        {
            for (var n = 0; n < 150; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small Xuncan/Caoshi fixture must keep both teams present.");
                if (P?.SkillPrompt?.SkillId == Watch) { Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); continue; }
                if (P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }) { Answer(c => c.Parameters.GetValueOrDefault("response") == "pass"); continue; }
                if (P?.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Xuncan/Caoshi native boundary: " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded Xuncan/Caoshi native continuation was not reached.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, G.PlayerCount).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves actual direction, first damage, bound opposite-gender target, judgment and paid native Draw child.");
        }
        public static Scene Start(bool caoshi = false, bool slashProbe = false)
        {
            var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new StandardShanheS5XuncanCaoshiPackage(), new Fixture(caoshi)]);
            var general = caoshi ? StandardShanheS5XuncanCaoshiPackage.CaoshiId : StandardShanheS5XuncanCaoshiPackage.XuncanId;
            var setup = new ShanheBattleSetup(73, 1, "fixture-s5-xuncan", general, 6, 5, 0, 0, 0, [Driver, Watch],
                caoshi ? [new(CardKind.Dodge, Suit.Spade, 6)] : slashProbe ? [new(CardKind.Slash, Suit.Club, 7)] : [], [],
                [new(Girl, Hp: 2) { StartingHand = [] }, new(Peer, Hp: 12, MaxHpBonus: 8) { StartingHand = [] }, new(Peer, Hp: 12, MaxHpBonus: 8) { StartingHand = [] }],
                [], new string('f', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(bool caoshi) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-xuncan", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}],"activations":[
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire","sourceRef":{"kind":"owner"}}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":5,"sourceRef":{"kind":"owner"}}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[
                {"id":"paid-gain-child","window":"cardsGained","subject":"owner","movementOccurrence":"perBatch","destinationZones":["hand"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]},
                {"id":"paid-recovery-child","window":"afterHpRecovered","subject":"owner","hpChangeOccurrence":"perEvent","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovery","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "实际伤害", description = "真实火伤与死亡，不注入判定或伤害事实" },
                    [Quiet] = new { name = "静默同桌", description = "仅跳过自主出牌" },
                    [Watch] = new { name = "实际获得牌孩子", description = "暂停真实获得牌子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Girl, "真实异性目标", "supporter", Quiet, "qun", 4, [Watch], GeneralGender.Female));
            b.AddGeneral(new(Peer, "真实其他目标", "supporter", Quiet, "qun", 4, [Watch]));
            b.AddDeck(new(Deck, "固定原生判定库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:dodge", caoshi ? Suit.Club : Suit.Spade, 6)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "荀粲曹氏真实流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5XuncanCaoshiPackage.XuncanId, StandardShanheS5XuncanCaoshiPackage.CaoshiId, Girl, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
