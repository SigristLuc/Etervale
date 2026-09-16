# Application services

Transport-free use cases and the shared authoritative world live in
[Shared/PristonTale.Application](../../../Shared/PristonTale.Application), reused by the online server and offline backend.

Keep this server-local directory for orchestration that is specific to server hosting.
Do not duplicate auth, character/session services or world rules here.
The gRPC boundary remains in `Net/`; it maps protobuf to neutral application inputs and outcomes.

See [AGENTS.md](../../../AGENTS.md) for repository guidance.
