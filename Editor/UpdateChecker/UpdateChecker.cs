using UnityEditor;
using Wagenheimer.PackageHub.Editor;

namespace Wagenheimer.Tk2dPorter.Editor
{
    public static class UpdateChecker
    {
        [MenuItem("Tools/Wagenheimer/Tk2d Porter/Check for Updates...", priority = 100)]
        public static void CheckForUpdateMenu() => CheckForUpdate(true);

        public static void CheckForUpdate(bool force = false)
        {
            PackageHubWindow.OpenToPackage("com.wagenheimer.tk2dporter");
        }
    }
}
