using System.Runtime.CompilerServices;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class GuoTuWeijieSpecializedChecks
{
    private const string Owner = "fixture:wjs-owner", Mode = "identity:classic-weijie-specialized",
        Driver = "fixture:wjs-driver", Quiet = "fixture:wjs-quiet",
        Watch = "fixture:wjs-watch", Dodge = "fixture:wjs-dodge", Incoming = "fixture:wjs-incoming";
    private static string Scene = "";

    public static void SpecializedForeignQigongHejiAndOwnTurnShanjiaBoundaryCold()
    {
        foreach (var scene in new[] { "qigong", "heji" })
        {
            var (g, registry) = Start(scene);
            var skill = scene == "qigong" ? "ol:qigong" : "ol:heji";
            if (scene == "qigong")
            {
                Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Incoming &&
                    p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "request-slash" && c.Cards.Count == 1));
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "request-slash" && c.Cards.Count == 1);
                Reach(g, () => Optional(g, skill));
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(g, () => Root(g, skill)?.LvKuangLvXiang?.Stage == "actor");
                Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "actor" && c.Targets.SequenceEqual([0]));
            }
            else
            {
                Reach(g, () => Optional(g, skill));
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            }
            Need(Root(g, skill) is { } expectedParent &&
                (scene == "qigong" ? expectedParent.LvKuangLvXiang is { Stage: "slash", Actor: 0 } :
                    expectedParent.WuJing is { Stage: WuJingStage.UseChoice }),
                "The activated specialized mother must remain live until its own real request can publish, including an empty-Hand actor with only Weijie available. " + Diagnostic(g));
            Reach(g, () => Open(g) && Root(g, skill) is { } live &&
                (scene == "qigong" ? live.LvKuangLvXiang is { Stage: "slash", Actor: 0 } :
                    live.WuJing is { Stage: WuJingStage.UseChoice }));
            var parent = Root(g, skill) ?? throw new InvalidOperationException("The exact specialized request vanished. " + Diagnostic(g));
            var parentId = parent.Id;
            var target = scene == "qigong" ? parent.LvKuangLvXiang!.Target : parent.WuJing!.TargetSeat;
            var originalId = scene == "qigong" ? parent.LvKuangLvXiang!.OriginalUseId!.Value : parent.WuJing!.UseId;
            var original = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == originalId);
            var lord = Lord(g);
            Need((scene == "qigong" ? target == lord : target != 0 && target != lord) &&
                original.Action is { EffectiveKind: CardKind.Slash } originalAction &&
                originalAction.ActorSeat == (scene == "qigong" ? 0 : lord) &&
                originalAction.PhysicalCards.Count == 1 && g.State.CurrentSeat == lord,
                "A genuine physical red Slash during the foreign Lord's actual turn creates the specialized production request.");
            Need(scene == "qigong" ? parent.OwnerSeat == 0 && parent.LvKuangLvXiang is { Stage: "slash", Actor: 0 } :
                parent.OwnerSeat == 0 && parent.WuJing is { Stage: WuJingStage.UseChoice },
                "Qigong retains its real issuer and legally nominated human actor; Heji retains its own completed-use parent.");
            if (scene == "qigong")
                Need(E<CardRespondedEvent>(g).Any(e => e.ResponderSeat == target && e.EffectiveCardKind == CardKind.Dodge),
                    "Qigong's original sole-target Slash genuinely finishes the target's physical Dodge response.");
            else
                Need(original.Step == ResolutionFrameStep.Completed &&
                    E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == originalId) == 1 && V(g, target).IsAlive,
                    "Heji observes a genuinely completed physical red Slash and its still-living actual recipient.");
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.ContainsKey("weijie-request"));
            Reach(g, () => Current(g) is { Stage: WeijieStage.Declare });
            Answer(g, c => c.Parameters.GetValueOrDefault("weijie-choice") == "declare" &&
                c.Parameters.GetValueOrDefault("kind") == nameof(CardKind.FireSlash) &&
                c.Parameters.GetValueOrDefault("target") == target.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Reach(g, () => Current(g) is { Stage: WeijieStage.Victim });
            var need = Current(g)!.Need;
            Need(need.OwnerFrameId == parentId && need.RequestFrameId == parentId && need.ActorSeat == 0 &&
                need.ProviderSeat == 0 && need.InstructionIndex == parent.InstructionIndex && need.Intent == WeijieIntent.AssistedSlash,
                "Weijie freezes the exact specialized parent, actual requester, instruction and original private native need.");
            Answer(g, c => c.Parameters.GetValueOrDefault("weijie-choice") == "victim" && c.Targets.SequenceEqual([3]));
            Reach(g, () => Current(g) is { Stage: WeijieStage.Card });
            Need(P(g) is { PlayerSeat: 0, IsPrivate: true } privateCost && privateCost.Choices.All(c => c.Cards.Count == 0) &&
                Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null),
                "The foreign actual Hand discard uses opaque private slots inside a specialized request.");
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("weijie-choice") == "card");
            Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
            var paid = E<WeijieCostPaidEvent>(g).Single();
            var issued = E<WeijieUseIssuedEvent>(g).Single(e => e.AttemptId == paid.FrameId);
            var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.Id == issued.UseId);
            Need(paid.ActualTurnOwner == lord && paid.VictimSeat == 3 && paid.PrintedKind == CardKind.Slash &&
                paid.EffectiveName == CardKind.Slash && g.CardMovements.Count(m => m.Sequence == paid.Sequence &&
                    m.CardId == paid.CardId && m.From == CardLocation.Hand(3) && m.To == CardLocation.DiscardPile &&
                    m.Reason.Value == "skill-program.guo-tu.weijie.discard") == 1,
                "A real foreign Hand Slash pays the current actual-turn attempt exactly once.");
            Need(issued.ActorSeat == 0 && issued.ProviderSeat == 0 && issued.RequesterSeat is null &&
                use.CardId == 0 && use.PhysicalCardIds is { Count: 0 } &&
                use.Action is { Type: CardActionType.Use, EffectiveKind: CardKind.FireSlash, PhysicalCards.Count: 0 } action &&
                action.ConversionChain is [var source] && source.SkillId == "ol:weijie" &&
                action.ActorSeat == 0 && action.ProviderSeat == 0 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.UseId) == 1 &&
                E<WeijieUseReturnedEvent>(g).All(e => e.AttemptId != paid.FrameId),
                "The production specialized Use paid zero native material and pauses in its genuine completed-use child before its typed parent return.");
            Need(scene == "qigong" ? use.Qigong is { Paid: true } q && q.ProgramId == parentId &&
                    q.OriginalAction.PhysicalCards.Count == 0 && E<QigongNativePaidEvent>(g).Count(e => e.Receipt.UseId == use.Id) == 1 :
                use.WuJingHeji?.Origin is { } w && w.ProgramFrameId == parentId && w.Materials.Count == 0 &&
                    E<WuJingHejiPaidEvent>(g).Count(e => e.ProgramFrameId == parentId && e.UseId == use.Id) == 1,
                "Each specialized parent owns its real original native zero-material payment invoice.");
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            Reach(g, () => E<ProgramBindingResolvedEvent>(g).Any(e => e.FrameId == parentId && e.Completed) &&
                !g.ResolutionStack.Any(f => f.Id == parentId || f.Id == issued.UseId || f.Id == originalId));
            Need(E<WeijieUseReturnedEvent>(g).Count(e => e.AttemptId == paid.FrameId && e.UseId == issued.UseId &&
                    e.ActionId == issued.ActionId) == 1 && E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == issued.UseId) == 1 &&
                E<ProgramBindingResolvedEvent>(g).Count(e => e.FrameId == parentId && e.Completed) == 1 &&
                E<WeijieCostPaidEvent>(g).Length == 1 && E<WeijieUseIssuedEvent>(g).Length == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == originalId) == 1,
                "The native Slash and its children finish once, and both the specialized instruction and original actual Slash retire completely.");
            if (scene == "qigong")
                Need(E<QigongNativeReturnedEvent>(g).Count(e => e.ProgramId == parentId && e.UseId == issued.UseId) == 1 &&
                    E<DamageAppliedEvent>(g).Count(e => e.SourceSeat == 0 && e.TargetSeat == target && e.Amount == 1 && e.Nature == DamageNature.Fire) == 1,
                    "Qigong's undodgeable genuine Fire Slash deals native damage and restores the original requested Dodged Slash.");
            else
                Need(E<WuJingHejiReturnedEvent>(g).Count(e => e.ProgramFrameId == parentId && e.UseId == issued.UseId) == 1 &&
                    E<WuJingRewardIssuedEvent>(g).Length == 0,
                    "Heji returns through its own specialized receipt; a zero-material converted follow-up earns no unconverted reward.");
            _ = Cold(g, registry);
        }
        {
            var (g, registry) = Start("shanjia");
            Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
            Need(g.State.CurrentSeat == 0, "Shanjia's actual initial request is issued from the owner's real own Play.");
            Accept(g, new UseProgramSkillCommand(0, "ol:shanjia", "draw-and-slash", [], [], g.Revision, P(g)!.PromptId));
            Reach(g, () => Root(g, "ol:shanjia")?.CaoChun is { Stage: CaoChunStage.InitialSlash });
            var parentId = Root(g, "ol:shanjia")!.Id;
            Need(!Open(g) && P(g)!.Choices.All(c => !c.Parameters.ContainsKey("weijie-request")) &&
                E<ShanjiaMovementPaidEvent>(g).Count(e => e.FrameId == parentId && e.Stage == CaoChunStage.DrawChildren && e.ActualCount == 3) == 1,
                "A genuine Shanjia draw-three initial Slash request cannot offer foreign-turn-only Weijie during its own actual turn.");
            g = Cold(g, registry);
            Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "slash" && c.Cards.Count == 1 && c.Targets.SequenceEqual([1]));
            Reach(g, () => E<ShanjiaNativeSlashReturnedEvent>(g).Any(e => e.FrameId == parentId) &&
                E<ProgramSkillResolvedEvent>(g).Any(e => e.FrameId == parentId && e.OwnerSeat == 0 &&
                    e.SkillId == "ol:shanjia" && e.ActivationId == "draw-and-slash" && e.Completed) &&
                !g.ResolutionStack.Any(f => f.Id == parentId));
            var native = E<ShanjiaNativeSlashIssuedEvent>(g).Single(e => e.FrameId == parentId);
            Need(!native.Ending && E<ShanjiaNativeMaterialPaidEvent>(g).Count(e => e.FrameId == parentId &&
                    e.NativeUseId == native.NativeUseId && e.Count == 1 && e.After > e.Before) == 1 &&
                E<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == native.NativeUseId) == 1 &&
                E<ShanjiaNativeSlashReturnedEvent>(g).Count(e => e.FrameId == parentId && e.NativeUseId == native.NativeUseId) == 1 &&
                E<ProgramSkillResolvedEvent>(g).Count(e => e.FrameId == parentId && e.OwnerSeat == 0 &&
                    e.SkillId == "ol:shanjia" && e.ActivationId == "draw-and-slash" && e.Completed) == 1 &&
                E<ShanjiaDebtArmedEvent>(g).Count(e => e.ActivationFrameId == parentId && e.FrozenX == 3) == 1 &&
                E<WeijieCostPaidEvent>(g).Length == 0 && E<WeijieUseIssuedEvent>(g).Length == 0,
                "The legitimate own-turn Shanjia request instead pays its real physical Slash, returns once and arms its original debt.");
            _ = Cold(g, registry);
        }
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static int Lord(GameEngine g) => g.CreateSnapshot(0).Players.Single(p => p.Role == Role.Lord).Seat;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static ProgramSkillFrame? Root(GameEngine g, string skill) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.SkillId == skill);
    private static WeijieReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.Weijie is not null)?.Weijie;
    private static bool Open(GameEngine g) => P(g) is { PlayerSeat: 0 } p && p.Choices.Any(c => c.Parameters.ContainsKey("weijie-request"));
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill && P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Need(bool condition, string text) { if (!condition) throw new InvalidOperationException(text + " Scene=" + Scene); }
    private static void Accept(GameEngine g, GameCommand command)
    {
        try { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Native command rejected."); }
        catch (Exception e) { throw new InvalidOperationException("Specialized Weijie command cause=" + e.Message + " " + Diagnostic(g), e); }
    }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    {
        var prompt = P(g); var choice = prompt?.Choices.FirstOrDefault(choose);
        Need(prompt is { PlayerSeat: 0 } && choice is not null, "Missing real human choice " + Diagnostic(g));
        Accept(g, new AnswerPromptCommand(0, prompt!.PromptId, choice!.Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 280; i++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, "Unexpected terminal state at " + line + " " + Diagnostic(g));
            var p = P(g);
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.SkillPrompt?.SkillId == Watch) Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification)
                Answer(g, c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("weijie-request"));
            else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") == "let-die");
            else if (p.Kind == DecisionKind.PlayCard) Accept(g, new EndPlayPhaseCommand(0, g.Revision, p.PromptId));
            else throw new InvalidOperationException("Unexpected specialized prompt at " + line + " " + Diagnostic(g));
        }
        throw new InvalidOperationException("Missing specialized boundary at " + line + " " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new
    {
        Scene, g.State.TurnNumber, g.State.CurrentSeat, g.State.Phase,
        Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(5).Select(c => new { c.Cards, c.Targets, c.Parameters }) } : null,
        Frames = g.ResolutionStack.TakeLast(7).Select(f => new { f.Id, Type = f.GetType().Name,
            Skill = (f as ProgramSkillFrame)?.SkillId, Index = (f as ProgramSkillFrame)?.InstructionIndex,
            Stage = (f as ProgramSkillFrame)?.LvKuangLvXiang?.Stage ?? (f as ProgramSkillFrame)?.WuJing?.Stage.ToString() ?? (f as ProgramSkillFrame)?.CaoChun?.Stage.ToString(),
            Trigger = (f as ProgramSkillFrame)?.TriggerId, Parent = (f as ProgramSkillFrame)?.WindowContext?.ParentFrameId }),
        Costs = E<WeijieCostPaidEvent>(g), Issued = E<WeijieUseIssuedEvent>(g),
        Recent = g.Events.Select(e => e.Payload).Where(e => e is WuJingStartedEvent or WuJingCompletedEvent or
            ProgramBindingStartedEvent or ProgramBindingResolvedEvent or QigongNativeReturnedEvent).TakeLast(10)
            .Select(e => new { Type = e.GetType().Name, Fact = JsonSerializer.Serialize(e, e.GetType()) })
    });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        g.CardMovements, Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))
    });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold restoration preserves all private prompts, original specialized parents and exact native once-paid receipts.");
        return restored;
    }
    private static (GameEngine, ContentRegistry) Start(string scene)
    {
        Scene = scene;
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = scene == "shanjia" ? Role.Lord : Role.Rebel,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand());
        Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        return (g, registry);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-weijie-specialized", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:guo-tu", "ol:lv-kuang-lv-xiang", "ol:wu-jing", "ol:cao-chun", "classic:zhuge-liang");
            var modifiers = new List<object> { new { id = "keep", query = "handLimit", operation = "add", value = 40, priority = 0 } };
            if (scene == "qigong") modifiers.Add(new { id = "real-request-target-hand", query = "initialHandSize", operation = "add", value = 4, priority = 0 });
            var setup = new List<object>();
            if (scene == "heji") setup.Add(new { id = "genuine-initial-empty-hand", window = "gameStarting", subject = "owner", optional = false,
                effects = new[] { new { op = "discardOwnedZoneCards", target = "owner", zones = new[] { "hand" } } } });
            var definitions = new object[]
            {
                new { id = Driver, revision = 1, modifiers, triggers = setup },
                new { id = Quiet, revision = 1, triggers = new[] { new { id = "quiet", window = "afterNormalDraw", subject = "owner", optional = false,
                    effects = new[] { new { op = "skipTurnPhases", target = "owner", phases = new[] { "play" } } } } } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "actual-specialized-paid-child", window = "cardUseCompleted", ownerRelation = "actor", cardKinds = new[] { "slash", "fireSlash", "thunderSlash" },
                    condition = new { kind = "cardUseConversionSkillIs", conversionSkillId = "ol:weijie" }, optional = false, usageScope = "game", usageLimit = 1,
                    effects = new[] { new { op = "chooseOption", target = "owner", resultBind = "paid", options = new[] { new { id = "continue" } } } } } } },
                new { id = Incoming, revision = 1, triggers = new[] { new { id = "real-foreign-request", window = "turnStartBeforeNormalFlow", subject = "owner", optional = false, usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "selectTarget", target = "owner", targetKind = "otherLivingHighestHand" }, new { op = "requestSlashByTarget", target = "selectedTarget", resultBind = "asked" } } } } },
                new { id = Dodge, revision = 1, viewAs = new[] { new { id = "physical-dodge", inputKinds = new[] { "slash" }, inputSuits = Array.Empty<string>(), outputKind = "dodge", forPlay = false, forResponse = true } } }
            };
            var ids = new[] { Driver, Quiet, Watch, Dodge, Incoming };
            var labels = ids.ToDictionary(id => id, id => new { name = id, description = "真实专有母请求与诿解原生回返", optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续原生杀" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "真实专有请求小夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            const string pick = "fixture:wjs-human-pick";
            builder.AddSkill(new(pick, "本人", "固定公开选择") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => -100000000d) });
            var skills = new List<string> { Driver, Watch, "ol:weijie" };
            if (scene == "qigong") skills.Add("ol:qigong");
            if (scene == "heji") { skills.Add("ol:heji"); skills.Add("classic:kongcheng"); }
            if (scene == "shanjia") skills.Add("ol:shanjia");
            builder.AddGeneral(new(Owner, "原生专有请求本人", "supporter", pick, "qun", 12, skills, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:wjs-peer-" + i).ToArray();
            for (var i = 1; i <= 3; i++)
            {
                var candidate = i; var weight = "fixture:wjs-peer-pick-" + i;
                builder.AddSkill(new(weight, "其他角色", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => candidate == 1 && role == Role.Lord ? 100000000d : 1000000d * (4 - candidate)) });
                var peerSkills = new List<string>();
                if (i != 1 || scene != "heji") peerSkills.Add(Quiet);
                if (i == 1 && scene == "qigong") peerSkills.Add(Incoming);
                if (scene != "shanjia") peerSkills.Add(Dodge);
                builder.AddGeneral(new(peers[i - 1], "真实其他角色", "supporter", weight, "wei", 12, peerSkills, GeneralGender.Male));
            }
            builder.AddDeck(new("fixture:wjs-deck", "真实小牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 72).Select(i => new ContentDeckPhysicalCard("standard:slash", Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "真实专有请求与回合外诿解", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:wjs-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
