using System.IO;
using NuGet.ProjectModel;

namespace Deps.Core
{
    // Which versions of NuGet support restore with lock file
    //      * NuGet.exe version 4.9 and above.
    //      * Visual Studio 2017 version 15.9 and above.
    //      * .NET SDK version 2.1.500 and above.
    // See also https://blog.nuget.org/20181217/Enable-repeatable-package-restores-using-a-lock-file.html

    public class LockFileService
    {
        public LockFile GetLockFile(string projectPath, string outputPath)
        {
            // Run the restore command
            var dotNetRunner = new DotNetRunner();
            string[] arguments = new[] {"restore", $"\"{projectPath}\""};
            dotNetRunner.Run(Path.GetDirectoryName(projectPath), arguments);

            // TODO: What about packages.lock.json based on package references used as input to download of packages from MyGet

            // Load the lock file
            string lockFilePath = Path.Combine(outputPath, "project.assets.json");
            return LockFileUtilities.GetLockFile(lockFilePath, NuGet.Common.NullLogger.Instance);
        }
    }
}
