using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class HappyShenDengAiChecks
{
    private const string Hero = "happy:shen-deng-ai", Tuoyu = "happy:tuoyu", Xianjin = "happy:xianjin", Qijing = "happy:qijing", Cuixin = "happy:cuixin",
        Driver = "fixture:happy-da-driver", EquipSlash = "fixture:happy-da-equipment-slash", NoDraw = "fixture:happy-da-no-draw",
        Quiet = "fixture:happy-da-quiet", Hold = "fixture:happy-da-draw-hold", Peer = "fixture:happy-da-peer", Deck = "fixture:happy-da-deck";

    public static void OptionalReallocationDevelopedDrawAndNonHandCuixinResumeCold()
    {
        var s = Scene.Start(); s.Play();
        var assigned = s.V(0).Hand.First(c => c.Kind == CardKind.Slash).Id;
        var weapon = s.V(0).Equipment.Single().Id;
        for (var domain = 1; domain <= 3; domain++)
        {
            s.Use("tap", [2]); s.Play(); s.Use("tap", [2]);
            s.Reach(() => s.P?.SkillPrompt?.SkillId == Xianjin && s.P.Choices.Any(c => Option(c) == "develop:" + domain));
            var before = s.V(0).HandCount;
            s.Answer(c => Option(c) == "develop:" + domain);
            if (domain == 2)
            {
                s.Reach(() => s.P?.SkillPrompt?.SkillId == Hold);
                var f = s.Root!;
                var draw = s.G.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(n => n.Context.ParentFrameId == f.Id);
                s.Need(f.SkillId == Xianjin && f.ShenDengAi is { Stage: ShenDengAiStage.DrawAttempt, DrawCount: 2 } && draw.Context.RequestedCount == 2 &&
                    Enumerable.Range(1, 3).All(i => s.V(i).HandCount < before),
                    "Happy Xianjin requests the actual two developed domains even when the owner has strictly the most hand cards.");
                s.Cold(); s.Skip(); s.Play();
                s.Need(s.V(0).HandCount == before + 2 && s.E<ShenDengAiDrawReturnedEvent>().Count(e => e.FrameId == f.Id && e.RequestedCount == 2 && e.ActualCount == 2) == 1 &&
                    s.E<NativeDrawAttemptDecidedEvent>().Count(e => e.AttemptFrameId == draw.Id && !e.Replaced) == 1,
                    "The once-issued two-card producer returns to the original occurrence after cold without reducing to the anniversary one-card exception.");
            }
            else s.Play();
        }
        s.Need(s.E<ShenDengAiDevelopedEvent>().Select(e => e.Domain).SequenceEqual(Enum.GetValues<ShenDengAiDomain>()) &&
            s.E<ShenDengAiDrawReturnedEvent>().Select(e => e.RequestedCount).SequenceEqual([1, 2, 3]),
            "Six genuine occurrences develop all three domains and draw one, two and three in their original paid order.");
        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Tuoyu); s.Activate();
        s.Answer(c => c.Cards.SequenceEqual([assigned]) && Option(c) == "assign:1:" + assigned);
        s.Answer(c => Option(c) == "skip");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Qijing); s.Answer(c => c.Targets.SequenceEqual([2, 3]));
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Tuoyu); s.Skip(); s.Play();
        s.Need(s.G.State.CurrentSeat == 0 && s.G.CreateSnapshot(0).TableOrder!.SequenceEqual([1, 2, 0, 3]) && s.V(0).MaxHp == 3 &&
            s.V(0).Skills!.Any(k => k.Id == Cuixin) && s.V(0).Hand.Single(c => c.Id == assigned).SkillMarkLabel == "清渠" &&
            s.E<ShenDengAiAssignedEvent>().Length == 1 && s.E<ProgramExtraTurnPendedEvent>().Length == 1,
            "Optional partial allocation leaves other hand cards unassigned; the native awakening moves seats and starts an extra turn whose optional allocation can be refused.");

        var hp2 = s.V(2).Hp; var hp3 = s.V(3).Hp;
        var equipment = s.G.GetHumanLegalActions().First(a => a.CardId == weapon && a.ConversionSource?.SkillId == EquipSlash && a.TargetSeats.SequenceEqual([2]));
        s.Accept(new PlayCardCommand(0, weapon, equipment.TargetSeats, s.G.Revision, s.P!.PromptId, equipment.PlayedCardKind, equipment.TargetCardId) { ConversionSource = equipment.ConversionSource });
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Cuixin);
        var mother = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == s.Root!.ShenDengAi!.OriginalUseId);
        s.Need(mother.Action!.PhysicalCards is [{ From: var from }] && from == CardLocation.Equipment(0) && s.V(2).Hp == hp2 - 1 &&
            s.P!.Choices.Any(c => Option(c) == "slash:3"),
            "Happy Cuixin admits the actual equipment-paid completed Slash, rather than silently retaining the anniversary hand-only restriction.");
        s.Cold(); s.Answer(c => Option(c) == "slash:3"); s.Play();
        s.Need(s.V(3).Hp == hp3 - 1 && s.E<ShenDengAiNativeIssuedEvent>() is [{ Origin.Source.SkillId: Cuixin }] &&
            s.E<ShenDengAiNativeReturnedEvent>().Length == 1 && s.E<ShenDengAiCursorRestoredEvent>().Length == 1 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == mother.Id) == 1,
            "The real opposite-neighbor use returns once after cold and restores the original equipment-paid mother without recursion or copied domain benefits.");

        hp2 = s.V(2).Hp; hp3 = s.V(3).Hp;
        s.Use("virtual", [2]); s.Reach(() => s.P?.SkillPrompt?.SkillId == Cuixin);
        var virtualMother = s.G.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == s.Root!.ShenDengAi!.OriginalUseId);
        s.Need(virtualMother.Action is { Type: CardActionType.Use, PhysicalCards.Count: 0 } && s.V(2).Hp == hp2 - 1,
            "The edition also accepts a genuine zero-material native virtual use, with its actual producer still suspended below the completed card.");
        s.Answer(c => Option(c) == "slash:3"); s.Play();
        s.Need(s.V(3).Hp == hp3 - 1 && s.E<ShenDengAiNativeIssuedEvent>().Length == 2 && s.E<ShenDengAiNativeReturnedEvent>().Length == 2 &&
            s.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == virtualMother.Id) == 1,
            "The original virtual producer and its one opposite-neighbor child both finish exactly once; Cuixin's own repeated uses do not recursively qualify.");

        s.End(); s.Reach(() => s.P?.SkillPrompt?.SkillId == Tuoyu); s.Activate();
        s.Need(s.P!.Choices.Any(c => c.Cards.SequenceEqual([assigned]) && Option(c) == "assign:2:" + assigned), "Happy's later allocation includes the still-held card already assigned to Qingqu.");
        s.Answer(c => c.Cards.SequenceEqual([assigned]) && Option(c) == "assign:2:" + assigned); s.Answer(c => Option(c) == "skip");
        s.Reach(() => s.P?.SkillPrompt?.SkillId == Tuoyu); s.Skip(); s.Play();
        s.Need(s.V(0).Hand.Single(c => c.Id == assigned).SkillMarkLabel == "丰田" &&
            s.E<ShenDengAiAssignedEvent>().Where(e => e.CardId == assigned).Select(e => e.Domain).SequenceEqual([ShenDengAiDomain.Qingqu, ShenDengAiDomain.Fengtian]),
            "A later real Play ending can reassign an existing domain; refusing the next optional start preserves the newly chosen domain.");
    }

    private static string? Option(PromptChoice choice) => choice.Parameters.GetValueOrDefault("option");
    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-happy-deng-ai", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, Hero);
            var labels = new[] { Driver, EquipSlash, NoDraw, Quiet, Hold }.ToDictionary(id => id, id => new { name = id, description = "真实副区域、装备和虚拟用牌与原生子返回" });
            var catalog = SkillProgramCatalog.Load(Rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description) { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Peer, "安静同桌", "", Quiet, "wei", 20));
            b.AddDeck(new(Deck, "固定真实牌", 0, 2, []) { PhysicalCards = Enumerable.Range(0, 80).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "欢乐邓艾实际座次小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Hero, Peer],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
        public static string Rules => $$"""
        {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
          {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep-samples","query":"handLimit","operation":"add","value":20,"priority":0}],"activations":[
            {"id":"tap","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damageParticipants","target":"selectedTarget","amount":1}]},
            {"id":"virtual","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualCard","target":"selectedTarget","outputKind":"slash","useCardActionWindows":true,"targetRestriction":"distanceUnlimitedAgainstTarget"}]}]},
          {"id":"{{EquipSlash}}","revision":1,"viewAs":[{"id":"equipment-slash","inputKinds":["crossbow"],"inputSuits":[],"inputCount":1,"sourceZones":["equipment"],"outputKind":"slash","forPlay":true,"forResponse":false}]},
          {"id":"{{NoDraw}}","revision":1,"triggers":[{"id":"no-draw","isSkillInvocation":false,"window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"adjustNormalDraw","target":"owner","amount":-2}]}]},
          {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","isSkillInvocation":false,"window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
          {"id":"{{Hold}}","revision":1,"triggers":[{"id":"draw","isSkillInvocation":false,"window":"nativeDrawStarting","subject":"owner","optional":true,"effects":[{"op":"setChainedState","target":"owner","chained":true}]}]}
        ]}
        """;
    }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(i => G.CreateSnapshot(i).PendingDecision).FirstOrDefault(p => p is not null);
        public ProgramSkillFrame? Root => G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.ShenDengAi is not null);
        public PlayerSnapshot V(int seat) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Use(string binding, IReadOnlyList<int> targets) => Accept(new UseProgramSkillCommand(0, Driver, binding, [], targets, G.Revision, P!.PromptId));
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void Skip() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        public void Answer(Func<PromptChoice, bool> predicate)
        { var p = P!; var c = p.Choices.FirstOrDefault(predicate); Need(c is not null, "Missing exact Happy Deng Ai choice."); Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, c!.Id, G.Revision)); }
        public void Reach(Func<bool> done)
        {
            for (var n = 0; n < 180; n++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Hold) Skip();
                else if (P is { PlayerSeat: 0 } response && response.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                    Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
                else if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected Happy Deng Ai boundary " + JsonSerializer.Serialize(P));
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            Need(false, "Missed the bounded Happy Deng Ai actual flow.");
        }
        public void Cold()
        {
            var views = Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i))).ToArray();
            var frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(views.SequenceEqual(Enumerable.Range(0, 4).Select(i => SnapshotJson.Serialize(G.CreateSnapshot(i)))) && frames.SequenceEqual(G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
                "Cold retains the original edition, occurrence, exact material source, owning native child and private choice.");
        }
        public void Accept(GameCommand command)
        { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected Happy Deng Ai command."); }
        public void Need(bool ok, string why)
        { if (!ok) throw new InvalidOperationException(why + JsonSerializer.Serialize(new { G.State.TurnNumber, G.State.CurrentSeat, P, Root = Root?.ShenDengAi, Draws = E<ShenDengAiDrawReturnedEvent>(), Facts = G.Events.TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) })); }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
            var peer = new ShanheCombatantDefinition(Peer, ExtraSkillIds: [NoDraw]) { StartingHand = [] };
            var setup = new ShanheBattleSetup(31, 1, "happy-da", Hero, 4, 0, 0, 0, 0, [Driver, EquipSlash, NoDraw, Hold],
                [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Heart, 8), new(CardKind.Peach, Suit.Heart, 3)],
                [new(CardKind.Crossbow, Suit.Club, 1)], [peer, peer], [peer], new string('d', 64)) { DefaultGodFactionId = "wu" };
            var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
            var s = new Scene(g, registry); s.Accept(new StartGameCommand()); return s;
        }
    }
}
