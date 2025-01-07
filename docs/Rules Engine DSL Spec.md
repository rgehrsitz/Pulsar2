# Rules Engine DSL Specification

## Overview

This DSL is a YAML-based format used to define a set of rules for a sensor-driven rules engine. The rules rely on sensor values retrieved from a Redis instance and can include temporal conditions, mathematical expressions, and logical groupings of conditions. They are designed to be human-readable, easily validated, and translatable into optimized C# code for runtime execution.

The DSL is part of a larger system that uses a separate global configuration file to define universal parameters and valid sensors. This separation allows system-wide tooling and services to reference a single source of truth for global configuration, while the rules DSL remains focused solely on rule logic.

## Key Objectives

1. **Human-Readable**:
   The DSL should be easy for engineers to read, write, and maintain.

2. **Separation of Concerns**:
   Global system configuration (including valid sensors) is defined in a separate **system configuration file**. The rules DSL references this global configuration indirectly, enabling different services and tools to validate their I/O against the same source of truth.

3. **Tooling & Validation**:
   A schema (e.g., JSON Schema) will validate both the system configuration and the rules DSL. The rules compiler ensures that all referenced sensors and actions are valid.

4. **Performance & Scalability**:
   The compiled C# code will run efficiently with hundreds to a thousand rules every 100ms. Temporal and mathematical conditions must be performant.

5. **Temporal & Mathematical Conditions**:
   Support conditions based on time durations and basic arithmetic expressions.

6. **Dependency & Ordering**:
   The compiler infers dependencies between rules and sensors, allowing for optimal execution ordering and potential runtime optimizations.

7. **Metadata Generation**:
   The compiler generates metadata for runtime debugging, documentation, and dependency visualization.

8. **Stable Production Deployment**:
   Rules are validated and compiled once, then remain stable unless changes occur during development or bug fixes.

## Configuration Files

### System Configuration File (e.g., `system_config.yaml`)

This file is the system-wide source of truth for global definitions. It is maintained separately from the rules file. Components throughout the system, including the rules compiler, can reference this file to ensure consistency.

**Structure:**

```yaml
version: 1
valid_sensors:
  - temperature
  - humidity
  - pressure
  - alerts:temperature
  - converted_temp
# Additional global configurations can be added here if needed.
```

- version (required): An integer indicating the global configuration schema version.
- valid_sensors (required): A list of strings representing all permissible sensor keys.

System components (e.g., data ingestion, logging services, monitoring tools) and the rules compiler all use this system_config.yaml as a single source of truth for what sensors are valid.

## Rule Format

### Current Implementation

Rules are defined in YAML using this structure:

```yaml
rules:
  - name: "ExampleRule"      # Required, unique identifier
    description: "..."       # Optional description
    conditions:             # Required condition group
      all:                  # or 'any'
        - condition:        # Individual condition
            type: "comparison|threshold_over_time|expression"
            # Additional fields based on type
    actions:               # Required list of actions
      - action_type:       # set_value or send_message
          # Action-specific fields
```

### Supported Condition Types

1. **Comparison Condition** (Currently Implemented)
```yaml
condition:
  type: comparison
  sensor: "sensor_name"    # Must exist in system_config.yaml
  operator: "<|>|<=|>=|==|!="
  value: <number>
```

2. **Expression Condition** (Currently Implemented)
```yaml
condition:
  type: expression
  expression: "sensor_a + (sensor_b * 2) > 100"
```

3. **Threshold Over Time** (In Development)
```yaml
condition:
  type: threshold_over_time
  sensor: "sensor_name"
  threshold: <number>
  duration: <milliseconds>
```

### Supported Actions

1. **Set Value** (Currently Implemented)
```yaml
set_value:
  key: "output_sensor"     # Must exist in system_config.yaml
  value: <static_value>    # OR
  value_expression: "sensor_a * 2"
```

2. **Send Message** (In Development)
```yaml
send_message:
  channel: "alert_channel"
  message: "Alert text"
```

## Compilation Process

1. **Validation Phase**
   - Load and validate system configuration
   - Parse rules YAML structure
   - Validate all referenced sensors exist in system configuration
   - Validate expressions and conditions
   - Check for circular dependencies

2. **Dependency Analysis**
   - Build dependency graph from rule inputs/outputs
   - Perform topological sort to determine evaluation order
   - Assign layer numbers to each rule
   - Validate no circular dependencies exist

3. **Code Generation**
   - Generate C# classes for rule evaluation
   - Create optimized evaluation order
   - Include temporal condition tracking where needed
   - Generate rule metadata for runtime use

### Runtime Considerations

1. **Temporal Conditions**
   - System maintains sliding window of values for temporal conditions
   - Window size determined by longest duration needed
   - Values tracked only for sensors used in temporal conditions

2. **Expression Evaluation**
   - Mathematical expressions compiled to C# code
   - Sensor values accessed via array indices for performance
   - Runtime maintains evaluation context for each cycle

3. **Action Execution**
   - Actions executed in order defined within each rule
   - Set value actions update the data store immediately
   - Message actions handled by message broker integration

### Error Handling

1. **Compilation Errors**
   - Invalid sensor references
   - Malformed expressions
   - Circular dependencies
   - Invalid duration values
   - Type mismatches in expressions

2. **Runtime Errors**
   - Missing sensor data
   - Expression evaluation errors
   - Action execution failures

## Example Complete Rule

```yaml
rules:
  - name: "TemperatureConversion"
    description: "Converts temperature and sets alert if too high"
    conditions:
      all:
        - condition:
            type: threshold_over_time
            sensor: "temperature_f"
            threshold: 100
            duration: 500
        - condition:
            type: expression
            expression: "(temperature_f - 32) * 5/9 > 37.8"
    actions:
      - set_value:
          key: "temperature_c"
          value_expression: "(temperature_f - 32) * 5/9"
      - send_message:
          channel: "alerts"
          message: "High temperature detected!"
```

## Implementation Status

Current implementation supports:
- Full YAML parsing and validation
- Comparison and expression conditions
- Set value actions
- Dependency analysis and ordering
- Redis integration

In development:
- Threshold over time conditions
- Send message actions
- Enhanced error reporting
- Monitoring and metrics
