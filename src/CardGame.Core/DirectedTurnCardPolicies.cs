namespace CardGame.Core;

public sealed record DirectedTurnCardPolicy(
    long GrantSequence, int TurnNumber, int TurnSeat, long ParentFrameId, int EffectIndex,
    CardUseEffectSource Source, int ActorSeat, int TargetSeat,
    IReadOnlyList<CardKind> CardKinds, DirectedTurnCardPolicyEffect Effects);

public sealed record DirectedTurnCardPolicyGrantedEvent(DirectedTurnCardPolicy Policy) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<DirectedTurnCardPolicy> _directedTurnCardPolicies = [];
    private long _directedTurnCardPolicySequence;

    private void GrantProgramDirectedTurnCardPolicy(ProgramSkillFrame frame,
        ProgramParticipantReference actorReference, ProgramParticipantReference targetReference,
        IReadOnlyList<CardKind> cardKinds, DirectedTurnCardPolicyEffect effects)
    {
        ValidateProgramTurnEffectGrant(frame);
        var actorSeat = ResolveProgramParticipant(frame, actorReference);
        var targetSeat = ResolveProgramParticipant(frame, targetReference);
        if (actorSeat == targetSeat || effects == DirectedTurnCardPolicyEffect.None)
            throw new InvalidOperationException("A directed turn policy requires distinct participants and effects.");
        var effectIndex = frame.InstructionIndex - 1;
        var source = CreateProgramTurnEffectSource(frame);
        var normalizedKinds = cardKinds.Distinct().Order().ToArray();
        var existing = _directedTurnCardPolicies.SingleOrDefault(item =>
            item.ParentFrameId == frame.Id && item.EffectIndex == effectIndex);
        if (existing is not null)
        {
            if (existing.Source != source || existing.ActorSeat != actorSeat || existing.TargetSeat != targetSeat ||
                !existing.CardKinds.SequenceEqual(normalizedKinds) || existing.Effects != effects)
                throw new InvalidOperationException("A directed-policy grant key changed its meaning.");
            return;
        }
        var policy = new DirectedTurnCardPolicy(++_directedTurnCardPolicySequence, _turnNumber, _currentSeat,
            frame.Id, effectIndex, source, actorSeat, targetSeat,
            Array.AsReadOnly(normalizedKinds), effects);
        _directedTurnCardPolicies.Add(policy);
        QueueGameEvent(new DirectedTurnCardPolicyGrantedEvent(policy));
    }

    private bool HasDirectedTurnCardPolicy(int actorSeat, int targetSeat, CardKind cardKind,
        DirectedTurnCardPolicyEffect effect) =>
        _directedTurnCardPolicies.Any(item => item.TurnNumber == _turnNumber && item.TurnSeat == _currentSeat &&
            item.ActorSeat == actorSeat && item.TargetSeat == targetSeat &&
            (item.CardKinds.Count == 0 || item.CardKinds.Contains(cardKind)) &&
            item.Effects.HasFlag(effect) &&
            HasRuntimeSkillInstance(_players[item.Source.OwnerSeat], item.Source.SkillId, item.Source.SkillInstanceId));

    private bool IsDirectedCardTargetProhibited(int actorSeat, int targetSeat, CardKind cardKind) =>
        HasDirectedTurnCardPolicy(actorSeat, targetSeat, cardKind, DirectedTurnCardPolicyEffect.ForbidTarget);

    private void ExpireDirectedTurnCardPolicies(int turnNumber, int turnSeat) =>
        _directedTurnCardPolicies.RemoveAll(item => item.TurnNumber == turnNumber && item.TurnSeat == turnSeat);
}
