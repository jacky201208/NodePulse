using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using NodePulse.Models;

namespace NodePulse.Services
{
    public static class AccountService
    {
        private static string GetPath()
        {
            try
            {
                return Path.Combine(AppContext.BaseDirectory, "accounts.json");
            }
            catch
            {
                return "accounts.json";
            }
        }

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true
        };

        public static AccountStore Load()
        {
            try
            {
                var path = GetPath();
                if (!File.Exists(path)) return new AccountStore();

                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AccountStore>(json, JsonOpts)
                       ?? new AccountStore();
            }
            catch
            {
                return new AccountStore();
            }
        }

        public static void Save(AccountStore store)
        {
            try
            {
                var path = GetPath();
                var json = JsonSerializer.Serialize(store, JsonOpts);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        public static Account? GetCurrent()
        {
            var store = Load();
            if (store.Accounts.Count == 0) return null;
            return store.Accounts.FirstOrDefault(a => a.Id == store.CurrentAccountId)
                   ?? store.Accounts[0];
        }
    }
}