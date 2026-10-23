using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5YuanshuChecks
{
    private const string Actor = "fixture:s5-yuanshu-actor", Driver = "fixture:s5-yuanshu-driver", Deck = "fixture:s5-yuanshu-deck";

    public static void TongjiActualRangeHandThresholdAndNativeVirtualSlashRecoverCold()
    {
        var registry = ComposedSkillContentRegistry.CreateShowcase(additionalPackages:
            [new StandardShanheS5YuanshuPackage(), new Fixture()]);
        var setup = new ShanheBattleSetup(113, 1, "fixture-s5-yuanshu", Actor, 8, 0, 0, 0, 0, [],
            [new(CardKind.ZhangbaSerpentSpear), new(CardKind.Slash), new(CardKind.Slash)], [],
            [new(StandardShanheS5YuanshuPackage.YuanyaoId) { StartingHand = [] },
             new(StandardShanheS5YuanshuPackage.YuanshuId) { StartingHand = Enumerable.Range(0, 4).Select(_ => new ShanheStartingCard(CardKind.Dodge)).ToArray() },
             new(StandardShanheS5YuanshuPackage.YuanyinId) { StartingHand = [] }], [], new string('a', 64));
        var g = Start(setup); Play(g);
        Need(V(g, 2).Hp == 4 && V(g, 2).HandCount == 4 && SlashActions(g).Any(a => a.TargetSeats.Contains(1)),
            "The complete three-enemy roster begins with hand equal to Yuan Shu's HP, so Tongji's strict greater-than condition is inactive.");
        Accept(g, new UseProgramSkillCommand(0, Driver, "lower", [], [2], g.Revision, Pending(g)!.PromptId)); Play(g);
        Need(V(g, 2).Hp == 3 && V(g, 2).HandCount == 4 && g.GetAttackRange(0) == 1 && g.GetCombatDistance(0, 2) == 2 &&
            SlashActions(g).Any(a => a.TargetSeats.Contains(1)),
            "Actual non-card damage enables Tongji's hand threshold, but an owner beyond this actor's real attack range imposes no target restriction.");
        var spear = g.GetHumanLegalActions().Single(a => a.CardId is { } id && V(g, 0).Hand.Any(c => c.Id == id && c.Kind == CardKind.ZhangbaSerpentSpear));
        Begin(g, spear); Play(g);
        Need(g.GetAttackRange(0) == 3 && V(g, 0).Equipment.Any(c => c.Kind == CardKind.ZhangbaSerpentSpear) &&
            SlashActions(g).Any(a => a.CardId is > 0) && SlashActions(g).Any(a => a.ConversionSource?.SkillId == "ol:weijing") &&
            SlashActions(g).All(a => a.TargetSeats.SequenceEqual([2])),
            "The real equipped Spear brings Yuan Shu into range, restricting both physical Slash and the existing cardless Weijing Slash to that owner.");
        g = Cold(g, registry);
        var native = SlashActions(g).First(a => a.CardId is > 0);
        var virtualSlash = SlashActions(g).Single(a => a.ConversionSource?.SkillId == "ol:weijing");
        var before = State(g);
        Need(!g.Submit(new PlayCardCommand(0, native.CardId!.Value, [1], g.Revision, Pending(g)!.PromptId, native.PlayedCardKind)
            { ConversionSource = native.ConversionSource }).Accepted && State(g) == before,
            "A forged physical Slash cannot bypass the live Tongji target domain or pay material.");
        Need(!g.Submit(new PlayCardCommand(0, virtualSlash.CardId!.Value, [1], g.Revision, Pending(g)!.PromptId, virtualSlash.PlayedCardKind)
            { ConversionSource = virtualSlash.ConversionSource }).Accepted && State(g) == before,
            "A forged zero-entity Slash cannot bypass the same target domain or consume its round usage.");
        Begin(g, virtualSlash);
        Until(g, () => Pending(g) is { PlayerSeat: 2, Kind: DecisionKind.RespondDodge });
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.RoundOnceCardlessBasicUse is not null);
        Need(use.TargetSeats.SequenceEqual([2]) && g.CreateSnapshot(0).PendingDecision is null &&
            g.CreateSnapshot(1).PendingDecision is null && g.CreateSnapshot(3).PendingDecision is null,
            "The genuine cardless Slash targets Yuan Shu and its physical Dodge choice stays private to that native responder.");
        g = Cold(g, registry);
        // A native NPC response is submitted through that seat's ordinary AI.
        Accept(g, new AdvanceOneStepCommand(g.Revision)); Play(g);
        Need(Events<CardRespondedEvent>(g).Any(e => e.ResponderSeat == 2 && e.EffectiveCardKind == CardKind.Dodge) &&
            V(g, 2).Hp == 3 && V(g, 2).HandCount == 3 &&
            g.CardMovements.Count(m => m.From == CardLocation.Hand(2) && m.Reason == CardMoveReasons.Respond) == 1 &&
            SlashActions(g).Any(a => a.TargetSeats.SequenceEqual([1])),
            "The actual Dodge pays one hand card, returning hand count to HP and immediately releasing Tongji's restriction.");
        var physical = SlashActions(g).First(a => a.CardId is > 0 && a.TargetSeats.SequenceEqual([1]));
        Begin(g, physical); Play(g);
        Need(Events<DamageAppliedEvent>(g).Last() is { SourceSeat: 0, TargetSeat: 1, Amount: 1, SourceCardUseResolutionId: not null } &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == physical.CardId).Location == CardLocation.DiscardPile,
            "After the condition ends, a real physical Slash may target the supporting general and completes its normal damage and material cleanup.");
        _ = Cold(g, registry);

        // Reuse this same roster and fixture through an actual skill-native
        // Slash entry, which does not pass the ordinary Play action filter.
        g = Start(setup with
        {
            StartingCards = [new(CardKind.Slash), new(CardKind.Slash)], StartingEquipment = [new(CardKind.ZhangbaSerpentSpear)],
            ExtraSkillIds = ["ol:zongluan"], EnemyRoster = setup.EnemyRoster.Select((enemy, index) => index == 1 ? enemy with { Hp = 3 } : enemy).ToArray()
        });
        Until(g, () => Pending(g)?.SkillPrompt?.SkillId == "ol:zongluan");
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Until(g, () => Pending(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("tao-qian") == "actor") == true);
        Answer(g, c => c.Parameters.GetValueOrDefault("tao-qian") == "actor" && c.Targets.SequenceEqual([0]));
        var choices = Pending(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("tao-qian") == "slash-toggle").ToArray();
        Need(choices.Length == 1 && choices[0].Targets.SequenceEqual([2]),
            "The actual Zongluan native target draft also excludes every target except the active in-range Tongji owner.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("tao-qian") == "slash-toggle" && c.Targets.SequenceEqual([2]));
        Answer(g, c => c.Parameters.GetValueOrDefault("tao-qian") == "slash-confirm");
        Until(g, () => Pending(g) is { PlayerSeat: 2, Kind: DecisionKind.RespondDodge });
        var skillUse = g.ResolutionStack.OfType<CardUseFrame>().Single(f => f.ZongluanSlash is not null);
        Need(skillUse.TargetSeats.SequenceEqual([2]) && skillUse.PhysicalCardIds is { Count: 0 } &&
            Events<ZongluanSlashIssuedEvent>(g).Single().Origin.UseId == skillUse.Id,
            "The paid native skill issues one real zero-entity Slash against its sole legal target.");
        g = Cold(g, registry); Accept(g, new AdvanceOneStepCommand(g.Revision));
        Until(g, () => Pending(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard } ||
            Pending(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("tao-qian") == "discard-confirm") == true);
        if (Pending(g)?.Kind != DecisionKind.PlayCard) Answer(g, c => c.Parameters.GetValueOrDefault("tao-qian") == "discard-confirm");
        Play(g);
        Need(Events<ZongluanSlashReturnedEvent>(g).Single() is { DamageRecipientCount: 0 } returned && returned.UseId == skillUse.Id &&
            Events<CardRespondedEvent>(g).Any(e => e.ResponderSeat == 2 && e.EffectiveCardKind == CardKind.Dodge),
            "The native-only Slash and actual Dodge return normally without bypassing Tongji or inventing damage/discard obligations.");
        _ = Cold(g, registry);

        GameEngine Start(ShanheBattleSetup recipe)
        {
            var game = GameEngine.CreateStandard(new()
            {
                Seed = recipe.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
                ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = recipe, UseInteractiveSetup = false,
                UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 4
            }, registry);
            Accept(game, new StartGameCommand()); return game;
        }
    }

    private static PlayerSnapshot V(GameEngine game, int seat) => game.CreateSnapshot(seat).Players[seat];
    private static PendingDecision? Pending(GameEngine game) => Enumerable.Range(0, game.PlayerCount)
        .Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static IEnumerable<LegalAction> SlashActions(GameEngine game) => game.GetHumanLegalActions().Where(a => a.Kind == LegalActionKind.Slash);
    private static T[] Events<T>(GameEngine game) => game.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine game, GameCommand command)
    { var result = game.Submit(command); Need(result.Accepted, result.Error?.ToString() ?? "The actual Tongji command was rejected."); }
    private static void Answer(GameEngine game, Func<PromptChoice, bool> predicate)
    { var pending = Pending(game)!; Accept(game, new AnswerPromptCommand(pending.PlayerSeat, pending.PromptId, pending.Choices.First(predicate).Id, game.Revision)); }
    private static void Begin(GameEngine game, LegalAction action) => Accept(game, new PlayCardCommand(0, action.CardId!.Value,
        action.TargetSeats, game.Revision, Pending(game)!.PromptId, action.PlayedCardKind, action.TargetCardId)
        { ConversionSource = action.ConversionSource });
    private static void Play(GameEngine game) => Until(game, () => Pending(game) is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 });
    private static void Until(GameEngine game, Func<bool> done)
    {
        for (var n = 0; n < 120; n++)
        {
            if (done()) return;
            Need(game.State.Winner == Winner.None, "The bounded Tongji fixture must keep its complete roster alive.");
            Need(Pending(game)?.PlayerSeat != 0, "Unexpected human Tongji boundary: " + JsonSerializer.Serialize(Pending(game)));
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The bounded native Tongji continuation was not reached.");
    }
    private static string State(GameEngine game) => JsonSerializer.Serialize(new
    {
        Views = Enumerable.Range(0, game.PlayerCount).Select(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat))),
        Frames = game.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())),
        Events = game.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), game.CardMovements,
        Zones = game.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(game.CreateCheckpoint())
    });
    private static GameEngine Cold(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Need(State(restored) == State(game), "Cold replay preserves Tongji's real range, live hand threshold, native response and complete three-enemy roster.");
        return restored;
    }

    private sealed class Fixture : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-s5-yuanshu", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            var catalog = SkillProgramCatalog.Load($$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[{"id":"{{{Driver}}}","revision":1,
              "modifiers":[{"id":"slash-quota","query":"slashLimit","operation":"add","value":4,"priority":0}],
              "activations":[{"id":"lower","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,
                "effects":[{"op":"damage","target":"selectedTarget","amount":1,"sourceRef":{"kind":"owner"}}]}]}]}
            """, JsonSerializer.Serialize(new
            {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = new Dictionary<string, object> { [Driver] = new { name = "真实伤害", description = "改变同疾持有者实际体力" } }
            }));
            b.AddSkill(new(Driver, catalog.Presentations[Driver].Name, catalog.Presentations[Driver].Description)
                { Program = catalog.Programs[Driver], ProgramPresentation = catalog.Presentations[Driver] });
            b.AddGeneral(new(Actor, "原生杀使用者", "supporter", Driver, "qun", 8, ["ol:weijing"]));
            b.AddDeck(new(Deck, "最小固定牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 32).Select(_ => new ContentDeckPhysicalCard("standard:slash", Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "同疾完整三敌", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Actor, StandardShanheS5YuanshuPackage.YuanyaoId, StandardShanheS5YuanshuPackage.YuanshuId, StandardShanheS5YuanshuPackage.YuanyinId],
                ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
