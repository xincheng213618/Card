using System.Text.Json;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SpZhangLiaoChecks
{
    private const string Mode = "identity:classic-sp-zhang-liao-fixture", General = "ol:sp-zhang-liao";
    private const string Mubing = "ol:mubing", Ziqu = "ol:ziqu", Diaoling = "ol:diaoling";
    private const string Driver = "fixture:spzl-driver", Quiet = "fixture:spzl-quiet";
    private const string Cost = "fixture:spzl-cost", Gain = "fixture:spzl-gain", Gift = "fixture:spzl-gift";
    private const string Hp = "fixture:spzl-hp";
    private const string Paid = "skill-program.ol:mubing.DiscardSelected";
    private const string Taken = "skill-program.ol:mubing.ObtainSelected";
    private const string Given = "skill-program.ol:mubing.GiveSelected";

    public static void NativeEqualAndZeroRankExchangesKeepOnePhysicalPayment()
    {
        var (g, registry) = Start();
        Activate(g); At(g, SpZhangLiaoStage.Payment);
        var firstReveal = E<ProgramCardsRevealedEvent>(g).Last(f => f.SkillId == Mubing).Cards.Select(c => c.Id).ToArray();
        Need(firstReveal.Length == 3 && firstReveal.All(id => Location(g, id) == CardLocation.Processing),
            "The actual first play phase reveals exactly three physical deck cards before any payment.");
        Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } && Enumerable.Range(1, 3).All(seat => g.CreateSnapshot(seat).PendingDecision is null),
            "Only the owner sees their private hand toggles and selected payment.");
        Reject(g, new AnswerPromptCommand(1, P(g)!.PromptId, P(g)!.Choices.Last().Id, g.Revision));
        g = Cold(g, registry); Option(g, "confirm"); At(g, SpZhangLiaoStage.Obtain);
        Need(P(g)!.Choices.All(c => Option(c) != "card"), "A zero payment offers no positive-rank acquisition.");
        Option(g, "confirm"); Play(g);
        Need(E<MubingCardsAcquiredEvent>(g).Single() is { CardCount: 0, MilitaryCardCount: 0, PaymentRank: 0, ObtainedRank: 0 } &&
            firstReveal.All(id => Location(g, id) == CardLocation.DiscardPile) && g.CardMovements.All(m => m.Reason.Value != Paid),
            "An accepted zero-for-zero exchange pays nothing and cleans up all three public entities once.");
        g = Cold(g, registry); NextMubing(g); Activate(g); At(g, SpZhangLiaoStage.Payment);
        var cost = V(g, 0).Hand.First().Id; Card(g, cost); g = Cold(g, registry); Option(g, "confirm");
        Reach(g, p => p.SkillPrompt?.SkillId == Cost);
        var payFrame = Owner(g);
        Need(payFrame.SpZhangLiao is { Stage: SpZhangLiaoStage.PaymentChildren, Budget: 7 } &&
            Location(g, cost) == CardLocation.DiscardPile && g.CardMovements.Count(m => m.CardId == cost && m.Reason.Value == Paid) == 1,
            "The owning frame pays the selected physical hand card before its native movement child pauses.");
        g = Cold(g, registry); Continue(g); At(g, SpZhangLiaoStage.Obtain);
        var offered = P(g)!.Choices.Where(c => Option(c) == "card").ToArray();
        Need(offered.Length == 3, "Equality permits taking any one rank-seven revealed entity after paying seven.");
        var acquired = offered[0].Cards.Single(); Card(g, acquired);
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, offered[1].Id, g.Revision));
        Need(Owner(g).SpZhangLiao is { Budget: 7, SpentRank: 7 } && P(g)!.Choices.Count(c => Option(c) == "card") == 1,
            "A selected exact-budget card remains deselectable while a second card over budget is atomically rejected.");
        g = Cold(g, registry); Option(g, "confirm"); Reach(g, p => p.SkillPrompt?.SkillId == Gain);
        Need(Location(g, acquired) == CardLocation.Hand(0) && E<MubingCardsAcquiredEvent>(g).Last() is
            { CardCount: 1, MilitaryCardCount: 1, PaymentRank: 7, ObtainedRank: 7 },
            "Actual acquisition records the paid equality and military weapon before its own gain child.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(g.CardMovements.Count(m => m.CardId == cost && m.Reason.Value == Paid) == 1 &&
            g.CardMovements.Count(m => m.CardId == acquired && m.Reason.Value == Taken) == 1,
            "Recovering the private selection, paid child and gain child neither repays nor reacquires an entity.");
        _ = Cold(g, registry);
    }

    public static void AwakeningCountsAcquisitionsBeforeUpgradeAndOneDistribution()
    {
        var (g, registry) = Start(returnGift: true);
        var maximum = V(g, 0).MaxHp;
        var earlier = new List<int>();
        for (var exchange = 0; exchange < 2; exchange++)
        {
            if (exchange != 0) NextMubing(g);
            Activate(g); At(g, SpZhangLiaoStage.Payment);
            var taken = Exchange(ref g, registry, 3); earlier.AddRange(taken); Play(g);
            foreach (var id in taken)
            {
                DriverUse(g, "give", [3], [id]); Play(g);
            }
            Need(taken.All(id => Location(g, id) == CardLocation.Hand(3)),
                "Each previous military acquisition really leaves the owner's hand before the next preparation.");
        }
        Need(E<MubingCardsAcquiredEvent>(g).Sum(f => f.MilitaryCardCount) == 6 &&
            earlier.All(id => Location(g, id) != CardLocation.Hand(0)) && !E<DiaolingAwakenedEvent>(g).Any(),
            "The six-card condition is cumulative actual acquisition, with none of those six entities currently held.");
        g = Cold(g, registry); End(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Diaoling && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "draw-two"));
        Need(E<DiaolingAwakenedEvent>(g).Single() is { OwnerSeat: 0, MilitaryCardCount: 6 } && V(g, 0).MaxHp == maximum,
            "The actual next preparation awakens once from historical acquisitions without reducing maximum HP.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "draw-two");
        Reach(g, p => Activation(p, Mubing));
        Need(g.CardMovements.Count(m => m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.ol:diaoling.Draw") == 2,
            "The selected awakening benefit draws exactly two real deck cards.");
        Activate(g); At(g, SpZhangLiaoStage.Payment);
        Need(E<ProgramCardsRevealedEvent>(g).Last(f => f.SkillId == Mubing).Cards.Count == 4,
            "The first post-awakening native play phase reveals four cards.");
        var acquired = Exchange(ref g, registry, 4, stopAtGift: true);
        Need(acquired.All(id => Location(g, id) == CardLocation.Hand(0)) &&
            E<MubingCardsAcquiredEvent>(g).Sum(f => f.MilitaryCardCount) == 10,
            "Upgraded Mubing first acquires all selected entities into the owner's real hand and counts them before distribution.");
        var giving = acquired.Take(2).ToArray();
        foreach (var id in giving) Card(g, id);
        g = Cold(g, registry); Option(g, "confirm"); At(g, SpZhangLiaoStage.GiftTarget);
        Need(g.CreateSnapshot(1).PendingDecision is null, "The proposed private gift subset and recipient prompt belong to the skill owner.");
        g = Cold(g, registry); Answer(g, c => Option(c) == "recipient" && c.Targets.SequenceEqual([1]));
        Reach(g, p => p.SkillPrompt?.SkillId == Gift);
        Need(giving.All(id => Location(g, id) == CardLocation.Hand(1)) && giving.All(id =>
            g.CardMovements.Single(m => m.CardId == id && m.Reason.Value == Taken).Sequence <
            g.CardMovements.Single(m => m.CardId == id && m.Reason.Value == Given).Sequence),
            "The recipient's genuine gain child starts only after acquisition and the separate one-time gift.");
        g = Cold(g, registry); Continue(g);
        Reach(g, p => p.SkillPrompt?.SkillId == Gift && p.Choices.Any(c => c.Targets.Contains(0)));
        Answer(g, c => c.Targets.SequenceEqual([0]));
        Reach(g, p => p.SkillPrompt?.SkillId == Gift && p.Choices.Any(c => c.Cards.Contains(giving[0])));
        Answer(g, c => c.Cards.SequenceEqual([giving[0]])); At(g, SpZhangLiaoStage.GiftCards);
        Need(Location(g, giving[0]) == CardLocation.Hand(0) && P(g)!.Choices.Where(c => Option(c) == "card")
                .SelectMany(c => c.Cards).Order().SequenceEqual(acquired.Except(giving).Order()),
            "A real native child returning an already distributed entity cannot make that entity distributable twice in the same Mubing.");
        g = Cold(g, registry); Option(g, "finish"); Play(g);
        Need(E<DiaolingAwakenedEvent>(g).Count() == 1 && V(g, 0).MaxHp == maximum &&
            giving.All(id => g.CardMovements.Count(m => m.CardId == id && m.Reason.Value == Given) == 1),
            "Nested gifting and all recovery boundaries preserve one awakening and one distribution per acquired entity.");
        _ = Cold(g, registry);
    }

    public static void ZiquUsesActualSourceHighestHeAndPerVictimGameQuota()
    {
        var (g, registry) = Start(); Skip(g); Play(g);
        DriverUse(g, "duel-others", [2, 3]); Play(g);
        Need(E<DamageAppliedEvent>(g).Any(f => f.SourceSeat == 2 && f.TargetSeat == 3) && !E<ZiquDamagePreventedEvent>(g).Any(),
            "A native Duel between two other players uses its actual damage source and never offers the requesting owner's Ziqu.");
        var equipped = V(g, 0).Hand.First().Id;
        DriverUse(g, "place", [1], [equipped]); Play(g);
        var hand = V(g, 1).Hand.Select(c => c.Id).ToArray();
        Need(Location(g, equipped) == CardLocation.Equipment(1), "The victim actually has a rank-seven weapon in equipment as well as equal-rank hand cards.");
        var hp = V(g, 1).Hp;
        Slash(g, 1); Reach(g, p => Activation(p, Ziqu));
        g = Cold(g, registry); Activate(g); At(g, SpZhangLiaoStage.HighestCard);
        Need(P(g) is { PlayerSeat: 1, IsPrivate: true } && g.CreateSnapshot(0).PendingDecision is null &&
            P(g)!.Choices.All(c => Option(c) == "highest") && P(g)!.Choices.SelectMany(c => c.Cards).Order()
                .SequenceEqual(hand.Append(equipped).Order()) && V(g, 1).Hp == hp,
            "After preventing the genuine native Slash, only the victim sees every tied maximum HE choice, including equipment.");
        Reject(g, new AnswerPromptCommand(0, P(g)!.PromptId, P(g)!.Choices.First().Id, g.Revision));
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([equipped])); Play(g);
        Need(Location(g, equipped) == CardLocation.Hand(0) && V(g, 1).Hp == hp &&
            E<ZiquDamagePreventedEvent>(g).Single() is { VictimSeat: 1, Source.OwnerSeat: 0 } &&
            g.CardMovements.Count(m => m.CardId == equipped && m.Reason.Value == "skill-program.ol:ziqu.GiveSelected") == 1,
            "The actual victim's selected tied equipment is transferred once and the whole incoming damage is prevented.");
        DriverUse(g, "hurt", [1]); Play(g);
        Need(V(g, 1).Hp == hp - 1 && E<ZiquDamagePreventedEvent>(g).Count() == 1,
            "A second damage to the same victim has no second Ziqu opportunity in the same turn.");
        DriverUse(g, "hurt", [2]); Reach(g, p => Activation(p, Ziqu));
        g = Cold(g, registry); Activate(g); At(g, SpZhangLiaoStage.HighestCard);
        var otherCard = P(g)!.Choices.First().Cards.Single(); Answer(g, c => c.Cards.SequenceEqual([otherCard])); Play(g);
        Need(E<ZiquDamagePreventedEvent>(g).Select(f => f.VictimSeat).Order().SequenceEqual([1, 2]),
            "A different actual victim has their independent once-per-game replacement.");
        g = Cold(g, registry); NextMubing(g); Skip(g); Play(g);
        DriverUse(g, "hurt", [1]); Play(g);
        Need(V(g, 1).Hp == hp - 2 && E<ZiquDamagePreventedEvent>(g).Count() == 2,
            "Beginning a later real turn does not renew a victim's already consumed game quota.");
        _ = Cold(g, registry);
    }

    public static void ZiquSilverLionRecoveryReturnsCold()
    {
        var (g, registry) = Start(silver: true); Skip(g); Play(g);
        DriverUse(g, "self-damage", []); Play(g);
        DriverUse(g, "source-less", []); Play(g);
        Need(E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.SourceSeat == 0) == 2 &&
            E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.SourceLess) == 1 &&
            !E<ProgramBindingStartedEvent>(g).Any(e => e.SkillId == Ziqu),
            "Real self-inflicted and source-less damage cannot offer Ziqu or manufacture a foreign victim quota.");
        var maximum = V(g, 1).MaxHp;
        DriverUse(g, "hurt", [1]); Reach(g, p => Activation(p, Ziqu)); Skip(g); Play(g);
        Need(V(g, 1).Hp == maximum - 1 && !E<ZiquDamagePreventedEvent>(g).Any(),
            "Declining the real first damage leaves the target wounded and its Ziqu quota unspent.");
        var lion = V(g, 0).Hand.First(c => c.Kind == CardKind.SilverLion).Id;
        DriverUse(g, "place", [1], [lion]); Play(g);
        Need(Location(g, lion) == CardLocation.Equipment(1) && !E<SilverLionRemovedRecoveryEvent>(g).Any(),
            "The actual wounded target equips the physical Silver Lion without an invented removal recovery.");
        Slash(g, 1); Reach(g, p => Activation(p, Ziqu)); Activate(g); At(g, SpZhangLiaoStage.HighestCard);
        g = Cold(g, registry); Answer(g, c => c.Cards.SequenceEqual([lion]));
        Reach(g, p => p.SkillPrompt?.SkillId == Hp);
        var root = Owner(g); var receipt = root.SpZhangLiao!;
        var before = g.ResolutionStack.OfType<BeforeDamageProgramWindowFrame>().Single(f => f.Id == receipt.ParentFrameId);
        var child = g.ResolutionStack.OfType<HpChangedTriggerWindowFrame>().Single();
        Need(receipt.Stage == SpZhangLiaoStage.HighestChildren && receipt.Selected.SequenceEqual([lion]) &&
            before is { SourceSeat: 0, TargetSeat: 1, Prevented: true } &&
            child.ResumeFrameId == root.Id && child.Continuation == PostEventContinuation.Program &&
            child.Change.ParentFrameId == root.Id && child.Change is
                { Kind: HpChangeKind.Recovery, SourceSeat: 1, TargetSeat: 1, Amount: 1 } &&
            Location(g, lion) == CardLocation.Hand(0) && V(g, 1).Hp == maximum &&
            g.CardMovements.Count(m => m.CardId == lion && m.From == CardLocation.Equipment(1) &&
                m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.ol:ziqu.GiveSelected") == 1,
            "The paid Lion is already transferred once while its one-point native recovery pauses under the same still-prevented damage parent and owning Ziqu frame.");
        Need(E<SilverLionRemovedRecoveryEvent>(g).Single() is
            { PlayerSeat: 1, RecoveredAmount: 1 } recovered && recovered.Reason.Value == "skill-program.ol:ziqu.GiveSelected" &&
            E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 1) == 1,
            "The actual Silver Lion removal recovers once; the prevented second native damage produces no applied damage.");
        g = Cold(g, registry); Continue(g); Play(g);
        Need(V(g, 1).Hp == maximum && Location(g, lion) == CardLocation.Hand(0) &&
            E<SilverLionRemovedRecoveryEvent>(g).Count() == 1 && E<ZiquDamagePreventedEvent>(g).Count() == 1 &&
            E<DamageAppliedEvent>(g).Count(e => e.TargetSeat == 1) == 1 &&
            g.CardMovements.Count(m => m.CardId == lion && m.Reason.Value == "skill-program.ol:ziqu.GiveSelected") == 1 &&
            !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.SpZhangLiao is not null),
            "Cold recovery resumes the exact HP child and original damage without repaying the Lion, repeating healing, applying prevented damage or leaving its parent suspended.");
        _ = Cold(g, registry);
    }

    private static int[] Exchange(ref GameEngine g, ContentRegistry registry, int count, bool stopAtGift = false)
    {
        At(g, SpZhangLiaoStage.Payment);
        var paying = V(g, 0).Hand.Take(count).Select(c => c.Id).ToArray();
        Need(paying.Length == count, "The small fixture provides enough actual hand cards for its rank payment.");
        foreach (var id in paying) Card(g, id);
        Option(g, "confirm"); Reach(g, p => p.SkillPrompt?.SkillId == Cost); g = Cold(g, registry); Continue(g);
        At(g, SpZhangLiaoStage.Obtain);
        var taken = E<ProgramCardsRevealedEvent>(g).Last(f => f.SkillId == Mubing).Cards.Take(count).Select(c => c.Id).ToArray();
        foreach (var id in taken) Card(g, id);
        Option(g, "confirm"); Reach(g, p => p.SkillPrompt?.SkillId == Gain); g = Cold(g, registry); Continue(g);
        if (stopAtGift) At(g, SpZhangLiaoStage.GiftCards); else Play(g);
        var completed = g;
        Need(paying.All(id => completed.CardMovements.Count(m => m.CardId == id && m.Reason.Value == Paid) == 1) &&
            taken.All(id => completed.CardMovements.Count(m => m.CardId == id && m.Reason.Value == Taken) == 1),
            "Native cost and gain children resume without paying or acquiring an actual entity twice.");
        return taken;
    }
    private static ProgramSkillFrame Owner(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.SpZhangLiao is not null);
    private static void At(GameEngine g, SpZhangLiaoStage stage) => Reach(g, p =>
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "sp-zhang-liao") && Owner(g).SpZhangLiao!.Stage == stage);
    private static string? Option(PromptChoice c) => c.Parameters.GetValueOrDefault("option");
    private static void Option(GameEngine g, string option) => Answer(g, c => Option(c) == option);
    private static void Card(GameEngine g, int id) => Answer(g, c => Option(c) == "card" && c.Cards.SequenceEqual([id]));
    private static bool Activation(PendingDecision p, string skill) => p.SkillPrompt?.SkillId == skill &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Activate(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
    private static void Skip(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
    private static void Continue(GameEngine g) => Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void NextMubing(GameEngine g) { End(g); Reach(g, p => Activation(p, Mubing)); }
    private static void Play(GameEngine g) => Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Slash(GameEngine g, int target)
    {
        var a = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Slash && a.TargetSeats.SequenceEqual([target]) && a.ConversionSource?.SkillId == Driver);
        Accept(g, new PlayCardCommand(0, a.CardId!.Value, a.TargetSeats, g.Revision, P(g)!.PromptId, a.PlayedCardKind, a.TargetCardId)
        { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    private static void DriverUse(GameEngine g, string activation, IReadOnlyList<int> targets, IReadOnlyList<int>? cards = null) =>
        Accept(g, new UseProgramSkillCommand(0, Driver, activation, cards ?? [], targets, g.Revision, P(g)!.PromptId));
    private static IEnumerable<T> E<T>(GameEngine g) => g.Events.Select(e => e.Payload).OfType<T>();
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(seat).Players[seat];
    private static CardLocation Location(GameEngine g, int id) => g.CreateCardZoneDiagnostics().Single(c => c.CardId == id).Location;
    private static PendingDecision? P(GameEngine g) => Enumerable.Range(0, 4).Select(seat => g.CreateSnapshot(seat).PendingDecision).FirstOrDefault(p => p is not null);
    private static void Answer(GameEngine g, Func<PromptChoice, bool> choose)
    {
        var p = P(g)!; Accept(g, new AnswerPromptCommand(p.PlayerSeat, p.PromptId, p.Choices.First(choose).Id, g.Revision));
    }
    private static void Reach(GameEngine g, Func<PendingDecision, bool> reached)
    {
        for (var step = 0; step < 200; step++)
        {
            if (P(g) is { } p)
            {
                if (reached(p)) return;
                if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("response") == "pass"))
                { Answer(g, c => c.Parameters.GetValueOrDefault("response") == "pass"); continue; }
            }
            Accept(g, new AdvanceOneStepCommand(g.Revision));
        }
        var pending = P(g);
        throw new InvalidOperationException("SP Zhang Liao missed the actual native boundary: " + JsonSerializer.Serialize(new {
            pending?.Kind, pending?.PlayerSeat, Skill = pending?.SkillPrompt?.SkillId,
            Stage = g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.SpZhangLiao is not null)?.SpZhangLiao?.Stage,
            Facts = g.Events.TakeLast(5).Select(e => e.Payload.GetType().Name).ToArray() }));
    }
    private static void Accept(GameEngine g, GameCommand command)
    {
        var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(result.Accepted, result.Error?.Message ?? "Rejected real SP Zhang Liao fixture command.");
    }
    private static void Reject(GameEngine g, GameCommand command)
    {
        var before = State(g); var result = g.Submit(CommandJson.Deserialize(CommandJson.Serialize([command])).Single());
        Need(!result.Accepted && State(g) == before, "An invalid actual chooser or over-budget choice is rejected without changing views, facts, entities, frames or accepted commands.");
    }
    private static string State(GameEngine g) => JsonSerializer.Serialize(new {
        Views = Enumerable.Range(0, 4).Select(seat => SnapshotJson.Serialize(g.CreateSnapshot(seat))).ToArray(),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())).ToArray(),
        Facts = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())).ToArray(),
        g.CardMovements, Commands = CommandJson.Serialize(g.AcceptedCommands), Zones = g.CreateCardZoneDiagnostics() });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    {
        var restored = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry);
        Need(State(g) == State(restored), "Every player's private snapshot, frozen native owning frame, actual movement and command prefix cold-restores exactly.");
        return restored;
    }
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static (GameEngine Game, ContentRegistry Registry) Start(bool returnGift = false, bool silver = false)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(returnGift, silver));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Mode, UseInteractiveSetup = true, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 20 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, p => p is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, General, g.Revision, P(g)!.PromptId));
        Reach(g, p => Activation(p, Mubing)); return (g, registry);
    }
    private sealed class Fixture(bool returnGift, bool silver) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sp-zhang-liao", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder b)
        {
            CharacterFixtureContent.Register(new OwnerBuilder(b), General);
            var returnEffects = returnGift ? """
                ,{"op":"selectTarget","target":"owner","targetKind":"otherLiving"},
                 {"op":"selectAndMoveOwnedCard","target":"owner","chooserRef":{"kind":"owner"},"cardOwnerRef":{"kind":"owner"},"zones":["hand"],"count":1,"destination":"selectedTargetHand","targetRef":{"kind":"selectedTarget"},"awaitMovementTriggers":true}
                """ : "";
            var rules = $$"""
            {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[
              {"id":"{{Driver}}","revision":1,"viewAs":[{"id":"as-slash","inputKinds":["{{(silver ? "silverLion" : "crossbow")}}"],"inputSuits":[],"outputKind":"slash","forPlay":true,"forResponse":false}],"activations":[
                {"id":"hurt","minCards":0,"maxCards":0,"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"selectedTarget","amount":1}]},
                {"id":"self-damage","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]},
                {"id":"source-less","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"receiveOwnerDamage","target":"owner","amount":1}]},
                {"id":"duel-others","minCards":0,"maxCards":0,"minTargets":2,"maxTargets":2,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"startVirtualDuel","target":"owner"}]},
                {"id":"place","minCards":1,"maxCards":1,"sourceZones":["hand"],"cardCategories":["equipment"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}]},
                {"id":"give","minCards":1,"maxCards":1,"sourceZones":["hand"],"minTargets":1,"maxTargets":1,"targetKind":"otherLiving","usesPerTurn":null,"effects":[{"op":"giveSelected","target":"selectedTarget","amount":1}]}]},
              {"id":"{{Quiet}}","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]},
              {"id":"{{Cost}}","revision":1,"triggers":[{"id":"paid","window":"cardsMoved","subject":"owner","sourceZones":["hand"],"movementOccurrence":"perOwnerBatch","movementReasons":["{{Paid}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gain}}","revision":1,"triggers":[{"id":"acquired","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Taken}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"acquired","options":[{"id":"continue"}]}]}]},
              {"id":"{{Gift}}","revision":1,"triggers":[{"id":"given","window":"cardsGained","subject":"owner","destinationZones":["hand"],"movementOccurrence":"perBatch","movementReasons":["{{Given}}"],"optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"given","options":[{"id":"continue"}]}{{returnEffects}}]}]},
              {"id":"{{Hp}}","revision":1,"triggers":[{"id":"recovered","window":"afterHpRecovered","subject":"owner","optional":false,"effects":[{"op":"chooseOption","target":"owner","resultBind":"recovered","options":[{"id":"continue"}]}]}]}
            ]}
            """;
            var ids = new[] { Driver, Quiet, Cost, Gain, Gift, Hp };
            var catalog = SkillProgramCatalog.Load(rules, JsonSerializer.Serialize(new {
                schemaVersion = SkillProgramCatalog.PresentationSchemaVersion,
                skills = ids.ToDictionary(id => id, id => new { name = id, description = "Small actual SP Zhang Liao native boundary fixture.",
                    optionLabels = id is Cost or Gain or Gift or Hp
                        ? new Dictionary<string, string> { ["continue"] = "Continue the actual native child" } : [] }) }));
            foreach (var (id, program) in catalog.Programs)
                b.AddSkill(new(id, id, "Actual SP Zhang Liao boundary fixture") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            for (var seat = 1; seat < 4; seat++) b.AddGeneral(new($"fixture:spzl-peer-{seat}", "Quiet native participant", "supporter", Quiet, "wei", 8,
                silver ? [Gift, Hp] : [Gift]));
            if (silver) b.AddCard(new("fixture:spzl-lion", "白银狮子", "装备牌", "实际移除恢复", CardKind.SilverLion));
            b.AddDeck(new("fixture:spzl-deck", "Uniform real rank-seven military weapons", 4, 2, []) {
                PhysicalCards = Enumerable.Range(0, 128).Select(_ => new ContentDeckPhysicalCard(silver ? "fixture:spzl-lion" : "standard:crossbow", Suit.Club, 7)).ToArray() });
            b.AddMode(new(Mode, "Actual SP Zhang Liao", 4, 4,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:spzl-deck",
                GeneralCandidateCount: 4, GeneralPoolIds: [General, "fixture:spzl-peer-1", "fixture:spzl-peer-2", "fixture:spzl-peer-3"]));
        }
    }
    private sealed class OwnerBuilder(IContentRegistryBuilder b) : IContentRegistryBuilder
    {
        public void AddGeneral(ContentGeneralDefinition d) => b.AddGeneral(d.Id == General
            ? d with { AdditionalSkillIds = [.. (d.AdditionalSkillIds ?? []), Driver, Cost, Gain] } : d);
        public void AddCard(ContentCardDefinition d) => b.AddCard(d);
        public void AddSkill(ContentSkillDefinition d) => b.AddSkill(d);
        public void AddDeck(ContentDeckRecipe d) => b.AddDeck(d);
        public void AddMode(ContentModeDefinition d) => b.AddMode(d);
    }
}
