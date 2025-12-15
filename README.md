# Learnit Server (ASP.NET Core + EF Core)

Backend API for the Learnit SPA. Targets .NET 9, uses PostgreSQL via Entity Framework Core, and secures every controller with JWT auth. All data is scoped per user (`sub` claim) and date/time values are stored/returned in UTC.

## Architecture

- Hosting: `Program.cs` registers DbContext (Npgsql), controllers, CORS for local Vite origins, scoped services (`JwtService`, `AiContextBuilder`, `FriendService`), and JWT bearer auth (issuer, audience, signing key). HTTPS dev certs are expected for local runs.
- Auth: `[Authorize]` on controllers; `GetUserId()` pulls `sub`/`NameIdentifier` claim and guards every handler. Tokens are stateless JWTs.
- Data model: Users, Courses, CourseModules (one level of `CourseSubModules`), ExternalLinks, ScheduleEvents, StudySessions, ActivityLogs. Modules keep `Order`; progress is derived from `EstimatedHours` and `IsCompleted`. All timestamps are stored/returned as UTC.
- AI: `OpenAiProvider` prefers Groq keys, falls back to OpenAI, else returns stub text (quota/missing key). `AiContextBuilder` summarizes user courses and recent hours for prompts.
- Friends: `FriendService` is in-memory per user; stats recompute from CourseModules and StudySessions. Restart clears friend lists.

## Feature surface (with mechanics)

- Auth (`/api/auth`): register/login (returns JWT), logout is a no-op server-side (client drops token). Passwords hashed via `PasswordHasher`.
- Courses (`/api/courses`):

  - GET with search/priority/difficulty/duration filters and sort; includes modules/submodules and computed progress (scheduled/completed hours, hours remaining) aggregated from ScheduleEvents and StudySessions.
  - GET `{id}` loads modules, submodules, external links, and active session.
  - POST creates course + modules + submodules + external links (order preserved) then recomputes `HoursRemaining` from module hours.
  - PUT `{id}` accepts partial field updates; `targetCompletionDate` normalized to UTC.
  - PUT `{id}/edit` replaces all modules/submodules/links with new payload.
  - DELETE cascades modules, submodules, and related scheduled events.
  - Module/submodule ops: create (`POST /{courseId}/modules`), update (`PUT /modules/{id}`), toggle completion (`PATCH /modules/{id}/toggle-completion`).
  - External links: add/update/delete under `/api/courses/*/external-links`.
  - Study sessions: start/stop/list under `/api/courses/{courseId}/sessions`; stopping writes ActivityLog and updates `LastStudiedAt` and `HoursRemaining`.
  - Active time: `/api/courses/{courseId}/active-time` updates an in-flight session and hours remaining.
  - Stats: `/api/courses/stats` returns counts/weekly focus summary.

- Schedule (`/api/schedule`):

  - CRUD events scoped by `UserId`; optional `from/to` query filters.
  - Auto-schedule (`POST /auto-schedule`) takes preferred hours, weekend flag, buffer minutes, daily/weekly caps, timezone offset, course priority order, and difficulty-aware block sizing. Ensures no overlaps, respects lunch gap, and generates events linked to modules.
  - Link/unlink modules (`POST /{eventId}/link-module/{moduleId}`, `DELETE /{eventId}/unlink-module`) renames titles when linked.
  - Reset (`DELETE /reset`) clears all events for the user.
  - Available modules (`GET /available-modules`) lists unscheduled modules.

- Progress (`/api/progress/dashboard`): Computes weekly scheduled vs completed hours (7-day window), current/longest streak, efficiency, per-course progress, and a 60-day activity heatmap from StudySessions.

- Profile (`/api/profile`): get profile + preferences; update name/email with uniqueness check; update preferences (study speed, max session minutes, weekly limit, dark mode); change password with current-password verification and length check.

- Friends (`/api/friends`): add/list/delete friends by email; stats (completion rate, weekly hours) recomputed on retrieval via CourseModules and StudySessions.

- AI (`/api/ai/*`):
  - `chat`: concise action-oriented bullets using context from `AiContextBuilder`.
  - `create-course`: strict JSON schema; repairs malformed JSON; heuristics fallback when parsing fails or AI is stubbed.
  - `schedule-insights` and `progress-insights`: short suggestion bullets using contextual prompt.
  - `compare`: benchmarks user vs selected friends (max 2) and returns narrative suggestions.

## Configuration

Set `appsettings.Development.json` or user secrets (`UserSecretsId=Learnit.Server-development`):

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Database=learnit;Username=postgres;Password=yourpassword"
  },
  "Jwt": {
    "Key": "<strong-secret>",
    "Issuer": "learnit",
    "Audience": "learnit"
  },
  "Groq": {
    "ApiKey": "<your-groq-key>",
    "Model": "llama-3.1-8b-instant"
  },
  "OpenAi": {
    "ApiKey": "<optional-openai-key>",
    "Model": "gpt-4o-mini"
  }
}
```

Env var fallbacks: `Groq:ApiKey`/`GROQ_API_KEY`, `OpenAi:ApiKey`/`OPENAI_API_KEY`.

## Local setup

1. Restore/build: `dotnet restore && dotnet build`
2. Apply migrations: `dotnet ef database update`
3. Run API (repo root or `Learnit.Server/`):

```
dotnet run --project Learnit.Server/Server.csproj
```

API surface is under `/api/*`; the Vite client proxies over HTTPS.

## Operational notes

- All handlers enforce user scoping; unauthorized tokens return 401.
- UTC everywhere; `ScheduleController` normalizes inputs and uses timezone offsets for auto-schedule calculations.
- AI endpoints log raw create-course replies; heuristics/stub kick in when keys are missing or quota is hit.
- FriendService data is transient; restart clears friend lists.

## Troubleshooting

- DB connectivity: verify connection string and run migrations if schema mismatches occur.
- HTTPS/CORS: trust dev certs (`dotnet dev-certs https --trust`) and ensure Vite dev origins match `AllowFrontend` in `Program.cs`.
- Auth: mismatched issuer/audience/key yields 401; client must send `Authorization: Bearer`.
- AI: missing/expired keys produce stub responses; rate limits fall back to stub instead of erroring.
