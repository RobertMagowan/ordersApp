@description('Azure region for the SQL logical server.')
param location string

@description('Globally unique Azure SQL logical server name.')
param name string

@description('Temporary Microsoft Entra administrator login name.')
param administratorLogin string

@description('Object ID of the temporary Microsoft Entra SQL administrator.')
param administratorObjectId string

@description('Microsoft Entra tenant ID of the SQL administrator.')
param tenantId string

@description('Tags applied to the SQL logical server.')
param tags object

module sqlServer 'br/avm:res/sql/server:0.22.0' = {
  name: 'cloudOrdersSqlServerAvm'
  params: {
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: administratorLogin
      principalType: 'User'
      sid: administratorObjectId
      tenantId: tenantId
    }
    auditSettings: {}
    enableTelemetry: false
    firewallRules: [
      {
        endIpAddress: '0.0.0.0'
        name: 'AllowAllWindowsAzureIps'
        startIpAddress: '0.0.0.0'
      }
    ]
    location: location
    minimalTlsVersion: '1.2'
    name: name
    publicNetworkAccess: 'Enabled'
    tags: tags
  }
}

output name string = sqlServer.outputs.name
output resourceId string = sqlServer.outputs.resourceId
output fullyQualifiedDomainName string = sqlServer.outputs.fullyQualifiedDomainName
