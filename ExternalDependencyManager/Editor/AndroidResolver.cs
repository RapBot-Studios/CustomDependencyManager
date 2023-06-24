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
#if UNITY_2022_1_OR_NEWER
            "        def unityProjectPath = {0}\n";
#else
        "([rootProject] + (rootProject.subprojects as List)).each {{ project ->\n    project.repositories {{\n        def unityProjectPath = {0}\n        maven {{\n            url \"https://maven.google.com\"\n        }}\n";
#endif
        private const string RepoSectionFooter =
#if UNITY_2022_1_OR_NEWER
            "        mavenLocal()";
#else
            "        mavenLocal()\n        mavenCentral()\n    }\n}";
#endif
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

            repoString += RepoSectionFooter;
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
#if UNITY_2022_1_OR_NEWER
            var settingsTemplatePath = Path.Combine(AndroidPluginsDir, "settingsTemplate.gradle");
            if (File.Exists(settingsTemplatePath))
            {
                var lines = File.ReadAllLines(settingsTemplatePath);
                var cleanedLines = RemoveResolverLines(lines);
                for (var i = 0; i < cleanedLines.Count; i++)
                {
                    if (cleanedLines[i].Contains("flatDir {"))
                    {
                        cleanedLines.Insert(i, GetRepoSection(repositories));
                        break;
                    }
                }

                File.WriteAllLines(settingsTemplatePath, cleanedLines);
            }
            else
            {
                throw new BuildFailedException("Settings Template Gradle file not found!");
            }
#endif
            var mainTemplatePath = Path.Combine(AndroidPluginsDir, "mainTemplate.gradle");
            if (File.Exists(mainTemplatePath))
            {
                var lines = File.ReadAllLines(mainTemplatePath);
                var cleanedLines = RemoveResolverLines(lines);
#if !UNITY_2022_1_OR_NEWER
                for (var i = 0; i < cleanedLines.Count; i++)
                {
                    if (cleanedLines[i].Contains("apply plugin: 'com.android.library'"))
                    {
                        cleanedLines.Insert(i, GetRepoSection(repositories));
                        break;
                    }
                }
#endif
                InsertDependenciesAndExclusions(cleanedLines, GetDependenciesSection(dependencies),
                    GetExclusionsSection());
                File.WriteAllLines(mainTemplatePath, cleanedLines);
            }
            else
            {
                throw new BuildFailedException("Main Template Gradle file not found!");
            }
        }

        public static List<string> RemoveResolverLines(string[] allLines)
        {
            var lines = new List<string>(allLines.Length);
            bool inResolverSection = false;
            for (var i = 0; i < allLines.Length; i++)
            {
                if (allLines[i].Contains("// Android Resolver"))
                {
                    inResolverSection = !inResolverSection;
                    continue;
                }

                if (!inResolverSection)
                {
                    lines.Add(allLines[i]);
                }
            }

            return lines;
        }

        public static void InsertRepos(List<string> lines, string repoSection)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains("apply plugin: 'com.android.library'"))
                {
                    lines.Insert(i, repoSection);
                    break;
                }
            }
        }

        public static void InsertDependenciesAndExclusions(List<string> lines, string dependenciesSection,
            string exclusions)
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains("**DEPS**}"))
                {
                    lines.Insert(i + 1, exclusions);
                    lines.Insert(i, dependenciesSection);
                    break;
                }
            }
        }

        public static string[] FindDependencies(string[] allLines, out int startPoint)
        {
            var startDependencies = 0;
            var endDependencies = 0;
            var resolverStart = -1;
            var resolverEnd = 0;
            for (var i = 0; i < allLines.Length; i++)
            {
                if (allLines[i].Contains("dependencies {"))
                {
                    startDependencies = i;
                }

                if (startDependencies > 0 && allLines[i].Contains("// Android Resolver Dependencies Start"))
                {
                    resolverStart = i;
                }

                if (resolverStart >= 0 && allLines[i].Contains("// Android Resolver Dependencies End"))
                {
                    resolverEnd = i;
                }

                if (allLines[i].Contains("**DEPS**"))
                {
                    endDependencies = i;
                    break;
                }
            }

            List<string> dependenciesSection = new List<string>();
            for (var i = startDependencies; i <= endDependencies; i++)
            {
                if (resolverStart >= 0 && i >= resolverStart && i <= resolverEnd) continue;
                dependenciesSection.Add(allLines[i]);
            }

            dependenciesSection.Insert(dependenciesSection.Count - 2, "");
            startPoint = startDependencies;
            return dependenciesSection.ToArray();
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