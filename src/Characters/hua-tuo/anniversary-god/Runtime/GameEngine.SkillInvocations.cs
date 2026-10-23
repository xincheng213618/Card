namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ShenHtJingyu = "anniversary-god:jingyu", ShenHtLuxin = "anniversary-god:luxin", ShenHtHuandao = "anniversary-god:huandao";
    private bool HasSkillInvocationObservers => _players.Any(p => p.IsAlive && HasRuntimeSkill(p, ShenHtJingyu)) ||
        HasKemingInvocationObservers ||
        ProgramEventHistory<ShenHuaTuoLuxinArmedEvent>().Any(a => !ProgramEventHistory<ShenHuaTuoLuxinConsumedEvent>().Any(c => c.LuxinFrameId == a.FrameId));
    private static string InvocationSourceKey(int actor, string skill, string occurrence) => actor + "|" + skill + "|" + occurrence;
    private static string SkillInvocationSourceFingerprint(ResolutionFrame frame) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            System.Text.Json.JsonSerializer.Serialize(frame, frame.GetType()))));

    private bool TryBeginProgramSkillInvocation(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != id || f.InstructionIndex != 0 ||
            f.TriggerId is not null && !GetProgramTrigger(f).IsSkillInvocation) return false;
        return TryBeginSkillInvocation(id, f.ActivatingSeat ?? f.OwnerSeat, f.SkillId, "program:" + GetProgramBindingId(f), f.SkillInstanceId);
    }
    private bool HasConvertedSkillInvocations(CardActionContext action, SkillProgramTriggerWindow window) =>
        HasSkillInvocationObservers && window is SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardResponseAccepted &&
        (action.ConversionChain.Any(IsAcceptedViewAsInvocation) || SkillInvocationEquipmentSources(action).Count > 0 ||
            HasKemingCompletedCardEffect(action, window));
    private bool IsAcceptedViewAsInvocation(CardConversionSource source) => _contentRegistry.Skills.GetValueOrDefault(source.SkillId)?.Program?
        .ViewAs.Any(v => v.Id == source.BindingId) == true;
    private IReadOnlyList<(string Skill, string Occurrence, int Actor)> SkillInvocationEquipmentSources(CardActionContext action)
    {
        var uses = _resolutionStack.OfType<CardUseFrame>().Where(u => u.Action?.ActionId == action.ActionId ||
            action.ParentActionId is { } parentId && u.Action?.ActionId == parentId).Select(u => u.Id).ToHashSet();
        var materials = action.PhysicalCards.Select(c => c.CardId).ToArray(); var result = new List<(string, string, int)>();
        if (ProgramEventHistory<ZhangbaSerpentSpearConvertedEvent>().LastOrDefault(e => uses.Contains(e.ResolutionId) &&
                (e.UserSeat == action.ActorSeat || e.UserSeat == action.ProviderSeat) && e.IsUse == (action.Type == CardActionType.Use) &&
                e.PhysicalCardIds.SequenceEqual(materials)) is { } spear)
            result.Add((CardCatalog.Get(CardKind.ZhangbaSerpentSpear).DisplayName, "zhangba:" + action.ActionId, spear.UserSeat));
        if (action.Type == CardActionType.Use && ProgramEventHistory<FangtianHalberdUsedEvent>().Any(e => uses.Contains(e.ResolutionId) &&
                e.SourceSeat == action.ActorSeat && materials.Contains(e.SlashCardId) && e.EffectiveSlashKind == action.EffectiveKind))
            result.Add((CardCatalog.Get(CardKind.FangtianHalberd).DisplayName, "fangtian:" + action.ActionId, action.ActorSeat));
        if (action.Type == CardActionType.Use && action.EffectiveKind == CardKind.FireSlash &&
            ProgramEventHistory<ZhuqueFanConvertedEvent>().Any(e => uses.Contains(e.ResolutionId) && e.SourceSeat == action.ActorSeat &&
                e.PhysicalCardIds.SequenceEqual(materials)))
            result.Add((CardCatalog.Get(CardKind.ZhuqueFan).DisplayName, "zhuque:" + action.ActionId, action.ActorSeat));
        return result;
    }
    private bool TryBeginConvertedSkillInvocations(ProgramCardTriggerWindowFrame f)
    {
        if (!HasConvertedSkillInvocations(f.Action, GetCardActionWindow(f))) return false;
        for (var i = 0; i < f.Action.ConversionChain.Count; i++)
        {
            var s = f.Action.ConversionChain[i];
            if (!IsAcceptedViewAsInvocation(s)) continue;
            if (TryBeginSkillInvocation(f.Id, s.OwnerSeat, s.SkillId, "conversion:" + f.Action.ActionId + ":" + i, s.SkillInstanceId)) return true;
            f = (ProgramCardTriggerWindowFrame)_resolutionStack[^1];
        }
        foreach (var source in SkillInvocationEquipmentSources(f.Action))
        {
            if (TryBeginSkillInvocation(f.Id, source.Actor, source.Skill, source.Occurrence)) return true;
            f = (ProgramCardTriggerWindowFrame)_resolutionStack[^1];
        }
        return false;
    }
    // Call only after a genuine activation has been accepted. The source's frozen
    // occurrence key survives retries; an unrelated activation needs a new key.
    private bool TryBeginSkillInvocation(long parentFrameId, int actorSeat, string skillKey, string occurrenceKey,
        string? skillInstanceId = null, SkillInvocationReturnKind returnKind = SkillInvocationReturnKind.RuntimeFrame)
    {
        if (!HasSkillInvocationObservers) return false;
        if (!IsValidPlayerSeat(actorSeat) || string.IsNullOrWhiteSpace(skillKey) || string.IsNullOrWhiteSpace(occurrenceKey) ||
            _pendingDecision is not null || parentFrameId == 0 && returnKind != SkillInvocationReturnKind.SkipDiscard ||
            parentFrameId != 0 && _resolutionStack.LastOrDefault()?.Id != parentFrameId)
            throw new InvalidOperationException("Skill invocation requires its exact accepted live source and occurrence.");
        var key = InvocationSourceKey(actorSeat, skillKey, occurrenceKey);
        if (parentFrameId != 0)
        {
            var parent = _resolutionStack[^1]; var issued = parent.SkillInvocations?.Issued ?? [];
            if (issued.Contains(key, StringComparer.Ordinal))
            {
                if (!ProgramEventHistory<SkillInvocationStartedEvent>().Any(e => e.ParentFrameId == parentFrameId && e.ActorSeat == actorSeat &&
                        e.SkillKey == skillKey && e.OccurrenceKey == occurrenceKey && ProgramEventHistory<SkillInvocationCompletedEvent>().Any(c => c.FrameId == e.FrameId)))
                    throw new InvalidOperationException("A skill invocation cannot resume before its exact typed child returned.");
                return false;
            }
            ReplaceRuntimeTop(parent with { SkillInvocations = new() { Issued = issued.Append(key).ToArray() } });
        }
        var definition = _contentRegistry.Skills.GetValueOrDefault(skillKey);
        var name = definition?.Name ?? skillKey;
        var id = ++_resolutionSequence;
        CaptureKemingInvocation(id, parentFrameId, actorSeat, skillKey, occurrenceKey, skillInstanceId ?? "");
        var reactions = new List<SkillInvocationReaction>();
        foreach (var armed in ProgramEventHistory<ShenHuaTuoLuxinArmedEvent>().Where(a => a.TargetSeat == actorSeat &&
                     !ProgramEventHistory<ShenHuaTuoLuxinConsumedEvent>().Any(c => c.LuxinFrameId == a.FrameId)).ToArray())
        {
            reactions.Add(new(SkillInvocationReactionKind.Luxin, armed.OwnerSeat, "", armed.FrameId, armed.HpDelta));
            AdvanceEventRulesAndQueueFact(new ShenHuaTuoLuxinConsumedEvent(armed.FrameId, id, actorSeat, armed.HpDelta));
        }
        if (name != _contentRegistry.Skills.GetValueOrDefault(ShenHtJingyu)?.Name)
        foreach (var owner in _players.Where(p => p.IsAlive && HasRuntimeSkill(p, ShenHtJingyu)).OrderBy(p => RelativeTableOrder(_currentSeat, p.Seat)))
        {
            if (ProgramEventHistory<ShenHuaTuoJingyuReservedEvent>().Any(e => e.OwnerSeat == owner.Seat && e.SkillName == name && e.RoundNumber == _roundNumber)) continue;
            var grant = owner.SkillGrants.Grants.First(g => g.SkillId == ShenHtJingyu && g.IsEnabled);
            reactions.Add(new(SkillInvocationReactionKind.Jingyu, owner.Seat, grant.SkillInstanceId));
            AdvanceEventRulesAndQueueFact(new ShenHuaTuoJingyuReservedEvent(id, owner.Seat, grant.SkillInstanceId, name, _roundNumber));
        }
        var frame = new SkillInvocationFrame(id, parentFrameId, actorSeat, skillKey, occurrenceKey, skillInstanceId ?? "",
            name, definition?.Program?.GameplayHash ?? "", _roundNumber, _turnNumber, _currentSeat, returnKind)
        {
            Reactions = reactions.OrderBy(r => RelativeTableOrder(_currentSeat, r.OwnerSeat)).ThenBy(r => r.Kind).ToArray(),
            SourceFrameFingerprint = parentFrameId != 0 && _resolutionStack[^1] is
                (ProgramSkillFrame { InstructionIndex: 0 } or ProgramCardTriggerWindowFrame { CandidateIndex: 0, Activated: false })
                ? SkillInvocationSourceFingerprint(_resolutionStack[^1]) : null
        };
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new SkillInvocationStartedEvent(id, parentFrameId, actorSeat, skillKey, occurrenceKey,
            frame.SkillInstanceId, name, frame.GameplayHash, frame.RoundNumber, frame.TurnNumber, frame.TurnOwnerSeat, returnKind));
        AdvanceRuntimeFrame(id); return true;
    }

    [NativeFrameRoute(typeof(SkillInvocationFrame), 38700)]
    private void ContinueSkillInvocation(SkillInvocationFrame supplied)
    {
        var id = supplied.Id;
        while (_resolutionStack.LastOrDefault() is SkillInvocationFrame f && f.Id == id && _pendingDecision is null)
        {
            AssertSkillInvocation(f);
            if (f.DyingFrameId is not null) throw new InvalidOperationException("A skill invocation requires its typed dying return.");
            if (f.Stage == SkillInvocationStage.Drawing) { CompleteSkillInvocationDraw(f); return; }
            if (f.Stage == SkillInvocationStage.Children)
            {
                if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.SkillInvocation) ||
                    TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.SkillInvocation) ||
                    TryBeginHpChangedProgramWindow(id, PostEventContinuation.SkillInvocation) || TryBeginCardsMovedProgramWindow(id)) return;
                f = (SkillInvocationFrame)_resolutionStack[^1];
                ReplaceRuntimeTop(f with { Cursor = f.Cursor + 1, Stage = SkillInvocationStage.Ready, DrawOutcome = null }); continue;
            }
            if (f.Cursor >= f.Reactions.Count || _winner != Winner.None)
            {
                AdvanceEventRulesAndQueueFact(new SkillInvocationCompletedEvent(f.Id, f.ParentFrameId, f.OccurrenceKey));
                PopResolutionFrame(f.Id, ResolutionFrameKind.SkillInvocation); ResumeNativeSkillInvocation(f); return;
            }
            var reaction = f.Reactions[f.Cursor];
            if (reaction.Kind == SkillInvocationReactionKind.Jingyu)
            {
                if (!_players[reaction.OwnerSeat].IsAlive) { ReplaceRuntimeTop(f with { Cursor = f.Cursor + 1 }); continue; }
                // Jingyu is itself a real skill activation for an armed Luxin.
                if (TryBeginSkillInvocation(f.Id, reaction.OwnerSeat, ShenHtJingyu, "jingyu:" + f.Cursor, reaction.SkillInstanceId)) return;
                f = (SkillInvocationFrame)_resolutionStack[^1];
                ReplaceRuntimeTop(f = f with { Stage = SkillInvocationStage.Drawing,
                    NativeDraw = new NativeDrawProducerReceipt(f.Cursor, 1, null, SkillProgramCardSetVisibility.Private, new("skill.shen-hua-tuo.jingyu.draw"))
                    { Recipients = [reaction.OwnerSeat], ReturnKind = NativeDrawAttemptReturnKind.SkillInvocation } });
                CompleteSkillInvocationDraw(f); return;
            }
            ApplySkillInvocationLuxin(f, reaction);
        }
    }
    private void CompleteSkillInvocationDraw(SkillInvocationFrame f)
    {
        var paid = f.NativeDraw ?? throw new InvalidOperationException("Jingyu lost its owned draw request.");
        if (paid.ActiveAttemptId is not null && !paid.AttemptReturned) throw new InvalidOperationException("Jingyu resumed before its exact draw request returned.");
        if (!paid.AttemptReturned && TryBeginNativeDrawAttempt(f)) return;
        f = (SkillInvocationFrame)_resolutionStack[^1]; paid = f.NativeDraw!;
        var seat = paid.Recipients.Single(); var before = _movementSequence;
        var drawn = paid.Replacement is null && _players[seat].IsAlive && _winner == Winner.None
            ? DrawCards(_players[seat], 1, true, paid.Reason) : Array.AsReadOnly(Array.Empty<int>());
        var outcome = new NativeDrawOutcome(seat, 1, paid.ActiveAttemptId, before, _movementSequence, paid.Replacement) { ActualDrawIds = drawn };
        ReplaceRuntimeTop(f with { Stage = SkillInvocationStage.Children, NativeDraw = null, DrawOutcome = outcome });
        AdvanceEventRulesAndQueueFact(new SkillInvocationDrawPaidEvent(f.Id, f.Cursor, seat, drawn.Count, before, _movementSequence,
            outcome.AttemptFrameId, outcome.Replacement is not null));
        AdvanceRuntimeFrame(f.Id);
    }
    private void ApplySkillInvocationLuxin(SkillInvocationFrame f, SkillInvocationReaction r)
    {
        var target = _players[f.ActorSeat];
        ReplaceRuntimeTop(f = f with { Stage = SkillInvocationStage.Children });
        if (!target.IsAlive || !target.HasHpValue) return;
        var before = target.Hp;
        if (r.HpDelta > 0)
        {
            if (TryQueueRecoveryReplacement(f.Id, r.OwnerSeat, f.ActorSeat, 1,
                    new(RecoveryAttemptProducer.SkillInvocation, f.Cursor))) return;
            var amount = Math.Min(1, target.MaxHp - target.Hp);
            if (amount > 0)
            {
                var recovery = BeginRecovery(f.Id, r.OwnerSeat, target.Seat, amount);
                target.Hp += amount;
                AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(r.OwnerSeat, target.Seat, amount, target.Hp));
                PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
            }
        }
        else
        {
            target.Hp = checked(target.Hp - 1); RecordHpChange(f.Id, null, target.Seat, before, target.Hp, HpChangeKind.Loss);
            AdvanceEventRulesAndQueueFact(new ProgramSkillHpLostEvent(f.Id, ShenHtLuxin, target.Seat, 1, target.Hp));
        }
        AdvanceEventRulesAndQueueFact(new SkillInvocationHpPaidEvent(f.Id, f.Cursor, r.OwnerSeat, target.Seat, r.HpDelta, before, target.Hp));
        if (r.HpDelta >= 0 || target.Hp > 0 || ActiveDying?.VictimSeat == target.Seat) return;
        var dyingId = ++_resolutionSequence;
        ReplaceRuntimeTop(f with { DyingFrameId = dyingId });
        PushRuntimeFrame(new DyingFrame(dyingId, f.Id, target.Seat, null, Array.AsReadOnly(BuildDyingResponderSeats(target.Seat).ToArray()), 0, DyingContinuationKind.SkillInvocation));
        AdvanceEventRulesAndQueueFact(new PlayerDyingEvent(dyingId, target.Seat, null)); _status = EngineStatus.Running;
        if (!TryBeginMandatorySelfDyingProgram(ActiveDying!) && !TryBeginDyingEntryProgramWindow(ActiveDying!)) ExposeHumanDyingPrompt();
    }
    private void ReturnSkillInvocationDying(DyingCompletionReceipt dying)
    {
        if (_resolutionStack.LastOrDefault() is not SkillInvocationFrame f || f.Id != dying.ParentFrameId ||
            f.DyingFrameId != dying.FrameId || f.ActorSeat != dying.VictimSeat || dying.Continuation != DyingContinuationKind.SkillInvocation ||
            f.Stage != SkillInvocationStage.Children)
            throw new InvalidOperationException("Luxin requires its exact once-paid invocation dying return.");
        ReplaceRuntimeTop(f with { DyingFrameId = null }); AdvanceRuntimeFrame(f.Id);
    }
    private void AssertSkillInvocation(SkillInvocationFrame f)
    {
        if (f.Cursor < 0 || f.Cursor > f.Reactions.Count || !Enum.IsDefined(f.Stage) || !Enum.IsDefined(f.ReturnKind) ||
            !ProgramEventHistory<SkillInvocationStartedEvent>().Any(e => e == new SkillInvocationStartedEvent(f.Id, f.ParentFrameId, f.ActorSeat,
                f.SkillKey, f.OccurrenceKey, f.SkillInstanceId, f.SkillName, f.GameplayHash, f.RoundNumber, f.TurnNumber, f.TurnOwnerSeat, f.ReturnKind)) ||
            f.ParentFrameId != 0 && !_resolutionStack.Any(p => p.Id == f.ParentFrameId && p.SkillInvocations?.Issued.Contains(
                InvocationSourceKey(f.ActorSeat, f.SkillKey, f.OccurrenceKey), StringComparer.Ordinal) == true) ||
            f.Stage == SkillInvocationStage.Drawing && (f.NativeDraw is not { ReturnKind: NativeDrawAttemptReturnKind.SkillInvocation } draw ||
                draw.InstructionIndex != f.Cursor || draw.RequestedCount != 1 || !draw.Recipients.SequenceEqual([f.Reactions[f.Cursor].OwnerSeat])))
            throw new InvalidOperationException("A skill invocation lost its exact accepted occurrence, source frame or paid cursor.");
    }
}
