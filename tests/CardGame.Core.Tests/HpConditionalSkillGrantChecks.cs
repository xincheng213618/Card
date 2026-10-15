using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class HpConditionalSkillGrantChecks
{
    private const string Source = "ol:baobian", Driver = "fixture:hp-grant-driver", Pause = "fixture:hp-grant-pause";
    private const string Owner = "fixture:hp-grant-owner", Mode = "identity:classic-hp-grant-fixture";
    private static readonly string[] Derived = ["classic:tiaoxin", "classic:paoxiao", "classic:shensu"];

    public static void ActualHpThresholdsPhaseQuotaAndColdReplay()
    {
        var (game, registry) = Start();
        Require(game.State.Players[0].Hp == 4 && Derived.All(skill => !Has(game, skill)),
            "A fully healthy Baobian source must not permanently own its three derived skills.");
        var turn = game.State.TurnNumber;
        Drive(game, "damage");
        Require(game.State.Players[0].Hp == 3 && Has(game, Derived[0]) && !Has(game, Derived[1]),
            "Actual damage must grant only Taunt immediately at three HP.");
        game = Cold(game, registry);
        Taunt(game, registry);
        var firstInstance = Grants(game).Single(grant => grant.SkillId == Derived[0] && grant.HpConditionalSource is not null).SkillInstanceId;
        Drive(game, "recover");
        Require(!Has(game, Derived[0]), "Recovery to four HP must withdraw Taunt immediately.");
        Drive(game, "loss");
        Require(Has(game, Derived[0]) && !CanTaunt(game) &&
            Grants(game).Single(grant => grant.SkillId == Derived[0] && grant.HpConditionalSource is not null).SkillInstanceId == firstInstance,
            "Leaving and reentering the HP range must preserve the same source instance and spent Play allowance.");
        game = Cold(game, registry);
        Accept(game, new EndPlayPhaseCommand(0, game.Revision, Pending(game)!.PromptId));
        Play(game);
        Require(game.State.TurnNumber == turn && CanTaunt(game),
            "The actual extra Play and normal Play must each permit one real Taunt in the same turn.");
        Taunt(game, registry);
        Drive(game, "loss");
        Require(game.State.Players[0].Hp == 2 && Has(game, Derived[1]) && !Has(game, Derived[2]),
            "Actual HP loss to two must enable Roar, but not Swift.");
        Drive(game, "independent-roar");
        Drive(game, "loss");
        Require(game.State.Players[0].Hp == 1 && Derived.All(skill => Has(game, skill)),
            "Exactly one HP enables all three skills.");
        game = Cold(game, registry);
        Drive(game, "recover");
        Require(!Has(game, Derived[2]) && Has(game, Derived[1]), "Recovery to two removes Swift.");
        Drive(game, "recover");
        Require(Has(game, Derived[1]) && Grants(game).All(grant => grant.SkillId != Derived[1] || grant.HpConditionalSource is null),
            "Recovery removes only Baobian's Roar grant while retaining an independent real turn grant.");
        Drive(game, "recover");
        Require(!Has(game, Derived[0]) && Has(game, Derived[1]), "Independent grants survive complete HP restoration.");
        Cold(game, registry);
    }

    // Source API lifecycle test; command-only checks above/below establish replay.
    public static void ExactSourceLifetimeStableSharedInstanceAndReadPrivacy()
    {
        var (game, _) = Start();
        Drive(game, "independent-taunt");
        var owner = Players(game)[0];
        var independent = owner.SkillGrants.Grants.Single(grant => grant.SkillId == Derived[0]);
        Drive(game, "loss");
        var conditional = owner.SkillGrants.Grants.Single(grant => grant.SkillId == Derived[0] && grant.HpConditionalSource is not null);
        Require(conditional.SkillInstanceId == independent.SkillInstanceId, "Independent and conditional grants must share one effective skill instance.");
        UseTauntToPlay(game);
        Drive(game, "recover");
        owner.SkillGrants.RemoveGrant(independent.GrantId);
        Synchronize(game);
        Drive(game, "loss");
        Require(!CanTaunt(game) && owner.SkillGrants.Grants.Single(grant => grant.SkillId == Derived[0]).SkillInstanceId == independent.SkillInstanceId,
            "If an independent shared source disappears while HP is out of range, reentry must retain the original instance and spent allowance.");
        var source = owner.SkillGrants.Grants.Single(grant => grant.SkillId == Source);
        var eventCount = game.Events.Count;
        var revision = owner.SkillGrants.Revision;
        owner.SkillGrants.SetEnabled(source.GrantId, false);
        var disabledRevision = owner.SkillGrants.Revision;
        Require(!Has(game, Derived[0]) && game.Events.Count == eventCount && owner.SkillGrants.Revision == disabledRevision,
            "A read must immediately reject a disabled source without mutating grants or producing events.");
        Synchronize(game);
        Require(owner.SkillGrants.Grants.All(grant => grant.HpConditionalSource is null), "Explicit rule progression removes disabled-source grants.");
        owner.SkillGrants.SetEnabled(source.GrantId, true);
        Synchronize(game);
        Require(Has(game, Derived[0]) && !CanTaunt(game), "Reenabling the same source cannot restore spent phase quota.");
        foreach (var hp in new[] { 1, 0, -1, 2, 4 })
        {
            owner.Hp = hp;
            Synchronize(game);
            Require(Has(game, Derived[2]) == (hp == 1), "Swift is strictly HP == 1, including dying and negative HP boundaries.");
            Require(Has(game, Derived[0]) == (hp <= 3) && Has(game, Derived[1]) == (hp <= 2), "The other two HP bounds include zero and negative values.");
        }
        owner.Hp = 1;
        Synchronize(game);
        owner.GeneralRevealed = false;
        var other = game.CreateSnapshot(1);
        Require(other.Players[0].Skills is null && other.Players[0].Hand.Count == 0 && other.PendingDecision is null &&
            !SnapshotJson.Serialize(other).Contains("HpConditionalSource", StringComparison.Ordinal),
            "An unrevealed general's derived relations and private hand choices cannot escape in another player's view.");
        owner.SkillGrants.RemoveGrant(source.GrantId);
        Synchronize(game);
        Require(Derived.All(skill => !Has(game, skill)) && owner.SkillGrants.Grants.All(grant => grant.HpConditionalSource is null),
            "Removing the exact capability removes only its dependent grants and leaves no dangling relation.");
        Require(((System.Collections.IDictionary)typeof(GameEngine).GetField("_hpConditionalSkillInstances", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!).Count == 0,
            "A removed exact source must release its remembered derived-instance relations.");
        Require(revision < owner.SkillGrants.Revision && !JsonSerializer.Serialize(new SkillGrant("g", "standard:none", "i", "s")).Contains("HpConditionalSource"),
            "Older grants omit the additive nullable source field.");
    }

    private static bool Has(GameEngine game, string skill) => game.CreateSnapshot(0, true).Players[0].Skills?.Any(s => s.ContentId == skill) == true;
    private static bool CanTaunt(GameEngine game) => game.GetHumanLegalActions().Any(action => action.ProgramSkillId == Derived[0]);
    private static List<CharacterState> Players(GameEngine game) => (List<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(game)!;
    private static IReadOnlyList<SkillGrant> Grants(GameEngine game) => Players(game)[0].SkillGrants.Grants;
    private static void Synchronize(GameEngine game) => typeof(GameEngine).GetMethod("SynchronizeHpConditionalSkillGrants", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(game, null);
    private static PendingDecision? Pending(GameEngine game) => game.PendingDecision ?? Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Play(GameEngine game) => Reach(game, p => p.PlayerSeat == 0 && p.Kind == DecisionKind.PlayCard);
    private static void Drive(GameEngine game, string activation) { Accept(game, new UseProgramSkillCommand(0, Driver, activation, [], [], game.Revision, Pending(game)!.PromptId)); Play(game); }
    private static void Taunt(GameEngine game, ContentRegistry registry) { UseTauntToPlay(game); Cold(game, registry); }
    private static void UseTauntToPlay(GameEngine game)
    {
        var count = game.State.Players[1].HandCount;
        Accept(game, new UseProgramSkillCommand(0, Derived[0], "taunt", [], [1], game.Revision, Pending(game)!.PromptId));
        for (var step = 0; step < 100; step++)
        {
            if (Pending(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 }) break;
            if (Pending(game) is { PlayerSeat: 0 }) Choose(game, _ => true);
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(Pending(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 } && game.State.Players[1].HandCount == count - 1 && !CanTaunt(game),
            "A real Taunt must finish its declined Slash request, blind discard and one phase debit.");
    }
    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 120; step++)
        {
            if (Pending(game) is { } prompt && predicate(prompt)) return;
            if (Pending(game) is { PlayerSeat: 0 } unexpected) throw new InvalidOperationException("Unexpected HP fixture prompt: " + unexpected.Kind + "; hp=" + game.State.Players[0].Hp);
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded HP fixture did not reach its next prompt.");
    }
    private static void Choose(GameEngine game, Func<PromptChoice, bool> predicate)
    { var prompt = Pending(game)!; Accept(game, new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, prompt.Choices.First(predicate).Id, game.Revision)); }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(command); Require(result.Accepted, result.Error?.Message ?? "Command rejected."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var copy = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(copy.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(copy.ResolutionStack) && game.CardMovements.SequenceEqual(copy.CardMovements) &&
            game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).SequenceEqual(copy.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType()))),
            "Cold command replay must preserve all viewer-safe snapshots, issued frames, events and movements.");
        return copy;
    }
    private static (GameEngine, ContentRegistry) Start(bool swift = false, bool silverLion = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(swift, silverLion));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 17, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(game, new StartGameCommand());
        Reach(game, p => p.Kind == DecisionKind.SelectGeneral && p.PlayerSeat == 0);
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, Pending(game)!.PromptId));
        Play(game);
        return (game, registry);
    }

    private sealed class ContractFixture(string target) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-hp-contract", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:hp-contract","revision":1,"hpSkillGrants":[{"id":"one","skillId":"{{target}}","maximumHp":3}]}]}""", """{"schemaVersion":3,"skills":{"fixture:hp-contract":{"name":"HP","description":"contract"}}}""");
            builder.AddSkill(new("fixture:hp-contract", "HP", "contract") { Program = catalog.Programs["fixture:hp-contract"] });
        }
    }

    private sealed class Fixture(bool swift, bool silverLion) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-hp-grants", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var effects = new Dictionary<string, object>
            {
                ["damage"] = new { op = "damage", target = "owner", amount = 1, nature = "normal" },
                ["loss"] = new { op = "loseHp", target = "owner", amount = 1 },
                ["recover"] = new { op = "recover", target = "owner", amount = 1 },
                ["independent-roar"] = new { op = "grantTurnSkills", target = "owner", skillIds = new[] { Derived[1] }, expiry = "actualTurnEnd" },
                ["independent-taunt"] = new { op = "grantTurnSkills", target = "owner", skillIds = new[] { Derived[0] }, expiry = "actualTurnEnd" }
            };
            var triggers = new List<object>
            {
                new { id = "extra-play", window = "turnStartBeforeNormalFlow", subject = "owner", optional = false,
                    priority = 100, effects = new[] { new { op = "insertPhase", target = "owner", phase = "play", phaseContinuation = "beforeNormalPreparation" } } }
            };
            // The lord's initial one HP gains one from identity mode. Resolve
            // actual loss before the turn-start trigger candidates are frozen.
            if (swift) triggers.Add(new { id = "opening-loss", window = "gameStarting", subject = "owner", optional = false,
                effects = new[] { new { op = "loseHp", target = "owner", amount = 1 } } });
            var rules = JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = new[] { new
            {
                id = Driver, revision = 1, activations = effects.Select(pair => new { id = pair.Key, minCards = 0, maxCards = 0,
                    minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null, effects = new[] { pair.Value } }).ToArray(),
                triggers
            } } });
            var catalog = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:hp-grant-driver":{"name":"体力与来源驱动","description":"真实HP与独立授予"}}}""");
            builder.AddSkill(new(Driver, "驱动", "真实命令") { Program = catalog.Programs[Driver] });
            var pause = SkillProgramCatalog.Load($$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Pause}}","revision":1,"triggers":[{"id":"recovery","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"pause","options":[{"id":"continue"}]}]}]}]}""", """{"schemaVersion":3,"skills":{"fixture:hp-grant-pause":{"name":"回血暂停","description":"真实银狮成本子链","optionLabels":{"continue":"继续"}}}}""");
            builder.AddSkill(new(Pause, "回血暂停", "真实银狮成本子链") { Program = pause.Programs[Pause], ProgramPresentation = pause.Presentations[Pause] });
            builder.AddGeneral(new(Owner, "豹变机制", "supporter", Source, "shu", 3,
                swift ? [Driver, Pause] : [Driver]) { InitialHp = swift ? 1 : null });
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:hp-grant-{seat}", "固定目标", "supporter", "standard:none", "wei", 8));
            builder.AddDeck(new("fixture:hp-grant-deck", "固定装备牌堆", 8, 2,
                [new(silverLion ? "classic:silver-lion" : "standard:crossbow", 96)]));
            builder.AddMode(new(Mode, "豹变阈值", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 },
                "fixture:hp-grant-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: [Owner, "fixture:hp-grant-1", "fixture:hp-grant-2", "fixture:hp-grant-3"]));
        }
    }
}
