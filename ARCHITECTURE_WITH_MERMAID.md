# System Architecture - With Mermaid Diagrams

## Overview

This document describes the architecture of the Salesforce CRM Integration system with visual diagrams.

## Architecture Diagram

```mermaid
graph TB
    subgraph "External Systems"
        SFDC["Salesforce CRM<br/>(OAuth2, REST API)"]
        AUTH0["Auth0<br/>(JWT, Token Mgmt)"]
    end
    
    subgraph "Frontend"
        ANGULAR["Angular Web App<br/>(Dashboard, Sync UI)"]
    end
    
    subgraph "Azure Cloud"
        APPGW["Azure App Gateway<br/>(Load Balancing)"]
        APPSERVICE["App Service<br/>(.NET 8 Core API)"]
        KEYVAULT["Key Vault<br/>(Secrets)"]
        SERVICEBUS["Service Bus<br/>(Messaging)"]
        SQLDB["SQL Database<br/>(Persistence)"]
        INSIGHTS["Application Insights<br/>(Monitoring)"]
    end
    
    subgraph ".NET Application Layers"
        CONTROLLERS["Controllers<br/>(REST API)"]
        SERVICES["Services<br/>(Business Logic)"]
        DATA["Data Layer<br/>(EF Core, Repositories)"]
    end
    
    SFDC -->|REST API Calls| APPSERVICE
    AUTH0 -->|JWT Validation| APPSERVICE
    ANGULAR -->|HTTP Requests| APPGW
    APPGW -->|Routes| APPSERVICE
    APPSERVICE -->|Retrieves| KEYVAULT
    APPSERVICE -->|Publishes| SERVICEBUS
    APPSERVICE -->|Reads/Writes| SQLDB
    APPSERVICE -->|Sends Telemetry| INSIGHTS
    
    APPSERVICE -->|1. Router| CONTROLLERS
    CONTROLLERS -->|2. Business Logic| SERVICES
    SERVICES -->|3. Data Access| DATA
    DATA -->|4. ORM| SQLDB
    
    style SFDC fill:#00a1de
    style AUTH0 fill:#eb5424
    style APPSERVICE fill:#512bd4
    style CONTROLLERS fill:#512bd4
    style SERVICES fill:#512bd4
    style DATA fill:#512bd4
    style KEYVAULT fill:#0078d4
    style SERVICEBUS fill:#0078d4
    style SQLDB fill:#0078d4
    style INSIGHTS fill:#0078d4
```

## Data Flow Sequence

### Sync From Salesforce Process

```mermaid
sequenceDiagram
    participant API as .NET API
    participant SF as Salesforce
    participant DB as Local Database
    participant SB as Service Bus
    
    API->>SF: 1. Authenticate (OAuth2)
    SF-->>API: Access Token
    API->>SF: 2. Query Modified Customers (SOQL)
    SF-->>API: Customer Records
    loop For Each Customer
        API->>DB: 3. Check if Exists
        DB-->>API: Existing Record
        API->>DB: 4. Create/Update
        DB-->>API: Success
        API->>SB: 5. Publish Sync Event
        SB-->>API: Acknowledged
    end
    API->>DB: 6. Save Checkpoint
    DB-->>API: Checkpoint Saved
```

## Sync To Salesforce Process

```mermaid
sequenceDiagram
    participant API as .NET API
    participant DB as Local Database
    participant SF as Salesforce
    
    API->>DB: 1. Query Modified Customers
    DB-->>API: Customer List
    loop For Each Customer
        API->>SF: 2. Create/Update (REST API)
        SF-->>API: Success with ID
        API->>DB: 3. Store Salesforce ID
        DB-->>API: Updated
    end
```

## Request/Response Flow

```mermaid
sequenceDiagram
    participant Client as Angular Client
    participant Auth0 as Auth0
    participant Gateway as App Gateway
    participant API as .NET API
    participant DB as Database
    
    Client->>Auth0: 1. Request Token
    Auth0-->>Client: JWT Token
    Client->>Gateway: 2. GET /api/customers (with token)
    Gateway->>API: Route Request
    API->>API: 3. Validate JWT Token
    API->>DB: 4. Query Customers
    DB-->>API: Customer Data
    API-->>Gateway: JSON Response
    Gateway-->>Client: 200 OK
    Client->>Client: Render Dashboard
```

## Dependency Injection Graph

```mermaid
graph LR
    DI["Dependency Injection<br/>Container"]
    
    DI -->|Registers| ProgramCs["Program.cs"]
    
    ProgramCs -->|Configures| DB["DbContext"]
    ProgramCs -->|Configures| Auth["Auth0Client"]
    ProgramCs -->|Configures| SF["SalesforceClient"]
    ProgramCs -->|Configures| Services["SyncService"]
    ProgramCs -->|Configures| Repos["Repositories"]
    
    SF -->|Uses| Config1["SalesforceConfig"]
    Auth -->|Uses| Config2["Auth0Config"]
    Services -->|Uses| SF
    Services -->|Uses| Repos
    Repos -->|Uses| DB
    
    style DI fill:#512bd4
    style Services fill:#512bd4
    style SF fill:#512bd4
    style Auth fill:#512bd4
```

## Error Handling Flow

```mermaid
graph TD
    A["Request Received"] -->|Valid| B["Process Request"]
    A -->|Invalid| C["Return 400"]
    
    B -->|Success| D["Return 200"]
    B -->|Unauthorized| E["Return 401"]
    B -->|Forbidden| F["Return 403"]
    B -->|Not Found| G["Return 404"]
    
    B -->|Server Error| H["Log Exception"]
    H -->|Retry| I["Polly Policy"]
    I -->|Success| D
    I -->|Fails| J["Return 500"]
    I -->|Rate Limited| K["Return 429"]
    
    style H fill:#ff6b6b
    style I fill:#ffd43b
    style J fill:#ff6b6b
```

## State Machine - Sync Operation

```mermaid
stateDiagram-v2
    [*] --> Idle
    
    Idle -->|Trigger Sync| Authenticating
    Authenticating -->|Token Obtained| Querying
    Querying -->|Data Retrieved| Processing
    
    Processing -->|Success| Checkpointing
    Processing -->|Failure| ErrorHandling
    
    ErrorHandling -->|Recoverable| Retry
    ErrorHandling -->|Unrecoverable| MarkFailed
    
    Retry -->|Success| Checkpointing
    Retry -->|Still Failing| MarkFailed
    
    Checkpointing -->|Saved| Completed
    MarkFailed -->|Logged| Completed
    Completed --> Idle
    
    note right of Processing
        Batches customers
        Validates data
        Saves to DB
        Publishes events
    end
    
    note right of ErrorHandling
        Logs error
        Records failure
        Determines action
    end
```

## Database Schema

```mermaid
erDiagram
    CUSTOMERS {
        int Id PK
        string SalesforceId UK
        string Name
        string Email
        string Phone
        string Industry
        string Website
        datetime LastSyncedAt
        datetime LastModifiedInSalesforce
        boolean IsDeleted
        datetime CreatedAt
        datetime UpdatedAt
    }
    
    SYNC_FAILURES {
        int Id PK
        string EntityType
        string ExternalId
        string ErrorMessage
        string StackTrace
        int RetryCount
        int MaxRetries
        boolean IsResolved
        datetime ResolvedAt
        datetime CreatedAt
    }
    
    SYNC_CHECKPOINTS {
        int Id PK
        string SyncType
        string EntityType
        datetime LastSyncTime
        string LastSyncedId
        int ProcessedCount
        int FailedCount
        datetime UpdatedAt
    }
    
    AUDIT_LOGS {
        int Id PK
        string EntityType
        int EntityId
        string Action
        string Changes
        string UserId
        datetime CreatedAt
    }
    
    CUSTOMERS ||--o{ SYNC_FAILURES : "has"
    CUSTOMERS ||--o{ AUDIT_LOGS : "recorded in"
    SYNC_CHECKPOINTS ||--|{ CUSTOMERS : "tracks"
```

## Authentication Flow - Auth0

```mermaid
sequenceDiagram
    participant User as User
    participant App as Angular App
    participant Auth0 as Auth0 Server
    participant API as .NET API
    
    User->>App: 1. Click Login
    App->>Auth0: 2. Redirect to Login
    Auth0->>User: 3. Show Login Form
    User->>Auth0: 4. Enter Credentials
    Auth0->>App: 5. Redirect with Auth Code
    App->>Auth0: 6. Exchange Code for Token
    Auth0-->>App: 7. JWT Token
    App->>API: 8. API Request + Token
    API->>API: 9. Validate JWT Signature
    API->>API: 10. Check Claims & Scopes
    API-->>App: 11. 200 OK (if valid)
    App->>User: 12. User Authenticated
```

## Deployment Architecture

```mermaid
graph TB
    subgraph "Development"
        DEV["Local Machine<br/>Docker Compose<br/>SQL Server<br/>RabbitMQ"]
    end
    
    subgraph "CI/CD Pipeline"
        GITHUB["GitHub Repo<br/>(Push/PR)"]
        ACTIONS["GitHub Actions<br/>Build<br/>Test<br/>Quality Check"]
        REGISTRY["Container Registry<br/>(Docker Image)"]
    end
    
    subgraph "Azure Production"
        PROD["App Service<br/>(Auto-scale)<br/>.NET 8 Runtime"]
        PRODSQL["SQL Database<br/>(Geo-redundant)<br/>Automated Backups"]
        PRODSB["Service Bus<br/>(Standard Tier)<br/>Message Topics"]
        PRODKV["Key Vault<br/>(HSM)"]
        PRODAI["App Insights<br/>(Analytics)"]
    end
    
    DEV -->|Test Locally| GITHUB
    GITHUB -->|Webhook| ACTIONS
    ACTIONS -->|Build & Test| REGISTRY
    REGISTRY -->|Deploy| PROD
    
    PROD -->|Queries| PRODSQL
    PROD -->|Publishes| PRODSB
    PROD -->|Retrieves Secrets| PRODKV
    PROD -->|Sends Telemetry| PRODAI
    
    style DEV fill:#ffd43b
    style PROD fill:#0078d4
    style ACTIONS fill:#512bd4
    style REGISTRY fill:#ff6b6b
```

## Class Hierarchy

```mermaid
graph TD
    Base["BaseEntity<br/>+ Id<br/>+ CreatedAt<br/>+ UpdatedAt"]
    
    Base -->|Inherits| Customer["Customer<br/>+ SalesforceId<br/>+ Name<br/>+ Email<br/>+ Phone"]
    Base -->|Inherits| SyncFailure["SyncFailure<br/>+ EntityType<br/>+ ErrorMessage<br/>+ RetryCount<br/>+ IsResolved"]
    Base -->|Inherits| SyncCheckpoint["SyncCheckpoint<br/>+ SyncType<br/>+ LastSyncTime<br/>+ ProcessedCount"]
    Base -->|Inherits| AuditLog["AuditLog<br/>+ Action<br/>+ Changes<br/>+ UserId"]
    
    Interface1["ISalesforceClient<br/>+ Authenticate<br/>+ GetCustomer<br/>+ CreateCustomer<br/>+ QueryCustomers"]
    Impl1["SalesforceClient<br/>(Implements)"]
    Interface1 -.->|Implements| Impl1
    
    Interface2["ICustomerRepository<br/>+ GetById<br/>+ Create<br/>+ Update<br/>+ Delete"]
    Impl2["CustomerRepository<br/>(Implements)"]
    Interface2 -.->|Implements| Impl2
    
    style Customer fill:#0078d4
    style Interface1 fill:#00a1de
    style Interface2 fill:#00a1de
```

## Load Balancing & Scaling

```mermaid
graph TB
    Traffic["Incoming Traffic"]
    LB["Azure Load Balancer<br/>(Geographic Distribution)"]
    
    LB -->|Route| Instance1["App Service Instance 1<br/>(East US)"]
    LB -->|Route| Instance2["App Service Instance 2<br/>(East US)"]
    LB -->|Route| Instance3["App Service Instance 3<br/>(Auto-scaled)"]
    
    Instance1 -->|Connection Pool| DB["SQL Database<br/>(Connection Pooling)"]
    Instance2 -->|Connection Pool| DB
    Instance3 -->|Connection Pool| DB
    
    Instance1 -->|Cache| Cache["In-Memory Cache<br/>(Token Cache)"]
    Instance2 -->|Cache| Cache
    Instance3 -->|Cache| Cache
    
    Traffic --> LB
    
    style LB fill:#0078d4
    style Instance1 fill:#512bd4
    style Instance2 fill:#512bd4
    style Instance3 fill:#512bd4
    style DB fill:#0078d4
    style Cache fill:#ffd43b
```

## Component Interactions

```mermaid
graph LR
    Client["Client<br/>(Angular)"]
    
    Client -->|REST API| Controller["Controller<br/>(Routing)"]
    Controller -->|Business Logic| Service["Service<br/>(SyncService)"]
    Service -->|Orchestration| SF["SalesforceClient"]
    Service -->|Data Access| Repo["Repository"]
    Service -->|Messaging| Bus["ServiceBus"]
    
    Repo -->|ORM| DbContext["DbContext<br/>(EF Core)"]
    DbContext -->|SQL| Database["Database<br/>(SQL Server)"]
    
    SF -->|OAuth2| SFOrg["Salesforce Org"]
    
    Bus -->|Publish| Topics["Event Topics"]
    
    style Service fill:#512bd4
    style SF fill:#00a1de
    style Bus fill:#ff6b6b
    style Database fill:#0078d4
```

---

**Key Takeaways:**

1. **Layered Architecture**: Clear separation of concerns (Controllers → Services → Data)
2. **Resilience**: Multiple retry mechanisms and error handling
3. **Scalability**: Horizontal scaling with load balancing
4. **Security**: Key Vault for secrets, JWT for auth
5. **Observability**: Application Insights for monitoring
6. **Cloud-Native**: Leverages Azure managed services
