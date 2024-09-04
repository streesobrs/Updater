using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Xml.Linq;
using System.Text.Json;

namespace Updater
{
    class Program
    {
        private static StreamWriter logWriter;

        static async Task Main(string[] args)
        {
            AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler(UnhandledExceptionHandler);

            try
            {
                // 初始化日志记录
                string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.log");
                logWriter = new StreamWriter(logFilePath, true);
                logWriter.AutoFlush = true;

                // 添加分界线和时间戳
                Log("========================================");
                Log($"日志开始时间: {DateTime.Now}");
                Log("========================================");

                // 获取版本号
                string currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
                Log($"当前版本: {currentVersion}");

                // 检查是否有更新
                if (await CheckForUpdatesAsync())
                {
                    Environment.Exit(0);
                }

                if (args.Length < 2)
                {
                    Log("Usage: Updater.exe <filePath> <mainAppPath>");
                    WaitForExit();
                    return;
                }

                string filePath = args[0];
                string mainAppPath = args[1].Trim('"'); // 去掉多余的引号

                // 检查文件路径
                if (!File.Exists(filePath))
                {
                    Log($"文件路径无效: {filePath}");
                    WaitForExit();
                    return;
                }

                if (!Directory.Exists(mainAppPath))
                {
                    Log($"主程序路径无效: {mainAppPath}");
                    WaitForExit();
                    return;
                }

                // 检查磁盘空间
                if (!HasEnoughDiskSpace(mainAppPath, filePath))
                {
                    Log("磁盘空间不足，无法解压文件。");
                    WaitForExit();
                    return;
                }

                // 输出传递的参数以进行调试
                Log($"filePath: {filePath}");
                Log($"mainAppPath: {mainAppPath}");

                try
                {
                    // 解压更新文件
                    using (ZipArchive archive = ZipFile.OpenRead(filePath))
                    {
                        int totalEntries = archive.Entries.Count;
                        int processedEntries = 0;

                        foreach (ZipArchiveEntry entry in archive.Entries)
                        {
                            try
                            {
                                string destinationPath = Path.Combine(mainAppPath, entry.FullName);
                                Log($"解压文件: {destinationPath}");

                                // 确保目标目录存在
                                string destinationDir = Path.GetDirectoryName(destinationPath);
                                if (!Directory.Exists(destinationDir))
                                {
                                    Directory.CreateDirectory(destinationDir);
                                }

                                // 检查文件是否被锁定
                                if (IsFileLocked(destinationPath))
                                {
                                    Log($"文件被锁定，无法解压: {destinationPath}");
                                    continue;
                                }

                                // 如果是文件夹，跳过删除和解压操作
                                if (string.IsNullOrEmpty(entry.Name))
                                {
                                    continue;
                                }

                                // 删除已存在的文件
                                if (File.Exists(destinationPath))
                                {
                                    File.Delete(destinationPath);
                                }

                                // 解压并覆盖现有文件
                                entry.ExtractToFile(destinationPath, true);

                                // 更新进度条
                                processedEntries++;
                                DisplayProgress(processedEntries, totalEntries);
                            }
                            catch (Exception ex)
                            {
                                Log($"解压文件时发生错误: {ex.Message}");
                            }
                        }

                        Log("解压完成！");
                        Log($"解压文件总数: {processedEntries}");

                        // 手动将进度设置为100%
                        DisplayProgress(totalEntries, totalEntries);
                    }

                    // 重新启动主程序
                    Process.Start(Path.Combine(mainAppPath, "Software.exe"));

                    // 等待用户输入以保持控制台窗口打开
                    Log("按任意键退出...");
                    await Task.Delay(60000); // 延迟60秒后自动关闭
                    Environment.Exit(0);
                }
                catch (FileNotFoundException ex)
                {
                    Log($"文件未找到: {ex.Message}");
                    Console.WriteLine($"文件未找到: {ex.Message}");
                    WaitForExit();
                }
                catch (UnauthorizedAccessException ex)
                {
                    Log($"访问被拒绝: {ex.Message}");
                    Console.WriteLine($"访问被拒绝: {ex.Message}");
                    WaitForExit();
                }
                catch (Exception ex)
                {
                    Log($"更新时发生意外错误: {ex.Message}");
                    Console.WriteLine($"更新时发生意外错误: {ex.Message}");
                    WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Log($"更新时发生意外错误: {ex.Message}");
                Console.WriteLine($"更新时发生意外错误: {ex.Message}");
                WaitForExit();
            }
        }

        private static async Task<bool> CheckForUpdatesAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    string updateInfoUrl = "http://example.com/update_info.json";
                    string updateInfoJson = await client.GetStringAsync(updateInfoUrl);
                    JsonDocument updateInfo = JsonDocument.Parse(updateInfoJson);

                    string latestVersion = updateInfo.RootElement.GetProperty("version").GetString();
                    string updateUrl = updateInfo.RootElement.GetProperty("updateUrl").GetString();

                    // 读取当前版本号
                    string currentVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

                    if (latestVersion != currentVersion)
                    {
                        Log($"发现新版本: {latestVersion}");
                        Log("正在下载更新文件...");

                        string updateFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.zip");
                        byte[] updateData = await client.GetByteArrayAsync(updateUrl);
                        await File.WriteAllBytesAsync(updateFilePath, updateData);

                        // 保存传递的参数到临时文件
                        string tempArgsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tempArgs.txt");
                        File.WriteAllLines(tempArgsFilePath, Environment.GetCommandLineArgs());

                        // 生成批处理文件
                        string batFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.bat");
                        using (StreamWriter writer = new StreamWriter(batFilePath))
                        {
                            writer.WriteLine("@echo off");
                            writer.WriteLine("timeout /t 5 /nobreak"); // 等待5秒，确保主程序完全退出
                            writer.WriteLine($"del \"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Updater.exe")}\"");
                            writer.WriteLine($"powershell -Command \"Expand-Archive -Path '{updateFilePath}' -DestinationPath '{AppDomain.CurrentDomain.BaseDirectory}' -Force\"");
                            writer.WriteLine($"del \"{updateFilePath}\"");
                            writer.WriteLine($"start \"\" \"{Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Updater.exe")}\" @\"{tempArgsFilePath}\"");
                            writer.WriteLine($"del \"{tempArgsFilePath}\"");
                            writer.WriteLine($"del \"%~f0\""); // 删除批处理文件自身
                        }

                        // 启动批处理文件
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = batFilePath,
                            WindowStyle = ProcessWindowStyle.Hidden
                        });

                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"检查更新时发生错误: {ex.Message}");
            }

            return false;
        }

        private static void DisplayProgress(int processedEntries, int totalEntries)
        {
            double progress = (double)processedEntries / totalEntries * 100;
            if (progress > 100)
            {
                progress = 100;
            }
            Log($"进度: {progress:F2}%");
        }

        private static bool HasEnoughDiskSpace(string mainAppPath, string filePath)
        {
            DriveInfo drive = new DriveInfo(Path.GetPathRoot(mainAppPath));
            long availableSpace = drive.AvailableFreeSpace;

            FileInfo fileInfo = new FileInfo(filePath);
            long requiredSpace = fileInfo.Length * 2; // 假设解压后的文件大小是压缩文件的两倍

            return availableSpace > requiredSpace;
        }

        private static bool IsFileLocked(string filePath)
        {
            try
            {
                using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    stream.Close();
                }
            }
            catch (IOException)
            {
                return true;
            }
            return false;
        }

        private static void WaitForExit()
        {
            Log("按任意键退出...");
            Console.ReadLine();
        }

        private static void UnhandledExceptionHandler(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            Log($"未处理的异常: {ex.Message}");
            WaitForExit();
        }

        private static void Log(string message)
        {
            Console.WriteLine(message);
            logWriter?.WriteLine($"{DateTime.Now}: {message}");
        }
    }
}
