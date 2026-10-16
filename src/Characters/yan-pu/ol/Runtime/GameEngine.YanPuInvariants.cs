namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 26410)]
    private void AssertYanPu(ProgramSkillFrame f)
    {
        if (f.YanPu is not { } r) return;
        var e = GetPausedPrivateOfferEffect(f);
        if (f.TriggerId is null || f.InstructionIndex != 1 || r.InstructionIndex != f.InstructionIndex || r.Source != YanPuSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != r.GameplayHash || e is null || !YanPuComposition.IsOperation(e.Op) ||
            r.WindowId != f.WindowContext?.ParentFrameId || r.Round != _roundNumber || r.Turn != _turnNumber || r.TurnOwner != _currentSeat ||
            !IsValidPlayerSeat(r.Target) || !Enum.IsDefined(r.Stage) || r.SelectedCards.Count != r.SelectedCards.Distinct().Count() ||
            r.SelectedCards.Count > r.RequiredGift || r.RequiredGift is < 0 or > 2 ||
            ProgramEventHistory<YanPuStartedEvent>().Count(x => x.ProgramFrameId == f.Id && x.Source == r.Source && x.GameplayHash == r.GameplayHash &&
                x.WindowId == r.WindowId && x.Round == r.Round && x.Turn == r.Turn && x.TurnOwner == r.TurnOwner && x.Target == r.Target) != 1)
            throw new InvalidOperationException("YanPu lost its owning instruction, source, actual round/turn, physical draft or one issued start.");
        if (e.Op == SkillProgramEffectOp.GiveCardSkipDrawForEnding)
        {
            if (r.Stage is not (YanPuStage.GiftChoice or YanPuStage.GiftChildren) || r.Exited is not null || r.Target != _currentSeat ||
                _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p => p.Id == r.WindowId) is not
                    { Window: SkillProgramTriggerWindow.DrawPhaseStarting } parent || parent.OwnerSeat != r.Target ||
                r.Stage == YanPuStage.GiftChoice && (r.Promise is not null || r.Movements.Count != 0) ||
                r.Stage == YanPuStage.GiftChildren && (r.Promise is not { } p || !ValidYanPuPromise(p) || p.Id != f.Id ||
                    parent.ActualPhaseSubstitution is not { } substitute || substitute.ParentFrameId != parent.Id || substitute.ProducerFrameId != f.Id ||
                    substitute.OwnerSeat != r.Target || substitute.ActualTurnNumber != r.Turn || substitute.SkippedPhase != SkillProgramTurnPhase.Draw))
                throw new InvalidOperationException("Huantu lost its exact Draw parent or its native gift/substitution invoice.");
        }
        else if (e.Op == SkillProgramEffectOp.ResolveHuantuEnding)
        {
            if (YanPuCurrentEndingFrameCandidate(f) is not { } c || r.Promise is not { } p || CurrentYanPuEnding(c, f.WindowContext!) != p || r.Exited is not null ||
                ProgramEventHistory<YanPuHuantuConsumedEvent>().Count(x => x.PromiseId == p.Id && x.EndingWindowId == r.WindowId && x.ProgramFrameId == f.Id) != 1 ||
                r.Stage is YanPuStage.GiftChoice or YanPuStage.GiftChildren or YanPuStage.ExitDrawChildren)
                throw new InvalidOperationException("Huantu Ending lost its exact consumed original-turn gift obligation.");
        }
        else if (r.Stage != YanPuStage.ExitDrawChildren || r.Promise is not null || r.Exited is not { } exited ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().SingleOrDefault(p => p.Id == r.WindowId) is not { } window || window.YanPuDyingExit != exited ||
            exited.VictimSeat != r.Target || exited.Round != r.Round || exited.Turn != r.Turn || exited.TurnOwner != r.TurnOwner ||
            ProgramEventHistory<DyingResolvedEvent>().Count(d => d.ResolutionId == exited.DyingFrameId && d.VictimSeat == exited.VictimSeat && d.Survived) != 1)
            throw new InvalidOperationException("Bihuo lost its actual survived Dying transition or typed native return.");
        var issued = ProgramEventHistory<YanPuMovementIssuedEvent>().Where(x => x.ProgramFrameId == f.Id).ToArray();
        if (issued.Length != r.Movements.Count) throw new InvalidOperationException("YanPu movement cursor differs from its issued history.");
        for (var i = 0; i < r.Movements.Count; i++)
        {
            var m = r.Movements[i];
            if (m.Index != i || issued[i].Movement != m || m.Before < 0 || m.After < m.Before || m.After > _movementSequence || m.Actual < 0)
                throw new InvalidOperationException("YanPu lost an ordered immutable movement invoice.");
            var actual = _cardMovements.Where(move => move.Sequence > m.Before && move.Sequence <= m.After && move.Reason.Value == m.Reason).ToArray();
            if (m.Reason.EndsWith(".draw", StringComparison.Ordinal) && (actual.Length != m.Actual || actual.Any(move => move.From != CardLocation.DrawPile || move.To != CardLocation.Hand(m.SubjectSeat))) ||
                m.Reason == YanPuGiftReason && (actual is not [var gift] || gift.CardId != m.CardId || gift.From != m.From || gift.To != m.To || m.Actual != 1) ||
                m.Reason.EndsWith(".ending-gift", StringComparison.Ordinal) && (actual.Length != m.Actual || m.Actual != r.RequiredGift ||
                    !actual.Select(move => move.CardId).Order().SequenceEqual(r.SelectedCards.Order()) || actual.Any(move => move.From != CardLocation.Hand(f.OwnerSeat) || move.To != CardLocation.Hand(r.Target))))
                throw new InvalidOperationException("YanPu invoice differs from its exact real transfer or private draw records.");
        }
    }
}
