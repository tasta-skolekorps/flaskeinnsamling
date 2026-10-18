using Azure.Provisioning.AppContainers;
using Azure.Provisioning.Storage;

var builder = DistributedApplication.CreateBuilder(args);

// Shared registry in rg-common instead of one registry per app.
var acr = builder.AddAzureContainerRegistry("acr")
    .PublishAsExisting("leifssharedacr", "rg-common");

// No managed Aspire dashboard in Azure to keep cost down; use Log Analytics instead.
builder.AddAzureContainerAppEnvironment("env")
    .WithAzureContainerRegistry(acr)
    .WithDashboard(false);

var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(azurite => azurite.WithDataVolume())
    .ConfigureInfrastructure(infra =>
    {
        var account = infra.GetProvisionableResources().OfType<StorageAccount>().Single();
        account.Sku = new StorageSku { Name = StorageSkuName.StandardLrs };
    });

var tables = storage.AddTables("tables");

var adminApiKey = builder.AddParameter("admin-api-key", secret: true);
var corsOrigins = builder.AddParameter("cors-origins", "https://tasta-skolekorps.github.io", publishValueAsDefault: true);

var api = builder.AddProject<Projects.Flaskeinnsamling_Api>("api")
    .WithReference(tables)
    .WithRoleAssignments(storage, StorageBuiltInRole.StorageTableDataContributor)
    .WaitFor(storage)
    .WithEnvironment("Admin__ApiKey", adminApiKey)
    .WithEnvironment("Cors__AllowedOrigins", corsOrigins)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints()
    .PublishAsAzureContainerApp((infra, app) =>
    {
        // Scale to zero when idle; one replica is plenty for a handful of admins.
        app.Template.Scale = new ContainerAppScale { MinReplicas = 0, MaxReplicas = 1 };
    });

// The registry is shared with other apps, so push to a distinct repository.
// The resource stays "api" to keep the Container App name and URL.
#pragma warning disable ASPIREPIPELINES003 // WithImagePushOptions is experimental in Aspire 13.x.
api.WithImagePushOptions(context => context.Options.RemoteImageName = "flaskeinnsamling-api");
#pragma warning restore ASPIREPIPELINES003

builder.Build().Run();
