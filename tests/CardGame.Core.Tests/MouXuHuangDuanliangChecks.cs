using System.Collections;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core.Campaigns;

namespace CardGame.Core.Tests;

internal static class MouXuHuangDuanliangChecks
{
    public static void SealedFourCellStrategyPrivacyAndExactPlayQuotaCold()
    {
        foreach (var defendSiege in new[] { false, true })
        {
            var privateViews = new List<string[]>();
            foreach (var attack in new[] { "siege", "advance" })
            {
                var s = MouXuHuangCheckScene.Start(new("sealed-" + defendSiege) { TargetHp = defendSiege ? 12 : 1, TargetArmor = defendSiege ? 10 : 0 });
                s.BeginMouyi(2); var root = s.DuanFrame(); var started = s.E<MouXuHuangMouyiStartedEvent>().Single();
                s.Need(started.TargetSeat == 2 && started.Source.OwnerSeat == 0 && started.Phase.Kind == ActualDiscardRecoveryPhaseKind.Play &&
                    root.MouXuHuangDuanliang is { Attack: null, Defense: null, Revealed: false } && s.E<MouXuHuangMouyiRevealedEvent>().Length == 0,
                    "The real zero-material accepted activation owns an unrevealed two-sided strategy and exact actual Play, even with no Hand cards.");
                s.Private(); s.RejectPrompt(); s.Cold(); s.Strategy(attack);
                s.Need(s.P is { PlayerSeat: 2, IsPrivate: true } && s.DuanFrame().MouXuHuangDuanliang is { Attack: not null, Defense: null, Revealed: false } &&
                    s.E<MouXuHuangStrategyCommittedEvent>() is [{ ParticipantSeat: 0, Attacker: true }] && s.E<MouXuHuangMouyiRevealedEvent>().Length == 0 &&
                    s.E<MouXuHuangNativeIssuedEvent>().Length == 0 && s.E<MouXuHuangObtainPaidEvent>().Length == 0,
                    "The attack bit stays only on its owning trusted frame. No public reveal, card use or material payment precedes the defender's independent real choice.");
                s.Private(); s.RejectPrompt(); s.Cold();
                privateViews.Add(Enumerable.Range(1, 3).Select(v => SnapshotJson.Serialize(s.G.CreateSnapshot(v))).ToArray());
                s.Accept(new AdvanceOneStepCommand(s.G.Revision));
                s.Reach(() => s.E<MouXuHuangMouyiRevealedEvent>().Length == 1);
                var reveal = s.E<MouXuHuangMouyiRevealedEvent>().Single();
                var defense = defendSiege ? MouXuHuangDefense.BlockSiege : MouXuHuangDefense.BlockAdvance;
                var success = attack == "siege" ? !defendSiege : defendSiege;
                s.Need(reveal.FrameId == root.Id && reveal.Attack == (attack == "siege" ? MouXuHuangAttack.Siege : MouXuHuangAttack.Advance) &&
                    reveal.Defense == defense && reveal.Success == success && s.E<MouXuHuangStrategyCommittedEvent>().Length == 2,
                    "All four independently committed cells use matching-defense failure and other-defense success; a public HP/Armor witness determines AI defense without reading the sealed attack bit.");
                s.FinishMouyi(root.Id); s.Need(s.E<MouXuHuangDuanliangCompletedEvent>().Single().Success == success,
                    "A defended strategy also completes once without inventing the other consequence.");
                s.PublicBudget(1); s.Cold(); s.PublicBudget(1); s.Need(!s.G.GetHumanLegalActions().Any(a => a.SkillId == MouXuHuangCheckScene.Duanliang),
                    "Accepted success and defended failure each consume the single actual-Play quota.");
                s.Reject(new UseProgramSkillCommand(0, MouXuHuangCheckScene.Duanliang, "private-mouyi", [], [2], s.G.Revision, s.P!.PromptId)); s.Cold();
            }
            if (!privateViews[0].SequenceEqual(privateViews[1])) throw new InvalidOperationException("The already committed attack bit changed a defender or peer player snapshot before simultaneous reveal.");
        }

        var regrant = MouXuHuangCheckScene.Start(new("real-source-regrant") { ConditionalSources = true, TargetHp = 12, TargetArmor = 10, InsertPlay = true });
        regrant.BeginMouyi(2); regrant.Strategy("siege"); regrant.FinishMouyi(regrant.DuanFrame().Id);
        regrant.Use("lose-self"); regrant.Play(); regrant.Need(!regrant.Has(MouXuHuangCheckScene.Duanliang), "A real HP-conditioned source is lost after its accepted strategy.");
        regrant.Use("recover-self"); regrant.Play(); regrant.Need(regrant.Has(MouXuHuangCheckScene.Duanliang) && !regrant.G.GetHumanLegalActions().Any(a => a.SkillId == MouXuHuangCheckScene.Duanliang),
            "A genuine same-phase source recovery/regrant cannot refresh the spent strategy allowance.");
        regrant.PublicBudget(1); regrant.Cold(); regrant.PublicBudget(1);
        var firstPhase = regrant.E<MouXuHuangMouyiStartedEvent>().Single().Phase; regrant.Cold(); regrant.End(); regrant.Play();
        regrant.Need(regrant.G.GetHumanLegalActions().Any(a => a.SkillId == MouXuHuangCheckScene.Duanliang), "The distinct normal Play after a real inserted Play has its own single allowance.");
        regrant.BeginMouyi(2); regrant.Strategy("siege"); regrant.FinishMouyi(regrant.DuanFrame().Id);
        regrant.Need(regrant.E<MouXuHuangMouyiStartedEvent>().Length == 2 && regrant.E<MouXuHuangMouyiStartedEvent>().Last().Phase != firstPhase,
            "The native actual phase identity resets the allowance without rewriting historical accepted strategy facts."); regrant.Cold();
    }

    public static void RealTopSupplyObtainAndNativeZeroDuelLegalityChildrenCold()
    {
        var far = MouXuHuangCheckScene.Start(new("real-far-top") { Hand = [MouXuHuangCheckScene.C(CardKind.SupplyShortage), MouXuHuangCheckScene.C(CardKind.Nullification)], TargetHp = 1 });
        far.Need(!far.G.GetHumanLegalActions().Any(a => a.Kind == LegalActionKind.SupplyShortage && a.TargetSeats.SequenceEqual([2])),
            "The ordinary real Hand SupplyShortage cannot reach the actual distance-two target before the skill.");
        var top = far.Top(); var beforeQueries = far.State(); for (var i = 0; i < 3; i++) _ = far.G.GetHumanLegalActions();
        far.Need(beforeQueries == far.State(), "Activation queries neither peek by mutation nor consume RNG, stock or phase quota.");
        far.BeginMouyi(2); far.Strategy("siege"); far.Reach(() => far.Hold("native-use")); var use = far.NativeUse(); var origin = use.MouXuHuangNative!;
        far.Need(origin.Kind == CardKind.SupplyShortage && origin.CardId == top.CardId && origin.PrintedKind == top.CardKind && origin.From == CardLocation.DrawPile &&
            origin.Suit == Suit.Spade && origin.Rank == 7 && origin.TargetSeat == 2 && use.Action is { ActorSeat: 0, ProviderSeat: 0, EffectiveKind: CardKind.SupplyShortage } acceptedTop &&
            acceptedTop.PhysicalCards is [{ From.Zone: CardZoneKind.DrawPile }] && acceptedTop.PhysicalCards[0].CardId == top.CardId,
            "Winning siege uses the exact genuine deck-top physical card, printed appearance and DrawPile provenance as native SupplyShortage, with only distance exempted.");
        far.Private(); far.RejectPrompt(); far.Cold(); far.Continue(); far.FinishMouyi(origin.ProgramFrameId);
        far.Need(far.V(2).Judgment.Any(c => c.Id == top.CardId && c.Kind == CardKind.SupplyShortage) &&
            far.G.CreateCardZoneDiagnostics().Single(c => c.CardId == top.CardId) is { CardKind: CardKind.Slash, Location.Zone: CardZoneKind.Judgment } &&
            far.E<MouXuHuangTopPaidEvent>().Count(e => e.CardId == top.CardId) == 1 && far.G.CardMovements.Count(m => m.CardId == top.CardId && m.From == CardLocation.DrawPile && m.To == CardLocation.Processing) == 1,
            "Native Judgment projection changes the effective delayed identity while retaining the original printed entity; top payment and placement occur once."); far.CheckNative(); far.Cold();
        far.End(); far.Reach(() => far.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification } && far.G.ResolutionStack.OfType<NullificationWindowFrame>().Any(w => w.EffectCardKind == CardKind.SupplyShortage));
        far.Private(); far.Cold(); far.Respond(far.V().Hand.Single(c => c.Kind == CardKind.Nullification).Id);
        far.Reach(() => far.G.CardMovements.Any(m => m.CardId == top.CardId && m.From == CardLocation.Judgment(2) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.DelayedCardFinish));
        far.Need(far.E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == origin.UseId && e.CardId == top.CardId) == 1 && far.E<NullificationRespondedEvent>().Length == 1,
            "The genuinely held converted delayed card later enters the native Judgment counterspell window, retires its original material and never declares a second card use."); far.Cold();

        foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment })
        {
            var obtain = MouXuHuangCheckScene.Start(new("duplicate-obtain-" + zone) { SetupRange = true, Hand = [MouXuHuangCheckScene.C(CardKind.SupplyShortage)],
                TargetHp = 1, TargetEquipment = zone == CardZoneKind.Equipment ? CardKind.SilverLion : null });
            obtain.Native(CardKind.SupplyShortage, [2]); obtain.Play(); var untouched = obtain.Top().CardId;
            obtain.BeginMouyi(2); obtain.Strategy("siege"); obtain.Reach(() => obtain.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("source-zone") == zone.ToString()) == true);
            var root = obtain.DuanFrame().Id; obtain.Private(); obtain.RejectPrompt(); obtain.Cold();
            var choices = obtain.P!.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == zone.ToString()).ToArray();
            if (zone == CardZoneKind.Hand) obtain.Need(choices.Length > 0 && choices.All(c => c.Cards.Count == 0 && !c.Parameters.ContainsKey("card-id")), "Foreign Hand obtain choices are opaque slots, never leaked entity faces.");
            obtain.Answer(c => c.Parameters.GetValueOrDefault("source-zone") == zone.ToString()); obtain.FinishMouyi(root);
            var paid = obtain.E<MouXuHuangObtainPaidEvent>().Single();
            obtain.Need(paid.FrameId == root && paid.TargetSeat == 2 && paid.From == new CardLocation(zone, 2) && paid.To == CardLocation.Hand(0) &&
                obtain.V().Hand.Any(c => c.Id == paid.CardId) && obtain.Top().CardId == untouched && obtain.E<MouXuHuangNativeIssuedEvent>().Length == 0 &&
                obtain.G.CardMovements.Count(m => m.CardId == paid.CardId && m.From == paid.From && m.To == paid.To) == 1,
                "An actual existing effective SupplyShortage replaces top use with one exact genuine HEJ obtain and leaves the stock untouched.");
            if (zone == CardZoneKind.Equipment) obtain.Need(obtain.E<RecoveryAppliedEvent>().Count(e => e.TargetSeat == 2 && e.Amount == 1) == 1,
                "The genuinely equipped wounded SilverLion performs its real native departure recovery before obtain completion."); obtain.Cold();
        }

        foreach (var blocker in new[] { "weimu", "ban", "self-only" })
        {
            var illegal = MouXuHuangCheckScene.Start(new("illegal-top-" + blocker) { TargetHp = 1, WeimuTarget = blocker == "weimu" });
            if (blocker != "weimu") { illegal.Use(blocker); illegal.Play(); }
            var id = illegal.Top().CardId; illegal.BeginMouyi(2); var root = illegal.DuanFrame().Id; illegal.Strategy("siege"); illegal.FinishMouyi(root);
            illegal.Need(illegal.E<MouXuHuangMouyiRevealedEvent>().Single().Success && illegal.Top().CardId == id &&
                illegal.E<MouXuHuangNativeIssuedEvent>().Length == 0 && illegal.E<MouXuHuangTopPaidEvent>().Length == 0 &&
                !illegal.G.CardMovements.Any(m => m.CardId == id) && !illegal.G.GetHumanLegalActions().Any(a => a.SkillId == MouXuHuangCheckScene.Duanliang),
                "A genuinely forbidden winning consequence leaves its real top unconsumed before declaration/payment, consumes the accepted strategy quota and never silently substitutes Duel."); illegal.Cold();
        }
        foreach (var reshuffle in new[] { false, true })
        {
            var empty = MouXuHuangCheckScene.Start(new(reshuffle ? "real-reshuffled-top" : "genuine-empty-stock") { DeckCount = 4, TargetHp = 1,
                Hand = reshuffle ? [MouXuHuangCheckScene.C(CardKind.Dodge)] : [] });
            int? discarded = reshuffle ? empty.V().Hand.Single().Id : null;
            empty.Use("draw-stock"); empty.Play();
            if (discarded is { } discardedId) { empty.Use("discard", cards: [discardedId]); empty.Play(); }
            empty.Need(!empty.G.CreateCardZoneDiagnostics().Any(c => c.Location == CardLocation.DrawPile), "Real native draw exhausts the small stock without injecting pile state.");
            empty.BeginMouyi(2); var root = empty.DuanFrame().Id; empty.Strategy("siege"); empty.FinishMouyi(root);
            if (reshuffle) empty.Need(empty.V(2).Judgment.Any(c => c.Id == discarded) && empty.G.CardMovements.Count(m => m.CardId == discarded && m.Reason == CardMoveReasons.Reshuffle) == 1 &&
                empty.E<MouXuHuangTopPaidEvent>().Single().CardId == discarded, "A real reshuffle supplies the original discarded entity once as the forthcoming native top use.");
            else empty.Need(empty.E<MouXuHuangNativeIssuedEvent>().Length == 0 && empty.E<MouXuHuangTopPaidEvent>().Length == 0 && empty.E<MouXuHuangDuanliangCompletedEvent>().Single() is { Success: true, UsedCard: false },
                "Winning with genuinely empty draw/discard stock completes without invented material, free draw or alternate Duel."); empty.CheckNative(); empty.Cold();
        }
        foreach (var counterspell in new[] { false, true })
        {
            var duel = MouXuHuangCheckScene.Start(new("native-duel-" + counterspell) { TargetHp = 12, TargetArmor = 10,
                Hand = counterspell ? [MouXuHuangCheckScene.C(CardKind.Nullification)] : [] });
            duel.BeginMouyi(2); var root = duel.DuanFrame().Id; duel.Strategy("advance"); duel.Reach(() => duel.Hold("native-use"));
            var native = duel.NativeUse(); var o = native.MouXuHuangNative!;
            duel.Need(o.Kind == CardKind.Duel && o.CardId == 0 && native.PhysicalCardIds.Count == 0 && native.Action is { Type: CardActionType.Use, ActorSeat: 0, ProviderSeat: 0, EffectiveSuit: Suit.None, EffectiveRank: 0, EffectiveIsRed: null } acceptedDuel && acceptedDuel.PhysicalCards.Count == 0,
                "Advance issues a genuine canonical zero-material native Duel, never bare virtual damage or a synthetic physical entity.");
            duel.Private(); duel.Cold(); duel.Continue();
            if (counterspell) { duel.Reach(() => duel.P is { PlayerSeat: 0, Kind: DecisionKind.Nullification }); duel.Cold(); duel.Respond(duel.V().Hand.Single().Id); }
            duel.FinishMouyi(root); duel.Need(duel.E<CardUseFinishedEvent>().Count(e => e.ResolutionId == o.UseId && e.CardId == 0 && e.CardKind == CardKind.Duel) == 1 &&
                (counterspell ? duel.E<NullificationRespondedEvent>().Length == 1 && duel.E<DamageAppliedEvent>().Length == 0 : duel.E<DamageRequestedEvent>().Any(e => e.SourceCard == CardKind.Duel) && duel.E<DamageAppliedEvent>().Any(e => e.SourceSeat == 0 && e.TargetSeat == 2)),
                "The real counterspell or Slash-response/damage subtree returns through one complete native Duel and one original strategy parent."); duel.CheckNative(); duel.Cold();
        }
        var multi = MouXuHuangCheckScene.Start(new("native-duel-late-added-target") { TargetHp = 12, TargetArmor = 10, AddDuelTarget = true });
        multi.BeginMouyi(2); var multiRoot = multi.DuanFrame().Id; multi.Strategy("advance");
        multi.Reach(() => multi.P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "select-target") == true);
        multi.Private(); multi.RejectPrompt(); multi.Cold(); multi.Answer(c => c.Targets.SequenceEqual([3])); multi.Reach(() => multi.Hold("native-use"));
        var multiple = multi.NativeUse(); multi.Need(multiple.TargetSeats.SequenceEqual([2, 3]) && multi.E<MouXuHuangNativeReturnedEvent>().Length == 0,
            "A genuine later native target addition retains the original strategy producer and cannot return after only its initial target.");
        multi.Cold(); multi.Continue(); multi.FinishMouyi(multiRoot);
        multi.Need(multi.E<MouXuHuangNativeIssuedEvent>().Length == 1 && multi.E<MouXuHuangNativeReturnedEvent>().Length == 1 &&
            multi.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 2) && multi.E<DamageAppliedEvent>().Any(e => e.TargetSeat == 3) && multi.V(3).Hp == 11,
            "The new producer waits for the entire genuine multi-target Duel, both damage children and one whole-use native Finish before its original frame returns."); multi.CheckNative(); multi.Cold();
    }
}

internal sealed record MouXuHuangCheckSpec(string Name)
{
    public bool Shipo { get; init; }
    public int SourceSeat { get; init; }
    public int OwnerHp { get; init; } = 4;
    public int TargetHp { get; init; } = 12;
    public int TargetArmor { get; init; }
    public bool ConditionalSources { get; init; }
    public bool InsertPlay { get; init; }
    public bool SetupRange { get; init; }
    public bool WeimuTarget { get; init; }
    public bool PreventTarget { get; init; }
    public bool AddDuelTarget { get; init; }
    public CardKind? TargetEquipment { get; init; }
    public int DeckCount { get; init; } = 48;
    public CardKind DeckKind { get; init; } = CardKind.Slash;
    public ShanheStartingCard[] Hand { get; init; } = [];
    public ShanheStartingCard[][] Peers { get; init; } = [[MouXuHuangCheckScene.C(CardKind.Dodge)], [MouXuHuangCheckScene.C(CardKind.Dodge)], []];
    public int[] PeerHp { get; init; } = [4, 12, 12];
    public string GiftChild { get; init; } = "none";
}

internal sealed class MouXuHuangCheckScene(MouXuHuangCheckSpec spec, GameEngine game, ContentRegistry registry)
{
    public const string Duanliang = "mou-mobile:duanliang", Shipo = "mou-mobile:shipo", Driver = "fixture:mxh-driver", Watch = "fixture:mxh-watch",
        Quiet = "fixture:mxh-quiet", Conditional = "fixture:mxh-conditional", Prevent = "fixture:mxh-prevent", Gear = "fixture:mxh-gear",
        Owner = "fixture:mxh-human", Ally = "fixture:mxh-ally", EnemyA = "fixture:mxh-enemy-a", EnemyB = "fixture:mxh-enemy-b", Deck = "fixture:mxh-deck",
        GiftReason = "skill-program.mou-mobile:shipo.give", TransferReason = "skill-program.mou-mobile:shipo.redistribute";
    public static ShanheStartingCard C(CardKind kind) => new(kind, Suit.Spade, 7);
    public GameEngine G { get; private set; } = game;
    public PendingDecision? P => Enumerable.Range(0, 4).Select(s => G.CreateSnapshot(s).PendingDecision).FirstOrDefault(p => p is not null);
    public PlayerSnapshot V(int seat = 0) => G.CreateSnapshot(seat).Players[seat];
    public T[] E<T>() => G.Events.Select(e => e.Payload).OfType<T>().ToArray();
    public bool Has(string skill, int seat = 0) => V(seat).Skills?.Any(s => s.ContentId == skill) == true;
    public bool Hold(string id) => P?.SkillPrompt?.SkillId == Watch && P.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == id);
    public bool ShipoBranch(string branch) => P?.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "mou-xu-huang-shipo" && c.Parameters.GetValueOrDefault("branch") == branch) == true;
    public ProgramSkillFrame DuanFrame() => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouXuHuangDuanliang is not null);
    public ProgramSkillFrame ShipoFrame() => G.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.MouXuHuangShipo is not null);
    public CardUseFrame NativeUse() => G.ResolutionStack.OfType<CardUseFrame>().Last(f => f.MouXuHuangNative is not null);
    public CardZoneDiagnostic Top() => G.CreateCardZoneDiagnostics().Where(c => c.Location == CardLocation.DrawPile).OrderByDescending(c => c.ZoneIndex).First();
    public void PublicBudget(int used)
    {
        foreach (var viewer in Enumerable.Range(0, 4))
        {
            var states = G.CreateSnapshot(viewer).Players[0].SkillRuntimeStates?.Single(x => x.SkillId == Duanliang).PublicRuleStates ?? [];
            Need(states.Count(x => x.StateId == "private-mouyi" && x.Kind == ProgramPublicRuleStateKind.ActivationLimit && x.Scope == SkillUsageScope.Phase && x.Value == 1 && x.Used == used) == 1,
                "Every viewer sees the same exact accepted-actual-Play allowance one and Used " + used + ", including source regrant and cold restore."); Frozen(states);
        }
    }
    public void BeginMouyi(int target) { PublicBudget(0); Accept(new UseProgramSkillCommand(0, Duanliang, "private-mouyi", [], [target], G.Revision, P!.PromptId)); PublicBudget(1); }
    public void Strategy(string option) => Answer(c => c.Parameters.GetValueOrDefault("program-action") == "mou-xu-huang-mouyi" && c.Parameters.GetValueOrDefault("option-id") == option);
    public void FinishMouyi(long frame) { Reach(() => E<MouXuHuangDuanliangCompletedEvent>().Any(e => e.FrameId == frame)); Play(); }
    public void Use(string id, int[]? targets = null, int[]? cards = null) => Accept(new UseProgramSkillCommand(0, Driver, id, cards ?? [], targets ?? [], G.Revision, P!.PromptId));
    public void Native(CardKind kind, int[] targets)
    {
        var a = G.GetHumanLegalActions().FirstOrDefault(a => a.CardId is { } id && V().Hand.Any(c => c.Id == id && c.Kind == kind) && a.ConversionSource is null && a.Kind != LegalActionKind.Recast && a.TargetSeats.SequenceEqual(targets));
        Need(a is not null, "A genuine native " + kind + " plan with exact ordered targets is required.");
        Accept(new PlayCardCommand(0, a!.CardId!.Value, a.TargetSeats, G.Revision, P!.PromptId, a.PlayedCardKind, a.TargetCardId) { ConversionSource = a.ConversionSource, AdditionalConversionSources = a.AdditionalConversionSources });
    }
    public void Respond(int id) => Answer(c => c.Cards.Contains(id) && c.Parameters.GetValueOrDefault("response") is not ("pass" or "take-damage" or "let-die"));
    public void Continue() => Answer(c => c.Parameters.GetValueOrDefault("option-id") is "native-use" or "gift-child" or "damage-child");
    public void Play() => Reach(() => P is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    public void End() => Accept(new EndPlayPhaseCommand(0, G.Revision, P!.PromptId));
    public void ActivateShipo()
    {
        End(); Reach(() => P is { PlayerSeat: 0 } && P.SkillPrompt?.SkillId == Shipo && P.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
        Private(); Cold(); Answer(c => c.Parameters.GetValueOrDefault("program-action") == "activate"); Reach(() => ShipoBranch("mode-single") || ShipoBranch("mode-all"));
    }
    public void Answer(Func<PromptChoice, bool> choose)
    {
        var p = P!; var c = p.Choices.FirstOrDefault(choose); Need(p.PlayerSeat == 0 && c is not null, "Only an exact currently published human choice can be submitted.");
        Accept(new AnswerPromptCommand(0, p.PromptId, c!.Id, G.Revision));
    }
    public void Reach(Func<bool> done, [CallerLineNumber] int line = 0)
    {
        for (var n = 0; n < 280; n++)
        {
            if (done()) return; Need(G.State.Winner == Winner.None, "Native terminal cutoff before boundary " + line);
            var p = P;
            if (p is null || p.PlayerSeat != 0) Accept(new AdvanceOneStepCommand(G.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End();
            else if (p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.ContainsKey("option-id"))) Continue();
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification or DecisionKind.RescueDying)
                Answer(c => c.Parameters.GetValueOrDefault("response") is "pass" or "take-damage" or "let-die");
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else if (ShipoBranch("decline-transfer")) Answer(c => c.Parameters.GetValueOrDefault("branch") == "decline-transfer");
            else if (ShipoBranch("damage")) Answer(c => c.Parameters.GetValueOrDefault("branch") == "damage");
            else throw new InvalidOperationException("Unexpected actual boundary " + line + " " + Diagnostic());
        }
        throw new InvalidOperationException("Missing bounded actual boundary " + line + " " + Diagnostic());
    }
    public void CheckNative()
    {
        foreach (var issued in E<MouXuHuangNativeIssuedEvent>())
        {
            var o = issued.Origin;
            Need(E<CardUseDeclaredEvent>().Count(e => e.ResolutionId == o.UseId && e.CardId == o.CardId && e.CardKind == o.Kind) == 1 &&
                E<CardUseFinishedEvent>().Count(e => e.ResolutionId == o.UseId && e.CardId == o.CardId && e.CardKind == o.Kind) == 1 &&
                E<MouXuHuangNativeReturnedEvent>().Count(e => e.FrameId == o.ProgramFrameId && e.UseId == o.UseId && e.ActionId == o.ActionId) == 1,
                "Every genuine native child declares, finishes and returns to its exact original owning frame once.");
        }
        Need(G.CardMovements.All(m => m.CardId > 0), "No zero-material representation becomes a physical card-zone entity.");
    }
    public void Private()
    {
        var p = P!; Need(p.IsPrivate && Enumerable.Range(0, 4).Where(v => v != p.PlayerSeat).All(v => G.CreateSnapshot(v).PendingDecision is null) &&
            Enumerable.Range(0, 4).All(v => G.CreateSnapshot(v).Players.Where(x => x.Seat != v).All(x => x.Hand.Count == 0)), "Only the actual chooser sees private choices; all other Hands remain opaque.");
        Frozen(p.Choices); foreach (var c in p.Choices) { Frozen(c.Cards); Frozen(c.Targets); }
    }
    public void Frozen<T>(IReadOnlyList<T> items)
    {
        if (items is IList list && list.Count > 0) { var before = JsonSerializer.Serialize(items); var rejected = false; try { list[0] = list[0]; } catch (NotSupportedException) { rejected = true; } Need(rejected && before == JsonSerializer.Serialize(items), "Exposed nested collections reject observer mutation."); }
    }
    public void RejectPrompt()
    {
        var p = P!; Reject(new AnswerPromptCommand(p.PlayerSeat == 0 ? 1 : 0, p.PromptId, p.Choices[0].Id, G.Revision));
        Reject(new AnswerPromptCommand(0, p.PromptId, new("forged-xu-huang-choice"), G.Revision));
        Reject(new AnswerPromptCommand(0, p.PromptId, p.Choices[0].Id, G.Revision - 1));
    }
    public string State() => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(v => SnapshotJson.Serialize(G.CreateSnapshot(v))), Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), G.CardMovements,
        Zones = G.CreateCardZoneDiagnostics(), Events = G.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(G.AcceptedCommands) });
    public void Cold() { var state = State(); G = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(G.CreateCheckpoint())), registry); Need(state == State(), "Cold replay retains all player views, sealed strategies, accepted roster/subset, paid native child cursors and original parent returns."); }
    public void Reject(GameCommand c) { var before = State(); Need(!G.Submit(c).Accepted && before == State(), "Rejected foreign, forged or stale input cannot change private commitments, quota, movement, RNG or command history."); }
    public void Accept(GameCommand c) { try { var result = G.Submit(CommandJson.Deserialize(CommandJson.Serialize([c])).Single()); Need(result.Accepted, "Rejected " + c.GetType().Name + ": " + result.Error?.Code + "/" + result.Error?.Message); } catch (Exception e) { throw new InvalidOperationException(spec.Name + ": " + e.Message + " " + Diagnostic(), e); } }
    public void Need(bool yes, string message) { if (!yes) throw new InvalidOperationException(message + " " + Diagnostic()); }
    private string Diagnostic() => JsonSerializer.Serialize(new { spec.Name, G.State.TurnNumber, G.State.CurrentSeat, G.State.Phase, G.State.Winner, Prompt = P,
        Players = Enumerable.Range(0, 4).Select(i => new { i, V(i).Hp, V(i).IsAlive, V(i).Armor, V(i).HandCount, Judgment = V(i).Judgment, Equipment = V(i).Equipment }),
        Frames = G.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), Facts = G.Events.Where(e => e.Payload.GetType().Name.StartsWith("MouXuHuang", StringComparison.Ordinal)).TakeLast(15).Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Moves = G.CardMovements.TakeLast(12) });

    public static MouXuHuangCheckScene Start(MouXuHuangCheckSpec spec)
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(spec));
        var setup = new ShanheBattleSetup(84, 1, "mxh-" + spec.Name, Owner, spec.OwnerHp, 0, 0, 0, 0, [], spec.Hand, [],
            [new(EnemyA, Hp: spec.Shipo ? spec.PeerHp[1] : spec.TargetHp) { StartingHand = spec.Peers[1] }, new(EnemyB, Hp: spec.PeerHp[2]) { StartingHand = spec.Peers[2] }],
            [new(Ally, Hp: spec.PeerHp[0]) { StartingHand = spec.Peers[0] }], new string('d', 64));
        var g = GameEngine.CreateStandard(new GameOptions { Seed = setup.BattleSeed, PlayerCount = 4, HumanSeat = 0, HumanRole = null, HumanTeamId = ShanheBattleSetup.PlayerTeamId,
            ModeId = ShanheBattleSetup.BattleModeId, ShanheBattle = setup, UseInteractiveSetup = false, UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 12 }, registry);
        var scene = new MouXuHuangCheckScene(spec, g, registry); scene.Accept(new StartGameCommand()); scene.Play();
        if (spec.TargetArmor > 0) { scene.Use("armor", [2]); scene.Play(); }
        return scene;
    }

    private sealed class Fixture(MouXuHuangCheckSpec spec) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-mou-xu-huang-native", new(1, 0, 0));
        public void Register(IContentRegistryBuilder b)
        {
            void Bundle(string name, params string[] ids)
            {
                string Read(string suffix) { using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms." + name + "." + suffix + ".json") ?? throw new InvalidOperationException("Missing real bundle " + name); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
                var real = SkillProgramCatalog.Load(Read("rules"), Read("presentation")); foreach (var id in ids) b.AddSkill(new(id, real.Presentations[id].Name, real.Presentations[id].Description) { Program = real.Programs[id], ProgramPresentation = real.Presentations[id] });
            }
            Bundle("mou-xu-huang-mobile", Duanliang, Shipo); if (spec.WeimuTarget) Bundle("classic-jia-xu", "classic:weimu");
            var kinds = spec.Hand.Concat(spec.Peers.SelectMany(x => x)).Select(c => c.Kind).Concat(new[] { spec.DeckKind, CardKind.Slash, CardKind.Dodge, CardKind.SupplyShortage, CardKind.Nullification, CardKind.SilverLion, CardKind.Peach }).Distinct();
            foreach (var kind in kinds) { var card = CardCatalog.Get(kind); b.AddCard(new("fixture:mxh-card-" + kind, card.DisplayName, card.CategoryName, card.Description, kind)); }
            static JsonNode N(string text) => JsonNode.Parse(text)!;
            static string Pause(string option) => "{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"held\",\"options\":[{\"id\":\"" + option + "\"}]}";
            static JsonNode A(string id, string effects, int targets = 0, int cards = 0) => N("{\"id\":\"" + id + "\",\"minCards\":" + cards + ",\"maxCards\":" + cards + ",\"sourceZones\":[\"hand\"],\"minTargets\":" + targets + ",\"maxTargets\":" + targets + ",\"targetKind\":\"anyLiving\",\"usesPerTurn\":null,\"effects\":[" + effects + "]}");
            var acts = new JsonArray(A("lose-self", "{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}"), A("recover-self", "{\"op\":\"recover\",\"target\":\"owner\",\"amount\":1}"),
                A("draw-stock", "{\"op\":\"draw\",\"target\":\"owner\",\"amount\":20}"), A("armor", "{\"op\":\"grantArmor\",\"target\":\"selectedTarget\",\"amount\":" + Math.Max(1, spec.TargetArmor) + "}", 1),
                A("discard", "{\"op\":\"discardSelected\",\"target\":\"owner\",\"amount\":1}", cards: 1),
                A("ban", "{\"op\":\"grantTurnCardActionProhibition\",\"target\":\"owner\",\"cardKinds\":[\"supplyShortage\"],\"actionTypes\":[\"use\"]}"),
                A("self-only", "{\"op\":\"grantTurnCardTargetRestriction\",\"target\":\"owner\",\"targetRestriction\":\"selfOnly\"}"));
            var mods = new JsonArray(N("{\"id\":\"keep\",\"query\":\"handLimit\",\"operation\":\"add\",\"value\":20,\"priority\":0}"));
            if (spec.SetupRange) mods.Add(N("{\"id\":\"setup-distance\",\"query\":\"outgoingDistance\",\"operation\":\"add\",\"value\":-4,\"priority\":0}"));
            var defs = new JsonArray(new JsonObject { ["id"] = Driver, ["revision"] = 1, ["activations"] = acts, ["modifiers"] = mods },
                N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            var watches = new JsonArray(N("{\"id\":\"real-native-use\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"supplyShortage\",\"duel\"],\"priority\":999,\"optional\":false,\"effects\":[" + Pause("native-use") + "]}"));
            if (spec.AddDuelTarget) watches.Add(N("{\"id\":\"add-real-duel-target\",\"window\":\"cardUseTargetsFinalized\",\"ownerRelation\":\"actor\",\"cardKinds\":[\"duel\"],\"priority\":1000,\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLegalCurrentCardTarget\"},{\"op\":\"addCurrentCardUseTarget\",\"target\":\"selectedTarget\"}]}"));
            if (spec.GiftChild != "none")
            {
                var effects = Pause("gift-child");
                if (spec.GiftChild == "source-loss") effects += ",{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":1}";
                else if (spec.GiftChild == "source-death") effects += ",{\"op\":\"selectTargets\",\"target\":\"owner\",\"targetKind\":\"anyLiving\",\"minimumTargets\":2,\"maximumTargets\":2,\"targetAiOrder\":\"stable\"},{\"op\":\"recover\",\"target\":\"selectedTargets\",\"amount\":20},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":20}";
                else if (spec.GiftChild == "remove-later") effects += ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"selectedTarget\"},\"zones\":[\"judgment\"],\"count\":1,\"destination\":\"discardPile\",\"awaitMovementTriggers\":true},{\"op\":\"recover\",\"target\":\"selectedTarget\",\"amount\":20}";
                else if (spec.GiftChild == "kill-later") effects += ",{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"loseHp\",\"target\":\"selectedTarget\",\"amount\":20}";
                else if (spec.GiftChild == "filter-obtained") effects += ",{\"op\":\"draw\",\"target\":\"owner\",\"amount\":1},{\"op\":\"selectTarget\",\"target\":\"owner\",\"targetKind\":\"otherLiving\"},{\"op\":\"selectAndMoveOwnedCard\",\"target\":\"owner\",\"chooserRef\":{\"kind\":\"owner\"},\"cardOwnerRef\":{\"kind\":\"owner\"},\"zones\":[\"hand\"],\"count\":1,\"destination\":\"selectedTargetHand\",\"targetRef\":{\"kind\":\"selectedTarget\"},\"awaitMovementTriggers\":true}";
                watches.Add(N("{\"id\":\"actual-gift-child\",\"window\":\"cardsGained\",\"subject\":\"owner\",\"destinationZones\":[\"hand\"],\"movementReasons\":[\"" + GiftReason + "\"],\"movementOccurrence\":\"perBatch\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[" + effects + "]}"));
            }
            defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = watches });
            if (spec.ConditionalSources) defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-dl\",\"skillId\":\"" + Duanliang + "\",\"minimumHp\":4},{\"id\":\"healthy-sp\",\"skillId\":\"" + Shipo + "\",\"minimumHp\":4}]}"));
            if (spec.PreventTarget) defs.Add(N("{\"id\":\"" + Prevent + "\",\"revision\":1,\"triggers\":[{\"id\":\"real-prevention\",\"window\":\"beforeDamageApplied\",\"subject\":\"damageTarget\",\"optional\":false,\"effects\":[{\"op\":\"preventCurrentDamage\",\"target\":\"owner\"}]}]}"));
            if (spec.TargetEquipment is not null) defs.Add(N("{\"id\":\"" + Gear + "\",\"revision\":1,\"triggers\":[{\"id\":\"actual-opening-equipment\",\"window\":\"gameStarting\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"useRandomDeckEquipment\",\"target\":\"owner\",\"targetRef\":{\"kind\":\"owner\"},\"resultBind\":\"real-equipment\"}]}]}"));
            if (spec.InsertPlay) defs.Add(N("{\"id\":\"fixture:mxh-insert\",\"revision\":1,\"triggers\":[{\"id\":\"insert\",\"window\":\"turnStartBeforeNormalFlow\",\"subject\":\"owner\",\"usageScope\":\"game\",\"usageLimit\":1,\"optional\":false,\"effects\":[{\"op\":\"insertPhase\",\"target\":\"owner\",\"phase\":\"play\",\"phaseContinuation\":\"beforeNormalPreparation\"}]}]}"));
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new
            {
                name = n!["id"]!.GetValue<string>(), description = "谋徐晃完整段落原生小夹具",
                optionLabels = new[] { "activations", "triggers" }.SelectMany(k => n[k]?.AsArray() ?? new JsonArray()).SelectMany(binding => binding!["effects"]!.AsArray())
                    .SelectMany(effect => effect!["options"]?.AsArray() ?? new JsonArray()).Select(option => option!["id"]!.GetValue<string>()).Distinct(StringComparer.Ordinal).ToDictionary(id => id, id => id)
            });
            var local = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }), JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, p) in local.Programs) b.AddSkill(new(id, labels[id].name, labels[id].description) { Program = p, ProgramPresentation = local.Presentations[id] });
            var own = new List<string> { Driver, Watch }; if (spec.SourceSeat == 0) { if (spec.ConditionalSources) own.Add(Conditional); else own.AddRange([Duanliang, Shipo]); } if (spec.InsertPlay) own.Add("fixture:mxh-insert");
            b.AddGeneral(new(Owner, "真实徐晃检查人", "", "standard:none", "wei", 4, own));
            b.AddGeneral(new(Ally, "真实同行", "", "standard:none", "wei", 12, spec.SourceSeat == 1 ? [Quiet, Duanliang, Shipo] : [Quiet]));
            var target = new List<string> { Quiet }; if (spec.WeimuTarget) target.Add("classic:weimu"); if (spec.TargetEquipment is not null) target.Add(Gear); if (spec.PreventTarget) target.Add(Prevent);
            b.AddGeneral(new(EnemyA, "真实对手甲", "", "standard:none", "qun", 12, target)); b.AddGeneral(new(EnemyB, "真实对手乙", "", "standard:none", "qun", 12, [Quiet]));
            b.AddDeck(new(Deck, "固定真实实体", 0, 0, []) { PhysicalCards = Enumerable.Range(0, spec.DeckCount).Select(i => new ContentDeckPhysicalCard("fixture:mxh-card-" + (i == 0 && spec.TargetEquipment is { } gear ? gear : spec.DeckKind), Suit.Spade, 7)).ToArray() });
            b.AddMode(new(ShanheBattleSetup.BattleModeId, "谋徐晃真实原生小局", 4, 4, new Dictionary<string, int>(), Deck, GeneralPoolIds: [Owner, Ally, EnemyA, EnemyB], ModeKind: ContentModeKind.Team,
                TeamCounts: new Dictionary<string, int> { [ShanheBattleSetup.PlayerTeamId] = 2, [ShanheBattleSetup.EnemyTeamId] = 2 }));
        }
    }
}
