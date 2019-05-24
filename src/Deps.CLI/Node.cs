using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Deps.CLI
{
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
        private readonly string[] _libAssemblyFiles;

        public string PackageId => Id;

        public string Version { get; }

        /// <summary>
        /// The nearest matching framework that is compatible with the target
        /// framework of the root Framework (i.e. the resolved framework under
        /// the lib folder).
        /// </summary>
        public string Framework { get; }

        /// <summary>
        /// The shortened version of the <see cref="Framework"/> using the default mappings.
        /// Ex: net45
        /// </summary>
        public string FrameworkMoniker { get; }

        /// <summary>
        /// Any lib folder items (i.e. assembly file names) of the nupkg.
        /// </summary>
        public IEnumerable<string> LibAssemblyFiles => _libAssemblyFiles;

        public PackageReferenceNode(
            string packageId,
            string version,
            string framework,
            string frameworkMoniker,
            IEnumerable<string> libAssemblyFiles = null)
            : base(packageId)
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Framework = framework ?? throw new ArgumentNullException(nameof(framework));
            FrameworkMoniker = frameworkMoniker ?? throw new ArgumentNullException(nameof(frameworkMoniker));
            _libAssemblyFiles = (libAssemblyFiles ?? Enumerable.Empty<string>()).ToArray();
        }

        public override bool Equals(Node other)
        {
            // NOTE: Framework/FrameworkMoniker should not be part of identity condition
            return base.Equals(other) && (!(other is PackageReferenceNode packageReference) ||
                                          Version.Equals(packageReference.Version, StringComparison.Ordinal));
        }

        public override string Type { get; } = "Package";

        public override string ToString()
        {
            return $"{PackageId} {Version} ({FrameworkMoniker}, Count={_libAssemblyFiles.Length})";
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
