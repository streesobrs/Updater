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

namespace Updater
{
    class Program
    {
        private static StreamWriter _logWriter;
        private static Stopwatch stopwatch;
        private static bool _isTestMode = false;
        private static int? _performanceTestScale = null;
        private static string _testUpdateType = "zip"; // 新增：测试的更新类型
        private static string _testBaseDir; // 新增：存储基础目录路径（模拟已安装文件）
        // 新增：标记是否需要显示帮助
        private static bool _needShowHelp = false;

        static void Main(string[] args)
        {
            // 优先检查帮助命令（--help/-h），优先级最高
            if (args.Contains("--help", StringComparer.OrdinalIgnoreCase) ||
                args.Contains("-h", StringComparer.OrdinalIgnoreCase))
            {
                _needShowHelp = true;
            }
            else
            {
                ParseTestModeArgs(args);
            }

            MainAsync(args).GetAwaiter().GetResult();
        }

        static void ParseTestModeArgs(string[] args)
        {
            if (args.Length > 0 && args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                _isTestMode = true;

                // 解析测试类型（默认zip，支持incremental）
                if (args.Length > 1 && (args[1].Equals("zip", StringComparison.OrdinalIgnoreCase) ||
                                       args[1].Equals("incremental", StringComparison.OrdinalIgnoreCase)))
                {
                    _testUpdateType = args[1].ToLower();
                    // 解析性能测试级别（1-5，数字越大测试强度越高）
                    if (args.Length > 2 && int.TryParse(args[2], out int scale))
                    {
                        _performanceTestScale = Math.Clamp(scale, 1, 5);
                    }
                }
                else
                {
                    // 兼容原有逻辑（仅指定级别，默认测试类型为zip）
                    if (args.Length > 1 && int.TryParse(args[1], out int scale))
                    {
                        _performanceTestScale = Math.Clamp(scale, 1, 5);
                    }
                }
            }
        }

        // 新增：显示帮助信息
        static void ShowHelp()
        {
            var helpText = $@"
======================================= Updater 程序帮助文档 =======================================
程序功能：支持ZIP全量更新、安装包更新、增量包更新，以及对应的测试模式（基础测试/性能测试）

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
  Updater.exe --test [测试类型] [性能测试强度]

参数说明：
  1. --test          ：固定前缀，标记进入测试模式
  2. 测试类型（可选）：默认zip，支持2种类型（不区分大小写）
                       - zip       ：ZIP全量更新测试（自动生成文本/二进制测试文件）
                       - incremental：增量更新测试（自动生成基础目录+增量包+删除列表）
  3. 性能测试强度（可选）：1-5的整数（数字越大，生成的测试文件越多/越大）
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


【3. 帮助命令】
触发方式（任意一种即可）：
  Updater.exe --help
  Updater.exe -h

说明：显示此帮助文档，忽略其他所有参数
====================================================================================================
";
            Console.WriteLine(helpText);
            // 直接输出到控制台（无需日志，避免初始化日志文件）
        }

        static async Task MainAsync(string[] args)
        {
            // 优先处理帮助命令：显示帮助后直接退出
            if (_needShowHelp)
            {
                ShowHelp();
                return;
            }

            InitializeLogging();
            Log("===== 更新程序启动 =====");
            Log($"==== 版本号: {Assembly.GetExecutingAssembly().GetName().Version} ====");
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            try
            {
                stopwatch = Stopwatch.StartNew();

                if (_isTestMode)
                {
                    await RunExtractionTest();
                    return;
                }

                if (args.Length < 5)
                {
                    Log("参数错误！需要5个参数（顺序）：");
                    Log("<主程序文件名> <安装包路径> <目标目录> <是否删除安装包> <更新类型(zip/installer/incremental)>");
                    Log($"实际接收到的参数数量: {args.Length}");
                    if (args.Length > 0)
                    {
                        Log("接收到的参数列表:");
                        for (int i = 0; i < args.Length; i++)
                        {
                            Log($"  参数{i + 1}: {args[i]}");
                        }
                    }
                    Log("提示：输入 Updater.exe --help 查看详细使用说明");
                    WaitForExit();
                    return;
                }

                string mainAppExe = args[0].Trim('"');
                string packagePath = args[1];
                string targetDir = Path.GetFullPath(args[2].Trim('"'));
                bool deleteAfterUpdate = bool.TryParse(args[3], out bool result) ? result : true;
                string updateType = args[4].ToLower();

                if (!File.Exists(packagePath))
                {
                    Log($"错误：安装包不存在 - {packagePath}");
                    WaitForExit();
                    return;
                }

                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                    Log($"创建目标目录：{targetDir}");
                }

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
                else if (updateType == "incremental")
                {
                    Log("===== 开始处理增量包更新 =====");
                    await HandleIncrementalUpdate(packagePath, targetDir);
                }
                else
                {
                    Log($"错误：不支持的更新类型 - {updateType}");
                    Log("支持的更新类型：zip / installer / incremental");
                    WaitForExit();
                    return;
                }

                if (deleteAfterUpdate)
                {
                    DeletePackage(packagePath);
                }
                else
                {
                    Log("用户选择保留安装包，不执行删除");
                }

                RecordUpdateCompletionTime(targetDir);
                LaunchMainApplication(targetDir, mainAppExe);
            }
            catch (Exception ex)
            {
                Log($"致命错误：{ex}");
                Log("提示：输入 Updater.exe --help 查看正确使用方法");
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
                    _logWriter?.Dispose();
                }
            }
        }

        #region 测试功能（原有代码不变，此处省略以节省篇幅）
        static async Task RunExtractionTest()
        {
            // （原有逻辑不变，无需修改）
            bool isPerformanceTest = _performanceTestScale.HasValue;
            if (isPerformanceTest)
            {
                Log($"===== 测试模式（性能测试，强度：{_performanceTestScale.Value}，类型：{_testUpdateType}）====");
            }
            else
            {
                Log($"===== 测试模式（基础功能测试，类型：{_testUpdateType}）====");
            }

            string testZipPath = null;
            string testTargetDir = null;
            var testStopwatch = Stopwatch.StartNew();

            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "UpdaterTest");
                Directory.CreateDirectory(tempDir);
                Log($"创建临时测试目录：{tempDir}");

                testZipPath = Path.Combine(tempDir, "test_package.zip");

                // 根据测试类型生成对应包
                if (isPerformanceTest)
                {
                    GeneratePerformanceTestZip(testZipPath);
                    testTargetDir = Path.Combine(tempDir, "extracted");
                }
                else
                {
                    if (_testUpdateType == "incremental")
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

                var zipFileInfo = new FileInfo(testZipPath);
                Log($"生成测试包：{testZipPath}（大小：{FormatFileSize(zipFileInfo.Length)}）");

                Log("开始更新测试...");
                var extractionStopwatch = Stopwatch.StartNew();
                var memoryBefore = Process.GetCurrentProcess().WorkingSet64;

                // 根据测试类型调用对应更新方法
                if (_testUpdateType == "incremental")
                {
                    await HandleIncrementalUpdate(testZipPath, testTargetDir);
                }
                else
                {
                    await HandleZipUpdate(testZipPath, testTargetDir);
                }

                extractionStopwatch.Stop();
                var memoryAfter = Process.GetCurrentProcess().WorkingSet64;
                var memoryUsed = memoryAfter - memoryBefore;

                // 性能测试日志
                if (isPerformanceTest)
                {
                    Log("===== 性能指标 =====");
                    Log($"总时间：{extractionStopwatch.Elapsed.TotalSeconds:F2}秒");
                    Log($"平均速度：{FormatFileSize((long)(zipFileInfo.Length / extractionStopwatch.Elapsed.TotalSeconds))}/秒");
                    Log($"峰值内存占用：{FormatFileSize(memoryUsed)}");
                }

                // 验证结果
                Log("验证更新结果...");
                bool verifySuccess = VerifyExtractedFiles(testTargetDir, isPerformanceTest);
                if (verifySuccess)
                {
                    Log($"===== {_testUpdateType}测试成功！所有文件验证通过 =====");
                }
                else
                {
                    Log($"===== {_testUpdateType}测试失败！部分文件验证未通过 =====");
                }
            }
            catch (Exception ex)
            {
                Log($"测试过程发生错误：{ex}");
            }
            finally
            {
                testStopwatch.Stop();
                Log($"===== 测试总耗时：{testStopwatch.Elapsed.TotalSeconds:F2}秒 =====");

                // 清理目录（包含基础目录）
                if (!string.IsNullOrEmpty(_testBaseDir) && Directory.Exists(_testBaseDir))
                {
                    try
                    {
                        Directory.Delete(_testBaseDir, recursive: true);
                        Log($"已清理测试基础目录：{_testBaseDir}");
                    }
                    catch (Exception ex)
                    {
                        Log($"清理解压目录失败：{ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(testTargetDir) && Directory.Exists(testTargetDir) && _testUpdateType != "incremental")
                {
                    try
                    {
                        Directory.Delete(testTargetDir, recursive: true);
                        Log($"已清理测试解压目录：{testTargetDir}");
                    }
                    catch (Exception ex)
                    {
                        Log($"清理解压目录失败：{ex.Message}");
                    }
                }

                if (!string.IsNullOrEmpty(testZipPath) && File.Exists(testZipPath))
                {
                    try
                    {
                        File.Delete(testZipPath);
                        Log($"已删除测试包：{testZipPath}");
                    }
                    catch (Exception ex)
                    {
                        Log($"删除测试包失败：{ex.Message}");
                    }
                }
            }
        }

        static void GenerateIncrementalTestZip(string zipPath, string tempDir)
        {
            // （原有逻辑不变）
            _testBaseDir = Path.Combine(tempDir, "base");
            Directory.CreateDirectory(_testBaseDir);

            File.WriteAllText(Path.Combine(_testBaseDir, "file1.txt"), "初始内容");
            File.WriteAllText(Path.Combine(_testBaseDir, "file2.txt"), "需要删除的文件");
            Directory.CreateDirectory(Path.Combine(_testBaseDir, "sub"));
            File.WriteAllText(Path.Combine(_testBaseDir, "sub", "file3.txt"), "子目录待删除文件");
            File.WriteAllText(Path.Combine(_testBaseDir, "file4.txt"), "保持不变的文件");

            string incrementalSource = Path.Combine(tempDir, "incremental_source");
            Directory.CreateDirectory(incrementalSource);

            File.WriteAllText(Path.Combine(incrementalSource, "file1.txt"), "更新后的内容");
            File.WriteAllText(Path.Combine(incrementalSource, "newfile.txt"), "这是新文件");
            Directory.CreateDirectory(Path.Combine(incrementalSource, "newsub"));
            File.WriteAllText(Path.Combine(incrementalSource, "newsub", "newsubfile.txt"), "新子目录文件");
            File.WriteAllLines(Path.Combine(incrementalSource, "_delete_list.txt"), new[] {
                "file2.txt",
                "sub/file3.txt"
            }, Encoding.UTF8);

            ZipFile.CreateFromDirectory(incrementalSource, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            Log($"生成增量测试包：{zipPath}（包含更新文件和删除列表）");
        }

        static void GenerateBasicTestZip(string zipPath)
        {
            // （原有逻辑不变）
            string tempSource = Path.Combine(Path.GetTempPath(), "UpdaterTest_Source");
            Directory.CreateDirectory(tempSource);

            try
            {
                File.WriteAllText(Path.Combine(tempSource, "readme.txt"), "This is a test file.\nLine 2\nLine 3");
                byte[] randomData = new byte[1024 * 5];
                new Random().NextBytes(randomData);
                File.WriteAllBytes(Path.Combine(tempSource, "data.bin"), randomData);
                File.WriteAllText(Path.Combine(tempSource, "测试文档.txt"), "中文路径测试");

                string subDir = Path.Combine(tempSource, "subfolder");
                Directory.CreateDirectory(subDir);
                File.WriteAllText(Path.Combine(subDir, "subfile.ini"), "[Section1]\nKey1=Value1");

                ZipFile.CreateFromDirectory(tempSource, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
            }
            finally
            {
                if (Directory.Exists(tempSource))
                    Directory.Delete(tempSource, recursive: true);
            }
        }

        static void GeneratePerformanceTestZip(string zipPath)
        {
            // （原有逻辑不变）
            string tempSource = Path.Combine(Path.GetTempPath(), "UpdaterPerformanceTest_Source");
            Directory.CreateDirectory(tempSource);
            var random = new Random();

            try
            {
                (int largeFileCount, int largeFileSizeMB, int smallFileCount, int dirDepth) = _performanceTestScale.Value switch
                {
                    1 => (2, 10, 50, 3),
                    2 => (3, 20, 100, 4),
                    3 => (5, 30, 300, 5),
                    4 => (8, 50, 500, 6),
                    5 => (10, 100, 1000, 8),
                    _ => (2, 10, 50, 3)
                };

                Log($"性能测试配置：{largeFileCount}个{largeFileSizeMB}MB大文件 + {smallFileCount}个小文件 + {dirDepth}层目录");

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
                    Log($"生成大文件：{fileName}（{largeFileSizeMB}MB）");
                }

                for (int i = 0; i < smallFileCount; i++)
                {
                    string dirPath = tempSource;
                    for (int d = 0; d < random.Next(1, dirDepth + 1); d++)
                    {
                        dirPath = Path.Combine(dirPath, $"dir_{random.Next(1, 10)}");
                        Directory.CreateDirectory(dirPath);
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
                        File.WriteAllText(filePath, content);
                    }
                    else
                    {
                        byte[] data = new byte[random.Next(1024, 10 * 1024)];
                        random.NextBytes(data);
                        File.WriteAllBytes(filePath, data);
                    }

                    if (i % 100 == 0)
                        Log($"已生成{smallFileCount}个小文件中的 {i + 1} 个");
                }

                string specialDir = Path.Combine(tempSource, "special_chars");
                Directory.CreateDirectory(specialDir);

                string longFileName = new string('a', 190) + ".txt";
                File.WriteAllText(Path.Combine(specialDir, longFileName), "长文件名测试");

                string specialChars = "!@#$%^&*()_+-=~`[]{};',.";
                string rawFileName = $"file_with_{specialChars}.txt";
                string safeFileName = SanitizeFileName(rawFileName);
                File.WriteAllText(Path.Combine(specialDir, safeFileName), "特殊字符文件名测试");

                Log("统计文件总数和大小...");
                var allFiles = Directory.EnumerateFiles(tempSource, "*.*", SearchOption.AllDirectories)
                    .Select(path => new FileInfo(path))
                    .ToList();

                int totalFilesToZip = allFiles.Count;
                long totalSizeBytes = allFiles.Sum(fi => fi.Length);
                Log($"准备压缩 {totalFilesToZip} 个文件（总大小：{FormatFileSize(totalSizeBytes)}）...");

                Log("开始创建测试ZIP包...");
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
                        using (var fileStream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read))
                        {
                            fileStream.CopyTo(entryStream);
                        }

                        filesProcessed++;
                        bytesProcessed += fileInfo.Length;

                        double currentProgressPercent = (double)bytesProcessed / totalSizeBytes * 100;
                        if (currentProgressPercent - lastProgressPercent >= 1 || filesProcessed == totalFilesToZip)
                        {
                            Log($"ZIP压缩进度：{currentProgressPercent:F1}% " +
                                $"（{FormatFileSize(bytesProcessed)}/{FormatFileSize(totalSizeBytes)}，" +
                                $"{filesProcessed}/{totalFilesToZip}个文件）");
                            lastProgressPercent = currentProgressPercent;
                        }
                    }
                }

                zipStopwatch.Stop();
                Log($"ZIP包创建完成，耗时：{zipStopwatch.Elapsed.TotalSeconds:F2}秒");
            }
            finally
            {
                if (Directory.Exists(tempSource))
                    Directory.Delete(tempSource, recursive: true);
            }
        }

        static bool VerifyExtractedFiles(string targetDir, bool isPerformanceTest)
        {
            // （原有逻辑不变）
            bool allSuccess = true;

            if (!isPerformanceTest)
            {
                if (_testUpdateType == "incremental")
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

        static bool VerifyIncrementalUpdate(string targetDir)
        {
            // （原有逻辑不变）
            bool allSuccess = true;

            string file1 = Path.Combine(targetDir, "file1.txt");
            if (File.Exists(file1) && File.ReadAllText(file1) == "更新后的内容")
                Log("验证通过：file1.txt已更新");
            else
            {
                Log("验证失败：file1.txt未正确更新");
                allSuccess = false;
            }

            string newFile = Path.Combine(targetDir, "newfile.txt");
            if (File.Exists(newFile))
                Log("验证通过：newfile.txt已添加");
            else
            {
                Log("验证失败：newfile.txt未添加");
                allSuccess = false;
            }

            string newSubFile = Path.Combine(targetDir, "newsub", "newsubfile.txt");
            if (File.Exists(newSubFile))
                Log("验证通过：newsub/newsubfile.txt已添加");
            else
            {
                Log("验证失败：newsub/newsubfile.txt未添加");
                allSuccess = false;
            }

            string file2 = Path.Combine(targetDir, "file2.txt");
            if (!File.Exists(file2))
                Log("验证通过：file2.txt已删除");
            else
            {
                Log("验证失败：file2.txt未被删除");
                allSuccess = false;
            }

            string file3 = Path.Combine(targetDir, "sub", "file3.txt");
            if (!File.Exists(file3))
                Log("验证通过：sub/file3.txt已删除");
            else
            {
                Log("验证失败：sub/file3.txt未被删除");
                allSuccess = false;
            }

            string file4 = Path.Combine(targetDir, "file4.txt");
            if (File.Exists(file4) && File.ReadAllText(file4) == "保持不变的文件")
                Log("验证通过：file4.txt保持不变");
            else
            {
                Log("验证失败：file4.txt被修改或删除");
                allSuccess = false;
            }

            return allSuccess;
        }

        static bool VerifyBasicFiles(string targetDir)
        {
            // （原有逻辑不变）
            bool allSuccess = true;

            string readmePath = Path.Combine(targetDir, "readme.txt");
            if (File.Exists(readmePath) && File.ReadAllText(readmePath).Contains("Line 2"))
                Log("验证通过：readme.txt");
            else
            {
                Log("验证失败：readme.txt");
                allSuccess = false;
            }

            string dataPath = Path.Combine(targetDir, "data.bin");
            if (File.Exists(dataPath) && new FileInfo(dataPath).Length == 1024 * 5)
                Log("验证通过：data.bin");
            else
            {
                Log("验证失败：data.bin");
                allSuccess = false;
            }

            string chineseFile = Path.Combine(targetDir, "测试文档.txt");
            if (File.Exists(chineseFile))
                Log("验证通过：测试文档.txt");
            else
            {
                Log("验证失败：测试文档.txt");
                allSuccess = false;
            }

            string subFile = Path.Combine(targetDir, "subfolder", "subfile.ini");
            if (File.Exists(subFile))
                Log("验证通过：subfolder/subfile.ini");
            else
            {
                Log("验证失败：subfolder/subfile.ini");
                allSuccess = false;
            }

            return allSuccess;
        }

        static bool VerifyPerformanceTestFiles(string targetDir)
        {
            // （原有逻辑不变）
            bool allSuccess = true;

            for (int i = 0; i < _performanceTestScale.Value; i++)
            {
                string largeFilePath = Path.Combine(targetDir, $"large_file_{i + 1}.bin");
                if (File.Exists(largeFilePath))
                {
                    var fileInfo = new FileInfo(largeFilePath);
                    if (fileInfo.Length > 1024 * 1024 * 5)
                        Log($"验证通过：{largeFilePath}（大小：{FormatFileSize(fileInfo.Length)}）");
                    else
                    {
                        Log($"验证失败：{largeFilePath}（文件过小）");
                        allSuccess = false;
                    }
                }
                else
                {
                    Log($"验证失败：未找到 {largeFilePath}");
                    allSuccess = false;
                }
            }

            string specialDir = Path.Combine(targetDir, "special_chars");
            if (Directory.Exists(specialDir))
            {
                string longFileName = new string('a', 190) + ".txt";
                if (File.Exists(Path.Combine(specialDir, longFileName)))
                    Log("验证通过：长文件名文件");
                else
                {
                    Log("验证失败：长文件名文件");
                    allSuccess = false;
                }
            }
            else
            {
                Log("验证失败：未找到特殊字符目录");
                allSuccess = false;
            }

            var fileCount = Directory.EnumerateFiles(targetDir, "*.*", SearchOption.AllDirectories).Count();
            Log($"解压后总文件数：{fileCount}");

            return allSuccess;
        }

        static string FormatFileSize(long bytes)
        {
            // （原有逻辑不变）
            if (bytes < 1024) return $"{bytes}B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1}KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1}MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F1}GB";
        }

        static string SanitizeFileName(string fileName)
        {
            // （原有逻辑不变）
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (char c in invalidChars)
            {
                fileName = fileName.Replace(c, '_');
            }
            return fileName;
        }
        #endregion

        #region 核心更新功能（原有代码不变，此处省略以节省篇幅）
        static async Task HandleZipUpdate(string zipPath, string targetDir)
        {
            // （原有逻辑不变）
            Encoding encoding = await DetectZipEncoding(zipPath);
            Log($"检测到ZIP编码：{encoding.EncodingName}");

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

                    while (retryCount < 3 && !success)
                    {
                        try
                        {
                            string destPath = GetSafePath(targetDir, entry.FullName);
                            Log($"解压：{destPath}");

                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            entry.ExtractToFile(destPath, overwrite: true);

                            success = true;
                            UpdateProgress(++processedFiles, totalFiles);
                        }
                        catch (IOException ex) when (IsFileLocked(ex))
                        {
                            retryCount++;
                            Log($"文件被占用（{retryCount}/3）：{entry.FullName}");
                            await Task.Delay(1000);
                        }
                        catch (Exception ex)
                        {
                            Log($"解压失败：{ex.Message}");
                            failedFiles.Add(entry.FullName);
                            break;
                        }
                    }
                }

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

        static async Task HandleIncrementalUpdate(string zipPath, string targetDir)
        {
            // （原有逻辑不变）
            Encoding encoding = await DetectZipEncoding(zipPath);
            Log($"检测到增量包编码：{encoding.EncodingName}");

            using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Read, encoding))
            {
                var deleteListEntry = archive.Entries.FirstOrDefault(e => e.Name == "_delete_list.txt");
                if (deleteListEntry != null)
                {
                    Log("找到删除列表文件，处理需要删除的文件...");
                    await ProcessDeleteList(deleteListEntry, targetDir);
                }
                else
                {
                    Log("未找到删除列表文件，跳过删除步骤");
                }

                int totalFiles = archive.Entries.Count(e => !string.IsNullOrEmpty(e.Name) && e.Name != "_delete_list.txt");
                int processedFiles = 0;
                var failedFiles = new List<string>();

                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name) || entry.Name == "_delete_list.txt") continue;

                    int retryCount = 0;
                    bool success = false;

                    while (retryCount < 3 && !success)
                    {
                        try
                        {
                            string destPath = GetSafePath(targetDir, entry.FullName);
                            Log($"解压：{destPath}");

                            Directory.CreateDirectory(Path.GetDirectoryName(destPath));
                            entry.ExtractToFile(destPath, overwrite: true);

                            success = true;
                            UpdateProgress(++processedFiles, totalFiles);
                        }
                        catch (IOException ex) when (IsFileLocked(ex))
                        {
                            retryCount++;
                            Log($"文件被占用（{retryCount}/3）：{entry.FullName}");
                            await Task.Delay(1000);
                        }
                        catch (Exception ex)
                        {
                            Log($"解压失败：{ex.Message}");
                            failedFiles.Add(entry.FullName);
                            break;
                        }
                    }
                }

                if (failedFiles.Count > 0)
                {
                    Log($"增量更新完成：成功{processedFiles}个，失败{failedFiles.Count}个，总计{totalFiles}个");
                    Log("失败文件列表：");
                    foreach (var file in failedFiles)
                        Log($"  - {file}");
                }
                else
                {
                    Log($"增量更新完成：全部{totalFiles}个文件成功");
                }
            }
        }

        static async Task ProcessDeleteList(ZipArchiveEntry deleteListEntry, string targetDir)
        {
            // （原有逻辑不变）
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
                        if (File.Exists(filePath))
                        {
                            try
                            {
                                File.Delete(filePath);
                                Log($"已删除文件：{line}");
                                deletedCount++;
                            }
                            catch (Exception ex)
                            {
                                Log($"删除文件失败：{line} - {ex.Message}");
                                failedCount++;
                            }
                        }
                        else
                        {
                            Log($"文件不存在，无需删除：{line}");
                        }
                    }

                    Log($"删除操作完成：成功{deletedCount}个，失败{failedCount}个");
                }
            }
            catch (Exception ex)
            {
                Log($"处理删除列表时出错：{ex.Message}");
            }
        }

        static async Task<Encoding> DetectZipEncoding(string zipPath)
        {
            // （原有逻辑不变）
            return await Task.Run(() =>
            {
                try
                {
                    byte[] buffer = new byte[8192];
                    using (var fs = File.OpenRead(zipPath))
                    {
                        int read = fs.Read(buffer, 0, buffer.Length);
                        var detectionResult = CharsetDetector.DetectFromBytes(buffer.AsSpan(0, read).ToArray());
                        return detectionResult.Detected?.Encoding ?? Encoding.GetEncoding(936);
                    }
                }
                catch
                {
                    return Encoding.GetEncoding(936);
                }
            });
        }

        static async Task HandleInstallerUpdate(string installerPath, string targetDir)
        {
            // （原有逻辑不变）
            using (var process = new Process())
            {
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = installerPath,
                    WorkingDirectory = targetDir,
                    UseShellExecute = true,
                    Verb = "runas"
                };

                try
                {
                    process.Start();
                    Log($"安装包已启动：{installerPath}");
                    Log("等待安装完成...");

                    await process.WaitForExitAsync();

                    if (process.ExitCode == 0)
                    {
                        Log("安装包执行成功");
                    }
                    else
                    {
                        Log($"警告：安装包退出码非0，退出码：{process.ExitCode}");
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

        #region 辅助功能（原有代码不变，此处省略以节省篇幅）
        static string GetSafePath(string baseDir, string relativePath)
        {
            // （原有逻辑不变）
            string fullPath = Path.GetFullPath(Path.Combine(baseDir, relativePath));
            if (!fullPath.StartsWith(baseDir, StringComparison.OrdinalIgnoreCase))
                throw new System.Security.SecurityException("非法路径访问尝试：" + fullPath);
            return fullPath;
        }

        static void LaunchMainApplication(string targetDir, string mainAppExe)
        {
            // （原有逻辑不变）
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

                startInfo.ArgumentList.Add("updated");
                startInfo.ArgumentList.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                Log($"启动主程序失败：{ex.Message}");
            }
        }

        private static void RecordUpdateCompletionTime(string targetDir)
        {
            // （原有逻辑不变）
            try
            {
                string timeFile = Path.Combine(targetDir, "update_completion.time");
                File.WriteAllText(timeFile, DateTime.Now.ToString("o"));
                Log($"已记录更新时间到：{timeFile}");
            }
            catch (Exception ex)
            {
                Log($"记录更新时间失败：{ex.Message}");
            }
        }

        private static void DeletePackage(string packagePath)
        {
            // （原有逻辑不变）
            for (int i = 0; i < 3; i++)
            {
                try
                {
                    if (File.Exists(packagePath))
                    {
                        File.Delete(packagePath);
                        Log($"已删除安装包：{packagePath}");
                    }
                    break;
                }
                catch (Exception ex)
                {
                    Log($"删除安装包失败（{i + 1}/3）：{ex.Message}");
                    if (i < 2) Task.Delay(500).Wait();
                }
            }
        }

        static void InitializeLogging()
        {
            // （原有逻辑不变）
            string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
            Directory.CreateDirectory(logDir);
            string logFileName = $"update_{DateTime.Now:yyyy-MM-dd}_{GetLogFileIndex(logDir)}.log";
            string logPath = Path.Combine(logDir, logFileName);
            _logWriter = new StreamWriter(logPath, true, Encoding.UTF8) { AutoFlush = true };
        }

        static int GetLogFileIndex(string logDir)
        {
            // （原有逻辑不变）
            string prefix = $"update_{DateTime.Now:yyyy-MM-dd}_";
            int index = 1;
            while (File.Exists(Path.Combine(logDir, $"{prefix}{index}.log")))
            {
                index++;
            }
            return index;
        }

        private static void UpdateProgress(int current, int total)
        {
            // （原有逻辑不变）
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

            if (current > 1 && stopwatch.IsRunning)
            {
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                double filesPerSecond = current / elapsedSeconds;
                double estimatedRemainingSeconds = (total - current) / filesPerSecond;

                Console.Write($" | 速度：{filesPerSecond:F1}文件/秒");
                Console.Write($" | 剩余：{FormatTime(estimatedRemainingSeconds)}");
            }

            if (current == total)
                Console.WriteLine();
        }

        private static string FormatTime(double seconds)
        {
            // （原有逻辑不变）
            if (seconds < 60)
                return $"{(int)seconds}秒";
            else if (seconds < 3600)
                return $"{seconds / 60:F1}分钟";
            else
                return $"{seconds / 3600:F1}小时";
        }

        static async Task GracefulShutdown()
        {
            // （原有逻辑不变）
            Log("更新操作已完成");

            int waitSeconds = _isTestMode ? 3 : 10;
            Log($"{waitSeconds}秒后自动退出...");

            for (int i = waitSeconds; i > 0; i--)
            {
                Log($"剩余 {i} 秒...");
                await Task.Delay(1000);
            }

            Log("程序已退出");
            Environment.Exit(0);
        }

        static void WaitForExit()
        {
            // （原有逻辑不变）
            Log("按任意键退出...");
            Console.ReadKey();
            Environment.Exit(1);
        }

        static void Log(string message)
        {
            // （原有逻辑不变）
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
            Console.WriteLine(logMessage);

            try
            {
                _logWriter?.WriteLine(logMessage);
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private static bool IsFileLocked(IOException ex)
        {
            // （原有逻辑不变）
            int errorCode = Marshal.GetHRForException(ex) & ((1 << 16) - 1);
            return errorCode == 32 || errorCode == 33;
        }
        #endregion
    }
}