using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;
namespace CardGame.Core.Tests;
internal static class MobileShenMaChaoChecks
{
    private const string Hero = "mobile:shen-ma-chao", Tingwei = "mobile-god:tingwei", Jimie = "mobile-god:jimie",
        Peer = "fixture:mobile-mc-peer", ActivePeer = "fixture:mobile-mc-active-peer", NoDraw = "fixture:mobile-mc-no-draw", Quiet = "fixture:mobile-mc-quiet",
        Hold = "fixture:mobile-mc-hold", Driver = "fixture:mobile-mc-driver", Cast = "fixture:mobile-mc-cast",
        Probe = "fixture:mobile-mc-probe", Deck = "fixture:mobile-mc-deck";
    public static void RealNatureMarkerJimieAndBothYuliRechargeCold()
    {
        var s = Scene.Start(); s.Play();
        s.Slash(CardKind.Slash, 2); s.Slash(CardKind.ThunderSlash, 2);
        var hits = s.E<DamageAppliedEvent>().Where(e => e.SourceSeat == 0 && e.TargetSeat == 2).ToArray();
        s.Need(hits.Select(e => e.Amount).SequenceEqual([1, 2]) && hits.All(e => e.Nature == DamageNature.Thunder) && s.Ting(0) == 8 &&
            s.E<MobileTingweiSelectedEvent>().Count(e => e.Mask == 0 && e.Paid == 0) == 2 &&
            s.E<ProgramChainedStateSetEvent>().Count(e => e.SkillId == Tingwei && e.TargetSeat == 2 && e.IsChained) == 2,
            "Real normal Slash converts without an extra point; already-thunder Slash gains exactly one, and declining every Tingwei option leaves four real Ting and enters chain each time.");
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Jimie); s.Activate(); s.Choose("target:3"); s.Play();
        var first = s.E<MobileMaChaoJimieUsedEvent>().Single();
        s.Need(first.Paid == 8 && s.Ting(0) == 0 && !s.V(3).IsAlive && s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 3 && e.Amount == 12 && e.Nature == DamageNature.Thunder) &&
            s.E<MobileMaChaoJimieRefreshedEvent>().Length == 0,
            "Jimie pays eight once and causes real maximum-HP damage; prior Yuli history does not precharge its next limited use.");
        s.UseDriver("draw-two"); s.Play(); s.Slash(CardKind.Slash, 2); s.Slash(CardKind.Slash, 2);
        s.Need(s.Ting(0) == 8, "Two more real accepted Slashes replenish the actual eight Ting.");
        var beforeEnd = s.G.State.TurnNumber; s.End(); s.Play();
        s.Need(s.G.State.TurnNumber > beforeEnd && s.E<MobileMaChaoJimieUsedEvent>().Length == 1 && s.Ting(0) == 8,
            "Even with eight payable Ting, Jimie is unavailable until both Yuli parts have occurred after its use.");
        var hp = s.V(0).Hp; var hand = s.V(0).HandCount; s.UseDriver("self-thunder");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MobileShenMaChao?.Stage == MobileMaChaoStage.Drawing));
        var draw = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MobileShenMaChao?.Stage == MobileMaChaoStage.Drawing);
        s.Need(draw.MobileShenMaChao is { DrawCount: 2, Op: SkillProgramEffectOp.ResolveMobileShenMaChaoYuli } && s.V(0).Hp == hp &&
            s.E<MobileMaChaoJimieRefreshedEvent>().Count(e => e.UseFrameId == first.FrameId && e.Owner == 0) == 1,
            "The actual thunder self-hit executes source then prevention, really prevents two and refreshes the spent limit before the native draw child.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        s.Need(s.V(0).Hp == hp && s.V(0).HandCount == hand + 2 && s.Ting(0) == 8 &&
            s.E<MobileMaChaoDrawReturnedEvent>().Count(e => e.FrameId == draw.Id && e.Requested == 2 && e.Actual == 2) == 1 &&
            s.E<MobileMaChaoJimieRefreshedEvent>().Count(e => e.UseFrameId == first.FrameId) == 1,
            "Cold resumes the original prevented-damage producer once without damage, repeated recharge or marker payment.");
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Jimie); s.Activate(); s.Choose("target:2");
        s.Reach(() => s.E<MobileMaChaoJimieUsedEvent>().Length == 2 && !s.V(2).IsAlive);
        s.Need(s.Ting(0) == 0 && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 12) == 1,
            "The refreshed limited skill genuinely pays eight again and returns through its native damage/death result.");
    }
    public static void FrozenTingweiSubsetGiftDamageAndNextTargetTurnExpiryCold()
    {
        var s = Scene.Start(subset: true); s.Play(); s.End();
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.P.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "mask:15"));
        var first = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MobileShenMaChao?.Stage == MobileMaChaoStage.Options);
        s.Need(first.OwnerSeat == 2 && first.MobileShenMaChao!.Target == 0 && s.Ting(2) == 4,
            "A real NPC-owned Slash target window lets the target choose one complete subset after the four Ting are granted.");
        s.Choose("mask:15");
        s.Reach(() => s.P is { PlayerSeat: 0 } && s.G.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.MobileShenMaChao?.Stage == MobileMaChaoStage.Gift));
        var f = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MobileShenMaChao?.Stage == MobileMaChaoStage.Gift);
        var gift = s.V(0).Hand.Single(c => c.Kind == CardKind.QinggangSword).Id;
        s.Need(f.Id == first.Id && f.MobileShenMaChao is { Mask: 15, Cursor: 3 } && s.Ting(2) == 0 &&
            s.E<MobileTingweiSelectedEvent>().Count(e => e.FrameId == f.Id && e.Paid == 4) == 1 &&
            s.Limit(0) == s.V(0).Hp && s.P!.Choices.All(c => c.Cards.SequenceEqual([gift])),
            "The frozen four-option set pays all four Ting once and suppresses nonlocked skills before its real target-owned equipment choice.");
        s.Cold(); s.Answer(c => c.Cards.SequenceEqual([gift]));
        s.Reach(() => s.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 2 && e.TargetSeat == 0));
        s.Need(s.V(0).Hp == 2 && s.V(0).HandCount == 0 && s.Ting(2) == 0 &&
            s.G.CardMovements.Count(m => m.CardId == gift && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(2) && m.Reason.Value == "skill.mobile-shen-ma-chao.tingwei.gift") == 1 &&
            s.G.CardMovements.Count(m => m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill.mobile-shen-ma-chao.tingwei.random-discard") == 1 &&
            s.E<MobileTingweiSelectedEvent>().Count(e => e.FrameId == f.Id) == 1 &&
            s.E<DamageAppliedEvent>().Single(e => e.SourceSeat == 2 && e.TargetSeat == 0).Nature == DamageNature.Thunder &&
            s.E<MobileTingweiExpiredEvent>().Length == 0,
            "Cold never repays the subset: equipment is really given, one remaining legal card is discarded, and only this original Slash target takes the extra point.");
        s.Play();
        s.Need(s.Limit(0) == s.V(0).Hp && s.E<MobileTingweiExpiredEvent>().Length == 0,
            "Suppression persists through the affected role's next actual turn, including its Play phase.");
        var turn = s.G.State.TurnNumber; s.End(); s.Reach(() => s.E<MobileTingweiExpiredEvent>().Any(e => e.FrameId == f.Id));
        s.Need(s.E<TurnEndedEvent>().Any(e => e.ActorSeat == 0 && e.TurnNumber == turn) && s.Limit(0) == s.V(0).Hp + 3 &&
            s.E<MobileTingweiExpiredEvent>().Count(e => e.FrameId == f.Id) == 1,
            "Only the target's next real TurnEnded expires the issued silence and restores qualification of the original nonlocked grant.");
    }
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mobile-shen-ma-chao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Hold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]},
              {"id":"{{Probe}}","revision":1,"modifiers":[{"id":"probe","query":"handLimit","operation":"add","value":3,"priority":0}]},
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"quota","query":"slashLimit","operation":"add","value":9,"priority":0},{"id":"distance","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}],"activations":[
                {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
                {"id":"self-thunder","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1,"nature":"thunder"}]}]},
              {"id":"{{Cast}}","revision":1,"triggers":[{"id":"real-slash","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingHighestHand"},{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","useCardActionWindows":true,"targetRestriction":"distanceUnlimitedAgainstTarget"}]}]}
            ]}
            """;
            var labels = new[] { NoDraw, Quiet, Hold, Probe, Driver, Cast }.ToDictionary(id => id, id => new { name = id, description = "移动神马超原生主流程" });
            var c = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in c.Programs) b.AddSkill(new(id, c.Presentations[id].Name, c.Presentations[id].Description) { Program = p, ProgramPresentation = c.Presentations[id], Tags = id == NoDraw ? SkillTag.Locked : SkillTag.None });
            b.AddGeneral(new(Peer, "真实回合的角色", "", Quiet, "wei", 12));
            b.AddGeneral(new(ActivePeer, "保留出牌阶段的目标", "", NoDraw, "wei", 4));
            b.AddDeck(new(Deck, "固定实体牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "移动神马超主流小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer, ActivePeer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(v => G.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int Ting(int seat) => V(seat).Markers?.SingleOrDefault(m => m.Kind == PlayerMarkerKind.MobileShenMaChaoTing)?.Count ?? 0;
        public int Limit(int seat) => (int)typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(G,
            [((System.Collections.IList)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(G)!)[seat]])!;
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Choose(string option) => Answer(c => c.Parameters.GetValueOrDefault("option") == option);
        public void UseDriver(string activation) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], [], G.Revision, P!.PromptId));
        public void Slash(CardKind kind, int target)
        {
            var id = V(0).Hand.First(c => c.Kind == kind).Id;
            var a = G.GetHumanLegalActions().First(a => a.CardId == id && a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
            Accept(new PlayCardCommand(0, id, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId));
            Reach(() => P?.SkillPrompt?.SkillId == Tingwei); Activate();
            Reach(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "target:" + target) == true); Choose("target:" + target); Play();
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, G.Revision)); }
        public void Reach(Func<bool> ready)
        {
            for (var i = 0; i < 200; i++)
            {
                if (ready()) return;
                if (P is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(c => c.Parameters.GetValueOrDefault("response") == "let-die");
                else if (P is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge }) Answer(c => c.Parameters.GetValueOrDefault("response") == "take-damage");
                else if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Hold) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected mobile Ma Chao boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded real mobile Ma Chao occurrence was not reached " + JsonSerializer.Serialize(P));
        }
        public void Cold()
        {
            var before = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before.SequenceEqual(Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v)))), "The original paid subset or prevented-damage producer survives cold restore.");
        }
        public void Accept(GameCommand command) { var r = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected mobile Ma Chao command."); }
        public void Need(bool ok, string reason) { if (!ok) throw new InvalidOperationException(reason + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat,
            Ting0 = Ting(0), Ting2 = Ting(2), Hp0 = V(0).Hp, Limit0 = Limit(0), P,
            MaChao = G.ResolutionStack.OfType<ProgramSkillFrame>().Where(f => f.MobileShenMaChao is not null).Select(f => new { f.Id, f.MobileShenMaChao }),
            Chain = E<ProgramChainedStateSetEvent>(), Selection = E<MobileTingweiSelectedEvent>(),
            Facts = G.Events.TakeLast(6).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start(bool subset = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var setup = new ShanheBattleSetup(37, 1, "mobile-ma-chao", subset ? ActivePeer : Hero, 4, 0, 0, 0, 0,
                subset ? [Probe] : [NoDraw, Driver, Hold],
                subset ? [new(CardKind.QinggangSword, Suit.Spade, 6), new(CardKind.Slash, Suit.Club, 7)] :
                    [new(CardKind.Slash, Suit.Club, 7), new(CardKind.ThunderSlash, Suit.Spade, 6), new(CardKind.Peach, Suit.Heart, 3)], [],
                [new(subset ? Hero : Peer, ExtraSkillIds: subset ? [NoDraw, Quiet, Cast] : [NoDraw]) { StartingHand = [] },
                 new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], [new(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] }], new string('e', 64)) { DefaultGodFactionId = "qun" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 16 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
