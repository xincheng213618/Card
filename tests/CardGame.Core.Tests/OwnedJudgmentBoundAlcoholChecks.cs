using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class OwnedJudgmentBoundAlcoholChecks
{
    private const string Baonue = "boundary:baonue-current";
    private const string Chunlao = "classic:chunlao";
    private const string Driver = "fixture:ojba-driver";
    private const string Gain = "fixture:ojba-gain";
    private const string Hp = "fixture:ojba-hp";
    private const string Completed = "fixture:ojba-completed";
    private const string Binding = "other-qun-damage-point-judgment";
    private const string Reason = "skill.boundary.baonue-current";
    private const string CostReason = "skill-program.classic:chunlao.UseBoundCardAsDyingAlcohol";
    private const string Mode = "identity:classic-owned-judgment-bound-alcohol";

    public static void ClaimedJudgmentGainDyingBoundAlcoholReturnsAndColdReplays()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:ojba-owner", game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);
        Require(game.State.Players[0].Hp == 2, "The actual classic Lord has base initial HP1 plus the formal Lord HP bonus.");

        // The real classic ending trigger prepares the public pile; no host state is injected.
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => Activation(p, Chunlao, "store-chun-at-turn-end"));
        Activate(game, Chunlao, "store-chun-at-turn-end"); Reach(game, p => Action(p, "select-owned-cards"));
        var costId = Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards").Cards.Single();
        Answer(game, c => c.Cards.SequenceEqual([costId]));
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards"); ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].ChunlaoCards is [var stored] && stored.Id == costId &&
            game.CardMovements.Count(m => m.CardId == costId && m.From == CardLocation.Hand(0) && m.To == CardLocation.Chunlao(0)) == 1 &&
            Events<TurnEndedEvent>(game).Count(e => e.ActorSeat == 0) == 1 && game.State.Players[0].Hp == 2,
            "Exactly one actual ending stores an owned Slash before the next actual turn's damage and judgment."); Cold(game, registry);

        Accept(game, new UseProgramSkillCommand(0, Driver, "incoming", [], [1], game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => Activation(p, Baonue, Binding)); Activate(game, Baonue, Binding);
        Reach(game, p => p.SkillPrompt?.SkillId == Hp);
        var judgment = game.ResolutionStack.OfType<JudgmentFrame>().Single(); var claimedId = judgment.CardId!.Value;
        Require(game.State.Players[0].Hp == 2 && game.CreateSnapshot(0).Players[0].Hand.All(c => c.Id != claimedId),
            "The original one-point damage takes HP2 to1; the finalized Spade first recovers HP before its claim.");
        AssertOwningJudgment(game, claimedId); Cold(game, registry); Reject(game); Continue(game);

        Reach(game, p => p.SkillPrompt?.SkillId == Gain);
        var gain = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Gain);
        var gainFrameId = gain.Id;
        var gainBatch = gain.WindowContext!.MovementBatch!;
        Require(game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == claimedId) && ClaimMoves(game, claimedId) == 1 &&
            game.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.ParentFrameId == gainBatch.ParentFrameId && w.Batch.Id == gainBatch.Id &&
                w.Batch.Movements.Any(m => m.CardId == claimedId && m.From == CardLocation.Judgment(0) && m.To == CardLocation.Hand(0))),
            "The actual judgment card is already claimed exactly once when its one-use gain observer pauses.");
        AssertOwningJudgment(game, claimedId); Cold(game, registry); Reject(game); Continue(game);

        Reach(game, p => p.Kind == DecisionKind.RescueDying && p.PlayerSeat == 0 && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Chunlao));
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single();
        Require(dying.Continuation == DyingContinuationKind.ProgramSkill && dying.ParentFrameId == gainFrameId &&
            dying.VictimSeat == 0 && game.State.Players[0].Hp == 0 && ClaimMoves(game, claimedId) == 1,
            "The real one-use gain payment loses2 HP and suspends the claim producer on its exact Dying child.");
        AssertOwningJudgment(game, claimedId); Cold(game, registry); Reject(game);
        Answer(game, c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Chunlao);
        Reach(game, p => Action(p, "select-source-card")); Cold(game, registry);
        Require(Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "select-source-card").Cards.SequenceEqual([costId]),
            "The rescue binds the exact Slash stored by the earlier classic Chunlao ending trigger.");
        Answer(game, c => c.Cards.SequenceEqual([costId]));

        Reach(game, p => p.SkillPrompt?.SkillId == Hp);
        var rescue = AssertRescue(game, gainFrameId, dying.Id, costId, claimedId);
        Require(game.State.Players[0].Hp == 1 && game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(w =>
            w.ResumeFrameId == rescue.Id && w.Change.ParentFrameId == rescue.Id && w.Change.TargetSeat == 0 &&
            w.Continuation == PostEventContinuation.CardUse),
            "Bound Alcohol really recovers HP1 and pauses its own HP return beneath the original claimed judgment.");
        AssertCostOnce(game, costId); Cold(game, registry); Reject(game); Continue(game);

        Reach(game, p => p.SkillPrompt?.SkillId == Completed);
        rescue = AssertRescue(game, gainFrameId, dying.Id, costId, claimedId);
        Require(game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Any(w => w.ParentFrameId == rescue.Id &&
            w.Continuation == ProgramCardContinuation.CompletedCard && w.Action.ActionId == rescue.Action!.ActionId),
            "The same exact Alcohol then pauses its completed-card observer before the Dying and claim parents return.");
        AssertCostOnce(game, costId); Cold(game, registry); Reject(game); Continue(game); ReachPlay(game);

        Require(game.State.Players[0].Hp == 1 && game.State.Players[0].IsAlive && game.CreateSnapshot(0).Players[0].ChunlaoCount == 0 &&
            game.CreateSnapshot(0).Players[0].Hand.Any(c => c.Id == claimedId) && ClaimMoves(game, claimedId) == 1 &&
            Events<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 1 &&
            Events<JudgmentRequestedEvent>(game).Count(e => e.Reason == Reason && e.TargetSeat == 0) == 1 &&
            Events<ProgramJudgmentCardClaimedEvent>(game).Count(e => e.SkillId == Baonue && e.CardId == claimedId && e.OwnerSeat == 0) == 1 &&
            Events<ProgramSkillHpLostEvent>(game).Count(e => e.SkillId == Gain && e.TargetSeat == 0 && e.Amount == 2) == 1 &&
            Events<DyingResolvedEvent>(game).Single(e => e.ResolutionId == dying.Id).Survived &&
            Events<ProgramDyingRescueEvent>(game).Count(e => e.DyingFrameId == dying.Id && e.SkillId == Chunlao &&
                e.CardId == costId && e.OwnerSeat == 0 && e.VictimSeat == 0 && e.RecoveredHp == 1) == 1 &&
            !game.CardMovements.Any(m => m.CardId == claimedId && m.From == CardLocation.Hand(0) &&
                m.To.Zone is CardZoneKind.DiscardPile or CardZoneKind.OutsideGame) &&
            !game.ResolutionStack.Any(f => f is JudgmentFrame or DyingFrame or CardUseFrame or HpChangedTriggerWindowFrame) &&
            !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.OwnedDamagePointJudgment is not null || f.SkillId == Chunlao),
            "The rescue, claim and original damage finish once; the already claimed card remains in Hand after empty producer cleanup.");
        AssertCostOnce(game, costId); Cold(game, registry);
    }

    private static void AssertOwningJudgment(GameEngine game, int cardId)
    {
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Baonue && f.TriggerId == Binding);
        var receipt = root.OwnedDamagePointJudgment!;
        var damage = game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Single(f => f.Id == receipt.DamageWindowId);
        var judgment = game.ResolutionStack.OfType<JudgmentFrame>().Single(f => f.Id == receipt.JudgmentFrameId);
        var candidate = damage.Candidates[damage.CandidateIndex];
        var finalized = game.ResolutionStack.OfType<ProgramJudgmentTriggerWindowFrame>().Single(f => f.ParentFrameId == judgment.Id);
        Require(root.OwnerSeat == 0 && root.WindowContext!.ParentFrameId == damage.Id && receipt.DamageOwnerFrameId == damage.ParentFrameId &&
            receipt.DamageSourceSeat == 1 && receipt.DamageTargetSeat == 0 && receipt.OccurrenceIndex == 0 &&
            candidate.OwnerSeat == root.OwnerSeat && candidate.ProgramId == root.SkillId && candidate.ProgramTriggerId == root.TriggerId &&
            candidate.SkillInstanceId == root.SkillInstanceId && candidate.GameplayHash == root.GameplayHash && candidate.OccurrenceIndex == 0 &&
            judgment.ParentFrameId == root.Id && judgment.CardId == cardId && judgment.TargetSeat == 0 && judgment.SourceSeat == 1 &&
            judgment.Continuation == JudgmentContinuationKind.ProgramSkill && judgment.ProgramResultBind == receipt.ResultBind &&
            judgment.Reason == Reason && finalized.Judgment.JudgmentFrameId == judgment.Id && finalized.Judgment.CardId == cardId &&
            finalized.Judgment.SourceSeat == 1 && finalized.Judgment.Suit == Suit.Spade,
            "The actual 3901 owner retains its source, point, frozen candidate, exact public judgment and typed finalized child.");
        foreach (var viewer in Enumerable.Range(1, 3))
            Require(game.CreateSnapshot(viewer).PendingDecision is null && game.CreateSnapshot(viewer).Players[0].Hand.Count == 0,
                "Other viewers retain only public judgment and pile facts, with no private benefit prompt or owner hand.");
    }

    private static CardUseFrame AssertRescue(GameEngine game, long gainFrameId, long dyingId, int costId, int claimedId)
    {
        AssertOwningJudgment(game, claimedId);
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single(f => f.Id == dyingId);
        var program = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chunlao);
        var rescue = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == costId);
        Require(dying.ParentFrameId == gainFrameId && dying.VictimSeat == 0 && dying.ResponderSeat == 0 &&
            game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Id == gainFrameId && f.SkillId == Gain) &&
            program.WindowContext is { Window: SkillProgramTriggerWindow.DyingResponse, ParentFrameId: var parent, TargetSeat: 0 } &&
            parent == dying.Id && program.OwnerSeat == 0 && program.CardSetBindings.Any(b => b.CardIds.SequenceEqual([costId]) &&
                b.SourceLocations.SequenceEqual([CardLocation.Chunlao(0)])) &&
            rescue.DyingResponse is null && rescue.SourceSeat == 0 && rescue.CardKind == CardKind.Alcohol && rescue.TargetSeats.SequenceEqual([0]) &&
            rescue.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.Alcohol } action &&
            action.PhysicalCards is [var cost] && cost.CardId == costId && cost.From == CardLocation.Chunlao(0) &&
            action.ConversionChain is [var conversion] && conversion.SkillId == program.SkillId && conversion.OwnerSeat == program.OwnerSeat &&
            conversion.BindingId == program.TriggerId && conversion.SkillInstanceId == program.SkillInstanceId,
            "The exact claimed judgment gain owns Dying, its bound-pile rescue program and the real Alcohol actor/provider/material use.");
        return rescue;
    }

    private static void AssertCostOnce(GameEngine game, int id) => Require(
        game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Chunlao(0) && m.To == CardLocation.Processing && m.Reason.Value == CostReason) == 1 &&
        game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == CostReason) == 1,
        "Every paused or completed child retains exactly one actual private-pile payment and one processing cleanup.");
    private static int ClaimMoves(GameEngine g, int id) => g.CardMovements.Count(m => m.CardId == id &&
        m.From == CardLocation.Judgment(0) && m.To == CardLocation.Hand(0) && m.Reason.Value == $"skill-program.{Baonue}.claimJudgmentCard");
    private static T[] Events<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Activation(PendingDecision p, string skill, string binding) => p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Activate(GameEngine g, string skill, string binding) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate" &&
        c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void ReachPlay(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 140; step++) { var p = Prompt(g); if (p is not null && predicate(p)) return; Advance(g); }
        throw new InvalidOperationException("Fixed owned-judgment rescue boundary was not reached: " + JsonSerializer.Serialize(Prompt(g)));
    }
    private static void Advance(GameEngine g)
    {
        var p = Prompt(g);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
            Accept(g, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, g.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        else if (p?.SkillPrompt?.SkillId is Hp or Completed) Continue(g);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine g)
    { var before = State(g); var p = Prompt(g)!; Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), g.Revision)).Accepted && State(g) == before,
        "An unpublished choice changes no paid child, original judgment or movement record."); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack), Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static void Cold(GameEngine g, ContentRegistry r) => Require(State(g) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r)),
        "All four prepared views, paid typed ancestry, original claim, events and real command prefix cold-restore exactly.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-owned-judgment-bound-alcohol", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                 {"id":"{{Driver}}","revision":1,"activations":[{"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":1}]}]},
                 {"id":"fixture:ojba-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                 {"id":"{{Gain}}","revision":1,"triggers":[{"id":"one-claim-payment","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.boundary:baonue-current.claimJudgmentCard"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"claim-seen","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":2}]}]},
                 {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
                 {"id":"{{Completed}}","revision":1,"triggers":[{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["alcohol"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"completed-seen","options":[{"id":"continue"}]}]}]}]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实来源伤害", description = "固定来源的一点伤害" },
                  ["fixture:ojba-quiet"] = new { name = "安静回合", description = "实际负杀次数额度" },
                  [Gain] = Observer("判定获牌真实HP付款"), [Hp] = Observer("实际回复子窗口"), [Completed] = Observer("酒完成子窗口") } }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "拥有判定救援真实夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:ojba-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:ojba-owner", "判定醇醪原始父链", "supporter", Baonue, "qun", 3,
                [Chunlao, Driver, Gain, Hp, Completed]) { InitialHp = 1 });
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:ojba-target-{i}", "实际其他来源", "supporter", "fixture:ojba-selection", "qun", 6,
                ["fixture:ojba-quiet"]));
            b.AddDeck(new("fixture:ojba-deck", "固定无桃无酒实际实体", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 80)
                .Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "拥有判定的真实醇醪返回", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:ojba-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:ojba-owner", "fixture:ojba-target-1", "fixture:ojba-target-2", "fixture:ojba-target-3"]));
        }
        private static object Observer(string name) => new { name, description = "精确拥有判定的真实救援暂停", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
