using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Google
{
    public static class VersionHandler
    {
#if UNITY_ANDROID
        public static void FindAndroidDependencies(out Dependency[] androidDependencies, out Source[] repositories)
        {
            var _androidDependencies = new List<Dependency>();
            var _repositories = new HashSet<Source>();
            var xmlPaths = FindAllDependencyXMLs();
            foreach (var path in xmlPaths)
            {
                ParseXML(path, out var dependencies, out var pods, out var repos, out var sources);
                foreach (var dependency in dependencies)
                {
                    var addedDependency = false;
                    for (var i = 0; i < _androidDependencies.Count; i++)
                    {
                        if (!_androidDependencies[i].Equals(dependency)) continue;
                        if (!dependency.Compare(_androidDependencies[i])) continue;
                        _androidDependencies[i] = dependency;
                        addedDependency = true;
                        break;
                    }

                    if (!addedDependency)
                    {
                        _androidDependencies.Add(dependency);
                    }
                }

                _repositories.UnionWith(repos);
            }

            androidDependencies = _androidDependencies.ToArray();
            repositories = _repositories.ToArray();
        }
#elif UNITY_IOS
        public static void FindIOSPods(out Dependency[] iosPods, out Source[] podSources)
        {
            var _iosPods = new List<Dependency>();
            var _podSources = new HashSet<Source>();
            var xmlPaths = FindAllDependencyXMLs();
            foreach (var path in xmlPaths)
            {
                ParseXML(path, out var dependencies, out var pods, out var repos, out var sources);
                foreach (var pod in pods)
                {
                    var addedDependency = false;
                    for (var i = 0; i < _iosPods.Count; i++)
                    {
                        if (!_iosPods[i].Equals(pod)) continue;
                        if (!pod.Compare(_iosPods[i])) continue;
                        _iosPods[i] = pod;
                        addedDependency = true;
                        break;
                    }

                    if (!addedDependency)
                    {
                        _iosPods.Add(pod);
                    }
                }

                _podSources.UnionWith(sources);
            }

            iosPods = _iosPods.ToArray();
            podSources = _podSources.ToArray();
        }
#endif
        private const string DependencyPattern = @".*[/\\]Editor[/\\].*Dependencies\.xml$";
        private static bool IsDependenciesFile(string filename) => Regex.Match(filename, DependencyPattern).Success;

        private static string[] FindAllDependencyXMLs()
        {
            var guids = AssetDatabase.FindAssets("Dependencies t:TextAsset", new[] {"Assets", "Packages"});
            var paths = new List<string>(guids.Count());
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (IsDependenciesFile(path))
                    paths.Add(path);
            }

            return paths.ToArray();
        }

        private static void ParseXML(string xmlPath, out List<Dependency> androidDependencies,
            out List<Dependency> iosPods, out List<Source> repositories, out List<Source> podSources)
        {
            androidDependencies = new List<Dependency>();
            iosPods = new List<Dependency>();
            repositories = new List<Source>();
            podSources = new List<Source>();
            try
            {
                using (var xmlReader = new XmlTextReader(new StreamReader(xmlPath)))
                {
                    while (xmlReader.Read())
                    {
                        if (!xmlReader.IsStartElement()) continue;
                        if (xmlReader.Name == "repository")
                        {
                            repositories.Add(new Source(xmlReader.ReadString(), xmlPath));
                        }
                        else if (xmlReader.Name == "source")
                        {
                            podSources.Add(new Source(xmlReader.ReadString(), xmlPath));
                        }
                        else
                        {
                            if (xmlReader.AttributeCount == 0) continue;
                            if (xmlReader.Name == "androidPackage")
                            {
                                var packageSplit = xmlReader.GetAttribute(0).Split(':');
                                var package = "";
                                for (var i = 0; i < packageSplit.Length - 1; i++)
                                {
                                    if (i > 0)
                                    {
                                        package += ":";
                                    }

                                    package += packageSplit[i];
                                }

                                androidDependencies.Add(new Dependency(package, packageSplit[^1], xmlPath));
                            }
                            else if (xmlReader.Name == "iosPod")
                            {
                                iosPods.Add(new Dependency(xmlReader.GetAttribute(0), xmlReader.GetAttribute(1),
                                    xmlPath));
                            }
                        }
                    }
                }
            }
            catch (System.Exception e)
            {
                System.Console.WriteLine(e);
                throw;
            }
        }

        public static CmdResult RunCommandLine(string toolPath, string arguments, string workingDirectory = null)
        {
            var inputEncoding = System.Console.InputEncoding;
            var outputEncoding = System.Console.OutputEncoding;
            try
            {
                System.Console.InputEncoding = System.Text.Encoding.UTF8;
                System.Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(string.Format(
                    "Unable to set console input / output encoding from {0} & {1} to {2} " +
                    "(e.g en_US.UTF8-8). Some commands may fail. {3}",
                    System.Console.InputEncoding, System.Console.OutputEncoding, System.Text.Encoding.UTF8, e));
            }

            if (!(toolPath.StartsWith("\"") || toolPath.StartsWith("'")))
            {
                // If the path isn't quoted normalize separators.
                // Windows can't execute commands using POSIX paths.
                toolPath = toolPath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            }

            var startInfo = new ProcessStartInfo(toolPath, arguments)
            {
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                RedirectStandardInput = false,
                UseShellExecute = false,
                WorkingDirectory = workingDirectory ?? System.Environment.CurrentDirectory,
            };
            startInfo.EnvironmentVariables["LANG"] =
                (System.Environment.GetEnvironmentVariable("LANG") ?? "en_US.UTF-8").Split('.')[0] + ".UTF-8";
            startInfo.EnvironmentVariables["PATH"] =
                "/usr/local/bin:" + (System.Environment.GetEnvironmentVariable("PATH") ?? "");

            var process = Process.Start(startInfo);

            process.WaitForExit();
            var result = new CmdResult()
            {
                exitCode = process.ExitCode,
                output = process.StandardOutput.ReadToEnd(),
                error = process.StandardError.ReadToEnd()
            };
            try
            {
                System.Console.InputEncoding = inputEncoding;
                System.Console.OutputEncoding = outputEncoding;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(string.Format(
                    "Unable to restore console input / output  encoding to {0} & {1}. {2}",
                    inputEncoding, outputEncoding, e));
            }

            return result;
        }
    }

    public struct CmdResult
    {
        public bool Success => exitCode == 0;
        public int exitCode;
        public string output;
        public string error;
    }

    [System.Serializable]
    public struct Dependency : System.IEquatable<Dependency>
    {
        public string package;
        public string version;
        public string xmlPath;

        public int[] VersionSplit
        {
            get
            {
                var split = version.Split('.');
                var versionSplit = new int[split.Length];
                for (var i = 0; i < split.Length; i++)
                {
                    int.TryParse(split[i], out versionSplit[i]);
                }

                return versionSplit;
            }
        }

        public bool Equals(Dependency other) => package == other.package;
        public override bool Equals(object obj) => obj is Dependency other && Equals(other);
        public override int GetHashCode() => package != null ? package.GetHashCode() : 0;

        public Dependency(string package, string version, string xmlPath)
        {
            this.package = package;
            this.version = version;
            this.xmlPath = xmlPath;
        }

        public int GetVersionNum(int numToMultiply) => GetVersionNum(VersionSplit, numToMultiply);

        public int GetVersionNum(int[] versionSplit, int numToMultiply)
        {
            var versionNum = 0;
            var multiplier = 1;
            if (numToMultiply > versionSplit.Length)
            {
                for (var i = 0; i < numToMultiply - versionSplit.Length; i++)
                {
                    multiplier *= 100;
                }
            }

            for (var i = versionSplit.Length - 1; i >= 0; i--)
            {
                versionNum += versionSplit[i] * multiplier;
                multiplier *= 100;
            }

            return versionNum;
        }

        public bool Compare(Dependency dependency)
        {
            var otherVersionSplit = dependency.VersionSplit;
            var versionSplit = VersionSplit;
            var numToMultiply = Mathf.Max(otherVersionSplit.Length, versionSplit.Length);
            return dependency.GetVersionNum(otherVersionSplit, numToMultiply) >
                   GetVersionNum(versionSplit, numToMultiply);
        }
    }

    [System.Serializable]
    public struct Source : System.IEquatable<Source>
    {
        public string source;
        public string xmlPath;
        public bool Equals(Source other) => source == other.source;
        public override bool Equals(object obj) => obj is Source other && Equals(other);
        public override int GetHashCode() => source != null ? source.GetHashCode() : 0;

        public Source(string source, string xmlPath)
        {
            this.source = source;
            this.xmlPath = xmlPath;
        }
    }
}