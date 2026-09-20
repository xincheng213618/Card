using CardGame.Content.Standard;
using CardGame.Core;

internal static class SpGuanYuChecks
{
    private const string GeneralId = "sp:guan-yu";
    private const string WushengSkillId = "sp:guan-yu-wusheng";
    private const string DanjiSkillId = "sp:danji";
    private const string MashuSkillId = "sp:guan-yu-mashu";
    private const string NuzhanSkillId = "sp:nuzhan";

    public static void ContentAndDanjiReplayBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 68, 0));
        Require(current.Generals[GeneralId] is
        {
            FactionId: "wei",
            BaseHp: 4,
            SkillIds: var skillIds
        } && skillIds.SequenceEqual([WushengSkillId, DanjiSkillId]),
            "The current package must register the formal Wei SP Guan Yu skill order.");
        Require(current.Skills[DanjiSkillId] is
        {
            LegacyKind: null,
            Tags: SkillTag.Awakening | SkillTag.Locked | SkillTag.Limited,
            ExecutionForms: SkillExecutionForm.Trigger
        } &&
            current.Skills[MashuSkillId] is
            {
                LegacyKind: SkillKind.Mashu,
                Tags: SkillTag.Locked,
                ExecutionForms: SkillExecutionForm.State
            } &&
            current.Skills[NuzhanSkillId] is
            {
                LegacyKind: null,
                Tags: SkillTag.Locked,
                ExecutionForms: SkillExecutionForm.State
            } &&
            !previous.Generals.ContainsKey(GeneralId) &&
            !previous.Skills.ContainsKey(DanjiSkillId),
            "Package 1.69.0 must add structured Danji, Mashu and Nuzhan without changing 1.68.0.");

        var registry = CreateRegistry();
        var game = CreateGame(registry);
        ReachHumanPlay(game);
        var owner = game.CreateSnapshot(0, revealAll: true).Players[0];
        Require(owner.GeneralId == GeneralId && owner.MaxHp == 4 && owner.Hp == 4 &&
                owner.Skills!.Select(skill => skill.ContentId).SequenceEqual(
                    [WushengSkillId, DanjiSkillId, MashuSkillId, NuzhanSkillId]),
            "Danji must reduce maximum HP once and append its two acquired skills in stable order.");
        var runtimeStates = owner.SkillRuntimeStates!;
        var danjiState = runtimeStates.Single(state => state.SkillId == DanjiSkillId);
        Require(!danjiState.IsAcquired && danjiState.Usages.Single() is
        { UsageId: "awakening", Scope: SkillUsageScope.Game, Count: 1 } &&
                runtimeStates.Single(state => state.SkillId == MashuSkillId).IsAcquired &&
                runtimeStates.Single(state => state.SkillId == NuzhanSkillId).IsAcquired,
            "The public runtime projection must distinguish the consumed awakening from acquired skills.");
        Require(game.Events.Select(item => item.Payload).OfType<MaximumHpChangedEvent>().Single() is
        { PlayerSeat: 0, Delta: -1, MaximumHp: 4, SkillId: DanjiSkillId } &&
                game.Events.Select(item => item.Payload).OfType<SkillsAcquiredEvent>().Single().SkillIds
                    .SequenceEqual([MashuSkillId, NuzhanSkillId]) &&
                game.Events.Select(item => item.Payload).OfType<SkillAwakenedEvent>().Single() is
                { PlayerSeat: 0, SkillId: DanjiSkillId, MaximumHp: 4 },
            "Danji must publish typed maximum-HP, acquisition and awakening events.");
        var diamondSlash = owner.Hand.First(card =>
            card.Kind == CardKind.Slash && card.Suit == Suit.Diamond);
        Require(game.GetHumanLegalActions().Any(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardId == diamondSlash.Id &&
                action.TargetSeat == 2),
            "SP Wusheng must remove the distance limit from a diamond Slash without removing its use count.");

        var checkpoint = RoundTrip(game.CreateCheckpoint());
        var restored = GameReplay.Restore(checkpoint, registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "Danji acquisition and its game-scoped record must restore from the command prefix.");

        var legacy = GameReplay.Restore(checkpoint with { RulesVersion = 97 }, registry);
        var legacyOwner = legacy.CreateSnapshot(0, revealAll: true).Players[0];
        Require(legacyOwner.MaxHp == 5 &&
                legacyOwner.Skills!.Select(skill => skill.ContentId)
                    .SequenceEqual([WushengSkillId, DanjiSkillId]) &&
                legacyOwner.SkillRuntimeStates is null,
            "Rules v97 must retain the pre-acquisition behavior for the same accepted setup commands.");
    }

    public static void NuzhanUsesExactConversionSource()
    {
        var registry = CreateRegistry();
        var game = CreateGame(registry);
        ReachHumanPlay(game);

        var initial = game.CreateSnapshot(0, revealAll: true);
        var owner = initial.Players[0];
        var equipmentCard = owner.Hand.First(card => EquipmentCatalog.IsEquipment(card.Kind));
        var equipmentAction = game.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == equipmentCard.Id &&
            action.ConversionSource?.SkillId == WushengSkillId);
        var targetSeat = equipmentAction.TargetSeat!.Value;
        var targetHp = initial.Players.Single(player => player.Seat == targetSeat).Hp;
        Play(game, equipmentAction);
        ReachHumanPlay(game, generalAlreadySelected: true);

        var afterEquipment = game.CreateSnapshot(0, revealAll: true);
        Require(afterEquipment.Players.Single(player => player.Seat == targetSeat).Hp == targetHp - 2,
            "Nuzhan must add one damage to a Slash converted from an equipment card.");
        Require(game.Events.Select(item => item.Payload).OfType<NuzhanAppliedEvent>().Single(item =>
                item.PhysicalCardId == equipmentCard.Id) is
        { IgnoredSlashLimit: false, DamageBonus: 1 },
            "The equipment branch must publish its typed Nuzhan modifier.");

        var trickCard = afterEquipment.Players[0].Hand.First(card =>
            CardCatalog.Get(card.Kind).CategoryName == "锦囊牌");
        var trickAction = game.GetHumanLegalActions().FirstOrDefault(action =>
            action.Kind == LegalActionKind.Slash &&
            action.CardId == trickCard.Id &&
            action.TargetSeat == targetSeat &&
            action.ConversionSource?.SkillId == WushengSkillId);
        Require(trickAction is not null,
            "Nuzhan must still publish a trick-converted Slash after the ordinary Slash allowance is spent.");
        Play(game, trickAction!);
        ReachHumanPlay(game, generalAlreadySelected: true);

        var final = game.CreateSnapshot(0, revealAll: true);
        Require(final.Players.Single(player => player.Seat == targetSeat).Hp == targetHp - 3,
            "The trick-converted Slash must deal ordinary damage while ignoring only the use count.");
        var trickModifier = game.Events.Select(item => item.Payload).OfType<NuzhanAppliedEvent>()
            .Single(item => item.PhysicalCardId == trickCard.Id);
        var accepted = game.Events.Select(item => item.Payload).OfType<CardActionAcceptedEvent>()
            .Single(item => item.Action.PhysicalCards.Any(card => card.CardId == trickCard.Id));
        Require(trickModifier is { IgnoredSlashLimit: true, DamageBonus: 0 } &&
                accepted.Action.ConversionChain.Single() is
                { SkillId: WushengSkillId, OwnerSeat: 0 },
            "Nuzhan must key its count exception to the exact SP Wusheng conversion source.");

        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)) &&
                restored.Events.Select(item => item.Payload.GetType().Name)
                    .SequenceEqual(game.Events.Select(item => item.Payload.GetType().Name)),
            "Both Nuzhan branches must replay exactly from accepted commands.");
    }

    private static GameEngine CreateGame(ContentRegistry registry) =>
        GameEngine.CreateStandard(new GameOptions
        {
            Seed = 17,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = 0,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 40
        }, registry);

    private static void ReachHumanPlay(GameEngine game, bool generalAlreadySelected = false)
    {
        if (!generalAlreadySelected)
            Require(game.Submit(new StartGameCommand()).Accepted,
                "The SP Guan Yu fixture failed to start.");
        for (var step = 0; step < 2_048; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) return;
            if (game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } selection)
            {
                Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
                    $"The SP Guan Yu candidate list omitted {GeneralId}: {string.Join(", ", selection.ValidContentIds)}.");
                var selected = game.Submit(new SelectGeneralCommand(
                    0,
                    GeneralId,
                    game.Revision,
                    selection.PromptId));
                Require(selected.Accepted,
                    selected.Error?.Message ?? "The SP Guan Yu fixture could not select its formal general.");
                continue;
            }
            Require(game.PendingDecision?.PlayerSeat != 0,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching SP Guan Yu's play phase.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The SP Guan Yu fixture could not advance to the human play phase.");
        }
        throw new InvalidOperationException("The SP Guan Yu fixture did not reach play in bounded steps.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(
            0,
            action.CardId!.Value,
            action.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            action.PlayedCardKind)
        {
            ConversionSource = action.ConversionSource
        });
        Require(result.Accepted, result.Error?.Message ?? "SP Guan Yu's Slash was rejected.");
    }

    private static ContentRegistry CreateRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-sp-guan-yu-test-4";
        private const string DeckId = "fixture:sp-guan-yu-deck";
        private static readonly string[] BlankGeneralIds =
            ["fixture:blank-guan-yu-1", "fixture:blank-guan-yu-2", "fixture:blank-guan-yu-3"];

        public PackageManifest Manifest { get; } = new(
            "sp-guan-yu-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 69, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var (id, index) in BlankGeneralIds.Select((id, index) => (id, index)))
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    $"怒斩目标{index + 1}",
                    "supporter",
                    "standard:none",
                    "qun",
                    BaseHp: 4));
            }

            var cards = new[]
            {
                (Id: "standard:draw_two", Suit: Suit.Heart),
                (Id: "standard:qinggang_sword", Suit: Suit.Heart),
                (Id: "standard:peach", Suit: Suit.Heart),
                (Id: "standard:slash", Suit: Suit.Diamond),
                (Id: "standard:draw_two", Suit: Suit.Diamond)
            };
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "SP关羽觉醒与怒斩测试牌堆",
                InitialHandSize: 8,
                DrawPerTurn: 0,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 64)
                    .Select(index => cards[index % cards.Length])
                    .Select((card, index) => new ContentDeckPhysicalCard(
                        card.Id,
                        card.Suit,
                        index % 13 + 1))
                    .ToArray()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "SP关羽觉醒与怒斩测试",
                4,
                4,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));
        }
    }
}
