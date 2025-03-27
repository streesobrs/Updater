using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Updater
{
    class Program
    {
        private static StreamWriter logWriter;

        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += UnhandledExceptionHandler;

            try
            {
                InitializeLogging();
                LogStartupInfo();

                if (!ValidateArguments(args, out string filePath, out string mainAppPath))
                {
                    WaitForExit();
                    return;
                }

                if (!CheckPrerequisites(filePath, mainAppPath))
                {
                    WaitForExit();
                    return;
                }

                await PerformUpdate(filePath, mainAppPath);
                RestartMainApplication(mainAppPath);
                await GracefulExit();
            }
            catch (Exception ex)
            {
                Log($"更新过程中发生严重错误: {ex}");
                WaitForExit();
            }
            finally
            {
                logWriter?.Close();
            }
        }

        #region Initialization
        private static void InitializeLogging()
        {
            string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.log");
            logWriter = new StreamWriter(logFilePath, true, Encoding.UTF8) { AutoFlush = true };
        }

        private static void LogStartupInfo()
        {
            Log("========================================");
            Log($"日志开始时间: {DateTime.Now}");
            Log($"当前版本: {Assembly.GetExecutingAssembly().GetName().Version}");
            Log("========================================");
        }
        #endregion

        #region Argument Validation
        private static bool ValidateArguments(string[] args, out string filePath, out string mainAppPath)
        {
            filePath = null;
            mainAppPath = null;

            if (args.Length < 2)
            {
                Log("参数错误: 需要两个参数 <filePath> <mainAppPath>");
                return false;
            }

            filePath = args[0];
            mainAppPath = Path.GetFullPath(args[1].Trim('"'));

            if (!File.Exists(filePath))
            {
                Log($"无效的文件路径: {filePath}");
                return false;
            }

            if (!Directory.Exists(mainAppPath))
            {
                Log($"无效的主程序路径: {mainAppPath}");
                return false;
            }

            return true;
        }
        #endregion

        #region Update Operations
        private static async Task PerformUpdate(string zipPath, string targetDir)
        {
            Log("开始执行更新操作...");
            await Task.Delay(5000); // 初始等待

            using (var archive = ZipFile.OpenRead(zipPath))
            {
                if (archive.Entries.Count == 0)
                {
                    Log("错误: ZIP文件为空");
                    return;
                }

                var totalFiles = archive.Entries.Count;
                var processedFiles = 0;

                foreach (var entry in archive.Entries)
                {
                    var destinationPath = GetSafeDestinationPath(entry, targetDir);
                    if (destinationPath == null) continue;

                    if (IsDirectoryEntry(entry)) continue;

                    await ProcessFileEntry(entry, destinationPath);
                    UpdateProgress(++processedFiles, totalFiles);
                }
            }
        }

        private static string GetSafeDestinationPath(ZipArchiveEntry entry, string targetDir)
        {
            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
                if (!fullPath.StartsWith(Path.GetFullPath(targetDir)))
                {
                    Log($"安全警告: 尝试写入非目标目录 {fullPath}");
                    return null;
                }
                return fullPath;
            }
            catch (Exception ex)
            {
                Log($"路径解析失败: {ex.Message}");
                return null;
            }
        }

        private static bool IsDirectoryEntry(ZipArchiveEntry entry)
        {
            return string.IsNullOrEmpty(entry.Name) &&
                   !string.IsNullOrEmpty(entry.FullName) &&
                   entry.FullName.EndsWith("/");
        }

        private static async Task ProcessFileEntry(ZipArchiveEntry entry, string destinationPath)
        {
            try
            {
                Log($"处理文件: {destinationPath}");
                EnsureDirectoryExists(destinationPath);
                DeleteExistingFile(destinationPath);
                await ExtractWithRetry(entry, destinationPath);
            }
            catch (Exception ex)
            {
                Log($"文件处理失败: {ex.Message}");
            }
        }

        private static void EnsureDirectoryExists(string filePath)
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!Directory.Exists(dir))
            {
                Log($"创建目录: {dir}");
                Directory.CreateDirectory(dir);
            }
        }

        private static void DeleteExistingFile(string path)
        {
            if (!File.Exists(path)) return;

            try
            {
                File.SetAttributes(path, FileAttributes.Normal);
                File.Delete(path);
                Log($"已删除现有文件: {path}");
            }
            catch (Exception ex)
            {
                Log($"文件删除失败: {ex.Message}");
                throw;
            }
        }

        private static async Task ExtractWithRetry(ZipArchiveEntry entry, string destinationPath, int maxRetries = 5)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                try
                {
                    entry.ExtractToFile(destinationPath, overwrite: true);
                    return;
                }
                catch (IOException) when (i < maxRetries - 1)
                {
                    Log($"文件被占用，等待重试 ({i + 1}/{maxRetries})...");
                    await Task.Delay(3000);
                }
            }
            throw new IOException($"无法解压文件: {destinationPath}");
        }
        #endregion

        #region UI Helpers
        private static void UpdateProgress(int processed, int total)
        {
            var progress = (double)processed / total * 100;
            Log($"进度: {Math.Min(progress, 100):F2}%");
        }

        private static void RestartMainApplication(string mainAppPath)
        {
            var exePath = Path.Combine(mainAppPath, "Software.exe");
            if (!File.Exists(exePath))
            {
                Log($"主程序未找到: {exePath}");
                return;
            }

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = mainAppPath,
                    UseShellExecute = true
                };
                Process.Start(startInfo);
                Log("主程序已成功启动");
            }
            catch (Exception ex)
            {
                Log($"启动失败: {ex.Message}");
            }
        }
        #endregion

        #region Exit Handling
        private static async Task GracefulExit()
        {
            Log("操作完成，10秒后自动退出...");
            await Task.Delay(10000);
            Environment.Exit(0);
        }

        private static void WaitForExit()
        {
            Log("按任意键退出...");
            Console.ReadKey();
        }
        #endregion

        #region Error Handling
        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            var ex = (Exception)e.ExceptionObject;
            Log($"未处理的异常: {ex}");
            WaitForExit();
            Environment.Exit(1);
        }
        #endregion

        #region Utility Methods
        private static bool CheckPrerequisites(string filePath, string mainAppPath)
        {
            if (!CheckDiskSpace(mainAppPath, filePath))
            {
                Log("错误: 磁盘空间不足");
                return false;
            }
            return true;
        }

        private static bool CheckDiskSpace(string targetDir, string zipPath)
        {
            try
            {
                var drive = new DriveInfo(Path.GetPathRoot(targetDir));
                var zipSize = new FileInfo(zipPath).Length;
                return drive.AvailableFreeSpace > zipSize * 3; // 更保守的估算
            }
            catch
            {
                return true; // 空间检查失败时不阻塞更新
            }
        }

        private static void Log(string message)
        {
            var formattedMessage = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            Console.WriteLine(formattedMessage);
            logWriter?.WriteLine(formattedMessage);
        }
        #endregion
    }
}