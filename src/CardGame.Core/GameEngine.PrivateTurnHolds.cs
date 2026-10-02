namespace CardGame.Core;
public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IPrivateTurnHoldProgramHost
    { public SkillProgramStepOutcome HoldOwnerHandUntilTurnEnd(ProgramSkillFrame f) => engine.HoldOwnerHandUntilTurnEnd(f); }
    private SkillProgramStepOutcome HoldOwnerHandUntilTurnEnd(ProgramSkillFrame f)
    {
        if (f.PrivateTurnHoldDraft is not null) throw new InvalidOperationException("A hold cost cannot be paid twice.");
        var cards = GetHand(_players[f.OwnerSeat]).ToArray();
        if (cards.Length == 0) return SkillProgramStepOutcome.Continue;
        var grant = _players[f.OwnerSeat].SkillGrants.Grants.Single(g => g.SkillId == f.SkillId && g.SkillInstanceId == f.SkillInstanceId);
        var location = new CardLocation(CardZoneKind.PrivateTurnHold, f.OwnerSeat,
            privateTurnHold: new(f.Id,f.SkillId,f.SkillInstanceId,grant.SourceId,_turnNumber));
        _cardZones.EnsurePrivateTurnHold(location);
        ReplaceRuntimeTop(GetActiveProgramFrame(f.Id) with { PrivateTurnHoldDraft = new(location,true), PendingMovementContinuation = new(f.OwnerSeat,0,null) });
        MoveCards(cards,CardLocation.Hand(f.OwnerSeat),location,new("skill.private-turn-hold.pay"));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private bool ResumePrivateTurnHold(long id)
    {
        var f = GetActiveProgramFrame(id);
        if (f.PrivateTurnHoldDraft is not {} draft) return false;
        if (!draft.Paid || draft.Location.PrivateTurnHold?.HoldId != id) throw new InvalidOperationException("A private hold lost its paid owning draft.");
        if (TryBeginCardsMovedProgramWindow()) return true;
        ReplaceRuntimeTop(f with { PrivateTurnHoldDraft = null });
        return false;
    }
    private bool HasActualTurnEndMovementPrelude => HasAfterTurnEndedPrograms || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.HoldOwnerHandUntilTurnEnd);
    private void ReturnPrivateTurnHoldsAtTurnEnd()
    {
        foreach (var location in _cardZones.PrivateTurnHoldLocations.Where(l => l.PrivateTurnHold!.ExpiresTurnNumber <= _turnNumber))
            MoveCards(_cardZones.CardsAt(location).ToArray(),location,
                _players[location.OwnerSeat!.Value].IsAlive ? CardLocation.Hand(location.OwnerSeat.Value) : CardLocation.DiscardPile,
                new("skill.private-turn-hold.return"));
    }
    private IReadOnlyList<PrivateTurnHoldSnapshot>? ProjectPrivateTurnHolds(int owner,int viewer,bool revealAll)
    {
        var holds = _cardZones.PrivateTurnHoldLocations.Where(l => l.OwnerSeat == owner).Select(l =>
        {
            var h = l.PrivateTurnHold!; var cards = _cardZones.CardsAt(l);
            return new PrivateTurnHoldSnapshot(h.HoldId,owner,h.SkillId,h.SkillInstanceId,h.SourceId,h.ExpiresTurnNumber,cards.Count,
                owner == viewer || revealAll ? Array.AsReadOnly(cards.Select(ToSnapshot).ToArray()) : null);
        }).ToArray();
        return holds.Length == 0 ? null : Array.AsReadOnly(holds);
    }
}
