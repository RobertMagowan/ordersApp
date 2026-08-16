@description('Azure region for the workspace.')
param location string

@description('Log Analytics workspace name.')
param name string

@description('Resource tags.')
param tags object

module workspace 'br/avm:res/operational-insights/workspace:0.8.0' = {
  name: 'logAnalyticsWorkspace'
  params: {
    dataRetention: 30
    location: location
    name: name
    tags: tags
    useResourcePermissions: true
  }
}

output resourceId string = workspace.outputs.resourceId
