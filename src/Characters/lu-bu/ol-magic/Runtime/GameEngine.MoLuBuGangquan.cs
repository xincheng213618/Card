namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static bool IsMoGangquanSource(CardConversionSource? s) => s?.SkillId == MoLuBuComposition.Gangquan && s.BindingId == "qi-equipment-fire-slash";
    private bool CanUseMoGangquan(CharacterState owner, IndexedSkillProgramInstance instance, SkillProgramViewAs rule, bool forResponse, bool dyingUse)
    {
        if (instance.SkillId != MoLuBuComposition.Gangquan) return true;
        if (rule.OutputKind == CardKind.Duel) return !MoLuBuLastUseWasQi(owner.Seat) && !forResponse;
        return MoLuBuLastUseWasQi(owner.Seat) && !dyingUse &&
            (!forResponse && _phase == TurnPhase.Play && _currentSeat == owner.Seat && _resolutionStack.Count == 0 || FuleTrueSlashUseFrontier(owner));
    }
    private bool MoGangquanPhysicalFire(CharacterState owner, Card card) => IsMoGangquanMaterial(owner, card);
    private bool IsMoGangquanMaterial(CharacterState owner, Card card) => HasMoLuBu && card.Id > 0 && HasRuntimeSkill(owner, MoLuBuComposition.Gangquan) &&
        GetProgramViewAsConversions(owner, card, CardKind.FireSlash, false).Any(IsMoGangquanSource);
    private bool IsMoGangquanMaterial(int owner, CardKind kind, IReadOnlyList<int>? ids) => HasMoLuBu && kind == CardKind.FireSlash &&
        ids is [var id] && id > 0 && IsValidPlayerSeat(owner) && HasRuntimeSkill(_players[owner], MoLuBuComposition.Gangquan) &&
        FindOwnedPlayableCard(_players[owner], id) is { } card && IsMoGangquanMaterial(_players[owner], card);
    private bool MoGangquanTargetAllowed(CharacterState actor, CharacterState target, CardKind kind, CardConversionSource? source, IReadOnlyList<int>? ids = null) =>
        !(IsMoGangquanSource(source) || IsMoGangquanMaterial(actor.Seat, kind, ids)) || GetAliveSeatDistance(actor.Seat, target.Seat) == 1;
    private bool MoGangquanBorrowedDistance(CharacterState actor, CharacterState target, CardKind kind, IReadOnlyList<int>? ids, CardConversionSource? source = null) =>
        kind == CardKind.FireSlash && target.IsAlive && target.Seat != actor.Seat && GetAliveSeatDistance(actor.Seat, target.Seat) == 1 &&
        (IsMoGangquanSource(source) || IsMoGangquanMaterial(actor.Seat, kind, ids) || ids is null && GetPlayableCards(actor).Concat(GetEquipment(actor)).Any(card => IsMoGangquanMaterial(actor, card)));
    private IReadOnlyList<int> MoGangquanTargets(CharacterState actor, Card card, CardConversionSource source)
    {
        return Array.AsReadOnly(_players.Where(target => target.IsAlive && target.Seat != actor.Seat && GetAliveSeatDistance(actor.Seat, target.Seat) == 1 &&
            CanSpendSlashUse(actor, target, false, CardKind.FireSlash, card) && CanUseSlashTarget(actor, target, card, source, CardKind.FireSlash, ignoreDistance: true))
            .OrderBy(target => (target.Seat - actor.Seat + _players.Count) % _players.Count).Select(target => target.Seat).ToArray());
    }
    [ProgramCardAction(ProgramCardActionPhase.AfterProgram, 30800)]
    private void AddMoGangquanActions(List<LegalAction> actions, CharacterState actor, IReadOnlyList<Card> playable, int? selectedPhysicalCardId = null)
    {
        actions.RemoveAll(a => IsMoGangquanSource(a.ConversionSource));
        foreach (var card in playable.Concat(GetEquipment(actor)).DistinctBy(c => c.Id).Where(c => selectedPhysicalCardId is null || c.Id == selectedPhysicalCardId))
        foreach (var source in GetProgramViewAsConversions(actor, card, CardKind.FireSlash, false).Where(IsMoGangquanSource))
        {
            if (IsTurnHandCardRestricted(actor, card)) continue;
            var targets = MoGangquanTargets(actor, card, source); if (targets.Count == 0) continue;
            actions.Add(new(LegalActionKind.Slash, card.Id, targets[0], "罡拳：将装备牌当火杀使用，指定全部合法相邻角色", CardKind.FireSlash, TargetSeats: targets) { ConversionSource = source });
        }
    }
    private bool IsMoGangquanMultiSlash(CharacterState actor, Card card, CardKind kind, CardConversionSource? source, IReadOnlyList<int> targets) =>
        kind == CardKind.FireSlash && IsMoGangquanSource(source) && targets.SequenceEqual(MoGangquanTargets(actor, card, source!));
    private IReadOnlyList<int> MoGangquanNativeTargets(int actor, Card card, CardKind kind, CardConversionSource? source, IReadOnlyList<int> original)
    {
        source ??= _selectedUseConversion ?? _selectedResponseConversion;
        if (kind != CardKind.FireSlash || !IsMoGangquanSource(source)) return original;
        var targets = MoGangquanTargets(_players[actor], card, source!);
        if (targets.Count == 0 || original.Any(target => !targets.Contains(target))) throw new InvalidOperationException("Gangquan must use its complete legal living-neighbor target plan.");
        return targets;
    }
    private CharacterState MoGangquanFirstTarget(CharacterState actor, CharacterState fallback, Card card, CardKind kind, CardConversionSource? source)
    {
        source ??= _selectedUseConversion ?? _selectedResponseConversion;
        if (kind != CardKind.FireSlash || !IsMoGangquanSource(source)) return fallback;
        var targets = MoGangquanTargets(actor, card, source!); return targets.Count > 0 ? _players[targets[0]] : fallback;
    }
    private IReadOnlyList<LegalAction> FilterMoGangquanActions(CharacterState actor, IEnumerable<LegalAction> actions) => Array.AsReadOnly(actions.Where(a => !IsMoGangquanSource(a.ConversionSource) ||
        a.CardId is { } id && FindOwnedPlayableCard(actor, id) is { } card && a.TargetSeats.SequenceEqual(MoGangquanTargets(actor, card, a.ConversionSource!))).ToArray());

    [ProgramCardLegality(ProgramCardLegalityStage.ConfiguredConversionBan, 40)]
    private bool MoGangquanConversionUnavailable(ProgramConfiguredViewAsQuery query) =>
        !CanUseMoGangquan(query.Owner, query.Instance, query.Rule, query.ForResponse, query.DyingUse);

    [ProgramDistanceRule(ProgramDistanceStage.BorrowedSlash, 10)]
    private bool DistanceBorrowedSlash10MoGangquanBorrowedDistance(ProgramSlashDistanceQuery q) =>
        MoGangquanBorrowedDistance(q.Actor, q.Target, q.Kind, q.PhysicalCardIds, q.Conversion);


    [ProgramDistanceRule(ProgramDistanceStage.BorrowedPhysicalSlash, 10)]
    private bool DistanceBorrowedPhysicalSlash10MoGangquanBorrowedDistance(ProgramSlashDistanceQuery q) =>
        MoGangquanBorrowedDistance(q.Actor, q.Target, q.Kind, [q.PhysicalCard!.Id]);

}
