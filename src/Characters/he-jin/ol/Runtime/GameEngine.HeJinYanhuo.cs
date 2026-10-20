using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ExactHeJinDeathEntry(ProgramSkillFrame f, out DeathFrame death, out ProgramDeathTriggerWindowFrame window)
    {
        death = null!; window = null!; var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (i < 2 || _resolutionStack[i - 1] is not ProgramDeathTriggerWindowFrame w || _resolutionStack[i - 2] is not DeathFrame d ||
            w.DeathFrameId != d.Id || w.OwnerSeat != f.OwnerSeat || d.VictimSeat != f.OwnerSeat || w.KillerSeat != d.KillerSeat ||
            w.CandidateIndex < 0 || w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]) ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.OwnerDied } c || c.ParentFrameId != w.Id || c.OwnerSeat != f.OwnerSeat ||
            c.SourceSeat != d.KillerSeat || c.TargetSeat != d.VictimSeat || _players[f.OwnerSeat].IsAlive) return false;
        death = d; window = w; return true;
    }
    // "Your cards" are owned Hand/Equipment, not cards merely situated in Judgment or a public skill pile.
    private int HeJinDeathOwnedCardCount(DeathFrame death) => death.HeJinYanhuoCards is { } cards ? cards.HandIds.Count + cards.EquipmentIds.Count : 0;
    private void CaptureHeJinYanhuoDeath(long deathId, CharacterState victim)
    {
        if (!EnabledContentSkillIds(victim).Any(id => _contentRegistry.Skills.GetValueOrDefault(id)?.Program?.Triggers.Any(t => t.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveYanhuoKillerDiscard)) == true)) return;
        var cards = new HeJinDeathCards(victim.Seat) { HandIds = GetHand(victim).Select(c => c.Id).ToArray(), EquipmentIds = GetEquipment(victim).Select(c => c.Id).ToArray() };
        UpdateDeathFrame(deathId, f => f with { HeJinYanhuoCards = cards });
        AdvanceEventRulesAndQueueFact(new HeJinYanhuoDeathCapturedEvent(deathId, victim.Seat, cards.HandIds.Count, cards.EquipmentIds.Count));
    }
    private SkillProgramStepOutcome BeginHeJinYanhuo(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.HeJinYanhuo is not null || f.SkillId != "ol:yanhuo" || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 ||
            !ExactHeJinDeathEntry(f, out var death, out var w)) throw new InvalidOperationException("Yanhuo requires the exact deceased owner's native death cursor.");
        if (death.KillerSeat is not { } killer || !IsValidPlayerSeat(killer) || killer == f.OwnerSeat || !_players[killer].IsAlive || HeJinDeathOwnedCardCount(death) == 0)
        { FinishProgramSkill(f, true); return SkillProgramStepOutcome.AwaitChild; }
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        var dying = death.ReturnKind == DeathReturnKind.Dying && i >= 3 && _resolutionStack[i - 3] is DyingFrame d && d.Id == death.ParentFrameId ? d : null;
        if (death.ReturnKind == DeathReturnKind.Dying && dying is null || death.ReturnKind is null || death.ReturnKind == DeathReturnKind.ProgramSkill &&
            (i < 3 || _resolutionStack[i - 3] is not ProgramSkillFrame p || p.Id != death.ParentFrameId))
            throw new InvalidOperationException("Yanhuo's original native death lost its typed parent.");
        var damage = dying?.Continuation == DyingContinuationKind.Damage ? _resolutionStack.OfType<DamageFrame>().SingleOrDefault(x => x.Id == dying.ParentFrameId) : null;
        if (dying?.Continuation == DyingContinuationKind.Damage && damage is null) throw new InvalidOperationException("Yanhuo lost the original damage parent.");
        var r = new HeJinYanhuoReceipt(HeJinSource(f), f.GameplayHash, f.InstructionIndex, w.Id, w.CandidateIndex, w.Step, w.Candidates[w.CandidateIndex],
            new(death.Id, death.ParentFrameId, death.VictimSeat, death.KillerSeat, death.ReturnKind, death.Step, death.OwnerDiedProgramsResolved, death.KillerProgramsResolved, death.CleanedUpCardIds),
            dying is null ? null : new(dying.Id, dying.ParentFrameId, dying.VictimSeat, dying.KillerSeat, dying.Continuation, dying.Step, dying.ResponderIndex, dying.ResponderSeats, dying.AttemptedSelfDyingBindings),
            damage is null ? null : new(damage.Id, damage.ParentFrameId, damage.SourceSeat, damage.TargetSeat, damage.Amount, damage.Nature, damage.Step),
            CurrentDamageAttempt?.ResolutionId, _turnNumber, _currentSeat, killer, HeJinDeathOwnedCardCount(death));
        ReplaceRuntimeTop(f = f with { HeJinYanhuo = r });
        AdvanceEventRulesAndQueueFact(new HeJinYanhuoStartedEvent(f.Id, r.Source, r.GameplayHash, death.Id, killer, r.Limit, HeJinDeathReturnHash(r)));
        PublishHeJinPrompt(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> HeJinYanhuoChoices(ProgramSkillFrame f)
    {
        var r = f.HeJinYanhuo!; var result = new List<PromptChoice>();
        if (r.Stage != HeJinYanhuoStage.ChoosingCard) return [];
        if (r.SelectedCards.Count < r.Limit && _players[r.KillerSeat].IsAlive)
            result.AddRange(BuildOwnedCardPaymentChoices(f.Id, f.OwnerSeat, r.KillerSeat, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard,
                canSelect: (_, card) => r.SelectedCards.All(s => s.CardId != card.Id)).Select(c => c with
                { Parameters = new Dictionary<string, string>(c.Parameters) { ["program-action"] = "he-jin-yanhuo", ["branch"] = "card" } }));
        result.Add(new(new($"he-jin.{f.Id}.yanhuo.confirm.{r.SelectedCards.Count}"), $"确认弃置{r.SelectedCards.Count}张牌", [], [],
            new Dictionary<string, string> { ["program-action"] = "he-jin-yanhuo", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = "confirm" }));
        return Array.AsReadOnly(result.ToArray());
    }
    [ProgramAnswer("he-jin-yanhuo")]
    private void AnswerHeJinYanhuo(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Yanhuo lost its owner.");
        RequireHeJin(f); var r = f.HeJinYanhuo!;
        if (_pendingDecision is not { IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat || !HeJinYanhuoChoices(f).Any(c => AssistedChoicesEqual([c], [choice])))
            throw new InvalidOperationException("Yanhuo needs its deceased owner's exact opaque card choice.");
        ClearPendingDecision();
        if (choice.Parameters["branch"] == "card")
        {
            var zone = Enum.Parse<CardZoneKind>(choice.Parameters["source-zone"]); var slot = int.Parse(choice.Parameters["slot-index"], CultureInfo.InvariantCulture);
            var from = new CardLocation(zone, r.KillerSeat); var card = _cardZones.CardsAt(from)[slot];
            ReplaceRuntimeTop(f = f with { HeJinYanhuo = r with { SelectedCards = r.SelectedCards.Append(new(card.Id, from)).ToArray() } });
            PublishHeJinPrompt(f); return;
        }
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { HeJinYanhuo = r with { Stage = HeJinYanhuoStage.DiscardChildren, Discarded = r.SelectedCards.Count, Before = before, After = before } });
        // Freeze the whole operation before paying either zone; newly gained cards cannot enter this discard.
        var logicalDiscard = CaptureNativeLogicalDiscardInvocation(f.OwnerSeat, f.OwnerSeat,
            r.SelectedCards.Select(card => card.CardId).ToArray(), new(HeJinDiscardReason), f.Id);
        foreach (var group in r.SelectedCards.GroupBy(s => (s.From, Outside: s.From.Zone == CardZoneKind.Equipment && GetAttackCard(s.CardId).IsGeneralWeapon)))
            MoveCards(group.Select(s => GetAttackCard(s.CardId)).ToArray(), group.Key.From, group.Key.Outside ? CardLocation.OutsideGame : CardLocation.DiscardPile, new(HeJinDiscardReason));
        CompleteNativeLogicalDiscardInvocation(logicalDiscard);
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f = f with { HeJinYanhuo = f.HeJinYanhuo! with { After = _movementSequence } });
        foreach (var (selected, index) in r.SelectedCards.Select((s, index) => (s, index)))
            AdvanceEventRulesAndQueueFact(new HeJinYanhuoDiscardPaidEvent(f.Id, index, r.KillerSeat, selected.CardId, selected.From, before, _movementSequence));
        AdvanceRuntimeProgram(f.Id);
    }
    private void ResumeHeJinYanhuo(ProgramSkillFrame f)
    {
        if (f.HeJinYanhuo!.Stage == HeJinYanhuoStage.ChoosingCard) { if (_pendingDecision is null) PublishHeJinPrompt(f); return; }
        if (DrainHeJinChildren(f.Id)) return;
        f = GetActiveProgramFrame(f.Id);
        AdvanceEventRulesAndQueueFact(new HeJinYanhuoCompletedEvent(f.Id, f.HeJinYanhuo!.Discarded));
        // The receipt remains until the frame is popped, keeping the original dead Dying suspended through the return.
        FinishProgramSkill(f, true);
    }
    private static string HeJinDeathReturnHash(HeJinYanhuoReceipt r) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_he_jin_ol_HeJinYanhuo.HeJinDeathReturnHash(r);
    private bool IsOriginalDyingSuspendedByHeJinYanhuo(DyingFrame dying) => IsValidPlayerSeat(dying.VictimSeat) && !_players[dying.VictimSeat].IsAlive &&
        _resolutionStack.OfType<ProgramSkillFrame>().Any(f => f.HeJinYanhuo?.OriginalDying?.FrameId == dying.Id && ValidHeJinYanhuo(f));
}
