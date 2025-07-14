using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GameManager : MonoBehaviour
{
    // Script References
    Board board;
    BoardUI boardUI;
    MovesHandler movesHandler;
    AIPlayer aiPlayer;
    UIManager uiManager;
    OnlineManager onlineManager;

    // Game Modes
    public enum GameMode {
        Local,
        Online,
        Stockfish
    }

    public GameMode gameMode;

    // Start Positions
    const string FEN_START = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR";
    const string FEN_CASTLING = "r3k2r/pppppppp/8/8/8/8/PPPPPPPP/R3K2R";
    const string FEN_CHECK_TEST = "2K5/q5QQ/4k3/8/8/8/8/8 b - - 0 2";

    // Game running
    public bool gameRunning = false;
    public bool canStartGame = false;

    // Whose turn
    public bool startPlayerIsWhite = true;
    public bool isWhitesTurn = true;

    public bool boardFlipped = false;

    // Match duration
    float startTime = 300f;
    float whitesTimeLeft, blacksTimeLeft;
    
    // Stockfish search depth
    int stockfishDepth = 5;
    
    // Online move tracking
    private Move lastMove;

    private void Awake() {
        // Get script references
        boardUI = FindObjectOfType<BoardUI>();
        board = GetComponent<Board>();
        movesHandler = GetComponent<MovesHandler>();
        aiPlayer = GetComponent<AIPlayer>();
        uiManager = GetComponent<UIManager>();
        onlineManager = GetComponent<OnlineManager>();

        // Setup game
        boardUI.CreateBoardUI();
        SetupGame();
        
        // Setup online manager events
        if (onlineManager != null)
        {
            onlineManager.OnRoomCreated += OnRoomCreated;
            onlineManager.OnRoomJoined += OnRoomJoined;
            onlineManager.OnGameStarted += OnOnlineGameStarted;
            onlineManager.OnMoveReceived += OnOnlineMoveReceived;
            onlineManager.OnPlayerResigned += OnOnlinePlayerResigned;
            onlineManager.OnError += OnOnlineError;
            onlineManager.OnPlayerCountChanged += OnPlayerCountChanged;
            onlineManager.OnTimerSync += OnTimerSync;
            onlineManager.OnTimerTimeout += OnTimerTimeout;
        }
    }

    private void Update(){
        if (gameRunning){
            UpdateTimer();
        }
    }

    public void SetupGame(){
        // Set start position - temporarily use castling position for testing
        board.PositionFromFen(FEN_START);
        // Update board pieces
        boardUI.UpdateBoard(board, boardFlipped);
        // Reset board colors
        boardUI.ResetAllSquareColors();
        // Set ui flipped
        uiManager.FlipBoard(boardFlipped);

        // Set match duration
        whitesTimeLeft = startTime;
        blacksTimeLeft = startTime;

        // Update timers
        uiManager.UpdateTimer(true, whitesTimeLeft);
        uiManager.UpdateTimer(false, blacksTimeLeft);
    }

    public void StartButtonPressed(){
        Debug.Log("Mode: " + gameMode);
        switch (gameMode)
        {
            case GameMode.Local:
                canStartGame = true;
                isWhitesTurn = true;
                break;
                
            case GameMode.Online:
                Debug.Log("Online game started");
                StartOnlineGame();
                break;
                
            case GameMode.Stockfish:
                canStartGame = true;
                isWhitesTurn = true;
                
                // Make starting move if AI is white
                if (isWhitesTurn != startPlayerIsWhite){
                    StartGame(); // Start the game before making AI move
                    aiPlayer.MakeStockfishMove(stockfishDepth);
                }
                break;
        }
    }

    public void StartGame(){
        gameRunning = true;
        canStartGame = false;

        whitesTimeLeft = startTime;
        blacksTimeLeft = startTime;

        board.moveIndex = 0;
        board.previousSquares.Clear();
        aiPlayer.SetRandomOpeningSequence();
        
        // Reset timer timeout flag
        timerTimeoutProcessed = false;
    }

    public void MoveMade(){
        // Start game if not started
        if (!gameRunning){
            StartGame();
        }

        // Loop through legal moves to check for checkmate
        if (board.GetAllLegalMoves(movesHandler, !isWhitesTurn).Count == 0){
            // If checkmate, stop game
            gameRunning = false;
            
            // The player who has no legal moves (!isWhitesTurn) is checkmated
            // The winner is the player who made the last move (isWhitesTurn)
            bool winnerIsWhite = isWhitesTurn;
            
            // Reset for next game
            isWhitesTurn = true;
            canStartGame = true;

            // Open win menu UI with message based on who won
            switch (gameMode){
                case GameMode.Local:
                    if (winnerIsWhite){
                        uiManager.OpenWinMenu("White won!", "White won by checkmate");
                    } else{
                        uiManager.OpenWinMenu("Black won!", "Black won by checkmate");
                    }
                    break;
                case GameMode.Online:
                    // In online mode, determine if the winner is the current player
                    bool youWon = (winnerIsWhite && startPlayerIsWhite) || (!winnerIsWhite && !startPlayerIsWhite);
                    if (youWon){
                        uiManager.OpenWinMenu("You won!", "You won by checkmate");
                    } else{
                        uiManager.OpenWinMenu("Opponent won!", "Opponent won by checkmate");
                    }
                    break;
                case GameMode.Stockfish:
                    if (winnerIsWhite == startPlayerIsWhite){
                        uiManager.OpenWinMenu("You won!", "You won by checkmate");
                    } else{
                        uiManager.OpenWinMenu("Stockfish won!", "It won by checkmate");
                    }
                    break;
                default:
                    break;
            }
        }

        // Change turn
        isWhitesTurn = !isWhitesTurn;

        // If AI's turn, make AI move (only for Stockfish mode)
        if (gameMode == GameMode.Stockfish && isWhitesTurn != startPlayerIsWhite){
            aiPlayer.MakeStockfishMove(stockfishDepth);
        }
        // For online mode, wait for opponent to make their move

        // Update board pieces and colors
        boardUI.UpdateBoard(board, boardFlipped);
        boardUI.ResetAllSquareColors();
        
        // Send move to online opponent if playing online
        if (gameMode == GameMode.Online && onlineManager != null && lastMove != null)
        {
            onlineManager.SendMove(lastMove);
            lastMove = null; // Reset after sending
        }
    }

    // Timer sync tracking
    private float lastTimerSyncTime = 0f;
    private const float TIMER_SYNC_INTERVAL = 1f; // Sync every second
    private bool timerTimeoutProcessed = false;

    private void UpdateTimer(){
        // Reduce current player's time
        if (isWhitesTurn){
            whitesTimeLeft -= Time.deltaTime;

            // If time is out, handle timeout
            if (whitesTimeLeft <= 0 && !timerTimeoutProcessed){
                whitesTimeLeft = 0; // Clamp to 0
                timerTimeoutProcessed = true;
                HandleTimerTimeout("white");
            }
        } else{
            blacksTimeLeft -= Time.deltaTime;

            // If time is out, handle timeout
            if (blacksTimeLeft <= 0 && !timerTimeoutProcessed){
                blacksTimeLeft = 0; // Clamp to 0
                timerTimeoutProcessed = true;
                HandleTimerTimeout("black");
            }
        }

        // Update timers
        uiManager.UpdateTimer(true, whitesTimeLeft);
        uiManager.UpdateTimer(false, blacksTimeLeft);
        
        // Send timer sync for online mode
        if (gameMode == GameMode.Online && onlineManager != null && Time.time - lastTimerSyncTime >= TIMER_SYNC_INTERVAL)
        {
            onlineManager.SendTimerSync(whitesTimeLeft, blacksTimeLeft, isWhitesTurn);
            lastTimerSyncTime = Time.time;
        }
    }
    
    private void HandleTimerTimeout(string timeoutSide)
    {
        gameRunning = false;
        
        if (gameMode == GameMode.Online && onlineManager != null)
        {
            // Send timeout message to server
            onlineManager.SendTimerTimeout(timeoutSide);
            // The server will handle the timeout and send back the result
        }
        else
        {
            // Handle locally for non-online modes
            switch (gameMode)
            {
                case GameMode.Local:
                    if (timeoutSide == "white")
                    {
                        uiManager.OpenWinMenu("Black won!", "Black won by time");
                    }
                    else
                    {
                        uiManager.OpenWinMenu("White won!", "White won by time");
                    }
                    break;
                case GameMode.Stockfish:
                    if (timeoutSide == "white")
                    {
                        if (startPlayerIsWhite)
                        {
                            uiManager.OpenWinMenu("Stockfish won!", "It won by time");
                        }
                        else
                        {
                            uiManager.OpenWinMenu("You won!", "You won by time");
                        }
                    }
                    else
                    {
                        if (startPlayerIsWhite)
                        {
                            uiManager.OpenWinMenu("You won!", "You won by time");
                        }
                        else
                        {
                            uiManager.OpenWinMenu("Stockfish won!", "It won by time");
                        }
                    }
                    break;
            }
        }
    }

    public void SetGameMode(string gamemode){
        // Set gamemode enum from string
        switch (gamemode){
            case "Local":
                gameMode = GameMode.Local;
                uiManager.SetNames("Player White", "Player Black");
                UpdateStartButtonState();
                break;
            case "Online":
                gameMode = GameMode.Online;
                // Player names will be set dynamically when game starts based on side assignment
                UpdateStartButtonState();
                break;
            case "Stockfish":
                gameMode = GameMode.Stockfish;
                uiManager.SetNames("Player", "Stockfish");
                UpdateStartButtonState();
                break;
            default:
                return;
        }
    }

    public void CreateRoom() {
        if (onlineManager != null)
        {
            Debug.Log("Creating room");
            onlineManager.CreateRoom(startTime);
        }
    }

    public void JoinRoom() {
        // Find InputField by tag (you can set a tag on your InputField)
        TMP_InputField inputField = GameObject.FindGameObjectWithTag("RoomCodeInputField")?.GetComponent<TMP_InputField>();
        
        // Or find by name if you know the exact name
        // TMP_InputField inputField = GameObject.Find("RoomInputField")?.GetComponent<TMP_InputField>();
        
        if (inputField != null) {
            string roomName = inputField.text;
            Debug.Log($"Joining room: {roomName}");
            if (onlineManager != null)
            {
                onlineManager.JoinRoom(roomName);
            }
        } else {
            Debug.LogError("Room InputField not found!");
        }
    }

    public void StartOnlineGame(string yourSide = null, bool isYourTurn = false) {
        Debug.Log("Starting online game");
        
        // If no side specified, this is a host-initiated start
        if (yourSide == null)
        {
            // Find the ErrorAnnouncer component
            GameObject errorAnnouncer = GameObject.Find("ErrorAnnouncer");
            TMP_Text errorText = errorAnnouncer?.GetComponent<TMP_Text>();
            
            if (onlineManager != null)
            {
                // Check if current player is the host
                if (!onlineManager.IsHost())
                {
                    Debug.LogError("Only the host can start the game");
                    if (errorText != null)
                    {
                        errorText.text = "Only the host can start the game";
                    }
                    return;
                }
                
                // Check if there are enough players (at least 2)
                if (onlineManager.GetPlayerCount() < 2)
                {
                    Debug.LogError("Not enough players to start the game");
                    if (errorText != null)
                    {
                        errorText.text = "Not enough players to start the game";
                    }
                    return;
                }
                
                // Send the start game message to server
                onlineManager.SendStartGame();
                
                // Clear any previous error messages
                if (errorText != null)
                {
                    errorText.text = "";
                }
            }
            return; // Host just sends message, waits for server response
        }
        
        // This is the actual game initialization (called for both players)
        Debug.Log($"Initializing online game - Your side: {yourSide}, Your turn: {isYourTurn}");
        
        // Ensure game mode is set to Online
        gameMode = GameMode.Online;
        
        // Set player names based on the player's side
        if (yourSide == "white")
        {
            uiManager.SetNames("You", "Opponent");
        }
        else
        {
            uiManager.SetNames("Opponent", "You");
        }
        
        // Set the initialization that StartButtonPressed would provide for Online mode
        canStartGame = true;
        
        // Set player side based on server assignment
        startPlayerIsWhite = (yourSide == "white");
        
        // isWhitesTurn should always be true at game start (white goes first)
        isWhitesTurn = true;
        
        // Set board orientation based on player side
        // White player sees board normally, black player sees flipped board
        if (yourSide == "black")
        {
            FlipBoard();
        }
        
        // Transition to board screen for both players
        uiManager.ShowBoardScreen();
        
        // Start the game
        StartGame();
    }
    
    public void SetStartTime(float time){
        startTime = time;
    }

    public void SetStockfishDepth(float rating){
        stockfishDepth = (int)(rating / 200);
    }

    public void SetPlayerSideForStockfish(string playerSide){
        startPlayerIsWhite = (playerSide == "white");
        
        // Update UI names based on player side
        if (gameMode == GameMode.Stockfish){
            if (playerSide == "white"){
                uiManager.SetNames("Player", "Stockfish");
            } else {
                uiManager.SetNames("Stockfish", "Player");
            }
        }
    }

    public void FlipBoard(){
        boardFlipped = !boardFlipped;
        boardUI.UpdateBoard(board, boardFlipped);

        uiManager.FlipBoard(boardFlipped);
    }

    public void Resign(){
        gameRunning = false;

        switch (gameMode){
            case GameMode.Local:
                uiManager.OpenWinMenu(isWhitesTurn ? "Black" : "White" + " won!", isWhitesTurn ? "Black" : "White" + " won by resignation");
                break;
            case GameMode.Online:
                // For online mode, show resignation message to the resigning player
                uiManager.OpenWinMenu("You resigned", "You resigned from the game");
                // Send resign message to server (opponent will get win message)
                if (onlineManager != null)
                {
                    onlineManager.SendResign();
                }
                break;
            case GameMode.Stockfish:
                uiManager.OpenWinMenu("Stockfish won!", "It won by resignation");
                break;
            default:
                break;
        }
    }
    
    // Online event handlers
    private void OnRoomCreated(string roomCode)
    {
        Debug.Log($"Room created successfully: {roomCode}");
        UpdateRoomCodeDisplay();
        UpdateStartButtonState();
    }
    
    private void OnRoomJoined(string roomCode)
    {
        Debug.Log($"Joined room successfully: {roomCode}");
        UpdateRoomCodeDisplay();
        UpdateStartButtonState();
    }
    
    private void OnOnlineGameStarted(string yourSide, bool isYourTurn)
    {
        Debug.Log($"Online game started - Your side: {yourSide}, Your turn: {isYourTurn}");
        
        // Use the centralized StartOnlineGame method with parameters
        StartOnlineGame(yourSide, isYourTurn);
    }
    
    private void OnOnlineMoveReceived(Move move)
    {
        Debug.Log($"Received online move: {move}");
        // Apply the move to the board
        if (board != null)
        {
            board.MovePiece(move);
            MoveMade();
        }
    }
    
    private void OnOnlinePlayerResigned(string playerHash)
    {
        Debug.Log($"Player resigned: {playerHash}");
        gameRunning = false;
        
        // Only show win menu to the opponent (not to the resigning player)
        if (onlineManager != null && playerHash != onlineManager.GetPlayerHash())
        {
            uiManager.OpenWinMenu("You won!", "Opponent resigned");
        }
    }
    
    private void OnOnlineError(string error)
    {
        Debug.LogError($"Online error: {error}");
        // You can show error message in UI
    }
    
    private void OnPlayerCountChanged(int playerCount)
    {
        Debug.Log($"Player count changed to: {playerCount}");
        UpdateRoomCodeDisplay();
        UpdateStartButtonState();
    }
    
    private void OnTimerSync(float whiteTimeLeft, float blackTimeLeft, bool isWhitesTurn)
    {
        Debug.Log($"Timer sync received: White={whiteTimeLeft}, Black={blackTimeLeft}, WhitesTurn={isWhitesTurn}");
        
        // Update local timer state with received values
        whitesTimeLeft = whiteTimeLeft;
        blacksTimeLeft = blackTimeLeft;
        isWhitesTurn = isWhitesTurn;
        
        // Update UI timers
        uiManager.UpdateTimer(true, whitesTimeLeft);
        uiManager.UpdateTimer(false, blacksTimeLeft);
    }
    
    private void OnTimerTimeout(string timeoutSide)
    {
        Debug.Log($"Timer timeout received: {timeoutSide} timed out");
        
        // Stop the game
        gameRunning = false;
        
        // Determine winner based on timeout side and player's actual side
        bool timeoutIsWhite = (timeoutSide == "white");
        bool youTimedOut = (timeoutIsWhite && startPlayerIsWhite) || (!timeoutIsWhite && !startPlayerIsWhite);
        
        if (youTimedOut)
        {
            uiManager.OpenWinMenu("You lost!", "You ran out of time");
        }
        else
        {
            uiManager.OpenWinMenu("You won!", "Opponent ran out of time");
        }
    }

    // Method to update the room code display
    private void UpdateRoomCodeDisplay()
    {
        GameObject roomCodeDisplayer = GameObject.Find("RoomCodeDisplayer");
        if (roomCodeDisplayer != null)
        {
            TMP_Text roomCodeText = roomCodeDisplayer.GetComponent<TMP_Text>();
            if (roomCodeText != null && onlineManager != null)
            {
                string roomCode = onlineManager.GetRoomCode();
                if (!string.IsNullOrEmpty(roomCode))
                {
                    int playerCount = onlineManager.GetPlayerCount();
                    string status = playerCount >= 2 ? "READY" : "WAITING";
                    
                    // Format game time limit as MM:SS
                    int minutes = (int)startTime / 60;
                    int seconds = (int)startTime % 60;
                    string timeString = string.Format("{0:00}:{1:00}", minutes, seconds);
                    
                    roomCodeText.text = $"{roomCode} ({playerCount}/2) - {status} - {timeString}";
                }
            }
        }
        else
        {
            Debug.LogError("RoomCodeDisplayer not found!");
        }
    }

    // Method to update the start button state
    private void UpdateStartButtonState()
    {
        // Find the start button by searching for its text
        GameObject[] allObjects = FindObjectsOfType<GameObject>();
        Button startButton = null;
        
        foreach (GameObject obj in allObjects)
        {
            TMP_Text textComponent = obj.GetComponent<TMP_Text>();
            if (textComponent != null && textComponent.text == "START GAME")
            {
                startButton = obj.GetComponentInParent<Button>();
                break;
            }
        }
        
        if (startButton != null)
        {
            // Only apply validation for Online mode
            if (gameMode == GameMode.Online && onlineManager != null)
            {
                // Enable button only if:
                // 1. Player is the host
                // 2. There are at least 2 players
                bool canStart = onlineManager.IsHost() && onlineManager.GetPlayerCount() >= 2;
                startButton.interactable = canStart;
                
                Debug.Log($"Start button state: {(canStart ? "ENABLED" : "DISABLED")} (Host: {onlineManager.IsHost()}, Players: {onlineManager.GetPlayerCount()})");
            }
            else
            {
                // For Local and Stockfish modes, always enable the button
                startButton.interactable = true;
                Debug.Log("Start button enabled for Local/Stockfish mode");
            }
        }
        else
        {
            Debug.LogError("Start button not found!");
        }
    }

    // Method to set the last move for online tracking
    public void SetLastMove(Move move)
    {
        lastMove = move;
    }


}
