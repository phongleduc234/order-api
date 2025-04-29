using System;

namespace OrderApi.Models
{
    public enum DeadLetterStatus
    {
        Pending,
        Processed,
        Failed
    }

    public class DeadLetterMessage
    {
        public int Id { get; set; }
        public string MessageContent { get; set; }
        public string Error { get; set; }
        public string Source { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastRetryAt { get; set; }
        public int RetryCount { get; set; }
        public DeadLetterStatus Status { get; set; }
    }
} 