using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HulaoCardChecks
{
    private const string Driver = "fixture:hulao-card-driver", Quiet = "fixture:hulao-card-quiet";
    private const string Owner = "fixture:hulao-card-owner", Mode = "fixture:hulao-card-mode";

    public static void AllianceFeastRetainsNativePerTargetNullificationAndColdChoice()
    {
        foreach (var counter in new[] { false, true })
        {
            var (game, registry) = Start(new Fixture(CardKind.AllianceFeast));
            var before = game.CreateSnapshot(0).Players.Select(p => (p.Hp, p.HandCount)).ToArray();
            var cardId = PlayCard(game, CardKind.AllianceFeast, []);
            Until(game, () => P(game)?.Kind == DecisionKind.Nullification);
            var use = game.ResolutionStack.OfType<CardUseFrame>().Single(use => use.CardKind == CardKind.AllianceFeast);
            Need(use.CardId == cardId && use.PhysicalCardIds!.SequenceEqual([cardId]) &&
                use.TargetSeats.SequenceEqual([0, 1, 2, 3]) && use.Action is { ActorSeat: 0 } &&
                game.CreateCardZoneDiagnostics().Single(c => c.CardId == cardId).Location == CardLocation.Processing,
                "Alliance feast owns a real committed global trick, physical cost and source-first full target list.");
            Cold(game, registry);
            var paidCounter = false;
            var choices = 0;
            for (var i = 0; i < 160 && !IsPlay(game); i++)
            {
                var p = P(game);
                if (p is { Kind: DecisionKind.Nullification, PlayerSeat: 0 })
                {
                    var window = game.ResolutionStack.OfType<NullificationWindowFrame>().Single();
                    Need(window.ParentFrameId == use.Id && window.TargetSeats.Count == 1 &&
                        window.TargetSeats.Single() == game.ResolutionStack.OfType<CardUseFrame>().Single().TargetSeats[
                            game.ResolutionStack.OfType<CardUseFrame>().Single().TargetIndex],
                        "Each living recipient has its own native nullification window and unchanged original use.");
                    var pay = counter && !paidCounter && window.TargetSeats.Single() == 1;
                    Answer(game, c => c.Parameters.GetValueOrDefault("response") == (pay ? "nullification" : "pass"));
                    paidCounter |= pay;
                }
                else if (p is { IncomingCard: CardKind.AllianceFeast, Kind: DecisionKind.ProgramTrigger })
                {
                    choices++;
                    Need(p.PlayerSeat != 0 && p.Choices.Count == 2 &&
                        game.CreateSnapshot(p.PlayerSeat).PendingDecision?.Choices.Count == 2 &&
                        game.CreateSnapshot(0).PendingDecision is null,
                        "Only the actual recipient can see the private recovery/draw choice.");
                    Cold(game, registry);
                    var state = State(game);
                    Need(!game.Submit(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, game.Revision)).Accepted &&
                        State(game) == state, "A foreign human cannot answer an AI recipient's exact benefit choice.");
                    Accept(game, new AdvanceOneStepCommand(game.Revision));
                }
                else Accept(game, new AdvanceOneStepCommand(game.Revision));
            }
            Need(IsPlay(game) && paidCounter == counter && choices == (counter ? 2 : 3),
                "A counterspell removes only its current recipient; later native targets still choose and resolve.");
            var benefits = Events<HulaoAllianceFeastBenefitEvent>(game).Where(e => e.CardUseFrameId == use.Id).ToArray();
            Need(benefits.Single(e => e.TargetSeat == 0) is { Benefit: "draw", Amount: 4 } &&
                game.CreateSnapshot(0).Players[0].HandCount == before[0].HandCount - 1 + 4 - (counter ? 1 : 0) &&
                benefits.Any(e => e.TargetSeat == 3 && e.Benefit == "draw") &&
                benefits.Any(e => e.TargetSeat == 2 && e.Benefit == "recover") &&
                game.CreateSnapshot(0).Players[2].Hp == before[2].Hp + 1 &&
                benefits.All(e => e.TargetSeat != 1 || !counter),
                "The source draws the real alive count, wounded AI chooses recovery, full-HP AI chooses a real card.");
            Need(Events<CardUseFinishedEvent>(game).Count(e => e.ResolutionId == use.Id) == 1 &&
                game.CardMovements.Count(m => m.CardId == cardId && m.To == CardLocation.DiscardPile) == 1 &&
                game.ResolutionStack.All(frame => frame.Id != use.Id),
                "The global physical card moves to discard and completes exactly once after every target returns.");
            Cold(game, registry);
        }
    }

    public static void EquipmentGrantsRespectNativeEffectsAndSetupEntities()
    {
        CheckInitialEntities();
        CheckRobe();
        CheckBelt(Suit.Heart);
        CheckBelt(Suit.Spade);
        CheckHalberd("draw");
        CheckHalberd("discard");
        CheckCrown();
    }

    private static void CheckInitialEntities()
    {
        var (g, registry) = Start(new Fixture(CardKind.AllianceFeast, initialEquipment: true));
        var zones = g.CreateCardZoneDiagnostics();
        Need(zones.All(c => c.CardKind is not (CardKind.FiveGrains or CardKind.PeachGarden)) &&
            zones.Any(c => c.CardKind == CardKind.AllianceFeast), "The real military physical recipe replaces both global tricks.");
        var added = zones.Where(c => c.CardKind is CardKind.WushuangHalberd or CardKind.PurpleGoldCrown or
            CardKind.RedCottonRobe or CardKind.LionBelt).ToArray();
        Need(added.Select(c => c.CardKind).Distinct().Count() is >= 1 and <= 4 &&
            added.Any(c => c.Location == CardLocation.Equipment(3)) &&
            g.CreateSnapshot(0).Players[3].Equipment.GroupBy(c => EquipmentCatalog.Get(c.Kind).Slot).All(group => group.Count() == 1) &&
            Events<HulaoInitialEquipmentPlacedEvent>(g).Count() == 1,
            "Random added kinds are real original entities; Jingjia respects one equipment per slot and runs once.");
        Need(added.Where(c => c.Location.Zone != CardZoneKind.Equipment).All(c => c.Location.Zone is
            CardZoneKind.DrawPile or CardZoneKind.Hand), "Unplaced selected equipment stays in the native deck/initial deal.");
        Cold(g, registry);
    }

    private static void CheckRobe()
    {
        var (g, registry) = Start(new Fixture(CardKind.RedCottonRobe));
        Use(g, "equip", 1); Play(g);
        var armorId = V(g, 1).Equipment.Single().Id;
        var hp = V(g, 1).Hp;
        Use(g, "fire", 1); Play(g);
        Need(V(g, 1).Hp == hp && Events<ProgramDamagePreventedEvent>(g).Any(e => e.SkillId == "hulao:red-cotton-robe"),
            "The actual equipped red robe prevents a native elemental program attack.");
        Use(g, "damage", 1); Play(g);
        Need(V(g, 1).Hp == hp - 1, "Red robe does not prevent normal damage.");
        Use(g, "disarm", 1);
        Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
        Answer(g, c => c.Cards.Contains(armorId)); Play(g);
        var lost = V(g, 1).Hp;
        Use(g, "fire", 1); Play(g);
        Need(V(g, 1).Hp == lost - 1 && V(g, 1).Equipment.Count == 0,
            "Losing the original equipment removes its grant and immediately restores elemental damage.");
        Cold(g, registry);
    }

    private static void CheckBelt(Suit suit)
    {
        var (g, registry) = Start(new Fixture(CardKind.LionBelt, suit));
        Use(g, "equip", 1); Play(g);
        var hp = V(g, 1).Hp;
        PlayCard(g, CardKind.Slash, [1]); Play(g);
        Need(Events<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == "hulao:lion-belt") &&
            V(g, 1).Hp == hp - (suit == Suit.Heart ? 0 : 1) &&
            g.CardMovements.Any(m => m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile &&
                m.Reason.Value.Contains("MoveBoundCards", StringComparison.Ordinal)),
            "The sole target's belt judges a real entity; only an actual heart makes the native Slash ineffective.");
        Cold(g, registry);
    }

    private static void CheckHalberd(string benefit)
    {
        var (g, registry) = Start(new Fixture(CardKind.WushuangHalberd));
        Use(g, "equip", 0); Play(g);
        var hand = V(g, 0).HandCount;
        var targetHand = V(g, 1).HandCount;
        PlayCard(g, CardKind.Slash, [1]);
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "hulao:wushuang-halberd");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == benefit) == true);
        Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == benefit);
        if (benefit == "discard")
        {
            Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card") == true);
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
        }
        Play(g);
        Need(V(g, 0).HandCount == hand - 1 + (benefit == "draw" ? 1 : 0) &&
            V(g, 1).HandCount == targetHand - (benefit == "discard" ? 1 : 0) &&
            Events<ProgramBindingResolvedEvent>(g).Count(e => e.SkillId == "hulao:wushuang-halberd" && e.Completed) == 1,
            "After a real native Slash hits its actual target, halberd executes exactly one selected native reward.");
        Cold(g, registry);
    }

    private static void CheckCrown()
    {
        var (g, registry) = Start(new Fixture(CardKind.PurpleGoldCrown));
        Use(g, "equip", 0); Play(g);
        var hp = V(g, 1).Hp;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => P(g)?.SkillPrompt?.SkillId == "hulao:purple-gold-crown");
        Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(g, () => P(g)?.Choices.Any(c => c.Targets.SequenceEqual([1])) == true);
        Answer(g, c => c.Targets.SequenceEqual([1])); Play(g);
        Need(V(g, 1).Hp == hp - 1 && Events<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1),
            "The real crown grants a preparation opportunity that deals native damage to another selected character.");
        Cold(g, registry);
    }

    private static (GameEngine, ContentRegistry) Start(Fixture fixture)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardHulaoActivityPackage(), fixture);
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = "team:allies", ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g);
        return (g, registry);
    }

    private sealed class Fixture(CardKind kind, Suit suit = Suit.Heart, bool initialEquipment = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("hulao-card-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"large-player-hand","query":"initialHandSize","operation":"add","value":24,"priority":0,"condition":{"kind":"always"}}],"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"disarm","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"selectedTarget"},"zones":["equipment"],"count":1,"destination":"discardPile"}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet }.ToDictionary(id => id, id => new { name = id, description = "Actual Hulao card behavior fixture." }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Actual Hulao fixture.")
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "Actual Hulao card player", "", Driver, "shu", 12));
            var peers = new[] { "fixture:hulao-card-peer-1", "fixture:hulao-card-peer-2" };
            foreach (var id in peers) b.AddGeneral(new(id, "Actual quiet allied player", "", Quiet, "shu", 12)
                { InitialHp = kind == CardKind.AllianceFeast ? 10 : null });
            const string boss = "fixture:hulao-card-boss";
            b.AddGeneral(new(boss, "Actual quiet Boss", "", Quiet, "god", 12));
            var cards = kind == CardKind.AllianceFeast
                ? Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(i % 4 == 0 ? "standard:nullification" :
                    initialEquipment ? i % 5 == 0 ? "standard:peach_garden" : "standard:five_grains" : "hulao:alliance-feast", suit, i % 13 + 1)).ToList()
                : Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(i % 4 == 0 ? kind switch
                {
                    CardKind.WushuangHalberd => "hulao:equipment-wushuang-halberd", CardKind.PurpleGoldCrown => "hulao:equipment-purple-gold-crown",
                    CardKind.RedCottonRobe => "hulao:equipment-red-cotton-robe", CardKind.LionBelt => "hulao:equipment-lion-belt", _ => throw new InvalidOperationException()
                } : "standard:slash", suit, i % 13 + 1)).ToList();
            if (initialEquipment)
            {
                cards.Add(new("classic:fangtian-halberd", Suit.Diamond, 12));
                cards.Add(new("standard:bagua", Suit.Spade, 2));
            }
            b.AddDeck(new("fixture:hulao-card-deck", "Small original physical Hulao card deck",
                kind == CardKind.AllianceFeast ? 0 : 1, 0, []) { PhysicalCards = cards });
            var mode = new ContentModeDefinition(Mode, "Actual Hulao card fixture", 4, 4, new Dictionary<string, int>(),
                "fixture:hulao-card-deck", 3, [Owner, .. peers, boss], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
            { BossActivity = new([Owner, .. peers], initialEquipment
                ? [new("cards", [boss], 0), new("cards-second", [boss], 0)] : [new("cards", [boss], 0)], 1, []) };
            if (initialEquipment) mode = mode with { HulaoActivity = new(TransformationHp: 4, ShuffleLimit: 2) };
            b.AddMode(mode);
        }
    }

    private static int PlayCard(GameEngine g, CardKind kind, IReadOnlyList<int> targets)
    {
        var action = g.GetHumanLegalActions().First(a => a.CardId is { } id &&
            (a.PlayedCardKind ?? V(g, 0).Hand.SingleOrDefault(c => c.Id == id)?.Kind) == kind && a.TargetSeats.SequenceEqual(targets) &&
            a.Kind != LegalActionKind.Recast && a.ConversionSource is null);
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, targets, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId));
        return action.CardId.Value;
    }
    private static void Use(GameEngine g, string id, int target) => Accept(g,
        new UseProgramSkillCommand(0, Driver, id, [], [target], g.Revision, P(g)!.PromptId));
    private static bool IsPlay(GameEngine g) => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static void Play(GameEngine g) => Until(g, () => IsPlay(g));
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> Events<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Answer(GameEngine g, Func<PromptChoice, bool> select)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, g.Revision)); }
    private static void Until(GameEngine g, Func<bool> ready)
    {
        for (var i = 0; i < 160; i++)
        {
            if (ready()) return;
            var p = P(g);
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Hulao fixture did not reach its native boundary: " + State(g));
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements,
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
    private static void Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold replay preserves the original private choice, native card use, grants, judgments and physical movements."); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Hulao card command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
