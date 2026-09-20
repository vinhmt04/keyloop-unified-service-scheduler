# Implementation Plan: Unified Service Scheduler

## Overview

This implementation plan builds the Unified Service Scheduler as a C# ASP.NET Core backend with PostgreSQL persistence. The approach follows the approved layered monolith architecture, implementing transactional resource booking with proper concurrency control. Each major milestone includes verification steps to ensure correctness before proceeding.

## Tasks

- [x] 1. Set up project structure and database foundation
  - [x] 1.1 Create ASP.NET Core project with PostgreSQL and EF Core
    - Initialize new ASP.NET Core Web API project
    - Add Entity Framework Core PostgreSQL provider
    - Configure connection string and database context
    - Add xUnit testing framework and Testcontainers.PostgreSql
    - _Requirements: 3.5 (persistence infrastructure)_

  - [x] 1.2 Create database schema and initial migration
    - Define Entity Framework models for Appointment, ServiceBay, Technician, ServiceType entities
    - Create TechnicianQualification junction table
    - Generate and apply initial database migration
    - Include seed data for service types and basic test data
    - _Requirements: 1.2, 2.1, 2.2, 3.1, 3.2_

- [x] 2. Implement core domain models and services
  - [x] 2.1 Implement appointment time interval logic
    - Create time slot validation methods using half-open intervals [start, end)
    - Implement appointment duration calculation from service types
    - _Requirements: 1.2, 1.3_

  - [x] 2.3 Implement AvailabilityService for resource checking
    - Create service bay availability checking with interval overlap detection
    - Create technician availability checking with qualification matching
    - Implement SQL queries for efficient availability checking
    - _Requirements: 2.1, 2.2, 2.3, 2.4_

  - [ ]* 2.4 Write unit tests for AvailabilityService
    - Test overlapping appointment detection
    - Test technician qualification matching
    - Test edge cases (boundary conditions, empty availability)
    - _Requirements: 2.1, 2.2_

- [x] 3. Checkpoint - Verify core scheduling logic
  - Ensure all tests pass, ask the user if questions arise.

- [x] 4. Implement transactional booking workflow
  - [x] 4.1 Implement ResourceAssignmentService with proper locking
    - Create candidate resource identification logic
    - Implement resource locking in consistent order (service bay first, then technician)
    - Add availability rechecking while resources are locked
    - _Requirements: 2.1, 2.2, 3.1, 3.2, 3.3, 3.4_

  - [x] 4.3 Implement AppointmentService with complete transaction flow
    - Orchestrate the full booking workflow: identify -> lock -> recheck -> insert -> commit
    - Handle transaction rollback on conflicts or errors
    - Implement atomic resource assignment logic
    - _Requirements: 3.1, 3.2, 3.3, 3.4, 3.5_

- [x] 5. Implement REST API and validation
  - [x] 5.1 Create appointment controller with POST endpoint
    - Implement POST /api/appointments endpoint
    - Add request validation (required fields, data formats, reference validation)
    - Implement proper error responses (400 Bad Request, 409 Conflict, 500 Internal Server Error)
    - _Requirements: 1.1, 2.3, 2.4, 3.1_

  - [x] 5.2 Create appointment retrieval endpoint
    - Implement GET /api/appointments/{appointmentId} endpoint
    - Return complete appointment details with resource assignments
    - Handle not found scenarios appropriately
    - _Requirements: 3.5_

  - [ ]* 5.3 Write unit tests for API controllers
    - Test request validation and error handling
    - Test successful appointment creation and retrieval
    - Test business rule validation scenarios
    - _Requirements: 1.1_

- [x] 6. Add OpenAPI documentation and health checks
  - [x] 6.1 Configure Swagger/OpenAPI documentation
    - Add Swashbuckle.AspNetCore package
    - Configure OpenAPI with proper request/response schemas
    - Add API documentation comments and examples
    - _Requirements: Implementation support_

  - [x] 6.2 Implement health and readiness checks
    - Add database connectivity health check
    - Configure health check endpoints for monitoring
    - Add basic structured logging for key operations
    - _Requirements: Implementation support_

- [x] 7. Implement comprehensive testing suite
  - [x] 7.1 Write PostgreSQL integration tests with Testcontainers
    - Test complete appointment booking workflow against real PostgreSQL
    - Test database constraint enforcement
    - Test transaction rollback scenarios
    - _Requirements: 2.1, 2.2, 3.1, 3.2, 3.3, 3.4, 3.5_

  - [x] 7.2 Write concurrent booking tests for double-booking prevention
    - Test multiple concurrent requests for same time slot
    - Verify only one booking succeeds while others fail with proper conflict errors
    - Test concurrent contention for the same resources
    - Test consistent resource lock ordering
    - Verify that overlapping double-bookings cannot be created
    - _Requirements: 3.3, 3.4_

- [x] 8. Final integration and verification
  - [x] 8.1 Verify application pipeline and dependency injection
    - Verify all required application services are registered with dependency injection
    - Verify database connection and Entity Framework context configuration
    - Make only the minimum application changes needed to complete the existing booking flow
    - _Requirements: All requirements_

  - [x] 8.2 Verify booking flow manually through HTTP API
    - Verify a successful appointment booking through the HTTP API
    - Verify resource conflicts are detected and reported through the HTTP API
    - Verify appointment retrieval returns the persisted appointment and assigned resources
    - Use the existing Swagger/OpenAPI interface or another lightweight HTTP client
    - _Requirements: All requirements_

  - [x] 8.3 Run final automated test suite
    - Run the complete existing automated test suite
    - Confirm PostgreSQL integration and concurrency tests pass
    - Fix only genuine correctness issues discovered by final verification
    - _Requirements: All requirements_

- [ ] 9. Complete submission documentation
  - [x] 9.1 Create README with build, run, and test instructions
    - Document prerequisites and local setup
    - Document how to build and run the application
    - Document how to run the automated tests
    - Document how to access and use Swagger/OpenAPI for the booking flow
    - _Requirements: Submission deliverable_

  - [x] 9.2 Add AI Collaboration Narrative to README
    - Add a dedicated AI Collaboration Narrative section to README.md
    - Summarize how GenAI was used across requirements, design, implementation, testing, and verification
    - Include concrete examples where AI output was challenged, corrected, narrowed, or rejected
    - Explain the human verification and engineering judgment applied to AI-generated work
    - _Requirements: Submission deliverable_

  - [ ] 9.3 Review design and documentation for consistency
    - Verify README, System Design Document, implementation, and tests describe the same implemented behavior
    - Remove stale or unsupported claims from final documentation
    - Confirm submission artifacts are concise and complete
    - _Requirements: Submission deliverable_

- [ ] 10. Final checkpoint - Complete system and submission verification
  - Ensure the application builds and runs
  - Ensure all required automated tests pass
  - Ensure the manual HTTP booking flow has been verified
  - Ensure all required submission documentation is complete.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Integration tests use Testcontainers for realistic PostgreSQL testing
- Concurrent booking tests specifically verify the approved concurrency strategy
- The transactional booking flow follows the exact sequence: identify candidates -> lock selected resources -> recheck availability while locked -> insert appointment -> commit
- Unit tests focus on business logic validation and error handling paths

## Task Dependency Graph

```json
{
  "waves": [
    { "id": 0, "tasks": ["1.1"] },
    { "id": 1, "tasks": ["1.2"] },
    { "id": 2, "tasks": ["2.1"] },
    { "id": 3, "tasks": ["2.3"] },
    { "id": 4, "tasks": ["2.4", "4.1"] },
    { "id": 5, "tasks": ["4.3"] },
    { "id": 6, "tasks": ["5.1"] },
    { "id": 7, "tasks": ["5.2", "5.3"] },
    { "id": 8, "tasks": ["6.1", "6.2"] },
    { "id": 9, "tasks": ["7.1", "7.2"] },
    { "id": 10, "tasks": ["8.1"] },
    { "id": 11, "tasks": ["8.2"] },
    { "id": 12, "tasks": ["8.3"] },
    { "id": 13, "tasks": ["9.1", "9.2"] },
    { "id": 14, "tasks": ["9.3"] }
  ]
}
```
