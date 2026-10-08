using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private long MatchingRecastSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private MatchingRecastMaterial[] MatchingRecastMaterials(int seat) => new[] { CardZoneKind.Hand, CardZoneKind.Equipment }
        .SelectMany(zone => _cardZones.CardsAt(new(zone, seat)).Select(c => new MatchingRecastMaterial(c.Id, c.Kind, new(zone, seat), c.IsGeneralWeapon))).ToArray();
    private static bool MatchingRecastGroup(IReadOnlyList<MatchingRecastMaterial> materials) => materials.Count >= 2 &&
        materials.Select(m => m.CardId).Distinct().Count() == materials.Count &&
        (materials.All(m => EquipmentCatalog.IsEquipment(m.PrintedKind)) ||
         materials.All(m => ProgramBasicCardName(m.PrintedKind) == ProgramBasicCardName(materials[0].PrintedKind)));
    private int[] MatchingRecastSelectableCards(int seat)
    {
        var available = MatchingRecastMaterials(seat);
        var equipmentCount = available.Count(m => EquipmentCatalog.IsEquipment(m.PrintedKind));
        var names = available.GroupBy(m => ProgramBasicCardName(m.PrintedKind)).ToDictionary(g => g.Key, g => g.Count());
        return available.Where(m => EquipmentCatalog.IsEquipment(m.PrintedKind) ? equipmentCount >= 2 : names[ProgramBasicCardName(m.PrintedKind)] >= 2)
            .Select(m => m.CardId).ToArray();
    }
    private bool ValidMatchingRecastSelection(int seat, IReadOnlyList<int> ids)
    {
        var available = MatchingRecastMaterials(seat);
        return ids.All(id => available.Any(m => m.CardId == id)) && MatchingRecastGroup(ids.Select(id => available.Single(m => m.CardId == id)).ToArray());
    }
    private bool MatchingRecastMaterialsStillOwned(MatchingRecastParticipantReceipt p) => p.Materials.All(m =>
        m.From.OwnerSeat == p.Seat && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        _cardZones.GetLocation(m.CardId) == m.From && _cardZones.CardsAt(m.From)
            .Any(c => c.Id == m.CardId && c.Kind == m.PrintedKind && c.IsGeneralWeapon == m.IsGeneralWeapon));
    private static bool MatchingRecastPeerCursor(ProgramMatchingRecastReceipt r) => r.Stage is
        MatchingRecastStage.PeerCostChildren or MatchingRecastStage.PeerDrawChildren;
    private static MatchingRecastParticipantReceipt CurrentMatchingRecast(ProgramMatchingRecastReceipt r) => MatchingRecastPeerCursor(r) ? r.Peer : r.Owner;
    private static ProgramMatchingRecastReceipt WithCurrentMatchingRecast(ProgramMatchingRecastReceipt r, MatchingRecastParticipantReceipt p) =>
        MatchingRecastPeerCursor(r) ? r with { Peer = p } : r with { Owner = p };

    private SkillProgramStepOutcome BeginMatchingRecast(ProgramSkillFrame supplied, int peerSeat, string ledgerId)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        var plan = ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!);
        if (plan.Activation is not { } activation || f.TriggerId is not null || f.WindowContext is not null ||
            f.InstructionIndex != 1 || f.MatchingRecast is not null || f.SelectedTargetSeats is not [var target] || target != peerSeat ||
            !IsValidPlayerSeat(target) || _winner != Winner.None || !_players[f.OwnerSeat].IsAlive || !_players[target].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId) || _phase != TurnPhase.Play ||
            _currentSeat != f.OwnerSeat || _cardUseDebitPhaseInstanceId <= 0 || _resolutionStack.Count != 1 ||
            !ValidMatchingRecastSelection(f.OwnerSeat, f.SelectedCardIds) || activation.TargetPhaseLedgerId != ledgerId ||
            !CanActivateTargetPhaseLedger(f.OwnerSeat, f.SkillId, ledgerId, peerSeat))
            throw new InvalidOperationException("Matching recast requires its original HE group, living target and unused actual play-phase target debit.");
        MatchingRecastComposition.ValidateActivation(f.SkillId, activation);
        var available = MatchingRecastMaterials(f.OwnerSeat);
        var materials = f.SelectedCardIds.Select(id => available.Single(m => m.CardId == id)).ToArray();
        ConsumeProgramTargetPhaseLedger(f, ledgerId);
        var r = new ProgramMatchingRecastReceipt { InstructionIndex = f.InstructionIndex,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash,
            UsageGroup = activation.UsageGroup, TargetLedgerId = ledgerId, ActualTurnNumber = _turnNumber,
            ActualTurnOwnerSeat = _turnProgression.OwnerSeat, PhaseInstanceId = _cardUseDebitPhaseInstanceId,
            Category = GetProgramCardCategory(materials[0].PrintedKind), Stage = MatchingRecastStage.OwnerReady,
            Owner = new(f.OwnerSeat) { Materials = materials }, Peer = new(peerSeat) };
        ReplaceRuntimeTop(f = f with { MatchingRecast = r });
        AdvanceEventRulesAndQueueFact(new MatchingRecastStartedEvent(f.Id, r.Source, r.GameplayHash, r.UsageGroup,
            ledgerId, peerSeat, r.ActualTurnNumber, r.ActualTurnOwnerSeat, r.PhaseInstanceId));
        IssueMatchingRecastCost(f, false);
        return SkillProgramStepOutcome.AwaitChild;
    }
    private void IssueMatchingRecastCost(ProgramSkillFrame f, bool peer)
    {
        var r = f.MatchingRecast!; var p = peer ? r.Peer : r.Owner;
        if (p.CostIssued || p.DrawIssued || p.Materials.Count != r.Owner.Materials.Count || !MatchingRecastMaterialsStillOwned(p) ||
            (peer ? r.Stage != MatchingRecastStage.ChoosingPeerCards : r.Stage != MatchingRecastStage.OwnerReady))
            throw new InvalidOperationException("Matching recast can pay only its exact frozen original materials once.");
        var before = MatchingRecastSequence;
        r = r with { Stage = peer ? MatchingRecastStage.PeerCostChildren : MatchingRecastStage.OwnerCostChildren };
        r = WithCurrentMatchingRecast(r, p with { CostIssued = true, CostBefore = before, CostAfter = before });
        ReplaceRuntimeTop(f = f with { MatchingRecast = r, PendingMovementContinuation = new(p.Seat, 0, null) });
        foreach (var player in _players) _observedSkillGrantRevisions.TryAdd(player.Seat, player.SkillGrants.Revision);
        // Recast bypasses discard eligibility. All physical costs commit before equipment removal hooks run.
        MoveProgramCardsFromMultipleSources(p.Materials.Select(m => m.CardId).ToArray(), CardLocation.DiscardPile, CardMoveReasons.RecastDiscard, (batch, records) =>
        {
            if (records.Count != p.Materials.Count || !records.Zip(p.Materials).All(pair => pair.First.CardId == pair.Second.CardId &&
                pair.First.CardKind == pair.Second.PrintedKind && pair.First.From == pair.Second.From &&
                pair.First.To == pair.Second.Destination && pair.First.Reason == CardMoveReasons.RecastDiscard))
                throw new InvalidOperationException("Matching recast changed its paid original physical HE group.");
            var current = GetActiveProgramFrame(f.Id); var receipt = current.MatchingRecast!;
            var after = records.Max(m => m.Sequence);
            ReplaceRuntimeTop(current with { MatchingRecast = WithCurrentMatchingRecast(receipt,
                CurrentMatchingRecast(receipt) with { CostAfter = after, CostBatchId = batch }) });
            foreach (var m in p.Materials)
                AdvanceEventRulesAndQueueFact(new MatchingRecastPaidEvent(f.Id, peer ? 1 : 0, p.Seat, m.CardId,
                    m.PrintedKind, m.From, batch, before, after));
        });
        AdvanceRuntimeProgram(f.Id);
    }
    private void IssueMatchingRecastDraw(ProgramSkillFrame f)
    {
        var r = f.MatchingRecast!; var p = CurrentMatchingRecast(r); var peer = MatchingRecastPeerCursor(r);
        if (r.Stage is not (MatchingRecastStage.OwnerCostChildren or MatchingRecastStage.PeerCostChildren) ||
            !p.CostIssued || p.DrawIssued || f.PendingMovementContinuation is not null)
            throw new InvalidOperationException("A matching recast draw must follow its drained real payment exactly once.");
        var before = MatchingRecastSequence;
        r = r with { Stage = peer ? MatchingRecastStage.PeerDrawChildren : MatchingRecastStage.OwnerDrawChildren };
        ReplaceRuntimeTop(f = f with { MatchingRecast = WithCurrentMatchingRecast(r, p with { DrawIssued = true, DrawBefore = before, DrawAfter = before }),
            PendingMovementContinuation = new(p.Seat, 0, null) });
        var actual = _winner == Winner.None && _players[p.Seat].IsAlive
            ? DrawCards(_players[p.Seat], p.Materials.Count, true, CardMoveReasons.RecastDraw).Count : 0;
        var current = GetActiveProgramFrame(f.Id); r = current.MatchingRecast!;
        ReplaceRuntimeTop(current with { MatchingRecast = WithCurrentMatchingRecast(r,
            CurrentMatchingRecast(r) with { ActualDrawCount = actual, DrawAfter = MatchingRecastSequence }) });
        AdvanceEventRulesAndQueueFact(new MatchingRecastDrawIssuedEvent(f.Id, peer ? 1 : 0, p.Seat, actual, before, MatchingRecastSequence));
        for (var i = 0; i < p.Materials.Count; i++) AdvanceEventRulesAndQueueFact(new CardRecastEvent(p.Seat, p.Materials[i].CardId, p.Materials[i].PrintedKind, i < actual ? 1 : 0));
        AdvanceRuntimeProgram(f.Id);
    }
    private PromptChoice MatchingRecastChoice(ProgramSkillFrame f, string step, string label, int? card = null) =>
        new(new($"matching-recast.{f.Id}.{step}.{card ?? -1}"), label, card is { } id ? [id] : [], [],
            new Dictionary<string, string> { ["program-action"] = "matching-recast", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["step"] = step });
    private IReadOnlyList<PromptChoice> MatchingRecastChoices(ProgramSkillFrame f)
    {
        var r = f.MatchingRecast!;
        if (r.Stage == MatchingRecastStage.ChoosingPeerCards)
            return Array.AsReadOnly(r.EligiblePeerMaterials.Where(m => r.Peer.Materials.All(p => p.CardId != m.CardId))
                .Select(m => MatchingRecastChoice(f, "card", $"重铸【{GetAdvancedCard(m.CardId).DisplayName}】", m.CardId)).ToArray());
        if (r.Stage != MatchingRecastStage.ChoosingPeer) return [];
        var choices = new List<PromptChoice>();
        if (r.EligiblePeerMaterials.Count >= r.Owner.Materials.Count)
            choices.Add(MatchingRecastChoice(f, "recast", $"重铸{r.Owner.Materials.Count}张同类型牌"));
        choices.Add(MatchingRecastChoice(f, "damage", "受到发动者的1点雷电伤害"));
        return Array.AsReadOnly(choices.ToArray());
    }
    private void PublishMatchingRecastChoice(ProgramSkillFrame f) => PublishParticipantHandChoice(f, f.MatchingRecast!.Peer.Seat,
        MatchingRecastChoices(f), f.MatchingRecast.Stage == MatchingRecastStage.ChoosingPeer
            ? "选择重铸等量同类型牌，或受到1点雷电伤害。" : "选择本次重铸的牌，选齐后同时重铸。", f.MatchingRecast.Peer.Seat);
    private void ResolveMatchingRecastChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Matching recast lost its choice owner.");
        AssertMatchingRecast(f); var r = f.MatchingRecast!;
        if (_pendingDecision is not { } prompt || !IsMatchingRecastChoice(f, prompt) || !MatchingRecastChoices(f).Any(c => SameNameHandChoicesEqual(c, choice)))
            throw new InvalidOperationException("Matching recast changed its exact participant-only private choice.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[r.Peer.Seat].IsAlive) { FinishMatchingRecast(f); return; }
        if (r.Stage == MatchingRecastStage.ChoosingPeer)
        {
            if (choice.Parameters["step"] == "damage")
            {
                ReplaceRuntimeTop(f = f with { MatchingRecast = r with { Stage = MatchingRecastStage.DamageIssued, DamageIssued = true } });
                AdvanceEventRulesAndQueueFact(new MatchingRecastDamageIssuedEvent(f.Id, f.OwnerSeat, r.Peer.Seat, 1, DamageNature.Thunder));
                BeginProgramSkillDamage(f, r.Peer.Seat, 1, nature: DamageNature.Thunder); return;
            }
            ReplaceRuntimeTop(f = f with { MatchingRecast = r with { Stage = MatchingRecastStage.ChoosingPeerCards } });
            PublishMatchingRecastChoice(f); return;
        }
        if (r.Stage != MatchingRecastStage.ChoosingPeerCards || choice.Parameters["step"] != "card")
            throw new InvalidOperationException("Matching recast lost its frozen peer payment stage.");
        var material = r.EligiblePeerMaterials.Single(m => m.CardId == choice.Cards.Single());
        var peer = r.Peer with { Materials = r.Peer.Materials.Append(material).ToArray() };
        if (!MatchingRecastMaterialsStillOwned(peer)) throw new InvalidOperationException("A peer recast must pay the selected still-owned original HE entities.");
        ReplaceRuntimeTop(f = f with { MatchingRecast = r with { Peer = peer } });
        if (peer.Materials.Count == r.Owner.Materials.Count) IssueMatchingRecastCost(f, true);
        else PublishMatchingRecastChoice(f);
    }
    private bool DrainMatchingRecastChildren(ProgramSkillFrame f) =>
        TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    private bool ResumeMatchingRecast(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MatchingRecast: { } r } f || f.Id != id) return false;
        AssertMatchingRecast(f);
        if (r.Stage is MatchingRecastStage.ChoosingPeer or MatchingRecastStage.ChoosingPeerCards)
        { if (_pendingDecision is null) PublishMatchingRecastChoice(f); return true; }
        if (r.Stage is MatchingRecastStage.OwnerCostChildren or MatchingRecastStage.OwnerDrawChildren or MatchingRecastStage.PeerCostChildren or MatchingRecastStage.PeerDrawChildren)
        {
            if (DrainMatchingRecastChildren(f)) return true;
            ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
            if (r.Stage is MatchingRecastStage.OwnerCostChildren or MatchingRecastStage.PeerCostChildren) { IssueMatchingRecastDraw(f); return true; }
            if (r.Stage == MatchingRecastStage.OwnerDrawChildren && _winner == Winner.None && _players[r.Peer.Seat].IsAlive)
            {
                ReplaceRuntimeTop(f = f with { MatchingRecast = r with { Stage = MatchingRecastStage.ChoosingPeer,
                    EligiblePeerMaterials = MatchingRecastMaterials(r.Peer.Seat).Where(m => GetProgramCardCategory(m.PrintedKind) == r.Category).ToArray() } });
                PublishMatchingRecastChoice(f); return true;
            }
        }
        if (r.Stage == MatchingRecastStage.DamageIssued && f.AttackAttempt is not null) return false;
        FinishMatchingRecast(f); return true;
    }
    private void FinishMatchingRecast(ProgramSkillFrame f)
    {
        var r = f.MatchingRecast!;
        ReplaceRuntimeTop(f = f with { MatchingRecast = r with { Stage = MatchingRecastStage.Complete }, PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new MatchingRecastCompletedEvent(f.Id, r.Owner.CostIssued, r.Peer.CostIssued, r.DamageIssued, r.Owner.ActualDrawCount, r.Peer.ActualDrawCount));
        FinishProgramSkill(f, true);
    }
    private bool ReturnMatchingRecastMovement(ProgramSkillFrame f)
    {
        if (f.MatchingRecast is not { Stage: MatchingRecastStage.OwnerCostChildren or MatchingRecastStage.OwnerDrawChildren or MatchingRecastStage.PeerCostChildren or MatchingRecastStage.PeerDrawChildren }) return false;
        AssertMatchingRecast(f);
        if (f.PendingMovementContinuation is null) throw new InvalidOperationException("A matching recast child lost its exact movement return.");
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool IsMatchingRecastMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.RecastMatchingCardsThenPeerChoice && f.MatchingRecast is
            { Stage: MatchingRecastStage.OwnerCostChildren or MatchingRecastStage.OwnerDrawChildren or MatchingRecastStage.PeerCostChildren or MatchingRecastStage.PeerDrawChildren } r &&
        f.PendingMovementContinuation == pending && pending.SubjectSeat == CurrentMatchingRecast(r).Seat && pending.BeforeCount == 0 &&
        pending.CoverageResultBind is null && ValidMatchingRecastReceipt(f);
    private bool CanContinueMatchingRecast(ProgramSkillFrame f) => f.MatchingRecast is { Owner.CostIssued: true } && ValidMatchingRecastReceipt(f);
    private PromptChoice SelectAiMatchingRecast(PendingDecision decision, ProgramSkillFrame f) => decision.Choices
        .OrderBy(c => c.Parameters["step"] == "damage" ? 1 : 0)
        .ThenBy(c => c.Cards.Count == 1 ? GetKeepValue(GetAdvancedCard(c.Cards[0]), _players[decision.PlayerSeat]) : 0)
        .ThenBy(c => c.Id.Value).First();
    private sealed partial class ProgramSkillHost : IMatchingRecastProgramHost
    {
        public SkillProgramStepOutcome RecastMatchingCardsThenPeerChoice(ProgramSkillFrame f, int peer, string ledger) => engine.BeginMatchingRecast(f, peer, ledger);
        public bool CanContinueMatchingRecast(ProgramSkillFrame f) => engine.CanContinueMatchingRecast(f);
    }
}
