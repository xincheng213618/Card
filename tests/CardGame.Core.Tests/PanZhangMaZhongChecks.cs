using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PanZhangMaZhongChecks
{
    private const string General = "classic:pan-zhang-ma-zhong";
    private const string Duodao = "classic:duodao";
    private const string Anjian = "classic:anjian";
    private const string ArmorSkill = "fixture:take-source-armor";
    private const string Mode = "identity:classic-pan-zhang-ma-zhong-check-5";


    public static void NaturalSlashReverseRangeAndReplay()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 100; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var weapon = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Equip && action.CardId is { } id &&
                game.CreateCardZoneDiagnostics().Any(zone => zone.CardId == id &&
                    zone.CardKind == CardKind.QinggangSword));
            if (weapon is null) continue;
            Play(game, weapon);
            ReachPlay(game);
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat == 2);
            if (slash is null) continue;
            var paused = GameReplay.Restore(GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Play(game, slash);
            Play(paused, paused.GetHumanLegalActions().Single(action =>
                action.Kind == LegalActionKind.Slash && action.CardId == slash.CardId &&
                action.TargetSeat == 2));
            Finish(game);
            Finish(paused);
            var modified = game.Events.Select(item => item.Payload)
                .OfType<ProgramCardDamageModifiedEvent>()
                .Where(item => item.Source.SkillId == Anjian && item.TargetSeat == 2).ToArray();
            Require(modified.Length == 1 && modified[0].ModifiedAmount == 2 &&
                    game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                        .Any(item => item.SourceSeat == 0 && item.TargetSeat == 2 && item.Amount == 2) &&
                    SnapshotJson.Serialize(game.CreateSnapshot(0, true)) ==
                    SnapshotJson.Serialize(paused.CreateSnapshot(0, true)) &&
                    Events(game).SequenceEqual(Events(paused)),
                $"A natural far Slash must gain one damage once and replay from a pre-attack checkpoint. " +
                $"modifier={modified.Length}, amount={modified.FirstOrDefault()?.ModifiedAmount}, " +
                $"weapon={weapon.CardId}, statesEqual={SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(paused.CreateSnapshot(0, true))}");
            return;
        }
        throw new InvalidOperationException("No seeded opening could equip Qinggang Sword and Slash seat two.");
    }





    private static ContentRegistry Registry(bool targetsHaveDuodao = true, bool armorSynthetic = false,
        bool noWeapons = false, bool tianxiang = false) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario(targetsHaveDuodao, armorSynthetic, noWeapons, tianxiang));

    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Pan Zhang skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, int seed, string generalId = General)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Pan Zhang fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, generalId, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Pan Zhang selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Pan Zhang fixture did not reach Play.");
    }

    private static void Finish(GameEngine game)
    {
        for (var step = 0; step < 90 && game.ResolutionStack.Count > 0; step++)
        {
            if (game.PendingDecision is { } prompt && prompt.Kind == DecisionKind.ProgramTrigger)
            {
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("program-action") == "activate") ?? prompt.Choices.First();
                var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                    choice.Id, game.Revision));
                Require(result.Accepted, result.Error?.Message ?? "Could not answer damage skill.");
            }
            else Advance(game);
        }
        Require(game.ResolutionStack.Count == 0, "A Slash resolution remained active.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Pan Zhang fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Pan Zhang card action failed.");
    }

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();
    private static void Reject(string rules, string presentation)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected invalid schema-56 composition to be rejected.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(bool targetsHaveDuodao, bool armorSynthetic, bool noWeapons,
        bool tianxiang) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("pan-zhang-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 141, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            if (armorSynthetic)
            {
                var catalog = SkillProgramCatalog.Load("""
                    {"schemaVersion":62,"skills":[{"id":"fixture:take-source-armor","revision":1,
                    "minimumRulesVersion": 171,"triggers":[{"id":"take","window":"afterDamageApplied",
                    "subject":"owner","damageOccurrence":"perDamage","damageCardKinds":["slash"],
                    "optional":false,"effects":[{"op":"selectSourceCard","target":"owner",
                    "zones":["equipment"],"equipmentSlots":["armor"],"skipIfNoCards":true,
                    "resultBind":"armor"},{"op":"moveBoundCards","target":"owner",
                    "sourceBind":"armor","destination":"ownerHand"}]}]}]}
                    """, """
                    {"schemaVersion":3,"skills":{"fixture:take-source-armor":{"name":"取甲","description":"测试"}}}
                    """);
                builder.AddSkill(new ContentSkillDefinition(ArmorSkill, "取甲", "测试")
                { Program = catalog.Programs[ArmorSkill] });
            }
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:pan-zhang-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "测试目标", "supporter",
                    armorSynthetic ? ArmorSkill : tianxiang ? Anjian :
                    targetsHaveDuodao ? Duodao : "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard((index % (noWeapons ? 2 : 3)) switch
                {
                    0 => "standard:slash",
                    1 when noWeapons => "standard:bagua",
                    1 => "standard:qinggang_sword",
                    _ => "standard:bagua"
                }, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:pan-zhang-deck", "潘璋马忠测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "潘璋马忠测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = tianxiang ? 0 : 1,
                    [nameof(Role.Rebel)] = tianxiang ? 4 : 2,
                    [nameof(Role.Renegade)] = tianxiang ? 0 : 1
                }, "fixture:pan-zhang-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [tianxiang ? "classic:xiao-qiao" : General, .. targets]));
        }
    }
}
