using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Google
{
#if UNITY_IOS
    public class IOSResolver : IPostprocessBuildWithReport
    {
        private const string SourceFormat = "source '{0}' #{1}";
        private const string PlatformFormat = "platform :{0}, '{1}'";
        private const string PodFormat = "  pod '{0}', '{1}' #{2}";

        private static bool HasCocoapods(out string path)
        {
            path = Path.Combine("/usr/local/bin", "pod");
            if (File.Exists(path))
            {
                return true;
            }

            path = Path.Combine("/usr/bin", "pod");
            if (File.Exists(path))
            {
                return true;
            }

            return false;
        }

        public static void GeneratePodfile(string xcodeProjectPath)
        {
            var podfile = "";
            foreach (var source in VersionHandler.Instance.podSources)
            {
                podfile += string.Format(SourceFormat, source.source, source.xmlPath) + "\n";
            }

            var platform = EditorUserBuildSettings.activeBuildTarget switch
            {
                BuildTarget.iOS => "ios",
                BuildTarget.tvOS => "tvos",
                _ => throw new BuildFailedException("Trying to generate podfile for platform " +
                                                    EditorUserBuildSettings.activeBuildTarget)
            };
            var targetIOSVersion =
                PlayerSettings.iOS.targetOSVersionString.Trim().Replace("iOS_", "").Replace("_", ".");
            podfile += string.Format(PlatformFormat, platform, targetIOSVersion) + "\n";
            podfile += "target 'UnityFramework' do\n";
            foreach (var pod in VersionHandler.Instance.iosPods)
            {
                podfile += string.Format(PodFormat, pod.package, pod.version, pod.xmlPath) + "\n";
            }

            podfile += "end\ntarget 'Unity-iPhone' do\nend\nuse_frameworks!";
            var podfilePath = Path.Combine(xcodeProjectPath, "Podfile");
            File.WriteAllText(podfilePath, podfile);
        }

        public static void InstallPods(string xcodeProjectPath, bool ranRepoUpdate = false)
        {
            if (!HasCocoapods(out var podPath))
            {
                throw new BuildFailedException("Cocoapods not found");
            }

            var result = VersionHandler.RunCommandLine(podPath, "install", xcodeProjectPath);
            if (result.Success)
            {
                Debug.Log(result.output);
            }
            else
            {
                if (!ranRepoUpdate)
                {
                    result = VersionHandler.RunCommandLine(podPath, "repo update", xcodeProjectPath);
                    if (result.Success)
                    {
                        InstallPods(xcodeProjectPath, true);
                    }
                    else
                    {
                        throw new BuildFailedException(result.error);
                    }
                }
                else
                {
                    throw new BuildFailedException(result.error);
                }
            }
        }

        public int callbackOrder => 999;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform is BuildTarget.iOS)
            {
                VersionHandler.Instance.FindDependencies();
                GeneratePodfile(report.summary.outputPath);
                InstallPods(report.summary.outputPath);
            }
        }
    }
#endif
}