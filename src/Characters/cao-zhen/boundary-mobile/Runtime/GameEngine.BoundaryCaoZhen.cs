namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BoundarySidiSkill = "boundary:sidi-mobile", BoundarySidiAction = "boundary-sidi";
    private static bool BoundarySidiNonDelayed(CardActionContext action) => action.EffectiveKind is not (CardKind.Lightning or CardKind.Indulgence or CardKind.SupplyShortage);

    private IReadOnlyList<BoundarySidiPrediction> BoundarySidiRecords(int owner, string instance) =>
        Array.AsReadOnly((_players[owner].BoundarySidiPredictions ?? []).Where(record => record.OwnerSeat == owner &&
            record.SkillInstanceId == instance && record.GameplayHash == _contentRegistry.GetSkill(BoundarySidiSkill).Program!.GameplayHash &&
            _players[record.HolderSeat].IsAlive && _players[record.TargetSeat].IsAlive).ToArray());

    private IReadOnlyList<BoundarySidiPredictionView>? GetBoundarySidiPredictionViews(int viewer)
    {
        if (!IsValidPlayerSeat(viewer) || !_players[viewer].IsAlive) return null;
        var values = GetSkillBindingShard(_players[viewer]).ProgramInstances.Where(instance => instance.SkillId == BoundarySidiSkill &&
                HasRuntimeSkillInstance(_players[viewer], instance.SkillId, instance.SkillInstanceId))
            .SelectMany(instance => BoundarySidiRecords(viewer, instance.SkillInstanceId))
            .OrderBy(record => record.HolderSeat).Select(record => new BoundarySidiPredictionView(record.HolderSeat, record.TargetSeat)).ToArray();
        return values.Length == 0 ? null : Array.AsReadOnly(values);
    }

    private int[] BoundarySidiEligibleHolders(int owner, string instance) => _players.Where(player => player.IsAlive && player.Seat != owner &&
        !BoundarySidiRecords(owner, instance).Any(record => record.HolderSeat == player.Seat)).Select(player => player.Seat).ToArray();

    private IReadOnlyList<int> BoundarySidiActualTargets(ProgramCardTriggerWindowFrame window)
    {
        var action = window.Action;
        var targets = action.EffectiveDesignatedTargetSeats;
        if (targets.Count != 0 || action.Type != CardActionType.Use ||
            action.EffectiveKind is not (CardKind.DrawTwo or CardKind.Alcohol) && GetProgramCardCategory(action.EffectiveKind) != SkillProgramCardCategory.Equipment ||
            LifecycleCardUse(window.ParentFrameId) is not { Action: { } original } use || original.ActionId != action.ActionId || use.SourceSeat != action.ActorSeat ||
            !ProgramEventHistory<CardUseDeclaredEvent>().Any(declared => declared.ResolutionId == use.Id && declared.SourceSeat == action.ActorSeat && declared.CardKind == action.EffectiveKind))
            return targets;
        // These native producers encode a self target as empty command targets.
        // Name the real effect recipient locally, retaining the accepted action.
        return use.TargetSeats.Count == 0 ? [action.ActorSeat] : use.TargetSeats;
    }

    private bool TryBoundarySidiWindow(ProgramSkillWindowContext? context, out ProgramCardTriggerWindowFrame window)
    {
        window = null!;
        if (context?.CardUse is not { } identity || context.Window is not (SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseCompleted) ||
            _resolutionStack.SingleOrDefault(frame => frame.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame current ||
            GetCardActionWindow(current) != context.Window || current.Action.ActionId != identity.CardActionId ||
            !IsFriendActualCardUse(current.Action) || !BoundarySidiNonDelayed(current.Action)) return false;
        if (current.Action.Type == CardActionType.Use)
        {
            if (LifecycleCardUse(current.ParentFrameId) is not { Action: { } actual } use || actual.ActionId != current.Action.ActionId ||
                context.Window == SkillProgramTriggerWindow.CardUseCompleted && use.Step != ResolutionFrameStep.Completed) return false;
        }
        else if (current.CompletedResponseReturn is not { } typed || typed.IsCommitted != (context.Window == SkillProgramTriggerWindow.CardUseCommitted) ||
            !IsCompletedResponseUse(current.Action, typed.Kind) || !IsCompletedResponseParentConsistent(current)) return false;
        window = current; return true;
    }

    [ProgramPipelineHook(ProgramPipelinePhase.CollectIssuedCardTrigger, 53400)]
    private void CollectBoundarySidiUses(CardActionContext action, SkillProgramTriggerWindow window, List<ProgramCardTriggerCandidate> result)
    {
        if (window is not (SkillProgramTriggerWindow.CardUseCommitted or SkillProgramTriggerWindow.CardUseCompleted) ||
            !IsFriendActualCardUse(action) || !BoundarySidiNonDelayed(action)) return;
        var binding = window == SkillProgramTriggerWindow.CardUseCompleted ? "prepare-forecast" : "consume-first-use";
        result.RemoveAll(candidate => candidate.SkillId == BoundarySidiSkill && candidate.TriggerId == binding);
        foreach (var owner in _players.Where(player => player.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, window).Where(candidate => candidate.SkillId == BoundarySidiSkill && candidate.BindingId == binding))
        {
            if (window == SkillProgramTriggerWindow.CardUseCompleted
                ? action.ActorSeat != owner.Seat || BoundarySidiEligibleHolders(owner.Seat, candidate.SkillInstanceId).Length == 0
                : !BoundarySidiRecords(owner.Seat, candidate.SkillInstanceId).Any(record => record.HolderSeat == action.ActorSeat)) continue;
            var context = CreateCardActionProgramContext(action, window, 0, owner.Seat, owner.Seat, CaptureProgramTriggerFacts(owner, action));
            result.Add(new(owner.Seat, owner.Seat, candidate.SkillId, candidate.BindingId, candidate.GameplayHash, candidate.SkillInstanceId, candidate.Priority, context));
        }
    }

    [ProgramPipelineHook(ProgramPipelinePhase.ObservesResponseUseCompletion, 53400)]
    private bool ObservesBoundarySidiResponse(CardActionContext action) => IsFriendActualCardUse(action) &&
        _players.Any(owner => owner.IsAlive && GetSkillBindingShard(owner).ProgramInstances.Any(instance => instance.SkillId == BoundarySidiSkill &&
            HasRuntimeSkillInstance(owner, instance.SkillId, instance.SkillInstanceId) &&
            (owner.Seat == action.ActorSeat || BoundarySidiRecords(owner.Seat, instance.SkillInstanceId).Any(record => record.HolderSeat == action.ActorSeat))));

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 53400)]
    private bool QualifyBoundarySidi(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (candidate.SkillId != BoundarySidiSkill) return true;
        if (!TryBoundarySidiWindow(context, out var window)) return false;
        return trigger.Id == "prepare-forecast"
            ? window.Action.ActorSeat == candidate.OwnerSeat && BoundarySidiEligibleHolders(candidate.OwnerSeat, candidate.SkillInstanceId).Length > 0
            : BoundarySidiRecords(candidate.OwnerSeat, candidate.SkillInstanceId).Any(record => record.HolderSeat == window.Action.ActorSeat) ||
                _resolutionStack.OfType<ProgramSkillFrame>().Any(frame => frame.OwnerSeat == candidate.OwnerSeat && frame.SkillInstanceId == candidate.SkillInstanceId && frame.BoundarySidi?.WindowId == window.Id);
    }

    [ProgramCapability]
    private sealed class BoundaryCaoZhenHost(GameEngine engine) : IBoundaryCaoZhenHost
    { public SkillProgramStepOutcome Execute(ProgramSkillFrame frame, SkillProgramEffect effect) => engine.BeginBoundarySidi(frame, effect); }

    private SkillProgramStepOutcome BeginBoundarySidi(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.BoundarySidi is not null || !TryBoundarySidiWindow(frame.WindowContext, out var window))
            throw new InvalidOperationException("Sidi requires its original true-use window and owning frame.");
        if (effect.Op == SkillProgramEffectOp.PrepareBoundaryCaoZhenSidi)
        {
            ReplaceRuntimeTop(frame = frame with { BoundarySidi = new(frame.InstructionIndex, BoundarySidiStage.Holder, window.Id, window.Action.ActionId) });
            PublishBoundarySidi(frame); return SkillProgramStepOutcome.AwaitChoice;
        }
        var record = BoundarySidiRecords(frame.OwnerSeat, frame.SkillInstanceId).Single(item => item.HolderSeat == window.Action.ActorSeat);
        _players[frame.OwnerSeat].BoundarySidiPredictions = Array.AsReadOnly((_players[frame.OwnerSeat].BoundarySidiPredictions ?? []).Where(item => item.Id != record.Id).ToArray());
        if (BoundarySidiActualTargets(window) is not [var target] || target != record.TargetSeat)
        { CommitBoundarySidiResult(frame, effect.ResultBind!, window.Action.ActionId, "none"); return SkillProgramStepOutcome.Continue; }
        if (target == frame.OwnerSeat)
        { CommitBoundarySidiResult(frame, effect.ResultBind!, window.Action.ActionId, "self"); return SkillProgramStepOutcome.Continue; }
        ReplaceRuntimeTop(frame = frame with { BoundarySidi = new(frame.InstructionIndex, BoundarySidiStage.Benefit, window.Id, window.Action.ActionId, record.HolderSeat, record.TargetSeat, effect.ResultBind!) });
        PublishBoundarySidi(frame); return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> BoundarySidiChoices(ProgramSkillFrame frame)
    {
        var draft = frame.BoundarySidi!;
        PromptChoice Choice(string branch, string label, int? target = null) => new(new($"boundary-sidi.{frame.Id}.{draft.Stage}.{branch}.{target?.ToString() ?? "none"}"),
            label, [], target is { } seat ? [seat] : [], new Dictionary<string, string> { ["program-action"] = BoundarySidiAction, ["branch"] = branch });
        return draft.Stage switch
        {
            BoundarySidiStage.Holder => Array.AsReadOnly(BoundarySidiEligibleHolders(frame.OwnerSeat, frame.SkillInstanceId).Select(seat => Choice("holder", "为 " + _players[seat].Name + " 秘密预测目标", seat)).ToArray()),
            BoundarySidiStage.Target => Array.AsReadOnly(_players.Where(player => player.IsAlive).Select(player => Choice("target", "秘密指定 " + player.Name + " 为司敌目标", player.Seat)).ToArray()),
            _ => Array.AsReadOnly(new[] { Choice("cancel", "取消该牌；若无人濒死，对使用者造成1点伤害"), Choice("draw", "摸两张牌") })
        };
    }

    private void PublishBoundarySidi(ProgramSkillFrame frame)
    {
        var choices = BoundarySidiChoices(frame); var skill = _contentRegistry.GetSkill(frame.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat,
            frame.BoundarySidi!.Stage == BoundarySidiStage.Holder ? "司敌：选择尚未预测的其他角色。" :
            frame.BoundarySidi.Stage == BoundarySidiStage.Target ? "司敌：秘密指定其下一张非延时牌的唯一目标。" : "司敌预测命中：取消该牌或摸两张牌。",
            [], Array.AsReadOnly(choices.SelectMany(choice => choice.Targets).Distinct().ToArray()), frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat, Choices = choices, SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AdvanceRulesAndPublishState();
    }

    [ProgramAnswer(BoundarySidiAction)]
    private void AnswerBoundarySidi(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { BoundarySidi: { } draft } frame ||
            !TryBoundarySidiWindow(frame.WindowContext, out var window) || window.Id != draft.WindowId || window.Action.ActionId != draft.ActionId ||
            !AssistedChoicesEqual([choice], BoundarySidiChoices(frame).Where(current => current.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Sidi lost its original private forecast chooser or current native use.");
        ClearPendingDecision();
        if (draft.Stage == BoundarySidiStage.Holder)
        { ReplaceRuntimeTop(frame = frame with { BoundarySidi = draft with { Stage = BoundarySidiStage.Target, HolderSeat = choice.Targets.Single() } }); PublishBoundarySidi(frame); return; }
        if (draft.Stage == BoundarySidiStage.Target)
        {
            var record = new BoundarySidiPrediction(frame.Id, frame.OwnerSeat, frame.SkillInstanceId, frame.GameplayHash, draft.HolderSeat, choice.Targets.Single());
            if (!BoundarySidiEligibleHolders(frame.OwnerSeat, frame.SkillInstanceId).Contains(record.HolderSeat)) throw new InvalidOperationException("Sidi cannot replace another unconsumed forecast.");
            _players[frame.OwnerSeat].BoundarySidiPredictions = Array.AsReadOnly((_players[frame.OwnerSeat].BoundarySidiPredictions ?? []).Append(record).ToArray());
            ReplaceRuntimeTop(frame with { BoundarySidi = null });
            AdvanceEventRulesAndQueueFact(new BoundarySidiForecastRecordedEvent(frame.Id, frame.OwnerSeat));
            AdvanceRuntimeProgram(frame.Id); return;
        }
        var outcome = "draw";
        if (choice.Parameters["branch"] == "cancel")
        {
            if (window.Action.Type != CardActionType.Use || LifecycleCardUse(window.ParentFrameId) is not { } use || use.Action?.ActionId != draft.ActionId)
                throw new InvalidOperationException("A targeted Sidi cancellation must retain its original native Use.");
            var proof = new BoundarySidiCancelledEvent(frame.Id, use.Id, draft.ActionId, frame.OwnerSeat, frame.SkillInstanceId, frame.GameplayHash);
            UpdateLifecycleCardUse(use.Id, current => current with { BoundarySidiCancellation = proof });
            AdvanceEventRulesAndQueueFact(proof);
            outcome = ActiveDying is null && !_players.Any(player => player.IsAlive && player.HasHpValue && player.Hp <= 0) ? "cancel-damage" : "cancel-only";
        }
        CommitBoundarySidiResult(frame, draft.ResultBind!, draft.ActionId, outcome);
        AdvanceRuntimeProgram(frame.Id);
    }

    private void CommitBoundarySidiResult(ProgramSkillFrame supplied, string bind, long action, string outcome)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        ReplaceRuntimeTop(frame with { BoundarySidi = null, ChoiceBindings = Array.AsReadOnly(frame.ChoiceBindings.Append(new ProgramChoiceResultBinding(bind, outcome, frame.OwnerSeat)).ToArray()) });
        AdvanceEventRulesAndQueueFact(new BoundarySidiResultEvent(frame.Id, frame.OwnerSeat, action, bind, outcome));
    }

    private bool IsBoundarySidiChoiceResult(ProgramSkillFrame frame, ProgramChoiceResultBinding binding) => frame.SkillId == BoundarySidiSkill &&
        binding.ChooserSeat == frame.OwnerSeat && frame.WindowContext?.CardUse is { } use &&
        ProgramEventHistory<BoundarySidiResultEvent>().Count(record => record.FrameId == frame.Id && record.OwnerSeat == frame.OwnerSeat &&
            record.ActionId == use.CardActionId && record.ResultBind == binding.Name && record.Outcome == binding.OptionId) == 1 &&
        ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).Instructions.Take(frame.InstructionIndex)
            .Count(effect => effect.Op == SkillProgramEffectOp.ResolveBoundaryCaoZhenSidi && effect.ResultBind == binding.Name) == 1;

    private bool IsBoundarySidiUseIneffective(CardUseFrame use)
    {
        if (use.BoundarySidiCancellation is not { } proof) return false;
        if (use.Action?.ActionId != proof.ActionId || use.Id != proof.UseFrameId || proof.GameplayHash != _contentRegistry.GetSkill(BoundarySidiSkill).Program?.GameplayHash ||
            ProgramEventHistory<BoundarySidiCancelledEvent>().Count(record => record == proof) != 1 ||
            !ProgramEventHistory<BoundarySidiResultEvent>().Any(record => record.FrameId == proof.ProgramFrameId && record.ActionId == proof.ActionId && record.Outcome is "cancel-only" or "cancel-damage"))
            throw new InvalidOperationException("Sidi cancellation lost its exact original use and committed result.");
        return true;
    }

    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 53400)]
    private bool AllowsBoundarySidiCancellationDamage(ProgramSkillFrame frame, int target, int amount,
        ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (frame.SkillId != BoundarySidiSkill || frame.TriggerId != "consume-first-use" || frame.AttackAttempt is not null ||
            _resolutionStack.LastOrDefault()?.Id != frame.Id || sourceLess || source is not null || nature is not null || amount != 1 ||
            ActiveDying is not null || _players.Any(player => player.IsAlive && player.HasHpValue && player.Hp <= 0) ||
            !TryBoundarySidiWindow(frame.WindowContext, out var window) || GetCardActionWindow(window) != SkillProgramTriggerWindow.CardUseCommitted ||
            window.Action.Type != CardActionType.Use || window.Action.ActorSeat != target ||
            LifecycleCardUse(window.ParentFrameId) is not { BoundarySidiCancellation: { } proof } use ||
            proof.ProgramFrameId != frame.Id || proof.OwnerSeat != frame.OwnerSeat || proof.SkillInstanceId != frame.SkillInstanceId ||
            CurrentDamageAttempt?.ResolutionId != use.Id || !IsBoundarySidiUseIneffective(use)) return false;
        var effect = GetPausedPrivateOfferEffect(frame);
        return effect is { Op: SkillProgramEffectOp.Damage, Amount: 1, ActorReference: null, DamageNature: null,
                TargetReference.Kind: ProgramParticipantRef.Actor, Condition.Kind: SkillProgramConditionKind.ChoiceIs } &&
            frame.ChoiceBindings.SingleOrDefault(binding => binding.Name == effect.Condition.SourceBind) is { OptionId: "cancel-damage" } result &&
            effect.Condition.OptionId == result.OptionId && IsBoundarySidiChoiceResult(frame, result);
    }

    private bool TryBoundarySidiEquipmentCommitted(long id, Card equipment)
    {
        if (LifecycleCardUse(id) is not { Action: { Type: CardActionType.Use } action } use || action.EffectiveKind != equipment.Kind ||
            use.BoundarySidiCancellation is null && !_players.Any(owner => owner.IsAlive &&
                GetSkillBindingShard(owner).ProgramInstances.Any(instance => instance.SkillId == BoundarySidiSkill &&
                    HasRuntimeSkillInstance(owner, instance.SkillId, instance.SkillInstanceId) &&
                    BoundarySidiRecords(owner.Seat, instance.SkillInstanceId).Any(record => record.HolderSeat == action.ActorSeat)))) return false;
        var continuation = new ProgramSimpleCardContinuation(equipment.Id, SimpleCardUseEffect.EquipmentPlacement);
        if (TryBeginCommittedCardUse(id, ProgramCardContinuation.CommittedSimpleCard, simple: continuation)) return true;
        return ContinueBoundarySidiIneffectiveSimple(id, continuation);
    }

    private bool ContinueBoundarySidiIneffectiveSimple(long id, ProgramSimpleCardContinuation continuation)
    {
        if (LifecycleCardUse(id) is not { BoundarySidiCancellation: not null } use || !IsBoundarySidiUseIneffective(use)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != id || use.Action is not { Type: CardActionType.Use }) throw new InvalidOperationException("Sidi lost its original simple-card return.");
        foreach (var card in GetCardUsePhysicalCards(id).Where(card => _cardZones.GetLocation(card.Id) == CardLocation.Processing))
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, new("skill-program.boundary-sidi.cancelled"));
        var original = use.CardId == 0 ? new Card(0, use.CardKind, Suit.None, 0) : GetAdvancedCard(use.CardId);
        FinishCardUse(id, original, use.CardKind); return true;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 53400)]
    private void AssertBoundarySidi(ProgramSkillFrame frame)
    {
        if (frame.BoundarySidi is not { } draft) return;
        if (draft.InstructionIndex != frame.InstructionIndex || !TryBoundarySidiWindow(frame.WindowContext, out var window) ||
            draft.WindowId != window.Id || draft.ActionId != window.Action.ActionId || !ReferenceEquals(frame, _resolutionStack.LastOrDefault()) ||
            _pendingDecision is not { IsPrivate: true } prompt || prompt.PlayerSeat != frame.OwnerSeat ||
            !AssistedChoicesEqual(prompt.Choices, BoundarySidiChoices(frame))) throw new InvalidOperationException("Sidi lost its frame-owned private forecast or current first-use choice.");
    }

    [ProgramAiChoice(SkillProgramEffectOp.PrepareBoundaryCaoZhenSidi)]
    private static PromptChoice BoundarySidiPrepareAi(PendingDecision prompt) => BoundarySidiAi(prompt);
    [ProgramAiChoice(SkillProgramEffectOp.ResolveBoundaryCaoZhenSidi)]
    private static PromptChoice BoundarySidiResolveAi(PendingDecision prompt) => BoundarySidiAi(prompt);
    private static PromptChoice BoundarySidiAi(PendingDecision prompt) => prompt.Choices.FirstOrDefault(choice => choice.Parameters.GetValueOrDefault("branch") == "draw") ?? prompt.Choices[0];
}
