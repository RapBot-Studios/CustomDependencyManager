using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Google
{
    public class Settings : ScriptableObject
    {
        private const string AndroidPluginsFolder = "Assets/Plugins/Android";
        public const string SettingsPath = "Assets/ExternalDependencyManager/Editor/Settings.asset";
        private static Settings _instance;

        public static Settings Instance
        {
            get
            {
                if (_instance == null)
                {
                    if (File.Exists(SettingsPath))
                    {
                        _instance = AssetDatabase.LoadAssetAtPath<Settings>(SettingsPath);
                    }
                    else
                    {
                        _instance = CreateInstance();
                    }
                }

                return _instance;
            }
        }

        public static Settings CreateInstance()
        {
            AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<Settings>(), SettingsPath);
            AssetDatabase.SaveAssets();
            _instance = AssetDatabase.LoadAssetAtPath<Settings>(SettingsPath);
            EditorUtility.SetDirty(_instance);
            AssetDatabase.SaveAssetIfDirty(_instance);
            AssetDatabase.Refresh();
            return _instance;
        }

        [HideInInspector] public bool useJetifier;

        public void ConfirmSettings()
        {
            if (useJetifier && SetCustomTemplate("gradleTemplate.properties"))
            {
                EnableJetifier();
            }
        }

        public void EnableJetifier()
        {
            var gradlePropertiesPath = Path.Join(AndroidPluginsFolder, "gradleTemplate.properties");
            var lines = File.ReadAllLines(gradlePropertiesPath).ToList();
            bool enabledAndroidX = false;
            bool enabledJetifier = false;
            int additionalPropertiesLine = -1;
            for (var i = 0; i < lines.Count; i++)
            {
                if (lines[i].Contains("android.useAndroidX"))
                {
                    lines[i] = "android.useAndroidX=true";
                    enabledAndroidX = true;
                }
                else if (lines[i].Contains("android.enableJetifier"))
                {
                    lines[i] = "android.enableJetifier=true";
                    enabledJetifier = true;
                }
                else if (lines[i].Contains("**ADDITIONAL_PROPERTIES**"))
                {
                    additionalPropertiesLine = i;
                    break;
                }
            }

            if (!enabledJetifier)
            {
                lines.Insert(additionalPropertiesLine, "android.enableJetifier=true");
            }

            if (!enabledAndroidX)
            {
                lines.Insert(additionalPropertiesLine, "android.useAndroidX=true");
            }

            File.WriteAllLines(gradlePropertiesPath, lines);
        }

        public static bool SetCustomTemplate(string templateName)
        {
            var destPath = Path.Join(AndroidPluginsFolder, templateName);
            if (File.Exists(destPath))
            {
                return true;
            }
            else if (File.Exists(destPath + ".DISABLED"))
            {
                File.Move(destPath + ".DISABLED", destPath);
                return true;
            }
            else
            {
                return ResetTemplate(templateName);
            }
        }

        public static bool ResetTemplate(string templateName)
        {
            var templatesFolder = GetTemplatesFolder();
            var templatePath = Path.Join(templatesFolder, templateName);
            if (File.Exists(templatePath))
            {
                var destPath = Path.Join(AndroidPluginsFolder, templateName);
                if (File.Exists(destPath))
                {
                    File.Delete(destPath);
                }

                File.Copy(templatePath, destPath);
                return true;
            }

            return false;
        }

        public static string GetTemplatesFolder()
        {
            return Path.Join(Directory.GetParent(EditorApplication.applicationPath).FullName,
                "PlaybackEngines/AndroidPlayer/Tools/GradleTemplates");
        }

        [SettingsProvider]
        public static SettingsProvider CreatePreferencesGUI()
        {
            return new SettingsProvider("Project/External Dependency Manager", SettingsScope.Project)
            {
                guiHandler = (searchContext) => PreferencesGUI(),
                keywords = new System.Collections.Generic.HashSet<string>() {"External", "Dependency", "Manager"}
            };
        }

        public static void PreferencesGUI()
        {
            EditorGUI.BeginChangeCheck();
            if (File.Exists("Assets/Plugins/Android/mainTemplate.gradle"))
            {
                EditorGUILayout.HelpBox("Main Template Gradle file is custom!",
                    MessageType.Info);
                if (GUILayout.Button("Reset to Unity's Template"))
                {
                    ResetTemplate("mainTemplate.gradle");
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Main Template Gradle file is not custom!",
                    MessageType.Error);
                if (GUILayout.Button("Set Custom"))
                {
                    SetCustomTemplate("mainTemplate.gradle");
                }
            }

            if (Instance.useJetifier)
            {
                if (File.Exists("Assets/Plugins/Android/gradleTemplate.properties"))
                {
                    EditorGUILayout.HelpBox("Gradle properties is custom!",
                        MessageType.Info);
                    if (GUILayout.Button("Reset to Unity's Template"))
                    {
                        ResetTemplate("gradleTemplate.properties");
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("Gradle properties is not custom!",
                        MessageType.Error);
                    if (GUILayout.Button("Set Custom"))
                    {
                        SetCustomTemplate("gradleTemplate.properties");
                    }
                }
            }

            Instance.useJetifier =
                EditorGUILayout.Toggle(
                    new GUIContent("Use Jetifier",
                        "Legacy Android support libraries and references to them from other libraries will be rewritten to use Jetpack using the Jetifier tool. Enabling option allows an application to use Android Jetpack when other libraries in the project use the Android support libraries."),
                    Instance.useJetifier);

            if (EditorGUI.EndChangeCheck())
            {
                Instance.ConfirmSettings();
                EditorUtility.SetDirty(Instance);
                AssetDatabase.SaveAssetIfDirty(Instance);
            }
        }
    }
}