using System.Collections.Generic;
using System.Threading.Tasks;
using NodePulse.Models;

namespace NodePulse.Services
{
    public interface IDialogService
    {
        Task ShowMessageAsync(string title, string message);
        Task<bool> ShowConfirmAsync(string title, string message);
        Task<string?> PickFolderAsync(string title);
        Task<string?> PickFileAsync(string title, string[] patterns);
        Task<string?> ShowTextInputAsync(string title, string label, string defaultValue = "");
        Task<YggdrasilLoginResult?> ShowYggdrasilLoginAsync();

        Task<ProfileInfo?> ShowProfileSelectAsync(string title, List<ProfileInfo> profiles);

        Task<string?> ShowSaveFileAsync(
            string title,
            string suggestedFileName,
            string defaultExtension = "jar",
            string? startDirectory = null);

        /// <summary>
        /// 弹出皮肤管理窗口。
        /// 返回：
        ///   null        → 用户取消
        ///   ""          → 默认 Steve
        ///   "__alex__"  → 默认 Alex
        ///   绝对路径    → 本地皮肤
        /// </summary>
        Task<string?> ShowSkinManagerAsync(Account account);
    }
}