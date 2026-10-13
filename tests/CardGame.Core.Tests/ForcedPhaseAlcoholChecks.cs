using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ForcedPhaseAlcoholChecks
{
    private const string Wine = "ol:zhendu", DeathDraw = "ol:qiluan", Driver = "fixture:phase-wine-driver";
    private const string Pause = "fixture:phase-wine-pause", Loss = "fixture:phase-wine-loss", Replacement = "fixture:phase-wine-replacement";
    private const string Mode = "identity:classic-phase-wine";

    public static void ContractsRequireRealPaymentAndPositiveFrozenDeaths()
    {
        Require((int)SkillProgramNumberExpression.LivingFactionCount == 0 &&
            (int)SkillProgramNumberExpression.EventTargetHp == 10,
            "Adding an explicit expression must preserve the legacy implicit numeric range for serialized programs and frames.");
        _ = Catalog();
        var legacy = JsonSerializer.Serialize(new SkillProgramTriggerFacts(0, 3, true));
        Require(!legacy.Contains("ActualTurnDeathCount", StringComparison.Ordinal) && !legacy.Contains("OwnerActualTurnKillCount", StringComparison.Ordinal),
            "New optional death facts must be absent from legacy serialized candidates.");
        var condition = Catalog().Programs[DeathDraw].Triggers[0].Condition;
        Require(!condition.Evaluate(new(0, 3, true) { ActualTurnDeathCount = 0 }) &&
            condition.Evaluate(new(0, 3, true) { ActualTurnDeathCount = 2 }), "Only positive frozen actual-turn deaths qualify.");
        Reject(() => condition.Evaluate(new(0, 3, true)));
        void Invalid(Action<JsonNode> edit)
        { var node = JsonNode.Parse(Resource("rules"))!; edit(node); Reject(() => SkillProgramCatalog.Load(node.ToJsonString(), Resource("presentation"))); }
        Invalid(n => n["skills"]![0]!["triggers"]![0]!["effects"]![0]!["zones"] = new JsonArray("equipment"));
        Invalid(n => n["skills"]![0]!["triggers"]![0]!["effects"]![1]!["awaitMovementTriggers"] = false);
        Invalid(n => n["skills"]![0]!["triggers"]![0]!["effects"]![0]!["maximumCards"] = 2);
        Invalid(n => n["skills"]![0]!["triggers"]![0]!["window"] = "turnEnding");
        Invalid(n => n["skills"]![1]!["triggers"]![0]!["condition"] = new JsonObject { ["kind"] = "always" });
        Invalid(n => n["skills"]![1]!["triggers"]![0]!["window"] = "turnEnding");
    }

    public static void RealActorWineCommitQuotaAndColdReturns()
    {
        foreach (var actor in new[] { 0, 1 })
        {
            var (game, registry) = Create(pause: true);
            if (actor == 1) { Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Reach(game, IsPlay); End(game); Reach(game, p => Offer(p, Wine) && game.State.CurrentSeat == 1); }
            var hp = game.State.Players[actor].Hp;
            Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(game, Selection);
            var selection = Prompt(game)!; var card = selection.Choices.First(c => c.Cards.Count == 1).Cards[0];
            for (var seat = 1; seat < 4; seat++) Require(game.CreateSnapshot(seat).PendingDecision is null, "Other players cannot inspect the owner's private payment candidates.");
            var checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint());
            Require(!game.Submit(new AnswerPromptCommand(1, selection.PromptId, selection.Choices[0].Id, game.Revision)).Accepted &&
                checkpoint == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "Wrong actor rejects before payment.");
            game = Cold(game, registry); Answer(game, c => c.Cards.SequenceEqual([card])); Reach(game, p => p.SkillPrompt?.SkillId == Pause);
            var use = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Alcohol);
            Require(use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } && use.SourceSeat == actor &&
                use.Action is { Type: CardActionType.Use, EffectiveKind: CardKind.Alcohol, ProviderSeat: 0 } action &&
                action.ActorSeat == actor && action.TargetSeats.SequenceEqual([actor]) && action.PhysicalCards.Count == 0,
                "Phase Wine uses the actual phase actor, owner provider, exact self-target and zero entities.");
            Require(Players(game)[actor].UsedPlayPhaseAlcoholThisTurn && !Players(game)[actor].HasAlcoholEffect,
                "Wine quota is spent at commitment before the paused effect.");
            var useId = use.Id; game = Cold(game, registry);
            Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(game, p => actor == 0 ? IsPlay(p) : IsPlay(p) || Offer(p, Wine));
            Require(Facts<CardUseDeclaredEvent>(game).Count(e => e.ResolutionId == useId && e.CardKind == CardKind.Alcohol && e.SourceSeat == actor) == 1 &&
                Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId && e.CardKind == CardKind.Alcohol) == 1 &&
                game.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == "skill-program.ol:zhendu.MoveBoundCards") == 1,
                "One real Wine declaration, one finish and one exact hand payment survive cold returns.");
            Require(Facts<AlcoholAppliedEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
                Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == actor && e.Amount == 1) == (actor == 0 ? 0 : 1),
                "Self Wine has no damage; a living foreign Wine actor takes the independent skill damage.");
            if (actor == 0) Require(game.State.Players[0].Hp == hp && !game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Alcohol),
                "A committed self Wine cannot return a second Wine action.");
            Cold(game, registry);
        }
        var (deadActor, deadActorRegistry) = Create(actorDeathWine: true);
        Answer(deadActor, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Reach(deadActor, IsPlay); End(deadActor);
        Reach(deadActor, p => Offer(p, Wine) && deadActor.State.CurrentSeat == 1);
        Answer(deadActor, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(deadActor, Selection);
        Answer(deadActor, c => c.Cards.Count == 1);
        Reach(deadActor, p => Offer(p, Wine) && deadActor.State.CurrentSeat == 2);
        var issued = Facts<CardUseDeclaredEvent>(deadActor).Single(e => e.CardKind == CardKind.Alcohol && e.SourceSeat == 1);
        Require(!deadActor.State.Players[1].IsAlive && deadActor.State.Winner == Winner.None &&
            Facts<CardUseFinishedEvent>(deadActor).Count(e => e.ResolutionId == issued.ResolutionId) == 1 &&
            !Facts<AlcoholAppliedEvent>(deadActor).Any(e => e.ResolutionId == issued.ResolutionId) &&
            !Facts<DamageAppliedEvent>(deadActor).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1),
            "A real committed-Wine child can kill its actual actor; the paid use finishes without Wine benefit or damage to a dead actor.");
        Cold(deadActor, deadActorRegistry);
    }

    public static void ForbiddenWineIsUnpaidAndPaidSourceLossCompletes()
    {
        var (blocked, blockedRegistry) = Create(ban: true, stopAtOffer: false);
        Require(IsPlay(Prompt(blocked)!) && !Facts<CardUseDeclaredEvent>(blocked).Any(e => e.CardKind == CardKind.Alcohol) &&
            !blocked.CardMovements.Any(m => m.Reason.Value == "skill-program.ol:zhendu.MoveBoundCards"),
            "A forbidden actual Wine offers no skill and pays no material.");
        Cold(blocked, blockedRegistry);
        var (game, registry) = Create(sourceLoss: true, pause: true);
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Reach(game, IsPlay); End(game);
        Reach(game, p => Offer(p, Wine) && game.State.CurrentSeat == 1);
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(game, Selection);
        var card = Prompt(game)!.Choices.First(c => c.Cards.Count == 1).Cards[0];
        Answer(game, c => c.Cards.SequenceEqual([card])); Reach(game, p => p.SkillPrompt?.SkillId == Pause);
        Require(!Players(game)[0].SkillGrants.Grants.Any(g => g.SkillId == Wine) && game.State.Players[0].Hp == 2 &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Wine && f.PhaseAlcohol is { Stage: ProgramPhaseAlcoholStage.WineIssued }),
            "Actual discard → dying recovery and skill replacement removes the issuing source before its paid Wine child. " +
            JsonSerializer.Serialize(new { Hp = game.State.Players[0].Hp, Skills = Players(game)[0].SkillGrants.Grants.Select(g => g.SkillId).ToArray(),
                Receipts = game.ResolutionStack.OfType<ProgramSkillFrame>().Select(f => new { f.SkillId, f.InstructionIndex, f.PhaseAlcohol }).ToArray(),
                Lost = Facts<ProgramOwnerSkillsReplacedEvent>(game).ToArray(), Bindings = Facts<ProgramBindingStartedEvent>(game).Where(e => e.SkillId == Loss).ToArray() }));
        game = Cold(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, IsPlay);
        Require(Facts<CardUseDeclaredEvent>(game).Count(e => e.CardKind == CardKind.Alcohol && e.SourceSeat == 1) == 1 &&
            Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            game.CardMovements.Count(m => m.CardId == card && m.Reason.Value == "skill-program.ol:zhendu.MoveBoundCards") == 1 &&
            !Prompt(game)!.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Wine),
            "The issued paid Wine/damage tail completes once after actual source loss, with no new source activation.");
        Cold(game, registry);
    }

    public static void DeathDrawFreezesMixedKillCountsAndDeadTurnOwner()
    {
        var (none, noneRegistry) = Create(stopAtOffer: false);
        End(none); Reach(none, p => IsPlay(p) || Offer(p, Wine));
        Require(!Facts<ProgramBindingStartedEvent>(none).Any(e => e.SkillId == DeathDraw), "No death means no Qiluan offer.");
        Cold(none, noneRegistry);
        var (ai, aiRegistry) = Create(stopAtOffer: false, aiDeathDraw: true);
        Use(ai, "loss", 2); End(ai); Reach(ai, p => Offer(p, DeathDraw));
        Answer(ai, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(ai, p => Offer(p, Wine));
        Require(Facts<ProgramBindingResolvedEvent>(ai).Any(e => e.SkillId == DeathDraw && e.OwnerSeat == 1 && e.Activated && e.Completed),
            "Native AI accepts its positive public frozen death reward instead of pricing the new expression at zero.");
        Cold(ai, aiRegistry);
        var (mixed, mixedRegistry) = Create(stopAtOffer: false);
        Use(mixed, "damage", 1); Use(mixed, "loss", 2); End(mixed); Reach(mixed, p => Offer(p, DeathDraw));
        var parent = mixed.ResolutionStack.OfType<DeferredTurnEndFrame>().Single();
        var facts = parent.AfterTurnEnded!.Items[parent.AfterTurnEnded.ItemIndex].Facts!;
        Require(facts.ActualTurnDeathCount == 2 && facts.OwnerActualTurnKillCount == 1, "Frozen actual-turn deaths distinguish one owner kill from one source-less death.");
        var before = mixed.State.Players[0].Hand.Count; mixed = Cold(mixed, mixedRegistry);
        Answer(mixed, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(mixed, p => IsPlay(p) || Offer(p, Wine));
        Require(mixed.State.Players[0].Hand.Count == before + 4 &&
            Facts<ProgramBindingResolvedEvent>(mixed).Count(e => e.SkillId == DeathDraw && e.Activated && e.Completed) == 1,
            "Two deaths plus two additional owner-kill cards draw four exactly once after actual TurnEnded.");
        Cold(mixed, mixedRegistry);
        var (dead, deadRegistry) = Create(deadTurnOwner: true, stopAtOffer: false);
        End(dead); Reach(dead, p => Offer(p, DeathDraw));
        Require(dead.State.CurrentSeat == 1 && !dead.State.Players[1].IsAlive && dead.State.Winner == Winner.None &&
            dead.ResolutionStack.OfType<DeferredTurnEndFrame>().Single().AfterTurnEnded!.Items.Any(i =>
                i.Candidate?.SkillId == DeathDraw && i.Facts?.ActualTurnDeathCount == 1 && i.Facts.OwnerActualTurnKillCount == 0),
            "A dead actual-turn actor still opens the surviving owner's positive after-turn death benefit.");
        var hand = dead.State.Players[0].Hand.Count; dead = Cold(dead, deadRegistry);
        Answer(dead, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(dead, p => IsPlay(p) || Offer(p, DeathDraw));
        Require(dead.State.Players[0].Hand.Count >= hand + 1 &&
            Facts<ProgramBindingResolvedEvent>(dead).Count(e => e.SkillId == DeathDraw && e.Activated && e.Completed) == 1,
            "The dead turn actor's one source-less death yields one successful surviving-owner reward.");
        Cold(dead, deadRegistry);
    }

    private static (GameEngine, ContentRegistry) Create(bool ban = false, bool sourceLoss = false,
        bool pause = false, bool deadTurnOwner = false, bool stopAtOffer = true, bool aiDeathDraw = false, bool actorDeathWine = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(ban, sourceLoss, pause, deadTurnOwner, aiDeathDraw, actorDeathWine));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:phase-wine-owner", game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => stopAtOffer ? Offer(p, Wine) : IsPlay(p)); return (game, registry);
    }
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s, false).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(PendingDecision p) => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0;
    private static bool Selection(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards");
    private static bool Offer(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static IReadOnlyList<CharacterState> Players(GameEngine g) => (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(g)!;
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match) { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, Prompt(g)!.PromptId));
    private static void Use(GameEngine g, string name, int target)
    { Accept(g, new UseProgramSkillCommand(0, Driver, name, [], [target], g.Revision, Prompt(g)!.PromptId)); Reach(g, IsPlay); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> match)
    {
        for (var i = 0; i < 180; i++)
        {
            var p = Prompt(g); if (p is not null && match(p)) return;
            if (p is { PlayerSeat: 0 })
            {
                if (IsPlay(p)) End(g);
                else if (p.Kind == DecisionKind.DiscardCards) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
                else
                {
                    var c = p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("program-action") == "skip") ??
                        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("option-id") == "continue") ??
                        p.Choices.FirstOrDefault(c => c.Cards.Count == 0 && c.Targets.Count == 0) ??
                        throw new InvalidOperationException("Unexpected phase Wine fixture prompt: " + JsonSerializer.Serialize(p));
                    Accept(g, new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision));
                }
            }
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Phase Wine fixture exceeded bounded prompt steps: " + JsonSerializer.Serialize(Prompt(g)));
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(r.Accepted, r.Error?.Message ?? "Rejected fixture command."); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s, false)) == SnapshotJson.Serialize(replay.CreateSnapshot(s, false))) &&
            JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(replay.ResolutionStack) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(replay.CardMovements) &&
            g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(replay.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))) &&
            g.ObserverFailures.Count == 0 && g.CreateCardZoneDiagnostics().Count == 80,
            "Private views, frozen deaths, exact payment, typed child frames and committed events must cold replay identically."); return replay;
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidOperationException) { return; } throw new InvalidOperationException("An invalid forced phase Wine/death contract was accepted."); }
    private static string Resource(string kind)
    { var asm = typeof(StandardContentPackage).Assembly; using var r = new StreamReader(asm.GetManifestResourceStream(asm.GetManifestResourceNames().Single(n => n.EndsWith("ol-he-tai-hou." + kind + ".json", StringComparison.Ordinal)))!); return r.ReadToEnd(); }
    private static SkillProgramCatalog Catalog() => SkillProgramCatalog.Load(Resource("rules"), Resource("presentation"));

    private sealed class Fixture(bool ban, bool sourceLoss, bool pause, bool deadTurnOwner, bool aiDeathDraw, bool actorDeathWine) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-phase-wine", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var he = Catalog(); foreach (var id in new[] { Wine, DeathDraw }) b.AddSkill(new(id, he.Presentations[id].Name, he.Presentations[id].Description) { Program = he.Programs[id], ProgramPresentation = he.Presentations[id] });
            void Add(string id, string body)
            {
                var c = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{id}}","revision":1,{{body}}}]}""",
                    JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object>
                    { [id] = new { name = id, description = id, optionLabels = id == Pause ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() } } }));
                b.AddSkill(new(id, id, id) { Program = c.Programs[id] });
            }
            Add(Driver, """
            "activations":[{"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
             "effects":[{"op":"damageParticipants","target":"selectedTarget","amount":30}]},
             {"id":"loss","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
             "effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]}]
            """);
            if (ban) Add("fixture:phase-wine-ban", """
            "triggers":[{"id":"wine-use-ban","window":"playPhaseStarting","subject":"owner","optional":false,"priority":100,
            "effects":[{"op":"grantTurnCardActionProhibition","target":"owner","cardKinds":["alcohol"],"actionTypes":["use"]}]}]
            """);
            if (pause) Add(Pause, """
            "triggers":[{"id":"pause-wine","window":"cardUseCommitted","ownerRelation":"observer","cardKinds":["alcohol"],"optional":false,
            "effects":[{"op":"chooseOption","target":"owner","resultBind":"resume","options":[{"id":"continue"}]}]}]
            """);
            if (sourceLoss)
            {
                Add(Loss, """
                "triggers":[{"id":"paid-discard-causes-dying","window":"cardsMoved","subject":"owner","optional":false,
                "sourceZones":["hand"],"movementOccurrence":"perBatch","movementDiscardOnly":true,
                "movementReasons":["skill-program.ol:zhendu.MoveBoundCards"],"effects":[{"op":"loseHp","target":"owner","amount":20},{"op":"loseHp","target":"owner","amount":1}]}]
                """);
                Add(Replacement, """
                "triggers":[{"id":"actual-dying-source-replacement","window":"dyingEntering","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,
                "effects":[{"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":2,"clampToMaxHp":true},
                {"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["ol:zhendu"],"sourceBind":"fixture:phase-wine-spent"}]}]
                """);
                b.AddSkill(new("fixture:phase-wine-spent", "已失去鸩毒", "付款子结算中的实际觉醒移除原技能。"));
            }
            if (deadTurnOwner) Add("fixture:phase-wine-actor-death", """
            "triggers":[{"id":"die-before-play","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
            "effects":[{"op":"loseHp","target":"owner","amount":20}]}]
            """);
            if (actorDeathWine) Add("fixture:phase-wine-committed-death", """
            "triggers":[{"id":"actual-actor-death-after-commit","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["alcohol"],"optional":false,
            "effects":[{"op":"loseHp","target":"owner","amount":20}]}]
            """);
            var common = new List<string>(); if (ban) common.Add("fixture:phase-wine-ban");
            b.AddGeneral(new("fixture:phase-wine-owner", "鸩毒机制", "supporter", Wine, "qun", 20,
                [DeathDraw, Driver, .. common, .. (pause ? new[] { Pause } : []), .. (sourceLoss ? new[] { Loss, Replacement } : [])]));
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:phase-wine-other-{seat}", "固定阶段角色", "supporter", "standard:none", "qun", 20,
                [.. common, .. (deadTurnOwner ? new[] { "fixture:phase-wine-actor-death" } : []), .. (aiDeathDraw ? new[] { DeathDraw } : []),
                    .. (actorDeathWine ? new[] { "fixture:phase-wine-committed-death" } : [])]));
            b.AddDeck(new("fixture:phase-wine-deck", "不主动使用的手牌", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "鸩毒及实际回合死亡机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:phase-wine-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:phase-wine-owner", "fixture:phase-wine-other-1", "fixture:phase-wine-other-2", "fixture:phase-wine-other-3"]));
        }
    }
}
