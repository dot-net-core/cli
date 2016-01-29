﻿using System;
using System.IO;
using Microsoft.DotNet.Cli.Utils;
using System.Collections.Generic;

namespace Microsoft.DotNet.Tools.Compiler.Native
{
    public class Program
    {
        public static int Main(string[] args)
        {
            DebugHelper.HandleDebugSwitch(ref args);

            return ExecuteApp(args);
        }        

        private static int ExecuteApp(string[] args)
        {   
            // Support Response File
            foreach(var arg in args)
            {
                if(arg.Contains(".rsp"))
                {
                    args = ParseResponseFile(arg);

                    if (args == null)
                    {
                        return 1;
                    }
                }
            }

            try
            {
                var cmdLineArgs = ArgumentsParser.Parse(args);

                if (cmdLineArgs.IsHelp) return cmdLineArgs.ReturnCode;

                var config = cmdLineArgs.GetNativeCompileSettings();

                DirectoryExtensions.CleanOrCreateDirectory(config.OutputDirectory);
                DirectoryExtensions.CleanOrCreateDirectory(config.IntermediateDirectory);

                // run mcg if requested
                if (config.EnableInterop)
                {
                    int exitCode = 0;
                    if ((exitCode = RunMcg(config)) > 0)
                    {
                        return exitCode;
                    }

                    string interopAssemblyPath = Path.Combine(Path.GetDirectoryName(config.InputManagedAssemblyPath), "Interop");
                    string inputAssemblyName = Path.GetFileNameWithoutExtension(config.InputManagedAssemblyPath);
                    config.AddReference(Path.Combine(interopAssemblyPath, inputAssemblyName + ".mcginterop.dll"));
                }

                var nativeCompiler = NativeCompiler.Create(config);

                var result = nativeCompiler.CompileToNative(config);

                return result ? 0 : 1;
            }
            catch (Exception ex)
            {
#if DEBUG
                Console.WriteLine(ex);
#else
                Reporter.Error.WriteLine(ex.Message);
#endif
                return 1;
            }
        }

        private static int RunMcg(NativeCompileSettings config)
        {
            var mcgArgs = new List<string>();
            string outPath = Path.Combine(Path.GetDirectoryName(config.InputManagedAssemblyPath), "Interop");
            mcgArgs.Add($"{config.InputManagedAssemblyPath}");
            mcgArgs.Add("--p");
            mcgArgs.Add(config.Architecture.ToString());
            mcgArgs.Add("--outputpath");
            mcgArgs.Add(outPath);

            var ilSdkPath = Path.Combine(config.IlcSdkPath, "sdk");
            foreach (string refPath in Directory.EnumerateFiles(ilSdkPath, "*.dll"))
            {

                mcgArgs.Add("--r");
                mcgArgs.Add(refPath);
            }

            foreach (string refPath in Directory.EnumerateFiles(config.AppDepSDKPath, "*.dll"))
            {
                // System.Runtime.Extensions define an internal type called System.Runtime.InteropServices.Marshal which 
                // conflicts with Marshal from S.P.Interop , we don't need System.Runtime.Extensions anyways,skip it.
                if (refPath.Contains("System.Runtime.Extensions.dll")) continue;

                mcgArgs.Add("--r");
                mcgArgs.Add(refPath);
            }

            // Write Response File
            var rsp = Path.Combine(config.IntermediateDirectory, $"dotnet-compile-mcg.rsp");
            File.WriteAllLines(rsp, mcgArgs);

            var result = Command.Create("dotnet-mcg", new string[] {"--rsp", $"{rsp}" })
                                .ForwardStdErr()
                                .ForwardStdOut()
                                .Execute();

            // Add interop assembly to project context 
            return result.ExitCode;
        }

        private static string[] ParseResponseFile(string rspPath)
        {
            if (!File.Exists(rspPath))
            {
                Reporter.Error.WriteLine("Invalid Response File Path");
                return null;
            }

            string content;
            try
            {
                content = File.ReadAllText(rspPath);
            }
            catch (Exception)
            {
                Reporter.Error.WriteLine("Unable to Read Response File");
                return null;
            }

            var nArgs = content.Split(new [] {"\r\n", "\n"}, StringSplitOptions.RemoveEmptyEntries);
            return nArgs;
        }
    }
}
