using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheZhanhunDaoChecks
{
    private const string Weapon = StandardShanheS5GuosiPackage.ZhanhunDaoSkillId;
    private const string Driver = "fixture:zhanhun-driver", Actor = "fixture:zhanhun-actor", Peer = "fixture:zhanhun-peer";
    private const string Quiet = "fixture:zhanhun-quiet", Watch = "fixture:zhanhun-health", Deck = "fixture:zhanhun-deck";

    public static void RealEquippedWeaponPreventsNativeAndCardlessDamageWithColdHealthChild()
    {
        CheckFixedTriggerShape();
        var s = Scene.Start(); s.Play();
        var equipment = s.V().Hand.Single(c => c.Kind == CardKind.ZhanhunDao).Id;
        s.Card(CardKind.ZhanhunDao, []); s.Play();
        Need(s.V().Equipment.Single(c => c.Id == equipment).Kind == CardKind.ZhanhunDao,
            "The supplemental physical weapon must first be played from Hand into its real equipment slot.");
        s.Use("damage", [1]);
        Need(s.V(1).Hp == 7 && s.E<ShanheZhanhunDaoResolvedEvent>().Length == 0,
            "Damage to a full-HP target resolves normally without a Zhanhun Dao offer.");

        s.Card(CardKind.Slash, [1]); s.Offer();
        var use = s.E<CardUseDeclaredEvent>().Last(e => e.CardKind == CardKind.Slash).ResolutionId;
        var before = s.E<DamageAppliedEvent>().Length;
        s.Cold(); s.Invoke(true);
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var weapon = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Weapon);
        Need(s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f => f.ResumeFrameId == weapon.Id) &&
            s.V(1) is { Hp: 7, MaxHp: 7 } && s.E<ShanheZhanhunDaoResolvedEvent>().Single() is
                { SourceSeat: 0, TargetSeat: 1, PreventedAmount: 1, MaximumHpBefore: 8, MaximumHpAfter: 7 },
            "Preventing the real Slash owns an actual maximum-HP health child on the original weapon program.");
        s.Cold(); s.Play();
        Need(s.E<DamageAppliedEvent>().Length == before && s.E<CardUseFinishedEvent>().Any(e => e.ResolutionId == use) &&
            s.E<ProgramDamagePreventedEvent>().Count(e => e.SkillId == Weapon) == 1 &&
            s.G.CardMovements.Any(m => m.CardId == equipment && m.To == CardLocation.Equipment(0)),
            "The original native Slash completes once without applying its prevented damage or moving the equipped weapon.");

        s.Use("damage", [1]); // Full HP again after the maximum change: make a new real wound.
        s.BeginUse("damage", [1]); s.Offer(); s.Cold(); s.Invoke(true); s.Play();
        Need(s.V(1) is { Hp: 6, MaxHp: 6 } && s.E<ShanheZhanhunDaoResolvedEvent>().Length == 2 &&
            s.E<ShanheZhanhunDaoResolvedEvent>().Last().DamageOwnerFrameId != use,
            "The same equipped weapon also prevents actual cardless program damage without a card-use-only filter.");
        s.Use("damage", [1]);
        s.BeginUse("damage", [1]); s.Offer(); s.Invoke(false); s.Play();
        Need(s.V(1) is { Hp: 4, MaxHp: 6 } && s.E<ShanheZhanhunDaoResolvedEvent>().Length == 2,
            "Declining the optional weapon ability leaves the original damage unchanged.");
        s.Use("unequip"); s.Use("damage", [1]);
        Need(s.V().Equipment.All(c => c.Id != equipment) && s.V(1) is { Hp: 3, MaxHp: 6 } &&
            s.E<ShanheZhanhunDaoResolvedEvent>().Length == 2,
            "Removing the exact physical weapon revokes its granted skill, so a later wound receives ordinary damage.");
        s.Cold();

        s = Scene.Start(); s.Play(); s.Card(CardKind.ZhanhunDao, []); s.Play();
        s.Use("damage", [2]); s.Use("chain", [1]); s.Use("chain", [2]);
        s.BeginUse("fire", [1]); s.Offer();
        Need(s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Driver).AttackAttempt is
                { IsChainPropagation: true, TargetSeat: 2, RangePreventionVisitedTargets: [2] } &&
            s.E<DamageAppliedEvent>().Last() is { TargetSeat: 1, Nature: DamageNature.Fire, Amount: 1 },
            "The full-HP origin receives real fire damage before the wounded chained recipient enters its one actual propagation window.");
        s.Cold(); s.Invoke(true); s.Play();
        Need(s.V(1) is { Hp: 7, MaxHp: 8 } && s.V(2) is { Hp: 7, MaxHp: 7 } &&
            s.E<ShanheZhanhunDaoResolvedEvent>().Single() is { TargetSeat: 2, MaximumHpAfter: 7 } &&
            s.E<DamageAppliedEvent>().Count(e => e.TargetSeat == 2) == 1 &&
            s.E<ProgramDamagePreventedEvent>().Count(e => e.SkillId == Weapon && e.TargetSeat == 2) == 1,
            "The chain-specific scan preserves the source's weapon and prevents only the actual recipient's segment, once, without replaying origin damage.");
        s.Cold();
    }

    public static void ActualSelfSourceQualifiesButSourceLessDamageDoesNot()
    {
        var s = Scene.Start(); s.Play(); s.Card(CardKind.ZhanhunDao, []); s.Play();
        s.Use("source-less");
        Need(s.V() is { Hp: 5, MaxHp: 8 } && s.E<DamageAppliedEvent>().Last() is { TargetSeat: 0, SourceLess: true } &&
            s.E<ShanheZhanhunDaoResolvedEvent>().Length == 0,
            "A source-less attack cannot use an occupied placeholder source's weapon.");
        s.BeginUse("self"); s.Offer(); s.Cold(); s.Invoke(true); s.Play();
        Need(s.V() is { Hp: 5, MaxHp: 7 } && s.E<ShanheZhanhunDaoResolvedEvent>().Single() is { SourceSeat: 0, TargetSeat: 0 } &&
            s.E<DamageAppliedEvent>().Length == 1,
            "A genuine wounded self recipient is still a character damaged by the actual source, so prevention does not add an unprinted other-player restriction.");
        s.Cold();
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static void CheckFixedTriggerShape()
    {
        var rules = $$$"""
        {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{Weapon}}}","revision":1,"triggers":[
          {"id":"fixed","window":"beforeDamageApplied","subject":"any","optional":true,"effects":[{"op":"resolveShanheZhanhunDao","target":"owner"}]}]}]}
        """;
        var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { [Weapon] = new { name = "斩魂刀", description = "固定的真实防止伤害合同" } } });
        foreach (var invalid in new[]
        {
            rules.Replace("\"subject\":\"any\"", "\"subject\":\"damageTarget\"", StringComparison.Ordinal),
            rules.Replace("\"window\":\"beforeDamageApplied\"", "\"window\":\"afterDamageApplied\"", StringComparison.Ordinal),
            rules.Replace("\"effects\":[", "\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1},", StringComparison.Ordinal)
        })
        {
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(invalid, presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The loader rejects a redirected participant, wrong window or extra instruction on the fixed weapon operation.");
        }
    }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, G.PlayerCount).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        private void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "Rejected native Zhanhun Dao command."); }
        public void Play() => Until(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        public void Offer() => Until(() => P is { Kind: DecisionKind.ProgramTrigger, PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Weapon);
        public void Invoke(bool yes)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == (yes ? "activate" : "skip")).Id, G.Revision)); }
        public void BeginUse(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null) { BeginUse(id, targets); Play(); }
        public void Card(CardKind kind, int[] targets)
        {
            var card = V().Hand.First(c => c.Kind == kind);
            var action = G.GetHumanLegalActions().Single(a => a.CardId == card.Id && a.ConversionSource is null && a.TargetSeats.SequenceEqual(targets));
            Accept(new PlayCardCommand(0, card.Id, targets, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId));
        }
        public void Until(Func<bool> done)
        {
            for (var step = 0; step < 120; step++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The bounded weapon fixture must retain its real combatants.");
                Need(P?.PlayerSeat != 0, "Unexpected Zhanhun Dao human boundary: " + JsonSerializer.Serialize(P));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded native weapon continuation was not reached.");
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, G.PlayerCount).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves the exact equipment entity, original damage window and once-only native maximum-HP child.");
        }
        public static Scene Start()
        {
            var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages: [new StandardShanheS5GuosiPackage(), new Fixture()]);
            var setup = new ShanheBattleSetup(61, 1, "fixture-zhanhun-dao", Actor, 6, 4, 0, 0, 0, [],
                [new(CardKind.ZhanhunDao, Suit.Spade, 2), new(CardKind.Slash, Suit.Heart, 7)], [],
                [new(Peer, Hp: 8, MaxHpBonus: 4) { StartingHand = [] }, new(Peer, Hp: 8, MaxHpBonus: 4) { StartingHand = [] },
                 new(Peer, Hp: 8, MaxHpBonus: 4) { StartingHand = [] }], [], new string('d', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3
            }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zhanhun-dao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"sourceRef":{"kind":"owner"}}]},
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire","sourceRef":{"kind":"owner"}}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"self","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"sourceRef":{"kind":"owner"}}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"unequip","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["equipment"]}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"maximum-child","window":"afterHealthChanged","subject":"owner","condition":{"kind":"healthChangeKindIs","hpChangeKind":"maximumHp"},"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"maximum","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实伤害与卸装", description = "沿用原生伤害与实体装备移动" },
                    [Quiet] = new { name = "静默同桌", description = "只跳过自主出牌" },
                    [Watch] = new { name = "真实上限孩子", description = "暂停真正上限变化窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Actor, "实际武器使用者", "supporter", Driver, "qun", 4));
            b.AddGeneral(new(Peer, "真实受伤目标", "supporter", Quiet, "qun", 4, [Watch]));
            b.AddDeck(new(Deck, "固定原生库存", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 24).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "斩魂刀真实伤害", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Actor, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
