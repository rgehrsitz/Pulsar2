# Pulsar System Overview

**What is Pulsar?**
Pulsar is a high-performance, polling-based rules evaluation engine designed to process hundreds to thousands of key/value inputs using Redis as its data store. It fetches inputs, applies rules, and writes outputs back on a configurable schedule (default 100ms). The system's primary goal is to provide deterministic, real-time evaluations with minimal runtime overhead.

**Key Concepts and Components:**

1. **Rule Definitions (YAML/DSL)**:
   Pulsar’s rules start as human-readable definitions in a structured language (YAML or similar lamguage supporting a specialized DSL). These rules:

   - Specify conditions (e.g., comparisons, arithmetic operations) and may reference historical sensor values or computed facts for temporal-based operations.
   - Declare Actions to be carried out as a result of evaluations.
   - Adhere to a controlled set of allowed functions and syntax to ensure security and predictability.

2. **Build-Time Compilation**:
   Instead of interpreting rules at runtime, Pulsar employs a build-time compiler:

   - Validates rule syntax and semantics, ensuring no cycles or illegal references.
   - Analyzes dependencies, performs a topological sort, and assigns each rule a layer number used for rule evaluation ordering.
   - Generates optimized C# code representing all rules and their execution order.
   - Sets up any necessary buffers used for temporal evaluations.

   This ensures no runtime parsing overhead and guarantees that only valid, verified rule sets are deployed and no circular references exist.

3. **Runtime Execution**:
   Per specified cycle time (default = 100ms), the runtime:

   - Pulls the latest sensor values from the KV store in bulk.
   - Uses the precompiled C# code to evaluate rules layer-by-layer, ensuring dependencies are respected and results are stable.
   - Supports temporal logic by maintaining ring buffers of historical values for those sensors/facts that require them.
   - Writes computed outputs back to the KV store.

4. **Performance and Stability**:
   Pulsar is designed for:

   - Deterministic timing: All evaluations finish reliably within the configured cycle time.
   - Minimal overhead: Index-based lookups and code generation minimize memory allocations and GC pressure.
   - Scalability: Can handle hundreds or thousands of rules without compromising performance.

   Additionally, it supports a four-node environment with one active instance at a time, enabling seamless failover and redundancy.

5. **Testing, Debugging, and Maintainability**:
   Engineers can:
   - Write and maintain rules as DSL files version-controlled alongside source code.
   - Run unit and integration tests on the generated code.
   - Debug and step through the compiled C# rules.
   - Utilize performance metrics, logs, and traces for troubleshooting and optimization.

**In Summary**:
Pulsar is a robust, precompiled rules engine that unites a human-readable rule definition stage with a high-performance runtime execution model. By separating the rule definition, build-time compilation, and runtime execution, Pulsar ensures reliability, maintainability, and predictable performance in a complex, real-time processing environment.

---

# Pulsar Requirements

Below is a structured set of requirements for the logic system. The requirements are organized by the major components—**Rules (DSL)**, **Compiler**, and **Runtime**—highlighting the separation of concerns.

---

## High-Level Objectives

- **Deterministic, Timely Evaluation:** Evaluate hundreds (potentially over 1000) of rules and inputs during every cycle, ensuring predictable execution time.
- **Maintainability and Testability:** Ensure that rules are easy to write, debug, test, and version-control.
- **Robustness and Reliability:** Provide a stable, redundant environment that can failover seamlessly and run continuously without runtime rule modifications.
- **Performance and Scalability:** Achieve minimal runtime overhead by precompiling rules, using efficient data structures, and avoiding unnecessary computations.

---

## Rules (JSON/DSL)

**Purpose:**
The rules are authored by engineers as a DSL that serves as the source of truth. These human-readable definitions govern what conditions trigger certain actions.

**Requirements:**

1. **Language and Syntax:**

   - **Flexible Expressions:**
     Support standard comparison operators and arithmetic operators.
     Allow a controlled subset of functions, primarily `System.Math` methods and domain-specific library functions on a whitelist.
   - **Temporal References:**
     Provide DSL constructs for referencing historical values, e.g., `previous("SensorA", 1)`, with constraints (only certain facts may have historical lookbacks).

2. **Dependency Declaration:**

   - **Input/Output Dependencies:**
     Dependencies are implicitly determined during the compilation phase based on the conditions and actions declared in the rules.

3. **No Arbitrary Code Execution:**

   - **Controlled Expressions Only:**
     No unbounded C# code. All allowed expressions must be parseable and safe.

4. **Validation Criteria:**

   - **Build-Time Checks:**
     The rules must be self-contained, syntactically correct, with no reference to unknown sensors or functions.
   - **No Cycles Allowed:**
     Any cycles in rule dependencies result in a build-time error.

5. **Version Control & Documentation:**
   - **Maintained in Source Control:**
     The DSL files must be human-readable and stored in source control.
   - **Documentation:**
     Provide guidance for rule authors, including syntax, allowed operators, and best practices.

---

## Compiler

**Purpose:**
The compiler transforms the human-readable rules into highly optimized C# code. This ensures no runtime parsing overhead and guarantees that only valid, cycle-free sets of rules reach production.

**Requirements:**

1. **Parsing and Validation:**

   - **Syntax Validation:**
     Parse the DSL to check correctness of rule syntax, operators, and allowed functions.
   - **Type Checking and Compatibility:**
     Validate that values used in conditions and actions are compatible with their expected types.
   - **Dependency Analysis:**
     Identify rule dependencies, build a DAG, and detect cycles. If cycles exist, fail the build.
   - **Temporal Checks:**
     Validate temporal references. Ensure that only facts that require history are allocated historical buffers. If a rule references `previous("SensorA", X)`, confirm `SensorA` is marked for historical tracking.

2. **Code Generation:**

   - **Topological Sort and Layer Assignment:**
     Compute an evaluation order from the DAG. Assign each rule a layer number.
   - **Generated C# Code:**
     Produce a `CompiledRules.cs` (or equivalent) that contains:
     - A function evaluating each layer in order.
     - Inline arithmetic and function calls for conditions.
     - Indices for sensors/facts instead of dictionary lookups.
     - Conditional logic for temporal lookups if required by that rule.

3. **Error Reporting:**

   - **Build-Time Failures:**
     On errors (syntax, unknown references, cycles), the compiler fails with a clear message.
   - **Human-Readable Output:**
     Generated code should be human-readable for debugging. Errors should be understandable by the engineers modifying the rules.

4. **Integration with CI/CD:**
   - **Automated Build Steps:**
     The compiler runs during CI/CD pipeline. A failure blocks deployment.
   - **No Runtime Overhead:**
     Once compiled, no further parsing or code generation is done at runtime.

---

## Runtime

**Purpose:**
The runtime system executes the compiled rules every 100ms by default, integrating with Redis, evaluating rules in their dependency order, managing temporal data, and producing outputs.

**Requirements:**

1. **Redis Integration:**

   - **Bulk Retrieval of Inputs:**
     Fetch all current sensor values from Redis efficiently every cycle using pipelining.
   - **Outputs Back to Redis:**
     After evaluation, write computed results using atomic operations.
   - **Index-Based Access:**
     Use array or span indexing for sensors to avoid dictionary overhead in hot paths.

2. **Evaluation Logic:**

   - **Layered Execution Order:**
     Evaluate all rules in layer 0 first, then layer 1, and so forth, ensuring dependencies are satisfied.
   - **Temporal Data Handling:**
     Maintain fixed-size ring buffers for historical values only for the facts that actually require them.
     If `previous("SensorA", 1)` is used by any rule, store `SensorA`’s values in a ring buffer. If no rule needs history for `SensorB`, do not allocate a buffer for it.
   - **Performance Guarantees:**
     - Finish evaluation well within defined cycle time.
     - Minimal GC pressure.
     - Possibly detect unchanged inputs (optional optimization) to skip unnecessary computations if all rules are stable.

3. **Robustness and Stability:**

   - **Redundancy and Failover:**
     Operate in a four-node environment with only one active node at a time. Seamlessly failover if the active node fails.
   - **Thread-Safety:**
     Ensure thread-safe operations, no race conditions.
   - **Error Handling:**
     - If a runtime error occurs (e.g., unexpected null), handle gracefully, log the issue, and continue processing subsequent cycles.

4. **Observability and Diagnostics:**

   - **Logging and Metrics:**
     Log evaluation times, triggered rules, and store performance metrics. Produce output compatible with Prometheus.
   - **Debugging Hooks:**
     Allow toggling verbose logging or stepping through generated code in a debug environment.
   - **Replay and Historical Analysis:**
     Support replaying historical data sets for testing or diagnostics.

5. **Testing and Validation:**
   - **Unit Tests for Generated Code:**
     Allow offline testing of `CompiledRules.cs` by passing known inputs and checking outputs.
   - **Integration Tests:**
     Verify the entire pipeline (input retrieval, rule evaluation, output writing) under controlled conditions.
   - **Performance and Stress Testing:**
     Ensure the runtime can handle peak loads without degrading response times.

---

## Redis-Specific Considerations

1. **Data Organization:**
   - Use appropriate Redis data types (String, Hash, etc.) for different sensor types
   - Implement efficient bulk operations using Redis pipelining
   - Consider TTL for temporal data

2. **Performance Optimization:**
   - Minimize Redis round trips through batching
   - Use Redis server-side scripts where appropriate
   - Implement connection pooling and retry policies

3. **Monitoring:**
   - Track Redis operation latencies
   - Monitor memory usage for temporal buffers
   - Implement Redis-specific health checks

---

## Safety and Security

- **No Unauthorized Code Execution:**
  The compiler strictly controls what code is generated. No external scripts or arbitrary code injection.
- **Input Validation:**
  Validate inputs from KV store before use, handle unexpected or invalid data gracefully.
- **Resource Management:**
  Avoid memory leaks by reusing buffers and ensuring stable resource usage over extended operation periods.

---

## Documentation and Guidance

- **Architectural Documentation:**
  Provide an overview of the entire system (rules, compiler, runtime) and how they fit together.
- **Rule Authoring Guide:**
  Instruct engineers on allowed syntax, temporal references, and best practices.
- **Deployment and Operations:**
  Explain how to deploy the logic engine, manage failover, tune performance, and troubleshoot issues.
- **Testing and Debugging Reference:**
  Provide instructions on how to run unit, integration, and performance tests, as well as debugging steps and logging configuration.

---

## Summary

This refined requirement set clarifies the separation of concerns:

- **Rules (DSL):** Human-readable, restricted DSL for defining conditions, including temporal logic.
- **Compiler:** A build-time tool that validates, analyzes dependencies, checks for cycles, and generates optimized C# code.
- **Runtime:** Executes the compiled code every cycle, handles data I/O, manages history only where needed, ensures determinism and performance, and provides observability and failover capabilities.

 ```mermaid
sequenceDiagram
    participant Timer
    participant Runtime
    participant Redis
    participant RingBuffer
    participant Rules
    
    Note over Timer,Rules: 100ms Cycle
    Timer->>Runtime: Trigger Cycle
    activate Runtime
    
    Runtime->>Redis: Bulk Fetch (Pipeline)
    Redis-->>Runtime: Sensor Values
    
    Runtime->>RingBuffer: Update Historical Values
    Runtime->>Rules: Layer 0 Evaluation
    Runtime->>Rules: Layer 1 Evaluation
    Runtime->>Rules: Layer N Evaluation
    
    Runtime->>Redis: Bulk Write Results (Pipeline)
    Redis-->>Runtime: Confirmation
    
    Runtime->>Runtime: Calculate Cycle Stats
    Runtime-->>Timer: Complete Cycle
    deactivate Runtime
    
    Note over Timer,Rules: Next 100ms Cycle
    ```