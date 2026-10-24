namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string AnqiFeirenSkill = "shanhe:anqi-feiren";
    private bool HasAnqiFeirenCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveAnqiFeiren);

    private bool ResolveAnqiFeirenEquipmentSkillGrant(Card card, CardLocation from, CardLocation to)
    {
        if (card.Kind != CardKind.AnqiFeiren) return false;
        var grantId = EquipmentSkillGrantId(card)!;
        if (from is { Zone: CardZoneKind.Equipment, OwnerSeat: { } previous })
            _players[previous].SkillGrants.RemoveGrant(grantId);
        if (to is { Zone: CardZoneKind.Equipment, OwnerSeat: { } next } && _contentRegistry.Skills.ContainsKey(AnqiFeirenSkill))
            _players[next].SkillGrants.Grant(new SkillGrant(grantId, AnqiFeirenSkill, grantId, grantId));
        return true;
    }

    private Card? ActiveAnqiFeiren(int owner, string skillId, string instanceId) => skillId != AnqiFeirenSkill ? null :
        GetEquipment(_players[owner]).SingleOrDefault(card => card.Kind == CardKind.AnqiFeiren &&
            IsActiveProgramSourceEquipmentCard(owner, skillId, instanceId, card));

    private string[] AnqiFeirenCandidates(int target) => GetSkillBindingShard(_players[target]).ActiveGrants
        .Where(grant => !grant.IsModeRule && !_contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Locked))
        .Select(grant => grant.SkillId).Where(id => id != "standard:none")
        .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    private bool HasSpentAnqiFeiren(int owner, int target) => ProgramEventHistory<AnqiFeirenSuppressedEvent>()
        .Any(e => e.Source.OwnerSeat == owner && e.TargetSeat == target && e.TurnNumber == _turnNumber && e.TurnOwnerSeat == _currentSeat);

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 5781, SkillProgramEffectOp.ResolveAnqiFeiren)]
    private bool CanOfferAnqiFeiren(ProgramTriggerLegalityContext gate) =>
        ActiveAnqiFeiren(gate.OwnerSeat, gate.SkillId, gate.Candidate.SkillInstanceId) is not null &&
        _turnNumber > 0 && _turnProgression.TurnNumber == _turnNumber && _turnProgression.OwnerSeat == _currentSeat &&
        gate.WindowContext is { Window: SkillProgramTriggerWindow.BeforeDamageApplied, SourceSeat: { } source, TargetSeat: { } target, Amount: > 0 } &&
        source == gate.OwnerSeat && source != target && _players[target].IsAlive &&
        CurrentDamageAttempt is { IsSourceLess: false } attack && attack.SourceSeat == source && attack.TargetSeat == target &&
        _resolutionStack.OfType<BeforeDamageProgramWindowFrame>().SingleOrDefault(w => w.Id == gate.Context.ParentFrameId) is { Prevented: false, Amount: > 0 } &&
        !HasSpentAnqiFeiren(source, target) && AnqiFeirenCandidates(target).Length > 0;

    private SkillProgramStepOutcome ResolveAnqiFeiren(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || f.WindowContext is not { Window: SkillProgramTriggerWindow.BeforeDamageApplied, TargetSeat: { } target, Amount: > 0 } context ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not BeforeDamageProgramWindowFrame window ||
            window.Id != context.ParentFrameId || window.Prevented || window.SourceSeat != f.OwnerSeat || window.TargetSeat != target || window.Amount <= 0 ||
            CurrentDamageAttempt is not { IsSourceLess: false } damage || damage.SourceSeat != f.OwnerSeat || damage.TargetSeat != target ||
            target == f.OwnerSeat || !_players[target].IsAlive || _turnNumber < 1 ||
            _turnProgression.TurnNumber != _turnNumber || _turnProgression.OwnerSeat != _currentSeat ||
            ActiveAnqiFeiren(f.OwnerSeat, f.SkillId, f.SkillInstanceId) is not { } weapon || HasSpentAnqiFeiren(f.OwnerSeat, target))
            throw new InvalidOperationException("Anqi Feiren lost its actual equipped source, positive native damage window or per-target actual-turn opportunity.");
        var candidates = AnqiFeirenCandidates(target);
        if (candidates.Length == 0) return SkillProgramStepOutcome.Continue;
        var index = _random.Next(candidates.Length);
        AdvanceEventRulesAndQueueFact(new AnqiFeirenSuppressedEvent(f.Id,
            new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), f.GameplayHash,
            window.Id, window.ParentFrameId, weapon.Id, target, _turnNumber, _currentSeat, candidates[index], index, candidates.Length));
        _currentTurnSkillSuppressionRevision++;
        return SkillProgramStepOutcome.Continue;
    }

    private bool IsAnqiFeirenGrantQualified(CharacterState owner, SkillGrant grant) =>
        !HasAnqiFeirenCapability || grant.IsModeRule || _contentRegistry.GetSkill(grant.SkillId).Tags.HasFlag(SkillTag.Locked) ||
        !ProgramEventHistory<AnqiFeirenSuppressedEvent>().Any(e => e.TargetSeat == owner.Seat && e.SuppressedSkillId == grant.SkillId &&
            !ProgramEventHistory<AnqiFeirenExpiredEvent>().Any(expired => expired.SuppressionFrameId == e.FrameId));

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 5781)]
    private void ExpireAnqiFeiren(TurnEndedEvent turn)
    {
        if (!HasAnqiFeirenCapability) return;
        foreach (var suppressed in ProgramEventHistory<AnqiFeirenSuppressedEvent>().Where(e => e.TargetSeat == turn.ActorSeat &&
                     turn.TurnNumber >= e.TurnNumber && !ProgramEventHistory<AnqiFeirenExpiredEvent>().Any(x => x.SuppressionFrameId == e.FrameId)).ToArray())
        {
            AdvanceEventRulesAndQueueFact(new AnqiFeirenExpiredEvent(suppressed.FrameId, suppressed.TargetSeat, turn.TurnNumber));
            _currentTurnSkillSuppressionRevision++;
        }
    }

    private sealed partial class ProgramSkillHost : IAnqiFeirenHost
    {
        public SkillProgramStepOutcome ResolveAnqiFeiren(ProgramSkillFrame frame) => engine.ResolveAnqiFeiren(frame);
    }
}
