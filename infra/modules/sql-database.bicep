@description('Azure region for the SQL database.')
param location string

@description('Azure SQL logical server name.')
param serverName string

@description('Database name.')
param name string

@description('Tags applied to the database.')
param tags object

module database 'br/avm:res/sql/server/database:0.3.0' = {
  name: 'cloudOrdersSqlDatabase'
  params: {
    autoPauseDelay: 60
    availabilityZone: -1
    enableTelemetry: false
    location: location
    minCapacity: '0.5'
    name: name
    serverName: serverName
    sku: {
      capacity: 1
      family: 'Gen5'
      name: 'GP_S_Gen5_1'
      tier: 'GeneralPurpose'
    }
    tags: tags
    zoneRedundant: false
  }
}

output name string = database.outputs.name
output resourceId string = database.outputs.resourceId
