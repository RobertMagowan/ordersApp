using '../main.bicep'

param location = 'ukwest'
param environmentName = 'development'
param appName = 'cloudorders-dev-api'
param acrName = 'cloudordersd583431devacr'
param managedEnvironmentName = 'cloudorders-dev-env'
param logAnalyticsName = 'cloudorders-dev-logs'
param containerImage = 'mcr.microsoft.com/k8se/quickstart:latest'
param useAcr = false
param createAcrPullRole = false
param deploySql = false
param sqlServerName = 'cloudordersd583431devsql'
param sqlDatabaseName = 'CloudOrders'
param migrationIdentityName = 'cloudorders-dev-migrator'
param migrationJobName = 'cloudorders-dev-migrations'
