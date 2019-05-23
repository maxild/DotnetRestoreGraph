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
