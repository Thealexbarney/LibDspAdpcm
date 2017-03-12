﻿using System;
using System.Diagnostics;
using VGAudio.Cli.Metadata;

namespace VGAudio.Cli
{
    public static class Converter
    {
        public static bool RunConverterCli(string[] args)
        {
            Options options = CliArguments.Parse(args);

            if (options == null) return false;

            if (options.Job == JobType.Convert)
            {
                Stopwatch watch = Stopwatch.StartNew();
                bool success = Convert.ConvertFile(options);
                watch.Stop();

                if (success)
                {
                    Console.WriteLine("Success!");
                    Console.WriteLine($"Time elapsed: {watch.Elapsed.TotalSeconds}");
                }
                else
                {
                    return false;
                }
            }

            if (options.Job == JobType.Metadata)
            {
                Print.PrintMetadata(options);
            }

            return true;
        }
    }
}