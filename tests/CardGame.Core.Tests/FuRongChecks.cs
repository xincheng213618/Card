using System.Text.Json;
using System.Reflection;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class FuRongChecks
{
    private const string Skill = "ol:xiaosi", Driver = "fixture:fr-driver", Identity = "fixture:fr-identity", Watch = "fixture:fr-watch";
    private const string Conditional = "fixture:fr-conditional", Claim = "fixture:fr-claim", Mode = "identity:classic-fu-rong-fixture", Owner = "fixture:fr-owner";
    private const string Reclaim = "fixture:fr-reclaim";
    public static void RealDiscardOrderNativeQuotaAndColdReturns()
    {
        var (g, registry) = Start("slash");
        foreach (var invalidId in new[] { 0, -1, int.MaxValue })
        {
            var beforeRejected = ReadState(g);
            var rejected = g.Submit(new PlayCardCommand(0, invalidId, [], g.Revision, P(g)!.PromptId));
            Need(!rejected.Accepted && beforeRejected == ReadState(g),
                "Unknown physical IDs and the reserved zero-material identity must reject without entering discard lookup or changing any command boundary.");
        }
        var ordinary = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == 1);
        PlayCard(g, ordinary); Play(g); Need(E<CardUseDebitRecordedEvent>(g).Count() == 1, "A genuine ordinary Slash spends the native allowance before Xiaosi.");
        var ownerCard = V(g, 0).Hand.First().Id; var targetCard = V(g, 1).Hand.First().Id;
        Begin(g); Need(P(g) is { PlayerSeat: 0, IsPrivate: true } && g.CreateSnapshot(1).PendingDecision is null,
            "The owner alone sees the first real basic-card payment choices.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([ownerCard]));
        Reach(g, () => P(g) is { PlayerSeat: 1, IsPrivate: true, SkillPrompt.SkillId: Skill });
        Need(g.CreateSnapshot(0).PendingDecision is null && E<FuRongPairPaidEvent>(g).Count() == 1,
            "The original other participant privately chooses its own mandatory legal basic card after the owner's real discard.");
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([targetCard])); UseBoundary(g);
        Need(E<FuRongPairPaidEvent>(g).Select(e => e.Payment.CardId).SequenceEqual([ownerCard, targetCard]) &&
            P(g)!.Choices.Any(c => c.Cards.SequenceEqual([targetCard]) && c.Targets.SequenceEqual([2])),
            "Both actual costs enter Discard once, and the other participant's material can be used first against a distance-two target after the ordinary Slash quota is spent.");
        var before = V(g, 2).Hp;
        g = Cold(g, registry); UseCard(g, targetCard, 2); UseBoundary(g); g = Cold(g, registry); UseCard(g, ownerCard, 2); Play(g);
        Need(V(g, 2).Hp == before - 2 && E<FuRongNativeIssuedEvent>(g).Select(e => e.Return.CardId).SequenceEqual([targetCard, ownerCard]) &&
            E<FuRongNativeReturnedEvent>(g).Count() == 2 && E<CardUseDebitRecordedEvent>(g).Count() == 1 &&
            !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill) &&
            new[] { ownerCard, targetCard }.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.DiscardPile && m.To == CardLocation.Processing) == 1) &&
            !g.CardMovements.Any(m => (m.CardId == ownerCard || m.CardId == targetCard) && m.From == CardLocation.DiscardPile && m.To == CardLocation.Hand(0)),
            "Each original discard uses its real native Slash once, in the chosen order, without taking it into hand, consuming ordinary allowance, or refreshing the phase skill quota.");
        _ = Cold(g, registry);
        ReclaimedMaterialCannotBorrowAnOutstandingUseAllowance();
    }
    private static void ReclaimedMaterialCannotBorrowAnOutstandingUseAllowance()
    {
        var (g, registry) = Start("reclaim");
        PlayCard(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeat == 1)); Play(g);
        DriverUse(g, "reclaim"); Play(g);
        var material = V(g, 0).Hand[0].Id;
        Begin(g); Answer(g, c => c.Cards.SequenceEqual([material]));
        Reach(g, () => P(g) is { PlayerSeat: 1, SkillPrompt.SkillId: Skill });
        Answer(g, _ => true); UseBoundary(g); UseCard(g, material, 2);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Reclaim &&
            P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-and-move-owned-card"));
        var slot = V(g, 2).Hand.Select((card, index) => (card, index)).Single(x => x.card.Id == material).index;
        Answer(g, c => c.Parameters.GetValueOrDefault("slot-index") == slot.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Reclaim &&
            P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        var use = g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.FuRongReturn?.CardId == material);
        Need(V(g, 0).Hand.Any(c => c.Id == material) && use.Action!.PhysicalCards.Single().From == CardLocation.DiscardPile &&
            E<CardUseDebitRecordedEvent>(g).Count() == 1 &&
            g.CardMovements.Any(m => m.CardId == material && m.From == CardLocation.Processing && m.To == CardLocation.Hand(2)) &&
            g.CardMovements.Any(m => m.CardId == material && m.From == CardLocation.Hand(2) && m.To == CardLocation.Hand(0)),
            "A real native target child gives the exact issued Slash to its target, then actually returns that same physical entity to the original owner while the old use is still outstanding.");
        g = Cold(g, registry);
        var before = ReadState(g);
        // Public play actions are intentionally unavailable at a native child
        // boundary. Read the same private pure legality queries used by native
        // producers, on actual engine-owned players/cards; no state is injected.
        var players = (IReadOnlyList<CharacterState>)typeof(GameEngine).GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        var card = (Card)typeof(GameEngine).GetMethod("GetAdvancedCard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(g, [material])!;
        bool Query(string name, params (string Name, object? Value)[] values)
        {
            var method = typeof(GameEngine).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            var supplied = values.ToDictionary(v => v.Name, v => v.Value);
            return (bool)method.Invoke(g, method.GetParameters().Select(p => supplied.TryGetValue(p.Name!, out var value) ? value :
                p.HasDefaultValue ? p.DefaultValue : throw new InvalidOperationException("Missing readonly query argument " + p.Name)).ToArray())!;
        }
        Need(!Query("CanSpendSlashUse", ("player", players[0]), ("target", players[2]), ("ignoresCount", false), ("physicalCard", card)) &&
            !Query("CanUseSlashTarget", ("source", players[0]), ("target", players[2]), ("slashCard", card)) &&
            Query("CanUseSlashTarget", ("source", players[0]), ("target", players[2]), ("slashCard", card), ("existingUseFrameId", use.Id)) &&
            Query("HasIssuedFuRongDistance", ("existingUseFrameId", use.Id), ("ownerSeat", 0)) &&
            !Query("HasIssuedFuRongDistance", ("existingUseFrameId", use.Id - 1), ("ownerSeat", 0)) && before == ReadState(g),
            "A fresh query on the recovered hand entity cannot borrow the old Slash's count or distance exemption. Only the exact issued frame keeps its own distance, and every query is pure.");
        Continue(g);
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Reclaim &&
            P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-owned-cards"));
        Answer(g, c => c.Cards.SequenceEqual([material]));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == Reclaim &&
            P(g)!.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        Need(g.CreateCardZoneDiagnostics().Single(c => c.CardId == material).Location == CardLocation.DiscardPile &&
            g.ResolutionStack.OfType<CardUseFrame>().Any(u => u.Id == use.Id && u.FuRongReturn is not null),
            "The same real native child then discards its recovered entity again while the original issued use still owns its return.");
        g = Cold(g, registry); players = (IReadOnlyList<CharacterState>)typeof(GameEngine)
            .GetField("_players", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(g)!;
        before = ReadState(g);
        Need(!Query("IsFuRongSelectedDiscard", ("seat", 0), ("id", material)) &&
            !Query("IsFuRongDiscardQuery", ("actor", players[0]), ("id", material)) &&
            typeof(GameEngine).GetMethod("FindOwnedPlayableCard", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(g, [players[0], material]) is null,
            "An unrelated descendant cannot borrow the ancestor's discard-card playability after the used entity returns to Discard.");
        var rejectedLocation = false;
        try { _ = typeof(GameEngine).GetMethod("FindOwnedCardLocation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(g, [players[0], card]); }
        catch (TargetInvocationException e) when (e.InnerException is InvalidOperationException) { rejectedLocation = true; }
        Need(rejectedLocation && before == ReadState(g), "The descendant cannot obtain a fake owned payment location, and all negative entity queries leave the actual state unchanged.");
        Continue(g); UseBoundaryOrPlay(g);
        if (P(g)?.Kind != DecisionKind.PlayCard) { Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "finish"); Play(g); }
        Need(E<FuRongNativeIssuedEvent>(g).Count() == 1 && E<CardUseDebitRecordedEvent>(g).Count() == 1 &&
            g.CreateCardZoneDiagnostics().Single(c => c.CardId == material).Location == CardLocation.DiscardPile,
            "The original native action returns once, and reclaiming its material does not authorize another Xiaosi use of that spent entity.");
        _ = Cold(g, registry);
    }
    public static void EffectiveBasicsReusePrintedEntitiesAndUnavailableCompensates()
    {
        foreach (var scenario in new[] { "identity", "unable", "claim" })
        {
            var (g, registry) = Start(scenario); var owner = V(g, 0).Hand[0].Id;
            Begin(g); Answer(g, c => c.Cards.SequenceEqual([owner]));
            if (scenario != "unable")
            {
                Reach(g, () => P(g) is { PlayerSeat: 1, SkillPrompt.SkillId: Skill });
                g = Cold(g, registry); Answer(g, _ => true);
            }
            UseBoundaryOrPlay(g);
            Need(E<FuRongPairPaidEvent>(g).All(e => e.Payment is { PrintedKind: CardKind.Crossbow, EffectiveKind: CardKind.Slash }),
                "A physical equipment treated as Slash while owned is a valid actual basic payment, rather than a printed-basic approximation.");
            if (scenario == "claim")
            {
                Need(P(g)?.Kind == DecisionKind.PlayCard && !E<FuRongNativeIssuedEvent>(g).Any() &&
                    E<FuRongPairPaidEvent>(g).All(e => g.CreateCardZoneDiagnostics().Single(c => c.CardId == e.Payment.CardId).Location is { Zone: CardZoneKind.Hand, OwnerSeat: > 0 }),
                    "Discard observer children claim the original materials before reuse; a stolen original cannot be replaced by a similarly named card.");
            }
            else
            {
                Need((scenario == "unable" ? E<FuRongCompensationDrawEvent>(g).Single().ActualCount == 1 : !E<FuRongCompensationDrawEvent>(g).Any()) &&
                    P(g)!.Choices.Where(c => c.Parameters.GetValueOrDefault("branch") == "use").All(c => c.Targets.Count == 0),
                    "An actually unpayable participant causes one compensation draw before reuse, and discarded equipment uses its printed equipment action.");
                while (P(g)?.Kind != DecisionKind.PlayCard)
                { g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "use"); UseBoundaryOrPlay(g); }
                Need(E<FuRongNativeIssuedEvent>(g).All(e => e.Return.Kind == CardKind.Crossbow) &&
                    E<FuRongNativeIssuedEvent>(g).Count() == (scenario == "unable" ? 1 : 2) && V(g, 0).Equipment.Single().Kind == CardKind.Crossbow,
                    "Native equipment children, replacement and typed returns complete each paid printed entity exactly once.");
            }
            _ = Cold(g, registry);
        }
    }
    public static void BasicUseLegalityAndPaidSourceLossSurviveColdChildren()
    {
        foreach (var scenario in new[] { "peach", "hurt-peach", "alcohol", "loss" })
        {
            var (g, registry) = Start(scenario);
            if (scenario == "hurt-peach") { DriverUse(g, "hurt"); Play(g); }
            var before = V(g, 0).Hp; Begin(g); Answer(g, _ => true);
            if (scenario == "loss")
            {
                Reach(g, () => P(g)?.SkillPrompt?.SkillId == Watch);
                Need(V(g, 0).Skills!.All(s => s.Id != Skill) && E<FuRongPairPaidEvent>(g).Count() == 1,
                    "An actual owner-discard child can remove the conditional skill after its completed cost.");
                g = Cold(g, registry); Continue(g);
            }
            Reach(g, () => P(g) is { PlayerSeat: 1, SkillPrompt.SkillId: Skill });
            g = Cold(g, registry); Answer(g, _ => true); UseBoundaryOrPlay(g);
            if (scenario == "peach")
                Need(P(g)?.Kind == DecisionKind.PlayCard && !E<FuRongNativeIssuedEvent>(g).Any(), "Full-health native Peach is not a legal optional use.");
            else
            {
                while (P(g)?.Kind != DecisionKind.PlayCard)
                { g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "use"); UseBoundaryOrPlay(g); }
                Need(E<FuRongNativeIssuedEvent>(g).Count() == (scenario == "hurt-peach" ? 1 : 2) &&
                    (scenario != "hurt-peach" || V(g, 0).Hp == before + 1),
                    "Each finished native use rechecks the remaining original: Peach stops at full HP, Alcohol can be used twice, and already-paid source loss does not retract the operation.");
            }
            Need(E<FuRongPairPaidEvent>(g).Count() == 2 && E<FuRongPairCompletedEvent>(g).Count() == 1,
                "Cold movement and native-use children never repay either participant or complete the parent twice.");
            _ = Cold(g, registry);
        }
        NativePrintedArmorReuseOwnsItsReplacementRecovery();
        PaidCostDeathAndUnpaidSourceLossRespectBoundaries();
    }
    private static void NativePrintedArmorReuseOwnsItsReplacementRecovery()
    {
        var (g, registry) = Start("native-lion");
        PlayCard(g, g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip)); Play(g);
        var oldLion = V(g, 0).Equipment.Single().Id;
        DriverUse(g, "hurt"); Play(g); DriverUse(g, "identity"); Play(g);
        var material = V(g, 0).Hand[0].Id; var before = V(g, 0).Hp;
        Begin(g);
        Need(P(g)!.Choices.All(c => !c.Cards.Contains(oldLion)) && P(g)!.Choices.Any(c => c.Cards.Contains(material)),
            "A hand-only permanent identity permits a Silver Lion hand card as basic cost while the actually equipped Silver Lion remains an equipment and cannot be paid.");
        Answer(g, c => c.Cards.SequenceEqual([material])); UseBoundary(g);
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([material]));
        Reach(g, () => P(g)?.SkillPrompt?.SkillId == "fixture:fr-hp");
        Need(E<FuRongPairPaidEvent>(g).Single().Payment is { PrintedKind: CardKind.SilverLion, EffectiveKind: CardKind.Slash, From.Zone: CardZoneKind.Hand } &&
            E<FuRongNativeIssuedEvent>(g).Single().Return.Kind == CardKind.SilverLion && E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0) == 1 &&
            V(g, 0).Hp == before + 1 && g.ResolutionStack.OfType<CardUseFrame>().Single(u => u.FuRongReturn is not null).CardId == material,
            "Using the original printed armor from Discard replaces the old equipped Lion and owns its one real removal recovery beneath the native use, rather than mislabelling an equipment as basic cost.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g, 0).Equipment.Single().Id == material && E<FuRongNativeReturnedEvent>(g).Count() == 1 &&
            E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0) == 1 &&
            g.CardMovements.Count(m => m.CardId == oldLion && m.From == CardLocation.Equipment(0) && m.To == CardLocation.DiscardPile) == 1,
            "Cold native replacement and HP children resume the paid parent once without replaying equipment placement, the old armor discard or its recovery.");
        _ = Cold(g, registry);
    }
    private static void PaidCostDeathAndUnpaidSourceLossRespectBoundaries()
    {
        {
            var (g, registry) = Start("loss"); DriverUse(g, "hurt"); Play(g);
            Need(V(g, 0).Skills!.All(s => s.Id != Skill) && !g.GetHumanLegalActions().Any(a => a.ProgramSkillId == Skill),
                "Source qualification lost before activation cannot publish an unpaid Xiaosi choice.");
            var revision = g.Revision; var commands = g.AcceptedCommands.Count;
            var rejected = g.Submit(new UseProgramSkillCommand(0, Skill, "paired-basic-discard", [], [1], g.Revision, P(g)!.PromptId));
            Need(!rejected.Accepted && g.Revision == revision && g.AcceptedCommands.Count == commands && !E<FuRongPairPaidEvent>(g).Any(),
                "An unpaid stale source cannot spend a card or create a paid parent through a forged activation.");
            _ = Cold(g, registry);
        }
        {
            var (g, registry) = Start("death"); Begin(g); Answer(g, _ => true);
            Reach(g, () => g.ResolutionStack.OfType<DamageFrame>().Any() || g.ResolutionStack.OfType<DyingFrame>().Any() || !V(g, 0).IsAlive);
            g = Cold(g, registry);
            Reach(g, () => g.State.Status == EngineStatus.Completed);
            Need(!V(g, 0).IsAlive && E<FuRongPairPaidEvent>(g).Count() == 1 && !E<FuRongNativeIssuedEvent>(g).Any() &&
                E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Amount > 0),
                "Real nested cost-observer damage and dying finish before the unstarted partner payment or optional reuse; the paid material is never restored or paid twice.");
            _ = Cold(g, registry);
        }
    }
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(s => g.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static void Accept(GameEngine g, GameCommand c) { var r = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(r.Accepted, r.Error?.Message ?? "Rejected Fu Rong command."); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate) { var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(predicate).Id, g.Revision)); }
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void Begin(GameEngine g) => Accept(g, new UseProgramSkillCommand(0, Skill, "paired-basic-discard", [], [1], g.Revision, P(g)!.PromptId));
    private static void DriverUse(GameEngine g, string id) => Accept(g, new UseProgramSkillCommand(0, Driver, id, [], [], g.Revision, P(g)!.PromptId));
    private static void UseCard(GameEngine g, int id, int target) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "use" && c.Cards.SequenceEqual([id]) && c.Targets.SequenceEqual([target]));
    private static void PlayCard(GameEngine g, LegalAction a) => Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void UseBoundary(GameEngine g) => Reach(g, () => P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use") == true);
    private static void UseBoundaryOrPlay(GameEngine g) => Reach(g, () => P(g)?.Kind == DecisionKind.PlayCard || P(g)?.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "use") == true);
    private static void Reach(GameEngine g, Func<bool> done)
    {
        for (var i = 0; i < 200; i++)
        {
            if (done()) return;
            if (P(g) is { } p && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue")) Continue(g);
            else if (P(g) is { PlayerSeat: 0 } human) throw new InvalidOperationException("Unexpected Fu Rong prompt: " + JsonSerializer.Serialize(human));
            else Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        throw new InvalidOperationException("Fu Rong boundary not reached: " + JsonSerializer.Serialize(P(g)));
    }
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(ReadState(g) == ReadState(restored), "Cold replay preserves private payment choices, effective/printed costs, exact physical origins, native action receipts, used entities and accepted commands."); return restored;
    }
    private static string ReadState(GameEngine e) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(e.CreateSnapshot(s))).ToArray(), Frames = e.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(), Events = e.Events.Select(x => JsonSerializer.Serialize(x.Payload, x.Payload.GetType())).ToArray(), e.CardMovements, Commands = CommandJson.Serialize(e.AcceptedCommands), Zones = e.CreateCardZoneDiagnostics() });
    private static (GameEngine, ContentRegistry) Start(string scenario)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scenario));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); Play(g); return (g, registry);
    }
    private sealed class Fixture(string scenario) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-fu-rong", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(b, "ol:fu-rong");
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"loseHp","target":"owner","amount":1}]},{"id":"identity","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Identity}}"]}]},{"id":"reclaim","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"grantSkills","target":"owner","skillIds":["{{Reclaim}}"]}]}]},
              {"id":"{{Identity}}","revision":1,"cardIdentities":[{"id":"spade-hand-slash","zones":["hand"],"inputKinds":[],"inputSuits":["spade"],"outputKind":"slash"}]},
              {"id":"{{Conditional}}","revision":1,"hpSkillGrants":[{"id":"healthy","skillId":"{{Skill}}","minimumHp":5}]},
              {"id":"{{Watch}}","revision":1,"triggers":[{"id":"cost-child","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["skill-program.fu-rong.xiaosi.discard"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{{(scenario == "death" ? "{\"op\":\"damage\",\"target\":\"owner\",\"amount\":20}" : "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}")}},{"op":"chooseOption","target":"owner","resultBind":"continued","options":[{"id":"continue"}]}]}]},
              {"id":"{{Claim}}","revision":1,"triggers":[{"id":"claim","window":"discardPileReceived","subject":"owner","discardOwnerScope":"other","movementOccurrence":"perCard","movementReasons":["skill-program.fu-rong.xiaosi.discard"],"optional":false,"effects":[{"op":"claimMovedCards","target":"owner"}]}]},
              {"id":"fixture:fr-hp","revision":1,"triggers":[{"id":"native-heal","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"health","options":[{"id":"continue"}]}]}]},
              {"id":"{{Reclaim}}","revision":1,"triggers":[{"id":"native-claim-return","window":"cardUseTargetsFinalized","ownerRelation":"actor","cardKinds":["slash"],"optional":false,"usageScope":"game","usageLimit":1,"effects":[{"op":"claimCurrentUsePhysicalCards","target":"owner"},{"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"eventTarget"},"zones":["hand"],"count":1,"destination":"ownerHand","resultBind":"returned-material","awaitMovementTriggers":true},{"op":"chooseOption","target":"owner","resultBind":"observe-returned","options":[{"id":"continue"}]},{"op":"selectOwnedCards","target":"owner","amount":1,"zones":["hand"],"resultBind":"discard-again"},{"op":"moveBoundCards","target":"owner","sourceBind":"discard-again","destination":"discardPile","awaitMovementTriggers":true},{"op":"chooseOption","target":"owner","resultBind":"observe-discarded","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids = new[] { Driver, Identity, Conditional, Watch, Claim, "fixture:fr-hp", Reclaim };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "真实效死边界夹具", optionLabels = id is Watch or "fixture:fr-hp" or Reclaim ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() }) }));
            foreach (var (id, p) in catalog.Programs) b.AddSkill(new(id, id, "真实规则夹具") { Program = p, ProgramPresentation = catalog.Presentations[id] });
            var humanRole = Role.Lord;
            b.AddSkill(new("fixture:fr-owner-pick", "固定主人", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == humanRole ? 100000d : -100000d) });
            b.AddSkill(new("fixture:fr-peer-pick", "固定其他", "公开评分") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, r => r == humanRole ? -100000d : 100000d) });
            var own = new List<string> { Driver, scenario == "loss" ? Conditional : Skill };
            if (scenario is "identity" or "unable" or "claim") own.Add(Identity); if (scenario is "loss" or "death") own.Add(Watch);
            if (scenario == "native-lion") own.Add("fixture:fr-hp");
            b.AddGeneral(new(Owner, "效死实际主人", "ol-fu-rong", "fixture:fr-owner-pick", "shu", 4, own));
            var peers = Enumerable.Range(1, 3).Select(i => $"fixture:fr-peer-{i}").ToArray();
            for (var i = 0; i < peers.Length; i++)
            {
                var extras = new List<string>(); if (scenario is "identity" or "claim") extras.Add(Identity); if (scenario == "claim") extras.Add(Claim);
                b.AddGeneral(new(peers[i], "实际其他参与者", "supporter", "fixture:fr-peer-pick", "wei", 8, extras));
            }
            var card = scenario is "identity" or "unable" or "claim" ? "crossbow" : scenario is "peach" or "hurt-peach" ? "peach" : scenario == "alcohol" ? "alcohol" : "slash";
            b.AddCard(new("fixture:fr-lion", "白银狮子", "装备牌", "真实原生替换防具", CardKind.SilverLion));
            b.AddDeck(new("fixture:fr-deck", "同质真实实体", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 64).Select(i => new ContentDeckPhysicalCard(scenario == "native-lion" ? "fixture:fr-lion" : "standard:" + card, Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "真实效死", 4, 4, new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:fr-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
