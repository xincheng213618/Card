namespace CardGame.Core;

[ProgramSelectionModule(ProgramSelectionStage.CardAfterDiscard, 50100, SkillProgramEffectOp.ResolveMobileHeQiShanxiView)]
internal sealed class MobileHeQiRedPayment : IProgramSelectionCardFilter
{
 public bool Allows(ProgramSelectionContext c, Card card, IProgramSelectionQueries q) => q.Get<IProgramSelectionSourceQuery>().EffectiveSuit(c.OwnerSeat, card) is Suit.Heart or Suit.Diamond;
}
[ProgramSelectionModule(ProgramSelectionStage.OrdinaryTarget, 50100, SkillProgramEffectOp.ResolveMobileHeQiShanxiView)]
internal sealed class MobileHeQiRangeTarget : IProgramSelectionTargetFilter
{
 public bool Allows(ProgramSelectionContext c, int target, IProgramSelectionQueries q)
 {
  var people = q.Get<IProgramSelectionSourceQuery>(); var range = q.Get<IHeQiSelectionQueries>();
  return target != c.OwnerSeat && people.Participants().Any(p => p.Seat == target && p.IsAlive && p.HandCount + p.EquipmentCount > 0) &&
   (range.HasRoundRange(c.OwnerSeat, target) || range.CombatDistance(c.OwnerSeat, target) <= range.AttackRange(c.OwnerSeat));
 }
}
