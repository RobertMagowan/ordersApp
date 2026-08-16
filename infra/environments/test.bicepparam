using '../main.bicep'

param location = 'ukwest'
param environmentName = 'test'
param releaseId = 'bootstrap'
param appName = 'cloudorders-test-api'
param acrName = 'cloudorderst583431testacr'
param managedEnvironmentName = 'cloudorders-test-env'
param logAnalyticsName = 'cloudorders-test-logs'
param containerImage = 'mcr.microsoft.com/k8se/quickstart:latest'
param useAcr = false
param createAcrPullRole = false
