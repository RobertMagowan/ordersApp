targetScope = 'resourceGroup'

@description('Azure region for the MVP resources.')
param location string = resourceGroup().location

@description('Stable environment label used in resource tags and names.')
param environmentName string = 'development'

@description('Immutable source release identifier applied to the Container App.')
param releaseId string = 'bootstrap'

@description('Globally unique Container App name.')
param appName string

@description('Globally unique Azure Container Registry name.')
param acrName string

@description('Container Apps managed environment name.')
param managedEnvironmentName string

@description('Log Analytics workspace name.')
param logAnalyticsName string

@description('Full container image reference. The public placeholder is used for the first infrastructure pass.')
param containerImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Use the Container App managed identity to pull the image from this template-created registry.')
param useAcr bool = false

@description('Create or update the API Container App. SQL bootstrap phases keep the current API release unchanged.')
param deployContainerApp bool = true

@description('Create the AcrPull role assignment from Bicep. The GitHub workflow bootstraps it idempotently with Azure CLI.')
param createAcrPullRole bool = false

@description('Minimum number of always-on replicas for the MVP.')
param minReplicas int = 1

@description('Maximum number of replicas for the MVP.')
param maxReplicas int = 2

@description('HTTP target port exposed by the current container image.')
param targetPort int = 8080

@description('Enable the API liveness probe. Disable only for the public bootstrap image.')
param enableLivenessProbe bool = true

@description('Deploy Azure SQL only to development or test during Sprint 3.')
param deploySql bool = false

@description('Globally unique Azure SQL logical server name.')
param sqlServerName string = ''

@description('CloudOrders Azure SQL database name.')
param sqlDatabaseName string = 'CloudOrders'

@description('User-assigned identity name for migration execution.')
param migrationIdentityName string = ''

@description('Container Apps Job name for migration execution.')
param migrationJobName string = ''

@description('Create the private-image migration job after its identity and registry access are provisioned.')
param deployMigrationJob bool = true

@description('Immutable migration-runner container image.')
param migrationImage string = 'mcr.microsoft.com/k8se/quickstart:latest'

@description('Temporary Microsoft Entra SQL administrator login name.')
param sqlAdministratorLogin string = ''

@description('Object ID of the temporary Microsoft Entra SQL administrator.')
param sqlAdministratorObjectId string = ''

@description('Microsoft Entra tenant ID for the temporary SQL administrator.')
param sqlAdministratorTenantId string = ''

@description('Resource tags applied to every resource.')
param tags object = {
  application: 'CloudOrders'
  environment: environmentName
  managedBy: 'Bicep'
}

var containerAppTags = union(tags, {
  release: releaseId
})

var sqlDeploymentEnabled = deploySql
  ? (environmentName == 'production'
      ? fail('Sprint 3 Azure SQL deployment is restricted to development and test.')
      : true)
  : false
var sqlTags = union(tags, {
  expiresOn: '2026-09-10'
  firewallException: 'AllowAllWindowsAzureIps'
  owner: 'Robert Magowan'
  removalSprint: '7'
})

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    location: location
    name: logAnalyticsName
    tags: tags
  }
}

module registryModule 'modules/container-registry.bicep' = {
  name: 'containerRegistry-${uniqueString(resourceGroup().id)}'
  params: {
    location: location
    name: acrName
    tags: tags
  }
}

module managedEnvironment 'modules/container-app-environment.bicep' = {
  name: 'managedEnvironment'
  params: {
    location: location
    logAnalyticsWorkspaceResourceId: observability.outputs.resourceId
    name: managedEnvironmentName
    tags: tags
  }
}

module sqlServer 'modules/sql-server.bicep' = if (sqlDeploymentEnabled) {
  name: 'cloudOrdersSqlServer'
  params: {
    administratorLogin: sqlAdministratorLogin
    administratorObjectId: sqlAdministratorObjectId
    location: location
    name: sqlServerName
    tags: sqlTags
    tenantId: sqlAdministratorTenantId
  }
}

module sqlDatabase 'modules/sql-database.bicep' = if (sqlDeploymentEnabled) {
  name: 'cloudOrdersSqlDatabase'
  params: {
    location: location
    name: sqlDatabaseName
    serverName: sqlServer!.outputs.name
    tags: sqlTags
  }
}

var apiSqlConnectionString = sqlDeploymentEnabled
  ? 'Server=tcp:${sqlServer!.outputs.fullyQualifiedDomainName},1433;Initial Catalog=${sqlDatabase!.outputs.name};Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=Active Directory Managed Identity'
  : ''

module migrationJob 'modules/migration-job.bicep' = if (sqlDeploymentEnabled) {
  name: 'cloudOrdersMigrationJob'
  params: {
    createJob: deployMigrationJob
    location: location
    managedEnvironmentResourceId: managedEnvironment.outputs.resourceId
    migrationIdentityName: migrationIdentityName
    migrationImage: migrationImage
    name: migrationJobName
    registryLoginServer: registryModule.outputs.loginServer
    registryName: registryModule.outputs.name
    sqlConnectionString: apiSqlConnectionString
    tags: sqlTags
  }
}

module containerApp 'modules/container-app.bicep' = if (deployContainerApp) {
  name: 'containerApp'
  params: {
    containerImage: containerImage
    enableLivenessProbe: enableLivenessProbe
    environmentResourceId: managedEnvironment.outputs.resourceId
    location: location
    maxReplicas: maxReplicas
    minReplicas: minReplicas
    name: appName
    registryLoginServer: registryModule.outputs.loginServer
    tags: containerAppTags
    targetPort: targetPort
    useAcr: useAcr
    sqlConnectionString: apiSqlConnectionString
  }
}

// The AVM Container App roleAssignments input is scoped to the app. AcrPull must be scoped to the registry.
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (deployContainerApp && useAcr && createAcrPullRole) {
  name: guid(registry.id, appName, 'AcrPull')
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: containerApp!.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

output containerAppName string = deployContainerApp ? containerApp!.outputs.name : ''
output containerAppFqdn string = deployContainerApp ? containerApp!.outputs.fqdn : ''
output registryName string = registryModule.outputs.name
output registryLoginServer string = registryModule.outputs.loginServer
output releaseId string = releaseId
output sqlServerFqdn string = sqlDeploymentEnabled ? sqlServer!.outputs.fullyQualifiedDomainName : ''
output databaseName string = sqlDeploymentEnabled ? sqlDatabase!.outputs.name : ''
output migrationJobName string = sqlDeploymentEnabled && deployMigrationJob ? migrationJob!.outputs.name : ''
output migrationIdentityClientId string = sqlDeploymentEnabled ? migrationJob!.outputs.identityClientId : ''
