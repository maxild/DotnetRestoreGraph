using System.Collections.Generic;
using System.Linq;
using NuGet.Frameworks;
using NuGet.ProjectModel;

namespace Deps.CLI
{
    public static class NearestNuGetFrameworkExtensions
    {
        /// <summary>
        /// Get the the nearest possible framework among the possible target frameworks of this package
        /// that is compatible with the given framework argument.
        /// </summary>
        /// <param name="packageSpec">The project to search for nearest target framework.</param>
        /// <param name="framework">The target framework to match (i.e. the root projects TargetFramework).</param>
        /// <returns>The nearest possible framework among the possible target frameworks of this package.</returns>
        public static NuGetFramework GetNearestFrameworkMatching(this PackageSpec packageSpec, NuGetFramework framework)
        {
            return packageSpec.RestoreMetadata.TargetFrameworks.Select(x => x.FrameworkName).GetNearestFrameworkMatching(framework);
        }

        /// <summary>
        /// Get the the nearest possible framework among the possible frameworks
        /// that is compatible with the given framework argument.
        /// </summary>
        /// <param name="possibleFrameworks">Possible frameworks to narrow down</param>
        /// <param name="framework">The target framework to match (i.e. the root projects TargetFramework).</param>
        /// <returns>The nearest possible framework among the possible frameworks.</returns>
        public static NuGetFramework GetNearestFrameworkMatching(this IEnumerable<NuGetFramework> possibleFrameworks,
            NuGetFramework framework)
        {
            var reducer = new FrameworkReducer();
            NuGetFramework nearest = reducer.GetNearest(framework, possibleFrameworks);
            return nearest;
        }
    }
}
