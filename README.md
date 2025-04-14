# Order API

## Overview

**Order API** is a microservice-based application for order management using the **Outbox Pattern** and **Dead Letter Queue (DLQ)** support. It's built on .NET 8 and uses modern architectural patterns to ensure reliability and scalability in distributed systems.

## Key Features

- **Outbox Pattern Implementation**: Ensures reliable message delivery even during service failures  
- **Dead Letter Queue (DLQ) Support**: Handles failed message processing gracefully  
- **Health Monitoring**: Includes health checks for database and RabbitMQ  
- **Saga Pattern Integration**: Works as part of a distributed transaction system  
- **Alerting System**: Provides notifications for message processing failures  

## Tech Stack

- **.NET 8**: Latest .NET platform  
- **Entity Framework Core**: ORM for database operations  
- **PostgreSQL**: Primary database  
- **MassTransit**: Message bus abstraction  
- **RabbitMQ**: Message broker  
- **Swagger/OpenAPI**: API documentation  

## Architecture

The application follows a microservices architecture with:

- **Outbox Pattern**: For reliable event publishing  
- **Transactional Consistency**: Ensures data integrity across operations  
- **Background Processing**: Asynchronous message publication  
- **Retry Mechanisms**: Configurable retry policy for failed operations  
- **Alerting**: Email and webhook alerts for critical failures  

## Key Components

- **OrdersController**: Handles HTTP requests for order creation and updates  
- **OutboxPublisherService**: Background service that processes outbox messages  
- **MassTransit Configuration**: Sets up message routing, retry policies, and DLQ handling  
- **Health Checks**: Monitors system health  

## Message Flow

1. Order creation is saved to the database  
2. Events are stored in the outbox table in the same transaction  
3. `OutboxPublisherService` processes unpublished messages  
4. Messages are published to RabbitMQ  
5. Consumers process messages with retry policies  
6. Failed messages are sent to Dead Letter Queues  
7. Alerts are triggered for persistent failures  

## Configuration

The service uses a hierarchical configuration system with `appsettings.json` files and environment variables.

Key configuration sections include:

- Database connections  
- RabbitMQ settings  
- Outbox processing parameters  
- Alert thresholds  

## Integration Points

- Communicates with **Payment Service** via HTTP  
- Exchanges messages with other services via **RabbitMQ**  
- Supports **Saga orchestration patterns**  

## Dependencies

- `MassTransit` (RabbitMQ and EF Core)  
- `Entity Framework Core`  
- `Npgsql` (PostgreSQL provider)  
- `Polly` (Resilience and transient-fault-handling)  
- Health checks for **PostgreSQL** and **RabbitMQ**  
- `Newtonsoft.Json` for serialization  

## Health Monitoring

The service exposes health endpoints that can be used by orchestration systems to monitor:

- Database connectivity  
- Outbox processing status  
- RabbitMQ connectivity  
