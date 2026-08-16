@description('Azure region for the registry.')
param location string

@description('Azure Container Registry name.')
param name string

@description('Resource tags.')
param tags object

module registry 'br/avm:res/container-registry/registry:0.6.0' = {
  name: 'containerRegistry'
  params: {
    acrAdminUserEnabled: false
    acrSku: 'Basic'
    location: location
    name: name
    publicNetworkAccess: 'Enabled'
    tags: tags
    zoneRedundancy: 'Disabled'
  }
}

output resourceId string = registry.outputs.resourceId
output name string = name
output loginServer string = registry.outputs.loginServer
