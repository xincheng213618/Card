using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5ZaiqiChecks
{
    private const string Skill = StandardShanheCampaignCatalog.S5ZaiqiSkillId;
    private const string Reward = StandardShanheCampaignCatalog.S5ZaiqiRewardId;
    private const string Owner = "fixture:s5-zaiqi-owner", Deck = "fixture:s5-zaiqi-deck";
    private const string Gain = "fixture:s5-zaiqi-gain", Recovery = "fixture:s5-zaiqi-recovery";
    private const string MoveReason = "skill-program." + Skill + ".MoveBoundCards";
    private static readonly Lazy<ContentRegistry> Production = new(() =>
        StandardShanheCampaignCatalog.CreateBattleRegistry(ContentSetup([Skill])));

    public static void LostHpPlusOneRevealsRealCardsAndReturnsGainAndRecoveryChildrenCold()
    {
        foreach (var scene in new[] { "hearts", "black" })
        {
            var s = Scene.Start(scene);
            s.Until(() => s.Optional());
            Need(s.V().Hp == 4 && s.V().MaxHp == 6 && s.V().HandCount == 0 && s.Reveals().Length == 0,
                "The wounded owner chooses a real draw-phase replacement before any normal draw or reveal.");
            s.Cold(); s.Answer(choice => Action(choice) == "activate");
            var watch = scene == "hearts" ? Recovery : Gain;
            s.Until(() => s.P?.SkillPrompt?.SkillId == watch && s.P.Choices.Any(IsContinue));
            var reveal = s.Reveals().Single();
            var ids = reveal.Cards.Select(card => card.Id).ToArray();
            Need(ids.Length == 3 && ids.Distinct().Count() == 3 && reveal.OwnerSeat == 0 &&
                reveal.Cards.All(card => card.Suit == (scene == "hearts" ? Suit.Heart : Suit.Spade)) &&
                s.G.CardMovements.Where(move => ids.Contains(move.CardId) && move.From == CardLocation.DrawPile &&
                    move.To == CardLocation.Processing).Select(move => move.CardId).SequenceEqual(ids) && s.NormalDraws().Length == 0,
                "The actual new 887 instruction reveals lost HP plus one original entity (three, not classic 116's two) and replaces the normal draw.");
            if (scene == "hearts")
            {
                var child = s.G.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
                Need(child.Change is { Kind: HpChangeKind.Recovery, SourceSeat: 0, TargetSeat: 0, Amount: 2, HpBefore: 4, HpAfter: 6 } &&
                    child.Change.ParentFrameId == reveal.FrameId && s.V().Hp == 6 && s.V().HandCount == 0 &&
                    s.E<RecoveryAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 0, Amount: 2, RemainingHp: 6 }] &&
                    s.E<ProgramRecoveryAppliedEvent>().Single().ProgramFrameId == reveal.FrameId,
                    "Three genuine hearts recover only the actual two missing HP and enter the original native recovery child before its parent continues.");
            }
            else
            {
                var child = s.G.ResolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single();
                Need(child.Batch.ParentFrameId == reveal.FrameId && child.Batch.NativeDrawInvocation is null &&
                    child.Batch.Movements.Select(move => move.CardId).SequenceEqual(ids) &&
                    child.Batch.Movements.All(move => move.From == CardLocation.Processing &&
                        move.To == CardLocation.Hand(0) && move.Reason.Value == MoveReason) &&
                    s.V().Hand.Select(card => card.Id).Order().SequenceEqual(ids.Order()) && s.V().Hp == 4 &&
                    s.E<RecoveryAppliedEvent>().Length == 0,
                    "Non-hearts enter the real owner's hand in the exact reveal-owned movement batch; obtaining them is not a fake normal draw or recovery.");
            }
            s.Cold(); s.Answer(IsContinue); s.Play();
            Need(s.Reveals().Length == 1 && s.NormalDraws().Length == 0 &&
                s.E<ProgramBindingResolvedEvent>().Count(fact => fact.SkillId == Skill && fact.Completed) == 1 &&
                s.E<ProgramBindingResolvedEvent>().Count(fact => fact.SkillId == watch && fact.Completed) == 1 &&
                ids.All(id => s.G.CardMovements.Count(move => move.CardId == id && move.From == CardLocation.Processing &&
                    move.To == (scene == "hearts" ? CardLocation.DiscardPile : CardLocation.Hand(0)) && move.Reason.Value == MoveReason) == 1) &&
                s.V().Hp == (scene == "hearts" ? 6 : 4) && s.V().HandCount == (scene == "hearts" ? 0 : 3) &&
                s.E<RecoveryAppliedEvent>().Length == (scene == "hearts" ? 1 : 0),
                "Returning from an in-flight native gain or recovery child neither reveals, recovers, moves materials nor completes the replacement twice: " + s.Diagnostic());
            s.Cold();
        }
    }

    public static void FullHpAndDeclinedReplacementKeepNormalDrawAndVerifiedCodexReward()
    {
        foreach (var scene in new[] { "full", "decline" })
        {
            var s = Scene.Start(scene);
            if (scene == "decline")
            {
                s.Until(() => s.Optional()); s.Cold(); s.Answer(choice => Action(choice) == "skip");
            }
            s.Play();
            Need(s.Reveals().Length == 0 && s.E<RecoveryAppliedEvent>().Length == 0 && s.NormalDraws().Length == 2 &&
                s.V().HandCount == 2 && s.V().Hp == (scene == "full" ? 6 : 4) &&
                (scene != "full" || s.E<ProgramBindingStartedEvent>().All(fact => fact.SkillId != Skill)),
                "A full-health owner receives no replacement offer; a wounded owner's genuine decline also preserves exactly the ordinary two-card draw.");
            s.Cold();
        }

        var current = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var old = StandardShanheCampaignCatalog.CreateCodexDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        var plain = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var reward = current.Rewards.Single(item => item.Id == Reward);
        var stock = current.Nodes.Where(node => node.Kind == ShanheNodeKind.Shop)
            .SelectMany(node => node.Choices ?? []).Where(choice => choice.RewardId == Reward).ToArray();
        Need(StandardShanheCampaignCatalog.CodexRewardIdsByNodeId[55] == Reward && reward.SkillId == Skill && reward.TacticSkillId is null &&
            StandardShanheCampaignCatalog.ShopMetadataByRewardId[Reward] is { SourceRewardId: 3051, Price: 300, Rarity: 2, Kind: "技能" } &&
            stock.Length > 0 && stock.All(choice => choice.GoldCost == 300) &&
            old.Rewards.All(item => item.Id != Reward && item.SkillId != Skill) && plain.Rewards.All(item => item.Id != Reward && item.SkillId != Skill) &&
            !StandardShanheCampaignCatalog.CreateBattleRegistry(ContentSetup([])).Skills.ContainsKey(Skill) &&
            Production.Value.GetSkill(Skill).ImplementationStatus == SkillImplementationStatus.Complete,
            "Only the current codex offers node 55's actual rare skill reward 3051/887 and its 300-copper stock; neither 2024 nor a bare recipe imports it as a tactic.");
        var session = ShanheCampaignSession.Create(current, new(83, current.PlayerGeneralIds[0])
            { UseCodexRewardPool = true, UnlockedRewardIds = [Reward] }, StandardShanheCampaignCatalog.CreateBattleRegistry);
        Need(!session.Snapshot.ExtraSkillIds.Contains(Skill) && !session.Snapshot.TacticIds.Contains(Reward),
            "A registry validation probe and an unlocked codex node do not grant their unearned skill or tactic to the real run.");
        ColdRun(ref session, current);
        for (var step = 0; step < 6 && session.Snapshot.Phase != ShanheCampaignPhase.Battle; step++)
        {
            if (session.Snapshot.Phase == ShanheCampaignPhase.Map)
                Submit(session, new MoveShanheNodeCommand(session.Snapshot.AvailableNodes[0].Id, session.Revision));
            else if (session.Snapshot.Phase == ShanheCampaignPhase.NodeChoice)
            {
                var choice = session.Snapshot.NodeChoices.First(option => option.RewardId == "supplies");
                Submit(session, new ChooseShanheNodeOptionCommand(choice.Id, null, session.Revision));
            }
            else throw new InvalidOperationException("Unexpected unearned Zaiqi route: " + session.Snapshot.Phase);
            ColdRun(ref session, current);
        }
        var unearned = session.PendingBattleSetup;
        Need(unearned is not null && !unearned.ExtraSkillIds.Contains(Skill) && !unearned.TacticSkillIds.Contains(Skill) &&
            !session.Snapshot.ExtraSkillIds.Contains(Skill),
            "The first genuinely issued battle recipe excludes an unlocked but unearned 887 skill despite the constructor's registry-only probe.");
        VerifyActualBattle(unearned!, shouldHaveSkill: false);
        VerifySmallRealSkillPurchase(reward);
    }

    private static void VerifySmallRealSkillPurchase(ShanheRewardDefinition reward)
    {
        var source = ContentSetup([]);
        var definition = new ShanheCampaignDefinition("fixture:s5-zaiqi-purchase", "真实再起商店短征程", [source.PlayerGeneralId], ["shop"],
            [new("shop", "只出售已解锁再起的真实商店", ShanheNodeKind.Shop, ["battle"], Choices:
                [new("buy", "传习·再起", GoldCost: 300, RewardId: Reward, LeavesNode: false)]),
             new("battle", "真实购买后的首战", ShanheNodeKind.Battle, [], Encounters:
                [new("actual", "原生队伍战斗", source.EnemyRoster, source.AllyRoster)])],
            [reward], StartingGold: 300, ExtraSkillSlots: 1);
        var run = ShanheCampaignSession.Create(definition, new(83, source.PlayerGeneralId)
            { UseCodexRewardPool = true, UnlockedRewardIds = [Reward] }, StandardShanheCampaignCatalog.CreateBattleRegistry);
        Submit(run, new MoveShanheNodeCommand("shop", run.Revision));
        var item = run.Snapshot.ShopItems.Single(stock => stock.Reward?.Id == Reward);
        Need(item.State == ShanheShopItemState.Available && item.GoldCost == 300 && !item.RequiresSkillReplacement,
            "The real shop publishes the newly unlocked rare skill at its exact source price before payment.");
        ColdRun(ref run, definition); Submit(run, new ChooseShanheNodeOptionCommand(item.ChoiceId, null, run.Revision));
        Need(run.Snapshot.Gold == 0 && run.Snapshot.ExtraSkillIds.SequenceEqual([Skill]) && run.Snapshot.TacticIds.Count == 0 &&
            run.Snapshot.ShopItems.Single(stock => stock.Reward?.Id == Reward).State == ShanheShopItemState.Purchased,
            "A genuine 300-copper purchase uses an extra general-skill slot and never grants a tactic or repeats the product.");
        ColdRun(ref run, definition); Submit(run, new ChooseShanheNodeOptionCommand("$leave", null, run.Revision));
        Submit(run, new MoveShanheNodeCommand("battle", run.Revision)); ColdRun(ref run, definition);
        var setup = run.PendingBattleSetup!;
        Need(setup.ExtraSkillIds.SequenceEqual([Skill]) && setup.TacticSkillIds.Count == 0,
            "Only the actual earned reward reaches the following engine recipe as the exact dedicated 887 extra skill.");
        VerifyActualBattle(setup, shouldHaveSkill: true);
    }

    private static void Submit(ShanheCampaignSession run, ShanheCampaignCommand command)
    {
        var result = run.Submit(command); Need(result.Accepted, result.Error ?? "Actual Zaiqi campaign command rejected.");
    }

    private static void ColdRun(ref ShanheCampaignSession run, ShanheCampaignDefinition definition)
    {
        var before = JsonSerializer.Serialize(run.Snapshot);
        run = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(run.CreateCheckpoint())),
            definition, StandardShanheCampaignCatalog.CreateBattleRegistry);
        Need(before == JsonSerializer.Serialize(run.Snapshot), "Cold campaign recovery retains exact earned skills, stock, price, slots and issued battle recipe.");
    }

    private static void VerifyActualBattle(ShanheBattleSetup setup, bool shouldHaveSkill)
    {
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
        var battle = StandardShanheCampaignCatalog.CreateBattle(setup);
        var result = battle.Submit(new StartGameCommand()); Need(result.Accepted, result.Error?.Message ?? "Actual Zaiqi encounter rejected.");
        Need((battle.CreateSnapshot(0).Players[0].Skills?.Any(skill => skill.ContentId == Skill) == true) == shouldHaveSkill &&
            registry.Skills.ContainsKey(Skill) == shouldHaveSkill,
            "The real native engine grants the new conditional 887 bundle exactly when the earned extra skill is in its encounter recipe.");
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(battle.CreateCheckpoint())), registry);
        Need(Enumerable.Range(0, battle.PlayerCount).All(seat => SnapshotJson.Serialize(battle.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))),
            "The actual purchased or unearned production encounter cold-restores all filtered views without changing acquisition state.");
    }

    private static ShanheBattleSetup ContentSetup(IReadOnlyList<string> skills)
    {
        var definition = StandardShanheCampaignCatalog.CreateDefinition();
        var node = definition.Nodes.First(item => item.Encounters is { Count: > 0 });
        var encounter = node.Encounters![0];
        return new(83, 1, node.Id, "classic:guan-yu", 4, 0, 0, 0, 0, skills, [], [],
            encounter.Enemies, encounter.Allies ?? [], ShanheCampaignContent.GetContentHash(definition));
    }

    private static string Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action") ?? "";
    private static bool IsContinue(PromptChoice choice) => choice.Parameters.GetValueOrDefault("option-id") == "continue";
    private static string Peer(int seat) => "fixture:s5-zaiqi-peer-" + seat;
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Scene(GameEngine game, ContentRegistry registry, string scene)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.CreateSnapshot(0).PendingDecision;
        public PlayerSnapshot V() => G.CreateSnapshot(0).Players[0];
        public T[] E<T>() => G.Events.Select(fact => fact.Payload).OfType<T>().ToArray();
        public ProgramCardsRevealedEvent[] Reveals() => E<ProgramCardsRevealedEvent>().Where(fact => fact.SkillId == Skill).ToArray();
        public CardMovementRecord[] NormalDraws() => G.CardMovements.Where(move => move.From == CardLocation.DrawPile &&
            move.To == CardLocation.Hand(0) && move.Reason == CardMoveReasons.Draw).ToArray();
        public bool Optional() => P?.SkillPrompt?.SkillId == Skill && P.Choices.Any(choice => Action(choice) == "activate");
        public void Accept(GameCommand command)
        {
            var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
            Need(result.Accepted, result.Error?.Message ?? "Actual S5 Zaiqi command rejected.");
        }
        public void Answer(Func<PromptChoice, bool> predicate)
        {
            var prompt = P ?? throw new InvalidOperationException("Missing real Zaiqi prompt.");
            Accept(new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(predicate).Id, G.Revision));
        }
        public void Play() => Until(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        public void Until(Func<bool> done)
        {
            for (var step = 0; step < 100; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 }) throw new InvalidOperationException("Unexpected real Zaiqi boundary: " + Diagnostic());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded real Zaiqi boundary: " + Diagnostic());
        }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
            Facts = G.Events.Select(fact => JsonSerializer.Serialize(fact.Payload, fact.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Commands = CommandJson.Serialize(G.AcceptedCommands)
        });
        public void Cold()
        {
            var before = State();
            G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(before == State(), "Cold replay retains exact revealed entities, real gain/recovery children, private prompts and once-only instruction cursors.");
        }
        public string Diagnostic() => JsonSerializer.Serialize(new
        {
            scene, V().Hp, V().HandCount, Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId },
            Reveals = Reveals(), Recoveries = E<RecoveryAppliedEvent>(), Bindings = E<ProgramBindingResolvedEvent>(),
            Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind })
        });
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene));
            var setup = new ShanheBattleSetup(83, 1, "fixture:s5-zaiqi-" + scene, Owner, scene == "full" ? 6 : 4,
                0, 0, 0, 0, [], [], [], [new(Peer(1)), new(Peer(2)), new(Peer(3))], [], new string('b', 64));
            var game = GameEngine.CreateStandard(new()
            {
                Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
                MaxTurns = 4, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
            }, registry);
            var result = new Scene(game, registry, scene); result.Accept(new StartGameCommand()); return result;
        }
    }

    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-s5-zaiqi", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            builder.AddSkill(Production.Value.GetSkill(Skill));
            var watch = scene == "hearts" ? Recovery : Gain;
            var trigger = scene == "hearts"
                ? "\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"hpChangeOccurrence\":\"perEvent\""
                : "\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + MoveReason + "\"]";
            var programs = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
                  {"id":"{{watch}}","revision":1,"triggers":[{"id":"actual-benefit-child",{{trigger}},"optional":false,
                    "effects":[{"op":"chooseOption","target":"owner","resultBind":"child-return","options":[{"id":"continue"}]}]}]}
                ]}
                """, JsonSerializer.Serialize(new
                {
                    schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                    skills = new Dictionary<string, object> { [watch] = new { name = "真实再起收益子流程", description = "只暂停真正获得牌或恢复体力后的窗口，不制造事件或收益",
                        optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } } }
                }));
            builder.AddSkill(new(watch, programs.Presentations[watch].Name, programs.Presentations[watch].Description)
                { Program = programs.Programs[watch], ProgramPresentation = programs.Presentations[watch] });
            builder.AddGeneral(new(Owner, "S5再起真实使用者", "", Skill, "qun", 6,
                scene is "hearts" or "black" ? [watch] : []));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new(Peer(seat), "无额外技能的真实对手", "", "standard:none", "wei", 6));
            builder.AddDeck(new(Deck, "固定花色实体牌堆", 0, 2, [])
            {
                PhysicalCards = Enumerable.Range(0, 40).Select(_ => new ContentDeckPhysicalCard("standard:dodge",
                    scene == "hearts" ? Suit.Heart : Suit.Spade, 7)).ToArray()
            });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "S5再起真实摸牌替代", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer(1), Peer(2), Peer(3)], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 3 }));
        }
    }
}
