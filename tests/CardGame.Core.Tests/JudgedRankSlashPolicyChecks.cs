using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class JudgedRankSlashPolicyChecks
{
    private const string Mode = "identity:classic-judged-rank-fixture", Qiangwu = "ol:qiangwu", Shenxian = "ol:shenxian";
    private const string Driver = "fixture:rank-driver", Final = "fixture:rank-final", Gain = "fixture:rank-gain", Peer = "fixture:rank-peer";
    private const string Activation = "judge-rank-slash-turn", DrawReason = "skill-program.ol:shenxian.DrawFromOtherActualBasicDiscard";

    public static void QiangwuActualJudgmentFrozenRankDistanceAndSeparateQuota()
    {
        var (g, r) = Start(); DriverUse(g, "quota"); ReachPlay(g);
        ActivateQiangwu(g); Reach(g, p => p.SkillPrompt?.SkillId == Final);
        var judgment = g.ResolutionStack.OfType<JudgmentFrame>().Single();
        Require(E<JudgmentResolvedEvent>(g).Single(e => e.ResolutionId == judgment.Id).Rank == 7 && E<TurnJudgedRankSlashPolicyGrantedEvent>(g).Length == 0,
            "The real final judgment observer completes before its parent issues X."); Cold(g, r); Continue(g); ReachPlay(g);
        var grant = E<TurnJudgedRankSlashPolicyGrantedEvent>(g).Single().Policy;
        Require(grant.Rank == 7 && grant.JudgmentFrameId == judgment.Id && grant.JudgmentCardId == judgment.CardId &&
            Enumerable.Range(0, 4).All(s => g.CreateSnapshot(s).Players[0].JudgedRankSlashThreshold is { Rank: 7 }) &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Qiangwu), "One real Play activation exposes only the public scalar X.");
        var low = V(g).Hand.Single(c => c.Rank == 5).Id; var equal = V(g).Hand.Single(c => c.Kind == CardKind.Slash && c.Rank == 7).Id;
        Require(Slash(g, low, 2) is not null && Slash(g, equal, 2) is null &&
            V(g).Hand.Where(c => c.Rank == 9).All(c => Slash(g, c.Id, 2) is null), "Only rank<X receives distance permission; =X and >X keep normal range.");
        PlaySlash(g, V(g).Hand.First(c => c.Rank == 9).Id, 1); ReachPlay(g);
        Require(E<CardUseDebitRecordedEvent>(g).Length == 0, "A high-rank first Slash leaves the finite normal quota available.");
        PlaySlash(g, low, 2); ReachPlay(g); PlaySlash(g, equal, 1); ReachPlay(g);
        Require(E<CardUseDebitRecordedEvent>(g).Length == 2 && Slash(g, V(g).Hand.Single(c => c.Kind == CardKind.Slash).Id, 1) is not null,
            "The below/equal uses each debit once; a high-rank action remains published after finite quota exhaustion.");
        PlaySlash(g, V(g).Hand.Single(c => c.Kind == CardKind.Slash).Id, 1); ReachPlay(g);
        var uses = E<JudgedRankSlashUsePolicyAppliedEvent>(g).Select(e => e.Receipt).ToArray();
        Require(uses.Length == 4 && uses.Count(u => u.IgnoresQuota) == 2 && uses.Count(u => u.IgnoresDistance) == 1 &&
            E<CardUseDebitRecordedEvent>(g).Length == 2 && E<CardUseDeclaredEvent>(g).Count(e => e.CardKind == CardKind.Slash) == 4 &&
            E<CardUseFinishedEvent>(g).Count(e => e.CardKind == CardKind.Slash) == 4,
            "Every high-rank attack is still a genuine whole use and finish, without a fabricated debit/refund."); Cold(g, r);
    }

    public static void QiangwuReplacementEmptyDeckAndIssuedSourceLossHostBoundary()
    {
        var (g, r) = Start(replacement: true); ActivateQiangwu(g);
        Reach(g, p => p.Kind == DecisionKind.ProgramJudgmentReplacement); Cold(g, r);
        var replacement = V(g).Hand.First(c => c.Rank == 9).Id; Answer(g, c => c.Cards.SequenceEqual([replacement]));
        Reach(g, p => p.SkillPrompt?.SkillId == Final); Cold(g, r); Continue(g); ReachPlay(g);
        Require(E<TurnJudgedRankSlashPolicyGrantedEvent>(g).Single().Policy is { Rank: 9, JudgmentCardId: var id } && id == replacement &&
            E<JudgmentResolvedEvent>(g).Single(e => e.Reason == Qiangwu).CardId == replacement,
            "X belongs to the actual final replacement card, rather than the initially revealed rank."); Cold(g, r);
        var (empty, er) = Start(empty: true); ActivateQiangwu(empty); ReachPlay(empty);
        Require(E<JudgmentResolvedEvent>(empty).Single(e => e.Reason == Qiangwu) is { CardId: null, Rank: null } &&
            E<TurnJudgedRankSlashPolicyGrantedEvent>(empty).Length == 0 && V(empty).JudgedRankSlashThreshold is null,
            "A genuinely empty real judgment creates no invented threshold."); Cold(empty, er);
        var (lost, lr) = Start(); ActivateQiangwu(lost); Reach(lost, p => p.SkillPrompt?.SkillId == Final); Continue(lost); ReachPlay(lost); Cold(lost, lr);
        // Explicit host state audit after a fully command-replayed real issuance; these mutations are not replay commands.
        var owner = HostPlayers(lost)[0]; owner.SkillGrants.RemoveGrant(owner.SkillGrants.Grants.Single(a => a.SkillId == Qiangwu).GrantId);
        Require(V(lost).JudgedRankSlashThreshold is { Rank: 7 } && V(lost).Hand.Where(c => c.Rank == 9).All(c => Slash(lost, c.Id, 1) is not null),
            "An issued turn policy retains its original result after source loss.");
        Accept(lost, new EndPlayPhaseCommand(0, lost.Revision, P(lost)!.PromptId));
        ReachState(lost, () => E<TurnStartedEvent>(lost).Any(e => e.ActorSeat == 1));
        Require(V(lost).JudgedRankSlashThreshold is null, "The original effect expires at real actual-turn departure.");
    }

    public static void ShenxianSkipThenRealDrawOnceWithFrozenGainChild()
    {
        var (g, r) = Start(shen: true); EndToOther(g); Reach(g, p => p.SkillPrompt?.SkillId == Shenxian);
        Require(E<OtherActualBasicDiscardDrawIssuedEvent>(g).Length == 0, "Optional exposure does not issue a draw or consume the fact quota."); Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(g, p => p.SkillPrompt?.SkillId == Shenxian); Cold(g, r);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var fact = E<OtherActualBasicDiscardDrawIssuedEvent>(g).Single();
        Require(fact.Source.OwnerSeat == 0 && fact.ActualTurnOwnerSeat == 1 && fact.DiscardOwnerSeat == 1 &&
            g.CardMovements.Count(m => m.Reason.Value == DrawReason && m.To == CardLocation.Hand(0)) == 1 &&
            g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Shenxian),
            "One accepted original basic discard issues one true Draw and pauses its gain child before the original observer returns."); Cold(g, r); Continue(g);
        ReachState(g, () => E<ProgramOwnedZoneCardsDiscardedEvent>(g).Count(e => e.SkillId == Peer) == 3);
        Require(E<OtherActualBasicDiscardDrawIssuedEvent>(g).Length == 1 &&
            E<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == Shenxian) == 1,
            "Skipping the first eligible batch leaves the second usable; a third same-turn discard cannot repeat the accepted draw."); Cold(g, r);
    }

    public static void ShenxianOriginalMovedEntityResetAndActualTurnPredicateHostBoundary()
    {
        var (g, r) = Start(shen: true); EndToOther(g); Reach(g, p => p.SkillPrompt?.SkillId == Shenxian); Cold(g, r);
        var window = g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Last(); var candidate = window.Candidates[window.CandidateIndex];
        var batch = window.Batch; var original = batch.Movements[candidate.OccurrenceIndex];
        // A narrow host audit exercises the frozen-fact selector after a legitimate real card move.
        // No checkpoint/replay claim is made for the following host mutations.
        var card = (Card)Invoke(g, "GetAttackCard", original.CardId)!;
        Invoke(g, "MoveCard", card, CardLocation.DiscardPile, CardLocation.Hand(0), new CardMoveReason("fixture.host.original-claim"), null!, true);
        Require(Indexes(g, batch, candidate).Contains(candidate.OccurrenceIndex) &&
            Indexes(g, batch with { MovementTiming = new(1, TurnPhase.Play, 0) }, candidate).Contains(candidate.OccurrenceIndex) &&
            Indexes(g, batch with { MovementTiming = new(0, TurnPhase.Play, 1) }, candidate).Length == 0,
            "Original ledger identity survives a later claim; actual owner, rather than an inserted phase's actor, determines outside-owner qualification.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        var owner = HostPlayers(g)[0]; var old = owner.SkillGrants.Grants.Single(a => a.SkillId == Shenxian);
        owner.SkillGrants.RemoveGrant(old.GrantId); owner.SkillGrants.Grant(new("fixture:rank-regrant", Shenxian, old.SkillInstanceId + ":later", "acquired:host-audit"));
        ((SkillRuntimeStateStore)typeof(GameEngine).GetField("_skillRuntimeState", Flags)!.GetValue(g)!).ResetSkill(0, Shenxian);
        Require(Indexes(g, batch, candidate).Length == 0, "An already issued draw survives ResetSkill and replacement instance as a same-turn quota fact.");
        Continue(g); ReachState(g, () => E<ProgramOwnedZoneCardsDiscardedEvent>(g).Count(e => e.SkillId == Peer) == 3);
        Require(E<OtherActualBasicDiscardDrawIssuedEvent>(g).Single().MovementSequence == original.Sequence &&
            E<OtherActualBasicDiscardDrawIssuedEvent>(g).Length == 1, "Source replacement cannot pay the original accepted basic discard twice.");
    }

    private static (GameEngine, ContentRegistry) Start(bool replacement = false, bool empty = false, bool shen = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(replacement, empty, shen));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(g, new SelectGeneralCommand(0, "fixture:rank-owner", g.Revision, P(g)!.PromptId)); ReachPlay(g); return (g, r);
    }
    private static void ActivateQiangwu(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Qiangwu, Activation, [], [], g.Revision, P(g)!.PromptId));
    private static void DriverUse(GameEngine g, string id) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [], g.Revision, P(g)!.PromptId));
    private static LegalAction? Slash(GameEngine g, int id, int target) => g.GetHumanLegalActions().FirstOrDefault(a => a.Kind == LegalActionKind.Slash && a.CardId == id && a.TargetSeats.SequenceEqual([target]));
    private static void PlaySlash(GameEngine g, int id, int target) { var a = Slash(g, id, target)!; Accept(g, new PlayCardCommand(0, id, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind)); }
    private static void EndToOther(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static PlayerSnapshot V(GameEngine g) => g.CreateSnapshot(0).Players[0];
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string id) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void ReachState(GameEngine g, Func<bool> predicate) { for (var n = 0; n < 128; n++) { if (predicate()) return; Advance(g); } throw new InvalidOperationException("Fixed rank/discard state boundary was not reached."); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate) { for (var n = 0; n < 128; n++) { var p = P(g); if (p is not null && predicate(p)) return; Advance(g); } throw new InvalidOperationException($"Fixed rank/discard fixture missed its named boundary: phase={g.State.Phase}, prompt={P(g)?.Kind}/{P(g)?.PlayerSeat}/{P(g)?.SkillPrompt?.SkillId}, frames={string.Join(',', g.ResolutionStack.Select(f => f.GetType().Name + ':' + f.Id))}."); }
    private static void Advance(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Accept(GameEngine g, GameCommand command) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rank/discard command rejected."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(), Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)), "Exact accepted commands restore the frozen final rank, debit facts and all private views.");
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static object? Invoke(GameEngine g, string name, params object[] args) => typeof(GameEngine).GetMethod(name, Flags)!.Invoke(g, args);
    private static IReadOnlyList<CharacterState> HostPlayers(GameEngine g) => (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", Flags)!.GetValue(g)!;
    private static int[] Indexes(GameEngine g, CardMovementBatchContext batch, ProgramTriggerCandidate candidate) => (int[])Invoke(g, "MatchingOtherActualBasicDiscardIndexes", batch, candidate)!;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool replacement, bool empty, bool shen) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-judged-rank-slash", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var replacementNode = replacement ? """
              ,{"id":"replace","window":"judgmentReplacing","subject":"owner","excludedReasons":[],"optional":true,
                "effects":[{"op":"replaceJudgment","target":"owner","zones":["hand"],"suits":["spade","club","heart","diamond"],"oldCardDestination":"discardPile"}]}
              """ : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"quota","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,
                "effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":1}]}]},
              {"id":"{{Final}}","revision":1,"triggers":[{"id":"final","window":"judgmentFinalized","subject":"owner","judgmentReasons":["ol:qiangwu"],"suits":["heart"],"minimumRank":1,"maximumRank":13,"excludedReasons":[],"optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"final-seen","options":[{"id":"continue"}]}]}{{replacementNode}}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"gain","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementReasons":["{{DrawReason}}"],"movementOccurrence":"perBatch","optional":false,
                "effects":[{"op":"chooseOption","target":"owner","resultBind":"gain-seen","options":[{"id":"continue"}]}]}]},
              {"id":"{{Peer}}","revision":1,"triggers":[{"id":"three-discards","window":"playPhaseStarting","subject":"owner","optional":false,"effects":[
                {"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"draw","target":"owner","amount":1},
                {"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]},{"op":"draw","target":"owner","amount":1},
                {"op":"discardOwnedZoneCards","target":"owner","zones":["hand"]}]}]}]}
            """;
            var names = new[] { Driver, Final, Gain, Peer }.ToDictionary(id => id, id => (object)new { name = id, description = "实际判定、实体和共享返口", optionLabels =
                id is Final or Gain ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var pair in catalog.Programs) b.AddSkill(new(pair.Key, pair.Key, "实际共享边界") { Program = pair.Value });
            b.AddSkill(new("fixture:rank-idle", "固定其他角色", "无运行技能"));
            b.AddGeneral(new("fixture:rank-owner", "公开阈值", "supporter", Qiangwu, "shu", 3, shen ? [Shenxian, Driver, Final, Gain] : [Driver, Final, Gain], GeneralGender.Female));
            for (var n = 1; n < 4; n++) b.AddGeneral(new($"fixture:rank-peer-{n}", "固定对手", "supporter", shen ? Peer : "fixture:rank-idle", "wei", 12, [], GeneralGender.Male));
            // Fixed seed31 source arithmetic: role shuffle2 + general shuffle3, then64 Fisher-Yates.
            // Owner recipe positions28,59,15,22,23; next judgment55. No seed search or game execution.
            var ranks = new Dictionary<int, int> { [28] = 5, [59] = 7, [15] = 9, [22] = 9 };
            var cards = Enumerable.Range(0, empty ? 20 : 64).Select(i => new ContentDeckPhysicalCard(!empty && !shen && ranks.ContainsKey(i) ? "standard:slash" : "standard:dodge", Suit.Heart, !empty && !shen && ranks.TryGetValue(i, out var rank) ? rank : 7)).ToArray();
            b.AddDeck(new("fixture:rank-deck", "有限固定实体", 5, 0, []) { PhysicalCards = cards });
            b.AddMode(new(Mode, "真实阈值边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:rank-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:rank-owner", "fixture:rank-peer-1", "fixture:rank-peer-2", "fixture:rank-peer-3"]));
        }
    }
}
