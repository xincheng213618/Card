using System.Globalization;
namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MobileSimaJilue = "mobile-god:jilue", MobileSimaLianpo = "mobile-god:lianpo";
    private static readonly string[] MobileSimaJilueSkills = ["classic:guicai", "classic:fangzhu", "classic:jizhi", "classic:zhiheng", "classic:wansha"];
    private static CardConversionSource MobileSimaSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private int MobileSimaAcquisitionOrdinal(int owner) => ProgramEventHistory<SkillsAcquiredEvent>().Count(e => e.PlayerSeat == owner && e.SkillIds.Contains(MobileSimaJilue));
    private bool MobileSimaNeedsInitialSkill(int owner, string instance) => !ProgramEventHistory<MobileSimaJilueInitializedEvent>().Any(e => e.OwnerSeat == owner &&
        e.SkillInstanceId == instance && e.AcquisitionOrdinal == MobileSimaAcquisitionOrdinal(owner));
    private string[] MobileSimaMissingSkills(int owner) => MobileSimaJilueSkills.Where(id => !_players[owner].SkillGrants.Grants.Any(g => g.SkillId == id)).ToArray();
    private int MobileSimaLearningCost(int owner) => Math.Max(2, ProgramEventHistory<MobileSimaJilueChoiceEvent>().Count(e => e.Source.OwnerSeat == owner &&
        e.Source.SkillId == MobileSimaJilue && e.Choice.StartsWith("learn:", StringComparison.Ordinal)) + 1);
    private bool MobileSimaCanExtraTurn(int owner) => !ProgramEventHistory<MobileSimaJilueChoiceEvent>().Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == MobileSimaLianpo &&
        e.Choice == "extra-turn" && e.ActualTurn == _turnNumber);
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 40425)]
    private bool AllowsMobileSimaTrigger(ProgramTriggerLegalityContext g)
    {
        if (g.Features.HasOperation(SkillProgramEffectOp.InitializeMobileShenSimaYiJilue))
            return MobileSimaNeedsInitialSkill(g.OwnerSeat, g.Candidate.SkillInstanceId) &&
                (g.Trigger.Window != SkillProgramTriggerWindow.SkillsChanged || MobileSimaAcquisitionOrdinal(g.OwnerSeat) > 0);
        return !g.Features.HasOperation(SkillProgramEffectOp.ResolveMobileShenSimaYiLianpo) || MobileSimaCanExtraTurn(g.OwnerSeat) ||
            HasRuntimeSkill(g.Owner, MobileSimaJilue) && MobileSimaMissingSkills(g.OwnerSeat).Length > 0;
    }
    [ProgramCapability] private sealed class MobileSimaYiHost(GameEngine e) : IMobileSimaYiHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame f, SkillProgramEffectOp op) => e.BeginMobileSimaYi(f, op); }
    private SkillProgramStepOutcome BeginMobileSimaYi(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MobileSimaYi is not null || f.InstructionIndex != 1 || f.TriggerId is null)
            throw new InvalidOperationException("Mobile Sima Yi requires his original accepted trigger instruction.");
        var r = new MobileSimaYiReceipt(MobileSimaSource(f), f.GameplayHash, f.InstructionIndex, op, MobileSimaLearningCost(f.OwnerSeat))
            { Skills = MobileSimaMissingSkills(f.OwnerSeat) };
        ReplaceRuntimeTop(f = f with { MobileSimaYi = r });
        if (op == SkillProgramEffectOp.InitializeMobileShenSimaYiJilue)
        {
            var faction = GetEffectiveFactionId(_players[f.OwnerSeat]);
            var bonus = faction switch { "wei" => "classic:fangzhu", "shu" => "classic:jizhi", "wu" => "classic:zhiheng", "qun" => "classic:wansha", _ => null };
            AdvanceEventRulesAndQueueFact(new MobileSimaJilueInitializedEvent(f.Id, f.OwnerSeat, f.SkillInstanceId, MobileSimaAcquisitionOrdinal(f.OwnerSeat), faction));
            ReplaceRuntimeTop(f = f with { MobileSimaYi = r with { Stage = MobileSimaYiStage.Children, Choice = "initialize" } });
            GrantProgramSkills(f, bonus is null ? ["classic:guicai"] : ["classic:guicai", bonus]);
            ContinueMobileSimaYi(f.Id); return SkillProgramStepOutcome.AwaitChild;
        }
        PublishMobileSimaYi(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MobileSimaYiChoices(ProgramSkillFrame f)
    {
        var r = f.MobileSimaYi!; var owner = _players[f.OwnerSeat];
        PromptChoice Choice(string key, string text) => new(new("mobile-sima." + f.Id + "." + key), text, [], [],
            new Dictionary<string, string> { ["program-action"] = "mobile-sima", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["option"] = key });
        var result = new List<PromptChoice>();
        if (r.Stage != MobileSimaYiStage.Choice) return result;
        var ren = owner.Markers.GetValueOrDefault(PlayerMarkerKind.Ren);
        var learning = r.Operation == SkillProgramEffectOp.ResolveMobileShenSimaYiJilue;
        if (learning ? ren >= r.Cost : HasRuntimeSkill(owner, MobileSimaJilue))
            foreach (var skill in r.Skills.Where(id => MobileSimaMissingSkills(f.OwnerSeat).Contains(id)))
                result.Add(Choice("learn:" + skill, (learning ? "移去" + r.Cost + "枚忍，" : "") + "获得【" + _contentRegistry.GetSkill(skill).Name + "】"));
        if (learning)
            for (var i = Math.Min(2, ren); i >= 0; i--) result.Add(Choice("draw:" + i, "移去" + i + "枚忍，摸" + i + "张牌"));
        else if (MobileSimaCanExtraTurn(f.OwnerSeat)) result.Add(Choice("extra-turn", "此回合结束后执行一个额外回合"));
        return result;
    }
    private void PublishMobileSimaYi(ProgramSkillFrame f)
    {
        var skill = _contentRegistry.GetSkill(f.SkillId); var choices = MobileSimaYiChoices(f);
        if (choices.Count == 0 || !_players[f.OwnerSeat].IsAlive || _winner != Winner.None) { FinishMobileSimaYi(f); return; }
        ReplaceRuntimeTop(f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, skill.Name + "：请选择。", [], [], f.OwnerSeat)
            { PromptId = CreatePromptId(), Choices = choices, IsPrivate = true, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running; AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mobile-sima")] private void AnswerMobileSimaYi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileSimaYi: { Stage: MobileSimaYiStage.Choice } r } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !MobileSimaYiChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Mobile Sima Yi requires his exact current choice and unpaid original instruction.");
        AssertMobileSimaYi(f); ClearPendingDecision(); var key = choice.Parameters["option"];
        var cost = key.StartsWith("draw:", StringComparison.Ordinal) ? int.Parse(key[5..], CultureInfo.InvariantCulture) :
            key.StartsWith("learn:", StringComparison.Ordinal) && r.Operation == SkillProgramEffectOp.ResolveMobileShenSimaYiJilue ? r.Cost : 0;
        if (cost > 0) PayProgramMarkerCost(_players[f.OwnerSeat], new(PlayerMarkerKind.Ren, cost), f.SkillId, GetProgramBindingId(f));
        var draw = key.StartsWith("draw:", StringComparison.Ordinal) ? cost : 0;
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect, MobileSimaYi = r with { Choice = key, DrawCount = draw,
            Stage = draw > 0 ? MobileSimaYiStage.Drawing : MobileSimaYiStage.Children } });
        AdvanceEventRulesAndQueueFact(new MobileSimaJilueChoiceEvent(f.Id, r.Source, key, cost, _turnNumber));
        if (key.StartsWith("learn:", StringComparison.Ordinal)) GrantProgramSkills(f, [key[6..]]);
        else if (key == "extra-turn") PendProgramExtraTurn(f, f.OwnerSeat);
        else if (draw > 0) { BeginOperationProgramDraw(f, f.OwnerSeat, draw, new("skill.mobile-god.jilue.draw")); return; }
        ContinueMobileSimaYi(f.Id);
    }
    private void ContinueMobileSimaYi(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileSimaYi: { } r } f || f.Id != id || r.Stage == MobileSimaYiStage.Drawing) return;
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) || TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) || TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id)) return;
        f = GetActiveProgramFrame(id); if (r.Stage == MobileSimaYiStage.Choice) PublishMobileSimaYi(f); else FinishMobileSimaYi(f);
    }
    private void FinishMobileSimaYi(ProgramSkillFrame f) { ReplaceRuntimeTop(f with { MobileSimaYi = null }); AdvanceRuntimeProgram(f.Id); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 40425)] private bool ResumeMobileSimaYi(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MobileSimaYi: not null } f || f.Id != id) return false; ContinueMobileSimaYi(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 40425)] private bool ReturnMobileSimaYiMovement(ProgramSkillFrame f) => f.MobileSimaYi is not null && ResumeMobileSimaYi(f.Id);
    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveMobileShenSimaYiJilue)] private void ReturnMobileSimaYiDraw(ProgramSkillFrame f, NativeDrawOutcome o)
    {
        if (f.MobileSimaYi is not { Stage: MobileSimaYiStage.Drawing } r || r.DrawCount != o.RequestedCount || o.RecipientSeat != f.OwnerSeat ||
            ProgramEventHistory<MobileSimaJilueDrawReturnedEvent>().Any(e => e.FrameId == f.Id)) throw new InvalidOperationException("Jilue requires its exact once-paid native draw return.");
        ReplaceRuntimeTop(f with { MobileSimaYi = r with { Stage = MobileSimaYiStage.Children, DrawOutcome = o } });
        AdvanceEventRulesAndQueueFact(new MobileSimaJilueDrawReturnedEvent(f.Id, f.OwnerSeat, o.RequestedCount, o.ActualDrawIds.Count, o.SequenceBefore, o.SequenceAfter));
        ResumeMobileSimaYi(f.Id);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 40425)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 40425)]
    private bool RetainMobileSimaYi(ProgramSkillFrame f) => f.MobileSimaYi is not null;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 40425)] private void AssertMobileSimaYi(ProgramSkillFrame f)
    {
        if (f.MobileSimaYi is not { } r) return;
        if (r.Source != MobileSimaSource(f) || r.GameplayHash != f.GameplayHash || r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 ||
            !Enum.IsDefined(r.Stage) || r.Cost < 2 || r.DrawCount is < 0 or > 2 || r.Skills.Distinct().Count() != r.Skills.Count ||
            r.Skills.Any(id => !MobileSimaJilueSkills.Contains(id))) throw new InvalidOperationException("Mobile Sima Yi lost his exact original trigger and frozen learning request.");
        if (r.Stage == MobileSimaYiStage.Choice)
        {
            if (r.Choice is not null || ProgramEventHistory<MobileSimaJilueChoiceEvent>().Any(e => e.FrameId == f.Id) || r.Cost != MobileSimaLearningCost(f.OwnerSeat))
                throw new InvalidOperationException("An unpaid Jilue selection cannot contain a previously paid result.");
        }
        else if (r.Choice == "initialize")
        {
            if (ProgramEventHistory<MobileSimaJilueInitializedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillInstanceId == f.SkillInstanceId) != 1)
                throw new InvalidOperationException("Jilue lost its actual skill-acquisition result.");
        }
        else if (ProgramEventHistory<MobileSimaJilueChoiceEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Choice == r.Choice &&
            e.Paid == (r.Choice!.StartsWith("draw:", StringComparison.Ordinal) ? r.DrawCount : r.Choice.StartsWith("learn:", StringComparison.Ordinal) && r.Operation == SkillProgramEffectOp.ResolveMobileShenSimaYiJilue ? r.Cost : 0)) != 1)
            throw new InvalidOperationException("Jilue or Lianpo lost its once-paid actual selected result.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.InitializeMobileShenSimaYiJilue)] private PromptChoice ChooseMobileSimaInitial(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShenSimaYiJilue)] private PromptChoice ChooseMobileSimaJilue(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
    [ProgramAiChoice(SkillProgramEffectOp.ResolveMobileShenSimaYiLianpo)] private PromptChoice ChooseMobileSimaLianpo(PendingDecision p, ProgramSkillFrame f) => p.Choices.Last();
}
