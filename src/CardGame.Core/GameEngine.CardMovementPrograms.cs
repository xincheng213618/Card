namespace CardGame.Core;

/// <summary>Generic post-movement program host. It contains no skill ids.</summary>
public sealed partial class GameEngine
{
    private CardMovementTiming? CaptureMovementTiming() => !(HasLeZhouFeiLingkongCapability || HasZhaoZhongCapability || _contentRegistry.ProgramDependencies.UsesGainPhaseQualification || TracksRedOwnedLoss || HasActualHandGainCapability ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveAfterBatchGain) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveGuyingAtomicLossChoice)) && !HasOtherActualBasicDiscardCapability ? null :
        _programPhaseSchedule is { Phase: TurnPhase.Draw } scheduled
            ? new(_turnProgression.OwnerSeat, TurnPhase.Draw,
                scheduled.Frame.ZhangShiPingHongji is { Stage: HongjiStage.ExtraPhase } hongji ? hongji.RecipientSeat : scheduled.Frame.OwnerSeat)
            : new(_turnProgression.OwnerSeat, _phase, _currentSeat);

    private static bool IsGainPhaseQualified(SkillProgramTrigger trigger, int ownerSeat, CardMovementTiming? timing) =>
        trigger.GainPhaseQualification is null || timing is not null &&
        (timing.Phase != TurnPhase.Draw || timing.PhaseActorSeat != ownerSeat);

    private readonly Stack<long> _suppressedCardMovedEventBatchIds = new();

    private sealed class CardMovementBatchBuilder(
        long id,
        long? parentFrameId,
        long? parentBatchId,
        long? awaitingProgramFrameId,
        int turnNumber,
        IReadOnlyDictionary<CardLocation, int> sourceCountsBefore,
        IReadOnlyDictionary<CardLocation, int> destinationCountsBefore,
        ProgramSkillFrame? originProgram)
    {
        public long Id { get; } = id;
        public long? ParentFrameId { get; } = parentFrameId;
        public long? ParentBatchId { get; } = parentBatchId;
        public long? AwaitingProgramFrameId { get; } = awaitingProgramFrameId;
        public int TurnNumber { get; } = turnNumber;
        public IReadOnlyDictionary<CardLocation, int> SourceCountsBefore { get; } = sourceCountsBefore;
        public IReadOnlyDictionary<CardLocation, int> DestinationCountsBefore { get; } = destinationCountsBefore;
        public ProgramSkillFrame? OriginProgram { get; } = originProgram;
        public CardMovementTiming? MovementTiming { get; init; }
        public ActualDiscardRecoveryPhaseKey? DiscardRecoveryPhase { get; init; }
        public IReadOnlyDictionary<int,IReadOnlyList<XiaHuiMovementSource>>? XiaHuiOriginalSources { get; init; }
        public IReadOnlyList<RuanYuMiaoxianPaymentSource>? RuanYuMiaoxianPaymentSources { get; init; }
        public int? WenhePoisonRound { get; init; }
        public IReadOnlyList<WuZishengMovementSource> WuZishengSources { get; init; } = [];
        public IReadOnlyList<TangTangOwnedOrigin>? TangTangOwnedOrigins { get; init; }
        private IReadOnlyList<GuYinSourceSnapshot> _guYinSources = Array.AsReadOnly(Array.Empty<GuYinSourceSnapshot>());
        public IReadOnlyList<GuYinSourceSnapshot> GuYinSources
        { get => _guYinSources; init => _guYinSources = Array.AsReadOnly(value.ToArray()); }
        private IReadOnlyList<BeifenLossSource> _beifenLossSources = Array.AsReadOnly(Array.Empty<BeifenLossSource>());
        public IReadOnlyList<BeifenLossSource> BeifenLossSources
        { get => _beifenLossSources; init => _beifenLossSources = Array.AsReadOnly(value.ToArray()); }
        private IReadOnlyList<JiaoweiLossSource> _jiaoweiLossSources = Array.AsReadOnly(Array.Empty<JiaoweiLossSource>());
        public IReadOnlyList<JiaoweiLossSource> JiaoweiLossSources
        { get => _jiaoweiLossSources; init => _jiaoweiLossSources = Array.AsReadOnly(value.ToArray()); }
    }

    private CardMovementBatchBuilder BeginCardMovementBatch(IEnumerable<CardLocation> sourceLocations, IEnumerable<CardLocation> destinationLocations)
    {
        var sources = sourceLocations.Distinct().ToDictionary(
            location => location,
            location => _cardZones.Count(location));
        var batch = new CardMovementBatchBuilder(
            ++_resolutionSequence,
            _resolutionStack.LastOrDefault()?.Id,
            _activeCardMovementBatchIds.TryPeek(out var parentBatchId) ? parentBatchId : null,
            _resolutionStack.LastOrDefault() is not DrawFundedDistinctBasicFrame &&
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is
                { } awaited && IsAwaitingProgramMovement(awaited)
                    ? awaited.Id : null,
            _turnNumber,
            sources,
            destinationLocations.Distinct().ToDictionary(location => location, location => _cardZones.Count(location)),
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()) { WuZishengSources = CaptureWuZishengMovementSources(), GuYinSources = CaptureGuYinSources(), BeifenLossSources = CaptureBeifenLossSources(sources.Keys), JiaoweiLossSources = CaptureJiaoweiLossSources(sources.Keys), MovementTiming = CaptureMovementTiming(), DiscardRecoveryPhase = CurrentActualDiscardRecoveryPhase(), XiaHuiOriginalSources = CaptureXiaHuiOriginalSources(sources.Keys), TangTangOwnedOrigins = CaptureTangTangOwnedOrigins(sources.Keys), RuanYuMiaoxianPaymentSources = CaptureRuanYuMiaoxianPaymentSources(sources.Keys), WenhePoisonRound = CaptureWenhePoisonRound() };
        _activeCardMovementBatchIds.Push(batch.Id);
        return batch;
    }
    private void CompleteCardMovementBatch(
        CardMovementBatchBuilder batch,
        IReadOnlyList<CardMovementRecord> movements,
        bool committed)
    {
        if (!_activeCardMovementBatchIds.TryPop(out var activeBatchId) || activeBatchId != batch.Id)
            throw new InvalidOperationException("The atomic card-movement batch nesting changed unexpectedly.");
        if (committed)
            foreach (var movement in movements)
            {
                ObserveDongBaiMovement(movement, batch.XiaHuiOriginalSources is { } sources &&
                    sources.TryGetValue(movement.CardId, out var originalSources) ? originalSources : null);
                ObserveZhugeGuoMovement(movement);
            }
        if (!committed || movements.Count == 0 || !_setupComplete ||
            _winner != Winner.None || _status == EngineStatus.Completed)
            return;
        var sourceCounts = batch.SourceCountsBefore
            .OrderBy(item => item.Key.Zone)
            .ThenBy(item => item.Key.OwnerSeat)
            .Select(item => new CardMovementSourceCount(
                item.Key,
                item.Value,
                _cardZones.Count(item.Key)))
            .ToArray();
        CaptureActualDiscardRecoveryBatch(batch.Id, batch.DiscardRecoveryPhase, movements);
        CaptureNeighborDiscardOpportunity(batch.Id, batch.TurnNumber, movements);
        CaptureTurnDiscardSuitFact(batch.TurnNumber,movements);
        CaptureTurnRedDiscardCount(batch.TurnNumber,movements);
        CaptureActionDiscardFact(batch.Id,batch.TurnNumber,movements);
        CaptureSettledActionCards(batch.Id,batch.TurnNumber,movements);
        CaptureMouWujingAllowance(batch.Id, movements);
        CaptureOffTurnUsedCards(batch.Id,batch.TurnNumber,movements);
        CaptureFirstGameDomainCrossings(batch.Id,batch.TurnNumber,movements);
        var completed = new CardMovementBatchContext(
            batch.Id,
            batch.ParentFrameId,
            batch.ParentBatchId,
            batch.TurnNumber,
            Array.AsReadOnly(movements.ToArray()),
            Array.AsReadOnly(sourceCounts),
            batch.AwaitingProgramFrameId,
            batch.DestinationCountsBefore.OrderBy(item => item.Key.Zone).ThenBy(item => item.Key.OwnerSeat)
                .Select(item => new CardMovementSourceCount(item.Key, item.Value, _cardZones.Count(item.Key))).ToArray(),
            batch.OriginProgram?.SkillId, batch.OriginProgram?.SkillInstanceId, batch.OriginProgram?.OwnerSeat)
            { MovementTiming = batch.MovementTiming, DiscardRecoveryPhase = batch.DiscardRecoveryPhase };
        completed = CaptureNianOwnedTransferBatch(completed, batch.OriginProgram);
        completed = CaptureWenhePoisonDepartures(completed, batch.WenhePoisonRound);
        // A processing zone has no owner. Only the native use invoice can attribute
        // its exact completed physical materials to an optional owner skill.
        if (CaptureLiJueUseDiscard(completed) is { } usedTrick)
            completed = completed with { LiJueUseDiscard = usedTrick };
        ObserveCaoChunMovement(completed);
        CaptureSlashUsePaymentBatch(completed);
        CaptureForeignTurnActualHandGains(completed);
        CaptureQimeiMovementQualification(completed);
        if (CaptureSanwenGainQualifications(completed) is { } sanwen) completed = completed with { SanwenGains = sanwen };
        completed = CaptureBianFuRenMovementBatch(completed);
        completed = CaptureDuFuRenGainBatch(completed);
        completed = CaptureFeiYiSingletonBatch(completed);
        CaptureFeiYiTrickPaymentBatch(completed);
        CaptureWuJingHejiTrickPaymentBatch(completed);
        CaptureHuiyunTrickPaymentBatch(completed);
        CaptureQuHuangEquipmentBatch(completed, batch.OriginProgram);
        CaptureQiejianUsePaymentBatch(completed);
        CaptureJuewuUsePaymentBatch(completed);
        CaptureWenhePoisonUsePayment(completed);
        CaptureRuanYuMiaoxianPaymentBatch(completed, batch.RuanYuMiaoxianPaymentSources);
        CaptureCaoYuMinimumTransitions(completed);
        completed = CaptureSunRuOlChishiBatch(completed);
        CaptureDuXiBixiongBatch(completed);
        CaptureDuanJiongSaoguBatch(completed);
        CaptureRuanHuiMovementBatch(completed);
        CaptureWuJingLiubingBatch(completed);
        completed = CaptureTangTangGainBatch(completed, batch.TangTangOwnedOrigins);
        CaptureYuanJiAtomicLoss(completed);
        CaptureBeifenLossQualifications(completed, batch.BeifenLossSources);
        CaptureJiaoweiLossQualifications(completed, batch.JiaoweiLossSources);
        CaptureLeiluanPaymentBatch(completed);
        CaptureQingyuanGainBatch(completed);
        CaptureNativePublicPilePaymentBatch(completed);
        CaptureNativeTrueUseProcessingEntries(completed);
        RecordMouXiahouDunDiscardOrigins(completed);
        CaptureGuYinDiscardBatch(completed, batch.GuYinSources);
        CaptureJiyunMovementBatch(completed);
        CaptureXieLingYuMovementBatch(completed);
        CaptureBiriPotentialGainBatch(completed);
        completed = CaptureLingkongGainBatch(completed);
        CaptureGuanduDiscardBatch(completed);
        completed = CaptureWuZishengMovementBatch(completed, batch.WuZishengSources);
        _pendingCardsMovedBatches.Add(completed);
    }

    private bool HasCardsMovedProgramBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not CardsMovedTriggerWindowFrame frame)
            return false;
        if (frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count && !IsPendingWenhePoisonWindow(frame) ||
            frame.Batch.Id != frame.Id || frame.Batch.Movements.Count == 0)
            throw new InvalidOperationException("The cards-moved boundary lost its batch or candidate cursor.");
        if (_resolutionStack.Count == 1 && frame.Step == ResolutionFrameStep.AwaitingResponse &&
            _pendingDecision?.Kind != DecisionKind.ProgramTrigger)
            throw new InvalidOperationException("The cards-moved boundary is waiting without its program prompt.");
        return true;
    }

    private bool TryBeginCardsMovedProgramWindow(long? instructionFrameId = null, int? subjectSeat = null)
    {
        var drawFunded = _resolutionStack.LastOrDefault() is DrawFundedDistinctBasicFrame funded &&
            funded.Id == instructionFrameId ? funded : null;
        var friendResponse = _resolutionStack.LastOrDefault() is FriendDuelResponseFrame friend && friend.Id == instructionFrameId ? friend : null;
        var factionRequestCost = _resolutionStack.LastOrDefault() is { PaidFactionRequestCostRecovery: not null } paidRequest &&
            paidRequest.Id == instructionFrameId ? paidRequest : null;
        var awaitingFrame = factionRequestCost is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame program &&
            (IsAwaitingProgramMovement(program) || program.Id == instructionFrameId) ? program : null;
        var declaration = _resolutionStack.LastOrDefault() is CardDeclarationFrame declared &&
            declared.Id == instructionFrameId && declared.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning ? declared : null;
        var recoveryReplacement = _resolutionStack.LastOrDefault() is RecoveryReplacementFrame recovery && recovery.Id == instructionFrameId ? recovery : null;
        var equipmentRecast = _resolutionStack.LastOrDefault() is EquipmentRecastFrame recast && recast.Id == instructionFrameId ? recast : null;
        var drawPhase = _resolutionStack.LastOrDefault() is DrawPhaseObligationFrame draw && draw.Id == instructionFrameId ? draw : null;
        var roundEnding = _resolutionStack.LastOrDefault() is RoundEndingBoundaryFrame round && round.Id == instructionFrameId ? round : null;
        var actualPhaseEnd = _resolutionStack.LastOrDefault() is ActualPhaseEndFrame end && end.Id == instructionFrameId ? end : null;
        var colorFireAttack = _resolutionStack.LastOrDefault() is CardUseFrame { ColorFireAttack.PaidCardId: not null } fire && fire.Id == instructionFrameId ? fire : null;
        var mouFireAttack = _resolutionStack.LastOrDefault() is CardUseFrame { MouZhitianPayment: not null } mouFire && mouFire.Id == instructionFrameId ? mouFire : null;
        var counterspellPayment = _resolutionStack.LastOrDefault() is NullificationWindowFrame { CounterspellPayment: not null } counter && counter.Id == instructionFrameId ? counter : null;
        var denglouUse = _resolutionStack.LastOrDefault() is CardUseFrame { DenglouNative: not null, DenglouCostDrained: false } denglou && denglou.Id == instructionFrameId ? denglou : null;
        var guoSiUse = _resolutionStack.LastOrDefault() is CardUseFrame { GuoSiNative: not null, GuoSiCostDrained: false } guoSi && guoSi.Id == instructionFrameId ? guoSi : null;
        var historicalEnding = _resolutionStack.LastOrDefault() is CardUseFrame { EndingHistoricalUseReturn: not null, EndingHistoricalCostDrained: false } history && history.Id == instructionFrameId ? history : null;
        var roundPileAlcohol = _resolutionStack.LastOrDefault() is CardUseFrame { RoundPileAlcoholReturn: not null, RoundPileAlcoholCostDrained: false } wine && wine.Id == instructionFrameId ? wine : null;
        var paidCardUse = _resolutionStack.LastOrDefault() is CardUseFrame { RecoveryPaidContinuation: { } paidContinuation } paidUse && paidUse.Id == instructionFrameId &&
            (HasNativePublicPileEquipmentTail(paidUse, paidContinuation) || HasWenhePoisonUsePaymentTail(paidUse, paidContinuation) || paidContinuation.Kind == RecoveryPaidCardUseKind.CommittedSlash || (HasFeiYiTrickPaymentTail(paidUse, paidContinuation) || HasWuJingHejiTrickPaymentTail(paidUse, paidContinuation) || (HasHuiyunTrickPaymentTail(paidUse, paidContinuation) || (HasQiejianUsePaymentTail(paidUse, paidContinuation) || HasJuewuUsePaymentTail(paidUse, paidContinuation))))) ? paidUse : null;
        var incomingRangeDraw = _resolutionStack.LastOrDefault() is CardUseFrame { SoleIncomingRangeDraw.Stage: SoleIncomingRangeDrawStage.Children } incomingDraw && incomingDraw.Id == instructionFrameId ? incomingDraw : null;
        var nativeCardDraw = _resolutionStack.LastOrDefault() is CardUseFrame { NativeCardEffectDraw.Stage: NativeCardEffectDrawStage.DrawingChildren } nativeDraw && nativeDraw.Id == instructionFrameId ? nativeDraw : null;
        var guanduCard = _resolutionStack.LastOrDefault() is CardUseFrame { GuanduCardEffect.Stage: GuanduCardEffectStage.DrawingChildren } guandu && guandu.Id == instructionFrameId ? guandu : null;
        var wumiaoFlood = _resolutionStack.LastOrDefault() is CardUseFrame { WumiaoFlood.Stage: WumiaoFloodStage.DrawingChildren or WumiaoFloodStage.DiscardChildren } flood && flood.Id == instructionFrameId ? flood : null;
        var dragonBoatCard = _resolutionStack.LastOrDefault() is CardUseFrame { DragonBoatCardEffect.Stage: DragonBoatCardEffectStage.DrawingChildren or DragonBoatCardEffectStage.DiscardChildren } dragon && dragon.Id == instructionFrameId ? dragon : null;
        var wenheCard = _resolutionStack.LastOrDefault() is CardUseFrame { WenheCardEffect: { Applied: true, Stage: WenheCardEffectStage.Children } } wenhe && wenhe.Id == instructionFrameId ? wenhe : null;
        bool Eligible(CardMovementBatchContext batch) => incomingRangeDraw is not null ? IsSoleIncomingRangeDrawMovement(incomingRangeDraw, batch) : wumiaoFlood is not null ? IsWumiaoFloodMovement(wumiaoFlood, batch) : guanduCard is not null ? IsGuanduCardEffectMovement(guanduCard, batch) : dragonBoatCard is not null ? IsDragonBoatCardEffectMovement(dragonBoatCard, batch) : wenheCard is not null ? IsWenheCardEffectMovement(wenheCard, batch) : nativeCardDraw is not null ? IsNativeCardEffectDrawMovement(nativeCardDraw, batch) : roundEnding is not null ? IsRoundEndingMovement(roundEnding, batch) : actualPhaseEnd is not null ? actualPhaseEnd.InheritedMovementBatchIds.Contains(batch.Id) && batch.AwaitingProgramFrameId is null : friendResponse is not null ? batch.ParentFrameId == friendResponse.Id && batch.AwaitingProgramFrameId is null : paidCardUse is not null ? IsCardUsePaymentBatch(paidCardUse, batch) : awaitingFrame is not null && (IsSelectedForeignCardSlashMovementBatch(awaitingFrame, batch) || IsZhangYanNativeSettlementBatch(awaitingFrame, batch) || IsWuJingNativeSettlementBatch(awaitingFrame, batch) || (IsChanshuangNativeSettlementBatch(awaitingFrame, batch) || ((IsMouLixianNativeSettlementBatch(awaitingFrame, batch) || (IsDiouNativeSettlementBatch(awaitingFrame, batch) || (IsMouLiuChengNativeSettlementBatch(awaitingFrame, batch) || IsMouXuHuangNativeSettlementBatch(awaitingFrame, batch)))) || IsMouZuifengNativeSettlementBatch(awaitingFrame, batch))) || IsSayingNativeSettlementBatch(awaitingFrame, batch) || IsHuiyunNativeSettlementBatch(awaitingFrame, batch) || (IsQuHuangNativeSettlementBatch(awaitingFrame, batch) || IsBiguoPindianSettlementBatch(awaitingFrame, batch))) || (mouFireAttack is not null ? batch.ParentFrameId == mouFireAttack.Id && batch.AwaitingProgramFrameId is null : drawFunded is not null ? batch.ParentFrameId == drawFunded.Id && batch.AwaitingProgramFrameId is null : denglouUse is not null ? batch.ParentFrameId == denglouUse.Id && batch.AwaitingProgramFrameId is null : guoSiUse is not null ? batch.ParentFrameId == guoSiUse.Id && batch.AwaitingProgramFrameId is null : historicalEnding is not null ? batch.ParentFrameId == historicalEnding.Id && batch.AwaitingProgramFrameId is null : roundPileAlcohol is not null ? batch.ParentFrameId == roundPileAlcohol.Id && batch.AwaitingProgramFrameId is null : counterspellPayment is not null ? batch.ParentFrameId == counterspellPayment.Id && batch.AwaitingProgramFrameId is null : colorFireAttack is not null ? batch.ParentFrameId == colorFireAttack.Id && batch.AwaitingProgramFrameId is null : drawPhase is not null ? batch.ParentFrameId == drawPhase.Id || drawPhase.InheritedMovementBatchIds?.Contains(batch.Id) == true : factionRequestCost is not null ? batch.ParentFrameId == factionRequestCost.Id : equipmentRecast is not null ? batch.ParentFrameId == equipmentRecast.Id : recoveryReplacement is not null ? batch.ParentFrameId == recoveryReplacement.Id : declaration is not null ? batch.ParentFrameId == declaration.Id : awaitingFrame is null
            ? batch.AwaitingProgramFrameId is null
            : batch.AwaitingProgramFrameId == awaitingFrame.Id ||
              batch.AwaitingProgramFrameId is null && batch.ParentFrameId == awaitingFrame.Id);
        if (_pendingDecision is not null ||
            (_resolutionStack.Count != 0 && roundEnding is null && actualPhaseEnd is null && friendResponse is null && awaitingFrame is null && declaration is null && recoveryReplacement is null && equipmentRecast is null && factionRequestCost is null && drawPhase is null && colorFireAttack is null && mouFireAttack is null && counterspellPayment is null && roundPileAlcohol is null && historicalEnding is null && guoSiUse is null && denglouUse is null && drawFunded is null && paidCardUse is null && incomingRangeDraw is null && nativeCardDraw is null && wenheCard is null && guanduCard is null && wumiaoFlood is null && dragonBoatCard is null) ||
            _winner != Winner.None || _status == EngineStatus.Completed)
            return false;

        // Equipment removal may have completed a nested recovery before this movement finished.
        if (awaitingFrame is not null &&
            IsAwaitingProgramMovement(awaitingFrame) &&
            TryBeginHpChangedProgramWindow(awaitingFrame.Id, PostEventContinuation.AwaitedProgramMovement)) return true;

        while (_pendingCardsMovedBatches.Any(Eligible))
        {
            var batch = _pendingCardsMovedBatches.Where(Eligible).OrderBy(item => item.Id).First();
            _pendingCardsMovedBatches.Remove(batch);
            var window = CreateCardsMovedProgramWindow(batch,
                awaitingFrame is not null && !IsAwaitingProgramMovement(awaitingFrame) ? awaitingFrame.Id : null);
            if (window is null) continue;
            if (awaitingFrame is not null && (IsSelectedForeignCardSlashMovementBatch(awaitingFrame, batch) || IsZhangYanNativeSettlementBatch(awaitingFrame, batch) || IsWuJingNativeSettlementBatch(awaitingFrame, batch) || (IsChanshuangNativeSettlementBatch(awaitingFrame, batch) || ((IsMouLixianNativeSettlementBatch(awaitingFrame, batch) || (IsDiouNativeSettlementBatch(awaitingFrame, batch) || (IsMouLiuChengNativeSettlementBatch(awaitingFrame, batch) || IsMouXuHuangNativeSettlementBatch(awaitingFrame, batch)))) || IsMouZuifengNativeSettlementBatch(awaitingFrame, batch))) || IsSayingNativeSettlementBatch(awaitingFrame, batch) || IsHuiyunNativeSettlementBatch(awaitingFrame, batch) || (IsQuHuangNativeSettlementBatch(awaitingFrame, batch) || IsBiguoPindianSettlementBatch(awaitingFrame, batch)) || IsNianRevivalMovementBatch(awaitingFrame, batch)))
                window = window with { ResumeProgramFrameId = awaitingFrame.Id };
            if (recoveryReplacement is not null) window = window with { ResumeRecoveryReplacementFrameId = recoveryReplacement.Id };
            if (paidCardUse is not null) window = window with { ResumePaidCardUseFrameId = paidCardUse.Id };
            if (friendResponse is not null) window = window with { ResumeFriendDuelResponseFrameId = friendResponse.Id, ResumeProgramFrameId = null };
            if (equipmentRecast is not null) window = window with { ResumeEquipmentRecastFrameId = equipmentRecast.Id };
            if (drawPhase is not null) window = window with { ResumeDrawPhaseObligationFrameId = drawPhase.Id };
            if (incomingRangeDraw is not null) window = window with { ResumeSoleIncomingRangeDrawFrameId = incomingRangeDraw.Id, ResumeProgramFrameId = null };
            if (nativeCardDraw is not null) window = window with { ResumeNativeCardEffectDrawFrameId = nativeCardDraw.Id };
            if (wenheCard is not null) window = window with { ResumeWenheCardEffectFrameId = wenheCard.Id, ResumeProgramFrameId = null };
            if (guanduCard is not null) window = window with { ResumeGuanduCardEffectFrameId = guanduCard.Id, ResumeProgramFrameId = null };
            if (wumiaoFlood is not null) window = window with { ResumeWumiaoFloodFrameId = wumiaoFlood.Id, ResumeProgramFrameId = null };
            if (dragonBoatCard is not null) window = window with { ResumeDragonBoatCardEffectFrameId = dragonBoatCard.Id, ResumeProgramFrameId = null };
            if (roundEnding is not null)
            {
                window = window with { ResumeRoundEndingBoundaryId = roundEnding.Id };
                ReplaceRuntimeTop(roundEnding with { ActiveWindowFrameId = window.Id });
            }
            if (actualPhaseEnd is not null) window = window with { ResumeActualPhaseEndFrameId = actualPhaseEnd.Id };
            if (colorFireAttack is not null) window = window with { ResumeColorFireAttackFrameId = colorFireAttack.Id };
            if (mouFireAttack is not null) window = window with { ResumeMouZhitianFireFrameId = mouFireAttack.Id };
            if (counterspellPayment is not null) window = window with { ResumeCounterspellPaymentFrameId = counterspellPayment.Id };
            if (guoSiUse is not null) window = window with { ResumeGuoSiUseFrameId = guoSiUse.Id };
            if (denglouUse is not null) window = window with { ResumeDenglouUseFrameId = denglouUse.Id };
            if (historicalEnding is not null) window = window with { ResumeHistoricalEndingUseFrameId = historicalEnding.Id };
            if (roundPileAlcohol is not null) window = window with { ResumeRoundPileAlcoholUseFrameId = roundPileAlcohol.Id };
            if (factionRequestCost is not null) window = window with { ResumeFactionRequestCostFrameId = factionRequestCost.Id, ResumeProgramFrameId = null };
            if (drawFunded is not null)
            {
                window = window with { ResumeDrawFundedDistinctBasicFrameId = drawFunded.Id, ResumeProgramFrameId = null };
                BeginDrawFundedDistinctBasicChild(drawFunded.Id, window.Id);
            }
            if (declaration is not null)
            {
                window = window with { ResumeDeclarationFrameId = declaration.Id };
                ReplaceRuntimeTop(declaration with { ActiveChildFrameId = window.Id });
            }
            if (awaitingFrame?.SelectedCardPayment is { } payment &&
                awaitingFrame.SelectedCardPaymentResult is null)
            {
                if (payment.ActiveChildFrameId is not null)
                    throw new InvalidOperationException("A selected-card payment already has an active child frame.");
                ReplaceRuntimeTop(awaitingFrame with
                {
                    SelectedCardPayment = payment with { ActiveChildFrameId = window.Id }
                });
            }
            PushRuntimeFrame(window);
            AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>();
            return true;
        }
        return false;
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectCardsMovedProgramCandidates(
        CardMovementBatchContext batch)
    {
        var candidates = new List<ProgramTriggerCandidate>();
        foreach (var window in new[] { SkillProgramTriggerWindow.CardsMoved, SkillProgramTriggerWindow.CardsGained })
        foreach (var (count, discardOriginOnly) in ProgramMovementSourceCounts(batch, window))
        {
            var ownerSeat = count.Location.OwnerSeat!.Value;
            if (!IsValidPlayerSeat(ownerSeat)) continue;
            foreach (var candidate in CollectProgramTriggerCandidates(_players[ownerSeat], window))
            {
                var trigger = GetProgramTrigger(candidate);
                if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveYuandiDiscardNames)) continue;
                if (IsWuZishengMovementTrigger(trigger) || IsGuYinDiscardTrigger(trigger) || IsBeifenLossTrigger(trigger) || IsHellZhiwangTrigger(trigger) || IsNianJingjueTrigger(trigger) || IsTangTangZhiyiTrigger(trigger) || trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair) || IsXiangxuMinimumTrigger(trigger) || IsQuHuangMovementTrigger(trigger) || trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit) || IsBiriTrigger(trigger) || IsLingkongGainTrigger(trigger) || IsQimeiEqualityTrigger(trigger) || IsNeighborDiscardTopTrigger(trigger) || IsThirdPartyHandGainTrigger(trigger) ||
                    IsOwnerSourceHandGainTrigger(trigger) || IsOtherLastHandLossTrigger(trigger) || IsBianFuRenWanweiTrigger(trigger) || IsSunRuOlChishiTrigger(trigger) || IsDuFuRenYiseTrigger(trigger) || IsFeiYiSingletonTrigger(trigger) || IsQingyuanGainTrigger(trigger) || trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimLiubingUndamagingSlash)) continue;
                if (!IsGainPhaseQualified(trigger, ownerSeat, batch.MovementTiming)) continue;
                if (discardOriginOnly && !trigger.MovementDiscardOnly) continue;
                if (!(window == SkillProgramTriggerWindow.CardsMoved ? trigger.SourceZones : trigger.DestinationZones).Contains(count.Location.Zone)) continue;
                if (trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerBatch)
                {
                    if (window != SkillProgramTriggerWindow.CardsMoved || candidates.Any(item =>
                        item.OwnerSeat == candidate.OwnerSeat && item.SkillId == candidate.SkillId &&
                        item.BindingId == candidate.BindingId && item.SkillInstanceId == candidate.SkillInstanceId)) continue;
                    var matching = MatchingOwnerBatchMovementIndexes(batch, candidate, trigger);
                    var ownerBatchFacts = CaptureCardsMovedTriggerFacts(_players[ownerSeat], matching.Length,
                        new CardMovementSourceCount(count.Location,
                            ProgramMovementSourceCounts(batch, window).Where(item => (!item.DiscardOriginOnly || trigger.MovementDiscardOnly) && item.Count.Location.OwnerSeat == ownerSeat && trigger.SourceZones.Contains(item.Count.Location.Zone)).Sum(item => item.Count.CountBefore),
                            ProgramMovementSourceCounts(batch, window).Where(item => (!item.DiscardOriginOnly || trigger.MovementDiscardOnly) && item.Count.Location.OwnerSeat == ownerSeat && trigger.SourceZones.Contains(item.Count.Location.Zone)).Sum(item => item.Count.CountAfter)), window);
                    ownerBatchFacts = CaptureOwnerBatchEquipmentLoss(ownerBatchFacts, batch, candidate, trigger, matching);
                    if (matching.Length > 0 && trigger.Condition.Evaluate(ownerBatchFacts, candidate.SkillId, candidate.SkillInstanceId))
                        candidates.Add(candidate);
                    continue;
                }
                if (trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerSourceOwner)
                {
                    candidates.AddRange(CollectSourceOwnerGainCandidates(batch, candidate, trigger, count));
                    continue;
                }
                var indexes = MatchingMovementIndexes(batch, candidate, trigger, count.Location);
                if (indexes.Length == 0) continue;
                var facts = CaptureCardsMovedTriggerFacts(_players[ownerSeat], FriendManjuanMovedCount(batch, ownerSeat, trigger, indexes.Length), count, window, batch.MovementTiming,
                    UsesNativeDrawOrdinal(trigger) ? batch : null);
                if (!trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId)) continue;
                // A judgment still in flight (for example while another skill replaces its
                // card) owns its judgment zone churn; starting a second judgment from that
                // movement would collide with the pending one and self-feedback the same
                // judgment, so such triggers wait for a settled zone instead.
                if (ActiveJudgment is not null &&
                    ProgramInstructionResolver.Default.Features(trigger).HasOperation(SkillProgramEffectOp.StartJudgment)) continue;
                var occurrences = trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch
                    ? new[] { count.Location.Zone == CardZoneKind.PrivatePersistentPile || trigger.Effects is [{ Op: SkillProgramEffectOp.RecoverAfterLastPublicRice }] ? indexes[0] : 0 } : indexes;
                candidates.AddRange(occurrences.Select(index => candidate with { OccurrenceIndex = index }));
            }
        }
        candidates.AddRange(CollectNianJingjueCandidates(batch));
        candidates.AddRange(CollectHellZhiwangCandidates(batch));
        candidates.AddRange(CollectNeighborDiscardCandidates(batch));
        candidates.AddRange(CollectThirdPartyHandGainCandidates(batch));
        candidates.AddRange(CollectOwnerSourceHandGainCandidates(batch));
        candidates.AddRange(CollectOtherLastHandLossCandidates(batch));
        candidates.AddRange(CollectQuHuangMovementCandidates(batch));
        candidates.AddRange(CollectKanggeHandGainCandidates(batch));
        candidates.AddRange(CollectBiriMovementCandidates(batch));
        candidates.AddRange(CollectLingkongMovementCandidates(batch));
        candidates.AddRange(CollectBeifenLossCandidates(batch));
        candidates.AddRange(CollectGuYinDiscardCandidates(batch));
        candidates.AddRange(CollectQimeiMovementCandidates(batch));
        candidates.AddRange(CollectBuZhiMovementCandidates(batch));
        candidates.AddRange(CollectBianFuRenLossCandidates(batch));
        candidates.AddRange(CollectDuFuRenYiseCandidates(batch));
        candidates.AddRange(CollectFeiYiSingletonCandidates(batch));
        candidates.AddRange(CollectSilvRefundCandidates(batch));
        candidates.AddRange(CollectQingyuanGainCandidates(batch));
        candidates.AddRange(CollectSunRuOlChishiCandidates(batch));
        candidates.AddRange(CollectRuanHuiClaimCandidates(batch));
        candidates.AddRange(CollectWuJingLiubingCandidates(batch));
        if (batch.LiJueUseDiscard is not null)
            candidates.AddRange(CollectLiJueUseDiscardCandidates(batch));
        candidates.AddRange(CollectTangTangZhiyiCandidates(batch));
        candidates.AddRange(CollectYuandiCandidates(batch));
        CollectJieyanCandidates(batch, candidates);
        CollectXiangxuMinimumCandidates(batch, candidates);
        candidates.AddRange(CollectWuZishengMovementCandidates(batch));
        return candidates
            .OrderBy(candidate => (candidate.OwnerSeat - _currentSeat + _players.Count) % _players.Count)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex)
            .ToArray();
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectDiscardPileReceivedCandidates(
        CardMovementBatchContext batch)
    {
        var additions = new List<ProgramTriggerCandidate>();
        for (var seat = 0; seat < _players.Count; seat++)
        {
            if (!_players[seat].IsAlive) continue;
            foreach (var candidate in CollectProgramTriggerCandidates(
                         _players[seat], SkillProgramTriggerWindow.DiscardPileReceived))
            {
                var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                    .Single(item => item.Id == candidate.BindingId);
                if (trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.ClaimLiubingUndamagingSlash)) continue;
                var indexes = MatchingDiscardPileIndexes(batch, candidate, trigger);
                if (indexes.Length == 0) continue;
                var occurrences = trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch
                    ? new[] { IsOtherActualBasicDiscardTrigger(trigger) || IsOrdinaryGuYongBingyiTrigger(trigger) ? indexes[0] : 0 }
                    : indexes;
                additions.AddRange(occurrences.Select(index => candidate with { OccurrenceIndex = index }));
            }
        }
        return additions;
    }

    private int[] MatchingDiscardPileIndexes(CardMovementBatchContext batch,
        ProgramTriggerCandidate candidate, SkillProgramTrigger trigger)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.CollectMouXiahouDunQingjian)) return MatchingMouXiahouDunDiscardIndexes(batch);
        var features = ProgramInstructionResolver.Default.Features(trigger);
        if (features.HasOperation(SkillProgramEffectOp.ReturnDiscardedSilvEntity) ||
            features.HasOperation(SkillProgramEffectOp.ObtainJingzhongUsedEntities)) return [];
        if (features.HasOperation(SkillProgramEffectOp.YuejianRevealAndClaimUsedCard))
            return MatchingBianFuRenUsedDiscardIndexes(batch, candidate);
        if (features.HasOperation(SkillProgramEffectOp.MouWujingDraw)) return MatchingMouWujingIndexes(batch, candidate);
        if (features.HasOperation(SkillProgramEffectOp.GiveOffTurnUsedCards))
            return MatchingOffTurnUsedCardIndexes(batch, candidate);
        if (features.HasOperation(SkillProgramEffectOp.DiscardAfterActualOutsideDiscard))
            return MatchingOutsideDiscardIndexes(batch, candidate);
        if (IsOtherActualBasicDiscardTrigger(trigger)) return MatchingOtherActualBasicDiscardIndexes(batch, candidate);
        if (features.HasOperation(SkillProgramEffectOp.XunxianGiftUsedCard))
            return MatchingOwnSettledActionCardIndexes(batch, candidate);
        if (features.HasOperation(SkillProgramEffectOp.TongxieFollowUp))
            return MatchingTongxieSettledSlashIndexes(batch);
        if (features.HasOperation(SkillProgramEffectOp.RestoreActualDiscardBatch))
            return MatchingActualDiscardRecoveryIndexes(batch, candidate);
        if (features.HasOperation(SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance))
            return MatchingProvenanceDiscardIndexes(batch, candidate, trigger);
        if (features.HasOperation(SkillProgramEffectOp.StoreAdjacentDiscardedSlash))
            return MatchingAdjacentDiscardIndexes(batch, candidate);
        if(features.HasOperation(SkillProgramEffectOp.RewardDiscardedActionColor))
        {
            var action=CompleteProgramEventHistory().OfType<ActionCardsDiscardedEvent>().LastOrDefault(e=>e.BatchId==batch.Id);
            return action?.ActorSeat==candidate.OwnerSeat ? batch.Movements.Select((m,i)=>(m,i)).Where(x=>x.m.To==CardLocation.DiscardPile).Select(x=>x.i).ToArray():[];
        }
        if (features.HasOperation(SkillProgramEffectOp.ReclaimFriendXuanjian))
            return batch.Movements.Select((m, i) => (m, i)).Where(x => x.m.To == CardLocation.DiscardPile &&
                _cardZones.GetLocation(x.m.CardId) == CardLocation.DiscardPile && GetAttackCard(x.m.CardId).Kind == CardKind.Xuanjian)
                .Select(x => x.i).ToArray();
        if (trigger.IgnoreOwnSkillMovements && batch.OriginOwnerSeat == candidate.OwnerSeat &&
            batch.OriginSkillId == candidate.SkillId &&
            batch.OriginSkillInstanceId == candidate.SkillInstanceId) return [];
        var discardPile = _cardZones.CardsAt(CardLocation.DiscardPile).ToDictionary(card => card.Id);
        return batch.Movements.Select((movement, index) => (movement, index))
            .Where(item => item.movement.To == CardLocation.DiscardPile &&
                (trigger.MovementDiscardOnly ? GetProgramDiscardSource(item.movement) : item.movement.From) is { } discardSource &&
                discardSource.OwnerSeat is { } source &&
                (trigger.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Own ? source == candidate.OwnerSeat : source != candidate.OwnerSeat) &&
                (trigger.SourceZones.Count == 0 || trigger.SourceZones.Contains(discardSource.Zone)) &&
                item.movement.From != item.movement.To &&
                (trigger.MovementReasons.Count == 0 ||
                    trigger.MovementReasons.Contains(item.movement.Reason.Value)) &&
                !trigger.ExcludedMovementReasons.Contains(item.movement.Reason.Value) &&
                (trigger.Suits.Count == 0 ||
                    discardPile.TryGetValue(item.movement.CardId, out var card) && trigger.Suits.Contains(card.Suit)) &&
                (trigger.CardCategories.Count == 0 || discardPile.TryGetValue(item.movement.CardId, out var filteredCard) &&
                    trigger.CardCategories.Any(category => MatchesSkillProgramCardCategory(filteredCard.Kind, category))) &&
                (trigger.CardKinds.Count == 0 || discardPile.TryGetValue(item.movement.CardId, out var kindCard) &&
                    trigger.CardKinds.Contains(kindCard.Kind)))
            .Select(item => item.index).ToArray();
    }

    private void ClaimProgramMovedCards(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not
            {
                Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementBatch: { } batch,
                MovementIndex: { } index
            } || index < 0 || index >= batch.Movements.Count)
            throw new InvalidOperationException("The moved-card claim lost its discard window.");
        var movement = batch.Movements[index];
        if (movement.To != CardLocation.DiscardPile ||
            movement.From.OwnerSeat is not { } source || source == active.OwnerSeat)
            throw new InvalidOperationException(
                "A moved-card claim must answer another player's discard into the discard pile.");
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _cardZones.GetLocation(movement.CardId) != CardLocation.DiscardPile)
            return;
        var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(item => item.Id == movement.CardId);
        MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(active.OwnerSeat),
            new CardMoveReason($"skill-program.{active.SkillId}.{SkillProgramEffectOp.ClaimMovedCards}"));
        AdvanceEventRulesAndQueueFact(new ProgramMovedCardsClaimedEvent(
            active.Id, active.SkillId, active.TriggerId!, active.OwnerSeat, source, card.Id));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，获得 {card.DisplayName}。",
            active.OwnerSeat, source);
    }

    private int[] MatchingMovementIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate,
        SkillProgramTrigger trigger, CardLocation location)
    {
        var features = ProgramInstructionResolver.Default.Features(trigger);
        var ordinalIndexes = UsesNativeDrawOrdinal(trigger) ? MatchingNativeDrawOrdinalIndexes(batch, candidate) : null;
        if (ordinalIndexes is { Length: not 1 }) return [];
        if (features.HasOperation(SkillProgramEffectOp.GainFaluTokensAfterActualDiscard))
            return location.OwnerSeat != candidate.OwnerSeat ? [] : batch.Movements
                .Select((movement, index) => (movement, index))
                .Where(item => _cardMovements.Contains(item.movement) &&
                    FaluDiscardOwnedSource(item.movement) == location && item.movement.From != item.movement.To &&
                    (trigger.MovementReasons.Count == 0 || trigger.MovementReasons.Contains(item.movement.Reason.Value)) &&
                    !trigger.ExcludedMovementReasons.Contains(item.movement.Reason.Value))
                .Select(item => item.index).ToArray();
        if (features.HasOperation(SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick))
            return location == CardLocation.Processing ? MatchingLiJueUseDiscardIndexes(batch, candidate) : [];
        if (features.HasOperation(SkillProgramEffectOp.RecoverAfterLastPublicRice))
        {
            var pile = _publicPersistentPiles.GetValueOrDefault((candidate.OwnerSeat, candidate.SkillId, candidate.SkillInstanceId));
            if (pile is null || pile.Location != location || !RiceProducer(pile) ||
                !batch.SourceCounts.Any(n => n.Location == location && n.CountBefore > 0 && n.CountAfter == 0)) return [];
        }
        if (features.HasOperation(SkillProgramEffectOp.DrawAfterActualPlayDrawInvocation))
            return MatchingZhanjiDrawIndexes(batch, candidate);
        if (features.HasOperation(SkillProgramEffectOp.DrawAfterActualOutsideDraw))
            return MatchingOutsideDrawIndexes(batch, candidate);
        if (features.HasOperation(SkillProgramEffectOp.GiveAfterBatchGain))
            return MatchingActualGainGiftIndexes(batch, candidate.OwnerSeat, location);
        if (features.HasOperation(SkillProgramEffectOp.RevealRedLossAndDraw))
            return MatchingRedOwnerLossIndexes(batch, candidate.OwnerSeat, location);
        if (features.HasOperation(SkillProgramEffectOp.DrawAfterActualOwnHandGain))
            return MatchingActualHandGainIndexes(batch, candidate.OwnerSeat, location);
        if (trigger.IgnoreOwnSkillMovements && batch.OriginOwnerSeat == candidate.OwnerSeat &&
            batch.OriginSkillId == candidate.SkillId &&
            batch.OriginSkillInstanceId == candidate.SkillInstanceId) return [];
        return batch.Movements.Select((movement, index) => (movement, index))
            .Where(item => (trigger.Window == SkillProgramTriggerWindow.CardsGained ? item.movement.To : trigger.MovementDiscardOnly ? GetProgramDiscardSource(item.movement) : item.movement.From) == location &&
                item.movement.From != item.movement.To &&
                (trigger.MovementReasons.Count == 0 || trigger.MovementReasons.Contains(item.movement.Reason.Value)) &&
                !trigger.ExcludedMovementReasons.Contains(item.movement.Reason.Value))
            .Where(item => trigger.MovementOccurrence != SkillProgramMovementOccurrence.PerSourceOwner ||
                item.movement.From.OwnerSeat == batch.Movements[candidate.OccurrenceIndex].From.OwnerSeat)
            .Where(item => ordinalIndexes is null || ordinalIndexes.Contains(item.index))
            .Select(item => item.index).ToArray();
    }

    private int[] MatchingOwnerBatchMovementIndexes(CardMovementBatchContext batch,
        ProgramTriggerCandidate candidate, SkillProgramTrigger trigger) =>
        ProgramMovementSourceCounts(batch, SkillProgramTriggerWindow.CardsMoved)
            .Where(item => (!item.DiscardOriginOnly || trigger.MovementDiscardOnly) && item.Count.Location.OwnerSeat == candidate.OwnerSeat &&
                trigger.SourceZones.Contains(item.Count.Location.Zone))
            .SelectMany(item => MatchingMovementIndexes(batch, candidate, trigger, item.Count.Location))
            .Distinct().Order().ToArray();

    private SkillProgramTriggerFacts CaptureCardsMovedTriggerFacts(
        CharacterState owner,
        int movedCardCount,
        CardMovementSourceCount sourceCount,
        SkillProgramTriggerWindow window, CardMovementTiming? timing = null, CardMovementBatchContext? nativeDrawBatch = null)
    {
        var facts = CaptureProgramTriggerFacts(owner);
        return facts with
        {
            MovementTiming = timing,
            NativeDrawActualTurnOrdinal = nativeDrawBatch?.NativeDrawInvocation is { } draw && draw.OwnerSeat == owner.Seat
                ? draw.ActualTurnDrawOrdinal : null,
            MovedCardCount = movedCardCount,
            SourceZoneCountBefore = window == SkillProgramTriggerWindow.CardsMoved ? sourceCount.CountBefore : 0,
            SourceZoneCountAfter = window == SkillProgramTriggerWindow.CardsMoved ? sourceCount.CountAfter : 0,
            DestinationZoneCountBefore = window == SkillProgramTriggerWindow.CardsGained ? sourceCount.CountBefore : 0,
            DestinationZoneCountAfter = window == SkillProgramTriggerWindow.CardsGained ? sourceCount.CountAfter : 0
        };
    }

    private ProgramSkillWindowContext CreateCardsMovedProgramContext(
        CardsMovedTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate)
    {
        if (IsWuZishengMovementTrigger(GetProgramTrigger(candidate))) return CreateWuZishengMovementContext(frame, candidate);
        if (IsBiriTrigger(GetProgramTrigger(candidate))) return CreateBiriMovementContext(frame, candidate);
        if (IsLingkongGainTrigger(GetProgramTrigger(candidate))) return CreateLingkongMovementContext(frame, candidate);
        if (IsBeifenLossTrigger(GetProgramTrigger(candidate))) return CreateBeifenMovementContext(frame, candidate);
        if (IsGuYinDiscardTrigger(GetProgramTrigger(candidate))) return CreateGuYinMovementContext(frame, candidate);
        if (IsNianJingjueTrigger(GetProgramTrigger(candidate))) return CreateNianJingjueContext(frame, candidate);
        if (IsHellZhiwangTrigger(GetProgramTrigger(candidate))) return CreateHellZhiwangContext(frame, candidate);
        if (IsXiangxuMinimumTrigger(GetProgramTrigger(candidate))) return CreateXiangxuMinimumContext(frame, candidate);
        if (IsQuHuangMovementTrigger(GetProgramTrigger(candidate))) return CreateQuHuangMovementContext(frame, candidate);
        if (IsQingyuanGainTrigger(GetProgramTrigger(candidate)))
            return CreateQingyuanGainContext(frame, candidate);
        if (IsFeiYiSingletonTrigger(GetProgramTrigger(candidate)))
            return CreateFeiYiSingletonContext(frame, candidate);
        if (IsDuFuRenYiseTrigger(GetProgramTrigger(candidate)))
            return CreateDuFuRenYiseContext(frame, candidate);
        if (frame.Contexts is { } contexts) return contexts[frame.CandidateIndex];
        if (GetProgramTrigger(candidate).Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveYuandiDiscardNames))
            return CreateYuandiMovementContext(frame, candidate);
        if (IsTangTangZhiyiTrigger(GetProgramTrigger(candidate))) return CreateTangTangZhiyiContext(frame, candidate);
        if (GetProgramTrigger(candidate).Effects.Any(e => e.Op == SkillProgramEffectOp.ResolveJieyanOppositeEndDrawPair)) return CreateJieyanContext(frame, candidate);
        if (IsWuJingLiubingCandidate(candidate)) return CreateWuJingLiubingContext(frame, candidate);
        var trigger = GetProgramTrigger(candidate);
        var features = ProgramInstructionResolver.Default.Features(trigger);
        if (features.HasOperation(SkillProgramEffectOp.WanweiObtainMatchingLostCard) ||
            features.HasOperation(SkillProgramEffectOp.YuejianRevealAndClaimUsedCard))
            return CreateBianFuRenMovementContext(frame, candidate);
        if (features.HasOperation(SkillProgramEffectOp.ResolveSanwenActualGainDuplicates))
            return new(trigger.Window, frame.Id, candidate.OwnerSeat, SourceSeat: frame.Batch.OriginOwnerSeat,
                TargetSeat: candidate.OwnerSeat, Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]), MovementBatch: frame.Batch)
                { Sanwen = SanwenContextFor(frame.Batch, candidate.OwnerSeat) };
        if (features.HasOperation(SkillProgramEffectOp.PayMaxHpAndObtainActualUsedTrick))
            return CreateLiJueMovementContext(frame, candidate);
        if (IsQimeiEqualityTrigger(trigger)) return CreateQimeiMovementContext(frame, candidate);
        if (IsSunRuOlChishiTrigger(trigger)) return CreateSunRuOlChishiContext(frame, candidate);
        if (features.HasOperation(SkillProgramEffectOp.OfferNativeBatchHandGainOrCardLossBenefit))
            return new(trigger.Window, frame.Id, candidate.OwnerSeat, SourceSeat: candidate.OwnerSeat, TargetSeat: candidate.OwnerSeat,
                Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]), MovementBatch: frame.Batch);
        if (features.HasOperation(SkillProgramEffectOp.DiscardAfterActualOutsideDiscard))
            return CreateOutsideDiscardContext(frame, candidate);
        if (features.HasOperation(SkillProgramEffectOp.RestoreActualDiscardBatch)) return CreateActualDiscardRecoveryContext(frame, candidate);
        if (trigger.Window == SkillProgramTriggerWindow.FirstGameDomainCrossing) return CreateFirstDomainContext(frame,candidate);
        if (IsNeighborDiscardTopTrigger(trigger)) return CreateNeighborDiscardContext(frame, candidate);
        if (trigger.Window == SkillProgramTriggerWindow.DiscardPileReceived)
        {
            var movement = frame.Batch.Movements[candidate.OccurrenceIndex];
            return new ProgramSkillWindowContext(
                trigger.Window,
                frame.Id,
                candidate.OwnerSeat,
                SourceSeat: features.HasOperation(SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance) ? DiscardedProvenanceOrigin(movement)?.SourceSeat : (trigger.MovementDiscardOnly ? GetProgramDiscardSource(movement) : movement.From)?.OwnerSeat,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], 1,
                    new CardMovementSourceCount(CardLocation.DiscardPile, 0, 0), trigger.Window),
                MovementBatch: frame.Batch,
                MovementIndex: candidate.OccurrenceIndex);
        }
        if (trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerBatch)
        {
            var counts = ProgramMovementSourceCounts(frame.Batch, trigger.Window)
                .Where(item => (!item.DiscardOriginOnly || trigger.MovementDiscardOnly) && item.Count.Location.OwnerSeat == candidate.OwnerSeat &&
                    trigger.SourceZones.Contains(item.Count.Location.Zone)).Select(item => item.Count).ToArray();
            var matching = MatchingOwnerBatchMovementIndexes(frame.Batch, candidate, trigger);
            return new ProgramSkillWindowContext(trigger.Window, frame.Id, candidate.OwnerSeat,
                SourceSeat: candidate.OwnerSeat, TargetSeat: candidate.OwnerSeat,
                Facts: CaptureOwnerBatchEquipmentLoss(CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], matching.Length,
                    new CardMovementSourceCount(counts[0].Location, counts.Sum(item => item.CountBefore), counts.Sum(item => item.CountAfter)), trigger.Window),
                    frame.Batch, candidate, trigger, matching),
                MovementBatch: frame.Batch);
        }
        if (trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOtherLastHandLoss)
        {
            var movement = frame.Batch.Movements[candidate.OccurrenceIndex];
            var source = movement.From.OwnerSeat
                ?? throw new InvalidOperationException("A last-hand-loss candidate lost its hand source.");
            var sourceCount = ProgramMovementSourceCounts(frame.Batch, trigger.Window)
                .FirstOrDefault(item => item.Count.Location == movement.From).Count;
            return new ProgramSkillWindowContext(trigger.Window, frame.Id, candidate.OwnerSeat,
                SourceSeat: source,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], 1,
                    sourceCount is { } matched ? matched
                        : new CardMovementSourceCount(movement.From, 0, 1), trigger.Window,
                    frame.Batch.MovementTiming),
                MovementBatch: frame.Batch,
                MovementIndex: candidate.OccurrenceIndex);
        }
        if (trigger.MovementOccurrence is SkillProgramMovementOccurrence.PerThirdPartyHandGain
            or SkillProgramMovementOccurrence.PerOwnerSourceHandGain)
        {
            var movement = frame.Batch.Movements[candidate.OccurrenceIndex];
            var destinationCount = ProgramMovementSourceCounts(frame.Batch, trigger.Window)
                .FirstOrDefault(item => item.Count.Location == movement.To).Count is { } matched ? matched
                : new CardMovementSourceCount(movement.To, 0, 1);
            return new ProgramSkillWindowContext(
                trigger.Window,
                frame.Id,
                candidate.OwnerSeat,
                SourceSeat: movement.From.OwnerSeat,
                TargetSeat: movement.To.OwnerSeat,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], 1,
                    destinationCount, trigger.Window),
                MovementBatch: frame.Batch,
                MovementIndex: candidate.OccurrenceIndex);
        }
        var gained = trigger.Window == SkillProgramTriggerWindow.CardsGained;
        var kind = (gained ? trigger.DestinationZones : trigger.SourceZones).Single();
        var ricePile = trigger.Effects is [{ Op: SkillProgramEffectOp.RecoverAfterLastPublicRice }];
        var privatePile = kind == CardZoneKind.PrivatePersistentPile || ricePile;
        if (privatePile && (candidate.OccurrenceIndex < 0 || candidate.OccurrenceIndex >= frame.Batch.Movements.Count))
            throw new InvalidOperationException("A named private-pile movement lost its exact physical occurrence.");
        var location = privatePile ? gained ? frame.Batch.Movements[candidate.OccurrenceIndex].To : frame.Batch.Movements[candidate.OccurrenceIndex].From
            : new CardLocation(kind, candidate.OwnerSeat);
        if (privatePile && (location.Zone != kind || location.OwnerSeat != candidate.OwnerSeat || !ricePile && string.IsNullOrWhiteSpace(location.PublicPileId)))
            throw new InvalidOperationException("A named private-pile movement changed its exact pile identity.");
        var count = ProgramMovementSourceCounts(frame.Batch, trigger.Window)
            .Single(item => item.Count.Location == location && (!item.DiscardOriginOnly || trigger.MovementDiscardOnly)).Count;
        var matchingIndexes = MatchingMovementIndexes(frame.Batch, candidate, trigger, location);
        if (privatePile && !matchingIndexes.Contains(candidate.OccurrenceIndex)) throw new InvalidOperationException("Private pile occurrence no longer matches its trigger.");
        return new ProgramSkillWindowContext(
            trigger.Window,
            frame.Id,
            candidate.OwnerSeat,
            SourceSeat: trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerSourceOwner
                ? frame.Batch.Movements[candidate.OccurrenceIndex].From.OwnerSeat
                : gained ? frame.Batch.OriginOwnerSeat : candidate.OwnerSeat,
            TargetSeat: candidate.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureCardsMovedTriggerFacts(
                _players[candidate.OwnerSeat], FriendManjuanMovedCount(frame.Batch, candidate.OwnerSeat, trigger, matchingIndexes.Length), count, trigger.Window, frame.Batch.MovementTiming,
                UsesNativeDrawOrdinal(trigger) ? frame.Batch : null),
            MovementBatch: frame.Batch,
            MovementIndex: trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerCard
                ? candidate.OccurrenceIndex
                : null);
    }

    private void ContinueCardsMovedProgramWindowCore()
    {
        if (_resolutionStack.LastOrDefault() is CardsMovedTriggerWindowFrame frame && TryAdvanceWenhePoisonLoss(frame)) return;
        ProgramTriggerWindowDriver.Run(new CardsMovedTriggerWindowHost(this));
    }

    private void CompleteCardsMovedProgramWindow(CardsMovedTriggerWindowFrame frame)
    {
        PopResolutionFrame(frame.Id, ResolutionFrameKind.CardsMovedTriggerWindow);
        if (ReturnWenhePoisonFireAttackMovement(frame)) return;
        if (ReturnQiaoZhouPreparationMovement(frame)) return;
        if (ReturnCardResponseCompletionMovement(frame)) return;
        if (ReturnCardSupplyCompletionMovement(frame)) return;
        if (frame.ResumeGuanduCardEffectFrameId is { } guanduId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame guandu || guandu.Id != guanduId || !IsGuanduCardEffectMovementReturn(guandu, frame))
                throw new InvalidOperationException("A Guandu card lost its exact movement return.");
            ContinueGuanduCardEffect(guanduId); return;
        }
        if (frame.ResumeWumiaoFloodFrameId is { } floodId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame flood || flood.Id != floodId || !IsWumiaoFloodMovementReturn(flood, frame))
                throw new InvalidOperationException("Wumiao Flood movement lost its exact card return.");
            ContinueWumiaoFlood(floodId); return;
        }
        if (frame.ResumeDragonBoatCardEffectFrameId is { } dragonId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame dragon || dragon.Id != dragonId || !IsDragonBoatCardEffectMovementReturn(dragon, frame))
                throw new InvalidOperationException("Dragon boat movement lost its exact card return.");
            ContinueDragonBoatCardEffect(dragonId); return;
        }
        if (frame.ResumeWenheCardEffectFrameId is { } wenheId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame wenhe || wenhe.Id != wenheId || !IsWenheCardEffectMovementReturn(wenhe, frame))
                throw new InvalidOperationException("A Wenhe card lost its exact atomic movement return.");
            ContinueWenheCardEffect(wenheId); return;
        }
        if (frame.ResumeSoleIncomingRangeDrawFrameId is { } incomingDrawId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame incomingDraw || incomingDraw.Id != incomingDrawId || !IsSoleIncomingRangeDrawMovementReturn(incomingDraw, frame))
                throw new InvalidOperationException("An incoming range draw lost its exact paid movement return.");
            ContinueSoleIncomingRangeDraw(incomingDrawId); return;
        }
        if (frame.ResumeNativeCardEffectDrawFrameId is { } nativeDrawId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame nativeDraw || nativeDraw.Id != nativeDrawId || !IsNativeCardEffectDrawMovementReturn(nativeDraw, frame))
                throw new InvalidOperationException("A native card draw lost its exact movement return.");
            ContinueNativeDrawTwo(nativeDrawId); return;
        }
        if (frame.ResumeFriendDuelResponseFrameId is { } friendId)
        {
            if (_resolutionStack.LastOrDefault() is not FriendDuelResponseFrame friendParent || !IsFriendDuelResponseMovementReturn(friendParent, frame))
                throw new InvalidOperationException("Friend Duel response lost its actual movement return.");
            ContinueFriendDuelResponse(friendId); return;
        }
        if (frame.ResumePaidCardUseFrameId is { } paidUseId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame paidParent ||
                paidParent.Id != paidUseId || !IsPaidCardUseMovementReturn(paidParent, frame))
                throw new InvalidOperationException("A card-use payment lost its original typed movement return.");
            ContinueRecoveryPaidCardUse(paidUseId);
            return;
        }
        if (frame.DeferredTurnEndReturn is not null)
        {
            ReturnDeferredTurnEndPrelude(frame);
            return;
        }
        if (frame.SoleTargetQuarantineEndReturn is not null)
        { ReturnSoleTargetQuarantineMovement(frame); return; }
        if (_resolutionStack.LastOrDefault() is ProgramSkillFrame
            { SelectedCardPayment: { } payment, SelectedCardPaymentResult: null } parent)
        {
            if (payment.ActiveChildFrameId != frame.Id)
                throw new InvalidOperationException("A selected-card payment lost its movement child frame.");
            ReplaceRuntimeTop(parent with
            {
                SelectedCardPayment = payment with
                {
                    ActiveChildFrameId = null,
                    LastCompletedChildFrameId = frame.Id
                }
            });
        }
        if (frame.ResumeDeclarationFrameId is { } declarationId)
        {
            if (_resolutionStack.LastOrDefault() is not CardDeclarationFrame declarationParent || declarationParent.Id != declarationId || declarationParent.ActiveChildFrameId != frame.Id)
                throw new InvalidOperationException("A declaration payment lost its exact movement child.");
            ReplaceRuntimeTop(declarationParent with { ActiveChildFrameId = null });
            AdvanceRuntimeFrame(declarationId); return;
        }
        if (frame.ResumeDrawFundedDistinctBasicFrameId is not null)
        { ResumeDrawFundedDistinctBasicMovement(frame); return; }
        if (frame.ResumeRecoveryReplacementFrameId is { } recoveryId)
        { AdvanceRuntimeFrame(recoveryId); return; }
        if (frame.ResumeRoundEndingBoundaryId is not null)
        { ReturnRoundEndingMovement(frame); return; }
        if (frame.ResumeActualPhaseEndFrameId is { } phaseEndId)
        {
            if (_resolutionStack.LastOrDefault() is not ActualPhaseEndFrame phaseEnd || phaseEnd.Id != phaseEndId ||
                phaseEnd.ItemsCaptured || !phaseEnd.InheritedMovementBatchIds.Contains(frame.Batch.Id) || frame.Batch.AwaitingProgramFrameId is not null)
                throw new InvalidOperationException("An actual phase end lost its exact inherited movement return.");
            AdvanceRuntimeFrame(phaseEndId); return;
        }
        if (frame.ResumeDrawPhaseObligationFrameId is { } drawId)
        {
            if (_resolutionStack.LastOrDefault() is not DrawPhaseObligationFrame drawParent || drawParent.Id != drawId ||
                frame.Batch.ParentFrameId != drawId && drawParent.InheritedMovementBatchIds?.Contains(frame.Batch.Id) != true)
                throw new InvalidOperationException("An actual draw lost its exact gain movement return.");
            AdvanceRuntimeFrame(drawId); return;
        }
        if (frame.ResumeDenglouUseFrameId is { } denglouId)
        { ContinueDenglouNativePayment(denglouId); return; }
        if (frame.ResumeGuoSiUseFrameId is { } guoSiId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame { GuoSiNative: not null, GuoSiCostDrained: false } guoSiParent || guoSiParent.Id != guoSiId || frame.Batch.ParentFrameId != guoSiId || frame.Batch.AwaitingProgramFrameId is not null)
                throw new InvalidOperationException("Sidao lost its exact native hand-material payment return.");
            AssertGuoSiNativeUse(guoSiParent);
            ContinueGuoSiNativePayment(guoSiId); return;
        }
        if (frame.ResumeHistoricalEndingUseFrameId is { } historyId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame { EndingHistoricalUseReturn: not null, EndingHistoricalCostDrained: false } historyParent || historyParent.Id != historyId || frame.Batch.ParentFrameId != historyId || frame.Batch.AwaitingProgramFrameId is not null)
                throw new InvalidOperationException("Historical Ending lost its exact one-Hand movement return.");
            ContinueHistoricalEndingPayment(historyId); return;
        }
        if (frame.ResumeRoundPileAlcoholUseFrameId is { } wineId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame { RoundPileAlcoholReturn: not null, RoundPileAlcoholCostDrained: false } wineParent ||
                wineParent.Id != wineId || frame.Batch.ParentFrameId != wineId || frame.Batch.AwaitingProgramFrameId is not null)
                throw new InvalidOperationException("Pile Alcohol lost its exact cost movement return.");
            ContinueRoundPileAlcoholUse(wineId); return;
        }
        if (frame.ResumeCounterspellPaymentFrameId is { } counterId)
        {
            if (_resolutionStack.LastOrDefault() is not NullificationWindowFrame { CounterspellPayment: not null } counterParent ||
                counterParent.Id != counterId || frame.Batch.ParentFrameId != counterId || frame.Batch.AwaitingProgramFrameId is not null)
                throw new InvalidOperationException("Counterspell lost its exact payment movement return.");
            ContinuePolicyCounterspellPayment(counterId); return;
        }
        if (frame.ResumeMouZhitianFireFrameId is { } mouFireId) { ContinueMouZhitianFirePayment(mouFireId); return; }
        if (frame.ResumeColorFireAttackFrameId is { } fireId)
        {
            if (_resolutionStack.LastOrDefault() is not CardUseFrame { ColorFireAttack.PaidCardId: not null } fireParent ||
                fireParent.Id != fireId || frame.Batch.ParentFrameId != fireId || frame.Batch.AwaitingProgramFrameId is not null)
                throw new InvalidOperationException("Color Fire Attack lost its exact movement return.");
            ContinueColorFireAttackPayment(fireId); return;
        }
        if (frame.ResumeEquipmentRecastFrameId is { } recastId)
        {
            if (_resolutionStack.LastOrDefault() is not EquipmentRecastFrame recastParent ||
                recastParent.Id != recastId || frame.Batch.ParentFrameId != recastId)
                throw new InvalidOperationException("Equipment recast lost its exact movement return.");
            AdvanceRuntimeFrame(recastId); return;
        }
        if (frame.ResumeProgramFrameId is { } resume)
        {
            AdvanceRuntimeProgram(resume);
            return;
        }
        if (frame.ResumeFactionRequestCostFrameId is { } requestCostId)
        {
            if (_resolutionStack.LastOrDefault() is not { PaidFactionRequestCostRecovery: not null } requestProducer ||
                requestProducer.Id != requestCostId || frame.Batch.ParentFrameId != requestCostId)
                throw new InvalidOperationException("Faction request cost lost its exact movement return.");
            ContinuePaidFactionRequestCost(requestCostId); return;
        }
        if (!TryBeginCardsMovedProgramWindow() &&
            _resolutionStack.LastOrDefault() is ProgramSkillFrame
                { } awaited && IsAwaitingProgramMovement(awaited))
            ReturnRuntimeProgramMovement(awaited.Id);
        return;
    }

    private void AdvanceCardsMovedProgramCandidate(
        CardsMovedTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate,
        bool activated,
        bool completed)
    {
        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(
            frame.Id, candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId,
            candidate.OwnerSeat, CreateCardsMovedProgramContext(frame, candidate).Window, activated, completed));
        AdvanceCardsMovedProgramCursor(frame);
    }

    private void AdvanceCardsMovedProgramCursor(CardsMovedTriggerWindowFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not CardsMovedTriggerWindowFrame current ||
            current.Id != frame.Id || current.CandidateIndex != frame.CandidateIndex)
            throw new InvalidOperationException("The cards-moved trigger cursor is no longer current.");
        ReplaceRuntimeTop(current with
        {
            CandidateIndex = current.CandidateIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        });
    }
}
