using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CardResponseCompletionCostChecks
{
    private const string Mode = "identity:classic-response-completion-cost";
    private const string Driver = "fixture:response-completion-cost-driver";
    private const string Conversion = "fixture:response-completion-cost-conversion";
    private const string HpObserver = "fixture:response-completion-cost-hp";
    private const string Zhefu = "ol:zhefu";
    private const string PublicPileMode = "fixture:public-pile-native-cost";
    private const string PublicPile = "fixture:public-pile-native-cost-source";
    private const string PublicPileDamage = "fixture:public-pile-native-cost-damage";

    public static void PublicPileSlashPaymentRemainsSeparateFromNativeMaterials()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new PublicPileFixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = PublicPileMode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, "fixture:public-pile-native-cost-actor", game.Revision, Prompt(game)!.PromptId));
        Reach(game, p => p is { PlayerSeat: 1 } && p.SkillPrompt?.SkillId == PublicPile &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
        var stored = View(game, 1).Hand.Take(2).Select(c => c.Id).ToArray();
        foreach (var id in stored) Answer(game, c => c.Cards.SequenceEqual([id]));
        Play(game);
        Require(View(game, 1).GeneralId == "fixture:public-pile-native-cost-owner" && View(game, 1).Hp == 4 &&
            View(game, 1).AuthorityCards!.Select(c => c.Id).Order().SequenceEqual(stored.Order()) &&
            registry.GetSkill("ol:yidu").Program!.Triggers.Any(t => t.Effects.Any(e =>
                e.Op == SkillProgramEffectOp.RevealUndamagedUseTargetHandAndDiscardSameColor)),
            "A real GameStarting selection stores two public Authority entities while the formal whole-use ledger capability is present.");
        foreach (var viewer in Enumerable.Range(0, 4))
            Require(game.CreateSnapshot(viewer).Players[1].AuthorityCards!.Select(c => c.Id).Order().SequenceEqual(stored.Order()),
                "The foreign actor pays published public-pile identities rather than another player's private hand.");
        var legal = game.GetHumanLegalActions().Single(a => a.ProgramSkillId == PublicPile &&
            a.ProgramActivationId == "public-pile-slash" && a.ProgramSkillOwnerSeat == 1 && a.TargetSeats.SequenceEqual([1]));
        Require(legal.SelectableCardIds.Order().SequenceEqual(stored.Order()), "The native foreign policy publishes exactly the two real public costs.");
        Accept(game, new UseProgramSkillCommand(0, PublicPile, "public-pile-slash", stored, [1], game.Revision,
            Prompt(game)!.PromptId) { SkillOwnerSeat = 1 });
        Reach(game, p => p is { PlayerSeat: 1 } && p.SkillPrompt?.SkillId == PublicPileDamage && HasContinue(p));
        var use = game.ResolutionStack.OfType<CardUseFrame>().Single();
        var action = use.Action!; var useId = use.Id; var actionId = action.ActionId;
        var recorded = Facts<CompletedUndamagedUseDamageRecordedEvent>(game).Single();
        Require(use is { SourceSeat: 0, CardId: 0, CardKind: CardKind.Slash, PhysicalCardIds.Count: 0 } &&
            use.TargetSeats.SequenceEqual([1]) && action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0,
                EffectiveKind: CardKind.Slash, PhysicalCards.Count: 2 } &&
            action.PhysicalCards.Select(c => c.CardId).SequenceEqual(stored) &&
            action.PhysicalCards.All(c => c.From == CardLocation.Authority(1)) &&
            action.ConversionChain is [{ SkillId: PublicPile, BindingId: "public-pile-slash", OwnerSeat: 1 }] &&
            use.CardAttack is { PhysicalCardIds.Count: 0, DamageWasApplied: true } &&
            recorded is { ActorSeat: 0, SourceSeat: 0, TargetSeat: 1, EffectiveKind: CardKind.Slash, Amount: 1,
                SourceLess: false, ChainPropagation: false, Redirected: false } &&
            recorded.CardUseFrameId == useId && recorded.CardActionId == actionId &&
            Facts<CardActionAcceptedEvent>(game).Count(e => e.Action.ActionId == actionId && e.Action.Type == CardActionType.Use) == 1,
            "The exact accepted foreign Slash records real damage with zero native materials and two distinct Authority activation costs.");
        Require(View(game, 1).Hp == 3 && View(game, 1).AuthorityCount == 0 &&
            stored.All(id => game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Authority(1) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "program.public-pile.slash-payment") == 1) &&
            !game.CardMovements.Any(m => stored.Contains(m.CardId) && m.To == CardLocation.Processing) &&
            Facts<CardUseFinishedEvent>(game).All(e => e.ResolutionId != useId),
            "The native costs are paid once directly to DiscardPile before damage, and the original Use awaits its real damage observer.");
        Frozen(action.PhysicalCards); Frozen(action.ConversionChain); FrozenEmptyNativeMaterials(use.PhysicalCardIds!);
        Private(game); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Play(game);
        Require(Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId && e.CardId == 0 && e.CardKind == CardKind.Slash) == 1 &&
            Facts<CompletedUndamagedUseDamageRecordedEvent>(game).Count(e => e.CardUseFrameId == useId && e.CardActionId == actionId) == 1 &&
            Facts<DamageAppliedEvent>(game).Count(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            Facts<CompletedUndamagedTargetRevealStartedEvent>(game).Length == 0 &&
            stored.All(id => game.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Authority(1) &&
                m.To == CardLocation.DiscardPile && m.Reason.Value == "program.public-pile.slash-payment") == 1) &&
            !game.ResolutionStack.Any(f => f.Id == useId) && View(game, 1).Hp == 3,
            "Cold return completes the original native Slash once without repeating its real costs, damage or ledger fact.");
        _ = Cold(game, registry);
    }

    public static void SilverLionEquipmentDodgeDrainsHealthBeforeCompletion()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 4 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(0, "fixture:response-completion-cost-owner", game.Revision, Prompt(game)!.PromptId));
        Play(game);
        Require(View(game, 1).GeneralId == "fixture:response-completion-cost-peer-1" && View(game, 1).Hp == 4,
            "Published role-weighted selection puts the real equipment-response owner at seat1 with native 4 HP.");
        var windows = registry.GetSkill(Zhefu).Program!.Triggers.Select(t => t.Window).ToHashSet();
        Require(windows.SetEquals([SkillProgramTriggerWindow.CardUseCompleted, SkillProgramTriggerWindow.CardResponseCompleted,
            SkillProgramTriggerWindow.CardSupplyCompleted]), "The fixture retains all three formal Zhefu completion windows.");

        var armor = View(game, 0).Hand.First(c => c.Kind == CardKind.SilverLion).Id;
        Use(game, "equip-other", [armor], [1]); Play(game);
        Require(View(game, 1).Equipment is [{ Id: var equipped, Kind: CardKind.SilverLion }] && equipped == armor,
            "A real selected hand entity is placed in the responder's native armor slot.");
        Use(game, "wound-other", [], [1]); Play(game);
        Require(View(game, 1).Hp == 3 && Facts<RecoveryAppliedEvent>(game).Length == 0,
            "A legal LoseHp operation wounds the armor owner before its actual response payment.");
        var ownerHp = View(game, 0).Hp;
        var slash = game.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == Driver &&
            a.ConversionSource.BindingId == "slash" && a.TargetSeats.SequenceEqual([1]));
        Accept(game, new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, Prompt(game)!.PromptId,
            slash.PlayedCardKind) { ConversionSource = slash.ConversionSource });
        Reach(game, p => p is { PlayerSeat: 1, Kind: DecisionKind.RespondDodge });
        var incoming = game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardKind == CardKind.Slash);
        var useId = incoming.Id; var useAction = incoming.Action!.ActionId;
        Require(Prompt(game)!.Choices.Any(c => c.Cards.SequenceEqual([armor])) &&
            View(game, 1).Hand.All(c => c.Kind == CardKind.SilverLion),
            "The published Dodge uses the equipped entity; ordinary hand armor cannot pay the equipment-only conversion.");
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Reach(game, p => p.SkillPrompt?.SkillId == HpObserver && HasContinue(p));

        var response = Facts<CardActionAcceptedEvent>(game).Single(e => e.Action.Type == CardActionType.Response).Action;
        var completion = game.ResolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.ResponseCompletion is not null);
        var receipt = completion.ResponseCompletion!;
        var hp = game.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        var observer = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == HpObserver);
        var observerId = observer.Id;
        Require(response is { ActorSeat: 1, ProviderSeat: 1, EffectiveKind: CardKind.Dodge, PhysicalCards.Count: 1 } &&
            response.ParentActionId == useAction && response.PhysicalCards[0].CardId == armor &&
            response.PhysicalCards[0].From == CardLocation.Equipment(1) &&
            response.ConversionChain is [{ SkillId: Conversion }],
            "The accepted native Dodge freezes precisely its equipment source, conversion and original Slash action.");
        Require(receipt is { CostsDrained: false, CompletionActorSeat: 1, CostRecoveryCursor: 0, CostHealthCursor: 0,
                CostHealthChanges.Count: 1 } && receipt.ActionId == response.ActionId && receipt.ParentFrameId == useId &&
            receipt.ActiveHealthChildFrameId == hp.Id && hp.Continuation == PostEventContinuation.ResponseCompletion &&
            hp.ResumeFrameId == completion.Id && hp.Change.ParentFrameId == useId &&
            hp.Change is { Kind: HpChangeKind.Recovery, SourceSeat: 1, TargetSeat: 1, Amount: 1, HpBefore: 3, HpAfter: 4 } &&
            observer.WindowContext?.ParentFrameId == hp.Id &&
            game.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == useId).CardAttack!.SuccessfulDodgeResponses == 0,
            "The paid response owns the real HP-change child before the original successful-Dodge cursor advances.");
        Require(Facts<SilverLionRemovedRecoveryEvent>(game) is [{ PlayerSeat: 1, RecoveredAmount: 1 }] &&
            Facts<RecoveryAppliedEvent>(game).Count(e => e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            View(game, 1).Hp == 4 && View(game, 1).Equipment.Count == 0 &&
            Facts<CardResponseCompletedEvent>(game).Length == 0 && Facts<SameNameHandStartedEvent>(game).Length == 0,
            "Leaving the real armor heals exactly once, but neither response completion nor Zhefu precedes its HP observer.");
        Require(game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(1) &&
                m.To == CardLocation.Processing && m.Reason == CardMoveReasons.Respond) == 1 &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Processing &&
                m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.ResponseFinished) == 1,
            "The native response has already committed its one equipment entry and one finish movement.");
        Frozen(receipt.NativeCosts); Frozen(receipt.CostBatches); Frozen(receipt.CostHealthChanges);
        foreach (var batch in receipt.CostBatches) { Frozen(batch.Movements); Frozen(batch.SourceCounts); }
        Private(game); game = Cold(game, registry);
        Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Reach(game, p => p.SkillPrompt?.SkillId == Zhefu && p.Choices.Any(IsActivate));
        Require(Facts<CardResponseCompletedEvent>(game) is [{ ActorSeat: 1, ProviderSeat: 1, NativeActorSeat: 1,
                EffectiveKind: CardKind.Dodge } done] && done.ActionId == response.ActionId && done.ParentFrameId == useId &&
            Facts<ProgramBindingResolvedEvent>(game).Count(e => e.FrameId == observerId && e.Completed) == 1 &&
            Sequence(game, e => e is RecoveryAppliedEvent { TargetSeat: 1 }) <
                Sequence(game, e => e is ProgramBindingStartedEvent { SkillId: HpObserver }) &&
            Sequence(game, e => e is ProgramBindingResolvedEvent resolved && resolved.FrameId == observerId) <
                Sequence(game, e => e is CardResponseCompletedEvent),
            "The exact cold-restored HP child resolves before the once-only native response completion offers formal Zhefu.");
        Private(game); game = Cold(game, registry); Answer(game, IsActivate);
        Reach(game, p => Demand(p, "target")); Answer(game, c => c.Targets.SequenceEqual([0]));
        Reach(game, p => Demand(p, "damage")); Answer(game, c => c.Parameters.GetValueOrDefault("step") == "damage");
        Play(game);
        var demand = Facts<SameNameHandStartedEvent>(game).Single();
        Require(demand.ActionId == response.ActionId && demand.OriginalParentFrameId == useId && demand.Source.OwnerSeat == 1 &&
            Sequence(game, e => e is CardResponseCompletedEvent) < Sequence(game, e => e is SameNameHandStartedEvent) &&
            Facts<SameNameHandCompletedEvent>(game).Count(e => e.FrameId == demand.FrameId && e.DamageIssued && !e.Discarded) == 1 &&
            Facts<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == useId) == 1 &&
            Facts<CardResponseCompletedEvent>(game).Count(e => e.ActionId == response.ActionId) == 1 &&
            Facts<CardResponseCompletionStartedEvent>(game).Count(e => e.ActionId == response.ActionId) == 1 &&
            Facts<SilverLionRemovedRecoveryEvent>(game).Count(e => e.PlayerSeat == 1 && e.RecoveredAmount == 1) == 1 &&
            Facts<RecoveryAppliedEvent>(game).Count(e => e.TargetSeat == 1 && e.Amount == 1) == 1 &&
            game.CardMovements.Count(m => m.CardId == armor && m.From == CardLocation.Equipment(1)) == 1 &&
            View(game, 1).Hp == 4 && View(game, 0).Hp == ownerHp - 1 &&
            !game.ResolutionStack.Any(f => f.Id == useId || f.Id == completion.Id || f.Id == hp.Id),
            "Formal Zhefu returns through the original native Slash once without repaying the armor or repeating its recovery.");
        _ = Cold(game, registry);
    }

    private static bool IsActivate(PromptChoice c) => c.Parameters.GetValueOrDefault("program-action") == "activate";
    private static bool HasContinue(PendingDecision p) => p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static bool Demand(PendingDecision p, string step) => p.Choices.Any(c =>
        c.Parameters.GetValueOrDefault("program-action") == "same-name-hand" && c.Parameters.GetValueOrDefault("step") == step);
    private static PlayerSnapshot View(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? Prompt(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] Facts<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static long Sequence(GameEngine g, Func<IGameEvent, bool> match) => g.Events.Single(e => match(e.Payload)).Sequence;
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Require(result.Accepted, result.Error?.Message ?? "A real response-cost command was rejected."); }
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
                throw new InvalidOperationException("The expected response-cost child already returned to Play.");
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("The fixed native response-cost boundary was not reached.");
    }
    private static void Frozen<T>(IReadOnlyList<T> items) => Require(items is System.Collections.IList { IsReadOnly: true }, "The exposed cost collection is frozen.");
    private static void FrozenEmptyNativeMaterials(IReadOnlyList<int> items)
    {
        Require(items.Count == 0 && items is System.Collections.IList { IsFixedSize: true },
            "The native zero-material list has fixed zero capacity.");
        var list = (System.Collections.IList)items;
        var addRejected = false;
        try { list.Add(-1); } catch (NotSupportedException) { addRejected = true; }
        var setRejected = false;
        try { list[0] = -1; }
        catch (NotSupportedException) { setRejected = true; }
        catch (ArgumentOutOfRangeException) { setRejected = true; }
        catch (IndexOutOfRangeException) { setRejected = true; }
        Require(addRejected && setRejected && items.Count == 0,
            "The native empty fixed list cannot gain a material or replace an element despite its array IsReadOnly flag.");
    }
    private static void Private(GameEngine g)
    {
        var p = Prompt(g)!; Require(p.IsPrivate, "The native health or formal optional choice is private.");
        foreach (var seat in Enumerable.Range(0, 4).Where(s => s != p.PlayerSeat))
            Require(g.CreateSnapshot(seat).PendingDecision is null && g.CreateSnapshot(seat).Players[p.PlayerSeat].Hand.Count == 0,
                "Another viewer cannot receive the private choice or foreign hand identities.");
        Frozen(p.Choices); Frozen(p.ValidCardIds); Frozen(p.ValidTargetSeats);
        var before = State(g);
        Require(!g.Submit(new AnswerPromptCommand(p.PlayerSeat, p.PromptId, new("not-published"), g.Revision)).Accepted && State(g) == before,
            "An unpublished choice cannot advance the response cursor, heal again or alter native payment.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))).ToArray(),
        Frames = JsonSerializer.Serialize(g.ResolutionStack),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(State(copy) == State(g), "Cold replay preserves all prepared views, private prompts, typed health invoices, native costs and event order."); return copy;
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:card-response-completion-cost", "1.0.0", "原生装备响应的回复子窗");
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
                "viewAs":[{"id":"slash","inputKinds":[],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],
                "activations":[
                  {"id":"equip-other","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"armor"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"armor"}]},
                  {"id":"wound-other","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":1}]}]},
              {"id":"{{{Conversion}}}","revision":1,"viewAs":[{"id":"equipment-dodge","inputKinds":["silverLion"],"inputSuits":[],"sourceZones":["equipment"],"outputKind":"dodge","forPlay":false,"forResponse":true}]},
              {"id":"{{{HpObserver}}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [Driver] = new { name = "真实装备和伤害驱动", description = "合法实体放置及失去体力" },
                [Conversion] = new { name = "装备转闪", description = "仅将装备区白银狮子当闪响应" },
                [HpObserver] = new { name = "原生回复观察", description = "完成前结清真实回复子窗", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:response-completion-cost-first", "唯一首个AI候选", "原生选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:response-completion-cost-peer", "其他候选", "原生选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            b.AddGeneral(new("fixture:response-completion-cost-owner", "真实使用者", "supporter", Driver, "jin", 4));
            b.AddGeneral(new("fixture:response-completion-cost-peer-1", "真实装备响应者", "supporter", "fixture:response-completion-cost-first", "qun", 4,
                [Conversion, HpObserver, Zhefu]));
            for (var i = 2; i < 4; i++) b.AddGeneral(new($"fixture:response-completion-cost-peer-{i}", "其他存活角色", "supporter", "fixture:response-completion-cost-peer", "qun", 4));
            b.AddCard(new("fixture:response-completion-cost-lion", "白银狮子", "装备牌", "失去装备区里的白银狮子后回复1点体力。",
                CardKind.SilverLion, AiTags: new Dictionary<string, string> { ["slot"] = "armor", ["on-loss"] = "recover-one" }));
            b.AddDeck(new("fixture:response-completion-cost-deck", "固定真实装备实体", 4, 0, []) {
                PhysicalCards = Enumerable.Range(0, 24).Select(i => new ContentDeckPhysicalCard("fixture:response-completion-cost-lion", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "小型原生响应成本", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:response-completion-cost-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:response-completion-cost-owner",
                    "fixture:response-completion-cost-peer-1", "fixture:response-completion-cost-peer-2", "fixture:response-completion-cost-peer-3"]));
        }
    }

    private sealed class PublicPileFixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:public-pile-native-cost", "1.0.0", "公开权牌费用与原生虚拟杀材料边界");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.rules.json")!);
            using var labels = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ordinary-guo-huai.presentation.json")!);
            var formal = SkillProgramCatalog.Load(rules.ReadToEnd(), labels.ReadToEnd());
            const string yidu = "ol:yidu";
            b.AddSkill(new(yidu, formal.Presentations[yidu].Name, formal.Presentations[yidu].Description)
                { Program = formal.Programs[yidu], ProgramPresentation = formal.Presentations[yidu] });
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{PublicPile}}}","revision":1,
                "triggers":[{"id":"store-two","window":"gameStarting","subject":"owner","optional":false,"effects":[
                  {"op":"selectOwnedCards","target":"owner","amount":2,"zones":["hand"],"resultBind":"authority"},
                  {"op":"moveBoundCards","target":"owner","sourceBind":"authority","destination":"ownerPersistentZone","destinationZone":"authority"}]}],
                "cardPolicies":[{"id":"public-pile-slash","kind":"foreignPublicPileSlash"}]},
              {"id":"{{{PublicPileDamage}}}","revision":1,"triggers":[{"id":"actual-damage","window":"afterDamageApplied","subject":"owner",
                "damageOccurrence":"perDamage","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"damage","options":[{"id":"continue"}]}]}]}
            ]}
            """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new Dictionary<string, object> {
                [PublicPile] = new { name = "真实公开权牌", description = "开局真实存入两张权，其他角色移去两张权视为对持有者使用杀", authorityName = "权" },
                [PublicPileDamage] = new { name = "真实伤害观察", description = "等待原生伤害返回", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } } }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new("fixture:public-pile-native-cost-first", "唯一公开权牌候选", "真实选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 10000d) });
            b.AddSkill(new("fixture:public-pile-native-cost-quiet", "其他存活角色", "真实选将权重")
                { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 100d) });
            b.AddGeneral(new("fixture:public-pile-native-cost-actor", "真实外人", "supporter", "fixture:public-pile-native-cost-quiet", "jin", 4));
            b.AddGeneral(new("fixture:public-pile-native-cost-owner", "真实权牌持有者", "supporter", "fixture:public-pile-native-cost-first", "qun", 4,
                [PublicPile, PublicPileDamage]));
            for (var i = 2; i < 4; i++) b.AddGeneral(new($"fixture:public-pile-native-cost-peer-{i}", "其他存活角色", "supporter", "fixture:public-pile-native-cost-quiet", "qun", 4));
            b.AddCard(new("fixture:public-pile-native-cost-slash", "杀", "基本牌", "真实杀实体", CardKind.Slash));
            b.AddDeck(new("fixture:public-pile-native-cost-deck", "固定真实无闪牌堆", 4, 0, []) {
                PhysicalCards = Enumerable.Range(0, 24).Select(i => new ContentDeckPhysicalCard("fixture:public-pile-native-cost-slash", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(PublicPileMode, "公开权牌成本小模式", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 },
                "fixture:public-pile-native-cost-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:public-pile-native-cost-actor",
                    "fixture:public-pile-native-cost-owner", "fixture:public-pile-native-cost-peer-2", "fixture:public-pile-native-cost-peer-3"]));
        }
    }
}
