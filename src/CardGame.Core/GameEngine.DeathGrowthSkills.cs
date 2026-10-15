using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string DeathGrowthDrawReason = "skill-program.other-death-growth.draw";
    private CardConversionSource DeathGrowthSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private bool CanOfferDeathGrowthSkill(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (trigger.Effects is not [{ Op: SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill }]) return true;
        if (context.Window != SkillProgramTriggerWindow.CharacterDied || context.TargetSeat is not { } victim ||
            candidate.OwnerSeat == victim || !IsValidPlayerSeat(victim) || _players[victim].IsAlive ||
            _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } window ||
            window.VictimSeat != victim || window.CandidateIndex < 0 || window.CandidateIndex >= window.Candidates.Count ||
            window.Candidates[window.CandidateIndex] != candidate || window.Contexts[window.CandidateIndex] != context)
            return false;
        return _resolutionStack.OfType<DeathFrame>().SingleOrDefault(d => d.Id == window.DeathFrameId) is { } death &&
            death.VictimSeat == victim && !death.KillerProgramsResolved;
    }
    private SkillProgramStepOutcome BeginDeathGrowthSkill(ProgramSkillFrame input, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(input.Id);
        if (f.DeathGrowthSkill is not null || f.WindowContext is not { } c ||
            !CanOfferDeathGrowthSkill(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority), GetProgramTrigger(f), c))
            throw new InvalidOperationException("Death growth lost its exact other-death candidate before the draw.");
        var window = _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().Single(w => w.Id == c.ParentFrameId);
        var death = _resolutionStack.OfType<DeathFrame>().Single(d => d.Id == window.DeathFrameId);
        var dying = death.ReturnKind == DeathReturnKind.Dying
            ? _resolutionStack.OfType<DyingFrame>().Single(d => d.Id == death.ParentFrameId) : null;
        var damage = dying?.Continuation == DyingContinuationKind.Damage
            ? _resolutionStack.OfType<DamageFrame>().Single(d => d.Id == dying.ParentFrameId) : null;
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { DeathGrowthSkill = new(f.InstructionIndex, window.Id, window.DeathFrameId,
            window.VictimSeat, DeathGrowthSource(f), f.GameplayHash, Array.AsReadOnly(effect.SkillIds.ToArray()), effect.Amount,
            before, before, 0, OriginalDeathHash: DeathGrowthCursorHash(death), OriginalDyingFrameId: dying?.Id,
            OriginalDyingHash: dying is null ? null : DeathGrowthCursorHash(dying),
            OriginalDamageHash: damage is null ? null : DeathGrowthCursorHash(damage)), PendingMovementContinuation = new(f.OwnerSeat, 0, null), Step = ResolutionFrameStep.ResolvingEffect });
        var drawn = DrawCards(_players[f.OwnerSeat], effect.Amount, true, new(DeathGrowthDrawReason));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { DeathGrowthSkill = f.DeathGrowthSkill! with { MovementAfter = _movementSequence, DrawCount = drawn.Count } });
        AdvanceEventRulesAndQueueFact(new DeathGrowthDrawIssuedEvent(f.Id, f.DeathGrowthSkill!));
        if (!DrainDeathGrowthChildren(f)) AdvanceDeathGrowthChoice(f);
        return _resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is not null
            ? SkillProgramStepOutcome.AwaitChoice : SkillProgramStepOutcome.AwaitChild;
    }
    private bool DrainDeathGrowthChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(f.Id);
    private void AdvanceDeathGrowthChoice(ProgramSkillFrame f)
    {
        RequireDeathGrowthSkill(f);
        if (!_players[f.OwnerSeat].IsAlive) { FinishDeathGrowthSkill(f); return; }
        ReplaceRuntimeTop(f = f with { DeathGrowthSkill = f.DeathGrowthSkill! with { Stage = DeathGrowthSkillStage.ChooseSkill }, PendingMovementContinuation = null,
            Step = ResolutionFrameStep.AwaitingResponse });
        var choices = DeathGrowthChoices(f);
        if (choices.Count == 0) { FinishDeathGrowthSkill(f); return; }
        var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, $"【{skill.Name}】请选择获得一个尚未拥有的技能。", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private IReadOnlyList<PromptChoice> DeathGrowthChoices(ProgramSkillFrame f) => Array.AsReadOnly(f.DeathGrowthSkill!.SkillIds
        .Where(id => !_players[f.OwnerSeat].SkillGrants.Grants.Any(g => g.IsEnabled && g.SkillId == id))
        .Select(id => new PromptChoice(new($"death-growth.{f.Id}.{id}"), $"获得【{_contentRegistry.GetSkill(id).Name}】", [], [],
            new Dictionary<string, string> { ["program-action"] = "death-growth-skill", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["skill-id"] = id }))
        .ToArray());
    [ProgramAnswer("death-growth-skill")]
    private void ResolveDeathGrowthChoice(PromptChoice selected)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A death-growth choice lost its owning frame.");
        RequireDeathGrowthSkill(f); var r = f.DeathGrowthSkill!;
        if (r.Stage != DeathGrowthSkillStage.ChooseSkill || _pendingDecision?.PlayerSeat != f.OwnerSeat || !_players[f.OwnerSeat].IsAlive ||
            !AssistedChoicesEqual([selected], DeathGrowthChoices(f).Where(c => c.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("The selected death-growth skill is stale, already owned or belongs to another chooser.");
        ClearPendingDecision(); var id = selected.Parameters["skill-id"];
        GrantProgramSkills(f, [id]);
        AdvanceEventRulesAndQueueFact(new DeathGrowthSkillGrantedEvent(f.Id, r.DeathFrameId, f.OwnerSeat, r.VictimSeat, r.Source, id));
        FinishDeathGrowthSkill(f);
    }
    private void FinishDeathGrowthSkill(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { DeathGrowthSkill = null, PendingMovementContinuation = null }); FinishProgramSkill(f, true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 21400)]
    private bool ResumeDeathGrowthSkill(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { DeathGrowthSkill.Stage: DeathGrowthSkillStage.DrawChildren } f || f.Id != id) return false;
        RequireDeathGrowthSkill(f); if (!DrainDeathGrowthChildren(f)) AdvanceDeathGrowthChoice(f); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 21400)]
    private bool ReturnDeathGrowthMovement(ProgramSkillFrame f)
    {
        if (f.DeathGrowthSkill?.Stage != DeathGrowthSkillStage.DrawChildren) return false;
        RequireDeathGrowthSkill(f); if (!DrainDeathGrowthChildren(f)) AdvanceDeathGrowthChoice(f); return true;
    }
    private bool CanContinueDeathGrowthSkill(ProgramSkillFrame f) => f.DeathGrowthSkill is not null && ValidDeathGrowthSkill(f);
    private bool IsDeathGrowthMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.DeathGrowthSkill is { Stage: DeathGrowthSkillStage.DrawChildren } r && r.InstructionIndex == f.InstructionIndex &&
        effect?.Op == SkillProgramEffectOp.DrawAfterOtherDeathThenChooseMissingSkill && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private sealed partial class ProgramSkillHost : IDeathGrowthSkillHost
    { public SkillProgramStepOutcome DrawAfterOtherDeathThenChooseMissingSkill(ProgramSkillFrame f, SkillProgramEffect e) => engine.BeginDeathGrowthSkill(f, e); }
}
