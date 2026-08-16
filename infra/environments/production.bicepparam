using '../main.bicep'

param location = 'ukwest'
param environmentName = 'production'
param appName = 'cloudorders-prod-api'
param acrName = 'cloudordersp583431prodacr'
param managedEnvironmentName = 'cloudorders-prod-env'
param logAnalyticsName = 'cloudorders-prod-logs'
param containerImage = 'mcr.microsoft.com/k8se/quickstart:latest'
param useAcr = false
param createAcrPullRole = false
