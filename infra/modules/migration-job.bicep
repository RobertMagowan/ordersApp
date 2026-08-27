@description('Azure region for the Container Apps Job.')
param location string

@description('Container Apps Job name.')
param name string

@description('Create the private-image migration job after its identity and registry access are provisioned.')
param createJob bool

@description('Container Apps managed environment resource ID.')
param managedEnvironmentResourceId string

@description('User-assigned identity name for database migrations.')
param migrationIdentityName string

@description('Migration runner container image.')
param migrationImage string

@description('ACR login server.')
param registryLoginServer string

@description('Azure Container Registry name for the migration image.')
param registryName string

@description('Managed-identity SQL connection string without secret material.')
param sqlConnectionString string

@description('Tags applied to the job and identity.')
param tags object

resource migrationIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  location: location
  name: migrationIdentityName
  tags: tags
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

resource migrationAcrPullRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, migrationIdentity.id, 'AcrPull')
  scope: registry
  properties: {
    principalId: migrationIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

resource migrationJob 'Microsoft.App/jobs@2024-03-01' = if (createJob) {
  dependsOn: [
    migrationAcrPullRole
  ]
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${migrationIdentity.id}': {}
    }
  }
  location: location
  name: name
  properties: {
    configuration: {
      manualTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
      }
      registries: [
        {
          identity: migrationIdentity.id
          server: registryLoginServer
        }
      ]
      replicaRetryLimit: 0
      replicaTimeout: 600
      triggerType: 'Manual'
    }
    environmentId: managedEnvironmentResourceId
    template: {
      containers: [
        {
          env: [
            {
              name: 'ConnectionStrings__CloudOrders'
              value: sqlConnectionString
            }
          ]
          image: migrationImage
          name: 'migrations'
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
        }
      ]
    }
  }
  tags: tags
}

output name string = createJob ? migrationJob.name : ''
output resourceId string = createJob ? migrationJob.id : ''
output identityResourceId string = migrationIdentity.id
output identityClientId string = migrationIdentity.properties.clientId
output identityPrincipalId string = migrationIdentity.properties.principalId
