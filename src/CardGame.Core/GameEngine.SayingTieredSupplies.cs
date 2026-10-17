using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SayingTieredSlashAction = "saying-tiered-zero-slash";

    private IReadOnlyList<PromptChoice> SayingTieredProgramSlashChoices(ProgramSkillFrame f)
    {
        if (!IsSayingSlashRequest(f) || !TryGetSayingSlashRequest(f, out var actorSeat, out var targets) || targets is not [var targetSeat]) return [];
        var actor = _players[actorSeat]; var target = _players[targetSeat];
        if (!actor.IsAlive || !target.IsAlive) return [];
        var choices = new List<PromptChoice>();
        foreach (var (source, rule, _) in TieredRoundZeroRules(actor).Where(r => IsSlashCard(r.Rule.OutputKind)))
        {
            var kind = rule.OutputKind;
            if (HasBeneficiarySuitShield(actorSeat, targetSeat, Suit.None) || IsCardTargetProhibited(target, kind, Suit.None) ||
                !CanUseSlashTarget(actor, target, new(0, kind, Suit.None, 0), source, kind, noEffectiveRank: true, physicalCardIds: []) ||
                IsEquipmentSlotNamedSkill(source.SkillId) && UnconsumedEquipmentSlotPayment(source) is null &&
                !Enum.GetValues<EquipmentSlot>().Any(slot => EquipmentSlotNamedChoiceIsPayable(actor, source, kind, EquipmentSlotNamedUseIntent.SayingSlash, targets, slot))) continue;
            var args = new Dictionary<string, string>
            {
                ["program-action"] = SayingTieredSlashAction, ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture),
                ["actor-seat"] = actorSeat.ToString(CultureInfo.InvariantCulture), ["output-kind"] = kind.ToString()
            };
            AddConversionParameters(args, source);
            var choice = new PromptChoice(new($"saying-tiered.{f.Id}.{source.SkillInstanceId}.{source.BindingId}.{kind}"),
                $"发动【{ProgramConversionName(source)}】，对 {target.Name} 使用【{CardCatalog.Get(kind).DisplayName}】", [], [targetSeat], args);
            if (IsEquipmentSlotNamedSkill(source.SkillId) && UnconsumedEquipmentSlotPayment(source) is { } paid &&
                (paid.Intent != EquipmentSlotNamedUseIntent.SayingSlash || paid.OriginalParentId != f.Id || paid.Source != source ||
                 paid.EffectiveKind != kind || paid.GameplayHash != _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash ||
                 paid.TargetSignature != EquipmentSlotNamedTargetSignature(null, choice))) continue;
            choices.Add(choice);
        }
        return Array.AsReadOnly(choices.ToArray());
    }

    [ProgramAnswer(SayingTieredSlashAction)]
    private void ResolveSayingTieredSlash(PromptChoice selected)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || !IsSayingSlashRequest(f) ||
            !TryGetSayingSlashRequest(f, out var actorSeat, out var targets) || targets is not [var target] ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } prompt || prompt.PlayerSeat != actorSeat ||
            prompt.SkillPrompt?.SkillId != f.SkillId || selected.Cards.Count != 0 || !selected.Targets.SequenceEqual(targets) ||
            !prompt.Choices.Any(c => AssistedChoicesEqual([c], [selected])) ||
            !SayingTieredProgramSlashChoices(f).Any(c => AssistedChoicesEqual([c], [selected])) ||
            !Enum.TryParse<CardKind>(selected.Parameters.GetValueOrDefault("output-kind"), out var kind))
            throw new InvalidOperationException("Saying's tiered supplier lost the exact original private request, native name or target.");
        var actor = _players[actorSeat]; var source = RequireConversionSource(selected);
        // The real equipment-slot program owns all cost children before any
        // global selected-use conversion or Saying native child is created.
        if (IsEquipmentSlotNamedSkill(source.SkillId) && UnconsumedEquipmentSlotPayment(source) is null)
        {
            BeginEquipmentSlotNamedUse(actor, source, kind, EquipmentSlotNamedUseIntent.SayingSlash, prompt, null, selected);
            return;
        }
        ClearPendingDecision();
        PrepareSayingSuppliedSlash(f, actorSeat, actorSeat, target, kind, [], source);
        var id = BeginCardUse(new(0, kind, Suit.None, 0), actorSeat, targets, kind,
            ignoresArmor: HasCardArmorBypass(actor, _players[target], kind), physicalCardIds: [], conversionSource: source);
        var attack = new CardAttackHandle(this, id, actorSeat, target, card: null, damageAmount: 1 + actor.AlcoholDamageBonus,
            playedCardKind: kind, ignoresArmor: HasCardArmorBypass(actor, _players[target], kind), programSkillCardUseFrameId: f.Id);
        ActiveCardAttack = attack; CaptureProgramAdjustedSlashBaseDamage(attack); CaptureProgramAlcoholConsumption(id, actor);
        actor.HasAlcoholEffect = false; MarkSlashUsedOrPlayedDuringCurrentPlayPhase(actorSeat, kind);
        AdvanceEventRulesAndQueueFact(new CardUsedEvent(0, kind, actorSeat, target, IgnoresArmor: attack.IgnoresArmor));
        NotifyAiOfSlash(actor, _players[target]);
        if (!TryMarkProgramUseCommitted(id) || !TryBeginProgramCardWindow(attack, LifecycleCardUse(id)!.Action!,
                SkillProgramTriggerWindow.CardUseCommitted, targets, ProgramCardContinuation.CommittedSlash)) BeginSlashTargetResolution(attack);
    }

    private bool SayingEquipmentSlotPaymentRidesOn(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!IsSayingSlashRequest(f) || !TryGetSayingSlashRequest(f, out var actor, out var targets) ||
            child is not ProgramSkillFrame { EquipmentSlotNamedUse: { Intent: EquipmentSlotNamedUseIntent.SayingSlash, ResponseChoice: { } choice } draft } payment ||
            payment.ParentFrameId != f.Id || draft.OriginalParentId != f.Id || payment.OwnerSeat != actor || draft.Source.OwnerSeat != actor ||
            payment.SkillId != draft.Source.SkillId || payment.SkillInstanceId != draft.Source.SkillInstanceId || payment.GameplayHash != draft.GameplayHash ||
            !IsEquipmentSlotNamedSkill(payment.SkillId) || !IsSlashCard(draft.EffectiveKind) || !choice.Targets.SequenceEqual(targets) ||
            RequireConversionSource(choice) != draft.Source || choice.Parameters.GetValueOrDefault("program-action") != SayingTieredSlashAction ||
            draft.OriginalDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } original || original.PlayerSeat != actor ||
            original.SourceSeat != f.OwnerSeat || original.TargetSeat != actor || original.SkillPrompt?.SkillId != f.SkillId ||
            !original.Choices.Any(c => AssistedChoicesEqual([c], [choice])) ||
            draft.OriginalDecisionSignature != EquipmentSlotNamedDecisionSignature(original) || draft.TargetSignature != EquipmentSlotNamedTargetSignature(null, choice)) return false;
        return ProgramEventHistory<EquipmentSlotNamedUseStartedEvent>().Count(e => e.FrameId == payment.Id && e.Source == draft.Source &&
            e.GameplayHash == draft.GameplayHash && e.Intent == draft.Intent && e.EffectiveKind == draft.EffectiveKind && e.OriginalParentId == f.Id &&
            e.OriginalPromptId == original.PromptId && e.OriginalRevision == original.Revision && e.OriginalDecisionSignature == draft.OriginalDecisionSignature &&
            e.TargetSignature == draft.TargetSignature && e.ActualTurn == draft.ActualTurn && e.ActualTurnOwner == draft.ActualTurnOwner && e.PhaseInstance == draft.PhaseInstance) == 1;
    }

    private bool SayingEquipmentSlotUseStillLegal(CharacterState actor, EquipmentSlotNamedUseDraft draft) =>
        draft.Intent == EquipmentSlotNamedUseIntent.SayingSlash && draft.OriginalParentId is { } parentId &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame f && f.Id == parentId &&
        draft.ResponseChoice is { } choice && TryGetSayingSlashRequest(f, out var originalActor, out _) && actor.Seat == originalActor &&
        SayingTieredProgramSlashChoices(f).Any(c => AssistedChoicesEqual([c], [choice]));
}
