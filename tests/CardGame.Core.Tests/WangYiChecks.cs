using CardGame.Content.Standard;
using CardGame.Core;

internal static class WangYiChecks
{
    private const int HumanSeat = 1;
    private const string GeneralId = "classic:wang-yi";
    private const string ZhenlieSkillId = "classic:zhenlie";
    private const string MijiSkillId = "classic:miji";

    public static void ContentPromptAndRulesBoundary()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 85, 0));
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 84, 0));
        Require(current.Packages.Any(package =>
                    package.Id == "standard-classic-generals" &&
                    package.Version == new Version(1, 85, 0)) &&
                current.Generals[GeneralId] is
                {
                    FactionId: "wei",
                    BaseHp: 3,
                    Gender: GeneralGender.Female,
                    PortraitKey: "wang_yi",
                    SkillIds: var skillIds
                } && skillIds.SequenceEqual([ZhenlieSkillId, MijiSkillId]) &&
                current.Skills[ZhenlieSkillId] is
                {
                    LegacyKind: SkillKind.Zhenlie,
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                current.Skills[MijiSkillId] is
                {
                    LegacyKind: SkillKind.Miji,
                    Tags: SkillTag.None,
                    ExecutionForms: SkillExecutionForm.Trigger,
                    ActionForms: SkillActionForm.None
                } &&
                !previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey(ZhenlieSkillId) &&
                !previous.Skills.ContainsKey(MijiSkillId) &&
                current.ContentHash != previous.ContentHash,
            "Package 1.85.0 must add exact Wang Yi content without mutating 1.84.0.");

        var fixture = FindFixture(ScenarioPackage.SlashModeId);
        var game = fixture.Game;
        var prompt = RequirePrompt(game, DecisionKind.Zhenlie);
        Require(prompt.IsPrivate &&
                prompt.PlayerSeat == HumanSeat &&
                prompt.SourceSeat is >= 0 &&
                prompt.IncomingCard == CardKind.Slash &&
                prompt.Choices.Count == 2 &&
                prompt.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["zhenlie-skip", "zhenlie-use"]),
            "Zhenlie must publish one private use/skip choice after another player's Slash targets Wang Yi.");

        var before = SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));
        var restored = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(HumanSeat, revealAll: true)) == before &&
                restored.PendingDecision is
                {
                    Kind: DecisionKind.Zhenlie,
                    PlayerSeat: HumanSeat,
                    IsPrivate: true,
                    IncomingCard: CardKind.Slash
                },
            "A paused Zhenlie activation choice must replay exactly.");

        var revision = game.Revision;
        var forged = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            new ChoiceId("zhenlie.forged"),
            game.Revision));
        Require(!forged.Accepted && game.Revision == revision &&
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)) == before,
            "A forged Zhenlie answer must be rejected atomically.");

    }

    public static void ZhenlieSlashAndMijiDistributionReplay()
    {
        var fixture = FindFixture(ScenarioPackage.SlashModeId);
        var game = fixture.Game;
        var activation = RequirePrompt(game, DecisionKind.Zhenlie);
        var sourceSeat = activation.SourceSeat ??
            throw new InvalidOperationException("The Zhenlie fixture did not identify the Slash source.");
        var sourceHandBeforeDiscard = game.CreateSnapshot(HumanSeat, revealAll: true)
            .Players[sourceSeat].HandCount;

        Answer(game, DecisionKind.Zhenlie, "zhenlie-use");
        var discard = RequirePrompt(game, DecisionKind.Zhenlie);
        var pausedAfterCost = game.CreateSnapshot(HumanSeat, revealAll: true);
        Require(pausedAfterCost.Players[HumanSeat].Hp == 2 &&
                game.ResolutionStack.OfType<CardUseFrame>().Single().IneffectiveTargetSeats?.SequenceEqual([HumanSeat]) == true &&
                discard.IsPrivate &&
                discard.TargetSeat == sourceSeat &&
                discard.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "zhenlie-discard-hand" &&
                    choice.Cards.Count == 0) &&
                game.Events.Select(item => item.Payload).OfType<SkillHpLostEvent>().Any(item =>
                    item is { SourceSeat: HumanSeat, Skill: SkillKind.Zhenlie, Amount: 1, RemainingHp: 2 }),
            "Using Zhenlie must pay HP first, mark only Wang Yi ineffective and keep source hand identities opaque.");

        var pausedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(pausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(pausedAfterCost) &&
                pausedReplay.PendingDecision is
                {
                    Kind: DecisionKind.Zhenlie,
                    PlayerSeat: HumanSeat,
                    IsPrivate: true
                } replayDiscard &&
                replayDiscard.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "zhenlie-discard-hand" &&
                    choice.Cards.Count == 0),
            "A paused post-cost Zhenlie discard must replay without exposing source hand card ids.");

        Answer(game, DecisionKind.Zhenlie, "zhenlie-discard-hand");
        var resolved = game.Events.Select(item => item.Payload).OfType<ZhenlieResolvedEvent>().Last();
        Require(resolved is
                {
                    OwnerSeat: HumanSeat,
                    SourceSeat: var resolvedSource,
                    CardKind: CardKind.Slash,
                    Used: true,
                    RemainingHp: 2,
                    DiscardedCardId: not null,
                    DiscardedFromZone: CardZoneKind.Hand
                } && resolvedSource == sourceSeat &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat].HandCount ==
                    sourceHandBeforeDiscard - 1 &&
                game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Any(item =>
                    item.TargetSeat == HumanSeat &&
                    item.CardKind == CardKind.Slash &&
                    item.Reason == CardEffectSkipReason.SkillNullified) &&
                game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                    .All(item => item.TargetSeat != HumanSeat),
            $"Zhenlie must discard one source card and finish the ineffective Slash without damage " +
            $"(resolved={resolved}, sourceHand={sourceHandBeforeDiscard}->" +
            $"{game.CreateSnapshot(HumanSeat, revealAll: true).Players[sourceSeat].HandCount}, " +
            $"skips={game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Count()}, " +
            $"damage=[{string.Join(',', game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Select(item => item.TargetSeat))}]).");

        var resolvedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(resolvedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A completed Zhenlie Slash branch must replay exactly.");

        ReachHumanPlay(game, skipAdditionalZhenlie: true);
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        Require(game.Submit(new EndPlayPhaseCommand(
                HumanSeat,
                game.Revision,
                play.PromptId)).Accepted,
            "The Wang Yi fixture could not end its play phase.");
        ReachMiji(game);
        var miji = RequirePrompt(game, DecisionKind.Miji);
        Require(miji.IsPrivate &&
                miji.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["miji-skip", "miji-use"]),
            "An injured Wang Yi must receive one private Miji use/skip choice at the end phase.");

        var mijiPausedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(mijiPausedReplay.PendingDecision is
                { Kind: DecisionKind.Miji, PlayerSeat: HumanSeat, IsPrivate: true } &&
                SnapshotJson.Serialize(mijiPausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Miji activation choice must replay exactly.");

        Answer(game, DecisionKind.Miji, "miji-use");
        var gift = RequirePrompt(game, DecisionKind.Miji);
        Require(gift.IsPrivate &&
                gift.ValidCardIds.Count > 0 &&
                gift.ValidTargetSeats.All(targetSeat => targetSeat != HumanSeat) &&
                gift.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "miji-skip-gift") &&
                gift.Choices.Any(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "miji-give" &&
                    choice.Cards.Count == 1 &&
                    choice.Targets.Count == 1),
            "After drawing one card, Miji must allow either no distribution or an exact hand-card recipient pair.");

        var optionalBranch = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Answer(optionalBranch, DecisionKind.Miji, "miji-skip-gift");
        Require(optionalBranch.Events.Select(item => item.Payload).OfType<MijiResolvedEvent>().Last() is
                {
                    OwnerSeat: HumanSeat,
                    Used: true,
                    LostHp: 1,
                    DrawnCardIds.Count: 1,
                    GivenCardIds.Count: 0,
                    TargetSeats.Count: 0
                },
            "Miji distribution must remain wholly optional after the draw.");

        var selectedGift = gift.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "miji-give");
        Answer(game, selectedGift);
        var mijiResolved = game.Events.Select(item => item.Payload).OfType<MijiResolvedEvent>().Last();
        Require(mijiResolved is
                {
                    OwnerSeat: HumanSeat,
                    Used: true,
                    LostHp: 1,
                    DrawnCardIds.Count: 1,
                    GivenCardIds.Count: 1,
                    TargetSeats.Count: 1
                } &&
                mijiResolved.TargetSeats[0] == selectedGift.Targets[0] &&
                mijiResolved.GivenCardIds[0] == selectedGift.Cards[0],
            "Once Miji distribution starts, it must give the exact drawn count to another living character.");

        var completedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), fixture.Registry);
        Require(SnapshotJson.Serialize(completedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "The completed Zhenlie and Miji command prefix must replay exactly.");
    }

    public static void ZhenlieNullifiesOnlyItsGroupEffect()
    {
        var fixture = FindFixture(ScenarioPackage.ArrowModeId);
        var game = fixture.Game;
        var prompt = RequirePrompt(game, DecisionKind.Zhenlie);
        Require(prompt.IncomingCard == CardKind.ArrowBarrage,
            "The group-effect fixture must pause on Arrow Barrage.");
        var resolutionId = game.ResolutionStack.OfType<CardUseFrame>().Single().Id;

        Answer(game, DecisionKind.Zhenlie, "zhenlie-use");
        Answer(game, DecisionKind.Zhenlie, "zhenlie-discard-hand");
        AdvanceUntilCardUseFinished(game, resolutionId);

        var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().ToArray();
        Require(game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Any(item =>
                    item.TargetSeat == HumanSeat &&
                    item.CardKind == CardKind.ArrowBarrage &&
                    item.Reason == CardEffectSkipReason.SkillNullified) &&
                damage.All(item => item.TargetSeat != HumanSeat) &&
                damage.Any(item => item.TargetSeat is 2 or 3),
            $"Zhenlie must skip only Wang Yi while the same group trick continues for other targets " +
            $"(skips=[{string.Join(';', game.Events.Select(item => item.Payload).OfType<CardEffectSkippedEvent>().Select(item => $"{item.TargetSeat}:{item.CardKind}:{item.Reason}"))}], " +
            $"damage=[{string.Join(';', damage.Select(item => $"{item.SourceSeat}>{item.TargetSeat}:{item.Amount}"))}]).");
    }

    private static void AdvanceUntilCardUseFinished(GameEngine game, long resolutionId)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.Events.Select(item => item.Payload).OfType<CardUseFinishedEvent>()
                .Any(item => item.ResolutionId == resolutionId))
            {
                return;
            }
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while resolving the group trick.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Wang Yi fixture could not finish the active group trick.");
        }
        throw new InvalidOperationException("The active group trick did not finish in bounded steps.");
    }

    private static Fixture FindFixture(string modeId)
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 2_048; seed++)
        {
            var game = CreateGame(registry, seed, modeId);
            if (!TryStartAndSelect(game)) continue;
            if (game.PendingDecision is not
                { Kind: DecisionKind.Zhenlie, PlayerSeat: HumanSeat, SourceSeat: var sourceSeat })
            {
                continue;
            }

            return new Fixture(game, registry, seed);
        }
        throw new InvalidOperationException($"No bounded Wang Yi fixture reached Zhenlie in mode {modeId}.");
    }

    private static bool TryStartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted,
            "The Wang Yi fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        if (!selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal)) return false;
        Require(game.Submit(new SelectGeneralCommand(
                HumanSeat,
                GeneralId,
                game.Revision,
                selection.PromptId)).Accepted,
            "The Wang Yi fixture could not select its formal general.");

        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is
                { PlayerSeat: HumanSeat, Kind: DecisionKind.Zhenlie or DecisionKind.PlayCard })
            {
                return true;
            }
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Wang Yi was targeted.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Wang Yi fixture could not reach its first target or play boundary.");
        }
        throw new InvalidOperationException("The Wang Yi fixture did not reach Zhenlie or play in bounded steps.");
    }

    private static void ReachHumanPlay(GameEngine game, bool skipAdditionalZhenlie)
    {
        for (var step = 0; step < 1_024; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            if (game.PendingDecision is { Kind: DecisionKind.Zhenlie, PlayerSeat: HumanSeat })
            {
                Require(skipAdditionalZhenlie,
                    "The fixture unexpectedly exposed an additional Zhenlie prompt.");
                Answer(game, DecisionKind.Zhenlie, "zhenlie-skip");
                continue;
            }
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while returning to play.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Wang Yi fixture could not advance to human play.");
        }
        throw new InvalidOperationException("The Wang Yi fixture did not reach human play in bounded steps.");
    }

    private static void ReachMiji(GameEngine game)
    {
        for (var step = 0; step < 256; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.Miji, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} before Miji.");
            Require(game.Submit(new AdvanceOneStepCommand(game.Revision)).Accepted,
                "The Wang Yi fixture could not advance to Miji.");
        }
        throw new InvalidOperationException("The Wang Yi fixture did not reach Miji in bounded steps.");
    }

    private static void Answer(GameEngine game, DecisionKind kind, string action)
    {
        var prompt = RequirePrompt(game, kind);
        var choice = prompt.Choices.First(candidate =>
            candidate.Parameters.GetValueOrDefault("action") == action);
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ??
            throw new InvalidOperationException("There is no Wang Yi prompt to answer.");
        var result = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Wang Yi prompt answer was rejected.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static GameEngine CreateGame(
        ContentRegistry registry,
        int seed,
        string modeId,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Rebel,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = modeId == ScenarioPackage.ArrowModeId ? 1 : 2,
            MaxTurns = 40
        }, registry);
        return rulesVersion == GameCheckpoint.CurrentRulesVersion
            ? game
            : GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
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

    private sealed record Fixture(GameEngine Game, ContentRegistry Registry, int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string SlashModeId = "identity:classic-wang-yi-slash-test-4";
        public const string ArrowModeId = "identity:classic-wang-yi-arrow-test-4";
        private const string SlashDeckId = "fixture:wang-yi-slash-deck";
        private const string ArrowDeckId = "fixture:wang-yi-arrow-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:wang-yi-lord",
            "fixture:wang-yi-supporter-a",
            "fixture:wang-yi-supporter-b"
        ];

        public PackageManifest Manifest { get; } = new(
            "wang-yi-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 85, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "贞烈测试目标",
                    "supporter",
                    "standard:none",
                    "shu",
                    BaseHp: 8));
            }

            builder.AddDeck(new ContentDeckRecipe(
                SlashDeckId,
                "王异贞烈杀测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 160)
                    .Select(index => Physical("standard:slash", index))
                    .ToArray()
            });

            builder.AddDeck(new ContentDeckRecipe(
                ArrowDeckId,
                "王异贞烈万箭测试牌堆",
                InitialHandSize: 4,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = Enumerable.Range(0, 160)
                    .Select(index => Physical("standard:arrow_barrage", index))
                    .ToArray()
            });

            AddMode(SlashModeId, SlashDeckId, "王异贞烈杀测试");
            AddMode(ArrowModeId, ArrowDeckId, "王异贞烈万箭测试");
            return;

            void AddMode(string modeId, string deckId, string displayName) =>
                builder.AddMode(new ContentModeDefinition(
                    modeId,
                    displayName,
                    4,
                    4,
                    new Dictionary<string, int>
                    {
                        [nameof(Role.Lord)] = 1,
                        [nameof(Role.Loyalist)] = 1,
                        [nameof(Role.Rebel)] = 1,
                        [nameof(Role.Renegade)] = 1
                    },
                    deckId,
                    GeneralCandidateCount: 4,
                    GeneralPoolIds: [GeneralId, .. BlankGeneralIds]));

            static ContentDeckPhysicalCard Physical(string cardId, int index) =>
                new(cardId, (Suit)(index % 4), index % 13 + 1);
        }
    }
}
