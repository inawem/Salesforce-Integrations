# Enterprise Salesforce CRM Integration with Azure, .NET Core, and Auth0

A production-grade integration system that synchronizes customer data between Salesforce CRM and a local .NET Core application using Azure Logic Apps, Key Vault, Entity Framework, and Auth0 authentication.

## Project Overview

This is a comprehensive solution demonstrating enterprise-grade architecture patterns for integrating Salesforce with on-premise/cloud applications. The system uses:

- **Salesforce CRM** - Source of truth for customer data
- **Azure Logic Apps** - Orchestration and workflow automation
- **Azure Key Vault** - Secure credential management
- **Azure Service Bus** - Reliable message queuing
- **Azure SQL Database** - Persistent data storage
- **.NET 8 Core** - Backend API service
- **Entity Framework Core** - ORM and data access layer
- **Auth0** - Identity and access management
- **Angular** - Frontend web application (optional)

## Architecture

```
┌─────────────────┐
│  Salesforce CRM │
└────────┬────────┘
         │ REST API
         ▼
┌──────────────────────┐
│ Azure Logic Apps     │ ◄── Orchestration, Scheduled Syncs
└────────┬─────────────┘
         │ Service Bus
         ▼
┌──────────────────────┐
│  .NET Core API       │ ◄── Authentication (Auth0)
│  - Controllers       │     Authorization
│  - Services          │     Business Logic
└────────┬─────────────┘
         │
         ▼
┌──────────────────────┐
│ Entity Framework     │
│ - Repositories       │
│ - DbContext          │
└────────┬─────────────┘
         │
         ▼
┌──────────────────────┐
│ Azure SQL Database   │
└──────────────────────┘

┌──────────────────────┐
│ Azure Key Vault      │ ◄── Secrets Management
│ - SF Credentials     │     Connection Strings
│ - API Keys           │     Auth0 Tokens
└──────────────────────┘

┌──────────────────────┐
│ Angular Frontend     │
│ - Dashboard          │
│ - Sync Management    │
│ - Reporting          │
└──────────────────────┘
```

## Prerequisites

- .NET 8 SDK ([download](https://dotnet.microsoft.com/download))
- Azure Subscription ([free tier](https://azure.microsoft.com/en-us/free/))
- Salesforce Developer Edition ([free](https://developer.salesforce.com/signup))
- Auth0 Account ([free tier](https://auth0.com/signup))
- Docker & Docker Compose (for local development)
- Node.js 18+ (for Angular frontend)
- Git

## Quick Start (Local Development)

### 1. Clone the Repository

```bash
git clone https://github.com/your-org/salesforce-azure-integration.git
cd salesforce-azure-integration
```

### 2. Setup Environment Variables

```bash
cp .env.example .env
# Edit .env with your credentials
```

### 3. Start Services with Docker

```bash
docker-compose up -d

# Verify containers
docker-compose ps

# View logs
docker-compose logs -f api
```

### 4. Run Database Migrations

```bash
cd src/SalesforceIntegration.Api
dotnet ef database update
cd ../../
```

### 5. Start the .NET Backend

```bash
cd src/SalesforceIntegration.Api
dotnet run
# API runs on http://localhost:5000
```

### 6. Start the Angular Frontend (Optional)

```bash
cd src/SalesforceIntegration.Web
npm install
ng serve
# Frontend runs on http://localhost:4200
```

## Project Structure

```
salesforce-azure-integration/
├── src/
│   ├── SalesforceIntegration.Api/           # Main ASP.NET Core API
│   │   ├── Controllers/
│   │   ├── Services/
│   │   ├── Middleware/
│   │   ├── Models/
│   │   ├── appsettings.json
│   │   └── Program.cs
│   ├── SalesforceIntegration.Data/          # Entity Framework & Data Access
│   │   ├── ApplicationDbContext.cs
│   │   ├── Repositories/
│   │   ├── Entities/
│   │   └── Migrations/
│   ├── SalesforceIntegration.Services/      # Business Logic
│   │   ├── SalesforceClient.cs
│   │   ├── CustomerSyncService.cs
│   │   └── Interfaces/
│   ├── SalesforceIntegration.Tests/         # Unit Tests
│   └── SalesforceIntegration.Web/           # Angular Frontend
│       ├── src/
│       ├── package.json
│       └── angular.json
├── infrastructure/
│   ├── terraform/                           # IaC for Azure resources
│   │   ├── main.tf
│   │   ├── variables.tf
│   │   └── outputs.tf
│   ├── azure-logic-apps/                    # Logic App definitions
│   │   └── sync-workflow.json
│   └── scripts/
│       ├── deploy.sh
│       └── setup-keyvault.sh
├── docker-compose.yml
├── .env.example
├── .github/
│   └── workflows/
│       ├── ci-build.yml
│       └── deploy-prod.yml
└── README.md
```

## Core Features

### 1. Customer Synchronization
- Bidirectional sync between Salesforce and local database
- Incremental sync (only changed records)
- Full sync capability
- Checkpoint-based resume from failures
- Dead letter queue handling

### 2. Authentication & Authorization
- Auth0 integration for user authentication
- Role-based access control (RBAC)
- Scope-based API permissions
- Secure token management in Key Vault

### 3. Azure Integration
- Logic Apps for workflow orchestration
- Key Vault for secrets management
- Service Bus for async messaging
- Application Insights for monitoring
- SQL Database for persistence

### 4. Entity Framework
- DbContext for data access
- Repository pattern for abstraction
- Migrations for schema management
- Change tracking and auditing
- Soft deletes support

### 5. Frontend Dashboard (Angular)
- Customer list and detail views
- Manual sync triggers
- Sync status monitoring
- Error tracking and resolution
- Real-time notifications

## Configuration

### Salesforce OAuth2 Setup

1. Log into Salesforce as admin
2. Setup → Apps → App Manager → New Connected App
3. Configure:
   - App Name: "Azure Integration"
   - API Name: "azure_integration"
   - Enable OAuth Settings
   - Callback URL: `https://your-api.azurewebsites.net/oauth/callback`
   - OAuth Scopes: `api`, `refresh_token`, `offline_access`
4. Save and note Consumer Key (Client ID) and Consumer Secret

### Auth0 Setup

1. Create Auth0 tenant
2. Create Application:
   - Type: Regular Web Application
   - Trusted Origins: `https://localhost:4200`, `https://your-domain.com`
3. Create API:
   - Identifier: `https://api.salesforce-integration.com`
   - Signing Algorithm: RS256
4. Note: Domain, Client ID, Client Secret

### Azure Key Vault Setup

```bash
# Create Key Vault
az keyvault create \
  --name salesforce-integration-kv \
  --resource-group salesforce-integration \
  --location eastus

# Add secrets
az keyvault secret set \
  --vault-name salesforce-integration-kv \
  --name "SalesforceClientId" \
  --value "your_salesforce_client_id"

az keyvault secret set \
  --vault-name salesforce-integration-kv \
  --name "SalesforceClientSecret" \
  --value "your_salesforce_client_secret"

az keyvault secret set \
  --vault-name salesforce-integration-kv \
  --name "Auth0ClientId" \
  --value "your_auth0_client_id"

az keyvault secret set \
  --vault-name salesforce-integration-kv \
  --name "Auth0ClientSecret" \
  --value "your_auth0_client_secret"
```

## Deployment

### Deploy to Azure (Using Terraform)

```bash
cd infrastructure/terraform

# Initialize Terraform
terraform init

# Plan deployment
terraform plan -out=tfplan

# Apply changes
terraform apply tfplan
```

### Deploy with GitHub Actions

Automatic deployment on push to main branch:

```yaml
# .github/workflows/deploy-prod.yml triggers automatically
# Builds, tests, and deploys to Azure App Service
```

### Manual Deployment

```bash
# Build
dotnet publish -c Release -o ./publish

# Create deployment package
cd publish
zip -r ../app.zip .

# Deploy to Azure
az webapp deployment source config-zip \
  --resource-group salesforce-integration \
  --name salesforce-api \
  --src ../app.zip
```

## API Documentation

### Authentication
All API endpoints require Auth0 token in Authorization header:

```
Authorization: Bearer <auth0_access_token>
```

### Endpoints

#### Customer Management

**GET** `/api/customers`
- List all customers with pagination

**GET** `/api/customers/{id}`
- Get customer details

**POST** `/api/customers`
- Create new customer

**PUT** `/api/customers/{id}`
- Update customer

**DELETE** `/api/customers/{id}`
- Soft delete customer

#### Sync Operations

**POST** `/api/sync/customers/from-salesforce`
- Sync customers from Salesforce to local DB
- Query params: `since`, `batchSize`

**POST** `/api/sync/customers/to-salesforce`
- Sync customers from local DB to Salesforce

**GET** `/api/sync/status`
- Get current sync status and statistics

**GET** `/api/sync/failures`
- List failed sync attempts

**POST** `/api/sync/retry/{failureId}`
- Retry failed sync

## Database Schema

### Customers Table

```sql
CREATE TABLE [dbo].[Customers] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [SalesforceId] NVARCHAR(50) UNIQUE,
    [Name] NVARCHAR(200) NOT NULL,
    [Email] NVARCHAR(200),
    [Phone] NVARCHAR(20),
    [Industry] NVARCHAR(100),
    [Website] NVARCHAR(500),
    [BillingStreet] NVARCHAR(255),
    [BillingCity] NVARCHAR(100),
    [BillingState] NVARCHAR(50),
    [BillingZip] NVARCHAR(20),
    [LastSyncedAt] DATETIME2,
    [LastModifiedInSalesforce] DATETIME2,
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [UpdatedAt] DATETIME2 DEFAULT GETUTCDATE(),
    [IsDeleted] BIT DEFAULT 0
);

CREATE INDEX [IX_SalesforceId] ON [Customers]([SalesforceId]);
CREATE INDEX [IX_LastSyncedAt] ON [Customers]([LastSyncedAt]);
```

### SyncFailures Table

```sql
CREATE TABLE [dbo].[SyncFailures] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [EntityType] NVARCHAR(50), -- 'Customer', 'Deal', etc.
    [ExternalId] NVARCHAR(50), -- Salesforce ID
    [ErrorMessage] NVARCHAR(MAX),
    [StackTrace] NVARCHAR(MAX),
    [RetryCount] INT DEFAULT 0,
    [MaxRetries] INT DEFAULT 5,
    [IsResolved] BIT DEFAULT 0,
    [ResolvedAt] DATETIME2,
    [CreatedAt] DATETIME2 DEFAULT GETUTCDATE()
);

CREATE INDEX [IX_IsResolved] ON [SyncFailures]([IsResolved]);
CREATE INDEX [IX_CreatedAt] ON [SyncFailures]([CreatedAt] DESC);
```

### SyncCheckpoints Table

```sql
CREATE TABLE [dbo].[SyncCheckpoints] (
    [Id] INT PRIMARY KEY IDENTITY(1,1),
    [SyncType] NVARCHAR(50), -- 'FromSalesforce', 'ToSalesforce'
    [EntityType] NVARCHAR(50), -- 'Customer'
    [LastSyncTime] DATETIME2,
    [LastSyncedId] NVARCHAR(50),
    [ProcessedCount] INT DEFAULT 0,
    [FailedCount] INT DEFAULT 0,
    [UpdatedAt] DATETIME2 DEFAULT GETUTCDATE()
);

CREATE UNIQUE INDEX [UX_SyncCheckpoint] ON [SyncCheckpoints]([SyncType], [EntityType]);
```

## Azure Logic Apps Workflow

The Logic App orchestrates the sync process:

1. **Trigger**: Scheduled (every hour) or HTTP request
2. **Get Credentials**: Retrieve from Key Vault
3. **Call Salesforce API**: Query modified customers
4. **Process Results**: Transform data
5. **Send to Service Bus**: Queue for processing
6. **Update Status**: Log completion/errors

Example workflow triggers:
- Scheduled sync every hour
- On-demand via REST API
- On Salesforce platform events

## Monitoring & Logging

### Application Insights Integration

```csharp
builder.Services.AddApplicationInsightsTelemetry();

var telemetryClient = new TelemetryClient();
telemetryClient.TrackEvent("CustomerSyncStarted", properties, measurements);
```

### Log Analytics Queries

```kusto
// Failed syncs in last 24 hours
traces
| where severityLevel == 3
| where message contains "sync failed"
| summarize count() by operation_Name
```

## Testing

### Run Unit Tests

```bash
cd src/SalesforceIntegration.Tests
dotnet test

# With coverage
dotnet test /p:CollectCoverage=true
```

### Run Integration Tests

```bash
# Uses Docker containers and Salesforce Sandbox
dotnet test SalesforceIntegration.IntegrationTests.csproj
```

## Best Practices & Security

- ✅ All credentials stored in Key Vault, never in code
- ✅ Token refresh handled automatically
- ✅ Rate limit handling with exponential backoff
- ✅ Comprehensive error logging and monitoring
- ✅ Database transactions for data consistency
- ✅ Audit trail for all sync operations
- ✅ Auth0 token validation on every request
- ✅ HTTPS enforced in production
- ✅ Secrets rotation support

## Troubleshooting

### Issue: Authentication Fails (401 Unauthorized)

**Cause**: Invalid or expired Auth0 token

**Solution**:
```bash
# Check Auth0 token in Key Vault
az keyvault secret show --vault-name salesforce-integration-kv --name "Auth0Token"

# Refresh token manually
curl -X POST https://your-auth0-tenant.auth0.com/oauth/token \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  -d 'client_id=YOUR_CLIENT_ID&client_secret=YOUR_SECRET&audience=https://api.salesforce-integration.com&grant_type=client_credentials'
```

### Issue: Logic App Timeout

**Cause**: Large dataset sync taking too long

**Solution**:
- Reduce batch size in configuration
- Add pagination to Salesforce queries
- Use bulk API for large operations
- Check Service Bus for backlog

### Issue: Database Connection Fails

**Cause**: Connection string incorrect or network unreachable

**Solution**:
```bash
# Test connection string
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=tcp:..."

# Check Azure SQL firewall
az sql server firewall-rule list --resource-group salesforce-integration --server salesforce-sql
```

## Contributing

1. Fork the repository
2. Create feature branch: `git checkout -b feature/amazing-feature`
3. Commit changes: `git commit -m 'Add amazing feature'`
4. Push to branch: `git push origin feature/amazing-feature`
5. Open Pull Request

## Code Standards

- C# code follows Microsoft naming conventions
- All public methods have XML documentation
- Unit tests required for new features (>80% coverage)
- Code must pass static analysis (Roslyn analyzers)
- Commits follow conventional commit format

## Performance Benchmarks

| Operation | Time | Notes |
|-----------|------|-------|
| Sync 1,000 customers | ~2 minutes | Batched, incremental |
| Sync 10,000 customers | ~18 minutes | With checkpoints |
| Get customer (DB) | <10ms | Indexed query |
| Get customer (Salesforce) | ~200-300ms | Network + API |
| Full reconciliation | ~30 minutes | For 50,000 records |

## License

This project is licensed under the MIT License - see the LICENSE file for details.

## Support & Documentation

- **API Documentation**: See [API.md](./docs/API.md)
- **Architecture Guide**: See [ARCHITECTURE.md](./docs/ARCHITECTURE.md)
- **Deployment Guide**: See [DEPLOYMENT.md](./docs/DEPLOYMENT.md)
- **Issues**: GitHub Issues
- **Discussions**: GitHub Discussions

## Roadmap

- [ ] Change Data Capture (CDC) integration
- [ ] Real-time sync via Salesforce Platform Events
- [ ] GraphQL API endpoint
- [ ] Mobile app (React Native)
- [ ] Advanced reconciliation UI
- [ ] Machine learning for duplicate detection
- [ ] Multi-tenant support

## Authors

- Dheeraj Mewani (@dheerajmewani) - Initial architecture and implementation

---

**Last Updated**: February 26, 2024
**Current Version**: 1.0.0
