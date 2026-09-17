```csharp
// hosts/Ged.Api/Program.cs

using FileSignatures;
using Ged.Adapters.FileTypes;
using Ged.Core.Ports.FileTypes;

// Built once and shared. The inspector performs optimisations at construction, so building one per
// request pays that cost on every upload.
builder.Services.AddSingleton<IFileFormatInspector>(_ => new FileFormatInspector());
builder.Services.AddSingleton<IContentFormatDetector, FileSignaturesContentDetector>();

builder.Services.AddGedFeatureHandlers(builder.Configuration);
```

```xml
<!-- hosts/Ged.Api/Ged.Api.csproj -->
<ProjectReference Include="../../src/Ged.Adapters.FileTypes/Ged.Adapters.FileTypes.csproj" />
<PackageReference Include="FileSignatures" />
```
