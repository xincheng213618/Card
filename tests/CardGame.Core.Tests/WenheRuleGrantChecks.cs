using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class WenheRuleGrantChecks
{
    private const string Driver = "fixture:wenhe-grant-driver", Quiet = "fixture:wenhe-grant-quiet",
        Plain = "fixture:wenhe-grant-plain", Curse = "fixture:wenhe-grant-curse",
        Owner = "fixture:wenhe-grant-owner", Mode = "fixture:wenhe-grant-mode";
    private static readonly string[] ModeSkills = ["wenhe:rules", "wenhe:chaos-resolution", "wenhe:champion-mark", "wenhe:niepan"];

    public static void ModeInstancesSurviveTemplateReplacementWithoutProtectingOrdinarySameName()
    {
        const string id = "wenhe:champion-mark";
        var normal = new SkillGrant("ordinary", id, "ordinary-instance", "acquired:fixture");
        using (var json = JsonDocument.Parse(JsonSerializer.Serialize(normal)))
            Need(json.RootElement.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal)
                .SetEquals(new[] { "GrantId", "SkillId", "SkillInstanceId", "SourceId", "IsEnabled" }) &&
                !JsonSerializer.Deserialize<SkillGrant>(JsonSerializer.Serialize(normal))!.IsModeRule,
                "The additive false mode flag must leave the historical ordinary grant JSON shape unchanged.");
        var general = new GeneralDefinition("fixture:grant-old", "old", "", [new("ordinary copy", "ordinary") { ContentId = id }]);
        var player = new CharacterState { Seat = 0, Name = "mode owner", IsHuman = true, Role = Role.Contestant,
            RoleRevealed = true, General = general, GeneralSelected = true, GeneralRevealed = true, Hp = 4, MaxHp = 4 };
        var rule = new SkillGrant("mode:fixture:champion", id, "mode:fixture:champion", "mode:wenhe") { IsModeRule = true };
        player.SkillGrants.Grant(rule); player.SkillGrants.Grant(normal);
        Need(player.SkillGrants.SetEnabled(normal.GrantId, false) && player.SkillGrants.RemoveGrant(normal.GrantId) &&
            player.SkillGrants.Grants.Any(g => g.GrantId == rule.GrantId && g.IsEnabled && g.IsModeRule),
            "A normal acquired grant with the identical skill ID can still be disabled and lost while the independent mode instance remains.");
        var revision = player.SkillGrants.Revision;
        Need(!player.SkillGrants.RemoveGrant(rule.GrantId) && !player.SkillGrants.SetEnabled(rule.GrantId, false) &&
            player.SkillGrants.Revision == revision, "Rejected ordinary loss of a mode-owned instance must not mutate its grant revision.");
        player.General = new("fixture:grant-replacement", "replacement", "", []);
        Need(player.General.Skills.Count == 0 && player.SkillGrants.Grants is [var remaining] && remaining == rule &&
            player.SkillGrants.EffectiveSkillIds.SequenceEqual([id]),
            "A native template replacement removes its printed copy and retains the separate permanent mode grant.");
        var prefixOnly = normal with { GrantId = "mode-looking-ordinary", SkillInstanceId = "mode-looking-ordinary", SourceId = "mode:wenhe" };
        player.SkillGrants.Grant(prefixOnly);
        Need(player.SkillGrants.RemoveGrant(prefixOnly.GrantId), "An unmarked ordinary grant receives no protection merely from a mode-like source string.");
        foreach (var bad in new[] { rule with { GrantId = "bad-source", SourceId = "acquired:fake" },
                     rule with { GrantId = "bad-disable", IsEnabled = false },
                     rule with { GrantId = "bad-expiry", TurnExpiry = new(1, 0) } })
        {
            var rejected = false;
            try { player.SkillGrants.Grant(bad); } catch (InvalidOperationException) { rejected = true; }
            Need(rejected && player.SkillGrants.Grants.Count == 1, "A protected mode instance cannot be forged from a disabled, expiring or character-owned source.");
        }
    }

    public static void NativeSuppressionAndDeathCursePreserveModeRulesNirvanaAndChampionMark()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 8, HumanSeat = 0, HumanRole = null,
            ModeId = Mode, WenheAspirationRank = 1, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 10 }, registry);
        Accept(game, new StartGameCommand()); Until(game, () => game.PendingDecision?.Kind == DecisionKind.SelectGeneral);
        Accept(game, new RequestGeneralRosterCommand(0, true, game.Revision, game.PendingDecision!.PromptId));
        Accept(game, new SelectGeneralCommand(0, Owner, game.Revision, game.PendingDecision!.PromptId)); Play(game);
        Need(Enumerable.Range(0, 8).All(seat => game.CreateSnapshot(seat).Players[seat].Skills is { } skills && ModeSkills.All(id => skills.Any(s => s.ContentId == id))) &&
            game.CreateSnapshot(0).Players.All(p => !registry.Generals[p.GeneralId].SkillIds.Contains("wenhe:rules")),
            "All four common battle capabilities are effective mode grants rather than appended printed general skills.");
        Use(game, "suppress", 1); Play(game);
        var suppressed = game.CreateSnapshot(1).Players[1];
        Need(suppressed.Skills!.All(s => s.ContentId != Plain && s.ContentId != Quiet) &&
            ModeSkills.All(id => suppressed.Skills!.Any(s => s.ContentId == id)),
            "A genuine issued nonlocked suppression removes ordinary bindings while locked rules, the nonlocked private mark and limited Nirvana remain effective.");
        Cold(game, registry);
        var opponent = V(game, 2);
        Need(opponent is { Hp: 5, MaxHp: 6 },
            "The tier-five opponent has lost one HP to the actual opening Chaos before its first lethal hit. " +
            JsonSerializer.Serialize(new { Opponent = new { opponent.GeneralId, opponent.Hp, opponent.MaxHp }, Tier = registry.Modes[Mode].WenheActivity!.PlayerTier,
                Rule = game.CreateSnapshot(0).WenheRule }));
        Use(game, "defeat", 2); Play(game);
        Need(V(game, 2) is { IsAlive: true, Hp: 3 } && E<ProgramBindingResolvedEvent>(game).Count(e =>
                e.OwnerSeat == 2 && e.SkillId == "wenhe:niepan" && e.Activated && e.Completed) == 1 &&
            E<DamageAppliedEvent>(game).Where(e => e.SourceSeat == 0 && e.TargetSeat == 2).ToArray() is
                [{ Amount: 5, RemainingHp: 0 }],
            "The quiet opponent must consume its actual tier-five native Nirvana on the first real lethal hit before a later true death.");
        var before = V(game, 0); Use(game, "defeat", 2); Play(game);
        var lost = E<CharacterSkillsLostEvent>(game).Single(e => e.Seat == 0 && e.SourceSeat == 2);
        var after = V(game, 0);
        Need(!V(game, 2).IsAlive && lost.SkillIds.Contains(Driver) && lost.SkillIds.Contains(Plain) &&
            E<DamageAppliedEvent>(game).Where(e => e.SourceSeat == 0 && e.TargetSeat == 2).ToArray() is
                [{ Amount: 5, RemainingHp: 0 }, { Amount: 5, RemainingHp: -2 }] &&
            E<ProgramBindingResolvedEvent>(game).Count(e => e.OwnerSeat == 2 && e.SkillId == "wenhe:niepan" && e.Activated) == 1 &&
            lost.SkillIds.Contains("wenhe:champion-mark") && !lost.SkillIds.Contains("wenhe:rules") &&
            !lost.SkillIds.Contains("wenhe:chaos-resolution") && !lost.SkillIds.Contains("wenhe:niepan") &&
            after.Skills!.All(s => s.ContentId != Driver && s.ContentId != Plain) && ModeSkills.All(id => after.Skills!.Any(s => s.ContentId == id)) &&
            after.Hp == before.Hp && after.MaxHp == before.MaxHp + 1 && after.HandCount == before.HandCount + 3 &&
            game.GetHumanLegalActions().Any(a => a.ProgramSkillId == "wenhe:champion-mark"),
            "Actual death-source skill loss disables the ordinary same-name champion copy and printed skills, while all four mode instances stay live and the real mode kill reward still raises maximum HP and draws three.");
        Cold(game, registry);
        var hand = V(game, 0).HandCount;
        Accept(game, new UseProgramSkillCommand(0, "wenhe:champion-mark", "draw-two", [], [], game.Revision, game.PendingDecision!.PromptId)); Play(game);
        Need(V(game, 0).HandCount == hand + 2 && E<WenheChampionMarkUsedEvent>(game).Count(e => e.Seat == 0) == 1 &&
            game.GetHumanLegalActions().All(a => a.ProgramSkillId != "wenhe:champion-mark"),
            "After the ordinary copy was genuinely lost, the protected mode mark still pays its own once-game battle benefit and cannot be reused.");
        Cold(game, registry);
    }

    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static IEnumerable<T> E<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>();
    private static void Use(GameEngine game, string id, int target) => Accept(game,
        new UseProgramSkillCommand(0, Driver, id, [], [target], game.Revision, game.PendingDecision!.PromptId));
    private static void Play(GameEngine game) => Until(game, () => game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Until(GameEngine game, Func<bool> ready)
    {
        for (var i = 0; i < 160; i++)
        {
            if (ready()) return;
            if (game.PendingDecision is { PlayerSeat: 0 } p && p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("response") is "pass" or "let-die") is { } pass)
                Accept(game, new AnswerPromptCommand(0, p.PromptId, pass.Id, game.Revision));
            else Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Mode-grant fixture missed its actual boundary: " + JsonSerializer.Serialize(game.PendingDecision));
    }
    private static void Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, 8).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(game.CardMovements) == JsonSerializer.Serialize(restored.CardMovements),
            "Accepted native commands rebuild protected grants, loss, suppression and private marker use under cold JSON replay.");
    }
    private static void Accept(GameEngine game, GameCommand command) { var result = game.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Mode grant command rejected."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("wenhe-mode-grant-fixture", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rules = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity.rules.json")!);
            using var presentation = new StreamReader(assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.wenhe-activity.presentation.json")!);
            var activity = SkillProgramCatalog.Load(rules.ReadToEnd(), presentation.ReadToEnd());
            foreach (var (id, program) in activity.Programs) b.AddSkill(new(id, activity.Presentations[id].Name, activity.Presentations[id].Description)
                { Program = program, ProgramPresentation = activity.Presentations[id], Tags = id == "wenhe:niepan" ? SkillTag.Limited : id == "wenhe:rules" ? SkillTag.Locked : SkillTag.None });
            var catalog = SkillProgramCatalog.Load($$$"""
              {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
                {"id":"{{{Driver}}}","revision":1,"activations":[
                  {"id":"suppress","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"issueCurrentTurnNonLockedSkillSuppression","target":"selectedTarget"}]},
                  {"id":"defeat","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":5}]}
                ]},
                {"id":"{{{Quiet}}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
                {"id":"{{{Curse}}}","revision":1,"triggers":[{"id":"curse","window":"ownerDied","subject":"owner","optional":false,"effects":[{"op":"loseDeathSourceSkills","target":"owner"}]}]}
              ]}
              """, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                  skills = new[] { Driver, Quiet, Curse }.ToDictionary(id => id, id => new { name = id, description = "Actual mode grant boundary fixture." }) }));
            foreach (var (id, program) in catalog.Programs) b.AddSkill(new(id, id, "Actual native producer.") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            b.AddSkill(new(Plain, "ordinary", "Ordinary removable general skill."));
            b.AddGeneral(new(Owner, Owner, "", Driver, "wei", 4, [Plain, "wenhe:champion-mark"]));
            var ids = new[] { Owner }.Concat(Enumerable.Range(1, 7).Select(i => "fixture:wenhe-grant-peer-" + i)).ToArray();
            foreach (var id in ids.Skip(1)) b.AddGeneral(new(id, id, "", Quiet, "wei", 4, [Plain, Curse]));
            const string deck = "fixture:wenhe-grant-deck";
            b.AddDeck(new(deck, deck, 4, 2, [new("standard:dodge", 240)]));
            b.AddMode(new(Mode, Mode, 8, 8, new Dictionary<string, int>(), deck, 5, ids, ContentModeKind.Survival) { WenheActivity = new() });
        }
    }
}
