using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
internal static class OlModernGodsChecks
{
    private const string Driver = "fixture:modern-driver";
    private const string Mode = "identity:modern-fixture";
    public static void AdvancedDefinitionValidation()
    {
        string[] invalid = [
            "{\"op\":\"alterEquipmentSlots\",\"target\":\"owner\",\"amount\":-1,\"equipmentSlots\":[\"weapon\"]}",
            "{\"op\":\"alterEquipmentSlots\",\"target\":\"owner\",\"amount\":2,\"equipmentSlots\":[]}",
            "{\"op\":\"equipSampledGenerals\",\"target\":\"owner\",\"amount\":2147483647}",
            "{\"op\":\"accumulateCardRank\",\"target\":\"owner\"}",
            "{\"op\":\"replaceSkillsOnAwakening\",\"target\":\"owner\",\"skillIds\":[]}",
            "{\"op\":\"obtainDeckRankSum\",\"target\":\"owner\",\"marker\":\"huang\",\"maximumRankSum\":2147483647}",
            "{\"op\":\"placeNamedWeapon\",\"target\":\"owner\",\"outputKind\":\"slash\"}",
            "{\"op\":\"inheritWeapon\",\"target\":\"owner\",\"amount\":1}"
        ];
        foreach (var effect in invalid)
        {
            var rejected = false;
            try
            {
                SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:invalid-advanced","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[{"id":"invalid","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{{effect}}]}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:invalid-advanced":{"name":"Fixture","description":"Fixture"}}}""");
            }
            catch (InvalidOperationException exception) when (exception.Message.Contains("Invalid skill program", StringComparison.Ordinal)) { rejected = true; }
            Require(rejected, "Invalid advanced input must fail at content load: " + effect);
        }
    }
    public static void SamplingAwakeningAndReplay()
    {
        var (game, registry) = Create("sun", drain: false);
        for (var i = 0; i < 100 && game.PendingDecision?.Choices.Any(choice => choice.Parameters.ContainsKey("advanced-value")) != true; i++) Step(game);
        for (var i = 0; i < 3; i++)
        {
            Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1));
            EqualReplay(game, registry);
        }
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("advanced-value") == "finish"));
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "ol:dili", "Skill acquisition must immediately open the awakening window.");
        EqualReplay(game, registry);
        for (var i = 0; i < 3; i++) Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("advanced-value") is { } id && id is not ("finish" or "ol:yuheng" or Driver)));
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("advanced-value") == "finish"));
        Drain(game);
        Require(game.CreateSnapshot(0, true).Players[0].Skills!.Count(skill => skill.ContentId is "ol:shengzhi" or "ol:quandao" or "ol:chigang") == 3, "Awakening must replace three selected skills with the ordered derived skills.");
        Require(game.CreateSnapshot(0, true).Players[0].MaxHp == 4, "Awakening must reduce maximum HP exactly once.");
        EqualReplay(game, registry);
        NextTurn(game);
        EqualReplay(game, registry);
    }
    public static void ExactDeckSumAndCardRankReplay()
    {
        var (game, registry) = Create("zhang");
        Use(game, "draw");
        var card = game.CreateSnapshot(0, true).Players[0].Hand.First(card => card.Kind == CardKind.DrawTwo);
        Accept(game.Submit(new PlayCardCommand(0, card.Id, [], game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        Require(game.CreateSnapshot(0, true).Players[0].Markers!.Single(marker => marker.Kind == PlayerMarkerKind.Huang).Count == card.Rank, "Card use must accumulate the actual effective rank.");
        Use(game, "markers");
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var i = 0; i < 500 && game.PendingDecision?.SkillPrompt?.SkillId != "ol:sijun"; i++) Step(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "ol:sijun", "Sijun must become available only when the marker total exceeds the deck size.");
        Step(game, activate: true);
        Require(game.CreateSnapshot(1).PendingDecision is null, "The deck subset prompt must be private to its chooser.");
        EqualReplay(game, registry);
        for (var i = 0; i < 36 && game.PendingDecision!.Choices.All(choice => choice.Parameters.GetValueOrDefault("advanced-value") != "finish"); i++)
            Answer(game, game.PendingDecision!.Choices.First(choice => choice.Cards.Count == 1));
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("advanced-value") == "finish"));
        Drain(game);
        var obtained = game.Events.Select(item => item.Payload).OfType<DeckRankCardsObtainedEvent>().Last(item => item.SkillId == "ol:sijun");
        Require(game.CreateSnapshot(0, true).Players[0].Hand.Where(card => obtained.CardIds.Contains(card.Id)).Sum(card => card.Rank) == 36, "An exact deck search must obtain precisely a rank sum of 36.");
        Require(game.CreateSnapshot(0, true).Players[0].Markers?.All(marker => marker.Kind != PlayerMarkerKind.Huang || marker.Count == 0) != false, "Sijun must remove all yellow markers.");
        EqualReplay(game, registry);
    }
    public static void GeneralWeaponsSlotsAndPerTargetDamage()
    {
        var (game, registry) = Create("dian", drain: false);
        for (var i = 0; i < 100 && game.PendingDecision?.SkillPrompt?.SkillId != "ol:qiexie"; i++) Step(game);
        for (var i = 0; i < 2; i++) Answer(game, game.PendingDecision!.Choices.First(choice => choice.Parameters.GetValueOrDefault("advanced-value") != "finish"));
        EqualReplay(game, registry);
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("advanced-value") == "finish"));
        Drain(game);
        var player = game.CreateSnapshot(0, true).Players[0];
        Require(player.EquipmentSlotCapacities!.GetValueOrDefault(EquipmentSlot.Armor) == 0 && player.EquipmentSlotCapacities!.GetValueOrDefault(EquipmentSlot.Weapon) == 2, "Startup must abolish armor and provide two weapon slots.");
        Require(player.Equipment.Count == 2 && player.Equipment.All(card => card.Kind == CardKind.GeneralWeapon && card.Suit == Suit.None && card.Rank == 0), "Selected generals must become two physical weapons without suit or rank.");
        Use(game, "draw");
        var action = game.GetHumanLegalActions().First(action => action.ProgramSkillId == "ol:cuijue");
        var payment = game.CreateSnapshot(0, true).Players[0].Hand.First().Id;
        Accept(game.Submit(new UseProgramSkillCommand(0, "ol:cuijue", "discard-farthest-damage", [payment], [], game.Revision, game.PendingDecision!.PromptId)));
        var target = game.PendingDecision!.Choices.First().Targets.Single();
        Answer(game, game.PendingDecision.Choices.First());
        Drain(game);
        Require(!game.GetHumanLegalActions().Any(action => action.ProgramSkillId == "ol:cuijue"), "The only farthest target must become unavailable for the entire turn.");
        var generalWeapon = player.Equipment[0].Id;
        var crossbow = game.CreateSnapshot(0, true).Players[0].Hand.First(card => card.Kind == CardKind.Crossbow);
        Accept(game.Submit(new PlayCardCommand(0, crossbow.Id, [], game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        Require(game.CreateCardZoneDiagnostics().Single(card => card.CardId == generalWeapon).Location == CardLocation.OutsideGame, "A replaced general weapon must be destroyed rather than entering the discard pile.");
        EqualReplay(game, registry);
        VerifyAiGeneralWeaponsAndTransfers();
    }
    private static void VerifyAiGeneralWeaponsAndTransfers()
    {
        var (game, registry) = Create("dian-target");
        Use(game, "draw");
        var holder = game.CreateSnapshot(0, true).Players.Single(player => player.GeneralId == "fixture:modern-opponent-1");
        var weapons = holder.Equipment.Where(card => card.Kind == CardKind.GeneralWeapon).Select(card => card.Id).ToArray();
        Require(weapons.Length == 2, "AI Qiexie must actually select and equip exactly two sampled general weapons.");
        var boundary = game.CreateCheckpoint();
        var borrowed = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.BorrowedSword && action.TargetSeats[0] == holder.Seat);
        Accept(game.Submit(new PlayCardCommand(0, borrowed.CardId!.Value, borrowed.TargetSeats, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        Require(weapons.All(id => game.CreateCardZoneDiagnostics().Single(card => card.CardId == id).Location == CardLocation.OutsideGame),
            "Declining Borrowed Sword must remove both weapons and destroy both general cards.");
        Require(weapons.All(id => game.CardMovements.Any(move => move.CardId == id && move.From == CardLocation.Equipment(holder.Seat) && move.To == CardLocation.OutsideGame && move.Reason == CardMoveReasons.BorrowedSwordGive)),
            "Both weapon exits must retain their actual Borrowed Sword movement reason.");
        EqualReplay(game, registry);
        foreach (var kind in new[] { LegalActionKind.Snatch, LegalActionKind.Dismantlement })
        {
            var branch = GameReplay.Restore(boundary, registry);
            var movementCount = branch.CardMovements.Count;
            var targetCard = weapons[0];
            var action = branch.GetHumanLegalActions().First(action => action.Kind == kind && action.TargetSeat == holder.Seat);
            Accept(branch.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, branch.Revision, branch.PendingDecision!.PromptId, TargetCardId: targetCard)));
            if (branch.PendingDecision?.Choices.FirstOrDefault(choice => choice.Cards.Contains(targetCard)) is { } selectedWeapon)
                Answer(branch, selectedWeapon);
            Drain(branch);
            Require(branch.CreateCardZoneDiagnostics().Single(card => card.CardId == targetCard).Location == CardLocation.OutsideGame &&
                !branch.CardMovements.Skip(movementCount).Any(move => move.CardId == targetCard && move.From == CardLocation.OutsideGame),
                $"Taking or discarding a general weapon must destroy it once and never return it from OutsideGame ({kind}, {branch.CreateCardZoneDiagnostics().Single(card => card.CardId == targetCard).Location}).");
            EqualReplay(branch, registry);
        }
    }
    public static void PermanentWeaponInheritanceAndReplay()
    {
        var (game, registry) = Create("huang");
        Use(game, "draw");
        var weapon = game.CreateSnapshot(0, true).Players[0].Hand.First(card => card.Kind == CardKind.Crossbow);
        Accept(game.Submit(new PlayCardCommand(0, weapon.Id, [], game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var i = 0; i < 500 && game.PendingDecision?.SkillPrompt?.SkillId != "ol:huaren"; i++) Step(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "ol:huaren", "Limited inheritance must be offered when an eligible weapon exists.");
        Step(game, activate: true);
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Cards.Contains(weapon.Id)));
        Drain(game);
        Require(game.CreateCardZoneDiagnostics().Single(card => card.CardId == weapon.Id).Location == CardLocation.OutsideGame, "Inherited weapons must be permanently removed.");
        var slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash);
        Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        Require(game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash), "Inherited Crossbow must allow another Slash without equipment.");
        EqualReplay(game, registry);
    }
    public static void NamedBladePlacementReclaimAndReplay()
    {
        var (game, registry) = Create("huang", drain: false);
        for (var i = 0; i < 100 && game.PendingDecision?.SkillPrompt?.SkillId != "ol:shenyu"; i++) Step(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "ol:shenyu", "Shenyu must offer its virtual Slash at play-phase start.");
        var handBefore = game.CreateSnapshot(0, true).Players[0].HandCount;
        Step(game, activate: true);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Targets.Contains(1)));
        Drain(game);
        Require(game.CreateSnapshot(0, true).Players[0].HandCount == handBefore + 3,
            "A skill-created virtual Slash must enter shared committed and completed windows, paying no physical card.");
        Require(game.Events.Any(item => item.Payload is CardActionAcceptedEvent accepted &&
            accepted.Action.Type == CardActionType.Use && accepted.Action.EffectiveKind == CardKind.Slash &&
            accepted.Action.PhysicalCards.Count == 0 && accepted.Action.EffectiveRank == 0),
            "A zero-cost virtual Slash must retain an accepted action with empty physical provenance and rank zero.");
        var blade = game.CreateSnapshot(0, true).Players[1].Equipment.Single(card => card.Kind == CardKind.RedBloodBlade);
        Require(game.GetAttackRange(1) == 0 && blade.Suit == Suit.Heart && blade.Rank == 13, "A generated blade must be Heart K and have range zero without Shenyu.");
        EqualReplay(game, registry);
        Use(game, "draw");
        var crossbow = game.CreateSnapshot(0, true).Players[0].Hand.First(card => card.Kind == CardKind.Crossbow);
        Accept(game.Submit(new PlayCardCommand(0, crossbow.Id, [], game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var i = 0; i < 100 && game.PendingDecision?.SkillPrompt?.SkillId != "ol:shenyu"; i++) Step(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "ol:shenyu", "Damage during the turn must enable reclaiming the blade at its end.");
        Step(game, activate: true);
        Require(game.CreateSnapshot(0, true).Players[0].Equipment.Any(card => card.Id == blade.Id) && game.GetAttackRange(0) == 3, "Reclaim must move the same physical blade to its owner's equipment and restore range three.");
        Require(game.CreateCardZoneDiagnostics().Single(card => card.CardId == crossbow.Id).Location == CardLocation.DiscardPile, "Reclaim must physically replace the owner's prior weapon.");
        EqualReplay(game, registry);
        Drain(game);
        Use(game, "draw");
        var slash = game.GetHumanLegalActions().Where(action => action.Kind == LegalActionKind.Slash)
            .MaxBy(action => game.GetCombatDistance(0, action.TargetSeat!.Value))!;
        var target = slash.TargetSeat!.Value;
        var hp = game.CreateSnapshot(0, true).Players[target].Hp;
        Use(game, "mist", drain: false);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Targets.Contains(target)));
        Drain(game);
        slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeat == target);
        Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        Require(game.CreateSnapshot(0, true).Players[target].Hp == hp, "Mist must actually prevent the first farthest-target Slash damage.");
        EqualReplay(game, registry);
        Use(game, "clear-mist", drain: false);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Targets.Contains(target)));
        Drain(game);
        slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeat == target);
        Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        Require(game.CreateSnapshot(0, true).Players[target].Hp == hp - 2, $"Prevented damage must leave the blade's first actual farthest-target Slash damage bonus available: before {hp}, now {game.CreateSnapshot(0, true).Players[target].Hp}, range {game.GetAttackRange(0)}, weapon {string.Join(',', game.CreateSnapshot(0, true).Players[0].Equipment.Select(card => card.Kind))}.");
        Require(game.GetHumanLegalActions().Any(action => action.Kind == LegalActionKind.Slash), "The blade must preserve the abilities of the first weapon it replaced, including Crossbow's unlimited Slashes.");
        EqualReplay(game, registry);
        slash = game.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash && action.TargetSeat == target);
        Accept(game.Submit(new PlayCardCommand(0, slash.CardId!.Value, slash.TargetSeats, game.Revision, game.PendingDecision!.PromptId)));
        AdvanceUntil(game, () => game.State.Status == EngineStatus.Completed ||
            game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0);
        Require(game.CreateSnapshot(0, true).Players[target].Hp == hp - 3, "Actual damage must consume the blade bonus exactly once during the turn.");
        EqualReplay(game, registry);
        VerifyRedirectedVirtualSlash();
    }

    private static void VerifyRedirectedVirtualSlash()
    {
        var (game, registry) = Create("redirect", drain: false);
        AdvanceUntil(game, () => game.PendingDecision?.SkillPrompt?.SkillId == "ol:shenyu");
        var target = game.CreateSnapshot(0, true).Players.Single(player => player.GeneralId == "fixture:modern-opponent-1").Seat;
        Step(game, activate: true);
        Answer(game, game.PendingDecision!.Choices.First(choice => choice.Targets.Contains(target)));
        EqualReplay(game, registry);
        for (var i = 0; i < 200 && !(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0); i++)
        {
            if (game.PendingDecision is { PlayerSeat: not 0 })
                Accept(game.Submit(new AdvanceOneStepCommand(game.Revision)));
            else Step(game);
        }
        var snapshot = game.CreateSnapshot(0, true);
        var holder = snapshot.Players.Single(player => player.Equipment.Any(card => card.Kind == CardKind.RedBloodBlade));
        Require(holder.Seat != target, "A real Liuli redirection must move Shenyu's generated blade to the final Slash target.");
        Require(game.Events.Any(item => item.Payload is ProgramBindingResolvedEvent { SkillId: "classic:liuli", Activated: true, Completed: true }),
            "The fixture must execute the existing Liuli program, including its physical card payment.");
        Require(game.CardMovements.Any(item => item.Reason.Value == "skill-program.classic:liuli.SelectAndMoveOwnedCard" && item.To == CardLocation.DiscardPile),
            "A redirect must preserve Liuli's actual physical discard cost.");
        Require(game.Events.Any(item => item.Payload is CardUseFinishedEvent { CardId: 0, CardKind: CardKind.Slash }),
            "The redirected zero-cost Slash must publish its shared completion event.");
        EqualReplay(game, registry);
    }
    public static void ShuffleThunderTargetsAndReplay()
    {
        var (game, registry) = Create("zhang");
        Use(game, "draw");
        var used = game.CreateSnapshot(0, true).Players[0].Hand.First(card => card.Kind == CardKind.DrawTwo);
        Accept(game.Submit(new PlayCardCommand(0, used.Id, [], game.Revision, game.PendingDecision!.PromptId)));
        Drain(game);
        for (var i = 0; i < 12 && !game.Events.Any(item => item.Payload is CardMovedEvent moved && moved.Reason == CardMoveReasons.Reshuffle); i++) Use(game, "draw");
        Require(game.Events.Any(item => item.Payload is CardMovedEvent moved && moved.Reason == CardMoveReasons.Reshuffle), "The fixture must perform a real draw-pile shuffle.");
        var before = game.CreateSnapshot(0, true).Players.ToArray();
        Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId)));
        for (var i = 0; i < 100 && game.PendingDecision?.SkillPrompt?.SkillId != "ol:tianjie"; i++) Step(game);
        Require(game.PendingDecision?.SkillPrompt?.SkillId == "ol:tianjie", "A real shuffle must enable Tianjie at turn end.");
        Step(game, activate: true);
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("advanced-value") == "1"));
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("advanced-value") == "3"));
        EqualReplay(game, registry);
        Answer(game, game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("advanced-value") == "finish"));
        for (var i = 0; i < 100 && !new[] { 1, 3 }.All(seat => game.Events.Any(item => item.Payload is DamageAppliedEvent { Nature: DamageNature.Thunder } damage && damage.TargetSeat == seat)); i++) Step(game);
        foreach (var seat in new[] { 1, 3 }) Require(game.Events.Any(item => item.Payload is DamageAppliedEvent { Nature: DamageNature.Thunder } damage && damage.TargetSeat == seat && damage.Amount == Math.Max(1, before[seat].Hand.Count(card => card.Kind == CardKind.Dodge))), "Every selected target must receive its own Dodge-count thunder damage, at least one.");
        EqualReplay(game, registry);
    }
    private static (GameEngine Game, ContentRegistry Registry) Create(string flavor, bool drain = true)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(flavor));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = flavor == "dian-target" ? "identity:classic-modern-fixture" : Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(game.Submit(new StartGameCommand()));
        Accept(game.Submit(new SelectGeneralCommand(0, "fixture:modern-owner", game.Revision, game.PendingDecision!.PromptId)));
        if (drain) Drain(game);
        return (game, registry);
    }
    private static void Use(GameEngine game, string id, bool drain = true)
    {
        Accept(game.Submit(new UseProgramSkillCommand(0, Driver, id, [], [], game.Revision, game.PendingDecision!.PromptId)));
        if (drain) Drain(game);
    }
    private static void Drain(GameEngine game)
    {
        for (var i = 0; i < 1000; i++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.ResolutionStack.Count == 0) return;
            Step(game);
        }
        throw new InvalidOperationException("Modern fixture did not return to owner play.");
    }
    private static void Step(GameEngine game, bool activate = false)
    {
        if (game.PendingDecision is not { } prompt) { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); return; }
        if (prompt.PlayerSeat != 0) { Accept(game.Submit(new AdvanceOneStepCommand(game.Revision))); return; }
        if (prompt.Kind == DecisionKind.PlayCard) { Accept(game.Submit(new EndPlayPhaseCommand(prompt.PlayerSeat, game.Revision, prompt.PromptId))); return; }
        var choice = prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("program-action") == (activate ? "activate" : "skip")) ??
            prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("response") is "take-damage" or "pass") ??
            prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("choice") == "finish") ?? prompt.Choices.First();
        Answer(game, choice);
    }
    private static void Answer(GameEngine game, PromptChoice choice) => Accept(game.Submit(new AnswerPromptCommand(game.PendingDecision!.PlayerSeat, game.PendingDecision.PromptId, choice.Id, game.Revision)));
    private static void AdvanceUntil(GameEngine game, Func<bool> complete)
    {
        for (var i = 0; i < 1000; i++) { if (complete()) return; Step(game); }
        throw new InvalidOperationException("Modern fixture did not reach its expected boundary.");
    }
    private static void NextTurn(GameEngine game) { Accept(game.Submit(new EndPlayPhaseCommand(0, game.Revision, game.PendingDecision!.PromptId))); Drain(game); }
    private static void EqualReplay(GameEngine game, ContentRegistry registry)
    {
        var replay = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(replay.CreateSnapshot(0, true)), "Modern state must survive checkpoint replay.");
        Require(game.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())).SequenceEqual(replay.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType()))), "Modern events must replay deterministically.");
    }
    private static void Accept(CommandResult result) => Require(result.Accepted, result.Error?.Message ?? "Modern command rejected.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed class Fixture(string flavor) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("modern-fixture", new Version(1, 0, 0), [new PackageDependency("standard", new Version(1, 0, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            if (flavor == "dian-target")
                builder.AddCard(new ContentCardDefinition("classic:borrowed-sword", "借刀杀人", "锦囊牌", "Fixture", LegacyKind: CardKind.BorrowedSword));
            foreach (var bundle in new[] { "ol-shen-sun-quan", "ol-shen-zhang-jiao", "ol-shen-dian-wei", "ol-shen-huang-zhong" })
            {
                string Read(string suffix) { using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + bundle + "." + suffix + ".json")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var rules = Read("rules");
                // Reach the AI sampling boundary before anyone can use tricks to remove the fixture weapons.
                if (flavor == "dian-target" && bundle == "ol-shen-dian-wei")
                    rules = rules.Replace("\"window\":\"turnStartBeforeNormalFlow\"", "\"window\":\"gameStarting\"", StringComparison.Ordinal);
                var catalog = SkillProgramCatalog.Load(rules, Read("presentation"));
                foreach (var (id, program) in catalog.Programs)
                {
                    var presentation = catalog.Presentations[id];
                    var tags = id switch { "ol:dili" => SkillTag.Awakening, "ol:huaren" => SkillTag.Limited,
                        "ol:chigang" => SkillTag.Locked | SkillTag.Conversion,
                        "ol:yuheng" or "ol:yizhao" or "ol:juanjia" or "ol:qiexie" or "ol:quandao" or "ol:shengzhi" => SkillTag.Locked, _ => SkillTag.None };
                    builder.AddSkill(new ContentSkillDefinition(id, presentation.Name, presentation.Description) { Program = program, ProgramPresentation = presentation, Tags = tags });
                }
            }
            if (flavor == "redirect")
            {
                string ReadLiuli(string suffix) { using var stream = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.passive-response-rules." + suffix + ".json")!; using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                // Force the optional activation in this fixture; keep the real target, cost and redirect instructions.
                var liuli = SkillProgramCatalog.Load(ReadLiuli("rules").Replace("\"optional\":true", "\"optional\":false", StringComparison.Ordinal), ReadLiuli("presentation"));
                builder.AddSkill(new ContentSkillDefinition("classic:liuli", "流离", "流离") { Program = liuli.Programs["classic:liuli"], ProgramPresentation = liuli.Presentations["classic:liuli"] });
            }
            var driver = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:modern-driver","revision":1,"minimumRulesVersion":{{GameCheckpoint.CurrentRulesVersion}},"activations":[
                {"id":"draw","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"draw","target":"owner","amount":20},{"op":"draw","target":"owner","amount":20}]},
                {"id":"draw-opponent","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"draw","target":"selectedTarget","amount":20},{"op":"draw","target":"selectedTarget","amount":20}]},
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"mist","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"setMarkerAmount","target":"selectedTarget","marker":"mist","amount":1}]},
                {"id":"clear-mist","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"setMarkerAmount","target":"selectedTarget","marker":"mist","amount":-1}]},
                {"id":"markers","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"setMarkerAmount","target":"owner","marker":"huang","amount":1000},{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},{"op":"placeNamedWeapon","target":"selectedTarget","outputKind":"redBloodBlade"}]}],"triggers":[{"id":"virtual-use-observer","window":"cardUseCommitted","ownerRelation":"actor","optional":false,"cardKinds":["slash"],"effects":[{"op":"draw","target":"owner","amount":2}]},{"id":"virtual-completed-observer","window":"cardUseCompleted","ownerRelation":"actor","optional":false,"cardKinds":["slash"],"effects":[{"op":"draw","target":"owner","amount":1}]}]}]}
                """,
                """{"schemaVersion":3,"skills":{"fixture:modern-driver":{"name":"Fixture","description":"Fixture"}}}""");
            builder.AddSkill(new ContentSkillDefinition(Driver, "Fixture", "Fixture") { Program = driver.Programs[Driver] });
            string[] skills = flavor switch { "sun" => ["ol:yuheng", "ol:dili"], "zhang" => ["ol:yizhao", "ol:sijun", "ol:tianjie"], "dian" => ["ol:juanjia", "ol:qiexie", "ol:cuijue"], "dian-target" => ["standard:none"], _ => ["ol:shenyu", "ol:huaren"] };
            builder.AddGeneral(new ContentGeneralDefinition("fixture:modern-owner", "Fixture", "supporter", skills[0], "god", BaseHp: flavor == "zhang" ? 3 : 4, AdditionalSkillIds: [.. skills.Skip(1), Driver]));
            var opponents = Enumerable.Range(1, 3).Select(index => $"fixture:modern-opponent-{index}").ToArray();
            foreach (var opponent in opponents) builder.AddGeneral(new ContentGeneralDefinition(opponent, "Opponent", "supporter", flavor == "redirect" && opponent == opponents[0] ? "classic:liuli" : flavor == "dian-target" && opponent == opponents[0] ? "ol:juanjia" : "standard:none", "qun", BaseHp: 20,
                AdditionalSkillIds: flavor == "dian-target" && opponent == opponents[0] ? ["ol:qiexie"] : null));
            for (var index = 0; index < 5; index++)
            {
                var id = $"fixture:wu-skill-{index}";
                builder.AddSkill(new ContentSkillDefinition(id, "Wu", "你的【杀】具备测试效果。"));
                builder.AddGeneral(new ContentGeneralDefinition($"fixture:wu-{index}", "Wu", "supporter", id, "wu", BaseHp: 3 + index));
            }
            builder.AddDeck(new ContentDeckRecipe("fixture:modern-deck", "Fixture", flavor == "sun" ? 8 : 4, 2, []) { PhysicalCards = Enumerable.Range(0, 400).Select(index => new ContentDeckPhysicalCard(flavor == "dian-target" ? new[] { "classic:borrowed-sword", "standard:snatch", "standard:dismantlement" }[index % 3] : index % 5 == 0 ? "standard:slash" : index % 3 == 0 ? "standard:draw_two" : "standard:crossbow", (Suit)(index % 4), index % 13 + 1)).ToArray() });
            var expandedPool = flavor is "dian" or "dian-target";
            builder.AddMode(new ContentModeDefinition(flavor == "dian-target" ? "identity:classic-modern-fixture" : Mode, "Fixture", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 1, [nameof(Role.Renegade)] = 1 }, "fixture:modern-deck", GeneralCandidateCount: expandedPool ? 9 : 4, GeneralPoolIds: expandedPool ? ["fixture:modern-owner", .. opponents, .. Enumerable.Range(0, 5).Select(index => $"fixture:wu-{index}")] : ["fixture:modern-owner", .. opponents]));
        }
    }
}

