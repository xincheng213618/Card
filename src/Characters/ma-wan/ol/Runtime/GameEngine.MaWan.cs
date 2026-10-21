namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string MaWanDrawReason = "skill-program.ma-wan.hunjiang.draw";
    private bool HasMaWanCapability => _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveHunjiangSimultaneousChoices);
    private CardConversionSource MaWanSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability]
    private sealed class MaWanCapability(GameEngine engine) : IMaWanProgramHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame f, SkillProgramEffectOp op) => op == SkillProgramEffectOp.ResolveHunjiangSimultaneousChoices ? engine.BeginMaWanVotes(f) : engine.BeginMaWanTargets(f); }

    private SkillProgramStepOutcome BeginMaWanVotes(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MaWanHunjiang is not null || f.TriggerId is not null || f.WindowContext is not null || f.InstructionIndex != 1 || f.SkillId != "ol:hunjiang" ||
            f.OwnerSeat != _currentSeat || _phase != TurnPhase.Play || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count < 1 ||
            f.SelectedTargetSeats.Distinct().Count() != f.SelectedTargetSeats.Count ||
            f.SelectedTargetSeats.Any(s => !IsValidPlayerSeat(s) || s == f.OwnerSeat || !_players[s].IsAlive || !IsWithinAttackRange(f.OwnerSeat, s)))
            throw new InvalidOperationException("Hunjiang requires its exact once-per-Play in-range living participants before any private commitment.");
        var r = new MaWanHunjiangReceipt(MaWanSource(f), f.GameplayHash, f.InstructionIndex, _turnNumber, _cardUseDebitPhaseInstanceId, MaWanHunjiangStage.PrivateVotes)
        { Participants = f.SelectedTargetSeats.OrderBy(s => RelativeTableOrder(f.OwnerSeat, s)).ToArray() };
        ReplaceRuntimeTop(f = f with { MaWanHunjiang = r });
        AdvanceEventRulesAndQueueFact(new MaWanHunjiangStartedEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurnNumber, r.PhaseInstanceId, r.Participants.Count));
        for (var i = 0; i < r.Participants.Count; i++) AdvanceEventRulesAndQueueFact(new MaWanHunjiangParticipantFrozenEvent(f.Id, i, r.Participants[i]));
        PublishMaWan(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private int MaWanChooser(ProgramSkillFrame f) => f.MaWanHunjiang!.Stage == MaWanHunjiangStage.PrivateVotes
        ? f.MaWanHunjiang.Participants[f.MaWanHunjiang.Votes.Count] : f.OwnerSeat;
    private IReadOnlyList<PromptChoice> MaWanChoices(ProgramSkillFrame f)
    {
        var r = f.MaWanHunjiang!;
        Dictionary<string, string> Args(string branch) => new() { ["program-action"] = "ma-wan-hunjiang", ["branch"] = branch };
        if (r.Stage == MaWanHunjiangStage.PrivateVotes)
            return Array.AsReadOnly(new[] {
                new PromptChoice(new($"hunjiang.{f.Id}.{MaWanChooser(f)}.extra"), "令其本阶段使用杀可以指定你为额外目标", [], [], Args("extra")),
                new PromptChoice(new($"hunjiang.{f.Id}.{MaWanChooser(f)}.draw"), "令其摸一张牌", [], [], Args("draw")) });
        var use = RequireMaWanUse(f);
        var choices = MaWanExtraTargets(f, use).Select(s => new PromptChoice(new($"hunjiang.{f.Id}.append.{s}"), $"额外指定{_players[s].Name}", [], [s], Args("append"))).ToList();
        choices.Add(new(new($"hunjiang.{f.Id}.finish"), "完成额外目标选择", [], [], Args("finish")));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMaWan(ProgramSkillFrame f)
    {
        var r = f.MaWanHunjiang!; var chooser = MaWanChooser(f); var skill = _contentRegistry.GetSkill(f.SkillId); var choices = MaWanChoices(f);
        ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.AwaitingResponse });
        _pendingDecision = new(DecisionKind.ProgramTrigger, chooser, r.Stage == MaWanHunjiangStage.PrivateVotes ? "浑疆：独立选择一项，全部答复后同时公开结算。" : "浑疆：选择本阶段已获资格的额外目标。",
            [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = r.Stage == MaWanHunjiangStage.PrivateVotes, TargetSeat = chooser,
            Choices = choices, SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[chooser].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    [ProgramAnswer("ma-wan-hunjiang")]
    private void AnswerMaWan(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MaWanHunjiang: { } r } f ||
            _pendingDecision?.PlayerSeat != MaWanChooser(f) || !AssistedChoicesEqual([choice], MaWanChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Hunjiang lost its exact independent chooser or legal extra-target commitment.");
        AssertMaWan(f); ClearPendingDecision(); ReplaceRuntimeTop(f = f with { Step = ResolutionFrameStep.ResolvingEffect });
        var branch = choice.Parameters["branch"];
        if (r.Stage == MaWanHunjiangStage.PrivateVotes)
        {
            if (branch is not ("extra" or "draw")) throw new InvalidOperationException("Invalid Hunjiang private choice.");
            var votes = r.Votes.Append(new MaWanHunjiangVote(MaWanChooser(f), branch == "extra")).ToArray();
            ReplaceRuntimeTop(f = f with { MaWanHunjiang = r with { Votes = votes } });
            if (votes.Length < r.Participants.Count) { PublishMaWan(f); return; }
            ResolveMaWanVotes(f); return;
        }
        if (branch == "append")
        { ReplaceRuntimeTop(f with { MaWanHunjiang = r with { SelectedTargets = r.SelectedTargets.Append(choice.Targets.Single()).ToArray() } }); PublishMaWan(GetActiveProgramFrame(f.Id)); return; }
        if (branch != "finish") throw new InvalidOperationException("Invalid Hunjiang target choice.");
        CompleteMaWanTargets(f);
    }
    private void ResolveMaWanVotes(ProgramSkillFrame f)
    {
        var r = f.MaWanHunjiang!;
        if (r.Votes.Count != r.Participants.Count) throw new InvalidOperationException("Hunjiang cannot reveal or settle an incomplete simultaneous decision.");
        var unanimous = r.Votes.Select(v => v.AllowExtraTarget).Distinct().Count() == 1;
        var draw = unanimous ? r.Participants.Count : r.Votes.Count(v => !v.AllowExtraTarget);
        for (var i = 0; i < r.Votes.Count; i++)
        {
            var vote = r.Votes[i]; AdvanceEventRulesAndQueueFact(new MaWanHunjiangVoteRevealedEvent(f.Id, i, vote.Seat, vote.AllowExtraTarget, unanimous));
            if (vote.AllowExtraTarget || unanimous) AdvanceEventRulesAndQueueFact(new MaWanHunjiangEntitledEvent(f.Id, r.Source, r.GameplayHash, r.ActualTurnNumber, r.PhaseInstanceId, vote.Seat));
        }
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { MaWanHunjiang = r with { Stage = MaWanHunjiangStage.DrawChildren, DrawCount = draw, SequenceBefore = before, SequenceAfter = before } });
        if (draw > 0 && _players[f.OwnerSeat].IsAlive) DrawProgramCards(f.Id, f.OwnerSeat, draw, null, null, SkillProgramCardSetVisibility.Private, new(MaWanDrawReason));
        f = GetActiveProgramFrame(f.Id); ReplaceRuntimeTop(f with { MaWanHunjiang = f.MaWanHunjiang! with { SequenceAfter = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new MaWanHunjiangDrawIssuedEvent(f.Id, f.OwnerSeat, draw, before, _movementSequence));
        ContinueMaWan(f.Id);
    }
    private bool DrainMaWan(long id) => TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.Program) ||
        TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(id, PostEventContinuation.Program) ||
        TryBeginCardsMovedProgramWindow(id) || TryBeginAdvancedSkillsChanged(id);
    private void ContinueMaWan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MaWanHunjiang: { } r } f || f.Id != id) return;
        AssertMaWan(f); if (DrainMaWan(id)) return;
        f = GetActiveProgramFrame(id); r = f.MaWanHunjiang!;
        if (r.Stage == MaWanHunjiangStage.DrawChildren) { FinishMaWan(f); return; }
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive) { FinishMaWan(f); return; }
        PublishMaWan(f);
    }
    private void FinishMaWan(ProgramSkillFrame f) { ReplaceRuntimeTop(f = f with { MaWanHunjiang = null }); FinishProgramSkill(f, completed: true); }
    [ProgramContinuation(ProgramContinuationKind.Resume, 26510)]
    private bool ResumeMaWan(long id) { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MaWanHunjiang: not null } f || f.Id != id) return false; ContinueMaWan(id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 26510)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 26510)]
    private bool ContinuePaidMaWan(ProgramSkillFrame f) => f.MaWanHunjiang is null
        ? f.TriggerId is { } binding && f.InstructionIndex is 0 or 1 && f.WindowContext is { } issued &&
            HasIssuedMaWanHunjiangCandidate(new(f.OwnerSeat, f.SkillId, binding, f.SkillInstanceId, f.GameplayHash, 0), issued)
        : f.MaWanHunjiang is { } r && (r.Stage == MaWanHunjiangStage.AppendTargets
        ? f.WindowContext is { } c && HasIssuedMaWanHunjiangCandidate(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), c)
        : ProgramEventHistory<MaWanHunjiangStartedEvent>().Any(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.ActualTurnNumber == r.ActualTurnNumber && e.PhaseInstanceId == r.PhaseInstanceId));
    [ProgramAiChoice(SkillProgramEffectOp.ResolveHunjiangSimultaneousChoices, SkillProgramEffectOp.OfferHunjiangPhaseSlashTargets)]
    private PromptChoice AiMaWan(PendingDecision p, ProgramSkillFrame f)
    {
        var r = f.MaWanHunjiang!;
        if (r.Stage == MaWanHunjiangStage.PrivateVotes)
        {
            var chooser = _players[p.PlayerSeat];
            var extra = !AreProgramDistributionAllies(chooser, _players[f.OwnerSeat]) && chooser.Hp >= 4;
            return p.Choices.Single(c => c.Parameters["branch"] == (extra ? "extra" : "draw"));
        }
        return p.Choices.FirstOrDefault(c => c.Targets.Count > 0 && !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[0]]))
            ?? p.Choices.Single(c => c.Parameters["branch"] == "finish");
    }
}
