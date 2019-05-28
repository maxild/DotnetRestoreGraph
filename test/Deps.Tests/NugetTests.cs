using System.Linq;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using Xunit;

namespace Deps.Tests
{
    public class NugetTests
    {
        [Fact]
        public void ConfigureNugetV3PackageSources()
        {
            //string settingsPath = "";
            //var settings = new Settings(settingsPath);

            var packageSourceProvider = new PackageSourceProvider(NullSettings.Instance, new []
            {
                CreatePackageSource("https://api.nuget.org/v3/index.json", "NuGet.org v3")
            });

            //var packageSourceProvider = new PackageSourceProvider(settings, ConfigurationDefaults.DefaultPackageSources);
            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());

            var repos = sourceRepositoryProvider.GetRepositories().ToList();

            Assert.Single(repos);
            Assert.Equal("NuGet.org v3", repos[0].PackageSource.Name);
            Assert.Equal("https://api.nuget.org/v3/index.json", repos[0].PackageSource.Source);
        }

        [Fact]
        public void ConfigureMygetV3PackageSource()
        {
            var packageSourceProvider = new PackageSourceProvider(NullSettings.Instance, new []
            {
                CreateAuthenticatedPackageSource("https://www.myget.org/F/brf-ci/api/v3/index.json", "BrfCiMyget", "username", "password")
            });

            //var packageSourceProvider = new PackageSourceProvider(settings, ConfigurationDefaults.DefaultPackageSources);
            var sourceRepositoryProvider = new SourceRepositoryProvider(packageSourceProvider, Repository.Provider.GetCoreV3());

            var repos = sourceRepositoryProvider.GetRepositories().ToList();

            Assert.Single(repos);
            Assert.Equal("BrfCiMyget", repos[0].PackageSource.Name);
            Assert.Equal("https://www.myget.org/F/brf-ci/api/v3/index.json", repos[0].PackageSource.Source);
            Assert.Equal("username", repos[0].PackageSource.Credentials.Username);
            Assert.Equal("password", repos[0].PackageSource.Credentials.Password); //clear text
        }

        [Fact]
        public void CorrectGlobalPackagesFolder()
        {
            Assert.Equal(@"C:\Users\br5904\.nuget\packages\", SettingsUtility.GetGlobalPackagesFolder(NullSettings.Instance));
        }

        static PackageSource CreatePackageSource(string sourceUrl, string sourceName)
        {
            return new PackageSource(sourceUrl, sourceName)
            {
                ProtocolVersion = 3
            };
        }

        static PackageSource CreateAuthenticatedPackageSource(string sourceUrl, string sourceName, string username, string password)
        {
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
    }
}
