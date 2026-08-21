// line-hf-bot — one-click Azure Container Apps deployment.
//
// Runs the published Docker Hub image on Azure Container Apps (ACA), which gives the
// bot the public HTTPS endpoint LINE requires. This template backs the "Deploy to Azure"
// button in the README; the portal form is defined in createUiDefinition.json.
//
// IMPORTANT — single instance only. Conversation history and generated media are kept
// in memory, so the app is pinned to exactly one always-on replica (min = max = 1).
// A second replica would have separate memory and break /media/{id} URLs and state;
// scale-to-zero would drop all state on every idle period.

@description('Azure region for all resources. Defaults to the resource group location.')
param location string = resourceGroup().location

@description('Name for the Container App and the basis for its public hostname.')
@minLength(2)
@maxLength(32)
param appName string = 'line-hf-bot'

@description('Container image to run. Use a pinned tag (e.g. :1.0.0) for reproducible deploys.')
param image string = 'docker.io/pierre3/line-hf-bot:latest'

// --- Secrets (stored as Container App secrets, never as plain env values) ---

@description('LINE Messaging API channel secret.')
@secure()
param lineChannelSecret string

@description('LINE Messaging API channel access token (long-lived).')
@secure()
param lineChannelAccessToken string

@description('Hugging Face access token with Inference Providers permission.')
@secure()
param huggingFaceApiKey string

// --- Main toggles (defaults mirror LineHfBot/Configuration/BotOptions.cs) ---

@description('UI language for user-facing text and the rich menu.')
@allowed([
  'en'
  'ja'
])
param locale string = 'en'

@description('Enable video (/video text-to-video and the "Make a video" image-to-video). Both run on the credit-heavy, slow fal-ai provider, so this is off by default.')
param videoEnabled bool = false

@description('Enable vision Q&A on sent photos and generated images. Uses chat-level HF credits (not fal).')
param visionEnabled bool = true

// --- Compute sizing ---

@description('vCPU cores for the single replica.')
@allowed([
  '0.25'
  '0.5'
  '0.75'
  '1.0'
  '2.0'
])
param cpu string = '0.5'

@description('Memory for the single replica. Must pair with the CPU value per ACA allowed combinations.')
@allowed([
  '0.5Gi'
  '1.0Gi'
  '1.5Gi'
  '2.0Gi'
  '4.0Gi'
])
param memory string = '1.0Gi'

// The app always listens on 8080 (base image sets ASPNETCORE_HTTP_PORTS=8080).
var targetPort = 8080

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: '${appName}-logs'
  location: location
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

resource managedEnv 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${appName}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalytics.properties.customerId
        sharedKey: logAnalytics.listKeys().primarySharedKey
      }
    }
  }
}

// The app's FQDN is deterministic once the environment exists:
//   <appName>.<managedEnv.defaultDomain>
// so App__PublicBaseUrl can be set up-front, making this a true one-pass deploy
// (no post-create patch needed). The FQDN is then stable for the life of the app.
var appFqdn = '${appName}.${managedEnv.properties.defaultDomain}'
var publicBaseUrl = 'https://${appFqdn}'

resource containerApp 'Microsoft.App/containerApps@2024-03-01' = {
  name: appName
  location: location
  properties: {
    managedEnvironmentId: managedEnv.id
    configuration: {
      ingress: {
        external: true
        targetPort: targetPort
        transport: 'auto'
      }
      secrets: [
        {
          name: 'line-channel-secret'
          value: lineChannelSecret
        }
        {
          name: 'line-channel-access-token'
          value: lineChannelAccessToken
        }
        {
          name: 'huggingface-api-key'
          value: huggingFaceApiKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: appName
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: [
            {
              name: 'Line__ChannelSecret'
              secretRef: 'line-channel-secret'
            }
            {
              name: 'Line__ChannelAccessToken'
              secretRef: 'line-channel-access-token'
            }
            {
              name: 'HuggingFace__ApiKey'
              secretRef: 'huggingface-api-key'
            }
            {
              name: 'App__PublicBaseUrl'
              value: publicBaseUrl
            }
            {
              name: 'App__Locale'
              value: locale
            }
            {
              name: 'App__VideoEnabled'
              value: string(videoEnabled)
            }
            {
              name: 'App__VisionEnabled'
              value: string(visionEnabled)
            }
          ]
        }
      ]
      // Single always-on replica — required by the in-memory architecture.
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

@description('Public HTTPS base URL of the deployed app.')
output appUrl string = publicBaseUrl

@description('Webhook URL to register in the LINE Developers console.')
output lineWebhookUrl string = '${publicBaseUrl}/webhook'
