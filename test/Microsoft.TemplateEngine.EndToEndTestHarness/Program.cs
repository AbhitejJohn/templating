﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Microsoft.DotNet.Cli.Utils;
using Microsoft.TemplateEngine.Abstractions;
using Microsoft.TemplateEngine.Abstractions.PhysicalFileSystem;
using Microsoft.TemplateEngine.Cli;
using Microsoft.TemplateEngine.Edge;
using Microsoft.TemplateEngine.Utils;
using Newtonsoft.Json.Linq;

namespace Microsoft.TemplateEngine.EndToEndTestHarness
{
    class Program
    {
        private const string HostIdentifier = "endtoendtestharness";
        private const string HostVersion = "1.0.0";
        private const string CommandName = "test-test";
        private static readonly Dictionary<string, Func<IPhysicalFileSystem, JObject, string, bool>> VerificationLookup = new Dictionary<string, Func<IPhysicalFileSystem, JObject, string, bool>>(StringComparer.OrdinalIgnoreCase);

        static int Main(string[] args)
        {
            VerificationLookup["dir_exists"] = CheckDirectoryExists;
            VerificationLookup["file_exists"] = CheckFileExists;
            VerificationLookup["dir_does_not_exist"] = CheckDirectoryDoesNotExist;
            VerificationLookup["file_does_not_exist"] = CheckFileDoesNotExist;
            VerificationLookup["file_contains"] = CheckFileContains;
            VerificationLookup["file_does_not_contain"] = CheckFileDoesNotContain;

            int batteryCount = int.Parse(args[0], CultureInfo.InvariantCulture);
            string[] passthroughArgs = new string[args.Length - 2 - batteryCount];
            string outputPath = args[batteryCount + 1];

            for(int i = 0; i < passthroughArgs.Length; ++i)
            {
                passthroughArgs[i] = args[i + 2 + batteryCount];
            }

            ITemplateEngineHost host = CreateHost();
            string profileDir = Environment.ExpandEnvironmentVariables("%USERPROFILE%");

            if (string.IsNullOrWhiteSpace(profileDir))
            {
                profileDir = Environment.ExpandEnvironmentVariables("$HOME");
            }
            if (string.IsNullOrWhiteSpace(profileDir))
            {
                Console.Error.WriteLine("Could not determine home directory");
                return 0;
            }

            host.VirtualizeDirectory(Path.Combine(profileDir, ".templateengine"));
            host.VirtualizeDirectory(outputPath);

            int result = New3Command.Run(CommandName, host, FirstRun, passthroughArgs);
            bool verificationsPassed = false;

            for (int i = 0; i < batteryCount; ++i)
            {
                string verificationsFile = args[i + 1];
                string verificationsFileContents = File.ReadAllText(verificationsFile);
                JArray verifications = JArray.Parse(verificationsFileContents);

                try
                {
                    verificationsPassed = RunVerifications(verifications, host.FileSystem, outputPath);
                }
                catch (Exception ex)
                {
                    verificationsPassed = false;
                    Console.Error.WriteLine(ex.ToString());
                }
            }

            return result != 0 ? result : verificationsPassed ? 0 : 1;
        }

        private static bool CheckFileDoesNotContain(IPhysicalFileSystem fs, JObject config, string outputPath)
        {
            string path = Path.Combine(outputPath, config["path"].ToString());
            string text = fs.ReadAllText(path);
            if (!text.Contains(config["text"].ToString()))
            {
                return true;
            }

            Console.Error.WriteLine($"Expected {path} to not contain {config["text"].ToString()} but it did");
            return false;
        }

        private static bool CheckFileContains(IPhysicalFileSystem fs, JObject config, string outputPath)
        {
            string path = Path.Combine(outputPath, config["path"].ToString());
            string text = fs.ReadAllText(path);
            if (text.Contains(config["text"].ToString()))
            {
                return true;
            }

            Console.Error.WriteLine($"Expected {path} to contain {config["text"].ToString()} but it did not");
            return false;
        }

        private static bool CheckFileExists(IPhysicalFileSystem fs, JObject config, string outputPath)
        {
            string path = Path.Combine(outputPath, config["path"].ToString());
            if(fs.FileExists(path))
            {
                return true;
            }
            
            Console.Error.WriteLine($"Expected a file {path} to exist but it did not");
            return false;
        }

        private static bool CheckDirectoryExists(IPhysicalFileSystem fs, JObject config, string outputPath)
        {
            string path = Path.Combine(outputPath, config["path"].ToString());
            if (fs.DirectoryExists(path))
            {
                return true;
            }

            Console.Error.WriteLine($"Expected a directory {path} to exist but it did not");
            return false;
        }

        private static bool CheckFileDoesNotExist(IPhysicalFileSystem fs, JObject config, string outputPath)
        {
            string path = Path.Combine(outputPath, config["path"].ToString());
            if (!fs.FileExists(path))
            {
                return true;
            }

            Console.Error.WriteLine($"Expected a file {path} to not exist but it did");
            return false;
        }

        private static bool CheckDirectoryDoesNotExist(IPhysicalFileSystem fs, JObject config, string outputPath)
        {
            string path = Path.Combine(outputPath, config["path"].ToString());
            if (!fs.DirectoryExists(path))
            {
                return true;
            }

            Console.Error.WriteLine($"Expected a directory {path} to not exist but it did");
            return false;
        }

        private static bool RunVerifications(JArray verifications, IPhysicalFileSystem fs, string outputPath)
        {
            bool success = true;
            foreach(JObject verification in verifications)
            {
                string kind = verification["kind"].ToString();
                if (!VerificationLookup.TryGetValue(kind, out Func<IPhysicalFileSystem, JObject, string, bool> func))
                {
                    Console.Error.WriteLine($"Unable to find a verification handler for {kind}");
                    return false;
                }

                success &= func(fs, verification, outputPath);
            }
            return success;
        }

        private static ITemplateEngineHost CreateHost()
        {
            var preferences = new Dictionary<string, string>
            {
                { "prefs:language", "C#" }
            };

            try
            {
                string versionString = Command.CreateDotNet("", new[] { "--version" }).CaptureStdOut().Execute().StdOut;
                if (!string.IsNullOrWhiteSpace(versionString))
                {
                    preferences["dotnet-cli-version"] = versionString.Trim();
                }
            }
            catch
            { }

            return new DefaultTemplateEngineHost(HostIdentifier, HostVersion, CultureInfo.CurrentCulture.Name, preferences, new[] { "dotnetcli" });
        }

        private static void FirstRun(IEngineEnvironmentSettings environmentSettings, IInstaller installer)
        {
            string codebase = typeof(Program).GetTypeInfo().Assembly.CodeBase;
            Uri cb = new Uri(codebase);
            string asmPath = cb.LocalPath;
            string dir = Path.GetDirectoryName(asmPath);

            string packages = Path.Combine(dir, "..", "..", "..", "..", "..", "artifacts", "packages") + Path.DirectorySeparatorChar + "*";
            string templates = Path.Combine(dir, "..", "..", "..", "..", "..", "template_feed") + Path.DirectorySeparatorChar;
            installer.InstallPackages(new[] { packages });
            installer.InstallPackages(new[] { templates });
        }
    }
}
