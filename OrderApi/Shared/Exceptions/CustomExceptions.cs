using System.Net;

namespace OrderApi.Shared.Exceptions
{
    public class ValidationException : Exception
    {
        public Dictionary<string, string[]> Errors { get; }

        public ValidationException(Dictionary<string, string[]> errors) : base("Validation failed")
        {
            Errors = errors;
        }
    }

    public class NotFoundException : Exception
    {
        public NotFoundException(string message) : base(message)
        {
        }
    }

    public class UnauthorizedException : Exception
    {
        public UnauthorizedException(string message) : base(message)
        {
        }
    }

    public class BusinessRuleException : Exception
    {
        public BusinessRuleException(string message) : base(message)
        {
        }
    }

    public class DatabaseException : Exception
    {
        public DatabaseException(string message, Exception innerException) 
            : base(message, innerException)
        {
        }
    }

    public class MessageQueueException : Exception
    {
        public MessageQueueException(string message, Exception innerException) 
            : base(message, innerException)
        {
        }
    }
} 