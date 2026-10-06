# Spec-Driven Development (SDD) & Spec Kit Workflow Rules

When implementing code or modifying features in this repository, all AI agents MUST adhere to the following workflow:

## 1. Single Source of Truth
- The project rules and architecture constraints are codified in [/.specify/memory/constitution.md](file:///d:/quanlytestcase/.specify/memory/constitution.md).
- Feature requirements, user stories, and acceptance scenarios are defined in [/specs/001-test-case-management/spec.md](file:///d:/quanlytestcase/specs/001-test-case-management/spec.md).
- Technical architecture, layer mapping, and entity contracts are defined in [/specs/001-test-case-management/plan.md](file:///d:/quanlytestcase/specs/001-test-case-management/plan.md).
- Actionable implementation checklist is tracked in [/specs/001-test-case-management/tasks.md](file:///d:/quanlytestcase/specs/001-test-case-management/tasks.md).

## 2. Implementation Execution Protocol
1. **Check Task Order**: Always pick the next uncompleted task (`- [ ] TXXX`) from `tasks.md`.
2. **Verify Prerequisites**: Ensure preceding phases (Setup, Foundational) are complete before starting user story implementations.
3. **Write Tests First**: For tasks with associated tests, write unit/integration tests and verify failure before implementing code.
4. **Update Progress**: Once a task is implemented and verified, update `tasks.md` by changing `- [ ] TXXX` to `- [x] TXXX`.
5. **No Spec Drift**: If an implementation detail deviates from `spec.md` or `plan.md`, the specification must be formally updated first.

## 3. Architecture Constraints
- **Framework**: ABP Framework (.NET 8/9 LTS).
- **Module Name**: `Acme.TestCaseManagement` (Reusable Application Module).
- **No Overwriting**: Never update a master `TestCase` when recording execution results. Always append a new `TestExecution` attempt to the `TestRunItem`.
- **Snapshots**: Bind all test run items to `TestCaseVersionId` (frozen snapshot), never directly to mutable `TestCaseId`.
