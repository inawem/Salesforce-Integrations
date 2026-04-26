# Deployment Guide

Complete guide for deploying the Salesforce Integration system to Azure production.

## Prerequisites

- Azure Subscription ([free tier](https://azure.microsoft.com/free/))
- Azure CLI installed ([download](https://learn.microsoft.com/cli/azure/install-azure-cli))
- Terraform installed ([download](https://www.terraform.io/downloads.html))
- .NET 8 SDK ([download](https://dotnet.microsoft.com/download))
- Docker installed ([download](https://www.docker.com/products/docker-desktop))
- Git ([download](https://git-scm.com/download))

## Step 1: Prepare Azure Account

```bash
# Login to Azure
az login

# Set default subscription
az account set --subscription "YOUR_SUBSCRIPTION_ID"

# Create resource group
az group create \
  --name salesforce-integration-rg \
  --location eastus

# Verify
az group show --name salesforce-integration-rg
```

## Step 2: Setup Azure Key Vault

```bash
# Create Key Vault
az keyvault create \
  --name salesforce-kv \
  --resource-group salesforce-integration-rg \
  --location eastus \
  --enable-rbac-authorization true

# Add secrets
az keyvault secret set \
  --vault-name salesforce-kv \
  --name "SalesforceClientId" \
  --value "YOUR_CLIENT_ID"

az keyvault secret set \
  --vault-name salesforce-kv \
  --name "SalesforceClientSecret" \
  --value "YOUR_CLIENT_SECRET"

az keyvault secret set \
  --vault-name salesforce-kv \
  --name "Auth0Domain" \
  --value "YOUR_TENANT.auth0.com"

az keyvault secret set \
  --vault-name salesforce-kv \
  --name "Auth0ClientId" \
  --value "YOUR_AUTH0_CLIENT_ID"

az keyvault secret set \
  --vault-name salesforce-kv \
  --name "Auth0ClientSecret" \
  --value "YOUR_AUTH0_CLIENT_SECRET"

# Grant access to App Service (after creating it)
az keyvault set-policy \
  --name salesforce-kv \
  --object-id <APP_SERVICE_MANAGED_IDENTITY> \
  --secret-permissions get list
```

## Step 3: Setup Azure SQL Database

```bash
# Create SQL Server
az sql server create \
  --name salesforce-sql-server \
  --resource-group salesforce-integration-rg \
  --location eastus \
  --admin-user sqladmin \
  --admin-password "ComplexPassword123!Change!Me"

# Configure firewall rule for Azure services
az sql server firewall-rule create \
  --resource-group salesforce-integration-rg \
  --server salesforce-sql-server \
  --name AzureServices \
  --start-ip-address 0.0.0.0 \
  --end-ip-address 0.0.0.0

# Create database
az sql db create \
  --resource-group salesforce-integration-rg \
  --server salesforce-sql-server \
  --name SalesforceIntegration \
  --service-objective S0 \
  --edition standard

# Get connection string
az sql db show-connection-string \
  --server salesforce-sql-server \
  --name SalesforceIntegration \
  --client ado.net
```

## Step 4: Setup Azure Service Bus

```bash
# Create Service Bus Namespace
az servicebus namespace create \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-sb \
  --location eastus \
  --sku Standard

# Create queue for sync events
az servicebus queue create \
  --resource-group salesforce-integration-rg \
  --namespace-name salesforce-integration-sb \
  --name customer-sync-events \
  --max-delivery-count 5

# Get connection string
az servicebus namespace authorization-rule keys list \
  --resource-group salesforce-integration-rg \
  --namespace-name salesforce-integration-sb \
  --name RootManageSharedAccessKey
```

## Step 5: Setup Azure App Service

```bash
# Create App Service Plan
az appservice plan create \
  --name salesforce-integration-plan \
  --resource-group salesforce-integration-rg \
  --sku B2 \
  --is-linux

# Create Web App
az webapp create \
  --resource-group salesforce-integration-rg \
  --plan salesforce-integration-plan \
  --name salesforce-integration-api \
  --runtime "DOTNET|8.0"

# Enable Managed Identity
az webapp identity assign \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api

# Configure app settings
az webapp config appsettings set \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api \
  --settings \
    ConnectionStrings__DefaultConnection="Server=tcp:salesforce-sql-server.database.windows.net,1433;Initial Catalog=SalesforceIntegration;Persist Security Info=False;User ID=sqladmin;Password=YOUR_PASSWORD;MultipleActiveResultSets=False;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;" \
    Auth0__Domain="@Microsoft.KeyVault(SecretUri=https://salesforce-kv.vault.azure.net/secrets/Auth0Domain/)" \
    Auth0__ClientId="@Microsoft.KeyVault(SecretUri=https://salesforce-kv.vault.azure.net/secrets/Auth0ClientId/)" \
    Auth0__ClientSecret="@Microsoft.KeyVault(SecretUri=https://salesforce-kv.vault.azure.net/secrets/Auth0ClientSecret/)" \
    Salesforce__ClientId="@Microsoft.KeyVault(SecretUri=https://salesforce-kv.vault.azure.net/secrets/SalesforceClientId/)" \
    Salesforce__ClientSecret="@Microsoft.KeyVault(SecretUri=https://salesforce-kv.vault.azure.net/secrets/SalesforceClientSecret/)" \
    ASPNETCORE_ENVIRONMENT="Production" \
    APPLICATIONINSIGHTS_INSTRUMENTATION_KEY="YOUR_INSTRUMENTATION_KEY"

# Configure CORS
az webapp cors add \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api \
  --allowed-origins "https://yourdomain.com"
```

## Step 6: Setup Application Insights

```bash
# Create Application Insights
az monitor app-insights component create \
  --resource-group salesforce-integration-rg \
  --app salesforce-integration-insights \
  --application-type web \
  --location eastus

# Get instrumentation key
az monitor app-insights component show \
  --resource-group salesforce-integration-rg \
  --app salesforce-integration-insights
```

## Step 7: Deploy API

### Option A: Deploy from Local

```bash
# Build
cd src/SalesforceIntegration.Api
dotnet publish -c Release -o ../../publish

# Create deployment package
cd ../../publish
zip -r ../app.zip .
cd ..

# Deploy
az webapp deployment source config-zip \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api \
  --src app.zip

# Verify deployment
az webapp log show \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api
```

### Option B: Deploy from Docker Registry

```bash
# Build Docker image
docker build -t salesforce-integration:latest .

# Tag for registry
docker tag salesforce-integration:latest myregistry.azurecr.io/salesforce-integration:latest

# Push to Azure Container Registry
docker push myregistry.azurecr.io/salesforce-integration:latest

# Configure App Service
az webapp config container set \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api \
  --docker-custom-image-name myregistry.azurecr.io/salesforce-integration:latest \
  --docker-registry-server-url https://myregistry.azurecr.io
```

## Step 8: Configure Database Migrations

```bash
# Run migrations via App Service console or
# Execute via published app startup

# Or manually apply migrations:
cd src/SalesforceIntegration.Api
dotnet ef database update --startup-project . -- --connection "YOUR_CONNECTION_STRING"
```

## Step 9: Setup Azure Logic App

Create a Logic App for scheduled syncs:

```json
{
  "definition": {
    "$schema": "https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#",
    "contentVersion": "1.0.0.0",
    "triggers": {
      "Recurrence": {
        "type": "Recurrence",
        "recurrence": {
          "frequency": "Hour",
          "interval": 1
        }
      }
    },
    "actions": {
      "HTTP_Sync_from_Salesforce": {
        "type": "Http",
        "inputs": {
          "method": "POST",
          "uri": "https://salesforce-integration-api.azurewebsites.net/api/sync/from-salesforce",
          "headers": {
            "Authorization": "Bearer YOUR_TOKEN"
          }
        }
      },
      "Log_Result": {
        "type": "ApiConnection",
        "inputs": {
          "host": {
            "connection": {
              "name": "applicationinsights"
            }
          },
          "method": "patch",
          "path": "/events",
          "body": {
            "eventType": "ScheduledSyncCompleted",
            "details": "@body('HTTP_Sync_from_Salesforce')"
          }
        }
      }
    }
  }
}
```

## Step 10: Configure Monitoring & Alerts

```bash
# Create action group for alerts
az monitor action-group create \
  --resource-group salesforce-integration-rg \
  --name alert-action-group \
  --short-name SalesforceIntegration

# Add email receiver
az monitor action-group receiver email create \
  --resource-group salesforce-integration-rg \
  --action-group-name alert-action-group \
  --name admin-email \
  --email-receiver admin@example.com

# Create alert rule for high error rate
az monitor metrics alert create \
  --resource-group salesforce-integration-rg \
  --name HighErrorRateAlert \
  --scopes /subscriptions/YOUR_SUBSCRIPTION_ID/resourceGroups/salesforce-integration-rg/providers/microsoft.insights/components/salesforce-integration-insights \
  --condition "avg RequestsFailed > 10 in PT5M" \
  --window-size PT5M \
  --evaluation-frequency PT1M \
  --action action-group
```

## Step 11: Configure CI/CD (GitHub Actions)

Add secrets to GitHub repository:

```bash
# GitHub Settings > Secrets > New Repository Secret

AZURE_CREDENTIALS: (output from: az ad sp create-for-rbac)
SONAR_TOKEN: (from SonarCloud)
DOCKER_REGISTRY_USERNAME: (ACR credentials)
DOCKER_REGISTRY_PASSWORD: (ACR credentials)
```

## Step 12: Configure Custom Domain (Optional)

```bash
# Create SSL certificate
az appservice web bind-ssl-cert \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api \
  --certificate-thumbprint CERT_THUMBPRINT \
  --ssl-type SNI

# Add custom domain
az webapp config hostname add \
  --resource-group salesforce-integration-rg \
  --webapp-name salesforce-integration-api \
  --hostname api.yourdomain.com
```

## Verification Checklist

- [ ] Resource group created
- [ ] Key Vault configured with all secrets
- [ ] SQL Database created and accessible
- [ ] Service Bus namespace and queue created
- [ ] App Service plan and app created
- [ ] Managed Identity enabled
- [ ] App settings configured with Key Vault references
- [ ] Database migrations applied
- [ ] API deployed and accessible
- [ ] Health endpoint responding: `GET /health`
- [ ] Swagger UI available: `GET /swagger`
- [ ] Application Insights receiving telemetry
- [ ] Alerts configured
- [ ] Logic App scheduled sync working
- [ ] Auth0 token validation working
- [ ] Salesforce API calls successful

## Post-Deployment Steps

### 1. Test API Endpoints

```bash
# Get Auth0 token
TOKEN=$(curl -X POST https://YOUR_TENANT.auth0.com/oauth/token \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  -d 'client_id=YOUR_CLIENT_ID&client_secret=YOUR_SECRET&audience=https://api.salesforce-integration.com&grant_type=client_credentials' \
  | jq -r '.access_token')

# Test sync endpoint
curl -X POST https://salesforce-integration-api.azurewebsites.net/api/sync/from-salesforce \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json"

# Get sync status
curl -X GET https://salesforce-integration-api.azurewebsites.net/api/sync/status \
  -H "Authorization: Bearer $TOKEN"
```

### 2. Monitor Initial Syncs

- Check Application Insights for errors
- Review sync performance metrics
- Monitor database growth
- Verify Service Bus message throughput

### 3. Setup Backups

```bash
# Configure automated backups
az sql db update \
  --resource-group salesforce-integration-rg \
  --server salesforce-sql-server \
  --name SalesforceIntegration \
  --backup-retention-days 30 \
  --long-term-retention-weekly-retention P12W
```

### 4. Document & Train

- Create operational runbooks
- Document troubleshooting procedures
- Train support team
- Setup escalation procedures

## Rollback Procedure

```bash
# If deployment fails, rollback to previous version:

# Stop App Service
az webapp stop \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api

# Redeploy previous version
az webapp deployment source config-zip \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api \
  --src previous-app.zip

# Start App Service
az webapp start \
  --resource-group salesforce-integration-rg \
  --name salesforce-integration-api

# Verify health
curl https://salesforce-integration-api.azurewebsites.net/health
```

## Cost Optimization

- Use B-series for non-production
- Enable autoscaling for production
- Use reserved instances for predictable workloads
- Monitor and right-size resources
- Use shared App Service plans for non-critical apps

## Troubleshooting

### Common Issues

**502 Bad Gateway**
- Check app logs
- Verify connection string
- Check Key Vault access
- Restart app service

**Database Connection Timeout**
- Check firewall rules
- Verify connection string
- Check database status
- Review query performance

**Salesforce API Errors**
- Verify OAuth credentials
- Check rate limits
- Review API permissions
- Check instance URL

**Auth0 Token Issues**
- Verify audience claim
- Check token expiration
- Review scopes
- Verify Key Vault secret

## Support

For issues or questions:
1. Check Application Insights logs
2. Review Azure Activity Log
3. Check Salesforce API status
4. Review Auth0 dashboard
5. Contact support with logs and correlation IDs
