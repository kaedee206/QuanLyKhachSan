using System;
using System.Security.Cryptography;
using System.Text;

namespace QuanLyKhachSan.Helpers
{
    public static class PasswordGenerator
    {
        private const string UpperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        private const string LowerChars = "abcdefghijklmnopqrstuvwxyz";
        private const string DigitChars = "0123456789";
        private const string SpecialChars = "!@#$%^&*()_+-=[]{}|;:,.<>?";
        private const string AllChars = UpperChars + LowerChars + DigitChars + SpecialChars;

        /// <summary>
        /// Generates a cryptographically secure random password of specified length (default 32).
        /// Contains uppercase, lowercase, numbers, and special characters.
        /// </summary>
        public static string Generate(int length = 32)
        {
            if (length < 8)
                throw new ArgumentException("Password length must be at least 8 characters.", nameof(length));

            var chars = new char[length];

            // Guarantee at least two characters from each required category
            chars[0] = UpperChars[RandomNumberGenerator.GetInt32(UpperChars.Length)];
            chars[1] = UpperChars[RandomNumberGenerator.GetInt32(UpperChars.Length)];
            chars[2] = LowerChars[RandomNumberGenerator.GetInt32(LowerChars.Length)];
            chars[3] = LowerChars[RandomNumberGenerator.GetInt32(LowerChars.Length)];
            chars[4] = DigitChars[RandomNumberGenerator.GetInt32(DigitChars.Length)];
            chars[5] = DigitChars[RandomNumberGenerator.GetInt32(DigitChars.Length)];
            chars[6] = SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)];
            chars[7] = SpecialChars[RandomNumberGenerator.GetInt32(SpecialChars.Length)];

            // Fill the remaining length with random choices from the entire character pool
            for (int i = 8; i < length; i++)
            {
                chars[i] = AllChars[RandomNumberGenerator.GetInt32(AllChars.Length)];
            }

            // Cryptographically secure Fisher-Yates shuffle
            for (int i = length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars);
        }
    }
}
