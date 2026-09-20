# Unified Service Scheduler

A .NET 8 ASP.NET Core Web API for scheduling automotive service appointments.

The scheduler checks the availability of both a service bay and a qualified technician for the requested time period, then creates the appointment transactionally to prevent resource conflicts and double-booking.

## Prerequisites

- .NET 8 SDK
- Docker Desktop
- `dotnet-ef` CLI tool
- Git

## Local Setup

### 1. Start PostgreSQL

```bash
docker run --name keyloop-postgres \
  -e POSTGRES_DB=unified_service_scheduler_dev \
  -e POSTGRES_USER=scheduler_dev_user \
  -e POSTGRES_PASSWORD=scheduler_dev_password \
  -p 5432:5432 \
  -d postgres:16
```

For subsequent runs:

```bash
docker start keyloop-postgres
```

### 2. Apply Database Migrations

From the repository root:

```bash
dotnet ef database update --project UnifiedServiceScheduler
```

This applies the existing EF Core migrations and creates the required schema and seed data.

## Build

```bash
dotnet build
```

## Run

```bash
dotnet run --project UnifiedServiceScheduler
```

Using the default HTTP launch profile, the API runs at:

```text
http://localhost:5246
```

Swagger UI:

```text
http://localhost:5246/swagger
```

## Test

Docker Desktop must be running because the integration and concurrency tests use PostgreSQL through Testcontainers.

Run the complete automated test suite:

```bash
dotnet test
```

The automated test suite covers core scheduling logic, PostgreSQL integration, resource availability, appointment workflows, and concurrent booking behavior.

## API

### Create an Appointment

`POST /api/Appointments`

Example:

```json
{
  "customerId": 1001,
  "vehicleVin": "TESTVIN123456789",
  "serviceTypeId": 1,
  "dealershipId": 1,
  "startTime": "2026-09-21T10:00:00Z"
}
```

A successful request returns `201 Created` with the persisted appointment and its assigned service bay and technician.

If the required resources are unavailable for the requested interval, the API returns `409 Conflict`.

### Retrieve an Appointment

`GET /api/Appointments/{appointmentId}`

Returns the persisted appointment or `404 Not Found` when the appointment does not exist.

## Technical Overview

- ASP.NET Core 8 Web API
- PostgreSQL with Entity Framework Core
- Layered monolith
- Transactional resource assignment
- PostgreSQL row locking (`SELECT FOR UPDATE`)
- Deterministic lock ordering: service bay before technician
- Half-open time intervals for overlap detection
- xUnit with PostgreSQL Testcontainers
- Swagger / OpenAPI

## AI Collaboration Narrative

I used GenAI as an engineering collaborator throughout requirements analysis, system design, implementation, testing, and final verification. My approach was to give the AI clear goals and boundaries, let it propose solutions, and then validate its output against the approved requirements, architecture, source code, and actual test results before accepting changes.

Several iterations required active human correction:

- **Requirements discipline:** The AI initially introduced business rules that were not supported by the assessment. I challenged those assumptions and removed them, keeping the implementation aligned with the stated requirements and documenting only necessary assumptions.

- **Concurrency correctness:** During resource-assignment implementation, the AI introduced a fallback path that bypassed PostgreSQL row locking. Although the generated tests passed, I rejected the change because it weakened the concurrency model. The final implementation uses a caller-owned transaction, deterministic lock ordering, `SELECT FOR UPDATE`, and an availability recheck while the locks are held.

- **API contract design:** The AI initially classified booking conflicts by parsing error-message text and also introduced an unrequested future-time validation rule. I rejected both approaches. The service contract was refined to use typed error classifications so the API can map expected outcomes to HTTP responses without depending on message wording.

- **Testing strategy:** Some AI-generated integration and concurrency tests were large, nondeterministic, or bypassed the designed transaction boundary. I reduced them to focused tests using real PostgreSQL through Testcontainers and verified the important invariant: concurrent bookings must never persist overlapping appointments that share the same service bay or technician.

- **Final verification:** A broad verification task caused the AI to expand the solution with additional packages, middleware, and a failing automated HTTP test suite. I stopped the iteration, reviewed the Git diff, rejected the speculative changes, and split verification into smaller stages: application pipeline verification, manual HTTP verification through Swagger, and the existing automated test suite.

The final solution therefore reflects AI-assisted implementation rather than unchecked AI-generated code. I treated generated output as a proposal that required source review, build/test evidence, and engineering judgment before it became part of the solution.