using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheRepeatableTacticChecks
{
    private const string Tactic = "shanhe:jingyu-moulue-2-2026";
    private const string Player = "fixture:repeat-tactic-player", Enemy = "fixture:repeat-tactic-enemy";
    private const string Driver = "fixture:repeat-tactic-driver", Deck = "fixture:repeat-tactic-deck";
    private const string Repeat = "fixture:repeat-tactic-generic", RepeatAlias = "fixture:repeat-tactic-alias";
    private const string Unique = "fixture:repeat-tactic-unique", UniqueAlias = "fixture:repeat-tactic-unique-alias";
    private const string Plain = "fixture:repeat-tactic-plain-unique";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        new(43, 1, "fixture-production-program", "classic:guan-yu", 4, 0, 0, 0, 0, [], [], [],
            [new("standard:cao-cao")], [], new string('e', 64))));
    private static readonly Lazy<ContentRegistry> CurrentRegistry = new(() => ContentRegistry.Build(new StandardContentPackage(), new Fixture()));
    private static readonly Lazy<ContentRegistry> ChangedRegistry = new(() => ContentRegistry.Build(new StandardContentPackage(), new Fixture(ruleDrift: true)));

    public static void JingyuTwoInstancesUsePaidSlashHandBoundaryConversionsAndCold()
    {
        VerifyHandLimitGrammarAndLegacyCanonical();
        var s = Battle.Start();
        Need(s.V.HandCount == 7, "The seven genuine starting entities establish the live post-payment boundary without searching seeds.");
        var first = s.Play(CardKind.Slash);
        Need(s.V.HandCount == 6 && s.Events<DamageAppliedEvent>() is [{ Amount: 1 }] &&
            s.Events<ProgramCardDamageModifiedEvent>().All(e => e.Source.SkillId != Tactic),
            "A paid physical Slash leaves exactly six Hand cards, so neither independent tactic instance qualifies.");
        var converted = s.Play(CardKind.Dodge, conversion: true);
        var pair = s.Events<ProgramCardDamageModifiedEvent>().Where(e => e.Source.SkillId == Tactic).ToArray();
        Need(s.V.HandCount == 5 && s.Events<DamageAppliedEvent>().Last().Amount == 3 && pair.Length == 2 &&
            pair.All(e => e.Source.OwnerSeat == 0 && e.CardKind == CardKind.Slash) &&
            pair.Select(e => e.Source.SkillInstanceId).Distinct(StringComparer.Ordinal).Count() == 2 &&
            pair.Select(e => (e.BaseAmount, e.ModifiedAmount)).SequenceEqual([(1, 2), (2, 3)]),
            "The original Wusheng conversion really pays a red Dodge and leaves five cards; two acquired copies each contribute their own +1 and instance receipt.");
        s.Play(CardKind.Duel);
        s.Use("damage");
        Need(s.Events<DamageAppliedEvent>().TakeLast(2).All(e => e.Amount == 1) &&
            s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Tactic) == 2,
            "A real non-Slash Duel and an actual untyped program damage action receive no bonus despite the low Hand count.");
        s.Play(CardKind.Alcohol);
        Need(s.V.HasAlcoholEffect, "The player must really pay and acquire the native Alcohol enhancement before the elemental Slash.");
        s.Play(CardKind.ThunderSlash);
        Need(s.Events<DamageAppliedEvent>().Last() is { Amount: 4, Nature: DamageNature.Thunder } && !s.V.HasAlcoholEffect &&
            s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Tactic) == 4,
            "The actual Thunder Slash inherits the Slash-family condition and consumes one real Alcohol enhancement while retaining both independent tactic bonuses.");
        s.Use("lose-tactic");
        Need(s.V.Skills?.Any(skill => skill.ContentId == Tactic) != true,
            "The existing native replacement operation removes all owned grants of the specified skill ID, without pretending it removes only one instance.");
        s.Cold(); s.Play(CardKind.Slash);
        Need(s.Events<DamageAppliedEvent>().Last().Amount == 1 &&
            s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Tactic) == 4 &&
            s.G.CardMovements.Count(m => m.CardId == first && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            s.G.CardMovements.Count(m => m.CardId == converted && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1,
            "Real skill loss disables both copies immediately, preserving already-paid physical and converted Slash costs exactly once through cold replay.");
        s.Conserved(); s.Cold();
        VerifyAdditionalLayerDoesNotEnterMaximumHpReplacement();
        VerifyElementalChainInheritsOnlyTheFinalizedFirstDamage();
    }

    public static void CampaignRepeatedAcquisitionsRespectIdentityHashAndKeepAllInstancesCold()
    {
        var choices = new List<ShanheNodeChoiceDefinition>
        {
            new("copy-alias", "同一规则的另一个名称", GoldCost: 1, RewardId: "repeat-alias", LeavesNode: false),
            new("unique", "唯一战法", GoldCost: 2, RewardId: "unique", LeavesNode: false),
            new("unique-alias", "唯一战法别名", GoldCost: 2, RewardId: "unique-alias", LeavesNode: false),
            new("plain", "未声明的普通战法", GoldCost: 2, RewardId: "plain", LeavesNode: false),
            new("plain-alias", "未声明的普通战法奖励别名", GoldCost: 2, RewardId: "plain-alias", LeavesNode: false)
        };
        choices.AddRange(Enumerable.Range(0, 31).Select(index => new ShanheNodeChoiceDefinition("copy-" + index,
            "另一次真实获取", GoldCost: 1, RewardId: "repeat", LeavesNode: false)));
        var definition = new ShanheCampaignDefinition("fixture:repeatable-acquisition", "重复战法机制", [Player], ["first"],
        [
            new("first", "真实首战", ShanheNodeKind.Battle, ["shop"], [new("first", "守军", [new(Enemy)])], ["repeat"]),
            new("shop", "固定商品", ShanheNodeKind.Shop, ["last"], Choices: choices),
            new("last", "真实次战", ShanheNodeKind.Battle, [], [new("last", "守军", [new(Enemy)])], ["repeat", "unique-alias", "plain-alias"])
        ],
        [
            new("repeat", "可重复规则") { TacticSkillId = Repeat },
            new("repeat-alias", "可重复规则别名") { TacticSkillId = RepeatAlias },
            new("unique", "唯一规则") { TacticSkillId = Unique },
            new("unique-alias", "唯一规则别名") { TacticSkillId = UniqueAlias },
            new("plain", "普通唯一规则") { TacticSkillId = Plain },
            new("plain-alias", "普通唯一规则的奖励别名") { TacticSkillId = Plain }
        ], StartingGold: 100, ExtraSkillSlots: 0);
        var session = ShanheCampaignSession.Create(definition, new(47, Player), Registry);
        var initial = session.CreateCheckpoint();
        Need(initial.Revision == 0 && initial.TacticAcquisitionHash is not null && initial.TacticAcquisitionHash == session.TacticAcquisitionHash,
            "Declared canonical acquisition policies have a separate exact checkpoint fingerprint before any command can obscure a rule drift.");
        Throws(() => ShanheCampaignSession.Restore(initial, definition, _ => ChangedRegistry.Value));
        Throws(() => ShanheCampaignSession.Restore(initial with { TacticAcquisitionHash = null }, definition, Registry));
        Throws(() => ShanheCampaignSession.Create(definition, new(47, Player) { EquippedTacticIds = ["repeat", "repeat-alias"] }, Registry));
        var oneInitial = ShanheCampaignSession.Create(definition, new(47, Player) { EquippedTacticIds = ["repeat"] }, Registry);
        Need(oneInitial.Snapshot.TacticIds.SequenceEqual(["repeat"]),
            "A repeatable in-run rule may be carried initially once, while its declared alias cannot bypass the initial canonical-identity uniqueness constraint.");

        Accept(session, new MoveShanheNodeCommand("first", session.Revision));
        CompleteRealBattle(session);
        Accept(session, new ChooseShanheRewardCommand("repeat", null, session.Revision));
        Need(session.Snapshot.TacticIds.SequenceEqual(["repeat"]) && session.Snapshot.Gold == 100,
            "Only a real completed battle issues the first repeatable reward, with no invented credit payment.");
        session = Cold(session, definition);
        Accept(session, new MoveShanheNodeCommand("shop", session.Revision));
        var oldStock = session.Snapshot.ShopItems;
        Accept(session, new ChooseShanheNodeOptionCommand("copy-alias", null, session.Revision));
        Need(session.Snapshot.TacticIds.SequenceEqual(["repeat", "repeat-alias"]) && session.Snapshot.Gold == 99 &&
            oldStock.Single(item => item.ChoiceId == "copy-alias").CanPurchase &&
            session.Snapshot.ShopItems.Single(item => item.ChoiceId == "copy-alias").IsPurchased &&
            session.Snapshot.ShopItems.Single(item => item.ChoiceId == "copy-0").CanPurchase,
            "A separate legitimate purchase of a declared repeatable alias keeps its provenance and pays once without mutating issued stock or hiding other copies.");
        var before = JsonSerializer.Serialize(session.Snapshot);
        Need(!session.Submit(new ChooseShanheNodeOptionCommand("copy-alias", null, session.Revision)).Accepted &&
            JsonSerializer.Serialize(session.Snapshot) == before,
            "Repeatable acquisition does not allow replaying a purchased product choice or paying the same issued purchase twice.");
        for (var index = 0; index < 31; index++)
            Accept(session, new ChooseShanheNodeOptionCommand("copy-" + index, null, session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("unique", null, session.Revision));
        Accept(session, new ChooseShanheNodeOptionCommand("plain", null, session.Revision));
        Need(session.Snapshot.TacticIds.Count == 35 && session.Snapshot.TacticIds.Count(id => id == "repeat") == 32 &&
            session.Snapshot.TacticIds.Count(id => id == "repeat-alias") == 1 && session.Snapshot.Gold == 64 &&
            session.Snapshot.ExtraSkillSlots == 0 && session.Snapshot.ExtraSkillIds.Count == 0 &&
            session.Snapshot.ShopItems.Single(item => item.ChoiceId == "unique-alias").State == ShanheShopItemState.AlreadyOwned &&
            session.Snapshot.ShopItems.Single(item => item.ChoiceId == "plain-alias").State == ShanheShopItemState.AlreadyOwned,
            "Thirty-three genuinely acquired copies exceed the former total cap, while explicit unique identities and undeclared ordinary tactic IDs still reject their aliases without consuming extra-skill slots.");
        before = JsonSerializer.Serialize(session.Snapshot);
        foreach (var unavailable in new[] { "unique-alias", "plain-alias" })
            Need(!session.Submit(new ChooseShanheNodeOptionCommand(unavailable, null, session.Revision)).Accepted &&
                JsonSerializer.Serialize(session.Snapshot) == before,
                "An unavailable unique alias must preserve exact gold, revision and every existing acquisition.");
        Need(session.Snapshot.TacticIds is IList<string> { IsReadOnly: true }, "Published multiplicities remain immutable.");
        session = Cold(session, definition);
        Accept(session, new ChooseShanheNodeOptionCommand("$leave", null, session.Revision));
        Accept(session, new MoveShanheNodeCommand("last", session.Revision));
        var setup = session.PendingBattleSetup!;
        Need(setup.TacticSkillIds.Count == 35 && setup.TacticSkillIds.Count(id => id == Repeat) == 32 &&
            setup.TacticSkillIds.Count(id => id == RepeatAlias) == 1 &&
            !ShanheBattleSetup.ValueEquals(setup, setup with { TacticSkillIds = setup.TacticSkillIds.Skip(1).ToArray() }),
            "The next real encounter recipe preserves every ordered copy and includes multiplicity in structural setup validation.");
        session = Cold(session, definition);
        var game = CreateBattle(setup, Registry(setup));
        Need(game.GetAttackRange(0) == 34,
            "All thirty-three generic repeatable rule instances independently add one to the actual base attack range, proving the mechanism does not special-case the production tactic ID or collapse aliases.");
        var coldBattle = new Battle(game, Registry(setup)); coldBattle.Cold();
        Need(coldBattle.G.GetAttackRange(0) == 34, "Cold battle replay retains all thirty-three independently effective instances.");
        Throws(() => CreateBattle(setup with { TacticSkillIds = [Unique, UniqueAlias] }, Registry(setup)));
        CompleteRealBattle(session, coldBattle.G);
        Need(session.Snapshot.RewardOffers.Select(reward => reward.Id).SequenceEqual(["repeat"]),
            "A repeatable rule remains eligible after acquisition while both forms of already-owned unique aliases stay out of real victory rewards.");
        session = Cold(session, definition);
        Accept(session, new ChooseShanheRewardCommand("repeat", null, session.Revision));
        Need(session.Snapshot.Phase == ShanheCampaignPhase.Won && session.Snapshot.TacticIds.Count == 36 && session.Snapshot.Gold == 64,
            "Another real reward adds the next independent copy instead of being silently truncated or charging an unrelated shop price.");
        _ = Cold(session, definition);
        VerifyUndeclaredLegacyRuleKeepsCheckpointShape();
        VerifyBossGroupsRespectCanonicalAcquisitionAndExplicitConfirmation();
        VerifyInitialOpeningAndTacticIdentitiesRemainSeparate(definition);
    }

    private static void VerifyBossGroupsRespectCanonicalAcquisitionAndExplicitConfirmation()
    {
        static ShanheCampaignDefinition Definition(string first, string second, bool ordinarySkill = false) => new(
            "fixture:repeatable-boss-groups", "双组规则", [Player], ["boss"],
            [new("boss", "真实首领", ShanheNodeKind.BossBattle, ["next"], [new("boss", "守军", [new(Enemy)])])
            {
                ChapterNumber = 1, ChapterTitle = "第一章", BossRewards = new(0, 0,
                    [new("first", "第一组", ["first"], 1), new("second", "第二组", ["second"], 1)])
            }, new("next", "下一场", ShanheNodeKind.Battle, [], [new("next", "守军", [new(Enemy)])])],
            ordinarySkill ? [new("first", "技能甲", SkillId: first), new("second", "技能乙", SkillId: second)] :
                [new("first", "战法甲") { TacticSkillId = first }, new("second", "战法乙") { TacticSkillId = second }],
            ExtraSkillSlots: 1);
        Throws(() => ShanheCampaignSession.Create(Definition(Unique, UniqueAlias), new(53, Player), Registry));
        Throws(() => ShanheCampaignSession.Create(Definition(Plain, Plain, ordinarySkill: true), new(53, Player), Registry));
        var definition = Definition(Repeat, RepeatAlias);
        var session = ShanheCampaignSession.Create(definition, new(53, Player), Registry);
        Accept(session, new MoveShanheNodeCommand("boss", session.Revision)); CompleteRealBattle(session);
        Need(session.Snapshot.BossRewardSummary is { CanConfirm: false } summary &&
            summary.Groups.SelectMany(group => group.Offers).Select(reward => reward.Id).SequenceEqual(["first", "second"]),
            "Different frozen boss groups may offer separate rewards of one repeatable canonical identity, while unique canonical aliases and ordinary skill IDs are rejected before any run can deadlock.");
        Accept(session, new ChooseShanheBossRewardCommand("first", "first", null, session.Revision));
        session = Cold(session, definition);
        Need(session.Snapshot.TacticIds.SequenceEqual(["first"]) && session.Snapshot.BossRewardSummary is { CanConfirm: false } &&
            !session.Submit(new ConfirmShanheBossRewardCommand(session.Revision)).Accepted,
            "Claiming the first copy preserves the second group's issued offer and cannot silently confirm an unfinished boss bundle.");
        Accept(session, new ChooseShanheBossRewardCommand("second", "second", null, session.Revision));
        session = Cold(session, definition);
        Need(session.Snapshot.TacticIds.SequenceEqual(["first", "second"]) &&
            session.Snapshot.BossRewardSummary is { CanConfirm: true } && session.Snapshot.Phase == ShanheCampaignPhase.Reward,
            "Both legitimate reward groups grant their own repeatable copy and still require explicit chapter confirmation.");
        Accept(session, new ConfirmShanheBossRewardCommand(session.Revision)); session = Cold(session, definition);
        Accept(session, new MoveShanheNodeCommand("next", session.Revision));
        var setup = session.PendingBattleSetup!;
        Need(setup.TacticSkillIds.SequenceEqual([Repeat, RepeatAlias]), "The next real encounter carries both claimed canonical-alias copies in acquisition order.");
        var battle = new Battle(CreateBattle(setup, Registry(setup)), Registry(setup));
        battle.Cold();
        Need(battle.G.GetAttackRange(0) == 3, "Both boss-granted instances independently affect the next real battle after cold recovery.");
    }

    private static void VerifyInitialOpeningAndTacticIdentitiesRemainSeparate(ShanheCampaignDefinition source)
    {
        var definition = source with
        {
            ExtraSkillSlots = 1,
            Rewards = [.. source.Rewards,
                new(Repeat, "与战法 canonical 字符串相同的传承奖励") { OpeningSkillRewardIds = ["draft-1", "draft-2", "draft-3"] },
                new("draft-1", "技能甲", SkillId: Plain), new("draft-2", "技能乙", SkillId: Unique),
                new("draft-3", "技能丙", SkillId: RepeatAlias)]
        };
        var session = ShanheCampaignSession.Create(definition, new(47, Player) { EquippedTacticIds = ["repeat", Repeat] }, Registry);
        Need(session.Snapshot.Phase == ShanheCampaignPhase.OpeningSkillDraft && session.Snapshot.OpeningSkillOffers.Count == 3 &&
            session.Snapshot.TacticIds.SequenceEqual(["repeat", Repeat]),
            "An opening-draft reward ID equal to a tactic canonical ID occupies a separate typed identity and cannot incorrectly collide with the equipped real tactic.");
        session = Cold(session, definition);
        Accept(session, new ChooseShanheOpeningSkillCommand(session.Snapshot.OpeningSkillOffers[0].Id, session.Revision));
        Need(session.Snapshot.Phase == ShanheCampaignPhase.Map && session.Snapshot.ExtraSkillIds.Count == 1 &&
            session.Snapshot.TacticIds.SequenceEqual(["repeat", Repeat]), "The distinct opening reward resolves once while preserving the original repeatable tactic.");
        _ = Cold(session, definition);
    }

    private static void VerifyUndeclaredLegacyRuleKeepsCheckpointShape()
    {
        var definition = new ShanheCampaignDefinition("fixture:legacy-tactic-acquisition", "旧唯一规则", [Player], ["shop"],
        [
            new("shop", "旧商店", ShanheNodeKind.Shop, ["battle"], Choices: [new("plain", "旧战法", RewardId: "plain", LeavesNode: false)]),
            new("battle", "旧战斗", ShanheNodeKind.Battle, [], [new("end", "守军", [new(Enemy)])])
        ], [new("plain", "普通战法") { TacticSkillId = Plain }]);
        var session = ShanheCampaignSession.Create(definition, new(47, Player), Registry);
        var checkpoint = session.CreateCheckpoint();
        Need(checkpoint.TacticAcquisitionHash is null &&
            !ShanheCampaignJson.Serialize(checkpoint).Contains("tacticAcquisitionHash", StringComparison.OrdinalIgnoreCase),
            "Content referencing only undeclared ordinary tactic rules keeps its original absent optional checkpoint field.");
        _ = Cold(session, definition);
    }

    private static ShanheCampaignSession Cold(ShanheCampaignSession session, ShanheCampaignDefinition definition)
    {
        var copy = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint())), definition, Registry);
        Need(JsonSerializer.Serialize(copy.Snapshot) == JsonSerializer.Serialize(session.Snapshot) &&
            copy.TacticAcquisitionHash == session.TacticAcquisitionHash && copy.AcceptedCommands.Count == session.AcceptedCommands.Count,
            "Cold expedition replay preserves all ordered acquisitions, stock payments, exact rule fingerprint and command prefix.");
        return copy;
    }

    private static void CompleteRealBattle(ShanheCampaignSession session, GameEngine? prepared = null)
    {
        var setup = session.PendingBattleSetup!;
        var battle = prepared ?? CreateBattle(setup, Registry(setup));
        var flow = new Battle(battle, Registry(setup)); flow.Accept(new StartGameCommand()); flow.ReachPlay();
        var prompt = battle.CreateSnapshot(0).PendingDecision!;
        flow.Accept(new UseProgramSkillCommand(0, Driver, "finish", [], [1], battle.Revision, prompt.PromptId));
        for (var step = 0; step < 80 && battle.State.Status != EngineStatus.Completed; step++)
        {
            var pending = battle.CreateSnapshot(0).PendingDecision;
            if (pending is { PlayerSeat: 0 })
            {
                var decline = pending.Choices.First(choice => choice.Parameters.GetValueOrDefault("response") is "pass" or "let-die");
                flow.Accept(new AnswerPromptCommand(0, pending.PromptId, decline.Id, battle.Revision));
            }
            else flow.Accept(new AdvanceOneStepCommand(battle.Revision));
        }
        Need(battle.State.Status == EngineStatus.Completed && session.CompleteBattle(battle, session.Revision).Accepted,
            "The bounded native HP-loss/death flow must finish a real battle checkpoint before a campaign reward is accepted.");
    }

    private static void Accept(ShanheCampaignSession session, ShanheCampaignCommand command)
    { var result = session.Submit(command); Need(result.Accepted, result.Error ?? "Repeated-tactic campaign command rejected."); }

    private static void VerifyHandLimitGrammarAndLegacyCanonical()
    {
        static JsonObject Skill(string condition, int? limit, string scope = "ownerUsed")
        {
            var modifier = new JsonObject { ["id"] = "legacy", ["cardKinds"] = new JsonArray("slash"),
                ["amount"] = 1, ["condition"] = condition, ["sourceScope"] = scope };
            if (limit is { } value) modifier["sourceHandCountLimit"] = value;
            return new() { ["id"] = "fixture:repeat-hand-limit", ["revision"] = 1, ["damageModifiers"] = new JsonArray(modifier) };
        }
        static SkillProgram Load(JsonObject skill) => LoadFixtureRules(new JsonObject
        { ["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion, ["skills"] = new JsonArray(skill) }.ToJsonString()).Programs["fixture:repeat-hand-limit"];
        Need(Load(Skill("sourceHandBelowLimit", 6)).DamageModifiers.Single().SourceHandCountLimit == 6,
            "The shared grammar keeps a positive explicit owner-used Hand threshold.");
        Throws(() => Load(Skill("sourceHandBelowLimit", null)));
        Throws(() => Load(Skill("sourceHandBelowLimit", 0)));
        Throws(() => Load(Skill("always", 6)));
        Throws(() => Load(Skill("sourceHandBelowLimit", 6, "damageSource")));
        const string canonical = """{"damageModifiers":[{"amount":1,"cardKinds":["slash"],"condition":"always","id":"legacy","sourceScope":"ownerUsed"}],"id":"fixture:repeat-hand-limit","revision":1}""";
        var legacy = Load(Skill("always", null));
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(SkillProgramCatalog.RuntimeVersion + "\n" + canonical))).ToLowerInvariant();
        Need(legacy.GameplayHash == expected && legacy.DamageModifiers.Single().SourceHandCountLimit is null &&
            !JsonSerializer.Serialize(legacy.DamageModifiers.Single()).Contains("SourceHandCountLimit", StringComparison.Ordinal),
            "Omitting the additive optional threshold preserves the old canonical gameplay bytes and hash without injecting a new default field.");
    }

    private static void VerifyAdditionalLayerDoesNotEnterMaximumHpReplacement()
    {
        var s = Battle.Start(replacement: true);
        s.Play(CardKind.Alcohol); s.Play(CardKind.Slash);
        Need(s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Single().Receipt.Amount == 2 &&
            s.Events<DamageAppliedEvent>().Length == 0 && s.Events<ProgramCardDamageModifiedEvent>().All(e => e.Source.SkillId != Tactic),
            "The real first-Slash maximum-HP replacement includes native Alcohol but excludes the two additional-damage tactic instances under the explicit local layer interpretation.");
        s.Cold(); s.Play(CardKind.Slash);
        Need(s.Events<CardDamageReplacedWithMaximumHpLossEvent>().Length == 1 &&
            s.Events<DamageAppliedEvent>().Single().Amount == 3 && s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Tactic) == 2,
            "After the real round allowance is consumed, the next actual Slash applies both independent tactic bonuses normally.");
        s.Conserved(); s.Cold();
    }

    private static void VerifyElementalChainInheritsOnlyTheFinalizedFirstDamage()
    {
        var s = Battle.Start(chained: true);
        s.Play(CardKind.IronChain, targets: [0, 1]);
        Need(s.G.CreateSnapshot(0).Players.All(player => player.IsChained) &&
            s.Events<IronChainResolvedEvent>().Single().TargetSeats.SequenceEqual([0, 1]),
            "A real two-target Iron Chain pays its physical material and links both the source and actual enemy.");
        s.Cold(); s.Play(CardKind.ThunderSlash);
        var damage = s.Events<DamageAppliedEvent>();
        Need(damage.Length == 2 && damage[0] is { TargetSeat: 1, Amount: 3, Nature: DamageNature.Thunder } &&
            damage[1] is { TargetSeat: 0, Amount: 3, Nature: DamageNature.Thunder } &&
            s.Events<ChainedDamagePropagatedEvent>() is [{ TargetSeat: 0, Amount: 3 }] &&
            s.Events<ProgramCardDamageModifiedEvent>().Count(e => e.Source.SkillId == Tactic) == 2 &&
            s.G.CreateSnapshot(0).Players.All(player => !player.IsChained),
            "Both copies apply only to the genuine first Slash damage; the native elemental link inherits finalized three without reapplying either instance to the source recipient.");
        s.Conserved(); s.Cold();
    }

    private sealed class Battle(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PlayerSnapshot V => G.CreateSnapshot(0).Players.Single(player => player.Seat == 0);
        private PendingDecision? Prompt => Enumerable.Range(0, 2).Select(seat => G.CreateSnapshot(seat).PendingDecision).FirstOrDefault(prompt => prompt is not null);
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(command); Need(result.Accepted, result.Error?.Message ?? "Repeated-tactic battle command rejected."); }
        public void ReachPlay()
        {
            for (var step = 0; step < 80; step++)
            {
                if (Prompt is { PlayerSeat: 0, Kind: DecisionKind.PlayCard }) return;
                Need(G.State.Winner == Winner.None && Prompt?.PlayerSeat != 0, "Unexpected human or completed repeated-tactic boundary: " + JsonSerializer.Serialize(Prompt));
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded repeated-tactic play prompt.");
        }
        public int Play(CardKind printedKind, bool conversion = false, int[]? targets = null)
        {
            var action = G.GetHumanLegalActions().First(a => a.CardId is { } id && V.Hand.Any(card => card.Id == id && card.Kind == printedKind &&
                (!conversion || card.Suit == Suit.Heart)) && (conversion ? a.ConversionSource?.SkillId == "classic:wusheng" : a.ConversionSource is null) &&
                (targets is null || a.TargetSeats.SequenceEqual(targets)));
            var cardId = action.CardId!.Value;
            Accept(new PlayCardCommand(0, cardId, action.TargetSeats, G.Revision, Prompt!.PromptId, action.PlayedCardKind, action.TargetCardId)
            { ConversionSource = action.ConversionSource });
            Cold(); ReachPlay(); return cardId;
        }
        public void Use(string activation)
        { Accept(new UseProgramSkillCommand(0, Driver, activation, [], activation == "damage" ? [1] : [], G.Revision, Prompt!.PromptId)); ReachPlay(); }
        private string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 2).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
            Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), G.CardMovements,
            Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint())
        });
        public void Cold()
        { var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(State() == before, "Cold replay retains each independent tactic grant, actual Hand boundary, native use and paid card exactly."); }
        public void Conserved()
        { var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Every real card retains exactly one zone."); }
        public static Battle Start(bool replacement = false, bool chained = false)
        {
            var cards = chained ? new ShanheStartingCard[] { new(CardKind.IronChain), new(CardKind.ThunderSlash) } :
                replacement ? [new(CardKind.Alcohol), new(CardKind.Slash), new(CardKind.Slash)] :
                [new(CardKind.Slash), new(CardKind.Slash), new(CardKind.Duel), new(CardKind.ThunderSlash), new(CardKind.Alcohol),
                    new(CardKind.Dodge, Suit.Heart, 8), new(CardKind.Dodge, Suit.Club, 9)];
            var setup = new ShanheBattleSetup(41, 1, "fixture-repeated-tactic", Player, 12, 0, 0, 0, 5,
                replacement ? ["shanhe:xiaoming"] : [], cards, [], [new(Enemy)], [], new string('f', 64))
            { TacticSkillIds = [Tactic, Tactic] };
            var registry = Registry(setup);
            var s = new Battle(CreateBattle(setup, registry), registry); s.Accept(new StartGameCommand()); s.ReachPlay(); return s;
        }
    }

    private static ContentRegistry Registry(ShanheBattleSetup setup) => CurrentRegistry.Value;
    private static GameEngine CreateBattle(ShanheBattleSetup setup, ContentRegistry registry) => GameEngine.CreateStandard(new()
    {
        Seed = setup.BattleSeed, PlayerCount = 2, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
        ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false,
        AdvanceAfterHumanCommands = false, MaxTurns = 4
    }, registry);

    private sealed class Fixture(bool ruleDrift = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-repeatable-tactic", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { Tactic, "shanhe:xiaoming", "classic:wusheng" }) builder.AddSkill(Production.Value.GetSkill(id));
            var rules = $$$"""
            {"schemaVersion":{{{SkillProgramCatalog.RulesSchemaVersion}}},"skills":[
              {"id":"{{{Driver}}}","revision":1,"activations":[
                {"id":"damage","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"finish","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"selectedTarget","amount":20}]},
                {"id":"lose-tactic","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["{{{Tactic}}}"],"sourceBind":"standard:none"}]}]},
              {"id":"{{{Repeat}}}","revision":1,"modifiers":[{"id":"range","query":"attackRange","operation":"add","value":1,"priority":0}]},
              {"id":"{{{RepeatAlias}}}","revision":1,"modifiers":[{"id":"range","query":"attackRange","operation":"add","value":1,"priority":0}]},
              {"id":"{{{Unique}}}","revision":1,"modifiers":[{"id":"hand","query":"handLimit","operation":"add","value":1,"priority":0}]},
              {"id":"{{{UniqueAlias}}}","revision":1,"modifiers":[{"id":"hand","query":"handLimit","operation":"add","value":1,"priority":0}]},
              {"id":"{{{Plain}}}","revision":1,"modifiers":[{"id":"hand","query":"handLimit","operation":"add","value":1,"priority":0}]}
            ]}
            """;
            var catalog = LoadFixtureRules(rules);
            foreach (var (id, program) in catalog.Programs)
                builder.AddSkill(new(id, id, "真实重复战法机制夹具")
                {
                    Program = program,
                    CampaignTacticAcquisition = id is Repeat or RepeatAlias ? new(Repeat, Repeatable: !ruleDrift) :
                        id is Unique or UniqueAlias ? new(Unique) : null
                });
            builder.AddGeneral(new(Player, "战法使用者", "", Driver, "shu", 12, ["classic:wusheng"]));
            builder.AddGeneral(new(Enemy, "真实守军", "", "standard:none", "qun", 20));
            builder.AddDeck(new(Deck, "零初手真实牌堆", 0, 0, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, i % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "重复战法真实遭遇", 2, 2, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Player, Enemy], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = 1, [ShanheBattleSetup.EnemyTeamId] = 1 }));
        }
    }

    private static SkillProgramCatalog LoadFixtureRules(string rules)
    {
        using var document = JsonDocument.Parse(rules);
        var ids = document.RootElement.GetProperty("skills").EnumerateArray()
            .Select(skill => skill.GetProperty("id").GetString()!).ToArray();
        return SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new
        {
            schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
            skills = ids.ToDictionary(id => id, id => new { name = id, description = "真实重复战法机制夹具" })
        }));
    }

    private static void Throws(Action action)
    { try { action(); } catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; } throw new InvalidOperationException("Invalid repeated-tactic input was accepted."); }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
