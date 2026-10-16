using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class LuoXianChecks
{
    private const string Skill = "ol:daili", Driver = "fixture:luoxian-driver", Quiet = "fixture:luoxian-quiet";
    private const string Flip = "fixture:luoxian-flip", Gain = "fixture:luoxian-gain", Inspect = "fixture:luoxian-inspect";
    private const string EndingDeath = "fixture:luoxian-ending-death";
    private const string Owner = "fixture:luoxian-owner", Mode = "identity:classic-luoxian-fixture";

    public static void ZeroEvenOwnOtherEndsAndSkippedFaceDownTurnCold()
    {
        foreach (var own in new[] { true, false })
        {
            var (game, registry) = Start(); Need(Shown(game, 0).Length == 0, "Unrevealed starting and normal draw cards are not public shown hand cards.");
            if (own)
                foreach (var card in V(game, 0).Hand.Take(2).Select(c => c.Id).ToArray()) Show(game, card);
            End(game); Reach(game, () => Offer(game));
            if (!own) { Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Reach(game, () => Offer(game) && P(game)!.SourceSeat != 0); }
            var endedSeat = P(game)!.SourceSeat;
            game = Cold(game, registry); Activate(game); Reach(game, () => E<DailiTurnEndCompletedEvent>(game).Length == 1);
            var issued = E<DailiTurnEndIssuedEvent>(game).Single();
            Need(issued.Receipt.ShownHandCount == (own ? 2 : 0) && issued.Receipt.TurnOwnerSeat == endedSeat && issued.Receipt.TurnOwnerSeat == (own ? 0 : 1) &&
                E<TurnEndedEvent>(game).Any(e => e.ActorSeat == endedSeat && e.TurnNumber == issued.Receipt.ActualTurn) && V(game, 0).IsFaceDown &&
                E<DailiDrawPaidEvent>(game) is [{ Count: 3 }] && E<DailiTurnEndCompletedEvent>(game) is [{ ActualDrawCount: 3, RevealedCount: 3 }],
                "Zero is even at an actual own or other completed turn, and native flip precedes exactly three real draws and public reveals.");
            var shown = Shown(game, 0);
            Need(shown.Length == (own ? 5 : 3) && Enumerable.Range(0, 4).All(s => game.CreateSnapshot(s).PublicRevealedCards.Where(c => c.PublicHandOwnerSeat == 0).Select(c => c.Id).Order().SequenceEqual(shown.Order())) &&
                Enumerable.Range(1, 3).All(s => game.CreateSnapshot(s).Players[0].Hand.Count == 0),
                "All viewers see only the same explicitly shown owner-tagged faces, including exactly three new draws; the remaining hidden hand is not exposed.");
            game = Cold(game, registry);
            Reach(game, () => E<TurnEndedEvent>(game).Count(e => e.ActorSeat == 0) >= 2);
            Need(!V(game, 0).IsFaceDown && E<DailiTurnEndIssuedEvent>(game).Length == 1,
                "The real face-down skipped turn turns the owner face up and completes normally; an odd current shown-hand count offers no new Daili.");
            _ = Cold(game, registry);
        }

        var (deadTurn, deadRegistry) = Start("dead-turn-owner");
        End(deadTurn); Reach(deadTurn, () => Offer(deadTurn));
        Answer(deadTurn, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Reach(deadTurn, () => Offer(deadTurn) && P(deadTurn)!.SourceSeat == 1);
        var ended = E<TurnEndedEvent>(deadTurn).Single(e => e.ActorSeat == 1);
        Need(!V(deadTurn, 1).IsAlive && V(deadTurn, 0).IsAlive && deadTurn.State.Winner == Winner.None &&
            E<DamageAppliedEvent>(deadTurn).Any(e => e.TargetSeat == 1 && e.Amount == 20) &&
            E<PlayerDiedEvent>(deadTurn).Count(e => e.VictimSeat == 1) == 1 && Shown(deadTurn, 0).Length == 0 &&
            deadTurn.ResolutionStack.OfType<DeferredTurnEndFrame>().Any(f => f.OwnerSeat == 1 && f.TurnNumber == ended.TurnNumber),
            "An AI actual turn owner really dies from native ending damage, while the surviving Luo Xian still receives a zero-even Daili offer after that exact TurnEnded.");
        deadTurn = Cold(deadTurn, deadRegistry); Activate(deadTurn);
        Reach(deadTurn, () => E<DailiTurnEndCompletedEvent>(deadTurn).Length == 1);
        Need(E<DailiTurnEndIssuedEvent>(deadTurn).Single().Receipt is { TurnOwnerSeat: 1, ShownHandCount: 0 } receipt && receipt.ActualTurn == ended.TurnNumber &&
            V(deadTurn, 0).IsFaceDown && E<DailiDrawPaidEvent>(deadTurn) is [{ Count: 3 }] &&
            E<DailiTurnEndCompletedEvent>(deadTurn) is [{ ActualDrawCount: 3, RevealedCount: 3 }] && Shown(deadTurn, 0).Length == 3 &&
            E<TurnEndedEvent>(deadTurn).Count(e => e.ActorSeat == 1 && e.TurnNumber == ended.TurnNumber) == 1 &&
            E<ProgramBindingResolvedEvent>(deadTurn).Count(e => e.SkillId == Skill && e.Activated && e.Completed) == 1,
            "A dead completed-turn owner does not cancel the living source's paid native flip, actual three-card draw, public reveal or original end cursor.");
        _ = Cold(deadTurn, deadRegistry);
    }

    public static void NativePublicHandResidencyAndPrivateInspectionCold()
    {
        var (game, registry) = Start();
        var first = V(game, 0).Hand[0].Id;
        Show(game, first); Show(game, first);
        Need(Shown(game, 0).SequenceEqual([first]) && E<PublicHandEntityRevealedEvent>(game).Count(e => e.Card.Id == first) == 1,
            "Another real skill's explicit reveal counts; repeated revelation within the same hand residency does not count the entity twice.");
        Use(game, "show-peer", targets: [1]); Play(game);
        var peerCard = E<PublicHandEntityRevealedEvent>(game).Last(e => e.OwnerSeat == 1).Card.Id;
        Need(Shown(game, 1).Contains(peerCard) && !Shown(game, 0).Contains(peerCard) &&
            E<ProgramCardsRevealedEvent>(game).Last().OwnerSeat == 0,
            "A real foreign hand revealed by the owner's skill belongs to its actual physical hand owner, not the event's skill owner.");
        Use(game, "give", targets: [1]); Reach(game, () => Action(game, "owned-hand-rank-sum"));
        Answer(game, c => c.Cards.SequenceEqual([first]));
        Reach(game, () => Action(game, "owned-hand-rank-sum") && P(game)!.Choices.Any(c => c.Cards.Count == 0));
        Answer(game, c => c.Cards.Count == 0); Play(game);
        Need(!Shown(game, 0).Contains(first) && !Shown(game, 1).Contains(first), "A public reveal before an actual gift does not follow the entity into another owner's hidden hand.");
        var slot = V(game, 1).Hand.Select((c, i) => (c.Id, Index: i)).Single(c => c.Id == first).Index;
        Use(game, "take", targets: [1]); Reach(game, () => Action(game, "select-and-move-owned-card"));
        Need(P(game) is { IsPrivate: true, PlayerSeat: 0 } && P(game)!.Choices.All(c => c.Cards.Count == 0) && game.CreateSnapshot(2).PendingDecision is null,
            "Even a previously revealed physical card retains a concealed current hand slot when selected from a foreign hand.");
        game = Cold(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand) && c.Parameters.GetValueOrDefault("slot-index") == slot.ToString()); Play(game);
        Need(V(game, 0).Hand.Any(c => c.Id == first) && !Shown(game, 0).Contains(first), "A genuine departure and reacquisition create a fresh unshown residency rather than reviving a historical card-ID mark.");
        End(game); Reach(game, () => Offer(game)); Activate(game); Reach(game, () => E<DailiTurnEndCompletedEvent>(game).Length == 1);
        Need(E<DailiTurnEndIssuedEvent>(game).Single().Receipt.ShownHandCount == 0, "The ending parity excludes all old and foreign residencies.");
        _ = Cold(game, registry);

        var (privateGame, privateRegistry) = Start("private");
        Need(Action(privateGame, "private-hand-take") && E<ProgramPrivateHandViewedEvent>(privateGame).Length == 1 &&
            privateGame.CreateSnapshot(0).PrivateRevealedCards is { Count: > 0 } && privateGame.CreateSnapshot(2).PrivateRevealedCards is null &&
            E<PublicHandEntityRevealedEvent>(privateGame).Length == 0 && Shown(privateGame, 1).Length == 0,
            "An actual private hand inspection exposes its faces only to the viewing player and never establishes public shown-hand state.");
        privateGame = Cold(privateGame, privateRegistry); Answer(privateGame, _ => true); Play(privateGame); End(privateGame); Reach(privateGame, () => Offer(privateGame));
        Activate(privateGame); Reach(privateGame, () => E<DailiTurnEndCompletedEvent>(privateGame).Length == 1);
        Need(E<DailiTurnEndIssuedEvent>(privateGame).Single().Receipt.ShownHandCount == 0, "Privately inspecting and obtaining one card does not turn the owner's zero shown-card parity odd.");
        _ = Cold(privateGame, privateRegistry);
    }

    public static void PaidFlipAndDrawChildrenKeepOriginalEntitiesAfterSourceLoss()
    {
        var (game, registry) = Start("children"); End(game); Reach(game, () => Offer(game)); Activate(game);
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == Flip);
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Daili is not null);
        Need(root.Daili is { Stage: DailiStage.FlipChildren } && V(game, 0).IsFaceDown && E<DailiDrawPaidEvent>(game).Length == 0 &&
            game.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.ResumeProgramFrameId == root.Id && w.CharacterStateContinuation == CharacterStateContinuation.Program),
            "The paid native flip owns its real turned-over observer; the three-card draw has not started before that child returns.");
        game = Cold(game, registry); Continue(game); Reach(game, () => Option(game, "after-loss"));
        Need(V(game, 0).Skills!.All(s => s.ContentId != Skill) && E<DailiDrawPaidEvent>(game).Length == 0,
            "Actual native HP change removes the source grant inside the paid flip child before any draw.");
        game = Cold(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "after-loss");
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == Gain && Option(game, "continue"));
        root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Daili is not null);
        var drawn = Drawn(game, root.Id);
        Need(root.Daili is { Stage: DailiStage.DrawChildren, Count: 3 } && drawn.Length == 3 && Shown(game, 0).Length == 0,
            "Source loss retains the issued draw entitlement; the exact three real entities remain hidden while their gain child is suspended.");
        game = Cold(game, registry); Continue(game); Reach(game, () => Action(game, "select-owned-cards"));
        Answer(game, c => c.Cards.SequenceEqual([drawn[0]])); Reach(game, () => Option(game, "after-replacement"));
        var replacement = V(game, 0).Hand.Select(c => c.Id).Except(drawn).Last();
        Need(game.CardMovements.Any(m => m.CardId == drawn[0] && m.To == CardLocation.DiscardPile) && !Shown(game, 0).Contains(replacement),
            "The real gain child discards an original drawn entity and draws a replacement without prematurely exposing that replacement.");
        game = Cold(game, registry); Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "after-replacement");
        Reach(game, () => E<DailiTurnEndCompletedEvent>(game).Length == 1);
        Need(Shown(game, 0).Order().SequenceEqual(drawn.Skip(1).Order()) && !Shown(game, 0).Contains(replacement) && E<DailiDrawPaidEvent>(game) is [{ Count: 3 }] &&
            E<DailiTurnEndCompletedEvent>(game) is [{ ActualDrawCount: 3, RevealedCount: 2 }],
            "The original paid draw resumes once after source loss and nested movements, revealing only its two surviving original hand entities.");
        _ = Cold(game, registry);
    }

    public static void DrawChildOwnerDeathCompletesWithoutRevealOrReplacement()
    {
        var (game, registry) = Start("death", human: 1, stopAtEnding: true); Activate(game);
        Reach(game, () => P(game)?.SkillPrompt?.SkillId == Gain);
        var root = game.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Daili is not null); var drawn = Drawn(game, root.Id);
        Need(drawn.Length == 3 && Shown(game, 1).Length == 0, "The actual other-turn draw is paid but still private before its lethal native child.");
        game = Cold(game, registry); Continue(game); Reach(game, () => E<DailiTurnEndCompletedEvent>(game).Any(e => e.FrameId == root.Id));
        Need(!V(game, 1).IsAlive && game.State.Winner == Winner.None && E<DailiDrawPaidEvent>(game) is [{ Count: 3 }] &&
            E<DailiTurnEndCompletedEvent>(game) is [{ ActualDrawCount: 3, RevealedCount: 0 }] &&
            !E<CardsRevealedEvent>(game).Any(e => e.ResolutionId == root.Id) && Shown(game, 1).Length == 0,
            "A real owner death drains its paid child without exposing dead hand cards, drawing substitutes or leaving the original ending cursor unfinished.");
        _ = Cold(game, registry);
    }

    private static int[] Drawn(GameEngine game, long frame) => E<DailiCardDrawnEvent>(game).Where(e => e.FrameId == frame).Select(e => game.CardMovements.Single(m => m.Sequence == e.MovementSequence).CardId).ToArray();
    private static int[] Shown(GameEngine game, int owner) => game.CreateSnapshot(0).PublicRevealedCards.Where(c => c.PublicHandOwnerSeat == owner).Select(c => c.Id).Order().ToArray();
    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine game) where T : IGameEvent => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Offer(GameEngine game) => P(game)?.SkillPrompt?.SkillId == Skill && Action(game, "activate");
    private static bool Action(GameEngine game, string action) => P(game)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    private static bool Option(GameEngine game, string id) => P(game)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id) == true;
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Daili command."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    { var prompt = P(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision)); }
    private static void Activate(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Continue(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Show(GameEngine game, int card)
    {
        Use(game, "show"); Reach(game, () => Action(game, "select-owned-cards"));
        Answer(game, c => c.Cards.SequenceEqual([card])); Play(game);
    }
    private static void Use(GameEngine game, string binding, IReadOnlyList<int>? cards = null, IReadOnlyList<int>? targets = null) =>
        Accept(game, new UseProgramSkillCommand(0, Driver, binding, cards ?? [], targets ?? [], game.Revision, P(game)!.PromptId));
    private static void End(GameEngine game) => Accept(game, new EndPlayPhaseCommand(P(game)!.PlayerSeat, game.Revision, P(game)!.PromptId));
    private static void Play(GameEngine game) => Reach(game, () => P(game) is { Kind: DecisionKind.PlayCard } p && V(game, p.PlayerSeat).IsHuman);
    private static void Reach(GameEngine game, Func<bool> done)
    {
        for (var step = 0; step < 240; step++)
        {
            if (done()) return;
            Need(game.State.Winner == Winner.None, "The real Daili fixture ended before its expected boundary.");
            var prompt = P(game);
            if (prompt is { Kind: DecisionKind.PlayCard } && V(game, prompt.PlayerSeat).IsHuman) End(game);
            else if (Action(game, "skip")) Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (prompt is { Kind: DecisionKind.RescueDying } && prompt.Choices.Any(c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass"))
                Answer(game, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Missing native Daili boundary: " + JsonSerializer.Serialize(new { Pending = P(game), Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = game.Events.TakeLast(12).Select(e => e.Payload.ToString()) }));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(game.CreateSnapshot(s))).ToArray(),
        Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), game.CardMovements
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(State(game) == State(restored), "Cold command replay retains exact public hand residencies, source, ended turn, paid flip, actual drawn entities, private viewers and native children.");
        return restored;
    }
    private static (GameEngine, ContentRegistry) Start(string scenario = "plain", int human = 0, bool stopAtEnding = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = human, HumanRole = human == 0 ? Role.Lord : Role.Rebel,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, () => P(game) is { Kind: DecisionKind.SelectGeneral });
        Accept(game, new SelectGeneralCommand(human, Owner, game.Revision, P(game)!.PromptId));
        if (stopAtEnding) Reach(game, () => Offer(game));
        else if (scenario == "private")
        {
            Reach(game, () => P(game)?.SkillPrompt?.SkillId == Inspect && P(game)!.Choices.Any(c => c.Targets.SequenceEqual([1])));
            Answer(game, c => c.Targets.SequenceEqual([1])); Reach(game, () => Action(game, "private-hand-take"));
        }
        else Play(game);
        return (game, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-luo-xian", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "ol:luo-xian");
            var rules = JsonNode.Parse($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[
                {"id":"show","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedCards","target":"owner","zones":["hand"],"minimumCards":1,"maximumCards":1,"resultBind":"shown"},{"op":"revealBoundCards","target":"owner","sourceBind":"shown"}]},
                {"id":"show-peer","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":1,"resultBind":"foreign"},{"op":"revealBoundCards","target":"owner","sourceBind":"foreign"}]},
                {"id":"give","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectOwnedHandRankSum","target":"owner","exactRankSum":7,"resultBind":"gift"},{"op":"moveBoundCards","target":"owner","sourceBind":"gift","destination":"selectedTargetHand","awaitMovementTriggers":true}]},
                {"id":"take","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","awaitMovementTriggers":true}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            var names = new Dictionary<string, object> { [Driver] = new { name = "真实公开手牌驱动", description = "实际展示与移动" }, [Quiet] = new { name = "安静的其他角色", description = "原生跳过出牌" } };
            var extra = new List<string> { Skill };
            if (scenario == "private")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Inspect}}","revision":1,"triggers":[{"id":"actual-private-inspection","window":"drawPhaseEnded","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"viewAndTakeSelectedTargetHand","target":"selectedTarget","amount":1}]}]}"""));
                names[Inspect] = new { name = "实际私下观看", description = "实际私有手牌窗口" }; extra.Add(Inspect);
            }
            if (scenario == "children")
            {
                extra.Remove(Skill); extra.AddRange(["fixture:luoxian-conditional", Flip, Gain]);
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"fixture:luoxian-conditional","revision":1,"hpSkillGrants":[{"id":"daili-at-thirteen","skillId":"{{Skill}}","minimumHp":13}]}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Flip}}","revision":1,"triggers":[{"id":"actual-paid-flip-source-loss","window":"characterTurnedOver","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"flipped","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"lost","options":[{"id":"after-loss"}]}]}]}"""));
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Gain}}","revision":1,"triggers":[{"id":"actual-drawn-entity-leaves","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.daili.draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"drawn","options":[{"id":"continue"}]},{"op":"selectOwnedCards","target":"owner","zones":["hand"],"minimumCards":1,"maximumCards":1,"resultBind":"departed"},{"op":"moveBoundCards","target":"owner","sourceBind":"departed","destination":"discardPile","awaitMovementTriggers":true},{"op":"draw","target":"owner","amount":1},{"op":"chooseOption","target":"owner","resultBind":"replaced","options":[{"id":"after-replacement"}]}]}]}"""));
                names["fixture:luoxian-conditional"] = new { name = "实际体力技能来源", description = "实际主公13体力授予带砺" };
                names[Flip] = new { name = "实际翻面后失源子窗", description = "原生翻面与失源", optionLabels = new Dictionary<string, string> { ["continue"] = "继续", ["after-loss"] = "继续原技能" } };
                names[Gain] = new { name = "实际摸牌后移走子窗", description = "移走本次实体并另摸替代牌", optionLabels = new Dictionary<string, string> { ["continue"] = "继续", ["after-replacement"] = "继续展示原实体" } };
            }
            if (scenario == "death")
            {
                extra.Add(Gain);
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{Gain}}","revision":1,"triggers":[{"id":"actual-draw-owner-death","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.daili.draw"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"chooseOption","target":"owner","resultBind":"drawn","options":[{"id":"continue"}]},{"op":"loseHp","target":"owner","amount":20}]}]}"""));
                names[Gain] = new { name = "实际摸牌后死亡子窗", description = "原生失去体力死亡", optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            }
            if (scenario == "dead-turn-owner")
            {
                rules["skills"]!.AsArray().Add(JsonNode.Parse($$"""{"id":"{{EndingDeath}}","revision":1,"triggers":[{"id":"actual-owner-ending-damage","window":"turnEnding","subject":"owner","optional":false,"effects":[{"op":"damage","target":"owner","amount":20}]}]}"""));
                names[EndingDeath] = new { name = "实际回合主人死亡", description = "在原生结束阶段承受20点伤害" };
            }
            var catalog = SkillProgramCatalog.Load(rules.ToJsonString(), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = names }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
            { Program = program, ProgramPresentation = catalog.Presentations[id], SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => id == Quiet ? 10000d : -10000d) });
            builder.AddGeneral(new(Owner, "实际罗宪", "ol-luo-xian", Driver, "shu", 12, extra, GeneralGender.Male));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:luoxian-peer-{i}").ToArray();
            foreach (var peer in peers) builder.AddGeneral(new(peer, "实际其他角色", "supporter", Quiet, "wei", 12,
                scenario == "dead-turn-owner" ? [EndingDeath] : []));
            builder.AddDeck(new("fixture:luoxian-deck", "固定实体牌堆", 4, 0, []) { PhysicalCards = Enumerable.Range(0, 64).Select(_ => new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, 7)).ToArray() });
            builder.AddMode(new(Mode, "真实带砺边界", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:luoxian-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
