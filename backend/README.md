# Chess Online Backend Server

A FastAPI backend server for the Unity Chess game's online mode.

## Features

- Room creation with 6-character codes (0-9, A-Z)
- Player joining with unique hash keys
- Real-time WebSocket communication
- Move broadcasting between players
- Game start/stop management
- Resignation handling

## Setup

1. Install Python 3.8+ if not already installed
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```

## Running the Server

```bash
python main.py
```

The server will start on `http://localhost:8000`

## API Endpoints

### POST /create-room
Creates a new room and returns room code and host hash.

**Request Body:**
```json
{
  "time": 300.0
}
```

**Response:**
```json
{
  "room_code": "ABC123",
  "host_hash": "a1b2c3d4e5f6..."
}
```

### GET /join-room/{room_code}
Joins an existing room and returns player hash and game time.

**Response:**
```json
{
  "player_hash": "f1e2d3c4b5a6...",
  "time": 300.0
}
```

### WebSocket /ws/{room_code}/{player_hash}
Real-time communication channel for game events.

**Message Types:**

1. **Start Game** (from host):
   ```json
   {
     "type": "start_game"
   }
   ```

2. **Move** (from any player):
   ```json
   {
     "type": "move",
     "move": {
       "from_rank": 0,
       "from_file": 0,
       "to_rank": 2,
       "to_file": 0,
       "from2_rank": null,
       "from2_file": null,
       "to2_rank": null,
       "to2_file": null
     }
   }
   ```

3. **Resign** (from any player):
   ```json
   {
     "type": "resign"
   }
   ```

## WebSocket Events

The server broadcasts these events to all players in a room:

1. **game_started** - When host starts the game
2. **move** - When a player makes a move
3. **resign** - When a player resigns

## Health Check

### GET /health
Returns server status and active room count.

## Development

The server uses in-memory storage for rooms and connections. For production, consider using a database for persistence. 