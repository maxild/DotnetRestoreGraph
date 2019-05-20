using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.ProjectModel;

namespace Deps.CLI
{
    /// <summary>
    /// TODO
    /// </summary>
    /// <remarks>
    /// Credit for the stuff happening in here goes to the https://github.com/jaredcnance/dotnet-status project
    /// </remarks>
    public class DependencyGraphService
    {
        public DependencyGraphSpec GenerateDependencyGraph(string projectPath)
        {
            var dotNetRunner = new DotNetRunner();

            // TODO: [name].dgspec.json tmp file
            string dgOutput = Path.Combine(Path.GetTempPath(), Path.GetTempFileName());

            // We will use the GenerateRestoreGraphFile MSBuild target to determine package dependencies. This
            // target writes the output of _GenerateRestoreGraph to disk. When invoked on a solution, it is meant
            // to find all projects and produce one json file per .sln file.
            //     dotnet msbuild [my.sln] /t:GenerateRestoreGraphFile /p:RestoreGraphOutputPath=graph.json
            string[] arguments = {"msbuild", $"\"{projectPath}\"", "/t:GenerateRestoreGraphFile", $"/p:RestoreGraphOutputPath={dgOutput}"};

            var runStatus = dotNetRunner.Run(Path.GetDirectoryName(projectPath), arguments);

            if (runStatus.IsSuccess)
            {
                // Read {0}.nuget.dgspec.json
                string dependencyGraphText = File.ReadAllText(dgOutput);
                // NuGet.ProjectModel type
                return new DependencyGraphSpec(JsonConvert.DeserializeObject<JObject>(dependencyGraphText));
            }
            else
            {
                throw new Exception($"Unable to process the the project `{projectPath}. Are you sure this is a valid .NET Core or .NET Standard project type?" +
                                    "\r\n\r\nHere is the full error message returned from the Microsoft Build Engine:\r\n\r\n" + runStatus.Output);
            }

            // TODO: Delete tmp file
        }
    }
}
