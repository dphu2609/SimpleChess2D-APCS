from fastapi import FastAPI, WebSocket, WebSocketDisconnect, HTTPException
from fastapi.middleware.cors import CORSMiddleware
from pydantic import BaseModel
import secrets
import string
import json
from typing import Dict, List, Optional
import asyncio
from datetime import datetime

app = FastAPI(title="Chess Online Server", version="1.0.0")

# Add CORS middleware
app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],  # In production, specify your Unity game's origin
    allow_credentials=True,
    allow_methods=["*"],
    allow_headers=["*"],
)

# Data models
class CreateRoomRequest(BaseModel):
    time: float

class CreateRoomResponse(BaseModel):
    room_code: str
    host_hash: str

class JoinRoomResponse(BaseModel):
    player_hash: str
    time: float

class MoveMessage(BaseModel):
    player_hash: str
    from_rank: int
    from_file: int
    to_rank: int
    to_file: int
    from2_rank: int = -1  # Use -1 to indicate null
    from2_file: int = -1  # Use -1 to indicate null
    to2_rank: int = -1    # Use -1 to indicate null
    to2_file: int = -1    # Use -1 to indicate null

class StartGameMessage(BaseModel):
    host_hash: str

class TimerSyncMessage(BaseModel):
    player_hash: str
    white_time_left: float
    black_time_left: float
    is_whites_turn: bool

class TimerTimeoutMessage(BaseModel):
    player_hash: str
    timeout_side: str  # "white" or "black"

# Room management
class Room:
    def __init__(self, room_code: str, host_hash: str, time: float):
        self.room_code = room_code
        self.host_hash = host_hash
        self.time = time
        self.players: List[str] = [host_hash]
        self.websockets: List[WebSocket] = []
        self.player_websockets: Dict[str, WebSocket] = {}  # Map player hash to websocket
        self.created_at = datetime.now()
        self.game_started = False
        # Timer state
        self.white_time_left = time
        self.black_time_left = time
        self.is_whites_turn = True
        self.last_timer_update = datetime.now()
        self.white_player_hash = None
        self.black_player_hash = None

# Global storage
rooms: Dict[str, Room] = {}
active_connections: Dict[str, WebSocket] = {}

def generate_room_code() -> str:
    """Generate a 6-character room code using 0-9 and A-Z"""
    characters = string.ascii_uppercase + string.digits
    return ''.join(secrets.choice(characters) for _ in range(6))

def generate_player_hash() -> str:
    """Generate a unique player hash"""
    return secrets.token_hex(16)

@app.post("/create-room", response_model=CreateRoomResponse)
async def create_room(request: CreateRoomRequest):
    """Create a new room and return room code and host hash"""
    room_code = generate_room_code()
    host_hash = generate_player_hash()
    
    # Create new room
    room = Room(room_code, host_hash, request.time)
    rooms[room_code] = room
    
    print(f"Room created: {room_code} by {host_hash} with time {request.time}")
    
    return CreateRoomResponse(room_code=room_code, host_hash=host_hash)

@app.get("/join-room/{room_code}", response_model=JoinRoomResponse)
async def join_room(room_code: str):
    """Join an existing room and return player hash and game time"""
    if room_code not in rooms:
        raise HTTPException(status_code=404, detail="Room not found")
    
    room = rooms[room_code]
    
    # Generate player hash for the joining player
    player_hash = generate_player_hash()
    room.players.append(player_hash)
    
    print(f"Player {player_hash} joined room {room_code}")
    
    return JoinRoomResponse(player_hash=player_hash, time=room.time)

@app.websocket("/ws/{room_code}/{player_hash}")
async def websocket_endpoint(websocket: WebSocket, room_code: str, player_hash: str):
    """WebSocket endpoint for real-time game communication"""
    await websocket.accept()
    
    if room_code not in rooms:
        await websocket.close(code=4004, reason="Room not found")
        return
    
    room = rooms[room_code]
    
    # Store the websocket connection
    room.websockets.append(websocket)
    room.player_websockets[player_hash] = websocket
    active_connections[player_hash] = websocket
    
    print(f"WebSocket connected: {player_hash} in room {room_code}")
    
    # Notify all players in the room about the updated player count
    player_count_message = {
        "type": "player_count_updated",
        "player_count": len(room.players)
    }
    for ws in room.websockets:
        try:
            await ws.send_text(json.dumps(player_count_message))
        except:
            pass  # Ignore if WebSocket is closed
    
    try:
        while True:
            # Receive message from client
            data = await websocket.receive_text()
            message = json.loads(data)
            
            message_type = message.get("type")
            
            if message_type == "start_game":
                # Only host can start the game
                if player_hash == room.host_hash:
                    # Check if there are enough players (need at least 2 for chess)
                    if len(room.players) < 2:
                        error_message = {
                            "type": "error",
                            "message": "There are not enough players"
                        }
                        await websocket.send_text(json.dumps(error_message))
                        print(f"Not enough players in room {room_code}")
                    else:
                        room.game_started = True
                        
                        # Assign sides: host plays white, second player plays black
                        host_hash = room.host_hash
                        second_player_hash = None
                        
                        # Find the second player (not the host)
                        for player in room.players:
                            if player != host_hash:
                                second_player_hash = player
                                break
                        
                        # Set up timer state
                        room.white_player_hash = host_hash
                        room.black_player_hash = second_player_hash
                        room.white_time_left = room.time
                        room.black_time_left = room.time
                        room.is_whites_turn = True
                        room.last_timer_update = datetime.now()
                        
                        # Send individual messages to each player with their side
                        if host_hash in room.player_websockets:
                            host_message = {
                                "type": "game_started",
                                "your_side": "white",
                                "is_your_turn": True
                            }
                            await room.player_websockets[host_hash].send_text(json.dumps(host_message))
                        
                        if second_player_hash and second_player_hash in room.player_websockets:
                            second_message = {
                                "type": "game_started",
                                "your_side": "black",
                                "is_your_turn": False
                            }
                            await room.player_websockets[second_player_hash].send_text(json.dumps(second_message))
                        
                        print(f"Game started in room {room_code}: {host_hash} (white) vs {second_player_hash} (black)")
                else:
                    error_message = {
                        "type": "error",
                        "message": "Only the host can start the game"
                    }
                    await websocket.send_text(json.dumps(error_message))
                    print(f"Non-host {player_hash} tried to start game in room {room_code}")
                
            elif message_type == "move":
                # Validate move message
                move_data = message.get("move", {})
                move_message = MoveMessage(
                    player_hash=player_hash,
                    from_rank=move_data.get("from_rank"),
                    from_file=move_data.get("from_file"),
                    to_rank=move_data.get("to_rank"),
                    to_file=move_data.get("to_file"),
                    from2_rank=move_data.get("from2_rank"),
                    from2_file=move_data.get("from2_file"),
                    to2_rank=move_data.get("to2_rank"),
                    to2_file=move_data.get("to2_file")
                )
                
                # Broadcast move to other players in the room
                move_broadcast = {
                    "type": "move",
                    "player_hash": player_hash,
                    "move": {
                        "from_rank": move_message.from_rank,
                        "from_file": move_message.from_file,
                        "to_rank": move_message.to_rank,
                        "to_file": move_message.to_file,
                        "from2_rank": move_message.from2_rank,
                        "from2_file": move_message.from2_file,
                        "to2_rank": move_message.to2_rank,
                        "to2_file": move_message.to2_file
                    }
                }
                
                for ws in room.websockets:
                    if ws != websocket:  # Don't send back to sender
                        await ws.send_text(json.dumps(move_broadcast))
                
                print(f"Move broadcasted in room {room_code} by {player_hash}")
                
            elif message_type == "resign":
                # Broadcast resignation
                resign_message = {
                    "type": "resign",
                    "player_hash": player_hash
                }
                for ws in room.websockets:
                    await ws.send_text(json.dumps(resign_message))
                print(f"Player {player_hash} resigned in room {room_code}")
                
            elif message_type == "timer_sync":
                # Handle timer synchronization
                timer_data = message.get("timer_data", {})
                
                # Update room timer state
                room.white_time_left = timer_data.get("white_time_left", room.white_time_left)
                room.black_time_left = timer_data.get("black_time_left", room.black_time_left)
                room.is_whites_turn = timer_data.get("is_whites_turn", room.is_whites_turn)
                room.last_timer_update = datetime.now()
                
                # Check for timer timeout
                if room.white_time_left <= 0:
                    # White timed out
                    timeout_message = {
                        "type": "timer_timeout",
                        "timeout_side": "white",
                        "winner_side": "black"
                    }
                    for ws in room.websockets:
                        await ws.send_text(json.dumps(timeout_message))
                    print(f"White player timed out in room {room_code}")
                    
                elif room.black_time_left <= 0:
                    # Black timed out
                    timeout_message = {
                        "type": "timer_timeout",
                        "timeout_side": "black",
                        "winner_side": "white"
                    }
                    for ws in room.websockets:
                        await ws.send_text(json.dumps(timeout_message))
                    print(f"Black player timed out in room {room_code}")
                    
                else:
                    # Broadcast timer sync to other players
                    sync_message = {
                        "type": "timer_sync",
                        "timer_data": {
                            "white_time_left": room.white_time_left,
                            "black_time_left": room.black_time_left,
                            "is_whites_turn": room.is_whites_turn
                        }
                    }
                    for ws in room.websockets:
                        if ws != websocket:  # Don't send back to sender
                            await ws.send_text(json.dumps(sync_message))
                    print(f"Timer sync broadcasted in room {room_code}")
                
            elif message_type == "timer_timeout":
                # Handle timer timeout reported by client
                timeout_side = message.get("timeout_side")
                winner_side = "black" if timeout_side == "white" else "white"
                
                timeout_message = {
                    "type": "timer_timeout",
                    "timeout_side": timeout_side,
                    "winner_side": winner_side
                }
                for ws in room.websockets:
                    await ws.send_text(json.dumps(timeout_message))
                print(f"{timeout_side} player timed out in room {room_code} (reported by client)")
                
    except WebSocketDisconnect:
        print(f"WebSocket disconnected: {player_hash} from room {room_code}")
    except Exception as e:
        print(f"WebSocket error: {e}")
    finally:
        # Clean up
        if websocket in room.websockets:
            room.websockets.remove(websocket)
        if player_hash in room.player_websockets:
            del room.player_websockets[player_hash]
        if player_hash in active_connections:
            del active_connections[player_hash]
        
        # Remove player from room's player list
        if player_hash in room.players:
            room.players.remove(player_hash)
        
        # Notify remaining players about updated player count
        if len(room.websockets) > 0:
            player_count_message = {
                "type": "player_count_updated",
                "player_count": len(room.players)
            }
            for ws in room.websockets:
                try:
                    await ws.send_text(json.dumps(player_count_message))
                except:
                    pass  # Ignore if WebSocket is closed
        
        # If room is empty, remove it
        if len(room.websockets) == 0:
            if room_code in rooms:
                del rooms[room_code]
                print(f"Room {room_code} removed (no players left)")

@app.get("/health")
async def health_check():
    """Health check endpoint"""
    return {"status": "healthy", "active_rooms": len(rooms)}

if __name__ == "__main__":
    import uvicorn
    uvicorn.run(app, host="localhost", port=8000) 