using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankNetworkLobby : MonoBehaviour
{
    const ushort Port = 7777;
    const string LoopbackAddress = "127.0.0.1";

    NetworkManager manager;
    UnityTransport transport;
    string address = LoopbackAddress;
    string status = "Выбери роль для проверки подключения.";

    void Awake()
    {
        transport = gameObject.AddComponent<UnityTransport>();
        manager = gameObject.AddComponent<NetworkManager>();
        manager.RunInBackground = true;
        manager.NetworkConfig = new NetworkConfig
        {
            NetworkTransport = transport,
            EnableSceneManagement = false,
            ConnectionApproval = true
        };
        manager.ConnectionApprovalCallback = ApproveConnection;
    }

    void ApproveConnection(NetworkManager.ConnectionApprovalRequest request,
        NetworkManager.ConnectionApprovalResponse response)
    {
        response.Approved = request.ClientNetworkId == NetworkManager.ServerClientId ||
            manager.ConnectedClientsIds.Count < 2;
        response.CreatePlayerObject = false;
        if (!response.Approved)
            response.Reason = "В сессии уже два игрока.";
    }

    public void Draw()
    {
        float width = Mathf.Min(310f, Screen.width - 24f);
        GUILayout.BeginArea(new Rect(Screen.width - width - 12f, 12f, width, 175f), GUI.skin.box);
        GUILayout.Label("Проверка сети · UDP " + Port);

        if (manager.IsListening || manager.ShutdownInProgress)
        {
            if (manager.IsHost)
                GUILayout.Label($"Игроков в сессии: {manager.ConnectedClientsIds.Count}/2");
            else
                GUILayout.Label(manager.IsConnectedClient ? "Подключено к хосту." : "Подключение…");

            if (GUILayout.Button("Отключиться"))
            {
                manager.Shutdown();
                status = "Соединение закрыто.";
            }
        }
        else
        {
            GUILayout.Label(status);
            GUILayout.Label("Адрес хоста");
            address = GUILayout.TextField(address);
            if (GUILayout.Button("Создать сессию"))
                Connect(true);
            if (GUILayout.Button("Подключиться"))
                Connect(false);
        }

        GUILayout.EndArea();
    }

    void Connect(bool host)
    {
        if (!host && string.IsNullOrWhiteSpace(address))
        {
            status = "Введи IP-адрес хоста.";
            return;
        }

        transport.SetConnectionData(host ? LoopbackAddress : address.Trim(), Port,
            host ? "0.0.0.0" : null);
        bool started = host ? manager.StartHost() : manager.StartClient();
        status = started ? "Сессия завершена или подключение не удалось." : "Не удалось запустить сеть.";
    }
}
