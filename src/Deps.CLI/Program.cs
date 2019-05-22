using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using McMaster.Extensions.CommandLineUtils;
using Microsoft.Extensions.Logging;
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

        private ILogger _logger;
        private ILoggerFactory _loggerFactory;

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

        [Option("--version <PACKAGEVERSION>", Description = "The version of the package to analyze.")]
        public string Version { get; }

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
                AnalyzePackage(Package, Version, Framework, _logger);
        }

        [SuppressMessage("ReSharper", "PossibleMultipleEnumeration")]
        static void AnalyzePackage(string packageId, string version, string framework, ILogger logger)
        {
            var packageIdentity = new PackageIdentity(packageId, NuGetVersion.Parse(version));
            var settings = Settings.LoadDefaultSettings(root: null, configFileName: null, machineWideSettings: null);
            var sourceRepositoryProvider = new SourceRepositoryProvider(settings, Repository.Provider.GetCoreV3());
            var nugetFramework = NuGetFramework.ParseFolder(framework);
            var nugetLogger = logger.AsNuGetLogger();

            using (var cacheContext = new SourceCacheContext())
            {
                var repositories = sourceRepositoryProvider.GetRepositories();
                var resolvedPackages = new ConcurrentDictionary<PackageIdentity, SourcePackageDependencyInfo>(PackageIdentityComparer.Default);

                // Builds transitive closure
                ResolvePackageDependencies(packageIdentity, nugetFramework, cacheContext, nugetLogger, repositories, resolvedPackages).Wait();

                var resolverContext = new PackageResolverContext(
                    DependencyBehavior.Lowest,
                    new[] { packageId },
                    Enumerable.Empty<string>(),
                    Enumerable.Empty<PackageReference>(),
                    Enumerable.Empty<PackageIdentity>(),
                    availablePackages: new HashSet<SourcePackageDependencyInfo>(resolvedPackages.Values),
                    packageSources: sourceRepositoryProvider.GetRepositories().Select(s => s.PackageSource),
                    log: nugetLogger);

                var resolver = new PackageResolver();
                IEnumerable<SourcePackageDependencyInfo> prunedPackages = resolver.Resolve(resolverContext, CancellationToken.None)
                    .Select(id => resolvedPackages[id]);

                // TODO: Lib folder items of root
                var rootNode = new PackageReferenceNode(packageIdentity.Id, packageIdentity.Version.ToString(), null);

                var packageNodes = new Dictionary<string, PackageReferenceNode>(StringComparer.OrdinalIgnoreCase);

                //var builder = new DependencyGraph.Builder(rootNode);

                // TODO: problem thta the graph is flattened!!!!!
                // TODO: Should we inspect the items (assemblies of each package). remember meta-packages contain other packages
                // TODO: dependencies are important


                // resolve contained assemblies of packages
                foreach (var target in prunedPackages)
                {
                    //target.Id
                    //target.Version
                    //target.Dependencies

                    var downloadResource = target.Source.GetResource<DownloadResource>();
                    var downloadResult = downloadResource.GetDownloadResourceResultAsync(
                        new PackageIdentity(target.Id, target.Version),
                        new PackageDownloadContext(cacheContext),
                        SettingsUtility.GetGlobalPackagesFolder(settings),
                        nugetLogger,
                        CancellationToken.None).Result;

                    // items in lib folder of target (a package is a collection of assemblies)
                    var libItems = downloadResult.PackageReader.GetLibItems();
                    var reducer = new FrameworkReducer();
                    // resolve the targetFramework of the items
                    var nearest = reducer.GetNearest(nugetFramework, libItems.Select(x => x.TargetFramework));

                    // Package
                    var assemblyReferences = libItems
                        .Where(@group => @group.TargetFramework.Equals(nearest))
                        .SelectMany(@group => @group.Items)
                        .Where(itemRelativePath => Path.GetExtension(itemRelativePath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
                        .Select(itemRelativePath => Path.GetFileName(itemRelativePath));

                    // we ignore framework references in nuspec (only used by MS)
                    //var frameworkItems = downloadResult.PackageReader.GetFrameworkItems();
                    //nearest = reducer.GetNearest(nugetFramework, frameworkItems.Select(x => x.TargetFramework));

                    // TODO: We ignore
                    //assemblyReferences = assemblyReferences.Concat(frameworkItems
                    //    .Where(@group => @group.TargetFramework.Equals(nearest))
                    //    .SelectMany(@group => @group.Items)
                    //    .Select(x => new AssemblyReferenceNode(x)));

                    var packageReferenceNode = new PackageReferenceNode(target.Id, target.Version.ToString(), assemblyReferences);

                    //builder.WithNode(packageReferenceNode);
                    //builder.WithNodes(assemblyReferences);

                    // TODO: Target package has edges to assembly nodes
                    //builder.WithEdges(assemblyReferences.Select(x => new Edge(packageReferenceNode, x)));

                    // TODO: Pack2Pack reference (directed vertex)
                    packageNodes.Add(target.Id, packageReferenceNode);
                }

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

                // resolve dependencies of packages
                foreach (SourcePackageDependencyInfo target in prunedPackages)
                {
                    // TODO: predecessor node in dependency package graph
                    //PackageReferenceNode packageReferenceNode = packageNodes[target.Id];

                    // TODO: directed edge with label of version range for each successor node (successor node carries resolved version)
                    //builder.WithEdges(target.Dependencies.Select(x =>
                    //    new Edge(packageReferenceNode, packageNodes[x.Id], x.VersionRange.ToString())));
                }

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
            // Avoid getting info for e.g. netstandard1.x if our framework is highet (e.g. netstandard2.0)
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
            var nugetFramework = NuGetFramework.ParseFolder(framework);

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


    public abstract class Node : IEquatable<Node>
    {
        public string Id { get; }

        public abstract string Type { get; }

        protected Node(string id)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        public virtual bool Equals(Node other)
        {
            if (other == null) return false;
            if (ReferenceEquals(this, other)) return true;
            return GetType() == other.GetType() && Id.Equals(other.Id, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object obj)
        {
            return obj is Node node && Equals(node);
        }

        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        public override string ToString()
        {
            return Id;
        }
    }

    public sealed class PackageReferenceNode : Node
    {
        public string PackageId => Id;

        public string Version { get; }

        public IEnumerable<string> LibAssemblyFiles { get; }

        public PackageReferenceNode(string packageId, string version, IEnumerable<string> libAssemblyFiles)
            : base(packageId)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            LibAssemblyFiles = (libAssemblyFiles ?? Enumerable.Empty<string>()).ToArray();
        }

        public override bool Equals(Node other)
        {
            return base.Equals(other) && (!(other is PackageReferenceNode packageReference) ||
                                          Version.Equals(packageReference.Version, StringComparison.Ordinal));
        }

        public override string Type { get; } = "Package";

        public override string ToString()
        {
            return $"{PackageId} {Version}";
        }
    }

    public sealed class ProjectReferenceNode : Node
    {
        public ProjectReferenceNode(string projectPath) : base(Path.GetFileName(projectPath))
        {
        }

        public override string Type { get; } = "Project";
    }
}
