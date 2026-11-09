# Clinical review checklist (milestone 09)

Milestone 09's gate includes a **manual clinical review**. It has not happened yet: the owner
needs to appoint a licensed clinician (see DECISIONS.md, open decisions). Until it is signed off,
all safety content is marked "Pending licensed clinician review" and the app must not be piloted.

The reviewer should check, and record their name, licence and date against each item:

| Item | Where | What to check |
|------|-------|---------------|
| Safety check questions and options | `src/Couples.Domain/Safety/SafetyContent.cs` | Wording, order, whether "skip" options are right, trauma-informed tone |
| Scoring rules | `SafetyContent.Assess` | Which answers count as "may be unsafe" (holds sharing) or "concern" (support only) |
| Result and support copy | `src/Couples.Application/Safety/SafetyCopy.cs` | Non-coercive, no promise of help the app can't give, no pressure to stay, leave or reconcile |
| Immediate danger and self-harm copy | `SafetyCopy.ImmediateDanger`, `SafetyCopy.SelfHarm` | Accurate and safe signposting |
| Support resources | `Safety:Resources` configuration, `SafetyOptions.BuiltIn` | Jurisdiction-specific services for the launch market (owner decision) |
| Safety mode and the silent hold | ADR-0012 | Whether holding the user's own sharing, and not telling the partner, is the right default |
| Quick exit | `QuickExit.razor`, `quick-exit.js`, `/support` | Behaviour and limits explained honestly |
| Coach safety handling | `CoachPrompt`, `DraftPage` support link | What the user sees when the coach flags a safety concern |
| Exercise content | `/admin/exercises` | Already tracked per exercise (ADR-0009) |

After review, bump `SafetyContent.Version`, update `ReviewStatus` and the pinned hash test, and
record the sign-off in PROGRESS.md.
