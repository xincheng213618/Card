using System.Text.Json.Serialization;

namespace CardGame.Core;
// These are trusted rule facts. Hidden draw identities are projected only through their owner's native card snapshots.
public sealed record SilvEntityMarkedEvent(int CardId, CardConversionSource Source, string GameplayHash, long ProducerFrameId,
    long GainMovementSequence) : IGameEvent;
internal sealed class DrawSilvMarkedCardAfterDamageDescriptor : LiWanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.DrawSilvMarkedCardAfterDamage; }
internal sealed class ReturnDiscardedSilvEntityDescriptor : LiWanDescriptor
{ public override SkillProgramEffectOp Op => SkillProgramEffectOp.ReturnDiscardedSilvEntity;
    public override bool AllowsDeadTarget(in ProgramEffectContinuationContext context) => true;
}
