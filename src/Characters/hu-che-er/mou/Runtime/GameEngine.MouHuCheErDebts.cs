namespace CardGame.Core;
public sealed partial class GameEngine
{
    private IEnumerable<MouHuCheErDebtEvent> ActiveMouHuCheErDebts(int target) => !HasMouHuCheEr ? [] : ProgramEventHistory<MouHuCheErDebtEvent>().Where(d => d.Target == target && !ProgramEventHistory<MouHuCheErDebtExpiredEvent>().Any(e => e.FrameId == d.FrameId));
    private bool MouHuCheErEquipmentDisabled(int target) => ActiveMouHuCheErDebts(target).Any();
    private IReadOnlyList<Card> KongwuEffectiveEquipment(CharacterState player) => MouHuCheErEquipmentDisabled(player.Seat) ? [] : GetEquipment(player).Where(card => !IsHappyWeiYanBone(card.Id)).ToArray();
    private IReadOnlyList<Card> KongwuEffectiveEquipment(int seat) => KongwuEffectiveEquipment(_players[seat]);
    private IReadOnlyList<Card> KongwuUsableWoodenOxGrain(CharacterState player) => MouHuCheErEquipmentDisabled(player.Seat) ? [] : GetWoodenOxGrain(player);
    private bool MouHuCheErEquipmentGrantQualified(CharacterState owner, SkillGrant grant) => !grant.SourceId.StartsWith("equipment:", StringComparison.Ordinal) || !MouHuCheErEquipmentDisabled(owner.Seat);
    private readonly Dictionary<long, (int Events, long Stamp)> _mouHuCheErQualificationDependencies = [];
    private long _mouHuCheErQualificationStamp;
    private long CaptureMouHuCheErEquipmentQualificationStamp(long original)
    {
        if (!HasMouHuCheEr) return original;
        var events = ProgramEventHistory<MouHuCheErDebtEvent>().Count() + ProgramEventHistory<MouHuCheErDebtExpiredEvent>().Count();
        if (events == 0) return original;
        if (_mouHuCheErQualificationDependencies.TryGetValue(original, out var cached) && cached.Events == events) return cached.Stamp;
        var stamp = long.MinValue + ++_mouHuCheErQualificationStamp;
        _mouHuCheErQualificationDependencies[original] = (events, stamp); return stamp;
    }
    private int MouHuCheErDrawPenalty(CharacterState player) => _currentSeat == player.Seat && _turnProgression.OwnerSeat == player.Seat
        ? ActiveMouHuCheErDebts(player.Seat).Count(d => ProgramEventHistory<MouHuCheErDebtTurnEvent>().Any(t => t.FrameId == d.FrameId && t.Target == player.Seat && t.Turn == _turnNumber)) : 0;
}
