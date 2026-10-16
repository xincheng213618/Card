using CardGame.Content.Standard;
using CardGame.Core;

internal static class Fame2015ShuChecks
{
    public static void AllHandDuelDrawLimitPaymentAndReplay()
    {
        foreach (var failure in new[] { false, true })
        {
            var registry = Registry(new Fixture("classic:zhanjue", failure ? ["standard:slash"] : ["standard:dodge"], targetHp: failure ? 1 : 10));
            var game = Create(registry); ReachPlay(game);
            var firstHand = game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).Order().ToArray();
            Atomic(game, new UseProgramSkillCommand(0, "classic:zhanjue", "all-hand-duel", firstHand[..^1], [], game.Revision, Prompt(game)!.PromptId));
            var count = failure ? 1 : 2;
            for (var use = 0; use < count; use++)
            {
                var hand = game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).Order().ToArray();
                Activate(game, "classic:zhanjue", "all-hand-duel", hand, []);
                Require(Prompt(game)!.Choices.All(choice => choice.Parameters.GetValueOrDefault("card-kind") == nameof(CardKind.Duel)), "A fixed all-hand conversion must expose only Duel.");
                var restored = Restore(game, registry);
                Both(game, restored, choice => choice.Targets.SequenceEqual(new[] { 1 })); Settle(game); Settle(restored); Equal(game, restored);
                foreach (var card in hand)
                    Require(game.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1,
                        "All-hand Duel must pay every selected physical owner card exactly once.");
                Require(game.CreateSnapshot(0, true).Players[0].Hand.Count == (failure ? 2 : 1), $"Successful Duel draws one owner card; suffering its actual damage draws two. failure={failure}, use={use}, hand={game.CreateSnapshot(0, true).Players[0].Hand.Count}, damage={System.Text.Json.JsonSerializer.Serialize(game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().ToArray())}");
                if (!failure && use == 0) Require(game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "classic:zhanjue"), "One actual owner draw must leave a second Duel legal.");
            }
            Require(game.GetHumanLegalActions().All(action => action.ProgramSkillId != "classic:zhanjue"), "Two actual participant draws must disable all-hand Duel for the phase.");
            Require(game.Events.Any(item => item.Payload is ProgramBooleanStateChangedEvent { StateId: "zhanjue-exhausted", Value: true }), "The threshold must publish its typed public state.");
        }
        {
            var registry = Registry(new Fixture("classic:zhanjue", ["standard:dodge"], actorReplacement: true));
            var game = Create(registry); ReachPlay(game);
            Activate(game, "classic:zhanjue", "all-hand-duel", game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToArray(), []);
            Answer(game, choice => choice.Targets.SequenceEqual(new[] { 1 }));
            Reach(game, prompt => prompt.SkillPrompt?.SkillId == "classic:zenhui" && prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate"));
            Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
            Answer(game, choice => choice.Targets.SequenceEqual(new[] { 2 }));
            Answer(game, choice => choice.Parameters.GetValueOrDefault("option-id") == "become-user");
            Require(game.ResolutionStack.OfType<CardUseFrame>().Single().Action is { ProviderSeat: 0 } action && action.ConversionChain.Any(source => source.OwnerSeat == 0 && source.SkillId == "classic:zhanjue"), "Actor replacement must retain the original all-hand conversion owner and physical provider in its frozen use.");
            var restored = Restore(game, registry); Both(game, restored, choice => choice.Cards.Count == 1);
            Require(game.Events.Any(item => item.Payload is ProgramCardUseActorReplacedEvent { ActorSeat: 2, ProviderSeat: 0 }), "The typed replacement must retain its new actor and original physical provider.");
            Settle(game); Settle(restored); Equal(game, restored);
            Require(game.CardMovements.Count(move => move.To == CardLocation.Hand(0) && move.Reason.Value == "skill-program.classic:zhanjue.completed-participant-draw") == 1 &&
                game.CardMovements.All(move => move.To != CardLocation.Hand(2) || move.Reason.Value != "skill-program.classic:zhanjue.completed-participant-draw"), "A replaced Duel actor must not receive the original skill owner's completion draw.");
        }
        {
            var registry = Registry(new Fixture("classic:zhanjue", ["standard:dodge"], targetHp: 1));
            var game = Create(registry); ReachPlay(game);
            Activate(game, "classic:zhanjue", "all-hand-duel", game.CreateSnapshot(0, true).Players[0].Hand.Select(card => card.Id).ToArray(), []);
            var restored = Restore(game, registry); Both(game, restored, choice => choice.Targets.SequenceEqual(new[] { 1 }));
            Settle(game); Settle(restored); Equal(game, restored);
            Require(!game.CreateSnapshot(0, true).Players[1].IsAlive && game.CardMovements.Count(move => move.To == CardLocation.Hand(0) && move.Reason.Value == "skill-program.classic:zhanjue.completed-participant-draw") == 1 &&
                game.Events.All(item => item.Payload is not ProgramBooleanStateChangedEvent { StateId: "zhanjue-exhausted", Value: true }), "A killed Duel target must leave the original owner completion draw intact; native kill rewards must not consume its two-draw limit.");
        }
    }

    private static ContentRegistry Registry(Fixture fixture) => ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), fixture);
    private static GameEngine Create(ContentRegistry registry, int seed = 31, Role humanRole = Role.Lord)
    {
        var game = GameEngine.CreateStandard(new GameOptions { Seed = seed, HumanSeat = 0, HumanRole = humanRole, PlayerCount = 4,
            ModeId = "identity:classic-fame2015-shu", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 40 }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Fixture must start.");
        Reach(game, prompt => prompt.PlayerSeat == 0 && prompt.Kind == DecisionKind.SelectGeneral);
        Require(game.Submit(new SelectGeneralCommand(0, "fixture:fame2015-shu-owner", game.Revision, Prompt(game)!.PromptId)).Accepted, "Fixture owner must be selected."); return game;
    }
    private static void Activate(GameEngine game, string skill, string activation, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
        Require(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([new UseProgramSkillCommand(0, skill, activation, cards, targets, game.Revision, Prompt(game)!.PromptId)])).Single()).Accepted, "A published generic activation must survive command JSON and be accepted.");
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat, true).PendingDecision).FirstOrDefault(prompt => prompt is not null);
    private static void ReachPlay(GameEngine game) => Reach(game, prompt => prompt.Kind == DecisionKind.PlayCard && prompt.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> condition)
    { for (var step = 0; step < 256; step++) { if (Prompt(game) is { } prompt && condition(prompt)) return; Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "Fixture advancement must be accepted."); } throw new InvalidOperationException($"Fixture failed to reach its required boundary. pending={Prompt(game)?.Kind}/{Prompt(game)?.PlayerSeat}, duel={System.Text.Json.JsonSerializer.Serialize(game.Events.Select(item => item.Payload).OfType<DuelResponseEvent>().ToArray())}"); }
    private static void Settle(GameEngine game)
    {
        for (var step = 0; step < 256 && game.ResolutionStack.Count > 0; step++)
            if (Prompt(game) is { Kind: DecisionKind.Nullification, PlayerSeat: 0 }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "pass");
            else if (Prompt(game) is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 }) Answer(game, choice => choice.Parameters.GetValueOrDefault("response") == "decline" || choice.Parameters.GetValueOrDefault("response") == "faction-slash-decline");
            else Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted, "A physical skill resolution must advance.");
        Require(game.ResolutionStack.Count == 0, "The physical resolution must leave no paused frame.");
        if (game.CreateSnapshot(0, true) is { CurrentSeat: 0, Phase: TurnPhase.Play } && Prompt(game) is null) ReachPlay(game);
    }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Prompt(game)!; var choice = prompt.Choices.First(predicate); Require(game.Submit(CommandJson.Deserialize(CommandJson.Serialize([new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision)])).Single()).Accepted, "A published physical choice must survive command JSON."); }
    private static void Both(GameEngine a, GameEngine b, Func<PromptChoice, bool> predicate) { var id = Prompt(a)!.Choices.First(predicate).Id; Answer(a, choice => choice.Id == id); Answer(b, choice => choice.Id == id); }
    private static GameEngine Restore(GameEngine game, ContentRegistry registry) => GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
    private static void Atomic(GameEngine game, GameCommand command)
    { var before = GameCheckpointJson.Serialize(game.CreateCheckpoint()); var moves = game.CardMovements.Count; Require(!game.Submit(command).Accepted && before == GameCheckpointJson.Serialize(game.CreateCheckpoint()) && moves == game.CardMovements.Count, "Illegal physical input must reject atomically without moving cards or consuming a phase allowance."); }
    private static void Equal(GameEngine a, GameEngine b) => Require(SnapshotJson.Serialize(a.CreateSnapshot(0, true)) == SnapshotJson.Serialize(b.CreateSnapshot(0, true)) && a.CardMovements.SequenceEqual(b.CardMovements) &&
        a.Events.Select(item => System.Text.Json.JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(b.Events.Select(item => System.Text.Json.JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))), "Checkpoint replay must reproduce typed events, snapshots and the physical ledger.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(string skill, IReadOnlyList<string> cards, bool allShu = false, bool boundaryDriver = false, int targetHp = 10, bool actorReplacement = false, bool responseDraw = false, bool factionDefense = false, int ownerHp = 10, bool responseTarget = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-fame2015-shu", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (boundaryDriver)
            {
                var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:fame2015-health","revision":1,"activations":[
                  {"id":"lose","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},
                  {"id":"recover","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recover","target":"owner","amount":1}]}
                ]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:fame2015-health":{"name":"体力边界","description":"真实体力变化"}}}""");
                builder.AddSkill(new("fixture:fame2015-health", "体力边界", "真实体力变化") { Program = catalog.Programs["fixture:fame2015-health"] });
            }
            if (responseDraw)
            {
                var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:fame2015-response-draw","revision":1,
                 "triggers":[{"id":"draw-after-dodge","window":"cardResponseAccepted","ownerRelation":"actor","cardKinds":["dodge"],
                   "optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:fame2015-response-draw":{"name":"响应摸牌","description":"响应后摸真实牌堆顶"}}}""");
                builder.AddSkill(new("fixture:fame2015-response-draw", "响应摸牌", "响应后摸真实牌堆顶") { Program = catalog.Programs["fixture:fame2015-response-draw"] });
            }
            if (responseTarget)
            {
                var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:fame2015-response-target","revision":1,
                 "triggers":[{"id":"also-defend-this-slash","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],
                  "condition":{"kind":"compare","left":{"kind":"cardUseDesignatedTargetCount"},"operator":"equal","right":{"kind":"integerConstant","value":1}},
                  "optional":false,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLegalCurrentCardTarget"},{"op":"addCurrentCardUseTarget","target":"selectedTarget"}]},
                 {"id":"slash-after-arrow","window":"cardUseCompleted","ownerRelation":"actor","cardKinds":["arrowBarrage"],"optional":false,
                  "effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
                   {"op":"useVirtualSlash","target":"selectedTarget"}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:fame2015-response-target":{"name":"追加响应目标","description":"同一张杀先护驾提供再本人防御"}}}""");
                builder.AddSkill(new("fixture:fame2015-response-target", "追加响应目标", "同一张杀先护驾提供再本人防御")
                {
                    Program = catalog.Programs["fixture:fame2015-response-target"],
                    SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = 100 }
                });
            }
            builder.AddGeneral(new("fixture:fame2015-shu-owner", "测试蜀将", "supporter", skill, factionDefense ? "wei" : "shu", BaseHp: ownerHp, AdditionalSkillIds: boundaryDriver ? ["fixture:fame2015-health"] : actorReplacement ? ["classic:zenhui"] : responseDraw ? ["fixture:fame2015-response-draw"] : [], Gender: skill == "classic:yanyu" ? GeneralGender.Female : GeneralGender.Male));
            for (var index = 1; index < 4; index++) builder.AddGeneral(new($"fixture:fame2015-shu-target-{index}", $"目标{index}", "supporter", factionDefense ? "classic:hujia" : "standard:none", allShu ? "shu" : "wei", BaseHp: targetHp, AdditionalSkillIds: responseTarget ? ["fixture:fame2015-response-target"] : [], Gender: GeneralGender.Male));
            builder.AddDeck(new("fixture:fame2015-shu-deck", "测试牌堆", 8, 0, []) { PhysicalCards = Enumerable.Range(0, 160).Select(index => new ContentDeckPhysicalCard(cards[index % cards.Count], Suit.Spade, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-fame2015-shu", "蜀将机制测试", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:fame2015-shu-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:fame2015-shu-owner", "fixture:fame2015-shu-target-1", "fixture:fame2015-shu-target-2", "fixture:fame2015-shu-target-3"]));
        }
    }
}
