using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShenGuoJiaHuishi = "mobile:huishi", ShenGuoJiaTianyi = "mobile:tianyi", ShenGuoJiaHuiShi = "mobile:huishi-gift", ShenGuoJiaZuoxing = "mobile:zuoxing";
    [ProgramCapability] private sealed class ShenGuoJiaHost(GameEngine engine) : IShenGuoJiaHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation) => engine.BeginShenGuoJia(frame, operation); }
    [ProgramCapability] private sealed class AwakeningOverrideQuery(GameEngine engine) : IProgramAwakeningOverrideQuery
    {
        public bool Granted(int owner, string skill, string? binding = null) => engine.HasShenGuoJiaAwakeningCondition(owner, skill, binding);
        public bool Issued(ProgramSkillFrame frame) => engine.HasShenGuoJiaAwakeningReceipt(frame);
    }
    private CardConversionSource ShenGuoJiaSource(ProgramSkillFrame frame) => new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 38000, SkillProgramEffectOp.ResolveShenGuoJiaHuishi)]
    private bool CanActivateShenGuoJiaHuishi(CharacterState owner) => owner.HasHpValue && owner.MaxHp < 10;
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 38001, SkillProgramEffectOp.ResolveShenGuoJiaZuoxing)]
    private bool CanActivateShenGuoJiaZuoxing(CharacterState owner) => ShenGuoJiaBenefactors(owner, GetRuntimeSkillInstanceId(owner, ShenGuoJiaZuoxing)).Count > 0 && ShenGuoJiaPlans(owner.Seat).Count > 0;
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.Collection, 38000, SkillProgramEffectOp.ResolveShenGuoJiaTianyi)]
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 38000, SkillProgramEffectOp.ResolveShenGuoJiaTianyi)]
    private bool CanAwakenShenGuoJia(ProgramTriggerLegalityContext gate) => HasShenGuoJiaAwakeningCondition(gate.OwnerSeat, gate.SkillId, gate.Candidate.BindingId) ||
        _players.Where(player => player.IsAlive).All(player => ProgramEventHistory<DamageAppliedEvent>().Any(damage => damage.TargetSeat == player.Seat && damage.Amount > 0));

    private SkillProgramStepOutcome BeginShenGuoJia(ProgramSkillFrame supplied, SkillProgramEffectOp operation)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (operation == SkillProgramEffectOp.ResolveShenGuoJiaTianyi)
        {
            if (frame.SkillId != ShenGuoJiaTianyi || frame.TriggerId != "all-living-damaged" || frame.SelectedTargetSeats is not [var recipient] || !_players[recipient].IsAlive)
                throw new InvalidOperationException("Tianyi requires the actual awakening's chosen living recipient.");
            var source = $"shen-guo-jia:{frame.OwnerSeat}:{frame.SkillInstanceId}:{frame.Id}";
            AcquireRuntimeSkills(_players[recipient], source, [ShenGuoJiaZuoxing]);
            AdvanceEventRulesAndQueueFact(new ShenGuoJiaBenefactorEvent(frame.Id, frame.OwnerSeat, recipient, source));
            AdvanceEventRulesAndQueueFact(new SkillAwakenedEvent(frame.OwnerSeat, frame.SkillId, _players[frame.OwnerSeat].MaxHp, Array.AsReadOnly(new[] { ShenGuoJiaZuoxing })));
            return SkillProgramStepOutcome.Continue;
        }
        if (frame.ShenGuoJia is not null || frame.TriggerId is not null || frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 ||
            operation == SkillProgramEffectOp.ResolveShenGuoJiaHuishi && (frame.SkillId != ShenGuoJiaHuishi || _players[frame.OwnerSeat].MaxHp >= 10) ||
            operation == SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant && (frame.SkillId != ShenGuoJiaHuiShi || frame.SelectedTargetSeats.Count != 1) ||
            operation == SkillProgramEffectOp.ResolveShenGuoJiaZuoxing && frame.SkillId != ShenGuoJiaZuoxing)
            throw new InvalidOperationException("Shen Guo Jia requires his unpaid own native activation.");
        var target = frame.SelectedTargetSeats.FirstOrDefault(-1);
        var stage = operation == SkillProgramEffectOp.ResolveShenGuoJiaHuishi ? ShenGuoJiaStage.Judging : operation == SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant ? ShenGuoJiaStage.AwakeningChoice : ShenGuoJiaStage.UseChoice;
        var receipt = new ShenGuoJiaReceipt(ShenGuoJiaSource(frame), frame.GameplayHash, operation, _turnNumber, _turnProgression.OwnerSeat, 0, stage) { Target = target };
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaStartedEvent(frame.Id, receipt.Source, frame.GameplayHash, operation, receipt.Turn, receipt.TurnOwner, 0, target));
        if (stage == ShenGuoJiaStage.Judging) BeginShenGuoJiaJudgment(frame);
        else if (stage == ShenGuoJiaStage.AwakeningChoice && (_players[frame.OwnerSeat].MaxHp < _players.Count(player => player.IsAlive) || ShenGuoJiaUnawakened(target).Count == 0))
        {
            ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Stage = ShenGuoJiaStage.DrawAttempt } });
            BeginOperationProgramDraw(frame, target, 4, new("skill-program.mobile:huishi-gift.draw"));
        }
        else PublishShenGuoJiaChoice(frame);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<(int Seat, string Instance)> ShenGuoJiaBenefactors(CharacterState owner, string instance) => Array.AsReadOnly(
        ProgramEventHistory<ShenGuoJiaBenefactorEvent>().Where(fact => fact.Recipient == owner.Seat && IsValidPlayerSeat(fact.Benefactor) && _players[fact.Benefactor].IsAlive && _players[fact.Benefactor].MaxHp > 1)
            .Where(fact => owner.SkillGrants.Grants.Any(grant => grant.IsEnabled && grant.SkillId == ShenGuoJiaZuoxing && grant.SkillInstanceId == instance && grant.SourceId == "acquired:" + fact.GrantSource))
            .Select(fact => (fact.Benefactor, fact.GrantSource)).Distinct().ToArray());
    private IReadOnlyList<IndexedSkillProgramInstance> ShenGuoJiaUnawakened(int seat) => Array.AsReadOnly(GetSkillBindingShard(_players[seat]).ProgramInstances
        .Where(instance => _contentRegistry.GetSkill(instance.SkillId).Tags.HasFlag(SkillTag.Awakening) && !ShenGuoJiaAlreadyAwakened(seat,instance.SkillId) &&
            !ProgramEventHistory<SkillAwakenedEvent>().Any(fact => fact.PlayerSeat == seat && fact.SkillId == instance.SkillId) &&
            !instance.Program.Triggers.Any(trigger => trigger.UsageScope == SkillUsageScope.Game && trigger.UsageLimit == 1 &&
                _skillRuntimeState.GetUsage(seat, instance.SkillId, ProgramTriggerUsageId(new(seat, instance.SkillId, trigger.Id, instance.SkillInstanceId, instance.Program.GameplayHash, trigger.Priority)), SkillUsageScope.Game) > 0))
        .DistinctBy(instance => instance.SkillId).ToArray());
    private bool ShenGuoJiaAlreadyAwakened(int seat, string skill) => skill switch
    {
        "ol:choujue" => WenYangHasAwakened(seat, 1),
        "ol:beishui" => WenYangHasAwakened(seat, 2),
        "ol:dili" => _skillRuntimeState.GetUsage(seat, skill, "awakening", SkillUsageScope.Game) > 0,
        "mou-mobile:zhiji" => ProgramEventHistory<MouJiangWeiAwakenedEvent>().Any(value => value.OwnerSeat == seat),
        _ => false
    };
    private IEnumerable<string> ShenGuoJiaAwakeningBindings(string skill) => _contentRegistry.GetSkill(skill).Program!.Triggers
        .Where(trigger => trigger.Effects.Any(effect => effect.Op is SkillProgramEffectOp.ChangeMaximumHp or SkillProgramEffectOp.GrantSkills or
            SkillProgramEffectOp.ReplaceSkillsOnAwakening or SkillProgramEffectOp.ResolveMouJiangWeiZhiji or SkillProgramEffectOp.ResolveMouCaoShuangFanshi ||
            effect.Op.ToString().Contains("Awaken", StringComparison.Ordinal))).Select(trigger => trigger.Id);
    private bool HasShenGuoJiaAwakeningCondition(int seat, string skill, string? binding = null) =>
        _contentRegistry.Skills.TryGetValue(skill, out var definition) && definition.Tags.HasFlag(SkillTag.Awakening) &&
        (binding is null || ShenGuoJiaAwakeningBindings(skill).Contains(binding, StringComparer.Ordinal)) &&
        ProgramEventHistory<ShenGuoJiaAwakeningOverrideEvent>().Any(fact => fact.Recipient == seat && fact.SkillId == skill && definition.Program?.GameplayHash == fact.GameplayHash &&
            HasRuntimeSkillInstance(_players[seat], skill, fact.InstanceId));
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.AwakeningConditionOverride, 38000)]
    private bool ShenGuoJiaAwakeningOverride(ProgramTriggerLegalityContext gate) => HasShenGuoJiaAwakeningCondition(gate.OwnerSeat, gate.SkillId, gate.Candidate.BindingId);

    private PromptChoice ShenGuoJiaChoice(ProgramSkillFrame frame, string branch, string label, IReadOnlyList<int>? targets = null, string? value = null) =>
        new(new($"shen-guo-jia.{frame.Id}.{frame.ShenGuoJia!.Stage}.{branch}.{value}"), label, [], targets ?? [],
            new Dictionary<string, string> { ["program-action"] = "shen-guo-jia", ["frame-id"] = frame.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch, ["value"] = value ?? "" });
    private IReadOnlyList<PromptChoice> ShenGuoJiaChoices(ProgramSkillFrame frame)
    {
        var receipt = frame.ShenGuoJia!; var choices = new List<PromptChoice>();
        switch (receipt.Stage)
        {
            case ShenGuoJiaStage.Repeat:
                if (receipt.MayRepeat && _players[frame.OwnerSeat].IsAlive && _players[frame.OwnerSeat].MaxHp < 10)
                    choices.Add(ShenGuoJiaChoice(frame, "repeat", "增加1点体力上限并继续判定"));
                choices.Add(ShenGuoJiaChoice(frame, "stop", "停止判定")); break;
            case ShenGuoJiaStage.Recipient:
                foreach (var player in _players.Where(player => player.IsAlive)) choices.Add(ShenGuoJiaChoice(frame, "recipient", $"将全部判定牌交给{player.Name}", [player.Seat], player.Seat.ToString(CultureInfo.InvariantCulture)));
                choices.Add(ShenGuoJiaChoice(frame, "decline", "不交给角色，将判定牌置入弃牌堆")); break;
            case ShenGuoJiaStage.AwakeningChoice:
                foreach (var instance in ShenGuoJiaUnawakened(receipt.Target)) choices.Add(ShenGuoJiaChoice(frame, "awakening", $"令{_players[receipt.Target].Name}满足【{_contentRegistry.GetSkill(instance.SkillId).Name}】觉醒条件", [receipt.Target], instance.SkillId)); break;
            case ShenGuoJiaStage.UseChoice:
                foreach (var benefactor in ShenGuoJiaBenefactors(_players[frame.OwnerSeat], frame.SkillInstanceId).Select(value => value.Seat).Distinct())
                    foreach (var (plan, index) in ShenGuoJiaPlans(frame.OwnerSeat).Select((plan, index) => (plan, index)))
                        choices.Add(ShenGuoJiaChoice(frame, "use", $"令{_players[benefactor].Name}减少1点体力上限：{CardCatalog.Get(plan.Kind).DisplayName}（{string.Join('、', plan.Targets.Select(seat => _players[seat].Name))}）", plan.Targets, $"{benefactor}:{index}"));
                choices.Add(ShenGuoJiaChoice(frame, "cancel", "取消发动")); break;
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishShenGuoJiaChoice(ProgramSkillFrame frame)
    {
        var choices = ShenGuoJiaChoices(frame);
        if (choices.Count == 0) { FinishShenGuoJia(frame); return; }
        ReplaceRuntimeTop(frame = frame with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(frame, frame.OwnerSeat, choices, $"【{_contentRegistry.GetSkill(frame.SkillId).Name}】请选择。", frame.OwnerSeat);
    }
    [ProgramAnswer("shen-guo-jia")]
    private void AnswerShenGuoJia(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenGuoJia: { } receipt } frame || _pendingDecision?.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual([choice], ShenGuoJiaChoices(frame).Where(current => current.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Shen Guo Jia requires the published current owner choice.");
        AssertShenGuoJia(frame); ClearPendingDecision(); var branch = choice.Parameters["branch"];
        if (receipt.Stage == ShenGuoJiaStage.Repeat)
        {
            if (branch == "repeat") { frame = SetShenGuoJiaChildren(frame, ShenGuoJiaStage.GrowChildren); PayShenGuoJiaMaximum(frame, frame.OwnerSeat, 1, "huishi-grow"); ContinueShenGuoJia(frame.Id); }
            else ChooseShenGuoJiaRecipient(frame); return;
        }
        if (receipt.Stage == ShenGuoJiaStage.Recipient)
        {
            var target = branch == "decline" ? -1 : choice.Targets.Single();
            ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Target = target } });
            MoveShenGuoJiaJudgments(frame, target); return;
        }
        if (receipt.Stage == ShenGuoJiaStage.AwakeningChoice)
        {
            var instance = ShenGuoJiaUnawakened(receipt.Target).Single(value => value.SkillId == choice.Parameters["value"]);
            AdvanceEventRulesAndQueueFact(new ShenGuoJiaAwakeningOverrideEvent(frame.Id, receipt.Source, receipt.Target, instance.SkillId, instance.SkillInstanceId, _contentRegistry.GetSkill(instance.SkillId).Program!.GameplayHash));
            PayShenGuoJiaFinalCost(frame, -2, "huishi-gift"); return;
        }
        if (receipt.Stage != ShenGuoJiaStage.UseChoice) throw new InvalidOperationException("The chosen instruction is already paid.");
        if (branch == "cancel")
        {
            var activation = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Activation!;
            var key = (frame.OwnerSeat, frame.SkillId, activation.UsageGroup);
            var turnUses = _programUses.GetValueOrDefault(key); var phaseUses = _programPhaseUses.GetValueOrDefault(key);
            if (turnUses < 1 || phaseUses < 1 || ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Any(fact => fact.FrameId == frame.Id)) throw new InvalidOperationException("Only the exact unpaid Zuoxing activation can return its reserved phase allowance.");
            _programUses[key] = turnUses - 1; _programPhaseUses[key] = phaseUses - 1;
            AdvanceEventRulesAndQueueFact(new ShenGuoJiaUnpaidCancelledEvent(frame.Id, receipt.Source, turnUses, phaseUses));
            FinishShenGuoJia(frame); return;
        }
        var values = choice.Parameters["value"].Split(':'); var benefactor = int.Parse(values[0], CultureInfo.InvariantCulture); var plan = ShenGuoJiaPlans(frame.OwnerSeat)[int.Parse(values[1], CultureInfo.InvariantCulture)];
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Plan = plan, Benefactor = benefactor } });
        frame = SetShenGuoJiaChildren(frame, ShenGuoJiaStage.CostChildren); PayShenGuoJiaMaximum(frame, benefactor, -1, "zuoxing"); ContinueShenGuoJia(frame.Id);
    }
    private ProgramSkillFrame SetShenGuoJiaChildren(ProgramSkillFrame frame, ShenGuoJiaStage stage)
    {
        frame = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = frame.ShenGuoJia! with { Stage = stage, Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) }); return frame;
    }
    private void FinishShenGuoJiaIssue(long id)
    { var frame = GetActiveProgramFrame(id); ReplaceRuntimeTop(frame with { ShenGuoJia = frame.ShenGuoJia! with { After = _movementSequence } }); }
    private void PayShenGuoJiaMaximum(ProgramSkillFrame frame, int seat, int amount, string purpose)
    {
        var player = _players[seat]; var before = player.MaxHp; var after = HasWushuangTactic(seat, 2020) ? 7 : Math.Max(0, checked(before + amount)); var hp = player.Hp;
        player.MaxHp = after; player.Hp = Math.Min(hp, after);
        frame = GetActiveProgramFrame(frame.Id); ReplaceRuntimeTop(frame with { ShenGuoJia = frame.ShenGuoJia! with { MaximumBefore = before, MaximumAfter = after } });
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(seat, after - before, after, frame.SkillId) { HpBeforeClamp = hp });
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaMaximumPaidEvent(frame.Id, ProgramEventHistory<ShenGuoJiaMaximumPaidEvent>().Count(fact => fact.FrameId == frame.Id), seat, before, after, purpose));
        if (after == 0 && player.IsAlive) BeginPlayerDeath(frame.Id, player, null, null, null, causingProgramSkillFrameId: frame.Id);
    }
    private void PayShenGuoJiaFinalCost(ProgramSkillFrame frame, int amount, string purpose)
    { frame = SetShenGuoJiaChildren(frame, ShenGuoJiaStage.CostChildren); PayShenGuoJiaMaximum(frame, frame.OwnerSeat, amount, purpose); ContinueShenGuoJia(frame.Id); }
    private bool DrainShenGuoJia(ProgramSkillFrame frame) => TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(frame.Id) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(frame.Id);
    private void ContinueShenGuoJia(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenGuoJia: { } receipt } frame || frame.Id != id) return;
        if (receipt.Stage is ShenGuoJiaStage.Judging or ShenGuoJiaStage.DrawAttempt or ShenGuoJiaStage.NativeUse or ShenGuoJiaStage.IssuingUse or ShenGuoJiaStage.ReturnedUse) return;
        if (DrainShenGuoJia(frame)) return;
        frame = GetActiveProgramFrame(id); receipt = frame.ShenGuoJia!;
        ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null });
        if (receipt.Stage == ShenGuoJiaStage.JudgmentChildren)
        {
            if (!_players[frame.OwnerSeat].IsAlive || _winner != Winner.None) { MoveShenGuoJiaJudgments(frame, -1); return; }
            if (receipt.MayRepeat && _players[frame.OwnerSeat].MaxHp < 10)
            { ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Stage = ShenGuoJiaStage.Repeat } }); PublishShenGuoJiaChoice(frame); }
            else ChooseShenGuoJiaRecipient(frame); return;
        }
        if (receipt.Stage == ShenGuoJiaStage.GrowChildren)
        { if (_players[frame.OwnerSeat].IsAlive && _winner == Winner.None) BeginShenGuoJiaJudgment(frame); else MoveShenGuoJiaJudgments(frame, -1); return; }
        if (receipt.Stage == ShenGuoJiaStage.GainChildren)
        {
            if (_players[frame.OwnerSeat].IsAlive && IsValidPlayerSeat(receipt.Target) && _players[receipt.Target].IsAlive && GetHand(_players[receipt.Target]).Count == _players.Where(player => player.IsAlive).Max(player => GetHand(player).Count))
            { PayShenGuoJiaFinalCost(frame, -1, "huishi-most-hand"); return; }
            FinishShenGuoJia(frame); return;
        }
        if (receipt.Stage == ShenGuoJiaStage.DrawChildren) { PayShenGuoJiaFinalCost(frame, -2, "huishi-gift"); return; }
        if (receipt.Stage == ShenGuoJiaStage.CostChildren && receipt.Operation == SkillProgramEffectOp.ResolveShenGuoJiaZuoxing && _players[frame.OwnerSeat].IsAlive && _winner == Winner.None)
        { IssueShenGuoJiaVirtualUse(frame); return; }
        if (receipt.Stage is ShenGuoJiaStage.CostChildren or ShenGuoJiaStage.CleanupChildren) { FinishShenGuoJia(frame); return; }
        PublishShenGuoJiaChoice(frame);
    }
    private void ChooseShenGuoJiaRecipient(ProgramSkillFrame frame)
    {
        if (!_players[frame.OwnerSeat].IsAlive || !frame.ShenGuoJia!.Cards.Any(id => _cardZones.GetLocation(id) == CardLocation.Processing)) { MoveShenGuoJiaJudgments(frame, -1); return; }
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = frame.ShenGuoJia with { Stage = ShenGuoJiaStage.Recipient } }); PublishShenGuoJiaChoice(frame);
    }
    private void MoveShenGuoJiaJudgments(ProgramSkillFrame frame, int target)
    {
        var ids = frame.ShenGuoJia!.Cards.Where(id => _cardZones.GetLocation(id) == CardLocation.Processing).ToArray();
        frame = SetShenGuoJiaChildren(frame, target < 0 ? ShenGuoJiaStage.CleanupChildren : ShenGuoJiaStage.GainChildren);
        MoveProgramCardsFromMultipleSources(ids, target < 0 ? CardLocation.DiscardPile : CardLocation.Hand(target), new(target < 0 ? "skill-program.mobile:huishi.cleanup" : "skill-program.mobile:huishi.give"),
            target < 0 ? null : NativeGiveCompletionRecords(frame.Id, frame.OwnerSeat, target, ids, _movementSequence, (_, _) => { }));
        FinishShenGuoJiaIssue(frame.Id); ContinueShenGuoJia(frame.Id);
    }
    private void FinishShenGuoJia(ProgramSkillFrame frame)
    {
        AdvanceEventRulesAndQueueFact(new ShenGuoJiaCompletedEvent(frame.Id));
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = null, PendingMovementContinuation = null }); FinishProgramSkill(frame, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 38000)] private bool ResumeShenGuoJia(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ShenGuoJia: not null } frame || frame.Id != id) return false; ContinueShenGuoJia(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 38000)] private bool ReturnShenGuoJiaMovement(ProgramSkillFrame frame) => frame.ShenGuoJia is not null && ResumeShenGuoJia(frame.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 38000)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 38000)]
    private bool ContinuePaidShenGuoJia(ProgramSkillFrame frame) => frame.ShenGuoJia is not null && ValidShenGuoJia(frame);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveShenGuoJiaHuishi, SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant, SkillProgramEffectOp.ResolveShenGuoJiaZuoxing)]
    private PromptChoice ChooseShenGuoJiaAi(PendingDecision prompt, ProgramSkillFrame frame) => prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "recipient" && choice.Targets.Contains(frame.OwnerSeat)) ?? prompt.Choices[0];
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveShenGuoJiaHuishiGrant)]
    private void ReturnShenGuoJiaFourDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var frame = GetActiveProgramFrame(supplied.Id); var receipt = frame.ShenGuoJia!;
        if (!ValidShenGuoJia(frame) || receipt.Stage != ShenGuoJiaStage.DrawAttempt || frame.NativeDraw is not null || outcome.RecipientSeat != receipt.Target || outcome.RequestedCount != 4 || outcome.SequenceAfter != _movementSequence)
            throw new InvalidOperationException("Hui Shi requires its exact four-card native draw return.");
        ReplaceRuntimeTop(frame = frame with { ShenGuoJia = receipt with { Stage = ShenGuoJiaStage.DrawChildren, DrawOutcome = outcome, Before = outcome.SequenceBefore, After = outcome.SequenceAfter }, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        ContinueShenGuoJia(frame.Id);
    }
}
