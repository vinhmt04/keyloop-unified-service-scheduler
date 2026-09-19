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
- **Input**: Customer ID, vehicle details, service type, dealership, desired time
- **Output**: Confirmed appointment with assigned resources, or availability conflict error
- **Validation**: Request completeness, service type existence, dealership existence

**GET /api/appointments/{appointmentId}**
- **Purpose**: Retrieve appointment details
- **Output**: Complete appointment information including resource assignments

### Core Services

**AppointmentService**
- Orchestrates complete appointment booking workflow
- Coordinates availability validation and resource assignment
- Manages transactional boundary for booking operations

**AvailabilityService**
- Validates service bay availability for time slot
- Validates technician availability and qualification match
- Implements resource locking strategy during availability checks

**ResourceAssignmentService**
- Selects specific service bay and technician for confirmed appointments
- Ensures resource assignments are recorded atomically

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
- **Rationale**: Type-safe database operations, strong migration support, excellent testability with InMemory provider
- **Benefits**: Reduces SQL injection risks, simplifies transaction management, enables clean unit testing

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

### Validation Errors (400 Bad Request)

**Request Completeness Validation:**
- Missing required fields (customerId, vehicleVin, serviceTypeId, dealershipId, desiredTime)
- Invalid data formats (non-numeric IDs, invalid datetime formats)
- Invalid references (non-existent serviceTypeId, non-existent dealershipId)

**Business Rule Validation:**
- Service type not offered at requested dealership

### Resource Conflict Errors (409 Conflict)

**Availability Conflicts:**
- No service bay available for requested time slot
- No qualified technician available for requested time slot
- Combined resource unavailability (both constraints failed)

**Error Response Format:**
```json
{
  "error": "ResourceUnavailable",
  "message": "No qualified technician available for requested time slot",
  "details": {
    "serviceType": "Oil Change",
    "requestedTime": "2024-01-15T10:00:00Z",
    "availableSlots": []
  }
}
```

### System Errors (500 Internal Server Error)

**Database Failures:**
- Connection timeouts during resource locking
- Transaction deadlocks (should be rare due to consistent lock ordering)
- Constraint violation errors (data integrity issues)

**Resilience Patterns:**
- Transaction rollback on any failure during booking process
- Structured error logging for debugging and monitoring

## Testing Strategy

### Integration Testing Focus

**PostgreSQL Integration Tests**
- Test actual database concurrency behavior with Testcontainers
- Verify transaction isolation and locking mechanisms work correctly
- Validate constraint enforcement and data integrity

**Business Logic Testing**
- Test appointment booking workflow with various scenarios
- Verify resource availability calculations with overlapping appointments
- Test error conditions and edge cases (no resources, qualification mismatches)

**ASP.NET Core Integration**
- Test complete HTTP request/response cycles
- Verify request validation and error response formats
- Test API contract adherence with OpenAPI specifications

### Unit Testing Approach

**Service Layer Tests**
- Mock database dependencies to test business logic in isolation
- Focus on resource selection algorithms and validation rules
- Test error handling paths and edge conditions

**Data Access Layer Tests**
- Use Entity Framework InMemory provider for repository pattern testing
- Verify query correctness and data mapping
- Test transaction boundary management

### Test Scenarios

**Concurrency Test Cases**
- Multiple concurrent requests for same time slot (expect one success, others fail)
- Concurrent requests for different non-overlapping slots (expect all succeed)
- Resource lock timeout scenarios

**Business Logic Test Cases**
- Valid appointment request with available resources (expect success)
- Request with no available service bays (expect conflict error)
- Request with no qualified technicians (expect conflict error)
- Request with invalid service type or dealership (expect validation error)

## Observability

### Structured Logging

**Key Log Events:**
- Appointment request received (with customerId, serviceType, dealership, requestedTime)
- Resource availability check initiated (with time slot and resource counts)
- Resource locking attempted (with resource IDs and lock success/failure)
- Appointment confirmed (with appointmentId and assigned resources)
- Booking conflicts detected (with conflict reasons and available alternatives)

**Log Format Example:**
```json
{
  "timestamp": "2024-01-15T10:30:00Z",
  "level": "INFO",
  "event": "AppointmentConfirmed",
  "appointmentId": 12345,
  "customerId": 67890,
  "serviceType": "Oil Change",
  "dealershipId": 42,
  "serviceBayId": 101,
  "technicianId": 201,
  "startTime": "2024-01-16T09:00:00Z",
  "endTime": "2024-01-16T10:00:00Z"
}
```

### Key Metrics

**Business Metrics:**
- Appointment confirmation rate (successful bookings / total requests)
- Resource utilization rates (service bay and technician occupancy)
- Average booking response time

**Technical Metrics:**
- Database transaction duration and success rate
- Concurrent request handling capacity
- Resource lock contention frequency


## GenAI Design-Phase Collaboration

- AI produced the initial requirements and design proposals.
- Human review identified unsupported business assumptions and removed unnecessary scope.
- Human review challenged over-engineering in the initial design, including unnecessary operational mechanisms and over-prescriptive testing.
- Human review identified that the concurrency design needed more precise reasoning about resource locking and availability rechecking.
- AI revised the concurrency flow to: identify candidates -> lock selected resources -> recheck availability while locks are held -> insert appointment -> commit.
- Human selected C# / ASP.NET Core as the implementation technology because of familiarity with the stack, enabling stronger review and ownership of AI-generated code.
- A later AI revision reintroduced unnecessary complexity; human review detected the regression and requested a minimal correction rather than accepting it.
- Human performed final verification and retains ownership of the resulting design decisions.