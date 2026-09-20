---
inclusion: auto
name: engineering-principles
description: Engineering guardrails and principles for Unified Service Scheduler development
---

# Engineering Principles: Unified Service Scheduler

## Core Engineering Priorities

### 1. Correctness of Resource-Constrained Booking
- **Double-booking prevention is critical** - No two appointments can share the same service bay or technician during overlapping time periods
- **Atomic resource assignment** - Either both service bay AND qualified technician are reserved, or the appointment fails entirely
- **Data integrity** - Resource assignments must be consistent across the entire system
- **Concurrency safety** - Handle simultaneous booking requests without data corruption or conflicts

### 2. Maintainability and Clarity
- **Explicit assumptions** - All technical decisions and business logic assumptions must be documented and justified
- **Clear separation of concerns** - Booking logic, availability checking, and persistence should be distinct components
- **Minimal complexity** - Avoid abstractions that don't directly serve the core requirements
- **Technology-agnostic design** - Focus on logical correctness before technology-specific implementation

### 3. Testability and Verification
- **Testable components** - Each component should be independently verifiable
- **Coverage of failure scenarios** - Test both success and failure paths in the booking workflow
- **Concurrent access testing** - Verify behavior under simultaneous booking requests
- **Testing strategy decisions** - Specific testing techniques and tools should be evaluated during design/implementation

### 4. Observability and Monitoring
- **System visibility** - Enable monitoring of booking operations and resource state
- **Error diagnosis** - Provide sufficient context for understanding booking failures
- **Observability strategy decisions** - Specific monitoring and logging approaches should be designed based on system architecture

## Development Guardrails

### Scope Management
- **Implement only approved requirements** - Build the three core requirements and their necessary supporting functionality
- **Justify any additions** - Any capability beyond core booking flow must have explicit approval
- **Challenge assumptions** - Question implementation details that weren't explicitly specified or derived from requirements

### Technical Decision Process  
- **Document trade-offs** - Explain why specific technical approaches were chosen
- **Prefer simplicity** - Choose the most straightforward solution that meets requirements
- **Avoid over-engineering** - Don't build for hypothetical future requirements
- **Defer system design decisions** - Architecture, technology choices, and implementation details belong in the design phase

### AI Development Guidelines
- **Human review required** - All AI-generated code, designs, and documentation require human review and approval before acceptance
- **Verify correctness** - Don't assume AI-generated logic is correct without validation
- **Question complex solutions** - If AI proposes intricate implementations, seek simpler alternatives first
- **Maintain human oversight** - Critical business logic and architectural decisions should be human-driven

## Implementation Standards

### Error Handling
- **Clear failure reporting** - Communicate specific reasons why appointments cannot be confirmed
- **Graceful resource unavailability** - Handle missing resources without system failure
- **Consistent error communication** - Provide uniform error information across failure scenarios

### Data Consistency
- **Appropriate transaction boundaries** - Ensure atomic operations where required by business logic
- **Concurrent access safety** - Prevent data corruption under simultaneous access
- **Reliability strategy decisions** - Recovery procedures and fault tolerance mechanisms should be evaluated during system design

## AI-Assisted Implementation

- Follow the approved requirements, design, and current task scope. Do not introduce unrequested business rules, architecture, or dependencies.
- Make the smallest coherent change required by the current task.
- Inspect the actual workspace files after making changes; do not rely solely on sub-agent completion reports.
- Before running build or tests, present significant implementation changes for human review.
- After human approval, run the relevant verification and report the actual results.