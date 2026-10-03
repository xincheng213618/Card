using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundaryPangTongChecks
{
    public static void LianhuanAddsThirdTargetAndRecastsOwnedEquipment()
    {
        var (raw, rawRegistry) = Start("chain");
        var action = raw.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain &&
            a.TargetSeats.SequenceEqual(new[] { 0, 1, 2 }));
        Require(raw.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.IronChain)
            .All(a => a.TargetSeats.Count <= 3), "Lianhuan adds exactly one target to a raw Iron Chain.");
        var before = SnapshotJson.Serialize(raw.CreateSnapshot(0));
        var invalid = raw.Submit(new PlayCardCommand(0, action.CardId!.Value, [0, 1, 2, 3],
            raw.Revision, P(raw)!.PromptId));
        Require(!invalid.Accepted && before == SnapshotJson.Serialize(raw.CreateSnapshot(0)),
            "A fourth target is rejected before payment.");
        Play(raw, action);
        Replay(raw, rawRegistry);
        Reach(raw, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(raw.CreateSnapshot(0).Players.Take(3).All(p => p.IsChained) &&
            !raw.CreateSnapshot(0).Players[3].IsChained,
            "One real raw Iron Chain switches exactly the three declared targets.");

        var (converted, convertedRegistry) = Start("equipment");
        var equip = converted.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
        Play(converted, equip);
        Reach(converted, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var equippedId = converted.CreateSnapshot(0).Players[0].Equipment.Single().Id;
        var equipmentUse = converted.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain &&
            a.CardId == equippedId && a.ConversionSource?.SkillId == "boundary:lianhuan" &&
            a.TargetSeats.SequenceEqual(new[] { 0, 1, 2 }));
        Play(converted, equipmentUse);
        Replay(converted, convertedRegistry);
        Reach(converted, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        Require(converted.CreateSnapshot(0).Players.Take(3).All(p => p.IsChained) &&
            converted.CardMovements.Count(m => m.CardId == equippedId &&
                m.From == CardLocation.Equipment(0) && m.To == CardLocation.Processing) == 1,
            "A club equipment conversion pays its real equipment source once and uses three targets.");

        var (recast, recastRegistry) = Start("equipment");
        Play(recast, recast.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip));
        Reach(recast, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        var armor = recast.CreateSnapshot(0).Players[0].Equipment.Single();
        var recastAction = recast.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Recast &&
            a.CardId == armor.Id && a.ConversionSource?.SkillId == "boundary:lianhuan");
        var handCount = recast.CreateSnapshot(0).Players[0].HandCount;
        var useCount = recast.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count();
        Accept(recast, new RecastCardCommand(0, armor.Id, recast.Revision, P(recast)!.PromptId)
        { ConversionSource = recastAction.ConversionSource });
        Require(recast.CreateSnapshot(0).Players[0].Equipment.Count == 0 &&
            recast.CreateSnapshot(0).Players[0].HandCount == handCount + 1 &&
            recast.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count() == useCount &&
            recast.CardMovements.Count(m => m.CardId == armor.Id &&
                m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1,
            "Equipment recast draws once, pays once and does not declare a trick use.");
        Replay(recast, recastRegistry);
        Conserve(raw); Conserve(converted); Conserve(recast);
    }

    public static void EquipmentRecastAwaitsRecoveryAndMovementChildren() => CheckEquipmentRecastChildren(false);
    public static void ProgramEquipmentRecastAwaitsRecoveryAndMovementChildren() => CheckEquipmentRecastChildren(true);

    private static void CheckEquipmentRecastChildren(bool program)
    {
        foreach (var branch in program ? new[] { "keep", "redirect" } : new[] { "no-policy", "keep", "redirect" })
        {
            var policy = branch != "no-policy";
            var (game, registry) = Start(program ? "recast-program" : policy ? "recast-policy" : "recast-silver");
            Play(game, game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            var before = game.CreateSnapshot(0).Players[0];
            var armor = before.Equipment.Single();
            var lord = game.CreateSnapshot(0, revealAll: true).Players.Single(p => p.Role == Role.Lord).Seat;
            while (policy && game.CreateSnapshot(lord).Players[lord].Hp > before.Hp)
            {
                Accept(game, new UseProgramSkillCommand(0, "fixture:pt-dying", "lower-other", [], [lord],
                    game.Revision, P(game)!.PromptId));
                Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            }
            var lordHp = game.CreateSnapshot(lord).Players[lord].Hp;
            Require(!policy || lordHp <= before.Hp && lordHp < game.CreateSnapshot(lord).Players[lord].MaxHp,
                $"The printed recovery boundary is qualified and wounded after real setup commands: owner={before.Hp}, lord={lordHp}.");
            var uses = game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count();
            var recasts = game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count();
            if (program)
                Accept(game, new UseProgramSkillCommand(0, "classic:huairou", "recast-equipment", [armor.Id], [],
                    game.Revision, P(game)!.PromptId));
            else
            {
                var action = game.GetHumanLegalActions().Single(a => a.Kind == LegalActionKind.Recast && a.CardId == armor.Id);
                Accept(game, new RecastCardCommand(0, armor.Id, game.Revision, P(game)!.PromptId)
                { ConversionSource = action.ConversionSource });
            }
            if (policy)
            {
                Reach(game, p => p.Kind == DecisionKind.RecoveryReplacement);
                Require(game.CreateSnapshot(0).Players[0].Hp == before.Hp &&
                    game.CreateSnapshot(0).Players[0].HandCount == before.HandCount &&
                    game.ResolutionStack.OfType<RecoveryReplacementFrame>().Single() is { } recovery &&
                    (program
                        ? game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Id == recovery.ParentFrameId) is
                            { SkillId: "classic:huairou", InstructionIndex: 1, EquipmentRecast.DrawApplied: false } &&
                          recovery.Return.Continuation == PostEventContinuation.Program
                        : recovery.ParentFrameId == game.ResolutionStack.OfType<EquipmentRecastFrame>().Single().Id &&
                          recovery.Return.Continuation == PostEventContinuation.EquipmentRecast),
                    "Equipment recast freezes its one paid armor cost before the exact replacement child and recast draw.");
                Replay(game, registry);
                Answer(game, P(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("recovery-action") == branch).Id);
            }
            Reach(game, p => p.SkillPrompt?.SkillId == "fixture:pt-recovery-child");
            Require(game.CreateSnapshot(0).Players[0].HandCount == before.HandCount &&
                game.CreateSnapshot(0).Players[0].Hp == before.Hp + (branch == "redirect" ? 0 : 1) &&
                game.CreateSnapshot(lord).Players[lord].Hp == lordHp + (branch == "redirect" || lord == 0 ? 1 : 0) &&
                game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count() == recasts,
                "The actual Silver Lion HP observer precedes replacement rewards and the paid recast tail.");
            Replay(game, registry); Answer(game, P(game)!.Choices.Single().Id);
            if (branch == "redirect")
            {
                Reach(game, p => p.SkillPrompt?.SkillId == "fixture:pt-recast-gain");
                Require(game.CreateSnapshot(0).Players[0].HandCount == before.HandCount + 1 &&
                    game.ResolutionStack.OfType<RecoveryReplacementFrame>().Single().Stage == RecoveryReplacementStage.RewardApplied &&
                    game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count() == recasts,
                    "The replacement reward movement returns before the recast draws its own card.");
                Replay(game, registry); Answer(game, P(game)!.Choices.Single().Id);
            }
            Reach(game, p => p.SkillPrompt?.SkillId == "fixture:pt-recast-gain");
            Require((program
                    ? game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "classic:huairou") is
                        { InstructionIndex: 1, EquipmentRecast: { DrawApplied: true, DrawCount: 1 } }
                    : game.ResolutionStack.OfType<EquipmentRecastFrame>().Single() is { DrawApplied: true, DrawCount: 1 }) &&
                game.CreateSnapshot(0).Players[0].HandCount == before.HandCount + (branch == "redirect" ? 2 : 1) &&
                game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count() == recasts + 1,
                "The recast draw owns a separate movement return and records its once-only scalar receipt before suspension.");
            Replay(game, registry); Answer(game, P(game)!.Choices.Single().Id);
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            Require(game.CardMovements.Count(m => m.CardId == armor.Id && m.From == CardLocation.Equipment(0) &&
                m.To == CardLocation.DiscardPile) == 1 &&
                game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count() == recasts + 1 &&
                game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Single(e => e.CardId == armor.Id).CardKind ==
                    (program ? CardKind.SilverLion : CardKind.IronChain) &&
                game.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Count() == uses &&
                !game.ResolutionStack.OfType<EquipmentRecastFrame>().Any(),
                "All child returns retain one physical payment, one recast draw and no card-use declaration.");
            Replay(game, registry); Conserve(game);
        }
    }

    public static void NiepanRestoresStateChoosesSkillAndStaysLimited()
    {
        foreach (var option in new[] { "bazhen", "huoji", "kanpo" })
        {
            var (game, registry) = Start("dying");
            Play(game, game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            Play(game, game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Lightning));
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            var ownerBefore = game.CreateSnapshot(0).Players[0];
            Require(ownerBefore.Equipment.Single().Kind == CardKind.SilverLion && ownerBefore.Judgment.Count == 1,
                "The fixed fixture enters Niepan with real armor and judgment cards.");
            var discardedIds = ownerBefore.Hand.Concat(ownerBefore.Equipment).Concat(ownerBefore.Judgment)
                .Select(c => c.Id).ToArray();
            InvokeDying(game);
            Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:niepan"));
            var activation = P(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:niepan");
            var hpBeforeNiepan = game.CreateSnapshot(0).Players[0].Hp;
            Require(hpBeforeNiepan == 0, "The shared real LoseHp operation clamps this fixture's lethal loss at zero before Niepan.");
            Require(game.Events.Select(e => e.Payload).OfType<ProgramSkillHpLostEvent>()
                .Single(e => e.SkillId == "fixture:pt-dying") is { RemainingHp: 0 } loss && loss.Amount == ownerBefore.Hp,
                "The lethal program reports only the four actual HP lost, rather than its raw eight-point request.");
            Replay(game, registry);
            Answer(game, activation.Id);
            Reach(game, p => p.SkillPrompt?.SkillId == "fixture:pt-recovery-child");
            var ownerAtDiscard = game.CreateSnapshot(0).Players[0];
            Require(ownerAtDiscard.IsFaceDown && ownerAtDiscard.IsChained,
                $"Silver Lion's child suspends restoration: faceDown={ownerAtDiscard.IsFaceDown}, chained={ownerAtDiscard.IsChained}.");
            Require(ownerAtDiscard.HandCount == 0 && ownerAtDiscard.Equipment.Count == 0 && ownerAtDiscard.Judgment.Count == 0,
                $"All HEJ costs finish before Silver Lion's child: hand={ownerAtDiscard.HandCount}, equipment={ownerAtDiscard.Equipment.Count}, judgment={ownerAtDiscard.Judgment.Count}.");
            Require(ownerAtDiscard.Hp == hpBeforeNiepan + 1,
                $"Silver Lion applies its one actual recovery before the observer: before={hpBeforeNiepan}, expected={hpBeforeNiepan + 1}, actual={ownerAtDiscard.Hp}.");
            var paidNiepan = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:niepan");
            Require(paidNiepan.InstructionIndex == 1,
                $"The paid discard retains Niepan's next instruction: expected=1, actual={paidNiepan.InstructionIndex}.");
            Require(game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Any(),
                "Silver Lion's real HP observer child stays above the paid Niepan frame.");
            Replay(game, registry);
            Answer(game, P(game)!.Choices.Single().Id);
            Reach(game, p => p.SkillPrompt?.SkillId == "boundary:niepan" &&
                p.Choices.Any(c => c.Parameters.GetValueOrDefault("result-bind") == "reborn-skill"));
            var owner = game.CreateSnapshot(0).Players[0];
            Require(owner is { Hp: 3, IsFaceDown: false, IsChained: false, HandCount: 3,
                Equipment.Count: 0, Judgment.Count: 0 },
                "Niepan discards all owned zones, restores both orientation states, draws three and recovers to three before its choice.");
            Require(discardedIds.All(id => game.CardMovements.Count(m => m.CardId == id &&
                m.From.OwnerSeat == 0 && m.To == CardLocation.DiscardPile) == 1),
                "Every original HEJ card leaves its real owned zone exactly once.");
            Require(game.Events.Select(e => e.Payload).OfType<SilverLionRemovedRecoveryEvent>()
                .Count(e => e.PlayerSeat == 0 && e.RecoveredAmount == 1) == 1,
                "Niepan's armor payment resolves the actual Silver Lion recovery once.");
            Require(game.ResolutionStack.OfType<ProgramSkillFrame>().Last() is
                { SkillId: "boundary:niepan", InstructionIndex: 6 },
                "The acquisition choice retains the owning paid self-dying program cursor.");
            Replay(game, registry);
            var selection = P(game)!.Choices.Single(c => c.Parameters.GetValueOrDefault("option-id") == option);
            Answer(game, selection.Id);
            Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
            var acquired = game.CreateSnapshot(0).Players[0].Skills!
                .Where(s => new[] { "classic:bazhen", "classic:huoji", "classic:kanpo" }.Contains(s.ContentId))
                .Select(s => s.ContentId).ToArray();
            Require(acquired.SequenceEqual(new[] { "classic:" + option }) &&
                game.Events.Select(e => e.Payload).OfType<SkillsAcquiredEvent>()
                    .Single(e => e.SourceSkillId == "boundary:niepan").SkillIds.SequenceEqual(acquired),
                "Each selected branch grants exactly its existing functional skill definition.");
            Require(game.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>()
                .Count(e => e.SkillId == "boundary:niepan" && e.Scope == SkillUsageScope.Game) == 1,
                "The limited mark is consumed exactly once before the suspended choice.");
            Replay(game, registry);
            InvokeDying(game);
            for (var step = 0; step < 80 && game.State.Status != EngineStatus.Completed; step++)
            {
                var pending = P(game);
                Require(pending?.Choices.All(c => c.Parameters.GetValueOrDefault("skill-id") != "boundary:niepan") != false,
                    "A second real dying occurrence cannot offer the exhausted limited skill.");
                if (pending is { Kind: DecisionKind.RescueDying })
                    Answer(game, pending.Choices.Single(c => c.Parameters.GetValueOrDefault("response") == "let-die").Id);
                else Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
            Require(game.State.Status == EngineStatus.Completed && !game.CreateSnapshot(0).Players[0].IsAlive &&
                game.CreateSnapshot(0).Players[0].Hp == 0 &&
                game.Events.Select(e => e.Payload).OfType<PlayerDiedEvent>().Count(e => e.VictimSeat == 0) == 1 &&
                game.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>()
                    .Count(e => e.SkillId == "boundary:niepan" && e.Scope == SkillUsageScope.Game) == 1 &&
                game.Events.Select(e => e.Payload).OfType<SkillsAcquiredEvent>()
                    .Count(e => e.SourceSkillId == "boundary:niepan") == 1,
                "The second lethal command resolves the real lord death without another Niepan payment or skill grant.");
            Replay(game, registry);
            Conserve(game);
        }
    }

    private static void InvokeDying(GameEngine game) => Accept(game,
        new UseProgramSkillCommand(0, "fixture:pt-dying", "enter", [], [], game.Revision, P(game)!.PromptId));

    private static (GameEngine, ContentRegistry) Start(string mode)
    {
        var definitions = ContentRegistry.Build(new StandardContentPackage(),
            new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage());
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(definitions, mode));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = mode is "recast-policy" or "recast-program" ? Role.Loyalist : Role.Lord,
            ModeId = "identity:classic-boundary-pang-tong-check", UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:pang-tong", game.Revision, P(game)!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
        return (game, registry);
    }

    private static PendingDecision? P(GameEngine game) => game.PendingDecision ??
        Enumerable.Range(0, 4).Select(v => game.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Answer(GameEngine game, ChoiceId choice) =>
        Accept(game, new AnswerPromptCommand(P(game)!.PlayerSeat, P(game)!.PromptId, choice, game.Revision));
    private static void Play(GameEngine game, LegalAction action) => Accept(game,
        new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, P(game)!.PromptId,
            action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var i = 0; i < 80; i++)
        {
            if (P(game) is { } prompt && predicate(prompt)) return;
            if (P(game) is { PlayerSeat: 0, Kind: DecisionKind.Nullification } nullification)
                Answer(game, nullification.Choices.First(c => c.Cards.Count == 0).Id);
            else if (P(game) is { SkillPrompt.SkillId: "fixture:pt-recovery-child" or "fixture:pt-recast-gain" } child)
                Answer(game, child.Choices.Single().Id);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Pang Tong fixture did not reach its requested prompt: " + P(game)?.Prompt);
    }
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(-1, 5).All(v => SnapshotJson.Serialize(game.CreateSnapshot(v)) ==
                SnapshotJson.Serialize(restored.CreateSnapshot(v))) &&
            game.CardMovements.SequenceEqual(restored.CardMovements) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack),
            "Cold replay preserves each player view, spectator privacy, exact movement history and typed owning frames.");
        Require(Enumerable.Range(0, 4).All(v => game.CreateSnapshot(v).Players.Where(p => p.Seat != v)
            .All(p => p.Hand.Count == 0)), "Other players' hand identities remain private.");
    }
    private static void Conserve(GameEngine game) => Require(game.CreateCardZoneDiagnostics()
        .Select(c => c.CardId).Distinct().Count() == 80, "All fixture physical entities are conserved.");
    private static void Require(bool value, string message)
    { if (!value) throw new InvalidOperationException(message); }

    private sealed class Fixture(ContentRegistry definitions, string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-boundary-pang-tong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { "boundary:lianhuan", "boundary:niepan", "classic:bazhen", "classic:huoji", "classic:kanpo" })
                builder.AddSkill(definitions.GetSkill(id));
            var recoveryPolicy = mode is "recast-policy" or "recast-program";
            if (recoveryPolicy) builder.AddSkill(definitions.GetSkill("boundary:jiuyuan"));
            if (mode == "recast-program") builder.AddSkill(definitions.GetSkill("classic:huairou"));
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:pt-dying","revision":1,
                "activations":[{"id":"enter","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,
                "targetKind":"anyLiving","usesPerTurn":2,"effects":[
                {"op":"setChainedState","target":"owner","chained":true},
                {"op":"setFaceState","target":"owner","faceDown":true},
                {"op":"loseHp","target":"owner","amount":8}]},
                {"id":"lower-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,
                "targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}]},
                {"id":"fixture:pt-recovery-child","revision":1,"triggers":[{"id":"real-recovery-child",
                "window":"afterHpRecovered","subject":"owner","optional":false,"effects":[
                {"op":"chooseOption","target":"owner","resultBind":"recovery-observed","options":[{"id":"continue"}]}]}]},
                {"id":"fixture:pt-recast-gain","revision":1,"triggers":[{"id":"draw-child","window":"cardsGained",
                "subject":"owner","optional":false,"destinationZones":["hand"],"movementOccurrence":"perBatch","effects":[
                {"op":"chooseOption","target":"owner","resultBind":"gain-observed","options":[{"id":"continue"}]}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:pt-dying":{"name":"进入濒死","description":"设置真实角色状态并失去体力。"},"fixture:pt-recovery-child":{"name":"真实回复子窗口","description":"暂停真实回复观察。","optionLabels":{"continue":"继续结算"}},"fixture:pt-recast-gain":{"name":"真实重铸摸牌子窗口","description":"暂停真实摸牌观察。","optionLabels":{"continue":"继续结算"}}}}""");
            builder.AddSkill(new("fixture:pt-dying", "进入濒死", "真实濒死边界")
            { Program = catalog.Programs["fixture:pt-dying"] });
            builder.AddSkill(new("fixture:pt-recovery-child", "真实回复子窗口", "暂停真实回复观察")
            { Program = catalog.Programs["fixture:pt-recovery-child"] });
            builder.AddSkill(new("fixture:pt-recast-gain", "真实摸牌子窗口", "暂停真实摸牌观察")
            { Program = catalog.Programs["fixture:pt-recast-gain"] });
            builder.AddSkill(new("fixture:pt-selection", "固定其他选将", "保留人工机制角色")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            var recast = mode.StartsWith("recast-", StringComparison.Ordinal);
            builder.AddGeneral(new("fixture:pang-tong", "界庞统机制", "pang_tong", "boundary:lianhuan", recoveryPolicy ? "wu" : "shu", recast ? 4 : 3,
                mode == "recast-program" ? ["fixture:pt-dying", "fixture:pt-recovery-child", "fixture:pt-recast-gain", "classic:huairou"] :
                recast ? ["fixture:pt-dying", "fixture:pt-recovery-child", "fixture:pt-recast-gain"] : mode == "dying" ? ["boundary:niepan", "fixture:pt-dying", "fixture:pt-recovery-child"]
                    : ["boundary:niepan", "fixture:pt-dying"]) { InitialHp = recast ? recoveryPolicy ? 3 : 1 : null });
            for (var i = 1; i < 4; i++)
                builder.AddGeneral(new($"fixture:pt-other-{i}", "其他" + i, "supporter", "fixture:pt-selection", "qun", 4,
                    recoveryPolicy ? ["boundary:jiuyuan", "fixture:pt-recovery-child"] : [])
                { InitialHp = recoveryPolicy ? 1 : null });
            builder.AddCard(new("fixture:pt-chain", "铁索连环", "锦囊牌", "固定铁索", CardKind.IronChain));
            builder.AddCard(new("fixture:pt-equipment", "诸葛连弩", "装备牌", "固定梅花装备", CardKind.Crossbow));
            builder.AddCard(new("fixture:pt-lion", "白银狮子", "装备牌", "真实移牌恢复", CardKind.SilverLion));
            builder.AddCard(new("fixture:pt-lightning", "闪电", "锦囊牌", "真实判定区牌", CardKind.Lightning));
            builder.AddDeck(new("fixture:pt-deck", "固定实体", mode == "dying" ? 8 : 4, 0, [])
            {
                PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard(
                    recast ? "fixture:pt-lion" : mode == "chain" ? "fixture:pt-chain" : mode == "dying"
                        ? i % 2 == 0 ? "fixture:pt-lion" : "fixture:pt-lightning" : "fixture:pt-equipment",
                    Suit.Club, i % 13 + 1)).ToArray()
            });
            builder.AddMode(new("identity:classic-boundary-pang-tong-check", "界庞统机制", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:pt-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:pang-tong", "fixture:pt-other-1", "fixture:pt-other-2", "fixture:pt-other-3"]));
        }
    }
}
