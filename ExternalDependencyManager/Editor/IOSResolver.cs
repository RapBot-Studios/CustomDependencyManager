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

        private const string FixDirectoriesInstruction =
            "post_install do |installer|\n\tinstaller.aggregate_targets.each do |target|\n\t\ttarget.xcconfigs.each do |variant, xcconfig|\n\t\t\txcconfig_path = target.client_root + target.xcconfig_relative_path(variant)\n\t\t\tIO.write(xcconfig_path, IO.read(xcconfig_path).gsub(\"DT_TOOLCHAIN_DIR\", \"TOOLCHAIN_DIR\"))\n\t\tend\n\tend\n\tinstaller.pods_project.targets.each do |target|\n\t\ttarget.build_configurations.each do |config|\n\t\t\tif config.base_configuration_reference.is_a? Xcodeproj::Project::Object::PBXFileReference\n\t\t\t\txcconfig_path = config.base_configuration_reference.real_path\n\t\t\t\tIO.write(xcconfig_path, IO.read(xcconfig_path).gsub(\"DT_TOOLCHAIN_DIR\", \"TOOLCHAIN_DIR\"))\n\t\t\tend\n\t\tend\n\tend\nend";
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
            VersionHandler.FindIOSPods(out var iosPods, out var podSources);
            var podfile = "";
            foreach (var source in podSources)
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
            foreach (var pod in iosPods)
            {
                podfile += string.Format(PodFormat, pod.package, pod.version, pod.xmlPath) + "\n";
            }

            podfile += "end\ntarget 'Unity-iPhone' do\nend\nuse_frameworks!\n" + FixDirectoriesInstruction;
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
                        Debug.LogError(result.output);
                        throw new BuildFailedException("Cocoapods repo update failed");
                    }
                }
                else
                {
                    Debug.LogError(result.output);
                    throw new BuildFailedException("Cocoapods install failed");
                }
            }
        }

        public int callbackOrder => 999;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform is BuildTarget.iOS)
            {
                GeneratePodfile(report.summary.outputPath);
                InstallPods(report.summary.outputPath);
            }
        }
    }
#endif
}