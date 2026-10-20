using System.Text.Json.Serialization;
namespace CardGame.Core;
internal sealed class CollectQiaiLivingContributionsDescriptor:WangCanDescriptor
{public override SkillProgramEffectOp Op=>SkillProgramEffectOp.CollectQiaiLivingContributions;public override IReadOnlyList<ProgramResourceOperation> Resources(SkillProgramEffect e)=>[new RequireTriggerWindow(SkillProgramTriggerWindow.DyingEntering)];}
