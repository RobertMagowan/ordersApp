using '../main.bicep'

// Override these values in the deployment command or replace the placeholders before use.
param location = 'uksouth'
param environmentName = 'development'
param appName = 'cloudorders-dev-api'
param acrName = 'cloudordersdevacr'
param managedEnvironmentName = 'cloudorders-dev-env'
param logAnalyticsName = 'cloudorders-dev-logs'
param containerImage = 'mcr.microsoft.com/k8se/quickstart:latest'
param useAcr = false
