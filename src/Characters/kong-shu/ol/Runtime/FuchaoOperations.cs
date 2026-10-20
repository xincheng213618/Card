using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CardGame.Core;
internal sealed class ResolveFuchaoCurrentTurnChainDescriptor() : KongShuDescriptor(SkillProgramEffectOp.ResolveFuchaoCurrentTurnChain) {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger; }
internal sealed class ResolveFuchaoRoundEndingDrawAndDiscardDescriptor() : KongShuDescriptor(SkillProgramEffectOp.ResolveFuchaoRoundEndingDrawAndDiscard) {
    public override ProgramDependencyKinds TracksActualTurnCursorReturn => ProgramDependencyKinds.Trigger;
    public override ProgramFactRequirements FactRequirements => new(Triggered: ProgramTrackedFact.NativeLogicalDiscard);
 }
