using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using UtfUnknown;

namespace SecureUpdater
{
    class Program
    {
        private static StreamWriter _logWriter;

        static async Task Main(string[] args)
        {
            // 初始化日志
            InitializeLogging();
            Log("===== 更新程序启动 =====");
            Log($"==== 版本号:{Assembly.GetExecutingAssembly().GetName().Version} ====");
            // 必须首先注册编码提供程序
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
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
                _logWriter?.Close();
                await GracefulShutdown();
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

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // 跳过目录

                    try
                    {
                        string destPath = GetSafePath(targetDir, entry.FullName);
                        Log($"解压: {destPath}");

                        Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                        entry.ExtractToFile(destPath, overwrite: true);

                        UpdateProgress(++processed, totalFiles);
                    }
                    catch (Exception ex)
                    {
                        Log($"解压失败: {ex.Message}");
                    }
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

        static void UpdateProgress(int current, int total)
        {
            double percent = (double)current / total * 100;
            Log($"[进度] {Math.Round(percent)}% ({current}/{total})");
        }

        static async Task GracefulShutdown()
        {
            Log("操作完成，10秒后自动退出...");
            await Task.Delay(10000);
            Environment.Exit(0);
        }

        static void WaitForExit()
        {
            Log("按任意键退出...");
            Console.ReadKey();
        }

        static void Log(string message)
        {
            string logMsg = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            Console.WriteLine(logMsg);
            _logWriter?.WriteLine(logMsg);
        }
        #endregion
    }
}