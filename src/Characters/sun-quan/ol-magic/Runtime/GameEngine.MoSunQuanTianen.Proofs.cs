namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool MoTianenHistoryOrigin(long id, out MoTianenBranchIssuedEvent issued)
    {
        issued = null!;
        if (ProgramEventHistory<MoTianenBranchIssuedEvent>().Where(e => e.FrameId == id).ToArray() is not [var e] ||
            e.Source.SkillId != "ol:mo-tianen" || e.Source.BindingId != "unique-target-branches" ||
            !IsValidPlayerSeat(e.Source.OwnerSeat) || !IsValidPlayerSeat(e.TargetSeat) || e.UseId <= 0 || e.ActionId <= 0 ||
            e.WindowId <= 0 || e.RoundNumber < 1 || e.ActualTurn < 1 || !IsValidPlayerSeat(e.TurnOwner) ||
            e.OwnerChoice is < MoQuanyuEffect.Baihong or > MoQuanyuEffect.Liuxing ||
            e.TargetChoice is < MoQuanyuEffect.Baihong or > MoQuanyuEffect.Liuxing ||
            e.Branch != (e.OwnerChoice == e.TargetChoice ? MoTianenBranch.Same : MoTianenBranch.Different) ||
            _contentRegistry.GetSkill(e.Source.SkillId).Program?.GameplayHash != e.GameplayHash ||
            ProgramEventHistory<MoTianenBranchIssuedEvent>().Count(x => x.Source.OwnerSeat == e.Source.OwnerSeat &&
                x.ActualTurn == e.ActualTurn && x.TurnOwner == e.TurnOwner && x.Branch == e.Branch) != 1 ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(x => x.FrameId == id && x.SkillId == e.Source.SkillId &&
                x.BindingId == e.Source.BindingId && x.SkillInstanceId == e.Source.SkillInstanceId && x.OwnerSeat == e.Source.OwnerSeat &&
                x.Window == SkillProgramTriggerWindow.CardUseTargetsFinalized) != 1 ||
            ProgramEventHistory<MoQuanyuSoleUseCapturedEvent>().Count(x => x.Original.UseId == e.UseId &&
                x.Original.ActionId == e.ActionId && x.Original.WindowId == e.WindowId &&
                x.Original.ActorSeat == e.Source.OwnerSeat && x.Original.TargetSeat == e.TargetSeat) != 1) return false;
        issued = e; return true;
    }

    private bool IsMoTianenHandLimitExempt(CharacterState owner, int id) => HasMoTianenCapability &&
        _cardZones.CardsAt(CardLocation.Hand(owner.Seat)).Any(c => c.Id == id) &&
        ProgramEventHistory<MoTianenSlashExemptionIssuedEvent>().Any(e => e.Source.OwnerSeat == owner.Seat && e.CardId == id && ValidMoTianenExemption(e));
    private bool HasMoTianenEntityMark(int id) => HasMoTianenCapability &&
        ProgramEventHistory<MoTianenSlashExemptionIssuedEvent>().Any(e => e.CardId == id && ValidMoTianenExemption(e));
    private bool ValidMoTianenExemption(MoTianenSlashExemptionIssuedEvent e) =>
        MoTianenHistoryOrigin(e.FrameId, out var issued) && issued.Branch == MoTianenBranch.Same &&
        e.Source == issued.Source && e.GameplayHash == issued.GameplayHash && IsSlashCard(e.PrintedKind) &&
        ProgramEventHistory<MoTianenSlashExemptionIssuedEvent>().Count(x => x.FrameId == e.FrameId) == 1 &&
        ProgramEventHistory<MoTianenSlashObtainIssuedEvent>().Where(x => x.FrameId == e.FrameId).ToArray() is [var obtained] &&
        obtained.CardId == e.CardId && obtained.PrintedKind == e.PrintedKind && obtained.From == CardLocation.DrawPile &&
        _cardMovements.Count(m => m.Sequence == e.EntrySequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(e.Source.OwnerSeat) && m.Reason.Value == MoTianenObtainReason) == 1;

    private bool ValidMoTianen(ProgramSkillFrame f)
    {
        if (f.MoTianen is not { } r || f.SkillId != "ol:mo-tianen" || f.TriggerId != "unique-target-branches" || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || f.NativeDraw is not null ||
            r.Source != MoTianenSource(f) || r.GameplayHash != f.GameplayHash || !Enum.IsDefined(r.Stage) ||
            f.WindowContext is not { } context || !TryGetMoQuanyuSingletonUse(f.OwnerSeat, context, out var use, out var window, out var target) ||
            r.UseId != use.Id || r.ActionId != use.Action!.ActionId || r.WindowId != window.Id || r.TargetSeat != target ||
            !MoTianenHistoryOrigin(f.Id, out var issued) ||
            issued != new MoTianenBranchIssuedEvent(f.Id, r.Source, r.GameplayHash, r.UseId, r.ActionId, r.WindowId,
                r.TargetSeat, r.RoundNumber, r.ActualTurn, r.TurnOwner, r.OwnerChoice, r.TargetChoice, r.Branch) ||
            !r.PaymentSealed || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            r.GrainMaterials is not System.Collections.IList { IsReadOnly: true } || r.Movements is not System.Collections.IList { IsReadOnly: true } ||
            r.GrainMaterials.Select(m => m.CardId).Distinct().Count() != r.GrainMaterials.Count ||
            r.Movements.Select(m => m.CardId).Distinct().Count() != r.Movements.Count ||
            ProgramEventHistory<MoTianenPaymentSealedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var seal] ||
            seal != new MoTianenPaymentSealedEvent(f.Id, r.Branch, r.Before, r.After, r.Movements.Count, r.Material?.CardId)) return false;
        var paid = ProgramEventHistory<MoTianenMaterialPaidEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (paid.Length != r.Movements.Count || paid.Where((e, i) => e != new MoTianenMaterialPaidEvent(f.Id, i,
                r.Movements[i].CardId, r.Movements[i].PrintedKind, r.Movements[i].From, r.Movements[i].To,
                r.Movements[i].Reason, r.Movements[i].Sequence)).Any() ||
            r.Movements.Any(m => m.Sequence <= r.Before || m.Sequence > r.After || _cardMovements.Count(x =>
                x.Sequence == m.Sequence && x.CardId == m.CardId && x.CardKind == m.PrintedKind && x.From == m.From && x.To == m.To && x.Reason.Value == m.Reason) != 1)) return false;
        if (r.Branch == MoTianenBranch.Same)
        {
            if (r.Stage is not (MoTianenStage.ObtainChildren or MoTianenStage.Completing) || r.GrainMaterials.Count != 0 ||
                r.QuanyuRequest is not null || r.QuanyuChildFrameId is not null || r.PoolCount != 0 || r.RandomIndex != -1 ||
                ProgramEventHistory<MoTianenRandomDiscardIssuedEvent>().Any(e => e.FrameId == f.Id) ||
                ProgramEventHistory<MoTianenSlashObtainIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var obtain] ||
                obtain != new MoTianenSlashObtainIssuedEvent(f.Id, r.Material?.CardId, r.Material?.PrintedKind, r.Material?.From, r.Before)) return false;
            if (r.Material is not { } material) { if (r.Movements.Count != 0 || r.Before != r.After ||
                ProgramEventHistory<MoTianenSlashExemptionIssuedEvent>().Any(e => e.FrameId == f.Id)) return false; }
            else if (material.From != CardLocation.DrawPile || !IsSlashCard(material.PrintedKind) ||
                r.Movements is not [var m] || m.CardId != material.CardId || m.PrintedKind != material.PrintedKind || m.From != material.From ||
                m.To != CardLocation.Hand(f.OwnerSeat) || m.Reason != MoTianenObtainReason ||
                ProgramEventHistory<MoTianenSlashExemptionIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var exempt] ||
                exempt.CardId != material.CardId || exempt.EntrySequence != m.Sequence || !ValidMoTianenExemption(exempt)) return false;
        }
        else
        {
            if (r.Stage == MoTianenStage.ObtainChildren || ProgramEventHistory<MoTianenSlashObtainIssuedEvent>().Any(e => e.FrameId == f.Id) ||
                ProgramEventHistory<MoTianenSlashExemptionIssuedEvent>().Any(e => e.FrameId == f.Id) ||
                ProgramEventHistory<MoTianenRandomDiscardIssuedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var random] ||
                random != new MoTianenRandomDiscardIssuedEvent(f.Id, r.PoolCount, r.RandomIndex, r.RandomBefore, r.RandomAfter,
                    r.Material?.CardId, r.Material?.PrintedKind, r.Material?.From, r.Before) || r.PoolCount < 0) return false;
            if (r.Material is not { } material)
            { if (r.PoolCount != 0 || r.RandomIndex != -1 || r.RandomBefore != r.RandomAfter || r.GrainMaterials.Count != 0 || r.Movements.Count != 0 || r.Before != r.After) return false; }
            else
            {
                if (r.PoolCount < 1 || r.RandomIndex < 0 || r.RandomIndex >= r.PoolCount || material.From.OwnerSeat != r.TargetSeat ||
                    material.From.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment)) return false;
                var randomProof = new DeterministicRandom(unchecked((int)r.RandomBefore));
                if (randomProof.Next(r.PoolCount) != r.RandomIndex || randomProof.State != r.RandomAfter) return false;
                var primary = r.Movements.Where(m => m.CardId == material.CardId).ToArray();
                if (primary is not [var m] || m.PrintedKind != material.PrintedKind || m.From != material.From || m.Reason != MoTianenDiscardReason ||
                    m.To != CardLocation.DiscardPile && !(m.To == CardLocation.OutsideGame && material.From.Zone == CardZoneKind.Equipment &&
                        (GetAdvancedCard(material.CardId).IsGeneralWeapon || IsGuanduThunderChariotDeparture(GetAdvancedCard(material.CardId), material.From, CardLocation.DiscardPile)))) return false;
                if (r.GrainMaterials.Count > 0 && (material.PrintedKind != CardKind.WoodenOx || material.From.Zone != CardZoneKind.Equipment) ||
                    r.GrainMaterials.Any(g => g.From != CardLocation.WoodenOxGrain(r.TargetSeat) || r.Movements.Count(x => x.CardId == g.CardId &&
                        x.PrintedKind == g.PrintedKind && x.From == g.From && x.To == CardLocation.DiscardPile && x.Reason == CardMoveReasons.WoodenOxGrainDiscard.Value) != 1) ||
                    r.Movements.Count != 1 + r.GrainMaterials.Count) return false;
            }
            var grains = ProgramEventHistory<MoTianenSpillMaterialFrozenEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
            if (grains.Length != r.GrainMaterials.Count || grains.Where((e, i) => e != new MoTianenSpillMaterialFrozenEvent(f.Id, i,
                r.GrainMaterials[i].CardId, r.GrainMaterials[i].PrintedKind, r.GrainMaterials[i].From)).Any()) return false;
        }
        var requests = ProgramEventHistory<MoTianenQuanyuRequestedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var returns = ProgramEventHistory<MoTianenQuanyuReturnedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.QuanyuRequest is { } request)
        {
            if (r.Branch != MoTianenBranch.Different || r.Stage is not (MoTianenStage.QuanyuChild or MoTianenStage.Completing) ||
                !MoTianenRequestMatches(f, request) || requests is not [var asked] || asked.Request != request) return false;
            if (r.Stage == MoTianenStage.QuanyuChild) { if (returns.Length != 0 || r.QuanyuChildFrameId is not null) return false; }
            else if (returns is not [var returned] || returned.Request != request || returned.ChildFrameId != r.QuanyuChildFrameId ||
                ProgramEventHistory<MoQuanyuTargetChoiceReturnedEvent>().Count(e => e.Result == new MoQuanyuTargetChoiceReturn(
                    returned.ChildFrameId, request, returned.ChosenEffect, returned.DrawCount)) != 1) return false;
        }
        else if (requests.Length != 0 || returns.Length != 0 || r.QuanyuChildFrameId is not null || r.Stage == MoTianenStage.QuanyuChild) return false;
        return f.PendingMovementContinuation is null || IsMoTianenMovement(f, GetPausedPrivateOfferEffect(f), f.PendingMovementContinuation);
    }

    private bool MoTianenRequestMatches(ProgramSkillFrame f, MoQuanyuTargetRequest request) => f.MoTianen is { } r &&
        r.Branch == MoTianenBranch.Different && r.PaymentSealed && request.ParentFrameId == f.Id &&
        request.ParentInstructionIndex == f.InstructionIndex && request.ParentSource == r.Source && request.ParentGameplayHash == r.GameplayHash &&
        request.UseId == r.UseId && request.ActionId == r.ActionId && request.TargetSeat == r.TargetSeat &&
        request.RoundNumber == r.RoundNumber && request.ActualTurn == r.ActualTurn && request.TurnOwner == r.TurnOwner &&
        request.DiscardBefore == r.Before && request.DiscardAfter == r.After &&
        request.QuanyuSource == new CardConversionSource("ol:mo-quanyu", "target-only-choices", f.OwnerSeat, "issued:tianen:" + f.Id) &&
        _contentRegistry.GetSkill("ol:mo-quanyu").Program?.GameplayHash == request.QuanyuGameplayHash;
    private bool IsMoSunQuanTianenQuanyuRequest(ProgramSkillFrame f, MoQuanyuTargetRequest request) =>
        f.MoTianen is { Stage: MoTianenStage.QuanyuChild, QuanyuRequest: { } issued, QuanyuChildFrameId: null } &&
        issued == request && f.PendingMovementContinuation is null && MoTianenRequestMatches(f, request) && ValidMoTianen(f);

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28920)]
    private void AssertMoTianen(ProgramSkillFrame f)
    { if (f.MoTianen is not null && !ValidMoTianen(f)) throw new InvalidOperationException("Tianen lost its frozen canonical Use, branch debit, original entity payment or signed target-only Quanyu return."); }
}
