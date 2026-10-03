using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

// Small fixed native matches use the registered Fangquan composition and the
// registered owner-only primitive without forcing an activation or target.
internal static class PhaseHandExtraTurnAiChecks
{
    private const string Skill = "classic:fangquan";
    private const string OwnerOnlySkill = "fixture:phase-owner-extra";
    private const string OwnerOnlyName = "旧自身额外回合";
    private const string Mode = "identity:classic-phase-hand-extra-turn-ai-fixture";
    private const string CostReason = "skill-program." + Skill + ".MoveBoundCards";

    public static void NativePublicDeclarationPaysAndExecutesSelectedBeneficiaryExtraTurn()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = -1, HumanRole = null, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        ProgramExtraTurnPendedEvent? receipt = null;
        ProgramBindingStartedEvent? paying = null;
        for (var step = 0; step < 240 && receipt is null; step++)
        {
            foreach (var issued in game.Events.Select(e => e.Payload).OfType<ProgramExtraTurnPendedEvent>().Where(e => e.SkillId == Skill))
            {
                var source = game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>()
                    .Single(e => e.FrameId == issued.FrameId && e.SkillId == Skill);
                var sourceView = game.CreateSnapshot(source.OwnerSeat);
                if (sourceView.Players[source.OwnerSeat].Role != Role.Loyalist || sourceView.Players[issued.Seat].Role != Role.Lord) continue;
                receipt = issued; paying = source; break;
            }
            if (receipt is not null) break;
            if (game.State.Status == EngineStatus.Completed)
                throw new InvalidOperationException("The fixed native fixture completed before a known ally's real benefit. " + NativeDiagnostic(game));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(receipt is not null && paying is not null && paying.Window == SkillProgramTriggerWindow.TurnEnding &&
                paying.SkillInstanceId.Length > 0 && paying.OwnerSeat != receipt.Seat,
            "Native public relation scoring actually selects the visible Lord as another Loyalist source's beneficiary.");
        var sourceSeat = paying!.OwnerSeat; var beneficiary = receipt!.Seat;
        var moves = game.CardMovements.Where(m => m.Reason.Value == CostReason && m.From == CardLocation.Hand(sourceSeat) &&
            m.To == CardLocation.DiscardPile).ToArray();
        Require(moves.Length == 1 && game.Events.Select(e => e.Payload).OfType<ProgramBooleanStateChangedEvent>().Any(e =>
                e.OwnerSeat == sourceSeat && e.SkillId == Skill && e.SkillInstanceId == paying.SkillInstanceId &&
                e.StateId == "fangquan-pending" && e.Value) &&
                game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>().Any(e =>
                    e.OwnerSeat == sourceSeat && e.SkillId == Skill && e.SkillInstanceId == paying.SkillInstanceId &&
                    e.Window == SkillProgramTriggerWindow.AfterNormalDraw),
            "The native source declares the registered classic same-instance skip and pays one exact hand entity before its TurnEnding extra-turn receipt.");
        var currentHistory = game.Events.Select(e => e.Payload).ToArray();
        var receiptIndex = Array.FindIndex(currentHistory, e => e is ProgramExtraTurnPendedEvent extra && extra.FrameId == receipt.FrameId);
        var sourceTurnStart = Array.FindLastIndex(currentHistory, receiptIndex, e => e is TurnStartedEvent turn && turn.ActorSeat == sourceSeat);
        Require(sourceTurnStart >= 0 && !currentHistory.Skip(sourceTurnStart + 1)
                .TakeWhile(e => e is not TurnStartedEvent).OfType<PhaseChangedEvent>()
                .Any(e => e.ActorSeat == sourceSeat && e.Phase == TurnPhase.Play) &&
                game.CreateSnapshot(beneficiary).Players[beneficiary].GeneralId != "fixture:ls-native-source" &&
                game.AiGeneralThoughts.Any(thought => thought.ActorSeat == sourceSeat &&
                    thought.SelectedGeneralId == "fixture:ls-native-source"),
            "Real phase history contains no source Play; formal native general selection gives the only Fangquan general to the Loyalist and leaves the beneficiary without it.");
        Require(!game.AcceptedCommands.Any(c => c is AnswerPromptCommand or UseProgramSkillCommand),
            "The native path is driven entirely by real Start/Advance commands without a manual forced skill activation or target answer.");
        Cold(game, registry);
        var eventIndex = game.Events.ToList().FindIndex(e => e.Payload is ProgramExtraTurnPendedEvent extra &&
            extra.FrameId == receipt.FrameId && extra.SkillId == Skill);
        var expectedResume = Enumerable.Range(1, 4).Select(offset => (sourceSeat + offset) % 4)
            .First(seat => game.CreateSnapshot(sourceSeat).Players[seat].IsAlive);
        int[] TurnsAfterReceipt() => game.Events.Skip(eventIndex + 1).Select(e => e.Payload).OfType<TurnStartedEvent>()
            .Take(2).Select(e => e.ActorSeat).ToArray();
        for (var step = 0; step < 140 && TurnsAfterReceipt().Length < 2; step++)
        {
            Require(game.State.Status != EngineStatus.Completed, "The native fixture completed before the extra/natural turn pair.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(TurnsAfterReceipt().SequenceEqual([beneficiary, expectedResume]) &&
                game.CardMovements.Count(m => m.CardId == moves[0].CardId && m.Reason.Value == CostReason) == 1,
            "The actual selected extra turn starts, then the saved natural seat resumes; the native continuation never repays its entity.");
        Cold(game, registry);
    }

    public static void OwnerOnlyNativeExtraTurnKeepsLegacyTwentyValue()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true),
            new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(), new Fixture(ownerOnly: true));
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = -1, HumanRole = null, ModeId = Mode,
            UseInteractiveSetup = true, UseInteractiveDiscard = true, AdvanceAfterHumanCommands = false, MaxTurns = 4
        }, registry);
        Accept(game, new StartGameCommand());
        ProgramExtraTurnPendedEvent? receipt = null;
        for (var step = 0; step < 60 && receipt is null; step++)
        {
            receipt = game.Events.Select(e => e.Payload).OfType<ProgramExtraTurnPendedEvent>()
                .FirstOrDefault(e => e.SkillId == OwnerOnlySkill);
            if (receipt is not null) break;
            if (game.State.Status == EngineStatus.Completed)
                throw new InvalidOperationException("The small owner-only fixture completed before its optional real benefit. " + NativeDiagnostic(game));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(receipt is not null, "The unchanged owner-only primitive actually issues an extra-turn fact through native activation.");
        var source = game.Events.Select(e => e.Payload).OfType<ProgramBindingStartedEvent>()
            .Single(e => e.FrameId == receipt!.FrameId && e.SkillId == OwnerOnlySkill);
        var actionText = $"发动【{OwnerOnlyName}】";
        Require(receipt!.Seat == source.OwnerSeat && source.Window == SkillProgramTriggerWindow.AfterNormalDraw &&
                game.AiThoughts.Any(thought => thought.ActorSeat == source.OwnerSeat && thought.Decision == actionText &&
                    thought.Candidates.Any(candidate => candidate.Action.Description == actionText && candidate.Score == 20d)) &&
                !game.AcceptedCommands.Any(command => command is AnswerPromptCommand or UseProgramSkillCommand),
            "The exact owner-only PendExtraTurn effect cloned from registered classic Lianpo retains native score20 and chooses its source.");
        Cold(game, registry);
        var issuedIndex = game.Events.ToList().FindIndex(e => e.Payload is ProgramExtraTurnPendedEvent extra &&
            extra.FrameId == receipt.FrameId && extra.SkillId == OwnerOnlySkill);
        for (var step = 0; step < 80 && !game.Events.Skip(issuedIndex + 1).Any(e => e.Payload is TurnStartedEvent); step++)
        {
            Require(game.State.Status != EngineStatus.Completed, "The owner-only fixture completed before its actual extra turn.");
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        Require(game.Events.Skip(issuedIndex + 1).Select(e => e.Payload).OfType<TurnStartedEvent>()
                .FirstOrDefault()?.ActorSeat == source.OwnerSeat &&
                game.Events.Select(e => e.Payload).OfType<ProgramExtraTurnPendedEvent>()
                    .Count(e => e.SkillId == OwnerOnlySkill && e.Seat == source.OwnerSeat) == 1,
            "The unchanged owner-only score20 benefit starts a real extra turn and its once-game fixture gate prevents a loop.");
        Cold(game, registry);
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Rejected native command.");
    }
    private static string NativeDiagnostic(GameEngine game) => JsonSerializer.Serialize(new
    {
        game.State.Status, game.State.TurnNumber, game.State.Revision,
        ExtraTurns = game.Events.Select(e => e.Payload).OfType<ProgramExtraTurnPendedEvent>().ToArray(),
        Thoughts = game.AiThoughts.Where(thought => thought.Decision.Contains("放权") || thought.Decision.Contains(OwnerOnlyName))
            .Select(thought => new { thought.ActorSeat, thought.Decision,
                Candidates = thought.Candidates.Select(candidate => new { candidate.Action.Description, candidate.Score }) }).ToArray()
    });
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, 4).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray(),
        Frames = JsonSerializer.Serialize(game.ResolutionStack), Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        game.CardMovements, game.AiThoughts, game.AiGeneralThoughts,
        Commands = CommandJson.Serialize(game.AcceptedCommands), Zones = game.CreateCardZoneDiagnostics()
    });
    private static void Cold(GameEngine game, ContentRegistry registry) => Require(State(game) == State(GameReplay.Restore(
        GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry)),
        "The real native command prefix cold-restores all four private views, paid entities, frames, zones and facts.");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(bool ownerOnly = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-phase-hand-extra-turn-ai", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            if (ownerOnly)
            {
                var assembly = typeof(StandardClassicGeneralPackage).Assembly;
                const string suffix = "classic-shen-sima-yi.rules.json";
                using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal)))!;
                using var reader = new StreamReader(stream);
                var rules = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
                var legacy = rules["skills"]!.AsArray().Single(node => node!["id"]!.GetValue<string>() == "classic:lianpo")!.DeepClone();
                var originalEffects = legacy["triggers"]!.AsArray().Single()!["effects"]!.DeepClone();
                legacy["id"] = OwnerOnlySkill;
                // Keep the actual registered owner-only effect. Only eligibility
                // is an ordinary once-game optional window, avoiding a kill rig.
                legacy["triggers"] = new JsonArray(new JsonObject
                {
                    ["id"] = "one-owner-extra", ["window"] = "afterNormalDraw", ["subject"] = "owner",
                    ["optional"] = true, ["priority"] = 0, ["usageScope"] = "game", ["usageLimit"] = 1,
                    ["effects"] = originalEffects
                });
                rules["skills"] = new JsonArray(legacy);
                var presentation = new JsonObject
                {
                    ["schemaVersion"] = 3,
                    ["skills"] = new JsonObject { [OwnerOnlySkill] = new JsonObject
                        { ["name"] = OwnerOnlyName, ["description"] = "一次原生自身额外回合估值回归。" } }
                };
                var actual = SkillProgramCatalog.Load(rules.ToJsonString(), presentation.ToJsonString());
                builder.AddSkill(new(OwnerOnlySkill, OwnerOnlyName, actual.Presentations[OwnerOnlySkill].Description)
                    { Program = actual.Programs[OwnerOnlySkill], ProgramPresentation = actual.Presentations[OwnerOnlySkill] });
                for (var seat = 0; seat < 4; seat++)
                    builder.AddGeneral(new($"fixture:ls-native-{seat}", OwnerOnlyName, "supporter", OwnerOnlySkill, "shu", 8, []));
            }
            else
            {
                builder.AddSkill(new("fixture:ls-source-selection", "唯一来源候选", "正式角色选择权重，只为 Loyalist 提供放权来源。")
                {
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Loyalist ? 100d : -100d)
                });
                builder.AddSkill(new("fixture:ls-quiet-selection", "无连锁候选", "其余角色不拥有放权。")
                {
                    SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, role => role == Role.Loyalist ? -100d : 100d)
                });
                builder.AddGeneral(new("fixture:ls-native-source", "公开放权来源", "supporter", "fixture:ls-source-selection", "shu", 8, [Skill]));
                for (var seat = 0; seat < 3; seat++)
                    builder.AddGeneral(new($"fixture:ls-native-{seat}", "无放权受赠者", "supporter", "fixture:ls-quiet-selection", "shu", 8, []));
            }
            builder.AddDeck(new("fixture:ls-native-deck", "小固定原生混合牌堆", 4, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 48).Select(index => new ContentDeckPhysicalCard(
                    index % 6 == 0 ? "standard:slash" : index % 6 == 1 ? "standard:duel" : "standard:dodge",
                    index % 2 == 0 ? Suit.Spade : Suit.Heart, index % 13 + 1)).ToArray()
            });
            builder.AddMode(new(Mode, "公开阶段付款原生AI", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Loyalist)] = 1 },
                "fixture:ls-native-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ownerOnly
                    ? ["fixture:ls-native-0", "fixture:ls-native-1", "fixture:ls-native-2", "fixture:ls-native-3"]
                    : ["fixture:ls-native-source", "fixture:ls-native-0", "fixture:ls-native-1", "fixture:ls-native-2"]));
        }
    }
}
