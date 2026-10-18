using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private ActualDiscardRecoveryPhaseKey? MouLixianPhase(int owner, SkillProgramTriggerWindow window)
    {
        var phase = CurrentActualDiscardRecoveryPhase();
        var kind = window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ? ActualDiscardRecoveryPhaseKind.Preparation : ActualDiscardRecoveryPhaseKind.Ending;
        return phase is { } p && p.Kind == kind && p.ActorSeat == owner && p.TurnNumber == _turnNumber && p.ActualTurnOwnerSeat == _turnProgression.OwnerSeat &&
            IsRecordedActualDiscardRecoveryPhase(p) ? p : null;
    }
    private bool MouLixianWindowAvailable(int owner, ProgramSkillWindowContext context) => context.Window switch
    {
        SkillProgramTriggerWindow.AfterDamageApplied => context.TargetSeat == owner && context.Amount > 0,
        SkillProgramTriggerWindow.TurnStartBeforeNormalFlow => MouLixianPreparationUnlocked(owner) && MouLixianPhase(owner, context.Window) is not null,
        SkillProgramTriggerWindow.TurnEnding => MouLixianEndingUnlocked(owner) && MouLixianPhase(owner, context.Window) is not null,
        _ => false
    };
    private IReadOnlyList<Card> MouLixianMaterials(int owner) => Array.AsReadOnly(GetHand(_players[owner]).Concat(GetEquipment(_players[owner]))
        .Concat(_cardZones.CardsAt(CardLocation.WoodenOxGrain(owner))).DistinctBy(c => c.Id)
        .Where(c => !IsTurnHandCardRestricted(_players[owner], c) && !IsTurnPhysicalUseForbidden(owner, [c.Id])).ToArray());
    private IReadOnlyList<ProgramOrdinaryTrickUseOption> MouLixianOptions(ProgramSkillFrame f, Card card, CardKind kind) =>
        BuildProgramOrdinaryTrickUseOptions(_players[f.OwnerSeat], kind, EffectiveSuit(_players[f.OwnerSeat], card), enforceUsePermission: true,
            beneficiaryShieldSuit: EffectiveSuit(_players[f.OwnerSeat], card), actualEffectiveColor: SuitColor(EffectiveSuit(_players[f.OwnerSeat], card)),
            hasActualColor: true, physicalCardIds: [card.Id], includeNextActualUseAdjustment: true, conversionSource: MouHuangYueyingSource(f))
            .Where(o => o.ActionKind != LegalActionKind.Recast).ToArray();
    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28960)]
    private bool CanOfferMouLixian(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger, ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveMouHuangYueyingLixian)) return true;
        if (candidate.SkillId != MouLixianSkill || !_players[candidate.OwnerSeat].IsAlive || !MouLixianWindowAvailable(candidate.OwnerSeat, context) ||
            GetSkillBindingShard(_players[candidate.OwnerSeat]).ProgramInstances.Where(i => i.SkillId == MouLixianSkill).OrderBy(i => i.SkillInstanceId, StringComparer.Ordinal).FirstOrDefault()?.SkillInstanceId != candidate.SkillInstanceId ||
            ProgramEventHistory<MouLixianStartedEvent>().Any(e => e.Source.OwnerSeat == candidate.OwnerSeat && e.Window == context.Window && e.OriginFrameId == context.ParentFrameId)) return false;
        var owner = _players[candidate.OwnerSeat];
        return MouLixianMaterials(owner.Seat).Any(card => MouLixianNames(owner.Seat).Any(kind =>
            BuildProgramOrdinaryTrickUseOptions(owner, kind, EffectiveSuit(owner, card), enforceUsePermission: true,
                beneficiaryShieldSuit: EffectiveSuit(owner, card), actualEffectiveColor: SuitColor(EffectiveSuit(owner, card)), hasActualColor: true,
                physicalCardIds: [card.Id], includeNextActualUseAdjustment: true,
                conversionSource: new(candidate.SkillId, candidate.BindingId, candidate.OwnerSeat, candidate.SkillInstanceId)).Any(o => o.ActionKind != LegalActionKind.Recast)));
    }
    private SkillProgramStepOutcome BeginMouLixian(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.MouLixian is not null) { ContinueMouLixian(f.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (f.SkillId != MouLixianSkill || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            f.WindowContext is not { } context || !MouLixianWindowAvailable(f.OwnerSeat, context) || !_players[f.OwnerSeat].IsAlive)
            throw new InvalidOperationException("Lixian requires its original damage/actually unlocked Preparation or Ending binding.");
        var phase = context.Window == SkillProgramTriggerWindow.AfterDamageApplied ? null : MouLixianPhase(f.OwnerSeat, context.Window);
        var r = new MouLixianReceipt(MouHuangYueyingSource(f), f.GameplayHash, context.ParentFrameId, context.ParentFrameId)
        { Stage = MouLixianStage.NameChoice, Phase = phase, Before = _movementSequence, After = _movementSequence };
        ReplaceRuntimeTop(f = f with { MouLixian = r });
        AdvanceEventRulesAndQueueFact(new MouLixianStartedEvent(f.Id, r.Source, r.GameplayHash, context.Window, r.OriginFrameId, phase));
        PublishMouLixian(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> MouLixianChoices(ProgramSkillFrame f)
    {
        var r = f.MouLixian!; var choices = new List<PromptChoice>();
        PromptChoice C(string branch, string suffix, string label, int? card = null, CardKind? kind = null, IReadOnlyList<int>? targets = null) =>
            new(new($"mou-lixian.{f.Id}.{branch}.{suffix}"), label, card is { } id ? [id] : [], targets ?? [],
                new Dictionary<string, string> { ["program-action"] = "mou-lixian", ["branch"] = branch,
                    ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["output-kind"] = kind?.ToString() ?? "" });
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return [];
        var materials = MouLixianMaterials(f.OwnerSeat);
        if (r.Stage == MouLixianStage.NameChoice)
            choices.AddRange(MouLixianNames(f.OwnerSeat).Where(kind => materials.Any(c => MouLixianOptions(f, c, kind).Count > 0))
                .Select(kind => C("name", kind.ToString(), $"使用理贤：转化为【{CardCatalog.Get(kind).DisplayName}】", kind: kind)));
        else if (r.Stage == MouLixianStage.MaterialChoice && r.OutputKind is { } name)
            choices.AddRange(materials.Where(c => MouLixianOptions(f, c, name).Count > 0).Select(c => C("material", c.Id.ToString(CultureInfo.InvariantCulture), $"将【{c.DisplayName}】当【{CardCatalog.Get(name).DisplayName}】使用", c.Id, name)));
        else if (r.Stage == MouLixianStage.TargetChoice && r.OutputKind is { } output && r.MaterialId is { } cardId && materials.SingleOrDefault(c => c.Id == cardId) is { } card)
            choices.AddRange(MouLixianOptions(f, card, output).Select(o => C("target", o.Id.Value, o.Description, cardId, output, o.TargetSeats)));
        choices.Add(C("cancel", "none", "取消理贤")); return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMouLixian(ProgramSkillFrame f)
    {
        var choices = MouLixianChoices(f); if (choices.Count == 0) { FinishMouLixian(f); return; }
        PublishParticipantHandChoice(f, f.OwnerSeat, choices, f.MouLixian!.Stage switch {
            MouLixianStage.NameChoice => "理贤：选择已解锁的锦囊牌名", MouLixianStage.MaterialChoice => "理贤：选择一张真实手牌、装备或木牛流马粮", _ => "理贤：选择原生合法目标" }, f.OwnerSeat);
        AdvanceRulesAndPublishState();
    }
    [ProgramAnswer("mou-lixian")]
    private void AnswerMouLixian(PromptChoice choice)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLixian: { } r } f || _pendingDecision?.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], MouLixianChoices(f).Where(c => c.Id == choice.Id).ToArray())) throw new InvalidOperationException("Lixian cannot answer an unpublished source, material or target.");
        ClearPendingDecision();
        switch (choice.Parameters["branch"])
        {
            case "cancel": FinishMouLixian(f); return;
            case "name": ReplaceRuntimeTop(f = f with { MouLixian = r with { OutputKind = Enum.Parse<CardKind>(choice.Parameters["output-kind"]), Stage = MouLixianStage.MaterialChoice } }); PublishMouLixian(f); return;
            case "material":
                var card = MouLixianMaterials(f.OwnerSeat).Single(c => c.Id == choice.Cards.Single());
                ReplaceRuntimeTop(f = f with { MouLixian = r with { Stage = MouLixianStage.TargetChoice, MaterialId = card.Id, PrintedKind = card.Kind,
                    From = _cardZones.GetLocation(card.Id), EffectiveSuit = EffectiveSuit(_players[f.OwnerSeat], card), EffectiveRank = EffectiveOwnedCardRank(_players[f.OwnerSeat], card) } });
                AdvanceEventRulesAndQueueFact(new MouLixianMaterialSelectedEvent(f.Id, r.OutputKind!.Value, card.Id, card.Kind, _cardZones.GetLocation(card.Id), EffectiveSuit(_players[f.OwnerSeat], card), EffectiveOwnedCardRank(_players[f.OwnerSeat], card)));
                PublishMouLixian(f); return;
            case "target": DispatchMouLixian(f, choice); return;
            default: throw new InvalidOperationException("Lixian cannot invent a use branch.");
        }
    }
    private void DispatchMouLixian(ProgramSkillFrame f, PromptChoice choice)
    {
        var r = f.MouLixian!; var actor = _players[f.OwnerSeat]; var card = MouLixianMaterials(f.OwnerSeat).Single(c => c.Id == r.MaterialId);
        var option = MouLixianOptions(f, card, r.OutputKind!.Value).Single(o => choice.Id.Value == $"mou-lixian.{f.Id}.target.{o.Id.Value}" && o.TargetSeats.SequenceEqual(choice.Targets));
        if (_cardZones.GetLocation(card.Id) != r.From || card.Kind != r.PrintedKind || EffectiveSuit(actor, card) != r.EffectiveSuit || EffectiveOwnedCardRank(actor, card) != r.EffectiveRank)
            throw new InvalidOperationException("Lixian's selected real entity changed its original zone or effective appearance before native acceptance.");
        ReplaceRuntimeTop(f = f with { MouLixian = r with { Stage = MouLixianStage.NativeIssuing, Paid = true, Targets = option.TargetSeats, Before = _movementSequence, After = _movementSequence } });
        r = f.MouLixian!;
        if (option.NextActualUseAdjusted) _selectedNextCardTargetSeats = option.TargetSeats;
        var appearance = card with { Suit = r.EffectiveSuit, Rank = r.EffectiveRank ?? card.Rank };
        var id = BeginCardUse(appearance, actor.Seat, option.TargetSeats, r.OutputKind.Value, physicalCardIds: [card.Id], conversionSource: r.Source);
        if (LifecycleCardUse(id)?.MouLixianOrigin is null) throw new InvalidOperationException("Lixian's true material use must bind its owning native return before payment.");
        MoveCard(card, r.From!.Value, CardLocation.Processing, CardMoveReasons.Use, disclosure: CardMovementDisclosure.FaceUp);
        var current = _resolutionStack.OfType<ProgramSkillFrame>().Single(p => p.Id == f.Id);
        if (current.MouLixian!.MovementSequence <= r.Before || ProgramEventHistory<MouLixianMaterialPaidEvent>().Count(p => p.UseId == id && p.CardId == card.Id) != 1)
            throw new InvalidOperationException("Lixian's original material must sign its native movement before removal children.");
        var targets = LifecycleCardUse(id)!.TargetSeats;
        AdvanceEventRulesAndQueueFact(new ProgramViewAsConvertedEvent(id, r.Source.SkillId, r.Source.BindingId, actor.Seat, [card.Id], r.OutputKind.Value, true, targets));
        BeginJizhiOrNullificationWindow(id, appearance, actor.Seat, targets, option.ActionKind, option.TargetCardId, option.RequiredCardKind, r.OutputKind.Value);
    }
    private void ContinueMouLixian(long id)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLixian: { } r } f || f.Id != id) return;
        if (!r.Paid) { PublishMouLixian(f); return; }
        if (!IsReturnedMouLixianUse(f))
            throw new InvalidOperationException("Lixian cannot advance before its whole accepted native Use returns.");
        if (DrainMouHuangYueying(f)) return;
        f = GetActiveProgramFrame(id); ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); FinishMouLixian(f);
    }
    private void FinishMouLixian(ProgramSkillFrame f)
    {
        var r = f.MouLixian!; AdvanceEventRulesAndQueueFact(new MouLixianCompletedEvent(f.Id, r.UseId, r.Paid));
        ReplaceRuntimeTop(f = f with { MouLixian = null, PendingMovementContinuation = null }); FinishProgramSkill(f, r.Paid);
    }
}
