using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;

namespace PleaseDontDrown.Editor
{
    public static class TripoBridgeSetup
    {
        public static void OpenAndVerify()
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies();
            Type window = null;
            foreach (var candidate in assembly)
                window ??= candidate.GetType("Tripo3D.UnityBridge.Editor.TripoWebSocketWindow");
            if (window == null) throw new InvalidOperationException("Tripo bridge assembly did not load");
            window.GetMethod("ShowWindow", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            var panel = EditorWindow.GetWindow(window);
            var server = window.GetField("_server", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(panel);
            if (server == null || !(bool)server.GetType().GetProperty("IsRunning").GetValue(server))
                throw new InvalidOperationException("Tripo bridge server did not start");
            var importer = window.Assembly.GetType("Tripo3D.UnityBridge.Editor.ModelImporter");
            var pipeline = importer.GetProperty("CurrentPipelineType").GetValue(null);
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(window.Assembly);
            Debug.Log($"[TripoSetup] PASS: bridge {package.version}, pipeline {pipeline}, local server 127.0.0.1:60610 running.");
        }
    }
}
