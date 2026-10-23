namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MuShunAction = "anniversary-mu-shun";
    private static CardConversionSource MuShunSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private int MuShunJin(int seat) => _players[seat].Markers.GetValueOrDefault(PlayerMarkerKind.MuShunJin);
    private bool HasMuShunCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryMuShunJinjian);
    private bool HasMuShunShizhaoCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnniversaryMuShunShizhao);
    [ProgramCapability] private sealed class AnniversaryMuShunHost(GameEngine engine) : IAnniversaryMuShunHost
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame, SkillProgramEffectOp operation) => operation == SkillProgramEffectOp.ResolveAnniversaryMuShunJinjian
            ? engine.BeginMuShunJinjian(frame) : engine.ResolveMuShunShizhao(frame);
    }
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 45300)]
    private bool QualifyMuShun(ProgramTriggerLegalityContext g)
    {
        if (g.SkillId == MuShunComposition.Jinjian)
            return g.WindowContext is { Window: SkillProgramTriggerWindow.AfterDamageApplied, Amount: > 0 } c &&
                (c.SourceSeat == g.OwnerSeat || c.TargetSeat == g.OwnerSeat);
        return g.SkillId != MuShunComposition.Shizhao || g.WindowContext is { } context && MuShunLossCandidate(g.Candidate, context, false);
    }
    private void SetMuShunJin(ProgramSkillFrame f, int count)
    {
        var owner = _players[f.OwnerSeat]; var before = MuShunJin(f.OwnerSeat);
        if (count < 0) throw new InvalidOperationException("Jin cannot be negative.");
        var key = (PlayerMarkerKind.MuShunJin, f.OwnerSeat);
        if (count == 0) { owner.Markers.Remove(PlayerMarkerKind.MuShunJin); owner.MarkerSourceCounts.Remove(key); }
        else { owner.Markers[PlayerMarkerKind.MuShunJin] = count; owner.MarkerSourceCounts[key] = count; }
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(f.Id, f.OwnerSeat, PlayerMarkerKind.MuShunJin, count - before, count, f.OwnerSeat, "skill-program." + f.SkillId + ".jin"));
    }
    private SkillProgramStepOutcome BeginMuShunJinjian(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MuShunJinjian is not null || f.InstructionIndex != 1 || f.SkillId != MuShunComposition.Jinjian || f.TriggerId != MuShunComposition.Damage ||
            f.ActivationId != f.TriggerId || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ExactOrderedPrintedSkillLossParent(f, out var window, out var damage, out var attack))
            throw new InvalidOperationException("Jinjian requires its exact original positive damage participant, candidate and binding.");
        var before = MuShunJin(f.OwnerSeat); var after = checked(before + 1);
        var r = new MuShunJinjianReceipt(MuShunSource(f), f.GameplayHash, window.Id, damage.Id, _turnNumber, attack.IsSourceLess ? null : attack.SourceSeat, before, after);
        ReplaceRuntimeTop(f = f with { MuShunJinjian = r });
        AdvanceEventRulesAndQueueFact(new MuShunJinjianIssuedEvent(f.Id, r.Source, r.Hash, r.WindowId, r.DamageFrameId, r.ActualTurn, r.DamageSourceSeat, before, after));
        SetMuShunJin(f, after);
        if (!CanMuShunContest(f)) { CloseMuShunWithoutContest(f); return SkillProgramStepOutcome.AwaitChild; }
        PublishMuShun(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private bool CanMuShunContest(ProgramSkillFrame f) => f.MuShunJinjian?.DamageSourceSeat is { } source && IsValidPlayerSeat(source) && source != f.OwnerSeat &&
        _winner == Winner.None && _players[f.OwnerSeat].IsAlive && _players[source].IsAlive &&
        HasWuLuKangPindianInput(_players[f.OwnerSeat]) && HasWuLuKangPindianInput(_players[source]) && CanBePindianTarget(f.OwnerSeat, source);
    private IReadOnlyList<PromptChoice> MuShunChoices(ProgramSkillFrame f)
    {
        var list = new List<PromptChoice>();
        if (CanMuShunContest(f)) list.Add(new(new($"mu-shun.{f.Id}.contest"), "与本次伤害来源拼点", [], [], new Dictionary<string, string> { ["program-action"] = MuShunAction, ["mode"] = "contest" }));
        list.Add(new(new($"mu-shun.{f.Id}.decline"), "保留已获得的劲，不拼点", [], [], new Dictionary<string, string> { ["program-action"] = MuShunAction, ["mode"] = "decline" }));
        return Array.AsReadOnly(list.ToArray());
    }
    private void PublishMuShun(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        PublishParticipantHandChoice(f, f.OwnerSeat, MuShunChoices(f), "劲坚：已获得1个劲，你可以与原伤害来源拼点。", f.MuShunJinjian!.DamageSourceSeat!.Value);
        AdvanceRulesAndPublishState();
    }
    private void CloseMuShunWithoutContest(ProgramSkillFrame f)
    {
        ReplaceRuntimeTop(f = f with { MuShunJinjian = f.MuShunJinjian! with { Stage = MuShunContestStage.Declined } });
        AdvanceEventRulesAndQueueFact(new MuShunContestClosedEvent(f.Id, 0, false, false));
        FinishProgramSkill(f, true);
    }
    [ProgramAnswer(MuShunAction)] private void AnswerMuShun(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MuShunJinjian.Stage: MuShunContestStage.Choice } f || !ValidMuShunJinjian(f) ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual([choice], MuShunChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Jinjian lost its exact original damage-source choice.");
        ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        if (choice.Parameters["mode"] == "decline" || !CanMuShunContest(f)) { CloseMuShunWithoutContest(f); return; }
        var opponent = f.MuShunJinjian!.DamageSourceSeat!.Value; var contestId = _resolutionSequence + 1;
        ReplaceRuntimeTop(f = f with { MuShunJinjian = f.MuShunJinjian with { Stage = MuShunContestStage.Contest, ContestId = contestId, MovementBefore = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MuShunContestIssuedEvent(f.Id, contestId, f.OwnerSeat, opponent, _movementSequence));
        var skill = _contentRegistry.GetSkill(f.SkillId);
        BeginSharedPindian(f.Id, new(f.SkillId, skill.Name, "劲坚 · 拼点", skill.Description), f.OwnerSeat, opponent,
            programResultBind: MuShunComposition.Result, programResultVisibility: SkillProgramCardSetVisibility.Public);
        AdvanceRulesAndPublishState();
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 45300)] private bool ResumeMuShunContest(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MuShunJinjian.Stage: MuShunContestStage.Contest or MuShunContestStage.ContestChildren } f || f.Id != id) return false;
        if (!ValidMuShunJinjian(f) || f.PindianResultBindings.Where(b => b.Name == MuShunComposition.Result).ToArray() is not [var result])
            throw new InvalidOperationException("Jinjian lost the original native pair Pindian return.");
        if (f.MuShunJinjian!.Stage == MuShunContestStage.Contest)
        {
            ReplaceRuntimeTop(f = f with { MuShunJinjian = f.MuShunJinjian with { Stage = MuShunContestStage.ContestChildren, MovementAfter = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new MuShunContestReturnedEvent(f.Id, f.MuShunJinjian.ContestId, f.MuShunJinjian.MovementBefore, f.MuShunJinjian.MovementAfter));
        }
        if (TryBeginCardsMovedProgramWindow(f.Id)) return true;
        f = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(f = f with { MuShunJinjian = f.MuShunJinjian! with { Stage = MuShunContestStage.Returned } });
        AdvanceEventRulesAndQueueFact(new MuShunContestClosedEvent(f.Id, f.MuShunJinjian!.ContestId, true, result.SourceWon));
        AdvanceRuntimeProgram(f.Id); return true;
    }
    [ProgramAiChoice(SkillProgramEffectOp.ResolveAnniversaryMuShunJinjian)] private PromptChoice MuShunAi(PendingDecision p, ProgramSkillFrame f) =>
        p.Choices.FirstOrDefault(c => c.Parameters.GetValueOrDefault("mode") == "contest") ?? p.Choices[0];
    [ProgramDistanceRule(ProgramDistanceStage.AttackRangeContributions, 45300)] private void MuShunRange(CharacterState actor, List<RuleQueryContribution> contributions)
    {
        if (!HasMuShunCapability || !HasRuntimeSkill(actor, MuShunComposition.Jinjian)) return;
        var count = MuShunJin(actor.Seat); if (count > 0) contributions.Add(new FiniteRuleQueryContribution("anniversary:jinjian:" + actor.Seat, SkillRuleOperation.Add, count));
    }
    private SkillProgramStepOutcome ResolveMuShunShizhao(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.SkillId != MuShunComposition.Shizhao || f.TriggerId != MuShunComposition.Loss || f.ActivationId != f.TriggerId ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.WindowContext is not { } c || !MuShunLossCandidate(MuShunCandidate(f), c, false))
            throw new InvalidOperationException("Shizhao requires its first original outside-turn last-Hand movement.");
        var before = MuShunJin(f.OwnerSeat); var after = before > 0 ? before - 1 : 0; var batch = c.MovementBatch!;
        AdvanceEventRulesAndQueueFact(new MuShunShizhaoIssuedEvent(f.Id, MuShunSource(f), f.GameplayHash, c.ParentFrameId, batch.Id, batch.TurnNumber,
            batch.MovementTiming!.ActualTurnOwnerSeat, before, after, before > 0));
        if (before > 0) { SetMuShunJin(f, after); return SkillProgramStepOutcome.Continue; }
        FinishProgramSkill(f, true); return SkillProgramStepOutcome.AwaitChild;
    }
}
