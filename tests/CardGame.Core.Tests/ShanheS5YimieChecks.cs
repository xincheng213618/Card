using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5YimieChecks
{
    private const string Driver = "fixture:s5-yimie-driver", Watch = "fixture:s5-yimie-health", Quiet = "fixture:s5-yimie-quiet";
    private const string Shield = "fixture:s5-yimie-shield", Peer = "fixture:s5-yimie-peer", Deck = "fixture:s5-yimie-deck";

    public static void FrozenXNativeCostDamageAndRecoveryChildrenKeepOriginalFramesCold()
    {
        foreach (var card in new[] { true, false })
        {
            var s = Scene.Start(); s.Play(); s.DriverUse("armor-one"); s.Play();
            if (card) s.Slash(); else s.DriverUse("damage-one");
            s.Offer(); s.Invoke(); s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
            var frozen = s.E<ShanheS5YimieFrozenEvent>().Single().Origin;
            Need(frozen is { OriginalDamage: 1, OriginalTargetHp: 4, X: 3, CostHpBefore: 3, TargetSeat: 1 } &&
                frozen.CardAttack == card && s.V().Hp == 2 && s.E<ProgramSkillHpLostEvent>().Single(e => e.SkillId == StandardShanheS5XiliangDarkPackage.YimieId) is { Amount: 1 } &&
                s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == frozen.ProgramFrameId) is { InstructionIndex: 2, ShanheS5YimieCost: not null },
                "Yimie freezes X from the original current damage and target HP, then pays exactly one native HP loss before its real child returns.");
            s.Cold(); s.Continue(); s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
            var recovery = s.G.ResolutionStack.OfType<ShanheS5YimieRecoveryFrame>().Single();
            Need(recovery.ParentFrameId == frozen.AttackId && recovery.Receipt.Origin == frozen && recovery.Requested &&
                s.E<DamageAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 1, Amount: 4, RemainingHp: 1 } &&
                s.E<RecoveryAppliedEvent>().Single() is { SourceSeat: 0, TargetSeat: 1, Amount: 3, RemainingHp: 4 } &&
                s.V(1) is { Hp: 4, Armor: 0 } && s.V().Hp == 2 && s.E<ShanheS5YimieCompletedEvent>().Length == 0,
                "The one real damage segment pays armor and HP before the frozen three-point native recovery; its genuine recovery child suspends the exact original attack.");
            s.Cold(); s.Continue(); s.Play();
            Need(s.E<ShanheS5YimieCompletedEvent>().Single() is { X: 3, ActualDamage: true, RecoveryRequested: true } &&
                s.E<ShanheS5YimieIncreasedEvent>().Single() is { OriginalDamage: 1, X: 3, IncreasedDamage: 4 } &&
                s.E<ShanheS5YimieRecoveryRequestedEvent>().Single().Amount == 3 &&
                s.E<ProgramSkillHpLostEvent>().Count(e => e.SkillId == StandardShanheS5XiliangDarkPackage.YimieId) == 1 &&
                !s.G.ResolutionStack.Any(f => f.ShanheS5YimieDamage is not null),
                "Both native CardUse and cardless Program attacks return once after the paid recovery, without duplicating the original HP cost.");
            s.DriverUse("damage-one"); s.Play();
            Need(s.V(1).Hp == 3 && s.V().Hp == 2 && s.E<ShanheS5YimieFrozenEvent>().Length == 1 && s.E<RecoveryAppliedEvent>().Length == 1,
                "A second actual damage that turn does not reopen Yimie or repeat its recovery obligation.");
            s.Cold();
        }

        var chain = Scene.Start(); chain.Play(); chain.DriverUse("armor-one", 2); chain.Play();
        foreach (var seat in new[] { 1, 2, 3 }) { chain.DriverUse("chain", seat); chain.Play(); }
        chain.DriverUse("fire"); chain.Offer(1); chain.Invoke(false); chain.Offer(2); chain.Invoke();
        chain.Until(() => chain.P?.SkillPrompt?.SkillId == Watch); chain.Cold(); chain.Continue();
        chain.Until(() => chain.P?.SkillPrompt?.SkillId == Watch);
        Need(chain.E<ShanheS5YimieFrozenEvent>().Single().Origin is { TargetSeat: 2, OriginalDamage: 1, OriginalTargetHp: 4, X: 3 } &&
            chain.E<RecipientScopedDamageBaseEvent>().Single() is { FirstTargetSeat: 1, Amount: 1 } &&
            chain.E<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual([(1, 1), (2, 4)]) && chain.V(3).Hp == 4,
            "After declining at the origin, accepting on the second actual chain recipient freezes only that segment's X and pauses its recovery before the third segment.");
        chain.Cold(); chain.Continue(); chain.Play();
        Need(chain.E<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount, e.Nature)).SequenceEqual(
                [(1, 1, DamageNature.Fire), (2, 4, DamageNature.Fire), (3, 1, DamageNature.Fire)]) &&
            chain.V(1).Hp == 3 && chain.V(2).Hp == 4 && chain.V(3).Hp == 3 && chain.V().Hp == 2 &&
            chain.E<ShanheS5YimieFrozenEvent>().Length == 1 && chain.E<ShanheS5YimieRecoveryRequestedEvent>().Single() is { TargetSeat: 2, Amount: 3 },
            "The following real chain recipient receives the original one-point base, while the second recipient alone gains its frozen three-point recovery and the source pays once.");
        chain.Cold();
    }

    public static void PreventionAndNonPositiveXKeepPaidCostWithoutNegativeDamageOrRecovery()
    {
        var s = Scene.Start(prevent: true); s.Play(); s.DriverUse("damage-one"); s.Offer(); s.Invoke();
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch); s.Cold(); s.Continue(); s.Play();
        Need(s.V().Hp == 2 && s.V(1).Hp == 4 && s.E<ShanheS5YimieFrozenEvent>().Single().Origin.X == 3 &&
            s.E<ProgramDamagePreventedEvent>().Single().SkillId == Shield && !s.E<DamageAppliedEvent>().Any() &&
            !s.E<RecoveryAppliedEvent>().Any() && !s.E<ShanheS5YimieRecoveryRequestedEvent>().Any() &&
            s.E<ShanheS5YimieCompletedEvent>().Single() is { ActualDamage: false, RecoveryRequested: false },
            "A later native prevention preserves the already paid cost and frozen X, while an unapplied segment creates no recovery.");
        s.Cold();

        s = Scene.Start(); s.Play(); s.DriverUse("armor-two"); s.Play(); s.DriverUse("damage-five"); s.Offer(); s.Invoke();
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch); s.Cold(); s.Continue(); s.Play();
        Need(s.E<ShanheS5YimieFrozenEvent>().Single().Origin is { OriginalTargetHp: 4, OriginalDamage: 5, X: 0 } &&
            s.E<ShanheS5YimieIncreasedEvent>().Single() is { OriginalDamage: 5, X: 0, IncreasedDamage: 5 } &&
            s.E<DamageAppliedEvent>().Single() is { Amount: 5, RemainingHp: 1 } && s.V(1) is { Hp: 1, Armor: 0 } && s.V().Hp == 2 &&
            !s.E<RecoveryAppliedEvent>().Any() && !s.E<ShanheS5YimieRecoveryRequestedEvent>().Any() &&
            s.E<ShanheS5YimieCompletedEvent>().Single() is { X: 0, ActualDamage: true, RecoveryRequested: false },
            "The explicitly permitted zero-X activation pays one HP but keeps the original five damage, without a negative increase or negative recovery.");
        s.Cold();
    }

    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, G.PlayerCount).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand c) { var r = G.Submit(c); Need(r.Accepted, r.Error?.ToString() ?? "The actual Yimie command failed."); }
        public void Answer(Func<PromptChoice, bool> pick) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, G.Revision)); }
        public void Invoke(bool yes = true) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == (yes ? "activate" : "skip"));
        public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        public void Offer(int? target = null) => Until(() => P is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } p &&
            p.SkillPrompt?.SkillId == StandardShanheS5XiliangDarkPackage.YimieId &&
            (target is null || G.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().LastOrDefault()?.TargetSeat == target));
        public void Play() => Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void DriverUse(string id, int target = 1) => Accept(new UseProgramSkillCommand(0, Driver, id, [], [target], G.Revision, P!.PromptId));
        public void Slash()
        {
            var a = G.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([1]));
            Accept(new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind));
        }
        public void Until(Func<bool> done)
        {
            for (var n = 0; n < 120; n++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small Yimie fixture must preserve its combatants.");
                if (P?.PlayerSeat == 0) throw new InvalidOperationException("Unexpected Yimie native boundary: " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded Yimie native continuation was not reached.");
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
            Need(before == State(), "Cold replay preserves the exact frozen X, native cost, original damage and once-requested recovery child.");
        }
        public static Scene Start(bool prevent = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardShanheS5XiliangDarkPackage(), new Fixture(prevent));
            var setup = new ShanheBattleSetup(81, 1, "fixture-s5-yimie", StandardShanheS5XiliangDarkPackage.ChenweiId,
                3, 0, 0, 0, 0, [Driver, Watch], [new(CardKind.Slash, Suit.Club, 7)], [],
                [new(Peer, Hp: 4) { StartingHand = [] }, new(Peer, Hp: 4) { StartingHand = [] }, new(Peer, Hp: 4) { StartingHand = [] }],
                [], new string('c', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture(bool prevent) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-yimie", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"damage-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"damage-five","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":5}]},
                {"id":"armor-one","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"selectedTarget","amount":1}]},
                {"id":"armor-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"grantArmor","target":"selectedTarget","amount":2}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[
                {"id":"paid-loss","window":"afterHpLost","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"loss","options":[{"id":"continue"}]}]},
                {"id":"paid-recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovery","options":[{"id":"continue"}]}]}]},
              {"id":"{{{Shield}}}","revision":1,"triggers":[{"id":"later-prevention","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"priority":-100,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实伤害与护甲", description = "仅用正常原生能力发起伤害与获得护甲" },
                    [Quiet] = new { name = "静默同桌", description = "跳过自主出牌" },
                    [Shield] = new { name = "后续伤害防止", description = "原伤害窗口里的真实后续防止" },
                    [Watch] = new { name = "真实健康孩子", description = "暂停已经实际支付的失去体力或回复", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "真实伤害目标", "supporter", Quiet, "qun", 4, prevent ? [Watch, Shield] : [Watch]));
            b.AddDeck(new(Deck, "固定原生库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "夷灭原生流程", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5XiliangDarkPackage.QinyiId, StandardShanheS5XiliangDarkPackage.ChenweiId, StandardShanheS5XiliangDarkPackage.LiheiId, Peer],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
