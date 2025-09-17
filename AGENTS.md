# Repository Guidelines

## Architecture & Principles
SharpServer delivers a horizontally scalable backend where the Gateway receives public traffic, abstracts client protocols (HTTP shipping now, WebSocket/MQTT next), and routes requests to GameServer instances. GameServer executes stateful gameplay workflows via MagicOnion RPC. Shared utilities in `SharpServer.Common` handle Redis-backed service discovery and load balancing. Roadmap modules (`Core`, `Data`, `MessageQueue`) will add domain models, persistence, and async pipelines. Design pillars from `CLAUDE.md`: scale each tier independently, stay protocol agnostic, decouple via queues, guard high availability, and push real-time updates.

## Project Layout
`SharpServer.sln` groups all projects under `src/`. Active folders: `SharpServer.Gateway`, `SharpServer.GameServer`, `SharpServer.Common`, and `SharpServer.Protocol`. Tests live under `tests/` with `<Project>.Tests` naming, and `test-rpc.http` supports manual RPC checks. Expect future top-level folders (`docker/`, `scripts/`, new service projects) as the architecture expands.

## Technology Stack
- .NET 9 (C#) with ASP.NET Core minimal APIs
- MagicOnion for RPC contracts (`src/SharpServer.Protocol`)
- Redis for caching, service registry, and session state
- MongoDB planned for persistence; etcd optional for configuration
- RabbitMQ/Kafka and MQTT earmarked for cross-service messaging

## Development Workflow & Commands
Follow the TDD cycle described in `CLAUDE.md`: write tests, implement, verify. Core commands:
- `dotnet restore`
- `dotnet build SharpServer.sln`
- `dotnet run --project src/SharpServer.Gateway/SharpServer.Gateway.csproj`
- `dotnet run --project src/SharpServer.GameServer/SharpServer.GameServer.csproj`
- `dotnet test` or `dotnet test --collect:"XPlat Code Coverage"`

## Coding & Testing Standards
Use four-space indentation, `var` for obvious types, `PascalCase` public members, `camelCase` locals, and prefix interfaces with `I`. Keep nullable warnings clean and group DI registrations logically in top-level `Program.cs`. Tests should live beside the relevant project tests folder, named `Method_WhenCondition_ShouldResult`. Target coverage on load balancing, service discovery, and RPC resiliency; prefer integration coverage via `WebApplicationFactory` for Gateway endpoints.

## Contribution Checklist
Adopt Conventional Commit messages (`feat(load-balancing): ...`) scoped to the affected module. Before opening a PR, ensure `dotnet build` and `dotnet test` pass, document configuration changes (Redis endpoints, ports, deployment envs), and attach relevant logs, HTTP transcripts, or screenshots. Provide context on architecture impacts (scaling, messaging, MQTT plans) so reviewers can assess alignment.

## Configuration Notes
Gateway defaults read `ConnectionStrings:GameServer` from `src/SharpServer.Gateway/appsettings.json`; override with `ConnectionStrings__GameServer`. Redis falls back to `localhost:6379`; set `ConnectionStrings__Redis` for remote clusters. Keep Gateway and GameServer configs synchronized so the registry advertises live hosts and future MQTT/message-queue components slot in without surprises.
