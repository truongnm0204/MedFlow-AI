# MedFlow AI — Clinical Workflow and Patient Routing System

Monorepo chứa toàn bộ source code của MedFlow AI.

## Cấu trúc

```
medflow-ai/
├── medflow-ai-frontend/    # React + TypeScript + Vite + Tailwind v4 + Zustand
├── medflow-ai-backend/     # ASP.NET Core Web API (N-Layer) + EF Core + PostgreSQL
├── tests/                  # E2E / Integration tests (Playwright)
├── docs/                   # SRS, SDS, API docs, diagrams
├── infrastructure/         # Docker, deployment configs
└── docker-compose.yml      # PostgreSQL dev environment
```

## Yêu cầu

- Node.js >= 20
- .NET SDK 10
- Docker Desktop (cho PostgreSQL qua docker-compose)

## Chạy local

```bash
# 1. Database
docker compose up -d postgres

# 2. Backend (http://localhost:5000)
cd medflow-ai-backend
dotnet ef database update --project src/MedFlow.Infrastructure --startup-project src/MedFlow.API   # khi có migration
dotnet run --project src/MedFlow.API --urls http://localhost:5000

# 3. Frontend (http://localhost:5173)
cd medflow-ai-frontend
npm install
npm run dev
```

## Kiểm thử

```bash
# Frontend unit/component
cd medflow-ai-frontend && npm test

# Backend unit + integration
cd medflow-ai-backend && dotnet test
```

## Conventions

- API versioned: `/api/v1/...`
- DTO-based contracts — không trả EF Entity trực tiếp.
- PascalCase cho C#, camelCase cho JSON/TS.
- Git workflow: `feature/...` → `develop` → `main`.
