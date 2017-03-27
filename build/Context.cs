using System;
using System.Collections.Generic;
using Cake.Core;
using Cake.Core.IO;
using Cake.Frosting;

namespace Build
{
    public class Context : FrostingContext
    {
        public Context(ICakeContext context) : base(context) { }

        public string Configuration { get; set; }
        public bool IsReleaseBuild => Configuration.Equals("release", StringComparison.CurrentCultureIgnoreCase);

        public DirectoryPath BaseDir { get; set; }
        public DirectoryPath BuildDir { get; set; }
        public DirectoryPath CakeToolsDir { get; set; }

        public DirectoryPath SourceDir { get; set; }
        public DirectoryPath LibraryDir { get; set; }
        public DirectoryPath CliDir { get; set; }
        public DirectoryPath TestsDir { get; set; }
        public DirectoryPath BenchmarkDir { get; set; }
        public DirectoryPath UwpDir { get; set; }

        public FilePath SlnFile { get; set; }
        public FilePath TestsCsproj { get; set; }

        public DirectoryPath TopBinDir { get; set; }
        public DirectoryPath BinDir { get; set; }
        public DirectoryPath LibraryBinDir { get; set; }
        public DirectoryPath CliBinDir { get; set; }
        public DirectoryPath UwpBinDir { get; set; }

        public bool SignBuild { get; set; }
        public string ReleaseCertThumbprint { get; set; }

        public Dictionary<string, LibraryBuildStatus> LibBuilds { get; } = new Dictionary<string, LibraryBuildStatus>
        {
            ["netstandard"] = new LibraryBuildStatus("netstandard1.1", "netcoreapp1.0", "netcoreapp1.0"),
            ["net45"] = new LibraryBuildStatus("net45", "net45", "net46")
        };

        public Dictionary<string, bool?> OtherBuilds { get; } = new Dictionary<string, bool?>
        {
            ["uwp"] = null
        };
    }
}