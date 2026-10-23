using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5YuanshaoChecks
{
    private const string Skill = StandardShanheS5YuanshaoPackage.LuanjiId;
    private const string Red = "red-hand-pair-as-arrow-barrage", Black = "black-hand-pair-as-arrow-barrage";
    private const string Ally = "fixture:shanhe-yuanshao-ally", Enemy = "fixture:shanhe-yuanshao-enemy";
    private const string Shielded = "fixture:shanhe-yuanshao-shielded", Shield = "fixture:shanhe-yuanshao-shield";

    public static void SameColorPairsAndOptionalNativeArrowTargetsRecoverCold()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new StandardShanheS5YuanshaoPackage(), new Fixture());
        var setup = new ShanheBattleSetup(83, 1, "fixture:s5-yuanshao", StandardShanheS5YuanshaoPackage.YuanshaoId,
            4, 0, 0, 0, 0, [],
            [new(CardKind.Crossbow, Suit.Heart, 7), new(CardKind.DefensiveHorse, Suit.Diamond, 8),
             new(CardKind.Crossbow, Suit.Spade, 9), new(CardKind.DefensiveHorse, Suit.Club, 10), new(CardKind.ArrowBarrage, Suit.Heart, 1)],
            [], [new(Enemy) { StartingHand = [] }, new(Shielded) { StartingHand = [] }],
            [new(Ally) { StartingHand = [new(CardKind.Dodge, Suit.Club, 2)] }], new string('a', 64));
        var game = GameEngine.CreateStandard(new()
        {
            Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId,
            ShanheBattle = setup, AdvanceAfterHumanCommands = false, UseInteractiveDiscard = false,
            MaxTurns = 3, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
        }, registry);
        Accept(game, new StartGameCommand());
        Until(game, () => Pending(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
        var hand = game.CreateSnapshot(0).Players[0].Hand;
        int Card(CardKind kind, Suit suit) => hand.Single(card => card.Kind == kind && card.Suit == suit).Id;
        var red = new[] { Card(CardKind.Crossbow, Suit.Heart), Card(CardKind.DefensiveHorse, Suit.Diamond) };
        var black = new[] { Card(CardKind.Crossbow, Suit.Spade), Card(CardKind.DefensiveHorse, Suit.Club) };
        var physical = Card(CardKind.ArrowBarrage, Suit.Heart);
        var dodge = game.CreateSnapshot(1).Players[1].Hand.Single().Id;
        var original = State(game);
        Need(!game.Submit(new UseProgramSkillCommand(0, Skill, Red, [red[0], black[0]], [], game.Revision, Pending(game)!.PromptId)).Accepted &&
            State(game) == original, "Mixed-color Hand materials cannot pay the same-color conversion or change its command prefix.");

        BeginConversion(game, Red, red);
        Until(game, () => AtOptional(game));
        var redUse = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Action?.EffectiveKind == CardKind.ArrowBarrage);
        Need(redUse.TargetSeats.SequenceEqual([1, 2]) &&
            Facts<ProgramViewAsConvertedEvent>(game) is [{ SkillId: Skill, BindingId: Red, OutputKind: CardKind.ArrowBarrage, IsUse: true } conversion] &&
            conversion.PhysicalCardIds.SequenceEqual(red) && conversion.FrameId == redUse.Id,
            "A Heart/Diamond pair becomes one genuine ArrowBarrage Use with both exact physical costs, excluding prohibited targets.");
        Privacy(game);
        game = Cold(game, registry);
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "skip");
        Until(game, () => Pending(game) is { Kind: DecisionKind.RespondDodge, PlayerSeat: 1 });
        Need(game.CreateSnapshot(0).PendingDecision is null && game.CreateSnapshot(2).PendingDecision is null,
            "The real allied Dodge response remains private to its responding seat.");
        game = Cold(game, registry);
        // This is another seat's private prompt; let that seat's native AI
        // submit its response rather than routing it through the human API.
        Accept(game, new AdvanceOneStepCommand(game.Revision));
        Finish(game, redUse.Id);
        Need(Facts<ProgramArrowBarrageTargetRemovedEvent>(game).Length == 0 &&
            Facts<GroupCardUsedEvent>(game).Single(fact => fact.ResolutionId == redUse.Id).TargetSeats.SequenceEqual([1, 2]) &&
            Facts<GroupResponseEvent>(game).Any(fact => fact.ResolutionId == redUse.Id && fact.ResponderSeat == 1 && fact.UsedResponse && fact.ResponseCardId == dodge) &&
            Facts<DamageAppliedEvent>(game) is [{ SourceSeat: 0, TargetSeat: 2, Amount: 1 }],
            "Skipping the optional exclusion keeps all legal targets and settles the actual Dodge and unresponded native damage.");
        Until(game, () => Pending(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });

        BeginConversion(game, Black, black);
        Until(game, () => AtOptional(game));
        var blackUse = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Action?.EffectiveKind == CardKind.ArrowBarrage);
        Need(Facts<ProgramViewAsConvertedEvent>(game).Single(fact => fact.FrameId == blackUse.Id) is { BindingId: Black } converted &&
            converted.PhysicalCardIds.SequenceEqual(black), "A Spade/Club pair also pays the independent black same-color conversion in the same Play phase.");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(game, () => AtSelection(game));
        Need(Pending(game)!.Choices.All(choice => choice.Targets.Count == 1 && choice.Targets.All(seat => seat is 1 or 2)),
            "The exclusion offers exactly one current legal target, never the prohibited third participant or a multi-target removal.");
        game = Cold(game, registry);
        Answer(game, choice => choice.Targets.SequenceEqual([1]));
        Finish(game, blackUse.Id);
        Need(Facts<ProgramArrowBarrageTargetRemovedEvent>(game) is [{ CardUseFrameId: var removedUse, OwnerSeat: 0, SkillId: Skill, TargetSeat: 1 }] &&
            removedUse == blackUse.Id && Facts<GroupCardUsedEvent>(game).Single(fact => fact.ResolutionId == blackUse.Id).TargetSeats.SequenceEqual([2]) &&
            !game.CreateSnapshot(0).Players[2].IsAlive && game.State.Winner == Winner.None,
            "Removing one target edits the actual black-converted Use; the remaining two-HP enemy dies through its second native damage while the shielded enemy keeps the battle active.");
        Until(game, () => Pending(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });

        var action = game.GetHumanLegalActions().Single(candidate => candidate.Kind == LegalActionKind.ArrowBarrage && candidate.CardId == physical);
        Accept(game, new PlayCardCommand(0, physical, action.TargetSeats, game.Revision, Pending(game)!.PromptId));
        Until(game, () => AtOptional(game));
        var physicalUse = game.ResolutionStack.OfType<CardUseFrame>().Single(frame => frame.Action?.EffectiveKind == CardKind.ArrowBarrage);
        Need(physicalUse.TargetSeats.SequenceEqual([1]) && physicalUse.Action!.ConversionChain.Count == 0,
            "A physical ArrowBarrage uses the same optional skill even when only one legal living target remains.");
        Answer(game, choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(game, () => AtSelection(game));
        game = Cold(game, registry);
        Answer(game, choice => choice.Targets.SequenceEqual([1]));
        Finish(game, physicalUse.Id);
        Need(Facts<ProgramArrowBarrageTargetRemovedEvent>(game).Count(fact => fact.CardUseFrameId == physicalUse.Id && fact.TargetSeat == 1) == 1 &&
            Facts<GroupCardUsedEvent>(game).Single(fact => fact.ResolutionId == physicalUse.Id).TargetSeats.Count == 0 &&
            !Facts<GroupResponseEvent>(game).Any(fact => fact.ResolutionId == physicalUse.Id) &&
            Facts<DamageAppliedEvent>(game).Length == 2 && Facts<DamageAppliedEvent>(game).All(fact => fact.TargetSeat == 2 && fact.Amount == 1) &&
            game.CreateSnapshot(0).Players[1].Hp == 8 && game.CreateSnapshot(0).Players[3].Hp == 8,
            "Omitting the sole legal target genuinely leaves zero actual targets: the paid physical Use finishes without requesting a response or applying another damage.");
        foreach (var card in red.Concat(black).Append(physical))
            Need(game.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Hand(0) && move.To == CardLocation.Processing) == 1 &&
                game.CardMovements.Count(move => move.CardId == card && move.From == CardLocation.Processing && move.To == CardLocation.DiscardPile) == 1 &&
                game.CreateCardZoneDiagnostics().Single(item => item.CardId == card).Location == CardLocation.DiscardPile,
                "Both same-color pairs and the targetless physical ArrowBarrage pay and clean up their original entities exactly once.");
        Need(Facts<CardUseFinishedEvent>(game).Count(fact => fact.CardKind == CardKind.ArrowBarrage) == 3,
            "All three real ArrowBarrage uses complete once through native card resolution.");
        Privacy(game);
        _ = Cold(game, registry);
    }

    private static void BeginConversion(GameEngine game, string activation, int[] cards) => Accept(game,
        new UseProgramSkillCommand(0, Skill, activation, cards, [], game.Revision, Pending(game)!.PromptId));
    private static bool AtOptional(GameEngine game) => Pending(game)?.SkillPrompt?.SkillId == Skill &&
        Pending(game)!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "activate");
    private static bool AtSelection(GameEngine game) => Pending(game)?.SkillPrompt?.SkillId == Skill &&
        Pending(game)!.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") == "select-targets");
    private static void Finish(GameEngine game, long use) => Until(game, () => Facts<CardUseFinishedEvent>(game).Any(fact => fact.ResolutionId == use));
    private static T[] Facts<T>(GameEngine game) where T : IGameEvent => game.Events.Select(envelope => envelope.Payload).OfType<T>().ToArray();
    private static PendingDecision? Pending(GameEngine game) => Enumerable.Range(0, game.PlayerCount)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(pending => pending is not null);
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    {
        var pending = Pending(game)!;
        Accept(game, new AnswerPromptCommand(pending.PlayerSeat, pending.PromptId, pending.Choices.First(predicate).Id, game.Revision));
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, result.Error?.Message ?? "The native same-color ArrowBarrage command was rejected.");
    }
    private static void Until(GameEngine game, Func<bool> arrived)
    {
        for (var step = 0; step < 100; step++)
        {
            if (arrived()) return;
            Need(Pending(game)?.PlayerSeat != 0, "Unexpected human prompt in the bounded ArrowBarrage fixture: " + JsonSerializer.Serialize(Pending(game)));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The native ArrowBarrage boundary was not reached.");
    }
    private static void Privacy(GameEngine game) => Need(Enumerable.Range(0, game.PlayerCount).All(viewer =>
        game.CreateSnapshot(viewer).Players.Where(player => player.Seat != viewer).All(player => player.Hand.Count == 0)),
        "The same-color selection and native responses retain every viewer's foreign-hand privacy.");
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint()), game.CardMovements,
        Frames = game.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())).ToArray(),
        Views = Enumerable.Range(0, game.PlayerCount).Select(viewer => SnapshotJson.Serialize(game.CreateSnapshot(viewer))).ToArray(),
        Facts = game.Events.Select(envelope => JsonSerializer.Serialize(envelope.Payload, envelope.Payload.GetType())).ToArray()
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(State(game) == State(restored), "Cold replay retains the exact conversion parent, native Use, target exclusion, private response, physical movements and accepted commands.");
        return restored;
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture:shanhe-s5-yuanshao", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            var catalog = SkillProgramCatalog.Load(
                $$"""{"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"{{Shield}}","revision":1,"cardPolicies":[{"id":"prohibit-arrow","kind":"prohibitTarget","cardKinds":["arrowBarrage"]}]}]}""",
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [Shield] = new { name = "固定禁选目标", description = "验证万箭仅减少一个合法目标" } } }));
            builder.AddSkill(new(Shield, "固定禁选目标", "真实万箭目标禁止") { Program = catalog.Programs[Shield], ProgramPresentation = catalog.Presentations[Shield] });
            builder.AddGeneral(new(Ally, "同行", "", "standard:none", "shu", 8));
            builder.AddGeneral(new(Enemy, "两血守军", "", "standard:none", "wei", 2));
            builder.AddGeneral(new(Shielded, "禁选守军", "", Shield, "wei", 8));
            builder.AddDeck(new("fixture:shanhe-yuanshao-deck", "固定闪牌堆", 0, 0, [new("standard:dodge", 40)]));
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "真实同色乱击", 4, 4, new Dictionary<string, int>(), "fixture:shanhe-yuanshao-deck",
                GeneralPoolIds: [StandardShanheS5YuanshaoPackage.YuanshaoId, Ally, Enemy, Shielded], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
