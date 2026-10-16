using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf;
using CardGame.Wpf.Controls;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class SkillTargetSelectionChecks
{

    internal sealed class TargetFixture(string kind, int playerCount) : IGameContentPackage
    {
        internal const string ModeId = "identity:skill-target-ui";
        internal const string OwnerId = "fixture:skill-target-owner";
        internal const string SkillId = "fixture:skill-target";
        public PackageManifest Manifest { get; } = new("fixture-skill-target-ui", new(1, 0, 0), []);
        public void Register(IContentRegistryBuilder builder)
        {
            var targetKind = kind == "ordered" ? "livingPairDistinct" : kind == "optional" ? "otherLivingWithHand" : "anyLiving";
            var minimum = kind == "optional" ? 0 : 2;
            var catalog = SkillProgramCatalog.Load($$"""
                {"schemaVersion":{{SkillProgramCatalog.RulesSchemaVersion}},"skills":[{"id":"fixture:skill-target","revision":1,
                "triggers":[{"id":"targets","window":"turnStartBeforeNormalFlow","subject":"owner","optional":false,
                "effects":[{"op":"selectTargets","target":"owner","targetKind":"{{targetKind}}","minimumTargets":{{minimum}},"maximumTargets":2,"targetAiOrder":"stable"},
                {"op":"draw","target":"owner","amount":1}]}]}]}
                """, """{"schemaVersion":3,"skills":{"fixture:skill-target":{"name":"选人测试","description":"选择合法目标"}}}""");
            builder.AddSkill(new(SkillId, "选人测试", "选择合法目标") { Program = catalog.Programs[SkillId], ProgramPresentation = catalog.Presentations[SkillId] });
            builder.AddGeneral(new(OwnerId, "选人者", "supporter", SkillId, "wei", 4));
            var others = Enumerable.Range(1, playerCount - 1).Select(seat => $"fixture:skill-target-{seat}").ToArray();
            foreach (var id in others) builder.AddGeneral(new(id, "目标", "supporter", "standard:none", "shu", 4));
            builder.AddMode(new(ModeId, "选人测试", playerCount, playerCount,
                new Dictionary<string, int> { [nameof(Role.Lord)] = 1, [nameof(Role.Rebel)] = playerCount - 1 },
                "standard:basic-demo", GeneralCandidateCount: playerCount, GeneralPoolIds: [OwnerId, .. others]));
        }
    }
}
