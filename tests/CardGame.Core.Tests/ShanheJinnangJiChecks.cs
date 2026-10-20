using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheJinnangJiChecks
{
    private const string Skill = StandardShanheCampaignCatalog.CurrentJinnangJiSkillId;
    private const string Huangkong = StandardShanheS5BossPackage.HuangkongId;
    private const string Zaiqi = StandardShanheCampaignCatalog.S5ZaiqiSkillId;
    private const string Owner = "fixture:jinnang-owner", Deck = "fixture:jinnang-deck";
    private const string Driver = "fixture:jinnang-driver", Gate = "fixture:jinnang-hp-source";
    private const string Gain = "fixture:jinnang-gain", ExtraDraw = "fixture:jinnang-extra-draw";
    private const string Ended = "fixture:jinnang-ended", Skip = "fixture:jinnang-skip", Marker = "fixture:jinnang-marker";
    private static string Peer(int seat) => "fixture:jinnang-peer-" + seat;
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(83, 1, "fixture:jinnang-production", StandardShanheS5BossPackage.ZhangliaoHuangkongId,
            4, 0, 0, 0, 0, [Skill, Zaiqi], [], [], [new("standard:cao-cao")], [], new string('b', 64))));

    public static void ActualDrawsIncludePlansAndExtraDrawsWithLateActiveSourcesCold()
    {
        var s = Scene.Start("plan");
        s.Until(s.BeforeExtraDraw);
        Need(s.V().Hp == 6 && s.V().HandCount == 6 && !s.HasSkill() && s.V().HandLimit == 6 && s.DrawTotal() == 6 &&
            s.E<ProgramNormalDrawAdjustedEvent>().Where(fact => fact.OwnerSeat == 0 &&
                fact.SkillId is "standard:yingzi" or Huangkong).Select(fact => fact.Adjustment).SequenceEqual([1, 3]),
            "At the genuine DrawEnded program before its extra Draw2, Yingzi's +1 and Huangkong's plan doubling have paid six real draws; the unowned tactic contributes nothing. " + s.Diagnostic());
        s.Cold(); s.AnswerContinue();
        s.Until(() => s.AtGain() && s.Draws().Any(fact => fact.Reason.Value == "skill-program." + ExtraDraw + ".Draw"));
        Need(s.DrawTotal() == 8 && s.V().HandCount == 8 && s.V().HandLimit == 6 &&
            s.Draws().Single(fact => fact.Reason.Value == "skill-program." + ExtraDraw + ".Draw").ActualCount == 2,
            "A genuine Draw2 inside DrawEnded is a second native draw, while absence of the tactic still suppresses its modifier.");
        s.Cold(); s.Play(); s.Use("lose-one");
        Need(s.HasSkill() && s.V().Hp == 5 && s.V().HandLimit == 9 &&
            s.E<HpConditionalSkillGrantChangedEvent>().Any(fact => fact.SkillId == Skill && fact.Added),
            "A real HP-conditional late acquisition reads the already issued eight draw-phase draws and adds four immediately.");
        s.Use("draw-three");
        Need(s.V().HandCount == 11 && s.DrawTotal() == 8 && s.V().HandLimit == 9 &&
            s.E<NativeDrawInvocationRecordedEvent>().Any(fact => fact.OwnerSeat == 0 &&
                fact.Phase?.Kind == ActualDiscardRecoveryPhaseKind.Play && fact.ActualCount == 3),
            "A real Play-phase Draw3 changes hand size but cannot borrow the old Draw-phase allowance.");
        s.Use("recover-one");
        Need(s.V().Hp == 6 && !s.HasSkill() && s.V().HandLimit == 6,
            "Recovery withdraws the actual HP source and immediately removes the numeric contribution without deleting draw facts.");
        s.Cold(); s.Use("lose-one");
        Need(s.V().Hp == 5 && s.HasSkill() && s.V().HandLimit == 9 && s.DrawTotal() == 8,
            "Reacquiring the source in the same real turn reads the same facts once rather than issuing another draw or counter.");
        s.AssertPureQueries();
        var turn = s.Turn;
        s.EndPlay(); s.Until(s.AtEnd);
        Need(s.E<TurnEndedEvent>().Any(fact => fact.TurnNumber == turn && fact.ActorSeat == 0) &&
            s.Turn == turn && s.V().HandLimit == 5 && s.HasSkill(),
            "A genuine TurnEnded boundary clears the contribution before the cursor advances or the turn number changes.");
        s.Cold();
    }

    public static void ReplacementGainsDoNotCountButRealDrawPhaseSkillDrawsDoCold()
    {
        foreach (var scene in new[] { "tuxi", "zaiqi" })
        {
            var s = Scene.Start(scene);
            var replacement = scene == "tuxi" ? "classic:tuxi" : Zaiqi;
            s.Until(() => s.P?.SkillPrompt?.SkillId == replacement && s.P.Choices.Any(choice => Action(choice) == "activate"));
            s.Cold(); s.Answer(choice => Action(choice) == "activate");
            if (scene == "tuxi")
            {
                s.Until(() => s.P?.Choices.Any(choice => choice.Targets.SequenceEqual([1, 2])) == true);
                s.Answer(choice => choice.Targets.SequenceEqual([1, 2]));
            }
            s.Until(s.AtGain);
            Need(s.DrawTotal() == 0 && s.V().HandLimit == s.V().Hp,
                "True Tuxi transfers and 887's reveal-then-gain create no native draw invoice and no hand-limit bonus. " + s.Diagnostic());
            if (scene == "tuxi")
                Need(s.E<ProgramRandomHandCardsTakenEvent>().Single().TargetSeats.SequenceEqual([1, 2]) &&
                    s.G.CardMovements.Any(move => move.From == CardLocation.Hand(1) && move.To == CardLocation.Processing &&
                        s.G.CardMovements.Any(gained => gained.CardId == move.CardId && gained.From == CardLocation.Processing &&
                            gained.To == CardLocation.Hand(0) && gained.Sequence > move.Sequence)),
                    "The first excluded gain really came from another player's original hand.");
            else
                Need(s.E<ProgramCardsRevealedEvent>().Single(fact => fact.SkillId == Zaiqi).Cards.Count == 3 &&
                    s.G.CardMovements.Count(move => move.From == CardLocation.Processing && move.To == CardLocation.Hand(0)) == 3,
                    "The excluded 887 gain consists of all three real black cards revealed for two lost HP plus one.");
            s.Cold(); s.Play();
            Need(s.DrawTotal() == 3 && s.V().HandLimit == s.V().Hp + 1 &&
                s.Draws().Single().Reason.Value == "skill-program." + ExtraDraw + ".Draw" && s.Draws().Single().ActualCount == 3 &&
                !s.G.CardMovements.Any(move => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0) &&
                    move.Reason == CardMoveReasons.Draw),
                "Replacing ordinary Draw does not suppress a separate real Draw3 in DrawEnded; three actual draws add floor(3/2), not the planned quota or gained-card total. " + s.Diagnostic());
            s.Cold();
        }
    }

    public static void MultipleRealDrawPhasesAccumulateAndTurnEndClearsWithScopedSchemaCold()
    {
        VerifyParserScope();
        var plain = Scene.Start("plain"); plain.Play();
        Need(plain.V().HandLimit == 6 && plain.E<NativeDrawInvocationRecordedEvent>().Length == 0 &&
            plain.E<ActualDiscardRecoveryPhaseStartedEvent>().Length == 0,
            "A registry with no temporal draw modifier retains the old zero-invoice/zero-phase shape.");
        plain.Cold();

        var s = Scene.Start("phases");
        s.Until(() => s.P?.SkillPrompt?.SkillId == Marker && s.P.Choices.Any(choice => choice.Targets.SequenceEqual([0])));
        s.Answer(choice => choice.Targets.SequenceEqual([0])); s.Play();
        Need(s.DrawTotal() == 0 && s.V().HandLimit == 6 &&
            s.E<ProgramLeastHandMarkerOrDrawEvent>().Single() is { TargetSeat: 0, GrantedMarker: true },
            "A genuinely skipped first Draw grants the minimum-hand owner a real future extra-phase marker, with no fabricated Draw.");
        s.EndPlay(); s.Until(s.AtEnd); s.Cold(); s.AnswerContinue();
        s.Until(() => s.AtPhaseDrawGain(extra: false));
        var turn = s.Turn;
        Need(s.Draws().Single().ActualCount == 3 && s.DrawTotal() == 3 && s.V().HandLimit == 7 &&
            s.E<ExtraDrawPhaseStartedEvent>().Length == 0,
            "The next real owner's ordinary Draw3 contributes floor(3/2) before its marked extra phase begins. " + s.PhaseDiagnostic());
        s.Cold(); s.AnswerContinue(); s.Until(() => s.AtPhaseDrawGain(extra: true));
        var draws = s.Draws();
        Need(s.Turn == turn && draws.Length == 2 && draws.Select(fact => fact.Phase!.Token).Distinct().Count() == 2 &&
            draws.Select(fact => fact.ActualCount).SequenceEqual([3, 3]) && s.DrawTotal() == 6 && s.V().HandLimit == 9 &&
            s.E<ExtraDrawPhaseStartedEvent>().Single().ActualTurnNumber == turn,
            "Two distinct, genuinely issued Draw3 phases add floor((3+3)/2)=3, not floor(3/2)+floor(3/2)=2. " + s.PhaseDiagnostic());
        s.Cold(); s.Play();
        Need(s.E<ProgramMarkerExtraDrawConsumedEvent>().Length == 1 && s.DrawTotal() == 6 && s.V().HandLimit == 9,
            "Cold replay and the exact extra-phase return neither consume the marker nor count its physical Draw3 twice. " + s.PhaseDiagnostic());
        s.EndPlay(); s.Until(s.AtEnd);
        Need(s.Turn == turn && s.V().HandLimit == 6, "The allowance is zero in the real ended-turn child, with the tactic still owned. " + s.PhaseDiagnostic());
        s.Cold();
    }

    private static void VerifyParserScope()
    {
        foreach (var change in new Action<Dictionary<string, object>>[]
        {
            modifier => modifier["query"] = "drawCount", modifier => modifier["operation"] = "set",
            modifier => modifier["recipientScope"] = "otherPublicTeamAllies", modifier => modifier["value"] = 2,
            modifier => modifier["valueMultiplier"] = 2, modifier => modifier["valueZone"] = "hand"
        })
        {
            var modifier = new Dictionary<string, object> { ["id"] = "draw-capacity", ["query"] = "handLimit",
                ["operation"] = "add", ["valueExpression"] = "currentTurnDrawPhaseDrawCountHalfFloor", ["priority"] = 0 };
            change(modifier);
            var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion,
                skills = new[] { new { id = "fixture:jinnang-invalid", revision = 1, modifiers = new[] { modifier } } } });
            var labels = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { ["fixture:jinnang-invalid"] = new { name = "非法查询", description = "限制表达式适用范围" } } });
            var rejected = false;
            try { _ = SkillProgramCatalog.Load(rules, labels); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected, "The actual-draw expression must reject an unrelated query, set, recipient, simultaneous value or extra input.");
        }
    }

    private static string Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action") ?? "";
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public int Turn => G.CreateSnapshot(0).TurnNumber;
        public PlayerSnapshot V() => G.CreateSnapshot(0).Players[0];
        public T[] E<T>() => G.Events.Select(fact => fact.Payload).OfType<T>().ToArray();
        public bool HasSkill() => V().Skills?.Any(skill => skill.ContentId == Skill) == true;
        public NativeDrawInvocationRecordedEvent[] Draws() => E<NativeDrawInvocationRecordedEvent>().Where(fact =>
            fact.OwnerSeat == 0 && fact.TurnNumber == Turn && fact.Phase is { Kind: ActualDiscardRecoveryPhaseKind.Draw, ActorSeat: 0 }).ToArray();
        public int DrawTotal() => Draws().Sum(fact => fact.ActualCount);
        public bool AtGain() => P?.SkillPrompt?.SkillId == Gain && P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue");
        public bool BeforeExtraDraw() => P?.SkillPrompt?.SkillId == ExtraDraw &&
            P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "before-extra");
        public bool AtPhaseDrawGain(bool extra)
        {
            if (!AtGain()) return false;
            var batch = G.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(frame => frame.SkillId == Gain)?.WindowContext?.MovementBatch;
            return batch?.NativeDrawInvocation is { OwnerSeat: 0, Phase: { Kind: ActualDiscardRecoveryPhaseKind.Draw, ActorSeat: 0 } } proof &&
                proof.LastBatchId == batch.Id && proof.Reason == (extra ? new CardMoveReason("program.extra-draw-phase.draw") : CardMoveReasons.Draw);
        }
        public bool AtEnd() => P?.SkillPrompt?.SkillId == Ended && P.Choices.Any(choice => choice.Parameters.GetValueOrDefault("option-id") == "ended");
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, (result.Error?.Message ?? "Jinnang Ji command rejected.") + " " + Diagnostic());
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var p = P ?? throw new InvalidOperationException("Missing Jinnang Ji prompt.");
            Accept(new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, G.Revision));
        }
        public void AnswerContinue() => Answer(choice => choice.Parameters.GetValueOrDefault("option-id") is "continue" or "ended" or "before-extra");
        public void Until(Func<bool> done)
        {
            for (var step = 0; step < 260; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 })
                {
                    if (AtGain() || AtEnd() || BeforeExtraDraw()) { AnswerContinue(); continue; }
                    throw new InvalidOperationException("Unexpected Jinnang Ji decision: " + Diagnostic());
                }
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded Jinnang Ji boundary: " + Diagnostic());
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Use(string activation)
        {
            Accept(new UseProgramSkillCommand(0, Driver, activation, [], [], G.Revision, P!.PromptId)); Play();
        }
        public void EndPlay() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
            Facts = G.Events.Select(fact => JsonSerializer.Serialize(fact.Payload, fact.Payload.GetType())),
            G.Revision, G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay preserves exact phase/draw facts, real children and hand-limit queries without issuing or counting a payment twice.");
        }
        public void AssertPureQueries()
        {
            var before = State();
            for (var repeat = 0; repeat < 3; repeat++)
                for (var viewer = 0; viewer < 4; viewer++) _ = G.CreateSnapshot(viewer);
            Need(before == State(), "Repeated filtered hand-limit queries must not issue facts, mutate state, reveal hands, or change accepted commands.");
        }
        public string Diagnostic() => JsonSerializer.Serialize(new
        {
            scene, Turn, V().Hp, V().HandCount, V().HandLimit, HasTactic = HasSkill(), Draws = Draws(),
            Prompt = P is null ? null : new { P.Kind, Skill = P.SkillPrompt?.SkillId, P.Choices },
            Bindings = E<ProgramBindingResolvedEvent>(), Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind })
        });
        public string PhaseDiagnostic() => JsonSerializer.Serialize(new
        {
            scene, Turn, V().Hp, V().HandCount, V().HandLimit, Draws = Draws(),
            Extra = E<ExtraDrawPhaseStartedEvent>(), Completed = E<ActualDrawPhaseCompletedEvent>(),
            PhaseStarts = E<ActualDiscardRecoveryPhaseStartedEvent>().Where(fact => fact.Phase.TurnNumber == Turn),
            DrawFrames = G.ResolutionStack.OfType<DrawPhaseObligationFrame>().Select(frame => new
            {
                frame.Id, frame.OwnerSeat, frame.ActualTurnNumber, frame.IsExtra, frame.Stage,
                frame.FrozenBaseDrawCount, frame.DrawAdjustment, frame.DrawCount
            }),
            GainBatches = G.ResolutionStack.OfType<ProgramSkillFrame>().Where(frame => frame.SkillId == Gain)
                .Select(frame => frame.WindowContext?.MovementBatch).Where(batch => batch is not null)
                .Select(batch => new { Id = batch!.Id, NativeDrawInvocation = batch!.NativeDrawInvocation }),
            Prompt = P is null ? null : new { P.Kind, Skill = P.SkillPrompt?.SkillId }
        });
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var setup = new ShanheBattleSetup(83, 1, "fixture:jinnang-" + scene, Owner, scene == "zaiqi" ? 4 : 6,
                0, 0, 0, 0, [], [], [], [new(Peer(1)), new(Peer(2)), new(Peer(3))], [], new string('b', 64));
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                MaxTurns = 12, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand()); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-jinnang", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            if (scene != "plain") b.AddSkill(Production.Value.GetSkill(Skill));
            if (scene == "plan") b.AddSkill(Production.Value.GetSkill(Huangkong));
            if (scene == "tuxi") b.AddSkill(Production.Value.GetSkill("classic:tuxi"));
            if (scene == "zaiqi") b.AddSkill(Production.Value.GetSkill(Zaiqi));
            object Pause(string option) => new { op = "chooseOption", target = "owner", resultBind = option, options = new[] { new { id = option } } };
            var definitions = new List<Dictionary<string, object>>();
            var presentation = new Dictionary<string, object>();
            void Add(string id, string property, object value, params string[] options)
            {
                definitions.Add(new() { ["id"] = id, ["revision"] = 1, [property] = value });
                presentation[id] = new { name = id, description = "真实阶段、摸牌与技能来源行为，不注入事件或计数",
                    optionLabels = options.ToDictionary(option => option, option => option) };
            }
            object Activation(string id, string op, int amount) => new
            {
                id, minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                effects = new[] { new { op, target = "owner", amount } }
            };
            Add(Driver, "activations", new[] { Activation("lose-one", "loseHp", 1), Activation("recover-one", "recover", 1),
                Activation("draw-three", "draw", 3) });
            if (scene != "plain")
            {
                Add(Gain, "triggers", new[]
                {
                    new { id = "real-gain", window = "cardsGained", subject = "owner", destinationZones = new[] { "hand" },
                        movementOccurrence = "perBatch", movementReasons = new[] { CardMoveReasons.Draw.Value,
                            "program.extra-draw-phase.draw", "skill-program." + ExtraDraw + ".Draw", "skill-program." + Driver + ".Draw",
                            "skill-program." + Zaiqi + ".MoveBoundCards", "skill-program.classic:tuxi.TakeRandomHandCardFromSelectedTargets" },
                        optional = false, effects = new[] { Pause("continue") } }
                }, "continue");
                Add(Ended, "triggers", new[]
                {
                    new { id = "actual-turn-ended", window = "afterTurnEnded", subject = "owner", optional = false,
                        effects = new[] { Pause("ended") } }
                }, "ended");
            }
            if (scene == "plan") Add(Gate, "hpSkillGrants", new[] { new { id = "wounded-source", skillId = Skill, maximumHp = 5 } });
            if (scene is "plan" or "tuxi" or "zaiqi") Add(ExtraDraw, "triggers", new[]
            {
                new { id = "real-extra-draw", window = "drawPhaseEnded", subject = "owner", optional = false,
                    effects = scene == "plan"
                        ? new object[] { Pause("before-extra"), new { op = "draw", target = "owner", amount = 2 } }
                        : new object[] { new { op = "draw", target = "owner", amount = 3 } } }
            }, scene == "plan" ? ["before-extra"] : []);
            if (scene == "phases")
            {
                Add(Skip, "triggers", new[]
                {
                    new { id = "first-real-draw-skip", window = "turnStartBeforeNormalFlow", subject = "owner",
                        usageScope = "game", usageLimit = 1, optional = false,
                        effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "draw" } } } }
                });
                Add(Marker, "triggers", new[]
                {
                    new { id = "real-extra-phase-marker", window = "drawPhaseSkipped", subject = "owner",
                        usageScope = "game", usageLimit = 1, optional = false, effects = new object[]
                        {
                            new { op = "selectTargets", target = "owner", targetKind = "anyLiving", minimumTargets = 1,
                                maximumTargets = 1, targetAiOrder = "supportDraw" },
                            new { op = "grantLeastHandMarkerOrDraw", target = "selectedTarget", marker = "zi", amount = 1 }
                        } }
                });
            }
            var programs = SkillProgramCatalog.Load(
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentation }));
            foreach (var (id, program) in programs.Programs) b.AddSkill(new(id, programs.Presentations[id].Name, programs.Presentations[id].Description)
                { Program = program, ProgramPresentation = programs.Presentations[id] });
            var skills = new List<string>();
            if (scene != "plain") skills.AddRange([Gain, Ended]);
            if (scene == "plan") skills.AddRange([Gate, Huangkong, "standard:yingzi", ExtraDraw]);
            else if (scene is "tuxi" or "zaiqi") skills.AddRange([Skill, scene == "tuxi" ? "classic:tuxi" : Zaiqi, ExtraDraw]);
            else if (scene == "phases") skills.AddRange([Skill, Skip, Marker]);
            b.AddGeneral(new(Owner, "锦囊计真实阶段使用者", "", Driver, "qun", 6, skills));
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new(Peer(seat), "仅持真实闪的对手", "", "standard:none", "wei", 6));
            b.AddDeck(new(Deck, "固定黑色闪实体", scene == "tuxi" ? 2 : 0, scene == "phases" ? 3 : 2, [])
            { PhysicalCards = Enumerable.Range(0, 96).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "锦囊计真实回合", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
