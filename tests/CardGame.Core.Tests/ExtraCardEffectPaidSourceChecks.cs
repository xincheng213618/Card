using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ExtraCardEffectPaidSourceChecks
{
    private const string Feng = "ol:fengzi", Driver = "fixture:extra-paid-driver", Watch = "fixture:extra-paid-watch";
    private const string Loss = "fixture:extra-paid-loss", Quiet = "fixture:extra-paid-quiet", Owner = "fixture:extra-paid-owner";
    private const string Mode = "identity:classic-extra-paid-source";
    public static void PaidHandCostSourceLossAndOwnerDeathKeepOriginalUseOnce()
    {
        foreach (var death in new[] { false, true })
        {
            var (g, registry) = Start(death);
            var slash = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.CardId is not null && a.TargetSeats.Count == 1);
            var material = slash.CardId!.Value; var target = slash.TargetSeats.Single(); var hp = V(g, target).Hp;
            Accept(g, new PlayCardCommand(0, material, slash.TargetSeats, g.Revision, P(g)!.PromptId, slash.PlayedCardKind, slash.TargetCardId)
                { ConversionSource = slash.ConversionSource, AdditionalConversionSources = slash.AdditionalConversionSources });
            Reach(g, () => Optional(P(g), Feng)); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "cost") == true);
            var payment = P(g)!.Choices.First(c => c.Parameters.GetValueOrDefault("branch") == "cost").Cards.Single();
            Need(payment != material && V(g, 0).Hand.Any(c => c.Id == payment), "The Fengzi cost is another actual same-category Hand entity, distinct from the paid Slash material.");
            Answer(g, c => c.Cards.SequenceEqual([payment]));
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
            var root = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ExtraEffectsAndRankGuess is not null);
            var receipt = root.ExtraEffectsAndRankGuess!; var useId = receipt.CardUseFrameId!.Value; var actionId = receipt.ActionId!.Value;
            Need(receipt is { Stage: ExtraEffectsAndRankGuessStage.MovementChildren } && receipt.PaidCardId == payment && root.PendingMovementContinuation is not null &&
                g.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Any(w => w.Batch.ParentFrameId == root.Id && w.Batch.AwaitingProgramFrameId == root.Id &&
                    w.Batch.Movements.Any(m => m.CardId == payment && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile &&
                        m.Reason.Value == "skill-program.extra-effect.cost")) &&
                g.CreateCardZoneDiagnostics().Single(z => z.CardId == material).Location == CardLocation.Processing,
                "A genuine CardsMoved observer suspends the already-paid exact cost while the original use and Slash material remain live.");
            Need(E<ExtraCardEffectIssuedEvent>(g).Length == 0 && E<DamageAppliedEvent>(g).Length == 0,
                "No extra-effect issue or original damage runs before its paid movement child returns.");
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(g, () => E<CardUseFinishedEvent>(g).Any(e => e.ResolutionId == useId));
            var issues = E<ExtraCardEffectIssuedEvent>(g).Where(e => e.CardUseFrameId == useId).ToArray();
            var effects = E<ExtraCardEffectStartedEvent>(g).Where(e => e.CardUseFrameId == useId).ToArray();
            var usages = E<SkillUsageConsumedEvent>(g).Where(e => e.SkillId == Feng).ToArray();
            var damages = E<DamageAppliedEvent>(g).Where(e => e.TargetSeat == target).ToArray();
            Need(issues is [{ ProgramFrameId: var program, ActionId: var issuedAction, PaidCardId: var paid }] && program == root.Id && issuedAction == actionId && paid == payment &&
                effects is [{ ActionId: var extraAction }] && extraAction == actionId &&
                usages is [{ SkillOwnerSeat: 0, Scope: SkillUsageScope.Phase, Count: 1 }] &&
                E<ProgramBindingStartedEvent>(g).Count(e => e.FrameId == root.Id && e.SkillId == Feng) == 1,
                "The immutable paid source issues one extra round and consumes its original actual phase quota exactly once after the source invalidation.");
            Need(damages.Length == 2 && damages.All(d => d.SourceSeat == 0 && d.Amount == 1) && V(g, target).Hp == hp - 2 && V(g, target).IsAlive,
                "The original Slash and its one extra effect still each resolve one real native damage against the surviving actual target.");
            Need(E<CardActionAcceptedEvent>(g).Count(e => e.Action.ActionId == actionId && e.Action.Type == CardActionType.Use) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId) == 1 &&
                g.CardMovements.Count(m => m.CardId == payment && m.From == CardLocation.Hand(0) && m.To == CardLocation.DiscardPile && m.Reason.Value == "skill-program.extra-effect.cost") == 1 &&
                g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Use) == 1 &&
                g.CardMovements.Count(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.UseFinished) == 1 &&
                g.CreateCardZoneDiagnostics().Single(z => z.CardId == material).Location == CardLocation.DiscardPile,
                "Cold child return never invents a second Use, discards the same-category cost twice, or settles the original Slash material twice.");
            Need(death ? !V(g, 0).IsAlive && g.State.Winner == Winner.None : V(g, 0).IsAlive && !V(g, 0).Skills!.Any(s => s.ContentId == Feng),
                "The test genuinely kills the non-Lord paid owner while the game continues, or removes the Fengzi grant after actual Dying recovery.");
            g = Cold(g, registry);
            Need(E<ExtraCardEffectIssuedEvent>(g).Count(e => e.CardUseFrameId == useId) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == useId) == 1,
                "A final command replay retains the completed original use and its single paid extra-effect issuance.");
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Optional(PendingDecision? p, string skill) => p?.SkillPrompt?.SkillId == skill && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Answer(GameEngine g, Func<PromptChoice, bool> pick) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(pick).Id, g.Revision)); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected actual paid extra-effect command."); }
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var step = 0; step < 180; step++)
        {
            if (done()) return; var p = P(g);
            Need(g.State.Winner == Winner.None, "The small paid extra-effect fixture ended before its exact use completed.");
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip"))
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.RescueDying or DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification })
                Answer(g, c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard })
                throw new InvalidOperationException("Paid extra effect returned to Play before its expected child/complete boundary.");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed paid-source fixture exceeded its small actual command bound: " + JsonSerializer.Serialize(new { Prompt = P(g), Frames = g.ResolutionStack }));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), g.CardMovements,
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), Commands = CommandJson.Serialize(g.AcceptedCommands)
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(copy), "Cold command replay preserves every viewer, exact paid cost, original material, owning child cursor and committed fact."); return copy;
    }
    private static void Need(bool condition, string why) { if (!condition) throw new InvalidOperationException(why); }
    private static string Embedded(string suffix)
    { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-wang-rong." + suffix)!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
    private static (GameEngine, ContentRegistry) Start(bool death)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(death));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = death ? Role.Rebel : Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        return (g, registry);
    }
    private sealed class Fixture(bool death) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("extra-paid-source-fixture", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            void Register(SkillProgramCatalog catalog)
            { foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = id == Quiet ? Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) : null }); }
            Register(SkillProgramCatalog.Load(Embedded("rules.json"), Embedded("presentation.json")));
            var loss = death ? "" : $$"""
            ,{"id":"{{Loss}}","revision":1,"triggers":[{"id":"actual-source-loss","window":"dyingEntering","subject":"owner","usageScope":"game","usageLimit":1,"optional":false,"effects":[
              {"op":"recoverTo","target":"owner","numberExpression":"integerConstant","minimumValue":8,"clampToMaxHp":true},
              {"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{Feng}}"],"sourceBind":"standard:none"}]}]}
            """;
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"actual-paid-cost-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.extra-effect.cost"],"usageScope":"game","usageLimit":1,"optional":false,"effects":[
                {"op":"chooseOption","target":"owner","resultBind":"child","options":[{"id":"continue"}]},
                {"op":"loseHp","target":"owner","amount":20}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"drawPhaseStarting","subject":"owner","optional":false,"effects":[{"op":"grantTurnRuleModifier","target":"owner","ruleQuery":"slashLimit","ruleOperation":"add","amount":-20}]}]}
              {{loss}}
            ]}
            """;
            var labels = new Dictionary<string, object> { [Watch] = new { name = "实际已付子窗", description = "真实弃牌后暂停及失体力", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } },
                [Quiet] = new { name = "固定其他角色", description = "实际其他角色不主动杀" } };
            if (!death) labels[Loss] = new { name = "实际失源", description = "真实濒死回复并移除丰姿" };
            Register(SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels })));
            b.AddSkill(new(Driver, "固定测试拥有者", "保留真实拥有者给人类公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -10000d) });
            b.AddGeneral(new(Owner, "真实额外效果拥有者", "supporter", Driver, "qun", 8, death ? [Feng, Watch] : [Feng, Watch, Loss]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:extra-paid-peer-{i}").ToArray();
            foreach (var peer in peers) b.AddGeneral(new(peer, "实际其他角色", "supporter", Quiet, "wei", 8));
            b.AddDeck(new("fixture:extra-paid-deck", "固定真实杀实体", 4, 0, [])
            { PhysicalCards = Enumerable.Range(0, 48).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(Mode, "实际丰姿支付后失源", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:extra-paid-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
