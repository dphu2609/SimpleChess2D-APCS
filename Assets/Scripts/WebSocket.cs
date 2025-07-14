using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

public enum WebSocketState
{
    Connecting,
    Open,
    Closing,
    Closed
}

public enum WebSocketCloseCode
{
    Normal = 1000,
    GoingAway = 1001,
    ProtocolError = 1002,
    UnsupportedData = 1003,
    NoStatusReceived = 1005,
    AbnormalClosure = 1006,
    InvalidFramePayloadData = 1007,
    PolicyViolation = 1008,
    MessageTooBig = 1009,
    InternalServerError = 1011
}

public class WebSocket
{
    private ClientWebSocket clientWebSocket;
    private CancellationTokenSource cancellationTokenSource;
    private readonly Queue<byte[]> messageQueue = new Queue<byte[]>();
    private readonly object queueLock = new object();
    private string url;
    
    public WebSocketState State { get; private set; } = WebSocketState.Closed;
    
    public event Action<byte[]> OnMessage;
    public event Action<string> OnError;
    public event Action<WebSocketCloseCode> OnClose;
    
    public WebSocket(string url)
    {
        this.url = url;
        clientWebSocket = new ClientWebSocket();
        cancellationTokenSource = new CancellationTokenSource();
    }
    
    public async void Connect()
    {
        try
        {
            State = WebSocketState.Connecting;
            await clientWebSocket.ConnectAsync(new Uri(url), cancellationTokenSource.Token);
            State = WebSocketState.Open;
            
            // Start receiving messages
            _ = Task.Run(async () => await ReceiveMessages());
        }
        catch (Exception e)
        {
            State = WebSocketState.Closed;
            OnError?.Invoke($"Connection failed: {e.Message}");
        }
    }
    
    private async Task ReceiveMessages()
    {
        var buffer = new byte[4096];
        
        try
        {
            while (clientWebSocket.State == System.Net.WebSockets.WebSocketState.Open && !cancellationTokenSource.Token.IsCancellationRequested)
            {
                var result = await clientWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationTokenSource.Token);
                
                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var message = new byte[result.Count];
                    Array.Copy(buffer, message, result.Count);
                    
                    lock (queueLock)
                    {
                        messageQueue.Enqueue(message);
                    }
                }
                else if (result.MessageType == WebSocketMessageType.Close)
                {
                    State = WebSocketState.Closing;
                    await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationTokenSource.Token);
                    State = WebSocketState.Closed;
                    OnClose?.Invoke(WebSocketCloseCode.Normal);
                    break;
                }
            }
        }
        catch (Exception e)
        {
            State = WebSocketState.Closed;
            OnError?.Invoke($"Receive error: {e.Message}");
            OnClose?.Invoke(WebSocketCloseCode.AbnormalClosure);
        }
    }
    
    public async void Send(string message)
    {
        if (State != WebSocketState.Open) return;
        
        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            var buffer = new ArraySegment<byte>(bytes);
            await clientWebSocket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationTokenSource.Token);
        }
        catch (Exception e)
        {
            OnError?.Invoke($"Send error: {e.Message}");
        }
    }
    
    public void DispatchMessageQueue()
    {
        lock (queueLock)
        {
            while (messageQueue.Count > 0)
            {
                var message = messageQueue.Dequeue();
                OnMessage?.Invoke(message);
            }
        }
    }
    
    public async void Close()
    {
        if (State == WebSocketState.Open)
        {
            State = WebSocketState.Closing;
            cancellationTokenSource.Cancel();
            try
            {
                await clientWebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error closing WebSocket: {e.Message}");
            }
        }
    }
    
    public void Dispose()
    {
        Close();
        clientWebSocket?.Dispose();
        cancellationTokenSource?.Dispose();
    }
} 