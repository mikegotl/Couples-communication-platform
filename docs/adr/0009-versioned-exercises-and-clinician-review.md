# ADR-0009: Versioned exercises with clinician review metadata

- Status: Accepted
- Date: 2026-11-09

## Context
Milestone 05 adds guided reflection exercises (reflective listening, feelings and needs,
pause before reply, repair attempts). They must be original, reviewable by a clinician, avoid
diagnosis, resume where the user left off, and stay private.

## Decision
- **Content in code, review in data.** Exercise wording lives in `ExerciseCatalog` as
  `ExerciseTemplate` records, so it is reviewed in pull requests. On first use each process
  publishes any missing `(key, version)` into `exercise_definitions` with a SHA-256 content hash
  and status `PendingClinicianReview`. Review status, reviewer, time and notes are recorded by an
  admin and never overwritten by code.
- **Versions are immutable.** A unit test pins each published version's hash. Changing wording
  means adding version N+1. Existing attempts stay on the version they started; new attempts use
  the highest version that is not retired. The admin page flags a stored version whose hash
  differs from code.
- **Approval gate is configurable.** `Exercises:RequireClinicianApproval` (default `false`) limits
  new attempts to approved versions. Until a clinician is engaged, exercises are offered with a
  visible "awaiting review by a licensed clinician" note. Turning the gate on for the pilot is an
  owner decision.
- **Attempts are owner-only.** `ExerciseAttempt` and `ExerciseResponse` carry `OwnerUserId` and
  are queried only through `IExerciseService` with the acting user's id. Answers are saved on every
  step; required steps must be answered before moving forward or finishing. Export includes
  attempts; account deletion erases them.
- **Admin role.** ASP.NET Core Identity roles are enabled; `Admin` is granted only from
  `Administration:AdminEmails` at startup. The admin page shows content metadata, never user data.
  Each review is written to the audit log.

## Consequences
Copy changes require a version bump and a new review. Clinician sign-off is a product process
outside the code; the app records it but cannot verify credentials.
