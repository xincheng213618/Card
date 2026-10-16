using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GaoGanChecks
{
    private const string Skill = "ol:juguan", Driver = "fixture:gg-driver", Gain = "fixture:gg-gain", Watch = "fixture:gg-watch";
    private const string Quiet = "fixture:gg-quiet", Extra = "fixture:gg-extra", Skip = "fixture:gg-skip", Counter = "fixture:gg-counter";
    private const string Conditional = "fixture:gg-conditional", Prevent = "fixture:gg-prevent", PeerSlash = "fixture:gg-peer-slash";
    private const string Owner = "fixture:gg-owner", Mode = "identity:classic-gaogan-fixture";

    public static void NativeUseVictimsAndSourceLossRetainOneDeferredDraw()
    {
        var (g, registry) = Start("loss");
        var action = Actions(g).First(a => a.PlayedCardKind == CardKind.Duel && a.TargetSeats.SequenceEqual([1])); var material = action.CardId!.Value;
        SubmitPlay(g, action); Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.JuguanUse is not null); var receipt = use.JuguanUse!;
        Need(receipt is { Source.OwnerSeat: 0, DeclaredKind: CardKind.Duel } && receipt.MaterialCardId == material && use.Action!.ActionId == receipt.CardActionId &&
            use.Action.PhysicalCards is [var cost] && cost.From == CardLocation.Hand(0) && !E<JuguanVictimDamagedEvent>(g).Any() &&
            V(g, 0).Skills!.All(s => s.Id != Skill) && P(g) is { IsPrivate: true, PlayerSeat: 0 } && g.CreateSnapshot(1).PendingDecision is null,
            "The real one-hand Duel owns its exact accepted use/action receipt; a native before-damage child may remove the source before any victim is invented.");
        g = Cold(g, registry); Continue(g); Play(g);
        var victim = E<JuguanVictimDamagedEvent>(g).Single();
        Need(victim.CardUseFrameId == use.Id && victim.CardActionId == receipt.CardActionId && victim is { OwnerSeat: 0, VictimSeat: 1, ActualDamageSourceSeat: 0, Amount: 1 } &&
            g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 && !Actions(g).Any(),
            "Actual positive damage of that issued Duel alone arms the victim obligation, despite source loss and without repeating its native material payment.");
        Use(g, "damage", [2]); Play(g); Use(g, "retaliate", [3]); Play(g);
        Need(E<JuguanVictimDamagedEvent>(g).Count() == 1 && !E<JuguanRetaliationRecordedEvent>(g).Any(),
            "An unrelated program damage to seat two and real incoming damage from an unrecorded seat do not replace the exact use or its actual victim.");
        Use(g, "kill", [1]); Play(g); Need(!V(g, 1).IsAlive && E<JuguanVictimDamagedEvent>(g).Count() == 1,
            "Death of the actual recorded victim preserves its already issued obligation, without treating the later unrelated killing damage as a new Juguan victim.");
        var firstTurn = g.State.TurnNumber; Use(g, "extra"); Play(g); End(g); OwnDraw(g, firstTurn);
        Need(g.State.TurnNumber == firstTurn + 1 && E<JuguanDeadlineSettledEvent>(g).Last() is { OwnerSeat: 0, Qualified: true } && ActualDrawCount(g) == 4 &&
            E<JuguanDrawPhaseBoundEvent>(g).Single().Phase == DrawBatch(g).DiscardRecoveryPhase && V(g, 0).Skills!.All(s => s.Id != Skill),
            "The next real extra turn freezes the deadline and gives its native first Draw exactly +2 even after the source vanished. " + JsonSerializer.Serialize(new { firstTurn, g.State.TurnNumber, Deadline = E<JuguanDeadlineSettledEvent>(g).ToArray(), Moves = ActualDrawCount(g), Bound = E<JuguanDrawPhaseBoundEvent>(g).ToArray(), Phase = DrawBatch(g).DiscardRecoveryPhase, Skills = V(g, 0).Skills!.Select(s => s.Id).ToArray() }));
        g = Cold(g, registry); Continue(g); Play(g); _ = Cold(g, registry);
        var rewardedTurn = g.State.TurnNumber; End(g); OwnDraw(g, rewardedTurn);
        Need(ActualDrawCount(g) == 2 && E<JuguanDeadlineSettledEvent>(g).Last() is { OwnerSeat: 0, Qualified: false } && E<JuguanDrawPhaseBoundEvent>(g).Count() == 1,
            "The old use is settled once and cannot reward another owner turn or copy its bound Draw allowance.");
        g = Cold(g, registry); Continue(g); Play(g); _ = Cold(g, registry);
    }

    private static PlayerSnapshot V(GameEngine g, int s) => g.CreateSnapshot(s).Players[s];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>();
    private static IEnumerable<LegalAction> Actions(GameEngine g) => g.GetHumanLegalActions().Where(a => a.ConversionSource?.SkillId == Skill);
    private static CardMovementBatchContext DrawBatch(GameEngine g) => g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last().Batch;
    private static int ActualDrawCount(GameEngine g) => g.CardMovements.Count(m => m.TurnNumber == g.State.TurnNumber && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw);
    private static void SubmitPlay(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? targets = null) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> test) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(test).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void OwnDraw(GameEngine g, int afterTurn) => Reach(g, () => g.State.TurnNumber > afterTurn && P(g) is { PlayerSeat: 0, SkillPrompt.SkillId: Gain });
    private static void Reach(GameEngine g, Func<bool> predicate)
    {
        for (var i = 0; i < 230; i++)
        {
            if (predicate()) return; var p = P(g);
            if (p?.SkillPrompt?.SkillId is Gain or Watch) Continue(g);
            else if (p?.SkillPrompt?.SkillId is Extra or Counter && p.Choices.Any(c => c.Targets.SequenceEqual([0]))) Answer(g, c => c.Targets.SequenceEqual([0]));
            else if (p?.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(p.PlayerSeat, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else if (p?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip") == true) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "take-damage")) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) End(g);
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var text = JsonSerializer.Serialize(P(g)); throw new InvalidOperationException("Gao Gan boundary missing: " + text[..Math.Min(500, text.Length)] + "; frames=" + JsonSerializer.Serialize(g.ResolutionStack.Select(f => new { f.Id, f.Kind, f.Step })) + "; facts=" + JsonSerializer.Serialize(g.Events.TakeLast(5).Select(e => e.Payload.ToString())));
    }
    private static void Need(bool ok, string text) { if (!ok) throw new InvalidOperationException(text); }
    private static void Accept(GameEngine g, GameCommand command) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Gao Gan native command."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry r)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
        static string State(GameEngine e) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(e.CreateSnapshot(s))).ToArray(), Frames = e.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = e.Events.Select(x => JsonSerializer.Serialize(x.Payload, x.Payload.GetType())).ToArray(), e.CardMovements, Commands = CommandJson.Serialize(e.AcceptedCommands), Zones = e.CreateCardZoneDiagnostics() });
        Need(State(g) == State(cold), "Cold replay preserves private views, original native card receipts, pending damage and Draw children, retaliation/deadline facts, exact entities and commands."); return cold;
    }
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 18 }, r);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, r);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-gao-gan", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:gao-gan");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"range","query":"slashDistanceLimit","operation":"unlimited","value":0,"priority":0}{{(scenario == "multi" ? ",{\"id\":\"multiple\",\"query\":\"cardTargetCount\",\"operation\":\"add\",\"value\":1,\"priority\":0,\"cardKinds\":[\"slash\"]}" : "")}}],
               "triggers":[{"id":"stock","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"draw","target":"owner","amount":6}]}],"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"kill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":20}]},
                {"id":"lose-hp","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                {"id":"retaliate","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
                {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"extra","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerGame":1,"effects":[{"op":"pendExtraTurn","target":"owner"}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"actual-draw","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["rule.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"gain","options":[{"id":"continue"}]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"source-loss","window":"beforeDamageApplied","subject":"damageSource","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"damage","options":[{"id":"continue"}]}]}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy-juguan","skillId":"{{Skill}}","minimumHp":5}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Extra}}","revision":1,"triggers":[{"id":"extra-play","window":"playEnding","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"insertPhase","target":"selectedTarget","phase":"play","phaseContinuation":"beforeNormalPreparation"}]}]},
              {"id":"{{Skip}}","revision":1,"triggers":[{"id":"real-skip","window":"turnStartBeforeNormalFlow","subject":"owner","optional":true,"usageScope":"game","usageLimit":1,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["draw"]}]}]},
              {"id":"{{Counter}}","revision":1,"triggers":[{"id":"immediate","window":"afterDamageApplied","subject":"owner","damageOccurrence":"perDamage","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"currentTurnPlayer"},{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Prevent}}","revision":1,"triggers":[{"id":"prevent","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"preventCurrentDamage","target":"owner"}]}]},
              {"id":"{{PeerSlash}}","revision":1,"viewAs":[{"id":"respond-native","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":false,"forResponse":true}]}
            ]}
            """;
            var ids = new[] { Driver, Gain, Watch, Conditional, Quiet, Extra, Skip, Counter, Prevent, PeerSlash };
            var labels = ids.ToDictionary(id => id, id => new { name = id, description = "高干实际伤害与阶段边界夹具", optionLabels = id is Gain or Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "实际规则夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:gg-owner-pick", "固定主人", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:gg-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == Role.Lord ? -100000d : 100000d) });
            var owner = new List<string> { scenario == "loss" ? Conditional : Skill, Driver, Gain };
            if (scenario == "loss") owner.Add(Watch); if (scenario == "multi") owner.Add(Extra); if (scenario == "skip") owner.Add(Skip);
            b.AddGeneral(new(Owner, "高干实际机制", "ol-gao-gan", "fixture:gg-owner-pick", "qun", 4, owner));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:gg-peer-{i}").ToArray();
            foreach (var id in peers)
            {
                var skills = new List<string> { Quiet, Gain };
                if (scenario == "self") skills.Add(PeerSlash); if (scenario == "immediate") skills.Add(Counter); if (scenario == "prevent") skills.Add(Prevent);
                b.AddGeneral(new(id, "其他", "supporter", "fixture:gg-peer-pick", "wei", scenario == "self" ? 1 : 8, skills));
            }
            b.AddDeck(new("fixture:gg-deck", "固定无基本响应实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "高干真实命令", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:gg-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
