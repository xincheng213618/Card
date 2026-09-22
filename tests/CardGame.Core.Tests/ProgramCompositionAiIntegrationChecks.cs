using CardGame.Content.Standard;
using CardGame.Core;

internal static class ProgramCompositionAiIntegrationChecks
{
    public static void JiemingAiActivatesAndDrawsForFriendlyTarget()
    {
        const int firstSeed = 730_001;
        const int seedCount = 16;
        string? lastObservation = null;

        for (var seed = firstSeed; seed < firstSeed + seedCount; seed++)
        {
            var game = CreateAiGame(seed, JiemingScenarioPackage.ModeId, CreateJiemingRegistry());
            if (!Advance(game, 4_000, current => current.Events.Any(envelope =>
                envelope.Payload is ProgramBindingResolvedEvent
                {
                    SkillId: "standard:jieming",
                    Activated: true,
                    Completed: true
                })))
            {
                lastObservation = $"seed {seed}: no completed AI Jieming binding in 4000 steps";
                continue;
            }

            var intervals = game.Events
                .Where(envelope => envelope.Payload is ProgramBindingStartedEvent
                {
                    SkillId: "standard:jieming",
                    BindingId: "draw-target-to-max-per-damage-point"
                })
                .Select(start =>
                {
                    var binding = (ProgramBindingStartedEvent)start.Payload;
                    var completed = game.Events.FirstOrDefault(envelope =>
                        envelope.Sequence > start.Sequence &&
                        envelope.Payload is ProgramBindingResolvedEvent resolved &&
                        resolved.FrameId == binding.FrameId && resolved.SkillId == binding.SkillId &&
                        resolved.BindingId == binding.BindingId && resolved.Activated && resolved.Completed);
                    return (Start: start, Binding: binding, Completed: completed);
                })
                .Where(item => item.Completed is not null)
                .ToArray();
            if (intervals.Length == 0)
            {
                lastObservation = $"seed {seed}: no matched Jieming Start-to-Completed frame";
                continue;
            }

            foreach (var interval in intervals)
            {
                var draws = game.Events
                    .Where(envelope => envelope.Sequence > interval.Start.Sequence &&
                        envelope.Sequence < interval.Completed!.Sequence)
                    .Select(envelope => envelope.Payload)
                    .OfType<CardMovedEvent>()
                    .Where(move => move.Reason.Value == "skill-program.standard:jieming.Draw" &&
                        move.From == CardLocation.DrawPile && move.To.Zone == CardZoneKind.Hand)
                    .ToArray();
                if (draws.Length == 0)
                {
                    lastObservation = $"seed {seed}, frame {interval.Binding.FrameId}: completed without a draw";
                    continue;
                }
                var targetSeats = draws.Select(move => move.To.OwnerSeat!.Value).Distinct().ToArray();
                if (targetSeats.Length == 1 && targetSeats[0] == interval.Binding.OwnerSeat)
                    return;
                lastObservation = $"seed {seed}, frame {interval.Binding.FrameId}: " +
                    $"owner={interval.Binding.OwnerSeat}, targets={string.Join(',', targetSeats)}";
            }
        }

        throw new InvalidOperationException(
            $"No real AI Jieming activation completed in seeds {firstSeed}..{firstSeed + seedCount - 1}; " +
            $"last={lastObservation ?? "no damage reached"}.");
    }

    public static void ZishouSelfOnlyPreventsWastefulJiangchiAssault()
    {
        var game = CreateAiGame(731_001, PolicyScenarioPackage.ModeId, CreatePolicyRegistry());
        Require(Advance(game, 1_024, current =>
        {
            var zishou = current.Events.FirstOrDefault(envelope => envelope.Payload is ProgramBindingStartedEvent
            {
                SkillId: "classic:zishou",
                BindingId: "extra-draw-self-only"
            });
            if (zishou?.Payload is not ProgramBindingStartedEvent binding) return false;
            return current.Events.Any(envelope => envelope.Sequence > zishou.Sequence &&
                envelope.Payload is PhaseChangedEvent
                {
                    Phase: TurnPhase.Play,
                    ActorSeat: var seat
                } && seat == binding.OwnerSeat);
        }), "The policy fixture did not reach Play after the Zishou/Jiangchi draw window.");

        var zishouEnvelope = game.Events.First(envelope => envelope.Payload is ProgramBindingStartedEvent
        {
            SkillId: "classic:zishou",
            BindingId: "extra-draw-self-only"
        });
        var zishou = (ProgramBindingStartedEvent)zishouEnvelope.Payload;
        Require(game.Events.Any(envelope => envelope.Sequence >= zishouEnvelope.Sequence &&
                envelope.Payload is CardTargetRestrictionGrantedEvent granted &&
                granted.Restriction.TurnSeat == zishou.OwnerSeat &&
                granted.Restriction.Restriction == SkillProgramCardTargetRestriction.SelfOnly),
            "The fixture did not apply Zishou's real self-only turn restriction before Jiangchi.");

        var laterOwnerStarts = game.Events
            .Where(envelope => envelope.Sequence > zishouEnvelope.Sequence)
            .Select(envelope => envelope.Payload)
            .OfType<ProgramBindingStartedEvent>()
            .Where(item => item.OwnerSeat == zishou.OwnerSeat && item.SkillId == "classic:jiangchi")
            .ToArray();
        Require(laterOwnerStarts.All(item => item.BindingId != "mode-assault"),
            "AI selected Jiangchi assault after Zishou had already made other-player Slash targets illegal.");
        Require(!game.Events.Select(envelope => envelope.Payload).OfType<ProgramNormalDrawAdjustedEvent>()
                .Any(item => item.OwnerSeat == zishou.OwnerSeat && item.SkillId == "classic:jiangchi" &&
                    item.BindingId == "mode-assault" && item.Adjustment < 0),
            "The incompatible Jiangchi assault branch reduced the real normal draw count.");
        Require(!game.Events.Select(envelope => envelope.Payload).OfType<TurnRuleModifierGrantedEvent>()
                .Any(item => item.Modifier.TurnSeat == zishou.OwnerSeat &&
                    item.Modifier.Source.SkillId == "classic:jiangchi" &&
                    item.Modifier.Source.BindingId == "mode-assault"),
            "The incompatible Jiangchi assault branch granted real Slash modifiers.");
    }

    private static GameEngine CreateAiGame(int seed, string modeId, ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed,
            PlayerCount = 4,
            ModeId = modeId,
            HumanSeat = -1,
            HumanRole = null,
            UseInteractiveSetup = false,
            UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false,
            AiPolicyVersion = 2,
            MaxTurns = 30
        }, registry);
        var started = game.Submit(new StartGameCommand());
        Require(started.Accepted, started.Error?.Message ?? "AI composition fixture failed to start.");
        return game;
    }

    private static bool Advance(GameEngine game, int maximumSteps, Func<GameEngine, bool> completed)
    {
        for (var step = 0; step < maximumSteps && game.CreateSnapshot(-1).Status != EngineStatus.Completed; step++)
        {
            if (completed(game)) return true;
            var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
            Require(result.Accepted, result.Error?.Message ?? $"AI fixture failed at step {step}.");
        }
        return completed(game);
    }

    private static ContentRegistry CreateJiemingRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new JiemingScenarioPackage());

    private static ContentRegistry CreatePolicyRegistry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new PolicyScenarioPackage());

    private static void RegisterScenario(
        IContentRegistryBuilder builder,
        string modeId,
        string deckId,
        string generalPrefix,
        string primarySkillId,
        IReadOnlyList<string>? additionalSkillIds = null)
    {
        var generalIds = Enumerable.Range(0, 4).Select(index => $"{generalPrefix}-{index}").ToArray();
        foreach (var (generalId, index) in generalIds.Select((id, index) => (id, index)))
            builder.AddGeneral(new ContentGeneralDefinition(
                generalId, $"AI组合目标{index + 1}", "supporter", primarySkillId,
                index < 2 ? "wei" : "qun", BaseHp: 4, AdditionalSkillIds: additionalSkillIds));

        var cards = Enumerable.Range(0, 240).Select(index => new ContentDeckPhysicalCard(
            "standard:slash", (Suit)(index % 4), index % 13 + 1)).ToArray();
        builder.AddDeck(new ContentDeckRecipe(deckId, "AI组合回归牌堆", 2, 2, [])
        {
            PhysicalCards = cards
        });
        builder.AddMode(new ContentModeDefinition(
            modeId, "AI组合回归", 4, 4,
            new Dictionary<string, int>
            {
                [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1,
                [nameof(Role.Rebel)] = 2
            },
            deckId,
            GeneralCandidateCount: 1,
            GeneralPoolIds: generalIds));
    }

    private sealed class JiemingScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:composition-ai-jieming-4";
        public PackageManifest Manifest { get; } = new(
            "composition-ai-jieming", new Version(1, 0, 0),
            [new PackageDependency("standard", StandardContentPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder) => RegisterScenario(
            builder, ModeId, "fixture:composition-ai-jieming-deck",
            "fixture:composition-ai-jieming", "standard:jieming");
    }

    private sealed class PolicyScenarioPackage : IGameContentPackage
    {
        public const string ModeId = "identity:composition-ai-policy-4";
        public PackageManifest Manifest { get; } = new(
            "composition-ai-policy", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", StandardClassicGeneralPackage.CurrentVersion)]);

        public void Register(IContentRegistryBuilder builder) => RegisterScenario(
            builder, ModeId, "fixture:composition-ai-policy-deck",
            "fixture:composition-ai-policy", "classic:zishou", ["classic:jiangchi"]);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
