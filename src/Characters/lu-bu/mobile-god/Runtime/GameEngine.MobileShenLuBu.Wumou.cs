namespace CardGame.Core;
public sealed partial class GameEngine
{
    private const string MobileLbWumou = "mobile-god:wumou";
    private CardConversionSource? MobileWumouSource(CharacterState actor, Card card) =>
        GetProgramCardIdentityMatches(actor, card).FirstOrDefault(m => m.Source.SkillId == MobileLbWumou && m.Identity.OutputKind == CardKind.Slash)?.Source;
    private IReadOnlyList<int> MobileWumouTemplateTargets(CharacterState actor, Card card, LegalAction template)
    {
        // Some native producers intentionally omit implicit targets from their
        // action. Retain the same targets their actual resolvers would declare.
        IReadOnlyList<int> targets = template.Kind switch
        {
            LegalActionKind.DrawTwo or LegalActionKind.SowBeans or LegalActionKind.BeamSwap or LegalActionKind.WenheChaos => [actor.Seat],
            LegalActionKind.ReturnSoul => HellReturnSoulUseTargets(actor, card),
            LegalActionKind.AllianceFeast => ApplyTurnCardGroupTargetRestrictions(actor,
                Enumerable.Range(0, _playerCount).Select(offset => _players[TableStepSeat(actor.Seat, offset)])
                    .Where(target => target.IsAlive && !IsShenwuRobeTargetProhibited(actor.Seat, target.Seat, card.Kind) &&
                        !HasCardPolicy(target, SkillProgramCardPolicyKind.ExcludeGlobalTarget, card.Kind) &&
                        !IsCardTargetProhibited(target, card.Kind, card.Suit, SuitColor(EffectiveSuit(actor, card))) &&
                        !HasBeneficiarySuitShield(actor.Seat, target.Seat, EffectiveSuit(actor, card)))
                    .Select(target => target.Seat).ToArray()),
            _ => DragonBoatExpectedNativeTargets(actor, card, template) ?? GuanduExpectedNativeTargets(actor, card, template) ??
                GetDeclaredCardTargets(actor, template.Kind, template.TargetSeats).ToArray()
        };
        return Array.AsReadOnly(targets.Distinct().OrderBy(s => RelativeTableOrder(actor.Seat, s)).ToArray());
    }
    private IReadOnlyList<LegalAction> ProjectMobileWumouActions(CharacterState actor, IReadOnlyList<LegalAction> allActions, int? selectedPhysicalCardId)
    {
        var materials = GetPlayableCards(actor).Where(c => selectedPhysicalCardId is null || c.Id == selectedPhysicalCardId)
            .Select(c => (Card: c, Source: MobileWumouSource(actor, c))).Where(p => p.Source is not null).ToArray();
        if (materials.Length == 0) return allActions;
        var ids = materials.Select(p => p.Card.Id).ToHashSet();
        var result = allActions.Where(a => a.CardId is not { } id || !ids.Contains(id)).ToList();
        foreach (var (card, source) in materials)
        foreach (var template in BuildMobileWumouPrintedTargetActions(actor, card))
        foreach (var variant in GetSlashUseVariants(actor, card, CardKind.Slash, source))
        {
            var targets = MobileWumouTemplateTargets(actor, card, template).Where(s => _players[s].IsAlive &&
                !IsCardUseForbidden(actor.Seat, variant.EffectiveKind, CardActionType.Use) &&
                !CardLegality.IsMaterialForbidden(new(actor.Seat, variant.EffectiveKind, [card.Id], false)) &&
                !IsDirectedCardTargetProhibited(actor.Seat, s, variant.EffectiveKind) &&
                !IsCardTargetProhibited(_players[s], variant.EffectiveKind, EffectiveSuit(actor, card), SuitColor(EffectiveSuit(actor, card))) &&
                !HasBeneficiarySuitShield(actor.Seat, s, EffectiveSuit(actor, card)) &&
                CanSpendSlashUse(actor, _players[s], IgnoresProgramSlashLimit(actor, source), variant.EffectiveKind, card)).ToArray();
            if (targets.Length == 0) continue;
            var action = new LegalAction(LegalActionKind.Slash, card.Id, targets[0],
                $"【无谋】将【{CardCatalog.Get(card.Kind).DisplayName}】当【{CardCatalog.Get(variant.EffectiveKind).DisplayName}】使用，保留原锦囊目标",
                PlayedCardKind: variant.EffectiveKind, TargetSeats: targets)
            { ConversionSource = source, AdditionalConversionSources = variant.AdditionalConversionSource is { } extra ? [extra] : null };
            if (!result.Any(a => SameMobileWumouAction(a, action))) result.Add(action);
        }
        return FilterBeneficiarySuitShieldActions(actor, FilterTurnCardUseRestrictions(actor, result, nativeOriginalCard: null));
    }
    private static bool SameMobileWumouAction(LegalAction a, LegalAction b) => a.Kind == b.Kind && a.CardId == b.CardId &&
        a.PlayedCardKind == b.PlayedCardKind && a.TargetSeats.SequenceEqual(b.TargetSeats) && a.ConversionSource == b.ConversionSource &&
        ConversionSourcesEqual(a.AdditionalConversionSources, b.AdditionalConversionSources);
    private bool TryResolveMobileWumouUse(CharacterState actor, LegalAction action)
    {
        if (action.ConversionSource?.SkillId != MobileLbWumou) return false;
        var card = FindOwnedPlayableCard(actor, action.CardId) ?? throw new InvalidOperationException("Wumou lost its real ordinary-trick material.");
        if (action.Kind != LegalActionKind.Slash || MobileWumouSource(actor, card) != action.ConversionSource ||
            action.PlayedCardKind is not { } kind || !IsSlashCard(kind) ||
            !ProjectMobileWumouActions(actor, [], card.Id).Any(a => SameMobileWumouAction(a, action)))
            throw new InvalidOperationException("Wumou must retain its exact native original-trick target template, Slash quota and conversion source.");
        if (action.TargetSeats.Count == 1)
        {
            var fan = kind == CardKind.FireSlash && (action.AdditionalConversionSources is null || action.AdditionalConversionSources.Count == 0) &&
                !IsDirectProgramFireSlashConversion(actor, card, action.ConversionSource);
            ResolveSlashCore(actor, _players[action.TargetSeats[0]], card, kind, actor.Seat,
                usesZhuqueFan: fan, conversionSource: action.ConversionSource, additionalConversionSources: action.AdditionalConversionSources);
            return true;
        }
        var id = BeginCardUse(card, actor.Seat, action.TargetSeats, kind, HasArmorBypass(actor),
            conversionSource: action.ConversionSource, additionalConversionSources: action.AdditionalConversionSources);
        var nuzhan = GetNuzhanModifiers(id, actor);
        PaySlashUseMaterials([card], actor.Seat, null); CardUsePayments.CompleteSlashPayment(id);
        var counted = !(LifecycleCardUse(id)?.UnlimitedUse == true) &&
            !CardUsePayments.ExemptsSlashDebit(ProgramCardUsePaymentStage.SlashCountExemption, id, actor.Seat) &&
            !nuzhan.IgnoresSlashLimit && !IgnoresProgramSlashLimit(actor, action.ConversionSource) &&
            _phase == TurnPhase.Play && actor.Seat == _currentSeat;
        if (counted) RecordSlashUseDebit(id, actor.Seat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actor.Seat, kind);
        var amount = 1 + actor.AlcoholDamageBonus + nuzhan.DamageBonus;
        CaptureProgramAlcoholConsumption(id, actor); actor.HasAlcoholEffect = false;
        var pending = new FangtianHalberdHandle(this, id, actor.Seat, card, kind, HasArmorBypass(actor), amount,
            action.TargetSeats, usesFangtian: false, countedTowardSlashLimit: counted, conversionSource: action.ConversionSource);
        ActiveFangtianHalberd = pending;
        if (kind == CardKind.FireSlash && (action.AdditionalConversionSources is null || action.AdditionalConversionSources.Count == 0) &&
            !IsDirectProgramFireSlashConversion(actor, card, action.ConversionSource))
            AdvanceEventRulesAndQueueFact(new ZhuqueFanConvertedEvent(id, actor.Seat, Array.AsReadOnly(new[] { card.Id }), action.TargetSeats));
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(card.Id, kind, actor.Seat, action.TargetSeats[0], IgnoresArmor: HasArmorBypass(actor)));
        foreach (var target in action.TargetSeats) NotifyAiOfSlash(actor, _players[target]);
        BeginNextFangtianHalberdTarget(pending); return true;
    }
}
