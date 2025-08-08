using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
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
            // 确保异步操作正确执行
            MainAsync(args).GetAwaiter().GetResult();
        }

        static async Task MainAsync(string[] args)
        {
            // 初始化日志
            InitializeLogging();
            Log("===== 更新程序启动 =====");
            Log($"==== 版本号: {Assembly.GetExecutingAssembly().GetName().Version} ====");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                stopwatch = Stopwatch.StartNew();

                // 参数说明（顺序固定）：
                // 1. 主程序文件名（如Software.exe）
                // 2. 安装包路径（ZIP文件或安装程序路径）
                // 3. 目标安装目录
                // 4. 是否删除安装包（true/false）
                // 5. 更新类型（zip/installer）
                if (args.Length < 5)
                {
                    Log("参数错误！需要5个参数（顺序）：");
                    Log("<主程序文件名> <安装包路径> <目标目录> <是否删除安装包> <更新类型(zip/installer)>");
                    Log($"实际接收到的参数数量: {args.Length}");
                    if (args.Length > 0)
                    {
                        Log("接收到的参数列表:");
                        for (int i = 0; i < args.Length; i++)
                        {
                            Log($"  参数{i + 1}: {args[i]}");
                        }
                    }
                    WaitForExit();
                    return;
                }

                // 打印接收到的所有参数（新增日志）
                Log("接收到的参数如下:");
                Log($"  1. 主程序文件名: {args[0]}");
                Log($"  2. 安装包路径: {args[1]}");
                Log($"  3. 目标目录: {args[2]}");
                Log($"  4. 是否删除安装包: {args[3]}");
                Log($"  5. 更新类型: {args[4]}");

                // 解析参数
                string mainAppExe = args[0].Trim('"');           // 1. 主程序文件名
                string packagePath = args[1];                     // 2. 安装包路径
                string targetDir = Path.GetFullPath(args[2].Trim('"')); // 3. 目标目录
                bool deleteAfterUpdate = bool.TryParse(args[3], out bool result) ? result : true; // 4. 是否删除
                string updateType = args[4].ToLower();            // 5. 更新类型

                // 验证安装包是否存在
                if (!File.Exists(packagePath))
                {
                    Log($"错误：安装包不存在 - {packagePath}");
                    WaitForExit();
                    return;
                }

                // 确保目标目录存在
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Log($"创建目标目录：{targetDir}");
                }

                // 根据更新类型执行对应逻辑
                if (updateType == "zip")
                {
                    Log("===== 开始处理ZIP更新 =====");
                    await HandleZipUpdate(packagePath, targetDir);
                }
                else if (updateType == "installer")
                {
                    Log("===== 开始处理安装包更新 =====");
                    await HandleInstallerUpdate(packagePath, targetDir);
                }
                else
                {
                    Log($"错误：不支持的更新类型 - {updateType}");
                    WaitForExit();
                    return;
                }

                // 根据用户选择删除安装包
                if (deleteAfterUpdate)
                {
                    DeletePackage(packagePath);
                }
                else
                {
                    Log("用户选择保留安装包，不执行删除");
                }

                // 记录更新完成时间（供主程序读取）
                RecordUpdateCompletionTime(targetDir);

                // 启动主程序
                LaunchMainApplication(targetDir, mainAppExe);
            }
            catch (Exception ex)
            {
                Log($"致命错误：{ex}");
                WaitForExit();
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
                    Console.WriteLine($"关闭时发生错误：{shutdownEx.Message}");
                }
                finally
                {
                    _logWriter?.Close();
                }
            }
        }

        #region 处理ZIP更新
        static async Task HandleZipUpdate(string zipPath, string targetDir)
        {
            // 检测ZIP文件编码（解决中文乱码）
            Encoding encoding = await DetectZipEncoding(zipPath);
            Log($"检测到ZIP编码：{encoding.EncodingName}");

            // 解压文件
            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, encoding))
            {
                int totalFiles = archive.Entries.Count;
                int processedFiles = 0;
                var failedFiles = new System.Collections.Generic.List<string>();

                foreach (var entry in archive.Entries)
                {
                    // 跳过空目录
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    int retryCount = 0;
                    bool success = false;

                    // 最多重试3次（解决文件占用问题）
                    while (retryCount < 3 && !success)
                    {
                        try
                        {
                            // 安全路径处理（防止路径遍历攻击）
                            string destPath = GetSafePath(targetDir, entry.FullName);
                            Log($"解压：{destPath}");

                            // 确保目标目录存在
                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));

                            // 覆盖已存在的文件
                            entry.ExtractToFile(destPath, overwrite: true);

                            success = true;
                            UpdateProgress(++processedFiles, totalFiles);
                        }
                        catch (IOException ex) when (IsFileLocked(ex))
                        {
                            retryCount++;
                            Log($"文件被占用（{retryCount}/3）：{entry.FullName}");
                            await Task.Delay(1000); // 等待1秒后重试
                        }
                        catch (Exception ex)
                        {
                            Log($"解压失败：{ex.Message}");
                            failedFiles.Add(entry.FullName);
                            break;
                        }
                    }
                }

                // 输出解压统计
                if (failedFiles.Count > 0)
                {
                    Log($"解压完成：成功{processedFiles}个，失败{failedFiles.Count}个，总计{totalFiles}个");
                    Log("失败文件列表：");
                    foreach (var file in failedFiles)
                        Log($"  - {file}");
                }
                else
                {
                    Log($"解压完成：全部{totalFiles}个文件成功");
                }
            }
        }

        // 检测ZIP文件编码
        static async Task<Encoding> DetectZipEncoding(string zipPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    byte[] buffer = new byte[8192];
                    using (var fs = File.OpenRead(zipPath))
                    {
                        int read = fs.Read(buffer, 0, buffer.Length);
                        var detectionResult = CharsetDetector.DetectFromBytes(buffer.AsSpan(0, read).ToArray());
                        return detectionResult.Detected?.Encoding ?? Encoding.GetEncoding(936); // 默认GBK
                    }
                }
                catch
                {
                    return Encoding.GetEncoding(936); // 失败时默认使用GBK
                }
            });
        }
        #endregion

        #region 处理安装包更新
        static async Task HandleInstallerUpdate(string installerPath, string targetDir)
        {
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true,
                    Verb = "runas" // 请求管理员权限（安装包通常需要）
                };

                try
                {
                    process.Start();
                    Log($"安装包已启动：{installerPath}");
                    Log("等待安装完成...（请在弹出的安装界面中完成操作）");

                    // 等待安装包执行完成
                    await process.WaitForExitAsync();

                    // 检查退出码（0通常表示成功）
                    if (process.ExitCode == 0)
                    {
                        Log("安装包执行成功");
                    }
                    else
                    {
                        Log($"警告：安装包退出码非0（可能安装失败），退出码：{process.ExitCode}");
                    }
                }
                catch (Exception ex)
                {
                    Log($"启动安装包失败：{ex.Message}");
                    throw;
                }
            }
        }
        #endregion

        #region 公共方法
        // 安全路径处理（防止路径遍历攻击）
        static string GetSafePath(string baseDir, string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(baseDir, relativePath));
            if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                throw new System.Security.SecurityException("非法路径访问尝试：" + fullPath);
            return fullPath;
        }

        // 启动主程序
        static void LaunchMainApplication(string targetDir, string mainAppExe)
        {
            string exePath = Path.Combine(targetDir, mainAppExe);
            if (!File.Exists(exePath)) return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                };

                // 只传递2个参数：标识 + 时间（去掉版本号）
                startInfo.ArgumentList.Add("updated"); // 固定标识
                startInfo.ArgumentList.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); // 仅传递时间

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Log($"启动主程序失败：{ex.Message}");
            }
        }

        // 记录更新完成时间到目标目录
        private static void RecordUpdateCompletionTime(string targetDir)
        {
            try
            {
                string timeFile = Path.Combine(targetDir, "update_completion.time");
                File.WriteAllText(timeFile, DateTime.Now.ToString("o")); // ISO格式时间
                Log($"已记录更新时间到：{timeFile}");
            }
            catch (Exception ex)
            {
                Log($"记录更新时间失败：{ex.Message}");
            }
        }

        // 删除安装包
        private static void DeletePackage(string packagePath)
        {
            try
            {
                if (File.Exists(packagePath))
                {
                    File.Delete(packagePath);
                    Log($"已删除安装包：{packagePath}");
                }
            }
            catch (Exception ex)
            {
                Log($"删除安装包失败：{ex.Message}（可能被占用）");
            }
        }
        #endregion

        #region 辅助功能
        // 初始化日志
        static void InitializeLogging()
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            string logPath = Path.Combine(logDir, $"update_{DateTime.Now:yyyy-MM-dd}.log");
            _logWriter = new StreamWriter(logPath, true, Encoding.UTF8) { AutoFlush = true };
        }

        // 显示解压进度
        private static void UpdateProgress(int current, int total)
        {
            double percent = (double)current / total * 100;
            int progressBars = (int)(percent / 2); // 每2%一个进度条字符

            // 清空当前行并绘制进度条
            Console.Write("\r" + new string(' ', Console.WindowWidth - 1) + "\r");
            Console.Write("[");
            Console.Write(new string('=', progressBars));
            Console.Write(new string(' ', 50 - progressBars));
            Console.Write("] ");

            // 显示百分比
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{percent:F1}%");
            Console.ResetColor();

            // 显示文件计数
            Console.Write($" （{current}/{total}）");

            // 显示速度和剩余时间
            if (current > 1 && stopwatch.IsRunning)
            {
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                double filesPerSecond = current / elapsedSeconds;
                double estimatedRemainingSeconds = (total - current) / filesPerSecond;

                Console.Write($" | 速度：{filesPerSecond:F1}文件/秒");
                Console.Write($" | 剩余：{FormatTime(estimatedRemainingSeconds)}");
            }

            // 完成时换行
            if (current == total)
                Console.WriteLine();
        }

        // 格式化时间显示
        private static string FormatTime(double seconds)
        {
            if (seconds < 60)
                return $"{(int)seconds}秒";
            else if (seconds < 3600)
                return $"{seconds / 60:F1}分钟";
            else
                return $"{seconds / 3600:F1}小时";
        }

        // 优雅关闭
        static async Task GracefulShutdown()
        {
            Log("更新操作已完成");
            Log("10秒后自动退出更新程序...");

            for (int i = 10; i > 0; i--)
            {
                Log($"剩余 {i} 秒...");
                await Task.Delay(1000);
            }

            Log("更新程序已退出");
            Environment.Exit(0);
        }

        // 等待用户按键退出（错误时）
        static void WaitForExit()
        {
            Log("按任意键退出...");
            Console.ReadKey();
            Environment.Exit(1);
        }

        // 日志记录
        static void Log(string message)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Console.WriteLine(logMessage);

            try
            {
                _logWriter?.WriteLine(logMessage);
            }
            catch (ObjectDisposedException)
            {
                // 日志写入器已关闭，忽略
            }
        }

        // 检测文件是否被锁定
        private static bool IsFileLocked(IOException ex)
        {
            int errorCode = Marshal.GetHRForException(ex) & ((1 << 16) - 1);
            return errorCode == 32 || errorCode == 33; // 32=共享冲突，33=文件被占用
        }
        #endregion
    }
}
