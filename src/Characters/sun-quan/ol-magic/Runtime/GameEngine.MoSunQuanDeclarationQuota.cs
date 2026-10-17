namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsMoQuanyuTrueSlashDeclaration(DeclaredCardPayment paid, CardDeclarationParentReturn parent) =>
        IsSlashCard(paid.DeclaredKind) && parent.Purpose is
            (CardDeclarationPurpose.Use or CardDeclarationPurpose.BorrowedSword or CardDeclarationPurpose.ProvidedSlash or
             CardDeclarationPurpose.BorrowedSwordProvidedSlash or CardDeclarationPurpose.QinglongProvidedSlash);

    private DeclaredCardPayment CaptureMoQuanyuDeclarationQuota(DeclaredCardPayment paid,
        CardDeclarationParentReturn parent, IReadOnlyList<int> targets)
    {
        if (!HasMoQuanyuSlash || !IsMoQuanyuTrueSlashDeclaration(paid, parent) || targets is not [var target]) return paid;
        if (paid.MoQuanyuDeclarationQuota is not null || paid.Claimed || paid.OwnerFrameId != paid.DeclarationId ||
            paid.ActorSeat != parent.ActorSeat || !IsValidPlayerSeat(parent.ActorSeat) || !IsValidPlayerSeat(target) ||
            paid.Cost.From != CardLocation.Hand(paid.ProviderSeat) || _cardZones.GetLocation(paid.Cost.CardId) != paid.Cost.From ||
            ProgramEventHistory<MoQuanyuDeclarationQuotaCapturedEvent>().Any(e => e.DeclarationId == paid.DeclarationId))
            throw new InvalidOperationException("Meteor must freeze the original unpaid declaration and its exact initial singleton once.");
        var kind = parent.FinalEffectiveKind ?? paid.DeclaredKind;
        if (!IsSlashCard(kind)) return paid;
        CardConversionSource? source = null; string? hash = null; var mask = 0; var entered = false;
        var actor = _players[parent.ActorSeat];
        var hasSource = TryGetMoQuanyuSource(actor.Seat, out var liveSource, out var liveHash);
        var eligible = _setupComplete && _winner == Winner.None && _turnNumber > 0 && _phase == TurnPhase.Play &&
            _currentSeat == actor.Seat && actor.IsAlive && _players[target].IsAlive && actor.Seat != target &&
            hasSource &&
            (MoQuanyuSlashMask(actor.Seat, target) & MoQuanyuBit(MoQuanyuEffect.Liuxing)) != 0;
        if (eligible)
        {
            source = liveSource; hash = liveHash;
            mask = MoQuanyuSlashMask(actor.Seat, target); entered = MoQiangangEntered(actor.Seat);
        }
        var q = new MoQuanyuDeclarationQuota(paid.DeclarationId, actor.Seat, paid.ProviderSeat, paid.Cost.CardId,
            paid.Cost.From, paid.Source, paid.DeclaredKind, kind, target, _turnNumber, _turnProgression.OwnerSeat,
            _movementSequence, eligible, source, hash, mask, entered);
        AdvanceEventRulesAndQueueFact(new MoQuanyuDeclarationQuotaCapturedEvent(paid.DeclarationId, paid.OwnerFrameId, q));
        return paid with { MoQuanyuDeclarationQuota = q };
    }

    // Called immediately after the native declaration payment, before its
    // movement window can reuse an entity or change the source's availability.
    private void RecordMoQuanyuDeclarationPayment(long declarationId)
    {
        if (_resolutionStack.LastOrDefault() is not CardDeclarationFrame { Stage: CardDeclarationStage.Paying } f ||
            f.Id != declarationId || f.Payment.MoQuanyuDeclarationQuota is not { } q) return;
        if (ProgramEventHistory<MoQuanyuDeclarationPaymentRecordedEvent>().Any(e => e.DeclarationId == declarationId) ||
            q.DeclarationId != declarationId || ProgramEventHistory<MoQuanyuDeclarationQuotaCapturedEvent>()
                .Count(e => e.DeclarationId == declarationId && e.OwnerFrameId == declarationId && e.Receipt == q) != 1)
            throw new InvalidOperationException("Meteor cannot replace a declaration's original physical payment invoice.");
        var moves = _cardMovements.Where(m => m.Sequence > q.SequenceBefore && m.CardId == q.CardId &&
            m.CardKind == f.Payment.Cost.CardKind && m.From == q.From && m.To == CardLocation.Processing &&
            m.Reason.Value == "conversion.declaration.pay").ToArray();
        if (moves is not [var paid]) throw new InvalidOperationException("Meteor requires the one genuine native declaration payment.");
        AdvanceEventRulesAndQueueFact(new MoQuanyuDeclarationPaymentRecordedEvent(declarationId, paid.Sequence));
    }

    private bool ValidMoQuanyuDeclarationQuota(DeclaredCardPayment paid, MoQuanyuDeclarationQuota q)
    {
        if (paid.MoQuanyuDeclarationQuota != q || paid.DeclarationId != q.DeclarationId || q.DeclarationId <= 0 ||
            paid.ActorSeat != q.ActorSeat || paid.ProviderSeat != q.ProviderSeat || paid.Cost.CardId != q.CardId ||
            paid.Cost.From != q.From || paid.Source != q.DeclarationSource || paid.DeclaredKind != q.DeclaredKind ||
            !IsValidPlayerSeat(q.ActorSeat) || !IsValidPlayerSeat(q.ProviderSeat) || !IsValidPlayerSeat(q.TargetSeat) ||
            q.From != CardLocation.Hand(q.ProviderSeat) || !IsSlashCard(q.DeclaredKind) || !IsSlashCard(q.OutputKind) ||
            q.ActualTurn < 1 || q.ActualTurn > _turnNumber || !IsValidPlayerSeat(q.TurnOwner) || q.SequenceBefore < 0 ||
            q.SequenceBefore > _movementSequence ||
            _resolutionStack.SingleOrDefault(f => f.Id == paid.OwnerFrameId) is not { } owner ||
            !(owner.AcceptedDeclarationPayment == paid || owner is CardDeclarationFrame d && d.Payment == paid) ||
            ProgramEventHistory<MoQuanyuDeclarationQuotaCapturedEvent>().Where(e => e.DeclarationId == q.DeclarationId)
                .ToArray() is not [var captured] || captured.OwnerFrameId != q.DeclarationId || captured.Receipt != q ||
            ProgramEventHistory<MoQuanyuDeclarationPaymentRecordedEvent>().Where(e => e.DeclarationId == q.DeclarationId)
                .ToArray() is not [var invoice] || invoice.MovementSequence <= q.SequenceBefore ||
            _cardMovements.Count(m => m.Sequence == invoice.MovementSequence && m.CardId == q.CardId &&
                m.CardKind == paid.Cost.CardKind && m.From == q.From && m.To == CardLocation.Processing &&
                m.Reason.Value == "conversion.declaration.pay") != 1 ||
            ProgramEventHistory<CardDeclarationCommittedEvent>().Count(e => e.DeclarationId == q.DeclarationId &&
                e.OwnerSeat == q.ProviderSeat && e.ActorSeat == q.ActorSeat && e.DeclaredKind == q.DeclaredKind &&
                e.TargetSeats.SequenceEqual([q.TargetSeat])) != 1) return false;
        if (q.Eligible)
        {
            if (q.Source is not { SkillId: "ol:mo-quanyu", BindingId: "target-only-choices" } source || source.OwnerSeat != q.ActorSeat ||
                string.IsNullOrEmpty(source.SkillInstanceId) || q.GameplayHash is null || q.ChoiceMask is < 1 or > 63 ||
                (q.ChoiceMask & MoQuanyuBit(MoQuanyuEffect.Liuxing)) == 0 ||
                _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash != q.GameplayHash) return false;
        }
        else if (q.Source is not null || q.GameplayHash is not null || q.ChoiceMask != 0 || q.Entered) return false;
        return owner is not CardDeclarationFrame declaration || declaration.Return.ActorSeat == q.ActorSeat &&
            IsMoQuanyuTrueSlashDeclaration(paid, declaration.Return) && declaration.TargetSeats.SequenceEqual([q.TargetSeat]) &&
            (declaration.Return.FinalEffectiveKind ?? paid.DeclaredKind) == q.OutputKind;
    }

    // A found native declaration is authoritative even when it captured no
    // privilege. Later source acquisition cannot turn that original false true.
    private bool TryGetMoQuanyuDeclarationQuota(CharacterState actor, CharacterState target, CardKind kind, out bool allowed)
    {
        allowed = false;
        var top = _resolutionStack.LastOrDefault();
        DeclaredCardPayment? paid;
        if (top is CardDeclarationFrame { Stage: CardDeclarationStage.Applying } declaration &&
            declaration.Return.ActorSeat == actor.Seat && IsSlashCard(declaration.Payment.DeclaredKind)) paid = declaration.Payment;
        else
        {
            if (top is ResponseWindowFrame response) top = _resolutionStack.SingleOrDefault(f => f.Id == response.ParentFrameId);
            paid = top?.AcceptedDeclarationPayment;
            if (paid is not { Claimed: false } || paid.ActorSeat != actor.Seat || !IsSlashCard(paid.DeclaredKind) ||
                _cardZones.GetLocation(paid.Cost.CardId) != CardLocation.Processing) return false;
        }
        allowed = paid.MoQuanyuDeclarationQuota is { Eligible: true } q && q.TargetSeat == target.Seat &&
            q.OutputKind == kind && ValidMoQuanyuDeclarationQuota(paid, q);
        return true;
    }

    private bool TryInheritMoQuanyuDeclarationQuota(CardUseFrame use)
    {
        if (!HasMoQuanyuSlash || use.Action is not { Type: CardActionType.Use } action || !IsSlashCard(use.CardKind)) return false;
        var paid = use.AcceptedDeclarationPayment;
        if (paid is null && action.PhysicalCards is [var material]) paid = UnclaimedDeclarationPayment(action.ProviderSeat, material.CardId);
        if (paid is null || !IsSlashCard(paid.DeclaredKind)) return false;
        // No initial-singleton declaration ticket means no Meteor privilege,
        // rather than permission to recapture the current live source.
        if (paid.MoQuanyuDeclarationQuota is not { } q) return true;
        if (_resolutionStack.LastOrDefault()?.Id != use.Id || !ValidMoQuanyuDeclarationQuota(paid, q) ||
            use.SourceSeat != q.ActorSeat || action.ActorSeat != q.ActorSeat || action.ProviderSeat != q.ProviderSeat ||
            use.CardKind != q.OutputKind || !use.TargetSeats.SequenceEqual([q.TargetSeat]) ||
            action.PhysicalCards is not [var cost] || cost.CardId != q.CardId || cost.CardKind != paid.Cost.CardKind || cost.From != q.From ||
            use.MoQuanyuQuota is not null || ProgramEventHistory<MoQuanyuDeclarationQuotaBoundEvent>().Any(e => e.UseId == use.Id))
            throw new InvalidOperationException("Meteor's accepted Use must inherit its exact initial declaration, actor and original paid material.");
        if (q.Eligible)
        {
            var accepted = new MoQuanyuQuotaReceipt(use.Id, action.ActionId, q.ActorSeat, q.TargetSeat, q.OutputKind,
                q.Source!, q.GameplayHash!, q.ChoiceMask, q.Entered);
            UpdateLifecycleCardUse(use.Id, current => current with { MoQuanyuQuota = accepted });
            AdvanceEventRulesAndQueueFact(new MoQuanyuQuotaAcceptedEvent(accepted));
        }
        AdvanceEventRulesAndQueueFact(new MoQuanyuDeclarationQuotaBoundEvent(q.DeclarationId, use.Id, action.ActionId,
            q.ActorSeat, q.TargetSeat, q.OutputKind, q.Eligible));
        return true;
    }

    [ProgramRuleObserver(ProgramRulePhase.BeforeStatePublish, 28911)]
    private void AssertMoQuanyuDeclarationQuotas()
    {
        foreach (var frame in _resolutionStack)
        {
            var paid = frame is CardDeclarationFrame declaration ? declaration.Payment : frame.AcceptedDeclarationPayment;
            if (paid?.MoQuanyuDeclarationQuota is not { } q) continue;
            if (!ValidMoQuanyuDeclarationQuota(paid, q))
                throw new InvalidOperationException("Meteor lost its original declaration admission or exact native payment.");
            if (frame is not CardUseFrame use || use.Action is not { Type: CardActionType.Use } action) continue;
            if (ProgramEventHistory<MoQuanyuDeclarationQuotaBoundEvent>().Where(e => e.UseId == use.Id).ToArray() is not [var bound] ||
                bound != new MoQuanyuDeclarationQuotaBoundEvent(q.DeclarationId, use.Id, action.ActionId, q.ActorSeat,
                    q.TargetSeat, q.OutputKind, q.Eligible) ||
                q.Eligible && use.MoQuanyuQuota != new MoQuanyuQuotaReceipt(use.Id, action.ActionId, q.ActorSeat,
                    q.TargetSeat, q.OutputKind, q.Source!, q.GameplayHash!, q.ChoiceMask, q.Entered) ||
                !q.Eligible && use.MoQuanyuQuota is not null)
                throw new InvalidOperationException("Meteor cannot recapture a declaration's quota from a later source or another native Use.");
        }
    }
}
