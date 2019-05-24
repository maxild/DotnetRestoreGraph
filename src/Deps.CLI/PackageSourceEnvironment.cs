namespace Deps.CLI
{
    public enum PackageSourceEnvironment
    {
        NugetOrg = 0,
        /// <summary>
        /// CI feed on MyGet
        /// </summary>
        MyGetCi,
        /// <summary>
        /// Production feed on MyGet
        /// </summary>
        MyGet,
        /// <summary>
        /// Production feed on premise in BRF
        /// </summary>
        Brf
    }
}
