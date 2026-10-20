namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasSoleIncomingEffectCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RepeatIncomingSoleCardEffectAndDrawByRange);
    private bool TryGetSoleIncomingEffect(int owner, ProgramSkillWindowContext? context, out CardUseFrame use, out ProgramCardTriggerWindowFrame window)
    {
        use = null!; window = null!;
        if (context is not { Window: SkillProgramTriggerWindow.CardUseTargetsFinalized, CardUse: { } id } ||
            _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is not ProgramCardTriggerWindowFrame w ||
            w.ParentFrameId != id.ParentCardUseFrameId || w.CompletedResponseReturn is not null ||
            w.Continuation is not (ProgramCardContinuation.Slash or ProgramCardContinuation.FinalizedTrick or ProgramCardContinuation.FinalizedSimpleCard) ||
            LifecycleCardUse(w.ParentFrameId) is not { Action: { Type: CardActionType.Use } a } current ||
            a.ActorSeat == owner || a.ActionId != id.CardActionId || a.ActorSeat != current.SourceSeat || a.EffectiveKind != current.CardKind ||
            w.Action.ActionId != a.ActionId || !w.Action.TargetSeats.SequenceEqual(current.TargetSeats) ||
            !ExtraEffectCardKind(current.CardKind) || current.CardKind is CardKind.Dodge or CardKind.Nullification ||
            !_players[owner].IsAlive) return false;
        var actual = a.EffectiveDesignatedTargetSeats.Distinct().ToArray();
        if (actual is not [var target] || target != owner || !current.TargetSeats.Contains(owner) ||
            current.SoleIncomingEffects is { Started: > 0 }) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == w.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != current.Id) return false;
        use = current; window = w; return true;
    }

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 1243, SkillProgramEffectOp.RepeatIncomingSoleCardEffectAndDrawByRange)]
    private bool CanOfferSoleIncomingEffect(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context) =>
        TryGetSoleIncomingEffect(candidate.OwnerSeat, context, out var use, out var window) &&
        DesignatedExtraTargetCandidateMatches(candidate, context, window) &&
        use.SoleIncomingEffects?.Grants.Any(g => g.Source.OwnerSeat == candidate.OwnerSeat && g.Source.SkillId == candidate.SkillId &&
            g.Source.SkillInstanceId == candidate.SkillInstanceId) != true;

    private void IssueSoleIncomingEffect(ProgramSkillFrame supplied, int cap)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { } context || !TryGetSoleIncomingEffect(f.OwnerSeat, context, out var use, out var window) ||
            !CanOfferSoleIncomingEffect(new(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId, f.GameplayHash, 0), GetProgramTrigger(f), context))
            throw new InvalidOperationException("An incoming repeat needs its exact sole target and original finalized native use.");
        var source = new CardConversionSource(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
        var grant = new SoleIncomingEffectGrant(f.Id, source, f.GameplayHash, window.Id, use.Id, use.Action!.ActionId,
            use.Action.ActorSeat, f.OwnerSeat, use.CardKind, cap);
        var previous = use.SoleIncomingEffects;
        var simple = window.SimpleContinuation is { } s ? s with
        { RecoveryPolicySources = s.RecoveryPolicySources is { } policies ? Array.AsReadOnly(policies.ToArray()) : null } : null;
        var native = previous?.Native ?? new ExtraCardEffectState { Source = source, ProgramFrameId = f.Id, ActionId = grant.ActionId,
            Trick = window.TrickContinuation, Simple = simple };
        UpdateLifecycleCardUse(use.Id, current => current with { SoleIncomingEffects = new(grant.ActionId, native)
        { Grants = (previous?.Grants ?? []).Append(grant).ToArray() } });
        AdvanceEventRulesAndQueueFact(new SoleIncomingEffectIssuedEvent(grant));
    }

    private bool HasUnstartedSoleIncomingEffect(long id) => LifecycleCardUse(id) is { SoleIncomingEffects: { } r, Action: { } a } use &&
        a.ActionId == r.ActionId && r.Started < r.Grants.Count && !IsNamedAmbushUseIneffective(use) && _winner == Winner.None;
    private bool IsSoleIncomingExtraRound(long id) => LifecycleCardUse(id)?.SoleIncomingEffects is { Active: true };
    private void CaptureSoleIncomingAttack(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { SoleIncomingEffects: { Started: 0 } rounds, CardAttack: { } original } use ||
            !IsSlashCard(use.CardKind) || NativeExtraEffectRoundOrdinal(use.Id) != 0 ||
            rounds.Native.OriginalAttacks.Any(s => s.TargetIndex == use.TargetIndex)) return;
        UpdateLifecycleCardUse(use.Id, current => current with { SoleIncomingEffects = rounds with { Native = rounds.Native with
        { OriginalAttacks = rounds.Native.OriginalAttacks.Append(new(use.TargetIndex, original)).ToArray(),
            Fangtian = current.Continuations.FangtianHalberd ?? rounds.Native.Fangtian } } });
    }
    private void CompleteSoleIncomingExtraRound(long id)
    {
        if (LifecycleCardUse(id) is not { SoleIncomingEffects: { Active: true, Started: > 0 } r } use) return;
        UpdateLifecycleCardUse(id, current => current with { SoleIncomingEffects = r with { Active = false } });
        AdvanceEventRulesAndQueueFact(new SoleIncomingEffectRoundCompletedEvent(id, r.ActionId, r.Grants[r.Started - 1].ProgramFrameId, r.Started));
    }
    private bool TryBeginSoleIncomingExtraRound(long id, bool afterAttack)
    {
        if (!HasUnstartedSoleIncomingEffect(id) || LifecycleCardUse(id) is not { SoleIncomingEffects: { } r, Action: { } action } use ||
            use.ExtraCardEffect is { WholeGroupRepeated: false } || HasUnstartedFeiYiExtraEffect(id) || HasUnstartedZixiExtraEffect(id) ||
            HasUnstartedMoQuanyuExtraRound(id) || HasUnstartedMouBingcaiExtraEffect(id) ||
            r.Native.Trick is not null && HasRemainingSequentialTrickTargets(id) || r.Native.Simple is not null && HasRemainingAdjustedSimpleTargets(id)) return false;
        if (_resolutionStack.LastOrDefault()?.Id != id) throw new InvalidOperationException("An incoming extra effect cannot pass an unfinished native child.");
        var previous = NativeExtraEffectState(use);
        var native = r.Native with { WholeGroupRepeated = true, MaterialClaims = previous?.MaterialClaims ?? r.Native.MaterialClaims,
            FrozenGroupDamageSourceSeat = previous?.FrozenGroupDamageSourceSeat ?? r.Native.FrozenGroupDamageSourceSeat };
        ReplaceRuntimeFrame(id, use with { SoleIncomingEffects = r with { Started = r.Started + 1, Active = true, Native = native },
            TargetIndex = 0, Step = ResolutionFrameStep.ResolvingEffect, FireAttackSelection = null, ColorFireAttack = null,
            UnexpectedAssaultReveal = null, VirtualBasicEffectApplied = false });
        var grant = r.Grants[r.Started];
        AdvanceEventRulesAndQueueFact(new SoleIncomingEffectRoundStartedEvent(id, action.ActionId, grant.ProgramFrameId, r.Started + 1));
        AdvanceEventRulesAndQueueFact(new ExtraCardEffectStartedEvent(id, action.ActionId, 0, use.TargetSeats.FirstOrDefault(use.SourceSeat), use.CardKind));
        if (afterAttack) { ActiveCardAttack = null; ActiveDuel = null; ClearPendingDecision(); }
        if (IsSlashCard(use.CardKind))
        {
            var first = native.OriginalAttacks.OrderBy(s => s.TargetIndex).FirstOrDefault(s => _players[s.Attack.TargetSeat].IsAlive && !IsCardEffectIneffective(id, s.Attack.TargetSeat));
            if (first is null) { CompleteSoleIncomingExtraRound(id); return false; }
            var prepared = use.PreparedTargetAttacks?.Select((state, index) => native.OriginalAttacks.SingleOrDefault(s => s.TargetIndex == index)?.Attack ?? state).ToArray();
            UpdateLifecycleCardUse(id, current => current with { TargetIndex = first.TargetIndex,
                PreparedTargetAttacks = prepared is null ? null : Array.AsReadOnly(prepared) });
            if (native.Fangtian is { } fang) { UpdateCardContinuations(id, state => state with { FangtianHalberd = fang with { Active = true, CurrentAttackOwnerId = id } }); ActiveFangtianHalberd = new(this, id); }
            UpdateCardAttackState(id, _ => ResetExtraCardEffectAttack(first.Attack));
            var attack = new CardAttackHandle(this, id); ActiveCardAttack = attack; ContinueSlashDefense(attack); return true;
        }
        if (native.Trick is { } trick)
        {
            ActiveGroupCard = null; ActiveBorrowedSword = null;
            var card = GetTrickRepresentation(id, use.CardId, requireProcessing: true);
            BeginNullificationWindow(id, card, use.SourceSeat, use.TargetSeats, trick.ActionKind, null, trick.RequiredCardKind, use.CardKind); return true;
        }
        if (native.Simple is { } simple)
        {
            if (use.TargetSeats.Count > 1 && simple.Effect is SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol or SimpleCardUseEffect.GuanduIntoxication)
                UpdateLifecycleCardUse(id, current => current with { AdjustedSimpleContinuation = simple });
            ContinueSimpleCardUse(id, simple); return true;
        }
        throw new InvalidOperationException("An incoming repeat lost its accepted original effect continuation.");
    }
    private bool ContinueSoleIncomingFangtian(CardAttackHandle attack)
    {
        if (LifecycleCardUse(attack.ResolutionId) is not { SoleIncomingEffects: { Active: true } r } use) return false;
        var seed = r.Native.OriginalAttacks.SingleOrDefault(s => s.TargetIndex == use.TargetIndex);
        if (seed is null) { CompleteAttack(attack); return true; }
        UpdateCardAttackState(use.Id, _ => ResetExtraCardEffectAttack(seed.Attack)); ContinueSlashDefense(attack); return true;
    }
    [ProgramCapability]
    private sealed class SoleIncomingEffectCapability(GameEngine engine) : ISoleIncomingEffectRepeatHost
    { public void IssueSoleIncomingEffect(ProgramSkillFrame frame, int maximumDrawCount) => engine.IssueSoleIncomingEffect(frame, maximumDrawCount); }
}
