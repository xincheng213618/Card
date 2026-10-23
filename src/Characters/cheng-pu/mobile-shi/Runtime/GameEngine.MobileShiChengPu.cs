using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiCpDuzuoSkill = "mobile:shi-duzuo", ShiCpBihanSkill = "mobile:shi-bihan";
    private const string ShiCpDuzuoGrantMoveReason = "skill-program.mobile:shi-duzuo.fire-slash-grant";
    private const string ShiCpBihanDiscardMoveReason = "skill-program.mobile:shi-bihan.discard";

    private static CardConversionSource ShiCpSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    [ProgramCapability]
    private sealed class MobileShiChengPuCapability(GameEngine engine) : IMobileShiChengPuProgramHost
    {
        public SkillProgramStepOutcome ResolveDuzuoGrant(ProgramSkillFrame frame) => engine.BeginMobileShiDuzuoGrantProgram(frame);
        public SkillProgramStepOutcome ResolveBihanGuard(ProgramSkillFrame frame) => engine.BeginMobileShiBihanGuardProgram(frame);
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 67300)]
    private bool CanOfferMobileShiChengPu(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId == ShiCpDuzuoSkill)
            return trigger.Id == MobileShiChengPuComposition.DuzuoGrantTrigger && MobileShiChengPuFireSlash() is not null;
        if (candidate.SkillId != ShiCpBihanSkill || trigger.Id != MobileShiChengPuComposition.BihanGuardTrigger ||
            context is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } victimSeat })
            return true;
        return new[] { candidate.OwnerSeat, victimSeat }.Distinct()
            .Any(seat => _players[seat].IsAlive && GetHand(_players[seat]).Count > _players[seat].Hp);
    }

    // 督佐：牌堆（自顶向下）优先于弃牌堆（自顶向下），两张公共区域都没有火杀时窗口不出价。
    private Card? MobileShiChengPuFireSlash() =>
        _cardZones.CardsAt(CardLocation.DrawPile).Reverse().FirstOrDefault(card => card.Kind == CardKind.FireSlash) ??
        _cardZones.CardsAt(CardLocation.DiscardPile).Reverse().FirstOrDefault(card => card.Kind == CardKind.FireSlash);

    private SkillProgramStepOutcome BeginMobileShiDuzuoGrantProgram(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (f.MobileShiChengPu is not null || _resolutionStack.LastOrDefault()?.Id != f.Id)
            throw new InvalidOperationException("Duzuo requires its original unpaid instruction.");
        if (f.TriggerId != MobileShiChengPuComposition.DuzuoGrantTrigger || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CardsGained, MovementBatch: not null })
            throw new InvalidOperationException("Duzuo requires its exact excluded hand-gain window.");
        var owner = _players[f.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId) ||
            MobileShiChengPuFireSlash() is null)
            return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f = f with { MobileShiChengPu = new("duzuo", _turnNumber) });
        ContinueMobileShiDuzuo(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void ContinueMobileShiDuzuo(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChengPu: { Operation: "duzuo" } r } f || f.Id != id) return;
        if (r.Stage == 9) { FinishProgramSkill(f, true); return; }
        if (r.TargetSeat < 0)
        {
            var candidates = _players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray();
            if (candidates.Length == 1)
            {
                ReplaceRuntimeTop(f = f with { MobileShiChengPu = r with { TargetSeat = candidates[0] } });
                ContinueMobileShiDuzuo(id);
                return;
            }
            PublishShiCpDuzuoRecipient(f);
            return;
        }
        var recipient = r.TargetSeat;
        if (!_players[recipient].IsAlive || MobileShiChengPuFireSlash() is not { } slash)
        { FinishProgramSkill(f, true); return; }
        var from = _cardZones.GetLocation(slash.Id);
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MobileShiChengPu = r with { Stage = 9, CardId = slash.Id, Before = before } });
        MoveCard(slash, from, CardLocation.Hand(recipient), new(ShiCpDuzuoGrantMoveReason));
        AdvanceEventRulesAndQueueFact(new MobileShiDuzuoGrantIssuedEvent(f.Id, ShiCpSource(f), f.GameplayHash,
            _turnNumber, recipient, slash.Id, from == CardLocation.DiscardPile, before, _movementSequence));
        AddLog("SkillEffect",
            $"{_players[f.OwnerSeat].Name} 督佐：{_players[recipient].Name} 从{(from == CardLocation.DiscardPile ? "弃牌堆" : "牌堆")}获得一张火【杀】。", f.OwnerSeat);
        if (!TryBeginCardsMovedProgramWindow()) FinishProgramSkill(f, true);
    }

    private IReadOnlyList<PromptChoice> ShiCpDuzuoChoices(ProgramSkillFrame f) => Array.AsReadOnly(_players
        .Where(p => p.IsAlive).Select(p => new PromptChoice(new($"shi-cheng-pu.{f.Id}.r{p.Seat}"),
            $"令 {_players[p.Seat].Name} 从牌堆或弃牌堆获得一张火【杀】", [], [p.Seat], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-duzuo", ["seat"] = p.Seat.ToString(CultureInfo.InvariantCulture) })).ToArray());

    private void PublishShiCpDuzuoRecipient(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "督佐：选择一名角色获得一张火【杀】。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = ShiCpDuzuoChoices(f),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("mobile-shi-duzuo")]
    private void AnswerMobileShiDuzuo(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChengPu: { Operation: "duzuo", TargetSeat: -1 } } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([selected], ShiCpDuzuoChoices(f).Where(current => current.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Duzuo choice lost its original frame, chooser or legal recipient.");
        ClearPendingDecision();
        var seat = int.Parse(selected.Parameters["seat"], CultureInfo.InvariantCulture);
        ReplaceRuntimeTop(f = f with { MobileShiChengPu = f.MobileShiChengPu! with { TargetSeat = seat },
            ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("duzuo-recipient",
                seat.ToString(CultureInfo.InvariantCulture), f.OwnerSeat)).ToArray()) });
        ContinueMobileShiDuzuo(f.Id);
        AdvanceRulesAndPublishState();
    }

    private SkillProgramStepOutcome BeginMobileShiBihanGuardProgram(ProgramSkillFrame frame)
    {
        var f = GetActiveProgramFrame(frame.Id);
        if (f.MobileShiChengPu is not null || _resolutionStack.LastOrDefault()?.Id != f.Id)
            throw new InvalidOperationException("Bihan requires its original unpaid instruction.");
        if (f.TriggerId != MobileShiChengPuComposition.BihanGuardTrigger || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } victim })
            throw new InvalidOperationException("Bihan requires its exact slash-damage window.");
        var owner = _players[f.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !HasRuntimeSkillInstance(owner, f.SkillId, f.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f = f with { MobileShiChengPu = new("bihan", _turnNumber, DamageTargetSeat: victim) });
        ContinueMobileShiBihan(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private int[] MobileShiBihanDiscarderSeats(ProgramSkillFrame f) => new[] { f.OwnerSeat, f.MobileShiChengPu!.DamageTargetSeat }
        .Distinct().Where(seat => _players[seat].IsAlive && GetHand(_players[seat]).Count > _players[seat].Hp).ToArray();

    private void BindShiCpBihanDiscarder(ProgramSkillFrame f, int seat)
    {
        var r = f.MobileShiChengPu!;
        var discarder = _players[seat];
        var planned = Math.Max(0, GetHand(discarder).Count - discarder.Hp);
        ReplaceRuntimeTop(f = f with { MobileShiChengPu = r with { Stage = 3, TargetSeat = seat },
            ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("bihan-discarder",
                seat.ToString(CultureInfo.InvariantCulture), f.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new MobileShiBihanGuardIssuedEvent(f.Id, ShiCpSource(f), f.GameplayHash,
            _turnNumber, r.DamageTargetSeat, seat, planned));
        AddLog("SkillEffect", $"{_players[f.OwnerSeat].Name} 蔽扞：{_players[seat].Name} 将手牌数弃置至当前体力值。", f.OwnerSeat);
    }

    private IReadOnlyList<PromptChoice> ShiCpBihanDiscarderChoices(ProgramSkillFrame f) => Array.AsReadOnly(
        MobileShiBihanDiscarderSeats(f).Select(seat => new PromptChoice(new($"shi-cheng-pu.{f.Id}.d{seat}"),
            $"令 {_players[seat].Name} 将手牌数弃置至当前体力值（{_players[seat].Name}：{GetHand(_players[seat]).Count} 张手牌，{_players[seat].Hp} 点体力）",
            [], [seat], new Dictionary<string, string>
            { ["program-action"] = "mobile-shi-bihan", ["branch"] = "discarder",
                ["seat"] = seat.ToString(CultureInfo.InvariantCulture) })).ToArray());

    private void PublishShiCpBihanDiscarder(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "蔽扞：选择将手牌数弃置至当前体力值的角色。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = ShiCpBihanDiscarderChoices(f),
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    private IReadOnlyList<PromptChoice> ShiCpBihanDiscardChoices(ProgramSkillFrame f) => Array.AsReadOnly(
        BuildOwnedCardPaymentChoices(f.Id, f.MobileShiChengPu!.TargetSeat, f.MobileShiChengPu.TargetSeat,
            [CardZoneKind.Hand], OwnedCardMoveIntent.Discard)
        .Select(choice => choice with { Parameters = new Dictionary<string, string>(choice.Parameters)
            { ["program-action"] = "mobile-shi-bihan", ["branch"] = "discard" } }).ToArray());

    private void PublishShiCpBihanDiscard(ProgramSkillFrame f)
    {
        var r = f.MobileShiChengPu!;
        var choices = ShiCpBihanDiscardChoices(f);
        if (choices.Count == 0) { AdvanceShiCpBihanReduction(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        var discarder = _players[r.TargetSeat];
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, r.TargetSeat,
            $"蔽扞：还需弃置 {GetHand(discarder).Count - discarder.Hp} 张手牌（弃置至当前体力值）。",
            choices.SelectMany(choice => choice.Cards).Distinct().ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = r.TargetSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = discarder.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer("mobile-shi-bihan")]
    private void AnswerMobileShiBihan(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChengPu: { Operation: "bihan" } r } f ||
            _pendingDecision?.PlayerSeat != (r.Stage == 0 ? f.OwnerSeat : r.TargetSeat) ||
            !AssistedChoicesEqual([selected], (r.Stage == 0 ? ShiCpBihanDiscarderChoices(f) : ShiCpBihanDiscardChoices(f))
                .Where(current => current.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Bihan choice lost its original frame, chooser or legal branch.");
        ClearPendingDecision();
        if (selected.Parameters["branch"] == "discarder")
        {
            if (r.Stage != 0) throw new InvalidOperationException("Bihan discarder choice left its unpaid stage.");
            BindShiCpBihanDiscarder(f, int.Parse(selected.Parameters["seat"], CultureInfo.InvariantCulture));
        }
        else
        {
            if (r.Stage != 3) throw new InvalidOperationException("Bihan discard choice left its discard stage.");
            var hand = GetHand(_players[r.TargetSeat]).ToArray();
            var index = int.Parse(selected.Parameters["slot-index"], CultureInfo.InvariantCulture);
            if (index < 0 || index >= hand.Length)
                throw new InvalidOperationException("Bihan discard choice left its exact hand slot.");
            var card = hand[index].Id;
            ReplaceRuntimeTop(f = f with { MobileShiChengPu = r with { Stage = 9, CardId = card, Before = _movementSequence,
                    Discarded = r.Discarded + 1 },
                PendingMovementContinuation = new(r.TargetSeat, 0, null) });
            MoveProgramLogicalDiscard(f, r.TargetSeat, [card], new(ShiCpBihanDiscardMoveReason));
        }
        ContinueMobileShiBihan(f.Id);
    }

    private bool DrainShiCpBihan(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);

    private void ContinueMobileShiBihan(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChengPu: { Operation: "bihan" } r } f || f.Id != id) return;
        if (r.Stage == 0)
        {
            var discarders = MobileShiBihanDiscarderSeats(f);
            if (discarders.Length == 0) { FinishProgramSkill(f, true); return; }
            if (discarders.Length > 1) { PublishShiCpBihanDiscarder(f); return; }
            BindShiCpBihanDiscarder(f, discarders[0]);
            ContinueMobileShiBihan(id);
            return;
        }
        if (r.Stage == 3)
        {
            var discarder = _players[r.TargetSeat];
            if (!discarder.IsAlive || GetHand(discarder).Count <= discarder.Hp) { AdvanceShiCpBihanReduction(f); return; }
            PublishShiCpBihanDiscard(f);
            return;
        }
        if (r.Stage == 9)
        {
            if (DrainShiCpBihan(f)) return;
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
            r = f.MobileShiChengPu!;
            var discarder = _players[r.TargetSeat];
            if (discarder.IsAlive && GetHand(discarder).Count > discarder.Hp)
            {
                ReplaceRuntimeTop(f = f with { MobileShiChengPu = r with { Stage = 3 } });
                PublishShiCpBihanDiscard(f);
                return;
            }
            AdvanceShiCpBihanReduction(f);
            return;
        }
        throw new InvalidOperationException("Bihan has an unknown native continuation stage.");
    }

    private void AdvanceShiCpBihanReduction(ProgramSkillFrame f)
    {
        var r = f.MobileShiChengPu!;
        ReplaceRuntimeTop(f = f with { MobileShiChengPu = r with { Stage = 6 } });
        r = f.MobileShiChengPu!;
        var reduced = false;
        if (f.WindowContext is { } context && _resolutionStack.OfType<BeforeDamageProgramWindowFrame>()
                .SingleOrDefault(window => window.Id == context.ParentFrameId) is { Prevented: false } window &&
            CurrentDamageAttempt is { } attack && attack.SourceSeat == window.SourceSeat && attack.TargetSeat == window.TargetSeat)
        {
            ReduceProgramCurrentDamage(f, 1);
            reduced = true;
        }
        AdvanceEventRulesAndQueueFact(new MobileShiBihanGuardCompletedEvent(f.Id, ShiCpSource(f),
            r.DamageTargetSeat, r.TargetSeat, r.Discarded, reduced));
        AddLog("SkillEffect", $"{_players[f.OwnerSeat].Name} 蔽扞：{_players[r.TargetSeat].Name} 弃置 {r.Discarded} 张手牌" +
            (reduced ? "，此伤害-1。" : "，此伤害已不存在。"), f.OwnerSeat);
        FinishProgramSkill(f, true);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 67300)]
    private bool ResumeShiCpChengPu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiChengPu: { } r } f || f.Id != id) return false;
        if (r.Operation == "bihan") ContinueMobileShiBihan(id);
        else ContinueMobileShiDuzuo(id);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 67300)]
    private bool ReturnShiCpChengPuMovement(ProgramSkillFrame f)
    {
        if (f.MobileShiChengPu is not { Operation: var operation, Stage: 9 }) return false;
        if (operation == "bihan") ContinueMobileShiBihan(f.Id);
        else ContinueMobileShiDuzuo(f.Id);
        return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 67300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 67300)]
    private bool ContinuePaidShiCpBihan(ProgramSkillFrame f) => f.MobileShiChengPu is { Operation: "bihan", Stage: > 0 };
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 67300)]
    private bool ShiCpBihanMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.MobileShiChengPu is { Operation: "bihan", Stage: 9 } r && effect?.Op == SkillProgramEffectOp.ResolveMobileShiBihanGuard &&
        p.CoverageResultBind is null && p.BeforeCount == 0 && p.SubjectSeat == r.TargetSeat;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 67300)]
    private void AssertShiCpChengPu(ProgramSkillFrame f)
    {
        if (f.MobileShiChengPu is not { } r) return;
        if (f.TriggerId is null || f.WindowContext is null || r.ActualTurn != _turnNumber || r.Before > _movementSequence ||
            r.Operation is not ("duzuo" or "bihan") ||
            r.Operation == "duzuo" && (f.SkillId != ShiCpDuzuoSkill || f.TriggerId != MobileShiChengPuComposition.DuzuoGrantTrigger ||
                r.Stage is not (0 or 9) || r.Stage == 0 && r.CardId != 0 || r.DamageTargetSeat != -1 || r.Discarded != 0) ||
            r.Operation == "bihan" && (f.SkillId != ShiCpBihanSkill || f.TriggerId != MobileShiChengPuComposition.BihanGuardTrigger ||
                !IsValidPlayerSeat(r.DamageTargetSeat) || r.Stage is not (0 or 3 or 6 or 9)))
            throw new InvalidOperationException("Shi Cheng Pu lost his original typed trigger or paid stage receipt.");
        if (r.Operation == "bihan" && r.Stage > 0 &&
            !ProgramEventHistory<MobileShiBihanGuardIssuedEvent>().Any(issued => issued.FrameId == f.Id &&
                issued.Source == ShiCpSource(f) && issued.GameplayHash == f.GameplayHash && issued.ActualTurn == r.ActualTurn &&
                issued.DamageTargetSeat == r.DamageTargetSeat && issued.DiscarderSeat == r.TargetSeat))
            throw new InvalidOperationException("Bihan lost its issued guard obligation.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiDuzuoGrant)]
    private PromptChoice ChooseShiCpDuzuo(PendingDecision prompt) =>
        prompt.Choices.FirstOrDefault(choice => choice.Targets is [var seat] && seat == prompt.PlayerSeat) ?? prompt.Choices[0];
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShiBihanGuard)]
    private PromptChoice ChooseShiCpBihan(PendingDecision prompt) => prompt.Choices[0];

    private bool IsMobileShiChengPuChoiceResult(ProgramSkillFrame frame, ProgramChoiceResultBinding binding) =>
        frame.MobileShiChengPu is { } r && frame.WindowContext is not null && binding.ChooserSeat == frame.OwnerSeat &&
        (binding.Name == "duzuo-recipient" && r.Operation == "duzuo" && r.TargetSeat >= 0 &&
            binding.OptionId == r.TargetSeat.ToString(CultureInfo.InvariantCulture) ||
         binding.Name == "bihan-discarder" && r.Operation == "bihan" && r.Stage > 0 && r.TargetSeat >= 0 &&
            binding.OptionId == r.TargetSeat.ToString(CultureInfo.InvariantCulture));
}
