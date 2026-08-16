targetScope = 'resourceGroup'

@description('Azure region for the MVP resources.')
param location string = resourceGroup().location

@description('Stable environment label used in resource tags and names.')
param environmentName string = 'development'

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

@description('Resource tags applied to every resource.')
param tags object = {
  application: 'CloudOrders'
  environment: environmentName
  managedBy: 'Bicep'
}

module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    location: location
    name: logAnalyticsName
    tags: tags
  }
}

module registryModule 'modules/container-registry.bicep' = {
  name: 'containerRegistry'
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

module containerApp 'modules/container-app.bicep' = {
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
    tags: tags
    targetPort: targetPort
    useAcr: useAcr
  }
}

// The AVM Container App roleAssignments input is scoped to the app. AcrPull must be scoped to the registry.
resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

resource acrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (useAcr && createAcrPullRole) {
  name: guid(registry.id, appName, 'AcrPull')
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
    principalId: containerApp.outputs.principalId
    principalType: 'ServicePrincipal'
  }
}

output containerAppName string = containerApp.outputs.name
output containerAppFqdn string = containerApp.outputs.fqdn
output registryName string = registryModule.outputs.name
output registryLoginServer string = registryModule.outputs.loginServer
