using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ParticipantHandProgramRescueChecks
{
    private const string Enyuan = "boundary:enyuan";
    private const string Chunlao = "classic:chunlao";
    private const string Driver = "fixture:phr-driver";
    private const string HpObserver = "fixture:phr-hp-observer";
    private const string CardObserver = "fixture:phr-card-observer";
    private const string Mode = "identity:classic-participant-hand-program-rescue";
    private const string CostReason = "skill-program.classic:chunlao.UseBoundCardAsDyingAlcohol";

    public static void BoundAlcoholPaidDamageReturnAndColdReplay()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:phr-owner", game.Revision, Prompt(game)!.PromptId)); ReachPlay(game);

        // Prepare a real public Chun pile through the ordinary turn-ending program.
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => Activation(p, Chunlao, "store-chun-at-turn-end"));
        Activate(game, Chunlao, "store-chun-at-turn-end"); Reach(game, p => Action(p, "select-owned-cards"));
        var costId = Prompt(game)!.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards").Cards.Single();
        Answer(game, c => c.Cards.SequenceEqual([costId]));
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "finish-owned-cards");
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].ChunlaoCards is [var stored] && stored.Id == costId &&
            game.State.Players[1].Hp == 1 && game.State.Players[1].IsAlive,
            "A fixed real turn stores one physical Chun before the living one-HP source causes the next damage.");
        Replay(game, registry);

        Accept(game, new UseProgramSkillCommand(0, Driver, "incoming", [], [1], game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => Activation(p, Enyuan, "repay-each-damage")); Activate(game, Enyuan, "repay-each-damage");
        Reach(game, p => Action(p, "select-target")); Answer(game, c => c.Targets.SequenceEqual([1]));
        Reach(game, p => Action(p, "hand-repayment"));
        Require(Prompt(game)!.Choices is [var loss] && loss.Parameters.GetValueOrDefault("repayment") == "lose-hp",
            "The actual spade Slash hand offers no red card for repayment.");
        Answer(game, c => c.Parameters.GetValueOrDefault("repayment") == "lose-hp");
        Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.RescueDying && p.Choices.Any(c =>
            c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Chunlao));
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single(f => f.VictimSeat == 1);
        Require(dying.Continuation == DyingContinuationKind.ProgramSkill && game.State.Players[1].Hp == 0,
            "One actual HP payment enters a typed Dying child before the second original damage point advances.");
        Replay(game, registry); Reject(game);
        Answer(game, c => c.Parameters.GetValueOrDefault("response") == "program-trigger" && c.Parameters.GetValueOrDefault("skill-id") == Chunlao);
        Reach(game, p => Action(p, "select-source-card"));
        Require(Prompt(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "select-source-card").Cards.SequenceEqual([costId]),
            "The ordinary Chunlao provider binds the exact previously stored public entity.");
        Replay(game, registry); Reject(game); Answer(game, c => c.Cards.SequenceEqual([costId]));

        Reach(game, p => p.SkillPrompt?.SkillId == HpObserver);
        var rescue = AssertRescueAncestors(game, dying.Id, costId);
        Require(game.State.Players[1].Hp == 1 && game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(f =>
                f.ResumeFrameId == rescue.Id && f.Change.ParentFrameId == rescue.Id && f.Change.TargetSeat == 1 &&
                f.Continuation == PostEventContinuation.CardUse) && Started(game, Enyuan, "repay-each-damage") == 1,
            "The real recovery pauses on the exact Alcohol use while its paid Enyuan ancestor retains the first damage-point cursor.");
        AssertCostOnce(game, costId); Replay(game, registry); Reject(game); Continue(game);

        Reach(game, p => p.SkillPrompt?.SkillId == CardObserver);
        rescue = AssertRescueAncestors(game, dying.Id, costId);
        var window = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(f => f.ParentFrameId == rescue.Id);
        var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == CardObserver);
        var candidate = window.Candidates[window.CandidateIndex];
        Require(window.Continuation == ProgramCardContinuation.CompletedCard && window.Action.ActionId == rescue.Action!.ActionId &&
            observer.WindowContext is { Window: SkillProgramTriggerWindow.CardUseCompleted, ParentFrameId: var parent } && parent == window.Id &&
            candidate.OwnerSeat == observer.OwnerSeat && candidate.SkillId == observer.SkillId && candidate.TriggerId == observer.TriggerId &&
            candidate.SkillInstanceId == observer.SkillInstanceId && candidate.GameplayHash == observer.GameplayHash &&
            Started(game, Enyuan, "repay-each-damage") == 1,
            "After its HP child, the same paid Alcohol pauses on the exact completed-card candidate without advancing the original damage point.");
        AssertCostOnce(game, costId); Replay(game, registry); Reject(game); Continue(game);

        Reach(game, p => Activation(p, Enyuan, "repay-each-damage"));
        Require(game.Events.Select(e => e.Payload).OfType<DyingResolvedEvent>().Single(e => e.ResolutionId == dying.Id).Survived,
            "The rescued Dying occurrence returns before a fresh candidate is offered for the second damage point.");
        Replay(game, registry); Skip(game); ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<ProgramHandRepaymentResolvedEvent>().Count(e => e.SourceSeat == 1 && e.RecipientSeat == 0 && e.LostHp) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>().Count(e => e.SkillId == Enyuan && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            game.Events.Select(e => e.Payload).OfType<ProgramDyingRescueEvent>().Count(e => e.DyingFrameId == dying.Id && e.SkillId == Chunlao &&
                e.OwnerSeat == 0 && e.VictimSeat == 1 && e.CardId == costId && e.RecoveredHp == 1 && e.VictimHp == 1) == 1 &&
            game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count(e => e.CardId == costId && e.CardKind == CardKind.Alcohol && e.SourceSeat == 1) == 1 &&
            game.Events.Select(e => e.Payload).OfType<CardUseFinishedEvent>().Count(e => e.CardId == costId && e.CardKind == CardKind.Alcohol) == 1 &&
            game.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Single(e => e.SourceSeat == 1 && e.TargetSeat == 0).Amount == 2 &&
            game.CreateSnapshot(0).Players[0].ChunlaoCount == 0 && game.State.Players[1].IsAlive && game.State.Players[1].Hp == 1 &&
            !game.ResolutionStack.Any(f => f is DyingFrame or CardUseFrame or HpChangedTriggerWindowFrame or ProgramCardTriggerWindowFrame) &&
            !game.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.HandRepayment is not null || f.SkillId == Chunlao),
            "Both real child returns finish one HP cost, one public Chun payment and one rescue while preserving the original two-point damage and clearing their owning frames.");
        AssertCostOnce(game, costId); Replay(game, registry);
    }

    private static CardUseFrame AssertRescueAncestors(GameEngine game, long dyingId, int costId)
    {
        var paid = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Enyuan && f.HandRepayment is { Paid: true, LostHp: true, PaidCardId: null });
        var dying = game.ResolutionStack.OfType<DyingFrame>().Single(f => f.Id == dyingId);
        var program = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Chunlao);
        var rescue = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == costId);
        var binding = program.CardSetBindings.Single(b => b.CardIds.SequenceEqual([costId]));
        Require(paid.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied } damage &&
            game.ResolutionStack.OfType<DamageTriggerWindowFrame>().Any(f => f.Id == damage.ParentFrameId) &&
            paid.InstructionIndex == paid.HandRepayment!.InstructionIndex && dying.ParentFrameId == paid.Id && dying.VictimSeat == 1 &&
            program.WindowContext is { Window: SkillProgramTriggerWindow.DyingResponse, ParentFrameId: var parent, TargetSeat: 1 } && parent == dying.Id &&
            program.OwnerSeat == 0 && dying.ResponderSeat == 0 && binding.SourceLocations.SequenceEqual([CardLocation.Chunlao(0)]) &&
            rescue.DyingResponse is null && rescue.SourceSeat == 1 && rescue.CardKind == CardKind.Alcohol && rescue.TargetSeats.SequenceEqual([1]) &&
            rescue.Action is { Type: CardActionType.Use, ActorSeat: 1, ProviderSeat: 0, EffectiveKind: CardKind.Alcohol } action &&
            action.PhysicalCards is [var cost] && cost.CardId == costId && cost.From == CardLocation.Chunlao(0) &&
            action.ConversionChain is [var conversion] && conversion.SkillId == program.SkillId && conversion.OwnerSeat == program.OwnerSeat &&
            conversion.BindingId == program.TriggerId && conversion.SkillInstanceId == program.SkillInstanceId,
            "The paused chain is the exact paid Enyuan → source Dying → responder program → bound-pile Alcohol use, with separate actor and provider provenance.");
        return rescue;
    }
    private static void AssertCostOnce(GameEngine game, int id) => Require(
        game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Chunlao(0) && m.To == CardLocation.Processing && m.Reason.Value == CostReason) == 1 &&
        game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason.Value == CostReason) == 1,
        "Each paused or completed return retains exactly one pile payment and one processing cleanup.");
    private static int Started(GameEngine game, string skill, string binding) => game.Events.Select(e => e.Payload)
        .OfType<ProgramBindingStartedEvent>().Count(e => e.SkillId == skill && e.BindingId == binding && e.OwnerSeat == 0);
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool Action(PendingDecision p, string action) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action);
    private static bool Activation(PendingDecision p, string skill, string binding) => p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "activate" && c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Activate(GameEngine game, string skill, string binding) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate" &&
        c.Parameters.GetValueOrDefault("skill-id") == skill && c.Parameters.GetValueOrDefault("binding-id") == binding);
    private static void Skip(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 150; step++) { var p = Prompt(game); if (p is not null && predicate(p)) return; Advance(game); }
        throw new InvalidOperationException("Fixed bound-Alcohol rescue boundary was not reached: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static void Advance(GameEngine game)
    {
        var p = Prompt(game);
        if (p is { PlayerSeat: 0, Kind: DecisionKind.DiscardCards })
            Accept(game, new DiscardCardsCommand(0, p.ValidCardIds.Take(p.RequiredCardCount).ToArray(), p.PromptId, game.Revision));
        else if (p is { PlayerSeat: 0 } && Action(p, "skip")) Skip(game);
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying }) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "let-die");
        else if (p?.SkillPrompt?.SkillId is HpObserver or CardObserver) Continue(game);
        else Accept(game, new AdvanceOneStepCommand(game.Revision));
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, game.Revision)); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected real command."); }
    private static void Reject(GameEngine game)
    { var before = State(game); var p = Prompt(game)!; Require(!game.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new ChoiceId("unpublished"), game.Revision)).Accepted && State(game) == before, "An unpublished answer cannot alter a paused rescue or repay its cost."); }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics() });
    private static void Replay(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "Four private views, exact paid owning frames, movement facts and real command prefixes cold-restore exactly.");
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-participant-hand-program-rescue", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{Driver}}","revision":1,"activations":[{"id":"incoming","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","sourceRef":{"kind":"selectedTarget"},"amount":2}]}]},
                  {"id":"fixture:phr-quiet","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]},
                  {"id":"{{HpObserver}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"hp-seen","options":[{"id":"continue"}]}]}]},
                  {"id":"{{CardObserver}}","revision":1,"triggers":[{"id":"completed","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["alcohol"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"card-seen","options":[{"id":"continue"}]}]}]}
                ]}
                """, JsonSerializer.Serialize(new { schemaVersion = 3, skills = new Dictionary<string, object>
                { [Driver] = new { name = "真实来源伤害", description = "来自所选实际来源的两点伤害" },
                  ["fixture:phr-quiet"] = new { name = "安静回合", description = "实际回合不使用普通杀" },
                  [HpObserver] = Observer("醇醪实际回复子窗口"), [CardObserver] = Observer("醇醪实际用牌完成子窗口") } }));
            foreach (var id in new[] { Driver, "fixture:phr-quiet", HpObserver, CardObserver })
                builder.AddSkill(new(id, id, "真实付款救援夹具") { Program = catalog.Programs[id] });
            builder.AddSkill(new("fixture:phr-selection", "固定选将", "无运行技能") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            builder.AddGeneral(new("fixture:phr-owner", "真实恩怨醇醪", "supporter", Enyuan, "wu", 3, [Chunlao, Driver]));
            for (var i = 1; i < 4; i++)
                builder.AddGeneral(new($"fixture:phr-target-{i}", "固定实际来源", "supporter", "fixture:phr-selection", "wu", 8,
                    ["fixture:phr-quiet", HpObserver, CardObserver]) { InitialHp = 1 });
            builder.AddDeck(new("fixture:phr-deck", "固定无红无救援牌", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 100)
                .Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实醇醪付款返回", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:phr-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:phr-owner", "fixture:phr-target-1", "fixture:phr-target-2", "fixture:phr-target-3"]));
        }
        private static object Observer(string name) => new { name, description = "真实救援付款后的子窗口", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
    }
}
