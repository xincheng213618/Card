using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ZhongHuiChecks
{
    private const int HumanSeat = 0;
    private const string GeneralId = "classic:zhong-hui";
    private const string QuanjiSkillId = "classic:quanji";
    private const string ZiliSkillId = "classic:zili";
    private const string PaiyiSkillId = "classic:paiyi";

    public static void ContentQuanjiAndBoundary()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 108,
            "Formal Zhong Hui must have an explicit rules-version boundary.");
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 86, 0));
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 85, 0));
        Require(current.Packages.Any(package =>
                package.Id == "standard-classic-generals" &&
                package.Version == new Version(1, 86, 0)),
            "The current registry must load classic-general package 1.86.0.");
        var general = current.Generals[GeneralId];
        Require(general.FactionId == "wei" && general.BaseHp == 4 &&
                general.Gender == GeneralGender.Male && general.PortraitKey == "zhong_hui" &&
                general.SkillIds.SequenceEqual([QuanjiSkillId, ZiliSkillId]),
            $"Classic Zhong Hui metadata drifted: faction={general.FactionId}, hp={general.BaseHp}, " +
            $"gender={general.Gender}, portrait={general.PortraitKey}, skills={string.Join(',', general.SkillIds)}.");
        var quanji = current.Skills[QuanjiSkillId];
        Require(quanji.LegacyKind == SkillKind.Quanji && quanji.Tags == SkillTag.None &&
                quanji.ExecutionForms == (SkillExecutionForm.State | SkillExecutionForm.Trigger) &&
                quanji.ActionForms == SkillActionForm.None,
            $"Quanji metadata drifted: kind={quanji.LegacyKind}, tags={quanji.Tags}, " +
            $"execution={quanji.ExecutionForms}, actions={quanji.ActionForms}.");
        var zili = current.Skills[ZiliSkillId];
        Require(zili.LegacyKind == SkillKind.Zili &&
                zili.Tags == (SkillTag.Awakening | SkillTag.Locked | SkillTag.Limited) &&
                zili.ExecutionForms == SkillExecutionForm.Trigger && zili.ActionForms == SkillActionForm.None,
            $"Zili metadata drifted: kind={zili.LegacyKind}, tags={zili.Tags}, " +
            $"execution={zili.ExecutionForms}, actions={zili.ActionForms}.");
        var paiyi = current.Skills[PaiyiSkillId];
        Require(paiyi.LegacyKind == SkillKind.Paiyi && paiyi.Tags == SkillTag.None &&
                paiyi.ActionForms == SkillActionForm.Active,
            $"Paiyi metadata drifted: kind={paiyi.LegacyKind}, tags={paiyi.Tags}, actions={paiyi.ActionForms}.");
        Require(!previous.Generals.ContainsKey(GeneralId) &&
                !previous.Skills.ContainsKey(QuanjiSkillId) &&
                !previous.Skills.ContainsKey(ZiliSkillId) &&
                !previous.Skills.ContainsKey(PaiyiSkillId) &&
                current.ContentHash != previous.ContentHash,
            "Package 1.86.0 must add Zhong Hui without mutating the 1.85.0 registry boundary.");

        var fixture = FindFixture();
        var registry = CreateRegistry();
        var game = CreateFixtureGame(registry, fixture.Seed);
        ResolveSelfFireAttack(game, expectQuanji: true, testForgery: true);
        var first = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        Require(first.AuthorityCount == 1 && first.AuthorityCards?.Count == 1 &&
                game.Events.Select(item => item.Payload).OfType<QuanjiResolvedEvent>().Last() is
                {
                    OwnerSeat: HumanSeat,
                    DamagePoint: 1,
                    Used: true,
                    DrawnCardId: not null,
                    AuthorityCardId: not null,
                    AuthorityCount: 1
                },
            "One point of damage must offer one Quanji draw-and-store result in the public Authority zone.");

        ResolveSelfFireAttack(game, expectQuanji: true);
        ResolveSelfFireAttack(game, expectQuanji: true);
        var snapshot = game.CreateSnapshot(HumanSeat, revealAll: true);
        Require(snapshot.Players[HumanSeat] is
                {
                    AuthorityCount: 3,
                    AuthorityCards.Count: 3
                } owner &&
                GetHandLimit(game, HumanSeat) == owner.Hp + 3 &&
                game.Events.Select(item => item.Payload).OfType<QuanjiResolvedEvent>().Count(item => item.Used) == 3,
            "Three independent damage points must create three public Authorities and add exactly three to hand limit.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(snapshot),
            "The completed three-Authority command prefix must replay exactly.");

        var legacy = CreateGame(registry, fixture.Seed, rulesVersion: 107);
        StartAndSelect(legacy);
        ResolveSelfFireAttack(legacy, expectQuanji: false);
        Require(legacy.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].AuthorityCount == 0 &&
                legacy.Events.Select(item => item.Payload).All(item => item is not QuanjiResolvedEvent),
            "Rules v107 must retain ordinary damage even when package 1.86.0 is loaded.");
    }

    public static void ZiliAndPaiyiReplay()
    {
        var fixture = FindFixture();
        var registry = CreateRegistry();
        var game = CreateFixtureGame(registry, fixture.Seed);
        ResolveSelfFireAttack(game, expectQuanji: true);
        ResolveSelfFireAttack(game, expectQuanji: true);
        ResolveSelfFireAttack(game, expectQuanji: true);
        var beforeAwakening = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];

        var play = RequirePrompt(game, DecisionKind.PlayCard);
        Require(game.Submit(new EndPlayPhaseCommand(HumanSeat, game.Revision, play.PromptId)).Accepted,
            "The Zhong Hui fixture could not end its play phase.");
        ReachPrompt(game, DecisionKind.Zili, 2_048);
        var zili = RequirePrompt(game, DecisionKind.Zili);
        Require(zili.IsPrivate &&
                zili.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["zili-draw", "zili-recover"]) &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].MaxHp ==
                    beforeAwakening.MaxHp,
            "Zili must awaken once at the next preparation phase and publish its benefit choice before losing maximum HP.");

        var pausedReplay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(pausedReplay.PendingDecision is { Kind: DecisionKind.Zili, PlayerSeat: HumanSeat, IsPrivate: true } &&
                SnapshotJson.Serialize(pausedReplay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)),
            "A paused Zili awakening choice must replay exactly.");

        var drawBranch = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Answer(drawBranch, DecisionKind.Zili, "zili-draw");
        var drawResult = drawBranch.Events.Select(item => item.Payload).OfType<ZiliResolvedEvent>().Last();
        Require(!drawResult.Recovered && drawResult.DrawnCardIds.Count == 2 &&
                drawResult.MaximumHp == beforeAwakening.MaxHp - 1 &&
                drawResult.AcquiredSkillIds.SequenceEqual([PaiyiSkillId]),
            "Zili's draw branch must draw exactly two before losing one maximum HP and acquiring Paiyi.");

        Answer(game, DecisionKind.Zili, "zili-recover");
        ReachPrompt(game, DecisionKind.PlayCard, 512);
        var awakened = game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat];
        Require(awakened.Skills?.Any(skill => skill.ContentId == PaiyiSkillId &&
                    skill.Kind == SkillKind.Paiyi && skill.ActionForms == SkillActionForm.Active) == true &&
                awakened.SkillRuntimeStates?.Single(state => state.SkillId == PaiyiSkillId).IsAcquired == true &&
                game.Events.Select(item => item.Payload).OfType<ZiliResolvedEvent>().Last() is
                {
                    OwnerSeat: HumanSeat,
                    Recovered: true,
                    DrawnCardIds.Count: 0,
                    MaximumHp: var recoveredMaximumHp,
                    AcquiredSkillIds.Count: 1
                } && recoveredMaximumHp == beforeAwakening.MaxHp - 1,
            "Zili's recovery branch must recover one before losing one maximum HP and expose dynamically acquired active Paiyi.");

        var beforePaiyi = RoundTrip(game.CreateCheckpoint());
        var selfBranch = GameReplay.Restore(beforePaiyi, registry);
        UsePaiyi(selfBranch, HumanSeat);
        var selfResult = selfBranch.Events.Select(item => item.Payload).OfType<PaiyiResolvedEvent>().Last();
        Require(selfResult is
                {
                    SourceSeat: HumanSeat,
                    TargetSeat: HumanSeat,
                    DrawnCardIds.Count: 2,
                    DamageTriggered: false
                } &&
                selfBranch.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].AuthorityCount == 2,
            "Paiyi may target its owner, draws two, removes one Authority and cannot damage the same player.");

        var targetSeat = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Where(player => player.IsAlive && player.Seat != HumanSeat)
            .OrderByDescending(player => player.HandCount)
            .First().Seat;
        var targetHp = game.CreateSnapshot(HumanSeat, revealAll: true).Players[targetSeat].Hp;
        UsePaiyi(game, targetSeat);
        var result = game.Events.Select(item => item.Payload).OfType<PaiyiResolvedEvent>().Last();
        Require(result is
                {
                    SourceSeat: HumanSeat,
                    TargetSeat: var resolvedTarget,
                    DrawnCardIds.Count: 2,
                    DamageTriggered: true
                } && resolvedTarget == targetSeat &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[targetSeat].Hp == targetHp - 1 &&
                game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].AuthorityCount == 2 &&
                game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Any(item =>
                    item.SourceSeat == HumanSeat && item.TargetSeat == targetSeat && item.Amount == 1),
            "Paiyi must draw first, compare current hands, then route its conditional damage through the normal damage chain.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), registry);
        Require(SnapshotJson.Serialize(replay.CreateSnapshot(HumanSeat, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)) &&
                replay.Events.Select(item => item.Payload).OfType<PaiyiResolvedEvent>().Last().DamageTriggered,
            "The completed Zili-to-Paiyi chain must replay exactly.");
    }

    private static void ResolveSelfFireAttack(GameEngine game, bool expectQuanji, bool testForgery = false)
    {
        var entryPrompt = game.PendingDecision;
        var entryHand = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat).Hand;
        var entryActions = game.GetHumanLegalActions();
        ReachPrompt(game, DecisionKind.PlayCard, 512);
        var legalActions = game.GetHumanLegalActions();
        var action = legalActions.FirstOrDefault(candidate =>
            candidate.Kind == LegalActionKind.FireAttack && candidate.TargetSeat == HumanSeat) ??
            throw new InvalidOperationException(
                "The Zhong Hui fixture has no self Fire Attack action; " +
                $"entryPrompt={entryPrompt?.Kind}/{entryPrompt?.PlayerSeat}, " +
                $"entryHand=[{string.Join(',', entryHand.Select(card => $"{card.Id}:{card.Kind}"))}], " +
                $"entryActions=[{string.Join(',', entryActions.Select(candidate => $"{candidate.Kind}:{candidate.CardId}:{candidate.TargetSeat}"))}], " +
                $"hand=[{string.Join(',', game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].Hand.Select(card => $"{card.Id}:{card.Kind}"))}], " +
                $"actions=[{string.Join(',', legalActions.Select(candidate => $"{candidate.Kind}:{candidate.CardId}:{candidate.TargetSeat}"))}], " +
                $"authorities=[{string.Join(',', game.CreateSnapshot(HumanSeat, revealAll: true).Players[HumanSeat].AuthorityCards?.Select(card => $"{card.Id}:{card.Kind}") ?? [])}], " +
                $"fireEvents={game.Events.Select(item => item.Payload).OfType<FireAttackResolvedEvent>().Count()}, " +
                $"quanjiEvents={game.Events.Select(item => item.Payload).OfType<QuanjiResolvedEvent>().Count()}, " +
                $"movements=[{string.Join(',', game.CardMovements.TakeLast(20).Select(move => $"{move.CardId}:{move.From}>{move.To}:{move.Reason}"))}], " +
                $"rules={game.CreateCheckpoint().RulesVersion}.");
        var play = RequirePrompt(game, DecisionKind.PlayCard);
        var played = game.Submit(new PlayCardCommand(
            HumanSeat,
            action.CardId!.Value,
            [HumanSeat],
            game.Revision,
            play.PromptId));
        Require(played.Accepted, played.Error?.Message ?? "The self Fire Attack was rejected.");
        AnswerCard(game, DecisionKind.FireAttackReveal, CardKind.Dodge);
        AnswerCard(game, DecisionKind.FireAttackDiscard, CardKind.Dodge);

        if (!expectQuanji)
        {
            ReachPrompt(game, DecisionKind.PlayCard, 512);
            return;
        }

        var quanji = RequirePrompt(game, DecisionKind.Quanji);
        Require(quanji.IsPrivate &&
                quanji.Choices.Select(choice => choice.Parameters.GetValueOrDefault("action"))
                    .Order(StringComparer.Ordinal)
                    .SequenceEqual(["quanji-skip", "quanji-use"]),
            "Each damage point must publish one private Quanji use/skip choice.");
        if (testForgery)
        {
            var before = SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));
            var revision = game.Revision;
            var forged = game.Submit(new AnswerPromptCommand(
                HumanSeat,
                quanji.PromptId,
                new ChoiceId("quanji.forged"),
                revision));
            Require(!forged.Accepted && game.Revision == revision &&
                    SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true)) == before,
                "A forged Quanji answer must be rejected atomically.");
        }

        Answer(game, DecisionKind.Quanji, "quanji-use");
        var store = RequirePrompt(game, DecisionKind.Quanji);
        var dodgeIds = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat).Hand
            .Where(card => card.Kind == CardKind.Dodge)
            .Select(card => card.Id)
            .ToHashSet();
        var cardChoice = store.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "quanji-store" &&
            choice.Cards.Count == 1 && dodgeIds.Contains(choice.Cards[0]));
        Answer(game, cardChoice);
        ReachPrompt(game, DecisionKind.PlayCard, 512);
    }

    private static void UsePaiyi(GameEngine game, int targetSeat)
    {
        ReachPrompt(game, DecisionKind.PlayCard, 512);
        var action = game.GetHumanLegalActions().Single(candidate =>
            candidate.Kind == LegalActionKind.UseSkill && candidate.Skill == SkillKind.Paiyi);
        Require(action.SelectableCardIds.Count > 0 && action.SelectableTargetSeats.Contains(targetSeat),
            "Paiyi did not publish its Authority card or requested living target.");
        var prompt = RequirePrompt(game, DecisionKind.PlayCard);
        var result = game.Submit(new UseSkillCommand(
            HumanSeat,
            SkillKind.Paiyi,
            [action.SelectableCardIds[0]],
            [targetSeat],
            game.Revision,
            prompt.PromptId));
        Require(result.Accepted, result.Error?.Message ?? "Paiyi was rejected.");
        ReachPrompt(game, DecisionKind.PlayCard, 512);
    }

    private static void AnswerCard(GameEngine game, DecisionKind kind, CardKind cardKind)
    {
        var prompt = RequirePrompt(game, kind);
        var matchingIds = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat).Hand
            .Where(card => card.Kind == cardKind)
            .Select(card => card.Id)
            .ToHashSet();
        var choice = prompt.Choices.First(candidate =>
            candidate.Cards.Count == 1 && matchingIds.Contains(candidate.Cards[0]));
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, DecisionKind kind, string action)
    {
        var prompt = RequirePrompt(game, kind);
        var choice = prompt.Choices.Single(candidate =>
            candidate.Parameters.GetValueOrDefault("action") == action);
        Answer(game, choice);
    }

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("There is no Zhong Hui prompt.");
        var result = game.Submit(new AnswerPromptCommand(
            HumanSeat,
            prompt.PromptId,
            choice.Id,
            game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "The Zhong Hui prompt answer was rejected.");
    }

    private static void ReachPrompt(GameEngine game, DecisionKind kind, int limit)
    {
        for (var step = 0; step < limit; step++)
        {
            if (game.PendingDecision is { PlayerSeat: HumanSeat } prompt)
            {
                if (prompt.Kind == kind) return;
                throw new InvalidOperationException($"Unexpected human prompt {prompt.Kind} before {kind}.");
            }
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? $"Could not advance to {kind}.");
        }
        throw new InvalidOperationException($"The fixture did not reach {kind} in bounded steps.");
    }

    private static PendingDecision RequirePrompt(GameEngine game, DecisionKind kind) =>
        game.PendingDecision is { PlayerSeat: HumanSeat } prompt && prompt.Kind == kind
            ? prompt
            : throw new InvalidOperationException(
                $"Expected human {kind}, found {game.PendingDecision?.Kind.ToString() ?? "no prompt"}.");

    private static int GetHandLimit(GameEngine game, int seat)
    {
        var playersField = typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine player store was not found.");
        var players = (System.Collections.IList)playersField.GetValue(game)!;
        var method = typeof(GameEngine).GetMethod("GetHandLimit", BindingFlags.NonPublic | BindingFlags.Instance) ??
            throw new InvalidOperationException("The engine hand-limit query was not found.");
        return (int)method.Invoke(game, [players[seat]])!;
    }

    private static Fixture FindFixture()
    {
        var registry = CreateRegistry();
        for (var seed = 1; seed <= 4_096; seed++)
        {
            var game = CreateGame(registry, seed);
            StartAndSelect(game);
            var human = game.CreateSnapshot(HumanSeat, revealAll: true).Players
                .Single(player => player.Seat == HumanSeat);
            var fireAttacks = human.Hand.Count(card => card.Kind == CardKind.FireAttack);
            var selfFireAttacks = game.GetHumanLegalActions().Count(action =>
                action.Kind == LegalActionKind.FireAttack && action.TargetSeat == HumanSeat);
            if (fireAttacks == 3 && selfFireAttacks == 3)
            {
                return new Fixture(seed);
            }
        }
        throw new InvalidOperationException("No bounded Zhong Hui fixture dealt all three Fire Attacks to the human seat.");
    }

    private static void StartAndSelect(GameEngine game)
    {
        Require(game.Submit(new StartGameCommand()).Accepted, "The Zhong Hui fixture failed to start.");
        var selection = RequirePrompt(game, DecisionKind.SelectGeneral);
        Require(selection.ValidContentIds.Contains(GeneralId, StringComparer.Ordinal),
            "The Zhong Hui fixture omitted its formal general.");
        Require(game.Submit(new SelectGeneralCommand(
                HumanSeat,
                GeneralId,
                game.Revision,
                selection.PromptId)).Accepted,
            "The Zhong Hui fixture could not select its formal general.");
        ReachPrompt(game, DecisionKind.PlayCard, 512);
    }

    private static GameEngine CreateFixtureGame(ContentRegistry registry, int seed)
    {
        var game = CreateGame(registry, seed);
        StartAndSelect(game);
        var before = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat);
        var actions = game.GetHumanLegalActions();
        var after = game.CreateSnapshot(HumanSeat, revealAll: true).Players
            .Single(player => player.Seat == HumanSeat);
        Require(before.Hand.Count(card => card.Kind == CardKind.FireAttack) == 3 &&
                actions.Count(action =>
                    action.Kind == LegalActionKind.FireAttack && action.TargetSeat == HumanSeat) == 3 &&
                after.Hand.Count(card => card.Kind == CardKind.FireAttack) == 3,
            $"Seed {seed} did not reproduce the bounded Zhong Hui deal; " +
            $"before=[{string.Join(',', before.Hand.Select(card => $"{card.Id}:{card.Kind}"))}], " +
            $"after=[{string.Join(',', after.Hand.Select(card => $"{card.Id}:{card.Kind}"))}], " +
            $"movements=[{string.Join(',', game.CardMovements.TakeLast(16).Select(move => $"{move.CardId}:{move.From}>{move.To}:{move.Reason}"))}].");
        return game;
    }

    private static GameEngine CreateGame(
        ContentRegistry registry,
        int seed,
        int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = ScenarioPackage.ModeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
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

    private sealed record Fixture(int Seed);

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:classic-zhong-hui-test-4";
        private const string DeckId = "fixture:zhong-hui-deck";
        private static readonly string[] BlankGeneralIds =
        [
            "fixture:zhong-hui-a",
            "fixture:zhong-hui-b",
            "fixture:zhong-hui-c"
        ];

        public PackageManifest Manifest { get; } = new(
            "zhong-hui-test",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 86, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in BlankGeneralIds)
            {
                builder.AddGeneral(new ContentGeneralDefinition(
                    id,
                    "排异测试目标",
                    "supporter",
                    "standard:none",
                    "shu",
                    BaseHp: 8));
            }

            var physicalCards = new List<ContentDeckPhysicalCard>();
            for (var index = 0; index < 3; index++)
            {
                physicalCards.Add(new ContentDeckPhysicalCard("standard:fire_attack", Suit.Spade, index + 1));
            }
            for (var index = 3; index < 160; index++)
            {
                physicalCards.Add(new ContentDeckPhysicalCard("standard:dodge", Suit.Spade, index % 13 + 1));
            }
            builder.AddDeck(new ContentDeckRecipe(
                DeckId,
                "钟会权计自立排异测试牌堆",
                InitialHandSize: 12,
                DrawPerTurn: 2,
                Cards: [])
            {
                PhysicalCards = physicalCards.AsReadOnly()
            });
            builder.AddMode(new ContentModeDefinition(
                ModeId,
                "钟会技能链测试",
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
