using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using UtfUnknown;

namespace SecureUpdater
{
    class Program
    {
        private static StreamWriter _logWriter;
        private static Stopwatch stopwatch;

        static void Main(string[] args)
        {
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            InitializeLogging();
            Log("===== 更新程序启动 =====");
            Log($"==== 版本号:{Assembly.GetExecutingAssembly().GetName().Version} ====");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                stopwatch = Stopwatch.StartNew();

                if (args.Length < 2)
                {
                    Log("需要两个参数：<zip文件路径> <目标目录>");
                    WaitForExit();
                    return;
                }

                string zipPath = args[0];
                string targetDir = Path.GetFullPath(args[1].Trim('"'));

                if (!File.Exists(zipPath))
                {
                    Log($"ZIP文件不存在: {zipPath}");
                    WaitForExit();
                    return;
                }

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Log($"创建目标目录: {targetDir}");
                }

                await ExtractZipWithEncoding(zipPath, targetDir);
                LaunchMainApp(targetDir);
            }
            catch (Exception ex)
            {
                Log($"致命错误: {ex}");
            }
            finally
            {
                try
                {
                    stopwatch?.Stop();
                    await GracefulShutdown();
                }
                catch (Exception shutdownEx)
                {
                    Console.WriteLine($"关闭程序时发生错误: {shutdownEx.Message}");
                }
                finally
                {
                    _logWriter?.Close(); // 确保所有日志操作完成后再关闭
                }
            }
        }

        static async Task ExtractZipWithEncoding(string zipPath, string targetDir)
        {
            // 检测ZIP文件编码
            Encoding encoding = await DetectZipEncoding(zipPath);
            Log($"检测到压缩包编码: {encoding.EncodingName}");

            // 使用正确编码打开ZIP文件
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, encoding))
            {
                int totalFiles = archive.Entries.Count;
                int processed = 0;
                var failedFiles = new System.Collections.Generic.List<string>();

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // 跳过目录

                    int retryCount = 0;
                    bool success = false;

                    while (retryCount < 3 && !success)
                    {
                        try
                        {
                            string destPath = GetSafePath(targetDir, entry.FullName);
                            Log($"解压: {destPath}");

                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            entry.ExtractToFile(destPath, overwrite: true);

                            success = true;
                            UpdateProgress(++processed, totalFiles);
                        }
                        catch (IOException ex) when (IsFileLocked(ex))
                        {
                            retryCount++;
                            Log($"文件被占用: {entry.FullName} - 尝试 {retryCount}/3");
                            await Task.Delay(1000); // 等待1秒后重试
                        }
                        catch (Exception ex)
                        {
                            Log($"解压失败: {ex.Message}");
                            failedFiles.Add(entry.FullName);
                            break;
                        }
                    }
                }

                if (failedFiles.Count > 0)
                {
                    Log($"解压完成: 成功={processed}, 失败={failedFiles.Count}, 总数={totalFiles}");
                    Log("失败文件列表:");
                    foreach (var file in failedFiles)
                    {
                        Log($"  - {file}");
                    }
                }
                else
                {
                    Log($"解压完成: 全部{totalFiles}个文件成功");
                }
            }
        }

        static async Task<Encoding> DetectZipEncoding(string zipPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    byte[] buffer = new byte[8192]; // 更大的缓冲区提高检测精度
                    using (var fs = File.OpenRead(zipPath))
                    {
                        int read = fs.Read(buffer, 0, buffer.Length);
                        var result = CharsetDetector.DetectFromBytes(buffer.AsSpan(0, read).ToArray());

                        // 优先使用检测到的编码，否则使用GBK(936)
                        return result.Detected?.Encoding ?? Encoding.GetEncoding(936);
                    }
                }
                catch
                {
                    return Encoding.GetEncoding(936); // 默认使用GBK
                }
            });
        }

        static string GetSafePath(string baseDir, string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(baseDir, relativePath));

            if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("非法路径访问尝试");

            return fullPath;
        }

        static void LaunchMainApp(string targetDir)
        {
            string exePath = Path.Combine(targetDir, "Software.exe");

            if (!File.Exists(exePath))
            {
                Log($"主程序未找到: {exePath}");
                Log($"目录内容: {string.Join("\n", Directory.GetFiles(targetDir))}");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                });
                Log("主程序启动成功");
            }
            catch (Exception ex)
            {
                Log($"启动失败: {ex.Message}");
            }
        }

        #region 辅助方法
        static void InitializeLogging()
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "update.log");
            _logWriter = new StreamWriter(logPath, true, Encoding.UTF8) { AutoFlush = true };
        }

        private static void UpdateProgress(int current, int total)
        {
            double percent = (double)current / total * 100;
            int bars = (int)(percent / 2); // 每2%一个进度条字符

            // 清空当前行
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");

            // 绘制进度条
            Console.Write("[");
            Console.Write(new string('=', bars));
            Console.Write(new string(' ', 50 - bars));
            Console.Write("] ");

            // 设置颜色显示百分比
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{percent:F1}%");
            Console.ResetColor();

            // 显示文件计数
            Console.Write($" ({current}/{total}) ");

            // 计算并显示速度和估计时间
            if (current > 1 && stopwatch.IsRunning)
            {
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                double filesPerSecond = current / elapsedSeconds;
                double estimatedSeconds = (total - current) / filesPerSecond;

                Console.Write($"| {filesPerSecond:F1} 文件/秒 ");
                Console.Write($"| 剩余: {FormatTime(estimatedSeconds)}");
            }

            // 只在完成时换行
            if (current == total)
                Console.WriteLine();
        }

        // 格式化时间显示
        private static string FormatTime(double seconds)
        {
            if (seconds < 60)
                return $"{seconds:F0}秒";
            else if (seconds < 3600)
                return $"{seconds / 60:F1}分钟";
            else
                return $"{seconds / 3600:F1}小时";
        }

        static async Task GracefulShutdown()
        {
            try
            {
                Log("操作完成，10秒后自动退出...");
                for (int i = 10; i > 0; i--)
                {
                    Log($"剩余 {i} 秒...");
                    await Task.Delay(1000);
                }
                Log("更新程序正常退出");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"退出过程中发生错误: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void WaitForExit()
        {
            Log("按任意键退出...");
            Console.ReadKey();
        }

        static void Log(string message)
        {
            string logMsg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}";
            Console.WriteLine(logMsg);

            try
            {
                _logWriter?.WriteLine(logMsg);
                _logWriter?.Flush();
            }
            catch (ObjectDisposedException)
            {  
                // 日志写入器已关闭，忽略异常
            }
        }

        // 检测是否为文件被占用异常
        private static bool IsFileLocked(IOException ex)
        {
            if (ex is IOException ioe)
            {
                int errorCode = Marshal.GetHRForException(ioe) & ((1 << 16) - 1);
                return errorCode == 32 || errorCode == 33; // 32=共享冲突, 33=文件被占用
            } 
            return false;
        }
        #endregion
    }
}