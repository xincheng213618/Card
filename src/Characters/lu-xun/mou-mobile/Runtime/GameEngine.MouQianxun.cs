namespace CardGame.Core;

public sealed partial class GameEngine
{
    private void PayMouLuXunHold(ProgramSkillFrame f)
    {
        var r = f.MouLuXun!;
        if (!ValidMouLuXun(f) || r.Stage != MouLuXunStage.HoldChoice || r.Selected.Count is < 1 || r.Selected.Count > r.Limit ||
            r.Selected.Distinct().Count() != r.Selected.Count || r.Selected.Any(id => _cardZones.GetLocation(id) is not { OwnerSeat: var owner, Zone: CardZoneKind.Hand or CardZoneKind.Equipment } || owner != f.OwnerSeat))
            throw new InvalidOperationException("Qianxun can only hold its accepted bounded actual HE subset.");
        var location = new CardLocation(CardZoneKind.PrivateTurnHold, f.OwnerSeat,
            privateTurnHold: new(f.Id, f.SkillId, f.SkillInstanceId, r.GrantSourceId, r.ActualTurn));
        _cardZones.EnsurePrivateTurnHold(location);
        ReplaceRuntimeTop(f = f with { MouLuXun = r with { Stage = MouLuXunStage.HoldChildren, HoldLocation = location,
            Before = _movementSequence, After = _movementSequence }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(r.Selected, location, new("skill-program.mou-mobile:qianxun.hold"),
            (batch, movements) =>
            {
                var current = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with { MouLuXun = current.MouLuXun! with { After = _movementSequence } });
                for (var i = 0; i < movements.Count; i++)
                {
                    var move = movements[i];
                    AdvanceEventRulesAndQueueFact(new MouLuXunEntityMovedEvent(f.Id, i, move.CardId, move.From, move.To, move.Sequence));
                }
            }, disclosure: CardMovementDisclosure.FaceDown);
        ContinueMouLuXun(f.Id);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 32800)]
    private bool ResumeMouLuXun(long id) => ContinueMouLuXun(id);

    [ProgramContinuation(ProgramContinuationKind.Movement, 32800)]
    private bool ResumeMouLuXunMovement(ProgramSkillFrame frame) => ContinueMouLuXun(frame.Id);

    private bool ContinueMouLuXun(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouLuXun: { } r } f || f.Id != id || _pendingDecision is not null) return false;
        if (r.Stage is MouLuXunStage.HoldChoice or MouLuXunStage.NameChoice or MouLuXunStage.UseChoice or
            MouLuXunStage.IssuingUse or MouLuXunStage.NativeUse or MouLuXunStage.Distribution) return false;
        if (DrainMouLuXun(f)) return true;
        f = GetActiveProgramFrame(id); r = f.MouLuXun!;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        if (r.Stage is MouLuXunStage.HoldChildren or MouLuXunStage.NativeReturned or MouLuXunStage.ReturnChildren)
        { FinishMouLuXun(f); return true; }
        if (r.Stage == MouLuXunStage.GiftChildren)
            ReplaceRuntimeTop(f = f with { MouLuXun = r = r with { Cursor = r.Cursor + 1 } });
        if (!_players[f.OwnerSeat].IsAlive || _winner != Winner.None)
        {
            ReturnMouLuXunUnassigned(f); return true;
        }
        if (r.Cursor >= r.Viewed.Count) { FinishMouLuXun(f); return true; }
        ReplaceRuntimeTop(f = f with { MouLuXun = r with { Stage = MouLuXunStage.Distribution } });
        PublishMouLuXun(f); return true;
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 32800)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 32800)]
    private bool ContinueIssuedMouLuXun(ProgramSkillFrame f) => f.MouLuXun is { Paid: true } && ValidMouLuXun(f);

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 32800)]
    private bool IsMouLuXunMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.MouLuXun is { Stage: MouLuXunStage.HoldChildren or MouLuXunStage.ViewChildren or MouLuXunStage.GiftChildren or MouLuXunStage.ReturnChildren } r &&
        effect?.Op == r.Operation && ValidMouLuXun(f) && pending == new ProgramMovementContinuation(f.OwnerSeat, 0, null) &&
        r.Before <= r.After && r.After <= _movementSequence;

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 32800)]
    private void AssertMouLuXun(ProgramSkillFrame f)
    {
        if (f.MouLuXun is not { } r) return;
        if (!ValidMouLuXun(f) || !Enum.IsDefined(r.Stage) || r.Limit is < 0 or > 5 || r.Cursor < 0 || r.Cursor > r.Viewed.Count ||
            r.Selected.Distinct().Count() != r.Selected.Count || r.Viewed.Distinct().Count() != r.Viewed.Count || r.Viewed.Count > r.Limit ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence)
            throw new InvalidOperationException("Lu Xun lost its issued source, bounded original physical entities or actual-turn cursor.");
        if (r.Operation == SkillProgramEffectOp.RecordMouLuXunQianxun &&
            (r.Kind is null || ProgramEventHistory<MouLuXunNameRecordedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat &&
                e.Kind == r.Kind && e.WindowId == r.WindowId && e.ActualTurn == r.ActualTurn) != 1))
            throw new InvalidOperationException("Qianxun lost its exact real trick-effect name registration.");
        if (r.Operation == SkillProgramEffectOp.UseMouLuXunRecordedTrick && r.Paid &&
            ProgramEventHistory<MouLuXunNameRemovedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.Kind == r.Kind && e.ActualTurn == r.ActualTurn) != 1)
            throw new InvalidOperationException("Qianxun cannot re-use or refund a paid recorded name.");
        if (r.Viewed.Select((card, index) => (card, index)).Any(pair =>
            ProgramEventHistory<MouLuXunTopViewedEvent>().Count(e => e.FrameId == f.Id && e.Index == pair.index && e.CardId == pair.card) != 1))
            throw new InvalidOperationException("Lianying lost an exact privately reserved top entity.");
    }
    [ProgramAiChoice(SkillProgramEffectOp.RecordMouLuXunQianxun, SkillProgramEffectOp.UseMouLuXunRecordedTrick, SkillProgramEffectOp.DistributeMouLuXunLianying)]
    private PromptChoice ChooseMouLuXunAi(PendingDecision prompt, ProgramSkillFrame f)
    {
        var r = f.MouLuXun!;
        if (r.Stage == MouLuXunStage.HoldChoice)
        {
            var dangerous = r.Kind is CardKind.Snatch or CardKind.Dismantlement or CardKind.FireAttack;
            return dangerous ? prompt.Choices.FirstOrDefault(c => c.Parameters["verb"] == "toggle" && !r.Selected.Contains(c.Cards[0]))
                ?? prompt.Choices.Single(c => c.Parameters["verb"] == "confirm") : prompt.Choices.Single(c => c.Parameters["verb"] == "confirm");
        }
        if (r.Stage == MouLuXunStage.Distribution)
            return prompt.Choices.Where(c => AreProgramDistributionAllies(_players[f.OwnerSeat], _players[c.Targets[0]]))
                .OrderBy(c => GetHand(_players[c.Targets[0]]).Count).FirstOrDefault() ?? prompt.Choices[0];
        if (r.Stage == MouLuXunStage.NameChoice) return prompt.Choices[0];
        var options = MouLuXunUseOptions(f);
        return prompt.Choices.FirstOrDefault(c => c.Parameters["verb"] == "use" &&
            (options[int.Parse(c.Parameters["value"])].ActionKind is LegalActionKind.DrawTwo or LegalActionKind.FiveGrains or LegalActionKind.PeachGarden ||
             c.Targets.Any(target => !AreProgramDistributionAllies(_players[f.OwnerSeat], _players[target])))) ?? prompt.Choices.Last();
    }
}
