using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static string NamedAcquisitionReason(ProgramSkillFrame f) =>
        $"skill-program.{f.SkillId}.{SkillProgramEffectOp.LoseSkillsAndObtainNamedCard}";
    private IEnumerable<(Card Card, CardLocation From)> PublicNamedCardCandidates(CardKind kind)
    {
        foreach (var p in _players.Where(p => p.IsAlive))
            foreach (var zone in new[] { CardLocation.Equipment(p.Seat), CardLocation.Judgment(p.Seat) })
                foreach (var c in _cardZones.CardsAt(zone).Where(c => c.Kind == kind && !c.IsGeneralWeapon)) yield return (c, zone);
        foreach (var c in _cardZones.CardsAt(CardLocation.DiscardPile).Where(c => c.Kind == kind && !c.IsGeneralWeapon))
            yield return (c, CardLocation.DiscardPile);
    }
    private Card? DeckNamedCard(CardKind kind) => _cardZones.CardsAt(CardLocation.DrawPile).FirstOrDefault(c => c.Kind == kind && !c.IsGeneralWeapon);
    private IReadOnlyList<PromptChoice> NamedCardAcquisitionChoices(ProgramSkillFrame f)
    {
        if (f.NamedCardAcquisition is not { Stage: NamedCardAcquisitionStage.Selecting } d) return [];
        PromptChoice Choice(string suffix, string label, IReadOnlyList<int> cards, CardLocation from) =>
            new(new($"named-acquisition.{f.Id}.{suffix}"), label, cards, [], new Dictionary<string,string>
            { ["program-action"] = "named-card-acquisition", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["source-zone"] = from.Zone.ToString(), ["source-owner"] = (from.OwnerSeat ?? -1).ToString(CultureInfo.InvariantCulture) });
        var list = PublicNamedCardCandidates(d.PrintedKind).Select(c =>
            Choice($"public-{c.Card.Id}", $"获得{c.Card.DisplayName}（{c.From.Zone}）", [c.Card.Id], c.From)).ToList();
        if (DeckNamedCard(d.PrintedKind) is not null)
            list.Add(Choice("deck", "从牌堆获得指定牌", [], CardLocation.DrawPile));
        return Array.AsReadOnly(list.ToArray());
    }
    private SkillProgramStepOutcome BeginNamedCardAcquisition(ProgramSkillFrame supplied, SkillProgramEffect e)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.NamedCardAcquisition is not null || e.OutputKind is not { } kind || e.SkillIds.Contains(f.SkillId))
            throw new InvalidOperationException("A named-card acquisition cannot replace itself or overwrite an issued draft.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        var grants = _players[f.OwnerSeat].SkillGrants.Grants.Where(g => e.SkillIds.Contains(g.SkillId)).ToArray();
        ReplaceRuntimeTop(f = f with { NamedCardAcquisition = new(f.InstructionIndex, kind, NamedCardAcquisitionStage.Selecting, true) });
        foreach (var g in grants) _players[f.OwnerSeat].SkillGrants.RemoveGrant(g.GrantId);
        AdvanceEventRulesAndQueueFact(new ProgramSelectedSkillsLostEvent(f.Id, f.OwnerSeat, f.SkillId, grants.Length));
        if (NamedCardAcquisitionChoices(f).Count == 0)
        {
            AdvanceEventRulesAndQueueFact(new ProgramNamedCardAcquiredEvent(f.Id, f.OwnerSeat, kind, null, false));
            ReplaceRuntimeTop(f with { NamedCardAcquisition = null });
            return SkillProgramStepOutcome.Continue;
        }
        PublishNamedCardAcquisition(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private void PublishNamedCardAcquisition(ProgramSkillFrame f)
    {
        var choices = NamedCardAcquisitionChoices(f);
        if (choices.Count == 0)
        {
            AdvanceEventRulesAndQueueFact(new ProgramNamedCardAcquiredEvent(f.Id, f.OwnerSeat, f.NamedCardAcquisition!.PrintedKind, null, false));
            ReplaceRuntimeTop(f with { NamedCardAcquisition = null }); AdvanceRuntimeProgram(f.Id); return;
        }
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "选择指定牌的公开来源，或从牌堆获得。",
            choices.SelectMany(c => c.Cards).ToArray(), [], f.OwnerSeat)
        { PromptId = CreatePromptId(), Choices = choices, IsPrivate = false,
            SkillPrompt = new(f.SkillId, _contentRegistry.GetSkill(f.SkillId).Name, "获得指定牌", _contentRegistry.GetSkill(f.SkillId).Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveNamedCardAcquisitionChoice(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { NamedCardAcquisition: { Stage: NamedCardAcquisitionStage.Selecting } d } f ||
            _pendingDecision?.PlayerSeat != f.OwnerSeat || selected.Parameters.GetValueOrDefault("frame-id") != f.Id.ToString(CultureInfo.InvariantCulture) ||
            !NamedCardAcquisitionChoices(f).Any(c => c.Id == selected.Id && c.Cards.SequenceEqual(selected.Cards)) ||
            !Enum.TryParse<CardZoneKind>(selected.Parameters.GetValueOrDefault("source-zone"), out var zone))
            throw new InvalidOperationException("Named-card acquisition requires its exact currently published source choice.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { ReplaceRuntimeTop(f with { NamedCardAcquisition = null }); CancelProgramBindingAndCleanup(GetActiveProgramFrame(f.Id), "指定牌收益尚未发行，原来源已失效。"); return; }
        Card card; CardLocation from;
        if (zone == CardZoneKind.DrawPile)
        { card = DeckNamedCard(d.PrintedKind) ?? throw new InvalidOperationException("The declared deck kind disappeared."); from = CardLocation.DrawPile; }
        else
        { var candidate = PublicNamedCardCandidates(d.PrintedKind).Single(c => selected.Cards.SequenceEqual([c.Card.Id])); card = candidate.Card; from = candidate.From; }
        var before = _cardMovements.LastOrDefault()?.Sequence ?? 0;
        ReplaceRuntimeTop(f = f with { NamedCardAcquisition = d with { Stage = NamedCardAcquisitionStage.MovementChildren,
            CardId = card.Id, From = from, SequenceBefore = before, SequenceAfter = before },
            PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveCard(card, from, CardLocation.Hand(f.OwnerSeat), new(NamedAcquisitionReason(f)));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { NamedCardAcquisition = f.NamedCardAcquisition! with
            { SequenceAfter = _cardMovements.LastOrDefault()?.Sequence ?? before } });
        AdvanceEventRulesAndQueueFact(new ProgramNamedCardAcquiredEvent(f.Id, f.OwnerSeat, d.PrintedKind, from.Zone, true));
        if (!TryDrainFireTargetMovement(f)) ReturnRuntimeProgramMovement(f.Id);
    }
    private bool ValidNamedCardAcquisition(ProgramSkillFrame f)
    {
        if (f.NamedCardAcquisition is not { } d || !NamedAcquisitionParentMatches(f) || f.InstructionIndex != d.InstructionIndex ||
            ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!)
                .GetPausedInstruction(f.InstructionIndex).Effect is not { Op: SkillProgramEffectOp.LoseSkillsAndObtainNamedCard } effect ||
            effect.OutputKind != d.PrintedKind || !d.SkillsRemoved ||
            CompleteProgramEventHistory().OfType<ProgramSelectedSkillsLostEvent>().Count(e => e.ProgramFrameId == f.Id &&
                e.OwnerSeat == f.OwnerSeat && e.InitiatingSkillId == f.SkillId) != 1) return false;
        if (d.Stage == NamedCardAcquisitionStage.Selecting) return d.CardId is null && d.From is null;
        return d.Stage == NamedCardAcquisitionStage.MovementChildren && d.CardId is { } card && d.From is { } from && d.SequenceAfter > d.SequenceBefore &&
            from.Zone is CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.DrawPile or CardZoneKind.DiscardPile &&
            _cardMovements.Count(m => m.Sequence > d.SequenceBefore && m.Sequence <= d.SequenceAfter &&
                m.CardId == card && m.CardKind == d.PrintedKind && m.From == from && m.To == CardLocation.Hand(f.OwnerSeat) &&
                m.Reason.Value == NamedAcquisitionReason(f)) == 1;
    }
    private void AssertNamedCardAcquisition(ProgramSkillFrame f)
    {
        if (f.NamedCardAcquisition is not null && !ValidNamedCardAcquisition(f))
            throw new InvalidOperationException("The named-card acquisition lost its exact issuance or original movement.");
    }
    private bool ResumeNamedCardAcquisition(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { NamedCardAcquisition: { } d } f || f.Id != id) return false;
        AssertNamedCardAcquisition(f);
        if (d.Stage == NamedCardAcquisitionStage.Selecting)
        {
            if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            { ClearPendingDecision(); ReplaceRuntimeTop(f with { NamedCardAcquisition = null }); CancelProgramBindingAndCleanup(GetActiveProgramFrame(id), "指定牌收益尚未发行，原来源已失效。"); }
            else if (_pendingDecision is null) PublishNamedCardAcquisition(f);
            return true;
        }
        if (f.PendingMovementContinuation is not null)
        { if (!TryDrainFireTargetMovement(f)) ReturnNamedCardAcquisitionMovement(f); return true; }
        ReplaceRuntimeTop(f with { NamedCardAcquisition = null });
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
            CancelProgramBindingAndCleanup(GetActiveProgramFrame(id), "指定牌的已发行移动与子结算已结清。");
        else AdvanceRuntimeProgram(id);
        return true;
    }
    private bool ReturnNamedCardAcquisitionMovement(ProgramSkillFrame f)
    {
        if (f.NamedCardAcquisition is not { Stage: NamedCardAcquisitionStage.MovementChildren }) return false;
        AssertNamedCardAcquisition(f);
        if (TryDrainFireTargetMovement(f)) return true;
        ReplaceRuntimeTop(f with { PendingMovementContinuation = null }); ResumeNamedCardAcquisition(f.Id); return true;
    }
    private bool IsNamedCardAcquisitionMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation pending) =>
        f.NamedCardAcquisition is { Stage: NamedCardAcquisitionStage.MovementChildren } d && d.InstructionIndex == f.InstructionIndex &&
        e?.Op == SkillProgramEffectOp.LoseSkillsAndObtainNamedCard && pending.SubjectSeat == f.OwnerSeat && pending.CoverageResultBind is null;
}
