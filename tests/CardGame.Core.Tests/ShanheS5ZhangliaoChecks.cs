using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Core.Campaigns;

internal static class ShanheS5ZhangliaoChecks
{
    public static void PlannedDrawTakesThreeSmallerHandsThenRealPojunReturnsCold()
    {
        var edition = StandardShanheCampaignCatalog.CreateDefinition(1, StandardShanheCampaignCatalog.Returning2026EditionId);
        var encounter = edition.Nodes.Single(node => node.Id == "hulao").Encounters!
            .Single(item => item.Enemies.Any(member => member.GeneralId == StandardShanheS5BossPackage.ZhangliaoBaseId));
        Need(encounter.Name.Contains("基础") && StandardShanheCampaignCatalog.CreateDefinition(1).Nodes
            .SelectMany(node => node.Encounters ?? []).SelectMany(item => item.Enemies)
            .All(member => member.GeneralId != StandardShanheS5BossPackage.ZhangliaoBaseId),
            "The current-configuration base form is an actual 2026 encounter, isolated from the 2024 local entry and advanced-difficulty claims.");
        var opponents = Enumerable.Range(1, 4).Select(seat => new ShanheCombatantDefinition("standard:cao-cao")
            { StartingHand = [new(CardKind.Dodge, Suit.Club, seat + 1)] }).ToArray();
        var setup = new ShanheBattleSetup(83, 1, "fixture-s5-zhangliao", StandardShanheS5BossPackage.ZhangliaoBaseId,
            4, 0, 0, 2, 0, [],
            [new(CardKind.Slash, Suit.Spade, 7), new(CardKind.Slash, Suit.Heart, 8),
                new(CardKind.Slash, Suit.Club, 9), new(CardKind.Slash, Suit.Diamond, 10)], [], opponents, [], new string('b', 64));
        var registry = StandardShanheCampaignCatalog.CreateBattleRegistry(setup);
        var ordinary = StandardShanheCampaignCatalog.CreateBattleRegistry(setup with { PlayerGeneralId = "classic:guan-yu" });
        Need(!ordinary.Generals.ContainsKey(StandardShanheS5BossPackage.ZhangliaoBaseId) &&
            registry.Generals[StandardShanheS5BossPackage.ZhangliaoBaseId].SkillIds.SequenceEqual(
                [StandardShanheS5BossPackage.TuxiId, "boundary:pojun"]),
            "Only a recipe using this form registers its separate package; the exact 3389 Pojun program accompanies its 722 Tuxi.");
        var g = GameEngine.CreateStandard(new()
        {
            Seed = 83, PlayerCount = 5, HumanSeat = 0, HumanRole = null,
            HumanTeamId = ShanheBattleSetup.PlayerTeamId, ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup,
            UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false,
            MaxTurns = 3, AiPolicyVersion = AiDecisionPolicy.CurrentVersion
        }, registry);
        Accept(new StartGameCommand());
        Reach(() => P()?.SkillPrompt?.SkillId == StandardShanheS5BossPackage.TuxiId);
        var handBefore = V(0).HandCount;
        Need(handBefore > 1 && Enumerable.Range(1, 4).All(seat => V(seat).HandCount == 1),
            "The genuine NPC already holds more cards than every eligible opponent before choosing Tuxi.");
        Cold(); Answer(c => Action(c) == "activate");
        Reach(() => P()?.Choices.Any(c => Action(c) == "select-targets") == true);
        Need(P()!.Choices.Any(c => c.Targets.SequenceEqual([1, 2, 3])) &&
            P()!.Choices.All(c => c.Targets.Count <= 4),
            "The real planned four-card budget admits three smaller-hand opponents, without the normal boundary Tuxi's hand-count gate or the old two-target parser cap.");
        Cold(); Answer(c => c.Targets.SequenceEqual([1, 2, 3])); Play();
        var transfers = g.CardMovements.Where(move => move.To == CardLocation.Hand(0) &&
            move.From is { Zone: CardZoneKind.Hand, OwnerSeat: >= 1 and <= 3 }).ToArray();
        var draws = g.CardMovements.Where(move => move.From == CardLocation.DrawPile &&
            move.To == CardLocation.Hand(0) && move.Reason == CardMoveReasons.Draw).ToArray();
        Need(transfers.Length == 3 && transfers.Select(move => move.From.OwnerSeat).SequenceEqual([1, 2, 3]) &&
            transfers.Select(move => move.CardId).Distinct().Count() == 3 && draws.Length == 1 &&
            V(0).HandCount == handBefore + 4 && Enumerable.Range(1, 3).All(seat => V(seat).HandCount == 0),
            "Three actual hand transfers replace exactly three planned draws; one normal draw remains, and transferred cards are not fabricated draws.");
        Cold();
        var held = g.CreateSnapshot(4).Players[4].Hand.Single().Id;
        var slash = g.GetHumanLegalActions().First(action => action.Kind == LegalActionKind.Slash &&
            action.CardId is not null && action.ConversionSource is null && action.TargetSeats.SequenceEqual([4]));
        Accept(new PlayCardCommand(0, slash.CardId!.Value, [4], g.Revision, P()!.PromptId, slash.PlayedCardKind));
        Reach(() => P()?.SkillPrompt?.SkillId == "boundary:pojun");
        Answer(c => Action(c) == "activate");
        Reach(() => P()?.Choices.Any(c => Action(c) == "select-owned-cards") == true);
        Cold(); Answer(c => Action(c) == "select-owned-cards");
        if (P()?.Choices.Any(c => Action(c) == "finish-owned-cards") == true)
            Answer(c => Action(c) == "finish-owned-cards");
        Play();
        Need(g.Events.Select(item => item.Payload).OfType<ProgramHoldCardsPlacedEvent>().Single() is
                { SkillId: "boundary:pojun", HolderSeat: 4 } placed && placed.CardIds.SequenceEqual([held]) &&
            g.CreateCardZoneDiagnostics().Single(card => card.CardId == held).Location == CardLocation.PojunHold(4) &&
            g.Events.Select(item => item.Payload).OfType<DamageAppliedEvent>().Single() is { TargetSeat: 4, Amount: 2 },
            "The same actual base NPC uses 3389's selected-card hold and real hand/equipment comparison bonus, rather than the all-hand variant or ordinary Zhang Liao.");
        Cold(); Accept(new EndPlayPhaseCommand(0, g.Revision, P()!.PromptId));
        Reach(() => g.CardMovements.Any(move => move.CardId == held && move.Reason == CardMoveReasons.PojunHoldReturn));
        Need(g.CardMovements.Count(move => move.CardId == held && move.From == CardLocation.PojunHold(4) &&
                move.To == CardLocation.Hand(4) && move.Reason == CardMoveReasons.PojunHoldReturn) == 1,
            "The real held entity returns to its owner exactly once at the actual turn end.");
        Cold();

        PendingDecision? P() => g.PendingDecision;
        PlayerSnapshot V(int seat) => g.CreateSnapshot(0).Players[seat];
        void Accept(GameCommand command) { var result = g.Submit(command); Need(result.Accepted, result.Error?.Message ?? "S5 NPC rejected a real command."); }
        void Answer(Func<PromptChoice, bool> choose) => Accept(new AnswerPromptCommand(0, P()!.PromptId,
            P()!.Choices.First(choose).Id, g.Revision));
        void Play() => Reach(() => P() is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
        void Reach(Func<bool> done)
        {
            for (var step = 0; step < 150; step++)
            {
                if (done()) return;
                Need(P()?.PlayerSeat != 0 && g.State.Winner == Winner.None,
                    "Unexpected S5 NPC boundary: " + JsonSerializer.Serialize(P()));
                Accept(new AdvanceOneStepCommand(g.Revision));
            }
            throw new InvalidOperationException("Missing bounded S5 NPC continuation.");
        }
        string State() => JsonSerializer.Serialize(new
        {
            Views = Enumerable.Range(0, 5).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))),
            Frames = g.ResolutionStack.Select(frame => JsonSerializer.Serialize(frame, frame.GetType())),
            Facts = g.Events.Select(item => JsonSerializer.Serialize(item.Payload, item.Payload.GetType())),
            g.CardMovements, Zones = g.CreateCardZoneDiagnostics(), Checkpoint = GameCheckpointJson.Serialize(g.CreateCheckpoint())
        });
        void Cold()
        {
            var before = State();
            g = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
            Need(State() == before, "Cold replay preserves all five views, original physical entities, target budget and Pojun's exact pending/paid returns.");
            var zones = g.CreateCardZoneDiagnostics();
            Need(zones.Select(card => card.CardId).Distinct().Count() == zones.Count, "Every real card occupies one zone.");
        }
    }

    private static string? Action(PromptChoice choice) => choice.Parameters.GetValueOrDefault("program-action");
    private static void Need(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
