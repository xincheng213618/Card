using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using CardGame.Content.Standard;
using CardGame.Core;
using CardGame.Wpf.Persistence;
using CardGame.Wpf.ViewModels;

internal static class TieredRoundZeroUseUiChecks
{
    public static void PublicZeroUseTargetsAndRealCardSubmission()
    {
        var registry = ContentRegistry.Build(new StandardContentPackage(), new Fixture());
        var game = GameEngine.CreateStandard(new GameOptions
        {
            Seed = 31, PlayerCount = 4, HumanSeat = 0, HumanRole = Role.Lord,
            ModeId = Fixture.Mode, UseInteractiveSetup = true, AdvanceAfterHumanCommands = false, MaxTurns = 8
        }, registry);
        Accept(game, new StartGameCommand());
        Accept(game, new SelectGeneralCommand(0, "fixture:zero-ui-owner", game.Revision, game.PendingDecision!.PromptId));
        Reach(game, p => p.Kind == DecisionKind.PlayCard);
        var store = new MemorySaveStore();
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        using var model = new MainViewModel(false, game.Seed, true, store, useExpandedContent: true,
            contentRegistry: registry) { IsMotionEnabled = false };
        model.LoadManualGameCommand.Execute(null);
        Program.Assert(!model.HasSaveError, model.SaveStatus);

        // Retain the real entity path in the same small game before tier2 exists.
        game = Program.Engine(model);
        var equip = game.GetHumanLegalActions().First(a => a.Kind == LegalActionKind.Equip && a.ConversionSource is null);
        var card = model.Hand.Single(c => c.Id == equip.CardId);
        model.SelectCardCommand.Execute(card);
        Program.Assert(model.CanConfirmSelected && model.Hand.All(c => c.Id > 0),
            "The existing physical equipment action remains selectable without a fake zero card.");
        model.ConfirmSelectedCommand.Execute(null);
        var physical = game.AcceptedCommands.OfType<PlayCardCommand>().Last();
        Program.Assert(physical.CardId == card.Id && physical.CardId > 0 && physical.ConversionSource is null &&
                       game.CreateSnapshot(0).Players[0].Equipment.Any(c => c.Id == card.Id) &&
                       game.CardMovements.Count(m => m.CardId == card.Id && m.From.Zone == CardZoneKind.Hand &&
                           m.To.Zone == CardZoneKind.Processing) == 1,
            "The old entity button submits the exact real PlayCard command and pays that card once.");
        Reach(game, p => p.Kind == DecisionKind.PlayCard);

        for (var tier = 0; tier < 2; tier++)
        {
            Accept(game, new UseProgramSkillCommand(0, Fixture.Driver, "hurt", [], [], game.Revision, game.PendingDecision!.PromptId));
            Reach(game, p => p.SkillPrompt?.SkillId == Fixture.Danxin && p.Choices.Any(c =>
                c.Parameters.GetValueOrDefault("program-action") == "activate"));
            var prompt = game.CreateSnapshot(0).PendingDecision!;
            Accept(game, new AnswerPromptCommand(0, prompt.PromptId,
                prompt.Choices.First(c => c.Parameters.GetValueOrDefault("program-action") == "activate").Id, game.Revision));
            Reach(game, p => p.Kind == DecisionKind.PlayCard);
        }
        store.Write(GameSaveSlot.Manual, new(1, DateTimeOffset.UtcNow, false, game.CreateCheckpoint()));
        model.LoadManualGameCommand.Execute(null);
        game = Program.Engine(model);
        var zero = model.HumanActiveSkillActions.First(a => a.CardId == 0 &&
            a.TieredRoundZeroUse is not null && a.PlayedCardKind == CardKind.Slash);
        Program.Assert(model.SupplementalActiveSkillActions.Contains(zero) && model.Hand.All(c => c.Id > 0),
            "A real published zero-material action is exposed through existing active-action buttons, without a card face.");
        var commandCount = game.AcceptedCommands.Count;
        model.SelectActiveSkillCommand.Execute(zero);
        Program.Assert(model.IsActiveSkillSelectionPending && !model.CanConfirmSelected &&
                       model.Seats.Where(s => s.IsLegalTarget).Select(s => s.Seat).ToHashSet().SetEquals(zero.TargetSeats),
            "The public action opens an exact target draft and does not submit before confirmation.");
        model.ClearSelectionCommand.Execute(null);
        Program.Assert(!model.IsActiveSkillSelectionPending && game.AcceptedCommands.Count == commandCount,
            "Cancelling an unconfirmed zero action neither issues a Use nor pays a material.");
        model.SelectActiveSkillCommand.Execute(zero);
        foreach (var target in zero.TargetSeats.Reverse())
            model.SelectTargetCommand.Execute(model.Seats.Single(s => s.Seat == target));
        Program.Assert(model.CanConfirmSelected, "The exact public target set enables the existing confirm control.");
        model.ConfirmSelectedCommand.Execute(null);
        var command = game.AcceptedCommands.OfType<PlayCardCommand>().Last();
        Program.Assert(command.CardId == 0 && command.PlayedCardKind == zero.PlayedCardKind &&
                       command.TargetCardId == zero.TargetCardId && command.TargetSeats!.SequenceEqual(zero.TargetSeats) &&
                       command.ConversionSource == zero.ConversionSource &&
                       (command.AdditionalConversionSources ?? []).SequenceEqual(zero.AdditionalConversionSources ?? []) &&
                       game.Events.Select(e => e.Payload).OfType<TieredRoundConversionUseIssuedEvent>().Count(e =>
                           e.Receipt.ActorSeat == 0 && e.Receipt.EffectiveKind == CardKind.Slash && e.Receipt.MaterialCount == 0) == 1 &&
                       game.CardMovements.All(m => m.CardId > 0) && model.Hand.All(c => c.Id > 0),
            "The UI submits the original PlayCard identity, ordered targets and all conversion sources, issuing one zero-material Use.");
    }

    private static void Accept(GameEngine game, GameCommand command)
    {
        var result = game.Submit(command);
        Program.Assert(result.Accepted, result.Error?.Message ?? "Expected the real fixture command to be accepted.");
    }

    private static void Reach(GameEngine game, Func<PendingDecision, bool> predicate)
    {
        for (var step = 0; step < 80; step++)
        {
            if (game.CreateSnapshot(0).PendingDecision is { } prompt && predicate(prompt)) return;
            Accept(game, new AdvanceOneStepCommand(game.Revision));
        }
        throw new InvalidOperationException("The fixed small UI fixture did not reach its actual published prompt.");
    }

    private sealed class Fixture : IGameContentPackage
    {
        internal const string Mode = "identity:classic-tiered-zero-ui";
        internal const string Driver = "fixture:zero-ui-driver";
        internal const string Danxin = "boundary:danxin-capped-current";
        private const string Jiaozhao = "boundary:jiaozhao-round-current";
        public PackageManifest Manifest { get; } = new("fixture:tiered-zero-ui", "1.0.0", "公开零实体动作界面小夹具");
        public void Register(IContentRegistryBuilder b)
        {
            var assembly = typeof(StandardContentPackage).Assembly;
            using var rs = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-guo-huang-hou.rules.json")!;
            using var ps = assembly.GetManifestResourceStream("CardGame.Content.Standard.SkillPrograms.boundary-guo-huang-hou.presentation.json")!;
            using var rr = new StreamReader(rs); using var pr = new StreamReader(ps);
            var production = SkillProgramCatalog.Load(rr.ReadToEnd(), pr.ReadToEnd());
            foreach (var id in new[] { Jiaozhao, Danxin }) b.AddSkill(new(id, id, "真实当前共享能力") { Program = production.Programs[id] });
            var root = JsonNode.Parse("""
            {"skills":[
              {"id":"fixture:zero-ui-driver","revision":1,"activations":[{"id":"hurt","minCards":0,"maxCards":0,"minTargets":0,"maxTargets":0,"targetKind":"anyLiving","usesPerTurn":null,"effects":[{"op":"damage","target":"owner","amount":1}]}]},
              {"id":"fixture:zero-ui-quiet","revision":1,"triggers":[{"id":"quiet","window":"afterNormalDraw","subject":"owner","optional":false,"effects":[{"op":"skipTurnPhases","target":"owner","phases":["play"]}]}]}
            ]}
            """)!;
            root["schemaVersion"] = SkillProgramCatalog.RulesSchemaVersion;
            var catalog = SkillProgramCatalog.Load(root.ToJsonString(), JsonSerializer.Serialize(new
            {
                schemaVersion = 3, skills = new Dictionary<string, object>
                {
                    [Driver] = new { name = "真实自伤", description = "两次实际伤害取得当前Tier2" },
                    ["fixture:zero-ui-quiet"] = new { name = "安静回合", description = "固定小回合不出牌" }
                }
            }));
            foreach (var id in catalog.Programs.Keys) b.AddSkill(new(id, id, "实际小夹具") { Program = catalog.Programs[id] });
            b.AddSkill(new("fixture:zero-ui-idle", "固定候选", "无运行能力")
            { SelectionWeights = Enum.GetValues<Role>().ToDictionary(role => role, _ => 100d) });
            b.AddGeneral(new("fixture:zero-ui-owner", "界面动作来源", "supporter", Jiaozhao, "wei", 8, [Danxin, Driver], GeneralGender.Female));
            for (var i = 1; i < 4; i++) b.AddGeneral(new($"fixture:zero-ui-target-{i}", "固定公开目标", "supporter",
                "fixture:zero-ui-idle", "qun", 8, ["fixture:zero-ui-quiet"], GeneralGender.Male));
            b.AddDeck(new("fixture:zero-ui-deck", "固定真实装备", 4, 2, [])
            { PhysicalCards = Enumerable.Range(0, 80).Select(i => new ContentDeckPhysicalCard("standard:crossbow", Suit.Spade, i % 13 + 1)).ToArray() });
            b.AddMode(new(Mode, "公开零实体动作", 4, 4, new Dictionary<string, int>
            { [nameof(Role.Lord)] = 1, [nameof(Role.Renegade)] = 3 }, "fixture:zero-ui-deck", GeneralCandidateCount: 4,
                GeneralPoolIds: ["fixture:zero-ui-owner", "fixture:zero-ui-target-1", "fixture:zero-ui-target-2", "fixture:zero-ui-target-3"]));
        }
    }
}
