using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;

internal static class SunHongChecks
{
    private const string Owner = "fixture:sun-hong-owner", Driver = "fixture:sh-driver", Quiet = "fixture:sh-quiet";
    private const string Conditional = "fixture:sh-conditional", Watch = "fixture:sh-watch", Mode = "identity:classic-sun-hong";
    private const string Skill = "ol:xianbi", DiscardReason = "skill-program.sun-hong.xianbi.discard";
    private const string Zenrun = "ol:zenrun", TakeReason = "skill-program.sun-hong.zenrun.obtain", CompensationReason = "skill-program.sun-hong.zenrun.compensation";
    private static string Scene = "";

    public static void XianbiFrozenAdjustmentAndRandomOriginalReturnsCold()
    {
        foreach (var scene in new[] { "discard", "self", "draw", "equal", "empty" })
        {
            var (g, registry) = Start(scene);
            if (scene is "self" or "draw" or "equal")
            { Use(g, "place", [V(g, 0).Hand.First().Id], [scene == "self" ? 0 : 1]); Play(g); }
            if (scene is "discard" or "self") { Use(g, "seed", V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray()); Play(g); }
            if (scene is "draw" or "empty" or "equal")
            {
                var cards = V(g, 0).Hand.Take(V(g, 0).HandCount - (scene == "equal" ? 1 : 0)).Select(c => c.Id).ToArray();
                Use(g, "give", cards, [2]); Play(g);
            }
            if (scene == "empty")
            {
                Need(V(g, 0).HandCount == 0 && Enumerable.Range(0, 4).All(s => V(g, s).Equipment.Count == 0) &&
                    !g.GetHumanLegalActions().Any(a => a.SkillId == Skill), "No unequal equipment count means no fake adjustment offer.");
                _ = Cold(g, registry); continue;
            }
            Begin(g);
            if (scene == "equal") Need(!P(g)!.Choices.Any(c => c.Targets.Contains(1)), "An equal-count role is not a legal Xianbi target.");
            var target = scene == "self" ? 0 : scene == "draw" ? 1 : 3;
            var handBefore = V(g, 0).HandCount; var equipment = V(g, target).Equipment.Count;
            Pick(g, "target", seat: target);
            if (scene != "draw")
            {
                Reach(g, () => Current(g)?.Stage == XianbiStage.DiscardChoice);
                var frozen = Current(g)!;
                Need(frozen.HandBefore == handBefore && frozen.EquipmentCount == equipment && frozen.TargetSeat == target &&
                    frozen.Required == handBefore - equipment, "The real target count and original Hand difference freeze before physical payment.");
                g = Cold(g, registry); SelectWhole(g); Pick(g, "pay");
            }
            Play(g);
            var started = E<XianbiStartedEvent>(g).Single(); var completed = E<XianbiCompletedEvent>(g).Single();
            Need(completed.FrameId == started.FrameId && completed.Issued && !g.GetHumanLegalActions().Any(a => a.SkillId == Skill),
                "One actual completed Play invocation consumes exactly its native phase allowance.");
            if (scene == "draw")
            {
                var drawn = E<XianbiDrawnEntityEvent>(g);
                Need(drawn.Length == equipment - handBefore && V(g, 0).HandCount == equipment &&
                    E<XianbiRandomIssuedEvent>(g).Length == 0 && E<XianbiPrimaryCommittedEvent>(g).Length == 0 &&
                    drawn.All(e => g.CardMovements.Count(m => m.CardId == e.CardId && m.From == CardLocation.DrawPile &&
                        m.To == CardLocation.Hand(0) && m.Reason.Value == "skill-program.sun-hong.xianbi.draw") == 1),
                    "Top-up is one genuine difference draw, with no fake discard or random return.");
            }
            else
            {
                var primary = E<XianbiPrimaryCommittedEvent>(g); var moves = E<XianbiEntityMovedEvent>(g).Where(e => e.Reason.Value == DiscardReason).ToArray();
                Need(primary.Length == handBefore - equipment && moves.Length == primary.Length && moves.Select(e => e.BatchId).Distinct().Count() == 1 &&
                    moves.All(e => e.From == CardLocation.Hand(0) && e.To == CardLocation.DiscardPile) &&
                    E<NativeLogicalDiscardInvocationRecordedEvent>(g).Count(e => e.ParentFrameId == started.FrameId && e.InitiatorSeat == 0 &&
                        e.ExecutorSeat == 0 && e.HandCount == primary.Length && e.ActualCount == primary.Length && e.Reason.Value == DiscardReason) == 1,
                    "The complete adjustment pays one real native logical Hand discard, not independent one-card instructions.");
                var returns = E<XianbiRandomIssuedEvent>(g); var skipped = E<XianbiReturnSkippedEvent>(g);
                Need(returns.Length + skipped.Length == primary.Length && returns.All(e => e.CardId != e.OriginalCardId &&
                    CardCatalog.Get(Kind(g, e.CardId)).CategoryName == CardCatalog.Get(primary.Single(p => p.Index == e.Cursor).EffectiveKind).CategoryName),
                    "Each actual primary receives its own same-category other original entity or an explicit empty-stock result.");
                if (scene is "discard" or "self") Need(returns.Length == primary.Length && skipped.Length == 0,
                    "The two real pre-existing stock entities prevent the last-primary self-only shortage in this fixture.");
                if (scene == "equal") Need(returns.Length == 0 && skipped.Length == 1, "A lone discarded original cannot be obtained as its own other card.");
            }
            _ = Cold(g, registry);
        }
    }

    public static void XianbiPrivateWholeDiscardChildrenLossDeathCold()
    {
        foreach (var scene in new[] { "mixed", "loss", "death" })
        {
            var (g, registry) = Start(scene);
            if (scene == "mixed") { Use(g, "stock"); Play(g); }
            Use(g, "seed", V(g, 0).Hand.Take(2).Select(c => c.Id).ToArray()); Play(g); Begin(g); Pick(g, "target", seat: 1);
            Reach(g, () => Current(g)?.Stage == XianbiStage.DiscardChoice);
            var prompt = P(g)!;
            Need(prompt.PlayerSeat == 0 && prompt.IsPrivate && Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null &&
                    g.CreateSnapshot(s).Players[0].Hand.Count == 0) && prompt.Choices.SelectMany(c => c.Cards).Distinct().Order().SequenceEqual(Current(g)!.Hand.Select(c => c.CardId).Order()),
                    "Only the real owner sees exactly the original discard-selection Hand entities; foreign views retain the count but zero faces.");
            Need(!g.Submit(new AnswerPromptCommand(1, prompt.PromptId, prompt.Choices[0].Id, g.Revision)).Accepted,
                "A foreign player cannot answer the owner's private exact Hand payment.");
            g = Cold(g, registry); SelectWhole(g); Pick(g, "pay");
            if (scene is "loss" or "death")
            {
                Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Watch &&
                    p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
                Need(E<XianbiEntityMovedEvent>(g).Any(e => e.Reason.Value == DiscardReason) && E<XianbiRandomIssuedEvent>(g).Length == 0 &&
                    E<XianbiCompletedEvent>(g).Length == 0, "The real paid discard child pauses before any random return or completion.");
                g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            }
            Reach(g, () => E<XianbiCompletedEvent>(g).Length == 1);
            var primary = E<XianbiPrimaryCommittedEvent>(g); var random = E<XianbiRandomIssuedEvent>(g);
            Need(primary.Length > 0 && g.CardMovements.Count(m => m.Reason.Value == DiscardReason) == primary.Length &&
                E<XianbiCompletedEvent>(g).Single().PrimaryCount == primary.Length, "Cold restoration never repays an already complete whole discard.");
            if (scene == "mixed")
            {
                Need(primary.Select(p => CardCatalog.Get(p.EffectiveKind).CategoryName).Distinct().Count() >= 2 && random.Length > 0,
                    "The fixed real mixed deck exercises more than one native category.");
                foreach (var candidate in E<XianbiRandomCandidateEvent>(g))
                {
                    var original = primary.Single(p => p.Index == candidate.Cursor);
                    Need(candidate.CardId != original.CardId && CardCatalog.Get(Kind(g, candidate.CardId)).CategoryName ==
                        CardCatalog.Get(original.EffectiveKind).CategoryName, "Every frozen RNG candidate is an eligible other same-category Discard entity.");
                }
            }
            else if (scene == "loss")
                Need(!(V(g, 0).Skills?.Any(s => s.Id == Skill) ?? false) && V(g, 0).IsAlive && random.Length == primary.Length,
                    "A genuine HP-conditional source loss after payment preserves every remaining issued return.");
            else Need(!V(g, 0).IsAlive && E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0) && random.Length == 0 &&
                !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Xianbi is not null),
                "True owner death fully resolves its native Dying/death child and closes payment once without a new dead-owner gain or menu.");
            _ = Cold(g, registry);
        }
    }

    public static void ZenrunOpaqueReplacementActualPhaseAndVictimChoiceCold()
    {
        foreach (var scene in new[] { "zenrun-decline", "zenrun-shortage", "zenrun-draw", "zenrun-block" })
        {
            var (g, registry) = Start(scene);
            var ownBefore = V(g, 0).HandCount;
            if (scene == "zenrun-shortage")
            {
                Use(g, "large-draw"); Play(g);
                Need(E<ZenrunStartedEvent>(g).Length == 0 && E<ZenrunPaidEvent>(g).Length == 0 && V(g, 0).HandCount == ownBefore + 20 &&
                    g.CardMovements.Count(m => m.Reason.Value == "skill-program." + Driver + ".Draw" && m.To == CardLocation.Hand(0)) == 20,
                    "An insufficient foreign HE cohort offers no partial replacement; all twenty requested cards remain a genuine draw.");
                _ = Cold(g, registry); continue;
            }
            if (scene == "zenrun-block")
            {
                var actor = ZenrunAiSeat(g);
                Use(g, "draw-other", targets: [actor]);
                Reach(g, () => Z(g)?.Stage == ZenrunStage.VictimChoice && P(g)?.PlayerSeat == 0);
                var frame = ZFrame(g); var r = frame.Zenrun!;
                Need(frame.OwnerSeat == actor && r.SourceSeat == 0 && r.Paid && r.Draw.RecipientSeat == actor &&
                    r.Draw.ActualTurnOwnerSeat == 0 && E<ZenrunCompletedEvent>(g).Length == 0,
                    "The genuine off-turn AI recipient replaces its own draw, and the actual human victim owns the compensation choice.");
                g = Cold(g, registry); Pick(g, "block"); Play(g);
                Need(E<ZenrunTargetBlockedEvent>(g) is [{ OwnerSeat: var issuer, TargetSeat: 0 }] && issuer == actor &&
                    E<ZenrunCompensationRequestedEvent>(g).Length == 0 && E<ZenrunCompletedEvent>(g).Single().Choice == 2,
                    "The victim may choose the permanent owner-target restriction instead of a fabricated compensation draw.");
                Use(g, "draw-two"); Play(g); End(g);
                Reach(g, () => E<ZenrunSourceSelectedEvent>(g).Length >= 2);
                var later = E<ZenrunSourceSelectedEvent>(g)[1];
                Need(later.SourceSeat != 0 && V(g, 0).HandCount >= 2 && E<ZenrunStartedEvent>(g)[1].Draw.ActualPhase is { Kind: ActualDiscardRecoveryPhaseKind.Draw },
                    "At the next real phase the same issuer can replace again, but cannot retarget the still-sufficient blocked person.");
                _ = Cold(g, registry); continue;
            }
            Use(g, "draw-two"); ReachZenrunOffer(g);
            var attempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single();
            Need(attempt.Context.RequestedCount == 2 && attempt.Context.RecipientSeat == 0 && attempt.Context.ActualPhase is { Kind: ActualDiscardRecoveryPhaseKind.Play } &&
                g.CardMovements.All(m => m.Reason.Value != "skill-program." + Driver + ".Draw"),
                "The genuine instruction pauses before any requested DrawPile material, using its actual Play token.");
            g = Cold(g, registry);
            if (scene == "zenrun-decline")
            {
                Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g);
                Use(g, "draw-two"); ReachZenrunOffer(g); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip"); Play(g);
                Need(E<ZenrunPaidEvent>(g).Length == 0 && E<ZenrunStartedEvent>(g).Length == 0 && V(g, 0).HandCount == ownBefore + 4,
                    "Declining leaves both ordinary draws intact and does not spend the phase's replacement permission.");
                _ = Cold(g, registry); continue;
            }
            Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, () => Z(g)?.Stage == ZenrunStage.Target); Pick(g, "target", seat: 1);
            Need(P(g) is { IsPrivate: true, PlayerSeat: 0 } p && p.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand))
                .All(c => c.Cards.Count == 0 && c.Parameters.ContainsKey("slot-index")) &&
                Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) && g.CreateSnapshot(0).Players[1].Hand.Count == 0,
                "Foreign Hand choices are genuine opaque slots, without leaking identities to the chooser or other viewers.");
            PickZone(g, CardZoneKind.Hand); PickZone(g, CardZoneKind.Hand); g = Cold(g, registry); Pick(g, "take"); Play(g);
            var paid = E<ZenrunPaidEvent>(g).Single(); var started = E<ZenrunStartedEvent>(g).Single();
            var materials = E<ZenrunMaterialCommittedEvent>(g);
            Need(materials.Length == 2 && materials.Select(m => m.CardId).Distinct().Count() == 2 && materials.All(m => m.From == CardLocation.Hand(1)) &&
                materials.All(c => g.CardMovements.Count(m => m.CardId == c.CardId && m.From == c.From && m.To == CardLocation.Hand(0) && m.Reason.Value == TakeReason) == 1) &&
                g.CardMovements.Count(m => m.Reason.Value == CompensationReason && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1)) == 2 &&
                E<ZenrunCompensationReturnedEvent>(g).Single() is { ActualDrawCount: 2, ReplacementGainedCount: 0 } &&
                E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.AttemptFrameId == started.Draw.AttemptFrameId && e.ParentFrameId == started.Draw.ParentFrameId && e.Replaced) == 1 &&
                g.CardMovements.All(m => m.Reason.Value != "skill-program." + Driver + ".Draw") && V(g, 0).HandCount == ownBefore + 2,
                "The original equal-count HE gain replaces the whole draw once, while the victim's two real drawn cards have a separate invoice.");
            Use(g, "draw-two"); Play(g);
            Need(E<ZenrunPaidEvent>(g).Length == 1 && V(g, 0).HandCount == ownBefore + 4,
                "A second real draw in the same phase remains ordinary after the one paid replacement.");
            End(g); Play(g); Use(g, "draw-two"); ReachZenrunOffer(g); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
            Reach(g, () => Z(g)?.Stage == ZenrunStage.Target); Pick(g, "target", seat: 1); PickZone(g, CardZoneKind.Hand); PickZone(g, CardZoneKind.Hand); Pick(g, "take"); Play(g);
            Need(E<ZenrunPaidEvent>(g).Length == 2 && E<ZenrunPaidEvent>(g)[1].Phase != paid.Phase && E<ZenrunPaidEvent>(g)[1].Phase.Kind == ActualDiscardRecoveryPhaseKind.Play,
                "A later actual Play phase receives its own genuine permission; no turn-wide or per-producer surrogate is used.");
            _ = Cold(g, registry);
        }
    }

    public static void ZenrunLionOxAndNestedXianbiPaidLossDeathCold()
    {
        foreach (var scene in new[] { "zenrun-lion", "zenrun-ox", "zenrun-loss", "zenrun-death" })
        {
            var (g, registry) = Start(scene); int? equipment = null, grain = null;
            if (scene is "zenrun-lion" or "zenrun-ox")
            {
                if (scene == "zenrun-lion") { Use(g, "wound-self"); Play(g); }
                var action = g.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip);
                Accept(g, new PlayCardCommand(0, action.CardId!.Value, action.TargetSeats, g.Revision, P(g)!.PromptId, action.PlayedCardKind, action.TargetCardId)
                { ConversionSource = action.ConversionSource, AdditionalConversionSources = action.AdditionalConversionSources ?? [] }); Play(g);
                equipment = V(g, 0).Equipment.Single().Id;
                if (scene == "zenrun-ox")
                {
                    grain = V(g, 0).Hand[0].Id;
                    Accept(g, new UseEquipmentEffectCommand(0, CardKind.WoodenOx, [grain.Value], [], g.Revision, P(g)!.PromptId)); Play(g);
                    Need(V(g, 0).WoodenOxGrain!.Any(c => c.Id == grain) && Enumerable.Range(1, 3).All(s => (g.CreateSnapshot(s).Players[0].WoodenOxGrain?.Count ?? 0) == 0),
                        "Real original Ox grain is private before the foreign equipment gain.");
                }
                Use(g, "give", V(g, 0).Hand.Select(c => c.Id).ToArray(), [2]); Play(g);
                Need(V(g, 0).HandCount == 0 && V(g, 0).Equipment.Count == 1, "The actual AI's first eligible source has only the intended real equipped material.");
                Use(g, "draw-other", targets: [ZenrunAiSeat(g)]);
            }
            else
            {
                Use(g, "place", [V(g, 0).Hand[0].Id], [1]); Play(g);
                Use(g, "give", V(g, 0).Hand.Select(c => c.Id).ToArray(), [3]); Play(g); Begin(g); Pick(g, "target", seat: 1);
                ReachZenrunOffer(g); Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
                Reach(g, () => Z(g)?.Stage == ZenrunStage.Target); Pick(g, "target", seat: 2); PickZone(g, CardZoneKind.Hand); Pick(g, "take");
            }
            Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Watch && p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
            var frame = ZFrame(g); var receipt = frame.Zenrun!;
            Need(receipt.Paid && receipt.Stage == ZenrunStage.TakeChildren && E<ZenrunPaidEvent>(g).Length == 1 && E<ZenrunCompletedEvent>(g).Length == 0 &&
                g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(a => a.Id == receipt.Draw.AttemptFrameId && a.Replacement is { } replacement &&
                    replacement.GainedIds.SequenceEqual(receipt.Materials.Select(m => m.CardId))),
                "The genuine replacement is already paid before its real equipment/health child and cannot be revoked or paid twice.");
            if (scene is "zenrun-loss" or "zenrun-death")
                Need(Current(g) is { Stage: XianbiStage.DrawRequest, Issued: true } && receipt.Draw.ParentFrameId ==
                    g.ResolutionStack.OfType<ProgramSkillFrame>().Single(f => f.Xianbi is not null).Id &&
                    E<XianbiDrawReturnedEvent>(g).Length == 0 && E<XianbiCompletedEvent>(g).Length == 0,
                    "The outer Xianbi operation truly waits through attempt, lifecycle, Zenrun and native health descendants before accepting its typed result.");
            g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue");
            if (scene is "zenrun-lion" or "zenrun-ox")
            {
                Reach(g, () => Z(g)?.Stage == ZenrunStage.VictimChoice && P(g)?.PlayerSeat == 0);
                Pick(g, "draw"); Play(g);
                Need(E<ZenrunMaterialCommittedEvent>(g) is [{ CardId: var realEquipment, From: var from }] && realEquipment == equipment && from == CardLocation.Equipment(0) &&
                    E<ZenrunCompensationReturnedEvent>(g).Single().ActualDrawCount == 1 && E<ZenrunCompletedEvent>(g).Length == 1,
                    "The actual equipped original is gained once, and only after native children does its living holder take one real compensation draw.");
                if (scene == "zenrun-lion") Need(V(g, 0).Hp == V(g, 0).MaxHp && E<RecoveryAppliedEvent>(g).Count(e => e.TargetSeat == 0 && e.Amount == 1) == 1,
                    "The donor's removed Lion performs exactly one native recovery before compensation.");
                else Need(g.CardMovements.Count(m => m.CardId == grain && m.From == CardLocation.WoodenOxGrain(0) && m.To == CardLocation.DiscardPile && m.Reason == CardMoveReasons.WoodenOxGrainDiscard) == 1 &&
                    !E<ZenrunMaterialCommittedEvent>(g).Any(e => e.CardId == grain) && E<DamageAppliedEvent>(g).Any(e => e.TargetSeat == 0 && e.Amount == 1),
                    "Ox grain spills as a distinct native child, triggers real damage, and is never counted as another replacement material.");
            }
            else
            {
                Reach(g, () => E<ZenrunCompletedEvent>(g).Length == 1 && E<XianbiCompletedEvent>(g).Length == 1);
                Need(E<XianbiDrawReturnedEvent>(g).Single() is { ActualDrawCount: 0, ReplacementGainedCount: 1 } && E<XianbiDrawnEntityEvent>(g).Length == 0 &&
                    E<XianbiRandomIssuedEvent>(g).Length == 0 && E<ZenrunEntityMovedEvent>(g).Count(e => e.Reason.Value == TakeReason) == 1 &&
                    E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.AttemptFrameId == receipt.Draw.AttemptFrameId && e.Replaced) == 1,
                    "Cold native return distinguishes the one real obtained material from zero actual Xianbi drawn cards and never repeats either effect.");
                if (scene == "zenrun-loss") Need(V(g, 0).IsAlive && !(V(g, 0).Skills?.Any(s => s.Id == Zenrun) ?? false) && V(g, 0).HandCount == 1,
                    "True HP-conditional source loss after replacement payment preserves the original replacement and promised living-victim compensation.");
                else Need(!V(g, 0).IsAlive && E<PlayerDiedEvent>(g).Any(e => e.VictimSeat == 0) &&
                    !g.ResolutionStack.OfType<ProgramSkillFrame>().Any(f => f.Zenrun is not null || f.Xianbi is not null),
                    "Real Dying/death finishes before the nested paid operation closes, without a new dead-recipient draw or stranded menu.");
            }
            _ = Cold(g, registry);
        }
    }

    public static void ZenrunActualNormalDrawPhaseReplacementCold()
    {
        var (g, registry) = Start("zenrun-phase", play: false);
        ReachZenrunOffer(g);
        var phase = g.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single();
        var attempt = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single();
        var handBefore = V(g, 0).HandCount;
        var pileBefore = g.CreateSnapshot(0).DrawPileCount;
        Need(phase is { OwnerSeat: 0, IsExtra: false, Skipped: false, DrawCount: 0, Stage: DrawPhaseObligationStage.RequestingDraw } &&
            phase.NativeDraw is { ReturnKind: NativeDrawAttemptReturnKind.DrawPhase, RequestedCount: 2, AttemptReturned: false, RecipientApplied: false } paid &&
            paid.ActiveAttemptId == attempt.Id && attempt.Context.ParentFrameId == phase.Id && attempt.Return.Kind == NativeDrawAttemptReturnKind.DrawPhase &&
            attempt.Context.RecipientSeat == 0 && attempt.Context.RequestedCount == 2 && attempt.Context.Reason == CardMoveReasons.Draw &&
            attempt.Context.ActualTurnNumber == phase.ActualTurnNumber && attempt.Context.ActualTurnOwnerSeat == 0 &&
            attempt.Context.ActualPhase is { Kind: ActualDiscardRecoveryPhaseKind.Draw } && g.State.Phase == TurnPhase.Draw &&
            !E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == phase.Id) &&
            !g.CardMovements.Any(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw),
            "The first normal Draw owns a genuine DrawPhase return before either its two original draw materials or a replacement is paid.");
        g = Cold(g, registry);
        Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "activate");
        Reach(g, () => Z(g)?.Stage == ZenrunStage.Target); Pick(g, "target", seat: 1);
        Need(Z(g) is { Stage: ZenrunStage.Cards, Paid: false } r && r.Draw == attempt.Context &&
            P(g) is { PlayerSeat: 0, IsPrivate: true } prompt && prompt.Choices.Where(c => c.Parameters.GetValueOrDefault("source-zone") == nameof(CardZoneKind.Hand))
                .All(c => c.Cards.Count == 0 && c.Parameters.ContainsKey("slot-index")) &&
            Enumerable.Range(1, 3).All(s => g.CreateSnapshot(s).PendingDecision is null) && V(g, 1).Hand.Count == 0,
            "The real normal-draw replacement privately selects only opaque original foreign Hand slots under that exact phase attempt.");
        g = Cold(g, registry); PickZone(g, CardZoneKind.Hand); PickZone(g, CardZoneKind.Hand);
        var selected = Z(g)!.Selected.Select(s => s.CardId).ToArray(); Pick(g, "take");
        Reach(g, () => Z(g)?.Stage == ZenrunStage.VictimChoice);
        var victim = ZFrame(g);
        var native = g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Single(a => a.Id == attempt.Id);
        Need(victim.Zenrun is { Paid: true, SourceSeat: 1 } replacement && replacement.Draw == attempt.Context &&
            native.Replacement is { } gained && gained.GainedIds.SequenceEqual(selected) && gained.GainedIds.Count == 2 &&
            g.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single(f => f.Id == phase.Id).NativeDraw is
                { ReturnKind: NativeDrawAttemptReturnKind.DrawPhase, AttemptReturned: false, Replacement: null } &&
            g.CreateSnapshot(1).PendingDecision is { PlayerSeat: 1, IsPrivate: true } choice && choice.SkillPrompt?.SkillId == Zenrun &&
            choice.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "draw") &&
            choice.Choices.Any(c => c.Parameters.GetValueOrDefault("branch") == "block") &&
            Enumerable.Range(0, 4).Where(s => s != 1).All(s => g.CreateSnapshot(s).PendingDecision is null) &&
            !E<NativeDrawAttemptDecidedEvent>(g).Any(e => e.AttemptFrameId == attempt.Id) &&
            !E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == phase.Id) && V(g, 0).HandCount == handBefore + 2,
            "Two real obtained materials are already paid, while only the actual AI victim sees its real choice and the normal phase still awaits the typed result.");
        g = Cold(g, registry);
        // The AI owns this private choice. A real native Advance chooses its
        // compensation; the test never answers on the victim's behalf.
        Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Watch &&
            p.Choices.Any(c => c.Parameters.GetValueOrDefault("option-id") == "continue"));
        var returned = g.ResolutionStack.OfType<DrawPhaseObligationFrame>().Single(f => f.Id == phase.Id);
        Need(returned is { DrawCount: 0, Skipped: false, IsExtra: false, NativeDraw: null, Stage: DrawPhaseObligationStage.ConsumeMarker } &&
            g.ResolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().Any(w => w.Window == SkillProgramTriggerWindow.DrawPhaseEnded &&
                w.ResumeDrawPhaseObligationFrameId == phase.Id && w.Id == returned.ActiveWindowFrameId) &&
            !g.ResolutionStack.OfType<NativeDrawAttemptFrame>().Any(a => a.Id == attempt.Id) &&
            E<NativeDrawAttemptDecidedEvent>(g).Count(e => e.AttemptFrameId == attempt.Id && e.ParentFrameId == phase.Id && e.RecipientSeat == 0 && e.RequestedCount == 2 && e.Replaced) == 1 &&
            E<ZenrunCompletedEvent>(g).Single().Paid && E<ZenrunCompensationReturnedEvent>(g).Single() is { ActualDrawCount: 2, ReplacementGainedCount: 0 } &&
            !E<ActualDrawPhaseCompletedEvent>(g).Any(e => e.FrameId == phase.Id) &&
            !g.CardMovements.Any(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(0) && m.Reason == CardMoveReasons.Draw),
            "The genuine DrawPhase return records zero actual drawn cards after the victim's separate real draw, then waits for its native DrawEnded observer.");
        g = Cold(g, registry); Answer(g, c => c.Parameters.GetValueOrDefault("option-id") == "continue"); Play(g);
        Need(E<ActualDrawPhaseCompletedEvent>(g).Single(e => e.FrameId == phase.Id) is { OwnerSeat: 0, IsExtra: false, Skipped: false, DrawCount: 0 } &&
            E<ZenrunPaidEvent>(g).Length == 1 && E<ZenrunMaterialCommittedEvent>(g).Select(e => e.CardId).SequenceEqual(selected) &&
            selected.All(id => g.CardMovements.Count(m => m.CardId == id && m.From == CardLocation.Hand(1) && m.To == CardLocation.Hand(0) && m.Reason.Value == TakeReason) == 1) &&
            g.CardMovements.Count(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(1) && m.Reason.Value == CompensationReason) == 2 &&
            g.CreateSnapshot(0).DrawPileCount == pileBefore - 2 && V(g, 0).HandCount == handBefore + 2 && E<ExtraDrawPhaseStartedEvent>(g).Length == 0,
            "Cold completion settles the original normal phase once as Draw0, retaining exactly two replacement gains and only the victim's two genuine DrawPile cards.");
        _ = Cold(g, registry);
    }

    private static ZenrunReceipt? Z(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.Zenrun is not null)?.Zenrun;
    private static ProgramSkillFrame ZFrame(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().Last(f => f.Zenrun is not null);
    private static int ZenrunAiSeat(GameEngine g) => Enumerable.Range(1, 3).Single(s => V(g, s).Skills?.Any(k => k.Id == Zenrun) ?? false);
    private static void ReachZenrunOffer(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0 } p && p.SkillPrompt?.SkillId == Zenrun &&
        p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "activate"));
    private static void PickZone(GameEngine g, CardZoneKind zone) => Answer(g, c => c.Parameters.GetValueOrDefault("branch") == "pick" &&
        c.Parameters.GetValueOrDefault("source-zone") == zone.ToString());
    private static T[] E<T>(GameEngine g) where T : IGameEvent => g.Events.Select(e => e.Payload).OfType<T>().ToArray();
    private static PlayerSnapshot V(GameEngine g, int seat) => g.CreateSnapshot(0).Players[seat];
    private static PendingDecision? P(GameEngine g) => g.PendingDecision;
    private static XianbiReceipt? Current(GameEngine g) => g.ResolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.Xianbi is not null)?.Xianbi;
    private static CardKind Kind(GameEngine g, int id) => g.CardMovements.First(m => m.CardId == id).CardKind;
    private static void Need(bool condition, string message) { if (!condition) throw new InvalidOperationException(message + " scene=" + Scene); }
    private static void Accept(GameEngine g, GameCommand command)
    { var result = g.Submit(command); if (!result.Accepted) throw new InvalidOperationException("scene=" + Scene + " command rejected: " + JsonSerializer.Serialize(result) + " " + Diagnostic(g)); }
    private static void Answer(GameEngine g, Func<PromptChoice, bool> predicate)
    {
        var p = P(g) ?? throw new InvalidOperationException("Missing real prompt " + Diagnostic(g));
        var c = p.Choices.FirstOrDefault(predicate) ?? throw new InvalidOperationException("Missing real choice " + Diagnostic(g));
        Need(p.PlayerSeat == 0, "This fixture answers only its human seat; native AI advances itself.");
        Accept(g, new AnswerPromptCommand(0, p.PromptId, c.Id, g.Revision));
    }
    private static void Pick(GameEngine g, string branch, int? card = null, int? seat = null) => Answer(g, c =>
        c.Parameters.GetValueOrDefault("branch") == branch && (card is null || c.Cards.SequenceEqual([card.Value])) &&
        (seat is null || c.Targets.SequenceEqual([seat.Value])));
    private static void SelectWhole(GameEngine g)
    { while (Current(g) is { } r && r.Selected.Count < r.Required) Pick(g, "pick"); }
    private static void Begin(GameEngine g)
    { Use(g, "adjust-and-return", skill: Skill); Reach(g, () => Current(g)?.Stage == XianbiStage.Target); }
    private static void Use(GameEngine g, string id, int[]? cards = null, int[]? targets = null, string skill = Driver) =>
        Accept(g, new UseProgramSkillCommand(0, skill, id, cards ?? [], targets ?? [], g.Revision, P(g)!.PromptId));
    private static void End(GameEngine g) => Accept(g, new EndPlayPhaseCommand(0, g.Revision, P(g)!.PromptId));
    private static void Play(GameEngine g) => Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.PlayCard });
    private static void Reach(GameEngine g, Func<bool> done, [CallerMemberName] string caller = "", [CallerLineNumber] int line = 0)
    {
        for (var i = 0; i < 300; i++)
        {
            if (done()) return;
            Need(g.State.Winner == Winner.None, $"Ended before {caller}:{line}: " + Diagnostic(g)); var p = P(g);
            if (p is null || p.PlayerSeat != 0) Accept(g, new AdvanceOneStepCommand(g.Revision));
            else if (p.Kind == DecisionKind.PlayCard) End(g);
            else if (p.Kind is DecisionKind.RespondDodge or DecisionKind.RespondSlash or DecisionKind.Nullification) Answer(g, c => c.Cards.Count == 0);
            else if (p.Kind == DecisionKind.RescueDying) Answer(g, c => c.Parameters.GetValueOrDefault("response") is "let-die" or "pass");
            else if (p.Choices.Any(c => c.Parameters.GetValueOrDefault("program-action") == "skip")) Answer(g, c => c.Parameters.GetValueOrDefault("program-action") == "skip");
            else throw new InvalidOperationException($"Unexpected {caller}:{line}: " + Diagnostic(g));
        }
        throw new InvalidOperationException($"Missing {caller}:{line}: " + Diagnostic(g));
    }
    private static string Diagnostic(GameEngine g) => JsonSerializer.Serialize(new { Scene, g.State.TurnNumber, g.State.CurrentSeat,
        g.State.Phase, Prompt = P(g) is { } p ? new { p.PlayerSeat, p.Kind, Skill = p.SkillPrompt?.SkillId, Choices = p.Choices.Take(5) } : null,
        Frames = g.ResolutionStack.TakeLast(5).Select(f => new { f.Id, Type = f.GetType().Name, Stage = (f as ProgramSkillFrame)?.Xianbi?.Stage,
            Zenrun = (f as ProgramSkillFrame)?.Zenrun?.Stage }), Health = Enumerable.Range(0, 4).Select(s => new { Seat = s, V(g, s).Hp, V(g, s).MaxHp, V(g, s).IsAlive }) });
    private static string State(GameEngine g) => JsonSerializer.Serialize(new { Views = Enumerable.Range(0, 4).Select(s => SnapshotJson.Serialize(g.CreateSnapshot(s))),
        Frames = g.ResolutionStack.Select(f => JsonSerializer.Serialize(f, f.GetType())), g.CardMovements, Zones = g.CreateCardZoneDiagnostics(),
        Events = g.Events.Select(e => JsonSerializer.Serialize(e.Payload, e.Payload.GetType())), Commands = CommandJson.Serialize(g.AcceptedCommands) });
    private static GameEngine Cold(GameEngine g, ContentRegistry registry)
    { var cold = GameReplay.Restore(GameCheckpointJson.Deserialize(GameCheckpointJson.Serialize(g.CreateCheckpoint())), registry); Need(State(g) == State(cold), "Cold replay preserves all private views, frozen original Hand cohort, RNG proof and once-paid native children."); return cold; }
    private static (GameEngine, ContentRegistry) Start(string scene, bool play = true)
    {
        Scene = scene; ContentRegistry registry;
        try { registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture(scene)); }
        catch (Exception e) { throw new InvalidOperationException("scene=" + scene + " fixture cause=" + e.Message, e); }
        var g = GameEngine.CreateStandard(new GameOptions { Seed = 7, PlayerCount = 4, HumanSeat = 0,
            HumanRole = scene is "death" or "zenrun-death" ? Role.Renegade : Role.Lord, ModeId = Mode, UseInteractiveSetup = true,
            UseInteractiveDiscard = false, AdvanceAfterHumanCommands = false, MaxTurns = 8 }, registry);
        Accept(g, new StartGameCommand()); Reach(g, () => P(g) is { PlayerSeat: 0, Kind: DecisionKind.SelectGeneral });
        Accept(g, new SelectGeneralCommand(0, Owner, g.Revision, P(g)!.PromptId)); if (play) Play(g); return (g, registry);
    }
    private sealed class Fixture(string scene) : IGameContentPackage
    {
        public PackageManifest Manifest { get; } = new("fixture-sun-hong-xianbi", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var zenrunScene = scene.StartsWith("zenrun-", StringComparison.Ordinal);
            var aiZenrun = scene is "zenrun-block" or "zenrun-lion" or "zenrun-ox";
            static string Resource(string suffix)
            {
                using var stream = typeof(StandardContentPackage).Assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.ol-sun-hong." + suffix + ".json")
                    ?? throw new InvalidOperationException("Missing actual SunHong skill resource " + suffix);
                using var reader = new StreamReader(stream); return reader.ReadToEnd();
            }
            var production = SkillProgramCatalog.Load(Resource("rules"), Resource("presentation"));
            builder.AddSkill(new(Skill, "险诐", production.Presentations[Skill].Description)
            { Program = production.Programs[Skill], ProgramPresentation = production.Presentations[Skill] });
            if (zenrunScene) builder.AddSkill(new(Zenrun, "谮润", production.Presentations[Zenrun].Description)
            { Program = production.Programs[Zenrun], ProgramPresentation = production.Presentations[Zenrun] });
            static JsonNode N(string s) => JsonNode.Parse(s)!;
            var defs = new JsonArray();
            JsonNode A(string id, string effects, int cards = 0, int targets = 0) => new JsonObject {
                ["id"] = id, ["minCards"] = cards, ["maxCards"] = cards, ["minTargets"] = targets,
                ["maxTargets"] = targets, ["targetKind"] = "anyLiving", ["usesPerTurn"] = null, ["effects"] = N("[" + effects + "]") };
            var give = A("give", """{"op":"giveSelected","target":"selectedTarget"}""", 1, 1); give["maxCards"] = 30;
            var place = A("place", """{"op":"captureSelectedCards","target":"owner","resultBind":"gear"},{"op":"placeSelectedEquipment","target":"selectedTarget","sourceBind":"gear"}""", 1, 1);
            place["sourceZones"] = N("[\"hand\"]"); place["cardCategories"] = N("[\"equipment\"]");
            defs.Add(new JsonObject { ["id"] = Driver, ["revision"] = 1,
                ["modifiers"] = N("""[{"id":"keep","query":"handLimit","operation":"add","value":40,"priority":0}]"""),
                ["activations"] = new JsonArray(A("seed", """{"op":"discardSelected","target":"owner","amount":2}""", 2),
                    A("stock", """{"op":"draw","target":"owner","amount":8}"""), give, place,
                    A("draw-two", """{"op":"draw","target":"owner","amount":2,"resultBind":"actual"}"""),
                    A("large-draw", """{"op":"draw","target":"owner","amount":20}"""),
                    A("draw-other", """{"op":"draw","target":"selectedTarget","amount":1}""", targets: 1),
                    A("wound-self", """{"op":"loseHp","target":"owner","amount":1}""")) });
            defs.Add(N("{\"id\":\"" + Quiet + "\",\"revision\":1,\"triggers\":[{\"id\":\"quiet\",\"window\":\"afterNormalDraw\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"skipTurnPhases\",\"target\":\"owner\",\"phases\":[\"play\"]}]}]}"));
            if (scene == "loss") defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-source\",\"skillId\":\"ol:xianbi\",\"minimumHp\":8}]}"));
            if (scene == "zenrun-loss") defs.Add(N("{\"id\":\"" + Conditional + "\",\"revision\":1,\"hpSkillGrants\":[{\"id\":\"healthy-source\",\"skillId\":\"ol:zenrun\",\"minimumHp\":8}]}"));
            if (scene is "loss" or "death") defs.Add(N("{\"id\":\"" + Watch + "\",\"revision\":1,\"triggers\":[{\"id\":\"paid-discard\",\"window\":\"cardsMoved\",\"subject\":\"owner\",\"sourceZones\":[\"hand\"],\"movementOccurrence\":\"perBatch\",\"movementReasons\":[\"" + DiscardReason + "\"],\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"paid\",\"options\":[{\"id\":\"continue\"}]},{\"op\":\"loseHp\",\"target\":\"owner\",\"amount\":" + (scene == "death" ? 20 : 1) + "}]}]}"));
            if (scene == "zenrun-phase") defs.Add(N("{\"id\":\"" + Watch + "\",\"revision\":1,\"triggers\":[{\"id\":\"phase-result\",\"window\":\"drawPhaseEnded\",\"subject\":\"owner\",\"optional\":false,\"effects\":[{\"op\":\"chooseOption\",\"target\":\"owner\",\"resultBind\":\"phase-result\",\"options\":[{\"id\":\"continue\"}]}]}]}"));
            if (scene is "zenrun-lion" or "zenrun-ox" or "zenrun-loss" or "zenrun-death")
            {
                var trigger = new JsonObject { ["id"] = "real-replacement-child", ["subject"] = "owner", ["optional"] = false };
                var effects = new JsonArray(N("""{"op":"chooseOption","target":"owner","resultBind":"paid","options":[{"id":"continue"}]}"""));
                if (scene == "zenrun-lion")
                { trigger["window"] = "afterHealthChanged"; trigger["condition"] = N("""{"kind":"healthChangeKindIs","hpChangeKind":"recovery"}"""); }
                else
                {
                    trigger["window"] = scene == "zenrun-ox" ? "cardsMoved" : "cardsGained";
                    trigger[scene == "zenrun-ox" ? "sourceZones" : "destinationZones"] = N(scene == "zenrun-ox" ? "[\"woodenOxGrain\"]" : "[\"hand\"]");
                    trigger["movementOccurrence"] = "perBatch";
                    trigger["movementReasons"] = new JsonArray(scene == "zenrun-ox" ? "equipment.wooden-ox.grain-discard" : TakeReason);
                    effects.Add(new JsonObject { ["op"] = scene == "zenrun-ox" ? "damage" : "loseHp", ["target"] = "owner", ["amount"] = scene == "zenrun-death" ? 20 : 1 });
                }
                trigger["effects"] = effects; defs.Add(new JsonObject { ["id"] = Watch, ["revision"] = 1, ["triggers"] = new JsonArray(trigger) });
            }
            var labels = defs.ToDictionary(n => n!["id"]!.GetValue<string>(), n => new { name = n!["id"]!.GetValue<string>(),
                description = "孙弘真实原生行为夹具", optionLabels = n["id"]!.GetValue<string>() == Watch ? new Dictionary<string, string> { ["continue"] = "继续" } : new Dictionary<string, string>() });
            var catalog = SkillProgramCatalog.Load(JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.RulesSchemaVersion, skills = defs }),
                JsonSerializer.Serialize(new { schemaVersion = SkillProgramCatalog.PresentationSchemaVersion, skills = labels }));
            foreach (var (id, program) in catalog.Programs) builder.AddSkill(new(id, id, "孙弘真实夹具") { Program = program, ProgramPresentation = catalog.Presentations[id] });
            var skills = new List<string> { Driver, scene == "loss" ? Conditional : Skill }; if (scene is "loss" or "death") skills.Add(Watch);
            if (zenrunScene && !aiZenrun) skills.Add(scene == "zenrun-loss" ? Conditional : Zenrun);
            if (scene is "zenrun-lion" or "zenrun-ox" or "zenrun-loss" or "zenrun-death") skills.Add(Watch);
            if (scene == "zenrun-phase") skills.Add(Watch);
            builder.AddSkill(new("fixture:sh-owner-pick", "本人", "固定公开选将") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => -100000000d) });
            builder.AddGeneral(new(Owner, "孙弘本体机制夹具", "supporter", "fixture:sh-owner-pick", "wu", 7, skills));
            var peers = Enumerable.Range(1, 3).Select(i => "fixture:sh-peer-" + i).ToArray();
            foreach (var (id, index) in peers.Select((id, index) => (id, index)))
            {
                var pick = "fixture:sh-pick-" + index;
                builder.AddSkill(new(pick, "其他角色", "固定公开顺序") { SelectionWeights = Enum.GetValues<Role>().ToDictionary(r => r, _ => 1000000d * (3 - index)) });
                builder.AddGeneral(new(id, "真实其他角色", "supporter", pick, "shu", 12, aiZenrun && index == 0 ? [Quiet, Zenrun] : [Quiet]));
            }
            var kinds = scene == "mixed" ? new[] { CardKind.Crossbow, CardKind.Dodge, CardKind.Duel } :
                scene == "zenrun-lion" ? [CardKind.SilverLion] : scene == "zenrun-ox" ? [CardKind.WoodenOx] : [CardKind.Crossbow];
            foreach (var kind in kinds) builder.AddCard(new("fixture:sh-" + kind, kind.ToString(), "真实原生实体", "保留原类型", kind));
            builder.AddDeck(new("fixture:sh-deck", "孙弘小固定牌堆", 4, 2, []) { PhysicalCards = Enumerable.Range(0, 96)
                .Select(i => new ContentDeckPhysicalCard("fixture:sh-" + kinds[i % kinds.Length], new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }[i % 4], i % 13 + 1)).ToArray() });
            builder.AddMode(new(Mode, "孙弘真实四人", 4, 4, scene is "death" or "zenrun-death" ? new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 2, [nameof(Role.Renegade)] = 1 }
                : new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = 3 }, "fixture:sh-deck", GeneralCandidateCount: 4, GeneralPoolIds: [Owner, .. peers]));
        }
    }
}
