using System;
using System.Collections.Generic;
using System.IO;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace CakeBuild;

public static class Program
{
    public static int Main(string[] args)
    {
        return new CakeHost()
            .UseContext<BuildContext>()
            .Run(args);
    }
}

// One entry per mod head. Both share Core (../Core), which isn't a mod itself and has no modinfo.json,
// so it's built implicitly as a ProjectReference of each head rather than getting its own Cake task.
public sealed record ModHead(string ProjectName, string ModRoot);

public class BuildContext : FrostingContext
{
    public static readonly ModHead[] Heads =
    [
        new ModHead("Collodion", "../Collodion"),
        new ModHead("Kosphotography", "../Kosphotography")
    ];

    public const string ReleasesDir = "../Releases";

    public string BuildConfiguration { get; }
    public bool SkipJsonValidation { get; }

    // modid -> version, keyed off each head's modinfo.json.
    public Dictionary<string, string> Versions { get; } = new();

    public BuildContext(ICakeContext context)
        : base(context)
    {
        BuildConfiguration = context.Argument("configuration", "Release");
        SkipJsonValidation = context.Argument("skipJsonValidation", false);
        foreach (var head in Heads)
        {
            var modInfo = context.DeserializeJsonFromFile<ModInfo>($"{head.ModRoot}/modinfo.json");
            Versions[modInfo.ModID] = modInfo.Version;
        }
    }
}

[TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        if (context.SkipJsonValidation)
        {
            return;
        }
        // Core's assets are linked into both heads' output, so validating each head's own assets folder
        // plus Core's covers everything that actually ships.
        var globs = new List<string> { "../Core/assets/**/*.json" };
        foreach (var head in BuildContext.Heads)
        {
            globs.Add($"{head.ModRoot}/assets/**/*.json");
        }

        foreach (var glob in globs)
        {
            foreach (var file in context.GetFiles(glob))
            {
                try
                {
                    var json = File.ReadAllText(file.FullPath);
                    JToken.Parse(json);
                }
                catch (JsonException ex)
                {
                    throw new Exception($"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}", ex);
                }
            }
        }
    }
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        foreach (var head in BuildContext.Heads)
        {
            context.DotNetClean($"{head.ModRoot}/{head.ProjectName}.csproj",
                new DotNetCleanSettings
                {
                    Configuration = context.BuildConfiguration
                });

            context.DotNetPublish($"{head.ModRoot}/{head.ProjectName}.csproj",
                new DotNetPublishSettings
                {
                    Configuration = context.BuildConfiguration
                });
        }
    }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
    public override void Run(BuildContext context)
    {
        context.EnsureDirectoryExists(BuildContext.ReleasesDir);
        context.CleanDirectory(BuildContext.ReleasesDir);

        foreach (var head in BuildContext.Heads)
        {
            var modInfo = context.DeserializeJsonFromFile<ModInfo>($"{head.ModRoot}/modinfo.json");
            var staging = $"{BuildContext.ReleasesDir}/{modInfo.ModID}";
            context.EnsureDirectoryExists(staging);

            // The publish output nests under bin/<config>/Mods/<modid>/publish because each head's
            // csproj sets OutputPath to bin/<config>/Mods/<modid> (so F5 debugging can load it loose via
            // --addModPath). The assembly only: publish also leaves a .pdb and .deps.json beside it that
            // a player has no use for.
            context.CopyFiles(
                $"{head.ModRoot}/bin/{context.BuildConfiguration}/Mods/{modInfo.ModID}/publish/*.dll", staging);

            // Taken from the source tree, not from the publish folder, since that's where Core's shared
            // assets get linked in from too.
            if (context.DirectoryExists($"{head.ModRoot}/assets"))
            {
                context.CopyDirectory($"{head.ModRoot}/assets", $"{staging}/assets");
            }
            if (context.DirectoryExists("../Core/assets"))
            {
                context.CopyDirectory("../Core/assets", $"{staging}/assets");
            }
            context.CopyFile($"{head.ModRoot}/modinfo.json", $"{staging}/modinfo.json");
            if (context.FileExists($"{head.ModRoot}/modicon.png"))
            {
                context.CopyFile($"{head.ModRoot}/modicon.png", $"{staging}/modicon.png");
            }

            context.Zip(staging, $"{BuildContext.ReleasesDir}/{modInfo.ModID}_{modInfo.Version}.zip");
        }
    }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask
{
}
