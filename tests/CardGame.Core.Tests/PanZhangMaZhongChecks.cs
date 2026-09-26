using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class PanZhangMaZhongChecks
{
    private const string General = "classic:pan-zhang-ma-zhong";
    private const string Duodao = "classic:duodao";
    private const string Anjian = "classic:anjian";
    private const string ArmorSkill = "fixture:take-source-armor";
    private const string Mode = "identity:classic-pan-zhang-ma-zhong-check-5";

    public static void DefinitionAndGenericSchema()
    {
        var current = StandardContentRegistry.CreateWithClassicGenerals();
        var previous = StandardContentRegistry.CreateWithClassicGenerals(new Version(1, 140, 0));
        Require(current.Generals[General] is { BaseHp: 4, FactionId: "wu" } general &&
                general.SkillIds.SequenceEqual([Duodao, Anjian]) &&
                current.Modes["identity:classic-5"].GeneralPoolIds!.Contains(General) &&
                current.Modes["identity:classic-8"].GeneralPoolIds!.Contains(General) &&
                !previous.Generals.ContainsKey(General) && !previous.Skills.ContainsKey(Duodao) &&
                !previous.Skills.ContainsKey(Anjian),
            "2013 Pan Zhang and Ma Zhong must first enter the 1.141 formal Wu roster with four HP.");
        var damage = current.Skills[Anjian].Program!.DamageModifiers.Single();
        Require(damage.Condition == SkillProgramDamageModifierCondition.SourceOutsideTargetAttackRange &&
                damage.SourceScope == SkillProgramDamageModifierSourceScope.OwnerUsed &&
                damage.CardKinds.SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]) &&
                current.Skills[Duodao].Program!.Triggers.Single().DamageCardKinds
                    .SequenceEqual([CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash]),
            "The two skills must use effective Slash kinds and the generic reverse-range modifier.");

        var generic = """
            {"schemaVersion":56,"skills":[{"id":"fixture:generic","revision":1,
            "minimumRulesVersion":166,
            "damageModifiers":[{"id":"distance","cardKinds":["duel"],"amount":2,
            "condition":"always"}],
            "triggers":[{"id":"take-armor","window":"afterDamageApplied","subject":"owner",
            "damageOccurrence":"perDamage","damageCardKinds":["duel"],"optional":true,
            "effects":[{"op":"selectSourceCard","target":"owner","zones":["equipment"],
            "equipmentSlots":["armor"],"skipIfNoCards":true,"resultBind":"armor"},
            {"op":"moveBoundCards","target":"owner","sourceBind":"armor","destination":"ownerHand"}]}]}]}
            """;
        const string presentation = """
            {"schemaVersion":1,"skills":{"fixture:generic":{"name":"通用","description":"测试"}}}
            """;
        Require(SkillProgramCatalog.Load(generic, presentation).Programs["fixture:generic"]
                .DamageModifiers.Single().Amount == 2,
            "A Duel modifier and source-armor collection must be independently definable.");
        Reject(generic.Replace("\"schemaVersion\":56", "\"schemaVersion\":55"), presentation);
        Reject(generic.Replace("\"damageCardKinds\":[\"duel\"]", "\"damageCardKinds\":[]"), presentation);
        Reject(generic.Replace("\"equipmentSlots\":[\"armor\"]", "\"equipmentSlots\":[]"), presentation);
        Reject(generic.Replace("\"condition\":\"always\"",
            "\"condition\":\"sourceOutsideTargetAttackRange\",\"sourceScope\":\"damageSource\""), presentation);
        Reject(generic.Replace("\"window\":\"afterDamageApplied\"", "\"window\":\"drawPhaseStarting\""), presentation);
    }

    public static void NaturalSlashReverseRangeAndReplay()
    {
        var registry = Registry();
        for (var seed = 1; seed <= 100; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var weapon = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Equip && action.CardId is { } id &&
                game.CreateCardZoneDiagnostics().Any(zone => zone.CardId == id &&
                    zone.CardKind == CardKind.QinggangSword));
            if (weapon is null) continue;
            Play(game, weapon);
            ReachPlay(game);
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat == 2);
            if (slash is null) continue;
            var paused = GameReplay.Restore(GameCheckpointJson.Deserialize(
                GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
            Play(game, slash);
            Play(paused, paused.GetHumanLegalActions().Single(action =>
                action.Kind == LegalActionKind.Slash && action.CardId == slash.CardId &&
                action.TargetSeat == 2));
            Finish(game);
            Finish(paused);
            var modified = game.Events.Select(item => item.Payload)
                .OfType<ProgramCardDamageModifiedEvent>()
                .Where(item => item.Source.SkillId == Anjian && item.TargetSeat == 2).ToArray();
            Require(modified.Length == 1 && modified[0].ModifiedAmount == 2 &&
                    game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                        .Any(item => item.SourceSeat == 0 && item.TargetSeat == 2 && item.Amount == 2) &&
                    SnapshotJson.Serialize(game.CreateSnapshot(0, true)) ==
                    SnapshotJson.Serialize(paused.CreateSnapshot(0, true)) &&
                    Events(game).SequenceEqual(Events(paused)),
                $"A natural far Slash must gain one damage once and replay from a pre-attack checkpoint. " +
                $"modifier={modified.Length}, amount={modified.FirstOrDefault()?.ModifiedAmount}, " +
                $"weapon={weapon.CardId}, statesEqual={SnapshotJson.Serialize(game.CreateSnapshot(0, true)) == SnapshotJson.Serialize(paused.CreateSnapshot(0, true))}");
            return;
        }
        throw new InvalidOperationException("No seeded opening could equip Qinggang Sword and Slash seat two.");
    }

    public static void NaturalDuodaoPaymentWeaponAndReplay()
    {
        var registry = Registry(targetsHaveDuodao: false);
        for (var seed = 1; seed <= 80; seed++)
        {
            var game = Start(registry, seed);
            for (var step = 0; step < 180 && game.State.Status != EngineStatus.Completed; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { PlayerSeat: 0, SkillPrompt.SkillId: Duodao })
                {
                    var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                        .LastOrDefault(item => item.TargetSeat == 0 && item.SourceSeat != 0);
                    if (damage is null || !game.CreateCardZoneDiagnostics().Any(zone =>
                            zone.Location == CardLocation.Equipment(damage.SourceSeat) &&
                            zone.CardKind == CardKind.QinggangSword))
                        break;
                    var paused = GameReplay.Restore(GameCheckpointJson.Deserialize(
                        GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    AnswerAction(game, "activate");
                    AnswerAction(paused, "activate");
                    var cost = game.PendingDecision;
                    Require(cost is { PlayerSeat: 0, Kind: DecisionKind.ProgramTrigger } &&
                            cost.Choices.Any(choice => choice.Parameters.GetValueOrDefault("program-action") ==
                                "select-and-move-owned-card"),
                        "Accepting Duodao must request exactly one owned-card payment.");
                    var selectedCost = cost!.Choices.First(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
                    Answer(game, selectedCost);
                    Answer(paused, paused.PendingDecision!.Choices.Single(choice => choice.Id == selectedCost.Id));
                    for (var follow = 0; follow < 20 && game.PendingDecision?.Choices.All(choice =>
                             choice.Parameters.GetValueOrDefault("program-action") != "select-source-card") == true; follow++)
                    {
                        Advance(game);
                        Advance(paused);
                    }
                    var weapon = game.PendingDecision?.Choices.SingleOrDefault(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "select-source-card") ??
                        throw new InvalidOperationException("A paid Duodao must offer the source weapon.");
                    Require(weapon.Cards.Count == 1,
                        "A paid Duodao must offer only the damage source's weapon.");
                    var afterPayment = GameReplay.Restore(GameCheckpointJson.Deserialize(
                        GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    Answer(game, weapon);
                    Answer(paused, paused.PendingDecision!.Choices.Single(choice => choice.Id == weapon.Id));
                    Answer(afterPayment, afterPayment.PendingDecision!.Choices.Single(choice => choice.Id == weapon.Id));
                    Finish(game);
                    Finish(paused);
                    Finish(afterPayment);
                    Require(game.CardMovements.Any(move => move.CardId == weapon.Cards.Single() &&
                                move.From == CardLocation.Equipment(damage.SourceSeat) &&
                                move.To == CardLocation.Hand(0)) &&
                            game.CardMovements.Any(move => move.To == CardLocation.DiscardPile &&
                                move.Reason.Value.Contains(Duodao, StringComparison.Ordinal)) &&
                            Events(game).SequenceEqual(Events(paused)) &&
                            Events(game).SequenceEqual(Events(afterPayment)) &&
                            SnapshotJson.Serialize(game.CreateSnapshot(0, true)) ==
                            SnapshotJson.Serialize(paused.CreateSnapshot(0, true)),
                        "Paid Duodao must move the real weapon once and replay across both pauses.");
                    return;
                }
                if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                {
                    var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
                    Require(ended.Accepted, ended.Error?.Message ?? "Could not end Pan Zhang's Play phase.");
                }
                else Advance(game);
            }
        }
        throw new InvalidOperationException("No seeded AI Slash with an equipped source weapon reached human Duodao.");
    }

    public static void GenericSourceArmorSelectionActuallyMovesCard()
    {
        var registry = Registry(targetsHaveDuodao: false, armorSynthetic: true);
        for (var seed = 1; seed <= 100; seed++)
        {
            var game = Start(registry, seed);
            ReachPlay(game);
            var actions = game.GetHumanLegalActions();
            var armor = actions.FirstOrDefault(action => action.Kind == LegalActionKind.Equip &&
                game.CreateCardZoneDiagnostics().Any(zone => zone.CardId == action.CardId &&
                    zone.CardKind == CardKind.BaguaFormation));
            var sword = actions.FirstOrDefault(action => action.Kind == LegalActionKind.Equip &&
                game.CreateCardZoneDiagnostics().Any(zone => zone.CardId == action.CardId &&
                    zone.CardKind == CardKind.QinggangSword));
            if (armor is null || sword is null) continue;
            Play(game, armor);
            ReachPlay(game);
            Play(game, game.GetHumanLegalActions().Single(action => action.CardId == sword.CardId &&
                action.Kind == LegalActionKind.Equip));
            ReachPlay(game);
            var slash = game.GetHumanLegalActions().FirstOrDefault(action =>
                action.Kind == LegalActionKind.Slash && action.TargetSeat == 2);
            if (slash is null) continue;
            Play(game, slash);
            Finish(game);
            Require(game.CardMovements.Any(move => move.CardId == armor.CardId &&
                    move.From == CardLocation.Equipment(0) && move.To == CardLocation.Hand(2) &&
                    move.Reason.Value.Contains(ArmorSkill, StringComparison.Ordinal)),
                "A non-Duodao schema-56 graph must actually transfer the source armor after Slash.");
            return;
        }
        throw new InvalidOperationException("No seeded opening could equip armor and sword before a Slash at seat two.");
    }

    public static void DuodaoMayPayWithoutSourceWeaponAndMayDecline()
    {
        var registry = Registry(targetsHaveDuodao: false, noWeapons: true);
        for (var seed = 1; seed <= 160; seed++)
        {
            var game = Start(registry, seed);
            for (var step = 0; step < 180 && game.State.Status != EngineStatus.Completed; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { PlayerSeat: 0, SkillPrompt.SkillId: Duodao })
                {
                    var damage = game.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>()
                        .LastOrDefault(item => item.TargetSeat == 0 && item.SourceSeat != 0);
                    if (damage is null || game.CreateCardZoneDiagnostics().Any(zone =>
                            zone.Location == CardLocation.Equipment(damage.SourceSeat) &&
                            EquipmentCatalog.Get(zone.CardKind).Slot == EquipmentSlot.Weapon))
                        break;
                    var before = GameReplay.Restore(GameCheckpointJson.Deserialize(
                        GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    var oldMoves = game.CardMovements.Count;
                    AnswerAction(game, "activate");
                    var payment = game.PendingDecision!.Choices.First(choice =>
                        choice.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card");
                    Answer(game, payment);
                    Finish(game);
                    Require(game.CardMovements.Skip(oldMoves).Count(move =>
                            move.To == CardLocation.DiscardPile &&
                            move.Reason.Value.Contains(Duodao, StringComparison.Ordinal)) == 1 &&
                            game.CardMovements.Skip(oldMoves).All(move =>
                                !(move.To == CardLocation.Hand(0) &&
                                  move.Reason.Value.Contains(Duodao, StringComparison.Ordinal))) &&
                            game.ResolutionStack.Count == 0,
                        "Duodao must permit a real one-card cost and safely skip absent weapon acquisition.");
                    AnswerAction(before, "skip");
                    Finish(before);
                    Require(before.CardMovements.Skip(oldMoves).All(move =>
                            !move.Reason.Value.Contains(Duodao, StringComparison.Ordinal)),
                        "Declining Duodao must pay and obtain nothing.");
                    return;
                }
                if (prompt is { Kind: DecisionKind.PlayCard, PlayerSeat: 0 })
                {
                    var ended = game.Submit(new EndPlayPhaseCommand(0, game.Revision, prompt.PromptId));
                    Require(ended.Accepted, ended.Error?.Message ?? "Could not end Pan Zhang's Play phase.");
                }
                else Advance(game);
            }
        }
        throw new InvalidOperationException("No seeded unarmed Slash source reached a payable Duodao prompt.");
    }

    public static void AnjianAmountIsFrozenAcrossTianxiangTransfer()
    {
        var registry = Registry(targetsHaveDuodao: false, tianxiang: true);
        var eligibleSetups = 0;
        var tianxiangPrompts = 0;
        var modifierEvents = 0;
        var humanDamageRequests = 0;
        var humanModifiers = 0;
        for (var seed = 1; seed <= 240; seed++)
        {
            var game = GameEngine.CreateStandard(new GameOptions
            {
                Seed = seed, PlayerCount = 5, HumanSeat = 2, HumanRole = Role.Rebel,
                ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
                AdvanceAfterHumanCommands = false, MaxTurns = 12
            }, registry);
            Require(game.Submit(new StartGameCommand()).Accepted, "Tianxiang fixture did not start.");
            var setup = game.PendingDecision!;
            if (!setup.ValidContentIds.Contains("classic:xiao-qiao")) continue;
            eligibleSetups++;
            var selected = game.Submit(new SelectGeneralCommand(2, "classic:xiao-qiao", game.Revision,
                setup.PromptId));
            Require(selected.Accepted, selected.Error?.Message ?? "Xiao Qiao selection failed.");
            for (var step = 0; step < 300 && game.State.Status != EngineStatus.Completed; step++)
            {
                var prompt = game.PendingDecision;
                if (prompt is { Kind: DecisionKind.Tianxiang, PlayerSeat: 2 }) tianxiangPrompts++;
                modifierEvents += game.Events.Select(item => item.Payload)
                    .OfType<ProgramCardDamageModifiedEvent>().Count(item => item.Source.SkillId == Anjian);
                if (prompt is { Kind: DecisionKind.Tianxiang, PlayerSeat: 2 } &&
                    prompt.Choices.Any(choice => choice.Parameters.GetValueOrDefault("action") == "tianxiang-use") &&
                    game.Events.Select(item => item.Payload).OfType<ProgramCardDamageModifiedEvent>()
                        .Any(item => item.Source.SkillId == Anjian && item.TargetSeat == 2 &&
                                     item.ModifiedAmount == 2))
                {
                    var paused = GameReplay.Restore(GameCheckpointJson.Deserialize(
                        GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
                    var use = prompt.Choices.First(choice =>
                        choice.Parameters.GetValueOrDefault("action") == "tianxiang-use");
                    Answer(game, use);
                    Answer(paused, paused.PendingDecision!.Choices.Single(choice => choice.Id == use.Id));
                    for (var follow = 0; follow < 70 &&
                         !game.Events.Select(item => item.Payload).OfType<TianxiangTransferredEvent>()
                             .Any(item => item.OwnerSeat == 2); follow++)
                    {
                        Advance(game);
                        Advance(paused);
                    }
                    var transfer = game.Events.Select(item => item.Payload)
                        .OfType<TianxiangTransferredEvent>().Last(item => item.OwnerSeat == 2);
                    Require(transfer.DamageAmount == 2 && transfer.TargetSeat != 2 &&
                            game.Events.Select(item => item.Payload).OfType<ProgramCardDamageModifiedEvent>()
                                .Count(item => item.Source.SkillId == Anjian &&
                                               item.ResolutionId == transfer.ResolutionId) == 1 &&
                            SnapshotJson.Serialize(game.CreateSnapshot(2, true)) ==
                            SnapshotJson.Serialize(paused.CreateSnapshot(2, true)) &&
                            Events(game).SequenceEqual(Events(paused)),
                        "Tianxiang must transfer the already increased amount without a second Anjian evaluation.");
                    return;
                }
                if (prompt is { PlayerSeat: 2 } human)
                {
                    if (human.Kind == DecisionKind.PlayCard)
                    {
                        var ended = game.Submit(new EndPlayPhaseCommand(2, game.Revision, human.PromptId));
                        Require(ended.Accepted, ended.Error?.Message ?? "Could not end Xiao Qiao Play phase.");
                    }
                    else
                    {
                        var choice = human.Choices.FirstOrDefault(item => item.Cards.Count == 0) ??
                                     human.Choices.FirstOrDefault();
                        if (choice is null) break;
                        Answer(game, choice);
                    }
                }
                else Advance(game);
            }
            humanDamageRequests += game.Events.Select(item => item.Payload)
                .OfType<DamageRequestedEvent>().Count(item => item.TargetSeat == 2);
            humanModifiers += game.Events.Select(item => item.Payload)
                .OfType<ProgramCardDamageModifiedEvent>().Count(item =>
                    item.Source.SkillId == Anjian && item.TargetSeat == 2);
        }
        throw new InvalidOperationException($"No seeded Anjian-increased Slash reached a human Tianxiang transfer: setups={eligibleSetups}, tianxiangPrompts={tianxiangPrompts}, modifierObservations={modifierEvents}, humanRequests={humanDamageRequests}, humanModifiers={humanModifiers}.");
    }

    private static ContentRegistry Registry(bool targetsHaveDuodao = true, bool armorSynthetic = false,
        bool noWeapons = false, bool tianxiang = false) => ContentRegistry.Build(
        new StandardContentPackage(), new StandardActiveSkillExpansionPackage(includeJijiu: true),
        new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage(),
        new Scenario(targetsHaveDuodao, armorSynthetic, noWeapons, tianxiang));

    private static void AnswerAction(GameEngine game, string action) => Answer(game,
        game.PendingDecision!.Choices.Single(choice => choice.Parameters.GetValueOrDefault("program-action") == action));

    private static void Answer(GameEngine game, PromptChoice choice)
    {
        var prompt = game.PendingDecision!;
        var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId, choice.Id, game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Pan Zhang skill answer failed.");
    }

    private static GameEngine Start(ContentRegistry registry, int seed, string generalId = General)
    {
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = seed, PlayerCount = 5, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false,
            AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Require(game.Submit(new StartGameCommand()).Accepted, "Pan Zhang fixture did not start.");
        var choice = game.PendingDecision!;
        var selected = game.Submit(new SelectGeneralCommand(0, generalId, game.Revision, choice.PromptId));
        Require(selected.Accepted, selected.Error?.Message ?? "Pan Zhang selection failed.");
        return game;
    }

    private static void ReachPlay(GameEngine game)
    {
        for (var step = 0; step < 50 && game.PendingDecision?.Kind != DecisionKind.PlayCard; step++)
            Advance(game);
        Require(game.PendingDecision?.Kind == DecisionKind.PlayCard, "Pan Zhang fixture did not reach Play.");
    }

    private static void Finish(GameEngine game)
    {
        for (var step = 0; step < 90 && game.ResolutionStack.Count > 0; step++)
        {
            if (game.PendingDecision is { } prompt && prompt.Kind == DecisionKind.ProgramTrigger)
            {
                var choice = prompt.Choices.FirstOrDefault(item =>
                    item.Parameters.GetValueOrDefault("program-action") == "activate") ?? prompt.Choices.First();
                var result = game.Submit(new AnswerPromptCommand(prompt.PlayerSeat, prompt.PromptId,
                    choice.Id, game.Revision));
                Require(result.Accepted, result.Error?.Message ?? "Could not answer damage skill.");
            }
            else Advance(game);
        }
        Require(game.ResolutionStack.Count == 0, "A Slash resolution remained active.");
    }

    private static void Advance(GameEngine game)
    {
        var result = game.Submit(new AdvanceOneStepCommand(game.Revision));
        Require(result.Accepted, result.Error?.Message ?? "Pan Zhang fixture did not advance.");
    }

    private static void Play(GameEngine game, LegalAction action)
    {
        var result = game.Submit(new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats,
            game.Revision, game.PendingDecision!.PromptId, action.PlayedCardKind, action.TargetCardId));
        Require(result.Accepted, result.Error?.Message ?? "Pan Zhang card action failed.");
    }

    private static string[] Events(GameEngine game) => game.Events.Select(item =>
        $"{item.Sequence}|{item.Payload.GetType().Name}|{JsonSerializer.Serialize(item.Payload, item.Payload.GetType())}")
        .ToArray();
    private static void Reject(string rules, string presentation)
    {
        try { _ = SkillProgramCatalog.Load(rules, presentation); }
        catch (InvalidOperationException) { return; }
        throw new InvalidOperationException("Expected invalid schema-56 composition to be rejected.");
    }
    private static void Require(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    private sealed class Scenario(bool targetsHaveDuodao, bool armorSynthetic, bool noWeapons,
        bool tianxiang) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("pan-zhang-check", new Version(1, 0, 0),
            [new PackageDependency("standard-classic-generals", new Version(1, 141, 0))]);
        public void Register(IContentRegistryBuilder builder)
        {
            if (armorSynthetic)
            {
                var catalog = SkillProgramCatalog.Load("""
                    {"schemaVersion":56,"skills":[{"id":"fixture:take-source-armor","revision":1,
                    "minimumRulesVersion":166,"triggers":[{"id":"take","window":"afterDamageApplied",
                    "subject":"owner","damageOccurrence":"perDamage","damageCardKinds":["slash"],
                    "optional":false,"effects":[{"op":"selectSourceCard","target":"owner",
                    "zones":["equipment"],"equipmentSlots":["armor"],"skipIfNoCards":true,
                    "resultBind":"armor"},{"op":"moveBoundCards","target":"owner",
                    "sourceBind":"armor","destination":"ownerHand"}]}]}]}
                    """, """
                    {"schemaVersion":1,"skills":{"fixture:take-source-armor":{"name":"取甲","description":"测试"}}}
                    """);
                builder.AddSkill(new ContentSkillDefinition(ArmorSkill, "取甲", "测试")
                { Program = catalog.Programs[ArmorSkill] });
            }
            var targets = Enumerable.Range(1, 4).Select(index => $"fixture:pan-zhang-target-{index}").ToArray();
            foreach (var target in targets)
                builder.AddGeneral(new ContentGeneralDefinition(target, "测试目标", "supporter",
                    armorSynthetic ? ArmorSkill : tianxiang ? Anjian :
                    targetsHaveDuodao ? Duodao : "standard:none", "qun", BaseHp: 8));
            var cards = Enumerable.Range(0, 180).Select(index =>
                new ContentDeckPhysicalCard((index % (noWeapons ? 2 : 3)) switch
                {
                    0 => "standard:slash",
                    1 when noWeapons => "standard:bagua",
                    1 => "standard:qinggang_sword",
                    _ => "standard:bagua"
                }, (Suit)(index % 4), index % 13 + 1)).ToArray();
            builder.AddDeck(new ContentDeckRecipe("fixture:pan-zhang-deck", "潘璋马忠测试牌堆", 5, 2, [])
            { PhysicalCards = cards });
            builder.AddMode(new ContentModeDefinition(Mode, "潘璋马忠测试", 5, 5,
                new Dictionary<string, int>
                {
                    [nameof(Role.Lord)] = 1,
                    [nameof(Role.Loyalist)] = tianxiang ? 3 : 1,
                    [nameof(Role.Rebel)] = tianxiang ? 1 : 2,
                    [nameof(Role.Renegade)] = tianxiang ? 0 : 1
                }, "fixture:pan-zhang-deck", GeneralCandidateCount: 5,
                GeneralPoolIds: [tianxiang ? "classic:xiao-qiao" : General, .. targets]));
        }
    }
}
