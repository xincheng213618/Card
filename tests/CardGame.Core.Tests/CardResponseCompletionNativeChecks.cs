using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardResponseCompletionNativeChecks
{
    private const string Mode = "identity:classic-response-completion-native";
    private const string Driver = "fixture:response-completion-native-driver";
    private const string Zhefu = "ol:zhefu";

    public static void NativeZhangbaDuelResponseCompletesTwoEntitiesOnceAndColdReturns()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, "fixture:response-completion-native-owner", game.Revision, Prompt(game)!.PromptId));
        Play(game);
        Require(View(game, 1) is { GeneralId: "fixture:response-completion-native-peer-1", Hp: 4, MaxHp: 4 },
            "Native role-weighted selection places the formal completion observer at seat1 with real 4 HP.");
        var ownerHp = View(game, 0).Hp;
        var weapon = View(game, 0).Hand.First(c => c.Kind == CardKind.ZhangbaSerpentSpear).Id;
        Use(game, "equip-other", [weapon], [1]); Play(game);
        Require(View(game, 1).Equipment is [{ Id: var equipped, Kind: CardKind.ZhangbaSerpentSpear }] && equipped == weapon,
            "A selected physical Hand entity really enters the NPC's native weapon slot.");
        Use(game, "wound-other", [], [1]); Play(game);
        Require(View(game, 1).Hp == 2 && Facts<ProgramSkillHpLostEvent>(game).Any(e => e.SkillId == Driver && e.TargetSeat == 1) &&
            View(game, 1).Hand.Count == 4 && View(game, 1).Hand.All(c => c.Kind == CardKind.ZhangbaSerpentSpear),
            "Legal HP loss2 makes the native Duel AI prefer its available weapon conversion; its Hand contains no actual Slash.");

        var duel = game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver &&
            a.ConversionSource.BindingId == "duel" && a.TargetSeats.SequenceEqual([1]));
        Accept(game, new PlayCardCommand(0, duel.CardId!.Value, duel.TargetSeats, game.Revision, Prompt(game)!.PromptId,
            duel.PlayedCardKind) { ConversionSource = duel.ConversionSource });
        Reach(game, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondSlash });
        var nativePrompt = Prompt(game)!;
        var publishedPairs = nativePrompt.Choices.Where(c => c.Parameters.GetValueOrDefault("response") == "zhangba-slash")
            .Select(c => c.Cards.ToArray()).ToArray();
        var parent = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Duel);
        var parentId = parent.Id; var parentActionId = parent.Action!.ActionId;
        Require(publishedPairs.Length > 0 && publishedPairs.All(pair => pair.Length == 2 && pair.Distinct().Count() == 2) &&
            !nativePrompt.Choices.Any(c => c.Cards.Count == 1) && Facts<CardResponseCompletedEvent>(game).Length == 0,
            "The actual private NPC response publishes two-entity Zhangba choices and no single-card Slash or premature completion.");
        Private(game);
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => p.SkillPrompt?.SkillId == Zhefu && p.PlayerSeat == 1 && p.Choices.Any(IsActivate));

        var response = Facts<CardActionAcceptedEvent>(game).Single(e => e.Action.Type == CardActionType.Response).Action;
        var pairIds = response.PhysicalCards.Select(c => c.CardId).ToArray();
        var converted = Facts<ZhangbaSerpentSpearConvertedEvent>(game).Single();
        var completion = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.ResponseCompletion is not null);
        var returned = completion.ResponseCompletion!;
        Require(response is { ActorSeat: 1, ProviderSeat: 1, RequesterSeat: null, ResponderSeat: 1,
                OpponentSeat: 0, EffectiveKind: CardKind.Slash, PhysicalCards.Count: 2, ConversionChain.Count: 0 } &&
            response.ParentActionId == parentActionId && response.TargetSeats.Count == 0 &&
            response.PhysicalCards.All(c => c.From == CardLocation.Hand(1) && c.CardKind == CardKind.ZhangbaSerpentSpear) &&
            publishedPairs.Any(p => p.SequenceEqual(pairIds)) && converted.ResolutionId == parentId &&
            converted is { UserSeat: 1, IsUse: false, TargetSeat: 0 } && converted.PhysicalCardIds.SequenceEqual(pairIds) &&
            !Facts<CardActionAcceptedEvent>(game).Any(e => e.Action.ActorSeat == 1 && e.Action.Type == CardActionType.Use) &&
            !game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c => c.PromptId == nativePrompt.PromptId),
            "One real AI Advance accepts the native Zhangba Response with its two original Hand costs; it never fabricates a basic-card Use or answers the NPC's response manually.");
        Require(returned is { CostsDrained: true, CompletionActorSeat: 1, OriginalContinuation: ProgramCardContinuation.DuelSlash,
                NativeCosts.Count: 4, ActiveCostChildFrameId: null, ActiveHealthChildFrameId: null } &&
            returned.ActionId == response.ActionId && returned.ParentFrameId == parentId && returned.AttackOwnerFrameId == parentId &&
            completion.ParentFrameId == parentId && completion.Action.ActionId == response.ActionId &&
            Facts<CardResponseCompletedEvent>(game) is [{ ActorSeat: 1, ProviderSeat: 1, NativeActorSeat: 1,
                EffectiveKind: CardKind.Slash, OriginalContinuation: ProgramCardContinuation.DuelSlash } completed] &&
            completed.ActionId == response.ActionId && completed.ParentFrameId == parentId && completed.AttackOwnerFrameId == parentId &&
            completed.ActualTurnOwnerSeat == 0 &&
            game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == parentId).Continuations.Duel is
                { Active: true, ResponderSeat: 1, SuccessfulSlashResponses: 0 } &&
            Facts<DuelResponseEvent>(game) is [{ ResponderSeat: 1, UsedSlash: true } firstResponse] && firstResponse.ResolutionId == parentId &&
            !Facts<CardUseFinishedEvent>(game).Any(e => e.ResolutionId == parentId),
            "Both native costs finish before the once-only response-completion window, while the exact Duel cursor still owes its original continuation.");
        AssertPairPayment(game, pairIds, returned.NativeCosts);
        Frozen(response.PhysicalCards); Frozen(response.TargetSeats); Frozen(response.ConversionChain);
        Frozen(converted.PhysicalCardIds); Frozen(returned.NativeCosts); Frozen(returned.CostBatches);
        foreach (var batch in returned.CostBatches) { Frozen(batch.Movements); Frozen(batch.SourceCounts); }
        Private(game); Answer(game, IsActivate);
        Reach(game, p => Demand(p, "target"));
        var binding = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SameNameHandDiscardOrDamage is not null);
        var receipt = binding.SameNameHandDiscardOrDamage!; var bindingId = binding.Id;
        Require(receipt is { Stage: SameNameHandStage.ChoosingTarget, EffectiveKind: CardKind.Slash,
                NormalizedName: CardKind.Slash, ActualTurnOwnerSeat: 0 } && receipt.Source.OwnerSeat == 1 &&
            receipt.Source.SkillId == Zhefu && receipt.ActionId == response.ActionId && receipt.CardWindowId == completion.Id &&
            receipt.OriginalParentFrameId == parentId && binding.WindowContext?.ParentFrameId == completion.Id &&
            Facts<SameNameHandStartedEvent>(game) is [{ FrameId: var startedId }] && startedId == bindingId,
            "Formal Zhefu owns the precise completed Response, completion window and original Duel parent, rather than a fake Slash-use parent.");
        Frozen(receipt.CandidateSeats); Frozen(receipt.EligibleMaterials); Private(game);
        game = Cold(game, registry);
        Answer(game, c => c.Targets.SequenceEqual([0]));
        Reach(game, p => Demand(p, "damage"));
        Require(Prompt(game) is { PlayerSeat: 0, ValidCardIds.Count: 0 } && Prompt(game)!.Choices.All(c => c.Cards.Count == 0),
            "The target's actual non-Slash Hand cannot pay a same-name Slash discard; only native damage is published.");
        Private(game); Answer(game, c => c.Parameters.GetValueOrDefault("step") == "damage");
        Play(game);

        AssertPairPayment(game, pairIds, returned.NativeCosts);
        Require(Facts<CardActionAcceptedEvent>(game).Count(e => e.Action.ActionId == response.ActionId) == 1 &&
            Facts<CardResponseCompletionStartedEvent>(game).Count(e => e.ActionId == response.ActionId) == 1 &&
            Facts<CardResponseCompletedEvent>(game).Count(e => e.ActionId == response.ActionId) == 1 &&
            Facts<SameNameHandStartedEvent>(game).Count(e => e.ActionId == response.ActionId) == 1 &&
            Facts<SameNameHandDamageIssuedEvent>(game) is [{ FrameId: var damageId, SourceSeat: 1, TargetSeat: 0, Amount: 1 }] && damageId == bindingId &&
            Facts<SameNameHandCompletedEvent>(game) is [{ FrameId: var completedId, TargetSeat: 0, Discarded: false, DamageIssued: true }] && completedId == bindingId &&
            Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == bindingId && e.Completed) == 1 &&
            Facts<DuelResponseEvent>(game).Count(e => e.ResolutionId == parentId && e.ResponderSeat == 1 && e.UsedSlash) == 1 &&
            Facts<DuelResponseEvent>(game).Count(e => e.ResolutionId == parentId && e.ResponderSeat == 0 && !e.UsedSlash) == 1 &&
            Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == parentId) == 1 &&
            Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 1 && e.TargetSeat == 0 && e.Amount == 1) == 2 &&
            View(game, 0).Hp == ownerHp - 2 && View(game, 1).Hp == 2 && View(game, 1).Equipment.Single().Id == weapon &&
            View(game, 1).Hand.Count == 2 && !game.ResolutionStack.Any(f => f.Id == parentId || f.Id == completion.Id || f.Id == bindingId) &&
            !Facts<CardSupplyCompletedEvent>(game).Any(),
            "Cold Zhefu returns once to the native Duel: its damage and the original failed reply each apply once, with no repeated pair payment, fake supply completion or stranded frame.");
    }

    private static void AssertPairPayment(GameEngine game, int[] pair, IReadOnlyList<CardMovementRecord> invoice)
    {
        Require(pair.Length == 2 && pair.Distinct().Count() == 2 && invoice.Count == 4 && invoice.Select(m => m.Sequence).Distinct().Count() == 4,
            "The owning response invoice contains exactly four unique physical movement facts for its two entities.");
        foreach (var id in pair)
        {
            var enter = game.CardMovements.Single(m => m.CardId == id && m.From == CardLocation.Hand(1) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond);
            var finish = game.CardMovements.Single(m => m.CardId == id && m.From == CardLocation.Processing &&
                m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished);
            Require(enter.Sequence < finish.Sequence && invoice.Contains(enter) && invoice.Contains(finish) &&
                Facts<CardRespondedEvent>(game).Count(e => e.CardId == id && e.ResponderSeat == 1 && e.SourceSeat == 0 && e.EffectiveCardKind == CardKind.Slash) == 1 &&
                View(game, 1).Hand.All(c => c.Id != id),
                "Each native Zhangba entity enters Processing, finishes to the public discard pile and reports its effective Slash response exactly once.");
        }
    }
    private static bool IsActivate(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "activate";
    private static bool Demand(PendingDecision p, string step) => p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "same-name-hand" && c.Parameters.GetValueOrDefault("step") == step);
    private static PlayerSnapshot View(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "A native response command was rejected."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> match)
    { var p = Prompt(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(match).Id, g.Revision)); }
    private static void Use(GameEngine g, string id, int[] cards, int[] targets) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, id, cards, targets, g.Revision, Prompt(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<PendingDecision, bool> stop)
    {
        for (var n = 0; n < 80; n++)
        {
            var p = Prompt(g); if (p is not null && stop(p)) return;
            if (p is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                throw new InvalidOperationException("The expected native Zhangba completion already returned to Play.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed native Zhangba completion boundary was not reached.");
    }
    private static void Frozen<T>(IReadOnlyList<T> items) => Require(items is System.Collections.IList { IsReadOnly: true }, "The exposed response collection is frozen.");
    private static void Private(GameEngine g)
    {
        var p = Prompt(g)!; Require(p.IsPrivate, "The published response or formal skill choice is private.");
        foreach (var seat in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat))
            Require(g.CreateSnapshot(seat).PendingDecision is null && g.CreateSnapshot(seat).Players[p.PlayerSeat].Hand.Count == 0,
                "Another viewer cannot receive the private response, choice or foreign Hand entities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats);
        foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); }
        var before = State(g);
        Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("not-published"), g.Revision)).Accepted && State(g) == before,
            "An unpublished choice cannot advance the native cursor or consume either response entity.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all four private views, frozen choices, exact native costs, typed receipts, frames and command provenance."); return copy;
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:card-response-completion-native", "1.0.0", "原生丈八两实体响应完成");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.rules.json")!);
            using var labels = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rules.ReadToEnd(), labels.ReadToEnd());
            b.AddSkill(new(Zhefu, formal.Presentations[Zhefu].Name, formal.Presentations[Zhefu].Description)
                { Program = formal.Programs[Zhefu], ProgramPresentation = formal.Presentations[Zhefu] });
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,
                "viewAs":[{"id":"duel","inputKinds":[],"inputSuits":[],"outputKind":"duel","forPlay":true,"forResponse":false,"singleCardTrickUse":true}],
                "activations":[
                  {"id":"equip-other","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"weapon"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"weapon"}]},
                  {"id":"wound-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":2}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [Driver] = new { name = "真实装备和决斗驱动", description = "合法实体装备及原生决斗响应" } } }));
            b.AddSkill(new(Driver, catalog.Presentations[Driver].Name, catalog.Presentations[Driver].Description)
                { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            b.AddSkill(new("fixture:response-completion-native-first", "唯一首个AI候选", "原生选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:response-completion-native-peer", "其他候选", "原生选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            b.AddGeneral(new("fixture:response-completion-native-owner", "真实决斗使用者", "supporter", Driver, "jin", 4));
            b.AddGeneral(new("fixture:response-completion-native-peer-1", "原生丈八响应者", "supporter", "fixture:response-completion-native-first", "qun", 4, [Zhefu]));
            for (var i = 2; i < 4; i++) b.AddGeneral(new($"fixture:response-completion-native-peer-{i}", "其他存活角色", "supporter", "fixture:response-completion-native-peer", "qun", 4));
            b.AddCard(new("fixture:response-completion-native-zhangba", "丈八蛇矛", "装备牌", "将两张手牌当一张杀使用或打出。",
                CardKind.ZhangbaSerpentSpear, AiTags: new Dictionary<string, string> { ["slot"] = "weapon", ["conversion"] = "two-hand-cards-as-slash" }));
            b.AddDeck(new("fixture:response-completion-native-deck", "固定真实丈八实体", 4, 0, []) {
                PhysicalCards = Enumerable.Range(0, 24).Select(i => new ContentDeckPhysicalCard("fixture:response-completion-native-zhangba", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "小型原生两实体响应", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:response-completion-native-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:response-completion-native-owner",
                    "fixture:response-completion-native-peer-1", "fixture:response-completion-native-peer-2", "fixture:response-completion-native-peer-3"]));
        }
    }
}
