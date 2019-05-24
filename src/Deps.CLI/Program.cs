using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Resolver;
using NuGet.Versioning;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace Deps.CLI
{
    // Input to NuGet is a set of Package References from the project file (Top-level/Direct dependencies)
    // and the output is a full closure/graph of all the package dependencies including transitive dependencies.

    [SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
    [SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
    [SuppressMessage("ReSharper", "UnassignedGetOnlyAutoProperty")]
    internal class Program
    {
        public static int Main(string[] args) => CommandLineApplication.Execute<Program>(args);

        private readonly ILogger _logger;
        // ReSharper disable once PrivateFieldCanBeConvertedToLocalVariable
        private readonly ILoggerFactory _loggerFactory;

        public Program()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            _loggerFactory = new LoggerFactory().AddConsole(Verbosity);
#pragma warning restore CS0618 // Type or member is obsolete
            _logger = _loggerFactory.CreateLogger(typeof(Program));
        }

        [Option("-v|--verbosity <LEVEL>", Description =
            "Sets the verbosity level of the command. Allowed values are Trace, Debug, Information, Warning, Error, Critical, None")]
        public LogLevel Verbosity { get; } = LogLevel.Information;

        [Argument(0, Description = "The (csproj) project file to analyze.")]
        public string Project { get; set; }

        [Option("-f|--framework <FRAMEWORK>", Description = "Analyzes for a specific framework.")]
        public string Framework { get; }

        [Option("--package <PACKAGE>", Description = "Analyzes a specific (nupkg) package.")]
        public string Package { get; }

        [Option("--version <PACKAGE_VERSION>", Description = "The version of the package to analyze.")]
        public string Version { get; }

        [Option("--source <PACKAGE_SOURCE_ENV>", Description =
            "The different package source configurations. Allowed values are NugetOrg, MyGetCi, MyGet and Brf.")]
        public PackageSourceEnvironment PackageSourceEnvironment { get; } = PackageSourceEnvironment.NugetOrg;

        // TODO: Option for the tmp directory to restore packages to (--packagesDir <PACKAGES_DIR>).

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        private ValidationResult OnValidate()
        {
            if (!string.IsNullOrEmpty(Project))
            {
                // Do we have an explicit/full csproj path
                if (File.Exists(Project))
                {
                    // Correct relative paths so they work when passed to Uri
                    var fullPath = Path.GetFullPath(Project);
                    if (fullPath != Project && File.Exists(fullPath))
                    {
                        Project = fullPath;
                    }

                    return ValidationResult.Success;
                }

                if (Project.Equals(".", StringComparison.Ordinal))
                {
                    Project = Directory.GetCurrentDirectory();
                }

                if (!Directory.Exists(Project))
                {
                    return new ValidationResult("Project path does not exist.");
                }

                var csproj = Directory.GetFiles(Project, "*.csproj", SearchOption.TopDirectoryOnly).ToArray();

                if (csproj.Length == 0)
                {
                    return new ValidationResult("Unable to find any project files in working directory.");
                }

                if (csproj.Length > 1)
                {
                    return new ValidationResult("More than one project file found in working directory.");
                }

                Project = csproj[0];
            }
            else
            {
                // Validate PackageIdentity
                if (string.IsNullOrEmpty(Package))
                {
                    return new ValidationResult("Either --project or --package must be specified.");
                }
                if (string.IsNullOrEmpty(Version))
                {
                    return new ValidationResult("--version must be specified.");
                }
            }

            if (string.IsNullOrEmpty(Framework))
            {
                return new ValidationResult("--framework must be specified.");
            }

            return ValidationResult.Success;
        }

        [SuppressMessage("ReSharper", "UnusedMember.Local")]
        private void OnExecute()
        {
            if (!string.IsNullOrEmpty(Project))
                AnalyzeProject(Project, Framework);
            else
                AnalyzePackage(Package, Version, Framework, _logger, PackageSourceEnvironment.Brf); // TODO: move to param
        }

        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        static void AnalyzePackage(
            string packageId,
            string version,
            string framework,
            ILogger logger,
            PackageSourceEnvironment packageSourceEnvironment)
        {
            var rootPackageIdentity = new PackageIdentity(packageId, NuGetVersion.Parse(version));
            var rootNuGetFramework = NuGetFramework.ParseFolder(framework);

            // If configFileName is null, the user specific settings file is %AppData%\NuGet\NuGet.config.
            // After that, the machine wide settings files are added (c:\programdata\NuGet\Config\*.config).
            //var settings = Settings.LoadDefaultSettings(root: null, configFileName: null, machineWideSettings: null);
            //var sourceRepositoryProvider = new SourceRepositoryProvider(settings, Repository.Provider.GetCoreV3());

            // TODO: Configure packageSources from external config
            // TODO: Use environment variables for MyGet username/password!!!!!!
            // TODO: Make 3 different environments
            //      1. MyGetCi
            //      2. MyGet
            //      3. Brf

            string username = null, password = null;
            if (packageSourceEnvironment == PackageSourceEnvironment.MyGetCi ||
                packageSourceEnvironment == PackageSourceEnvironment.MyGet)
            {
                username = Environment.GetEnvironmentVariable("MYGET_USERNAME");
                if (string.IsNullOrEmpty(username)) username = "maxfire";
                password = Environment.GetEnvironmentVariable("MYGET_PASSWORD");
                if (string.IsNullOrEmpty(password)) throw new InvalidOperationException("Missing MYGET_PASSWORD environment variable.");
            }

            PackageSourceProvider packageSourceProvider;
            switch (packageSourceEnvironment)
            {
                case PackageSourceEnvironment.MyGetCi:
                    packageSourceProvider = new PackageSourceProvider(NullSettings.Instance, new []
                    {
                        CreatePackageSource("https://api.nuget.org/v3/index.json", "NuGet.org v3"),
                        CreatePackageSource("https://www.myget.org/F/maxfire-ci/api/v3/index.json", "MaxfireCi"),
                        CreateAuthenticatedPackageSource("https://www.myget.org/F/brf-ci/api/v3/index.json", "BrfCiMyGet", username, password)
                    });
                    break;
                case PackageSourceEnvironment.MyGet:
                    packageSourceProvider = new PackageSourceProvider(NullSettings.Instance, new []
                    {
                        CreatePackageSource("https://api.nuget.org/v3/index.json", "NuGet.org v3"),
                        CreateAuthenticatedPackageSource("https://www.myget.org/F/brf/api/v3/index.json", "BrfMyGet", username, password)
                    });
                    break;
                case PackageSourceEnvironment.Brf:
                    packageSourceProvider = new PackageSourceProvider(NullSettings.Instance, new []
                    {
                        CreatePackageSource("https://api.nuget.org/v3/index.json", "NuGet.org v3"),
                        CreatePackageSource("http://pr-nuget/nuget", "Brf", protocolVersion: 2)
                    });
                    break;
                case PackageSourceEnvironment.NugetOrg:
                default:
                    packageSourceProvider = new PackageSourceProvider(NullSettings.Instance, new []
                    {
                        CreatePackageSource("https://api.nuget.org/v3/index.json", "NuGet.org v3")
                    });
                    break;
            }

            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());

            Console.WriteLine("Feeds used:");
            foreach (var packageSource in packageSourceProvider.LoadPackageSources())
            {
                Console.WriteLine($"    {packageSource}");
            }
            Console.WriteLine();

            //var nugetLogger = logger.AsNuGetLogger();
            var nugetLogger = NullLogger.Instance;

            var tmpDirToRestoreTo = Path.Combine(Path.GetTempPath(), "packages");

            using (var cacheContext = new SourceCacheContext {NoCache = true})
            {
                var repositories = sourceRepositoryProvider.GetRepositories();
                var resolvedPackages = new ConcurrentDictionary<PackageIdentity, SourcePackageDependencyInfo>(PackageIdentityComparer.Default);

                // Builds transitive closure
                // TODO: is Wait okay?
                ResolvePackageDependencies(rootPackageIdentity, rootNuGetFramework, cacheContext, nugetLogger, repositories, resolvedPackages).Wait();

                var resolverContext = new PackageResolverContext(
                    DependencyBehavior.Lowest,
                    targetIds: new[] { packageId },
                    availablePackages: new HashSet<SourcePackageDependencyInfo>(resolvedPackages.Values),
                    packageSources: sourceRepositoryProvider.GetRepositories().Select(s => s.PackageSource),
                    log: nugetLogger,
                    requiredPackageIds: Enumerable.Empty<string>(),
                    packagesConfig: Enumerable.Empty<PackageReference>(),
                    preferredVersions: Enumerable.Empty<PackageIdentity>());

                var resolver = new PackageResolver();
                List<SourcePackageDependencyInfo> prunedPackages;
                //try
                //{
                    prunedPackages = resolver.Resolve(resolverContext, CancellationToken.None)
                        .Select(id => resolvedPackages[id]).ToList();
                //}
                //catch (Exception ex)
                //{
                //    Console.WriteLine($"ERROR: {ex.Message}");
                //    return;
                //}

                Console.WriteLine($"root package identity: {rootPackageIdentity}");
                Console.WriteLine($"root target framework: {rootNuGetFramework.DotNetFrameworkName} ({rootNuGetFramework.GetShortFolderName()})");
                Console.WriteLine();

                var packageNodes = new Dictionary<string, PackageReferenceNode>(StringComparer.OrdinalIgnoreCase);

                //var builder = new DependencyGraph.Builder(rootNode);

                // TODO: problem that the graph is flattened!!!!!
                // TODO: Should we inspect the items (assemblies of each package). remember meta-packages contain other packages
                // TODO: dependencies are important

                Console.WriteLine("Vertices of dependency package graph:");
                Console.WriteLine();

                PackageReferenceNode rootPackage = null;

                // resolve contained assemblies of packages
                foreach (SourcePackageDependencyInfo target in prunedPackages)
                {
                    //target.Id
                    //target.Version
                    //target.Dependencies

                    // TODO: --no-cache, --packages $tmpDirToRestoreTo
                    var downloadResource = target.Source.GetResource<DownloadResource>();

                    // TODO: .Result of Async
                    var downloadResult = downloadResource.GetDownloadResourceResultAsync(
                        new PackageIdentity(target.Id, target.Version),
                        new PackageDownloadContext(cacheContext, directDownloadDirectory: tmpDirToRestoreTo, directDownload: true),
                        SettingsUtility.GetGlobalPackagesFolder(NullSettings.Instance),
                        nugetLogger,
                        CancellationToken.None).Result;

                    // items in lib folder of target (a package is a collection of assemblies)
                    var packageReader = downloadResult.PackageReader;
                    if (packageReader == null)
                    {
                        downloadResult.PackageStream.Seek(0, SeekOrigin.Begin);
                        packageReader = new PackageArchiveReader(downloadResult.PackageStream);
                    }

                    var libItems = packageReader.GetLibItems();

                    var reducer = new FrameworkReducer();
                    // resolve the targetFramework of the items
                    NuGetFramework nearest = reducer.GetNearest(rootNuGetFramework, libItems.Select(x => x.TargetFramework));

                    // assembly references is a sequence of assembly names (file name including the extension)
                    var assemblyReferences = libItems
                        .Where(group => group.TargetFramework.Equals(nearest))
                        .SelectMany(group => group.Items)
                        .Where(itemRelativePath => Path.GetExtension(itemRelativePath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                        .Select(Path.GetFileName);
                        //.Select(assemblyName => new AssemblyReferenceNode(assemblyName)); // we do not include assembly references in the graph

                    // TODO we ignore framework references in nuspec (only used by MS)
                    //var frameworkItems = packageReader.GetFrameworkItems();
                    //nearest = reducer.GetNearest(nugetFramework, frameworkItems.Select(x => x.TargetFramework));

                    //// TODO: Why not use Path.GetFileName here?
                    //var frameworkAssemblyReferences = frameworkItems
                    //    .Where(@group => @group.TargetFramework.Equals(nearest))
                    //    .SelectMany(@group => @group.Items)
                    //    .Select(Path.GetFileName); // Why
                    //    //.Select(assemblyName => new AssemblyReferenceNode(assemblyName)); // we do not include assembly references in the graph

                    //assemblyReferences = assemblyReferences.Concat(frameworkAssemblyReferences);

                    var packageReferenceNode = new PackageReferenceNode(target.Id, target.Version.ToString(),
                        nearest.DotNetFrameworkName, nearest.GetShortFolderName(), assemblyReferences);

                    if (rootPackageIdentity.Equals(new PackageIdentity(target.Id, target.Version)))
                    {
                        if (rootPackage != null) throw new InvalidOperationException("UNEXPECTED: Root package should be unique.");
                        rootPackage = packageReferenceNode;
                    }

                    Console.WriteLine($"    {packageReferenceNode}");

                    //builder.WithNode(packageReferenceNode);
                    //builder.WithNodes(assemblyReferences);

                    // TODO: Target package has edges to assembly nodes
                    //builder.WithEdges(assemblyReferences.Select(x => new Edge(packageReferenceNode, x)));

                    // TODO: Pack2Pack reference (directed vertex)
                    packageNodes.Add(target.Id, packageReferenceNode);
                }
                Console.WriteLine();

                // NOTE: We have transitive closure so all references are resolved!!!!
                // NOTE: The relation is A 'depends on' B shown like A ---> B
                // NOTE: The inverse relation is 'used by'....

                // TODO: How to represent digraph (DAG)
                // TODO: How to represent the topological order (evaluation order, traversal order)
                // TODO: A directed acyclic graph (DAG) with a single root is not a tree!!!!!
                // NOTE: Both trees and DAGs are connected, directed, rooted, and have no cycles
                //       so this means that starting from any node and going up the parents you will
                //       eventually work your way up to the top (root).
                //       However, since DAG nodes have multiple parents, there will be multiple paths
                //       on the way up (that eventually merge). This is like GIT history (DAG)
                // Another way to see it is Tree is like single class inheritance, and DAG is like multiple class inheritance.
                // A (successor, downstream, core) package can be depended on by many (predecessor, upstream) packages

                Console.WriteLine("Edges of dependency package graph:");
                Console.WriteLine();

                // resolve dependencies of packages
                foreach (SourcePackageDependencyInfo target in prunedPackages)
                {
                    // TODO: predecessor node in dependency package graph
                    PackageReferenceNode sourceNode = packageNodes[target.Id];

                    // traverse all dependencies of nuspec
                    foreach (PackageDependency dependency in target.Dependencies)
                    {
                        //dependency.Id
                        //dependency.VersionRange

                        // resolved dependency of sourceNode
                        PackageReferenceNode targetNode = packageNodes[dependency.Id];

                        //targetNode.PackageId
                        //targetNode.Type (package)
                        //targetNode.Version

                        // labeled edge
                        //new Edge(sourceNode, targetNode, x.VersionRange.ToString())

                        Console.WriteLine($"    {sourceNode}---{dependency.VersionRange}---->{targetNode}");
                    }

                    // TODO: directed edge with label of version range for each successor node (successor node carries resolved version)
                    //builder.WithEdges(target.Dependencies.Select(x =>
                    //    new Edge(sourceNode, packageNodes[x.Id], x.VersionRange.ToString())));
                }

                Console.WriteLine();
                Console.WriteLine($"root package: {rootPackage}");


                //return builder.Build();
            }
        }

        // Recursive
        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        static async Task ResolvePackageDependencies(
            PackageIdentity package,
            NuGetFramework framework,
            SourceCacheContext cacheContext,
            NuGet.Common.ILogger logger,
            IEnumerable<SourceRepository> repositories,
            ConcurrentDictionary<PackageIdentity, SourcePackageDependencyInfo> availablePackages)
        {
            if (availablePackages.ContainsKey(package))
            {
                return;
            }

            // TODO
            // Avoid getting info for e.g. netstandard1.x if our framework is highest (e.g. netstandard2.0)
            //if (framework.IsPackageBased &&
            //    package.Id.Equals("netstandard.library", StringComparison.OrdinalIgnoreCase) &&
            //    NuGetFrameworkUtility.IsCompatibleWithFallbackCheck(framework,
            //        NuGetFramework.Parse($"netstandard{package.Version.Major}.{package.Version.Minor}")))
            //{
            //    return;
            //}

            foreach (var sourceRepository in repositories)
            {
                var dependencyInfoResource = await sourceRepository.GetResourceAsync<DependencyInfoResource>();
                var dependencyInfo = await dependencyInfoResource.ResolvePackage(
                    package, framework, cacheContext, logger, CancellationToken.None);

                if (dependencyInfo == null)
                {
                    continue;
                }

                // TODO: try add should be changed to console.writeline reporting
                if (availablePackages.TryAdd(new PackageIdentity(dependencyInfo.Id, dependencyInfo.Version), dependencyInfo))
                {
                    await Task.WhenAll(dependencyInfo.Dependencies.Select(dependency =>
                    {
                        // recursive traversal of graph/tree
                        return ResolvePackageDependencies(new PackageIdentity(dependency.Id, dependency.VersionRange.MinVersion),
                            framework, cacheContext, logger, repositories, availablePackages);
                    }));
                }
            }
        }

        static PackageSource CreatePackageSource(string sourceUrl, string sourceName, int protocolVersion = 3)
        {
            return new PackageSource(sourceUrl, sourceName)
            {
                ProtocolVersion = protocolVersion
            };
        }

        static PackageSource CreateAuthenticatedPackageSource(string sourceUrl, string sourceName, string username, string password)
        {
            if (username == null) throw new ArgumentNullException(nameof(username));
            if (password == null) throw new ArgumentNullException(nameof(password));

            // credentials required to authenticate user within package source web requests.
            var credentials = new PackageSourceCredential(sourceName, username, password,
                isPasswordClearText: true,
                validAuthenticationTypesText: null);

            var packageSource = new PackageSource(sourceUrl, sourceName)
            {
                Credentials = credentials,
                ProtocolVersion = 3
            };

            return packageSource;
        }

        private static readonly string[] PACKAGE_PREFIXES_TO_IGNORE =
        {
            "System.",
            "NETStandard.",
            "Microsoft.NETCore.",
        };

        // TODO: Should we use framework here
        //
        // 1. Create dgspec.json via .NET Core CLI: GenerateRestoreGraphFile target
        // 2. Load dgspec.json into DependencyGraphSpec instance/object
        // For each SDK project (ProjectStyle.PackageReference) in the graph spec
        // 3. Get assets file (project.assets.json) for the project
        // 4. Construct LockFile from assets file for the project
        static void AnalyzeProject(string projectPath, string framework)
        {
            // TODO: use framework (csproj must have this targetFramework)
            var rootNuGetFramework = NuGetFramework.ParseFolder(framework);

            // HACK
            if (string.IsNullOrEmpty(projectPath))
            {
                var rootPath = GetRepoRootPath();

                //projectPath = Path.Combine(rootPath, "DotnetDependencies.sln");
                projectPath = Path.Combine(
                    Path.Combine(
                        Path.Combine(rootPath, "src"), "Deps.CLI"), "Deps.CLI.csproj");
            }

            // 'package graph' is a better word
            // Load dependency graph via nuget client tools build into msbuild (.NET Core CLI, .NET Core SDK)
            var dependencyGraphService = new DependencyGraphService();
            var dependencyGraph = dependencyGraphService.GenerateDependencyGraph(projectPath);

            // PackageReference based SDK projects
            // For each top-level (kernel item) package reference
            // ProjectStyle.PackageReference: MSBuild style <PackageReference>, where project.assets.json (lock file) is
            // generated in the RestoreOutputPath folder
            foreach (PackageSpec project in dependencyGraph.Projects.Where(p => p.RestoreMetadata.ProjectStyle == ProjectStyle.PackageReference))
            {
                // TODO: Maybe just use the project.assets.json created by .NET Core SDK tooling
                // Generate lock file: A lock file has the package dependency graph for the project/solution/repo
                // that includes both the direct as well as transitive dependencies.
                var lockFileService = new LockFileService();
                LockFile lockFile = lockFileService.GetLockFile(project.FilePath, project.RestoreMetadata.OutputPath);

                //var libraries = lockFile.Targets.Single(x => x.TargetFramework.Framework == framework)
                //    .Libraries.Where(x => x.Type.Equals("package", StringComparison.OrdinalIgnoreCase)).ToList();

                Console.WriteLine(project.Name);

                // TODO: How to resolve 'TargetFramework' from project of dgspec (resolved restore graph)
                //TargetFrameworkInformation targetFramework2 = project.TargetFrameworks.Single(t => t.FrameworkName.Equals(nugetFramework));

                // TODO: How to resolve 'TargetFramework' from lockfile (resolved package graph)
                //LockFileTarget lockFileTargetFramework2 =
                //    lockFile.Targets.FirstOrDefault(t => t.TargetFramework.Equals(nugetFramework));

                foreach (TargetFrameworkInformation targetFramework in project.TargetFrameworks)
                {
                    Console.WriteLine($"  [{targetFramework.FrameworkName}]");

                    // Find the transitive closure for this tfm
                    LockFileTarget lockFileTargetFramework =
                        lockFile.Targets.FirstOrDefault(t => t.TargetFramework.Equals(targetFramework.FrameworkName));

                    if (lockFileTargetFramework != null)
                    {
                        // For each transitive (closure item) dependency
                        foreach (LibraryDependency dependency in targetFramework.Dependencies)
                        {
                            // Find the _resolved_ package reference
                            LockFileTargetLibrary resolvedPackageReference =
                                lockFileTargetFramework.Libraries.FirstOrDefault(library =>
                                    library.Name == dependency.Name);

                            ReportLockFilePackageDependencies(resolvedPackageReference, lockFileTargetFramework, 1);
                        }
                    }

                }
            }
        }

        // TODO: Find the packages to download
        // recursive!!!
        static void ReportLockFilePackageDependencies(LockFileTargetLibrary projectLibrary, LockFileTarget lockFileTargetFramework, int indentLevel)
        {
            const int INDENT_SIZE = 2;

            if (PACKAGE_PREFIXES_TO_IGNORE.Any(prefix => projectLibrary.Name.StartsWith(prefix)))
            {
                return;
            }

            // indent shows levels of the graph
            Console.Write(new string(' ', indentLevel * INDENT_SIZE));
            Console.WriteLine($"{projectLibrary.Name}, v{projectLibrary.Version}");

            if (!projectLibrary.Type.Equals("package", StringComparison.Ordinal))
            {
                // TODO: Throw, Log error
                Console.WriteLine($"UNEXPECTED: {projectLibrary.Type}, {projectLibrary.Name}, v{projectLibrary.Version}");
                return;
            }

            foreach (PackageDependency dependencyReference in projectLibrary.Dependencies)
            {
                // dependencyReference has (Id, VersionRange) and is the unresolved reference

                // name and version of package dependency
                var packageId = dependencyReference.Id;

                // Find the dependency (reference) among the libraries (the transitive closure of that tfm)
                LockFileTargetLibrary dependencyLibrary =
                    lockFileTargetFramework.Libraries.FirstOrDefault(library => library.Name == packageId);

                // dependencyLibrary has (Name, Framework, Version, Type, Dependencies, ...) attributes

                ReportLockFilePackageDependencies(dependencyLibrary, lockFileTargetFramework, indentLevel + 1);
            }
        }

        private static string GetRepoRootPath()
        {
            string path = new Uri(Assembly.GetExecutingAssembly().CodeBase).LocalPath;
            while (true)
            {
                path = Path.GetDirectoryName(path);
                if (Directory.GetDirectories(path, ".git", SearchOption.TopDirectoryOnly).Length == 1)
                {
                    break;
                }
            }
            return path;
        }
    }
}
