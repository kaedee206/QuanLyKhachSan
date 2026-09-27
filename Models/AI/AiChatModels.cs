namespace QuanLyKhachSan.Models.AI
{
    using System;
    using System.Collections.Generic;
    
    // Access tier for RAG documents
    public enum DocumentAccessTier
    {
        PUBLIC_TIER = 0,
        INTERNAL_STAFF = 1,
        MANAGEMENT_CONFIDENTIAL = 2,
        SYSTEM_ADMIN = 3
    }

    // Chat request from frontend
    public class AiChatRequest
    {
        public string Message { get; set; } = string.Empty;
        public string? SessionId { get; set; }
        public string? Role { get; set; }
        public List<AiChatMessage>? History { get; set; }
    }

    // Chat response to frontend
    public class AiChatResponse
    {
        public string Reply { get; set; } = string.Empty;
        public string SessionId { get; set; } = string.Empty;
        public List<AiQuickAction>? QuickActions { get; set; }
        public AiFunctionCallResult? FunctionResult { get; set; }
    }

    // Quick action buttons
    public class AiQuickAction
    {
        public string Label { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
    }

    // Function call result
    public class AiFunctionCallResult
    {
        public string FunctionName { get; set; } = string.Empty;
        public bool Success { get; set; }
        public object? Data { get; set; }
        public string? ErrorMessage { get; set; }
    }

    // Chat session storage
    public class AiChatSession
    {
        public string SessionId { get; set; } = Guid.NewGuid().ToString();
        public List<AiChatMessage> Messages { get; set; } = new();
        public string UserTier { get; set; } = "GUEST";
        public int? UserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
        public AiBookingDraft BookingDraft { get; set; } = new();
        public Dictionary<string, string> SynthesizedFacts { get; set; } = new();
    }

    public class AiBookingDraft
    {
        public int? RoomTypeId { get; set; }
        public string? RoomTypeName { get; set; }
        public string? GuestName { get; set; }
        public string? Phone { get; set; }
        public string? Email { get; set; }
        public string? PaymentMethod { get; set; }
        public DateOnly? CheckInDate { get; set; }
        public DateOnly? CheckOutDate { get; set; }
        public int NumGuests { get; set; } = 1;
        public string? Notes { get; set; }
        public bool IsAwaitingConfirmation { get; set; }
        public string? LastCreatedBookingCode { get; set; }

        public bool HasMinimumInfo => RoomTypeId.HasValue &&
                                      !string.IsNullOrWhiteSpace(GuestName) &&
                                      !string.IsNullOrWhiteSpace(Phone) &&
                                      !string.IsNullOrWhiteSpace(Email) &&
                                      !string.IsNullOrWhiteSpace(PaymentMethod) &&
                                      CheckInDate.HasValue &&
                                      CheckOutDate.HasValue;
    }

    public class AiChatMessage
    {
        public string Role { get; set; } = string.Empty; // "user" | "assistant" | "system"
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    // Tool/Function definitions for AI
    public class AiToolDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public object Parameters { get; set; } = new { };
        public string MinimumRole { get; set; } = "GUEST";
    }

    // RAG document metadata 
    public class RagDocumentMetadata
    {
        public string DocId { get; set; } = string.Empty;
        public string SourceFile { get; set; } = string.Empty;
        public DocumentAccessTier RequiredTier { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    // AI Configuration loaded from env/appsettings
    public class AiProviderConfig
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ModelName { get; set; } = string.Empty;
        public string EmbeddingModel { get; set; } = string.Empty;
        public double Temperature { get; set; } = 0.2;
        public int MaxTokens { get; set; } = 1500;
        public int RequestTimeout { get; set; } = 30000;
    }
}
