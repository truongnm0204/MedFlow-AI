# Backend Coding Standards

Applies to `medflow-ai-backend/` — .NET 10, ASP.NET Core Web API, EF Core,
Clean Architecture: `MedFlow.Api` / `MedFlow.Application` / `MedFlow.Domain` /
`MedFlow.Infrastructure`, plus `MedFlow.UnitTests` / `MedFlow.IntegrationTests`.

## 1. Naming

| Thing | Convention | Example |
|---|---|---|
| Classes, methods, properties | PascalCase | `AppointmentService`, `GetByIdAsync` |
| Interfaces | `I` + PascalCase | `IAppointmentRepository` |
| Private fields | `_camelCase` | `_dbContext`, `_logger` |
| Parameters, locals | camelCase | `patientId`, `cancellationToken` |
| Constants | PascalCase | `DefaultPageSize` |
| Async methods | `*Async` suffix | `CreateAsync`, `SendEmailAsync` |
| Files | match primary type | `AppointmentService.cs` |
| DTOs | `*Request` / `*Response` / `*Dto` | `BookAppointmentRequest` |
| Tests | `<Unit>Tests.cs`, `Method_Scenario_Expected` | `CreateAsync_InvalidInput_Throws` |

## 2. Variables & language use

- `var` when the type is obvious from the right side (`var patient = await ...`),
  explicit type when it isn't (`decimal total = ...`).
- `Nullable` is **enabled** — declare intent: `string? notes`, never silent nulls.
  Guard with `ArgumentNullException.ThrowIfNull(x)` at public entry points.
- `record` for DTOs/value objects; `class` for entities/services/controllers.
- `required` modifier for mandatory DTO/entity properties.
- Collection returns: `IReadOnlyList<T>` (never expose `List<T>` publicly).
- `sealed` on classes not designed for inheritance (default for handlers/validators).
- File-scoped namespaces: `namespace MedFlow.Application.Appointments;` — one
  per file, no nested braces.
- `ImplicitUsings` is on — don't re-add covered usings.

```csharp
public sealed record BookAppointmentRequest(
    [property: Required] Guid PatientId,
    [property: Required] Guid DoctorId,
    [property: Required] DateTime ScheduledAt);
```

## 3. Layer rules (Clean Architecture)

```
API            -> controllers, middleware, Program.cs. No business logic.
Application    -> use-cases/services, DTOs, interfaces, validation.
                  Depends only on Domain. No EF Core, no ASP.NET types.
Domain         -> entities, value objects, domain events, repository interfaces.
                  Zero dependencies on other layers or frameworks.
Infrastructure -> EF Core, DbContext, repositories, external services.
                  Implements Application/Domain interfaces.
```

- Controllers stay thin: bind → call application service → map to response.
- EF Core entities/configurations live in `Infrastructure/Persistence` —
  `modelBuilder.ApplyConfigurationsFromAssembly` is already wired; add an
  `IEntityTypeConfiguration<T>` per entity.
- Register services via `DependencyInjection.cs` extension per layer
  (`AddInfrastructure`, `AddApplication`), called from `Program.cs`.

## 4. Templates

### Controller

```csharp
[ApiController]
[Route("api/v1/[controller]")]
public sealed class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _service;

    public AppointmentsController(IAppointmentService service) => _service = service;

    [HttpPost]
    [ProducesResponseType<AppointmentResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AppointmentResponse>> Create(
        [FromBody] BookAppointmentRequest request, CancellationToken ct)
    {
        var result = await _service.CreateAsync(request, ct);
        return CreatedAtAction(nameof(GetById), new { id = result.Id }, result);
    }
}
```

### Application service

```csharp
public sealed class AppointmentService : IAppointmentService
{
    private readonly IAppointmentRepository _repo;
    private readonly IUnitOfWork _uow;

    public AppointmentService(IAppointmentRepository repo, IUnitOfWork uow)
        => (_repo, _uow) = (repo, uow);

    public async Task<AppointmentResponse> CreateAsync(
        BookAppointmentRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // domain rules ...
        var entity = Appointment.Create(request.PatientId, request.DoctorId, request.ScheduledAt);
        await _repo.AddAsync(entity, ct);
        await _uow.SaveChangesAsync(ct);
        return AppointmentResponse.From(entity);
    }
}
```

### Entity (Domain)

```csharp
public sealed class Appointment
{
    public Guid Id { get; private set; }
    public Guid PatientId { get; private set; }
    public AppointmentStatus Status { get; private set; }

    private Appointment() { } // EF Core

    public static Appointment Create(Guid patientId, Guid doctorId, DateTime scheduledAt)
        => new() { Id = Guid.NewGuid(), PatientId = patientId, /* ... */ };
}
```

### Unit test

```csharp
public class AppointmentServiceTests
{
    [Fact]
    public async Task CreateAsync_PastDate_ThrowsValidation()
    {
        // arrange / act / assert
    }
}
```

## 5. Async & error handling

- Every I/O method is async end-to-end; accept `CancellationToken ct = default`
  and pass it through (EF calls, HTTP clients).
- No `async void` (except event handlers), no `.Result`/`.Wait()`.
- Domain/business errors → typed exceptions (`DomainException`,
  `NotFoundException`, `ValidationException`) mapped to HTTP status codes by a
  single middleware in `MedFlow.API/Middleware` — never return 500 for
  validation failures.
- Wrap external calls with try/catch; log with `ILogger<T>` and rethrow or
  translate — don't swallow.

## 6. API conventions

- Routes: `api/v1/<resource>` plural, kebab-case for multi-word
  (`api/v1/patient-records`).
- Versioning prefix is fixed at `v1` (matches frontend `apiClient` baseURL).
- Response shape: DTOs only — never leak EF entities across the boundary.
- Problem-details (`RFC 7807`) for error responses via the middleware.
- AuthN/Z via `[Authorize]` / policies — RBAC roles come from the auth module
  (`role:*` labels on issues map to these).

## 7. Data & migrations

- All persistence through `MedFlowDbContext` — no raw `SqlConnection` in
  application code.
- EF migrations: `dotnet ef migrations add <Name>` in `Infrastructure`;
  descriptive names (`Add_Appointment_Slot_Index`).
- Seed/config data via `IEntityTypeConfiguration` `HasData` or a dedicated
  seeder — never in controllers.

## 8. Quality gates

- `dotnet build` clean — warnings treated as real problems (fix or justify).
- `dotnet test` must pass (UnitTests + IntegrationTests) before push.
- New public API → integration test; new business rule → unit test.
- Files under ~200 lines; one type per file; extract when it grows.
