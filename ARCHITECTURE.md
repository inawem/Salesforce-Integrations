# System Architecture

## Overview

This document describes the architecture of the Salesforce CRM Integration system, including components, interactions, and design decisions.

## Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                          Client Layer                               │
├─────────────────────────────────────────────────────────────────────┤
│ Angular Web App (4200) │ Mobile Apps │ Third-party Integrations    │
└────────────────────────┬───────────────────────────────────────────┘
                         │ HTTPS
                         ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    API Gateway / Load Balancer                      │
│                       (Azure Application Gateway)                    │
└────────────────────────┬───────────────────────────────────────────┘
                         │
        ┌────────────────┼────────────────┐
        │                │                │
        ▼                ▼                ▼
┌──────────────┐  ┌──────────────┐  ┌──────────────┐
│ REST API     │  │ Health Check │  │ Swagger/API  │
│ (5000)       │  │ (5000)       │  │ Docs (5000)  │
└──────┬───────┘  └──────────────┘  └──────────────┘
       │
       │ Dependency Injection
       ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Services Layer (.NET Core)                       │
├─────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────┐   │
│ │ Controllers                                                  │   │
│ │  - CustomersController                                       │   │
│ │  - SyncController                                            │   │
│ └────────────────┬─────────────────────────────────────────────┘   │
│                  │                                                  │
│ ┌────────────────▼─────────────────────────────────────────────┐   │
│ │ Business Services                                            │   │
│ │  - CustomerSyncService                                       │   │
│ │  - SalesforceClient                                          │   │
│ │  - Auth0Client                                               │   │
│ │  - ServiceBusPublisher                                       │   │
│ └─┬──────┬──────┬──────┬──────────────────────────────────────┘   │
│   │      │      │      │                                           │
└─┬─┴──────┴──────┴──────┴─────────────────────────────────────────┘
  │
  │ Entity Framework
  ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Data Access Layer (EF Core)                      │
├─────────────────────────────────────────────────────────────────────┤
│ ┌──────────────────────────────────────────────────────────────┐   │
│ │ DbContext & Repositories                                     │   │
│ │  - ApplicationDbContext                                      │   │
│ │  - CustomerRepository                                        │   │
│ │  - SyncCheckpointRepository                                  │   │
│ │  - SyncFailureRepository                                     │   │
│ └────────────────┬─────────────────────────────────────────────┘   │
└────────────────┼──────────────────────────────────────────────────┘
                 │
                 ▼
        ┌────────────────┐
        │ Azure SQL DB   │
        │ (Persistent    │
        │  Data Store)   │
        └────────────────┘

External Integrations:

┌────────────────┐
│ Salesforce     │ ◄───── REST API calls (OAuth2)
│ (CRM)          │        - Query customers
│                │        - Create/Update records
└────────────────┘

┌────────────────┐
│ Azure Key      │ ◄───── Secrets retrieval
│ Vault          │        - Credentials
│                │        - Connection strings
└────────────────┘

┌────────────────┐
│ Azure Service  │ ◄───── Message publishing
│ Bus / Event    │        - Sync events
│ Grid           │        - Notifications
└────────────────┘

┌────────────────┐
│ Auth0          │ ◄───── Token validation
│ (IdP)          │        - JWT verification
│                │        - User info
└────────────────┘

┌────────────────┐
│ Azure Logic    │ ◄───── Workflow orchestration
│ Apps           │        - Scheduled syncs
│                │        - Integration workflows
└────────────────┘

┌────────────────┐
│ Application    │ ◄───── Telemetry & Monitoring
│ Insights       │        - Logs
│                │        - Performance metrics
└────────────────┘
```

## Component Descriptions

### 1. Client Layer

**Angular Web Application**
- User interface for managing customers
- Manual sync triggers
- Sync status monitoring
- Real-time notifications
- Authentication via Auth0

**Mobile & Third-party**
- RESTful API consumption
- Webhooks for real-time updates

### 2. API Gateway

**Azure Application Gateway**
- Load balancing
- SSL/TLS termination
- Request routing
- DDoS protection
- Web Application Firewall (WAF)

### 3. REST API Services

**CustomersController**
- GET /api/customers
- GET /api/customers/{id}
- POST /api/customers
- PUT /api/customers/{id}
- DELETE /api/customers/{id}

**SyncController**
- POST /api/sync/from-salesforce
- POST /api/sync/to-salesforce
- GET /api/sync/status
- POST /api/sync/retry/{id}

### 4. Business Services

**CustomerSyncService**
- Orchestrates bidirectional sync
- Checkpoint-based resume
- Error handling & recovery
- Event publishing

**SalesforceClient**
- OAuth2 authentication
- REST API calls
- Token caching & refresh
- Rate limit handling

**Auth0Client**
- Token management
- User information retrieval

**ServiceBusPublisher**
- Message queuing
- Event publishing

### 5. Data Access Layer

**Entity Framework DbContext**
- Customer entity mapping
- Change tracking
- Migrations management
- Soft delete support

**Repository Pattern**
- Abstraction over DbContext
- CRUD operations
- Query methods
- Error handling

### 6. Database

**Azure SQL Database**
- Tables: Customers, SyncFailures, SyncCheckpoints, AuditLogs
- Indexes for performance
- Backup & recovery
- Point-in-time restore

### 7. External Systems

**Salesforce**
- Source of truth for customer data
- OAuth2 authentication
- SOQL queries
- Create/Read/Update operations

**Azure Key Vault**
- Secrets storage
- Credential management
- Access control

**Azure Service Bus**
- Message queuing
- Event publishing
- Pub/Sub patterns

**Auth0**
- Identity provider
- JWT token generation
- User management

**Azure Logic Apps**
- Workflow orchestration
- Scheduled syncs
- Integration workflows

**Application Insights**
- Telemetry collection
- Performance monitoring
- Error tracking
- Log analytics

## Data Flow

### Sync From Salesforce

```
1. SyncController.SyncFromSalesforce() called
   ↓
2. CustomerSyncService retrieves checkpoint
   ↓
3. Salesforce API queried for modified customers since checkpoint
   ↓
4. Results paginated (if > 50,000 records)
   ↓
5. For each customer:
   - Transform Salesforce → Local model
   - Check if exists in DB (by SalesforceId)
   - Create or update in DB
   - Publish sync event to Service Bus
   - Save checkpoint every 100 records
   ↓
6. Final checkpoint saved
   ↓
7. Return sync result (success/failure counts)
```

### Sync To Salesforce

```
1. SyncController.SyncToSalesforce() called
   ↓
2. CustomerSyncService queries local DB for modified customers
   ↓
3. For each customer:
   - Transform Local → Salesforce model
   - If no SalesforceId: Create in Salesforce, store returned Id
   - If SalesforceId exists: Update in Salesforce
   - Publish sync event
   ↓
4. Return sync result
```

### Authentication Flow

```
1. Client requests /api/customers with Auth0 token in Authorization header
   ↓
2. JwtBearerMiddleware validates token:
   - Verifies signature
   - Checks expiration
   - Validates audience & scopes
   ↓
3. If valid: ClaimsPrincipal populated with user identity
   ↓
4. Authorization policy checked (e.g., "read", "write")
   ↓
5. If authorized: Request proceeds to controller
   ↓
6. If unauthorized: 401 or 403 response
```

## Resilience Patterns

### 1. Retry Policy (Polly)
- Exponential backoff: 2s, 4s, 8s, 16s, 32s
- Handles transient failures (timeouts, temporary network issues)
- Maximum 3 retries

### 2. Rate Limit Handling
- Detects 429 (Too Many Requests)
- Backs off and retries
- Maximum 5 retries with exponential backoff

### 3. Circuit Breaker
- Opens after 5 consecutive failures
- Waits 30 seconds before attempting again
- Prevents cascading failures

### 4. Token Caching
- Salesforce tokens cached in memory
- Refreshed 5 minutes before expiry
- Reduces authentication requests

### 5. Database Retry
- Automatic retry on transient SQL errors
- 3 retries with 10-second delays

## Error Handling

### Sync Failures
- Recorded in SyncFailures table
- Include error message and stack trace
- Support retry mechanism
- Dead letter queue for unrecoverable errors

### HTTP Errors
- Global ErrorHandlingMiddleware
- Returns structured error responses
- Logs all exceptions
- Maps exceptions to HTTP status codes

## Performance Considerations

### Batch Processing
- Sync in batches of 1,000 records
- Reduce memory usage
- Enable progress checkpoints

### Indexing
- SalesforceId: unique index
- LastSyncedAt: for incremental queries
- Email: for lookups
- IsDeleted: for soft delete filtering

### Caching
- Token caching (Salesforce & Auth0)
- In-memory cache with TTL
- Reduced API calls

### Pagination
- 100 customers per page (configurable)
- Offset-based for UI
- Cursor-based for large datasets

## Scalability

### Horizontal Scaling
- Stateless design (no session state)
- Load balancer distributes requests
- Database connection pooling

### Vertical Scaling
- Async/await for non-blocking I/O
- Connection pooling
- Efficient EF queries

### Asynchronous Processing
- Service Bus for async operations
- Logic Apps for scheduled tasks
- Background workers for long-running operations

## Security

### Authentication
- OAuth2 (Salesforce)
- JWT (Auth0)
- Token validation on every request

### Authorization
- Role-based access control (RBAC)
- Scope-based permissions
- Policy-based authorization

### Data Protection
- Encryption in transit (HTTPS/TLS)
- Encryption at rest (Azure SQL TDE)
- No secrets in code/config

### Secrets Management
- Azure Key Vault
- Managed Identity access
- Automatic rotation

## Monitoring & Observability

### Logging
- Structured logging (ILogger)
- Log levels: Debug, Information, Warning, Error
- Centralized via Application Insights

### Metrics
- Request count & latency
- Error rates
- Sync performance
- API response times

### Alerts
- High error rates
- Sync failures
- Database connection issues
- External API timeouts

## Deployment Architecture

### Local Development
- Docker Compose
- SQL Server container
- Service Bus emulator (RabbitMQ)
- API container

### Azure Production
- App Service (Linux)
- Azure SQL Database
- Azure Service Bus
- Application Gateway
- Key Vault
- Logic Apps
- Application Insights
