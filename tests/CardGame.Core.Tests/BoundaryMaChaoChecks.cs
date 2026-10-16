using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryMaChaoChecks
{
    private static ContentRegistry Classic => ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());

    public static void TieqiPaoxiaoTrueDodgeAndNextUseReplay()
    {
        var (declined, declinedRegistry) = Start(Suit.Heart, "paoxiao");
        Slash(declined, 1); Activate(declined);
        Reach(declined, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        Replay(declined, declinedRegistry);
        Answer(declined, c => c.Cards.Count == 0); Settle(declined);
        Require(declined.Events.Select(e => e.Payload).OfType<CardRespondedEvent>().Count(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) == 1 &&
            declined.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Single().PaymentCompleted == false &&
            !declined.Events.Select(e => e.Payload).OfType<NextSlashDamageReservedEvent>().Any() && declined.CreateSnapshot(1).Players[1].Hp == 3,
            "Tieqi declined true suit cost: a real full Dodge responds but cannot cancel or issue Paoxiao reserve.");
        Replay(declined, declinedRegistry);

        var (paid, paidRegistry) = Start(Suit.Heart, "paoxiao");
        Slash(paid, 1); Activate(paid);
        Reach(paid, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "slash-suit-discard"));
        var payment = Prompt(paid)!.Choices.First(c => c.Cards.Count == 1 &&
            paid.CreateSnapshot(1, true).Players[1].Hand.Single(h => h.Id == c.Cards[0]).Kind == CardKind.Slash);
        Replay(paid, paidRegistry); Answer(paid, c => c.Id == payment.Id); Settle(paid);
        var receipt = paid.Events.Select(e => e.Payload).OfType<ProgramSlashSuitDiscardResolvedEvent>().Single();
        var reserve = paid.Events.Select(e => e.Payload).OfType<NextSlashDamageReservedEvent>().Single();
        Require(receipt.PaymentCompleted && receipt.JudgmentSuit == Suit.Heart && receipt.PaidCardId == payment.Cards.Single() &&
            paid.CardMovements.Count(m => m.CardId == receipt.PaidCardId && m.From == CardLocation.Hand(1) && m.To == CardLocation.DiscardPile) == 1 &&
            paid.Events.Select(e => e.Payload).OfType<CardRespondedEvent>().Count(e => e.ResponderSeat == 1 && e.EffectiveCardKind == CardKind.Dodge) == 1 &&
            reserve.OwnerSeat == 0 && reserve.TargetSeat == 1 && reserve.CardUseFrameId == receipt.CardUseFrameId && reserve.Amount == 1 &&
            paid.CreateSnapshot(1).Players[1].Hp == 4,
            "Completed true same-suit HE movement lets full real Dodge cancel and issue exactly one actual-actor reserve.");
        Replay(paid, paidRegistry);
        paid = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(paid.CreateCheckpoint())), paidRegistry);
        Slash(paid, 1);
        var use = paid.ResolutionStack.OfType<CardUseFrame>().Single();
        var consumed = paid.Events.Select(e => e.Payload).OfType<NextSlashDamageConsumedEvent>().Single();
        Require(use.Action is { Type: CardActionType.Use, ActorSeat: 0 } && use.NextSlashDamage == 1 &&
            use.Id != receipt.CardUseFrameId && consumed.CardUseFrameId == use.Id && consumed.OwnerSeat == 0 && consumed.Amount == 1,
            "The next genuine Slash Use after cold restore freezes and consumes the issued reserve once before Tieqi response.");
        Replay(paid, paidRegistry);
        Answer(paid, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Settle(paid);
        Require(paid.Events.Select(e => e.Payload).OfType<NextSlashDamageConsumedEvent>().Count() == 1,
            "Completing response after a frozen next Use cannot consume its reserve twice.");
        Replay(paid, paidRegistry);
    }

    private static (GameEngine, ContentRegistry) Start(Suit suit, string scenario = "normal")
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(suit, scenario));
        var game = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0,
            HumanRole = Role.Lord, ModeId = "identity:classic-mc", UseInteractiveSetup = true, UseInteractiveDiscard = true,
            AdvanceAfterHumanCommands = false, MaxTurns = 6 }, registry);
        Accept(game, new StartGameCommand()); Reach(game, p => p.Kind == DecisionKind.SelectGeneral);
        Accept(game, new SelectGeneralCommand(0, "fixture:mc-owner", game.Revision, Prompt(game)!.PromptId));
        Settle(game); return (game, registry);
    }
    private static void Slash(GameEngine game, int target)
    {
        var action = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]));
        Accept(game, new PlayCardCommand(0, action.CardId!.Value, [target], game.Revision, Prompt(game)!.PromptId, action.PlayedCardKind));
        Reach(game, p => p.SkillPrompt?.SkillId == "boundary:tieqi");
    }
    private static PendingDecision? Prompt(GameEngine game) => game.PendingDecision ?? Enumerable.Range(0, 4).Select(seat => game.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null && p.Choices.Count > 0);
    private static void Activate(GameEngine game) => Answer(game, c => c.Parameters.GetValueOrDefault("program-action") == "activate" || c.Parameters.GetValueOrDefault("action") == "program-judgment-trigger-activate");
    private static void Answer(GameEngine game, Func<PromptChoice, bool> select)
    {
        var p = Prompt(game)!; Accept(game, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(select).Id, game.Revision));
    }
    private static void Settle(GameEngine game) => Reach(game, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine game, Func<PendingDecision, bool> goal)
    {
        for (var step = 0; step < 100; step++)
        {
            if (Prompt(game) is { } prompt && goal(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("Expected bounded command boundary, found " + Prompt(game)?.Kind);
    }
    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Require(result.Accepted, result.Error?.Message ?? "Rejected real command.");
    }
    private static void Replay(GameEngine game, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(game.CreateCheckpoint())), registry);
        Require(Enumerable.Range(0, 4).All(seat => SnapshotJson.Serialize(game.CreateSnapshot(seat)) == SnapshotJson.Serialize(restored.CreateSnapshot(seat))) &&
            JsonSerializer.Serialize(game.ResolutionStack) == JsonSerializer.Serialize(restored.ResolutionStack), "Four viewer cold replay preserves exact typed stack.");
        Require(game.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 80, "Every physical entity is conserved.");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class Fixture(Suit suit, string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mc", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            CharacterFixtureContent.Register(builder, "boundary:ma-chao");
            foreach (var id in new[] { "classic:mashu", "classic:qingguo" }) builder.AddSkill(Classic.GetSkill(id));
            if (scenario == "paoxiao") builder.AddSkill(Classic.GetSkill("boundary:paoxiao"));
            if (scenario == "replacement") builder.AddSkill(Classic.GetSkill("classic:guicai"));
            if (scenario == "redirect") builder.AddSkill(Classic.GetSkill("classic:liuli"));
            if (scenario == "missing") builder.AddSkill(Classic.GetSkill("classic:hongyan"));
            if (scenario == "bazhen") builder.AddSkill(Classic.GetSkill("classic:bazhen"));
            if (scenario == "legacy") builder.AddSkill(Classic.GetSkill("classic:tieqi"));
            if (scenario == "equipment") builder.AddCard(Classic.GetCard("classic:silver-lion"));

            var rules = """
                {"schemaVersion":62,"skills":[
                {"id":"fixture:mc-driver","revision":1,"activations":[
                  {"id":"request","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"requestSlashAgainstChosenTarget","target":"selectedTarget","resultBind":"requested"}]},
                  {"id":"lose-source","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseOwnerSkillsAndGrant","target":"owner","skillIds":["boundary:tieqi"],"sourceBind":"fixture:mc-locked"}]},
                  {"id":"equip","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"useRandomDeckEquipment","target":"owner","resultBind":"equipped"}]},
                  {"id":"wound","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]}]},
                {"id":"fixture:mc-locked","revision":1,"triggers":[{"id":"equipment-leave","window":"cardsMoved","subject":"owner","sourceZones":["equipment"],"movementOccurrence":"perBatch","optional":true,"effects":[{"op":"draw","target":"owner","amount":1}]}]},
                {"id":"fixture:mc-claim","revision":1,"triggers":[{"id":"claim","window":"judgmentFinalized","subject":"owner","minimumRank":1,"maximumRank":13,"excludedReasons":[],"suits":["spade","heart","club","diamond"],"optional":true,"effects":[{"op":"claimJudgmentCard","target":"owner"}]}]}]}
                """;
            var programs = SkillProgramCatalog.Load(rules, """{"schemaVersion":3,"skills":{"fixture:mc-driver":{"name":"真实链","description":"真实链"},"fixture:mc-locked":{"name":"锁定子链","description":"锁定子链"},"fixture:mc-claim":{"name":"取判定牌","description":"取判定牌"}}}""");
            foreach (var id in programs.Programs.Keys) builder.AddSkill(new(id, id, id)
            { Program = programs.Programs[id], Tags = id == "fixture:mc-locked" ? SkillTag.Locked : SkillTag.None });
            if (scenario == "death")
            {
                var death = SkillProgramCatalog.Load("""{"schemaVersion":62,"skills":[{"id":"fixture:mc-death","revision":1,"triggers":[{"id":"judgment-hp-child","window":"judgmentFinalized","subject":"any","minimumRank":1,"maximumRank":13,"excludedReasons":[],"suits":["spade","heart","club","diamond"],"optional":true,"effects":[{"op":"loseHp","target":"owner","amount":20}]}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:mc-death":{"name":"实际濒死子链","description":"实际濒死子链"}}}""");
                builder.AddSkill(new("fixture:mc-death", "实际濒死子链", "实际濒死子链") { Program = death.Programs["fixture:mc-death"], Tags = SkillTag.Locked });
            }
            if (scenario == "multi")
            {
                var enhance = SkillProgramCatalog.Load("""{"schemaVersion":62,"skills":[{"id":"fixture:mc-enhance","revision":1,"triggers":[{"id":"extra","window":"cardUseCommitted","ownerRelation":"actor","cardKinds":["slash"],"optional":true,"effects":[{"op":"applyCurrentCardEnhancements","target":"owner","amount":1}]}]}]}""",
                    """{"schemaVersion":3,"skills":{"fixture:mc-enhance":{"name":"实际多目标","description":"实际多目标"}}}""");
                builder.AddSkill(new("fixture:mc-enhance", "实际多目标", "实际多目标") { Program = enhance.Programs["fixture:mc-enhance"] });
            }
            builder.AddSkill(new("fixture:mc-marker", "固定", "固定") { SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = -1000 } });
            builder.AddGeneral(new("fixture:mc-owner", "测试马超", "supporter", scenario == "legacy" ? "classic:tieqi" : "boundary:tieqi", "shu", 4,
                new[] { "classic:mashu", "fixture:mc-driver", "fixture:mc-marker" }.Concat(scenario == "paoxiao" ? ["boundary:paoxiao"] : Array.Empty<string>()).Concat(scenario == "replacement" ? ["classic:guicai", "fixture:mc-claim"] : Array.Empty<string>()).Concat(scenario == "multi" ? ["fixture:mc-enhance"] : scenario == "bazhen" ? ["classic:bazhen"] : Array.Empty<string>()).ToArray()));
            for (var seat = 1; seat < 4; seat++) builder.AddGeneral(new($"fixture:mc-{seat}", "目标" + seat, "supporter", "classic:qingguo", "wei", 4,
                new[] { "fixture:mc-locked" }.Concat(scenario == "redirect" && seat == 1 ? ["classic:liuli"] :
                    scenario == "death" && seat == 1 ? ["fixture:mc-death"] : scenario == "missing" ? ["classic:hongyan"] : scenario == "bazhen" ? ["classic:bazhen", "boundary:tieqi"] : scenario == "outside" ? ["boundary:tieqi"] : Array.Empty<string>()).ToArray()));
            var armorId = scenario == "equipment" ? "classic:silver-lion" : "standard:renwang_shield";
            builder.AddDeck(new("fixture:mc-deck", "固定", scenario == "replacement" ? 8 : 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(index => new ContentDeckPhysicalCard(
                scenario == "bazhen" ? "standard:slash" : scenario is "equipment" or "renwang" && index % 6 == 0 ? armorId : index % 2 == 0 ? "standard:slash" : "standard:dodge",
                scenario == "replacement" ? (index % 2 == 0 ? Suit.Heart : Suit.Spade) : suit, index % 13 + 1)).ToArray() });
            builder.AddMode(new("identity:classic-mc", "固定", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1,
                [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:mc-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:mc-owner", "fixture:mc-1", "fixture:mc-2", "fixture:mc-3"]));
        }
    }
}
