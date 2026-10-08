using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class TurnEndDamageMarkerChecks
{
    private const string MarkerSkill = "fixture:ending-damage-marker";
    private const string Driver = "fixture:ending-damage-driver";
    private const string Before = "fixture:ending-damage-before";
    private const string After = "fixture:ending-damage-after";
    private const string Owner = "fixture:ending-damage-owner";
    private const string Mode = "identity:classic-ending-damage-marker";

    public static void FrozenActualDamageAddsOnceAcrossChildrenAndColdReplay()
    {
        RejectUnsupportedContracts();
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 12
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, g => P(g) is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, P(game)!.PromptId));
        ReachPlay(game);
        Require(Enumerable.Range(0, 4).All(seat => Marker(game, seat) == 4) &&
                PlayerMarkerCatalog.GetDisplayName(PlayerMarkerKind.Jue) == "爵",
            "The distinct public Jue marker starts through the existing attributed marker operation.");

        Use(game, "outgoing", [1]); ReachPlay(game);
        Use(game, "hp-loss"); ReachPlay(game);
        Use(game, "source-less"); ReachPlay(game);
        Use(game, "incoming", [1]); ReachPlay(game);
        Require(Facts<DamageAppliedEvent>(game).Count(f => f.SourceSeat == 0 && f.TargetSeat == 1 && f.Amount == 2 && !f.SourceLess) == 1 &&
                Facts<DamageAppliedEvent>(game).Count(f => f.TargetSeat == 0 && f.Amount == 1 && f.SourceLess) == 1 &&
                Facts<DamageAppliedEvent>(game).Count(f => f.SourceSeat == 1 && f.TargetSeat == 0 && f.Amount == 1 && !f.SourceLess) == 1 &&
                Facts<ProgramSkillHpLostEvent>(game).Count(f => f.SkillId == Driver && f.Amount == 1) == 1,
            "The small fixture issues genuine outgoing damage, HP loss, sourceless damage and another source's damage before ending.");

        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, g => P(g)?.SkillPrompt?.SkillId == Before);
        var ending = game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single();
        var parentId = ending.Id; var turn = ending.TurnNumber;
        Require(ending.OwnerSeat == 0 && ending.Facts.TurnOwnerDamageDealtThisTurn == 2 &&
                !Facts<ProgramEndingTurnDamageMarkerAddedEvent>(game).Any(),
            "The actual own ending boundary freezes two damage, excluding HP loss, sourceless damage and another actor's damage.");
        RejectWrongActor(game);
        game = Cold(game, registry);
        Continue(game);
        Reach(game, g => P(g)?.SkillPrompt?.SkillId == After);
        var added = Facts<ProgramEndingTurnDamageMarkerAddedEvent>(game).Single(f => f.Source.OwnerSeat == 0);
        Require(Facts<DamageAppliedEvent>(game).Where(f => f.SourceSeat == 0 && !f.SourceLess).Sum(f => f.Amount) == 3 &&
                game.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Single().Facts.TurnOwnerDamageDealtThisTurn == 2 &&
                added is { FrozenDamage: 2, CountBefore: 4, CountAfter: 6, Marker: PlayerMarkerKind.Jue } &&
                added.ParentFrameId == parentId && added.ActualTurnNumber == turn &&
                added.Source.SkillId == MarkerSkill && added.Source.BindingId == "ending" &&
                added.GameplayHash == registry.GetSkill(MarkerSkill).Program!.GameplayHash &&
                Facts<ProgramBindingStartedEvent>(game).Count(f => f.FrameId == added.FrameId && f.SkillId == MarkerSkill &&
                    f.BindingId == "ending" && f.SkillInstanceId == added.Source.SkillInstanceId && f.OwnerSeat == 0) == 1 &&
                Facts<PlayerMarkerChangedEvent>(game).Count(f => f.ResolutionId == added.FrameId && f.PlayerSeat == 0 &&
                    f.Marker == PlayerMarkerKind.Jue && f.Delta == 2 && f.Count == 6 && f.SkillOwnerSeat == 0) == 1,
            "A genuine earlier ending child adds further damage but the original exact source adds only its frozen two markers once.");
        Require(Enumerable.Range(0, 4).All(viewer => game.CreateSnapshot(viewer).Players[0].Markers!
                    .Single(m => m.Kind == PlayerMarkerKind.Jue).Count == 6),
            "The scalar result is public in every proper player projection.");
        RejectWrongActor(game);
        game = Cold(game, registry);
        Continue(game);
        ReachPlay(game);
        Require(Marker(game, 0) == 6 && Enumerable.Range(1, 3).All(seat => Marker(game, seat) == 4) &&
                Facts<ProgramEndingTurnDamageMarkerAddedEvent>(game).Count(f => f.Source.OwnerSeat != 0) == 3 &&
                Facts<ProgramEndingTurnDamageMarkerAddedEvent>(game).Where(f => f.Source.OwnerSeat != 0)
                    .All(f => f.FrozenDamage == 0 && f.CountBefore == 4 && f.CountAfter == 4) &&
                Facts<ProgramEndingTurnDamageMarkerAddedEvent>(game).Count(f => f.ParentFrameId == parentId) == 1,
            "Native AI ending boundaries do not borrow another turn's damage and cold return cannot add the first ending amount twice.");
        game = Cold(game, registry);

        Accept(game, new EndPlayPhaseCommand(0, game.Revision, P(game)!.PromptId));
        Reach(game, g => Facts<ProgramEndingTurnDamageMarkerAddedEvent>(g).Count(f => f.Source.OwnerSeat == 0) == 2);
        var zero = Facts<ProgramEndingTurnDamageMarkerAddedEvent>(game).Last(f => f.Source.OwnerSeat == 0);
        Require(zero is { FrozenDamage: 0, CountBefore: 6, CountAfter: 6 } && zero.ActualTurnNumber > turn &&
                zero.ParentFrameId != parentId && Marker(game, 0) == 6 &&
                !Facts<PlayerMarkerChangedEvent>(game).Any(f => f.ResolutionId == zero.FrameId),
            "A real later zero-damage ending settles exactly once without a zero mutation or a borrowed earlier-turn amount.");
        _ = Cold(game, registry);
    }

    private static int Marker(GameEngine g, int seat) => g.CreateSnapshot(0).Players[seat].Markers?
        .Where(marker => marker.Kind == PlayerMarkerKind.Jue).Sum(marker => marker.Count) ?? 0;
    private static PendingDecision? P(GameEngine g) => g.PendingDecision;
    private static T[] Facts<T>(GameEngine g) => g.Events.Select(item => item.Payload).OfType<T>().ToArray();
    private static void Use(GameEngine g, string activation, int[]? targets = null) => Accept(g,
        new UseProgramSkillCommand(0, Driver, activation, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Continue(GameEngine g)
    {
        var prompt = P(g)!;
        var choice = prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Accept(g, new AnswerPromptCommand(0, prompt.PromptId, choice.Id, g.Revision));
    }
    private static void ReachPlay(GameEngine g) => Reach(g, current => P(current) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Reach(GameEngine g, Func<GameEngine, bool> expected)
    {
        for (var step = 0; step < 240; step++)
        {
            if (expected(g)) return;
            if (P(g) is { PlayerSeat: 0 } prompt)
                throw new InvalidOperationException($"Unexpected ending-marker boundary: {prompt.Kind} {prompt.SkillPrompt?.SkillId}: {prompt.Prompt}");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed ending-marker fixture did not reach its bounded actual boundary.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "The actual serialized command was rejected.");
    }
    private static void RejectWrongActor(GameEngine g)
    {
        var prompt = P(g)!; var before = State(g);
        Require(!g.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted && State(g) == before,
            "A foreign actor cannot mutate the exact ending child, frozen amount, markers or accepted prefix.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(g.CreateSnapshot(viewer))).ToArray(),
        Frames = g.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())).ToArray(),
        g.CardMovements, Events = g.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(g) == State(restored), "Cold accepted-prefix replay preserves the exact ending parent, frozen facts, scalar receipts and markers.");
        Require(Enumerable.Range(0, 4).All(viewer => restored.CreateSnapshot(viewer).Players
                .Where(player => player.Seat != viewer).All(player => player.Hand.Count == 0)),
            "Public ending markers never expose another participant's physical hand.");
        return restored;
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void RejectUnsupportedContracts()
    {
        var rules = JsonNode.Parse($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{MarkerSkill}}}","revision":1,
             "triggers":[{"id":"ending","window":"turnEnding","subject":"owner","optional":false,
              "effects":[{"op":"addEndingTurnDamageMarker","target":"owner","marker":"jue"}]}]}]}
            """)!;
        var presentation = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = new Dictionary<string, object> { [MarkerSkill] = new { name = "真实结束伤害", description = "冻结真实伤害后增加爵" } } });
        _ = SkillProgramCatalog.Load(rules.ToJsonString(), presentation);
        foreach (var mutation in new Action<JsonObject>[]
        {
            trigger => trigger["window"] = "playEnding",
            trigger => trigger["subject"] = "any",
            trigger => trigger["optional"] = true,
            trigger => trigger["turnOwnerScope"] = "otherLiving",
            trigger => { trigger["usageScope"] = "turn"; trigger["usageLimit"] = 1; },
            trigger => trigger["effects"]!.AsArray().Add(JsonNode.Parse("{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}")),
            trigger => trigger["effects"]![0]!["amount"] = 2,
            trigger => trigger["effects"]![0]!["target"] = "selectedTarget",
            trigger => trigger["condition"] = JsonNode.Parse("{\"kind\":\"compare\",\"left\":{\"kind\":\"turnOwnerDamageDealtThisTurn\"},\"operator\":\"greaterThan\",\"right\":{\"kind\":\"integerConstant\",\"value\":0}}")
        })
        {
            var invalid = JsonNode.Parse(rules.ToJsonString())!;
            mutation(invalid["skills"]![0]!["triggers"]![0]!.AsObject());
            try { _ = SkillProgramCatalog.Load(invalid.ToJsonString(), presentation); }
            catch (InvalidOperationException) { continue; }
            throw new InvalidOperationException("An optional, foreign, costed, composite or caller-sized ending damage marker must fail strict loading.");
        }
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-ending-damage-marker", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                 {"id":"{{{MarkerSkill}}}","revision":1,"triggers":[
                  {"id":"initial","window":"gameStarting","subject":"owner","optional":false,"effects":[{"op":"changeParticipantMarker","target":"owner","marker":"jue","amount":4}]},
                  {"id":"ending","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"addEndingTurnDamageMarker","target":"owner","marker":"jue"}]}]},
                 {"id":"{{{Driver}}}","revision":1,"activations":[
                  {"id":"outgoing","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"damage","target":"selectedTarget","amount":2}]},
                  {"id":"hp-loss","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                  {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":1,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                  {"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":1,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]},
                 {"id":"{{{Before}}}","revision":1,"triggers":[{"id":"before","window":"turnEnding","subject":"owner","priority":10,"optional":false,"usageScope":"game","usageLimit":1,
                  "effects":[{"op":"chooseOption","target":"owner","resultBind":"before","options":[{"id":"continue"}]},{"op":"damage","target":"owner","amount":1}]}]},
                 {"id":"{{{After}}}","revision":1,"triggers":[{"id":"after","window":"turnEnding","subject":"owner","priority":-10,"optional":false,"usageScope":"game","usageLimit":1,
                  "effects":[{"op":"chooseOption","target":"owner","resultBind":"after","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new[] { MarkerSkill, Driver, Before, After }.ToDictionary(id => id, id =>
                    {
                        var fields = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实结束伤害和冻结父窗" };
                        if (id is Before or After) fields["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                        return fields;
                    })
                }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "真实结束伤害") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "固定真实标记使用者", "supporter", MarkerSkill, "jin", 8, [Driver, Before, After]));
            var peers = Enumerable.Range(1, 3).Select(index => "fixture:ending-damage-peer-" + index).ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "固定无主动动作目标", "supporter", MarkerSkill, "wei", 8));
            const string deck = "fixture:ending-damage-deck";
            builder.AddDeck(new(deck, "固定闪实体", 1, 0, [])
            { PhysicalCards = Enumerable.Range(0, 16).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(Mode, "正式真实结束伤害小夹具", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, deck,
                GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
