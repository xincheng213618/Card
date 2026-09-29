using CardGame.Content.Standard;
using CardGame.Core;

internal static class BoundarySlashStageChecks
{
    private const string FixtureGeneral = "fixture:boundary-paoxiao-owner";
    private const string FixtureMode = "identity:boundary-slash-stage-5";

    public static void StagedDefinitionsAndPaoxiaoReplay()
    {
        var registry = Registry();
        foreach (var (id, name, faction, hp, skills) in new[]
                 {
                     ("boundary:zhang-fei", "界张飞", "shu", 4,
                         new[] { "boundary:paoxiao", "boundary:tishen" }),
                     ("boundary:huang-zhong", "界黄忠", "shu", 4,
                         new[] { "boundary:liegong" }),
                     ("boundary:xiahou-yuan", "界夏侯渊", "wei", 4,
                         new[] { "boundary:shensu", "boundary:shebian" })
                 })
        {
            var general = registry.Generals[id];
            Require(general.Name == name && general.FactionId == faction &&
                    general.BaseHp == hp && general.SkillIds.SequenceEqual(skills) &&
                    !registry.IsGeneralPlayable(id) &&
                    !registry.Modes["identity:classic-5"].GeneralPoolIds!.Contains(id) &&
                    !registry.Modes["identity:classic-8"].GeneralPoolIds!.Contains(id),
                $"{id} must be catalogued with complete identities, but remain outside the formal pool while incomplete.");
        }

        Require(registry.Skills["boundary:paoxiao"].ImplementationStatus == SkillImplementationStatus.Complete &&
                registry.Skills["boundary:paoxiao"].Program!.Modifiers.Single() is
                { Query: SkillRuleQuery.SlashLimit, Operation: SkillRuleOperation.Unlimited } &&
                registry.Skills["boundary:tishen"].ImplementationStatus == SkillImplementationStatus.Planned &&
                registry.Skills["boundary:liegong"].ImplementationStatus == SkillImplementationStatus.Planned &&
                registry.Skills["boundary:shensu"].ImplementationStatus == SkillImplementationStatus.Partial &&
                registry.Skills["boundary:shebian"].ImplementationStatus == SkillImplementationStatus.Planned,
            "The five boundary skill identities must report their real implementation status.");
        var shensu = registry.Skills["boundary:shensu"].Program!.Triggers;
        Require(shensu.Select(trigger => trigger.Window).SequenceEqual([
                    SkillProgramTriggerWindow.TurnStartBeforeNormalFlow,
                    SkillProgramTriggerWindow.AfterNormalDraw]) &&
                shensu[0].Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.SkipTurnPhases,
                    SkillProgramEffectOp.UseVirtualCard]) &&
                shensu[1].Effects.Select(effect => effect.Op).SequenceEqual([
                    SkillProgramEffectOp.SelectTarget, SkillProgramEffectOp.SelectAndMoveOwnedCard,
                    SkillProgramEffectOp.SkipTurnPhases, SkillProgramEffectOp.UseVirtualCard]),
            "Partial Shensu must contain only its two executable phase substitutions.");

        var game = StartPaoxiaoFixture(registry);
        var before = game.Events.Count;
        PlayFirstSlash(game);
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The first Slash must finish at its owner's play boundary.");
        PlayFirstSlash(game);
        Require(game.Events.Skip(before).Select(item => item.Payload).OfType<CardUsedEvent>()
                    .Count(item => item.SourceSeat == 0 && item.CardKind == CardKind.Slash) == 2,
            "A complete Paoxiao must allow two distinct physical Slashes in the same play phase.");
        var restored = GameReplay.Restore(game.CreateCheckpoint(), registry);
        Require(SnapshotJson.Serialize(restored.CreateSnapshot(0, revealAll: true)) ==
                SnapshotJson.Serialize(game.CreateSnapshot(0, revealAll: true)),
            "The second Paoxiao Slash must replay without changing the published state.");
    }

    private static ContentRegistry Registry() => ContentRegistry.Build(
        new StandardContentPackage(),
        new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(),
        new StandardClassicGeneralPackage(),
        new SyntheticPackage("boundary-paoxiao-fixture", builder =>
        {
            builder.AddGeneral(new ContentGeneralDefinition(
                FixtureGeneral, "咆哮能力测试", "zhang_fei", "boundary:paoxiao", "shu", 4));
            builder.AddDeck(new ContentDeckRecipe(
                "fixture:boundary-slash-deck", "杀牌能力测试牌堆", 4, 2,
                [new ContentDeckCardCount("standard:slash", 160)]));
            builder.AddMode(new ContentModeDefinition(
                FixtureMode, "咆哮能力身份场", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1,
                    [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1
                },
                "fixture:boundary-slash-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [FixtureGeneral, "classic:liu-bei", "classic:sun-quan",
                    "classic:cao-cao", "classic:huang-gai"]));
        }));

    private static GameEngine StartPaoxiaoFixture(ContentRegistry registry)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 7, HumanSeat = 0, HumanRole = Role.Lord, PlayerCount = 5,
            ModeId = FixtureMode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = true, MaxTurns = 20
        }, registry);
        Require(game.Submit(new StartGameCommand(game.Revision)).Accepted &&
                game.PendingDecision is { Kind: DecisionKind.SelectGeneral, PlayerSeat: 0 } setup &&
                setup.ValidContentIds.Contains(FixtureGeneral) &&
                game.Submit(new SelectGeneralCommand(0, FixtureGeneral, game.Revision, setup.PromptId)).Accepted,
            "The Paoxiao-only general must be selectable in its isolated test mode.");
        Require(game.PendingDecision is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 },
            "The isolated Paoxiao owner must reach its first play phase.");
        return game;
    }

    private static void PlayFirstSlash(GameEngine game)
    {
        var action = game.GetHumanLegalActions().FirstOrDefault(item =>
            item.Kind == LegalActionKind.Slash && item.PlayedCardKind == CardKind.Slash &&
            item.CardId is not null && item.TargetSeats.Count == 1) ??
            throw new InvalidOperationException("No physical Slash was published for the Paoxiao fixture.");
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value,
            action.TargetSeats, game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind)
        { ConversionSource = action.ConversionSource,
          AdditionalConversionSources = action.AdditionalConversionSources });
        Require(result.Accepted, result.Error?.Message ?? "A published Paoxiao Slash was rejected.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
