using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheBossChecks
{
    private const string Wushuang = "shanhe:shengui-wushuang", Shenqu = "shanhe:shengui-shenqu";
    private const string Owner = "fixture:shanhe-boss-owner", Driver = "fixture:shanhe-boss-driver";
    private const string Quiet = "fixture:shanhe-boss-quiet", Watch = "fixture:shanhe-boss-draw-watch";
    private const string Mode = "identity:classic-shanhe-boss-rules";

    public static void BattleSlashResponsesCountCurrentConvertedVirtualUsesAcrossTurnsAndColdDuel()
    {
        var (g, registry) = Start(false);
        Play(g);
        var converted = g.GetHumanLegalActions().First(a => a.ConversionSource?.SkillId == "classic:wusheng" &&
            a.TargetSeats.SequenceEqual([1]) && a.PlayedCardKind == CardKind.Slash);
        Accept(g, new PlayCardCommand(0, converted.CardId!.Value, converted.TargetSeats, g.Revision,
            P(g)!.PromptId, converted.PlayedCardKind) { ConversionSource = converted.ConversionSource });
        CheckSlashRequirement(g, 1);
        Require(E<CardUseDeclaredEvent>(g).Single(e => e.SourceSeat == 0 && IsSlash(e.CardKind)).CardId != 0,
            "The first converted physical Slash is a genuine use and requires one Dodge, including the current declaration.");
        g = Cold(g, registry); Play(g);

        Use(g, "slash", [1]); CheckSlashRequirement(g, 2); Play(g);
        // Reverse the actual Duel's initiator before the count reaches its cap.
        // Lvbu responds once, but the initiator still owes two, not three, Slashes.
        Use(g, "refill", [1]); Play(g);
        Use(g, "duel", [1, 0]);
        Until(g, () => P(g) is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        var before = E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 0 && IsSlash(e.CardKind));
        Answer(g, c => c.Cards.Count == 1 && c.Parameters.GetValueOrDefault("response") != "pass");
        Until(g, () => P(g) is { Kind: DecisionKind.RespondSlash, PlayerSeat: 1 });
        var incomingDuel = g.ResolutionStack.OfType<ProgramSkillFrame>()
            .Single(f => f.Continuations.Duel is { Active: true }).Id;
        g = Cold(g, registry);
        Accept(g, new AdvanceOneStepCommand(g.Revision));
        Until(g, () => E<RequiredResponseProgressEvent>(g).Any(e => e.ResolutionId == incomingDuel &&
            e.ResponderSeat == 1 && e.ResponseCount == 2 && e.RequiredResponseCount == 2));
        Require(E<CardActionAcceptedEvent>(g).Any(e => e.Action.Type == CardActionType.Response &&
                e.Action.ActorSeat == 0 && e.Action.EffectiveKind == CardKind.Slash) &&
            E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 0 && IsSlash(e.CardKind)) == before &&
            E<DuelResponseEvent>(g).Count(e => e.ResolutionId == incomingDuel && e.ResponderSeat == 1 && e.UsedSlash) == 2 &&
            E<DuelResponseEvent>(g).Where(e => e.ResolutionId == incomingDuel && e.ResponderSeat == 1 && e.UsedSlash)
                .Select(e => e.SlashCardId).Distinct().Count() == 2,
            "A converted Slash merely played in response does not add a use; incoming Duel applies the current two-use count.");
        g = Cold(g, registry);
        Until(g, () => P(g) is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        Play(g);

        var originalTurn = g.State.TurnNumber;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId)); Play(g);
        Require(g.State.TurnNumber > originalTurn, "The next attacks occur after other actual turns, not an inserted phase.");
        foreach (var (count, target) in new[] { (3, 2), (4, 3), (5, 1), (6, 2) })
        {
            Use(g, "refill", [target]); Play(g);
            Use(g, "slash", [target]); CheckSlashRequirement(g, Math.Min(count, 5));
            if (count == 6) g = Cold(g, registry);
            Play(g);
        }
        Require(E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 0 && IsSlash(e.CardKind)) == 6 &&
            E<CardUseDeclaredEvent>(g).Count(e => e.SourceSeat == 0 && IsSlash(e.CardKind) && e.CardId == 0) == 5,
            "Five actual virtual uses compose with the original conversion across turns, with no parallel mutable counter.");

        Use(g, "refill", [1]); Play(g);
        Use(g, "duel", [0, 1]);
        Until(g, () => P(g) is { Kind: DecisionKind.RespondSlash, PlayerSeat: 1 });
        var outgoingDuel = g.ResolutionStack.OfType<ProgramSkillFrame>()
            .Single(f => f.Continuations.Duel is { Active: true }).Id;
        g = Cold(g, registry);
        Accept(g, new AdvanceOneStepCommand(g.Revision));
        Until(g, () => E<RequiredResponseProgressEvent>(g).Any(e => e.ResolutionId == outgoingDuel &&
            e.ResponderSeat == 1 && e.ResponseCount == 5 && e.RequiredResponseCount == 5));
        Require(E<DuelResponseEvent>(g).Count(e => e.ResolutionId == outgoingDuel && e.ResponderSeat == 1 && e.UsedSlash) == 5 &&
            E<DuelResponseEvent>(g).Where(e => e.ResolutionId == outgoingDuel && e.ResponderSeat == 1 && e.UsedSlash)
                .Select(e => e.SlashCardId).Distinct().Count() == 5,
            "An outgoing Duel requires five consecutive Slashes from the opponent.");
        g = Cold(g, registry);
        Until(g, () => P(g) is { Kind: DecisionKind.RespondSlash, PlayerSeat: 0 });
        Answer(g, c => c.Parameters.GetValueOrDefault("response") == "take-damage");
        Play(g);
    }

    public static void ShenquDrawsMaximumRatherThanRefillingFreezesNativeRequestAndUsesRealPeach()
    {
        var (g, registry) = Start(true);
        Until(g, () => Optional(g, Shenqu));
        var opening = V(g, 0);
        Require(opening.HandCount == opening.MaxHp && opening.MaxHp > 2,
            $"The real preparation qualifies at equality, with more than the old fixed two-card draw (hand={opening.HandCount}, hp={opening.Hp}, max={opening.MaxHp}, phase={g.State.Phase}).");
        var estimate = ProgramCompositionAi.Estimate(registry.GetSkill(Shenqu).Program!.Triggers
            .Single(t => t.Id == "draw-maximum-hp-at-every-preparation").Effects,
            new PlayerSkillContext(0, opening.Hp, opening.MaxHp, opening.HandCount, TurnPhase.Draw));
        Require(estimate.Score > 0 && double.IsFinite(estimate.Score),
            "The NPC's public-state evaluation values maximum-HP draw instead of pricing its expression as zero cards.");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
        var request = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single().Context;
        Require(request.RecipientSeat == 0 && request.RequestedCount == opening.MaxHp &&
            V(g, 0).MaxHp == opening.MaxHp + 2 && V(g, 0).HandCount == opening.HandCount,
            "The native request freezes the full original maximum before its real observer changes maximum HP.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(g);
        Require(ShenquDraws(g) == opening.MaxHp && V(g, 0).HandCount == opening.HandCount + opening.MaxHp + 2,
            "Shenqu adds X physical cards and then permits normal Draw2; it neither refills to X nor recomputes the paused request.");

        var before = V(g, 0);
        Use(g, "damage", []);
        Until(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "hulao-peach") == true);
        Require(V(g, 0).Hp == before.Hp - 1, "The actual damage applies before the optional physical Peach.");
        g = Cold(g, registry);
        var peach = P(g)!.Choices.First(c => c.Cards.Count == 1).Cards.Single();
        Answer(g, c => c.Cards.SequenceEqual([peach])); Play(g);
        Require(V(g, 0).Hp == before.Hp && V(g, 0).HandCount == before.HandCount - 1 &&
            E<CardUseDeclaredEvent>(g).Any(e => e.SourceSeat == 0 && e.CardKind == CardKind.Peach && e.CardId == peach) &&
            E<HulaoPeachIssuedEvent>(g).Count() == 1 && E<HulaoPeachReturnedEvent>(g).Count() == 1 &&
            g.CardMovements.Any(m => m.CardId == peach && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) &&
            g.CardMovements.Any(m => m.CardId == peach && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile),
            "The independent Shanhe skill consumes and uses an actual Peach exactly once, retaining its original native child chain.");

        var total = ShenquDraws(g);
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        Until(g, () => g.State.CurrentSeat != 0 && Optional(g, Shenqu));
        var maximum = V(g, 0).MaxHp;
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(g, () => ShenquDraws(g) == total + maximum);
        Require(maximum == opening.MaxHp + 2 && g.State.CurrentSeat != 0,
            "Another participant's preparation offers Shenqu and uses the then-current maximum, preserving the foreign actual turn.");
        _ = Cold(g, registry);
    }

    public static void FixedCompanionOpeningReplacesRandomBaseFreezesFacesAndReplaysBeforeNativeFudao()
    {
        var input = new List<ShanheStartingCard>
        {
            new(CardKind.Slash, Suit.Heart, 5), new(CardKind.Dodge, Suit.Heart, 13),
            new(CardKind.ArrowBarrage, Suit.Heart, 1), new(CardKind.Alcohol, Suit.Spade, 3)
        };
        var expected = input.ToArray();
        var ally = new ShanheCombatantDefinition(StandardShanheBossPackage.FenghuangId, InitialHandBonus: 2)
        { StartingHand = input };
        input.Clear();
        Require(ally.StartingHand!.SequenceEqual(expected), "A combatant immediately freezes its caller-owned starting-hand collection.");
        var rejectedMutation = false;
        try { ((IList<ShanheStartingCard>)ally.StartingHand)[0] = new(CardKind.Slash); }
        catch (NotSupportedException) { rejectedMutation = true; }
        Require(rejectedMutation, "An exposed combatant starting hand rejects mutation.");
        var definition = OpeningDefinition(ally);
        var different = OpeningDefinition(ally with { StartingHand = [new(CardKind.Slash, Suit.Diamond, 9), .. expected.Skip(1)] });
        Require(ShanheCampaignContent.GetContentHash(definition) != ShanheCampaignContent.GetContentHash(different) &&
            !JsonSerializer.Serialize(new ShanheCombatantDefinition("classic:ma-chao")).Contains("StartingHand", StringComparison.Ordinal),
            "Physical recipe faces enter the campaign fingerprint while an absent optional recipe preserves legacy JSON shape.");
        var session = ShanheCampaignSession.Create(definition, new(17, "classic:cao-cao"), StandardShanheCampaignCatalog.CreateBattleRegistry);
        Require(session.Submit(new MoveShanheNodeCommand("gate", 0)).Accepted, "A real campaign command opens the companion encounter.");
        var checkpoint = ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint()));
        var restored = ShanheCampaignSession.Restore(checkpoint, definition, StandardShanheCampaignCatalog.CreateBattleRegistry);
        Require(ShanheBattleSetup.ValueEquals(session.PendingBattleSetup, restored.PendingBattleSetup),
            "Campaign JSON replay retains the frozen per-member opening recipe.");
        RejectRecipe(() => ShanheCampaignSession.Restore(checkpoint, different, StandardShanheCampaignCatalog.CreateBattleRegistry));

        var setup = session.PendingBattleSetup!;
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
        var g = StandardShanheCampaignCatalog.CreateBattle(setup);
        var generated = g.CardMovements.Where(m => m.From == CardLocation.OutsideGame && m.To == CardLocation.Hand(1)).ToArray();
        var hand = V(g, 1).Hand;
        Require(V(g, 1).HandCount == expected.Length + 2 && generated.Length == expected.Length &&
            generated.Select(m => hand.Single(c => c.Id == m.CardId)).Select(c => new ShanheStartingCard(c.Kind, c.Suit, c.Rank)).SequenceEqual(expected) &&
            g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1)) == 2,
            "The exact four fixed faces replace the normal random base, while the two-card initial bonus still draws two genuine deck entities.");
        Require(g.CreateSnapshot(0).Players.Single(p => p.Seat == 1).Hand.Count == 0,
            "A fixed companion hand remains private in the player's filtered view.");
        g = Cold(g, registry);
        Accept(g, new StartGameCommand());
        Until(g, () => E<FudaoHandRecordedEvent>(g).Any(e => e.Source.OwnerSeat == 1));
        Require(g.CardMovements.Count(m => m.To == CardLocation.Hand(1) && m.Reason.Value == "skill-program.ding-shang-wan.draw") == 3 &&
            g.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == g.CreateCardZoneDiagnostics().Count,
            "The original Fudao GameStarting program draws three only after actual opening placement, with every generated entity in one real zone.");
        _ = Cold(g, registry);

        var empty = setup with { EnemyRoster = [new("classic:ma-chao") { StartingHand = [] }] };
        var zero = StandardShanheCampaignCatalog.CreateBattle(empty);
        Require(V(zero, 2).HandCount == 0, "An explicitly empty fixed hand means zero base cards rather than falling back to the ordinary random hand.");
        RejectRecipe(() => ShanheCampaignContent.FreezeAndValidate(OpeningDefinition(ally with
        { StartingHand = [new(CardKind.Slash, Suit.None, 1)] })));
    }

    private static ShanheCampaignDefinition OpeningDefinition(ShanheCombatantDefinition ally) => new(
        "fixture:shanhe-fixed-opening", "Actual fixed companion opening", ["classic:cao-cao"], ["gate"],
        [new("gate", "Fixed opening encounter", ShanheNodeKind.Battle, [],
            [new("encounter", "Fixed companion", [new("classic:ma-chao")], [ally])])], []);

    private static void RejectRecipe(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("An invalid or mismatched fixed opening recipe was accepted.");
    }

    private static bool IsSlash(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash;
    private static int ShenquDraws(GameEngine g) => g.CardMovements.Count(m => m.From == CardLocation.DrawPile &&
        m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program." + Shenqu + ".Draw");
    private static bool Optional(GameEngine g, string skill) => P(g)?.SkillPrompt?.SkillId == skill &&
        P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void CheckSlashRequirement(GameEngine g, int count)
    {
        Until(g, () => P(g)?.Kind == DecisionKind.RespondDodge);
        var attack = g.ResolutionStack.OfType<CardUseFrame>().Last(f => IsSlash(f.CardKind)).CardAttack;
        Require(attack is { Active: true } && attack.RequiredDodgeResponses == count,
            $"The actual native Slash response requires {count} Dodges from the battle's used-Slash count.");
    }
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players.Single(p => p.Seat == seat);
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, g.CreateSnapshot(0).Players.Count)
        .Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Use(GameEngine g, string activation, int[] targets) => Accept(g,
        new UseProgramSkillCommand(0, Driver, activation, [], targets, g.Revision, P(g)!.PromptId));
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g)!;
        Require(p.PlayerSeat == 0, "Only the real human may be answered; NPCs must use their native AI command.");
        Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(predicate).Id, g.Revision));
    }
    private static void Play(GameEngine g) => Until(g, () => P(g) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Until(GameEngine g, Func<bool> ready)
    {
        for (var step = 0; step < 200; step++)
        {
            if (ready()) return;
            Require(g.State.Winner == Winner.None, "Shanhe boss fixture ended before its required boundary.");
            var p = P(g);
            if (p is { PlayerSeat: 0 } && p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
                Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Shanhe boss fixture missed its bounded native boundary: " + JsonSerializer.Serialize(P(g)));
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, g.CreateSnapshot(0).Players.Count).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(restored.CreateSnapshot(s))) &&
            JsonSerializer.Serialize(g.CardMovements) == JsonSerializer.Serialize(restored.CardMovements) &&
            g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).SequenceEqual(
                restored.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType()))),
            "Cold command replay preserves the exact responses, native draw request, private cards and physical movements.");
        return restored;
    }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Shanhe boss command was rejected."); }
    private static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException(reason); }

    private static (GameEngine, ContentRegistry) Start(bool shenqu)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new StandardShanheBossPackage(), new Fixture(shenqu));
        var g = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(g, new StartGameCommand()); Until(g, () => P(g)?.Kind == DecisionKind.SelectGeneral);
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId));
        return (g, registry);
    }

    private sealed class Fixture(bool shenqu) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("shanhe-boss-rules-fixture", new Version(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"modifiers":[{"id":"slash-room","query":"slashLimit","operation":"add","value":10,"priority":0}],"activations":[
                {"id":"slash","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useVirtualSlash","target":"selectedTarget"}]},
                {"id":"refill","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"selectedTarget","amount":8}]},
                {"id":"duel","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"pause-original-draw","window":"nativeDrawStarting","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"changeMaximumHp","target":"owner","amount":2},{"op":"chooseOption","target":"owner","resultBind":"resume","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new[] { Driver, Quiet, Watch }.ToDictionary(id => id, id => new
                {
                    name = id, description = "Small actual Shanhe boss behavior fixture.",
                    optionLabels = id == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : []
                })
            }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "Actual Shanhe native rule fixture.") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddGeneral(new(Owner, "Shanhe rule actor", "", Driver, "qun", shenqu ? 3 : 20,
                shenqu ? [Shenqu, Watch] : [Wushuang, "classic:wusheng"]));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:shanhe-boss-peer-{i}").ToArray();
            // Native Duel AI conserves Slash at full HP; two missing HP makes it
            // genuinely answer, without bypassing its private response command.
            foreach (var peer in peers) b.AddGeneral(new(peer, "Shanhe quiet opponent", "", Quiet, "wei", 20,
                shenqu ? [] : ["classic:wusheng"]) { InitialHp = shenqu ? null : 18 });
            b.AddDeck(new("fixture:shanhe-boss-deck", "Uniform actual cards", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 160).Select(i => new ContentDeckPhysicalCard(
                    shenqu ? "standard:peach" : "standard:dodge", Suit.Heart, i % 13 + 1)).ToArray()
            });
            b.AddMode(new(Mode, "Native Shanhe boss rules", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 },
                "fixture:shanhe-boss-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
