namespace CardGame.Core;

public sealed record ProgramDyingAlcoholPermission(long DyingFrameId, int ActorSeat, int ResponderSeat,
    string SkillId, string BindingId, string SkillInstanceId, string GameplayHash, SkillProgramTriggerWindow Window);

public sealed partial class GameEngine
{
    private bool HasAlcoholKingIdentityPolicy(CharacterState owner) =>
        HasCardPolicy(owner, SkillProgramCardPolicyKind.AlcoholKingIdentityRank, CardKind.Alcohol);

    private int? CaptureAlcoholIdentityRank(CharacterState owner, Card card) => card.Kind == CardKind.Alcohol &&
        (_cardZones.GetLocation(card.Id) == CardLocation.Hand(owner.Seat) || _cardZones.GetLocation(card.Id) == CardLocation.WoodenOxGrain(owner.Seat)) &&
        HasAlcoholKingIdentityPolicy(owner) ? 13 : null;

    private int EffectiveOwnedCardRank(CharacterState owner, Card card) => CaptureAlcoholIdentityRank(owner, card) ?? card.Rank;

    private int? CaptureAlcoholPindianRank(PindianFrame frame, CharacterState owner, Card card, bool source, bool top)
    {
        if (card.Kind != CardKind.Alcohol || !HasAlcoholKingIdentityPolicy(owner) || !ReferenceEquals(_resolutionStack.LastOrDefault(), frame) ||
            frame.PindianStep != PindianStep.ChooseOpponentCard || owner.Seat != (source ? frame.SourceSeat : frame.OpponentSeat)) return null;
        // Both arguments are the actual validated selections in RevealPindian,
        // including a precisely reserved source top or the opponent's current top.
        if (source && (frame.SourceCardId != card.Id || frame.SourceUsesDrawPileTop != top) ||
            _cardZones.GetLocation(card.Id) != (top ? source ? CardLocation.Processing : CardLocation.DrawPile : CardLocation.Hand(owner.Seat)) ||
            source && top && (frame.ParentProcessingCardIds ?? []).Contains(card.Id)) return null;
        return 13;
    }

    private bool IsEffectiveHandKindExempt(CharacterState owner, Card card, IReadOnlySet<CardKind> kinds) =>
        kinds.Contains(card.Kind) || card.Kind == CardKind.Alcohol && kinds.Contains(CardKind.Slash) && HasAlcoholKingIdentityPolicy(owner);

    private int? CaptureAlcoholActionRank(int providerSeat, CardKind kind, IReadOnlyList<int> ids)
    {
        if (!IsSlashCard(kind) || ids.Count != 1) return null;
        var card = _cardZones.CardsAt(_cardZones.GetLocation(ids[0])).Single(c => c.Id == ids[0]);
        return CaptureAlcoholIdentityRank(_players[providerSeat], card);
    }

    private int? CaptureAlcoholResponseRank(CharacterState provider, CardKind kind, IReadOnlyList<CardActionCost> costs) =>
        IsSlashCard(kind) && costs.Count == 1 && costs[0].CardKind == CardKind.Alcohol &&
        costs[0].From.OwnerSeat == provider.Seat && costs[0].From.Zone is CardZoneKind.Hand or CardZoneKind.WoodenOxGrain &&
        HasAlcoholKingIdentityPolicy(provider) ? 13 : null;

    private bool IsForeignTurnAlcoholUseForbidden(int actorSeat, CardKind kind, CardActionType type) =>
        kind == CardKind.Alcohol && type == CardActionType.Use && _turnNumber > 0 && actorSeat != _currentSeat &&
        _players[_currentSeat].IsAlive && HasCardPolicy(_players[_currentSeat], SkillProgramCardPolicyKind.ForeignTurnAlcoholUseProhibition, CardKind.Alcohol);

    // Only this opted-in identity extends the existing hand identity to its
    // owner's playable Wooden Ox grain. Other identities keep their old zones.
    private bool IsAlcoholIdentityOwnedLocation(CharacterState owner, Card card) =>
        card.Kind == CardKind.Alcohol && _cardZones.GetLocation(card.Id) == CardLocation.WoodenOxGrain(owner.Seat) && HasAlcoholKingIdentityPolicy(owner);

    private static bool HasDyingAlcoholProducer(SkillProgramTrigger trigger) =>
        ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.UseBoundCardAsDyingAlcohol) ||
        ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.UseVirtualDyingAlcohol) ||
        ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.UseRoundPricedPileDyingAlcohol);

    private bool CanRunProgramDyingAlcoholPolicy(SkillProgramTrigger trigger, ProgramSkillWindowContext context, int responder)
    {
        if (!HasDyingAlcoholProducer(trigger)) return true;
        if (context.Window is not (SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.SelfDyingResponse) ||
            ActiveDying is not { } dying || dying.FrameId != context.ParentFrameId || dying.ResponderSeat != responder ||
            context.TargetSeat != dying.VictimSeat) return false;
        // A pile provider does not use Wine: its exact dying victim is the actor.
        return !IsForeignTurnAlcoholUseForbidden(dying.VictimSeat, CardKind.Alcohol, CardActionType.Use);
    }

    private ProgramDyingAlcoholPermission? CaptureProgramDyingAlcoholPermission(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        // Keep historical producer state unchanged when this new policy is absent.
        if (!_contentRegistry.ProgramDependencies.HasCardPolicy(SkillProgramCardPolicyKind.ForeignTurnAlcoholUseProhibition)) return null;
        var trigger = GetProgramTrigger(candidate);
        if (!HasDyingAlcoholProducer(trigger)) return null;
        if (!CanRunProgramDyingAlcoholPolicy(trigger, context, candidate.OwnerSeat) || ActiveDying is not { } dying)
            throw new InvalidOperationException("A prohibited Wine producer cannot issue a dying-use permission.");
        return new(dying.FrameId, dying.VictimSeat, candidate.OwnerSeat, candidate.SkillId, candidate.BindingId,
            candidate.SkillInstanceId, candidate.GameplayHash, context.Window);
    }

    private bool HasFrozenProgramDyingAlcoholPermission(ProgramSkillFrame frame, SkillProgramEffectOp op)
    {
        if (frame.DyingAlcoholPermission is not { } permission || frame.WindowContext is not { } context ||
            ActiveDying is not { } dying || permission.DyingFrameId != dying.FrameId || context.ParentFrameId != dying.FrameId ||
            permission.ActorSeat != dying.VictimSeat || permission.ResponderSeat != dying.ResponderSeat || permission.ResponderSeat != frame.OwnerSeat ||
            permission.SkillId != frame.SkillId || permission.BindingId != frame.TriggerId || permission.SkillInstanceId != frame.SkillInstanceId ||
            permission.GameplayHash != frame.GameplayHash || permission.Window != context.Window || context.TargetSeat != dying.VictimSeat ||
            context.Window != (op == SkillProgramEffectOp.UseVirtualDyingAlcohol ? SkillProgramTriggerWindow.SelfDyingResponse : SkillProgramTriggerWindow.DyingResponse)) return false;
        var trigger = _contentRegistry.GetSkill(frame.SkillId).Program!.Triggers.Single(t => t.Id == frame.TriggerId);
        return ProgramInstructionResolver.Default.Features(trigger).HasOperation(op);
    }

    private bool CanUseProgramDyingAlcohol(ProgramSkillFrame frame, SkillProgramEffectOp op) =>
        HasFrozenProgramDyingAlcoholPermission(frame, op) || ActiveDying is { } dying &&
        !IsForeignTurnAlcoholUseForbidden(dying.VictimSeat, CardKind.Alcohol, CardActionType.Use);
}
