using System;
using UnityEditor;
using UnityEngine;

namespace MadMachines.Networking.Editor
{
    internal static class NetworkDevelopmentSettingsEditor
    {
        private const string SettingsPath = "Preferences/Mad Machines/Networking";
        private static readonly string[] Modes = { "Offline", "Development Online", "Remote Online" };

        [MenuItem("Tools/Mad Machines/Networking/Development Settings")]
        private static void Open() => SettingsService.OpenUserPreferences(SettingsPath);

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            NetworkDevelopmentSettings draft = null;
            string error = null;
            return new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                activateHandler = (_, _) =>
                {
                    try
                    {
                        draft = NetworkDevelopmentSettings.Load();
                        error = null;
                    }
                    catch (Exception exception)
                    {
                        draft = new NetworkDevelopmentSettings();
                        error = "Не удалось прочитать настройки. Проверьте поля и сохраните их. " + exception.Message;
                    }
                },
                guiHandler = _ =>
                {
                    draft.mode = (NetworkConnectionMode)EditorGUILayout.Popup("Режим подключения", (int)draft.mode, Modes);
                    EditorGUILayout.LabelField("Локальный адрес", NetworkDevelopmentSettings.LocalAddress);
                    draft.localPort = EditorGUILayout.IntField("Локальный UDP-порт", draft.localPort);
                    draft.remoteAddress = EditorGUILayout.TextField("Удалённый IPv4", draft.remoteAddress);
                    draft.remotePort = EditorGUILayout.IntField("Удалённый UDP-порт", draft.remotePort);
                    EditorGUILayout.HelpBox("Offline оставляет Playground автономным и выключает автоподключение NetworkSandbox. Настройки вступят в силу при следующем запуске клиента. Для смены порта работающего локального сервера используйте Restart Development Server.", MessageType.Info);
                    if (GUILayout.Button("Сохранить"))
                    {
                        try
                        {
                            draft.remoteAddress = draft.remoteAddress.Trim();
                            draft.Validate();
                            NetworkBuildOutput.WriteAtomically(NetworkDevelopmentSettings.FilePath, JsonUtility.ToJson(draft, true));
                            error = null;
                        }
                        catch (Exception exception)
                        {
                            error = exception.Message;
                        }
                    }
                    if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
                }
            };
        }
    }
}
