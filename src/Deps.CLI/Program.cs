using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NuGet.LibraryModel;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;

namespace Deps.CLI
{
    // Input to NuGet is a set of Package References from the project file (Top-level/Direct dependencies)
    // and the output is a full closure/graph of all the package dependencies including transitive dependencies.

    static class Program
    {
        private static readonly string[] PACKAGE_PREFIXES_TO_IGNORE =
        {
            "System.",
            "NETStandard.",
            "Microsoft.NETCore.",
        };

        static void Main(string[] args)
        {
            var rootPath = GetRepoRootPath();

            //var defaultProjectPath = Path.Combine(rootPath, "DotnetDependencies.sln");
            var defaultProjectPath =
                Path.Combine(
                    Path.Combine(
                        Path.Combine(rootPath, "src"), "Deps.CLI"), "Deps.CLI.csproj");

            var projectPath = args == null || args.Length == 0
                ? defaultProjectPath
                : args[0];

            // Load dependency graph via nuget client tools build into msbuild (.NET Core CLI, .NET Core SDK)
            var dependencyGraphService = new DependencyGraphService();
            var dependencyGraph = dependencyGraphService.GenerateDependencyGraph(projectPath);

            foreach (PackageSpec project in dependencyGraph.Projects.Where(p => p.RestoreMetadata.ProjectStyle == ProjectStyle.PackageReference))
            {
                // Generate lock file: A lock file has the package dependency graph for the project/solution/repo
                // that includes both the direct as well as transitive dependencies.
                var lockFileService = new LockFileService();
                LockFile lockFile = lockFileService.GetLockFile(project.FilePath, project.RestoreMetadata.OutputPath);

                Console.WriteLine(project.Name);

                foreach (TargetFrameworkInformation targetFramework in project.TargetFrameworks)
                {
                    Console.WriteLine($"  [{targetFramework.FrameworkName}]");

                    // Find the transitive closure for this tfm
                    LockFileTarget lockFileTargetFramework =
                        lockFile.Targets.FirstOrDefault(t => t.TargetFramework.Equals(targetFramework.FrameworkName));

                    if (lockFileTargetFramework != null)
                    {
                        foreach (LibraryDependency dependency in targetFramework.Dependencies)
                        {
                            // Find the _resolved_ package reference
                            LockFileTargetLibrary resolvedPackageReference =
                                lockFileTargetFramework.Libraries.FirstOrDefault(library =>
                                    library.Name == dependency.Name);

                            ReportPackageDependencies(resolvedPackageReference, lockFileTargetFramework, 1);
                        }
                    }

                }
            }
        }

        // TODO: Find the packages to download
        // recursive!!!
        private static void ReportPackageDependencies(LockFileTargetLibrary projectLibrary, LockFileTarget lockFileTargetFramework, int indentLevel)
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

                ReportPackageDependencies(dependencyLibrary, lockFileTargetFramework, indentLevel + 1);
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
