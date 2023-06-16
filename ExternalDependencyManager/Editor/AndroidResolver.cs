using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Google
{
#if UNITY_ANDROID
    public class AndroidResolver : IPreprocessBuildWithReport
    {
        private const string AndroidPluginsDir = "Assets/Plugins/Android";

        private const string RepoSectionHeader =
            "([rootProject] + (rootProject.subprojects as List)).each {{ project ->\n    project.repositories {{\n        def unityProjectPath = {0}\n        maven {{\n            url \"https://maven.google.com\"\n        }}\n";

        private const string RepoSectionFormat = "// Android Resolver Repos Start\n{0}\n// Android Resolver Repos End";

        private const string DependencySectionFormat =
            "// Android Resolver Dependencies Start\n{0}// Android Resolver Dependencies End";

        private const string ExclusionsSectionFormat =
            "// Android Resolver Exclusions Start\nandroid {{\n  packagingOptions {{\n{0}  }}\n}}\n// Android Resolver Exclusions End";

        private const string RepositoryFormat = "        maven {{\n           url \"{0}\"  //{1}\n        }}\n";
        private const string DependencyFormat = "    implementation '{0}:{1}' // {2}\n";
        private const string ExclusionFormat = "      exclude ('/lib/{0}/*' + '*')\n";

        private static string[] AllSupportedABI =
            {"armeabi", "armeabi-v7a", "arm64-v8a", "x86", "x86_64", "mips", "mips64"};

        private const string RepoProjectPath = "$/file:///**DIR_UNITYPROJECT**/$.replace(\"\\\\\", \"/\")";

        private static string GetRepoSection(Source[] repositories)
        {
            var repoString = string.Format(RepoSectionHeader, RepoProjectPath);
            foreach (var repo in repositories)
            {
                repoString += string.Format(RepositoryFormat, repo.source, repo.xmlPath);
            }

            repoString += "        mavenLocal()\n        mavenCentral()\n    }\n}";
            return string.Format(RepoSectionFormat, repoString);
        }

        private static string GetDependenciesSection(Dependency[] dependencies)
        {
            var dependenciesString = "";
            foreach (var dependency in dependencies)
            {
                dependenciesString += string.Format(DependencyFormat, dependency.package, dependency.version,
                    dependency.xmlPath);
            }

            return string.Format(DependencySectionFormat, dependenciesString);
        }

        private static string GetExclusionsSection()
        {
            var abis = GetExcludedABIs();
            var exclusionsString = "";
            foreach (var abi in abis)
            {
                exclusionsString += string.Format(ExclusionFormat, abi);
            }

            return string.Format(ExclusionsSectionFormat, exclusionsString);
        }

        private static List<string> GetExcludedABIs()
        {
            List<string> allABIs = AllSupportedABI.ToList();
            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARMv7) != 0)
            {
                allABIs.Remove("armeabi-v7a");
            }

            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.ARM64) != 0)
            {
                allABIs.Remove("arm64-v8a");
            }

            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.X86) != 0)
            {
                allABIs.Remove("x86");
            }

            if ((PlayerSettings.Android.targetArchitectures & AndroidArchitecture.X86_64) != 0)
            {
                allABIs.Remove("x86_64");
            }

            return allABIs;
        }

        public static void PatchMainTemplate()
        {
            VersionHandler.FindAndroidDependencies(out var dependencies, out var repositories);
            var mainTemplatePath = Path.Combine(AndroidPluginsDir, "mainTemplate.gradle");
            var lines = File.ReadAllLines(mainTemplatePath);
            var fullString = new List<string>();
            string[] header = {""};
            string[] repos = {""};
            string[] applyPlugins = {""};
            string[] dependenciesHeader = {""};
            string dependenciesFooter = "**DEPS**}";
            string[] exclusions = {""};
            string[] footer = {""};
            var readStep = 0;
            var startStep = 0;
            for (var i = 0; i < lines.Length; i++)
            {
                if (readStep == 0)
                {
                    if (startStep == 0)
                    {
                        if (lines[i] == "// Android Resolver Repos Start")
                        {
                            startStep = i;
                        }
                        else if (lines[i].Contains("[rootProject]"))
                        {
                            startStep = string.IsNullOrWhiteSpace(lines[i - 1]) ? i - 1 : i;
                        }

                        header = lines[..startStep];
                    }
                    else
                    {
                        if (lines[i] == "// Android Resolver Repos End")
                        {
                            repos = lines[startStep..(i + 1)];
                            readStep = 1;
                            startStep = i + 1;
                        }
                        else if (lines[i].StartsWith("apply plugin:"))
                        {
                            repos = lines[startStep..i];
                            readStep = 1;
                            startStep = i;
                        }
                    }
                }
                else if (readStep == 1)
                {
                    if (lines[i].StartsWith("dependencies"))
                    {
                        applyPlugins = lines[startStep..i];
                        startStep = i;
                    }
                    else
                    {
                        if (lines[i] == "// Android Resolver Dependencies Start" || lines[i] == dependenciesFooter)
                        {
                            dependenciesHeader = lines[startStep..i];
                            startStep = 0;
                            readStep++;
                        }
                    }
                }
                else if (readStep == 2)
                {
                    if (startStep == 0)
                    {
                        if (lines[i] == "// Android Resolver Exclusions Start")
                        {
                            startStep = i;
                        }
                        else if (lines[i].StartsWith("android {"))
                        {
                            startStep = i;
                        }
                    }
                    else
                    {
                        if (lines[i] == "// Android Resolver Exclusions End")
                        {
                            exclusions = lines[startStep..(i + 1)];
                            footer = lines[(i + 1)..];
                            break;
                        }

                        if (lines[i].Contains("compileSdkVersion"))
                        {
                            exclusions = lines[startStep..(i - 1)];
                            footer = lines[(i - 1)..];
                            break;
                        }
                    }
                }
            }

            fullString.AddRange(header);
            fullString.Add(GetRepoSection(repositories));
            fullString.AddRange(applyPlugins);
            fullString.AddRange(dependenciesHeader);
            fullString.Add(GetDependenciesSection(dependencies));
            fullString.Add(dependenciesFooter + "\n");
            fullString.Add(GetExclusionsSection());
            fullString.AddRange(footer);
            File.WriteAllLines(mainTemplatePath, fullString);
        }

        public int callbackOrder => -1;

        public void OnPreprocessBuild(BuildReport report)
        {
            if (report.summary.platform is BuildTarget.Android)
            {
                PatchMainTemplate();
            }
        }
    }
#endif
}