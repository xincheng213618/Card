using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MobileShenZhangLiaoChecks
{
    private const string Hero = "mobile:shen-zhang-liao", Driver = "fixture:mobile-szl-driver", Probe = "fixture:mobile-szl-probe",
        Quiet = "fixture:mobile-szl-quiet", Prevent = "fixture:mobile-szl-prevent", Ally = "fixture:mobile-szl-ally", Enemy = "fixture:mobile-szl-enemy", Deck = "fixture:mobile-szl-deck";

    public static void DuoruiPaysRealSlotAndBorrowsUntilTargetTurnEndsCold()
    {
        var s = Scene.Start(); s.Play(); var weapon = s.V(0).Hand.Single(c => c.Kind == CardKind.Crossbow).Id;
        s.Accept(new PlayCardCommand(0, weapon, [], s.G.Revision, s.P!.PromptId)); s.Play();
        s.Use("damage", [2]); s.Reach(() => s.P?.SkillPrompt?.SkillId == "mobile:duorui");
        s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        s.Need(s.P!.Choices.Count == 4 && s.P.Choices.All(c => c.Parameters.GetValueOrDefault("value") != "Treasure"), "Duorui's payment offers exactly the four printed slot kinds, including currently empty usable slots.");
        s.Answer(c => c.Parameters.GetValueOrDefault("value") == "Weapon");
        s.Reach(() => s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MobileShenZhangLiao?.Stage == MobileShenZhangLiaoStage.ChooseSkill));
        s.Need(s.Capacity(EquipmentSlot.Weapon) == 0 && s.G.CardMovements.Count(m => m.CardId == weapon && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1,
            "Abolishing the real weapon slot moves its equipped entity once before choosing the borrowed skill.");
        var ownerLimit = s.Limit(0); var targetLimit = s.Limit(2); s.Cold();
        s.Answer(c => c.Parameters.GetValueOrDefault("value") == Probe); s.Play();
        s.Need(s.Limit(0) == ownerLimit + 2 && s.Limit(2) == targetLimit - 2 && s.E<MobileDuoruiIssuedEvent>() is [{ SelectedSkillId: Probe, TargetSeat: 2, PaidSlot: EquipmentSlot.Weapon }],
            "The exact printed target skill is genuinely granted to the borrower and suppressed at its original target.");
        s.Use("damage", [3]); s.Play(); s.Need(s.E<MobileDuoruiIssuedEvent>().Length == 1, "The borrower cannot invoke Duorui again while this skill lease is active.");
        s.End(); s.Reach(() => s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 2));
        s.Need(s.E<MobileDuoruiExpiredEvent>() is [{ TargetSeat: 2, Died: false }] && s.Limit(0) == ownerLimit && s.Limit(2) == targetLimit &&
            s.Capacity(EquipmentSlot.Weapon) == 0, "At the target's next real turn end only this borrowed grant and suppression expire; the paid slot remains abolished.");
        s.Replay();
    }

    public static void ZhitiRestoresAtTruePindianDuelAndDamageBoundaries()
    {
        var s = Scene.Start(contests: true); s.Play(); s.Use("wound", [2]); s.Play();
        s.Use("abolish-weapon", []); s.Play(); s.Use("contest", [2]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("action") == "pindian-source-card") == true);
        var king = s.V(0).Hand.Single(c => c.Rank == 13).Id; s.Answer(c => c.Cards.SequenceEqual([king]));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("value") == "Weapon") == true);
        s.Need(s.G.ResolutionStack.OfType<PindianFrame>().Single().Result is { SourceWon: true } && s.Capacity(EquipmentSlot.Weapon) == 0,
            "The actual revealed winning Pindian retains its original host while Zhiti chooses a disabled slot before claims.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("value") == "Weapon"); s.Play();
        s.Need(s.Capacity(EquipmentSlot.Weapon) == 1 && s.E<MobileZhitiSlotRestoredEvent>().Length == 1, "The true Pindian result restores its exact slot and returns once to the original contest.");
        s.Use("abolish-armor", []); s.Play(); var hp = s.V(2).Hp; var damage = s.E<DamageAppliedEvent>().Length;
        var duel = s.V(0).Hand.Single(c => c.Kind == CardKind.Duel).Id;
        s.Accept(new PlayCardCommand(0, duel, [2], s.G.Revision, s.P!.PromptId));
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("value") == "Armor") == true);
        s.Need(s.E<DuelResponseEvent>().Any(e => !e.UsedSlash && e.ResponderSeat == 2) && s.V(2).Hp == hp && s.E<DamageAppliedEvent>().Length == damage,
            "Winning Duel is frozen at the genuine failed Slash response, before damage or its prevention can change eligibility.");
        s.Answer(c => c.Parameters.GetValueOrDefault("value") == "Armor"); s.Play();
        s.Need(s.Capacity(EquipmentSlot.Armor) == 1 && s.V(2).Hp == hp && s.E<DamageAppliedEvent>().Length == damage,
            "A prevented Duel damage does not erase the actual Duel win or repeat the failed response.");
        s.Use("abolish-treasure", []); s.Play(); var ownHp = s.V(0).Hp; s.Use("incoming", [2]);
        s.Reach(() => s.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("value") == "Treasure") == true);
        s.Answer(c => c.Parameters.GetValueOrDefault("value") == "Treasure"); s.Play();
        s.Need(s.Capacity(EquipmentSlot.Treasure) == 1 && s.V(0).Hp == ownHp - 1 && s.E<MobileZhitiSlotRestoredEvent>().Length == 3,
            "Real damage from a wounded source in range also restores a slot; unlike Duorui's payment, restoration includes Treasure.");
        s.Replay();
    }

    private sealed class Fixture(bool contests) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-shen-zhang-liao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":20,"priority":0},{"id":"range","query":"attackRange","operation":"add","value":10,"priority":0}],"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
                {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"contest","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":null,"effects":[{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"}]},
                {"id":"abolish-weapon","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"abolishEquipmentSlotGroup","target":"owner","equipmentSlots":["weapon"]}]},
                {"id":"abolish-armor","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"abolishEquipmentSlotGroup","target":"owner","equipmentSlots":["armor"]}]},
                {"id":"abolish-treasure","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"abolishEquipmentSlotGroup","target":"owner","equipmentSlots":["treasure"]}]}]},
              {"id":"{{Probe}}","revision":1,"modifiers":[{"id":"visible-borrow","query":"handLimit","operation":"add","value":2,"priority":0}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Prevent}}","revision":1,"triggers":[{"id":"prevent","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]}]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Probe, Quiet, Prevent }.ToDictionary(id => id, id => new { name = id, description = "移动神张辽真实规则小局" }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = p, ProgramPresentation = catalog.Presentations[id], Tags = id is Quiet or Prevent ? SkillTag.Locked : SkillTag.None });
            b.AddGeneral(new(Ally, "安静队友", "", Quiet, "wei", 8, Gender: GeneralGender.Male));
            b.AddGeneral(new(Enemy, "真实被借技者", "", Probe, "wei", 8, contests ? [Quiet, Prevent] : [Quiet], GeneralGender.Female));
            b.AddDeck(new(Deck, "固定无杀实体牌堆", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 2)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动神张辽小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Hero, Ally, Enemy], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Capacity(EquipmentSlot slot) => V(0).EquipmentSlotCapacities?.GetValueOrDefault(slot, 1) ?? 1;
        public int Limit(int seat) => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G,
            [((System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!)[seat]])!;
        public void Use(string activation, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], targets, G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Answer(Func<PromptChoice, bool> predicate) { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 180; i++)
            {
                if (done()) return;
                Need(G.State.Winner == Winner.None, "The small fixture must remain alive until its actual boundary.");
                if (P is not { PlayerSeat: 0 }) Accept(new AdvanceOneStepCommand(G.Revision));
                else if (P.Kind == DecisionKind.PlayCard) End();
                else if (P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else throw new InvalidOperationException("Unexpected Mobile Shen Zhang Liao boundary " + JsonSerializer.Serialize(P));
            }
            throw new InvalidOperationException("Mobile Shen Zhang Liao occurrence was not reached " + JsonSerializer.Serialize(P));
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray(), Frames = G.ResolutionStack,
            Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Moves = G.CardMovements });
        public void Cold() { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(before == State(), "Cold restoration preserves the original paid slot, borrowed skill menu and genuine contest parent."); }
        public void Replay() { var before = State(); G = GameReplay.Replay(G.CreateCheckpoint().Options, G.AcceptedCommands, registry); Need(before == State(), "Accepted-command replay preserves exact slot payments, borrowing expiry and native result returns."); }
        public void Accept(GameCommand c) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected command."); }
        public void Need(bool ok, string text) { if (!ok) throw new InvalidOperationException(text + JsonSerializer.Serialize(new { G.State.CurrentSeat, G.State.TurnNumber, P, Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool contests = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(contests));
            var setup = new ShanheBattleSetup(37, 1, "mobile-szl", Hero, 4, 0, 0, 0, 0, [Driver],
                [new(CardKind.Crossbow, Suit.Club, 1), new(CardKind.Dodge, Suit.Spade, 13), new(CardKind.Duel, Suit.Spade, 1)], [],
                [new(Enemy) { StartingHand = [new(CardKind.Dodge, Suit.Club, 2)] }, new(Ally) { StartingHand = [] }],
                [new(Ally) { StartingHand = [] }], new string('c', 64)) { DefaultGodFactionId = "wei" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
