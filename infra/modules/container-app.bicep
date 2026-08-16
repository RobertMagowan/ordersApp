@description('Azure region for the Container App.')
param location string

@description('Container App name.')
param name string

@description('Container Apps managed environment resource ID.')
param environmentResourceId string

@description('Full container image reference.')
param containerImage string

@description('Use the managed identity to pull from the template-created ACR.')
param useAcr bool

@description('Azure Container Registry login server.')
param registryLoginServer string

@description('HTTP target port exposed by the container image.')
param targetPort int

@description('Enable the API liveness probe.')
param enableLivenessProbe bool

@description('Minimum number of always-on replicas.')
param minReplicas int

@description('Maximum number of replicas.')
param maxReplicas int

@description('Resource tags.')
param tags object

var containers = [
  {
    name: 'api'
    image: containerImage
    resources: {
      cpu: '0.25'
      memory: '0.5Gi'
    }
    probes: enableLivenessProbe ? [
      {
        type: 'Liveness'
        httpGet: {
          path: '/health/live'
          port: targetPort
        }
        initialDelaySeconds: 10
        periodSeconds: 10
        failureThreshold: 3
      }
    ] : []
  }
]

module containerApp 'br/avm:res/app/container-app:0.11.0' = {
  name: 'cloudOrdersContainerApp'
  params: {
    activeRevisionsMode: 'Single'
    containers: containers
    environmentResourceId: environmentResourceId
    ingressAllowInsecure: false
    ingressExternal: true
    ingressTargetPort: targetPort
    ingressTransport: 'auto'
    location: location
    managedIdentities: {
      systemAssigned: true
    }
    name: name
    registries: useAcr ? [
      {
        identity: 'system'
        server: registryLoginServer
      }
    ] : []
    scaleMaxReplicas: maxReplicas
    scaleMinReplicas: minReplicas
    tags: tags
  }
}

output name string = containerApp.outputs.name
output resourceId string = containerApp.outputs.resourceId
output fqdn string = containerApp.outputs.fqdn
output principalId string = containerApp.outputs.systemAssignedMIPrincipalId
