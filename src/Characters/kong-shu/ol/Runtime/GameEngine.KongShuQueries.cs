namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasLeiluanCapability => _contentRegistry?.ProgramDependencies.UsesLeiluanBasicUse == true;
    private bool HasKongShuCapability => HasLeiluanCapability || _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveFuchaoCurrentTurnChain) == true || _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveFuchaoRoundEndingDrawAndDiscard) == true;
    private int LeiluanCurrentX => Math.Max(1, _players.Count(p => p.IsAlive && p.IsChained));
    private bool LeiluanDisabled(int owner, string state)
    {
        var history = CompleteProgramEventHistory().ToArray();
        var at = Array.FindLastIndex(history, e => e is LeiluanDisabledEvent d && d.OwnerSeat == owner && d.SkillId == "ol:leiluan" && d.StateId == state);
        return at >= 0 && !history.Skip(at + 1).OfType<DamageAppliedEvent>().Any(e => e.TargetSeat == owner && e.Amount > 0);
    }
    private bool LeiluanNameUsed(int owner, CardKind kind)
    {
        var history = CompleteProgramEventHistory().ToArray();
        foreach (var e in history.OfType<TrueRoundCardNameUsedEvent>().Where(e => e.RoundNumber == _roundNumber && ProgramBasicCardName(e.EffectiveKind) == ProgramBasicCardName(kind)))
        {
            var actor = e.ActorSeat;
            var native = history.OfType<CurrentTurnCardUseKindsRecordedEvent>().SingleOrDefault(n => e.CardActionId is { } id ? n.CardActionId == id : n.CardActionId is null && n.OriginFrameId == e.OriginFrameId);
            var accepted = e.CardActionId is { } actionId ? history.OfType<CardActionAcceptedEvent>().FirstOrDefault(a => a.Action.ActionId == actionId)?.Action : null;
            var response = e.NullificationUse || native?.RoundActionType == CardActionType.Response || accepted?.Type == CardActionType.Response;
            if (!response)
            {
                var declared = history.OfType<CardUseDeclaredEvent>().SingleOrDefault(d => d.ResolutionId == e.OriginFrameId);
                if (declared is null || declared.SourceSeat != e.ActorSeat) throw new InvalidOperationException("Leiluan round history lost its native declaration identity.");
                foreach (var change in history.OfType<ProgramCardUseActorReplacedEvent>().Where(c => c.CardUseFrameId == e.OriginFrameId))
                {
                    if (change.PreviousActorSeat != actor || !IsValidPlayerSeat(change.ActorSeat)) throw new InvalidOperationException("Leiluan round history lost its real actor replacement chain.");
                    actor = change.ActorSeat;
                }
            }
            if (actor == owner) return true;
        }
        return false;
    }
    private bool LeiluanNativeDirection(CharacterState owner, CardKind kind, bool response)
    {
        if (!response) return kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.Peach or CardKind.Alcohol;
        if (kind is CardKind.Peach or CardKind.Alcohol)
            return ActiveDying is { } dying && dying.ResponderSeat == owner.Seat && _players[dying.VictimSeat].IsAlive && _players[dying.VictimSeat].Hp <= 0 &&
                (kind == CardKind.Peach ? CanUsePeachToRescue(owner.Seat, dying.VictimSeat) : dying.VictimSeat == owner.Seat && !HasSelfCardTargetProhibition(owner.Seat));
        return kind == CardKind.Dodge && ActiveFactionDefense is null && IsProgramResponseCardUse(owner, kind);
    }
    private IReadOnlyList<ProgramMultiCardViewAsSelection> LeiluanSelections(CharacterState owner, CardKind kind, bool forResponse, bool ignoreSuitUseProhibition = false)
    {
        if (!HasLeiluanCapability || !owner.IsAlive || _winner != Winner.None || _roundNumber < 1 || !LeiluanNativeDirection(owner, kind, forResponse)) return [];
        var x = LeiluanCurrentX;
        var candidates = GetHand(owner).Concat(GetEquipment(owner)).Concat(UsesFormalWoodenOx && GetEquipment(owner).Any(c => c.Kind == CardKind.WoodenOx) ? _cardZones.CardsAt(CardLocation.WoodenOxGrain(owner.Seat)) : [])
            .Where(c => !IsTurnHandCardRestricted(owner, c) && !HasProgramCardIdentity(owner, c)).OrderBy(c => c.Id).ToArray();
        if (candidates.Length < x) return [];
        var result = new List<ProgramMultiCardViewAsSelection>();
        foreach (var instance in GetSkillBindingShard(owner).ProgramInstances)
        foreach (var rule in instance.Program.ViewAs.Where(r => r.LeiluanBasicUse is not null && (r.OutputKind == kind || !forResponse && kind == CardKind.Slash && IsSlashCard(r.OutputKind))))
        {
            if (LeiluanDisabled(owner.Seat, rule.LeiluanBasicUse!.StateId) || LeiluanNameUsed(owner.Seat, rule.OutputKind) ||
                IsCardUseForbidden(owner.Seat, rule.OutputKind, CardActionType.Use)) continue;
            var source = new CardConversionSource(instance.SkillId, rule.Id, owner.Seat, instance.SkillInstanceId);
            foreach (var cards in EnumerateCardCombinations(candidates, x))
            {
                var ids = cards.Select(c => c.Id).ToArray();
                if (forResponse ? !IsRecordedSuitResponseAllowed(owner, cards, kind) || IsCuiYanNativeResponseForbidden(owner, kind, cards)
                    : !ignoreSuitUseProhibition && (IsTurnPhysicalUseForbidden(owner.Seat, ids) || IsCuiYanNextUseForbidden(owner.Seat, ids, cards.Count == 1 ? PhysicalGroupSuit(owner, cards) : Suit.None))) continue;
                result.Add(new(Array.AsReadOnly(cards.ToArray()), source, rule.OutputKind));
            }
        }
        return Array.AsReadOnly(result.DistinctBy(s => (s.Source, s.OutputKind, Cards: string.Join('-', s.Cards.Select(c => c.Id)))).ToArray());
    }
    private bool IsLeiluanSelection(ProgramMultiCardViewAsSelection selection) => ViewAsRule(selection.Source)?.LeiluanBasicUse is not null;
    private bool IsLeiluanSource(CardConversionSource? source) => source is not null && ViewAsRule(source)?.LeiluanBasicUse is not null;
}