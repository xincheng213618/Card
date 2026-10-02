using System.Reflection;
using System.Text.Json;
using CardGame.Core;
using CardGame.Content.Standard;

internal static class BoundaryZhangFeiChecks
{
    private static readonly ContentRegistry Catalog = ContentRegistry.Build(new StandardContentPackage(), new StandardActiveSkillExpansionPackage(true), new StandardRescueSkillExpansionPackage(), new StandardClassicGeneralPackage());
    public static void CancellationAndUseFreeze()
    {
        var (g, r) = Start();
        Require(!g.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == 2), "Unlimited uses retain ordinary distance.");
        Driver(g, "draw"); Settle(g);
        Slash(g, [1]); Settle(g); Replay(g, r);
        Require(Reserved(g).Count() == 1 && !Consumed(g).Any(), "Real fully satisfied Dodge issues one reserve.");
        Slash(g, [1]);
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single();
        Require(use.NextSlashDamage == 1 && Consumed(g).Count() == 1, "Next accepted real Use freezes and consumes once before response.");
        Settle(g); Replay(g, r);
        Require(Reserved(g).Count() == 2, "Canceled boosted Use consumes its old reserve and creates a fresh one.");
        for (var i = 0; i < 3; i++) { Slash(g, [1]); Settle(g); }
        Require(g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Any(e => e.TargetSeat == 1 && e.Amount == 2), "After the four real Dodges the fifth Slash deals the frozen bonus.");
        Replay(g, r);
    }

    public static void MultiTargetAndExpiry()
    {
        var (g, r) = Start("multi"); Driver(g, "draw"); Settle(g);
        Slash(g, [1, 3]); Settle(g);
        Require(Reserved(g).Count() == 2, "Each separately fully dodged target accumulates one under the explicit default ruling.");
        Slash(g, [1, 3]);
        Require(g.ResolutionStack.OfType<CardUseFrame>().Single().NextSlashDamage == 2 && Consumed(g).Single().Amount == 2, "One use freezes the full accumulated reserve for all targets.");
        Settle(g); Replay(g, r);
        var turn = g.CreateSnapshot(0).TurnNumber;
        Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
        for (var i = 0; i < 40 && g.CreateSnapshot(0).TurnNumber == turn; i++)
        {
            if (P(g) is { Kind: DecisionKind.DiscardCards, PlayerSeat: 0 } p)
                Accept(g, new DiscardCardsCommand(0, g.State.Players[0].Hand.Take(p.RequiredCardCount).Select(c => c.Id).ToArray(), p.PromptId, g.Revision));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        Require(g.Events.Select(e => e.Payload).OfType<NextSlashDamageExpiredEvent>().Single().Amount == 2, "Unused fresh reserve expires at the actual turn boundary."); Replay(g, r);
    }

    public static void RecoveryReceiptAndLimited()
    {
        var (g, r) = Start("recovery", stopAtPreparation: true);
        var before = g.State.Players[0].HandCount;
        var frozen = GameCheckpointJson.Serialize(g.CreateCheckpoint()); var revision = g.Revision;
        var invalid = g.Submit(new AnswerPromptCommand(0, P(g)!.PromptId, new ChoiceId("foreign"), revision));
        Require(!invalid.Accepted && revision == g.Revision && frozen == GameCheckpointJson.Serialize(g.CreateCheckpoint()), "Invalid preparation input rejects atomically.");
        Activate(g); Reach(g, p => p.Kind == DecisionKind.ProgramTrigger && p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "fixture:zf-child"));
        Require(g.State.Players[0].HandCount == before && g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.SkillId == "boundary:tishen").RecoveryReceipt?.Recovered == 2,
            "Real recovery receipt exists before its nested HP child; no premature reward."); Replay(g, r);
        Activate(g); Settle(g);
        Require(g.State.Players[0].Hp == g.State.Players[0].MaxHp - 1 && g.State.Players[0].HandCount == before + 4,
            "Nested Loss does not change the two recovered points; receipt draws two then normal draw two.");
        Require(g.Events.Select(e => e.Payload).OfType<RecoveryAppliedEvent>().Single().Amount == 2 &&
            g.Events.Select(e => e.Payload).OfType<SkillUsageConsumedEvent>().Any(e => e.SkillId == "boundary:tishen" && e.Scope == SkillUsageScope.Game), "First-turn wound may use the named limited quota."); Replay(g, r);
        var players = Players(g); var grant = players[0].SkillGrants.Grants.Single(x => x.SkillId == "boundary:tishen");
        players[0].SkillGrants.RemoveGrant(grant.GrantId); players[0].SkillGrants.Grant(grant with { GrantId = "fixture:renewed", SkillInstanceId = "fixture:renewed", SourceId = "fixture:renewed" });
        var runtime = (SkillRuntimeStateStore)typeof(GameEngine).GetField("_skillRuntimeState", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        Require(runtime.GetUsage(0, "boundary:tishen", "named-group:limited-recovery", SkillUsageScope.Game) == 1, "Host lifecycle audit: regrant preserves named limited usage.");
        var (full, fr) = Start("full", stopAtPreparation: true); var hand = full.State.Players[0].HandCount;
        Activate(full); Settle(full);
        Require(!full.Events.Select(e => e.Payload).OfType<RecoveryAppliedEvent>().Any() && full.State.Players[0].HandCount == hand + 2,
            "Full HP zero receipt never gives free cards."); Replay(full, fr);
        var (lost, _) = Start("recovery", stopAtPreparation: true); hand = lost.State.Players[0].HandCount; Activate(lost);
        Reach(lost, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "fixture:zf-child"));
        var tishen = Players(lost)[0].SkillGrants.Grants.Single(x => x.SkillId == "boundary:tishen"); Players(lost)[0].SkillGrants.RemoveGrant(tishen.GrantId);
        Activate(lost); Settle(lost);
        Require(lost.State.Players[0].HandCount == hand + 2, "Host source-lifetime audit: a lost recovery source cannot issue a tail draw after its HP child.");
    }

    public static void StoneAxeAndQinglong()
    {
        foreach (var paid in new[] { false, true })
        {
            var (g, r) = Start("axe"); Driver(g, "equip", [0]); Settle(g); Driver(g, "draw"); Settle(g);
            Slash(g, [1]); Reach(g, p => p.Kind == DecisionKind.StoneAxe);
            Require(Reserved(g).Count() == 1 && g.ResolutionStack.OfType<CardUseFrame>().Single().NextSlashDamage == 0,
                "Full Dodge issues reserve before axe payment and never modifies the current Slash."); Replay(g, r);
            var p = P(g)!; var chosen = p.Choices.First(c => paid ? c.Cards.Count == 2 : c.Cards.Count == 0);
            Accept(g, new AnswerPromptCommand(0, p.PromptId, chosen.Id, g.Revision)); Settle(g);
            Require(g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Count(e => e.TargetSeat == 1) == (paid ? 1 : 0), "Axe payment independently restores current damage.");
            if (paid) Require(g.Events.Select(e => e.Payload).OfType<DamageAppliedEvent>().Single(e => e.TargetSeat == 1).Amount == 1, "Successful axe deals only current Slash base damage.");
            Require(Reserved(g).Count() == 1 && !Consumed(g).Any(), "Both paid and declined axe keep the next-Use reserve."); Replay(g, r);
        }
        var (blade, br) = Start("qinglong"); Driver(blade, "equip", [0]); Settle(blade); Driver(blade, "draw"); Settle(blade);
        Slash(blade, [1]); Reach(blade, p => p.Kind == DecisionKind.QinglongCrescentBlade);
        Require(Reserved(blade).Count() == 1, "Original full cancellation precedes Qinglong followup.");
        var choice = P(blade)!; Accept(blade, new AnswerPromptCommand(0, choice.PromptId, choice.Choices.First(c => c.Parameters.GetValueOrDefault("action") == "qinglong-slash").Id, blade.Revision));
        Require(Consumed(blade).Single().Amount == 1 && blade.ResolutionStack.OfType<CardUseFrame>().Single().NextSlashDamage == 1,
            "Qinglong starts a genuine new Use and consumes the original reserve."); Replay(blade, br);
    }

    public static void CancellationNegativesAndSourceLifetime()
    {
        var (doubleDodge, dr) = Start("wushuang"); Slash(doubleDodge, [1]); Settle(doubleDodge);
        Require(doubleDodge.Events.Select(e => e.Payload).OfType<CardRespondedEvent>().Count(e => e.ResponderSeat == 1) == 1 && !Reserved(doubleDodge).Any(), "One Dodge never fully cancels Wushuang's two-response Slash."); Replay(doubleDodge, dr);
        var (uncancelable, ur) = Start("uncancelable"); Slash(uncancelable, [1]); Settle(uncancelable);
        Require(uncancelable.Events.Select(e => e.Payload).OfType<CardRespondedEvent>().Any(e => e.ResponderSeat == 1) && !Reserved(uncancelable).Any(), "Real Dodge response to uncancelable Slash produces no cancellation reserve."); Replay(uncancelable, ur);
        var (g, r) = Start(); Slash(g, [1]); Settle(g);
        var owner = Players(g)[0]; var grant = owner.SkillGrants.Grants.Single(x => x.SkillId == "boundary:paoxiao");
        owner.SkillGrants.RemoveGrant(grant.GrantId); owner.SkillGrants.Grant(grant with { GrantId = "fixture:renewed", SkillInstanceId = "fixture:renewed", SourceId = "fixture:renewed" });
        Slash(g, [1]); Require(Consumed(g).Single().Amount == 1, "Host lifecycle audit: lost/regranted source cannot delete or refresh issued facts.");
    }

    public static void PlayedExtraTurnAndActor()
    {
        var (played, pr) = Start("played"); Slash(played, [1]); Settle(played);
        Accept(played, new UseProgramSkillCommand(0, "fixture:zf-extras", "duel", [], [1, 0], played.Revision, P(played)!.PromptId));
        Reach(played, p => p.RequiredCardKind == CardKind.Slash && p.PlayerSeat == 0);
        var prompt = P(played)!; Accept(played, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(c => c.Cards.Count == 1).Id, played.Revision));
        Require(!Consumed(played).Any() && played.Events.Select(e => e.Payload).OfType<CardRespondedEvent>().Any(e => e.ResponderSeat == 0 && e.EffectiveCardKind == CardKind.Slash), "Actual Played Slash response pays an entity without consuming the next Use reserve."); Replay(played, pr);
        Reach(played, p => p.Kind == DecisionKind.PlayCard || p.RequiredCardKind == CardKind.Slash && p.PlayerSeat == 0);
        if (P(played)!.Kind != DecisionKind.PlayCard) { prompt = P(played)!; Accept(played, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(c => c.Cards.Count == 0).Id, played.Revision)); } Settle(played);
        Slash(played, [1]); Require(Consumed(played).Single().Amount == 1, "Next genuine Use still consumes after Played responses."); Replay(played, pr);

        var (extra, er) = Start("extra"); Slash(extra, [1]); Settle(extra);
        Accept(extra, new UseProgramSkillCommand(0, "fixture:zf-extras", "extra", [], [], extra.Revision, P(extra)!.PromptId)); Settle(extra);
        var turn = extra.CreateSnapshot(0).TurnNumber; Accept(extra, new EndPlayPhaseCommand(0, extra.Revision, P(extra)!.PromptId));
        Reach(extra, p => p.Kind == DecisionKind.ProgramTrigger && p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:tishen"));
        prompt = P(extra)!; Accept(extra, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "skip").Id, extra.Revision)); Settle(extra);
        Require(extra.CreateSnapshot(0).TurnNumber == turn + 1 && extra.CreateSnapshot(0).CurrentSeat == 0 && extra.Events.Select(e => e.Payload).OfType<NextSlashDamageExpiredEvent>().Single().Amount == 1, "Extra turn is a new actual turn and expires old reserves.");
        Slash(extra, [1]); Require(!Consumed(extra).Any(), "Extra-turn first Use cannot consume the preceding actual turn's reserve."); Replay(extra, er);

        var (actor, ar) = Start("actor"); Slash(actor, [1]); Reach(actor, p => p.Kind == DecisionKind.ProgramTrigger);
        prompt = P(actor)!; Accept(actor, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "skip").Id, actor.Revision)); Settle(actor);
        Slash(actor, [1]); Reach(actor, p => p.Kind == DecisionKind.ProgramTrigger); Activate(actor);
        Reach(actor, p => p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target")); prompt = P(actor)!;
        Accept(actor, new AnswerPromptCommand(0, prompt.PromptId, prompt.Choices.First(c => c.Targets.SequenceEqual([3])).Id, actor.Revision));
        var use = actor.ResolutionStack.OfType<CardUseFrame>().Single();
        Require(use.Action?.ActorSeat == 3 && use.NextSlashDamage == 0 && !Consumed(actor).Any(), "Final real actor replacement happens before reserve ownership consumption."); Settle(actor); Replay(actor, ar);
        Require(Reserved(actor).Select(e => e.OwnerSeat).SequenceEqual([0, 3]), "Replacement user's canceled Use owns its own named reserve: " + string.Join(",", Reserved(actor).Select(e => e.OwnerSeat)) + "; responses " + string.Join(",", actor.Events.Select(e => e.Payload).OfType<CardRespondedEvent>().Select(e => e.ResponderSeat)));
    }

    public static void BorrowedSwordAndNonCancellation()
    {
        var (g, r) = Start("borrowed"); Driver(g, "equip", [1]); Settle(g); Driver(g, "draw"); Settle(g);
        Slash(g, [3]); Reach(g, p => p.Kind == DecisionKind.ProgramTrigger); Activate(g);
        var p = P(g)!; Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.First(c => c.Targets.SequenceEqual([1])).Id, g.Revision)); Settle(g);
        Require(Reserved(g).Single().OwnerSeat == 1, "Off-turn actual replacement user owns the cancellation reserve.");
        var borrowed = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.BorrowedSword && a.TargetSeats.SequenceEqual([1, 3]));
        Accept(g, new PlayCardCommand(0, borrowed.CardId!.Value, borrowed.TargetSeats, g.Revision, P(g)!.PromptId));
        for (var i = 0; i < 40 && !Consumed(g).Any(); i++) Accept(g, new AdvanceOneStepCommand(g.Revision));
        Require(Consumed(g).Single().OwnerSeat == 1 && Consumed(g).Single().Amount == 1 &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(f => f.CardKind == CardKind.Slash && f.Action?.ActorSeat == 1 && f.NextSlashDamage == 1),
            "Actual Borrowed Sword forced Use consumes the real weapon user's off-turn reserve."); Replay(g, r);
        var (armor, ar) = Start("armor"); Driver(armor, "equip", [1]); Settle(armor); Slash(armor, [1]); Settle(armor);
        Require(armor.Events.Select(e => e.Payload).OfType<ArmorEffectAppliedEvent>().Any(e => e.ArmorCard == CardKind.RenwangShield) && !Reserved(armor).Any(), "Renwang immunity is not Dodge cancellation."); Replay(armor, ar);
        var (prevent, vr) = Start("prevent"); Slash(prevent, [1]); Settle(prevent);
        Require(prevent.Events.Select(e => e.Payload).OfType<ProgramDamagePreventedEvent>().Any() && !Reserved(prevent).Any(), "Prevented damage is not Dodge cancellation."); Replay(prevent, vr);
        var (audit, _) = Start(); var owner = Players(audit)[0]; var original = owner.SkillGrants.Grants.Single(x => x.SkillId == "boundary:paoxiao");
        owner.SkillGrants.Grant(original with { GrantId = "fixture:second", SkillInstanceId = "fixture:second", SourceId = "fixture:second" }); Slash(audit, [1]); Settle(audit);
        Require(Reserved(audit).Count() == 1, "Host lifecycle audit: multiple instances of one named skill cannot duplicate one cancellation fact.");
    }

    public static void StrictOperationBoundaries()
    {
        foreach (var effect in new[] { "recoverToMaximum", "drawRecoveryReceipt", "reserveNextSlashDamage" })
        {
            var body = "\"activations\":[{\"id\":\"bad\",\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"" + effect + "\",\"target\":\"owner\"}]}]";
            Reject(body);
        }
        foreach (var body in new[] {
            "\"triggers\":[{\"id\":\"bad\",\"window\":\"afterDamageApplied\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"reserveNextSlashDamage\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"bad\",\"window\":\"slashFullyDodged\",\"ownerRelation\":\"target\",\"optional\":false,\"effects\":[{\"op\":\"reserveNextSlashDamage\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"bad\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"drawRecoveryReceipt\",\"target\":\"owner\"}]}]",
            "\"triggers\":[{\"id\":\"bad\",\"window\":\"drawPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"recoverToMaximum\",\"target\":\"owner\"},{\"op\":\"drawRecoveryReceipt\",\"target\":\"owner\"}]}]"
        }) Reject(body);
        Require(!JsonSerializer.Serialize(new CardUseFrame(1, 0, 1, CardKind.Slash, [1])).Contains("NextSlashDamage") &&
            !JsonSerializer.Serialize(new CardUseFrame(1, 0, 1, CardKind.Slash, [1])).Contains("NamedSlashCancellations"), "Old card-use canonical omits new null fields.");
        void Reject(string body)
        {
            var rejected = false;
            try { SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-invalid\",\"revision\":1," + body + "}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-invalid\":{\"name\":\"bad\",\"description\":\"bad\"}}}"); }
            catch (InvalidOperationException) { rejected = true; }
            Require(rejected, "New receipt/reserve operations reject unsupported entry or owner contexts.");
        }
    }

    private static IEnumerable<NextSlashDamageReservedEvent> Reserved(GameEngine g) => g.Events.Select(e => e.Payload).OfType<NextSlashDamageReservedEvent>();
    private static IEnumerable<NextSlashDamageConsumedEvent> Consumed(GameEngine g) => g.Events.Select(e => e.Payload).OfType<NextSlashDamageConsumedEvent>();
    private static CharacterState[] Players(GameEngine g) => ((IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(g)!).ToArray();
    private static void Slash(GameEngine g, int[] targets)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == targets[0]);
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, targets, g.Revision, P(g)!.PromptId, a.PlayedCardKind) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    private static (GameEngine, ContentRegistry) Start(string scenario = "normal", bool stopAtPreparation = false)
    {
        var r = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord, ModeId = "identity:classic-zf", UseInteractiveSetup = true, UseInteractiveDiscard = scenario != "extra", AdvanceAfterHumanCommands = false, MaxTurns = 6 }, r);
        Accept(g, new StartGameCommand()); Reach(g, p => p.Kind == DecisionKind.SelectGeneral); Accept(g, new SelectGeneralCommand(0, "fixture:zf", g.Revision, P(g)!.PromptId));
        Reach(g, p => p.Kind == DecisionKind.ProgramTrigger && p.Choices.Any(c => c.Parameters.GetValueOrDefault("skill-id") == "boundary:tishen"));
        if (!stopAtPreparation) { var p = P(g)!; Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "skip").Id, g.Revision)); Settle(g); }
        return (g, r);
    }
    private static void Activate(GameEngine g) { var p = P(g)!; Accept(g, new AnswerPromptCommand(0, p.PromptId, p.Choices.Single(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, g.Revision)); }
    private static PendingDecision? P(GameEngine g) => g.PendingDecision ?? Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s)).Select(s => s.PendingDecision).FirstOrDefault(p => p is not null);
    private static void Driver(GameEngine g, string id, int[]? targets = null) => Accept(g, new UseProgramSkillCommand(0, "fixture:zf-driver", id, [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void Settle(GameEngine g) => Reach(g, p => p.Kind == DecisionKind.PlayCard && p.PlayerSeat == 0);
    private static void Reach(GameEngine g, Func<PendingDecision, bool> goal) { for (var i = 0; i < 100; i++) { if (P(g) is { } p && goal(p)) return; Accept(g, new AdvanceOneStepCommand(g.Revision)); } throw new InvalidOperationException("Boundary not reached: " + P(g)?.Kind); }
    private static void Accept(GameEngine g, GameCommand c) { var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Require(result.Accepted, result.Error?.Message ?? "Rejected"); }
    private static void Replay(GameEngine g, ContentRegistry r) { var h = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), r); Require(Enumerable.Range(0, 4).All(s => SnapshotJson.Serialize(g.CreateSnapshot(s)) == SnapshotJson.Serialize(h.CreateSnapshot(s))) && JsonSerializer.Serialize(g.ResolutionStack) == JsonSerializer.Serialize(h.ResolutionStack) && JsonSerializer.Serialize(g.Events) == JsonSerializer.Serialize(h.Events), "Four viewers, typed stack and issued facts restore exactly."); Require(g.CreateCardZoneDiagnostics().Select(c => c.CardId).Distinct().Count() == 100, "All physical card entities conserved."); }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-zf", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            typeof(StandardContentPackage).Assembly.GetType("CardGame.Content.Standard.BoundaryZhangFeiContent")!.GetMethod("Register", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [b]);
            b.AddSkill(Catalog.GetSkill("classic:wusheng")); if (scenario == "wushuang") b.AddSkill(Catalog.GetSkill("classic:wushuang"));
            if (scenario == "uncancelable") b.AddSkill(Catalog.GetSkill("boundary:zhaxiang"));
            var weapon = scenario == "armor" ? "standard:renwang_shield" : scenario is "axe" or "borrowed" ? "classic:stone-axe" : "classic:qinglong-crescent-blade";
            if (scenario is "axe" or "qinglong" or "borrowed") b.AddCard(Catalog.GetCard(weapon)); if (scenario == "borrowed") b.AddCard(Catalog.GetCard("classic:borrowed-sword"));
            var multi = scenario == "multi" ? ",\"modifiers\":[{\"id\":\"extra\",\"query\":\"cardTargetCount\",\"operation\":\"add\",\"value\":1,\"priority\":0,\"cardKinds\":[\"slash\"]}]" : "";
            var wound = scenario == "recovery" ? "[{\"id\":\"wound\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"optional\":false,\"priority\":100,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":2}]}]" : scenario == "uncancelable" ? "[{\"id\":\"wound\",\"window\":\"playPhaseStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]" : "[]";
            var driver = SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-driver\",\"revision\":1" + multi + ",\"triggers\":" + wound + ",\"activations\":[{\"id\":\"draw\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"draw\",\"target\":\"owner\",\"amount\":12}]},{\"id\":\"equip\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":1,\"maxTargets\":1,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"resultBind\":\"gear\"}]}]}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-driver\":{\"name\":\"固定链\",\"description\":\"固定链\"}}}");
            b.AddSkill(new("fixture:zf-driver", "固定链", "固定链") { Program = driver.Programs["fixture:zf-driver"] });
            var actorTrigger = scenario is "actor" or "borrowed" ? "[{\"id\":\"replace\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"slash\"],\"optional\":true,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"replaceCurrentCardUseActor\",\"target\":\"selectedTarget\"}]}]" : "[]";
            var extras = SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-extras\",\"revision\":1,\"triggers\":" + actorTrigger + ",\"activations\":[{\"id\":\"duel\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":2,\"maxTargets\":2,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"startVirtualDuel\",\"target\":\"owner\"}]},{\"id\":\"extra\",\"usesPerTurn\":null,\"minCards\":0,\"maxCards\":0,\"minTargets\":0,\"maxTargets\":0,\"targetKind\":\"anyLiving\",\"effects\":[{\"op\":\"pendExtraTurn\",\"target\":\"owner\"}]}]}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-extras\":{\"name\":\"实际分支\",\"description\":\"实际分支\"}}}");
            b.AddSkill(new("fixture:zf-extras", "实际分支", "实际分支") { Program = extras.Programs["fixture:zf-extras"] });
            if (scenario == "prevent")
            {
                var shield = SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-prevent\",\"revision\":1,\"triggers\":[{\"id\":\"prevent\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-prevent\":{\"name\":\"防止伤害\",\"description\":\"防止伤害\"}}}");
                b.AddSkill(new("fixture:zf-prevent", "防止伤害", "防止伤害") { Program = shield.Programs["fixture:zf-prevent"] });
            }
            if (scenario == "recovery")
            {
                var child = SkillProgramCatalog.Load("{\"schemaVersion\":" + SkillProgramCatalog.RulesSchemaVersion + ",\"skills\":[{\"id\":\"fixture:zf-child\",\"revision\":1,\"triggers\":[{\"id\":\"child\",\"window\":\"afterHpRecovered\",\"subject\":\"owner\",\"optional\":true,\"effects\":[{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}]}]}]}", "{\"schemaVersion\":3,\"skills\":{\"fixture:zf-child\":{\"name\":\"子窗口\",\"description\":\"子窗口\"}}}");
                b.AddSkill(new("fixture:zf-child", "子窗口", "子窗口") { Program = child.Programs["fixture:zf-child"] });
            }
            b.AddSkill(new("fixture:zf-marker", "固定", "固定") { SelectionWeights = new Dictionary<Role, double> { [Role.Lord] = -1000 } });
            b.AddGeneral(new("fixture:zf", "张飞", "supporter", "boundary:paoxiao", "shu", 4,
                new[] { "boundary:tishen", "classic:wusheng", "fixture:zf-driver", "fixture:zf-extras", "fixture:zf-marker" }.Concat(scenario == "recovery" ? ["fixture:zf-child"] : scenario == "wushuang" ? ["classic:wushuang"] : scenario == "uncancelable" ? ["boundary:zhaxiang"] : Array.Empty<string>()).ToArray()));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:zf-{i}", "其他" + i, "supporter", scenario == "played" ? "classic:wusheng" : scenario is "actor" or "borrowed" ? "boundary:paoxiao" : scenario == "prevent" ? "fixture:zf-prevent" : "standard:none", "wei", 4, scenario == "borrowed" ? ["classic:wusheng"] : []));
            b.AddDeck(new("fixture:zf-deck", "固定", scenario == "wushuang" ? 1 : 4, 2, []) { PhysicalCards = Enumerable.Range(0, 100).Select(i => new ContentDeckPhysicalCard(scenario is "axe" or "qinglong" or "borrowed" or "armor" && i < 20 ? weapon : scenario == "borrowed" && i < 40 ? "classic:borrowed-sword" : scenario is "armor" or "prevent" ? "standard:slash" : "standard:dodge", scenario == "armor" ? Suit.Spade : Suit.Heart, i % 13 + 1)).ToArray() });
            b.AddMode(new("identity:classic-zf", "固定", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Loyalist)] = 1, [nameof(Role.Rebel)] = 2 }, "fixture:zf-deck", GeneralCandidateCount: 4, GeneralPoolIds: ["fixture:zf", "fixture:zf-1", "fixture:zf-2", "fixture:zf-3"]));
        }
    }
}
