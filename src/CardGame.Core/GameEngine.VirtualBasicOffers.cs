namespace CardGame.Core;

public sealed record ProgramVirtualBasicDraft(int InstructionIndex, long? ChildFrameId = null);
public sealed record ProgramVirtualBasicReturn(long ParentFrameId, int InstructionIndex);

public sealed partial class GameEngine
{
    private void AssertVirtualBasicDraft(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        if (frame.VirtualBasicDraft is not { } draft) return;
        if ((effect.Op != SkillProgramEffectOp.OfferVirtualBasicCard && !(effect.Op == SkillProgramEffectOp.UseVirtualAlcohol && IsExactProvenanceVirtualBasicDraft(frame, draft))) || draft.InstructionIndex != frame.InstructionIndex)
            throw new InvalidOperationException("A virtual basic draft lost its exact instruction.");
        if (draft.ChildFrameId is { } childId)
        {
            var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
            if (index < 0 || index + 1 >= _resolutionStack.Count || _resolutionStack[index + 1] is not CardUseFrame child || child.Id != childId ||
                child.VirtualBasicReturn != new ProgramVirtualBasicReturn(frame.Id, draft.InstructionIndex) || child.CardId != 0 || child.PhysicalCardIds?.Count != 0 ||
                child.Action is not { Type: CardActionType.Use } action || action.ActorSeat != child.SourceSeat || action.ProviderSeat != frame.OwnerSeat || action.PhysicalCards.Count != 0 || action.EffectiveKind != child.CardKind)
                throw new InvalidOperationException("A virtual basic child has no exact zero-entity parent return.");
            return;
        }
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || _pendingDecision is not { Kind: DecisionKind.ProgramTrigger } prompt || prompt.PlayerSeat != frame.OwnerSeat || prompt.Choices.Count == 0 ||
            prompt.Choices.Any(c => c.Cards.Count != 0 || c.Parameters.GetValueOrDefault("program-action") != "virtual-basic" || c.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
                (c.Parameters.GetValueOrDefault("basic-option") == "skip" ? c.Targets.Count != 0 : !VirtualBasicOptions(frame.OwnerSeat).Any(o => c.Parameters.GetValueOrDefault("basic-option") == $"{o.Kind}:{o.Target}" && c.Targets.SequenceEqual(new[] {o.Target})))))
            throw new InvalidOperationException("A virtual basic chooser lost its exact legal options.");
    }

    private IEnumerable<(CardKind Kind, int Target)> VirtualBasicOptions(int owner)
    {
        var actor = _players[owner];
        if (!actor.IsAlive || _winner != Winner.None || _phase != TurnPhase.Play || _currentSeat != owner) yield break;
        foreach (var kind in new[] { CardKind.Slash, CardKind.FireSlash, CardKind.ThunderSlash })
            foreach (var target in _players.Where(t => CanUseVirtualSlashTarget(actor, t, kind) && !IsDirectedCardTargetProhibited(owner,t.Seat,kind) && !IsCardTargetProhibited(t,kind,Suit.None))) yield return (kind, target.Seat);
        if (actor.Hp < actor.MaxHp && !IsCardUseForbidden(owner, CardKind.Peach, CardActionType.Use) && !IsDirectedCardTargetProhibited(owner, owner, CardKind.Peach)) yield return (CardKind.Peach, owner);
        if (!actor.HasAlcoholEffect && (!actor.UsedPlayPhaseAlcoholThisTurn || HasTargetCardQuotaAllowance(actor.Seat, actor.Seat) || HasNextUnlimitedCard(actor) || HasCardPolicy(actor, SkillProgramCardPolicyKind.UnlimitedAlcoholUse, CardKind.Alcohol)) && !IsCardUseForbidden(owner, CardKind.Alcohol, CardActionType.Use) && !IsDirectedCardTargetProhibited(owner, owner, CardKind.Alcohol)) yield return (CardKind.Alcohol, owner);
    }

    private SkillProgramStepOutcome OfferVirtualBasicCard(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.VirtualBasicDraft is not null) throw new InvalidOperationException("A virtual basic offer cannot start twice.");
        var options = VirtualBasicOptions(active.OwnerSeat).ToArray();
        if (options.Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(active with { VirtualBasicDraft = new(active.InstructionIndex) });
        PromptChoice Choice(string option, string label, IReadOnlyList<int> targets) => new(new($"virtual-basic.{active.Id}.{option}"), label, [], targets,
            new Dictionary<string, string> { ["program-action"] = "virtual-basic", ["frame-id"] = active.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["basic-option"] = option });
        var choices = options.Select(o => Choice($"{o.Kind}:{o.Target}", $"视为对 {_players[o.Target].Name} 使用【{CardCatalog.Get(o.Kind).DisplayName}】", [o.Target])).Append(Choice("skip", "不使用基本牌", [])).ToArray();
        var skill = _contentRegistry.GetSkill(active.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, active.OwnerSeat, "请选择视为使用的基本牌，或不使用。", [], options.Select(o => o.Target).Distinct().ToArray(), active.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = active.OwnerSeat, Choices = Array.AsReadOnly(choices), SkillPrompt = new(active.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[active.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveVirtualBasicChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("A basic offer lost its program parent.");
        if (frame.VirtualBasicDraft is not { ChildFrameId: null } draft || draft.InstructionIndex != frame.InstructionIndex || _pendingDecision?.PlayerSeat != frame.OwnerSeat || selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) || selected.Cards.Count != 0)
            throw new InvalidOperationException("A basic offer lost its exact unpaid chooser.");
        var option = selected.Parameters.GetValueOrDefault("basic-option");
        if (option == "skip")
        {
            ClearPendingDecision(); ReplaceRuntimeTop(frame with { VirtualBasicDraft = null }); AdvanceRuntimeProgram(frame.Id); return;
        }
        var chosen = VirtualBasicOptions(frame.OwnerSeat).SingleOrDefault(o => $"{o.Kind}:{o.Target}" == option);
        if (option != $"{chosen.Kind}:{chosen.Target}" || !selected.Targets.SequenceEqual(new[] { chosen.Target })) throw new InvalidOperationException("This virtual basic use is no longer legal.");
        ClearPendingDecision();
        var actor = _players[frame.OwnerSeat];
        var id = ++_resolutionSequence;
        ReplaceRuntimeTop(frame with { VirtualBasicDraft = draft with { ChildFrameId = id } });
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence, null, CardActionType.Use, actor.Seat, actor.Seat, null, null, null, chosen.Kind, [chosen.Target], [], [], effectiveSuit: Suit.None, effectiveRank: 0));
        PushRuntimeFrame(new CardUseFrame(id, actor.Seat, 0, chosen.Kind, [chosen.Target], PhysicalCardIds: []) { Action = action, VirtualBasicReturn = new(frame.Id, draft.InstructionIndex), FirstOwnPlayUseDistanceUnlimited = HasFirstActualPlayUseDistance(actor) });
        if (TracksPlayCardHistory) AdvanceEventRulesAndQueueFact(new CardUseAppearanceCapturedEvent(action));
        AdvanceEventRulesAndQueueFact(new CardUseDeclaredEvent(id, 0, chosen.Kind, actor.Seat));
        AdvanceEventRulesAndQueueFact(new TargetsConfirmedEvent(id, [chosen.Target]));
        RecordYingboCardUse(id, actor.Seat, chosen.Kind);
        RecordProgramUsedBasicCard(actor.Seat, chosen.Kind);
        RecordActualPlayPhaseUse(action);
        if (IsSlashCard(chosen.Kind))
        {
            if (!(LifecycleCardUse(id)?.UnlimitedUse == true)) RecordSlashUseDebit(id, actor.Seat);
            MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, chosen.Kind);
            var attack = new CardAttackHandle(this, id, actor.Seat, chosen.Target, null, damageAmount: actor.HasAlcoholEffect ? 2 : 1,
                playedCardKind: chosen.Kind, ignoresArmor: HasCardArmorBypass(actor, _players[chosen.Target], chosen.Kind));
            CaptureProgramAlcoholConsumption(id, actor);
            actor.HasAlcoholEffect = false;
            ActiveCardAttack = attack;
            AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, chosen.Kind, actor.Seat, chosen.Target));
            TryMarkProgramUseCommitted(id);
            if (!TryBeginProgramCardWindow(attack, action, SkillProgramTriggerWindow.CardUseCommitted, action.TargetSeats, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
        }
        else BeginSimpleCardUse(id, new(0, chosen.Kind == CardKind.Peach ? SimpleCardUseEffect.Recovery : SimpleCardUseEffect.Alcohol));
    }

    private bool ContinueVirtualBasicEffect(long id, ProgramSimpleCardContinuation continuation)
    {
        var use = LifecycleCardUse(id)!;
        if (use.VirtualBasicReturn is null) return false;
        if (continuation.CardId != 0 || use.CardId != 0 || use.PhysicalCardIds?.Count != 0 || use.CardKind is not (CardKind.Peach or CardKind.Alcohol) || use.Step == ResolutionFrameStep.Completed || use.VirtualBasicEffectApplied == true)
            throw new InvalidOperationException("A virtual basic effect lost its exact zero-cost use.");
        var actor = _players[use.SourceSeat];
        SetCardUseStep(id, ResolutionFrameStep.ResolvingEffect);
        if (actor.IsAlive && _winner == Winner.None && !IsCardEffectIneffective(id, actor.Seat))
        {
            if (use.CardKind == CardKind.Peach)
            {
                if (!TryQueueRecoveryReplacement(id, actor.Seat, actor.Seat, 1, new(RecoveryAttemptProducer.VirtualBasic)))
                {
                    var recovery = BeginRecovery(id, actor.Seat, actor.Seat, 1);
                    try { actor.Hp = Math.Min(actor.MaxHp, actor.Hp + 1); AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(actor.Seat, actor.Seat, 1, actor.Hp)); }
                    finally { PopResolutionFrame(recovery, ResolutionFrameKind.Recovery); }
                    AddLog("Recovered", $"{actor.Name} 使用【桃】回复1点体力，至 {actor.Hp}/{actor.MaxHp}。", actor.Seat, actor.Seat);
                }
            }
            else
            {
                actor.UsedPlayPhaseAlcoholThisTurn = true; actor.HasAlcoholEffect = true;
                AdvanceEventRulesAndQueueFact(new AlcoholAppliedEvent(id, actor.Seat, 1));
                AddLog("CardEffect", $"{actor.Name} 使用【酒】，下一张直接杀造成的伤害 +1。", actor.Seat, actor.Seat);
            }
        }
        UpdateLifecycleCardUse(id, frame => frame with { VirtualBasicEffectApplied = true });
        FinishVirtualBasicUse(id);
        return true;
    }

    private void FinishVirtualBasicUse(long id)
    {
        var use = LifecycleCardUse(id) ?? throw new InvalidOperationException("A virtual basic completion lost its card-use parent.");
        if (use.VirtualBasicReturn is null || use.VirtualBasicEffectApplied != true || use.CardId != 0 || use.PhysicalCardIds?.Count != 0 || use.CardKind is not (CardKind.Peach or CardKind.Alcohol) || use.Step == ResolutionFrameStep.Completed)
            throw new InvalidOperationException("A virtual basic completion lost its paid effect marker.");
        if (TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.VirtualBasicCardUse, cardKind:use.CardKind)) return;
        if (TryBeginHpChangedProgramWindow(id, PostEventContinuation.VirtualBasicCardUse, cardKind:use.CardKind)) return;
        SetCardUseStep(id, ResolutionFrameStep.Completed);
        AdvanceEventRulesAndQueueFact(new CardUseFinishedEvent(id, 0, use.CardKind));
        if (_winner == Winner.None && TryBeginProgramCardWindow(null, use.Action!, SkillProgramTriggerWindow.CardUseCompleted, use.TargetSeats, ProgramCardContinuation.CompletedCard)) return;
        PopFinishedCardUse(id);
    }

    private void ReturnVirtualBasicUse(ProgramVirtualBasicReturn? receipt, long childId)
    {
        if (receipt is null) return;
        var frame = GetActiveProgramFrame(receipt.ParentFrameId);
        if (frame.VirtualBasicDraft is not { } draft || draft.ChildFrameId != childId || draft.InstructionIndex != receipt.InstructionIndex || frame.InstructionIndex != receipt.InstructionIndex || _resolutionStack.LastOrDefault()?.Id != frame.Id)
            throw new InvalidOperationException("The virtual basic child lost its exact paid program return.");
        ReplaceRuntimeTop(frame with { VirtualBasicDraft = null });
        AdvanceRuntimeProgram(frame.Id);
    }
}
