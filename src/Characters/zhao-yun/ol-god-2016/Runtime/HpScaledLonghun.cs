namespace CardGame.Core;

/// <summary>Only public scalar cost facts; no unplayed hand-card identities enter these events.</summary>
public sealed record HpScaledProgramSelectionEvent(long FrameId, CardConversionSource Source, string GameplayHash,
    int OwnerHp, int MaterialCount) : IGameEvent;
public sealed record HpScaledCardUsePaidEvent(long ActionId, CardConversionSource Source, string GameplayHash,
    int OwnerHp, int MaterialCount) : IGameEvent;

internal static class HpScaledLonghunComposition
{
    internal static void ValidateViewAs(string path, SkillProgramViewAs r)
    {
        if (r.InputCountExpression is null) return;
        var suit = r.OutputKind switch
        {
            CardKind.Peach => Suit.Heart, CardKind.FireSlash => Suit.Diamond,
            CardKind.Dodge => Suit.Club, CardKind.Nullification => Suit.Spade, _ => Suit.None
        };
        if (r.InputCountExpression != SkillProgramCardCountExpression.OwnerHpAtLeastOne || r.InputCount != 1 || r.VariableInputCount ||
            suit == Suit.None || !r.InputSuits.SequenceEqual([suit]) || r.InputKinds.Count != 0 || r.InputCategories.Count != 0 ||
            !r.SourceZones.SequenceEqual([CardZoneKind.Hand, CardZoneKind.Equipment]) || !r.ExtendedUse ||
            r.ForPlay != (r.OutputKind is CardKind.Peach or CardKind.FireSlash) || !r.ForResponse ||
            !r.AllowSameKind || r.Condition.Kind != SkillProgramConditionKind.Always ||
            r.AllowChainedInput || r.SameSuit || r.InheritPreviousPlaySuit || r.AllHandCardsOfSelectedSuit || r.DistanceUnlimited ||
            r.UsesPerPhase is not null || r.UsesPerActualTurn is not null || r.UsageGroup is not null || r.ActivationUsageGroup is not null ||
            r.DamageBonus != 0 || r.RecoveryBonus != 0 || r.CostDestination is not null || r.UseOnly || r.DeclaredEntity ||
            r.SingleCardTrickUse || r.ExcludeOwnerEffects || r.NoDying || r.UnusedOutputThisTurn || r.UnusedOutputNameThisGame ||
            r.NameLedgerId is not null || r.ConversionStateId is not null || r.SourcePileSkillId is not null ||
            r.DeclarationValidation is not null || r.TieredRoundConversion is not null || r.RoundOnceCardlessBasic is not null ||
            r.LeiluanBasicUse is not null || r.XieweiBaitBasicUse is not null || r.ChainedStateCost is not null ||
            r.RoundDistinctBasicUse is not null || r.DrawFundedDistinctBasic is not null || r.DisableSkillUntilTurnEndIfNoDamage is not null ||
            r.LastInSourceZone is not null || r.RoundGainedRedHandOnly is not null || r.KenshangUse is not null ||
            r.FieldMountConversion is not null || r.EqualNameLengthUse || r.RankTwoDamageUse || r.NonBasicUnrespondableSlash || r.UseEffectiveInputSuit is not null)
            throw new InvalidOperationException(path + ": HP-scaled Longhun requires its native same-suit hand/equipment cost and plain original four conversions.");
    }

    [ProgramCatalogValidation(40650)] internal static void Validate(string path, SkillProgram p)
    {
        foreach (var a in p.Activations.Where(a => a.CardCountExpression == SkillProgramCardCountExpression.OwnerHpAtLeastOne))
        {
            if (a.Effects is not [{ Op: SkillProgramEffectOp.UseSelectedCardsAs, SourceBind: { } binding, OutputKind: { } output }] ||
                p.ViewAs.SingleOrDefault(r => r.Id == binding) is not { InputCountExpression: SkillProgramCardCountExpression.OwnerHpAtLeastOne } r ||
                output != r.OutputKind || a.MinCards != 1 || a.MaxCards != int.MaxValue ||
                !a.SourceZones.SequenceEqual(r.SourceZones) || a.UsesPerPhase is not null || a.UsesPerTurn is not null || a.UsesPerGame is not null ||
                a.MinTargets != (output == CardKind.FireSlash ? 1 : 0) || a.MaxTargets != a.MinTargets ||
                a.TargetKind != SkillProgramTargetKind.OtherLiving || a.SelectedCardsSameSuit || a.SelectedCardsDistinctSuits)
                throw new InvalidOperationException(path + ": a HP-scaled activation must select exactly its current native conversion cost without a second payment or quota.");
        }
    }
}

public sealed partial class GameEngine
{
    // Undefined-HP characters cannot pay a quantity defined by current HP.
    private static int HpScaledCardCount(CharacterState owner) => owner.HasHpValue ? Math.Max(1, owner.Hp) : int.MaxValue;
    private static int EffectiveProgramViewAsInputCount(CharacterState owner, SkillProgramViewAs rule) => rule.InputCountExpression switch
    {
        null => rule.InputCount,
        SkillProgramCardCountExpression.OwnerHpAtLeastOne => HpScaledCardCount(owner),
        _ => throw new InvalidOperationException("Unsupported native conversion card-count expression.")
    };

    [ProgramCardLegality(ProgramCardLegalityStage.ViewAsBan, 40650)]
    private bool HpScaledViewAsCountForbidden(ProgramViewAsLegalityQuery q) => ViewAsRule(q.Source) is { InputCountExpression: not null } r &&
        (q.Source.OwnerSeat != q.Owner.Seat || q.CardIds.Count != EffectiveProgramViewAsInputCount(q.Owner, r));

    private void IssueHpScaledProgramSelection(ProgramSkillFrame f, SkillProgramActivation a)
    {
        if (a.CardCountExpression != SkillProgramCardCountExpression.OwnerHpAtLeastOne) return;
        var owner = _players[f.OwnerSeat];
        if (f.SelectedCardIds.Count != HpScaledCardCount(owner))
            throw new InvalidOperationException("A HP-scaled conversion must revalidate its live material count before creating its original program frame.");
        AdvanceEventRulesAndQueueFact(new HpScaledProgramSelectionEvent(f.Id,
            new(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId), f.GameplayHash, owner.Hp, f.SelectedCardIds.Count));
    }

    private bool HpScaledProgramSelectionIsExact(ProgramSkillFrame f) =>
        ProgramEventHistory<HpScaledProgramSelectionEvent>().Where(e => e.FrameId == f.Id).ToArray() is [var e] &&
        e.Source == new CardConversionSource(f.SkillId, f.ActivationId, f.OwnerSeat, f.SkillInstanceId) && e.GameplayHash == f.GameplayHash &&
        e.MaterialCount == f.SelectedCardIds.Count && e.MaterialCount == Math.Max(1, e.OwnerHp) &&
        ProgramEventHistory<ProgramSkillStartedEvent>().Count(s => s.FrameId == f.Id && s.OwnerSeat == f.OwnerSeat &&
            s.SkillId == f.SkillId && s.ActivationId == f.ActivationId) == 1;

    private CardActionContext CaptureHpScaledCardUseAction(CardActionContext action)
    {
        foreach (var source in action.ConversionChain)
        {
            if (ViewAsRule(source) is not { InputCountExpression: not null } r) continue;
            var owner = _players[action.ProviderSeat];
            if (source.OwnerSeat != owner.Seat || action.PhysicalCards.Count != EffectiveProgramViewAsInputCount(owner, r))
                throw new InvalidOperationException("A HP-scaled native use must pay its current HP-defined material count.");
            AdvanceEventRulesAndQueueFact(new HpScaledCardUsePaidEvent(action.ActionId, source,
                _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash, owner.Hp, action.PhysicalCards.Count));
        }
        return action;
    }

    private bool HasPaidHpScaledViewAsAction(CardActionContext action) => action.ConversionChain is [var source] &&
        ViewAsRule(source) is { InputCountExpression: SkillProgramCardCountExpression.OwnerHpAtLeastOne } &&
        ProgramEventHistory<HpScaledCardUsePaidEvent>().Where(e => e.ActionId == action.ActionId).ToArray() is [var e] &&
        e.Source == source && e.GameplayHash == _contentRegistry.GetSkill(source.SkillId).Program!.GameplayHash &&
        e.MaterialCount == action.PhysicalCards.Count && e.MaterialCount == Math.Max(1, e.OwnerHp) && source.OwnerSeat == action.ProviderSeat &&
        ProgramEventHistory<CardActionAcceptedEvent>().Any(accepted => accepted.Action.ActionId == action.ActionId &&
            accepted.Action.ActorSeat == action.ActorSeat && accepted.Action.ProviderSeat == action.ProviderSeat && accepted.Action.EffectiveKind == action.EffectiveKind &&
            accepted.Action.ConversionChain.SequenceEqual(action.ConversionChain) && accepted.Action.PhysicalCards.SequenceEqual(action.PhysicalCards));
}
