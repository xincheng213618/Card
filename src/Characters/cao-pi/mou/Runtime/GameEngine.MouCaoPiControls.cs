namespace CardGame.Core;

public sealed partial record PlayerSnapshot
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<MouCaoPiControl>? MouCaoPiControls
    {
        get => _mouCaoPiControls;
        init => _mouCaoPiControls = value is null ? null : Array.AsReadOnly(value.ToArray());
    }
    private readonly IReadOnlyList<MouCaoPiControl>? _mouCaoPiControls;
}

public sealed partial class GameEngine
{
    // Issued lasting restrictions, independent of the source skill and pending
    // instruction. Normal command replay rebuilds this per-match state.
    private readonly List<MouCaoPiControl> _mouCaoPiControls = [];
    private IReadOnlyList<MouCaoPiControl>? MouCaoPiControlsSnapshot(int seat)
    { var controls = ActiveMouCaoPiControls(seat).ToArray(); return controls.Length == 0 ? null : Array.AsReadOnly(controls); }
    private IEnumerable<MouCaoPiControl> ActiveMouCaoPiControls(int seat)
    {
        return _mouCaoPiControls.Where(c => c.TargetSeat == seat);
    }
    private bool MouCaoPiSkillsQualified(CharacterState owner, SkillGrant grant) => grant.SourceId.StartsWith("equipment:", StringComparison.Ordinal) ||
        !ActiveMouCaoPiControls(owner.Seat).Any(c => c.Kind == MouCaoPiControlKind.SkillsInvalid);

    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 28000)]
    private void ExpireMouCaoPiControls(TurnEndedEvent e)
    {
        foreach (var c in ActiveMouCaoPiControls(e.ActorSeat).Where(c => e.TurnNumber > c.GrantedTurn).ToArray())
        {
            _mouCaoPiControls.Remove(c);
            _currentTurnSkillSuppressionRevision++;
            AdvanceEventRulesAndQueueFact(new MouCaoPiControlExpiredEvent(c.FrameId, c.TargetSeat, e.TurnNumber));
        }
    }
    private bool MouCaoPiCannotRespondTo(int responder, int source) => responder != source &&
        ActiveMouCaoPiControls(responder).Any(c => c.Kind == MouCaoPiControlKind.CannotRespond);
    private int? MouCaoPiResponseSource() => _resolutionStack.OfType<NullificationWindowFrame>().LastOrDefault() is { } counter
        ? counter.ChainDepth == 0 ? counter.SourceSeat : ProgramEventHistory<CardActionAcceptedEvent>()
            .LastOrDefault(e => e.Action.EffectiveKind == CardKind.Nullification)?.Action.ActorSeat ?? counter.SourceSeat
        : ActiveCardAttack?.CardUserSeat ?? _resolutionStack.OfType<CardUseFrame>().LastOrDefault()?.SourceSeat;
    private bool MouCaoPiResponseForbidden(int actor, CardKind kind, CardActionType type) =>
        (type == CardActionType.Response || kind == CardKind.Nullification) && MouCaoPiResponseSource() is { } source && MouCaoPiCannotRespondTo(actor, source);

    // Only actual uses of hand material are restricted. Pure plays (e.g. Duel's
    // Slash), discard costs, equipment conversions and material-free cards remain legal.
    private bool MouCaoPiHandUseForbidden(int actor, CardKind effectiveKind, IEnumerable<int> ids, bool response = false)
    {
        if (response && effectiveKind != CardKind.Nullification && !IsProgramResponseCardUse(_players[actor], effectiveKind)) return false;
        if (!ids.Any(id => _cardZones.GetLocation(id) == CardLocation.Hand(actor))) return false;
        var category = GetProgramCardCategory(effectiveKind);
        return ActiveMouCaoPiControls(actor).Any(c => c.Kind switch
        {
            MouCaoPiControlKind.BasicHandOnly => category != SkillProgramCardCategory.Basic,
            MouCaoPiControlKind.TrickHandOnly => category != SkillProgramCardCategory.Trick,
            MouCaoPiControlKind.EquipmentHandOnly => category != SkillProgramCardCategory.Equipment,
            _ => false
        });
    }

    [ProgramCardLegality(ProgramCardLegalityStage.BeforeTurnUseBan, 40)]
    private bool MouCaoPiCardUseForbidden(ProgramCardUseQuery query) =>
        MouCaoPiResponseForbidden(query.ActorSeat, query.Kind, query.ActionType);

    [ProgramCardLegality(ProgramCardLegalityStage.AfterLegalActionKind, 20)]
    private bool MouCaoPiLegalActionForbidden(ProgramLegalActionQuery query) =>
        query.Action.Kind != LegalActionKind.Recast && MouCaoPiHandUseForbidden(query.Actor.Seat,
            query.EffectiveKind!.Value, query.Action.CardId is { } material && material != 0 ? [material] : []);

    [ProgramCardLegality(ProgramCardLegalityStage.MaterialUseBan, 10)]
    private bool MouCaoPiMaterialUseForbidden(ProgramCardMaterialQuery query) =>
        MouCaoPiHandUseForbidden(query.ActorSeat, query.Kind, query.CardIds, query.IsResponse);
}
