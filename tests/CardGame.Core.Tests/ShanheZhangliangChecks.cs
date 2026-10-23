using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheZhangliangChecks
{
    private const string Boss = StandardShanheS5ZhangliangPackage.ZhangliangId;
    private const string Rain = StandardShanheS5ZhangliangPackage.BaoyuId;
    private const string Shadow = StandardShanheS5ZhangliangPackage.WuyingId;
    private const string Driver = "fixture:shanhe-zhangliang-driver", Watch = "fixture:shanhe-zhangliang-watch";
    private const string Quiet = "fixture:shanhe-zhangliang-quiet", Human = "fixture:shanhe-zhangliang-human";

    public static void NativeFirePreventionAndRoundDamageRecoverCold()
    {
        var s = Scene.Start(); s.Play();
        Need(s.V().HandCount == 10 && s.WuyingCount == 0 && s.V().Hp == 19,
            "Round one rain damages the actual enemy once; a native Draw2 reaching exactly ten cards does not trigger Wuying.");
        var bossHp = s.V(1).Hp;
        var fire = s.V().Hand.Single(c => c.Kind == CardKind.FireSlash);
        s.Accept(new PlayCardCommand(0, fire.Id, [1], s.G.Revision, s.P!.PromptId)); s.Play();
        Need(s.V(1).Hp == bossHp && !s.Facts<DamageAppliedEvent>().Any(e => e.SourceSeat == 0),
            "A real paid FireSlash is prevented before applying damage to the boss.");
        s.Cold(); s.Use("thunder", 1); s.Play();
        Need(s.V(1).Hp == bossHp - 1 && s.Facts<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Nature == DamageNature.Thunder),
            "Non-fire native damage remains effective.");
        s.Cold(); s.End(); s.Play();
        Need(s.Facts<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Rain && e.BindingId == "round-rain" && e.Completed) == 2,
            "The next real round applies rain once again through the shared sequential damage flow: " +
            JsonSerializer.Serialize(new { s.G.CreateSnapshot(0).TurnNumber, RainFacts = s.Facts<ProgramBindingResolvedEvent>().Where(e => e.SkillId == Rain) }));
        s.Cold();
    }

    public static void OwnTurnActualDrawAndObtainBatchesDealOnceCold()
    {
        var s = Scene.Start(); s.Play(); var hp = s.V().Hp;
        s.Use("draw-two"); s.AtWatch();
        s.CheckGainFrame(CardLocation.DrawPile);
        Need(s.V().HandCount == 12 && s.V().Hp == hp - 1,
            "One real two-card draw batch above ten hand cards deals one native damage, rather than damage per entity.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.WuyingCount == 1, "The paid native damage returns to the original gain frame once.");
        s.Use("gain-two", 1); s.AtWatch(); s.CheckGainFrame(CardLocation.Hand(1));
        Need(s.V().HandCount == 14 && s.V().Hp == hp - 2,
            "A two-card hand acquisition also qualifies once; Wuying is not limited to draws.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.WuyingCount == 2, "Cold recovery cannot repeat a completed acquisition penalty.");
        var otherHand = s.V(1).HandCount;
        s.Use("draw-other", 1); s.Play();
        Need(s.V(1).HandCount == otherHand + 2 && s.WuyingCount == 2,
            "Another character gaining cards during the enemy's turn cannot borrow the actual turn owner's Wuying qualification.");
        s.Cold();
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(0).Players[seat];
        public T[] Facts<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public int WuyingCount => Facts<ProgramBindingResolvedEvent>().Count(e => e.SkillId == Shadow && e.Completed);
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.Message ?? "The Zhang Liang native command was rejected.");
        }
        public void Use(string id, params int[] targets) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets, G.Revision, P!.PromptId));
        public void Continue() => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.Single().Id, G.Revision));
        public void End()
        {
            var turn = G.CreateSnapshot(0).TurnNumber;
            Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
            Accept(new AdvanceOneStepCommand(G.Revision));
            Reach(() => G.CreateSnapshot(0).TurnNumber > turn && P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, allowWatch: true);
        }
        public void AtWatch() => Reach(() => P?.SkillPrompt?.SkillId == Watch, allowWatch: false);
        public void Play() => Reach(() => P is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }, allowWatch: true);
        private void Reach(Func<bool> arrived, bool allowWatch)
        {
            for (var i = 0; i < 150; i++)
            {
                if (arrived()) return;
                if (P is { } pending)
                {
                    Need(pending.SkillPrompt?.SkillId == Driver || allowWatch && pending.SkillPrompt?.SkillId == Watch,
                        "Unexpected human prompt: " + JsonSerializer.Serialize(pending));
                    Accept(new AnswerPromptCommand(0, pending.PromptId, pending.Choices.First().Id, G.Revision));
                }
                else Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("The bounded Zhang Liang flow did not reach its expected native window.");
        }
        public void CheckGainFrame(CardLocation origin)
        {
            var frame = G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Shadow);
            Need(frame.InstructionIndex == 2 && frame.SelectedTargetSeats.SequenceEqual([0]) &&
                frame.WindowContext is { Window: SkillProgramTriggerWindow.CardsGained, MovementBatch: { } batch } &&
                (origin == CardLocation.DrawPile
                    ? batch.NativeDrawInvocation is { ActualCount: 2 } proof &&
                        proof.Materials.All(m => G.CardMovements.Any(move => move.Sequence == m.MovementSequence && move.CardId == m.CardId &&
                            move.From == origin && move.To == CardLocation.Hand(0)))
                    : batch.Movements.Count(m => m.From == origin && m.To == CardLocation.Hand(0)) == 2),
                "The shared native damage child remains attached to its exact real two-card gain batch and selected enemy: " +
                JsonSerializer.Serialize(new { frame.InstructionIndex, frame.SelectedTargetSeats, frame.WindowContext }));
        }
        public void Cold()
        {
            string State() => JsonSerializer.Serialize(new
            {
                Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()), G.CardMovements,
                Views = Enumerable.Range(0, G.PlayerCount).Select(s => SnapshotJson.Serialize(G.CreateSnapshot(s))).ToArray(),
                Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
                Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray()
            });
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves the actual gain, paid native damage child, views and round cursor.");
        }
        public static Scene Start()
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardShanheS5ZhangliangPackage(), new Fixture());
            var setup = new ShanheBattleSetup(83, 1, "fixture-zhangliang", Human, 20, 0, 0, 0, 0, [Driver, Watch],
                [new(CardKind.FireSlash), .. Enumerable.Range(0, 7).Select(_ => new ShanheStartingCard(CardKind.Dodge))], [],
                [new(Boss) { ExtraSkillIds = [Quiet], StartingHand = Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Peach)).ToArray() }],
                [], new string('a', 64));
            var g = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 2, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 6
            }, registry);
            var scene = new Scene(g, registry); scene.Accept(new StartGameCommand()); return scene;
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-zhangliang", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}],
                "activations":[
                  {"id":"draw-two","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":2}]},
                  {"id":"draw-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":2}]},
                  {"id":"gain-two","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"selectedTarget","minimumCards":2,"maximumCards":2,"zones":["hand"],"resultBind":"acquired"},{"op":"moveBoundCards","target":"owner","sourceBind":"acquired","destination":"ownerHand","awaitMovementTriggers":true}]},
                  {"id":"thunder","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"thunder"}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]},{"id":"quiet-play","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"paid-damage","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实获牌", description = "驱动原生牌流" },
                    [Quiet] = new { name = "静默守军", description = "固定回合" },
                    [Watch] = new { name = "伤害证人", description = "原生已付伤害后暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } }
                }
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Human, "试炼义士", "", "standard:none", "shu", 20));
            b.AddDeck(new("fixture:shanhe-zhangliang-deck", "固定闪牌堆", 0, 2, [new("standard:dodge", 60)]));
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "真实暴雨无影", 2, 2, new Dictionary<string, int>(), "fixture:shanhe-zhangliang-deck",
                GeneralPoolIds: [Human, Boss], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }
}
