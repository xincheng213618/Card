# 726 zero-material active action UI draft

This delivery is frozen for static integration only. No compiler, loader, Core or WPF check, native game, UI, benchmark, network or media operation has been run. The original Core author's thirteen NEW files, twenty-two OLD patch and follow-ups remain unchanged.

The four OLD inputs are WPF files. `HumanActiveSkillActions` lives in `MainViewModel.Presentation.cs` in the WPF project; there is no new Core Presentation hook. Its predicate adds only a published `LegalAction` with `CardId == 0` and non-null `TieredRoundZeroUse`. The original `Kind`, target-card selector and conversion chain remain intact.

The existing supplemental active-action ItemsControl displays these public actions through its existing `Description` button. A new ViewModel partial stores a selected immutable public action as a UI draft. Every use, refresh and confirmation resolves it against the current legal view. Exact action targets supply selectable seats and a required count; confirmation uses the original action's target order, even if seats were clicked in a different order. The command is the existing `CreatePlayCommand(..., cardId: 0, ...)`, including played kind, target card, primary and additional conversion sources. No `CardViewModel(0)`, owned-card lookup or `UseProgramSkill` command is created. No protocol or version changes are included.

Cancel, engine replacement, prompt replacement, loss of published legality, selecting a real card and selecting another active action clear the local draft. Null-marker actions retain the existing physical-card and active-skill paths. Self/global zero actions whose public `TargetSeats` are empty submit directly through the same accepted action rather than inventing UI targets.

Response actions reuse existing exact prompt-choice buttons and `AnswerPromptCommand`; this patch changes no response consumer. Read-only audit evidence is separately frozen in `../guo-huang-hou-ui-audit`.

## Statically checked and future execution

The new partial's members, command factory, partial class identity, active action entry, target commands, confirm/cancel controls, SDK implicit compilation and manual save APIs were checked against the named source inputs. The data-only generator checks exact text anchors and preserves each input baseline's raw bytes. Each OLD file has one Update header in the patch. LF previews intentionally omit a possible BOM; the manifest separately records raw-before, BOM-preserving LF-before, BOM-free LF-before, raw-preview-after and BOM-free LF-after.

One existing WPF runner entry points to `TieredRoundZeroUseUiChecks.PublicZeroUseTargetsAndRealCardSubmission`. Its fixed seed31, four-general small registry uses the production current rules. It first selects/submits a real Crossbow through the old hand-card confirm path and checks one real payment. Two actual self-damage/optional Danxin commands then earn tier2. The same UI loads the resulting real checkpoint, exposes a public zero Slash action, opens the exact target draft, cancels without a command, selects again and confirms. The draft asserts the real `PlayCardCommand` identity and exactly one zero-material use issuance with no fake card movement or card face. It is an uncompiled, unexecuted behavior draft, not a passing test or visual acceptance.

Required future checks include this new WPF method and the relevant retained active-skill/entity/response methods. The Core author's zero-use action-value follow-up remains separate; this UI delivery does not claim that all Core target enhancement combinations are already accepted or tested.

## Integration mapping

Copy the two `src/` and `tests/` NEW files to their same repository-relative paths. Apply `ui-wiring.patch` after verifying all four raw input hashes; it registers the single check in the existing WPF runner and needs no new runner switch. The Core author's twenty-two OLD files do not overlap these four WPF inputs. `delivery-manifest.json` is the final authoritative payload list; frozen audit and implementation manifests have independent hashes.
