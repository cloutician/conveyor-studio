using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

[InitializeOnLoad]
public static class ConveyorReleaseBuild
{
    private const string PendingKey = "ConveyorStudio.ReleaseBuildPending";
    private const string ReportPath = "Library/ConveyorWindowsReleaseReport.json";

    [Serializable] private sealed class Report
    {
        public bool success;
        public string result, executable, unityVersion, completedUtc, error;
        public ulong bytes;
        public double seconds;
        public int errors, warnings;
    }

    static ConveyorReleaseBuild()
    {
        EditorApplication.playModeStateChanged += state =>
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PendingKey, false)) return;
            SessionState.SetBool(PendingKey, false); EditorApplication.delayCall += BuildWindows;
        };
    }

    [MenuItem("Tools/Conveyor Studio/Export Windows Release %#&F8")]
    public static void Export()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            SessionState.SetBool(PendingKey, true); EditorApplication.isPlaying = false;
            return;
        }
        BuildWindows();
    }

    public static void BuildWindows()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        { EditorApplication.delayCall += BuildWindows; return; }
        var result = new Report { unityVersion = Application.unityVersion };
        try
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Standalone, BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Windows build support is not installed for this Unity editor.");
            if (!File.Exists(ConveyorStudioBuilder.ScenePath)) throw new FileNotFoundException("The studio scene is missing.");

            PlayerSettings.companyName = "George"; PlayerSettings.productName = "Conveyor Studio";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.defaultScreenWidth = 1600; PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed; PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true; PlayerSettings.usePlayerLog = true;
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone, "com.george.conveyorstudio");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Studio/ConveyorStudioIcon.png");
            if (icon != null)
            {
                int count = PlayerSettings.GetIconSizes(NamedBuildTarget.Standalone, IconKind.Any).Length;
                if (count > 0) PlayerSettings.SetIcons(NamedBuildTarget.Standalone, Enumerable.Repeat(icon, count).ToArray(), IconKind.Any);
            }
            AssetDatabase.SaveAssets();

            string folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../output/submission/ConveyorStudio-Windows"));
            Directory.CreateDirectory(folder); result.executable = Path.Combine(folder, "ConveyorStudio.exe");
            Debug.Log("Exporting the Windows release to " + folder);
            var build = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ConveyorStudioBuilder.ScenePath },
                locationPathName = result.executable,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.CompressWithLz4HC
            });
            var summary = build.summary;
            result.success = summary.result == BuildResult.Succeeded;
            result.result = summary.result.ToString(); result.bytes = summary.totalSize;
            result.seconds = summary.totalTime.TotalSeconds; result.errors = summary.totalErrors; result.warnings = summary.totalWarnings;
            if (!result.success) result.error = "Windows export did not succeed; inspect the build report and Console.";
        }
        catch (Exception error) { result.error = error.ToString(); Debug.LogException(error); }
        result.completedUtc = DateTime.UtcNow.ToString("O");
        File.WriteAllText(ReportPath, JsonUtility.ToJson(result, true));
        if (result.success) Debug.Log("Windows release exported successfully: " + result.executable);
        else Debug.LogError("Windows release export failed. See " + ReportPath);
    }
}
