using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using System.Text;
using System;
using TMPro;

[System.Serializable]
public class CreateRoomRequest
{
    public float time;
}

[System.Serializable]
public class CreateRoomResponse
{
    public string room_code;
    public string host_hash;
}

[System.Serializable]
public class JoinRoomResponse
{
    public string player_hash;
    public float time;
}

[System.Serializable]
public class MoveMessage
{
    public string type = "move";
    public MoveData move;
}

[System.Serializable]
public class MoveData
{
    public int from_rank;
    public int from_file;
    public int to_rank;
    public int to_file;
    public int from2_rank = -1;  // Use -1 to indicate null
    public int from2_file = -1;  // Use -1 to indicate null
    public int to2_rank = -1;    // Use -1 to indicate null
    public int to2_file = -1;    // Use -1 to indicate null
}

[System.Serializable]
public class StartGameMessage
{
    public string type = "start_game";
}

[System.Serializable]
public class ResignMessage
{
    public string type = "resign";
}

[System.Serializable]
public class TimerSyncMessage
{
    public string type = "timer_sync";
    public TimerData timer_data;
}

[System.Serializable]
public class TimerData
{
    public float white_time_left;
    public float black_time_left;
    public bool is_whites_turn;
}

[System.Serializable]
public class TimerTimeoutMessage
{
    public string type = "timer_timeout";
    public string timeout_side;
}

public class OnlineManager : MonoBehaviour
{
    private const string SERVER_URL = "http://localhost:8000";
    private const string WS_URL = "ws://localhost:8000";
    
    private string roomCode;
    private string playerHash;
    private bool isHost;
    private WebSocket webSocket;
    private GameManager gameManager;
    private int playerCount = 0;
    
    public System.Action<string> OnRoomCreated;
    public System.Action<string> OnRoomJoined;
    public System.Action<string, bool> OnGameStarted; // (side, isYourTurn)
    public System.Action<Move> OnMoveReceived;
    public System.Action<string> OnPlayerResigned;
    public System.Action<string> OnError;
    public System.Action<int> OnPlayerCountChanged; // (playerCount)
    public System.Action<float, float, bool> OnTimerSync; // (whiteTimeLeft, blackTimeLeft, isWhitesTurn)
    public System.Action<string> OnTimerTimeout; // (timeoutSide)

    private void Awake()
    {
        gameManager = GetComponent<GameManager>();
    }

    public void CreateRoom(float time)
    {
        StartCoroutine(CreateRoomCoroutine(time));
    }

    private IEnumerator CreateRoomCoroutine(float time)
    {
        CreateRoomRequest request = new CreateRoomRequest { time = time };
        string json = JsonUtility.ToJson(request);

        using (UnityWebRequest www = new UnityWebRequest($"{SERVER_URL}/create-room", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            www.uploadHandler = new UploadHandlerRaw(bodyRaw);
            www.downloadHandler = new DownloadHandlerBuffer();
            www.SetRequestHeader("Content-Type", "application/json");

            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                CreateRoomResponse response = JsonUtility.FromJson<CreateRoomResponse>(www.downloadHandler.text);
                roomCode = response.room_code;
                playerHash = response.host_hash;
                isHost = true;
                playerCount = 1; // Host is the first player
                
                Debug.Log($"Room created: {roomCode}");
                OnRoomCreated?.Invoke(roomCode);
                OnPlayerCountChanged?.Invoke(playerCount);
                
                // Connect to WebSocket
                ConnectWebSocket();
            }
            else
            {
                Debug.LogError($"Failed to create room: {www.error}");
                OnError?.Invoke($"Failed to create room: {www.error}");
            }
        }
    }

    public void JoinRoom(string code)
    {
        StartCoroutine(JoinRoomCoroutine(code));
    }

    private IEnumerator JoinRoomCoroutine(string code)
    {
        using (UnityWebRequest www = UnityWebRequest.Get($"{SERVER_URL}/join-room/{code}"))
        {
            yield return www.SendWebRequest();

            if (www.result == UnityWebRequest.Result.Success)
            {
                JoinRoomResponse response = JsonUtility.FromJson<JoinRoomResponse>(www.downloadHandler.text);
                roomCode = code;
                playerHash = response.player_hash;
                isHost = false;
                playerCount = 2; // Assume 2 players when joining (host + joiner)
                
                // Set the game time limit from the host's room settings
                if (gameManager != null)
                {
                    gameManager.SetStartTime(response.time);
                    Debug.Log($"Set game time limit to: {response.time} seconds");
                }
                
                Debug.Log($"Joined room: {roomCode}");
                OnRoomJoined?.Invoke(roomCode);
                OnPlayerCountChanged?.Invoke(playerCount);
                
                // Connect to WebSocket
                ConnectWebSocket();
            }
            else
            {
                Debug.LogError($"Failed to join room: {www.error}");
                OnError?.Invoke($"Failed to join room: {www.error}");
            }
        }
    }

    private void ConnectWebSocket()
    {
        string wsUrl = $"{WS_URL}/ws/{roomCode}/{playerHash}";
        webSocket = new WebSocket(wsUrl);
        
        webSocket.OnMessage += OnWebSocketMessage;
        webSocket.OnError += OnWebSocketError;
        webSocket.OnClose += OnWebSocketClose;
        
        webSocket.Connect();
        Debug.Log($"WebSocket connecting to: {wsUrl}");
    }

    private void OnWebSocketMessage(byte[] data)
    {
        string message = System.Text.Encoding.UTF8.GetString(data);
        Debug.Log($"WebSocket message received: {message}");
        
        try
        {
            // First, try to parse as a basic message to get the type
            var baseMessage = JsonUtility.FromJson<WebSocketMessage>(message);
            
            switch (baseMessage.type)
            {
                case "game_started":
                    Debug.Log("Game started message received");
                    HandleGameStartedMessage(message);
                    break;
                    
                case "move":
                    HandleMoveMessage(message);
                    break;
                    
                case "resign":
                    OnPlayerResigned?.Invoke(baseMessage.player_hash);
                    break;
                    
                case "error":
                    HandleErrorMessage(message);
                    break;
                    
                case "player_count_updated":
                    HandlePlayerCountUpdatedMessage(message);
                    break;
                    
                case "timer_sync":
                    HandleTimerSyncMessage(message);
                    break;
                    
                case "timer_timeout":
                    HandleTimerTimeoutMessage(message);
                    break;
                    
                default:
                    Debug.LogWarning($"Unknown message type: {baseMessage.type}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Error parsing WebSocket message: {e.Message}");
            Debug.LogError($"Message content: {message}");
        }
    }

    private void HandleMoveMessage(string message)
    {
        // Parse move data
        var moveObj = JsonUtility.FromJson<MoveMessage>(message);
        var moveData = moveObj.move;
        
        // Create Move object
        Coord from = new Coord(moveData.from_rank, moveData.from_file);
        Coord to = new Coord(moveData.to_rank, moveData.to_file);
        
        Coord from2 = null;
        Coord to2 = null;
        
        if (moveData.from2_rank != -1 && moveData.from2_file != -1 &&
            moveData.to2_rank != -1 && moveData.to2_file != -1)
        {
            from2 = new Coord(moveData.from2_rank, moveData.from2_file);
            to2 = new Coord(moveData.to2_rank, moveData.to2_file);
            Debug.Log($"Castling move received: King {from} to {to}, Rook {from2} to {to2}");
        }
        else
        {
            Debug.Log($"Regular move received: {from} to {to}");
        }
        
        Move move = new Move(from, to, from2, to2);
        OnMoveReceived?.Invoke(move);
    }
    
    private void HandleGameStartedMessage(string message)
    {
        // Parse game started message
        var gameStartedObj = JsonUtility.FromJson<GameStartedMessage>(message);
        
        Debug.Log($"Game started: Your side is {gameStartedObj.your_side}, Your turn: {gameStartedObj.is_your_turn}");
        
        // Invoke the event with side and turn information
        OnGameStarted?.Invoke(gameStartedObj.your_side, gameStartedObj.is_your_turn);
    }
    
    private void HandleErrorMessage(string message)
    {
        // Parse error message
        var errorObj = JsonUtility.FromJson<ErrorMessage>(message);
        
        // Find the ErrorAnnouncer component and display the error
        GameObject errorAnnouncer = GameObject.Find("ErrorAnnouncer");
        if (errorAnnouncer != null)
        {
            TMP_Text errorText = errorAnnouncer.GetComponent<TMP_Text>();
            if (errorText != null)
            {
                errorText.text = errorObj.message;
            }
        }
        
        Debug.LogError($"Server error: {errorObj.message}");
    }
    
    private void HandlePlayerCountUpdatedMessage(string message)
    {
        // Parse player count update message
        var playerCountObj = JsonUtility.FromJson<PlayerCountMessage>(message);
        
        // Update local player count
        playerCount = playerCountObj.player_count;
        
        Debug.Log($"Player count updated to: {playerCount}");
        
        // Trigger the event to update UI
        OnPlayerCountChanged?.Invoke(playerCount);
    }
    
    private void HandleTimerSyncMessage(string message)
    {
        // Parse timer sync message
        var timerSyncObj = JsonUtility.FromJson<TimerSyncReceivedMessage>(message);
        
        Debug.Log($"Timer sync received: White={timerSyncObj.timer_data.white_time_left}, Black={timerSyncObj.timer_data.black_time_left}, WhitesTurn={timerSyncObj.timer_data.is_whites_turn}");
        
        // Trigger the event to update timers
        OnTimerSync?.Invoke(timerSyncObj.timer_data.white_time_left, timerSyncObj.timer_data.black_time_left, timerSyncObj.timer_data.is_whites_turn);
    }
    
    private void HandleTimerTimeoutMessage(string message)
    {
        // Parse timer timeout message
        var timeoutObj = JsonUtility.FromJson<TimerTimeoutReceivedMessage>(message);
        
        Debug.Log($"Timer timeout received: {timeoutObj.timeout_side} timed out, winner: {timeoutObj.winner_side}");
        
        // Trigger the event to handle timeout
        OnTimerTimeout?.Invoke(timeoutObj.timeout_side);
    }

    private void OnWebSocketError(string errorMsg)
    {
        Debug.LogError($"WebSocket error: {errorMsg}");
        OnError?.Invoke($"WebSocket error: {errorMsg}");
    }

    private void OnWebSocketClose(WebSocketCloseCode closeCode)
    {
        Debug.Log($"WebSocket closed: {closeCode}");
    }

    public void SendMove(Move move)
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            MoveMessage moveMessage = new MoveMessage();
            moveMessage.move = new MoveData
            {
                from_rank = move.from.rank,
                from_file = move.from.file,
                to_rank = move.to.rank,
                to_file = move.to.file,
                from2_rank = move.from2?.rank ?? -1,
                from2_file = move.from2?.file ?? -1,
                to2_rank = move.to2?.rank ?? -1,
                to2_file = move.to2?.file ?? -1
            };
            
            // Debug logging for castling moves
            if (move.from2 != null && move.to2 != null)
            {
                Debug.Log($"Sending castling move: King {move.from} to {move.to}, Rook {move.from2} to {move.to2}");
            }
            else
            {
                Debug.Log($"Sending regular move: {move.from} to {move.to}");
            }
            
            string json = JsonUtility.ToJson(moveMessage);
            webSocket.Send(json);
            Debug.Log($"Sent move: {json}");
        }
    }

    public void SendStartGame()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open && isHost)
        {
            StartGameMessage startMessage = new StartGameMessage();
            string json = JsonUtility.ToJson(startMessage);
            webSocket.Send(json);
            Debug.Log("Sent start game message");
        }
    }

    public void SendResign()
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            ResignMessage resignMessage = new ResignMessage();
            string json = JsonUtility.ToJson(resignMessage);
            webSocket.Send(json);
            Debug.Log("Sent resign message");
        }
    }
    
    public void SendTimerSync(float whiteTimeLeft, float blackTimeLeft, bool isWhitesTurn)
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            TimerSyncMessage timerSyncMessage = new TimerSyncMessage();
            timerSyncMessage.timer_data = new TimerData
            {
                white_time_left = whiteTimeLeft,
                black_time_left = blackTimeLeft,
                is_whites_turn = isWhitesTurn
            };
            string json = JsonUtility.ToJson(timerSyncMessage);
            webSocket.Send(json);
            Debug.Log($"Sent timer sync: White={whiteTimeLeft}, Black={blackTimeLeft}, WhitesTurn={isWhitesTurn}");
        }
    }
    
    public void SendTimerTimeout(string timeoutSide)
    {
        if (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            TimerTimeoutMessage timeoutMessage = new TimerTimeoutMessage();
            timeoutMessage.timeout_side = timeoutSide;
            string json = JsonUtility.ToJson(timeoutMessage);
            webSocket.Send(json);
            Debug.Log($"Sent timer timeout: {timeoutSide}");
        }
    }

    private void Update()
    {
        if (webSocket != null)
        {
            webSocket.DispatchMessageQueue();
        }
    }

    private void OnDestroy()
    {
        if (webSocket != null)
        {
            webSocket.Close();
        }
    }

    public string GetRoomCode() => roomCode;
    public string GetPlayerHash() => playerHash;
    public bool IsHost() => isHost;
    public int GetPlayerCount() => playerCount;
}

[System.Serializable]
public class WebSocketMessage
{
    public string type;
    public string player_hash;
}

[System.Serializable]
public class GameStartedMessage
{
    public string type;
    public string your_side;
    public bool is_your_turn;
}

[System.Serializable]
public class ErrorMessage
{
    public string type;
    public string message;
}

[System.Serializable]
public class PlayerCountMessage
{
    public string type;
    public int player_count;
}

[System.Serializable]
public class TimerSyncReceivedMessage
{
    public string type;
    public TimerData timer_data;
}

[System.Serializable]
public class TimerTimeoutReceivedMessage
{
    public string type;
    public string timeout_side;
    public string winner_side;
} 