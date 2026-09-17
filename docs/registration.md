# Choosing a detector

```jsonc
// hosts/Ged.Api/appsettings.json
"Ged": {
  "Uploads": {
    "Detector": "builtin",          // or "filesignatures"
    "AllowedFormats": [ "pdf", "docx", "xlsx", "odt", "txt", "csv", "jpeg", "png" ]
  }
}
```

```csharp
// hosts/Ged.Api/Program.cs
using FileSignatures;
using Ged.Adapters.FileTypes;
using Ged.Core.Ports.FileTypes;

// One composition root, two detectors. Neither the feature layer nor the domain knows which line
// below ran: they see IContentFormatDetector and nothing else — the same shape as the two
// persistence providers and the storage adapters.
var detector = builder.Configuration["Ged:Uploads:Detector"] ?? "builtin";

_ = detector.ToLowerInvariant() switch
{
    "builtin" =>
        builder.Services.AddSingleton<IContentFormatDetector, BuiltInContentFormatDetector>(),

    "filesignatures" => builder.Services
        .AddSingleton<IFileFormatInspector>(_ => new FileFormatInspector())
        .AddSingleton<IContentFormatDetector,
                      Ged.Adapters.FileTypes.FileSignatures.FileSignaturesContentDetector>(),

    _ => throw new InvalidOperationException($"Unknown Ged:Uploads:Detector '{detector}'."),
};
```

```xml
<!-- hosts/Ged.Api/Ged.Api.csproj -->
<ProjectReference Include="../../src/Ged.Adapters.FileTypes/Ged.Adapters.FileTypes.csproj" />
<ProjectReference Include="../../src/Ged.Adapters.FileTypes.FileSignatures/Ged.Adapters.FileTypes.FileSignatures.csproj" />
```

Referencing both costs one assembly and no runtime weight: only the registered one is ever
constructed. Drop the second reference the day the choice is settled.
