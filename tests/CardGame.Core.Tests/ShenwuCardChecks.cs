using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ShenwuCardChecks
{
    private const string Driver = "fixture:shenwu-card-driver", Quiet = "fixture:shenwu-card-quiet";
    private const string Owner = "fixture:shenwu-card-owner", Mode = "fixture:shenwu-card-mode";
    private const string Bridge = "shenwu:transplant-wood-resolution";

    public static void OrdinaryTricksRetainNativeRequestsPaymentsAndColdReturns()
    {
        foreach (var god in new[] { false, true })
        {
            var (g, registry) = Start(new Fixture(CardKind.SowBeans, god: god));
            var before = V(g, 0).HandCount;
            var id = PlayCard(g, CardKind.SowBeans, []);
            Play(g);
            var expected = god ? 5 : Math.Max(0, 5 - (before - 1));
            Need(V(g, 0).HandCount == before - 1 + expected &&
                Facts<CardUseFinishedEvent>(g).Count(e => e.CardId == id && e.CardKind == CardKind.SowBeans) == 1 &&
                g.CreateCardZoneDiagnostics().Single(card => card.CardId == id).Location == CardLocation.DiscardPile,
                "A god draws X directly; an ordinary general fills to X after paying the actual original trick.");
            Cold(g, registry);
        }
        CheckCounterspell();
        CheckTransplant(useSlash: false, cardless: false, actorHand: 1);
        CheckTransplant(useSlash: false, cardless: false, actorHand: 3);
        CheckTransplant(useSlash: true, cardless: false, actorHand: 3);
        CheckTransplant(useSlash: true, cardless: true, actorHand: 1);
        CheckTransplant(useSlash: true, cardless: false, actorHand: 3, parentMaterials: "multi");
        CheckTransplant(useSlash: true, cardless: false, actorHand: 3, parentMaterials: "zero");
    }

    private static void CheckCounterspell()
    {
        var (g, registry) = Start(new Fixture(CardKind.SowBeans, god: true, counter: true));
        var before = V(g, 0).HandCount;
        var id = PlayCard(g, CardKind.SowBeans, []);
        Until(g, () => P(g)?.Kind == DecisionKind.Nullification);
        Cold(g, registry);
        var counter = P(g)!.Choices.First(choice => choice.Parameters.GetValueOrDefault("response") == "nullification").Cards.Single();
        Answer(g, choice => choice.Cards.SequenceEqual([counter]) && choice.Parameters.GetValueOrDefault("response") == "nullification");
        Play(g);
        Need(V(g, 0).HandCount == before - 2 &&
            Facts<NullificationResolvedEvent>(g).Any(e => e.EffectCardKind == CardKind.SowBeans && e.EffectNullified) &&
            new[] { id, counter }.All(card => g.CreateCardZoneDiagnostics().Single(c => c.CardId == card).Location == CardLocation.DiscardPile),
            "Sow beans resolves through a paid native counterspell and does not draw after it is nullified.");
        Cold(g, registry);
    }

    private static void CheckTransplant(bool useSlash, bool cardless, int actorHand, string? parentMaterials = null)
    {
        var (g, registry) = Start(new Fixture(CardKind.TransplantWood, slash: useSlash && !cardless,
            cardless: cardless, actorHand: actorHand, parentMaterials: parentMaterials));
        var target = useSlash ? cardless ? 2 : new[] { 2, 3 }
            .First(seat => V(g, seat).Hand.Any(card => card.Kind == CardKind.Slash)) : 1;
        var before = V(g, target).HandCount;
        if (parentMaterials == "zero")
            for (var tier = 0; tier < 2; tier++)
            {
                Accept(g, new UseProgramSkillCommand(0, Driver, "upgrade", [], [], g.Revision, P(g)!.PromptId));
                Play(g);
            }
        var sourceHand = V(g, 0).HandCount;
        var sourceCards = V(g, 0).Hand.Select(card => card.Id).ToArray();
        int id;
        if (parentMaterials == "multi")
        {
            Accept(g, new UseProgramSkillCommand(0, Driver, "all-transplant", sourceCards, [], g.Revision, P(g)!.PromptId));
            Until(g, () => P(g)?.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "use-all-hand-as-ordinary-trick") == true);
            Cold(g, registry);
            Answer(g, choice => choice.Parameters.GetValueOrDefault("card-kind") == CardKind.TransplantWood.ToString() && choice.Targets.SequenceEqual([target]));
            id = sourceCards.Min();
        }
        else if (parentMaterials == "zero")
        {
            var action = g.GetHumanLegalActions().First(action => action.CardId == 0 &&
                action.PlayedCardKind == CardKind.TransplantWood && action.TargetSeats.SequenceEqual([target]) &&
                action.ConversionSource?.BindingId == "zero-transplant");
            Accept(g, new PlayCardCommand(0, 0, action.TargetSeats, g.Revision, P(g)!.PromptId,
                action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
            id = 0;
        }
        else id = PlayCard(g, CardKind.TransplantWood, [target]);
        Until(g, () => g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.ShenwuTransplantReturn is not null));
        var program = g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.ShenwuTransplantReturn is not null);
        var parent = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.CardId == id);
        Need(program.SkillId == Bridge && program.SelectedTargetSeats.SequenceEqual([target]) &&
            program.ShenwuTransplantReturn is { } receipt && receipt.CardUseFrameId == parent.Id &&
            receipt.ActionId == parent.Action!.ActionId && parent.ShenwuTransplant!.ProgramFrameId == program.Id &&
            parent.Action.PhysicalCards.Count == (parentMaterials == "zero" ? 0 : parentMaterials == "multi" ? sourceCards.Length : 1) &&
            parent.Action.PhysicalCards.All(cost => g.CreateCardZoneDiagnostics().Single(card => card.CardId == cost.CardId).Location == CardLocation.Processing),
            "The real trick suspends on an exact native request program while retaining its committed entity.");
        Need(!g.GetHumanLegalActions().Any(action => action.ProgramSkillId == Bridge),
            "The internal card-effect activation cannot be voluntarily used.");
        Cold(g, registry);
        var observedChoice = false;
        for (var i = 0; i < 140 && !IsPlay(g); i++)
        {
            var p = P(g);
            if (p?.SkillPrompt?.SkillId == Bridge)
            {
                observedChoice = true;
                Need(p.PlayerSeat == target && g.CreateSnapshot(0).PendingDecision is null &&
                    g.CreateSnapshot(target).PendingDecision?.IsPrivate == true,
                    "Only the actual requested actor can see its target, Slash or gift payment choice.");
                Cold(g, registry);
            }
            if (g.ResolutionStack.OfType<CardUseFrame>().Any(child => child.Id != parent.Id &&
                child.CardKind == CardKind.Slash && child.Action?.ParentActionId == parent.Action!.ActionId))
            { Cold(g, registry); }
            Step(g);
        }
        var result = Facts<ShenwuTransplantResolvedEvent>(g).Single(e => e.CardUseFrameId == parent.Id);
        Need(IsPlay(g) && observedChoice && result.Completed && result.UsedSlash == useSlash &&
            (!useSlash || Facts<CardUseFinishedEvent>(g).Any(e => e.CardKind == CardKind.Slash && e.ResolutionId != parent.Id)) &&
            Facts<CardUseFinishedEvent>(g).Count(e => e.ResolutionId == parent.Id) == 1 &&
            parent.Action!.PhysicalCards.All(cost => g.CardMovements.Count(move => move.CardId == cost.CardId && move.To == CardLocation.DiscardPile) == 1) &&
            (parentMaterials != "zero" || g.CardMovements.All(move => move.CardId != 0)) &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SkillId == Bridge) &&
            !(V(g, 0).Skills ?? []).Any(skill => skill.Id == Bridge),
            "A requested Slash or complete real gift returns once before the original trick finishes and removes its temporary grant.");
        if (!useSlash)
            Need(V(g, target).HandCount == before - Math.Min(2, before) &&
                V(g, 0).HandCount == sourceHand - 1 + Math.Min(2, before),
                "Declining gives two actual cards, or every actual card when fewer than two remain.");
        if (cardless)
            Need(Facts<RoundOnceCardlessProgramRequestReturnedEvent>(g).Any() &&
                Facts<CardUseFinishedEvent>(g).Any(e => e.CardId == 0 && e.CardKind == CardKind.Slash) &&
                g.CardMovements.All(move => move.CardId != 0),
                "The existing zero-material request pays its genuine round quota and completes its typed native return without synthetic card movement.");
        Cold(g, registry);
    }

    public static void ExclusiveEquipmentUsesOriginalEntitiesAndNativeEffects()
    {
        CheckRobe(); CheckQimen(); CheckZither(); CheckDustless(); CheckShura(); CheckCrown();
        CheckInitialEquipment();
        CheckRecipientScopedChains();
    }
    private static void CheckRobe()
    {
        var (g, registry) = Start(new Fixture(CardKind.NationalStyleRobe));
        Use(g, "equip", 1); Play(g);
        Need(V(g, 1).Equipment.Single().Kind == CardKind.NationalStyleRobe &&
            g.GetHumanLegalActions().All(a => a.Kind != LegalActionKind.TransplantWood || a.TargetSeat != 1) &&
            g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.TransplantWood && a.TargetSeat == 2),
            "The real equipped robe removes only its protected wearer from other actors' ordinary-trick targets.");
        Cold(g, registry);
    }
    private static void CheckQimen()
    {
        var (g, registry) = Start(new Fixture(CardKind.QimenEightTrigrams));
        Use(g, "equip", 1); Play(g); var hp = V(g, 1).Hp;
        PlayCard(g, CardKind.Slash, [1]); Play(g);
        Need(V(g, 1).Hp == hp && Facts<ArmorEffectAppliedEvent>(g).Any(e => e.ArmorCard == CardKind.QimenEightTrigrams && e.TargetSeat == 1),
            "Qimen prevents the actual incoming native Slash before its Dodge prompt.");
        Cold(g, registry);
    }
    private static void CheckZither()
    {
        var (g, registry) = Start(new Fixture(CardKind.CrimsonSoulZither));
        Use(g, "equip", 0); Play(g); Use(g, "damage", 1); Play(g);
        Need(Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Nature == DamageNature.Fire),
            "The actual zither changes even attributed native program damage to fire.");
        Cold(g, registry);
    }
    private static void CheckDustless()
    {
        var (g, registry) = Start(new Fixture(CardKind.DustlessGoldenSpear));
        var enemyToPeer = g.GetCombatDistance(3, 2); var friendToPeer = g.GetCombatDistance(0, 2);
        Use(g, "equip", 1); Play(g);
        Need(g.GetCombatDistance(3, 2) == enemyToPeer + 1 && g.GetCombatDistance(0, 2) == friendToPeer &&
            g.GetCombatDistance(3, 1) == g.GetSeatDistance(3, 1) + 1,
            "Dustless keeps its own defensive-horse distance and shields only other allies against enemies.");
        Cold(g, registry);
    }
    private static void CheckShura()
    {
        var (g, registry) = Start(new Fixture(CardKind.ShuraInfernoHalberd));
        Use(g, "equip", 0); Play(g);
        Need(g.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash && action.TargetSeats.Count >= 3),
            "The actual halberd exposes additional legal in-range Slash targets through the existing target-count mechanism.");
        var hp = V(g, 1).Hp; PlayCard(g, CardKind.Slash, [1]); Play(g);
        Need(Facts<DamageAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 2) &&
            Facts<RecoveryAppliedEvent>(g).Any(e => e.SourceSeat == 0 && e.TargetSeat == 1 && e.Amount == 1) && V(g, 1).Hp == hp - 1,
            "The native Slash adds one damage before the cap and then the damaged target recovers one through real HP observers.");
        Cold(g, registry);
    }
    private static void CheckCrown()
    {
        var (g, registry) = Start(new Fixture(CardKind.DelusionCrown));
        Use(g, "equip", 0); Play(g);
        Need((V(g, 0).Skills ?? []).Any(skill => skill.Id == "shenwu:delusion-crown") && V(g, 0).HandLimit == V(g, 0).Hp - 1,
            "The original equipped crown owns its actual modifier grant and reduces the real hand limit.");
        var draws = g.CardMovements.Count(move => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0));
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Need(g.CardMovements.Count(move => move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(0)) == draws + 2,
            "Crown adds two actual original card entities during a later normal draw phase.");
        Cold(g, registry);
    }

    private static void CheckInitialEquipment()
    {
        var registry = BaseRegistry();
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 6, HumanSeat = 0,
            HumanRole = null, HumanTeamId = "team:allies", ModeId = "activity:shenwu-2017", UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 6 }, registry);
        Accept(game, new StartGameCommand()); Until(game, () => P(game)?.Kind == DecisionKind.SelectGeneral);
        var god = P(game)!.Choices.First(choice => choice.Parameters.GetValueOrDefault("shenwu-shared-god") == "true");
        Cold(game, registry);
        Accept(game, new SelectGeneralCommand(0, god.Parameters["candidate-general-id"], game.Revision, P(game)!.PromptId));
        Until(game, () => Facts<ShenwuEquipmentInitializedEvent>(game).Any());
        var selected = V(game, 0);
        Need(selected.Equipment.Count != 0 && game.CreateCardZoneDiagnostics().All(c =>
            c.CardKind is not (CardKind.DrawTwo or CardKind.BorrowedSword or CardKind.Indulgence or CardKind.SupplyShortage)) &&
            Facts<ShenwuEquipmentInitializedEvent>(game).Count() == 1,
            "Equipment selection follows the real shared-god draft; the original military deck is changed before the first deal exactly once.");
        Cold(game, registry);
    }

    private static void CheckRecipientScopedChains()
    {
        foreach (var physical in new[] { false, true })
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new ChainFixture());
            Need(registry.Skills.Values.All(skill => skill.Program?.Triggers.SelectMany(trigger => trigger.Effects)
                    .All(effect => effect.Op != SkillProgramEffectOp.DiscardOwnedCardToAdjustCurrentDamage) != false),
                "The minimal chain regression must not borrow signed-damage capability from an unrelated general package.");
            var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
                HumanRole = null, HumanTeamId = "team:allies", ModeId = ChainFixture.Mode,
                UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
            Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
            Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g);
            var first = g.CreateSnapshot(0).Players.Single(p => p.GeneralId == ChainFixture.Plain).Seat;
            var recipients = g.CreateSnapshot(0).Players.Where(p => p.GeneralId is ChainFixture.NitaiOne or ChainFixture.NitaiTwo)
                .Select(p => p.Seat).Order().ToArray();
            foreach (var seat in recipients.Prepend(first)) { Use(g, "chain", seat); Play(g); }
            Need(recipients.Length == 2 && recipients.Prepend(first).All(seat => V(g, seat).IsChained),
                "Three actual living native recipients are chained before the damage is declared.");
            Cold(g, registry);
            var start = g.Events.Count;
            if (physical)
            {
                Need(V(g, 0).Hand.Any(card => card.Kind == CardKind.FireSlash),
                    "The 2v2 initiative adjustment leaves one real FireSlash available for the native chain regression.");
                PlayCard(g, CardKind.FireSlash, [first]);
            }
            else Use(g, "fire", first);
            var checkpoints = 0;
            for (var i = 0; i < 160 && !IsPlay(g); i++)
            {
                if (P(g) is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger, SkillPrompt.SkillId: Driver } decision &&
                    decision.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "skip"))
                {
                    checkpoints++;
                    Cold(g, registry);
                    Answer(g, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
                    continue;
                }
                Step(g);
            }
            var damage = g.Events.Skip(start).Select(e => e.Payload).OfType<DamageAppliedEvent>().ToArray();
            Need(IsPlay(g) && checkpoints == 3 && damage.Length == 3 && damage[0] is { Amount: 1, Nature: DamageNature.Fire } &&
                damage[0].TargetSeat == first && damage.Skip(1).All(e => e.Amount == 2 && e.Nature == DamageNature.Fire) &&
                damage.Skip(1).Select(e => e.TargetSeat).Order().SequenceEqual(recipients) &&
                Facts<RecipientScopedDamageBaseEvent>(g).Any(e => e.FirstTargetSeat == first && e.Amount == 1) &&
                Facts<RecipientScopedDamageAdvancedEvent>(g).Count(e => recipients.Contains(e.TargetSeat) && e.Amount == 1) == 2,
                "Each native chained Nitai recipient adds one to the frozen first amount, rather than inheriting the previous recipient's increment. " +
                JsonSerializer.Serialize(new { physical, first, recipients, damage }));
            Cold(g, registry);
        }
    }

    // Load the actual shipped Nitai definition, without registering the full
    // general catalog whose unrelated signed-damage dependency would mask this
    // regression. A plain Team mode also exercises the explicit re-finalizer.
    private sealed class ChainFixture : IGameContentPackage
    {
        internal const string Mode = "fixture:shenwu-chain-mode", Plain = "fixture:shenwu-chain-plain";
        internal const string NitaiOne = "fixture:shenwu-chain-nitai-1", NitaiTwo = "fixture:shenwu-chain-nitai-2";
        public PackageManifest Manifest { get; } = new("shenwu-chain-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardShenwuActivityPackage).Assembly;
            const string prefix = "CardGame.Content.Standard.SkillPrograms.shenwu-npc";
            using var rulesStream = assembly.GetManifestResourceStream(prefix + ".rules.json")!;
            using var presentationStream = assembly.GetManifestResourceStream(prefix + ".presentation.json")!;
            using var ruleReader = new StreamReader(rulesStream);
            using var presentationReader = new StreamReader(presentationStream);
            var source = SkillProgramCatalog.Load(ruleReader.ReadToEnd(), presentationReader.ReadToEnd());
            b.AddSkill(new("shenwu:nitai", source.Presentations["shenwu:nitai"].Name,
                source.Presentations["shenwu:nitai"].Description)
            { Program = source.Programs["shenwu:nitai"], ProgramPresentation = source.Presentations["shenwu:nitai"], Tags = SkillTag.Locked });
            var fixtureRules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"chain-target-range","query":"attackRange","operation":"add","value":3,"priority":0}],"activations":[
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"selectedTarget","chained":true}]},
                {"id":"fire","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1,"nature":"fire"}]}],
                "triggers":[{"id":"observe-native-recipient","window":"afterDamageApplied","subject":"damageSource","damageOccurrence":"perDamage","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
              {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            var fixture = SkillProgramCatalog.Load(fixtureRules, JsonSerializer.Serialize(new
            { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = new[] { Driver, Quiet }
                .ToDictionary(id => id, id => new { name = id, description = "Minimal native chain regression." }) }));
            foreach (var (id, program) in fixture.Programs) b.AddSkill(new(id, id, "Native chain fixture.")
            { Program = program, ProgramPresentation = fixture.Presentations[id] });
            b.AddGeneral(new(Owner, "Chain source", "", Driver, "wei", 8));
            b.AddGeneral(new(Plain, "Plain first recipient", "", Quiet, "wei", 8));
            b.AddGeneral(new(NitaiOne, "First Nitai recipient", "", Quiet, "qun", 8, ["shenwu:nitai"]));
            b.AddGeneral(new(NitaiTwo, "Second Nitai recipient", "", Quiet, "qun", 8, ["shenwu:nitai"]));
            b.AddDeck(new("fixture:shenwu-chain-deck", "Original native elemental cards", 2, 0,
                [new ContentDeckCardCount("standard:fire_slash", 100)]));
            b.AddMode(new(Mode, "Minimal native chain", 4, 4, new Dictionary<string, int>(), "fixture:shenwu-chain-deck", 4,
                [Owner, Plain, NitaiOne, NitaiTwo], ContentModeKind.Team,
                new Dictionary<string, int> { ["team:allies"] = 2, ["team:other"] = 2 }));
        }
    }

    private static ContentRegistry BaseRegistry(params IGameContentPackage[] extra) => ContentRegistry.Build(
        [new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(),
            new StandardClassicGeneralPackage(), new StandardShenwuActivityPackage(), .. extra]);
    private static (GameEngine, ContentRegistry) Start(Fixture fixture)
    {
        var registry = BaseRegistry(fixture);
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = null, HumanTeamId = "team:allies", ModeId = Mode, UseInteractiveSetup = true,
            AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(CardKind kind, bool god = false, bool counter = false, bool slash = false,
        bool cardless = false, int actorHand = 1, string? parentMaterials = null) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shenwu-card-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var large = kind == CardKind.SowBeans && !counter ? 0 : 24;
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"modifiers":[{"id":"large-hand","query":"initialHandSize","operation":"add","value":{{{large + 3}}},"priority":0,"condition":{"kind":"always"}}],"activations":[
                {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
              {"id":"{{{Quiet}}}","revision":1,{{{(cardless ? "\"viewAs\":[{\"id\":\"round-slash\",\"inputKinds\":[],\"inputSuits\":[],\"inputCount\":0,\"sourceZones\":[],\"outputKind\":\"slash\",\"forPlay\":true,\"forResponse\":false,\"roundOnceCardlessBasic\":{\"usageGroup\":\"fixture\",\"usesPerRound\":1}}]," : "")}}}
                "triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """;
            if (parentMaterials is not null)
            {
                var document = System.Text.Json.Nodes.JsonNode.Parse(rules)!;
                var actor = document["skills"]![0]!;
                actor["activations"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
                {"id":"all-transplant","minCards":1,"maxCards":64,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                 "effects":[{"op":"useAllHandCardsAsOrdinaryTrick","target":"owner","viewAsId":"all-transplant","outputKind":"transplantWood"}]}
                """));
                actor["activations"]!.AsArray().Add(System.Text.Json.Nodes.JsonNode.Parse("""
                {"id":"upgrade","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,
                 "effects":[{"op":"upgradeConversionTier","target":"owner","stateId":"fixture-transplant-tier"}]}
                """));
                actor["viewAs"] = System.Text.Json.Nodes.JsonNode.Parse("""
                [{"id":"zero-transplant","inputKinds":[],"inputSuits":[],"inputCount":0,"sourceZones":[],"outputKind":"transplantWood","forPlay":true,"forResponse":false,
                  "useOnly":true,"allowSameKind":true,"singleCardTrickUse":true,"conversionStateId":"fixture-transplant-tier","minimumTier":2,"maximumTier":2,
                  "tieredRoundConversion":{"stateId":"fixture-transplant-tier","usageId":"transplant"}}]
                """);
                rules = document.ToJsonString();
            }
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet }.ToDictionary(id => id, id => new { name = id, description = "Actual Shenwu card fixture." }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Actual card fixture.")
            { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "Actual card player", "", Driver, god ? "god" : "wei", 5));
            var peers = new[] { "fixture:shenwu-card-peer-1", "fixture:shenwu-card-peer-2" };
            foreach (var peer in peers) b.AddGeneral(new(peer, "Actual quiet ally", "", Quiet, "wei", 8));
            const string boss = "fixture:shenwu-card-boss";
            b.AddGeneral(new(boss, "Actual quiet enemy", "", Quiet, "qun", 8));
            var equipment = kind switch
            {
                CardKind.NationalStyleRobe => "national-style-robe", CardKind.QimenEightTrigrams => "qimen-eight-trigrams",
                CardKind.CrimsonSoulZither => "crimson-soul-zither", CardKind.DustlessGoldenSpear => "dustless-golden-spear",
                CardKind.ShuraInfernoHalberd => "shura-inferno-halberd", CardKind.DelusionCrown => "delusion-crown", _ => null
            };
            var cards = Enumerable.Range(0, 180).Select(i => new ContentDeckPhysicalCard(
                kind == CardKind.SowBeans ? counter && i % 3 == 0 ? "standard:nullification" : "shenwu:sow-beans" :
                equipment is not null ? i % 4 == 0 ? $"shenwu:equipment-{equipment}" : i % 5 == 0 ? "shenwu:transplant-wood" : "standard:slash" :
                slash && i % 3 != 0 ? "standard:slash" : "shenwu:transplant-wood", Suit.Heart, i % 13 + 1)).ToArray();
            b.AddDeck(new("fixture:shenwu-card-deck", "Original card deck", counter ? 0 : actorHand, 0, []) { PhysicalCards = cards });
            b.AddMode(new(Mode, "Actual native cards", 4, 4, new Dictionary<string, int>(), "fixture:shenwu-card-deck", 3,
                [Owner, .. peers, boss], ContentModeKind.Team, new Dictionary<string, int> { ["team:allies"] = 3, ["team:boss"] = 1 })
            { BossActivity = new([Owner, .. peers], [new("cards", [boss], 0)], 1, []) });
        }
    }
    private static int PlayCard(GameEngine g, CardKind kind, IReadOnlyList<int> targets)
    {
        var action = g.GetHumanLegalActions().First(a => a.CardId is { } id &&
            (a.PlayedCardKind ?? V(g, 0).Hand.SingleOrDefault(c => c.Id == id)?.Kind) == kind &&
            a.TargetSeats.SequenceEqual(targets) && a.Kind != LegalActionKind.Recast && a.ConversionSource is null);
        Accept(g, new PlayCardCommand(0, action.CardId!.Value, targets, g.Revision, P(g)!.PromptId,
            action.PlayedCardKind, action.TargetCardId)); return action.CardId.Value;
    }
    private static void Use(GameEngine g, string id, int target) => Accept(g,
        new UseProgramSkillCommand(0, Driver, id, [], [target], g.Revision, P(g)!.PromptId));
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? P(GameEngine g) => g.CreateSnapshot(0).Players.Select(p => p.Seat)
        .Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static bool IsPlay(GameEngine g) => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 };
    private static void Play(GameEngine g) => Until(g, () => IsPlay(g));
    private static IEnumerable<T> Facts<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision)); }
    private static void Step(GameEngine g)
    {
        var p = P(g);
        if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
            Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
        else if (p is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge } &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "take-damage"))
            Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        else Accept(g, new AdvanceOneStepCommand(g.Revision));
    }
    private static void Until(GameEngine g, Func<bool> ready)
    { for (var i = 0; i < 160; i++) { if (ready()) return; Step(g); }
        throw new InvalidOperationException("Shenwu fixture did not reach its boundary: " + State(g)); }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new
    { Views = g.CreateSnapshot(0).Players.Select(p => SnapshotJson.Serialize(g.CreateSnapshot(p.Seat))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements,
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())) });
    private static void Cold(GameEngine g, ContentRegistry registry)
    { var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Cold replay preserves exact native requests, private choices, original entities and typed returns."); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Rejected actual Shenwu card command."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
