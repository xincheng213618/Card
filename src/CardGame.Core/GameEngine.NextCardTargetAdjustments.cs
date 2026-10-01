namespace CardGame.Core;

public sealed partial class GameEngine
{
    private readonly Dictionary<int, CardConversionSource> _nextCardTargetAdjustmentOwners = [];
    private IReadOnlyList<int>? _selectedNextCardTargetSeats;

    private bool HasRemainingAdjustedSimpleTargets(long frameId) => (LifecycleCardUse(frameId)?.AdjustedSimpleContinuation is not null) &&
        _resolutionStack.OfType<CardUseFrame>().Any(frame => frame.Id == frameId && frame.TargetIndex + 1 < frame.TargetSeats.Count);

    private bool TryContinueAdjustedSimpleCardUse(long frameId)
    {
        if (LifecycleCardUse(frameId)?.AdjustedSimpleContinuation is not { } continuation) return false;
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == frameId);
        if (frame.TargetIndex + 1 >= frame.TargetSeats.Count || _winner != Winner.None)
        { UpdateLifecycleCardUse(frameId, use => use with { AdjustedSimpleContinuation = null }); return false; }
        SetCardUseTargetIndex(frameId, frame.TargetIndex + 1);
        ContinueSimpleCardUse(frameId, continuation);
        return true;
    }

    private bool HasRemainingAdjustedBorrowedSwordTargets(long frameId) => _winner == Winner.None && (LifecycleCardUse(frameId)?.TargetsAdjusted == true) &&
        _resolutionStack.OfType<CardUseFrame>().Any(frame => frame.Id == frameId && frame.CardKind == CardKind.BorrowedSword &&
            frame.TargetSeats.Count >= 4 && frame.TargetSeats.Count % 2 == 0 && frame.TargetIndex + 2 < frame.TargetSeats.Count);

    private bool TryContinueAdjustedBorrowedSwordUse(long frameId)
    {
        if (!HasRemainingAdjustedBorrowedSwordTargets(frameId)) return false;
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == frameId);
        SetCardUseTargetIndex(frameId, frame.TargetIndex + 2);
        var card = _cardZones.CardsAt(CardLocation.Processing).Single(item => item.Id == frame.CardId);
        BeginJizhiOrNullificationWindow(frameId, card, frame.SourceSeat,
            frame.TargetSeats.Skip(frame.TargetIndex + 2).Take(2).ToArray(), LegalActionKind.BorrowedSword);
        return true;
    }

    private sealed partial class ProgramSkillHost : INextCardTargetAdjustmentProgramHost
    {
        public void GrantNextCardTargetAdjustment(ProgramSkillFrame frame) => engine.GrantNextCardTargetAdjustment(frame);
    }

    private void GrantNextCardTargetAdjustment(ProgramSkillFrame frame)
    {
        ValidateProgramTurnEffectGrant(frame);
        _nextCardTargetAdjustmentOwners[frame.OwnerSeat] = new(frame.SkillId, "next-card-target-adjustment", frame.OwnerSeat, frame.SkillInstanceId);
    }

    private void ObserveNextCardTargetAdjustmentEvent(IGameEvent payload)
    {
        if (payload is TurnStartedEvent)
        {
            _nextCardTargetAdjustmentOwners.Clear();
            _redAdditionalTargetGrants.Clear();
        }
        else if (payload is CardUseDeclaredEvent used)
            _nextCardTargetAdjustmentOwners.Remove(used.SourceSeat);
    }

    private bool HasLegacyNextCardTargetAdjustment(CharacterState owner) =>
        _nextCardTargetAdjustmentOwners.TryGetValue(owner.Seat, out var source) &&
        HasRuntimeSkillInstance(owner, source.SkillId, source.SkillInstanceId);

    private void AddNextCardTargetAdjustmentActions(List<LegalAction> actions, CharacterState actor)
    {
        if (!HasLegacyNextCardTargetAdjustment(actor)) return;
        var source = _nextCardTargetAdjustmentOwners[actor.Seat];
        var ordinary = actions.ToArray();
        foreach (var action in ordinary.Where(item => item.CardId is not null && item.Kind != LegalActionKind.Recast))
        {
            var normal = action.Kind is LegalActionKind.DrawTwo or LegalActionKind.Peach or LegalActionKind.Alcohol ? new[] { actor.Seat }
                : GetDeclaredCardTargets(actor, action.Kind, action.TargetSeats).ToArray();
            if (normal.Length == 0) continue;
            var kind = action.PlayedCardKind ?? FindOwnedPlayableCard(actor, action.CardId)!.Kind;
            if (kind == CardKind.BorrowedSword)
            {
                foreach (var owner in _players.Where(player => player.IsAlive && player.Seat != actor.Seat &&
                    player.Seat != action.TargetSeats[0] && GetWeapon(player) is not null &&
                    !IsDirectedCardTargetProhibited(actor.Seat, player.Seat, kind) &&
                    !IsCardTargetProhibited(player, kind, FindOwnedPlayableCard(actor, action.CardId)!.Suit)))
                    foreach (var victim in _players.Where(player => IsLegalBorrowedSwordSlashTarget(owner, player)))
                        Add([.. action.TargetSeats, owner.Seat, victim.Seat]);
                continue;
            }
            // Placement cards retain their legal target
            // contract; their actual use still consumes the next-card grant.
            if (EquipmentCatalog.IsEquipment(kind) || kind is
                CardKind.Indulgence or CardKind.SupplyShortage or CardKind.Lightning or CardKind.BorrowedSword) continue;
            var possible = action.Kind is LegalActionKind.BarbarianAssault or LegalActionKind.ArrowBarrage or
                LegalActionKind.PeachGarden or LegalActionKind.FiveGrains ? normal : _players
                    .Where(player => CanBeExtraNextCardTarget(actor, player, action, kind)).Select(player => player.Seat).ToArray();
            foreach (var seat in possible.Except(normal)) Add(normal.Append(seat).ToArray());
            if (normal.Length > 1)
                foreach (var seat in normal) Add(normal.Where(item => item != seat).ToArray());

            void Add(IReadOnlyList<int> targets)
            {
                if (targets.Count == 0 || actions.Any(item => item.CardId == action.CardId && item.Kind == action.Kind &&
                    item.PlayedCardKind == action.PlayedCardKind && item.ConversionSource == action.ConversionSource &&
                    item.TargetCardId == action.TargetCardId && item.TargetSeats.SequenceEqual(targets))) return;
                actions.Add(action with
                {
                    TargetSeat = targets[0], TargetSeats = targets,
                    ProgramSkillId = source.SkillId, ProgramActivationId = source.BindingId,
                    Description = action.Description + "（下一张牌增减一个目标）"
                });
            }
        }
    }

    private bool CanBeExtraNextCardTarget(CharacterState actor, CharacterState target, LegalAction action, CardKind kind, Suit? effectiveUseSuit = null)
    {
        var physical = FindOwnedPlayableCard(actor, action.CardId)!;
        if (!target.IsAlive || IsDirectedCardTargetProhibited(actor.Seat, target.Seat, kind) ||
            IsCardTargetProhibited(target, kind, effectiveUseSuit ?? physical.Suit) ||
            HasBeneficiarySuitShield(actor.Seat, target.Seat, effectiveUseSuit ?? EffectiveSuit(actor, ApplyProgramUseAppearance(actor, physical, action.ConversionSource)))) return false;
        return action.Kind switch
        {
            LegalActionKind.Slash => CanUseSlashTarget(actor, target, physical, action.ConversionSource, kind, ignoreDistance: true),
            LegalActionKind.Peach => target.Hp < target.MaxHp,
            LegalActionKind.Alcohol or LegalActionKind.DrawTwo or LegalActionKind.IronChain => true,
            LegalActionKind.Duel => target.Seat != actor.Seat,
            LegalActionKind.FireAttack => GetHand(target).Count > 0,
            LegalActionKind.Dismantlement or LegalActionKind.Snatch => target.Seat != actor.Seat &&
                GetHand(target).Count + GetEquipment(target).Count + GetJudgment(target).Count > 0,
            _ => false
        };
    }
}
