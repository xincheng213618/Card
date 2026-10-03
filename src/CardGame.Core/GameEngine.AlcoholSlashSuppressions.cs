namespace CardGame.Core;

// These public receipts contain no physical card IDs or private hand state.
public sealed record CardUseAlcoholConsumptionCapturedEvent(long CardUseFrameId, long ActionId, int ActorSeat) : IGameEvent;
public sealed record CurrentTurnOwnSkillSuppression(int TurnNumber, int TurnOwnerSeat, long ParentFrameId,
    int EffectIndex, CardUseEffectSource Source, string SuppressedSkillId, long CardUseFrameId, long CardActionId);
public sealed record CurrentTurnOwnSkillSuppressionIssuedEvent(CurrentTurnOwnSkillSuppression Suppression) : IGameEvent;
public sealed record CurrentTurnOwnSkillSuppressionsExpiredEvent(int TurnNumber, int TurnOwnerSeat) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<CurrentTurnOwnSkillSuppression> _currentTurnOwnSkillSuppressions = [];

    // A catalog opt-in keeps earlier fingerprints' event/frame shape unchanged.
    // The bit describes actual consumption by this original action, never a inferred damage amount.
    private void CaptureProgramAlcoholConsumption(long cardUseFrameId, CharacterState actor)
    {
        if (!actor.HasAlcoholEffect ||
            !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.SuppressOwnSkillAfterAlcoholSlashDamage)) return;
        var use = LifecycleCardUse(cardUseFrameId);
        if (use?.Action is not { Type: CardActionType.Use } action || action.ActorSeat != actor.Seat ||
            use.SourceSeat != actor.Seat || !IsSlashCard(action.EffectiveKind) || use.CardKind != action.EffectiveKind)
            throw new InvalidOperationException("Alcohol consumption requires its original real Slash use and actor.");
        if (use.ConsumedAlcoholBoost) throw new InvalidOperationException("A Slash cannot consume its Alcohol effect twice.");
        ReplaceRuntimeFrame(use.Id, use with { ConsumedAlcoholBoost = true });
        AdvanceEventRulesAndQueueFact(new CardUseAlcoholConsumptionCapturedEvent(use.Id, action.ActionId, actor.Seat));
    }

    private bool CanRunAlcoholSlashSuppression(SkillProgramTrigger trigger, ProgramSkillWindowContext context,
        int ownerSeat) => !trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.SuppressOwnSkillAfterAlcoholSlashDamage) ||
        TryAlcoholSlashSuppressionUse(context, ownerSeat, out _);

    private bool TryAlcoholSlashSuppressionUse(ProgramSkillWindowContext context, int ownerSeat, out CardUseFrame use)
    {
        use = null!;
        if (_winner != Winner.None || !IsValidPlayerSeat(ownerSeat) || !_players[ownerSeat].IsAlive ||
            context.Window != SkillProgramTriggerWindow.AfterDamageApplied || context.OwnerSeat != ownerSeat ||
            context.SourceSeat != ownerSeat ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().LastOrDefault(f => f.Id == context.ParentFrameId) is not { } window ||
            window.TriggerWindow != SkillProgramTriggerWindow.AfterDamageApplied || window.SourceSeat != ownerSeat ||
            ActiveDamageTrigger?.Id != window.Id) return false;
        var attack = GetDamageTriggerAttack(window);
        var original = LifecycleCardUse(attack.ResolutionId);
        if (attack.IsSourceLess || attack.IsChainPropagation || attack.SourceSeat != ownerSeat ||
            attack.CardUserSeat != ownerSeat || attack.EffectiveCardKind is not { } kind || !IsSlashCard(kind) ||
            original is not { ConsumedAlcoholBoost: true, Action: { Type: CardActionType.Use } action } ||
            action.ActorSeat != ownerSeat || action.EffectiveKind != kind || original.SourceSeat != ownerSeat ||
            original.CardKind != kind || window.TargetSeat != attack.TargetSeat || context.TargetSeat != attack.TargetSeat) return false;
        use = original;
        return true;
    }

    private void SuppressOwnSkillAfterAlcoholSlashDamage(ProgramSkillFrame frame, string skillId)
    {
        ValidateProgramTurnEffectGrant(frame);
        var active = GetActiveProgramFrame(frame.Id);
        if (!ReferenceEquals(active, _resolutionStack.LastOrDefault()) || active.WindowContext is not { } context ||
            !TryAlcoholSlashSuppressionUse(context, active.OwnerSeat, out var use) ||
            !HasRuntimeSkillInstance(_players[active.OwnerSeat], active.SkillId, active.SkillInstanceId) ||
            !_contentRegistry.Skills.ContainsKey(skillId) ||
            _resolutionStack.OfType<DamageTriggerWindowFrame>().Single(f => f.Id == context.ParentFrameId) is not { } window ||
            window.CandidateIndex >= window.Candidates.Count || window.Candidates[window.CandidateIndex].ToProgramCandidate() is not { } candidate ||
            candidate.OwnerSeat != active.OwnerSeat || candidate.SkillId != active.SkillId || candidate.BindingId != active.TriggerId ||
            candidate.SkillInstanceId != active.SkillInstanceId || candidate.GameplayHash != active.GameplayHash)
            throw new InvalidOperationException("An own-skill suppression requires the exact live source instance and paid Alcohol Slash damage candidate.");
        var plan = ProgramInstructionResolver.Default.Resolve(active, _contentRegistry.GetSkill(active.SkillId).Program!);
        if (active.InstructionIndex < 1 || active.InstructionIndex > plan.Instructions.Count ||
            plan.Instructions[active.InstructionIndex - 1] is not
                { Op: SkillProgramEffectOp.SuppressOwnSkillAfterAlcoholSlashDamage } effect ||
            !effect.SkillIds.SequenceEqual([skillId]) || _turnProgression.TurnNumber != _turnNumber ||
            _turnProgression.OwnerSeat != _currentSeat)
            throw new InvalidOperationException("An own-skill suppression lost its instruction or actual turn.");
        var issued = new CurrentTurnOwnSkillSuppression(_turnNumber, _currentSeat, active.Id,
            active.InstructionIndex - 1, CreateProgramTurnEffectSource(active), skillId, use.Id, use.Action!.ActionId);
        var prior = _currentTurnOwnSkillSuppressions.SingleOrDefault(s => s.ParentFrameId == issued.ParentFrameId &&
            s.EffectIndex == issued.EffectIndex);
        if (prior is not null)
        {
            if (prior != issued) throw new InvalidOperationException("An own-skill suppression issue changed identity.");
            return;
        }
        _currentTurnOwnSkillSuppressions.Add(issued);
        _currentTurnSkillSuppressionRevision++;
        AdvanceEventRulesAndQueueFact(new CurrentTurnOwnSkillSuppressionIssuedEvent(issued));
    }

    // An issued fact affects only its issuer's named skill, including locked grants.
    // It continues until this actual turn ends if the issuer is later suppressed.
    private bool IsCurrentTurnOwnSkillGrantQualified(CharacterState owner, SkillGrant grant) =>
        !_currentTurnOwnSkillSuppressions.Any(s => s.TurnNumber == _turnNumber && s.TurnOwnerSeat == _currentSeat &&
            s.Source.OwnerSeat == owner.Seat && s.SuppressedSkillId == grant.SkillId);

    private void ExpireCurrentTurnOwnSkillSuppressions(int turnNumber, int turnOwnerSeat)
    {
        if (_currentTurnOwnSkillSuppressions.RemoveAll(s => s.TurnNumber == turnNumber && s.TurnOwnerSeat == turnOwnerSeat) == 0) return;
        _currentTurnSkillSuppressionRevision++;
        AdvanceEventRulesAndQueueFact(new CurrentTurnOwnSkillSuppressionsExpiredEvent(turnNumber, turnOwnerSeat));
    }

    private void AssertCurrentTurnOwnSkillSuppressions()
    {
        if (_currentTurnOwnSkillSuppressions.Any(s => s.TurnNumber != _turnNumber || s.TurnOwnerSeat != _currentSeat ||
                s.ParentFrameId <= 0 || s.EffectIndex < 0 || s.CardUseFrameId <= 0 || s.CardActionId <= 0 ||
                !IsValidPlayerSeat(s.Source.OwnerSeat) || string.IsNullOrWhiteSpace(s.Source.SkillId) ||
                string.IsNullOrWhiteSpace(s.Source.BindingId) || string.IsNullOrWhiteSpace(s.Source.SkillInstanceId) ||
                !_contentRegistry.Skills.ContainsKey(s.SuppressedSkillId)) ||
            _currentTurnOwnSkillSuppressions.GroupBy(s => (s.ParentFrameId, s.EffectIndex)).Any(group => group.Count() != 1))
            throw new InvalidOperationException("An own-skill suppression lost its frozen identity or actual-turn expiry.");
        foreach (var use in _resolutionStack.OfType<CardUseFrame>().Where(f => f.ConsumedAlcoholBoost))
            if (use.Action is not { Type: CardActionType.Use } action || action.ActorSeat != use.SourceSeat ||
                action.EffectiveKind != use.CardKind || !IsSlashCard(use.CardKind))
                throw new InvalidOperationException("An Alcohol consumption receipt lost its original Slash action.");
    }

    private sealed partial class ProgramSkillHost : IAlcoholSlashSuppressionProgramHost
    {
        public void SuppressOwnSkillAfterAlcoholSlashDamage(ProgramSkillFrame frame, string skillId) =>
            engine.SuppressOwnSkillAfterAlcoholSlashDamage(frame, skillId);
        public SkillProgramStepOutcome StartOwnedDamagePointJudgment(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.BeginOwnedDamagePointJudgment(frame, effect);
    }
}
