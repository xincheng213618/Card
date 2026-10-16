using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class CorrelatedActiveSkillAiChecks
{
    private enum Scenario { HandPair, Equipment }
    private const string Dimeng = "boundary:dimeng-current", Zhijian = "boundary:zhijian";
    private const string ExchangeReason = "skill-program." + Dimeng + ".exchange";
    private const string DebtReason = "skill-program." + Dimeng + ".MoveBoundCards";

    public static void Run(int policyVersion)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        VerifyMissingPolicy(registry);
        var rejectedCombinations = new List<string>();
        foreach (var scenario in Enum.GetValues<Scenario>())
        {
            var game = Create(registry, scenario, policyVersion);
            var view = game.CreateSnapshot(0);
            var action = game.GetHumanActionView().LegalActions.Single(candidate => candidate.ProgramSkillId == Skill(scenario) &&
                candidate.ProgramActivationId == Activation(scenario));
            var before = game.CreateCheckpoint();
            var unchanged = Boundary(game);
            var expectedOldCards = scenario == Scenario.HandPair ? Array.Empty<int>() :
                new[] { view.Players[0].Hand.Where(card => card.Kind == CardKind.BaguaFormation).Min(card => card.Id) };
            var expectedOldTargets = scenario == Scenario.HandPair
                ? view.Players.Where(player => player.Seat != 0 && player.HandCount is 1 or 3).Select(player => player.Seat).Order().ToArray()
                : new[] { view.Players.Single(player => Capacity(player, EquipmentSlot.Armor) == 0).Seat };
            var expectedError = scenario == Scenario.HandPair ? CommandErrorCode.InvalidTarget : CommandErrorCode.InvalidCard;
            foreach (var historicalPolicy in new[] { 1, 2, 3 })
            {
                var oldBrain = new SimpleAiBrain(0, 31, historicalPolicy);
                var oldCards = oldBrain.ChooseActiveSkillCards(view, action);
                var oldTargets = oldBrain.ChooseActiveSkillTargets(view, action);
                var oldGame = GameReplay.Restore(before, registry);
                var oldResult = oldGame.Submit(Command(oldGame, scenario, oldCards, oldTargets));
                Console.WriteLine($"Correlated AI {scenario}/legacy{historicalPolicy}: cards={string.Join(',', oldCards)}, " +
                    $"targets={string.Join(',', oldTargets)}, nativeAccepted={oldResult.Accepted}, error={oldResult.Error?.Code}.");
                Require(oldCards.SequenceEqual(expectedOldCards) && oldTargets.SequenceEqual(expectedOldTargets) &&
                    !oldResult.Accepted && oldResult.Error?.Code == expectedError && Boundary(oldGame) == unchanged,
                    "The recorded historical policies must retain their original independent choices and unchanged native rejection, rather than silently adopting new target correlations.");
            }

            var brain = new SimpleAiBrain(0, 31, policyVersion);
            var cards = brain.ChooseActiveSkillCards(view, action);
            var targets = brain.ChooseActiveSkillTargets(view, action, cards);
            Require(targets.SequenceEqual(brain.ChooseActiveSkillTargets(view, action)),
                "The retained two-argument API must derive the same current legal combination as the actual-material overload.");
            if (scenario == Scenario.Equipment && policyVersion >= AiDecisionPolicy.CorrelatedActiveSkillVersion)
            {
                var weapon = view.Players[0].Hand.Where(card => card.Kind == CardKind.Crossbow).Min(card => card.Id);
                var weaponTargets = brain.ChooseActiveSkillTargets(view, action, [weapon]);
                Require(weaponTargets.SequenceEqual(expectedOldTargets),
                    "Passing the actual weapon must retain its preferred partial-slot recipient rather than silently reselecting cheapest armor.");
                var weaponGame = GameReplay.Restore(before, registry);
                Require(weaponGame.Submit(Command(weaponGame, scenario, [weapon], weaponTargets)).Accepted,
                    "The actual-material overload's different weapon pair must also be accepted by native validation.");
            }
            Require(Boundary(game) == unchanged, "Standalone AI material and target queries cannot advance the native game or mutate its player views.");
            var reference = GameReplay.Restore(before, registry);
            var referenceResult = reference.Submit(Command(reference, scenario, cards, targets));
            var result = game.Submit(Command(game, scenario, cards, targets));
            Console.WriteLine($"Correlated AI {scenario}/policy{policyVersion}: cards={string.Join(',', cards)}, " +
                $"targets={string.Join(',', targets)}, nativeAccepted={result.Accepted}, error={result.Error?.Code}, " +
                $"cardPairs={action.SelectableCardTargets?.Count}, targetSequences={action.SelectableTargetSequences?.Count}.");
            if (!result.Accepted || !referenceResult.Accepted)
            {
                rejectedCombinations.Add($"{scenario}: {result.Error?.Code}");
                continue;
            }
            Require(game.Revision == before.Revision + 1 && game.AcceptedCommands.Count == before.Commands.Count + 1 &&
                game.AcceptedCommands.Last() is UseProgramSkillCommand submitted && submitted.ActorSeat == 0 &&
                submitted.SkillId == Skill(scenario) && submitted.ActivationId == Activation(scenario) &&
                submitted.CardIds.SequenceEqual(cards) && submitted.TargetSeats.SequenceEqual(targets) &&
                submitted.ExpectedRevision == before.Revision && submitted.PromptId == view.PendingDecision!.PromptId &&
                Boundary(game) == Boundary(reference),
                "The AI-derived activation must commit exactly one original native command and match the independently executed four safe views, movements and events.");
            Require(scenario == Scenario.HandPair
                    ? cards.Count == 0 && action.SelectableTargetSequences!.Any(sequence => sequence.SequenceEqual(targets))
                    : cards.Count == 1 && targets.Count == 1 &&
                      action.SelectableCardTargets!.Contains(new(cards[0], targets[0])),
                "The selected current inputs must match an exact published complete target sequence or physical-fee/recipient association.");
            Cold(game, registry);
            for (var step = 0; step < 16 && game.CreateSnapshot(0).PendingDecision is not
                     { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            {
                Accept(reference, new AdvanceOneStepCommand(reference.Revision));
                Accept(game, new AdvanceOneStepCommand(game.Revision));
                Require(Boundary(game) == Boundary(reference),
                    "Each short native activation continuation must reproduce the exact independent result.");
            }
            Require(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
                "The small actual activation must return to its original human Play boundary without completing a match.");
            if (scenario == Scenario.HandPair) VerifyExchange(game, before, targets);
            else VerifyEquipment(game, before, cards[0], targets[0]);
            Cold(game, registry);
        }
        Require(rejectedCombinations.Count == 0,
            "The current AI selected native rejected combinations: " + string.Join("; ", rejectedCombinations));
        VerifyNativeAdvance(registry, policyVersion);
    }

    private static void VerifyNativeAdvance(ContentRegistry registry, int policyVersion)
    {
        // The real human selects an ordinary one-card general. Every other general
        // is selected natively by AI; the only remaining Dimeng holder is an AI.
        var game = Create(registry, Scenario.HandPair, policyVersion, ownerIsAi: true);
        var view = game.CreateSnapshot(0);
        var owner = view.Players.Single(player => player.GeneralId == Fixture.Owner(Scenario.HandPair)).Seat;
        Require(owner != 0 && view.Players[owner].HandCount == 1 && view.Players[owner].Equipment.Count == 0 &&
            view.Players.Where(player => player.Seat != owner).Select(player => player.HandCount).Order().SequenceEqual([1, 2, 3]),
            "Actual interactive AI general selection must leave the only formal Dimeng holder outside the human seat with one HE and real other hands 1/2/3.");
        var before = game.CreateCheckpoint();
        var reference = GameReplay.Restore(before, registry);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, view.PendingDecision!.PromptId));
        Accept(reference, new EndPlayPhaseCommand(0, reference.Revision, reference.CreateSnapshot(0).PendingDecision!.PromptId));
        PhaseHandExchangeDebtIssuedEvent? debt = null;
        for (var step = 0; step < 80 && debt is null; step++)
        {
            debt = game.Events.Select(item => item.Payload).OfType<PhaseHandExchangeDebtIssuedEvent>()
                .SingleOrDefault(item => item.Source.SkillId == Dimeng && item.Source.OwnerSeat == owner);
            if (debt is not null) break;
            Require(game.CreateSnapshot(0).Status != EngineStatus.Completed,
                "The short native AI witness cannot finish its match before the original holder's first real exchange.");
            Accept(reference, new AdvanceOneStepCommand(reference.Revision));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
            Require(Boundary(game) == Boundary(reference),
                "Each real Start/End/Advance-driven AI decision must replay identically with its recorded policy.");
        }
        Require(debt is not null && debt.FirstSeat != owner && debt.SecondSeat != owner &&
            Math.Abs(view.Players[debt.FirstSeat].HandCount - view.Players[debt.SecondSeat].HandCount) == 1 &&
            debt.FrozenDifference == 1 &&
            game.AiThoughts.Any(thought => thought.ActorSeat == owner && thought.Candidates.Any(candidate =>
                candidate.Action.ProgramSkillId == Dimeng && candidate.Action.ProgramActivationId == "swap-first")) &&
            game.AcceptedCommands.All(command => command is not (UseProgramSkillCommand or AnswerPromptCommand)),
            "The engine's own RunOneAiPlayDecision must select and execute a real payable pair solely through bounded native Advance, without a forced skill command, private state edit or fabricated action.");
        Console.WriteLine($"Correlated AI native Advance/policy{policyVersion}: owner={owner}, " +
            $"orderedTargets={debt!.FirstSeat},{debt.SecondSeat}, frozenDifference={debt.FrozenDifference}.");
        VerifyExchange(game, before, [debt.FirstSeat, debt.SecondSeat], owner);
        Cold(game, registry);
    }

    private static void VerifyMissingPolicy(ContentRegistry registry)
    {
        var original = Create(registry, Scenario.HandPair, 1);
        var json = JsonNode.Parse(GameCheckpointJson.Serialize(original.CreateCheckpoint()))!;
        Require(json["Options"]!.AsObject().Remove("AiPolicyVersion"),
            "The small policy-one checkpoint must originally record its decision policy.");
        var checkpoint = GameCheckpointJson.Deserialize(json.ToJsonString());
        Require(checkpoint.Options.AiPolicyVersion == 1,
            "Removing the old optional AI policy field must retain default policy one.");
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(Boundary(restored) == Boundary(original),
            "A checkpoint with an omitted AI policy must restore the policy-one four safe views, accepted commands, events and movements exactly.");
    }

    private static void VerifyExchange(GameEngine game, GameCheckpoint before, IReadOnlyList<int> targets, int owner = 0)
    {
        var original = GameReplay.Restore(before, game.ContentRegistry);
        var first = OwnHand(original, targets[0]);
        var second = OwnHand(original, targets[1]);
        var ownerCards = OwnHand(original, owner);
        var third = Enumerable.Range(0, 4).Single(seat => seat != owner && !targets.Contains(seat));
        var thirdCards = OwnHand(original, third);
        Require(OwnHand(game, targets[0]).SequenceEqual(second) && OwnHand(game, targets[1]).SequenceEqual(first) &&
            OwnHand(game, owner).SequenceEqual(ownerCards) && OwnHand(game, third).SequenceEqual(thirdCards),
            "The exact two original physical hands must exchange while the source and unused peer retain every original entity.");
        var moved = game.CardMovements.Skip(original.CardMovements.Count).ToArray();
        var facts = game.Events.Skip(original.Events.Count).Select(item => item.Payload).ToArray();
        Require(moved.Length == 2 * (first.Length + second.Length) && moved.All(move => move.Reason.Value == ExchangeReason) &&
            first.All(id => Paid(id, targets[0], targets[1])) && second.All(id => Paid(id, targets[1], targets[0])) &&
            facts.OfType<PhaseHandExchangeDebtIssuedEvent>().ToArray() is [var debt] && debt.Source.OwnerSeat == owner &&
            debt.FirstSeat == targets[0] && debt.SecondSeat == targets[1] && debt.FirstCount == first.Length &&
            debt.SecondCount == second.Length && debt.FrozenDifference == 1 &&
            !facts.OfType<PhaseHandExchangeDebtPaymentStartedEvent>().Any() && !moved.Any(move => move.Reason.Value == DebtReason),
            "The actual AI exchange must transfer each entity once through Processing, issue one ordered frozen debt, and never prepay its later Ending cost.");
        bool Paid(int id, int from, int to) => moved.Count(move => move.CardId == id && move.From == CardLocation.Hand(from) &&
            move.To == CardLocation.Processing && move.Reason.Value == ExchangeReason) == 1 &&
            moved.Count(move => move.CardId == id && move.From == CardLocation.Processing &&
                move.To == CardLocation.Hand(to) && move.Reason.Value == ExchangeReason) == 1;
    }

    private static void VerifyEquipment(GameEngine game, GameCheckpoint before, int card, int target)
    {
        var original = GameReplay.Restore(before, game.ContentRegistry);
        var physical = original.CreateSnapshot(0).Players[0].Hand.Single(item => item.Id == card);
        var moved = game.CardMovements.Skip(original.CardMovements.Count).Where(move => move.CardId == card).ToArray();
        var facts = game.Events.Skip(original.Events.Count).Select(item => item.Payload).ToArray();
        Require(physical.Kind == CardKind.BaguaFormation && Capacity(game.CreateSnapshot(0).Players[target], EquipmentSlot.Armor) > 0 &&
            !OwnHand(game, 0).Contains(card) && game.CreateSnapshot(0).Players[target].Equipment.Single(item => item.Id == card).Kind == physical.Kind &&
            moved is [var placed] && placed.From == CardLocation.Hand(0) && placed.To == CardLocation.Equipment(target) &&
            placed.Reason == CardMoveReasons.EquipmentEnter &&
            facts.OfType<EquipmentChangedEvent>().Count(item => item.CardId == card && item.PlayerSeat == target) == 1 &&
            facts.OfType<CapturedEquipmentPlacedEvent>().Count() == 1 &&
            facts.OfType<CapturedEquipmentRewardDrawnEvent>().ToArray() is [var drawn] && drawn.Actual == 1,
            "The cheapest real Bagua cost must enter a payable recipient once and drain the formal Zhijian continuation into exactly one real owner reward draw.");
    }

    private static GameEngine Create(ContentRegistry registry, Scenario scenario, int policyVersion, bool ownerIsAi = false)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Fixture.Mode(scenario),
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false,
            AiPolicyVersion = policyVersion, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Require(game.CreateSnapshot(0).PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 },
            "The correlated AI fixture must start through the native private general-selection pipeline.");
        Accept(game, new SelectGeneralCommand(0, ownerIsAi ? Fixture.Peer(scenario, 1) : Fixture.Owner(scenario),
            game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId));
        for (var step = 0; step < 40 && game.CreateSnapshot(0).PendingDecision is not
                 { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }; step++)
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        var view = game.CreateSnapshot(0);
        Require(view.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && view.Players.All(player => player.IsAlive),
            "Native setup and bounded startup advancement must reach the original human Play with four living generals.");
        var owner = view.Players.Single(player => player.GeneralId == Fixture.Owner(scenario));
        Require(owner.Equipment.Count == 0 && owner.HandCount == (scenario == Scenario.HandPair ? 1 : 10),
            "The only formal active-skill holder must receive its exact native one-HE or ten-of-twelve hand.");
        if (scenario == Scenario.HandPair)
            Require(view.Players.Where(player => player.Seat != owner.Seat).Select(player => player.HandCount).Order().SequenceEqual([1, 2, 3]) &&
                (ownerIsAi || view.Players.Where(player => player.Seat != 0 && player.HandCount is 1 or 3).All(player => player.Hp == 3) &&
                    view.Players.Single(player => player.Seat != 0 && player.HandCount == 2).Hp == 4),
                "The real peer hands must be 1/2/3; the direct fixture's low/high HP3 precede the bridge HP4 in the unchanged independent target order.");
        else
            Require(view.Players[0].Hand.Count(card => card.Kind == CardKind.Crossbow) >= 4 &&
                view.Players[0].Hand.Count(card => card.Kind == CardKind.BaguaFormation) >= 4 &&
                view.Players.Single(player => Capacity(player, EquipmentSlot.Armor) == 0) is { Hp: 3, IsEquipmentAreaAbolished: false } limited &&
                Capacity(limited, EquipmentSlot.Weapon) == 1 && view.Players.Where(player => player.Seat != 0 && player.Seat != limited.Seat)
                    .All(player => player.Hp == 4 && Capacity(player, EquipmentSlot.Armor) == 1),
                "The real mixed equipment deal and original startup Armor0 instruction must leave one HP3 restricted target and legal HP4 armor recipients.");
        return game;
    }

    private static string Skill(Scenario scenario) => scenario == Scenario.HandPair ? Dimeng : Zhijian;
    private static string Activation(Scenario scenario) => scenario == Scenario.HandPair ? "swap-first" : "place-equipment";
    private static UseProgramSkillCommand Command(GameEngine game, Scenario scenario, IReadOnlyList<int> cards, IReadOnlyList<int> targets) =>
        new(0, Skill(scenario), Activation(scenario), cards, targets, game.Revision, game.CreateSnapshot(0).PendingDecision!.PromptId);
    private static int Capacity(PlayerSnapshot player, EquipmentSlot slot) => player.IsEquipmentAreaAbolished ? 0 :
        player.EquipmentSlotCapacities?.GetValueOrDefault(slot, 1) ?? 1;
    private static int[] OwnHand(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat].Hand.Select(card => card.Id).Order().ToArray();
    private static string Boundary(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.Revision, Commands = CommandJson.Serialize(game.AcceptedCommands), Movements = game.CardMovements,
        Events = game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).ToArray(),
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))).ToArray()
    });
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Boundary(game) == Boundary(cold), "The recorded AI policy must cold restore every actual command, movement, debt and safe player view.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Require(result.Accepted, result.Error?.Message ?? "A native correlated-AI fixture command was rejected.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        internal static string Owner(Scenario scenario) => $"fixture:correlated-ai-owner-{scenario.ToString().ToLowerInvariant()}";
        internal static string Peer(Scenario scenario, int number) => $"fixture:correlated-ai-peer-{scenario.ToString().ToLowerInvariant()}-{number}";
        internal static string Mode(Scenario scenario) => $"identity:classic-correlated-ai-{scenario.ToString().ToLowerInvariant()}";
        private static string Initial(int count) => $"fixture:correlated-ai-initial-{count}";
        private const string Partial = "fixture:correlated-ai-armor-zero";
        public PackageManifest Manifest { get; } = new("fixture-correlated-ai", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var rules = new List<object>();
            var presentations = new Dictionary<string, object>();
            foreach (var count in new[] { 1, 2, 3, 10 })
            {
                rules.Add(new { id = Initial(count), revision = 1, modifiers = new[] { new { id = "real-holder-hand", query = "initialHandSize",
                    operation = "add", value = count, priority = 0, condition = new { kind = "always" } } } });
                presentations.Add(Initial(count), new { name = $"原生起手{count}张", description = "仅持有者实际取得指定数量的实体牌" });
            }
            rules.Add(new { id = Partial, revision = 1, triggers = new[] { new { id = "real-armor-zero", window = "gameStarting", subject = "owner",
                optional = false, effects = new[] { new { op = "alterEquipmentSlots", target = "owner", equipmentSlots = new[] { "armor" }, amount = 0 } } } } });
            presentations.Add(Partial, new { name = "实际部分装备槽", description = "原生开局仅令护甲槽容量为零" });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = rules }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = presentations }));
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                    { Program = program, ProgramPresentation = catalog.Presentations[id] });
            foreach (var scenario in Enum.GetValues<Scenario>())
            {
                builder.AddGeneral(new(Owner(scenario), "原生关联技能AI", "supporter", Skill(scenario), "wu", 4,
                    [Initial(scenario == Scenario.HandPair ? 1 : 10)], GeneralGender.Male));
                var peers = Enumerable.Range(1, 3).Select(number => Peer(scenario, number)).ToArray();
                for (var index = 0; index < peers.Length; index++)
                {
                    var skill = scenario == Scenario.HandPair ? Initial(index + 1) : index == 0 ? Partial : "standard:none";
                    var hp = scenario == Scenario.HandPair ? index == 1 ? 4 : 3 : index == 0 ? 3 : 4;
                    builder.AddGeneral(new(peers[index], "真实关联候选", "supporter", skill, "shu", hp, Gender: GeneralGender.Male));
                }
                var deck = $"fixture:correlated-ai-deck-{scenario.ToString().ToLowerInvariant()}";
                builder.AddDeck(new(deck, "固定十二张实体牌", 0, 0, [])
                { PhysicalCards = Enumerable.Range(0, 12).Select(index => new ContentDeckPhysicalCard(
                    scenario == Scenario.HandPair ? "standard:dodge" : index < 6 ? "standard:crossbow" : "standard:bagua",
                    new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[index % 4], index + 1)).ToArray() });
                builder.AddMode(new(Mode(scenario), "原生关联AI", 4, 4, new Dictionary<string, int>
                    { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, deck,
                    GeneralCandidateCount: 4, GeneralPoolIds: [Owner(scenario), .. peers]));
            }
        }
    }
}
