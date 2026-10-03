using System.Reflection;
using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using static BoundaryLiDianChecks;

internal static class BoundaryXuShuChecks
{
    private const string Zhuhai = "boundary:zhuhai-current";
    private const string Qianxin = "boundary:qianxin-current";
    private const string Jianyan = "boundary:jianyan-current";

    public static void RunCurrentRulesAndAwakening()
    {
        var (g, registry) = Start();
        Driver(g, "damage", [1]); Settle(g);
        Require(!g.CreateSnapshot(0).Players[0].Skills!.Any(skill => skill.ContentId == Jianyan),
            "An unwounded source does not awaken after actual damage.");
        Driver(g, "hurt"); Settle(g);
        var before = g.State.Players[0].MaxHp;
        Driver(g, "damage", [1]); Settle(g);
        Require(g.State.Players[0].MaxHp == before - 1 &&
            g.CreateSnapshot(0).Players[0].Skills!.Any(skill => skill.ContentId == Jianyan),
            "Wounded actual source damage reduces maximum HP once and grants the derived Jianyan skill.");
        Driver(g, "damage", [1]); Settle(g);
        Require(g.State.Players[0].MaxHp == before - 1, "The game-scoped awakening cannot repeat.");
        ReplayFourViews(g, registry);

        var (silent, sr) = Start(damagingTurns: false);
        var turn = silent.State.TurnNumber;
        EndPlay(silent);
        Reach(silent, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0 && silent.State.TurnNumber > turn);
        Require(!silent.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.SkillId == Zhuhai),
            "Other-turn HP loss alone does not count as dealt damage or offer Zhuhai.");
        ReplayFourViews(silent, sr);

        var old = new SkillProgramTriggerFacts(0, 3, true);
        Require(!JsonSerializer.Serialize(old).Contains("TurnOwnerDamageDealtThisTurn", StringComparison.Ordinal) &&
            !JsonSerializer.Serialize(new ProgramSkillFrame(1, 0, "old", "old", "hash", 1, [], []))
                .Contains("FixedTargetSlash", StringComparison.Ordinal),
            "Historical catalogs omit the new nullable facts and draft fields.");
        foreach (var members in new[]
        {
            "\"activations\":[" + Activation("bad", "[{\"op\":\"useOwnerSlashAgainstTurnOwner\",\"target\":\"owner\",\"ignoreDistance\":true}]") + "]",
            "\"triggers\":[{\"id\":\"bad\",\"window\":\"preparationStarting\",\"subject\":\"owner\",\"optional\":false,\"condition\":{\"kind\":\"compare\",\"left\":{\"kind\":\"turnOwnerDamageDealtThisTurn\"},\"operator\":\"greaterThan\",\"right\":{\"kind\":\"integerConstant\",\"value\":0}},\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]}]",
            "\"activations\":[" + Activation("bad", "[{\"op\":\"declareDeckCriterionAndGiveMatchingCard\",\"target\":\"owner\"},{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1}]") + "]"
        })
        {
            var rejected = false;
            try { Load("fixture:xu-bad", members); } catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "The new owning operations reject an unsupported entry point or composition.");
        }
    }

    public static void RunFixedTargetPhysicalSlash()
    {
        var (g, registry) = Start();
        ReachFarZhuhai(g); Activate(g);
        Require(g.GetCombatDistance(0, 2) > 1, "The selected other-turn owner is outside the bare attack range.");
        var prompt = P(g)!;
        var use = prompt.Choices.First(choice => choice.Parameters.GetValueOrDefault("request-option") == "use");
        Require(use.Cards.Count == 1 && use.Targets.SequenceEqual([2]),
            "Zhuhai publishes a real physical Slash against its frozen distant ending-turn owner.");
        var card = use.Cards.Single();
        ReplayFourViews(g, registry);
        var revision = g.Revision;
        var rejected = g.Submit(new AnswerPromptCommand(0, prompt.PromptId, new ChoiceId("fixed-slash.foreign"), revision));
        Require(!rejected.Accepted && g.Revision == revision, "A foreign fixed-target input is rejected before payment.");
        Answer(g, choice => choice.Id == use.Id);
        DrainFixedSlash(g);
        Require(g.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
            g.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Any(e => e.CardId == card && e.SourceSeat == 0 &&
                g.Events.Select(item => item.Payload).OfType<TargetsConfirmedEvent>().Any(targets => targets.ResolutionId == e.ResolutionId && targets.TargetSeats.SequenceEqual([2]))),
            "The distant attack declares one real Use and pays its hand entity once.");
        ReplayFourViews(g, registry);

        var (faction, fr) = Start(faction: true); ReachFarZhuhai(faction); Activate(faction);
        Answer(faction, choice => choice.Parameters.GetValueOrDefault("request-option") == "faction");
        DrainFixedSlash(faction);
        Require(faction.Events.Select(e => e.Payload).OfType<FactionSlashRequestedEvent>().Any(e => e.OwnerSeat == 0 && e.TargetSeat == 2) &&
            faction.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Zhuhai && e.Completed),
            "The fixed distant owner request runs existing faction providers and returns to its exact parent finitely.");
        ReplayFourViews(faction, fr);

        var (lost, lr) = Start(); ReachFarZhuhai(lost); Activate(lost); ReplayFourViews(lost, lr);
        var grant = Players(lost)[0].SkillGrants.Grants.Single(item => item.SkillId == Zhuhai);
        Players(lost)[0].SkillGrants.RemoveGrant(grant.GrantId);
        Answer(lost, choice => choice.Parameters.GetValueOrDefault("request-option") == "use");
        Require(!lost.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Zhuhai) &&
            !lost.Events.Select(e => e.Payload).OfType<CardUseDeclaredEvent>().Any(e => e.SourceSeat == 0),
            "Losing the exact skill instance before issuance cancels without paying a card.");

        var (native, nr) = Start(native: true);
        for (var step = 0; step < 100 && !native.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.SkillId == Zhuhai); step++)
            Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(native.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e => e.SkillId == Zhuhai),
            "A native AI owner actually starts the optional fixed-target Slash operation. " +
            $"seat={native.State.CurrentSeat}, turn={native.State.TurnNumber}, phase={native.State.Phase}, pending={P(native)?.Kind}/{P(native)?.PlayerSeat}/{P(native)?.SkillPrompt?.SkillId}; " +
            "damage=" + string.Join(';', native.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Select(e => $"{e.SourceSeat}>{e.TargetSeat}:{e.Amount}")) +
            "; bindings=" + string.Join(';', native.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Select(e => $"{e.SkillId}@{e.OwnerSeat}")));
        for (var step = 0; step < 40 && native.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Zhuhai); step++)
            Accept(native, new AdvanceOneStepCommand(native.Revision));
        Require(native.Events.Select(e => e.Payload).OfType<ProgramBindingResolvedEvent>().Any(e => e.SkillId == Zhuhai && e.Completed),
            "Native AI completes the fixed-target physical Use finitely.");
        ReplayFourViews(native, nr);
    }

    public static void RunDeclaredDeckCriterion()
    {
        var (g, registry) = Start(initialJianyan: true, mixedDeck: true);
        var top = Zones(g).CardsAt(CardLocation.DrawPile).Reverse().ToArray();
        var criterion = top[0].Suit is Suit.Heart or Suit.Diamond ? "black" : "red";
        var expected = Array.FindIndex(top, card => (card.Suit is Suit.Heart or Suit.Diamond) == (criterion == "red"));
        Require(expected > 0, "The bounded mixed recipe stages both a failed reveal and a later match.");
        UseJianyan(g);
        var declaration = P(g)!;
        Require(declaration.Choices.Select(c => c.Parameters["criterion"]).SequenceEqual(["basic", "trick", "equipment", "red", "black"]),
            "Exactly three categories and two colors are declared without reading the private deck in the choice.");
        ReplayFourViews(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("criterion") == criterion);
        Reach(g, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "declared-deck-recipient"));
        var match = top[expected].Id;
        Require(Zones(g).GetLocation(match) == CardLocation.Processing &&
            top.Take(expected).All(card => Zones(g).GetLocation(card.Id) == CardLocation.DiscardPile),
            "The first matching top entity is held after every preceding unmatched entity enters discard.");
        Require(P(g)!.Choices.Any(c => c.Targets.SequenceEqual([0])) &&
            P(g)!.Choices.All(c => Players(g)[c.Targets.Single()].Gender == GeneralGender.Male),
            "Jianyan includes the male owner and excludes current female recipients.");
        for (var viewer = 0; viewer < 4; viewer++)
            Require(g.CreateSnapshot(viewer).PublicRevealedCards.Any(card => card.Id == match),
                "Every viewer can see the actual matching revealed card.");
        Require(g.Events.Select(e => e.Payload).OfType<ProgramDeckCriterionRevealedEvent>().Select(e => e.CardId)
            .SequenceEqual(top.Take(expected + 1).Select(c => c.Id)), "The event ledger preserves top-first reveal order.");
        ReplayFourViews(g, registry);
        var stale = g.Submit(new AnswerPromptCommand(0, declaration.PromptId, declaration.Choices[0].Id, g.Revision));
        Require(!stale.Accepted, "A stale criterion prompt cannot restart the committed search.");
        Answer(g, c => c.Targets.SequenceEqual([0])); Settle(g);
        Require(Zones(g).GetLocation(match) == CardLocation.Hand(0) &&
            g.CardMovements.Count(m => m.CardId == match && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0)) == 1 &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Jianyan),
            "The matching card transfers once and consumes one actual play-phase quota.");
        ReplayFourViews(g, registry);

        var (absent, ar) = Start(initialJianyan: true); var budget = Pile(absent).Length;
        UseJianyan(absent); Answer(absent, c => c.Parameters.GetValueOrDefault("criterion") == "red"); Settle(absent);
        Require(absent.Events.Select(e => e.Payload).OfType<ProgramDeckCriterionRevealedEvent>().Count() == budget &&
            !absent.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Jianyan) &&
            !absent.CardMovements.Any(move => move.Reason == CardMoveReasons.Reshuffle),
            "A missing criterion scans the initial pile once, finishes, and does not cycle unmatched cards through reshuffles.");
        ReplayFourViews(absent, ar);

        var (child, chr) = Start(initialJianyan: true, gainChild: true); UseJianyan(child);
        Answer(child, c => c.Parameters.GetValueOrDefault("criterion") == "basic");
        Reach(child, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "declared-deck-recipient"));
        var childHeld = child.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jianyan).DeckCriterion!.MatchedCardId!.Value;
        var hp = child.State.Players[0].Hp; Answer(child, c => c.Targets.SequenceEqual([0]));
        Reach(child, p => p.SkillPrompt?.SkillId == "fixture:xu-gain");
        Require(child.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == Jianyan) is
            { DeckCriterion.Stage: "gift", PendingMovementContinuation: not null },
            "The committed gift keeps its owning draft while the actual gained-card child is pending.");
        ReplayFourViews(child, chr); Activate(child); Settle(child);
        Require(child.State.Players[0].Hp == hp - 1 &&
            child.CardMovements.Count(m => m.CardId == childHeld && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0)) == 1,
            "The actual gain child resolves once and cannot repeat the completed matching-card gift.");
        ReplayFourViews(child, chr);

        var (cancel, cr) = Start(initialJianyan: true); UseJianyan(cancel);
        Answer(cancel, c => c.Parameters.GetValueOrDefault("criterion") == "basic");
        Reach(cancel, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "declared-deck-recipient"));
        var held = cancel.ResolutionStack.OfType<ProgramSkillFrame>().Single(frame => frame.SkillId == Jianyan).DeckCriterion!.MatchedCardId!.Value;
        ReplayFourViews(cancel, cr);
        var source = Players(cancel)[0].SkillGrants.Grants.Single(item => item.SkillId == Jianyan);
        Players(cancel)[0].SkillGrants.RemoveGrant(source.GrantId);
        Answer(cancel, c => c.Targets.SequenceEqual([0])); Settle(cancel);
        Require(Zones(cancel).GetLocation(held) == CardLocation.DiscardPile &&
            !cancel.CardMovements.Any(m => m.CardId == held && m.From == CardLocation.Processing && m.To == CardLocation.Hand(0)),
            "Exact source loss at the recipient boundary cleans the held card without a gift.");
    }

    private static void ReachFarZhuhai(GameEngine g)
    {
        EndPlay(g);
        for (var step = 0; step < 160; step++)
        {
            if (P(g) is { Kind: DecisionKind.ProgramTrigger } p && p.SkillPrompt?.SkillId == Zhuhai)
            {
                if (g.ResolutionStack.OfType<TurnEndingBoundaryFrame>().Last().OwnerSeat == 2) return;
                Skip(g); continue;
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Xu Shu fixture did not reach the distant ending-turn source.");
    }
    private static void DrainFixedSlash(GameEngine g)
    {
        for (var step = 0; step < 80 && g.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Zhuhai); step++)
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        Require(!g.ResolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.SkillId == Zhuhai), "Fixed-target Slash returns to its original frame once.");
    }
    private static void EndPlay(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Driver(GameEngine g, string id, int[]? targets = null) =>
        Accept(g, new UseProgramSkillCommand(0, "fixture:xu-driver", id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void UseJianyan(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Jianyan, "declare-and-give", [], [], g.Revision, P(g)!.PromptId));
    private static CardZoneStore Zones(GameEngine g) => (CardZoneStore)typeof(GameEngine).GetField("_cardZones", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
    private static CharacterState[] Players(GameEngine g) => ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!).ToArray();
    private static SkillProgram Load(string id, string members) => SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"" + id + "\",\"revision\":1," + members + "}]}",
        "{\"schemaVersion\":3,\"skills\":{\"" + id + "\":{\"name\":\"fixture\",\"description\":\"fixture\"}}}").Programs[id];
    private static string Activation(string id, string effects, int targets = 0) => "{\"id\":\"" + id + "\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"effects\":" + effects + "}";
    private static (GameEngine, ContentRegistry) Start(bool damagingTurns = true, bool initialJianyan = false, bool mixedDeck = false, bool native = false,
        bool faction = false, bool gainChild = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(damagingTurns, initialJianyan, mixedDeck, native, faction, gainChild));
        var game = GameEngine.CreateStandard(new GameOptions
        { Seed = 31, PlayerCount = 4, HumanSeat = native ? -1 : 0, HumanRole = native ? null : Role.Lord,
            ModeId = "identity:xu", UseInteractiveSetup = !native, AdvanceAfterHumanCommands = false,
            UseInteractiveDiscard = false, MaxTurns = 8 }, registry);
        Accept(game, new StartGameCommand());
        if (!native)
        {
            Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
            Accept(game, new SelectGeneralCommand(0, "fixture:xu", game.Revision, P(game)!.PromptId)); Settle(game);
        }
        return (game, registry);
    }

    private sealed class Fixture(bool damagingTurns, bool initialJianyan, bool mixedDeck, bool native, bool faction, bool gainChild) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-xu", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryXuShuContent")!
                .GetMethod("Register", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [builder]);
            builder.AddSkill(new("fixture:xu-driver", "fixture", "fixture")
            { Program = Load("fixture:xu-driver", "\"activations\":[" + Activation("hurt", "[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]") + "," +
                Activation("damage", "[{\"op\":\"damage\",\"target\":\"selectedTarget\",\"amount\":1}]", 1) + "]") });
            builder.AddSkill(new("fixture:xu-quiet", "quiet", "quiet")
            { Program = Load("fixture:xu-quiet", "\"triggers\":[{\"id\":\"quiet\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"grantTurnCardActionProhibition\",\"target\":\"owner\",\"cardKinds\":[\"slash\",\"fireSlash\",\"thunderSlash\"],\"actionTypes\":[\"use\"]}]}]") });
            builder.AddSkill(new("fixture:xu-source", "source", "source")
            { Program = Load("fixture:xu-source", "\"triggers\":[{\"id\":\"source\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"" + (damagingTurns ? "damage" : "loseHp") + "\",\"target\":\"owner\",\"amount\":1}]}]") });
            if (faction) builder.AddSkill(new("fixture:xu-faction", "激将", "fixture")
            { Tags = SkillTag.Lord, Program = Load("fixture:xu-faction", "\"cardPolicies\":[{\"id\":\"allied\",\"kind\":\"factionResponseRequest\",\"requiredCardKinds\":[\"slash\"],\"factionId\":\"shu\",\"ownerRole\":\"lord\"}]") });
            if (gainChild) builder.AddSkill(new("fixture:xu-gain", "实际获得子窗", "fixture")
            { Program = Load("fixture:xu-gain", "\"triggers\":[{\"id\":\"gift-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"program.declared-deck.give\"],\"movementOccurrence\":\"perBatch\",\"optional\":true,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]") });
            builder.AddGeneral(new("fixture:xu", "徐庶", "boundary_xu_shu", Zhuhai, "shu", 4,
                new[] { Qianxin }.Concat(initialJianyan ? [Jianyan] : Array.Empty<string>()).Concat(native ? ["fixture:xu-source", "fixture:xu-quiet"] : ["fixture:xu-driver"])
                    .Concat(faction ? ["fixture:xu-faction"] : Array.Empty<string>()).Concat(gainChild ? ["fixture:xu-gain"] : Array.Empty<string>()).ToArray(), GeneralGender.Male));
            for (var i = 1; i < 4; i++)
                builder.AddGeneral(new($"fixture:xu-{i}", "其他" + i, "supporter", native ? Zhuhai : "standard:none", "shu", 8,
                    ["fixture:xu-quiet", "fixture:xu-source"], i == 1 ? GeneralGender.Female : GeneralGender.Male));
            builder.AddDeck(new("fixture:xu-deck", "固定", 4, 1, [])
            { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:slash", mixedDeck && i % 2 == 0 ? Suit.Heart : Suit.Spade, i % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:xu", "固定", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:xu-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:xu", "fixture:xu-1", "fixture:xu-2", "fixture:xu-3"]));
        }
    }
}
