# Design Document: Unified Service Scheduler

## Overview

The Unified Service Scheduler is a resource-constrained booking system that enables dealership customers to request service appointments. The system validates availability of both service bays and qualified technicians before confirming appointments, ensuring no double-booking occurs.

The core workflow follows three steps:
1. **Request Processing**: Accept customer appointment requests with vehicle, service type, dealership, and desired time
2. **Availability Validation**: Verify both service bay and qualified technician availability for the required duration
3. **Appointment Confirmation**: Create persistent appointment records with resource assignments when availability is confirmed

## Architecture

The system implements a **layered monolith** architecture optimized for transaction consistency and resource coordination:

```
┌─────────────────────────────────────────┐
│              REST API Layer             │
├─────────────────────────────────────────┤
│             Business Logic              │
│    - Appointment Processing Service     │
│    - Availability Validation Service    │
│    - Resource Assignment Service        │
├─────────────────────────────────────────┤
│             Data Access Layer           │
│         (Entity Framework Core)         │
├─────────────────────────────────────────┤
│            PostgreSQL Database          │
└─────────────────────────────────────────┘
```

### Architectural Decisions

**Layered Monolith Rationale:**
- Transaction consistency across resource allocation is critical for booking integrity
- Resource contention requires coordinated locking across service bays and technicians
- Appointment confirmation involves multiple entities that must be updated atomically
- Single deployment simplifies resource coordination and reduces distributed transaction complexity

**Transactional Booking Approach:**
All appointment operations occur within database transactions to ensure:
- Atomic resource allocation (both service bay and technician reserved together)
- Consistent availability state during concurrent requests
- Prevention of double-booking through proper isolation levels

## Components and Interfaces

### REST API Endpoints

**POST /api/appointments**
- **Purpose**: Request new service appointment
- **Input**: Customer ID, vehicle VIN, service type, dealership, and desired start time
- **Output**: Confirmed appointment with assigned resources, or a validation/resource conflict response
- **Validation**: Request validity and service type existence

**GET /api/appointments/{appointmentId}**
- **Purpose**: Retrieve appointment details
- **Output**: Complete appointment information including resource assignments

### Core Services

**AppointmentService**
- Orchestrates complete appointment booking workflow
- Coordinates availability validation and resource assignment
- Manages transactional boundary for booking operations

**AvailabilityService**
- Validates service bay availability for a time slot
- Validates technician availability and qualification match

**ResourceAssignmentService**
- Selects a specific service bay and qualified technician
- Locks the selected service bay first and technician second using PostgreSQL row locks
- Rechecks availability while the locks are held before returning the assignment

### External Dependencies

**PostgreSQL Database**
- Primary data store for appointments, resources, and availability
- Provides ACID transaction support for resource coordination
- Supports row-level locking for concurrency control

## Data Models

### Core Entities

```csharp
public class Appointment
{
    public int AppointmentId { get; set; }
    public int CustomerId { get; set; }
    public string VehicleVin { get; set; }
    public int ServiceTypeId { get; set; }
    public int DealershipId { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int ServiceBayId { get; set; }
    public int TechnicianId { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ServiceBay
{
    public int ServiceBayId { get; set; }
    public int DealershipId { get; set; }
    public string BayNumber { get; set; }
    public bool IsActive { get; set; }
}

public class Technician
{
    public int TechnicianId { get; set; }
    public int DealershipId { get; set; }
    public string Name { get; set; }
    public List<TechnicianQualification> Qualifications { get; set; }
    public bool IsActive { get; set; }
}

public class ServiceType
{
    public int ServiceTypeId { get; set; }
    public string Name { get; set; }
    public TimeSpan Duration { get; set; }
}
```

### Resource Availability Model

**Time Interval Logic:**
- Uses half-open intervals [start, end) for precise availability calculations
- Appointment from 09:00-10:00 occupies [09:00, 10:00), making resources available again at exactly 10:00
- Prevents ambiguity in boundary conditions and enables precise scheduling

**Availability Query Pattern:**
```sql
-- Check if resource is available for requested [requestStart, requestEnd)
SELECT COUNT(*) FROM appointments 
WHERE resource_id = @resourceId 
  AND start_time < @requestEnd 
  AND end_time > @requestStart
-- Returns 0 if available, >0 if conflicted
```

## Concurrency Control Strategy

### Transaction Isolation and Locking

**Isolation Level**: READ COMMITTED with SELECT FOR UPDATE
- Prevents dirty reads while allowing concurrent read operations
- Explicit row-level locking during resource selection ensures consistency
- Balances concurrency with consistency requirements

**Resource Locking Order:**
1. Lock service bays first (by ServiceBayId ascending)
2. Lock technicians second (by TechnicianId ascending) 
3. Consistent ordering prevents deadlock scenarios

### Detailed Transaction Flow

```
BEGIN TRANSACTION (READ COMMITTED)

1. IDENTIFY CANDIDATES
   - Query available service bays (no explicit lock)
   - Query available qualified technicians (no explicit lock)
   - If no candidates found, ROLLBACK and return conflict error

2. LOCK RESOURCES IN ORDER
   - SELECT * FROM ServiceBays WHERE ServiceBayId = @selectedBayId FOR UPDATE
   - SELECT * FROM Technicians WHERE TechnicianId = @selectedTechnicianId FOR UPDATE

3. RECHECK AVAILABILITY WHILE LOCKED
   - Verify service bay still available for time slot
   - Verify technician still available for time slot  
   - If either unavailable, ROLLBACK and return conflict error

4. INSERT APPOINTMENT
   - INSERT INTO Appointments (all appointment details)
   - Record service bay and technician assignments

5. COMMIT TRANSACTION
```

**Concurrency Behavior:**
- Concurrent requests for same resources will serialize at step 2 (resource locking)
- First transaction to acquire locks will succeed if resources remain available
- Subsequent transactions will find resources unavailable during step 3 recheck
- Failed transactions rollback cleanly without data corruption

## Technology Stack

### Core Technologies

**C# and ASP.NET Core**
- **Rationale**: Strong typing reduces booking errors, mature ecosystem, excellent Entity Framework integration
- **Benefits**: Built-in dependency injection, comprehensive middleware pipeline, robust async/await support

**PostgreSQL**
- **Rationale**: ACID compliance essential for booking integrity, excellent concurrency control, proven scalability
- **Benefits**: Row-level locking, reliable READ COMMITTED isolation, JSON support for flexible data

**Entity Framework Core**
- **Rationale**: Type-safe database operations, strong PostgreSQL integration, and migration support
- **Benefits**: Simplifies persistence and transaction management while allowing database behavior to be verified against real PostgreSQL through Testcontainers

### Development and Testing

**xUnit with Testcontainers**
- **Rationale**: Industry standard for .NET testing, Testcontainers enables realistic PostgreSQL integration tests
- **Benefits**: True integration testing against actual database behavior, isolated test environments

**OpenAPI (Swagger)**
- **Rationale**: Automatic API documentation, client SDK generation, interactive testing interface
- **Benefits**: Reduces integration friction, ensures API contract clarity, enables frontend development

## Correctness Properties

*These properties provide formal specifications that guide testing and validation efforts, ensuring the system behaves correctly across all scenarios.*

### Property 1: Resource Exclusivity
For any confirmed appointment, no other appointment can exist that uses the same service bay or technician for any overlapping time period.
**Validates: Requirements 2.1, 2.2, 3.3, 3.4**

### Property 2: Appointment Completeness
For any confirmed appointment, the system must have verified and recorded assignments for exactly one service bay and one qualified technician for the entire service duration.
**Validates: Requirements 2.1, 2.2, 3.1, 3.2**

### Property 3: Resource Qualification
For any confirmed appointment, the assigned technician must possess qualifications for the requested service type.
**Validates: Requirements 2.2, 3.2**

### Property 4: Temporal Consistency
For any confirmed appointment, the end time must equal the start time plus the service type's predefined duration.
**Validates: Requirements 1.2, 1.3, 3.5**

### Property 5: Atomic Resource Assignment
For any appointment creation attempt, either both service bay and technician are assigned and the appointment is confirmed, or neither resource is assigned and the request fails.
**Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**

## Error Handling

### Expected Request and Business Outcomes

- `400 Bad Request` for invalid booking requests or a service type that does not exist.
- `409 Conflict` when no suitable service bay or qualified technician is available for the requested interval.
- `201 Created` when the appointment is successfully persisted with both resource assignments.
- `404 Not Found` when the requested appointment does not exist.

Expected booking failures are represented by typed service-layer error classifications so the API does not depend on parsing error-message text.

### Technical Failures

Unexpected database, locking, or infrastructure exceptions are not converted into normal booking outcomes by the service layer. The active transaction is disposed/rolled back when the booking cannot complete, and technical failures propagate to the API boundary for framework-level handling and logging.

## Testing Strategy

### PostgreSQL Integration Testing

The automated suite uses xUnit with PostgreSQL Testcontainers so availability queries, transactions, row locking, and persistence are exercised against the same database engine used by the application.

The suite covers:
- Complete appointment booking and persistence workflows
- Service bay and technician availability, including half-open interval boundaries
- Technician qualification matching
- Expected booking failures such as unavailable resources and missing service types
- Transactional resource assignment behavior

### Concurrency Testing

Focused concurrent booking tests verify the persistence invariant rather than relying on timing assumptions:
- Concurrent requests for the same time slot cannot persist overlapping appointments that share a service bay or technician
- Overlapping requests preserve resource exclusivity
- Non-overlapping requests can succeed concurrently

The HTTP booking flow is additionally verified manually through Swagger against the local PostgreSQL database. Automated HTTP end-to-end tests and EF Core InMemory tests are intentionally not part of the final suite.

## Observability

### Implemented

- Structured application logging is used for key booking operations and outcomes.
- ASP.NET Core logging provides contextual properties that can be consumed by the configured logging provider.
- A lightweight `/health` endpoint reports basic application health. It does not currently perform a database readiness check.

### Future Operational Considerations

For a production deployment, useful additions would include metrics such as booking success/conflict rate, response time, resource utilization, transaction duration, and lock contention. Database readiness checks and distributed tracing could also be added if operational requirements justify them. These are design considerations, not features implemented in this assessment.


## GenAI Design-Phase Collaboration

- AI produced the initial requirements and design proposals.
- Human review identified unsupported business assumptions and removed unnecessary scope.
- Human review challenged over-engineering in the initial design, including unnecessary operational mechanisms and over-prescriptive testing.
- Human review identified that the concurrency design needed more precise reasoning about resource locking and availability rechecking.
- AI revised the concurrency flow to: identify candidates -> lock selected resources -> recheck availability while locks are held -> insert appointment -> commit.
- Human selected C# / ASP.NET Core as the implementation technology because of familiarity with the stack, enabling stronger review and ownership of AI-generated code.
- A later AI revision reintroduced unnecessary complexity; human review detected the regression and requested a minimal correction rather than accepting it.
- Human performed final verification and retains ownership of the resulting design decisions.