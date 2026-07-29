using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;
using System.Threading.Tasks;
using UtfUnknown;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using System.Text.Json;

namespace Updater
{
    /// <summary>
    /// 配置常量类 - 集中管理所有常量和配置参数
    /// </summary>
    public static class Config
    {
        // 更新类型常量
        public const string UpdateTypeZip = "zip";
        public const string UpdateTypeInstaller = "installer";
        public const string UpdateTypeIncremental = "incremental";

        // 命令行参数常量
        public const string TestModeArg = "--test";
        public const string HelpArg1 = "--help";
        public const string HelpArg2 = "-h";
        public const string TestErrorArg = "--test-error";

        // 异常测试类型
        public const string TestErrorFileCorruption = "file-corruption";
        public const string TestErrorDiskFull = "disk-full";
        public const string TestErrorPermissionDenied = "permission-denied";

        // 文件相关常量
        public const string DeleteListFileName = "_delete_list.txt";
        public const string UpdateCompletionFileName = "update_completion.time";
        public const string LogDirectoryName = "logs";
        public const string LogFilePrefix = "update_";
        public const string TestReportFileName = "test_report.txt";  // 改为文本格式

        // 编码和格式常量
        public static readonly Encoding DefaultEncoding = Encoding.GetEncoding(936);
        public const string DateTimeFormat = "yyyy-MM-dd HH:mm:ss.fff";
        public const string DateFormat = "yyyy-MM-dd";
        public const string IsoDateTimeFormat = "o"; // ISO 8601格式

        // 性能相关常量
        public const int MaxRetryCount = 3;
        public const int RetryDelayMs = 1000;
        public const int BufferSize = 81920;
        public const int ProgressUpdateThreshold = 1; // 进度更新阈值百分比

        // 测试模式常量
        public const int MinPerformanceScale = 1;
        public const int MaxPerformanceScale = 5;
        public const int TestFileCorruptionPosition = 1024; // 损坏文件的位置
        public const int TestFileCorruptionSize = 1024; // 损坏数据大小
        public const long TestDiskFullReserveBytes = 1024 * 1024 * 50; // 保留50MB空间用于测试

        // 目录深度和文件大小配置（用于性能测试）
        public static readonly Dictionary<int, (int LargeFileCount, int LargeFileSizeMB, int SmallFileCount, int DirDepth)>
            PerformanceScales = new Dictionary<int, (int, int, int, int)>
        {
            {1, (2, 10, 50, 3)},
            {2, (3, 20, 100, 4)},
            {3, (5, 30, 300, 5)},
            {4, (8, 50, 500, 6)},
            {5, (10, 100, 1000, 8)}
        };

        // 自动退出等待时间（秒）
        public const int GracefulShutdownWaitSeconds = 10;
        public const int TestModeShutdownWaitSeconds = 3;
    }

    /// <summary>
    /// 测试报告数据类
    /// </summary>
    public class TestReport
    {
        public string TestType { get; set; }
        public string ErrorScenario { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public double DurationSeconds { get; set; }
        public bool Success { get; set; }
        public string ExpectedResult { get; set; }
        public string ActualResult { get; set; }
        public string ErrorMessage { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new Dictionary<string, object>();

        // 重写ToString方法，生成文本报告
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== 测试报告 ===");
            sb.AppendLine($"测试类型: {TestType}");
            sb.AppendLine($"异常场景: {ErrorScenario ?? "无"}");
            sb.AppendLine($"开始时间: {StartTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"结束时间: {EndTime:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"总耗时: {DurationSeconds:F2}秒");
            sb.AppendLine($"测试结果: {(Success ? "成功" : "失败")}");
            sb.AppendLine($"预期结果: {ExpectedResult}");
            sb.AppendLine($"实际结果: {ActualResult}");

            if (!string.IsNullOrEmpty(ErrorMessage))
                sb.AppendLine($"错误信息: {ErrorMessage}");

            sb.AppendLine("\n=== 性能指标 ===");
            foreach (var metric in Metrics)
                sb.AppendLine($"{metric.Key}: {metric.Value}");

            return sb.ToString();
        }
    }

    /// <summary>
    /// 文件系统操作接口 - 提高可测试性
    /// </summary>
    public interface IFileSystem
    {
        bool FileExists(string path);
        bool DirectoryExists(string path);
        void CreateDirectory(string path);
        void DeleteFile(string path);
        void DeleteDirectory(string path, bool recursive);
        Stream OpenRead(string path);
        Stream OpenWrite(string path);
        string[] ReadAllLines(string path);
        void WriteAllText(string path, string content);
        void WriteAllLines(string path, string[] contents, Encoding encoding);
        IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);
        FileInfo GetFileInfo(string path);
        void SetFileAttributes(string path, FileAttributes attributes);
        void SetDirectoryPermissions(string path, bool readOnly);
        long GetDriveFreeSpace(string path);
    }

    /// <summary>
    /// 物理文件系统实现 - 包装System.IO操作
    /// </summary>
    public class PhysicalFileSystem : IFileSystem
    {
        public bool FileExists(string path) => File.Exists(path);
        public bool DirectoryExists(string path) => Directory.Exists(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public void DeleteFile(string path) => File.Delete(path);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
        public Stream OpenRead(string path) => File.OpenRead(path);
        public Stream OpenWrite(string path) => File.OpenWrite(path);
        public string[] ReadAllLines(string path) => File.ReadAllLines(path);
        public void WriteAllText(string path, string content) => File.WriteAllText(path, content);
        public void WriteAllLines(string path, string[] contents, Encoding encoding) => File.WriteAllLines(path, contents, encoding);
        public IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption) =>
            Directory.EnumerateFiles(path, searchPattern, searchOption);
        public FileInfo GetFileInfo(string path) => new FileInfo(path);
        public void SetFileAttributes(string path, FileAttributes attributes) => File.SetAttributes(path, attributes);
        public void SetDirectoryPermissions(string path, bool readOnly)
        {
            var dirInfo = new DirectoryInfo(path);
            var accessRules = dirInfo.GetAccessControl();
            var currentUser = WindowsIdentity.GetCurrent().Name;

            if (readOnly)
            {
                accessRules.RemoveAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.Write | FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }
            else
            {
                accessRules.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.Write | FileSystemRights.Modify,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            dirInfo.SetAccessControl(accessRules);
        }

        public long GetDriveFreeSpace(string path)
        {
            string driveLetter = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(driveLetter))
                return 0;

            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                if (drive.Name.Equals(driveLetter, StringComparison.OrdinalIgnoreCase))
                {
                    return drive.AvailableFreeSpace;
                }
            }
            return 0;
        }
    }

    /// <summary>
    /// 性能监控类 - 用于测量操作耗时和内存使用
    /// </summary>
    public class PerformanceMonitor : IDisposable
    {
        private readonly Stopwatch _stopwatch;
        private readonly string _operationName;
        private readonly long _initialMemory;

        public PerformanceMonitor(string operationName)
        {
            _operationName = operationName;
            _stopwatch = Stopwatch.StartNew();
            _initialMemory = GC.GetTotalMemory(false);
            Program.Log($"开始操作: {operationName}", Program.LogLevel.Debug);
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            long finalMemory = GC.GetTotalMemory(false);
            long memoryUsed = finalMemory - _initialMemory;

            Program.Log($"完成操作: {_operationName}, 耗时: {_stopwatch.Elapsed.TotalSeconds:F2}秒, " +
                       $"内存使用: {FormatMemorySize(memoryUsed)}", Program.LogLevel.Debug);
        }

        private static string FormatMemorySize(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1}MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1}GB";
        }
    }

    /// <summary>
    /// 版本辅助类 - 用于版本号处理
    /// </summary>
    public static class VersionHelper
    {
        public static bool IsValidVersion(string versionString)
        {
            return Version.TryParse(versionString, out _);
        }

        public static int CompareVersions(string version1, string version2)
        {
            if (!Version.TryParse(version1, out Version v1) || !Version.TryParse(version2, out Version v2))
                return 0;

            return v1.CompareTo(v2);
        }
    }

    /// <summary>
    /// 主程序类 - 负责程序更新功能
    /// </summary>
    class Program
    {
        // 日志级别枚举
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error
        }

        // 私有字段
        private static StreamWriter _logWriter;
        private static Stopwatch _stopwatch;
        private static bool _isTestMode = false;
        private static int? _performanceTestScale = null;
        private static string _testUpdateType = Config.UpdateTypeZip;
        private static string _testBaseDir;
        private static bool _needShowHelp = false;
        private static IFileSystem _fileSystem = new PhysicalFileSystem();
        private static string _testErrorScenario = null;
        private static TestReport _currentTestReport = null;
        private static string _backupDir = null;

        /// <summary>
        /// 程序入口点
        /// </summary>
        /// <param name="args">命令行参数</param>
        static void Main(string[] args)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            // 优先检查帮助命令（--help/-h），优先级最高
            if (args.Contains(Config.HelpArg1, StringComparer.OrdinalIgnoreCase) ||
                args.Contains(Config.HelpArg2, StringComparer.OrdinalIgnoreCase))
            {
                _needShowHelp = true;
            }
            else
            {
                ParseTestModeArgs(args);
            }

            MainAsync(args).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 解析测试模式参数
        /// </summary>
        /// <param name="args">命令行参数</param>
        static void ParseTestModeArgs(string[] args)
        {
            if (args.Length > 0 && args[0].Equals(Config.TestModeArg, StringComparison.OrdinalIgnoreCase))
            {
                _isTestMode = true;
                int argIndex = 1;

                // 检查是否有异常测试参数
                if (argIndex < args.Length && args[argIndex].Equals(Config.TestErrorArg, StringComparison.OrdinalIgnoreCase))
                {
                    argIndex++;
                    if (argIndex < args.Length)
                    {
                        _testErrorScenario = args[argIndex].ToLower();
                        argIndex++;

                        // 验证异常场景类型是否有效
                        if (!new[] { Config.TestErrorFileCorruption, Config.TestErrorDiskFull, Config.TestErrorPermissionDenied }.Contains(_testErrorScenario))
                        {
                            Log($"警告：未知的异常测试类型 '{_testErrorScenario}'，将使用默认值", LogLevel.Warning);
                            _testErrorScenario = null;
                        }
                    }
                }

                // 解析测试类型（默认zip，支持incremental）
                if (argIndex < args.Length && (args[argIndex].Equals(Config.UpdateTypeZip, StringComparison.OrdinalIgnoreCase) ||
                                       args[argIndex].Equals(Config.UpdateTypeIncremental, StringComparison.OrdinalIgnoreCase)))
                {
                    _testUpdateType = args[argIndex].ToLower();
                    argIndex++;
                    // 解析性能测试级别（1-5，数字越大测试强度越高）
                    if (argIndex < args.Length && int.TryParse(args[argIndex], out int scale))
                    {
                        _performanceTestScale = Math.Clamp(scale, Config.MinPerformanceScale, Config.MaxPerformanceScale);
                    }
                }
                else
                {
                    // 兼容原有逻辑（仅指定级别，默认测试类型为zip）
                    if (argIndex < args.Length && int.TryParse(args[argIndex], out int scale))
                    {
                        _performanceTestScale = Math.Clamp(scale, Config.MinPerformanceScale, Config.MaxPerformanceScale);
                        argIndex++;
                    }
                }
            }
        }

        /// <summary>
        /// 显示帮助信息
        /// </summary>
        static void ShowHelp()
        {
            var helpText = @"
======================================= Updater 程序帮助文档 =======================================
程序功能：支持ZIP全量更新、安装包更新、增量包更新，以及对应的测试模式（基础测试/性能测试/异常测试）

【1. 普通更新模式（默认）】
用途：实际环境中执行程序更新，需传入5个必选参数
参数格式：
  Updater.exe <主程序文件名> <安装包路径> <目标目录> <是否删除安装包> <更新类型>

参数说明：
  1. 主程序文件名    ：更新完成后需要启动的主程序（如：Software.exe）
  2. 安装包路径      ：更新包的完整路径（支持ZIP包、exe安装包）
  3. 目标目录        ：程序安装/更新的目标文件夹（如：D:\Software）
  4. 是否删除安装包  ：更新后是否删除安装包（true/false，不区分大小写）
  5. 更新类型        ：支持3种类型（不区分大小写）
                       - zip       ：ZIP全量包更新（解压覆盖文件）
                       - installer ：EXE安装包更新（调用安装包执行）
                       - incremental：增量包更新（需包含_delete_list.txt删除列表）

普通模式示例：
  1. 增量更新（保留安装包）：
     Updater.exe Software.exe ""D:\update\0.1.0_to_0.2.0.zip"" ""D:\Software"" false incremental
  2. ZIP全量更新（删除安装包）：
     Updater.exe App.exe ""C:\temp\full_update.zip"" ""C:\App"" true zip
  3. 安装包更新：
     Updater.exe Tool.exe ""E:\setup\v2.0.exe"" ""E:\Tool"" true installer


【2. 测试模式（--test）】
用途：开发/测试阶段验证更新功能，无需实际程序环境，自动生成测试数据
触发方式：参数以 --test 开头（不区分大小写）
参数格式：
  Updater.exe --test [--test-error 异常类型] [测试类型] [性能测试强度]

参数说明：
  1. --test          ：固定前缀，标记进入测试模式
  2. --test-error（可选）：异常场景测试类型（不区分大小写）
                       - file-corruption ：文件损坏测试（故意损坏ZIP包）
                       - disk-full       ：磁盘空间不足测试
                       - permission-denied：权限不足测试
  3. 测试类型（可选）：默认zip，支持2种类型（不区分大小写）
                       - zip       ：ZIP全量更新测试（自动生成文本/二进制测试文件）
                       - incremental：增量更新测试（自动生成基础目录+增量包+删除列表）
  4. 性能测试强度（可选）：1-5的整数（数字越大，生成的测试文件越多/越大）
                       - 1级（默认）：2个10MB大文件 + 50个小文件 + 3层目录
                       - 5级（最高）：10个100MB大文件 + 1000个小文件 + 8层目录
                       * 不传入此参数则执行「基础功能测试」，仅验证更新逻辑正确性

测试模式示例：
  1. 增量更新基础测试（验证逻辑，不测试性能）：
     Updater.exe --test incremental
  2. ZIP更新性能测试（强度3级，中等规模）：
     Updater.exe --test zip 3
  3. 增量更新性能测试（强度5级，最大规模）：
     Updater.exe --test incremental 5
  4. 文件损坏异常测试：
     Updater.exe --test --test-error file-corruption zip
  5. 磁盘空间不足异常测试：
     Updater.exe --test --test-error disk-full incremental


【3. 帮助命令】
触发方式（任意一种即可）：
  Updater.exe --help
  Updater.exe -h

说明：显示此帮助文档，忽略其他所有参数
====================================================================================================
";
            Console.WriteLine(helpText);
        }

        /// <summary>
        /// 异步主程序入口
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <returns>异步任务</returns>
        static async Task MainAsync(string[] args)
        {
            // 优先处理帮助命令：显示帮助后直接退出
            if (_needShowHelp)
            {
                ShowHelp();
                return;
            }

            InitializeLogging();
            Log("===== 更新程序启动 =====", LogLevel.Info);
            Log($"==== 版本号: {Assembly.GetExecutingAssembly().GetName().Version} ====", LogLevel.Info);
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                _stopwatch = Stopwatch.StartNew();

                if (_isTestMode)
                {
                    await RunExtractionTest();
                    return;
                }

                // 验证参数并提取参数值
                if (!ValidateArguments(args, out string mainAppExe, out string packagePath,
                    out string targetDir, out bool deleteAfterUpdate, out string updateType))
                {
                    WaitForExit();
                    return;
                }

                // 创建备份目录
                CreateBackup(targetDir);

                // 根据更新类型执行相应的更新操作
                bool updateSuccess = false;
                try
                {
                    if (updateType == Config.UpdateTypeZip)
                    {
                        Log("===== 开始处理ZIP更新 =====", LogLevel.Info);
                        await HandleZipUpdate(packagePath, targetDir);
                    }
                    else if (updateType == Config.UpdateTypeInstaller)
                    {
                        Log("===== 开始处理安装包更新 =====", LogLevel.Info);
                        await HandleInstallerUpdate(packagePath, targetDir);
                    }
                    else if (updateType == Config.UpdateTypeIncremental)
                    {
                        Log("===== 开始处理增量包更新 =====", LogLevel.Info);
                        await HandleIncrementalUpdate(packagePath, targetDir);
                    }
                    else
                    {
                        Log($"错误：不支持的更新类型 - {updateType}", LogLevel.Error);
                        Log($"支持的更新类型：{Config.UpdateTypeZip} / {Config.UpdateTypeInstaller} / {Config.UpdateTypeIncremental}", LogLevel.Info);
                        WaitForExit();
                        return;
                    }
                    updateSuccess = true;
                }
                catch (Exception ex)
                {
                    Log($"更新过程中发生错误，将尝试回滚: {ex.Message}", LogLevel.Error);
                    RollbackUpdate(targetDir);
                    throw;
                }

                // 根据用户选择决定是否删除安装包
                if (deleteAfterUpdate)
                {
                    DeletePackage(packagePath);
                }
                else
                {
                    Log("用户选择保留安装包，不执行删除", LogLevel.Info);
                }

                // 记录更新完成时间并启动主程序
                RecordUpdateCompletionTime(targetDir);
                LaunchMainApplication(targetDir, mainAppExe);

                // 更新成功后清理备份
                if (updateSuccess && !string.IsNullOrEmpty(_backupDir) && _fileSystem.DirectoryExists(_backupDir))
                {
                    try
                    {
                        _fileSystem.DeleteDirectory(_backupDir, true);
                        Log($"已清理备份目录: {_backupDir}", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        Log($"清理备份目录失败: {ex.Message}", LogLevel.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"致命错误：{ex}", LogLevel.Error);
                Log($"提示：输入 {AppDomain.CurrentDomain.FriendlyName} {Config.HelpArg1} 查看正确使用方法", LogLevel.Info);
                WaitForExit();
            }
            finally
            {
                try
                {
                    _stopwatch?.Stop();
                    await GracefulShutdown();
                }
                catch (Exception shutdownEx)
                {
                    Console.WriteLine($"关闭时发生错误：{shutdownEx.Message}");
                }
                finally
                {
                    _logWriter?.Dispose();
                }
            }
        }

        /// <summary>
        /// 创建更新前的备份
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        private static void CreateBackup(string targetDir)
        {
            if (!_fileSystem.DirectoryExists(targetDir))
            {
                Log("目标目录不存在，无需创建备份", LogLevel.Info);
                return;
            }

            try
            {
                _backupDir = $"{targetDir}_backup_{DateTime.Now:yyyyMMddHHmmss}";
                Log($"开始创建备份到: {_backupDir}", LogLevel.Info);

                // 创建备份目录
                _fileSystem.CreateDirectory(_backupDir);

                // 备份关键文件类型
                var backupPatterns = new[] { "*.exe", "*.dll", "*.config", "*.xml", "*.json" };
                foreach (var pattern in backupPatterns)
                {
                    foreach (var file in _fileSystem.EnumerateFiles(targetDir, pattern, SearchOption.AllDirectories))
                    {
                        string relativePath = Path.GetRelativePath(targetDir, file);
                        string destPath = Path.Combine(_backupDir, relativePath);

                        _fileSystem.CreateDirectory(Path.GetDirectoryName(destPath));
                        File.Copy(file, destPath, true);
                    }
                }

                Log("备份创建完成", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"创建备份失败: {ex.Message}", LogLevel.Warning);
                _backupDir = null;
            }
        }

        /// <summary>
        /// 回滚更新
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        private static void RollbackUpdate(string targetDir)
        {
            if (string.IsNullOrEmpty(_backupDir) || !_fileSystem.DirectoryExists(_backupDir))
            {
                Log("没有可用的备份，无法回滚", LogLevel.Warning);
                return;
            }

            try
            {
                Log($"开始从备份回滚: {_backupDir}", LogLevel.Info);

                // 恢复备份文件
                var backupPatterns = new[] { "*.exe", "*.dll", "*.config", "*.xml", "*.json" };
                foreach (var pattern in backupPatterns)
                {
                    foreach (var file in _fileSystem.EnumerateFiles(_backupDir, pattern, SearchOption.AllDirectories))
                    {
                        string relativePath = Path.GetRelativePath(_backupDir, file);
                        string destPath = Path.Combine(targetDir, relativePath);

                        _fileSystem.CreateDirectory(Path.GetDirectoryName(destPath));
                        File.Copy(file, destPath, true);
                    }
                }

                Log("回滚完成", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"回滚失败: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 验证参数并提取参数值
        /// </summary>
        /// <param name="args">命令行参数</param>
        /// <param name="mainAppExe">主程序文件名</param>
        /// <param name="packagePath">安装包路径</param>
        /// <param name="targetDir">目标目录</param>
        /// <param name="deleteAfterUpdate">是否删除安装包</param>
        /// <param name="updateType">更新类型</param>
        /// <returns>验证是否成功</returns>
        private static bool ValidateArguments(string[] args, out string mainAppExe, out string packagePath,
            out string targetDir, out bool deleteAfterUpdate, out string updateType)
        {
            mainAppExe = null;
            packagePath = null;
            targetDir = null;
            deleteAfterUpdate = true;
            updateType = null;

            if (args.Length < 5)
            {
                Log("参数错误！需要5个参数（顺序）：", LogLevel.Error);
                Log("<主程序文件名> <安装包路径> <目标目录> <是否删除安装包> <更新类型(zip/installer/incremental)>", LogLevel.Info);
                Log($"实际接收到的参数数量: {args.Length}", LogLevel.Info);

                if (args.Length > 0)
                {
                    Log("接收到的参数列表:", LogLevel.Info);
                    for (int i = 0; i < args.Length; i++)
                    {
                        Log($"  参数{i + 1}: {args[i]}", LogLevel.Info);
                    }
                }

                Log($"提示：输入 {AppDomain.CurrentDomain.FriendlyName} {Config.HelpArg1} 查看详细使用说明", LogLevel.Info);
                return false;
            }

            mainAppExe = args[0].Trim('"');
            packagePath = args[1];
            targetDir = Path.GetFullPath(args[2].Trim('"'));
            deleteAfterUpdate = bool.TryParse(args[3], out bool result) ? result : true;
            updateType = args[4].ToLower();

            if (!_fileSystem.FileExists(packagePath))
            {
                Log($"错误：安装包不存在 - {packagePath}", LogLevel.Error);
                return false;
            }

            return true;
        }

        #region 测试功能
        /// <summary>
        /// 运行提取测试
        /// </summary>
        /// <returns>异步任务</returns>
        static async Task RunExtractionTest()
        {
            // 初始化测试报告
            _currentTestReport = new TestReport
            {
                TestType = _testUpdateType,
                ErrorScenario = _testErrorScenario,
                StartTime = DateTime.Now,
                Success = false
            };

            bool isPerformanceTest = _performanceTestScale.HasValue;
            if (_testErrorScenario != null)
            {
                Log($"===== 测试模式（异常场景测试：{_testErrorScenario}，类型：{_testUpdateType}）====", LogLevel.Info);
                _currentTestReport.ExpectedResult = GetExpectedResultForErrorScenario();
            }
            else if (isPerformanceTest)
            {
                Log($"===== 测试模式（性能测试，强度：{_performanceTestScale.Value}，类型：{_testUpdateType}）====", LogLevel.Info);
            }
            else
            {
                Log($"===== 测试模式（基础功能测试，类型：{_testUpdateType}）====", LogLevel.Info);
            }

            string testZipPath = null;
            string testTargetDir = null;
            var testStopwatch = Stopwatch.StartNew();

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "UpdaterTest");
                _fileSystem.CreateDirectory(tempDir);
                Log($"创建临时测试目录：{tempDir}", LogLevel.Info);

                testZipPath = Path.Combine(tempDir, "test_package.zip");

                // 根据测试类型生成对应包
                if (isPerformanceTest)
                {
                    GeneratePerformanceTestZip(testZipPath);
                    testTargetDir = Path.Combine(tempDir, "extracted");
                }
                else
                {
                    if (_testUpdateType == Config.UpdateTypeIncremental)
                    {
                        // 增量测试：生成基础目录和增量包
                        GenerateIncrementalTestZip(testZipPath, tempDir);
                        testTargetDir = _testBaseDir; // 目标目录为基础目录
                    }
                    else
                    {
                        // 原有ZIP测试
                        GenerateBasicTestZip(testZipPath);
                        testTargetDir = Path.Combine(tempDir, "extracted");
                    }
                }

                // 应用异常场景
                if (_testErrorScenario != null)
                {
                    await ApplyErrorScenario(testZipPath, testTargetDir);
                }

                var zipFileInfo = _fileSystem.GetFileInfo(testZipPath);
                Log($"生成测试包：{testZipPath}（大小：{FormatFileSize(zipFileInfo.Length)}）", LogLevel.Info);

                Log("开始更新测试...", LogLevel.Info);
                var extractionStopwatch = Stopwatch.StartNew();
                var memoryBefore = Process.GetCurrentProcess().WorkingSet64;

                // 执行更新操作并捕获异常
                bool updateThrewException = false;
                Exception updateException = null;

                try
                {
                    // 根据测试类型调用对应更新方法
                    if (_testUpdateType == Config.UpdateTypeIncremental)
                    {
                        await HandleIncrementalUpdate(testZipPath, testTargetDir);
                    }
                    else
                    {
                        await HandleZipUpdate(testZipPath, testTargetDir);
                    }
                }
                catch (Exception ex)
                {
                    updateThrewException = true;
                    updateException = ex;
                    Log($"更新过程中捕获到预期异常: {ex.Message}", LogLevel.Info);
                }

                extractionStopwatch.Stop();
                var memoryAfter = Process.GetCurrentProcess().WorkingSet64;
                var memoryUsed = memoryAfter - memoryBefore;

                // 记录测试指标
                _currentTestReport.Metrics["TotalTimeSeconds"] = extractionStopwatch.Elapsed.TotalSeconds;
                _currentTestReport.Metrics["MemoryUsedBytes"] = memoryUsed;
                _currentTestReport.Metrics["PackageSizeBytes"] = zipFileInfo.Length;

                if (zipFileInfo.Length > 0 && extractionStopwatch.Elapsed.TotalSeconds > 0)
                {
                    _currentTestReport.Metrics["AverageSpeedBytesPerSecond"] =
                        zipFileInfo.Length / extractionStopwatch.Elapsed.TotalSeconds;
                }

                // 性能测试日志
                if (isPerformanceTest)
                {
                    Log("===== 性能指标 =====", LogLevel.Info);
                    Log($"总时间：{extractionStopwatch.Elapsed.TotalSeconds:F2}秒", LogLevel.Info);
                    Log($"平均速度：{FormatFileSize((long)(zipFileInfo.Length / extractionStopwatch.Elapsed.TotalSeconds))}/秒", LogLevel.Info);
                    Log($"峰值内存占用：{FormatFileSize(memoryUsed)}", LogLevel.Info);
                }

                // 验证结果
                Log("验证更新结果...", LogLevel.Info);
                bool verifySuccess = false;

                // 对于异常测试，验证是否按预期失败
                if (_testErrorScenario != null)
                {
                    verifySuccess = VerifyErrorScenarioResult(updateThrewException, updateException);
                    _currentTestReport.ActualResult = updateException?.Message ?? "更新未抛出异常";
                }
                else
                {
                    verifySuccess = VerifyExtractedFiles(testTargetDir, isPerformanceTest);
                }

                if (verifySuccess)
                {
                    Log($"===== {_testUpdateType}测试成功！所有验证通过 =====", LogLevel.Info);
                    _currentTestReport.Success = true;
                }
                else
                {
                    Log($"===== {_testUpdateType}测试失败！部分验证未通过 =====", LogLevel.Error);
                    _currentTestReport.Success = false;
                    _currentTestReport.ErrorMessage = updateException?.ToString();
                }
            }
            catch (Exception ex)
            {
                Log($"测试过程发生错误：{ex}", LogLevel.Error);
                _currentTestReport.ErrorMessage = ex.ToString();
            }
            finally
            {
                testStopwatch.Stop();
                _currentTestReport.EndTime = DateTime.Now;
                _currentTestReport.DurationSeconds = testStopwatch.Elapsed.TotalSeconds;

                // 保存测试报告
                SaveTestReport();

                Log($"===== 测试总耗时：{testStopwatch.Elapsed.TotalSeconds:F2}秒 =====", LogLevel.Info);

                // 清理目录（包含基础目录）
                if (!string.IsNullOrEmpty(_testBaseDir) && _fileSystem.DirectoryExists(_testBaseDir))
                {
                    try
                    {
                        // 恢复权限以便删除
                        if (_testErrorScenario == Config.TestErrorPermissionDenied)
                        {
                            _fileSystem.SetDirectoryPermissions(_testBaseDir, false);
                        }

                        _fileSystem.DeleteDirectory(_testBaseDir, recursive: true);
                        Log($"已清理测试基础目录：{_testBaseDir}", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        Log($"清理解压目录失败：{ex.Message}", LogLevel.Warning);
                    }
                }

                if (!string.IsNullOrEmpty(testTargetDir) && _fileSystem.DirectoryExists(testTargetDir) && _testUpdateType != Config.UpdateTypeIncremental)
                {
                    try
                    {
                        // 恢复权限以便删除
                        if (_testErrorScenario == Config.TestErrorPermissionDenied)
                        {
                            _fileSystem.SetDirectoryPermissions(testTargetDir, false);
                        }

                        _fileSystem.DeleteDirectory(testTargetDir, recursive: true);
                        Log($"已清理测试解压目录：{testTargetDir}", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        Log($"清理解压目录失败：{ex.Message}", LogLevel.Warning);
                    }
                }

                if (!string.IsNullOrEmpty(testZipPath) && _fileSystem.FileExists(testZipPath))
                {
                    try
                    {
                        _fileSystem.DeleteFile(testZipPath);
                        Log($"已删除测试包：{testZipPath}", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        Log($"删除测试包失败：{ex.Message}", LogLevel.Warning);
                    }
                }
            }
        }

        /// <summary>
        /// 应用异常场景
        /// </summary>
        /// <param name="zipPath">ZIP包路径</param>
        /// <param name="targetDir">目标目录</param>
        static async Task ApplyErrorScenario(string zipPath, string targetDir)
        {
            switch (_testErrorScenario)
            {
                case Config.TestErrorFileCorruption:
                    await ApplyFileCorruption(zipPath);
                    break;
                case Config.TestErrorDiskFull:
                    await ApplyDiskFull(targetDir);
                    break;
                case Config.TestErrorPermissionDenied:
                    ApplyPermissionDenied(targetDir);
                    break;
            }
        }

        /// <summary>
        /// 应用文件损坏场景
        /// </summary>
        /// <param name="zipPath">ZIP包路径</param>
        static async Task ApplyFileCorruption(string zipPath)
        {
            Log("应用文件损坏场景...", LogLevel.Info);

            try
            {
                // 读取文件内容
                byte[] fileBytes = await File.ReadAllBytesAsync(zipPath);

                // 确保文件足够大可以损坏
                if (fileBytes.Length > 4096) // 要求文件至少4KB才能有效损坏
                {
                    // 方案1：损坏ZIP文件头部（前512字节）- 这是ZIP格式的关键部分
                    byte[] corruptData = new byte[512];
                    new Random().NextBytes(corruptData);
                    Array.Copy(corruptData, 0, fileBytes, 0, 512);

                    // 方案2：损坏ZIP文件中央目录区域（文件末尾）
                    int centralDirStart = Math.Max(0, fileBytes.Length - 2048);
                    byte[] centralDirCorrupt = new byte[1024];
                    new Random().NextBytes(centralDirCorrupt);
                    Array.Copy(centralDirCorrupt, 0, fileBytes, centralDirStart, 1024);

                    // 写回损坏的文件
                    await File.WriteAllBytesAsync(zipPath, fileBytes);
                    Log($"已损坏ZIP文件的关键结构（头部512字节和中央目录区域）", LogLevel.Info);
                }
                else
                {
                    Log("文件太小，无法有效应用损坏场景", LogLevel.Warning);
                    // 对于小文件，直接填充随机数据完全破坏它
                    byte[] corruptData = new byte[fileBytes.Length];
                    new Random().NextBytes(corruptData);
                    await File.WriteAllBytesAsync(zipPath, corruptData);
                    Log("已完全破坏小型ZIP文件", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Log($"应用文件损坏场景时出错: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 应用磁盘空间不足场景
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        static async Task ApplyDiskFull(string targetDir)
        {
            Log("应用磁盘空间不足场景...", LogLevel.Info);

            string tempFile = Path.Combine(Path.GetDirectoryName(targetDir), "disk_full_test.tmp");
            try
            {
                // 获取当前可用空间
                long freeSpace = _fileSystem.GetDriveFreeSpace(targetDir);
                Log($"当前可用空间: {FormatFileSize(freeSpace)}", LogLevel.Info);

                // 计算需要填充的空间（保留少量空间用于测试）
                long spaceToFill = freeSpace - Config.TestDiskFullReserveBytes;

                if (spaceToFill > 0)
                {
                    Log($"将填充 {FormatFileSize(spaceToFill)} 空间，仅保留 {FormatFileSize(Config.TestDiskFullReserveBytes)}", LogLevel.Info);

                    // 创建大文件填充磁盘
                    using (var fs = new FileStream(tempFile, FileMode.Create))
                    {
                        byte[] buffer = new byte[1024 * 1024]; // 1MB缓冲区
                        new Random().NextBytes(buffer);

                        long bytesWritten = 0;
                        while (bytesWritten < spaceToFill)
                        {
                            int writeSize = (int)Math.Min(buffer.Length, spaceToFill - bytesWritten);
                            await fs.WriteAsync(buffer, 0, writeSize);
                            bytesWritten += writeSize;

                            // 每写入100MB更新一次进度
                            if (bytesWritten % (100 * 1024 * 1024) == 0)
                            {
                                Log($"已填充 {FormatFileSize(bytesWritten)} / {FormatFileSize(spaceToFill)}", LogLevel.Info);
                            }
                        }
                    }

                    Log("磁盘空间填充完成", LogLevel.Info);
                }
                else
                {
                    Log("可用空间已足够小，无需额外填充", LogLevel.Info);
                }
            }
            catch (Exception ex)
            {
                Log($"应用磁盘空间不足场景时出错: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 应用权限不足场景
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        static void ApplyPermissionDenied(string targetDir)
        {
            Log("应用权限不足场景...", LogLevel.Info);

            try
            {
                // 创建目标目录
                _fileSystem.CreateDirectory(targetDir);

                // 设置目录为只读
                _fileSystem.SetDirectoryPermissions(targetDir, true);
                Log($"已将目录 {targetDir} 设置为只读权限", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"应用权限不足场景时出错: {ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 获取异常场景的预期结果
        /// </summary>
        /// <returns>预期结果描述</returns>
        static string GetExpectedResultForErrorScenario()
        {
            switch (_testErrorScenario)
            {
                case Config.TestErrorFileCorruption:
                    return "更新程序应检测到ZIP包损坏并抛出异常，提示文件损坏";
                case Config.TestErrorDiskFull:
                    return "更新程序应检测到磁盘空间不足并抛出异常，提示磁盘已满";
                case Config.TestErrorPermissionDenied:
                    return "更新程序应检测到权限不足并抛出异常，提示无法写入文件";
                default:
                    return "未知异常场景的预期结果";
            }
        }

        /// <summary>
        /// 验证异常场景的结果
        /// </summary>
        /// <param name="threwException">是否抛出了异常</param>
        /// <param name="exception">异常对象</param>
        /// <returns>验证是否成功</returns>
        static bool VerifyErrorScenarioResult(bool threwException, Exception exception)
        {
            if (!threwException)
            {
                Log("异常场景验证失败：预期会抛出异常，但实际未抛出", LogLevel.Error);
                return false;
            }

            switch (_testErrorScenario)
            {
                case Config.TestErrorFileCorruption:
                    // 检查是否是ZIP损坏相关的异常
                    bool isZipError = exception is InvalidDataException ||
                                     (exception.InnerException != null && exception.InnerException is InvalidDataException) ||
                                     exception.Message.Contains("ZIP", StringComparison.OrdinalIgnoreCase) ||
                                     exception.Message.Contains("压缩", StringComparison.OrdinalIgnoreCase);

                    if (isZipError)
                    {
                        Log("异常场景验证通过：正确检测到ZIP包损坏", LogLevel.Info);
                        return true;
                    }
                    else
                    {
                        Log($"异常场景验证失败：抛出了非预期的异常类型 - {exception.GetType().Name}", LogLevel.Error);
                        return false;
                    }

                case Config.TestErrorDiskFull:
                    // 检查是否是磁盘空间不足相关的异常
                    bool isDiskError = exception is IOException &&
                                      (exception.Message.Contains("空间", StringComparison.OrdinalIgnoreCase) ||
                                       exception.Message.Contains("disk", StringComparison.OrdinalIgnoreCase) ||
                                       exception.Message.Contains("full", StringComparison.OrdinalIgnoreCase));

                    if (isDiskError)
                    {
                        Log("异常场景验证通过：正确检测到磁盘空间不足", LogLevel.Info);
                        return true;
                    }
                    else
                    {
                        Log($"异常场景验证失败：抛出了非预期的异常类型 - {exception.GetType().Name}", LogLevel.Error);
                        return false;
                    }

                case Config.TestErrorPermissionDenied:
                    // 检查是否是权限不足相关的异常
                    bool isPermissionError = exception is UnauthorizedAccessException ||
                                           (exception is IOException &&
                                            exception.Message.Contains("权限", StringComparison.OrdinalIgnoreCase)) ||
                                           (exception.Message.Contains("access", StringComparison.OrdinalIgnoreCase) &&
                                            exception.Message.Contains("denied", StringComparison.OrdinalIgnoreCase));

                    if (isPermissionError)
                    {
                        Log("异常场景验证通过：正确检测到权限不足", LogLevel.Info);
                        return true;
                    }
                    else
                    {
                        Log($"异常场景验证失败：抛出了非预期的异常类型 - {exception.GetType().Name}", LogLevel.Error);
                        return false;
                    }

                default:
                    Log("未知异常场景的验证结果", LogLevel.Warning);
                    return false;
            }
        }

        /// <summary>
        /// 保存测试报告
        /// </summary>
        static void SaveTestReport()
        {
            try
            {
                string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.LogDirectoryName);
                _fileSystem.CreateDirectory(logDir);

                string reportPath = Path.Combine(logDir,
                    $"{Config.TestReportFileName.Replace(".txt", $"_{DateTime.Now:yyyyMMddHHmmss}.txt")}");

                // 使用重写的ToString()方法生成文本报告
                _fileSystem.WriteAllText(reportPath, _currentTestReport.ToString());

                Log($"测试报告已保存到: {reportPath}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"保存测试报告失败: {ex.Message}", LogLevel.Warning);
            }
        }

        /// <summary>
        /// 生成增量测试ZIP包
        /// </summary>
        /// <param name="zipPath">ZIP包路径</param>
        /// <param name="tempDir">临时目录</param>
        static void GenerateIncrementalTestZip(string zipPath, string tempDir)
        {
            _testBaseDir = Path.Combine(tempDir, "base");
            _fileSystem.CreateDirectory(_testBaseDir);

            _fileSystem.WriteAllText(Path.Combine(_testBaseDir, "file1.txt"), "初始内容");
            _fileSystem.WriteAllText(Path.Combine(_testBaseDir, "file2.txt"), "需要删除的文件");
            _fileSystem.CreateDirectory(Path.Combine(_testBaseDir, "sub"));
            _fileSystem.WriteAllText(Path.Combine(_testBaseDir, "sub", "file3.txt"), "子目录待删除文件");
            _fileSystem.WriteAllText(Path.Combine(_testBaseDir, "file4.txt"), "保持不变的文件");

            string incrementalSource = Path.Combine(tempDir, "incremental_source");
            _fileSystem.CreateDirectory(incrementalSource);

            _fileSystem.WriteAllText(Path.Combine(incrementalSource, "file1.txt"), "更新后的内容");
            _fileSystem.WriteAllText(Path.Combine(incrementalSource, "newfile.txt"), "这是新文件");
            _fileSystem.CreateDirectory(Path.Combine(incrementalSource, "newsub"));
            _fileSystem.WriteAllText(Path.Combine(incrementalSource, "newsub", "newsubfile.txt"), "新子目录文件");
            _fileSystem.WriteAllLines(Path.Combine(incrementalSource, Config.DeleteListFileName), new[] {
                "file2.txt",
                "sub/file3.txt"
            }, Encoding.UTF8);

            ZipFile.CreateFromDirectory(incrementalSource, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            Log($"生成增量测试包：{zipPath}（包含更新文件和删除列表）", LogLevel.Info);
        }

        /// <summary>
        /// 生成基础测试ZIP包
        /// </summary>
        /// <param name="zipPath">ZIP包路径</param>
        static void GenerateBasicTestZip(string zipPath)
        {
            string tempSource = Path.Combine(Path.GetTempPath(), "UpdaterTest_Source");
            _fileSystem.CreateDirectory(tempSource);

            try
            {
                _fileSystem.WriteAllText(Path.Combine(tempSource, "readme.txt"), "This is a test file.\nLine 2\nLine 3");
                byte[] randomData = new byte[1024 * 5];
                new Random().NextBytes(randomData);
                File.WriteAllBytes(Path.Combine(tempSource, "data.bin"), randomData);
                _fileSystem.WriteAllText(Path.Combine(tempSource, "测试文档.txt"), "中文路径测试");

                string subDir = Path.Combine(tempSource, "subfolder");
                _fileSystem.CreateDirectory(subDir);
                _fileSystem.WriteAllText(Path.Combine(subDir, "subfile.ini"), "[Section1]\nKey1=Value1");

                ZipFile.CreateFromDirectory(tempSource, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            finally
            {
                if (_fileSystem.DirectoryExists(tempSource))
                    _fileSystem.DeleteDirectory(tempSource, recursive: true);
            }
        }

        /// <summary>
        /// 生成性能测试ZIP包
        /// </summary>
        /// <param name="zipPath">ZIP包路径</param>
        static void GeneratePerformanceTestZip(string zipPath)
        {
            string tempSource = Path.Combine(Path.GetTempPath(), "UpdaterPerformanceTest_Source");
            _fileSystem.CreateDirectory(tempSource);
            var random = new Random();

            try
            {
                // 使用配置类获取性能测试参数
                if (!_performanceTestScale.HasValue ||
                    !Config.PerformanceScales.TryGetValue(_performanceTestScale.Value, out var config))
                {
                    config = Config.PerformanceScales[1]; // 默认使用级别1
                }

                int largeFileCount = config.LargeFileCount;
                int largeFileSizeMB = config.LargeFileSizeMB;
                int smallFileCount = config.SmallFileCount;
                int dirDepth = config.DirDepth;

                Log($"性能测试配置：{largeFileCount}个{largeFileSizeMB}MB大文件 + {smallFileCount}个小文件 + {dirDepth}层目录", LogLevel.Info);

                for (int i = 0; i < largeFileCount; i++)
                {
                    string fileName = $"large_file_{i + 1}.bin";
                    string filePath = Path.Combine(tempSource, fileName);

                    using (var fs = new FileStream(filePath, FileMode.Create))
                    {
                        byte[] buffer = new byte[1024 * 1024];
                        long totalBytes = 0;
                        long targetBytes = (long)largeFileSizeMB * 1024 * 1024;

                        while (totalBytes < targetBytes)
                        {
                            int writeSize = (int)Math.Min(buffer.Length, targetBytes - totalBytes);
                            random.NextBytes(buffer);
                            fs.Write(buffer, 0, writeSize);
                            totalBytes += writeSize;
                        }
                    }
                    Log($"生成大文件：{fileName}（{largeFileSizeMB}MB）", LogLevel.Info);
                }

                for (int i = 0; i < smallFileCount; i++)
                {
                    string dirPath = tempSource;
                    for (int d = 0; d < random.Next(1, dirDepth + 1); d++)
                    {
                        dirPath = Path.Combine(dirPath, $"dir_{random.Next(1, 10)}");
                        _fileSystem.CreateDirectory(dirPath);
                    }

                    string fileName = i % 2 == 0
                        ? $"small_text_{i}.txt"
                        : $"small_bin_{i}.dat";

                    string filePath = Path.Combine(dirPath, fileName);

                    if (i % 2 == 0)
                    {
                        string content = i % 4 == 0
                            ? $"This is a small text file. Test {i}\nLine 2\nLine 3"
                            : $"这是一个小文本文件，测试编号 {i}\n第二行\n第三行";
                        _fileSystem.WriteAllText(filePath, content);
                    }
                    else
                    {
                        byte[] data = new byte[random.Next(1024, 10 * 1024)];
                        random.NextBytes(data);
                        File.WriteAllBytes(filePath, data);
                    }

                    if (i % 100 == 0)
                        Log($"已生成{smallFileCount}个小文件中的 {i + 1} 个", LogLevel.Info);
                }

                string specialDir = Path.Combine(tempSource, "special_chars");
                _fileSystem.CreateDirectory(specialDir);

                string longFileName = new string('a', 190) + ".txt";
                _fileSystem.WriteAllText(Path.Combine(specialDir, longFileName), "长文件名测试");

                string specialChars = "!@#$%^&*()_+-=~`[]{};',.";
                string rawFileName = $"file_with_{specialChars}.txt";
                string safeFileName = SanitizeFileName(rawFileName);
                _fileSystem.WriteAllText(Path.Combine(specialDir, safeFileName), "特殊字符文件名测试");

                Log("统计文件总数和大小...", LogLevel.Info);
                var allFiles = _fileSystem.EnumerateFiles(tempSource, "*.*", SearchOption.AllDirectories)
                    .Select(path => _fileSystem.GetFileInfo(path))
                    .ToList();

                int totalFilesToZip = allFiles.Count;
                long totalSizeBytes = allFiles.Sum(fi => fi.Length);
                Log($"准备压缩 {totalFilesToZip} 个文件（总大小：{FormatFileSize(totalSizeBytes)}）...", LogLevel.Info);

                Log("开始创建测试ZIP包...", LogLevel.Info);
                var zipStopwatch = Stopwatch.StartNew();
                int filesProcessed = 0;
                long bytesProcessed = 0;
                double lastProgressPercent = 0;

                using (var zipStream = new FileStream(zipPath, FileMode.Create))
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
                {
                    foreach (var fileInfo in allFiles)
                    {
                        string relativePath = Path.GetRelativePath(tempSource, fileInfo.FullName);
                        var entry = archive.CreateEntry(relativePath, CompressionLevel.Fastest);

                        using (var entryStream = entry.Open())
                        using (var fileStream = _fileSystem.OpenRead(fileInfo.FullName))
                        {
                            fileStream.CopyTo(entryStream);
                        }

                        filesProcessed++;
                        bytesProcessed += fileInfo.Length;

                        double currentProgressPercent = (double)bytesProcessed / totalSizeBytes * 100;
                        if (currentProgressPercent - lastProgressPercent >= Config.ProgressUpdateThreshold || filesProcessed == totalFilesToZip)
                        {
                            Log($"ZIP压缩进度：{currentProgressPercent:F1}% " +
                                $"（{FormatFileSize(bytesProcessed)}/{FormatFileSize(totalSizeBytes)}，" +
                                $"{filesProcessed}/{totalFilesToZip}个文件）", LogLevel.Info);
                            lastProgressPercent = currentProgressPercent;
                        }
                    }
                }

                zipStopwatch.Stop();
                Log($"ZIP包创建完成，耗时：{zipStopwatch.Elapsed.TotalSeconds:F2}秒", LogLevel.Info);
            }
            finally
            {
                if (_fileSystem.DirectoryExists(tempSource))
                    _fileSystem.DeleteDirectory(tempSource, recursive: true);
            }
        }

        /// <summary>
        /// 验证提取的文件
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        /// <param name="isPerformanceTest">是否为性能测试</param>
        /// <returns>验证是否成功</returns>
        static bool VerifyExtractedFiles(string targetDir, bool isPerformanceTest)
        {
            bool allSuccess = true;

            if (!isPerformanceTest)
            {
                if (_testUpdateType == Config.UpdateTypeIncremental)
                {
                    allSuccess &= VerifyIncrementalUpdate(targetDir);
                }
                else
                {
                    allSuccess &= VerifyBasicFiles(targetDir);
                }
            }
            else
            {
                allSuccess &= VerifyPerformanceTestFiles(targetDir);
            }

            return allSuccess;
        }

        /// <summary>
        /// 验证增量更新
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        /// <returns>验证是否成功</returns>
        static bool VerifyIncrementalUpdate(string targetDir)
        {
            bool allSuccess = true;

            string file1 = Path.Combine(targetDir, "file1.txt");
            if (_fileSystem.FileExists(file1) && _fileSystem.ReadAllLines(file1)[0] == "更新后的内容")
                Log("验证通过：file1.txt已更新", LogLevel.Info);
            else
            {
                Log("验证失败：file1.txt未正确更新", LogLevel.Error);
                allSuccess = false;
            }

            string newFile = Path.Combine(targetDir, "newfile.txt");
            if (_fileSystem.FileExists(newFile))
                Log("验证通过：newfile.txt已添加", LogLevel.Info);
            else
            {
                Log("验证失败：newfile.txt未添加", LogLevel.Error);
                allSuccess = false;
            }

            string newSubFile = Path.Combine(targetDir, "newsub", "newsubfile.txt");
            if (_fileSystem.FileExists(newSubFile))
                Log("验证通过：newsub/newsubfile.txt已添加", LogLevel.Info);
            else
            {
                Log("验证失败：newsub/newsubfile.txt未添加", LogLevel.Error);
                allSuccess = false;
            }

            string file2 = Path.Combine(targetDir, "file2.txt");
            if (!_fileSystem.FileExists(file2))
                Log("验证通过：file2.txt已删除", LogLevel.Info);
            else
            {
                Log("验证失败：file2.txt未被删除", LogLevel.Error);
                allSuccess = false;
            }

            string file3 = Path.Combine(targetDir, "sub", "file3.txt");
            if (!_fileSystem.FileExists(file3))
                Log("验证通过：sub/file3.txt已删除", LogLevel.Info);
            else
            {
                Log("验证失败：sub/file3.txt未被删除", LogLevel.Error);
                allSuccess = false;
            }

            string file4 = Path.Combine(targetDir, "file4.txt");
            if (_fileSystem.FileExists(file4) && _fileSystem.ReadAllLines(file4)[0] == "保持不变的文件")
                Log("验证通过：file4.txt保持不变", LogLevel.Info);
            else
            {
                Log("验证失败：file4.txt被修改或删除", LogLevel.Error);
                allSuccess = false;
            }

            return allSuccess;
        }

        /// <summary>
        /// 验证基础文件
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        /// <returns>验证是否成功</returns>
        static bool VerifyBasicFiles(string targetDir)
        {
            bool allSuccess = true;

            string readmePath = Path.Combine(targetDir, "readme.txt");
            if (_fileSystem.FileExists(readmePath) && _fileSystem.ReadAllLines(readmePath)[1] == "Line 2")
                Log("验证通过：readme.txt", LogLevel.Info);
            else
            {
                Log("验证失败：readme.txt", LogLevel.Error);
                allSuccess = false;
            }

            string dataPath = Path.Combine(targetDir, "data.bin");
            if (_fileSystem.FileExists(dataPath) && _fileSystem.GetFileInfo(dataPath).Length == 1024 * 5)
                Log("验证通过：data.bin", LogLevel.Info);
            else
            {
                Log("验证失败：data.bin", LogLevel.Error);
                allSuccess = false;
            }

            string chineseFile = Path.Combine(targetDir, "测试文档.txt");
            if (_fileSystem.FileExists(chineseFile))
                Log("验证通过：测试文档.txt", LogLevel.Info);
            else
            {
                Log("验证失败：测试文档.txt", LogLevel.Error);
                allSuccess = false;
            }

            string subFile = Path.Combine(targetDir, "subfolder", "subfile.ini");
            if (_fileSystem.FileExists(subFile))
                Log("验证通过：subfolder/subfile.ini", LogLevel.Info);
            else
            {
                Log("验证失败：subfolder/subfile.ini", LogLevel.Error);
                allSuccess = false;
            }

            return allSuccess;
        }

        /// <summary>
        /// 验证性能测试文件
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        /// <returns>验证是否成功</returns>
        static bool VerifyPerformanceTestFiles(string targetDir)
        {
            bool allSuccess = true;

            if (_performanceTestScale.HasValue &&
                Config.PerformanceScales.TryGetValue(_performanceTestScale.Value, out var config))
            {
                for (int i = 0; i < config.LargeFileCount; i++)
                {
                    string largeFilePath = Path.Combine(targetDir, $"large_file_{i + 1}.bin");
                    if (_fileSystem.FileExists(largeFilePath))
                    {
                        var fileInfo = _fileSystem.GetFileInfo(largeFilePath);
                        long expectedSize = (long)config.LargeFileSizeMB * 1024 * 1024;
                        // 允许1%的误差
                        if (fileInfo.Length >= expectedSize * 0.99 && fileInfo.Length <= expectedSize * 1.01)
                            Log($"验证通过：{largeFilePath}（大小：{FormatFileSize(fileInfo.Length)}）", LogLevel.Info);
                        else
                        {
                            Log($"验证失败：{largeFilePath}（文件大小不符合预期，实际：{FormatFileSize(fileInfo.Length)}，预期：{FormatFileSize(expectedSize)}）", LogLevel.Error);
                            allSuccess = false;
                        }
                    }
                    else
                    {
                        Log($"验证失败：未找到 {largeFilePath}", LogLevel.Error);
                        allSuccess = false;
                    }
                }
            }

            string specialDir = Path.Combine(targetDir, "special_chars");
            if (_fileSystem.DirectoryExists(specialDir))
            {
                string longFileName = new string('a', 190) + ".txt";
                if (_fileSystem.FileExists(Path.Combine(specialDir, longFileName)))
                    Log("验证通过：长文件名文件", LogLevel.Info);
                else
                {
                    Log("验证失败：长文件名文件", LogLevel.Error);
                    allSuccess = false;
                }
            }
            else
            {
                Log("验证失败：未找到特殊字符目录", LogLevel.Error);
                allSuccess = false;
            }

            var fileCount = _fileSystem.EnumerateFiles(targetDir, "*.*", SearchOption.AllDirectories).Count();
            Log($"解压后总文件数：{fileCount}", LogLevel.Info);

            return allSuccess;
        }

        /// <summary>
        /// 格式化文件大小
        /// </summary>
        /// <param name="bytes">字节数</param>
        /// <returns>格式化后的文件大小字符串</returns>
        static string FormatFileSize(long bytes)
        {
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1}MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1}GB";
        }

        /// <summary>
        /// 清理文件名中的非法字符
        /// </summary>
        /// <param name="fileName">原始文件名</param>
        /// <returns>清理后的文件名</returns>
        static string SanitizeFileName(string fileName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
        #endregion

        #region 核心更新功能
        /// <summary>
        /// 处理ZIP更新
        /// </summary>
        /// <param name="zipPath">ZIP文件路径</param>
        /// <param name="targetDir">目标目录</param>
        /// <returns>异步任务</returns>
        static async Task HandleZipUpdate(string zipPath, string targetDir)
        {
            using (var performanceMonitor = new PerformanceMonitor("处理ZIP更新"))
            {
                Encoding encoding = await DetectZipEncoding(zipPath);
                Log($"检测到ZIP编码：{encoding.EncodingName}", LogLevel.Info);

                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, encoding))
                {
                    int totalFiles = archive.Entries.Count;
                    int processedFiles = 0;
                    var failedFiles = new List<string>();

                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name)) continue;

                        int retryCount = 0;
                        bool success = false;

                        while (retryCount < Config.MaxRetryCount && !success)
                        {
                            try
                            {
                                string destPath = GetSafePath(targetDir, entry.FullName);
                                Log($"解压：{destPath}", LogLevel.Debug);

                                _fileSystem.CreateDirectory(Path.GetDirectoryName(destPath));
                                entry.ExtractToFile(destPath, overwrite: true);

                                success = true;
                                UpdateProgress(++processedFiles, totalFiles);
                            }
                            catch (IOException ex) when (IsFileLocked(ex))
                            {
                                retryCount++;
                                Log($"文件被占用（{retryCount}/{Config.MaxRetryCount}）：{entry.FullName}", LogLevel.Warning);
                                await Task.Delay(Config.RetryDelayMs);
                            }
                            catch (Exception ex)
                            {
                                Log($"解压失败：{ex.Message}", LogLevel.Error);
                                failedFiles.Add(entry.FullName);
                                break;
                            }
                        }
                    }

                    if (failedFiles.Count > 0)
                    {
                        Log($"解压完成：成功{processedFiles}个，失败{failedFiles.Count}个，总计{totalFiles}个", LogLevel.Info);
                        Log("失败文件列表：", LogLevel.Info);
                        foreach (var file in failedFiles)
                            Log($"  - {file}", LogLevel.Info);

                        if (failedFiles.Count > totalFiles / 2) // 如果失败文件超过一半，视为严重错误
                        {
                            throw new Exception($"解压失败率过高，共{failedFiles.Count}个文件解压失败");
                        }
                    }
                    else
                    {
                        Log($"解压完成：全部{totalFiles}个文件成功", LogLevel.Info);
                    }
                }
            }
        }

        /// <summary>
        /// 处理增量包更新
        /// </summary>
        /// <param name="zipPath">ZIP文件路径</param>
        /// <param name="targetDir">目标目录</param>
        /// <returns>异步任务</returns>
        static async Task HandleIncrementalUpdate(string zipPath, string targetDir)
        {
            using (var performanceMonitor = new PerformanceMonitor("处理增量包更新"))
            {
                Encoding encoding = await DetectZipEncoding(zipPath);
                Log($"检测到增量包编码：{encoding.EncodingName}", LogLevel.Info);

                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, encoding))
                {
                    var deleteListEntry = archive.Entries.FirstOrDefault(e => e.Name == Config.DeleteListFileName);
                    if (deleteListEntry != null)
                    {
                        Log("找到删除列表文件，处理需要删除的文件...", LogLevel.Info);
                        await ProcessDeleteList(deleteListEntry, targetDir);
                    }
                    else
                    {
                        Log("未找到删除列表文件，跳过删除步骤", LogLevel.Info);
                    }

                    int totalFiles = archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name) && e.Name != Config.DeleteListFileName);
                    int processedFiles = 0;
                    var failedFiles = new List<string>();

                    foreach (var entry in archive.Entries)
                    {
                        if (string.IsNullOrEmpty(entry.Name) || entry.Name == Config.DeleteListFileName) continue;

                        int retryCount = 0;
                        bool success = false;

                        while (retryCount < Config.MaxRetryCount && !success)
                        {
                            try
                            {
                                string destPath = GetSafePath(targetDir, entry.FullName);
                                Log($"解压：{destPath}", LogLevel.Debug);

                                _fileSystem.CreateDirectory(Path.GetDirectoryName(destPath));
                                entry.ExtractToFile(destPath, overwrite: true);

                                success = true;
                                UpdateProgress(++processedFiles, totalFiles);
                            }
                            catch (IOException ex) when (IsFileLocked(ex))
                            {
                                retryCount++;
                                Log($"文件被占用（{retryCount}/{Config.MaxRetryCount}）：{entry.FullName}", LogLevel.Warning);
                                await Task.Delay(Config.RetryDelayMs);
                            }
                            catch (Exception ex)
                            {
                                Log($"解压失败：{ex.Message}", LogLevel.Error);
                                failedFiles.Add(entry.FullName);
                                break;
                            }
                        }
                    }

                    if (failedFiles.Count > 0)
                    {
                        Log($"增量更新完成：成功{processedFiles}个，失败{failedFiles.Count}个，总计{totalFiles}个", LogLevel.Info);
                        Log("失败文件列表：", LogLevel.Info);
                        foreach (var file in failedFiles)
                            Log($"  - {file}", LogLevel.Info);

                        if (failedFiles.Count > totalFiles / 2) // 如果失败文件超过一半，视为严重错误
                        {
                            throw new Exception($"增量更新失败率过高，共{failedFiles.Count}个文件更新失败");
                        }
                    }
                    else
                    {
                        Log($"增量更新完成：全部{totalFiles}个文件成功", LogLevel.Info);
                    }
                }
            }
        }

        /// <summary>
        /// 处理删除列表
        /// </summary>
        /// <param name="deleteListEntry">删除列表ZIP条目</param>
        /// <param name="targetDir">目标目录</param>
        /// <returns>异步任务</returns>
        static async Task ProcessDeleteList(ZipArchiveEntry deleteListEntry, string targetDir)
        {
            try
            {
                using (var stream = deleteListEntry.Open())
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    int deletedCount = 0;
                    int failedCount = 0;

                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        string filePath = GetSafePath(targetDir, line.Trim());
                        if (_fileSystem.FileExists(filePath))
                        {
                            try
                            {
                                _fileSystem.DeleteFile(filePath);
                                Log($"已删除文件：{line}", LogLevel.Info);
                                deletedCount++;
                            }
                            catch (Exception ex)
                            {
                                Log($"删除文件失败：{line} - {ex.Message}", LogLevel.Error);
                                failedCount++;
                            }
                        }
                        else
                        {
                            Log($"文件不存在，无需删除：{line}", LogLevel.Info);
                        }
                    }

                    Log($"删除操作完成：成功{deletedCount}个，失败{failedCount}个", LogLevel.Info);

                    if (failedCount > 0 && failedCount > deletedCount) // 如果失败数超过成功数，视为严重错误
                    {
                        throw new Exception($"删除文件失败率过高，共{failedCount}个文件删除失败");
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"处理删除列表时出错：{ex.Message}", LogLevel.Error);
                throw;
            }
        }

        /// <summary>
        /// 检测ZIP文件编码
        /// </summary>
        /// <param name="zipPath">ZIP文件路径</param>
        /// <returns>检测到的编码</returns>
        static async Task<Encoding> DetectZipEncoding(string zipPath)
        {
            return await Task.Run(() =>
            {
                try
                {
                    byte[] buffer = new byte[8192];
                    using (var fs = _fileSystem.OpenRead(zipPath))
                    {
                        int read = fs.Read(buffer, 0, buffer.Length);
                        var detectionResult = CharsetDetector.DetectFromBytes(buffer.AsSpan(0, read).ToArray());
                        return detectionResult.Detected?.Encoding ?? Config.DefaultEncoding;
                    }
                }
                catch
                {
                    return Config.DefaultEncoding;
                }
            });
        }

        /// <summary>
        /// 处理安装包更新
        /// </summary>
        /// <param name="installerPath">安装包路径</param>
        /// <param name="targetDir">目标目录</param>
        /// <returns>异步任务</returns>
        static async Task HandleInstallerUpdate(string installerPath, string targetDir)
        {
            // 参数安全：如果传进来的是 .zip 文件，自动转为 ZIP 更新处理
            if (Path.GetExtension(installerPath)?.Equals(".zip", StringComparison.OrdinalIgnoreCase) == true)
            {
                Log($"检测到安装包为ZIP格式，自动切换为ZIP更新处理：{installerPath}", LogLevel.Warning);
                await HandleZipUpdate(installerPath, targetDir);
                return;
            }

            using (var performanceMonitor = new PerformanceMonitor("处理安装包更新"))
            {
                using (var process = new Process())
                {
                    // 检查是否需要管理员权限
                    bool isAdminRequired = IsAdminRequiredForPath(targetDir);

                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = installerPath,
                        WorkingDirectory = targetDir,
                        UseShellExecute = true,
                        Verb = isAdminRequired ? "runas" : null
                    };

                    try
                    {
                        process.Start();
                        Log($"安装包已启动：{installerPath}，{(isAdminRequired ? "已请求管理员权限" : "无需管理员权限")}", LogLevel.Info);
                        Log("等待安装完成...", LogLevel.Info);

                        await process.WaitForExitAsync();

                        if (process.ExitCode == 0)
                        {
                            Log("安装包执行成功", LogLevel.Info);
                        }
                        else
                        {
                            Log($"警告：安装包退出码非0，退出码：{process.ExitCode}", LogLevel.Warning);
                            throw new Exception($"安装包执行失败，退出码：{process.ExitCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Log($"启动安装包失败：{ex.Message}", LogLevel.Error);
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// 判断路径是否需要管理员权限
        /// </summary>
        /// <param name="path">路径</param>
        /// <returns>是否需要管理员权限</returns>
        static bool IsAdminRequiredForPath(string path)
        {
            try
            {
                // 系统目录需要管理员权限
                string[] systemPaths = {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32")
                };

                return systemPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
            }
            catch
            {
                return false;
            }
        }
        #endregion

        #region 辅助功能
        /// <summary>
        /// 获取安全路径（防止路径遍历攻击）
        /// </summary>
        /// <param name="baseDir">基础目录</param>
        /// <param name="relativePath">相对路径</param>
        /// <returns>安全路径</returns>
        static string GetSafePath(string baseDir, string relativePath)
        {
            string fullPath = Path.GetFullPath(Path.Combine(baseDir, relativePath));
            if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("非法路径访问尝试：" + fullPath);
            return fullPath;
        }

        /// <summary>
        /// 启动主应用程序
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        /// <param name="mainAppExe">主程序文件名</param>
        static void LaunchMainApplication(string targetDir, string mainAppExe)
        {
            string exePath = Path.Combine(targetDir, mainAppExe);
            if (!_fileSystem.FileExists(exePath)) return;

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = exePath,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true
                };

                startInfo.ArgumentList.Add("updated");
                startInfo.ArgumentList.Add(DateTime.Now.ToString(Config.DateTimeFormat));

                Process.Start(startInfo);
                Log($"已启动主应用程序：{exePath}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"启动主程序失败：{ex.Message}", LogLevel.Error);
            }
        }

        /// <summary>
        /// 记录更新完成时间
        /// </summary>
        /// <param name="targetDir">目标目录</param>
        private static void RecordUpdateCompletionTime(string targetDir)
        {
            try
            {
                string timeFile = Path.Combine(targetDir, Config.UpdateCompletionFileName);
                _fileSystem.WriteAllText(timeFile, DateTime.Now.ToString(Config.IsoDateTimeFormat));
                Log($"已记录更新时间到：{timeFile}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"记录更新时间失败：{ex.Message}", LogLevel.Warning);
            }
        }

        /// <summary>
        /// 删除安装包
        /// </summary>
        /// <param name="packagePath">安装包路径</param>
        private static void DeletePackage(string packagePath)
        {
            for (int i = 0; i < Config.MaxRetryCount; i++)
            {
                try
                {
                    if (_fileSystem.FileExists(packagePath))
                    {
                        _fileSystem.DeleteFile(packagePath);
                        Log($"已删除安装包：{packagePath}", LogLevel.Info);
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Log($"删除安装包失败（{i + 1}/{Config.MaxRetryCount}）：{ex.Message}", LogLevel.Warning);
                    if (i < Config.MaxRetryCount - 1) Task.Delay(Config.RetryDelayMs).Wait();
                }
            }
        }

        /// <summary>
        /// 初始化日志系统
        /// </summary>
        static void InitializeLogging()
        {
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Config.LogDirectoryName);
            _fileSystem.CreateDirectory(logDir);
            string logFileName = $"{Config.LogFilePrefix}{DateTime.Now.ToString(Config.DateFormat)}_{GetLogFileIndex(logDir)}.log";
            string logPath = Path.Combine(logDir, logFileName);
            _logWriter = new StreamWriter(logPath, true, Config.DefaultEncoding) { AutoFlush = true };
        }

        /// <summary>
        /// 获取日志文件索引
        /// </summary>
        /// <param name="logDir">日志目录</param>
        /// <returns>日志文件索引</returns>
        static int GetLogFileIndex(string logDir)
        {
            string prefix = $"{Config.LogFilePrefix}{DateTime.Now.ToString(Config.DateFormat)}_";
            int index = 1;
            while (_fileSystem.FileExists(Path.Combine(logDir, $"{prefix}{index}.log")))
            {
                index++;
            }
            return index;
        }

        /// <summary>
        /// 更新进度显示
        /// </summary>
        /// <param name="current">当前进度</param>
        /// <param name="total">总进度</param>
        private static void UpdateProgress(int current, int total)
        {
            int windowWidth = Console.WindowWidth > 0 ? Console.WindowWidth : 120;
            double percent = (double)current / total * 100;
            int progressBars = (int)(percent / 2);

            Console.Write("\r" + new string(' ', windowWidth - 1) + "\r");
            Console.Write("[");
            Console.Write(new string('=', progressBars));
            Console.Write(new string(' ', 50 - progressBars));
            Console.Write("] ");

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write($"{percent:F1}%");
            Console.ResetColor();

            Console.Write($" （{current}/{total}）");

            if (current > 1 && _stopwatch.IsRunning)
            {
                double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
                double filesPerSecond = current / elapsedSeconds;
                double estimatedRemainingSeconds = (total - current) / filesPerSecond;

                Console.Write($" | 速度：{filesPerSecond:F1}文件/秒");
                Console.Write($" | 剩余：{FormatTime(estimatedRemainingSeconds)}");
            }

            if (current == total)
                Console.WriteLine();
        }

        /// <summary>
        /// 格式化时间
        /// </summary>
        /// <param name="seconds">秒数</param>
        /// <returns>格式化后的时间字符串</returns>
        private static string FormatTime(double seconds)
        {
            if (seconds < 60)
                return $"{(int)seconds}秒";
            else if (seconds < 3600)
                return $"{seconds / 60:F1}分钟";
            else
                return $"{seconds / 3600:F1}小时";
        }

        /// <summary>
        /// 优雅关闭程序
        /// </summary>
        /// <returns>异步任务</returns>
        static async Task GracefulShutdown()
        {
            Log("更新操作已完成", LogLevel.Info);

            int waitSeconds = _isTestMode ? Config.TestModeShutdownWaitSeconds : Config.GracefulShutdownWaitSeconds;
            Log($"{waitSeconds}秒后自动退出...", LogLevel.Info);

            for (int i = waitSeconds; i > 0; i--)
            {
                Log($"剩余 {i} 秒...", LogLevel.Info);
                await Task.Delay(1000);
            }

            Log("程序已退出", LogLevel.Info);
            Environment.Exit(0);
        }

        /// <summary>
        /// 等待用户按键退出
        /// </summary>
        static void WaitForExit()
        {
            Log("按任意键退出...", LogLevel.Info);
            Console.ReadKey();
            Environment.Exit(1);
        }

        /// <summary>
        /// 记录日志
        /// </summary>
        /// <param name="message">日志消息</param>
        /// <param name="level">日志级别</param>
        /// <param name="caller">调用方法名</param>
        public static void Log(string message, LogLevel level = LogLevel.Info, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
        {
            string logMessage = $"[{DateTime.Now.ToString(Config.DateTimeFormat)}] [{level}] [{caller}] {message}";

            ConsoleColor originalColor = Console.ForegroundColor;
            try
            {
                switch (level)
                {
                    case LogLevel.Warning:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        break;
                    case LogLevel.Error:
                        Console.ForegroundColor = ConsoleColor.Red;
                        break;
                    case LogLevel.Debug:
                        Console.ForegroundColor = ConsoleColor.Gray;
                        break;
                }

                Console.WriteLine(logMessage);
            }
            finally
            {
                Console.ForegroundColor = originalColor;
            }

            try
            {
                _logWriter?.WriteLine(logMessage);
            }
            catch (ObjectDisposedException)
            {
                // 忽略日志写入器已释放的异常
            }
        }

        /// <summary>
        /// 检查文件是否被占用
        /// </summary>
        /// <param name="ex">IO异常</param>
        /// <returns>是否文件被占用</returns>
        private static bool IsFileLocked(IOException ex)
        {
            int errorCode = Marshal.GetHRForException(ex) & ((1 << 16) - 1);
            return errorCode == 32 || errorCode == 33;
        }
        #endregion
    }
}