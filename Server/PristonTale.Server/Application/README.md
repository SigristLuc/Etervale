# Application services

Transport-free use cases and the shared authoritative world live in
[Shared/PristonTale.Application](../../../Shared/PristonTale.Application), used by the game server.

Keep this server-local directory for orchestration that is specific to server hosting.
Do not duplicate auth, character/session services or world rules here.
The TCP boundary in `Net/` maps requests to application inputs and outcomes.

See [AGENTS.md](../../../AGENTS.md) for repository guidance.
