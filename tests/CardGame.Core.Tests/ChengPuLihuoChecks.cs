using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class ChengPuLihuoChecks
{
    private const int HumanSeat = 0;
    private const string OwnerGeneralId = "fixture:lihuo-owner";
    private const string LihuoSkillId = "classic:lihuo";

    public static void ContentAndRulesBoundary()
    {
        Require(GameCheckpoint.CurrentRulesVersion >= 113,
            "Formal Lihuo must have an explicit rules-version boundary.");
        var current = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 91, 0));
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 90, 0));
        var skill = current.Skills[LihuoSkillId];

        Require(skill.LegacyKind == SkillKind.Lihuo &&
                skill.Tags == SkillTag.None &&
                skill.ActionForms == SkillActionForm.None &&
                skill.ExecutionForms == SkillExecutionForm.State,
            "Lihuo must be a continuous state rule, not a standalone active button or trigger prompt.");
        Require(!previous.Skills.ContainsKey(LihuoSkillId) &&
                current.ContentHash != previous.ContentHash,
            "Package 1.91.0 must add Lihuo without mutating package 1.90.0.");
        Require((current.Modes["identity:classic-5"].GeneralPoolIds ?? []).SequenceEqual(
                    previous.Modes["identity:classic-5"].GeneralPoolIds ?? []),
            "The partial Lihuo runtime must not publish an incomplete Cheng Pu general in the current pool.");

        var legacy = CreateGame(ScenarioPackage.SlashModeId, seed: 1, rulesVersion: 112);
        ReachHumanPlay(legacy);
        Require(legacy.GetHumanLegalActions().All(action =>
                action.CardKindModifierSkill != SkillKind.Lihuo &&
                action.TargetCountModifierSkill != SkillKind.Lihuo),
            "Rules v112 must not publish Lihuo modifiers even when package 1.91.0 is loaded.");
    }

    public static void ConvertedFireSlashAddsTargetAndLosesHpOnce()
    {
        var game = CreateGame(ScenarioPackage.SlashModeId, seed: 1);
        ReachHumanPlay(game);
        var slash = Player(game, HumanSeat).Hand.First(card => card.Kind == CardKind.Slash);
        var actions = game.GetHumanLegalActions().Where(action =>
            action.Kind == LegalActionKind.Slash && action.CardId == slash.Id).ToArray();
        var ordinary = actions.FirstOrDefault(action =>
            action.PlayedCardKind is null &&
            action.CardKindModifierSkill is null &&
            action.TargetCountModifierSkill is null);
        var convertedSingle = actions.FirstOrDefault(action =>
            action.PlayedCardKind == CardKind.FireSlash &&
            action.CardKindModifierSkill == SkillKind.Lihuo &&
            action.TargetCountModifierSkill is null);
        var convertedMulti = actions.FirstOrDefault(action =>
            action.PlayedCardKind == CardKind.FireSlash &&
            action.CardKindModifierSkill == SkillKind.Lihuo &&
            action.TargetCountModifierSkill == SkillKind.Lihuo &&
            action.TargetSeats.Count == 2);
        Require(ordinary is not null && convertedSingle is not null && convertedMulti is not null,
            "Lihuo must preserve ordinary Slash while publishing separate converted one/two-target Fire Slash actions.");
        ArgumentNullException.ThrowIfNull(convertedSingle);
        ArgumentNullException.ThrowIfNull(convertedMulti);

        var before = game.CreateCheckpoint();
        var missingCardKindModifier = game.Submit(new PlayCardCommand(
            HumanSeat,
            convertedSingle.CardId!.Value,
            convertedSingle.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            convertedSingle.PlayedCardKind));
        Require(!missingCardKindModifier.Accepted && State(game) == State(GameReplay.Restore(before, Registry())),
            "A forged Fire Slash command without its Lihuo card-kind modifier must be rejected atomically.");

        var missingTargetModifier = game.Submit(new PlayCardCommand(
            HumanSeat,
            convertedMulti.CardId!.Value,
            convertedMulti.TargetSeats,
            game.Revision,
            game.PendingDecision!.PromptId,
            convertedMulti.PlayedCardKind)
        {
            CardKindModifierSkill = SkillKind.Lihuo
        });
        Require(!missingTargetModifier.Accepted && State(game) == State(GameReplay.Restore(before, Registry())),
            "A forged multi-target Lihuo command without its target modifier must be rejected atomically.");

        var ownerHpBefore = Player(game, HumanSeat).Hp;
        var eventStart = game.Events.Count;
        var used = Play(game, convertedMulti);
        Require(used.Accepted, used.Error?.Message ?? "The exact converted Lihuo Fire Slash was rejected.");
        var events = game.Events.Skip(eventStart).Select(item => item.Payload).ToArray();
        var lihuo = events.OfType<LihuoSlashUsedEvent>().Single();
        var hpLoss = events.OfType<SkillHpLostEvent>().Single(item => item.Skill == SkillKind.Lihuo);
        var finishedIndex = Array.FindIndex(events, item => item is CardUseFinishedEvent);
        var hpLossIndex = Array.FindIndex(events, item => item is SkillHpLostEvent lost && lost.Skill == SkillKind.Lihuo);
        Require(lihuo.ConvertedFromOrdinarySlash && lihuo.AddedTarget &&
                lihuo.TargetSeats.SequenceEqual(convertedMulti.TargetSeats) &&
                events.OfType<CardUsedEvent>().Count() == 1 &&
                events.OfType<DamageAppliedEvent>().Count() >= 2 &&
                Player(game, HumanSeat).Hp == ownerHpBefore - 1 &&
                hpLoss.Amount == 1 &&
                finishedIndex >= 0 && hpLossIndex > finishedIndex,
            "One converted multi-target Fire Slash must resolve every target, finish once, then lose exactly one HP.");

        var replay = GameReplay.Restore(RoundTrip(game.CreateCheckpoint()), Registry());
        Require(State(replay) == State(game) && Events(replay).SequenceEqual(Events(game)),
            "A completed Lihuo multi-target use must replay exactly.");
    }

    public static void NativeAndZhuqueFireSlashDoNotPayConversionPenalty()
    {
        var native = CreateGame(ScenarioPackage.FireModeId, seed: 1);
        ReachHumanPlay(native);
        var nativeFire = native.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash &&
            action.TargetSeats.Count == 2 &&
            action.TargetCountModifierSkill == SkillKind.Lihuo &&
            action.CardKindModifierSkill is null);
        var nativeHp = Player(native, HumanSeat).Hp;
        var nativeStart = native.Events.Count;
        Require(Play(native, nativeFire).Accepted, "The native Fire Slash Lihuo target extension was rejected.");
        var nativeEvents = native.Events.Skip(nativeStart).Select(item => item.Payload).ToArray();
        Require(nativeEvents.OfType<LihuoSlashUsedEvent>().Single() is
                    { ConvertedFromOrdinarySlash: false, AddedTarget: true } &&
                nativeEvents.OfType<DamageAppliedEvent>().Count() >= 2 &&
                nativeEvents.All(item => item is not SkillHpLostEvent { Skill: SkillKind.Lihuo }) &&
                Player(native, HumanSeat).Hp == nativeHp,
            "A native Fire Slash may add one Lihuo target but must not pay the conversion penalty.");

        var zhuque = FindZhuqueGame();
        var weapon = Player(zhuque, HumanSeat).Hand.First(card => card.Kind == CardKind.ZhuqueFan);
        Require(zhuque.Submit(new PlayCardCommand(
            HumanSeat,
            weapon.Id,
            [],
            zhuque.Revision,
            zhuque.PendingDecision!.PromptId)).Accepted,
            "The Lihuo fixture could not equip Zhuque Fan.");
        var zhuqueAction = zhuque.GetHumanLegalActions().First(action =>
            action.Kind == LegalActionKind.Slash &&
            action.PlayedCardKind == CardKind.FireSlash &&
            action.CardKindModifierSkill is null &&
            action.TargetCountModifierSkill == SkillKind.Lihuo &&
            action.TargetSeats.Count == 2);
        var zhuqueHp = Player(zhuque, HumanSeat).Hp;
        var zhuqueStart = zhuque.Events.Count;
        Require(Play(zhuque, zhuqueAction).Accepted,
            "The Zhuque-converted Fire Slash with a Lihuo extra target was rejected.");
        var zhuqueEvents = zhuque.Events.Skip(zhuqueStart).Select(item => item.Payload).ToArray();
        Require(zhuqueEvents.OfType<ZhuqueFanConvertedEvent>().Count() == 1 &&
                zhuqueEvents.OfType<LihuoSlashUsedEvent>().Single() is
                    { ConvertedFromOrdinarySlash: false, AddedTarget: true } &&
                zhuqueEvents.All(item => item is not SkillHpLostEvent { Skill: SkillKind.Lihuo }) &&
                Player(zhuque, HumanSeat).Hp == zhuqueHp,
            "Zhuque Fan supplies the Fire conversion, so Lihuo's extra target must not cause HP loss.");
    }

    public static void FullyDodgedConversionDoesNotLoseHp()
    {
        GameEngine? selected = null;
        LegalAction? selectedAction = null;
        for (var seed = 1; seed <= 512 && selected is null; seed++)
        {
            var candidate = CreateGame(ScenarioPackage.DodgeModeId, seed);
            ReachHumanPlay(candidate);
            var revealed = candidate.CreateSnapshot(HumanSeat, revealAll: true);
            selectedAction = candidate.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash &&
                action.CardKindModifierSkill == SkillKind.Lihuo &&
                action.TargetCountModifierSkill is null &&
                action.TargetSeats.Count == 1 &&
                revealed.Players[action.TargetSeats[0]].Role == Role.Rebel &&
                revealed.Players[action.TargetSeats[0]].Hand.Any(card => card.Kind == CardKind.Dodge));
            if (selectedAction is not null) selected = candidate;
        }
        Require(selected is not null && selectedAction is not null,
            "No bounded hostile Dodge fixture exposed a converted Lihuo Slash.");
        ArgumentNullException.ThrowIfNull(selected);
        ArgumentNullException.ThrowIfNull(selectedAction);

        var ownerHp = Player(selected, HumanSeat).Hp;
        var start = selected.Events.Count;
        Require(Play(selected, selectedAction).Accepted, "The Dodge-boundary Lihuo Slash was rejected.");
        var events = selected.Events.Skip(start).Select(item => item.Payload).ToArray();
        Require(events.OfType<CardRespondedEvent>().Any(item => item.EffectiveCardKind == CardKind.Dodge) &&
                events.All(item => item is not DamageAppliedEvent) &&
                events.All(item => item is not SkillHpLostEvent { Skill: SkillKind.Lihuo }) &&
                Player(selected, HumanSeat).Hp == ownerHp,
            "A converted Fire Slash canceled by Dodge must not make the Lihuo owner lose HP.");
    }

    private static GameEngine FindZhuqueGame()
    {
        for (var seed = 1; seed <= 512; seed++)
        {
            var game = CreateGame(ScenarioPackage.ZhuqueModeId, seed);
            ReachHumanPlay(game);
            var hand = Player(game, HumanSeat).Hand;
            if (hand.Any(card => card.Kind == CardKind.ZhuqueFan) &&
                hand.Any(card => card.Kind == CardKind.Slash))
            {
                return game;
            }
        }
        throw new InvalidOperationException("No bounded Lihuo/Zhuque fixture dealt both required cards.");
    }

    private static CommandResult Play(GameEngine game, LegalAction action) => game.Submit(new PlayCardCommand(
        HumanSeat,
        action.CardId!.Value,
        action.TargetSeats,
        game.Revision,
        game.PendingDecision!.PromptId,
        action.PlayedCardKind,
        action.TargetCardId)
    {
        ConversionSource = action.ConversionSource,
        CardKindModifierSkill = action.CardKindModifierSkill,
        TargetCountModifierSkill = action.TargetCountModifierSkill
    });

    private static GameEngine CreateGame(string modeId, int seed, int rulesVersion = GameCheckpoint.CurrentRulesVersion)
    {
        var registry = Registry();
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = HumanSeat,
            HumanRole = Role.Lord,
            UseInteractiveSetup = true,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = true,
            AiPolicyVersion = 2,
            MaxTurns = 20
        }, registry);
        if (rulesVersion != GameCheckpoint.CurrentRulesVersion)
        {
            game = GameReplay.Restore(game.CreateCheckpoint() with { RulesVersion = rulesVersion }, registry);
        }
        Require(game.Submit(new StartGameCommand()).Accepted, "The Lihuo fixture failed to start.");
        var prompt = game.PendingDecision ?? throw new InvalidOperationException("The Lihuo fixture has no setup prompt.");
        Require(prompt.Kind == DecisionKind.SelectGeneral && prompt.ValidContentIds.Contains(OwnerGeneralId),
            "The Lihuo fixture did not offer its owner general.");
        var selected = game.Submit(new SelectGeneralCommand(
            HumanSeat,
            OwnerGeneralId,
            game.Revision,
            prompt.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "The Lihuo fixture could not select its owner.");
        return game;
    }

    private static void ReachHumanPlay(GameEngine game)
    {
        for (var step = 0; step < 512; step++)
        {
            if (game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: HumanSeat }) return;
            Require(game.PendingDecision?.PlayerSeat != HumanSeat,
                $"Unexpected human prompt {game.PendingDecision?.Kind} while reaching Lihuo play.");
            var advanced = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(advanced.Accepted, advanced.Error?.Message ?? "The Lihuo fixture could not advance.");
        }
        throw new InvalidOperationException("The Lihuo fixture did not reach human Play.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new ScenarioPackage());

    private static PlayerSnapshot Player(GameEngine game, int seat) =>
        game.CreateSnapshot(HumanSeat, revealAll: true).Players.Single(player => player.Seat == seat);

    private static GameCheckpoint RoundTrip(GameCheckpoint checkpoint) =>
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(checkpoint));

    private static string State(GameEngine game) =>
        SnapshotJson.Serialize(game.CreateSnapshot(HumanSeat, revealAll: true));

    private static IReadOnlyList<string> Events(GameEngine game) => game.Events
        .Select(item => $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class ScenarioPackage : IGameContentPackage
    {
        public const string SlashModeId = "identity:classic-lihuo-slash-test-4";
        public const string FireModeId = "identity:classic-lihuo-fire-test-4";
        public const string ZhuqueModeId = "identity:classic-lihuo-zhuque-test-4";
        public const string DodgeModeId = "identity:classic-lihuo-dodge-test-4";
        private const string SlashDeckId = "fixture:lihuo-slash-deck";
        private const string FireDeckId = "fixture:lihuo-fire-deck";
        private const string ZhuqueDeckId = "fixture:lihuo-zhuque-deck";
        private const string DodgeDeckId = "fixture:lihuo-dodge-deck";

        public PackageManifest Manifest { get; } = new(
            "lihuo-scenario",
            new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 91, 0))]);

        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                OwnerGeneralId,
                "疠火测试",
                "lihuo_fixture",
                LihuoSkillId,
                "wu",
                BaseHp: 4));
            AddDeck(builder, SlashDeckId, "疠火普通杀牌堆", [new("standard:slash", 48)]);
            AddDeck(builder, FireDeckId, "疠火火杀牌堆", [new("standard:fire_slash", 48)]);
            AddDeck(builder, ZhuqueDeckId, "疠火朱雀牌堆",
                [new("classic:zhuque-fan", 16), new("standard:slash", 32)]);
            AddDeck(builder, DodgeDeckId, "疠火闪避牌堆",
                [new("standard:slash", 8), new("standard:dodge", 56)]);
            AddMode(builder, SlashModeId, SlashDeckId);
            AddMode(builder, FireModeId, FireDeckId);
            AddMode(builder, ZhuqueModeId, ZhuqueDeckId);
            AddMode(builder, DodgeModeId, DodgeDeckId);
        }

        private static void AddDeck(
            IContentRegistryBuilder builder,
            string id,
            string name,
            IReadOnlyList<ContentDeckCardCount> cards) =>
            builder.AddDeck(new ContentDeckRecipe(id, name, InitialHandSize: 4, DrawPerTurn: 0, cards));

        private static void AddMode(IContentRegistryBuilder builder, string modeId, string deckId) =>
            builder.AddMode(new ContentModeDefinition(
                modeId,
                "四人经典身份（疠火场景）",
                MinPlayers: 4,
                MaxPlayers: 4,
                RoleCounts: new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 1,
                    [nameof(Role.Renegade)] = 1
                },
                DeckId: deckId,
                GeneralCandidateCount: 4,
                GeneralPoolIds:
                [
                    OwnerGeneralId,
                    "classic:sun-quan",
                    "classic:huang-gai",
                    "classic:gan-ning"
                ]));
    }
}
