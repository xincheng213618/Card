# Independent static review of 726 zero-material UI delivery

Input: `guo-huang-hou-ui/delivery-manifest.json` SHA `2a7a37d4f9becec9233b266ca7322603a4a0d1c5f2b08dc40297901a17171fa6`. The author's original frozen bytes, main files and all Core original/followup bytes remain unchanged by this review. No compiler, loader, test, UI, native AI, benchmark or network operation was performed.

## Confirmed P2

`tests/CardGame.Wpf.Tests/TieredRoundZeroUseUiChecks.cs:40–41` uses `CardZone.Hand` and `CardZone.Processing`. The current Core type is `CardZoneKind` (`src/CardGame.Core/CardZones.cs:7`); the named WPF/test projects and linked Shared sources have no `CardZone` alias/type. This retained new check cannot compile with those identifiers. Minimal independent correction: use `CardZoneKind.Hand` and `CardZoneKind.Processing`. Root and Sun were notified; the original delivery must stay frozen and any effective correction be independently identified. This conclusion is from source, not a compiler run.

## Reviewed consumer boundaries

- NEW `MainViewModel.TieredRoundZeroActions.cs:11–23`: only a currently published `CardId == 0` action with the explicit new policy marker enters this path. Selection/confirmation re-resolves kind, original target order, target-card selection, primary source, policy and ordered additional sources against current view legal actions. It does not use an arbitrary card-zero sentinel as permission.
- NEW `:31–85`: the draft requires zero selected materials and the exact public target set, then submits the original action's target order through existing `CreatePlayCommand`. The factory at current `MainViewModel.cs:1739–1751` retains played kind, target-card selector and every conversion source in a real `PlayCardCommand`. No owned-card-zero lookup, `CardViewModel(0)` or `UseProgramSkill` substitution occurs.
- Preview `MainViewModel.Presentation.cs:357–410` and unchanged `TableSurface.cs:16–27`: marked zero actions enter the retained supplemental buttons; nullable-marker old actions retain their prior active/entity paths. Exact action targets supply max/selectable counts without changing the Core `LegalAction` or adding UI-generated targets.
- Preview `MainViewModel.cs:490–530`, `:968–990`, `:1269–1296`, `:1806–1811`, `:1917–1936`, `:1941` and Preview Presentation `:703–715`: prompt/engine replacement, publication loss, cancel, selecting a physical card, selecting another active action and Ending clear the zero draft. The active target picker accepts only the original action's published seats. The existing confirm dispatcher calls the new use branch only when that marked action is selected.
- Existing response choice consumers are unchanged. The NEW route is an actual Play action route, not a replacement for Nullification/Dodge/Dying response prompts.
- Public selection is taken from the human action view. No hidden hand/entity query is added; the draft stores already projected immutable `LegalAction` values. Source/target revalidation occurs before submission. The engine still performs final command legality validation.

Within these named files, no additional confirmed P1/P2 consumer issue was found. This is bounded static review and does not certify visual behavior or all output combinations.

## Check draft scope and execution limits

The single existing-runner method `PublicZeroUseTargetsAndRealCardSubmission` uses production 726 rules/presentation, a four-general fixed seed31 registry, real Crossbow cards and two actual self-damage/optional Danxin choices. It loads actual accepted-journal checkpoints into the ViewModel, first asserts one real physical Equip payment, then cancellation and exact real zero Slash submission with no fake material/card face.

The tested draft selects a single-target Slash. Reversing a one-element target sequence is not evidence of multi-target ordering. That ordering is statically supported by passing the original `action.TargetSeats` to the unchanged command factory. Multiple targets, target-card ordinary tricks, targetless direct submission, refresh/regrant-invalidated drafts and all eighteen output families remain unexecuted. No extra runner or routine-scope change is requested by this review.

## Text/hash evidence

`ui-text-verification.json` records all 2 NEW raw hashes, 4 current-main/baseline raw hashes and both LF conventions, 4 full previews, 8 support files and 15 explicitly named readonly inputs. All 23 patch hunks reconstruct the full previews exactly, with one Update per old file. Numeric unified-diff hunk headers in the frozen patch are metadata; root may normalize only these headers for `apply_patch`, preserving hunk text and original frozen input.

The evidence generator was a data-only selected-file text/hash comparison. It did not compile or execute Card, a loader, any check or the UI.
