using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiSjXiongtu = "mobile:shi-xiongtu", ShiSjXianshuai = "mobile:shi-xianshuai";
    private const string ShiSjDiscard = "skill.mobile-shi-sun-jun.discard", ShiSjDraw = "skill-program.mobile-shi-sun-jun.draw";
    private static CardConversionSource ShiSjSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability] private sealed class ShiSunJunHost(GameEngine e) : IShiSunJunHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, bool benefit) => e.BeginShiSj(f, benefit); }

    private int ShiSjPhaseLimit(int owner) => CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Play } phase &&
        phase.ActorSeat == owner && ProgramEventHistory<ShiSunJunBenefitIssuedEvent>().Any(e => e.Owner == owner && e.Turn == _turnNumber && e.Phase == phase) ? 2 : 1;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCondition, 49200, SkillProgramEffectOp.ResolveMobileShiXiongtu)]
    private bool AllowsShiSjActivation(ProgramActivationLegalityContext gate) =>
        _programPhaseUses.GetValueOrDefault((gate.OwnerSeat, gate.SkillId, gate.Activation.UsageGroup)) < ShiSjPhaseLimit(gate.OwnerSeat);

    private bool ShiSjDamageBenefitEligible(int owner, string instance, ProgramSkillWindowContext c) =>
        c is { Window: SkillProgramTriggerWindow.AfterDamageApplied, SourceSeat: { } source, DamageFrameId: { } damageId, Amount: > 0 } && source == owner &&
        GetProgramBooleanState(owner, ShiSjXiongtu, instance, "armed") &&
        _resolutionStack.SingleOrDefault(f => f.Id == damageId) is DamageFrame damage &&
        !(_resolutionStack.SingleOrDefault(f => f.Id == damage.ParentFrameId) is ProgramSkillFrame { SkillId: ShiSjXiongtu, ShiSunJun: { DamageBranch: true } }) &&
        !ProgramEventHistory<ShiSunJunBenefitIssuedEvent>().Any(e => e.Owner == owner && e.Turn == _turnNumber && e.DamageId == damageId);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 49200, SkillProgramEffectOp.ResolveMobileShiXiongtuDamageBenefit)]
    private bool AllowsShiSjDamageBenefit(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context) =>
        ShiSjDamageBenefitEligible(c.OwnerSeat, c.SkillInstanceId, context);

    private SkillProgramStepOutcome BeginShiSj(ProgramSkillFrame input, bool benefit)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.ShiSunJun is not null || f.InstructionIndex != 1 || f.SkillId != ShiSjXiongtu)
            throw new InvalidOperationException("Shi Sun Jun requires the original unpaid Xiongtu instruction.");
        var phase = CurrentActualDiscardRecoveryPhase();
        if (benefit)
        {
            if (f.TriggerId != "other-damage" || f.WindowContext is not { } context || !ShiSjDamageBenefitEligible(f.OwnerSeat, f.SkillInstanceId, context))
                throw new InvalidOperationException("Xiongtu benefit requires its exact different-source real damage occurrence.");
            ReplaceRuntimeTop(f = f with { ShiSunJun = new(ShiSjSource(f), f.GameplayHash, _turnNumber, phase, f.OwnerSeat, ShiSunJunStage.Drawing)
                { DamageId = context.DamageFrameId!.Value, Before = _movementSequence, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new ShiSunJunStartedEvent(f.Id, ShiSjSource(f), f.GameplayHash, _turnNumber, f.OwnerSeat));
            AdvanceEventRulesAndQueueFact(new ShiSunJunBenefitIssuedEvent(f.Id, f.OwnerSeat, _turnNumber, context.DamageFrameId.Value, phase));
            BeginOperationProgramDraw(f, f.OwnerSeat, 1, new(ShiSjDraw)); return SkillProgramStepOutcome.AwaitChild;
        }
        if (f.TriggerId is not null || f.ActivationId != "show-hand" || f.SelectedTargetSeats is not [var target] || target == f.OwnerSeat ||
            !_players[target].IsAlive || GetHand(_players[target]).Count == 0 || phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } || phase.ActorSeat != f.OwnerSeat)
            throw new InvalidOperationException("Xiongtu requires one other living actual Hand target in its own actual Play phase.");
        ReplaceRuntimeTop(f = f with { ShiSunJun = new(ShiSjSource(f), f.GameplayHash, _turnNumber, phase, target, ShiSunJunStage.Hand)
            { Hand = GetHand(_players[target]).Select(c => c.Id).ToArray() } });
        AdvanceEventRulesAndQueueFact(new ShiSunJunStartedEvent(f.Id, ShiSjSource(f), f.GameplayHash, _turnNumber, target));
        PublishShiSj(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> ShiSjOwnDiscardChoices(ProgramSkillFrame f) => BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, f.OwnerSeat,
        [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard,
        canSelect: (zone, card) => !IsSelfHandCategoryDiscardForbidden(f.OwnerSeat, card, new(zone, f.OwnerSeat), OwnedCardMoveIntent.Discard));
    private bool ShiSjCanDiscardRevealed(ProgramSkillFrame f) => f.ShiSunJun is { Revealed: > 0 } r &&
        _cardZones.GetLocation(r.Revealed) == CardLocation.Hand(r.Target) && BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.Target,
            [CardZoneKind.Hand], OwnedCardMoveIntent.Discard, canSelect: (_, card) => card.Id == r.Revealed).Count > 0;
    private IReadOnlyList<PromptChoice> ShiSjChoices(ProgramSkillFrame f)
    {
        var r = f.ShiSunJun!;
        PromptChoice C(string branch, string token, string label, IReadOnlyList<int>? cards = null) => new(new($"shi-sun-jun.{f.Id}.{r.Stage}.{branch}.{token}"), label, cards ?? [], [],
            new Dictionary<string, string> { ["program-action"] = "mobile-shi-sun-jun", ["branch"] = branch, ["token"] = token, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) });
        if (r.Stage == ShiSunJunStage.Hand) return r.Hand.Select((id, slot) => C("hand", slot.ToString(CultureInfo.InvariantCulture), $"展示 {_players[r.Target].Name} 的第 {slot + 1} 张手牌" )).ToArray();
        if (r.Stage == ShiSunJunStage.Branch)
        {
            var choices = new List<PromptChoice>();
            if (ShiSjCanDiscardRevealed(f)) choices.Add(C("discard", "discard", "弃置已展示的这张手牌"));
            if (ShiSjOwnDiscardChoices(f).Count >= r.Required) choices.Add(C("damage", "damage", $"弃置 {r.Required} 张牌，对其造成1点伤害"));
            return choices.ToArray();
        }
        if (r.Stage != ShiSunJunStage.Payment) return [];
        return ShiSjOwnDiscardChoices(f).Where(c => c.Cards is [var id] && !r.Payment.Contains(id)).Select(c =>
            C("cost", c.Cards.Single().ToString(CultureInfo.InvariantCulture), c.Description, c.Cards)).ToArray();
    }
    private void PublishShiSj(ProgramSkillFrame f)
    {
        var choices = ShiSjChoices(f);
        if (choices.Count == 0) { FinishShiSj(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, f.ShiSunJun!.Stage == ShiSunJunStage.Hand ? "凶图：选择一个不公开身份的手牌位置，随后公开展示该实体。" :
            f.ShiSunJun.Stage == ShiSunJunStage.Payment ? $"凶图：选择弃置 {f.ShiSunJun.Required} 张牌（已选 {f.ShiSunJun.Payment.Count} 张）。" : "凶图：选择弃置展示牌或支付冻结费用造成伤害。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mobile-shi-sun-jun")]
    private void AnswerShiSj(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiSunJun: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShiSjChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Xiongtu lost its exact private slot, frozen branch or actual cost choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (r.Stage == ShiSunJunStage.Hand)
        {
            if (!GetHand(_players[r.Target]).Select(c => c.Id).SequenceEqual(r.Hand)) throw new InvalidOperationException("Xiongtu's opaque Hand slots changed before selection.");
            var card = r.Hand[int.Parse(choice.Parameters["token"], CultureInfo.InvariantCulture)];
            ReplaceRuntimeTop(f = f with { ShiSunJun = r with { Stage = ShiSunJunStage.RevealChildren, Revealed = card } });
            AdvanceEventRulesAndQueueFact(new ShiSunJunRevealedEvent(f.Id, r.Target, card));
            AdvanceEventRulesAndQueueFact(new ProgramCardsRevealedEvent(f.Id, f.SkillId, f.ActivationId, f.OwnerSeat, "xiongtu-hand", Array.AsReadOnly(new[] { ToSnapshot(GetAdvancedCard(card)) })));
            if (!TryBeginPublicHandRevealWindow(f.Id, "mobile-shi-sun-jun-hand")) AfterShiSjPublicReveal(f);
            return;
        }
        if (r.Stage == ShiSunJunStage.Branch)
        {
            AdvanceEventRulesAndQueueFact(new ShiSunJunBranchIssuedEvent(f.Id, r.Required, branch == "damage"));
            if (branch == "damage")
            {
                ReplaceRuntimeTop(f = f with { ShiSunJun = r with { Stage = ShiSunJunStage.Payment, DamageBranch = true } });
                if (r.Required == 0) PayShiSjCost(f); else PublishShiSj(f);
                return;
            }
            SetProgramBooleanState(f, "armed", true);
            ReplaceRuntimeTop(f = f with { ShiSunJun = r with { Stage = ShiSunJunStage.DiscardChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
            MoveProgramLogicalDiscard(f, f.OwnerSeat, [r.Revealed], new(ShiSjDiscard), (batch, moves) =>
            { foreach (var m in moves) RecordBianFuRenForeignDiscardProducer(m, f.OwnerSeat, f.Id); });
            SealShiSj(f.Id); ContinueShiSj(f.Id); return;
        }
        if (r.Stage != ShiSunJunStage.Payment || branch != "cost" || choice.Cards is not [var paid]) throw new InvalidOperationException("Xiongtu cannot pay outside its frozen cost selection.");
        ReplaceRuntimeTop(f = f with { ShiSunJun = r with { Payment = r.Payment.Append(paid).ToArray() } });
        if (f.ShiSunJun!.Payment.Count == r.Required) PayShiSjCost(f); else PublishShiSj(f);
    }
    [PublicHandRevealReturn("mobile-shi-sun-jun-hand")]
    private void AfterShiSjPublicReveal(ProgramSkillFrame f)
    {
        if (f.ShiSunJun is not { Stage: ShiSunJunStage.RevealChildren } r) throw new InvalidOperationException("Xiongtu public reveal lost its original selected Hand entity.");
        // Freeze before the branch and all actual payment movements. The cost cannot reduce itself.
        if (!TracksTurnDiscardSuits) throw new InvalidOperationException("Xiongtu requires the enabled real turn discard-suit history.");
        var x = 4 - System.Numerics.BitOperations.PopCount((uint)(CurrentTurnDiscardSuitMask & 15));
        ReplaceRuntimeTop(f = f with { ShiSunJun = r with { Stage = ShiSunJunStage.Branch, Required = x } }); PublishShiSj(f);
    }
    private void PayShiSjCost(ProgramSkillFrame f)
    {
        var r = f.ShiSunJun!;
        if (r.Stage != ShiSunJunStage.Payment || r.Payment.Count != r.Required || r.Payment.Distinct().Count() != r.Required ||
            r.Payment.Any(id => !ShiSjOwnDiscardChoices(f).Any(c => c.Cards.SequenceEqual([id])))) throw new InvalidOperationException("Xiongtu must pay its complete originally frozen actual owned cost.");
        SetProgramBooleanState(f, "armed", true);
        ReplaceRuntimeTop(f = f with { ShiSunJun = r with { Stage = ShiSunJunStage.CostChildren, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (r.Payment.Count > 0) MoveProgramLogicalDiscard(f, f.OwnerSeat, r.Payment, new(ShiSjDiscard));
        SealShiSj(f.Id); var active = GetActiveProgramFrame(f.Id);
        AdvanceEventRulesAndQueueFact(new ShiSunJunCostPaidEvent(f.Id, r.Required, active.ShiSunJun!.Before, active.ShiSunJun.After));
        ContinueShiSj(f.Id);
    }
    private void SealShiSj(long id)
    { var f = GetActiveProgramFrame(id); ReplaceRuntimeFrame(id, f with { ShiSunJun = f.ShiSunJun! with { After = _movementSequence } }); }
    private bool DrainShiSj(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private void ContinueShiSj(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiSunJun: { } r } f || f.Id != id) return;
        if (r.Stage is ShiSunJunStage.Hand or ShiSunJunStage.Branch or ShiSunJunStage.Payment) { PublishShiSj(f); return; }
        if (r.Stage == ShiSunJunStage.RevealChildren) { if (!IsPublicHandRevealProducerSuspended(id, "mobile-shi-sun-jun-hand")) AfterShiSjPublicReveal(f); return; }
        if (r.Stage == ShiSunJunStage.Drawing) return;
        if (r.Stage == ShiSunJunStage.DamageIssued && f.AttackAttempt is not null) { CompleteDamageAttack(new ProgramAttackHandle(this, id)); return; }
        if (DrainShiSj(f)) return;
        ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null }); r = f.ShiSunJun!;
        if (r.Stage == ShiSunJunStage.CostChildren && _winner == Winner.None && _players[r.Target].IsAlive)
        { ReplaceRuntimeTop(f = f with { ShiSunJun = r with { Stage = ShiSunJunStage.DamageIssued } }); BeginProgramSkillDamage(f, r.Target, 1); return; }
        FinishShiSj(f);
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShiXiongtuDamageBenefit)]
    private void ReturnShiSjDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.ShiSunJun is not { Stage: ShiSunJunStage.Drawing } r || outcome.RecipientSeat != f.OwnerSeat || outcome.RequestedCount != 1 ||
            ProgramEventHistory<ShiSunJunDrawReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Xiongtu reward lost its once-issued exact native draw return.");
        ReplaceRuntimeTop(f = f with { ShiSunJun = r with { Stage = ShiSunJunStage.DrawChildren, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new ShiSunJunDrawReturnedEvent(f.Id, r.DamageId, outcome.ActualDrawIds.Count)); ContinueShiSj(f.Id);
    }
    private void FinishShiSj(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = GetActiveProgramFrame(f.Id) with { ShiSunJun = f.ShiSunJun! with { Stage = ShiSunJunStage.Finished }, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 49200)] private bool ResumeShiSj(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShiSunJun: not null } f || f.Id != id) return false; ContinueShiSj(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 49200)] private bool ReturnShiSjMovement(ProgramSkillFrame f)
    { if (f.ShiSunJun is null) return false; ContinueShiSj(f.Id); return true; }
    [ProgramViewContributor(ProgramViewContribution.ProgramPublicCards, 49200)]
    private IEnumerable<CardSnapshot> ShiSjPublicHandCard() => _resolutionStack.OfType<ProgramSkillFrame>().Where(f => f.ShiSunJun is { Revealed: > 0 })
        .Select(f => f.ShiSunJun!).Where(r => _cardZones.GetLocation(r.Revealed) == CardLocation.Hand(r.Target)).Select(r => ToSnapshot(GetAdvancedCard(r.Revealed)));
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiXiongtu)] private PromptChoice ChooseShiSj(PendingDecision p) => p.Choices[0];

    private PendingDecision? ProjectShiSjCardSelection(PendingDecision decision, ProgramSkillFrame f) =>
        f.ShiSunJun is { Stage: ShiSunJunStage.Payment } r && decision.PlayerSeat == f.OwnerSeat ?
            NativeOwnedSelection(decision, f, "shi-sun-jun:cost", "branch", ["cost"], [], r.Payment,
                r.Required - r.Payment.Count, r.Required - r.Payment.Count) : null;

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiSunJunPublicStates(CharacterState owner, string id)
    {
        if (id == ShiSjXianshuai)
            return EnabledRuntimeSkillGrants(owner).Where(g => g.SkillId == id).DistinctBy(g => g.SkillInstanceId).SelectMany(g =>
                new[] { new ProgramPublicRuleStateSnapshot(g.SkillInstanceId, "xianshuai-own-turn", ProgramPublicRuleStateKind.GameActivationProgress, ShiSjOwnTurn(owner.Seat) ? 1 : 0) }
                    .Concat(new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond }.Select(suit => new ProgramPublicRuleStateSnapshot(g.SkillInstanceId,
                        "xianshuai-suit-" + suit, ProgramPublicRuleStateKind.GameActivationProgress, ShiSjSuitSeen(owner.Seat, suit) ? 1 : 0)))).ToArray();
        if (id != ShiSjXiongtu) return [];
        return GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == id).SelectMany(i => new[] {
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "show-hand", ProgramPublicRuleStateKind.ActivationLimit, ShiSjPhaseLimit(owner.Seat), _programPhaseUses.GetValueOrDefault((owner.Seat, id, i.Program.Activations.Single().UsageGroup)), SkillUsageScope.Phase),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "xiongtu-cost", ProgramPublicRuleStateKind.GameActivationProgress, 4 - System.Numerics.BitOperations.PopCount((uint)(CurrentTurnDiscardSuitMask & 15))),
            new ProgramPublicRuleStateSnapshot(i.SkillInstanceId, "xiongtu-armed", ProgramPublicRuleStateKind.GameActivationProgress, GetProgramBooleanState(owner.Seat, id, i.SkillInstanceId, "armed") ? 1 : 0)
        }).ToArray();
    }
}
