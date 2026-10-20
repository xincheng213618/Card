using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheFarStrikeChecks
{
    private const string Skill = "shanhe:current-yuanji-2129", Reward = "tactic-yuanji-current-2129";
    private const string Owner = "fixture:far-strike-owner", Peer = "fixture:far-strike-peer", Driver = "fixture:far-strike-driver";
    private const string Watch = "fixture:far-strike-paid", HpGate = "fixture:far-strike-hp-grant", Loss = "fixture:far-strike-loss";
    private const string AiSource = "fixture:far-strike-ai-source", AiGeneral = "fixture:far-strike-ai-general";
    private const string Deck = "fixture:far-strike-deck", Mashu = "classic:mashu", Fenxun = "ol:fenxun";
    private static readonly Lazy<ContentRegistry> Production = new(() => StandardShanheCampaignCatalog.CreateBattleRegistry(
        ProductionSetup() with { TacticSkillIds = [Skill] }));

    public static void NativeAndProgramDamageUseFinalDirectedDistanceArmorAndPublicPredictionCold()
    {
        VerifyConditionalRegistryAndEditionIsolation();
        VerifyDistanceConditionLoaderBoundaries();
        var s = Scene.Start("native");
        Need(s.G.GetCombatDistance(0, 1) == 1 && s.G.GetCombatDistance(0, 2) == 2,
            "The real four-seat table provides both sides of the strict final-distance boundary.");
        var near = s.Action(CardKind.Slash, [1]); var far = s.Action(CardKind.Slash, [2]); var before = s.State();
        Need(s.G.PredictPublicActionDamage(0, near, 1).DamageAmount == 1 &&
            s.G.PredictPublicActionDamage(0, far, 2).DamageAmount == 2 && s.State() == before,
            "Pure public estimates distinguish actual near and far physical Slash actions without moving cards, advancing a frame or consuming the source.");
        var nearCard = s.BeginCard(near); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 1, Amount: 1 }] && s.Modifiers().Length == 0,
            "A genuine distance-one physical Slash receives no Far Strike bonus.");
        s.Cold(); s.Continue(); s.Play();

        s.Use("armor-two", [2]); far = s.Action(CardKind.Slash, [2]); before = s.State();
        Need(s.G.PredictPublicActionDamage(0, far, 2) is { DamageAmount: 2, ArmorAbsorbed: 2, HpLossAmount: 0, HpAfter: 20 } &&
            s.State() == before, "The public far-hit estimate retains full damage while Armor pays the two points before HP.");
        var farCard = s.BeginCard(far); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 2, Amount: 2,
                ArmorPayment: { DamageAmount: 2, ArmorBefore: 2, ArmorAbsorbed: 2, HpLossAmount: 0 } } &&
            s.V(2) is { Hp: 20, Armor: 0 } && s.Modifiers() is [{ TargetSeat: 2, CardKind: CardKind.Slash, BaseAmount: 1, ModifiedAmount: 2 }],
            "The actual far physical Slash adds once, then genuinely consumes Armor without erasing its two-point damage fact.");
        Need(s.Modifiers().Single().Source is { OwnerSeat: 0, BindingId: "far-source-damage" } source &&
            source.SkillInstanceId == "campaign:shanhe-tactic:" + Skill,
            "The actual modifier names the equipped tactic's original acquisition instance and production binding, rather than a printed or extra-skill substitute.");
        s.Cold(); s.Continue(); s.Play();
        Need(s.Events<AfterDamageEvent>().Last() is { TargetSeat: 2, Amount: 2 },
            "Full Armor absorption preserves the full native AfterDamage amount after the paid child returns.");

        var duel = s.Action(CardKind.Duel, [2]); before = s.State();
        Need(s.G.PredictPublicActionDamage(0, duel, 2).DamageAmount == 2 && s.State() == before,
            "The same pure public condition also applies to a real non-Slash Duel.");
        var duelCard = s.BeginCard(duel); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 2, Amount: 2 } &&
            s.Modifiers().Last() is { CardKind: CardKind.Duel, BaseAmount: 1, ModifiedAmount: 2 },
            "An actual ordinary Duel is not incorrectly restricted to the Slash family.");
        s.Cold(); s.Continue(); s.Play(); s.Begin("hit", [2]); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Last() is { SourceSeat: 0, TargetSeat: 2, Amount: 2, SourceLess: false } &&
            s.Modifiers().Last() is { CardKind: null, TargetSeat: 2, BaseAmount: 1, ModifiedAmount: 2 },
            "A real program Damage instruction also qualifies without a card-use identity or an invented effective card kind.");
        s.Cold(); s.Continue(); s.Play(); var modifiers = s.Modifiers().Length;
        s.Begin("source-less"); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Last() is { TargetSeat: 0, Amount: 1, SourceLess: true } &&
            s.Modifiers().Length == modifiers && s.V().Hp == 11,
            "A genuine source-less received hit cannot borrow the target's Far Strike as a nonexistent source modifier.");
        s.Cold(); s.Continue(); s.Play();

        var horse = s.EquipDefensiveHorse(1);
        Need(s.G.GetSeatDistance(0, 1) == 1 && s.G.GetCombatDistance(0, 1) == 2,
            "A real paid defensive horse increases final directed distance while leaving the raw seat ring unchanged.");
        var horseHit = s.BeginCard(s.Action(CardKind.Slash, [1])); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>().Last() is { TargetSeat: 1, Amount: 2 } && s.Modifiers().Last().TargetSeat == 1,
            "The real horse, rather than a seat-number approximation, makes the adjacent physical hit eligible.");
        s.Cold(); s.Continue(); s.Play();
        foreach (var card in new[] { nearCard, farCard, duelCard, horseHit }) s.AssertPaidOnce(card);
        Need(s.G.CardMovements.Count(m => m.CardId == horse && m.From == CardLocation.Hand(0) && m.To == CardLocation.Equipment(1)) == 1,
            "The distance-changing horse itself is a real uniquely paid equipment entity."); s.Cold();

        var riding = Scene.Start("mashu");
        Need(riding.G.GetSeatDistance(0, 2) == 2 && riding.G.GetCombatDistance(0, 2) == 1,
            "The production Ma Shu definition reduces the actual opposite-seat directed distance to one.");
        riding.BeginCard(riding.Action(CardKind.Slash, [2])); riding.AtDamage();
        Need(riding.Events<DamageAppliedEvent>() is [{ TargetSeat: 2, Amount: 1 }] && riding.Modifiers().Length == 0,
            "A real physical Slash at Ma Shu's final distance one receives no bonus.");
        riding.Cold(); riding.Continue(); riding.Play(); riding.Use("hit", [2]);
        Need(riding.Events<DamageAppliedEvent>().Last().Amount == 1 && riding.Modifiers().Length == 0,
            "Program damage uses the same native Ma Shu distance as physical damage."); riding.Cold();

        var fixedOne = Scene.Start("fixed");
        fixedOne.UseSkill(Fenxun, "fixed-one-actual-turn", [2]);
        Need(fixedOne.G.GetCombatDistance(0, 2) == 1 && fixedOne.Events<FixedDistanceOneTurnGrantedEvent>() is [{ Grant.TargetSeat: 2 }],
            "Production Fenxun really issues its current-turn fixed-distance-one policy."); fixedOne.Cold();
        fixedOne.Use("hit", [2]);
        Need(fixedOne.Events<DamageAppliedEvent>() is [{ TargetSeat: 2, Amount: 1 }] && fixedOne.Modifiers().Length == 0,
            "A genuine directed fixed distance of one excludes Far Strike even though the original seat ring remains two."); fixedOne.Cold();

        var unowned = Scene.Start("unowned"); unowned.Use("hit", [2]);
        Need(unowned.Events<DamageAppliedEvent>() is [{ TargetSeat: 2, Amount: 1 }] && unowned.Modifiers().Length == 0 &&
            unowned.Events<RecipientScopedDamageBaseEvent>().Length == 0,
            "Merely registering the production capability does not activate an absent source or create a propagation scope."); unowned.Cold();
        VerifyNativeProgramAiSelectsTheActuallyFarEnemy();
    }

    public static void ElementalChainRechecksRecipientDistanceAndLiveSkillLossCold()
    {
        // These exact chain amounts preserve the engine's existing per-segment
        // source-modifier interpretation; they are not a dedicated original FAQ.
        foreach (var scene in new[] { "chain-near", "chain-two-far", "chain-far", "chain-loss", "chain-unowned" })
        {
            var s = Scene.Start(scene); var twoFar = scene is "chain-two-far" or "chain-loss";
            if (twoFar) s.EquipDefensiveHorse(3);
            var chainA = s.PlayCard(CardKind.IronChain, [1, 2]); var chainB = s.PlayCard(CardKind.IronChain, [3]);
            Need(new[] { 1, 2, 3 }.All(seat => s.V(seat).IsChained) && !s.V().IsChained,
                "Two paid original Iron Chain cards genuinely link exactly the three intended recipients.");
            var farOrigin = scene is "chain-far" or "chain-loss"; var origin = farOrigin ? 2 : 1;
            var slash = s.BeginCard(s.Action(CardKind.ThunderSlash, [origin]));
            if (scene == "chain-loss")
            {
                s.AtSkill(Loss);
                Need(s.Events<DamageAppliedEvent>() is [{ SourceSeat: 0, TargetSeat: 2, Amount: 2 }] &&
                    s.V().Hp == 11 && s.V().Skills?.Any(skill => skill.ContentId == Skill) != true &&
                    s.Events<HpConditionalSkillGrantChangedEvent>().Count(e => e.OwnerSeat == 0 && e.SkillId == Skill && !e.Added) == 1 &&
                    s.Events<RecipientScopedDamageBaseEvent>().Length == 0,
                    "After the real far origin pays two, a legitimate HP payment withdraws the exact derived source before subsequent chain recipients are issued.");
                s.Cold(); s.Continue();
            }
            int[] targets = farOrigin ? [2, 1, 3] : [1, 2, 3];
            int[] amounts = scene == "chain-two-far" ? [1, 2, 3] : farOrigin ? [2, 2, 2] : scene == "chain-unowned" ? [1, 1, 1] : [1, 2, 2];
            for (var index = 0; index < targets.Length; index++)
            {
                s.AtDamage();
                Need(s.Events<DamageAppliedEvent>().Last() is { Nature: DamageNature.Thunder, SourceSeat: 0, SourceLess: false } hit &&
                    hit.TargetSeat == targets[index] && hit.Amount == amounts[index] && s.Events<DamageAppliedEvent>().Length == index + 1,
                    "Each actual elemental recipient uses the live directed distance and the previous finalized native segment: " + s.Diagnostic());
                s.Cold(); s.Continue();
            }
            s.Play();
            var expected = targets.Zip(amounts).ToArray();
            Need(s.Events<DamageAppliedEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual(expected) &&
                s.Events<AfterDamageEvent>().Select(e => (e.TargetSeat, e.Amount)).SequenceEqual(expected) &&
                s.Events<RecipientScopedDamageBaseEvent>().Length == 0 && s.Events<RecipientScopedDamageAdvancedEvent>().Length == 0 &&
                s.Events<ChainedDamagePropagatedEvent>().Length == 2,
                "The unscoped chain retains 1/2/2, 1/2/3 or 2/2/2; registration alone neither freezes a new base nor reapplies the origin modifier twice.");
            var modifiers = s.Modifiers();
            int[] modifiedTargets = scene == "chain-two-far" ? [2, 3] : scene == "chain-unowned" ? [] : [2];
            Need(modifiers.All(e => e.Source.OwnerSeat == 0 && e.ModifiedAmount == e.BaseAmount + 1) &&
                modifiers.Select(e => e.TargetSeat).SequenceEqual(modifiedTargets) &&
                modifiers.GroupBy(e => (e.ResolutionId, e.TargetSeat)).All(group => group.Count() == 1) &&
                new[] { 1, 2, 3 }.All(seat => !s.V(seat).IsChained),
                "Every qualifying segment records one real source binding; a near recipient or a genuinely withdrawn source never adds another bonus.");
            foreach (var card in new[] { chainA, chainB, slash }) s.AssertPaidOnce(card); s.Cold();
            if (scene == "chain-loss")
            {
                var previous = s.Modifiers().Length; s.Use("hit", [3]);
                Need(s.G.GetCombatDistance(0, 3) == 2 && s.Events<DamageAppliedEvent>().Last() is { TargetSeat: 3, Amount: 1 } &&
                    s.Modifiers().Length == previous, "A subsequent genuinely far program hit also observes the lost source, without repaying or refunding any earlier segment."); s.Cold();
            }
        }
    }

    private static ShanheBattleSetup ProductionSetup(string general = "classic:guan-yu") =>
        new(83, 1, "fixture-far-strike-production", general, 4, 0, 0, 0, 0, [], [], [], [new("standard:cao-cao")], [], new string('f', 64));

    private static void VerifyNativeProgramAiSelectsTheActuallyFarEnemy()
    {
        var s = Scene.Start("ai");
        Need(s.G.GetCombatDistance(1, 2) == 1 && s.G.GetCombatDistance(1, 3) == 2 && s.V(2).Hp == s.V(3).Hp,
            "The native program chooser has two equally healthy public enemies, with only their actual directed distance differing.");
        s.Accept(new EndPlayPhaseCommand(0, s.G.Revision, s.P!.PromptId)); s.AtDamage();
        Need(s.Events<DamageAppliedEvent>() is [{ SourceSeat: 1, TargetSeat: 3, Amount: 2 }] &&
            s.Modifiers() is [{ Source.OwnerSeat: 1, TargetSeat: 3, CardKind: null, BaseAmount: 1, ModifiedAmount: 2 }] &&
            s.V(2).Hp == 20 && s.V(3).Hp == 18,
            "The real NPC program's native target selection uses the public known-recipient damage estimate and chooses the far enemy; it does not treat all Damage1 candidates alike.");
        s.Cold(); s.Continue(); s.FinishProgram(AiSource);
        Need(s.Events<DamageAppliedEvent>().Length == 1 && s.Modifiers().Length == 1 &&
            s.Events<ProgramBindingResolvedEvent>().Count(e => e.SkillId == AiSource && e.Completed) == 1,
            "Returning from the real human damage observer does not issue or pay the NPC's selected program damage twice."); s.Cold();
    }

    private static void VerifyConditionalRegistryAndEditionIsolation()
    {
        foreach (var farFirst in new[] { false, true })
        {
            var bare = ProductionSetup(farFirst ? "standard:liu-bei" : "classic:guan-yu");
            var equipped = bare with { TacticSkillIds = [Skill] };
            var first = StandardShanheCampaignCatalog.CreateBattleRegistry(farFirst ? equipped : bare);
            var second = StandardShanheCampaignCatalog.CreateBattleRegistry(farFirst ? bare : equipped);
            var plainRegistry = farFirst ? second : first; var farRegistry = farFirst ? first : second;
            Need(!plainRegistry.Skills.ContainsKey(Skill) && farRegistry.Skills.ContainsKey(Skill) &&
                plainRegistry.ContentHash != farRegistry.ContentHash && bare.CampaignHash == equipped.CampaignHash &&
                ReferenceEquals(plainRegistry, StandardShanheCampaignCatalog.CreateBattleRegistry(bare)) &&
                ReferenceEquals(farRegistry, StandardShanheCampaignCatalog.CreateBattleRegistry(equipped)),
                "With the same run hash and roster, warming either recipe first cannot leak or omit the conditionally registered current Far Strike package.");
        }
        var old = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.LocalEditionId);
        var current = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        Need(old.Rewards.All(reward => reward.Id != Reward && reward.TacticSkillId != Skill) &&
            current.Rewards.Single(reward => reward.Id == Reward).TacticSkillId == Skill &&
            !StandardShanheCampaignCatalog.CreateBattleRegistry(ProductionSetup()).Skills.ContainsKey(Skill),
            "Only the current local edition offers the verified tactic; an ordinary old-style battle recipe retains its unextended registry.");
        var options = new ShanheCampaignOptions(83, "classic:guan-yu");
        var session = ShanheCampaignSession.Create(current, options, StandardShanheCampaignCatalog.CreateBattleRegistry);
        var restored = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(session.CreateCheckpoint())),
            current, StandardShanheCampaignCatalog.CreateBattleRegistry);
        Need(session.Snapshot.TacticIds.Count == 0 && JsonSerializer.Serialize(session.Snapshot) == JsonSerializer.Serialize(restored.Snapshot),
            "The real current campaign validates its unequipped reward catalogue using a probe that can resolve the conditional tactic, without granting it or changing the cold initial state.");

        var equippedRun = ShanheCampaignSession.Create(current, options with
        {
            EquippedTacticIds = [Reward], UnlockedRewardIds = [Reward]
        }, StandardShanheCampaignCatalog.CreateBattleRegistry);
        Need(equippedRun.Snapshot.TacticIds.SequenceEqual([Reward]), "The real unlocked loadout grants exactly its equipped reward before any map command.");
        void Submit(ShanheCampaignCommand command)
        {
            var result = equippedRun.Submit(command); Need(result.Accepted, result.Error ?? "The real equipped campaign command was rejected.");
        }
        void ColdRun()
        {
            var before = JsonSerializer.Serialize(equippedRun.Snapshot);
            equippedRun = ShanheCampaignSession.Restore(ShanheCampaignJson.Deserialize(ShanheCampaignJson.Serialize(equippedRun.CreateCheckpoint())),
                current, StandardShanheCampaignCatalog.CreateBattleRegistry);
            Need(JsonSerializer.Serialize(equippedRun.Snapshot) == before, "Cold campaign recovery preserves the actual equipped reward, route and issued first-battle recipe.");
        }
        ColdRun();
        for (var step = 0; step < 6 && equippedRun.Snapshot.Phase != ShanheCampaignPhase.Battle; step++)
        {
            if (equippedRun.Snapshot.Phase == ShanheCampaignPhase.Map)
                Submit(new MoveShanheNodeCommand(equippedRun.Snapshot.AvailableNodes[0].Id, equippedRun.Revision));
            else if (equippedRun.Snapshot.Phase == ShanheCampaignPhase.NodeChoice)
            {
                var choice = equippedRun.Snapshot.NodeChoices.First(option => option.RewardId is { } rewardId &&
                    current.Rewards.Single(reward => reward.Id == rewardId).TacticSkillId is null &&
                    option.GoldCost <= equippedRun.Snapshot.Gold && option.HpCost < equippedRun.Snapshot.Hp);
                Submit(new ChooseShanheNodeOptionCommand(choice.Id, null, equippedRun.Revision));
            }
            else throw new InvalidOperationException("Unexpected first-battle route phase: " + equippedRun.Snapshot.Phase);
            ColdRun();
        }
        var actual = equippedRun.PendingBattleSetup;
        Need(equippedRun.Snapshot.Phase == ShanheCampaignPhase.Battle && actual is not null &&
            actual.TacticSkillIds.SequenceEqual([Skill]) && equippedRun.Snapshot.TacticIds.SequenceEqual([Reward]) &&
            actual.CampaignHash == equippedRun.ContentHash,
            "Real map and opening-event commands issue only the owned tactic, never the probe's full reward catalogue, in the exact first-battle recipe.");
        var actualRegistry = StandardShanheCampaignCatalog.CreateBattleRegistry(actual!);
        Need(actualRegistry.Skills.ContainsKey(Skill), "The real owned encounter recipe imports its conditional production bundle.");
        var game = StandardShanheCampaignCatalog.CreateBattle(actual!);
        var started = game.Submit(new StartGameCommand()); Need(started.Accepted, started.Error?.Message ?? "The actual equipped first battle failed to start.");
        void AssertAcquired(GameEngine battle)
        {
            var own = battle.CreateSnapshot(0).Players[0];
            Need(own.Skills?.Count(skill => skill.ContentId == Skill) == 1 &&
                own.SkillRuntimeStates?.Count(state => state.SkillId == Skill) == 1 &&
                Enumerable.Range(1, battle.PlayerCount - 1).All(seat => battle.CreateSnapshot(seat).Players[seat].Skills?.Any(skill => skill.ContentId == Skill) != true) &&
                battle.CreateCheckpoint().Options.ShanheBattle!.TacticSkillIds.SequenceEqual([Skill]),
                "The actual production encounter exposes exactly one owner skill and runtime state from its equipped recipe; the validation probe gives neither additional tactics nor enemy grants, and campaign grants are not relabeled as native acquired sources.");
        }
        string BattleState(GameEngine battle) => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, battle.PlayerCount).Select(seat => SnapshotJson.Serialize(battle.CreateSnapshot(seat))),
            Frames = battle.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
            Facts = battle.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())),
            battle.CardMovements, Zones = battle.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(battle.CreateCheckpoint())
        });
        AssertAcquired(game); var beforeBattle = BattleState(game);
        game = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), actualRegistry);
        Need(BattleState(game) == beforeBattle, "The exact production first battle, owner tactic acquisition, native startup frames and physical entities survive cold recovery."); AssertAcquired(game);
    }

    private static void VerifyDistanceConditionLoaderBoundaries()
    {
        var assembly = typeof(StandardShanheS5BossPackage).Assembly;
        string Read(string suffix)
        {
            var name = assembly.GetManifestResourceNames().Single(resource => resource.EndsWith(suffix, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name)!; using var reader = new StreamReader(stream); return reader.ReadToEnd();
        }
        var source = Read("shanhe-current-yuanji.rules.json"); var presentation = Read("shanhe-current-yuanji.presentation.json");
        void Invalid(Action<JsonObject> edit)
        {
            var root = JsonNode.Parse(source)!.AsObject(); edit(root["skills"]![0]!["damageModifiers"]![0]!.AsObject());
            try { _ = SkillProgramCatalog.Load(root.ToJsonString(), presentation); }
            catch (Exception error) when (error is ArgumentException or InvalidOperationException) { return; }
            throw new InvalidOperationException("An invalid generic final-distance damage condition was accepted: " + root);
        }
        Invalid(modifier => modifier.Remove("distanceThreshold"));
        Invalid(modifier => modifier["distanceThreshold"] = -1);
        Invalid(modifier => modifier["distanceThreshold"] = (long)int.MaxValue + 1);
        foreach (var scope in new[] { "ownerUsed", "damageParticipant", "allLivingSkillOwners" }) Invalid(modifier => modifier["sourceScope"] = scope);
        Invalid(modifier => modifier["cardKinds"] = new JsonArray("slash"));
        Invalid(modifier => modifier["damageLayer"] = "cardDamage");
        Invalid(modifier => modifier["condition"] = "sourceHandBelowHp");
        foreach (var threshold in new[] { 0, int.MaxValue })
        {
            var valid = JsonNode.Parse(source)!; valid["skills"]![0]!["damageModifiers"]![0]!["distanceThreshold"] = threshold;
            _ = SkillProgramCatalog.Load(valid.ToJsonString(), presentation);
        }
    }

    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Scene(GameEngine game, ContentRegistry registry)
    {
        public GameEngine G { get; private set; } = game;
        public PendingDecision? P => G.PendingDecision;
        public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
        public T[] Events<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
        public ProgramCardDamageModifiedEvent[] Modifiers() => Events<ProgramCardDamageModifiedEvent>().Where(e => e.Source.SkillId == Skill).ToArray();
        public void Accept(GameCommand command) { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single()); Need(result.Accepted, result.Error?.Message ?? "Far Strike command rejected."); }
        public void Begin(string id, int[]? targets = null) => Accept(new UseProgramSkillCommand(0, Driver, id, [], targets ?? [], G.Revision, P!.PromptId));
        public void Use(string id, int[]? targets = null) { Begin(id, targets); Play(); }
        public void UseSkill(string skill, string id, int[] targets) { Accept(new UseProgramSkillCommand(0, skill, id, [], targets, G.Revision, P!.PromptId)); Play(); }
        public LegalAction Action(CardKind kind, int[] targets) => G.GetHumanLegalActions().First(action => action.CardId is { } id &&
            V().Hand.Any(card => card.Id == id && card.Kind == kind) && action.ConversionSource is null && action.Kind != LegalActionKind.Recast && action.TargetSeats.SequenceEqual(targets));
        public int BeginCard(LegalAction action)
        {
            Accept(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, G.Revision, P!.PromptId, action.PlayedCardKind, action.TargetCardId)); return action.CardId.Value;
        }
        public int PlayCard(CardKind kind, int[] targets) { var card = BeginCard(Action(kind, targets)); Play(); return card; }
        public int EquipDefensiveHorse(int target)
        {
            var card = V().Hand.Single(card => card.Kind == CardKind.DefensiveHorse).Id;
            Accept(new UseProgramSkillCommand(0, Driver, "equip", [card], [target], G.Revision, P!.PromptId)); Play(); return card;
        }
        public void Continue() => Accept(new AnswerPromptCommand(0, P!.PromptId, P.Choices.Single(choice => choice.Parameters.GetValueOrDefault("option-id") == "continue").Id, G.Revision));
        public void AtSkill(string id) => Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == id);
        public void AtDamage() => AtSkill(Watch);
        public void FinishProgram(string skill) => Reach(() => Events<ProgramBindingResolvedEvent>().Any(e => e.SkillId == skill && e.Completed));
        public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        private void Reach(Func<bool> done)
        {
            for (var step = 0; step < 180; step++)
            {
                if (done()) return;
                if (P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Watch) { Continue(); continue; }
                Need(P?.PlayerSeat != 0 && G.State.Winner == Winner.None, "Unexpected actual Far Strike boundary: " + Diagnostic());
                Accept(new AdvanceOneStepCommand(G.Revision));
            }
            throw new InvalidOperationException("Missing bounded actual Far Strike boundary: " + Diagnostic());
        }
        public void AssertPaidOnce(int card) => Need(G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Hand(0) && m.To == CardLocation.Processing) == 1 &&
            G.CardMovements.Count(m => m.CardId == card && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile) == 1,
            "Every actual tested physical card retains one Hand payment and one native finished-use cleanup through cold recovery.");
        public string Diagnostic() => JsonSerializer.Serialize(new { Prompt = P is null ? null : new { P.Kind, P.PlayerSeat, Skill = P.SkillPrompt?.SkillId },
            Damage = Events<DamageAppliedEvent>().Select(e => new { e.SourceSeat, e.TargetSeat, e.Amount, e.Nature }), Modifiers = Modifiers(),
            Chain = Events<ChainedDamagePropagatedEvent>().Select(e => new { e.SourceSeat, e.TargetSeat, e.Amount }),
            Source = new { V().Hp, Skills = V().Skills?.Select(skill => skill.ContentId) }, Frames = G.ResolutionStack.Select(frame => new { frame.Id, frame.Kind }) });
        public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(G.CreateSnapshot(seat))),
            Frames = G.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())), Facts = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())),
            G.CardMovements, Zones = G.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(G.CreateCheckpoint()) });
        public void Cold()
        {
            var before = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves real final distance, source grants, per-segment damage, Armor receipts and already-paid card entities without duplicate modifiers.");
            var zones = G.CreateCardZoneDiagnostics(); Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Every actual entity still owns exactly one native zone.");
        }
        public static Scene Start(string scene)
        {
            var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene == "ai"));
            var chain = scene.StartsWith("chain-", StringComparison.Ordinal);
            var own = new List<string> { Watch, Fenxun }; if (scene == "mashu") own.Add(Mashu);
            if (scene == "chain-loss") own.AddRange([HpGate, Loss]);
            ShanheStartingCard[] cards = chain ? [new(CardKind.IronChain, Suit.Club, 6), new(CardKind.IronChain, Suit.Spade, 8),
                new(CardKind.ThunderSlash, Suit.Spade, 7), new(CardKind.DefensiveHorse, Suit.Heart, 5)] :
                [new(CardKind.Slash, Suit.Spade, 3), new(CardKind.Slash, Suit.Club, 4), new(CardKind.Slash, Suit.Spade, 5),
                 new(CardKind.Duel, Suit.Club, 6), new(CardKind.DefensiveHorse, Suit.Heart, 5)];
            ShanheCombatantDefinition[] enemies = scene == "ai" ? [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }] :
                [new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }, new(Peer) { StartingHand = [] }];
            ShanheCombatantDefinition[] allies = scene == "ai" ? [new(AiGeneral, ExtraSkillIds: [Skill]) { StartingHand = [] }] : [];
            var setup = new ShanheBattleSetup(83, 1, "fixture-far-strike-" + scene, Owner, 12, 0, 0, 0, 0, own, cards, [],
                enemies, allies, new string('f', 64))
            { TacticSkillIds = scene is "unowned" or "chain-unowned" or "ai" or "chain-loss" ? [] : [Skill] };
            var game = GameEngine.CreateStandard(new() { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null,
                HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
                UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 3,
                AiPolicyVersion = AiDecisionPolicy.CurrentVersion }, registry);
            var s = new Scene(game, registry); s.Accept(new StartGameCommand()); s.Play(); return s;
        }
    }

    private sealed class Fixture(bool aiSource = false) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-shanhe-far-strike", new(1, 0, 0));
        public void Register(IContentRegistryBuilder builder)
        {
            foreach (var id in new[] { Skill, Mashu, Fenxun }) builder.AddSkill(Production.Value.GetSkill(id));
            object Pause(string name) => new { op = "chooseOption", target = "owner", resultBind = name, options = new[] { new { id = "continue" } } };
            var definitions = new object[] {
                new { id = Driver, revision = 1,
                    modifiers = new[] { new { id = "test-range", query = "attackRange", operation = "add", value = 3, priority = 0 },
                        new { id = "test-slashes", query = "slashLimit", operation = "add", value = 10, priority = 0 },
                        new { id = "test-hand", query = "handLimit", operation = "add", value = 20, priority = 0 } },
                    activations = new object[] {
                        new { id = "hit", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                            effects = new[] { new { op = "damage", target = "selectedTarget", amount = 1 } } },
                        new { id = "source-less", minCards = 0, maxCards = 0, minTargets = 0, maxTargets = 0, targetKind = "anyLiving", usesPerTurn = (int?)null,
                            effects = new[] { new { op = "receiveOwnerDamage", target = "owner", amount = 1 } } },
                        new { id = "armor-two", minCards = 0, maxCards = 0, minTargets = 1, maxTargets = 1, targetKind = "anyLiving", usesPerTurn = (int?)null,
                            effects = new[] { new { op = "grantArmor", target = "selectedTarget", amount = 2 } } },
                        new { id = "equip", minCards = 1, maxCards = 1, sourceZones = new[] { "hand" }, cardCategories = new[] { "equipment" },
                            minTargets = 1, maxTargets = 1, targetKind = "otherLiving", usesPerTurn = (int?)null,
                            effects = new object[] { new { op = "captureSelectedCards", target = "owner", resultBind = "gear" },
                                new { op = "placeSelectedEquipment", target = "selectedTarget", sourceBind = "gear" } } } } },
                new { id = Watch, revision = 1, triggers = new[] { new { id = "paid-child", window = "afterDamageApplied", subject = "any",
                    damageOccurrence = "perDamage", optional = false, effects = new[] { Pause("paid") } } } },
                new { id = HpGate, revision = 1, hpSkillGrants = new[] { new { id = "full-hp-production-source", skillId = Skill, minimumHp = 12 } } },
                new { id = Loss, revision = 1, triggers = new[] { new { id = "withdraw-after-real-origin", window = "afterDamageApplied", subject = "any",
                    damageOccurrence = "perDamage", optional = false, priority = 200, usageScope = "game", usageLimit = 1,
                    effects = new object[] { new { op = "loseHp", target = "owner", amount = 1 }, Pause("withdrawn") } } } },
                new { id = AiSource, revision = 1, triggers = new[] { new { id = "native-program-target-choice", window = "afterNormalDraw", subject = "owner",
                    optional = false, usageScope = "game", usageLimit = 1, effects = new object[] {
                        new { op = "selectTarget", target = "owner", targetKind = "otherLiving" },
                        new { op = "damage", target = "selectedTarget", amount = 1 } } } } }
            };
            var labels = new Dictionary<string, object> { [Driver] = new { name = "真实远击测试操作", description = "仅使用实体牌和原生伤害、护甲、装备指令" },
                [HpGate] = new { name = "实际HP条件授予", description = "在12点体力时授予真实生产远击技" },
                [AiSource] = new { name = "实际NPC程序伤害选择", description = "让原生公开目标评分选择真实受害者" } };
            foreach (var id in new[] { Watch, Loss }) labels[id] = new { name = "真实伤害已付子窗口", description = "在真实伤害及来源变化后暂停",
                optionLabels = new Dictionary<string, string> { ["continue"] = "继续" } };
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = definitions }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, catalog.Presentations[id].Name, catalog.Presentations[id].Description)
                { Program = program, ProgramPresentation = catalog.Presentations[id] });
            builder.AddGeneral(new(Owner, "真实远击来源", "", Driver, "qun", 12));
            builder.AddGeneral(new(Peer, "真实远击受害者", "", "standard:none", "wei", 20));
            builder.AddGeneral(new(AiGeneral, "实际NPC远击选择者", "", AiSource, "qun", 20));
            builder.AddDeck(new(Deck, "零自然摸牌", 0, 0, []) { PhysicalCards = Enumerable.Range(0, 64)
                .Select(index => new ContentDeckPhysicalCard("standard:dodge", Suit.Club, index % 13 + 1)).ToArray() });
            builder.AddMode(new(ShanheBattleSetup.BattleModeId, "实际远击与方向距离", 4, 4, new Dictionary<string, int>(), Deck,
                GeneralPoolIds: [Owner, Peer, AiGeneral], ModeKind: ContentModeKind.Team, TeamCounts: new Dictionary<string, int>
                { [ShanheBattleSetup.PlayerTeamId] = aiSource ? 2 : 1, [ShanheBattleSetup.EnemyTeamId] = aiSource ? 2 : 3 }));
        }
    }
}
