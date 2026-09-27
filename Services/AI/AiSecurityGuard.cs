using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace QuanLyKhachSan.Services.AI
{
    public class AiSecurityGuard
    {
        private readonly ILogger<AiSecurityGuard> _logger;

        public AiSecurityGuard(ILogger<AiSecurityGuard> logger)
        {
            _logger = logger;
        }

        public bool IsInputSafe(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return true;

            if (Regex.IsMatch(input, @"(ignore previous|DAN mode|jailbreak|system prompt)", RegexOptions.IgnoreCase))
            {
                _logger.LogWarning("Prompt injection pattern detected in input.");
                return false;
            }
            return true;
        }

        public string FilterOutput(string output)
        {
            if (string.IsNullOrWhiteSpace(output)) return output;

            // Simple redaction for Credit Card numbers or similar patterns
            output = Regex.Replace(output, @"\b\d{16}\b", "[REDACTED-CC]");
            output = Regex.Replace(output, @"(sk-[a-zA-Z0-9]{20,})", "[REDACTED-API-KEY]");
            return output;
        }
    }
}
