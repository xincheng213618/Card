using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryLiaoHuaChecks
{
    private const string Dangxian = "boundary:dangxian", Fuli = "boundary:fuli";
    private const string Mode = "identity:classic-lh-fixture", Driver = "fixture:lh-driver";
    private const string Gain = "fixture:lh-gain", Hp = "fixture:lh-hp", Face = "fixture:lh-face", UsePause = "fixture:lh-use-pause";
    private const string ClaimReason = "program.granted-phase-slash.claim", FuliDrawReason = "program.frozen-faction-recovery.draw";

    public static void ExtraPhaseEntityDistanceAndClaimChildrenAreExact()
    {
        var (g, r) = Create(initialHp: 2, pauseDistanceUse: true);
        var original = g.CreateSnapshot(0).Players[0].Hand.Select(c => c.Id).ToArray();
        var prompt = P(g)!;
        Require(prompt.Choices.Single(c => c.Parameters.GetValueOrDefault("option") == "deck").Cards.Count == 0 &&
                prompt.ValidCardIds.Count == 0 && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("option") == "skip"),
            "A hidden deck offers a source and a real refusal, never hidden entity IDs.");
        Private(g); Cold(g, r); Reject(g);
        ChooseSource(g, "deck");
        Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var claim = Facts<GrantedPhaseSlashClaimedEvent>(g).Single();
        var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.GrantedPhaseSlashClaim is not null);
        Require(root.GrantedPhaseSlashClaim is { Paid: true } cost &&
                cost.CardId == claim.CardId && cost.ClaimMovementSequence == claim.MovementSequence &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(f => f.ResumeProgramFrameId == root.Id) &&
                g.CardMovements.Count(m => m.CardId == claim.CardId && m.Reason.Value == ClaimReason) == 1,
            "The actual entity payment remains on its starting producer while the gain child is paused.");
        Require(!g.CardMovements.Any(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw),
            "This is the forced extra Play before the actual normal Draw.");
        Private(g); Cold(g, r); Reject(g); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        Require(Facts<GrantedPhaseSlashClaimedEvent>(g).Count() == 1 &&
                !Facts<GrantedEntityPhaseEndedEvent>(g).Any(),
            "The gain-owned actual Recovery child settles before the freely interactive extra phase.");
        Cold(g, r); Continue(g); Play(g);
        Require(FarSlash(g, claim.CardId) && original.All(id => !FarSlash(g, id)),
            "Only the exact obtained entity bypasses distance; the original hand has no blanket range grant.");
        Accept(g, new PlayCardCommand(0, claim.CardId, [2], g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == UsePause);
        Cold(g, r); Reject(g); AuditGrantedDistancePolicy(g, r);
        Continue(g); Play(g);
        var issued = Facts<GrantedEntityDistanceUseIssuedEvent>(g).Single().Policy;
        Require(issued.CardId == claim.CardId && issued.PhaseInstanceId == claim.PhaseInstanceId &&
                issued.PhaseProducerFrameId == claim.PhaseProducerFrameId &&
                Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 2 && e.Amount == 1) &&
                !g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "The genuine distance-two Slash uses the exact issued receipt and still consumes its normal phase quota.");
        Cold(g, r); End(g); Play(g);
        Require(Facts<GrantedEntityPhaseEndedEvent>(g).Single().DamageDealt == 1 &&
                !Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 0) &&
                Facts<PhaseChangedEvent>(g).Count(e => e.ActorSeat == 0 && e.Phase == TurnPhase.Play) == 2,
            "Actual extra-phase damage cancels its self-damage debt and resumes a separate ordinary Play.");
        Cold(g, r);
    }

    public static void ExtraPhaseSkipAndUnusedClaimPaySelfDamageOnce()
    {
        foreach (var take in new[] { false, true })
        {
            var (g, r) = Create(initialHp: 3);
            ChooseSource(g, take ? "deck" : "skip");
            DrainToPlay(g);
            var hp = g.State.Players[0].Hp;
            End(g); Reach(g, p => p.SkillPrompt?.SkillId == Hp);
            var phase = Facts<GrantedEntityPhaseEndedEvent>(g).Single();
            var trailer = g.ResolutionStack.OfType<ProgramSkillFrame>().SingleOrDefault(f => f.IssuedEntityPhase is not null)
                ?? throw new InvalidOperationException("The paid extra-phase trailer is missing: " +
                    JsonSerializer.Serialize(new { take, g.ResolutionStack,
                        Events = g.Events.TakeLast(12).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray() }));
            Require(phase.DamageDealt == 0 && trailer.IssuedEntityPhase is { Stage: GrantedEntityPhaseStage.DamagePaid } &&
                    g.State.Players[0].Hp == hp - 1 &&
                    Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0 && e.Amount == 1 && !e.SourceLess) == 1 &&
                    !Facts<ProgramSkillHpLostEvent>(g).Any(),
                "Taking or declining the Slash pays one real sourced self-damage through the issued phase trailer.");
            Cold(g, r); Reject(g); Continue(g); Play(g);
            Require(Facts<GrantedEntityPhaseEndedEvent>(g).Count() == 1 &&
                    Facts<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == 0) == 1 &&
                    Facts<GrantedPhaseSlashClaimedEvent>(g).Count() == (take ? 1 : 0) &&
                    g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw) == 2,
                "Returning the paid damage/HP child neither repeats cost nor skips the actual normal Draw.");
            if (take)
                Require(!FarSlash(g, Facts<GrantedPhaseSlashClaimedEvent>(g).Single().CardId),
                    "A still-held obtained Slash loses its distance grant at this exact extra phase end.");
            Cold(g, r);
        }
    }

    public static void FuliFrozenFactionsDamagePointsAndBenefitChildren()
    {
        foreach (var sufficientDamage in new[] { false, true })
        {
            var (g, r) = Create(baseHp: sufficientDamage ? 4 : 1, initialHp: sufficientDamage ? 4 : 1);
            ChooseSource(g, "skip"); Play(g);
            if (sufficientDamage) Use(g, "dealt-four", targets: [1]);
            Play(g); End(g); DrainToPlay(g);
            if (sufficientDamage)
            {
                // Keep four genuinely applied points from the preceding turn.
                // The next extra phase adds one real self-damage point: neither
                // a current-turn counter nor an occurrence count can substitute.
                Cold(g, r); End(g);
                Reach(g, p => p.SkillPrompt?.SkillId == Dangxian && p.Choices.Any(c =>
                    c.Parameters.GetValueOrDefault("program-action") == "granted-phase-slash"));
                ChooseSource(g, "skip"); Play(g); End(g); DrainToPlay(g);
            }
            Require(Facts<DamageAppliedEvent>(g).Where(e => e.SourceSeat == 0 && !e.SourceLess).Sum(e => e.Amount) ==
                    (sufficientDamage ? 5 : 1),
                "The threshold history counts actual applied points, including the phase's real self-damage.");
            Use(g, sufficientDamage ? "fatal-incoming" : "empty-and-hurt", targets: [1]);
            Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == Fuli));
            Require(g.State.Players[0].Hp == 0 && g.ResolutionStack.OfType<DyingFrame>().Any(f => f.VictimSeat == 0),
                "Fuli is offered by a genuine self-dying damage producer after the actual hand payment.");
            Cold(g, r); Reject(g);
            Answer(g, c => c.Parameters.GetValueOrDefault("skill-id") == Fuli);
            Reach(g, p => p.SkillPrompt?.SkillId == Hp);
            var captured = Facts<FrozenFactionRecoveryCapturedEvent>(g).Single();
            var root = FuliRoot(g);
            Require(captured.FactionCount == 4 && captured.GameDamagePoints == (sufficientDamage ? 5 : 1) &&
                    root.FrozenFactionRecovery is { DrawIssued: false, FaceIssued: false } &&
                    g.State.Players[0].Hp == Math.Min(4, g.State.Players[0].MaxHp) &&
                    g.State.Players[0].HandCount == 0,
                "Activation freezes live factions and full-game owner damage, clamps only HP, and waits for Recovery children.");
            Cold(g, r); Reject(g); Continue(g);
            Reach(g, p => p.SkillPrompt?.SkillId == Gain);
            Require(FuliRoot(g).FrozenFactionRecovery is { DrawIssued: true, FaceIssued: false } &&
                    g.State.Players[0].HandCount == 4 &&
                    Facts<FrozenFactionHandDrawIssuedEvent>(g).Single().DrawCount == 4 &&
                    g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == FuliDrawReason) == 4,
                "The original frozen X draws four actual entities once, including when MaxHp is only two.");
            Private(g); Cold(g, r); Reject(g);
            if (!sufficientDamage)
            {
                Reach(g, p => p.SkillPrompt?.SkillId == Face);
                Require(g.State.Players[0].IsFaceDown && FuliRoot(g).FrozenFactionRecovery is { FaceIssued: true },
                    "Only after every actual gain child returns does X greater than frozen damage issue a real flip.");
                Cold(g, r); Reject(g); Continue(g);
            }
            DrainToPlay(g);
            Require(g.State.Players[0].IsAlive && g.State.Players[0].IsFaceDown == !sufficientDamage &&
                    Facts<FrozenFactionRecoveryCapturedEvent>(g).Count() == 1 &&
                    Facts<FrozenFactionHandDrawIssuedEvent>(g).Count() == 1 &&
                    Facts<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == Fuli && e.Scope == SkillUsageScope.Game) == 1 &&
                    Facts<CharacterStateChangedEvent>(g).Count(e => e.Change.TargetSeat == 0 &&
                        e.Change.Window == SkillProgramTriggerWindow.CharacterTurnedOver) == (sufficientDamage ? 0 : 1),
                "The threshold uses damage points rather than occurrences; paid recovery/draw/flip and the game quota are each issued once.");
            Cold(g, r);
        }
    }

    public static void PublicDiscardChoiceLeavesAndNativePhaseHistory()
    {
        var (g, r) = Create(publicDiscard: true, initialHp: 3);
        var choice = P(g)!.Choices.Single(c => c.Cards.Count == 1);
        var id = choice.Cards.Single();
        Require(g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile) &&
                g.CreateCardZoneDiagnostics().Any(z => z.Location == CardLocation.DiscardPile && z.CardId == id),
            "The discard source is an actual previously paid public entity, not an injected zone.");
        Private(g); Cold(g, r); Answer(g, c => c.Id == choice.Id); DrainToPlay(g);
        Require(Facts<GrantedPhaseSlashClaimedEvent>(g).Single().CardId == id && FarSlash(g, id),
            "The explicit public discard choice grants only its actual obtained entity.");
        Use(g, "give-and-regain", cards: [id], targets: [1]);
        Reach(g, p => p.ValidCardIds.Contains(id) && p.SkillPrompt?.SkillId == Driver);
        Private(g); Cold(g, r); Answer(g, c => c.Cards.SequenceEqual([id])); DrainToPlay(g);
        Require(g.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == id) &&
                g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(0) && m.To == CardLocation.Hand(1)) &&
                g.CardMovements.Any(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0)) &&
                !FarSlash(g, id),
            "An actual departure and subsequent actual return cannot revive the phase's single entity grant.");
        Cold(g, r); End(g); DrainToPlay(g); Require(!FarSlash(g, id), "A returned entity remains ordinary after the extra phase ends."); Cold(g, r);

        var (ai, ar) = Create(native: true);
        Until(ai, () => ai.State.Status == EngineStatus.Completed);
        var source = ai.State.Players.Single(p => p.GeneralId == "fixture:lh-owner").Seat;
        var started = Facts<GrantedEntityPhaseStartedEvent>(ai).Single();
        var ended = Facts<GrantedEntityPhaseEndedEvent>(ai).Single();
        var claim = Facts<GrantedPhaseSlashClaimedEvent>(ai).Single();
        Require(started.Source.OwnerSeat == source && started.PhaseInstanceId == ended.PhaseInstanceId &&
                ended.Source == started.Source && ended.DamageDealt > 0 && claim.Source == started.Source &&
                ai.CardMovements.Count(m => m.CardId == claim.CardId && m.Reason.Value == ClaimReason) == 1 &&
                Facts<TurnEndedEvent>(ai).Count() == 1 && !ai.ResolutionStack.OfType<ProgramSkillFrame>().Any(f =>
                    f.GrantedPhaseSlashClaim is not null || f.IssuedEntityPhase is not null),
            "Native AI takes the real source, resolves actual attacks, and completes the exact first inserted phase/turn without lingering receipts.");
        Cold(ai, ar);
    }

    private static bool FarSlash(GameEngine g, int id) => g.GetHumanLegalActions().Any(a =>
        a.Kind == LegalActionKind.Slash && a.CardId == id && a.TargetSeats.SequenceEqual([2]));
    private static ProgramSkillFrame FuliRoot(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.FrozenFactionRecovery is not null);
    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void ChooseSource(GameEngine g, string source) => Answer(g, c => c.Parameters.GetValueOrDefault("option") == source);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void DrainToPlay(GameEngine g) => Play(g);
    private static void Use(GameEngine g, string id, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!;
        Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var n = 0; n < 180; n++) { if (P(g) is { } p && predicate(p)) return; Step(g); }
        throw new InvalidOperationException("Liao Hua exact boundary missing: " + JsonSerializer.Serialize(P(g)));
    }
    private static void Until(GameEngine g, Func<bool> predicate)
    {
        for (var n = 0; n < 220; n++) { if (predicate()) return; Step(g); }
        throw new InvalidOperationException("Liao Hua exact event/native boundary missing.");
    }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"))
            Answer(g, c => c.Cards.Count == 1);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
            Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
            Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Expected a real Liao Hua command.");
    }
    private static void Reject(GameEngine g)
    {
        var p = P(g)!; var before = State(g);
        Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("unpublished"), g.Revision)).Accepted && State(g) == before,
            "An unpublished choice cannot repeat the paid phase/entity/benefit.");
    }
    private static void Private(GameEngine g)
    {
        Require(P(g) is { IsPrivate: true, PlayerSeat: 0 }, "This real choice belongs to the owner alone.");
        for (var seat = 1; seat < 4; seat++)
            Require(g.CreateSnapshot(seat).PendingDecision is null && g.CreateSnapshot(seat).Players[0].Hand.Count == 0,
                "Other prepared views expose neither the owner's hidden choices nor hidden material IDs.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics()
    });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "Four prepared views, actual accepted commands, private owning receipts and complete movement/history cold-restore identically.");
    private static void AuditGrantedDistancePolicy(GameEngine g, ContentRegistry r)
    {
        var accepted = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.GrantedEntityDistance is not null);
        var policy = accepted.GrantedEntityDistance!;
        foreach (var bad in new[] {
            policy with { CardUseFrameId = policy.CardUseFrameId + 1 },
            policy with { PhaseProducerFrameId = policy.PhaseProducerFrameId + 1 },
            policy with { PhaseInstanceId = policy.PhaseInstanceId + 1 } })
        {
            // This is a host-only invariant audit on a replay-restored clone.
            // The actual accepted-command engine keeps its original receipt.
            var invalid = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r);
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var stack = (FrameStore)typeof(GameEngine).GetField("_resolutionStack", flags)!.GetValue(invalid)!;
            var use = stack.OfType<CardUseFrame>().Single(f => f.Id == accepted.Id);
            stack.Replace(use with { GrantedEntityDistance = bad });
            var rejected = false;
            try { typeof(GameEngine).GetMethod("AssertCoreInvariants", flags)!.Invoke(invalid, null); }
            catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is
                InvalidOperationException { Message: var message } && message.Contains("granted-entity distance", StringComparison.Ordinal))
            { rejected = true; }
            Require(rejected, "A foreign use, phase producer or actual phase scalar cannot authorize an issued entity distance policy.");
        }
        Cold(g, r);
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static (GameEngine, ContentRegistry) Create(int baseHp = 4, int initialHp = 4, bool publicDiscard = false, bool native = false, bool pauseDistanceUse = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(baseHp, initialHp, publicDiscard, native, pauseDistanceUse));
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = native ? 1 : 8
        }, r);
        Accept(g, new StartGameCommand());
        if (native) return (g, r);
        Reach(g, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, "fixture:lh-owner", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.SkillPrompt?.SkillId == Dangxian && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("program-action") == "granted-phase-slash"));
        return (g, r);
    }

    private sealed class Fixture(int baseHp, int initialHp, bool publicDiscard, bool native, bool pauseDistanceUse) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-liao-hua", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = FixtureRules.Replace("$SCHEMA$", SkillProgramCatalog.RulesSchemaVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var presentations = new Dictionary<string, object>();
            foreach (var id in new[] { Driver, Gain, Hp, Face, UsePause, "fixture:lh-seed-discard" })
                presentations[id] = id is Gain or Hp or Face or UsePause ?
                    new { name = id, description = "真实拥有帧边界", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } :
                    (object)new { name = id, description = "真实拥有帧边界" };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = 3, skills = presentations }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "真实拥有帧边界") { Program = program });
            b.AddSkill(new("fixture:lh-pick-owner", "固定主公来源", "公开选将评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? 100000d : -100000d) });
            b.AddSkill(new("fixture:lh-pick-other", "固定其他角色", "公开选将评分")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Lord ? -100000d : 100000d) });
            var skills = new List<string> { Dangxian, Fuli };
            if (!native) skills.AddRange([Driver, Gain, Hp, Face]);
            if (publicDiscard) skills.Add("fixture:lh-seed-discard");
            if (pauseDistanceUse) skills.Add(UsePause);
            b.AddGeneral(new("fixture:lh-owner", "界廖化真实机制", "supporter", "fixture:lh-pick-owner", "shu", baseHp, skills.ToArray()) { InitialHp = initialHp });
            var factions = new[] { "wei", "wu", "qun" };
            for (var i = 1; i < 4; i++)
                b.AddGeneral(new($"fixture:lh-other-{i}", "固定其他角色", "supporter", "fixture:lh-pick-other", factions[i - 1], 12));
            b.AddDeck(new("fixture:lh-deck", "固定实物杀", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 100).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "界廖化实际命令", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:lh-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:lh-owner", "fixture:lh-other-1", "fixture:lh-other-2", "fixture:lh-other-3"]));
        }
    }

    private const string FixtureRules = """
    {"schemaVersion":$SCHEMA$,"skills":[
      {"id":"fixture:lh-driver","revision":1,"activations":[
        {"id":"dealt-four","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":4}]},
        {"id":"empty-and-hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]},
        {"id":"fatal-incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":8}]},
        {"id":"give-and-regain","minCards":1,"maxCards":1,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1},{"op":"revealTargetHandCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"resultBind":"returned","mode":"chooser"},{"op":"moveBoundCards","target":"owner","sourceBind":"returned","destination":"ownerHand","awaitMovementTriggers":true}]}
      ]},
      {"id":"fixture:lh-gain","revision":1,"triggers":[
        {"id":"claim-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.granted-phase-slash.claim"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"claim-seen","options":[{"id":"continue"}]},{"op":"recover","target":"owner","amount":1}]},
        {"id":"fuli-gain-child","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["program.frozen-faction-recovery.draw"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"draw-seen","options":[{"id":"continue"}]}]}
      ]},
      {"id":"fixture:lh-hp","revision":1,"triggers":[{"id":"hp-child","window":"afterHealthChanged","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:lh-use-pause","revision":1,"triggers":[{"id":"accepted-slash","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"accepted-use","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:lh-face","revision":1,"triggers":[{"id":"face-child","window":"characterTurnedOver","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"face-seen","options":[{"id":"continue"}]}]}]},
      {"id":"fixture:lh-seed-discard","revision":1,"triggers":[{"id":"real-public-source","window":"playPhaseStarting","subject":"owner","priority":100,"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"public-source"},{"op":"moveBoundCards","target":"owner","sourceBind":"public-source","destination":"discardPile","awaitMovementTriggers":true}]}]}
    ]}
    """;
}
