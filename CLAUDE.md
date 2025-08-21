# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Repository Overview

SharpServer is a C# game server project designed as a distributed, horizontally scalable game server architecture.

## Project Architecture

### Gateway Server
- **Purpose**: Entry point for all client requests
- **Features**:
  - Multi-protocol support (HTTP, WebSocket, TCP, UDP)
  - Horizontally scalable
  - Load balancing and routing to Game Servers
  - Protocol abstraction layer

### Game Server
- **Purpose**: Core game logic processing
- **Features**:
  - Horizontally scalable
  - Stateful game session management
  - Business logic processing
  - Communication with databases and message queues

### Database Layer
- **Redis**: High-performance caching and session storage
- **MongoDB**: Primary data persistence
- **etcd**: Distributed configuration and service discovery (optional)

### Message Queue
- **Purpose**: Asynchronous communication between services
- **Use Cases**: Event processing, service decoupling, scaling

### MQTT Component
- **Purpose**: Real-time messaging with clients
- **Features**: Publish/subscribe messaging pattern for game events

## Project Structure

```
SharpServer/
├── src/
│   ├── SharpServer.Gateway/        # Gateway server implementation
│   ├── SharpServer.GameServer/     # Game logic server
│   ├── SharpServer.Core/           # Shared core components
│   ├── SharpServer.Data/           # Data access layer
│   ├── SharpServer.Protocol/       # Protocol definitions
│   ├── SharpServer.Common/         # Common utilities
│   └── SharpServer.MessageQueue/   # Message queue abstractions
├── tests/
│   ├── SharpServer.Gateway.Tests/
│   ├── SharpServer.GameServer.Tests/
│   └── SharpServer.Core.Tests/
├── docker/                         # Docker configurations
└── scripts/                        # Build and deployment scripts
```


## Technology Stack

- **Framework**: .NET 8+
- **Protocols**: HTTP/WebSocket (ASP.NET Core), TCP/UDP (custom implementation)
- **Databases**: Redis, MongoDB, etcd
- **Message Queue**: RabbitMQ/Apache Kafka (TBD)
- **MQTT**: MQTT
- **Containerization**: Docker
- **Orchestration**: Kubernetes (planned)

## Architecture Principles

- **Horizontal Scalability**: Both Gateway and Game Servers can scale independently
- **Protocol Agnostic**: Support multiple communication protocols
- **Service Decoupling**: Use message queues for loose coupling
- **High Availability**: Distributed design with no single points of failure
- **Real-time Communication**: MQTT for low-latency game events

## Code Standards

- **Convention over Configuration**: Prefer established conventions over complex configuration
- **Simplicity over Complexity**: Choose simple, clear solutions over complex ones
- **Test-Driven Development**: Always write tests before making code changes
- **Quality Gates**: All changes must compile and pass tests before completion

## Development Workflow

1. **Write Tests First**: Create test cases before implementing features
2. **Implement**: Write code to make tests pass
3. **Verify**: Run build and tests to ensure quality

## .NET Commands

```bash
# Create project
dotnet new webapi -n ProjectName

# Build
dotnet build

# Run
dotnet run

# Test
dotnet test
```
