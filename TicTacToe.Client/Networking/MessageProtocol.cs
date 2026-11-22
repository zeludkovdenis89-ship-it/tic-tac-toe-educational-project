using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TicTacToe.Client.Networking;

public enum MsgType
{
    CreateSession, ListSessions, JoinSession,
    MiniGameChoice, Move, ChatMessage, ResetSession,
    CreateSessionOk, SessionList, JoinSessionOk,
    MiniGameStart, MiniGameResult,
    MoveOk, MoveRejected, StateUpdate, GameOver, Error,
    MiniGameChoiceUpdate, LeaveSession, LeaveSessionOk, OpponentLeft
}

public sealed class SessionInfo
{
    public string SessionId { get; set; } = "";
    public int PlayersCount { get; set; }
    public int MaxPlayers { get; set; } = 2;
    public string Status { get; set; } = "Waiting";

    public override string ToString()
        => $"{(SessionId.Length >= 8 ? SessionId[..8] : SessionId)}…  {PlayersCount}/{MaxPlayers}  [{Status}]";
}

public sealed class ChatMessageDto
{
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.Now;
}

public sealed class NetMessage
{
    public MsgType Type { get; set; }
    public string? SessionId { get; set; }
    public string? PlayerId { get; set; }
    public string? Symbol { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public string? Choice { get; set; }
    public string? Text { get; set; }
    public string? Reason { get; set; }
    public string? Result { get; set; }
    public string? WinnerId { get; set; }
    public string[]? Board { get; set; }
    public string? CurrentTurn { get; set; }
    public string? Status { get; set; }
    public List<SessionInfo>? Sessions { get; set; }
    public List<ChatMessageDto>? Chat { get; set; }

    private static readonly JsonSerializerOptions Opts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public string Serialize() => JsonSerializer.Serialize(this, Opts);
    public static NetMessage Deserialize(string json)
        => JsonSerializer.Deserialize<NetMessage>(json, Opts) ?? new NetMessage();
}