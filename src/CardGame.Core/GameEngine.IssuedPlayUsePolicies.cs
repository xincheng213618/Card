namespace CardGame.Core;

public sealed record IssuedPlayPhaseUseProhibition(CardConversionSource Source, int ActorSeat, int TurnNumber, int PhaseInstanceId, long CardActionId);
public sealed record IssuedCardNoResponse(long CardActionId, long ParentFrameId, CardConversionSource Source, int TurnNumber, int PhaseInstanceId);
public sealed record IssuedCounterspellNode(long ActionId, int ChainDepth, IssuedCardNoResponse Policy);
public sealed record IssuedCardNoResponseEvent(IssuedCardNoResponse Effect) : IGameEvent;

public sealed partial class GameEngine
{
    // Issued phase facts survive source loss/suppression until this actual phase ends.
    private readonly List<IssuedPlayPhaseUseProhibition> _issuedPlayPhaseUseProhibitions = [];
    private bool HasFirstActualPlayUseDistance(CharacterState actor) =>
        TracksActualPlayPhaseCardUses && _phase == TurnPhase.Play && actor.Seat == _currentSeat &&
        GetActualPlayPhaseUseCount(actor.Seat) == 0 && HasCardPolicy(actor, SkillProgramCardPolicyKind.FirstActualPlayUseDistanceUnlimited);
    private bool HasIssuedFirstPlayUseDistance(long? frameId) => frameId is { } id &&
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == id)?.FirstOwnPlayUseDistanceUnlimited == true;
    private bool HasIssuedPlayPhaseUseBan(int seat) => _winner == Winner.None && _phase == TurnPhase.Play && seat == _currentSeat &&
        _issuedPlayPhaseUseProhibitions.Any(policy => policy.ActorSeat == seat && policy.TurnNumber == _turnNumber && policy.PhaseInstanceId == _cardUseDebitPhaseInstanceId);
    private void CleanupIssuedPlayPhaseUseBans() => _issuedPlayPhaseUseProhibitions.RemoveAll(policy =>
        _winner != Winner.None || _phase != TurnPhase.Play || policy.ActorSeat != _currentSeat || policy.TurnNumber != _turnNumber || policy.PhaseInstanceId != _cardUseDebitPhaseInstanceId);
    private bool HasIssuedPolicyForAction(long actionId) => _issuedPlayPhaseUseProhibitions.Any(policy => policy.CardActionId == actionId);
    private bool IsIssuedCardUnrespondable(long frameId) =>
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == frameId) is { Action: { } action, IssuedNoResponse: { } issued } &&
        issued.CardActionId == action.ActionId && issued.ParentFrameId == frameId;

    private sealed partial class ProgramSkillHost : IIssuedPlayUseProgramHost
    {
        public void IssueCardNoResponseAndPlayUseBan(ProgramSkillFrame frame) => engine.IssueCardNoResponseAndPlayUseBan(frame);
    }
    private void IssueCardNoResponseAndPlayUseBan(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not { Window: SkillProgramTriggerWindow.CardUseCommitted, CardUse: { } context } window ||
            _resolutionStack.Count < 2 || _resolutionStack[^2] is not ProgramCardTriggerWindowFrame parent ||
            parent.Id != window.ParentFrameId || parent.Action.ActionId != context.CardActionId ||
            parent.Action.ActorSeat != frame.OwnerSeat || window.OwnerSeat != frame.OwnerSeat ||
            _phase != TurnPhase.Play || _currentSeat != frame.OwnerSeat ||
            !_actualPlayPhaseUseActions.Contains(context.CardActionId) || !IsEligibleIssuedNoResponseCard(parent.Action.EffectiveKind) || HasIssuedPolicyForAction(context.CardActionId))
            throw new InvalidOperationException("Issued card policy requires its exact own Play-use committed parent.");
        var isResponse = parent.Action.Type == CardActionType.Response;
        if (isResponse && (parent.CompletedResponseReturn is not { IsCommitted: true } typed || !IsCompletedResponseUse(parent.Action, typed.Kind) || !IsCompletedResponseParentConsistent(parent)))
            throw new InvalidOperationException("Issued response policy lost its exact typed use parent.");
        var action = parent.Action;
        var source = new CardConversionSource(frame.SkillId, frame.TriggerId!, frame.OwnerSeat, frame.SkillInstanceId);
        var issued = new IssuedCardNoResponse(action.ActionId, parent.ParentFrameId, source, _turnNumber, _cardUseDebitPhaseInstanceId);
        if (!isResponse)
        {
            var index = _resolutionStack.FindIndex(item => item.Id == parent.ParentFrameId);
            if (index < 0 || _resolutionStack[index] is not CardUseFrame use || use.Action?.ActionId != action.ActionId ||
                parent.Continuation is not (ProgramCardContinuation.CommittedSlash or ProgramCardContinuation.CommittedTrick or ProgramCardContinuation.CommittedSimpleCard) || use.IssuedNoResponse is not null)
                throw new InvalidOperationException("Issued card policy lost its actual owning card-use frame.");
            ReplaceRuntimeFrame(use.Id, use with { IssuedNoResponse = issued });
        }
        else if (parent.CompletedResponseReturn?.Kind == ProgramCompletedResponseKind.Nullification)
        {
            var pending = ActiveNullificationWindow ?? throw new InvalidOperationException("Issued counterspell lost its chain.");
            var index = _resolutionStack.FindIndex(item => item.Id == pending.Id);
            if (index < 0 || _resolutionStack[index] is not NullificationWindowFrame counter || counter.IssuedNoResponseNode is not null)
                throw new InvalidOperationException("Issued counterspell lost its owning frame.");
            ReplaceRuntimeFrame(counter.Id, counter with { IssuedNoResponseNode = new(action.ActionId, pending.ChainDepth, issued) });
        }
        _issuedPlayPhaseUseProhibitions.Add(new(source, frame.OwnerSeat, _turnNumber, _cardUseDebitPhaseInstanceId, action.ActionId));
        AdvanceEventRulesAndQueueFact(new IssuedCardNoResponseEvent(issued));
    }
    private static bool IsEligibleIssuedNoResponseCard(CardKind kind) => CardCatalog.Get(kind).CategoryName == "基本牌" || IsOrdinaryTrick(kind);
}
