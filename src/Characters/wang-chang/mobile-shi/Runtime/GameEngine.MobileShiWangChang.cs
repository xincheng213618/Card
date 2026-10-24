using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShiKaiji = "mobile:shi-kaiji", ShiShepan = "mobile:shi-shepan";
    private const string ShiKaijiDraw = "skill-program.mobile:shi-kaiji.recipient-draw";
    private static CardConversionSource ShiWangChangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static CardKind ShiWangChangName(CardKind kind) => kind is CardKind.FireSlash or CardKind.ThunderSlash ? CardKind.Slash : kind;
    private int ShiKaijiMaximum() => Math.Max(1, ProgramEventHistory<PlayerDyingEvent>().Select(e => e.VictimSeat).Distinct().Count(s => _players[s].IsAlive));
    private CardUseFrame? ShiShepanUse(ProgramSkillWindowContext c, int owner) =>
        c is { Window: SkillProgramTriggerWindow.CardUseBeforeTargetEffects, CardUse: { } identity } &&
        _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().SingleOrDefault(w => w.Id == c.ParentFrameId) is { } window &&
        window.ParentFrameId == identity.ParentCardUseFrameId && window.Action.ActionId == identity.CardActionId &&
        LifecycleCardUse(window.ParentFrameId) is { Action: { Type: CardActionType.Use } a } use &&
        a.ActorSeat != owner && a.ActorSeat == c.SourceSeat && a.ActionId == identity.CardActionId &&
        use.TargetSeats.Contains(owner) && c.TargetSeat == owner && IsDamageCardKind(a.EffectiveKind) ? use : null;

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 47400)]
    private bool CanOfferShiWangChang(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != ShiShepan) return true;
        return ShiShepanUse(context, candidate.OwnerSeat) is { } use &&
            !ProgramEventHistory<MobileShiShepanChosenEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat &&
                e.Source.SkillInstanceId == candidate.SkillInstanceId && e.GameplayHash == candidate.GameplayHash &&
                e.Round == _roundNumber && e.Name == ShiWangChangName(use.CardKind));
    }

    [ProgramCapability]
    private sealed class MobileShiWangChangCapability(GameEngine engine) : IMobileShiWangChangHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp op) => engine.ResolveShiWangChang(frame, op);
    }
    private SkillProgramStepOutcome ResolveShiWangChang(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (op == SkillProgramEffectOp.BindMobileShiKaijiReward)
        {
            RequireShiWangChang(f);
            if (f.SkillId != ShiKaiji || f.InstructionIndex != 2 || f.MobileShiWangChang is not { Stage: MobileShiWangChangStage.Complete } r)
                throw new InvalidOperationException("Kaiji reward lost its once-paid recipient cursor.");
            ReplaceRuntimeTop(f with { ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("kaiji-reward", r.NonBasicGained ? "yes" : "no", f.OwnerSeat)).ToArray()) });
            AdvanceEventRulesAndQueueFact(new MobileShiKaijiRewardBoundEvent(f.Id, r.NonBasicGained));
            return SkillProgramStepOutcome.Continue;
        }
        if (op == SkillProgramEffectOp.ResolveMobileShiShepan)
        {
            RequireShiWangChang(f);
            if (f.SkillId != ShiShepan || f.InstructionIndex != 4 || f.MobileShiWangChang is not { Stage: MobileShiWangChangStage.Resolving, Mode: not null } r ||
                f.WindowContext is not { } context || ShiShepanUse(context, f.OwnerSeat) is not { } use || use.Id != r.UseId || use.Action!.ActionId != r.ActionId)
                throw new InvalidOperationException("Shepan lost its original current use after its paid adjustment.");
            var own = GetHand(_players[f.OwnerSeat]).Count; var actor = GetHand(_players[r.Actor]).Count;
            var ineffective = _players[f.OwnerSeat].IsAlive && own == actor;
            ReplaceRuntimeTop(f with { MobileShiWangChang = r with { Stage = MobileShiWangChangStage.Complete } });
            if (ineffective)
            {
                MarkCardEffectIneffective(use.Id, f.OwnerSeat);
                AdvanceEventRulesAndQueueFact(new CardEffectSkippedEvent(use.Id, r.Actor, f.OwnerSeat, use.CardKind, CardEffectSkipReason.SkillNullified));
            }
            AdvanceEventRulesAndQueueFact(new MobileShiShepanResolvedEvent(f.Id, use.Id, r.ActionId, f.OwnerSeat, r.Actor, own, actor, ineffective));
            return SkillProgramStepOutcome.Continue;
        }
        if (f.MobileShiWangChang is not null || f.InstructionIndex != 1 || f.WindowContext is not { } c || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0)
            throw new InvalidOperationException("Shi Wang Chang requires its original unpaid trigger frame.");
        var kaiji = op == SkillProgramEffectOp.SelectMobileShiKaijiTargets;
        if (kaiji && (f.SkillId != ShiKaiji || c.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || c.OwnerSeat != f.OwnerSeat || c.SourceSeat != f.OwnerSeat) ||
            !kaiji && (op != SkillProgramEffectOp.ChooseMobileShiShepan || f.SkillId != ShiShepan || ShiShepanUse(c, f.OwnerSeat) is null))
            throw new InvalidOperationException("Shi Wang Chang lost its exact native preparation or target boundary.");
        var parent = kaiji ? null : ShiShepanUse(c, f.OwnerSeat)!;
        var receipt = new MobileShiWangChangReceipt(ShiWangChangSource(f), f.GameplayHash, c.ParentFrameId,
            _turnNumber, _currentSeat, kaiji ? ShiKaijiMaximum() : 0, kaiji ? MobileShiWangChangStage.Targets : MobileShiWangChangStage.Mode)
        { UseId = parent?.Id ?? 0, ActionId = parent?.Action?.ActionId ?? 0, Actor = parent?.SourceSeat ?? -1, Kind = parent?.CardKind ?? default };
        ReplaceRuntimeTop(f = f with { MobileShiWangChang = receipt });
        AdvanceEventRulesAndQueueFact(new MobileShiWangChangStartedEvent(f.Id, receipt.Source, f.GameplayHash, receipt.WindowId,
            receipt.ActualTurn, receipt.TurnOwner, receipt.Maximum, receipt.UseId, receipt.ActionId, receipt.Actor, receipt.Kind));
        PublishShiWangChang(f); return SkillProgramStepOutcome.AwaitChoice;
    }

    private PromptChoice ShiWangChangChoice(ProgramSkillFrame f, string branch, string label, int? target = null) => new(
        new($"shi-wang-chang.{f.Id}.{branch}.{target ?? -1}"), label, [], target is { } t ? [t] : [],
        new Dictionary<string, string> { ["program-action"] = "shi-wang-chang", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch });
    private IReadOnlyList<PromptChoice> ShiWangChangChoices(ProgramSkillFrame f)
    {
        var r = f.MobileShiWangChang!; var choices = new List<PromptChoice>();
        if (r.Stage == MobileShiWangChangStage.Targets)
        {
            foreach (var p in _players.Where(p => p.IsAlive && (r.Targets.Count < r.Maximum || r.Targets.Contains(p.Seat))))
                choices.Add(ShiWangChangChoice(f, r.Targets.Contains(p.Seat) ? "unpick" : "pick", (r.Targets.Contains(p.Seat) ? "取消选择 " : "选择 ") + p.Name, p.Seat));
            choices.Add(ShiWangChangChoice(f, "done", r.Targets.Count == 0 ? "取消开济" : $"令所选 {r.Targets.Count} 名角色各摸一张牌"));
        }
        if (r.Stage == MobileShiWangChangStage.Mode)
        {
            choices.Add(ShiWangChangChoice(f, "draw", "摸一张牌"));
            if (GetHand(_players[r.Actor]).Count > 0) choices.Add(ShiWangChangChoice(f, "top", "将使用者一张手牌置于牌堆顶"));
        }
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishShiWangChang(ProgramSkillFrame f)
    {
        var r = f.MobileShiWangChang!; var choices = ShiWangChangChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            r.Stage == MobileShiWangChangStage.Targets ? $"开济：已选择 {r.Targets.Count}/{r.Maximum} 名角色（可以选择自己）。" : "慑叛：调整手牌数后，相同则当前牌对你无效。", [],
            choices.SelectMany(choice => choice.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("shi-wang-chang")]
    private void AnswerShiWangChang(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiWangChang: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !ShiWangChangChoices(f).Any(c => AssistedChoicesEqual([c], [choice]))) throw new InvalidOperationException("Shi Wang Chang rejects a stale target or adjustment branch.");
        RequireShiWangChang(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (branch is "pick" or "unpick")
        {
            var seat = choice.Targets.Single(); var targets = branch == "pick" ? r.Targets.Append(seat).ToArray() : r.Targets.Where(s => s != seat).ToArray();
            ReplaceRuntimeTop(f = f with { MobileShiWangChang = r with { Targets = targets } }); PublishShiWangChang(f); return;
        }
        if (branch == "done")
        {
            for (var i = 0; i < r.Targets.Count; i++) AdvanceEventRulesAndQueueFact(new MobileShiKaijiTargetCommittedEvent(f.Id, i, r.Targets[i]));
            ReplaceRuntimeTop(f = f with { MobileShiWangChang = r with { Stage = MobileShiWangChangStage.DrawChildren } });
            ContinueShiKaiji(f.Id); return;
        }
        if (branch is not ("draw" or "top")) throw new InvalidOperationException("Unknown Shepan adjustment.");
        ReplaceRuntimeTop(f = f with { MobileShiWangChang = r with { Stage = MobileShiWangChangStage.Resolving, Mode = branch },
            ChoiceBindings = Array.AsReadOnly(f.ChoiceBindings.Append(new ProgramChoiceResultBinding("shepan-mode", branch, f.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new MobileShiShepanChosenEvent(f.Id, r.Source, r.GameplayHash, _turnNumber, _roundNumber,
            ShiWangChangName(r.Kind), branch, r.UseId, r.ActionId, r.Actor));
        AdvanceRuntimeProgram(f.Id); AdvanceRulesAndPublishState();
    }

    private void ContinueShiKaiji(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileShiWangChang: { } r } f || f.Id != id ||
            f.SkillId != ShiKaiji || f.InstructionIndex != 1 || r.Stage != MobileShiWangChangStage.DrawChildren) return;
        RequireShiWangChang(f);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        f = GetActiveProgramFrame(id); r = f.MobileShiWangChang!;
        var index = r.Index + (r.Draw is null ? 0 : 1);
        while (index < r.Targets.Count && !_players[r.Targets[index]].IsAlive) index++;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null, MobileShiWangChang = r with { Index = index, Draw = null } });
        if (index >= r.Targets.Count || _winner != Winner.None)
        {
            ReplaceRuntimeTop(f with { MobileShiWangChang = f.MobileShiWangChang! with { Stage = MobileShiWangChangStage.Complete } });
            AdvanceRuntimeProgram(id); return;
        }
        ReplaceRuntimeTop(f = f with { MobileShiWangChang = f.MobileShiWangChang! with { Stage = MobileShiWangChangStage.Drawing } });
        BeginOperationProgramDraw(f, r.Targets[index], 1, new(ShiKaijiDraw));
    }
    [ProgramNativeDrawReturn(SkillProgramEffectOp.SelectMobileShiKaijiTargets)]
    private void ReturnShiKaijiDraw(ProgramSkillFrame f, NativeDrawOutcome outcome)
    {
        if (f.MobileShiWangChang is not { Stage: MobileShiWangChangStage.Drawing, Draw: null } r || outcome.RequestedCount != 1 ||
            r.Index >= r.Targets.Count || outcome.RecipientSeat != r.Targets[r.Index]) throw new InvalidOperationException("Kaiji lost its original once-issued recipient draw.");
        var nonBasic = outcome.ActualDrawIds.Concat(outcome.ReplacementGainedIds).Any(id => GetProgramCardCategory(GetAdvancedCard(id).Kind) != SkillProgramCardCategory.Basic);
        ReplaceRuntimeTop(f = f with { MobileShiWangChang = r with { Stage = MobileShiWangChangStage.DrawChildren, Draw = outcome,
            Before = outcome.SequenceBefore, After = outcome.SequenceAfter, NonBasicGained = r.NonBasicGained || nonBasic }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new MobileShiKaijiDrawReturnedEvent(f.Id, r.Index, outcome.RecipientSeat, outcome.ActualDrawIds.Count,
            outcome.ReplacementGainedIds.Count, nonBasic, outcome.SequenceBefore, outcome.SequenceAfter));
        ContinueShiKaiji(f.Id);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 47400)]
    private bool ResumeShiKaiji(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SkillId: ShiKaiji, InstructionIndex: 1, MobileShiWangChang: { Stage: MobileShiWangChangStage.DrawChildren } } f || f.Id != id) return false;
        ContinueShiKaiji(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 47400)]
    private bool ReturnShiKaijiMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeShiKaiji(f.Id);

    private bool IsMobileShiWangChangChoiceResult(ProgramSkillFrame f, ProgramChoiceResultBinding b) =>
        f.MobileShiWangChang is { } r && r.Source == ShiWangChangSource(f) && r.GameplayHash == f.GameplayHash && b.ChooserSeat == f.OwnerSeat &&
        (f.SkillId == ShiKaiji && f.InstructionIndex >= 2 && b.Name == "kaiji-reward" && b.OptionId == (r.NonBasicGained ? "yes" : "no") &&
            ProgramEventHistory<MobileShiKaijiRewardBoundEvent>().Count(e => e.FrameId == f.Id && e.Qualified == r.NonBasicGained) == 1 ||
         f.SkillId == ShiShepan && f.InstructionIndex >= 1 && b.Name == "shepan-mode" && b.OptionId == r.Mode &&
            ProgramEventHistory<MobileShiShepanChosenEvent>().Count(e => e.FrameId == f.Id && e.Mode == r.Mode && e.UseId == r.UseId && e.ActionId == r.ActionId) == 1);

    [ProgramAiChoice(SkillProgramEffectOp.SelectMobileShiKaijiTargets, SkillProgramEffectOp.ChooseMobileShiShepan)]
    private PromptChoice ChooseShiWangChangAi(PendingDecision decision, ProgramSkillFrame f)
    {
        var r = f.MobileShiWangChang!;
        if (r.Stage == MobileShiWangChangStage.Mode)
        {
            var own = GetHand(_players[f.OwnerSeat]).Count; var actor = GetHand(_players[r.Actor]).Count;
            return decision.Choices.FirstOrDefault(c => c.Parameters["branch"] == (own == actor - 1 ? "top" : "draw")) ?? decision.Choices[0];
        }
        return decision.Choices.FirstOrDefault(c => c.Parameters["branch"] == "pick" &&
            AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets.Single()])) ?? decision.Choices.Single(c => c.Parameters["branch"] == "done");
    }

    private IReadOnlyList<ProgramPublicRuleStateSnapshot> MobileShiWangChangPublicStates(CharacterState owner, string skillId)
    {
        if (skillId is not (ShiKaiji or ShiShepan)) return [];
        var states = new List<ProgramPublicRuleStateSnapshot>();
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances.Where(i => i.SkillId == skillId))
        {
            if (skillId == ShiKaiji) states.Add(new(instance.SkillInstanceId, "kaiji-maximum", ProgramPublicRuleStateKind.GameActivationProgress, ShiKaijiMaximum()));
            else
            {
                states.Add(new(instance.SkillInstanceId, "shepan-adjust", ProgramPublicRuleStateKind.ActivationLimit, 1,
                    _skillRuntimeState.GetUsage(owner.Seat, skillId, "shepan-adjust@" + instance.SkillInstanceId, SkillUsageScope.Turn), SkillUsageScope.Turn));
                states.AddRange(ProgramEventHistory<MobileShiShepanChosenEvent>().Where(e => e.Round == _roundNumber && e.Source.OwnerSeat == owner.Seat &&
                    e.Source.SkillInstanceId == instance.SkillInstanceId).Select(e => e.Name).Distinct()
                    .Select(kind => new ProgramPublicRuleStateSnapshot(instance.SkillInstanceId, "shepan-name-" + kind, ProgramPublicRuleStateKind.ActivationLimit, 1, 1, SkillUsageScope.Round)));
            }
        }
        return Array.AsReadOnly(states.ToArray());
    }
}
