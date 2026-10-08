using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GameActivationAwakeningChecks
{
    private const string Counted = "fixture:awakening-counted", Activation = "actual-use", Awake = "fixture:activation-awakening";
    private const string Driver = "fixture:activation-awakening-driver", HpChild = "fixture:activation-awakening-hp";
    private const string Suppressor = "fixture:activation-awakening-suppressor", Granted = "fixture:activation-awakening-granted";
    private const string Mode = "fixture:activation-awakening-mode";

    public static void ActualRefundedActivationsQualifyPreparationOnceAndSurviveSourceLoss()
    {
        foreach (var sourceLoss in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(sourceLoss));
            var g = Start(registry);
            foreach (var seat in new[] { 1, 2, 3 }) { UseDriver(g, "collect", [seat]); Play(g); }
            Require(g.CreateSnapshot(0).Players[0].Hand.Count == 16, "Real initial entities from the fixed deck are collected by an actual skill command.");
            UseCounted(g, 0); Play(g); GiveThreeCategories(g, 1); UseCounted(g, 1); Play(g);
            AssertCountAndRefunds(g, 2);
            if (!sourceLoss)
            {
                NextPlay(g);
                Require(!E<GameActivationAwakeningMaximumPaidEvent>(g).Any() && !E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Awake) &&
                    !g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Granted),
                    "Two actual refunded activations do not publish or consume the game-once awakening at the next real preparation.");
                UseCounted(g, 0); Play(g);
            }
            else { GiveThreeCategories(g, 2); UseCounted(g, 2); Play(g); }
            AssertCountAndRefunds(g, 3);
            var maximumBefore = g.CreateSnapshot(0).Players[0].MaxHp;
            Require(maximumBefore == 5 && g.CreateSnapshot(0).Players[0].Hp == 5,
                "This fixed non-classic identity fixture uses the native Lord's actual 5 HP, independent of a general's ignored classic-only BaseHp.");
            var turn = g.State.TurnNumber;
            Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
            Reach(g, p => p.SkillPrompt?.SkillId == HpChild);
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.GameActivationAwakening is not null);
            var receipt = root.GameActivationAwakening!; var rootId = root.Id;
            var native = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single(w => w.ResumeFrameId == rootId);
            var countIds = E<ProgramSkillStartedEvent>(g).Where(e => e.SkillId == Counted && e.ActivationId == Activation).Select(e => e.FrameId).Order().ToArray();
            Require(g.State.TurnNumber > turn && receipt.Stage == GameActivationAwakeningStage.MaximumPaid &&
                receipt.ActivationFrameIds.SequenceEqual(countIds) && receipt.ActivationFrameIds.Count == 3 && receipt.MinimumValue == 3 &&
                receipt.CountedSkillId == Counted && receipt.CountedActivationId == Activation && receipt.SkillIds.SequenceEqual([Granted]) &&
                receipt.MaximumBefore == maximumBefore && receipt.MaximumAfter == maximumBefore - 1 &&
                receipt.Source.SkillId == Awake && receipt.Source.OwnerSeat == 0 && receipt.Source.SkillInstanceId == root.SkillInstanceId &&
                root.WindowContext is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context &&
                context.ParentFrameId == receipt.LifecycleFrameId && g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w =>
                    w.Id == receipt.LifecycleFrameId && w.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow &&
                    w.Candidates[w.CandidateIndex].SkillInstanceId == root.SkillInstanceId) &&
                native.Change is { Kind: HpChangeKind.MaximumHp, Amount: 1, TargetSeat: 0 } && native.Change.ParentFrameId == rootId &&
                native.Continuation == PostEventContinuation.Program && !E<GameActivationAwakeningGrantIssuedEvent>(g).Any(),
                "The third actual activation qualifies only the next real preparation; one typed max-HP payment waits for its exact native MaximumHp child before any grant.");
            Require(E<MaximumHpChangedEvent>(g).Count(e => e.SkillId == Awake && e.Delta == -1 && e.MaximumHp == maximumBefore - 1) == 1 &&
                E<SkillUsageConsumedEvent>(g).Count(e => e.SkillId == Awake && e.UsageId == "awaken" && e.Scope == SkillUsageScope.Game && e.Count == 1) == 1,
                "The max-HP primitive and the independent game-once awakening usage each commit once.");
            Frozen(receipt.ActivationFrameIds); Frozen(receipt.SkillIds); Private(g);
            g = Cold(g, registry); Continue(g); Play(g);
            Require(E<GameActivationAwakeningMaximumPaidEvent>(g).Single().FrameId == rootId &&
                E<GameActivationAwakeningGrantIssuedEvent>(g).Single() == new GameActivationAwakeningGrantIssuedEvent(rootId, 0, Awake, Granted) &&
                E<GameActivationAwakeningCompletedEvent>(g).Single() is { GrantsIssued: true, OwnerAlive: true } completed && completed.FrameId == rootId &&
                E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Awake && e.SkillIds.SequenceEqual([Granted])) == 1 &&
                E<SkillAwakenedEvent>(g).Count(e => e.PlayerSeat == 0 && e.SkillId == Awake && e.AcquiredSkillIds.SequenceEqual([Granted])) == 1 &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == rootId && e.Completed) == 1,
                "Cold native health return issues the original requested skill grant, awakening fact and owning binding completion once without repaying maximum HP.");
            if (sourceLoss)
            {
                var owner = g.CreateSnapshot(0).Players[0];
                var suppressorAcquisitions = E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == HpChild && e.SkillIds.Contains(Suppressor));
                Require(owner.Hp == 4 && owner.MaxHp == 4 && suppressorAcquisitions == 1 &&
                    owner.Skills!.Any(s => s.Id == Suppressor) && !owner.Skills!.Any(s => s.Id == Awake) &&
                    !E<ProgramOwnerSkillsReplacedEvent>(g).Any(e => e.LostSkillIds.Contains(Awake)),
                    $"The real MaximumHp observer acquires an independent suppressor and removes the acquired awakening's qualification; its paid receipt still grants without claiming physical removal. HP={owner.Hp}/{owner.MaxHp}, suppressorAcquisitions={suppressorAcquisitions}, skills={string.Join(',', owner.Skills!.Select(s => s.Id))}.");
            }
            else
            {
                Require(g.CreateSnapshot(0).Players[0].Skills!.Any(s => s.Id == Granted), "The actual runtime grant is present in the owner's prepared snapshot.");
                NextPlay(g);
                Require(E<GameActivationAwakeningMaximumPaidEvent>(g).Length == 1 && E<GameActivationAwakeningGrantIssuedEvent>(g).Length == 1 &&
                    E<ProgramBindingStartedEvent>(g).Count(e => e.SkillId == Awake) == 1 &&
                    g.CreateSnapshot(0).Players[0].MaxHp == maximumBefore - 1,
                    "A later genuine preparation retains the three game activations and cannot awaken, pay max HP or grant again.");
            }
            AssertCountAndRefunds(g, 3); _ = Cold(g, registry);
        }
    }

    private static void AssertCountAndRefunds(GameEngine g, int count)
    {
        var started = E<ProgramSkillStartedEvent>(g).Where(e => e.OwnerSeat == 0 && e.SkillId == Counted && e.ActivationId == Activation).ToArray();
        Require(started.Length == count && started.Select(e => e.FrameId).Distinct().Count() == count &&
            E<DrawDiscardCategoryRefundedEvent>(g).Count(e => e.SkillId == Counted && e.BeforeUsage == 1 && e.AfterUsage == 0) == count &&
            E<DrawDiscardCategoryCompletedEvent>(g).Count(e => e.DistinctNonEmpty && e.BonusIssued && e.QuotaRefunded && e.TargetBanned) == count &&
            started.All(s => E<DrawDiscardCategoryDrawIssuedEvent>(g).Count(e => e.FrameId == s.FrameId && !e.Bonus && e.RequestedCount == 3) == 1 &&
                E<DrawDiscardCategoryDrawIssuedEvent>(g).Count(e => e.FrameId == s.FrameId && e.Bonus && e.RequestedCount == 1) == 1),
            "Each genuine distinct payment refunds only its phase quota; exactly one original Draw3 and bonus Draw1 remain in independent non-rollback game activation history.");
    }
    private static void GiveThreeCategories(GameEngine g, int target)
    {
        var hand = g.CreateSnapshot(0).Players[0].Hand;
        var ids = new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }
            .Select(category => hand.First(c => Category(c.Kind) == category).Id).ToArray();
        Accept(g, new UseProgramSkillCommand(0, Driver, "give-three", ids, [target], g.Revision, P(g)!.PromptId)); Play(g);
    }
    private static void UseCounted(GameEngine g, int target)
    {
        var offer = g.GetHumanLegalActions().Single(a => a.ProgramSkillId == Counted);
        Require(offer.MinCardCount == 0 && offer.MaxCardCount == 0 && offer.SelectableTargetSeats.Contains(target), "A fresh real phase offer contains the intended living target with no original card material.");
        Accept(g, new UseProgramSkillCommand(0, Counted, Activation, [], [target], g.Revision, P(g)!.PromptId));
        if (target != 0) return;
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "draw-discard-category"));
        var receipt = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.DrawDiscardCategoryRefund is not null).DrawDiscardCategoryRefund!;
        var ids = new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }
            .Select(category => receipt.EligibleMaterials.First(m => m.Category == category).CardId).ToArray();
        foreach (var id in ids) Answer(g, c => c.Cards.SequenceEqual([id]));
    }
    private static SkillProgramCardCategory Category(CardKind kind) => EquipmentCatalog.IsEquipment(kind) ? SkillProgramCardCategory.Equipment :
        kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Dodge or CardKind.Peach or CardKind.Alcohol ? SkillProgramCardCategory.Basic : SkillProgramCardCategory.Trick;
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Require(p.PlayerSeat == 0, "Only the original human chooser answers fixture prompts."); Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(choose).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void UseDriver(GameEngine g, string activation, IReadOnlyList<int> targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void NextPlay(GameEngine g)
    { var turn = g.State.TurnNumber; Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Reach(g, p => p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && g.State.TurnNumber > turn); }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var i = 0; i < 160; i++)
        {
            var p = P(g); if (p is not null && stop(p)) return;
            if (p is { PlayerSeat: 0 } && p.SkillPrompt?.SkillId == HpChild) Continue(g);
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards }) Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
            else { Require(p is not { PlayerSeat: 0 }, $"Unexpected awakening boundary {p?.Kind}/{p?.SkillPrompt?.SkillId}."); Accept(g, new AdvanceOneStepCommand(g.Revision)); }
        }
        throw new InvalidOperationException("The bounded fixed-seed awakening fixture did not reach its exact native boundary.");
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Actual fixture command rejected."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(cold) == State(g), "Cold accepted-command restoration preserves every exact awakening frame, payment fact, hand, view and command."); return cold;
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Private(GameEngine g)
    {
        var before = State(g);
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var view = g.CreateSnapshot(viewer);
            Require(view.Players.Where(p => p.Seat != viewer).All(p => p.Hand.Count == 0) &&
                (viewer == 0 ? view.PendingDecision is { IsPrivate: true, PlayerSeat: 0 } : view.PendingDecision is null),
                "Each prepared player view hides other hands and publishes the native private health choice only to its original actor.");
            if (viewer == 0) { Frozen(view.PendingDecision!.Choices); Frozen(view.PendingDecision.ValidCardIds); }
        }
        Require(State(g) == before, "Readonly private projections preserve the original accepted state.");
    }
    private static void Frozen<T>(IReadOnlyList<T> values)
    {
        Require(values is IList<T> { IsReadOnly: true }, "An exposed nested collection is detached and readonly.");
        try { ((IList<T>)values).Add(default!); } catch (NotSupportedException) { return; }
        throw new InvalidOperationException("A supposedly frozen collection accepted mutation.");
    }
    private static GameEngine Start(ContentRegistry registry)
    {
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 });
        Require(P(g)!.ValidContentIds.Contains("fixture:activation-awakening-owner"), "The intended general is a genuinely published fixed candidate.");
        Accept(g, new SelectGeneralCommand(0, "fixture:activation-awakening-owner", g.Revision, P(g)!.PromptId)); Play(g);
        UseDriver(g, "acquire", []); Play(g);
        Require(E<SkillsAcquiredEvent>(g).Count(e => e.PlayerSeat == 0 && e.SourceSkillId == Driver && e.SkillIds.SequenceEqual([Counted, Awake])) == 1,
            "Both tested skill sources are actual runtime acquisitions before the counted commands."); return g;
    }
    private sealed class Fixture(bool sourceLoss) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("activation-count-awakening-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var rules = JsonNode.Parse($$$"""
            {"skills":[
              {"id":"{{{Counted}}}","revision":1,"activations":[{"id":"{{{Activation}}}","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"usesPerPhase":1,"usesPerGame":null,"effects":[{"op":"drawThenDiscardDistinctCategories","target":"selectedTarget","amount":3,"stateId":"fixture:activation-awakening-targets"}]}]},
              {"id":"{{{Awake}}}","revision":1,"triggers":[{"id":"awaken","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"awakenAfterGameActivations","target":"owner","sourceSkillId":"{{{Counted}}}","activationId":"{{{Activation}}}","minimumValue":3,"skillIds":["{{{Granted}}}"]}]}]},
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"acquire","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{{Counted}}}","{{{Awake}}}"]}]},
                {"id":"collect","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelectedTargetHand","target":"selectedTarget"}]},
                {"id":"give-three","minCards":3,"maxCards":3,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":3}]}]},
              {"id":"{{{HpChild}}}","revision":1,"triggers":[{"id":"native-maximum","window":"afterHealthChanged","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"seen-maximum","options":[{"id":"continue"}]}]}]},
              {"id":"fixture:activation-awakening-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            rules["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            if (sourceLoss) rules["skills"]![3]!["triggers"]![0]!["effects"]!.AsArray().Add(JsonNode.Parse($$$"""{"op":"grantSkills","target":"owner","skillIds":["{{{Suppressor}}}"]}"""));
            var presentations = rules["skills"]!.AsArray().ToDictionary(n => n!["id"]!.GetValue<string>(), n =>
            {
                var id = n!["id"]!.GetValue<string>(); var presentation = new Dictionary<string, object> { ["name"] = id, ["description"] = "真实发动历史与原生觉醒返回" };
                if (id == HpChild) presentation["optionLabels"] = new Dictionary<string, string> { ["continue"] = "继续" };
                return (object)presentation;
            });
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "真实支付与觉醒来源")
                { Program = program, ProgramPresentation = catalog.Presentations[id], Tags = id == Awake ? SkillTag.Awakening : SkillTag.None });
            b.AddSkill(new(Suppressor, "实际HP4资格抑制", "资格抑制不物理删除grant") { SuppressionRule = new(4) });
            b.AddSkill(new(Granted, "实际觉醒授予", "独立真实runtime grant"));
            b.AddSkill(new("fixture:activation-awakening-selection", "固定原生选将", "小模式") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:activation-awakening-owner", "发动计数拥有者", "supporter", Driver, "jin", 20, [HpChild]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:activation-awakening-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "原生其他角色", "supporter", "fixture:activation-awakening-selection", "shu", 6, ["fixture:activation-awakening-quiet"]));
            var kinds = new[] { "standard:slash", "standard:duel", "standard:bagua" };
            b.AddDeck(new("fixture:activation-awakening-deck", "固定三类实体", 4, 0, [])
                { PhysicalCards = Enumerable.Range(0, 96).Select(i => new ContentDeckPhysicalCard(kinds[i % 3], Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实发动次数觉醒", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:activation-awakening-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:activation-awakening-owner", .. peers]));
        }
    }
}
