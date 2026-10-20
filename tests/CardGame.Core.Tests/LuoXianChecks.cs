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
            Need(shown.Length == (own ? 5 : 3) && Enumerable.Range(0, 4).All(s => game.CreateSnapshot(s).PublicRevealedCards.Count == 0) &&
                Enumerable.Range(1, 3).All(s => game.CreateSnapshot(s).Players[0].Hand.Count == 0),
                "Daili retains its historical shown-hand count, while completed reveals no longer expose any hand face to viewers.");
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
    public static void TemporaryRevealsReturnToOpaqueTargetSlotsCold()
    {
        var (game, registry) = Start("temporary-visibility");
        Native(CardKind.FireAttack);
        Reach(game, () => P(game)?.Kind == DecisionKind.FireAttackDiscard);
        var revealed = E<FireAttackCardRevealedEvent>(game).Single().CardId;
        Need(Enumerable.Range(0, 4).All(viewer => game.CreateSnapshot(viewer).PublicRevealedCards.Any(card => card.Id == revealed)),
            "The actual FireAttack response window must still show its currently revealed hand card.");
        Answer(game, choice => choice.Cards.Count == 0);
        Play(game);
        Concealed();
        Need(Shown(game, 1).SequenceEqual([revealed]), "Ending the display must preserve Daili's historical shown-card fact.");
        game = Cold(game, registry);
        Concealed();

        Accept(game, new UseProgramSkillCommand(0, "classic:gongxin", "watch-and-dispose", [], [1], game.Revision, P(game)!.PromptId));
        Reach(game, () => Action(game, "reveal-target-hand-card-decline"));
        Need(P(game) is { IsPrivate: true } inspection && inspection.ValidCardIds.Count == V(game, 1).HandCount &&
            game.CreateSnapshot(2).PendingDecision is null,
            "Gongxin must reach its actual private whole-hand inspection without exposing that prompt to another viewer.");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "reveal-target-hand-card-decline");
        Play(game);
        Concealed();

        var count = V(game, 1).HandCount;
        Native(CardKind.Dismantlement);
        Reach(game, () => P(game)?.Kind == DecisionKind.SelectTargetCard);
        Need(P(game) is { IsPrivate: true } selection && selection.Choices.Count == count &&
            selection.Choices.All(choice => choice.Cards.Count == 0 && choice.Parameters.GetValueOrDefault("action") == "target-card-slot"),
            "After FireAttack and Gongxin, Dismantlement must publish only current opaque slots without carrying known entity faces forward.");
        Concealed();
        game = Cold(game, registry);
        Answer(game, _ => true);
        Play(game);
        Need(V(game, 1).HandCount == count - 1, "The opaque choice must still discard exactly one actual hand card.");
        Concealed();
        _ = Cold(game, registry);

        void Native(CardKind kind)
        {
            var action = game.GetHumanLegalActions().First(action => action.PlayedCardKind == kind && action.TargetSeat == 1 &&
                action.TargetCardId is null && action.ConversionSource?.SkillId == Driver);
            Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, P(game)!.PromptId,
                action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
        }
        void Concealed()
        {
            Need(Enumerable.Range(0, 4).All(viewer => game.CreateSnapshot(viewer).PublicRevealedCards.Count == 0 &&
                game.CreateSnapshot(viewer).PrivateRevealedCards is not { Count: > 0 }) &&
                Enumerable.Range(0, 4).Where(viewer => viewer != 1).All(viewer => game.CreateSnapshot(viewer).Players[1].Hand.Count == 0),
                "Completed viewing must leave no current public/private hand faces, even after restore or another opaque selection.");
        }
    }

    private static int[] Shown(GameEngine game, int owner) => E<PublicHandEntityRevealedEvent>(game)
        .Where(e => e.OwnerSeat == owner && V(game, owner).Hand.Any(card => card.Id == e.Card.Id) &&
            !game.CardMovements.Any(move => move.CardId == e.Card.Id && move.Sequence > e.HandEntryMovementSequence && move.From != move.To))
        .Select(e => e.Card.Id).Distinct().Order().ToArray();
    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine game) => Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static T[] E<T>(GameEngine game) where T : IGameEvent => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static bool Offer(GameEngine game) => P(game)?.SkillPrompt?.SkillId == Skill && Action(game, "activate");
    private static bool Action(GameEngine game, string action) => P(game)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == action) == true;
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Rejected real Daili command."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    { var prompt = P(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision)); }
    private static void Activate(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
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
            if (scenario == "temporary-visibility")
            {
                CharacterFixtureContent.Register(builder, "classic:shen-lu-meng");
                extra.Add("classic:gongxin");
                rules["skills"]![0]!["viewAs"] = JsonNode.Parse("""
                    [{"id":"fire","inputKinds":[],"inputSuits":[],"outputKind":"fireAttack","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true},
                     {"id":"dismantle","inputKinds":[],"inputSuits":[],"outputKind":"dismantlement","forPlay":true,"forResponse":false,"useOnly":true,"singleCardTrickUse":true}]
                    """);
            }
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
