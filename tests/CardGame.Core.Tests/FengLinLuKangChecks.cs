using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class FengLinLuKangChecks
{
    public static void ChainAndTargetPolicies()
    {
        var (game, registry) = Create("normal");
        Grow(game);
        Skill(game, "fixture:driver", "chain");
        Require(!game.CreateSnapshot(0).Players[0].IsChained, "A live entering-chain policy blocks the real state instruction.");
        var chain = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.IronChain && a.TargetSeats.Contains(0) && a.TargetSeats.Contains(2));
        Play(game, chain);
        ReachPlay(game);
        Require(!game.CreateSnapshot(0).Players[0].IsChained && game.CreateSnapshot(0).Players[2].IsChained, "Native Iron Chain keeps the protected recipient upright while another actual target becomes chained.");
        Replay(game, registry);
        Require(!game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Lightning), "Self Lightning is forbidden before entity payment.");
        Require(!game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Indulgence && a.TargetSeats.Contains(1)), "A protected other target cannot receive a delayed trick.");
        var before = State(game);
        var bad = game.Submit(new UseProgramSkillCommand(0, "fixture:driver", "contest", [], [1], game.Revision, Prompt(game)!.PromptId));
        Require(!bad.Accepted && before == State(game), "An opponent protected from other actors' Pindian is rejected atomically.");
        var payment = game.CreateCardZoneDiagnostics().First(c => c.Location == CardLocation.Hand(0)).CardId;
        Require(!game.Submit(new UseProgramSkillCommand(0, "fixture:driver", "legacy-contest", [payment], [1], game.Revision, Prompt(game)!.PromptId)).Accepted && State(game) == before,
            "The retained one-input Pindian path also rejects the protected target before moving its actual payment.");
        Accept(game, new UseProgramSkillCommand(0, "fixture:driver", "legacy-contest", [payment], [2], game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        Replay(game, registry);
        Skill(game, "fixture:driver", "choose-draw", wait: false);
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:driver");
        Require(Prompt(game)!.Choices.Any(c => c.Targets.Contains(1)), "A protected recipient remains eligible for an ordinary non-Pindian selection.");
        Answer(game, c => c.Targets.Contains(1));
        ReachPlay(game);
        Skill(game, "fixture:driver", "choose-contest", wait: false);
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:driver");
        Require(!Prompt(game)!.Choices.Any(c => c.Targets.Contains(1)) && Prompt(game)!.Choices.Any(c => c.Targets.Contains(2)), "Only the immediately consumed unconditional owner Pindian selection filters its protected opponent.");
        Replay(game, registry);
        Answer(game, c => c.Targets.Contains(2));
        Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Cards.Count == 1));
        Replay(game, registry);
        Answer(game, c => c.Cards.Count == 1);
        ReachPlay(game);
        Skill(game, "fixture:driver", "remove");
        Require(game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Lightning), "Losing the precise live target policy restores the native delayed-trick target.");
        Skill(game, "fixture:driver", "chain");
        Require(game.CreateSnapshot(0).Players[0].IsChained, "A lost policy no longer blocks entering chain.");
        Skill(game, "fixture:driver", "grant");
        Require(game.CreateSnapshot(0).Players[0].IsChained, "Receiving the policy does not retroactively clear an existing chain state.");
        Replay(game, registry);
        Skill(game, "fixture:driver", "suppress-contest", wait: false);
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:driver");
        Answer(game, c => c.Targets.Contains(1));
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:driver");
        Answer(game, c => c.Description.Contains("谦节", StringComparison.Ordinal));
        Reach(game, p => p.PlayerSeat == 0 && p.Choices.Any(c => c.Cards.Count == 1));
        Require(game.CreateSnapshot(0).Players[1].IsChained && game.ResolutionStack.OfType<PindianFrame>().Last().OpponentSeat == 1,
            "An actually suppressed target policy allows the real chain state change and subsequent Pindian against that exact opponent.");
        Replay(game, registry);
        Answer(game, c => c.Cards.Count == 1);
        ReachPlay(game);
        Replay(game, registry);
    }

    public static void MountGroupAndDistance()
    {
        var (game, registry) = Create("normal");
        Grow(game);
        Skill(game, "fixture:driver", "partial-mount");
        Require(!game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.Contains(2)), "Distant Snatch is unavailable before the actual slot cost.");
        Skill(game, "classic:jueyan", "mounts");
        Require(Capacity(game, EquipmentSlot.OffensiveHorse) == 0 && Capacity(game, EquipmentSlot.DefensiveHorse) == 0, "The paired option pays the remaining live mount slot.");
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:jueyan"), "All slot options share one actual-phase use.");
        Play(game, game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Snatch && a.TargetSeats.Contains(2)));
        Reach(game, p => p.Kind == DecisionKind.SelectTargetCard && p.PlayerSeat == 0);
        Require(Prompt(game)!.Choices.All(c => c.Cards.Count == 0), "The actual distant Snatch still presents hidden hand slots privately.");
        Replay(game, registry);
        Reject(game);
        Answer(game, _ => true);
        ReachPlay(game);
        Replay(game, registry);
    }

    public static void SlotCostNestedMovement()
    {
        var (game, registry) = Create("nested");
        Grow(game);
        Play(game, game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && game.CreateCardZoneDiagnostics().Single(c => c.CardId == a.CardId).CardKind == CardKind.BaguaFormation));
        ReachPlay(game);
        var equipment = game.CreateCardZoneDiagnostics().Single(c => c.Location == CardLocation.Equipment(0) && c.CardKind == CardKind.BaguaFormation).CardId;
        var hand = game.CreateSnapshot(0).Players[0].HandCount;
        Skill(game, "classic:jueyan", "armor", wait: false);
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:observer");
        Require(Capacity(game, EquipmentSlot.Armor) == 0 && game.CreateSnapshot(0).Players[0].HandCount == hand, "Actual equipment loss pauses after the committed slot cost, before Draw3 reward.");
        Require(game.CreateCardZoneDiagnostics().Single(c => c.CardId == equipment).Location.Zone == CardZoneKind.DiscardPile, "The real equipment entity paid the cost.");
        Require(Enumerable.Range(1, 3).All(v => game.CreateSnapshot(v).PendingDecision is null), "The movement child choice is private to its recipient.");
        Replay(game, registry);
        Reject(game);
        Answer(game, _ => true);
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].HandCount == hand + 3 && game.CardMovements.Count(m => m.CardId == equipment && m.To.Zone == CardZoneKind.DiscardPile) == 1, "Resume rewards exactly once and never pays the completed equipment cost twice.");
        Replay(game, registry);
    }

    public static void EquipmentRecastAtomicChild()
    {
        var (game, registry) = Create("nested");
        Grow(game);
        Play(game, game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && game.CreateCardZoneDiagnostics().Single(c => c.CardId == a.CardId).CardKind == CardKind.BaguaFormation));
        ReachPlay(game);
        var equipment = game.CreateCardZoneDiagnostics().Single(c => c.Location == CardLocation.Equipment(0) && c.CardKind == CardKind.BaguaFormation).CardId;
        var hand = game.CreateSnapshot(0).Players[0].HandCount;
        Skill(game, "classic:huairou", "recast-equipment", [equipment], wait: false);
        Reach(game, p => p.SkillPrompt?.SkillId == "fixture:observer");
        Require(game.CreateSnapshot(0).Players[0].HandCount == hand + 1 && game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count() == 1, "Existing recast protocol commits one real equipment loss and one gain before its movement child.");
        Replay(game, registry);
        Reject(game);
        Answer(game, _ => true);
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].HandCount == hand + 1 && game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count() == 1, "A resumed recast child never repeats payment or gain.");
        var card = game.CreateCardZoneDiagnostics().First(c => c.Location == CardLocation.Hand(0) && EquipmentCatalog.IsEquipment(c.CardKind)).CardId;
        Skill(game, "classic:huairou", "recast-equipment", [card]);
        Require(game.Events.Select(e => e.Payload).OfType<CardRecastEvent>().Count() == 2, "An owned hand equipment entity can also be truly recast.");
        var other = game.CreateCardZoneDiagnostics().First(c => c.Location == CardLocation.Hand(1)).CardId;
        var before = State(game);
        Require(!game.Submit(new UseProgramSkillCommand(0, "classic:huairou", "recast-equipment", [other], [], game.Revision, Prompt(game)!.PromptId)).Accepted && State(game) == before, "Another owner's entity cannot pay recast, atomically.");
        Replay(game, registry);
    }

    public static void PreparationAwakeningAndTurnGrant()
    {
        var (game, registry) = Create("peace");
        Grow(game);
        Skill(game, "classic:jueyan", "treasure");
        Require(game.CreateSnapshot(0).Players[0].Skills!.Any(s => s.ContentId == "classic:jizhi"), "Treasure cost issues a real temporary Jizhi grant.");
        var hand = game.CreateSnapshot(0).Players[0].HandCount;
        Play(game, game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.DrawTwo));
        Reach(game, p => p.SkillPrompt?.SkillId == "classic:jizhi");
        Replay(game, registry);
        Reject(game);
        Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0].HandCount == hand + 2, "The exact temporary grant executes real Jizhi Draw1 in addition to the paid DrawTwo.");
        Skill(game, "fixture:driver", "wound");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        var owner = game.CreateSnapshot(0).Players[0];
        Require(owner.MaxHp == 4 && !owner.Skills!.Any(s => s.ContentId == "classic:jueyan") && owner.Skills!.Any(s => s.ContentId == "classic:huairou") && !owner.Skills!.Any(s => s.ContentId == "classic:jizhi"), "Actual next preparation reduces max HP, replaces Jueyan with Huairou, and the previous turn grant expires.");
        Require(game.Events.Select(e => e.Payload).OfType<ProgramOwnerSkillsReplacedEvent>().Count(e => e.SkillId == "classic:poshi") == 1, "Awakening replacement executes once on its actual preparation boundary.");
        Replay(game, registry);
    }

    public static void NativeAiSlotChoice()
    {
        var (game, registry) = Create("ai");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<EquipmentSlotCapacityChangedEvent>().Any(e => e.Seat != 0 && e.Capacity == 0), "Actual AdvanceOneStep AI chooses and pays a real slot group.");
        Require(!game.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c => c.ActorSeat != 0), "AI choices are executed by the normal runtime, without human impersonation.");
        Replay(game, registry);

        var (contest, contestRegistry) = Create("ai-contest");
        Accept(contest, new EndPlayPhaseCommand(0, contest.Revision, Prompt(contest)!.PromptId));
        ReachPlay(contest);
        var results = contest.Events.Select(e => e.Payload).OfType<PindianResultDeterminedEvent>().ToArray();
        Require(results.Length > 0 && results.All(e => e.Result.OpponentSeat != 0),
            "Normal AI pays actual Pindian entities against other legal opponents while the live protected human is excluded.");
        Require(!contest.AcceptedCommands.OfType<AnswerPromptCommand>().Any(c => c.ActorSeat != 0),
            "Native AI contest selection and card payment never use manufactured human answers.");
        Replay(contest, contestRegistry);
    }

    public static void BothMountEntitiesAndSlotAwakening()
    {
        var (game, registry) = Create("peace");
        Grow(game);
        foreach (var kind in new[] { CardKind.OffensiveHorse, CardKind.DefensiveHorse })
        {
            Play(game, game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip &&
                game.CreateCardZoneDiagnostics().Single(c => c.CardId == a.CardId).CardKind == kind));
            ReachPlay(game);
        }
        var mounts = game.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.Equipment(0)).Select(c => c.CardId).ToArray();
        Require(mounts.Length == 2, "The fixture equips two real mount entities.");
        Skill(game, "classic:jueyan", "mounts");
        Require(mounts.All(id => game.CardMovements.Count(m => m.CardId == id && m.To.Zone == CardZoneKind.DiscardPile) == 1), "One paired activation discards both actual mount entities exactly once.");
        Replay(game, registry);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:jueyan" && a.ProgramActivationId == "mounts") &&
            game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:jueyan" && a.ProgramActivationId == "weapon"), "The next actual phase restores the shared limit but excludes the fully abolished mount group.");

        Skill(game, "fixture:driver", "all-slots");
        while (game.CreateCardZoneDiagnostics().FirstOrDefault(c => c.Location == CardLocation.Hand(0)) is { } card)
            Skill(game, "fixture:driver", "discard", [card.CardId]);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        Require(game.CreateSnapshot(0).Players[0] is { MaxHp: 4, HandCount: 6 } &&
            game.Events.Select(e => e.Payload).OfType<ProgramOwnerSkillsReplacedEvent>().Count(e => e.SkillId == "classic:poshi") == 1,
            "Five abolished slots awaken at healthy HP; hand fills to the new max before the normal Draw2.");
        Replay(game, registry);
    }

    public static void StrictResourceContracts()
    {
        const string presentation = """{"schemaVersion":3,"skills":{"fixture:contract":{"name":"公共契约","description":"边界"}}}""";
        var recast = $$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:contract","revision":1,"activations":[{"id":"recast","minCards":1,"maxCards":1,"sourceZones":["hand","equipment"],"cardCategories":["equipment"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"recastSelectedEquipment","target":"owner"}]}]}]}""";
        SkillProgramCatalog.Load(recast, presentation);
        var slot = $$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:contract","revision":1,"activations":[{"id":"pay","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"abolishEquipmentSlotGroup","target":"owner","equipmentSlots":["armor"]},{"op":"draw","target":"owner","amount":1}]}]}]}""";
        SkillProgramCatalog.Load(slot, presentation);
        var preparation = $$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:contract","revision":1,"triggers":[{"id":"replace","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"replaceSkillsOnPreparation","target":"owner","skillIds":["standard:none"],"sourceBind":"classic:huairou"}]}]}]}""";
        SkillProgramCatalog.Load(preparation, presentation);
        foreach (var bad in new[]
        {
            recast.Replace("\"minCards\":1", "\"minCards\":0"),
            recast.Replace("\"cardCategories\":[\"equipment\"]", "\"cardCategories\":[\"basic\"]"),
            recast.Replace("\"hand\",\"equipment\"", "\"judgment\""),
            slot.Replace("\"equipmentSlots\":[\"armor\"]", "\"equipmentSlots\":[\"armor\",\"armor\"]"),
            slot.Replace("\"target\":\"owner\",\"equipmentSlots\"", "\"target\":\"selectedTarget\",\"equipmentSlots\""),
            slot.Replace("\"effects\":[", "\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1},"),
            preparation.Replace("\"turnStartBeforeNormalFlow\"", "\"turnEnding\""),
            preparation.Replace("\"optional\":false", "\"optional\":true"),
            preparation.Replace("\"usageScope\":\"game\"", "\"usageScope\":\"turn\""),
            preparation.Replace("\"subject\":\"owner\"", "\"subject\":\"owner\",\"turnOwnerScope\":\"otherLiving\"")
        })
        {
            var rejected = false;
            try { SkillProgramCatalog.Load(bad, presentation); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "Unsafe owner/card category/source zone/slot ordering is rejected by the public resource contract.");
        }
    }

    public static void ExtraActualPlayPhaseLimit()
    {
        var (game, registry) = Create("extra");
        Skill(game, "classic:jueyan", "weapon");
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        Require(game.Events.Select(e => e.Payload).OfType<ProgramPhaseScheduledEvent>().Last() is { Phase: TurnPhase.Play, Started: true }, "The second fixed preparation inserts a real extra Play phase.");
        Skill(game, "classic:jueyan", "armor");
        Require(!game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:jueyan"), "The actual extra phase consumes the common group limit once.");
        Replay(game, registry);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        Require(game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "classic:jueyan" && a.ProgramActivationId == "mounts"), "The later normal Play in the same actual turn has an independent phase limit.");
        Skill(game, "classic:jueyan", "mounts");
        Replay(game, registry);
    }

    private static int Capacity(GameEngine game, EquipmentSlot slot) => game.CreateSnapshot(0).Players[0].EquipmentSlotCapacities?.GetValueOrDefault(slot) ?? 1;
    private static PendingDecision? Prompt(GameEngine game) => Enumerable.Range(0, 4).Select(v => game.CreateSnapshot(v).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Grow(GameEngine game) => Skill(game, "fixture:driver", "grow");
    private static void Skill(GameEngine game, string id, string activation, IReadOnlyList<int>? cards = null, bool wait = true)
    {
        Accept(game, new UseProgramSkillCommand(0, id, activation, cards ?? [], [], game.Revision, Prompt(game)!.PromptId));
        if (wait) ReachPlay(game);
    }
    private static void Play(GameEngine game, LegalAction action) => Accept(game, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind, action.TargetCardId) { ConversionSource = action.ConversionSource });
    private static void Answer(GameEngine game, Func<PromptChoice, bool> choose)
    {
        var prompt = Prompt(game)!;
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(choose).Id, game.Revision));
    }
    private static void ReachPlay(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> done)
    {
        for (var step = 0; step < 300; step++)
        {
            if (Prompt(game) is { } prompt && done(prompt)) return;
            if (Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger }) Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.Nullification }) Answer(game, c => c.Parameters.GetValueOrDefault("response") == "pass");
            else if (Prompt(game) is { PlayerSeat: 0, Kind: DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.RescueDying }) Answer(game, c => c.Cards.Count == 0);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Fixed fixture did not reach its prompt: " + JsonSerializer.Serialize(Prompt(game)));
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(game.CreateSnapshot(v))).ToArray(), Frames = game.ResolutionStack, Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(), game.CardMovements, Zones = game.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(game.AcceptedCommands) });
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(State(game) == State(restored), "All viewer snapshots, exact owning frames, entities, events and command JSON restore identically.");
        var cards = game.CreateCardZoneDiagnostics();
        Require(cards.Count == 64 && cards.Select(c => c.CardId).Distinct().Count() == 64, "Actual entity conservation includes every zone.");
    }
    private static void Reject(GameEngine game)
    {
        var before = State(game);
        var prompt = Prompt(game)!;
        Require(!game.Submit(new AnswerPromptCommand((prompt.PlayerSeat + 1) % 4, prompt.PromptId, prompt.Choices[0].Id, game.Revision)).Accepted && before == State(game), "Wrong actor cannot mutate a private child.");
        Require(!game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, new ChoiceId("illegal"), game.Revision)).Accepted && before == State(game), "Illegal answer cannot mutate the owning frame.");
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Rejected command");
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
    private static (GameEngine, ContentRegistry) Create(string mode)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(mode));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-lukang-fixture", UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:owner", game.Revision, Prompt(game)!.PromptId));
        ReachPlay(game);
        return (game, registry);
    }
    private sealed class Fixture(string mode) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-lukang", new Version(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$$"""
                {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:driver","revision":1,"activations":[
                {"id":"grow","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20}]},
                {"id":"chain","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setChainedState","target":"owner","chained":true}]},
                {"id":"partial-mount","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["offensiveHorse"],"amount":0}]},
                {"id":"all-slots","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"alterEquipmentSlots","target":"owner","equipmentSlots":["weapon","armor","offensiveHorse","defensiveHorse","treasure"],"amount":0}]},
                {"id":"discard","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"discardSelected","target":"owner","amount":1}]},
                {"id":"wound","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":4}]},
                {"id":"contest","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":null,"effects":[{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"}]},
                {"id":"legacy-contest","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":null,"effects":[{"op":"pindian","target":"selectedTarget","amount":1}]},
                {"id":"choose-draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingWithHand"},{"op":"draw","target":"selectedTarget","amount":1}]},
                {"id":"choose-contest","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingWithHand"},{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"}]},
                {"id":"suppress-contest","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLivingWithHand"},{"op":"suppressGeneralSkill","target":"selectedTarget"},{"op":"setChainedState","target":"selectedTarget","chained":true},{"op":"startPindian","target":"owner","opponentRef":{"kind":"selectedTarget"},"resultBind":"contest","visibility":"public"}]},
                {"id":"remove","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["classic:qianjie"],"sourceBind":"standard:none"}]},
                {"id":"grant","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["classic:qianjie"]}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:driver":{"name":"实体夹具","description":"固定小夹具"}}}""");
            builder.AddSkill(new("fixture:driver", "实体夹具", "固定小夹具") { Program = catalog.Programs["fixture:driver"] });
            var observer = SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:observer","revision":1,"triggers":[{"id":"equipment-loss","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"finish","options":[{"id":"continue"}]}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:observer":{"name":"真实装备移动观察","description":"嵌套暂停","optionLabels":{"continue":"继续"}}}}""");
            builder.AddSkill(new("fixture:observer", "移动观察", "嵌套") { Program = observer.Programs["fixture:observer"] });
            var phase = SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:phase","revision":1,"triggers":[{"id":"insert","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,"effects":[{"op":"replaceJudgmentPhase","target":"owner"}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:phase":{"name":"实际额外阶段","description":"既有调度能力"}}}""");
            builder.AddSkill(new("fixture:phase", "实际额外阶段", "既有调度") { Program = phase.Programs["fixture:phase"] });
            var aiContest = SkillProgramCatalog.Load($$$"""{"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"fixture:ai-contest","revision":1,"activations":[{"id":"contest","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLivingWithHand","usesPerTurn":1,"effects":[{"op":"pindian","target":"selectedTarget","amount":1},{"op":"draw","target":"owner","amount":3}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:ai-contest":{"name":"正常AI拼点","description":"真实支付"}}}""");
            builder.AddSkill(new("fixture:ai-contest", "正常AI拼点", "真实支付") { Program = aiContest.Programs["fixture:ai-contest"] });
            builder.AddGeneral(new("fixture:owner", "陆抗机制", "supporter", mode == "ai" ? "standard:none" : "classic:qianjie", "wu", mode == "peace" ? 4 : 12, mode == "ai" ? ["fixture:driver"] : mode == "extra" ? ["classic:jueyan", "fixture:driver", "fixture:phase"] : mode == "nested" ? ["classic:jueyan", "classic:poshi", "classic:huairou", "fixture:driver", "fixture:observer"] : ["classic:jueyan", "classic:poshi", "fixture:driver"]));
            for (var i = 1; i < 4; i++) builder.AddGeneral(new($"fixture:target-{i}", "目标", "supporter", mode == "ai" ? "classic:jueyan" : mode == "ai-contest" ? "fixture:ai-contest" : i == 1 ? "classic:qianjie" : "standard:none", "wei", 12, []));
            var kinds = mode is "peace" or "ai" or "extra" or "ai-contest" ? new[] { "standard:bagua", "standard:defensive_horse", "standard:offensive_horse", "standard:dodge", "standard:draw_two" } : new[] { "standard:bagua", "standard:iron_chain", "standard:indulgence", "standard:lightning", "standard:snatch", "standard:defensive_horse", "standard:offensive_horse", "standard:dodge" };
            builder.AddDeck(new("fixture:deck", "真实实体", 3, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(kinds[i % kinds.Length], Suit.Heart, i % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-lukang-fixture", "机制", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:owner", "fixture:target-1", "fixture:target-2", "fixture:target-3"]));
        }
    }
}
