using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class DamageNaturePreventionChecks
{
    private const string DriverSkill = "fixture:nature-driver";
    private const string PauseSkill = "fixture:nature-pause";
    private const string Mode = "identity:classic-nature-prevention";

    public static void ActualFireAndChainPreventedNormalThunderAndHpLossPreserved()
    {
        var (game, registry) = Start();
        var immune = game.State.Players.First(p => p.Seat != 0 && p.Skills?.Any(skill => skill.ContentId == "mobile:shixin") == true).Seat;
        var plain = game.State.Players.Single(p => p.Seat != 0 && p.Skills?.Any(skill => skill.ContentId == "mobile:shixin") != true).Seat;
        var hp = game.State.Players[immune].Hp;
        Driver(game, "normal", immune);
        Driver(game, "thunder", immune);
        Driver(game, "loss", immune);
        Require(game.State.Players[immune].Hp == hp - 3,
            "Normal damage, Thunder damage and direct HP loss each remain effective against Shixin.");
        Driver(game, "fire", immune);
        Require(game.State.Players[immune].Hp == hp - 3 &&
            Facts<ProgramDamagePreventedEvent>(game).Count(e => e.TargetSeat == immune) == 1,
            "A skill-created Fire damage attempt with no physical card is prevented exactly once.");

        var selfHp = game.State.Players[0].Hp;
        Driver(game, "fire", 0, wait: false);
        Reach(game, p => p.SkillPrompt?.SkillId == PauseSkill);
        var window = game.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Last();
        Require(window.Nature == DamageNature.Fire && window.Candidates.All(c => c.Facts.DamageNature == DamageNature.Fire),
            "The dependency index captures actual nature in every frozen before-damage candidate.");
        game = Cold(game, registry);
        var prompt = Pending(game)!;
        var before = GameCheckpointJson.Serialize(game.CreateCheckpoint());
        Require(!game.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, game.Revision)).Accepted &&
            before == GameCheckpointJson.Serialize(game.CreateCheckpoint()), "The paused damage window rejects another actor atomically.");
        Choose(game, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
        Play(game);
        Require(game.State.Players[0].Hp == selfHp && Facts<ProgramDamagePreventedEvent>(game).Count(e => e.TargetSeat == 0) == 1,
            "The restored native damage parent resumes the locked prevention exactly once.");

        var physical = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([immune]));
        Accept(game, new PlayCardCommand(0, physical.CardId!.Value, physical.TargetSeats, game.Revision, Pending(game)!.PromptId));
        Play(game);
        Require(game.State.Players[immune].Hp == hp - 3 && Facts<ProgramDamagePreventedEvent>(game).Count(e => e.TargetSeat == immune) == 2,
            "A real physical Fire Slash is prevented without an applied-damage event.");
        game = Cold(game, registry);

        var (chain, chainRegistry) = Start();
        var chainedImmuneSeats = chain.State.Players.Where(p => p.Seat != 0 && p.Skills?.Any(skill => skill.ContentId == "mobile:shixin") == true)
            .Select(p => p.Seat).ToArray();
        immune = chainedImmuneSeats[0];
        plain = chain.State.Players.Single(p => p.Seat != 0 && p.Skills?.Any(skill => skill.ContentId == "mobile:shixin") != true).Seat;
        foreach (var seat in chainedImmuneSeats) Driver(chain, "chain", seat);
        Driver(chain, "chain", plain);
        var immuneHp = chainedImmuneSeats.ToDictionary(seat => seat, seat => chain.State.Players[seat].Hp);
        var plainHp = chain.State.Players[plain].Hp;
        var attack = chain.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([plain]));
        Accept(chain, new PlayCardCommand(0, attack.CardId!.Value, attack.TargetSeats, chain.Revision, Pending(chain)!.PromptId));
        Play(chain);
        Require(chain.State.Players[plain].Hp == plainHp - 1 && chainedImmuneSeats.All(seat =>
            chain.State.Players[seat].Hp == immuneHp[seat] &&
            Facts<ProgramDamagePreventedEvent>(chain).Count(e => e.TargetSeat == seat) == 1 &&
            !Facts<DamageAppliedEvent>(chain).Any(e => e.TargetSeat == seat)),
            "A successful physical Fire Slash still propagates, but each chained Shixin target prevents its own actual attempt.");
        Cold(chain, chainRegistry);
    }

    public static void PreparationSlashHasDistanceFreedomAndNormalPlayQuota()
    {
        var (game, registry) = Start(preparation: true, stopAtPreparation: true);
        Require(Pending(game)?.SkillPrompt?.SkillId == "mobile:yingjian", "The actual mobile program offers Yingjian before normal draw.");
        game = Cold(game, registry);
        Choose(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(game, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target"));
        Require(Pending(game)!.Choices.Any(c => c.Targets.SequenceEqual([2])), "Yingjian permits the seat beyond ordinary attack range.");
        var hp = game.State.Players[2].Hp;
        Choose(game, c => c.Targets.SequenceEqual([2]));
        Play(game);
        Require(game.State.Players[2].Hp == hp - 1 &&
            Facts<CardUseFinishedEvent>(game).Any(e => e.CardId == 0 && e.CardKind == CardKind.Slash) &&
            game.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash),
            "Yingjian creates a genuine zero-entity ordinary Slash and does not consume the later Play-phase Slash allowance.");
        Cold(game, registry);

        var (declined, declinedRegistry) = Start(preparation: true, stopAtPreparation: true);
        Choose(declined, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
        Play(declined);
        Require(!Facts<CardUseFinishedEvent>(declined).Any(e => e.CardId == 0), "Declining the optional preparation skill emits no phantom Slash.");
        Cold(declined, declinedRegistry);
    }

    private static (GameEngine, ContentRegistry) Start(bool preparation = false, bool stopAtPreparation = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(preparation));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, "fixture:nature-owner", game.Revision, Pending(game)!.PromptId));
        if (stopAtPreparation) Reach(game, p => p.SkillPrompt?.SkillId == "mobile:yingjian");
        else Play(game);
        return (game, registry);
    }

    private static PendingDecision? Pending(GameEngine game) => game.PendingDecision ??
        Enumerable.Range(0, 4).Select(s => game.CreateSnapshot(s, false).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<T> Facts<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static void Play(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Driver(GameEngine game, string activation, int target, bool wait = true)
    {
        Accept(game, new UseProgramSkillCommand(0, DriverSkill, activation, [], [target], game.Revision, Pending(game)!.PromptId));
        if (wait) Play(game);
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 100; step++)
        {
            if (Pending(game) is { } prompt && predicate(prompt)) return;
            if (Pending(game) is { PlayerSeat: 0 } unexpected)
                throw new InvalidOperationException("Unexpected fixture decision: " + JsonSerializer.Serialize(unexpected));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The damage fixture failed to reach its bounded next prompt.");
    }
    private static void Choose(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var prompt = Pending(game)!;
        var choice = prompt.Choices.FirstOrDefault(predicate) ?? throw new InvalidOperationException("Missing choice: " + JsonSerializer.Serialize(prompt));
        Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, "Command rejected: " + result.Error?.Message);
    }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(game.CreateSnapshot(s, false)) == SnapshotJson.Serialize(restored.CreateSnapshot(s, false))) &&
            game.CardMovements.SequenceEqual(restored.CardMovements) &&
            game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(
                restored.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))),
            "Cold replay preserves every player view, committed event and physical move.");
        Require(game.ObserverFailures.Count == 0 && game.CreateCardZoneDiagnostics().Count == 64 &&
            game.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 64, "Damage preserves all entities and observer boundaries.");
        return restored;
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool preparation) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-damage-nature", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var activations = new[] { "normal", "fire", "thunder", "loss", "chain" }.Select(id => new
            {
                id, minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "anyLiving", usesPerTurn = (int?)null,
                effects = new[] { id == "chain" ? (object)new { op = "setChainedState", target = "selectedTarget", chained = true } :
                    id == "loss" ? new { op = "loseHp", target = "selectedTarget", amount = 1 } :
                    new { op = "damage", target = "selectedTarget", amount = 1, nature = id } }
            }).ToArray();
            var driver = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new[] { new
            {
                id = DriverSkill, revision = 1, activations,
                modifiers = preparation ? Array.Empty<object>() : [new { id = "reach", query = "slashDistanceLimit", operation = "unlimited", value = 0, priority = 0 }]
            } } });
            var programs = SkillProgramCatalog.Load(driver, """{"schemaVersion":3,"skills":{"fixture:nature-driver":{"name":"驱动","description":"真实属性伤害及连环"}}}""");
            builder.AddSkill(new(DriverSkill, "驱动", "真实属性伤害及连环") { Program = programs.Programs[DriverSkill] });
            var pause = SkillProgramCatalog.Load($$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:nature-pause","revision":1,
            "triggers":[{"id":"freeze","window":"beforeDamageApplied","subject":"damageTarget","optional":false,"priority":100,
            "condition":{"kind":"all","children":[{"kind":"damageNatureIs","damageNature":"fire"}]},
            "effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}]}
            """, """{"schemaVersion":3,"skills":{"fixture:nature-pause":{"name":"冻结","description":"冻结真实伤害属性","optionLabels":{"continue":"继续"}}}}""");
            builder.AddSkill(new(PauseSkill, "冻结", "冻结真实伤害属性") { Program = pause.Programs[PauseSkill], ProgramPresentation = pause.Presentations[PauseSkill] });
            builder.AddGeneral(new("fixture:nature-owner", "机制来源", "supporter", DriverSkill, "wu", 8,
                preparation ? ["mobile:yingjian", "mobile:shixin"] : ["mobile:shixin", PauseSkill], GeneralGender.Female));
            for (var i = 1; i < 4; i++)
                builder.AddGeneral(new($"fixture:nature-{i}", "机制目标", "supporter", i == 3 ? "standard:none" : "mobile:shixin", "qun", 8));
            builder.AddDeck(new("fixture:nature-deck", "火杀实体牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard("standard:fire_slash", (Suit)(i % 4), i % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "伤害属性机制", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:nature-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:nature-owner", "fixture:nature-1", "fixture:nature-2", "fixture:nature-3"]));
        }
    }
}
