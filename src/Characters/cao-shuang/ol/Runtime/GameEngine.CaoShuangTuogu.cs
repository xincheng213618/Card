using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardConversionSource CaoShuangSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability]
    private sealed class CaoShuangProgramCapability(GameEngine e) : ICaoShuangProgramHost
    {
        public SkillProgramStepOutcome InheritDeadGeneralChosenSkill(ProgramSkillFrame f) => e.BeginCaoShuangTuogu(f);
        public SkillProgramStepOutcome PlaceDamagedCardAsDelayedTrick(ProgramSkillFrame f) => e.BeginCaoShuangShanzhuan(f);
    }
    private string[] CaoShuangPrintedInheritanceSkills(int victim) =>
        _players[victim].General.Skills.Concat(_players[victim].SecondaryGeneral?.Skills ?? [])
            .Where(s => s.ContentId is not null && (s.Tags & (SkillTag.Limited | SkillTag.Awakening | SkillTag.Lord)) == 0 &&
                !s.Name.Contains("隐匿", StringComparison.Ordinal) && !s.Description.Contains("隐匿", StringComparison.Ordinal))
            .Select(s => s.ContentId!).Distinct(StringComparer.Ordinal).ToArray();

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 26060)]
    private bool CanOfferCaoShuang(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext w)
    {
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.InheritDeadGeneralChosenSkill))
            return w.Window == SkillProgramTriggerWindow.CharacterDied && w.TargetSeat is { } victim &&
                IsValidPlayerSeat(victim) && victim != c.OwnerSeat && !_players[victim].IsAlive &&
                _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().Any(p => p.Id == w.ParentFrameId && p.VictimSeat == victim) &&
                CaoShuangPrintedInheritanceSkills(victim).Length > 0;
        if (t.Effects.Any(e => e.Op == SkillProgramEffectOp.PlaceDamagedCardAsDelayedTrick))
            return w is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0, TargetSeat: { } target } &&
                w.SourceSeat == c.OwnerSeat && target != c.OwnerSeat && CaoShuangCanPlace(c.OwnerSeat, target);
        return true;
    }

    private SkillProgramStepOutcome BeginCaoShuangTuogu(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.CaoShuangTuogu is not null || f.InstructionIndex != 1 || f.TriggerId is null ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.CharacterDied, TargetSeat: { } victim } context ||
            _resolutionStack.OfType<ProgramKillTriggerWindowFrame>().SingleOrDefault(w => w.Id == context.ParentFrameId) is not { } kill ||
            kill.VictimSeat != victim || kill.CandidateIndex >= kill.Candidates.Count ||
            !MountObserverCandidateMatches(f, kill.Candidates[kill.CandidateIndex]) || kill.Contexts[kill.CandidateIndex] != context ||
            _resolutionStack.OfType<DeathFrame>().SingleOrDefault(d => d.Id == kill.DeathFrameId) is not { KillerProgramsResolved: false } death ||
            death.VictimSeat != victim || _players[victim].IsAlive || victim == f.OwnerSeat)
            throw new InvalidOperationException("Tuogu requires the exact current other-death candidate and deceased general.");
        var ids = CaoShuangPrintedInheritanceSkills(victim);
        if (ids.Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(f = f with { CaoShuangTuogu = new(CaoShuangSource(f), f.GameplayHash, f.InstructionIndex,
            death.Id, kill.Id, victim) { SkillIds = ids }, Step = ResolutionFrameStep.AwaitingResponse });
        PublishCaoShuangTuogu(f);
        return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> CaoShuangTuoguChoices(ProgramSkillFrame f) => Array.AsReadOnly(f.CaoShuangTuogu!.SkillIds
        .Select(id => new PromptChoice(new($"caoshuang-tuogu.{f.Id}.{id}"), $"令 {_players[f.OwnerSeat].Name} 获得【{_contentRegistry.GetSkill(id).Name}】", [], [],
            new Dictionary<string, string> { ["program-action"] = "caoshuang-tuogu", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["chosen-skill"] = id })).ToArray());
    private void PublishCaoShuangTuogu(ProgramSkillFrame f)
    {
        var r = f.CaoShuangTuogu!; var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, r.VictimSeat, "托孤：为曹爽选择你武将牌上的一个技能", [], [], f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = CaoShuangTuoguChoices(f), SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[r.VictimSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("caoshuang-tuogu")]
    private void AnswerCaoShuangTuogu(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { CaoShuangTuogu: { Granted: false } r } f ||
            _pendingDecision?.PlayerSeat != r.VictimSeat || _players[r.VictimSeat].IsAlive ||
            !AssistedChoicesEqual([choice], CaoShuangTuoguChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Only the exact deceased chooser can select its frozen printed Tuogu skill.");
        ClearPendingDecision();
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { ReplaceRuntimeTop(f = f with { CaoShuangTuogu = null }); FinishProgramSkill(f, false); return; }
        var skillId = choice.Parameters["chosen-skill"]; var owner = _players[f.OwnerSeat];
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        var previous = ProgramEventHistory<CaoShuangTuoguInheritedEvent>().LastOrDefault(e => e.OwnerSeat == f.OwnerSeat);
        if (previous is not null) owner.SkillGrants.RemoveGrant(previous.GrantId);
        var existing = GetSkillBindingShard(owner).ActiveGrants.Where(g => g.SkillId == skillId)
            .OrderBy(g => g.SourceId.StartsWith("turn:", StringComparison.Ordinal) ? 1 : 0).ThenBy(g => g.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault();
        var grantId = $"tuogu:{f.OwnerSeat}:{f.Id}:{skillId}";
        var instance = existing?.SkillInstanceId ?? grantId;
        ReplaceRuntimeTop(f = f with { CaoShuangTuogu = r with { Granted = true }, Step = ResolutionFrameStep.ResolvingEffect });
        owner.SkillGrants.Grant(new(grantId, skillId, instance, "acquired:ol:tuogu"));
        RegisterTaggedConversionSkill(owner, skillId);
        AdvanceEventRulesAndQueueFact(new CaoShuangTuoguInheritedEvent(f.Id, r.DeathFrameId, f.OwnerSeat, r.VictimSeat, r.Source,
            skillId, grantId, instance, previous?.GrantId));
        if (existing is null) AdvanceEventRulesAndQueueFact(new SkillsAcquiredEvent(f.OwnerSeat, f.SkillId, Array.AsReadOnly(new[] { skillId })));
        // Settle the same registered ownership/source-loss rules as normal rule
        // publication while this paid instruction still owns cleanup movements.
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { CaoShuangTuogu = f.CaoShuangTuogu! with { SequenceBefore = before, SequenceAfter = before } });
        AdvanceRulesAndPublishState();
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with { CaoShuangTuogu = f.CaoShuangTuogu! with { SequenceAfter = _movementSequence } });
        ContinueCaoShuangProgram(f.Id);
    }
    [ProgramAiChoice(SkillProgramEffectOp.InheritDeadGeneralChosenSkill)]
    private PromptChoice ChooseAiCaoShuangTuogu(PendingDecision p, ProgramSkillFrame f) => p.Choices.First();
}
