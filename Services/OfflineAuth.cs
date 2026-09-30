using System;
using System.Security.Cryptography;
using System.Text;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class OfflineAuth
    {
        public static Account Create(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                username = "Player";

            username = username.Trim();

            string raw = "OfflinePlayer:" + username;
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(raw));

            hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);

            string uuid = new Guid(hash).ToString("N");

            return new Account
            {
                Username = username,
                Uuid = uuid,
                AccessToken = "0",
                UserType = "legacy",
                AccountType = "offline",
                LastLoginTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }
    }
}