using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NuGet.Frameworks;

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

    // TODO: Probably change this to use generic args for TVertex
    public sealed class Edge : IEquatable<Edge>
    {
        public Node Source { get; }

        public Node Target { get; }

        //public string Label { get; }

        public Edge(Node source, Node target)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Target = target ?? throw new ArgumentNullException(nameof(target));
        }

        //public Edge(Node start, Node end, string label)
        //{
        //    Source = start ?? throw new ArgumentNullException(nameof(start));
        //    Target = end ?? throw new ArgumentNullException(nameof(end));
        //    Label = label;
        //}

        public bool Equals(Edge other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(Source, other.Source) && Equals(Target, other.Target);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            return obj is Edge edge && Equals(edge);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (Source != null ? Source.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Target != null ? Target.GetHashCode() : 0);
                //hashCode = (hashCode * 397) ^ (Label != null ? Label.GetHashCode() : 0);
                return hashCode;
            }
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} ---> {1}", Source, Target);
            //return string.Format(CultureInfo.InvariantCulture, "{0} --{2}--> {1}", Source, Target,
            //    string.IsNullOrEmpty(Label) ? string.Empty : $"[{Label}]");
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
        public ProjectReferenceNode(string projectPath, string version, NuGetFramework framework) : base(Path.GetFileName(projectPath))
        {
            Version = version ?? throw new ArgumentNullException(nameof(version));
            Framework = framework.DotNetFrameworkName;
            FrameworkMoniker = framework.GetShortFolderName();
        }

        public override string Type { get; } = "Project";

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

        public override bool Equals(Node other)
        {
            // NOTE: Framework/FrameworkMoniker should not be part of identity condition
            return base.Equals(other) && (!(other is ProjectReferenceNode projectReference) ||
                                          Version.Equals(projectReference.Version, StringComparison.Ordinal));
        }
    }
}
