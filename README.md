# Unity Chess with Online Multiplayer

A Unity chess game with local, AI (Stockfish), and online multiplayer modes.

## Features

- **Local Mode**: Play against another player on the same device
- **AI Mode**: Play against the Stockfish chess engine
- **Online Mode**: Play against another player over the internet with real-time synchronization

## How to Run

### Step 1: Clone Repository
```bash
git clone <repository-url>
cd Unity-Chess
```

### Step 2: Open in Unity
1. Open Unity Hub
2. Click "Open" and select the Unity-Chess folder
3. **Important**: Use the exact Unity version specified by the project (Unity 2021.3 LTS or later)
4. Wait for Unity to import all assets

### Step 3: Install Stockfish
1. Download Stockfish from the official website: https://stockfishchess.org/download/
2. Extract the downloaded file
3. Build the stockfish.exe file (follow the build instructions in the Stockfish documentation)
4. Copy the stockfish.exe file to the `Assets/stockfish/` directory in your Unity project

### Step 4: Run Backend Server
1. Open a terminal/command prompt
2. Navigate to the backend directory:
   ```bash
   cd backend
   ```
3. Install Python dependencies:
   ```bash
   pip install -r requirements.txt
   ```
4. Start the server:
   ```bash
   python main.py
   ```
5. The server will start on `http://localhost:8000`
6. **Keep the server running** while playing online games

**Note**: The backend server is not deployed and runs locally for testing purposes. This setup serves as a reference for future deployment to a production environment.

### Step 5: Build the Game
1. In Unity, go to File → Build Settings
2. Select your target platform (Windows, macOS, Linux)
3. Click "Build" and choose a location for your game files
4. The built game will be ready to run

## Game Modes

### Local Mode
- Play chess against another player on the same computer
- No internet connection required

### AI Mode (Stockfish)
- Play against the Stockfish chess engine
- Adjustable difficulty levels
- Requires Stockfish to be properly installed (Step 3)

### Online Mode
- Create or join rooms using 6-character codes
- Real-time multiplayer over the internet
- Requires the backend server to be running (Step 4)

## Assets and Credits

### Chess Piece Sprites
The chess piece sprites used in this game are based on the [Chess Pieces Sprite.svg](https://commons.wikimedia.org/wiki/File:Chess_Pieces_Sprite.svg) by jurgenwesterhof (adapted from work of Cburnett), licensed under the [Creative Commons Attribution-Share Alike 3.0 Unported license](https://creativecommons.org/licenses/by-sa/3.0/).
