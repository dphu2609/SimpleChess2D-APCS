using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Board : MonoBehaviour
{
    // Script references
    GameManager gm;
    UIManager ui;

    // Board squares
    public int[] squares;

    [HideInInspector]
    public int moveIndex = 0;

    // List of previous board squares
    // Used to go back in time
    public List<int[]> previousSquares = new List<int[]>();

    // Castling rights tracking
    [HideInInspector]
    public bool whiteKingMoved = false;
    [HideInInspector]
    public bool blackKingMoved = false;
    [HideInInspector]
    public bool whiteKingsideRookMoved = false;
    [HideInInspector]
    public bool whiteQueensideRookMoved = false;
    [HideInInspector]
    public bool blackKingsideRookMoved = false;
    [HideInInspector]
    public bool blackQueensideRookMoved = false;

    private void Start(){
        // Assign script reference
        gm = GetComponent<GameManager>();
        ui = GetComponent<UIManager>();
    }

    public void PositionFromFen(string fen){
        // Reset squares variable
        squares = new int[64];

        // Reset castling rights
        whiteKingMoved = false;
        blackKingMoved = false;
        whiteKingsideRookMoved = false;
        whiteQueensideRookMoved = false;
        blackKingsideRookMoved = false;
        blackQueensideRookMoved = false;

        // Split position string into ranks
        string[] fenParts = fen.Split(' ');
        string[] fenRanks = fenParts[0].Split('/');

        // Loop through rank strings
        for (int rank = 7; rank >= 0; rank--)
        {
            string fenRank = fenRanks[7 - rank];
            int fileOffset = 0;

            // Loop through rank string
            for (int file = 0; file < 8; file++)
            {
                // Check file is shorter than rank string length
                if (file >= fenRank.Length) break;
                
                // Get character from string
                char fenChar = fenRank[file];
                // Get piece color based on lowercase or uppercase
                int color = char.IsUpper(fenChar) ? Piece.White : Piece.Black;
                // Make character lower case
                fenChar = char.ToLower(fenChar);

                // Check what piece character repressents and set squares to piece and color
                switch (fenChar){
                    case 'p':
                        squares[rank * 8 + file + fileOffset] = color + Piece.Pawn;
                        break;
                    case 'n':
                        squares[rank * 8 + file + fileOffset] = color + Piece.Knight;
                        break;
                    case 'b':
                        squares[rank * 8 + file + fileOffset] = color + Piece.Bishop;
                        break;
                    case 'r':
                        squares[rank * 8 + file + fileOffset] = color + Piece.Rook;
                        break;
                    case 'q':
                        squares[rank * 8 + file + fileOffset] = color + Piece.Queen;
                        break;
                    case 'k':
                        squares[rank * 8 + file + fileOffset] = color + Piece.King;
                        break;
                    default:
                        // Go forwards amount of steps if character is number
                        int emptySquares = int.Parse(fenChar.ToString());
                        for (int i = 0; i < emptySquares; i++)
                        {
                            squares[rank * 8 + file + fileOffset + i] = Piece.Empty;
                        }
                        fileOffset += emptySquares - 1;
                        break;
                }
            }
        }

        // Whose turn?
        if (fenParts.Length >= 2 && gm != null){
            if (fenParts[1] == "w"){
                gm.isWhitesTurn = true;
            } else {
                gm.isWhitesTurn = false;
            }
        }
    }

    public string BoardToFenString(){
        // Create empty position string
        string fen = "";

        // Loop through each rank
        for (int rank = 7; rank >= 0; rank--)
        {
            int emptySquares = 0;
            // Loop through each file
            for (int file = 0; file < 8; file++)
            {
                int piece = squares[rank * 8 + file];
                if(piece == Piece.Empty){
                    emptySquares++;
                } else {
                    if(emptySquares > 0){
                        fen += emptySquares.ToString();
                        emptySquares = 0;
                    }

                    fen += Piece.PieceToFenChar(piece).ToString();
                }
            }

            // Add number of empty squares as number
            if(emptySquares > 0){
                fen += emptySquares.ToString();
            }

            // Add slash to make new rank in position string
            if(rank > 0){
                fen += "/";
            }
        }

        // Add whose turn to position string
        fen += (gm.isWhitesTurn ? " w" : " b");

        return fen;
    }

    public void MovePiece(Move move){
        moveIndex++;
        // Store current board positions
        previousSquares.Add(squares);

        // Update castling rights before moving
        UpdateCastlingRights(move);

        // Move the piece
        int piece = squares[move.from.file * 8 + move.from.rank];
        squares[move.from.file * 8 + move.from.rank] = Piece.Empty;
        int capture = squares[move.to.file * 8 + move.to.rank];
        squares[move.to.file * 8 + move.to.rank] = piece;

        if (capture != Piece.Empty && capture != Piece.OutOfBounds){
            int captureColor = Piece.PieceColor(capture);
            ui.AddCapturedPiece(capture, captureColor == Piece.White);
        }
        
        // Move secondary piece if it exists
        // Example is the rook when castling
        if (move.from2 != null && move.to2 != null){
            int piece2 = squares[move.from2.file * 8 + move.from2.rank];
            squares[move.from2.file * 8 + move.from2.rank] = Piece.Empty;
            squares[move.to2.file * 8 + move.to2.rank] = piece2;
        }
    }

    public void TempMovePiece(Move move){
        // Move the piece
        int piece = squares[move.from.file * 8 + move.from.rank];
        squares[move.from.file * 8 + move.from.rank] = Piece.Empty;
        squares[move.to.file * 8 + move.to.rank] = piece;

        // Move secondary piece if it exists
        // Example is the rook when castling
        if (move.from2 != null && move.to2 != null){
            int piece2 = squares[move.from2.file * 8 + move.from2.rank];
            squares[move.from2.file * 8 + move.from2.rank] = Piece.Empty;
            squares[move.to2.file * 8 + move.to2.rank] = piece2;
        }
    }

    public int GetPieceFromCoord(Coord coord){
        // Return out of bounds if outside board
        if (coord.file < 0 || coord.file > 7 || coord.rank < 0 || coord.rank > 7){
            return Piece.OutOfBounds;
        }

        // Get square index from file and rank
        int index = coord.file * 8 + coord.rank;
        
        // Return piece on square
        return squares[index];
    }

    public bool CanPromote(Coord coord){
        // Get piece from coord
        int piece = GetPieceFromCoord(coord);
        // Get piece type
        int pieceType = Piece.PieceType(piece);
        // Get piece color
        int pieceColor = Piece.PieceColor(piece);

        // If piece if pawn
        if(pieceType == Piece.Pawn){
            // If white and on last file
            if(pieceColor == Piece.White && coord.file == 7){
                return true;
            } 
            // If black and on first file
            else if(pieceColor == Piece.Black && coord.file == 0){
                return true;
            }
        }

        return false;
    }

    public void Promote(Coord coord, int newPiece){
        // Get piece from coord
        int piece = GetPieceFromCoord(coord);
        // Get color of piece
        int pieceColor = Piece.PieceColor(piece);
        // Replace piece with promoted piece of same color
        squares[coord.file * 8 + coord.rank] = pieceColor + newPiece;
    }

    public List<Move> GetAllLegalMoves(MovesHandler movesHandler, bool isWhite){
        // Get value of color
        int color = (isWhite ? Piece.White : Piece.Black);
        
        // Create empty moves list
        List<Move> moves = new List<Move>();

        // Loop through squares
        for (int i = 0; i < squares.Length; i++)
        {
            // Get coord from index
            Coord coord = new Coord(i % 8, i / 8);
            
            // Get piece on square
            int piece = squares[i];
            
            // Check if piece is the right color
            if (piece != Piece.Empty && Piece.PieceColor(piece) == color){
                // Add legal moves for this piece to the list
                List<Move> pieceMoves = movesHandler.GetLegalMoves(this, coord, isWhite);
                if (pieceMoves != null){
                    moves.AddRange(pieceMoves);
                }
            }
        }

        return moves;
    }

    // Castling rights methods
    public bool CanCastleKingside(bool isWhite)
    {
        bool result;
        if (isWhite)
        {
            result = !whiteKingMoved && !whiteKingsideRookMoved;
            Debug.Log($"White kingside castling: whiteKingMoved={whiteKingMoved}, whiteKingsideRookMoved={whiteKingsideRookMoved}, result={result}");
        }
        else
        {
            result = !blackKingMoved && !blackKingsideRookMoved;
            Debug.Log($"Black kingside castling: blackKingMoved={blackKingMoved}, blackKingsideRookMoved={blackKingsideRookMoved}, result={result}");
        }
        return result;
    }

    public bool CanCastleQueenside(bool isWhite)
    {
        bool result;
        if (isWhite)
        {
            result = !whiteKingMoved && !whiteQueensideRookMoved;
            Debug.Log($"White queenside castling: whiteKingMoved={whiteKingMoved}, whiteQueensideRookMoved={whiteQueensideRookMoved}, result={result}");
        }
        else
        {
            result = !blackKingMoved && !blackQueensideRookMoved;
            Debug.Log($"Black queenside castling: blackKingMoved={blackKingMoved}, blackQueensideRookMoved={blackQueensideRookMoved}, result={result}");
        }
        return result;
    }

    public void UpdateCastlingRights(Move move)
    {
        int piece = GetPieceFromCoord(move.from);
        int pieceType = Piece.PieceType(piece);
        int pieceColor = Piece.PieceColor(piece);

        // Update castling rights based on piece moved
        if (pieceType == Piece.King)
        {
            if (pieceColor == Piece.White)
            {
                whiteKingMoved = true;
            }
            else
            {
                blackKingMoved = true;
            }
        }
        else if (pieceType == Piece.Rook)
        {
            if (pieceColor == Piece.White)
            {
                // Check if it's the kingside or queenside rook
                if (move.from.rank == 0 && move.from.file == 0)
                {
                    whiteQueensideRookMoved = true;
                }
                else if (move.from.rank == 7 && move.from.file == 0)
                {
                    whiteKingsideRookMoved = true;
                }
            }
            else
            {
                // Check if it's the kingside or queenside rook
                if (move.from.rank == 0 && move.from.file == 7)
                {
                    blackQueensideRookMoved = true;
                }
                else if (move.from.rank == 7 && move.from.file == 7)
                {
                    blackKingsideRookMoved = true;
                }
            }
        }
    }
}