# Learnit Server (ASP.NET Core)

Backend API for Learnit. Provides auth, courses, scheduling, progress, profile, friends, and AI helpers. Targets .NET 9 and uses PostgreSQL via Entity Framework Core.

## Features

- Auth: JWT issuance/validation, account endpoints.
- Courses: courses/modules CRUD, module ordering, notes.
- Scheduling: manual events, auto-schedule modules into workdays, link/unlink events to modules.
- Progress: activity logs and progress summaries.
- Profile & friends: profile fields and friend list/compare support.
- AI helpers: chat, course draft generation, schedule insights, progress insights, friend compare; works with Groq/OpenAI via `OpenAiProvider`.

## Prerequisites

- .NET SDK 9.0+
- PostgreSQL instance and connection string
- (Optional) `dotnet-ef` CLI for migrations: `dotnet tool install --global dotnet-ef`

## Configuration

Create/update `appsettings.Development.json` or user secrets (`UserSecretsId=Learnit.Server-development`):

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=learnit;Username=postgres;Password=yourpassword"
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

Environment variable fallbacks: `Groq:ApiKey` or `GROQ_API_KEY`; `OpenAi:ApiKey` or `OPENAI_API_KEY`.

## Database

1. Restore/build: `dotnet restore && dotnet build`
2. Apply migrations: `dotnet ef database update`

## Run

```bash
# from repo root or Learnit.Server/
dotnet run --project Learnit.Server/Server.csproj
```

The SPA proxy is configured to point to the Vite client; API surface is under `/api/*` (e.g., `/api/auth`, `/api/courses`, `/api/schedule`).

## Notable endpoints (summary)

- `POST /api/auth/login`, `POST /api/auth/register`
- `GET /api/courses`, CRUD on `/api/courses/{id}`, modules nested
- `GET/POST/PUT/DELETE /api/schedule` for events; `POST /api/schedule/auto-schedule` for workday auto placement; `GET /api/schedule/available-modules` to list unscheduled modules
- `GET /api/progress` summaries
- `GET/PUT /api/profile`
- `GET/POST/DELETE /api/friends`
- `POST /api/ai/*` for chat, course draft, schedule insights, progress insights, friend compare

## Troubleshooting

- Ensure PostgreSQL is reachable from the configured connection string.
- If HTTPS dev cert issues arise, run `dotnet dev-certs https --trust`.
- AI requests return stub responses if no API key is configured.
- Auth failures: verify JWT key/issuer/audience match between server and client.
