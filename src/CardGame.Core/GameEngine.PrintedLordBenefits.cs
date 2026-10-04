using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool PrintedLordUnissuedSource(ProgramSkillFrame f) => _players[f.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) && _winner == Winner.None;
    private bool ExactPrintedLordStart(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context ||
            f.OwnerSeat != _currentSeat || context.OwnerSeat != f.OwnerSeat || context.SourceSeat != f.OwnerSeat ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not
                { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow, Continuation: ProgramLifecycleContinuation.NormalTurnStart } parent ||
            parent.OwnerSeat != f.OwnerSeat || parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count ||
            !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex])) return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        if (index <= 0 || _resolutionStack[index - 1].Id != parent.Id) return false;
        return GetProgramTrigger(f).Effects is [{ Op: SkillProgramEffectOp.RaiseMaximumRecoverAndQualifyPrintedLord }];
    }
    private SkillProgramStepOutcome BeginPrintedLordBenefit(ProgramSkillFrame input)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.PrintedLordBenefit is not null || f.InstructionIndex != 1 || !ExactPrintedLordStart(f))
            throw new InvalidOperationException("Printed lord qualification requires the exact own actual-start candidate.");
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        ReplaceRuntimeTop(f = f with { PrintedLordBenefit = new(1, RecipientConsequencesSource(f), f.GameplayHash,
            _turnNumber, f.WindowContext!.ParentFrameId, PrintedLordBenefitStage.ChoosingBeneficiary) });
        PublishPrintedLordChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private int[] PrintedLordBeneficiaries(ProgramSkillFrame f) => _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat && p.Gender == GeneralGender.Male)
        .Select(p => p.Seat).ToArray();
    private IReadOnlyList<PromptChoice> PrintedLordChoices(ProgramSkillFrame f) => Array.AsReadOnly(PrintedLordBeneficiaries(f)
        .Select(s => new PromptChoice(new($"printed-lord.{f.Id}.beneficiary-{s}"), $"令 {_players[s].Name} 增加体力上限、回复体力并启用其主公技。", [], [s],
            new Dictionary<string, string> { ["program-action"] = "printed-lord-benefit", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture) })).ToArray());
    private void PublishPrintedLordChoice(ProgramSkillFrame f)
    {
        if (RecipientConsequencesGameEnded()) return;
        var choices = PrintedLordChoices(f);
        if (!PrintedLordUnissuedSource(f) || choices.Count == 0) { CancelProgramBindingAndCleanup(f, "没有仍然合法的拥嫡受益者。"); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "选择一名其他男性角色。", [], choices.SelectMany(c => c.Targets).ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }
    private void ResolvePrintedLordChoice(PromptChoice choice)
    {
        var f = (ProgramSkillFrame)_resolutionStack.Last(); AssertRecipientConsequences(f); var r = f.PrintedLordBenefit!;
        if (r.Stage != PrintedLordBenefitStage.ChoosingBeneficiary || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], [PrintedLordChoices(f).Single(c => c.Id == choice.Id)]))
            throw new InvalidOperationException("Printed lord benefit lost its exact published beneficiary.");
        ClearPendingDecision();
        if (!PrintedLordUnissuedSource(f)) { CancelProgramBindingAndCleanup(f, "拥嫡支付前来源已失效。"); return; }
        var seat = choice.Targets.Single(); var target = _players[seat]; var before = target.MaxHp;
        var after = checked(before + 1);
        ReplaceRuntimeTop(f with { PrintedLordBenefit = r with { Stage = PrintedLordBenefitStage.MaximumPaid,
            BeneficiarySeat = seat, MaximumBefore = before, MaximumAfter = after } });
        target.MaxHp = after;
        AdvanceEventRulesAndQueueFact(new MaximumHpChangedEvent(seat, 1, after, f.SkillId));
        AdvanceEventRulesAndQueueFact(new PrintedLordMaximumPaidEvent(f.Id, seat, before, after));
        AdvanceRuntimeProgram(f.Id);
    }
    private bool ResumePrintedLordBenefit(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.PrintedLordBenefit is not { } r) return false;
        AssertRecipientConsequences(f);
        if (RecipientConsequencesGameEnded()) return true;
        if (r.Stage == PrintedLordBenefitStage.ChoosingBeneficiary)
        { if (_pendingDecision is null) PublishPrintedLordChoice(f); return true; }
        if (DrainRecipientConsequences(f, false)) return true;
        f = GetActiveProgramFrame(id); r = f.PrintedLordBenefit!;
        if (r.Stage == PrintedLordBenefitStage.Complete) { FinishProgramSkill(f, true); return true; }
        if (!PrintedLordUnissuedSource(f) || !_players[r.BeneficiarySeat].IsAlive)
        { CancelProgramBindingAndCleanup(f, "拥嫡已支付的上限、回复和全部孩子保留，未发行的后继取消。"); return true; }
        if (r.Stage == PrintedLordBenefitStage.MaximumPaid)
        {
            var requested = Math.Min(1, Math.Max(0, _players[r.BeneficiarySeat].MaxHp - _players[r.BeneficiarySeat].Hp));
            ReplaceRuntimeTop(f with { PrintedLordBenefit = r with { Stage = PrintedLordBenefitStage.RecoveryPaid, RecoveryRequested = requested } });
            AdvanceEventRulesAndQueueFact(new PrintedLordRecoveryRequestedEvent(id, r.BeneficiarySeat, requested));
            new ProgramSkillHost(this).Recover(id, f.OwnerSeat, r.BeneficiarySeat, requested, null, null);
            AdvanceRuntimeProgram(id); return true;
        }
        if (r.Stage == PrintedLordBenefitStage.RecoveryPaid)
        {
            // “然后” freezes the actual printed grants after recovery children,
            // including a genuine general replacement made by those children.
            r = r with { Stage = PrintedLordBenefitStage.Qualifying, PrintedQualifications = CapturePrintedLordQualifications(f, r.BeneficiarySeat) };
            ReplaceRuntimeTop(f = f with { PrintedLordBenefit = r });
            AdvanceEventRulesAndQueueFact(new PrintedLordQualificationCapturedEvent(id, r.BeneficiarySeat));
            IssueCapturedPrintedLordQualifications(f, r);
            ReplaceRuntimeTop(f = GetActiveProgramFrame(id) with { PrintedLordBenefit = r with { Stage = PrintedLordBenefitStage.Complete } });
            AdvanceRuntimeProgram(id); return true;
        }
        throw new InvalidOperationException("A printed qualification cannot resume a completed benefit twice.");
    }

    private PromptChoice SelectAiRecipientConsequences(PendingDecision p, ProgramSkillFrame f)
    {
        if (f.BlackGiftContest is { Stage: BlackGiftContestStage.ChoosingDiscard })
            return p.Choices.OrderBy(c => GetKeepValue(GetAdvancedCard(c.Cards.Single()), _players[p.PlayerSeat])).ThenBy(c => c.Cards.Single()).First();
        var hint = f.PrintedLordBenefit is not null ? new SkillProgramAiHint(0, 0, 0, 0, 1, 0, false, false) :
            new SkillProgramAiHint(0, 0, 0, 0, 0, 1, false, false);
        // Shared public team/HP/hand-count target scoring; no foreign Hand ids.
        return p.Choices.OrderByDescending(c => _aiBrains[f.OwnerSeat].ScoreProgramTarget(CreateSnapshot(f.OwnerSeat), c.Targets.Single(), hint))
            .ThenBy(c => c.Targets.Single()).First();
    }
}
