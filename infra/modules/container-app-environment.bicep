@description('Azure region for the managed environment.')
param location string

@description('Container Apps managed environment name.')
param name string

@description('Log Analytics workspace resource ID.')
param logAnalyticsWorkspaceResourceId string

@description('Resource tags.')
param tags object

module managedEnvironment 'br/avm:res/app/managed-environment:0.8.1' = {
  name: 'containerAppsEnvironment'
  params: {
    location: location
    logAnalyticsWorkspaceResourceId: logAnalyticsWorkspaceResourceId
    logsDestination: 'log-analytics'
    name: name
    tags: tags
    zoneRedundant: false
  }
}

output resourceId string = managedEnvironment.outputs.resourceId
