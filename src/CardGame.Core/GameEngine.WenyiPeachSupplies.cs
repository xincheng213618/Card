using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string WenyiPeachSupplyAction = "wenyi-peach-supply";

    private bool IsWenyiPeachSupplyQuery(CharacterState actor, int target) =>
        _resolutionStack.LastOrDefault() is ProgramSkillFrame f && IsWenyiPeachRequest(f) &&
        TryGetWenyiPeachRequest(f, out var requestedActor, out var requestedTarget) &&
        requestedActor == actor.Seat && requestedTarget == target;

    private bool WenyiPeachUseLegal(int actorSeat, int targetSeat, IReadOnlyList<Card> materials,
        CardConversionSource? source = null)
    {
        if (!IsValidPlayerSeat(actorSeat) || !IsValidPlayerSeat(targetSeat) || _winner != Winner.None ||
            !_players[actorSeat].IsAlive || !_players[targetSeat].IsAlive || !IsRuleWounded(_players[targetSeat]) ||
            IsCardUseForbidden(actorSeat, CardKind.Peach, CardActionType.Use) ||
            IsDirectedCardTargetProhibited(actorSeat, targetSeat, CardKind.Peach) ||
            source is not null && ViewAsRule(source) is { } rule &&
                (rule.ExcludeOwnerEffects && actorSeat == targetSeat || rule.NoDying &&
                    (ActiveDying is not null || _players.Any(p => p.IsAlive && p.Hp <= 0))) ||
            actorSeat == targetSeat && HasSelfCardTargetProhibition(actorSeat) ||
            IsTurnPhysicalUseForbidden(actorSeat, materials.Select(c => c.Id).ToArray())) return false;
        var actor = _players[actorSeat];
        if (materials.Any(card => IsTurnHandCardRestricted(actor, card))) return false;
        Suit? suit = materials.Count == 0 ? Suit.None : materials.Count == 1
            ? EffectiveSuit(actor, ApplyProgramUseAppearance(actor, materials[0], source))
            : PhysicalGroupSuit(actor, materials);
        var color = materials.Count == 0 ? null : materials.Count == 1
            ? SuitColor(suit!.Value) : PhysicalGroupColor(actor, materials);
        return !HasBeneficiarySuitShield(actorSeat, targetSeat, suit) &&
            !IsCardTargetProhibited(_players[targetSeat], CardKind.Peach, suit, color) &&
            (suit is null || !IsTurnSuitUseForbidden(actorSeat, suit.Value));
    }

    private IReadOnlyList<PromptChoice> WenyiPeachSupplyChoices(ProgramSkillFrame f)
    {
        if (!IsWenyiPeachRequest(f) || !TryGetWenyiPeachRequest(f, out var actorSeat, out var target)) return [];
        var actor = _players[actorSeat]; var result = new List<PromptChoice>();
        Dictionary<string, string> Args(string kind) => new()
        {
            ["program-action"] = WenyiPeachSupplyAction, ["wenyi-supply"] = kind,
            ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture)
        };
        void Add(string kind, IReadOnlyList<Card> cards, CardConversionSource? source, string label)
        {
            if (!WenyiPeachUseLegal(actorSeat, target, cards, source)) return;
            var args = Args(kind); if (source is not null) AddConversionParameters(args, source);
            if (kind == "original-pile") args["native-original-public-pile"] = "true";
            result.Add(new(new($"wenyi.peach.{f.Id}.{kind}.{source?.SkillId}.{source?.BindingId}.{source?.SkillInstanceId}.{string.Join('-', cards.Select(c => c.Id))}"),
                label, cards.Select(c => c.Id).ToArray(), [target], args));
        }

        var natural = GetPlayableCards(actor).Where(c => _cardZones.GetLocation(c.Id) is var from &&
            (from == CardLocation.Hand(actorSeat) || from == CardLocation.WoodenOxGrain(actorSeat)) &&
            c.Kind == CardKind.Peach && !HasProgramCardIdentity(actor, c));
        foreach (var card in natural) Add("native", [card], null, "对其使用【桃】");
        var publicRequest = NativePublicPileUseRequest(actorSeat, actorSeat, CardKind.Peach, f.Id);
        foreach (var selection in GetNativePublicPileOriginalCards(publicRequest).Where(s => s.Card.Kind == CardKind.Peach))
            Add("original-pile", [selection.Card], null, "使用公共粮原牌【桃】");

        var inputs = GetPlayableCards(actor).Concat(GetEquipment(actor)).Concat(GetOwnPublicPileConversionCards(actor))
            .DistinctBy(c => c.Id).Where(c => !IsTurnHandCardRestricted(actor, c)).ToArray();
        foreach (var card in inputs)
        foreach (var source in FindProgramViewAsConversions(actor, card, CardKind.Peach, false, requestedUseTarget: target)
            .Concat(GetProgramCardIdentitySources(actor, card, CardKind.Peach, false)).Distinct())
            Add("single", [card], source, $"发动【{ProgramConversionName(source)}】，将此牌当【桃】对其使用");
        foreach (var selection in GetProgramMultiCardViewAsSelections(actor, CardKind.Peach, false, requestedUseTarget: target))
            Add("multi", selection.Cards, selection.Source, $"发动【{ProgramConversionName(selection.Source)}】，将{selection.Cards.Count}张牌当【桃】对其使用");
        foreach (var (source, rule, _) in TieredRoundZeroRules(actor, CardKind.Peach))
        {
            if (rule.ExcludeOwnerEffects && actorSeat == target) continue;
            if (IsEquipmentSlotNamedSkill(source.SkillId))
            {
                var paid = UnconsumedEquipmentSlotPayment(source);
                if (paid is not null) continue; // Only the exact typed return can redeem this request's paid invoice.
                if (!Enum.GetValues<EquipmentSlot>().Any(slot => EquipmentSlotNamedChoiceIsPayable(actor, source,
                        CardKind.Peach, EquipmentSlotNamedUseIntent.WenyiPeach, [target], slot))) continue;
            }
            Add("tiered-zero", [], source, $"发动【{ProgramConversionName(source)}】，视为对其使用【桃】");
        }
        return Array.AsReadOnly(result.Select(PromptChoiceProjection.Freeze).ToArray());
    }

    [ProgramAnswer(WenyiPeachSupplyAction)]
    private void ResolveWenyiPeachSupply(PromptChoice choice)
    {
        if (!TryResolveWenyiPeachSupply(choice)) throw new InvalidOperationException("Wenyi requires its exact published Peach supply.");
    }

    private bool TryResolveWenyiPeachSupply(PromptChoice choice)
    {
        if (choice.Parameters.GetValueOrDefault("program-action") != WenyiPeachSupplyAction) return false;
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || !IsWenyiPeachRequest(f) ||
            !TryGetWenyiPeachRequest(f, out var actorSeat, out var target) ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt ||
            prompt.PlayerSeat != actorSeat || prompt.SkillPrompt?.SkillId != f.SkillId ||
            !prompt.Choices.Any(c => AssistedChoicesEqual([c], [choice])) ||
            !WenyiPeachSupplyChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Wenyi Peach changed its native parent, target, source or selected entities.");
        var actor = _players[actorSeat]; var branch = choice.Parameters["wenyi-supply"];
        TryReadConversionSource(choice.Parameters, out var source);
        var cards = choice.Cards.Select(id => _cardZones.CardsAt(_cardZones.GetLocation(id)).Single(c => c.Id == id)).ToArray();
        if (branch == "tiered-zero" && source is not null && IsEquipmentSlotNamedSkill(source.SkillId))
        {
            // Reserve the accepted Wenyi right before the real slot cost and its children.
            PrepareWenyiSuppliedPeach(f, actorSeat, [], source);
            BeginEquipmentSlotNamedUse(actor, source, CardKind.Peach, EquipmentSlotNamedUseIntent.WenyiPeach, prompt, null, choice);
            return true;
        }
        if (branch == "original-pile")
        {
            var request = NativePublicPileUseRequest(actorSeat, actorSeat, CardKind.Peach, f.Id);
            var selected = GetNativePublicPileOriginalCards(request).Single(s => s.Card.Id == cards.Single().Id);
            PrepareNativePublicPilePayment(f, selected);
        }
        PrepareWenyiSuppliedPeach(f, actorSeat, cards, source);
        ClearPendingDecision();
        var appearance = cards.Length == 0 ? new Card(0, CardKind.Peach, Suit.None, 0)
            : cards.Length == 1 ? ApplyProgramUseAppearance(actor, cards[0], source) : cards[0];
        ResolveRecoveryCard(actor, _players[target], appearance, "桃", CardKind.Peach,
            recoveryAmount: 1 + (source is null ? 0 : ViewAsRule(source)?.RecoveryBonus ?? 0),
            conversionSource: source, physicalCards: cards);
        return true;
    }

    private bool IsReservedWenyiPeachSupply(ProgramSkillFrame f, int actor, int target, CardConversionSource source) =>
        TryGetWenyiPeachRequest(f, out var actualActor, out var actualTarget) && actualActor == actor && actualTarget == target &&
        f.HuanShuJun is { Paid: true, Stage: HuanShuJunStage.NativeIssuing, Kind: CardKind.Peach, Materials.Count: 0 } r &&
        r.Sources.SequenceEqual([source]) && source.OwnerSeat == actor &&
        ProgramEventHistory<WenyiPaidEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == actor &&
            e.TargetSeat == target && e.HealthWindowId == r.WindowId && e.Branch == "peach") == 1;

    private bool WenyiDrawFundedParentMatches(ProgramSkillFrame f, DrawFundedDistinctBasicPayment p) =>
        p.Intent == DrawFundedDistinctBasicIntent.WenyiPeach && p.EffectiveKind == CardKind.Peach &&
        p.ParentFrameId == f.Id && p.RequestFrameId == f.Id && p.Cursor == f.InstructionIndex &&
        p.TargetSeat is { } target && IsReservedWenyiPeachSupply(f, p.Source.OwnerSeat, target, p.Source);

    private void ReserveWenyiDrawFundedPeach(PendingDecision original, DrawFundedBasicNeed need,
        CardConversionSource source, CardKind kind)
    {
        if (kind != CardKind.Peach || need.Intent != DrawFundedDistinctBasicIntent.WenyiPeach ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame parent || parent.Id != need.ParentFrameId ||
            need.RequestFrameId != parent.Id || parent.InstructionIndex != need.Cursor ||
            !IsWenyiPeachRequest(parent) || !TryGetWenyiPeachRequest(parent, out var actor, out var target) ||
            actor != source.OwnerSeat || original.PlayerSeat != actor || need.FixedTargetSeat != target ||
            _pendingDecision?.PromptId != original.PromptId)
            throw new InvalidOperationException("Wenyi cannot reserve a changed draw-funded Peach request.");
        PrepareWenyiSuppliedPeach(parent, actor, [], source);
    }

    private bool WenyiDrawFundedNeedStillLegal(DrawFundedDistinctBasicFrame paid) =>
        _resolutionStack.SingleOrDefault(f => f.Id == paid.Payment.ParentFrameId) is ProgramSkillFrame parent &&
        WenyiDrawFundedParentMatches(parent, paid.Payment) &&
        WenyiPeachUseLegal(paid.Payment.Source.OwnerSeat, paid.Payment.TargetSeat!.Value, [], paid.Payment.Source);

    private void ExecuteWenyiDrawFundedPeach(DrawFundedDistinctBasicFrame paid)
    {
        var p = paid.Payment;
        if (!ValidDrawFundedDistinctBasicPayment(p) || _resolutionStack.LastOrDefault() is not ProgramSkillFrame parent ||
            !WenyiDrawFundedParentMatches(parent, p)) throw new InvalidOperationException("Wenyi lost its once-paid native draw supplier.");
        if (!WenyiPeachUseLegal(p.Source.OwnerSeat, p.TargetSeat!.Value, [], p.Source))
        { FinishUnissuedWenyiPeach(parent); return; }
        ResolveRecoveryCard(_players[p.Source.OwnerSeat], _players[p.TargetSeat.Value], new(0, CardKind.Peach, Suit.None, 0),
            "桃", CardKind.Peach, conversionSource: p.Source, physicalCards: [], drawFundedPayment: p);
    }

    private bool TryCancelWenyiPeachSupply(long? parentId)
    {
        if (parentId is null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != parentId ||
            f.HuanShuJun is not { Paid: true, Stage: HuanShuJunStage.NativeIssuing, Kind: CardKind.Peach, Materials.Count: 0 }) return false;
        FinishUnissuedWenyiPeach(f); return true;
    }

    private int[] WenyiRequestedDeckPeachTargets(RequestedDeckBasicFrame view, Card card) =>
        view.Receipt.Intent == RequestedDeckBasicIntent.WenyiPeach && card.Kind == CardKind.Peach &&
        _resolutionStack.SingleOrDefault(f => f.Id == view.Receipt.OwnerFrameId) is ProgramSkillFrame parent &&
        parent.Id == view.Receipt.RequestFrameId && parent.InstructionIndex == view.Receipt.Cursor &&
        IsWenyiPeachRequest(parent) && TryGetWenyiPeachRequest(parent, out var actor, out var target) &&
        actor == view.Receipt.ActorSeat && WenyiPeachUseLegal(actor, target, [card]) ? [target] : [];

    private void ExecuteWenyiRequestedDeckPeach(Card card, IReadOnlyList<int> targets)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame parent || !IsWenyiPeachRequest(parent) ||
            !TryGetWenyiPeachRequest(parent, out var actor, out var target) || !targets.SequenceEqual([target]) ||
            parent.RequestedDeckBasicMaterial is not { Intent: RequestedDeckBasicIntent.WenyiPeach, Claiming: true } receipt ||
            receipt.OwnerFrameId != parent.Id || receipt.RequestFrameId != parent.Id || receipt.Cursor != parent.InstructionIndex ||
            receipt.ActorSeat != actor || receipt.SelectedCardId != card.Id || receipt.SelectedKind != CardKind.Peach ||
            card.Kind != CardKind.Peach || !HasValidRequestedDeckBasicMaterial(parent) ||
            !WenyiPeachUseLegal(actor, target, [card]))
            throw new InvalidOperationException("Wenyi lost its exact privately selected native DrawPile Peach.");
        PrepareWenyiSuppliedPeach(parent, actor, [card], null);
        ClearPendingDecision();
        ResolveRecoveryCard(_players[actor], _players[target], card, "桃", CardKind.Peach, physicalCards: [card]);
    }

    private bool WenyiEquipmentSlotPaymentRidesOn(ProgramSkillFrame f, ResolutionFrame child) =>
        child is ProgramSkillFrame { EquipmentSlotNamedUse: { Intent: EquipmentSlotNamedUseIntent.WenyiPeach } d } slot &&
        d.OriginalParentId == f.Id && slot.OwnerSeat == f.OwnerSeat && d.EffectiveKind == CardKind.Peach &&
        d.ResponseChoice is { Cards.Count: 0, Targets: [var target] } choice &&
        IsReservedWenyiPeachSupply(f, slot.OwnerSeat, target, d.Source) &&
        d.TargetSignature == EquipmentSlotNamedTargetSignature(null, choice) &&
        d.OriginalDecision.PlayerSeat == slot.OwnerSeat && d.OriginalDecision.SkillPrompt?.SkillId == f.SkillId &&
        d.OriginalDecision.Choices.Any(c => AssistedChoicesEqual([c], [choice]));

    private bool ResumeWenyiEquipmentSlotPeach(EquipmentSlotNamedUseDraft draft)
    {
        if (draft.Intent != EquipmentSlotNamedUseIntent.WenyiPeach) return false;
        if (draft.Payment is not { } paid || draft.OriginalParentId is not { } parentId ||
            _resolutionStack.LastOrDefault() is not ProgramSkillFrame parent || parent.Id != parentId ||
            draft.ResponseChoice is not { Cards.Count: 0, Targets: [var target] } choice ||
            !IsReservedWenyiPeachSupply(parent, paid.Source.OwnerSeat, target, paid.Source) ||
            paid.Intent != draft.Intent || paid.EffectiveKind != CardKind.Peach || paid.Source != draft.Source ||
            paid.GameplayHash != draft.GameplayHash || paid.OriginalParentId != parentId ||
            paid.TargetSignature != draft.TargetSignature || paid.TargetSignature != EquipmentSlotNamedTargetSignature(null, choice) ||
            ProgramEventHistory<EquipmentSlotNamedUsePaidEvent>().Count(e => e.Payment == paid) != 1)
            throw new InvalidOperationException("Wenyi lost the exact completed equipment-slot supplier invoice.");
        ClearPendingDecision();
        if (!WenyiPeachUseLegal(paid.Source.OwnerSeat, target, [], paid.Source))
        {
            AdvanceEventRulesAndQueueFact(new EquipmentSlotNamedUseCancelledEvent(paid.FrameId));
            FinishUnissuedWenyiPeach(parent); return true;
        }
        ResolveRecoveryCard(_players[paid.Source.OwnerSeat], _players[target], new(0, CardKind.Peach, Suit.None, 0),
            "桃", CardKind.Peach, conversionSource: paid.Source, physicalCards: []);
        return true;
    }

    // Capture the original request even while the supplier's paid child owns
    // the stack. Qualification still requires the live request or that exact
    // supplier payment; this structural query alone grants no new right.
    private bool TryGetWenyiWeijiePeachNeed(ProgramSkillFrame f, int provider, out int target)
    {
        target = f.HuanShuJun?.TargetSeat ?? -1;
        return f.OwnerSeat == provider && f.HuanShuJun is
            { Op: SkillProgramEffectOp.ResolveWenyiHealthOneAid, Paid: false, Stage: HuanShuJunStage.WenyiPeachRequest } &&
            IsValidPlayerSeat(target) && WenyiHpRoot(f);
    }

    private bool WenyiWeijiePeachLegal(WeijieNeed need, int target)
    {
        if (need.Intent != WeijieIntent.WenyiPeach || need.ActorSeat != need.ProviderSeat || need.RequesterSeat is not null ||
            _resolutionStack.SingleOrDefault(f => f.Id == need.OwnerFrameId) is not ProgramSkillFrame parent ||
            need.RequestFrameId != parent.Id || need.InstructionIndex != parent.InstructionIndex ||
            need.RequestOp != SkillProgramEffectOp.ResolveWenyiHealthOneAid || !need.Targets.SequenceEqual([target]) ||
            !TryGetWenyiWeijiePeachNeed(parent, need.ActorSeat, out var originalTarget) || originalTarget != target ||
            !WenyiPeachUseLegal(need.ActorSeat, target, [])) return false;
        if (IsWenyiPeachRequest(parent)) return true;
        return ActiveWeijieDraft() is { Weijie: { Paid: { } paid } } && SameWeijieNeed(paid.Need, need) &&
            IsWenyiPeachRequestAfterPaidSupply(parent, paid);
    }

    private void ExecuteWenyiWeijiePeach(WeijiePaidOrigin paid)
    {
        if (paid.Need.Intent != WeijieIntent.WenyiPeach || _resolutionStack.LastOrDefault() is not ProgramSkillFrame parent ||
            paid.Need.OwnerFrameId != parent.Id || !IsWenyiPeachRequestAfterPaidSupply(parent, paid) ||
            ProgramEventHistory<WeijieAttemptResolvedEvent>().Count(e => e.FrameId == paid.AttemptId && e.Matched && e.NativeLegal) != 1)
            throw new InvalidOperationException("Wenyi lost its successful once-paid Weijie Peach supplier.");
        if (!WenyiPeachUseLegal(paid.Need.ActorSeat, paid.TargetSeat, []))
        { PublishHuanShuJun(parent); return; }
        PrepareWenyiSuppliedPeach(parent, paid.Need.ActorSeat, [], paid.Source, paidWeijie: paid);
        DispatchWeijieRecovery(paid, false);
    }

    private void RepublishWenyiAfterWeijie(WeijieReceipt receipt)
    {
        var need = receipt.Need;
        if (need.Intent != WeijieIntent.WenyiPeach || _resolutionStack.LastOrDefault() is not ProgramSkillFrame parent ||
            need.OwnerFrameId != parent.Id || need.RequestFrameId != parent.Id || parent.InstructionIndex != need.InstructionIndex ||
            !TryGetWenyiWeijiePeachNeed(parent, need.ActorSeat, out var target) || !need.Targets.SequenceEqual([target]))
            throw new InvalidOperationException("Wenyi's failed supplier lost its original unconsumed request.");
        if (IsWenyiPeachRequest(parent)) PublishHuanShuJun(parent);
        else FinishHuanShuJun(parent);
    }
}
