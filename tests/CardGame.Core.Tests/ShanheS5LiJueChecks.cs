using System.Text.Json;
using CardGame.Core;
using CardGame.Core.Campaigns;
using CardGame.Content.Standard;

internal static class ShanheS5LiJueChecks
{
    private const string Langxi = StandardShanheS5LiJuePackage.LangxiId;
    private const string Peer = "fixture:shanhe-li-jue-peer";
    private const string Quiet = "fixture:shanhe-li-jue-quiet";
    private const string Watch = "fixture:shanhe-li-jue-watch";
    private const string Target = "fixture:shanhe-li-jue-target";
    private const string Deck = "fixture:shanhe-li-jue-deck";

    public static void EveryActualPreparePositiveRandomDamageAndPaidChildCold()
    {
        var s = Scene.Start(); s.Offer();
        s.Activate(); s.TargetChoice();
        Need(s.P!.Choices.SelectMany(c => c.Targets).Order().SequenceEqual([1, 2]),
            "The real target selector excludes the owner and the living five-HP character, but includes lower and equal HP.");
        var original = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Langxi);
        Need(original.WindowContext is { SourceSeat: 0 } && original.LiJue is { Stage: LiJueStage.ChoosingTarget },
            "The first opportunity belongs to the owner's actual preparation window and unpaid target instruction.");
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([1]));
        s.Until(() => s.P?.SkillPrompt?.SkillId == Watch);
        var paid = s.E<LangxiRandomResolvedEvent>().Single();
        var damage = s.E<DamageAppliedEvent>().Single();
        Need(paid.FrameId == original.Id && paid.Source.OwnerSeat == 0 && paid.TargetSeat == 1 && paid.Amount is 1 or 2 &&
            damage.SourceSeat == 0 && damage.TargetSeat == 1 && damage.Amount == paid.Amount && !damage.SourceLess &&
            damage.SourceCardUseResolutionId is null && s.V(1).Hp == 3 - paid.Amount &&
            s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == original.Id).LiJue is { Stage: LiJueStage.DamageChildren, Roll: var roll } && roll == paid.Amount,
            "Exactly one accepted target samples positive 1..2 and deals real attributed cardless damage before its native damage child.");
        s.Cold(); s.Answer(c => c.Parameters.GetValueOrDefault("option-id") == "continue"); s.Play();
        Need(s.E<LangxiRandomResolvedEvent>().Length == 1 && s.E<DamageAppliedEvent>().Length == 1 &&
            s.E<ProgramBindingResolvedEvent>().Single(e => e.FrameId == original.Id) is { Activated: true, Completed: true },
            "The paid damage child resumes its original frame without another roll or damage payment.");
        s.End(); s.Offer(); s.Activate(); s.TargetChoice();
        var foreign = s.G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Langxi);
        Need(foreign.OwnerSeat == 0 && foreign.WindowContext is { SourceSeat: 1 } &&
            foreign.LiJue is { ActualTurn: 2, Stage: LiJueStage.ChoosingTarget } &&
            !s.P!.Choices.SelectMany(c => c.Targets).Contains(3),
            "The same owner receives a new real optional opportunity in another living character's preparation, with current HP qualification.");
        s.Cold(); s.Answer(c => c.Targets.SequenceEqual([2]));
        s.Until(() => s.E<ProgramBindingResolvedEvent>().Any(e => e.FrameId == foreign.Id && e.Activated && e.Completed));
        var second = s.E<LangxiRandomResolvedEvent>().Single(e => e.FrameId == foreign.Id);
        Need(second.ActualTurn == 2 && second.Source.OwnerSeat == 0 && second.Amount is 1 or 2 &&
            s.V(2).Hp == 4 - second.Amount && s.E<DamageAppliedEvent>().Count(e => e.SourceSeat == 0) == 2,
            "Foreign-turn activation keeps Li Jue as the damage source and samples its own single positive result.");
        s.Cold();

        var npc = Scene.Start(npc: true);
        npc.Until(() => npc.E<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Langxi && e.Activated && e.Completed));
        var npcRoll = npc.E<LangxiRandomResolvedEvent>().Single();
        Need(npcRoll.Amount is 1 or 2 && npc.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == npcRoll.TargetSeat && e.Amount == npcRoll.Amount),
            "The actual NPC optional activation and target choice issue real positive damage rather than rejecting a zero-valued new operation.");
        npc.Cold();
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => Enumerable.Range(0, 4).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.ToString() ?? "The real S5 Langxi command was rejected.");
        }
        public void Answer(Func<PromptChoice, bool> choose)
        {
            var p = P!;
            Accept(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, G.Revision));
        }
        public void Until(Func<bool> reached)
        {
            for (var i = 0; i < 120; i++)
            {
                if (reached()) return;
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected S5 Langxi choice: " + JsonSerializer.Serialize(P));
                Need(G.State.Winner == Winner.None, "The small Langxi fixture has not finished before its intended boundary.");
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The real S5 Langxi boundary was not reached: " + JsonSerializer.Serialize(P));
        }
        public void Offer() => Until(() => P?.SkillPrompt?.SkillId == Langxi && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        public void Activate() => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        public void TargetChoice() => Until(() => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "langxi-target") == true);
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(G.CreateSnapshot(s))),
            Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Every player view, source/window identity, RNG receipt, paid child, movement and command checkpoint cold-replays identically.");
        }
        public static Scene Start(bool npc = false)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
                new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheS5LiJuePackage(), new Fixture());
            var setup = new ShanheBattleSetup(17, 1, "fixture-li-jue", StandardShanheS5LiJuePackage.LijueId,
                4, 0, 0, 0, 0, [], [], [],
                [new(Target, Hp: 3) { StartingHand = [] }, new(Peer, Hp: 4) { StartingHand = [] }, new(Peer, Hp: 5) { StartingHand = [] }],
                [], new string('b', 64));
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = npc ? -1 : 0,
                HumanTeamId = npc ? null : ShanheBattleSetup.PlayerTeamId, HumanRole = null,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = npc ? 1 : 4
            }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); return s;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-li-jue", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{{Watch}}}","revision":1,"triggers":[{"id":"paid-damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var labels = new Dictionary<string, object>
            {
                [Quiet] = new { name = "静默同桌", description = "跳过出牌阶段" },
                [Watch] = new { name = "真实伤害孩子", description = "原生伤害后继续", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
            };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Peer, "真实高体力同桌", "supporter", Quiet, "wei", 6));
            builder.AddGeneral(new(Target, "真实伤害目标", "supporter", Quiet, "wei", 6, [Watch]));
            builder.AddDeck(new(Deck, "空起手真实牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "S5狼袭原生小局", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [StandardShanheS5LiJuePackage.LijueId, Peer, Target], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
