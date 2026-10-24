using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class BoundaryCaoXiuMobileChecks
{
    private const string Hero = "boundary:cao-xiu-mobile", Skill = "boundary:qingxi-mobile", Driver = "fixture:boundary-cao-xiu-driver",
        Quiet = "fixture:boundary-cao-xiu-quiet", SkipPlay = "fixture:boundary-cao-xiu-skip-play", Pulse = "fixture:boundary-cao-xiu-native-pulse",
        Hold = "fixture:boundary-cao-xiu-paid", Peer = "fixture:boundary-cao-xiu-peer";

    public static void DistanceHandPaymentActualTurnQuotaAndNativeDamageCold()
    {
        var s = Scene.Start(); s.Play();
        s.Need(s.G.GetCombatDistance(0, 2) == 2 && s.V().Equipment.Count == 0, "The real starting table has no weapon and two-seat distance.");
        s.Use("wound", 0); s.Play();
        s.Need(s.V().Hp == 3 && s.G.GetCombatDistance(0, 2) == 1, "The reused Qianju reduces outgoing distance by the actual lost HP.");
        s.Use("recover", 0); s.Play();
        s.Need(s.G.GetCombatDistance(0, 2) == 2, "Native recovery removes the lost-HP distance contribution.");
        s.Use("damage", 0); s.Play();
        s.Need(s.Facts<BoundaryCaoXiuQingxiResolvedEvent>().Length == 0, "Self damage does not offer Qingxi or consume its other-role quota.");
        s.Use("recover", 0); s.Play();

        s.Use("damage", 2); s.Offer(); s.Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip"); s.Play();
        var before = s.V(2).Hp;
        s.Use("damage", 2); s.Offer(); s.Activate(); s.TargetChoice();
        var draft = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill).BoundaryCaoXiuQingxi!;
        s.Need(draft is { TargetSeat: 2, Distance: 2, RequiredCount: 2 }, "Non-card native damage uses current directional distance, without requiring a Slash or weapon.");
        var originals = s.V(2).Hand.Take(2).Select(c => c.Id).ToArray();
        s.Answer(c => c.Cards.SequenceEqual([originals[0]])); s.Cold();
        s.Answer(c => c.Cards.SequenceEqual([originals[1]]));
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold);
        s.Need(originals.All(id => s.Zone(id) == CardLocation.DiscardPile) &&
            s.G.CardMovements.Count(m => originals.Contains(m.CardId) && m.Reason.Value == "skill-program.boundary-qingxi.discard") == 2 &&
            s.V(2).Hp == before && s.Facts<BoundaryCaoXiuQingxiResolvedEvent>() is [{ Discarded: true, RequiredCount: 2, DamageBefore: 1, DamageAfter: 1 }],
            "Both original hand entities pay once in one native discard batch before its movement child returns and before damage is applied.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.V(2).Hp == before - 1 && s.Facts<BoundaryCaoXiuQingxiResolvedEvent>().Length == 1,
            "Cold returns to the original one-damage attempt after the paid movement child without repaying the cards.");
        s.Use("damage", 3); s.Play();
        s.Need(s.Facts<BoundaryCaoXiuQingxiResolvedEvent>().Length == 1, "An accepted Qingxi consumes this actual turn's quota across different damage targets.");
        s.End(); s.Play();
        s.Need(s.G.State.TurnNumber == 5, "A later original own turn starts after all three actual intervening turns.");
        var hp = s.V(2).Hp; s.Use("damage", 2); s.Offer(); s.Activate(); s.TargetChoice();
        s.Need(s.P!.Choices.Count == 1 && s.P.Choices[0].Parameters.GetValueOrDefault("branch") == "bonus",
            "Insufficient hand cards cannot make a partial payment; the next actual turn has a fresh invocation quota.");
        s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "bonus"); s.Play();
        s.Need(s.V(2).Hp == hp - 2 && s.Facts<BoundaryCaoXiuQingxiResolvedEvent>().Last() is { Discarded: false, DamageBefore: 1, DamageAfter: 2 },
            "The bonus changes the original native damage to two and keeps the once-per-actual-turn limit.");
    }

    public static void FarDistanceClampsToOneHandCard()
    {
        var s = Scene.Start(far: true); s.Play(); s.Use("damage", 2); s.Offer(); s.Activate(); s.TargetChoice();
        var draft = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Skill).BoundaryCaoXiuQingxi!;
        s.Need(draft.Distance >= 4 && draft.RequiredCount == 1, "Qingxi clamps a nonpositive four-minus-distance cost to one actual hand card.");
        var original = s.V(2).Hand[0].Id; var hp = s.V(2).Hp;
        s.Answer(c => c.Cards.SequenceEqual([original])); s.Answer(c => c.Parameters.GetValueOrDefault("branch") == "discard");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        s.Need(s.Zone(original) == CardLocation.DiscardPile && s.V(2).Hp == hp - 1 &&
            s.Facts<BoundaryCaoXiuQingxiResolvedEvent>() is [{ RequiredCount: 1, Discarded: true }], "The clamped payment really discards exactly one hand entity and retains ordinary damage.");
    }

    internal static (GameEngine Game, ContentRegistry Registry) CreateQingxiChoice(bool humanTarget = false)
    {
        var scene = Scene.Start(humanTarget: humanTarget);
        if (humanTarget)
        {
            // An AI does not publish an artificial human PlayCard decision.
            // Stop its deterministic native pre-Play damage on the real human target instead.
            for (var i = 0; i < 3; i++)
            {
                scene.Reach(() => scene.P?.SkillPrompt?.SkillId == Skill &&
                    scene.P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
                if (scene.G.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Last().TargetSeat == 2)
                {
                    scene.Activate(); scene.TargetChoice();
                    scene.Need(scene.P!.RequiredCardCount >= 2, "The actual source's original directional distance retains a real multi-card target payment.");
                    return (scene.G, scene.Registry);
                }
                scene.Answer(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
            }
            throw new InvalidOperationException("The fixed native Qingxi pulse did not reach human target two.");
        }
        scene.Play(); scene.Use("damage", 2); scene.Offer(); scene.Activate(); scene.TargetChoice();
        return (scene.G, scene.Registry);
    }

    internal sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G = game;
        public ContentRegistry Registry => registry;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
        public T[] Facts<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public CardLocation Zone(int id) => G.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Offer() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.PlayerSeat == 0);
        public void TargetChoice() => Reach(() => P?.SkillPrompt?.SkillId == Skill && P.PlayerSeat == 2);
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Use(string activation, int target) => Accept(new UseProgramSkillCommand(0, Driver, activation, [], [target], G.Revision, P!.PromptId));
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P!; var choice = p.Choices.Single(predicate);
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, choice.Id, G.Revision));
        }
        public void Reach(Func<bool> done)
        {
            for (var i = 0; i < 220; i++)
            {
                if (done()) return;
                if (P is { Kind: DecisionKind.SelectGeneral } selecting)
                { Accept(new SelectGeneralCommand(selecting.PlayerSeat, selecting.PlayerSeat == 0 ? Hero : selecting.Choices.First().ContentIds[0], G.Revision, selecting.PromptId)); continue; }
                if (P is { Kind: DecisionKind.PlayCard, PlayerSeat: not 0 } other)
                { Accept(new EndPlayPhaseCommand(other.PlayerSeat, G.Revision, other.PromptId)); continue; }
                Need(P is null, "The fixed Qingxi flow reached an unexpected native decision.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "The bounded Qingxi flow did not reach its next native boundary.");
        }
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold preserves the original target selection, actual quota and paid native movement child.");
        }
        private string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray(),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), G.CardMovements });
        public void Need(bool ok, string why)
        {
            if (!ok) throw new InvalidOperationException(why + " " + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, P,
                Root = G.ResolutionStack.OfType<ProgramSkillFrame>().Where(f => f.SkillId == Skill).Select(f => f.BoundaryCaoXiuQingxi),
                Last = G.Events.TakeLast(8).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) }));
        }
        private void Accept(GameCommand command)
        { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Rejected Qingxi native command."); }
        public static Scene Start(bool far = false, bool humanTarget = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(far, humanTarget));
            if (humanTarget)
            {
                var native = GameEngine.CreateStandard(new GameOptions { Seed = 37, PlayerCount = 4, HumanSeat = 2,
                    HumanRole = Role.Rebel, ModeId = "identity:classic-boundary-cao-xiu", UseInteractiveSetup = true, UseInteractiveDiscard = false,
                    AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
                var targetScene = new Scene(native, registry); targetScene.Accept(new StartGameCommand()); return targetScene;
            }
            ShanheStartingCard[] hand = [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Peach, Suit.Heart, 3)];
            var setup = new ShanheBattleSetup(37, 1, "boundary-cao-xiu", Hero, 4, 0, 0, 0, 0, [Driver, Quiet], [], [],
                [new(Peer, Hp: 8) { StartingHand = hand }, new(Peer, Hp: 8) { StartingHand = hand }],
                [new(Peer, Hp: 8) { StartingHand = hand }], new string('c', 64));
            var engine = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            var scene = new Scene(engine, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture(bool far, bool humanTarget) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-cao-xiu-mobile", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(humanTarget ? new OwnerBuilder(builder) : builder, Hero, "classic:cao-xiu");
            var modifiers = far ? "\"modifiers\":[{\"id\":\"far-distance\",\"query\":\"outgoingDistance\",\"operation\":\"add\",\"value\":3,\"priority\":0}]," : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
             {"id":"{{Driver}}","revision":1,{{modifiers}}"activations":[
              {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
              {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]},
              {"id":"recover","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"selectedTarget","amount":1}]}]},
             {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"no-normal-draw","window":"drawPhaseStarting","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
             {"id":"{{SkipPlay}}","revision":1,"triggers":[{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
             {"id":"{{Pulse}}","revision":1,"triggers":[{"id":"one-native-pulse","window":"afterNormalDraw","subject":"owner","optional":false,"isSkillInvocation":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"damageOtherLiving","target":"owner","amount":1}]}]},
             {"id":"{{Hold}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementReasons":["skill-program.boundary-qingxi.discard"],"movementOccurrence":"perBatch","optional":false,"isSkillInvocation":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"held","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var names = new Dictionary<string, object>
            {
                [Driver] = new { name = "界曹休实际伤害驱动", description = "无武器的原生技能伤害、体力变化及距离输入。" },
                [Quiet] = new { name = "界曹休固定摸牌", description = "保留实际回合，仅移除普通摸牌。" },
                [SkipPlay] = new { name = "界曹休安静同桌", description = "同桌保留真实回合而跳过主动出牌。" },
                [Pulse] = new { name = "界曹休真实伤害触发", description = "在AI主动出牌前发起一次真实逐目标伤害。" },
                [Hold] = new { name = "界曹休已付子窗", description = "整批弃牌后的原生观察者。", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "固定原生目标", "", Quiet, "wei", 8, [Hold, SkipPlay]));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new(Peer + seat, "固定原生目标" + seat, "", Quiet, "wei", 8, [Hold, SkipPlay]));
            const string deck = "fixture:boundary-cao-xiu-deck";
            builder.AddDeck(new(deck, "固定不摸牌库存", humanTarget ? 3 : 0, 0, []) { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Club, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "界曹休实际伤害场景", 4, 4, new Dictionary<string, int>(), deck, GeneralPoolIds: [Hero, Peer], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
            builder.AddMode(new("identity:classic-boundary-cao-xiu", "界曹休实际多人选择", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Hero, Peer + 1, Peer + 2, Peer + 3])
                { GeneralSelectionWeights = new Dictionary<string, int> { [Hero] = 1000 } });
        }
    }

    private sealed class OwnerBuilder(IContentRegistryBuilder builder) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition definition) => builder.AddGeneral(definition.Id == Hero
            ? definition with { AdditionalSkillIds = [.. definition.AdditionalSkillIds ?? [], Quiet, Pulse] } : definition);
        public void AddSkill(ContentSkillDefinition definition) => builder.AddSkill(definition);
        public void AddCard(ContentCardDefinition definition) => builder.AddCard(definition);
        public void AddDeck(ContentDeckRecipe definition) => builder.AddDeck(definition);
        public void AddMode(ContentModeDefinition definition) => builder.AddMode(definition);
    }
}
