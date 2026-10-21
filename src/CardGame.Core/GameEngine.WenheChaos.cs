using System.Text.Json.Serialization;

namespace CardGame.Core;

public sealed record WenheChaosReceipt(long ParentFrameId, long? CardActionId, int RoundNumber, int TurnNumber)
{
    private readonly IReadOnlyList<int> _actors = Array.Empty<int>();
    public IReadOnlyList<int> ActorSeats { get => _actors; init => _actors = Array.AsReadOnly(value.ToArray()); }
}
public sealed record WenheChaosIssuedEvent(long ProgramFrameId, long ParentFrameId, long? CardActionId,
    int OwnerSeat, string SkillInstanceId, string GameplayHash, int RoundNumber, int TurnNumber, int ActorCount) : IGameEvent;
public sealed record WenheChaosActorEvent(long ProgramFrameId, int Index, int Seat) : IGameEvent;
public sealed partial record ProgramSkillFrame
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WenheChaosReceipt? WenheChaos { get; init; }
}
public sealed partial class GameEngine
{
    private void BeginWenheCardChaos(CardUseFrame use) => BeginWenheChaos(null, use);
    private void BeginWenheChaos(ProgramSkillFrame? roundParent, CardUseFrame? cardParent)
    {
        if ((roundParent is null) == (cardParent is null) ||
            cardParent is { WenheCardEffect.Stage: not WenheCardEffectStage.AwaitingChaos } ||
            roundParent is { WenheRuleFlow.Stage: not 3 })
            throw new InvalidOperationException("Chaos requires one already issued native parent.");
        var parentId = roundParent?.Id ?? cardParent!.Id;
        var ownerSeat = roundParent?.OwnerSeat ?? cardParent!.SourceSeat;
        if (_resolutionStack.LastOrDefault()?.Id != parentId) throw new InvalidOperationException("Chaos cannot change its live parent.");
        var living = _players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray();
        var start = cardParent is null && living.Length > 0 ? living[_random.Next(living.Length)] : TableStepSeat(ownerSeat, 1);
        var actors = Enumerable.Range(0, _playerCount).Select(offset => TableStepSeat(start, offset))
            .Where(seat => _players[seat].IsAlive && (cardParent is null || seat != ownerSeat)).ToArray();
        const string skillId = "wenhe:chaos-resolution";
        var program = _contentRegistry.GetSkill(skillId).Program ?? throw new InvalidOperationException("Chaos program is missing.");
        var skillInstance = GetRuntimeSkillInstanceId(_players[ownerSeat], skillId);
        var child = new ProgramSkillFrame(++_resolutionSequence, ownerSeat, skillId, "card-effect", program.GameplayHash, 0, [], [])
        { SkillInstanceId = skillInstance, WenheChaos = new(parentId, cardParent?.Action?.ActionId, _roundNumber, _turnNumber) { ActorSeats = actors } };
        PushRuntimeFrame(child);
        AdvanceEventRulesAndQueueFact(new WenheChaosIssuedEvent(child.Id, parentId, child.WenheChaos.CardActionId,
            ownerSeat, skillInstance, program.GameplayHash, _roundNumber, _turnNumber, actors.Length));
        for (var i = 0; i < actors.Length; i++) AdvanceEventRulesAndQueueFact(new WenheChaosActorEvent(child.Id, i, actors[i]));
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(child.Id, ownerSeat, skillId, "card-effect"));
        AdvanceRuntimeProgram(child.Id);
    }
    private bool IsExactWenheChaos(ProgramSkillFrame frame)
    {
        if (frame.WenheChaos is not { } r || frame.SkillId != "wenhe:chaos-resolution" || frame.TriggerId is not null ||
            frame.ActivationId != "card-effect" || frame.SelectedCardIds.Count != 0 || r.RoundNumber != _roundNumber ||
            r.TurnNumber != _turnNumber || r.ActorSeats is not System.Collections.IList { IsReadOnly: true } ||
            r.ActorSeats.Any(seat => !IsValidPlayerSeat(seat) || r.CardActionId is not null && seat == frame.OwnerSeat) ||
            r.ActorSeats.Distinct().Count() != r.ActorSeats.Count ||
            frame.GameplayHash != _contentRegistry.GetSkill(frame.SkillId).Program!.GameplayHash ||
            ProgramEventHistory<WenheChaosIssuedEvent>().Count(e => e.ProgramFrameId == frame.Id && e.ParentFrameId == r.ParentFrameId &&
                e.CardActionId == r.CardActionId && e.OwnerSeat == frame.OwnerSeat && e.SkillInstanceId == frame.SkillInstanceId &&
                e.GameplayHash == frame.GameplayHash && e.RoundNumber == r.RoundNumber && e.TurnNumber == r.TurnNumber && e.ActorCount == r.ActorSeats.Count) != 1 ||
            r.ActorSeats.Where((seat, index) => ProgramEventHistory<WenheChaosActorEvent>().Count(e => e.ProgramFrameId == frame.Id && e.Index == index && e.Seat == seat) != 1).Any()) return false;
        var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
        if (index < 1 || _resolutionStack[index - 1].Id != r.ParentFrameId) return false;
        return r.CardActionId is { } actionId ? _resolutionStack[index - 1] is CardUseFrame
            { CardKind: CardKind.WenheChaos, WenheCardEffect.Stage: WenheCardEffectStage.AwaitingChaos, Action: { } action } use &&
                use.SourceSeat == frame.OwnerSeat && action.ActionId == actionId :
            _resolutionStack[index - 1] is ProgramSkillFrame { WenheRuleFlow: { Operation: SkillProgramEffectOp.ResolveWenheRoundStart, Stage: 3 } } parent &&
                parent.OwnerSeat == frame.OwnerSeat;
    }
    private bool TryCompleteWenheChaosProgram(ProgramSkillFrame frame, bool completed)
    {
        if (frame.WenheChaos is not { } r) return false;
        if (!IsExactWenheChaos(frame)) throw new InvalidOperationException("Chaos lost its exact native return.");
        AdvanceEventRulesAndQueueFact(new ProgramSkillResolvedEvent(frame.Id, frame.OwnerSeat, frame.SkillId, frame.ActivationId, completed));
        PopResolutionFrame(frame.Id, ResolutionFrameKind.ProgramSkill);
        if (r.CardActionId is not null) CompleteWenheCardChaos(r.ParentFrameId);
        else
        {
            var parent = GetActiveProgramFrame(r.ParentFrameId);
            ReplaceRuntimeTop(parent with { WenheRuleFlow = null });
            AdvanceRuntimeProgram(parent.Id);
        }
        return true;
    }
    private bool WenheChaosFrameRidesOn(ResolutionFrame child, ResolutionFrame parent) =>
        child is ProgramSkillFrame { WenheChaos: { } r } frame && r.ParentFrameId == parent.Id && IsExactWenheChaos(frame);
    private bool HasWenheChaosPrompt() => _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.WenheChaos is not null && IsExactWenheChaos(f));
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 41001)]
    private void AssertWenheChaosFrame(ProgramSkillFrame f)
    { if (f.WenheChaos is not null && !IsExactWenheChaos(f)) throw new InvalidOperationException("Chaos actor queue lost its native parent or frozen issuance."); }
}
