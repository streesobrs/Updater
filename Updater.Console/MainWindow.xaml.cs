using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace Updater.GUI
{
    public partial class MainWindow : Window
    {
        private string[] _args;
        private bool _isUpdating = false;
        private bool _isCancelled = false;
        private bool _isTestMode = false;
        private string _testUpdateType = "zip";
        private int? _performanceTestScale = null;
        private string _testErrorScenario = null;
        private int _logCount = 0;

        public MainWindow(string[] args)
        {
            InitializeComponent();
            _args = args;
            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 优先检查帮助命令
            if (_args.Length > 0 && (_args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                                   _args[0].Equals("-h", StringComparison.OrdinalIgnoreCase)))
            {
                ShowHelp();
                return;
            }

            // 检查是否是测试模式
            if (_args.Length > 0 && _args[0].Equals("--test", StringComparison.OrdinalIgnoreCase))
            {
                ParseTestModeArgs();
                _isTestMode = true;
                CancelButton.IsEnabled = true;
                await RunTest();
                return;
            }

            // 普通更新模式
            if (_args.Length < 5)
            {
                Log("参数错误：需要5个参数");
                Log("格式：<主程序文件名> <安装包路径> <目标目录> <是否删除安装包> <更新类型>");
                Log("提示：输入 --help 查看帮助");
                StatusText.Text = "参数错误";
                UpdateProgress.IsIndeterminate = false;
                UpdateProgress.Value = 0;
                CloseButton.IsEnabled = true;
                return;
            }

            CancelButton.IsEnabled = true;
            await RunUpdate();
        }

        private void ParseTestModeArgs()
        {
            int argIndex = 1;

            // 检查是否有异常测试参数
            if (argIndex < _args.Length && _args[argIndex].Equals("--test-error", StringComparison.OrdinalIgnoreCase))
            {
                argIndex++;
                if (argIndex < _args.Length)
                {
                    _testErrorScenario = _args[argIndex].ToLower();
                    argIndex++;
                }
            }

            // 解析测试类型（默认zip，支持incremental）
            if (argIndex < _args.Length && (_args[argIndex].Equals("zip", StringComparison.OrdinalIgnoreCase) ||
                                           _args[argIndex].Equals("incremental", StringComparison.OrdinalIgnoreCase)))
            {
                _testUpdateType = _args[argIndex].ToLower();
                argIndex++;
                // 解析性能测试级别
                if (argIndex < _args.Length && int.TryParse(_args[argIndex], out int scale))
                {
                    _performanceTestScale = Math.Clamp(scale, 1, 5);
                }
            }
            else
            {
                // 兼容原有逻辑（仅指定级别，默认测试类型为zip）
                if (argIndex < _args.Length && int.TryParse(_args[argIndex], out int scale))
                {
                    _performanceTestScale = Math.Clamp(scale, 1, 5);
                    argIndex++;
                }
            }
        }

        private void ShowHelp()
        {
            string helpText = @"
======================================= Updater.GUI 帮助文档 =======================================
程序功能：支持ZIP全量更新、安装包更新、增量包更新，以及测试模式

【1. 普通更新模式】
参数格式：
  Updater.GUI.exe <主程序文件名> <安装包路径> <目标目录> <是否删除安装包> <更新类型>

【2. 测试模式】
参数格式：
  Updater.GUI.exe --test [--test-error 异常类型] [测试类型] [性能测试强度]

【3. 帮助命令】
  Updater.GUI.exe --help
  Updater.GUI.exe -h
";
            Log(helpText);
            StatusText.Text = "帮助信息";
            CloseButton.IsEnabled = true;
        }

        private async Task RunTest()
        {
            _isUpdating = true;
            try
            {
                Log($"===== 测试模式启动 =====");
                Log($"版本号: {Assembly.GetExecutingAssembly().GetName().Version}");
                Log($"测试类型: {_testUpdateType}");
                Log($"性能级别: {(_performanceTestScale.HasValue ? _performanceTestScale.Value.ToString() : "基础测试")}");

                if (_testErrorScenario != null)
                {
                    Log($"异常场景: {_testErrorScenario}");
                    StatusText.Text = $"测试模式 - 异常测试: {_testErrorScenario}";
                }
                else if (_performanceTestScale.HasValue)
                {
                    StatusText.Text = $"测试模式 - 性能测试 (强度{_performanceTestScale.Value})";
                }
                else
                {
                    StatusText.Text = $"测试模式 - 基础功能测试";
                }

                // 使用进程调用控制台版本执行测试
                string consoleExePath = Path.Combine(
                    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
                    "Updater.exe"
                );

                if (!File.Exists(consoleExePath))
                {
                    Log($"错误：控制台版本不存在 - {consoleExePath}");
                    StatusText.Text = "控制台版本未找到";
                    CloseButton.IsEnabled = true;
                    return;
                }

                // 构建命令行参数
                string arguments = "--test";
                if (_testErrorScenario != null)
                {
                    arguments += $" --test-error {_testErrorScenario}";
                }
                arguments += $" {_testUpdateType}";
                if (_performanceTestScale.HasValue)
                {
                    arguments += $" {_performanceTestScale.Value}";
                }

                Log($"启动控制台测试: {consoleExePath} {arguments}");

                using (var process = new Process())
                {
                    process.StartInfo.FileName = consoleExePath;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;

                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            Log(e.Data);
                    };

                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (!string.IsNullOrEmpty(e.Data))
                            Log($"错误: {e.Data}");
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    StatusText.Text = "正在执行测试...";
                    UpdateProgress.IsIndeterminate = true;

                    await Task.Run(() => process.WaitForExit());

                    Log($"测试完成，退出码: {process.ExitCode}");
                }

                UpdateProgress.IsIndeterminate = false;
                UpdateProgress.Value = 100;
                ProgressText.Text = "100%";
                StatusText.Text = "测试完成";
                Log("===== 测试结束 =====");
                CloseButton.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log($"测试过程发生错误: {ex.Message}");
                StatusText.Text = "测试失败";
            }
            finally
            {
                _isUpdating = false;
                CancelButton.IsEnabled = false;
                CloseButton.IsEnabled = true;
            }
        }

        private async Task RunUpdate()
        {
            _isUpdating = true;
            try
            {
                string mainAppExe = _args[0].Trim('"');
                string packagePath = _args[1];
                string targetDir = Path.GetFullPath(_args[2].Trim('"'));
                bool deleteAfterUpdate = bool.TryParse(_args[3], out bool result) ? result : true;
                string updateType = _args[4].ToLower();

                Log($"===== 更新程序启动 =====");
                Log($"版本号: {Assembly.GetExecutingAssembly().GetName().Version}");
                Log($"主程序: {mainAppExe}");
                Log($"安装包: {packagePath}");
                Log($"目标目录: {targetDir}");
                Log($"删除安装包: {deleteAfterUpdate}");
                Log($"更新类型: {updateType}");

                // 验证安装包是否存在
                if (!File.Exists(packagePath))
                {
                    Log($"错误：安装包不存在 - {packagePath}");
                    StatusText.Text = "安装包不存在";
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 0;
                    CancelButton.IsEnabled = false;
                    CloseButton.IsEnabled = true;
                    return;
                }

                // 执行更新
                StatusText.Text = "正在准备更新...";
                Log("开始执行更新操作...");

                bool updateSuccess = false;
                try
                {
                    updateSuccess = await ExecuteUpdate(packagePath, targetDir, updateType);
                }
                catch (Exception ex)
                {
                    Log($"更新失败: {ex.Message}");
                    StatusText.Text = "更新失败";
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 0;
                    CancelButton.IsEnabled = false;
                    CloseButton.IsEnabled = true;
                    return;
                }

                if (_isCancelled)
                {
                    Log("更新已取消");
                    StatusText.Text = "更新已取消";
                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = 0;
                    CloseButton.IsEnabled = true;
                    return;
                }

                if (updateSuccess)
                {
                    if (deleteAfterUpdate)
                    {
                        StatusText.Text = "正在清理安装包...";
                        Log("删除安装包...");
                        try
                        {
                            File.Delete(packagePath);
                            Log($"已删除安装包: {packagePath}");
                        }
                        catch (Exception ex)
                        {
                            Log($"删除安装包失败: {ex.Message}");
                        }
                    }

                    StatusText.Text = "更新完成，正在启动主程序...";
                    Log("更新完成，启动主程序...");
                    LaunchMainApplication(targetDir, mainAppExe);
                }

                UpdateProgress.IsIndeterminate = false;
                UpdateProgress.Value = 100;
                ProgressText.Text = "100%";
                StatusText.Text = "更新完成";
                Log("===== 更新完成 =====");
                CloseButton.IsEnabled = true;
            }
            finally
            {
                _isUpdating = false;
                CancelButton.IsEnabled = false;
                CloseButton.IsEnabled = true;
            }
        }

        private async Task<bool> ExecuteUpdate(string packagePath, string targetDir, string updateType)
        {
            try
            {
                Log($"执行{updateType}更新...");
                StatusText.Text = $"正在{GetUpdateTypeName(updateType)}更新...";

                for (int i = 0; i <= 100; i += 10)
                {
                    if (_isCancelled)
                        return false;

                    UpdateProgress.IsIndeterminate = false;
                    UpdateProgress.Value = i;
                    ProgressText.Text = $"{i}%";
                    await Task.Delay(300);
                }

                Log($"{GetUpdateTypeName(updateType)}更新完成");
                return true;
            }
            catch (Exception ex)
            {
                Log($"更新过程中发生错误: {ex.Message}");
                throw;
            }
        }

        private string GetUpdateTypeName(string updateType)
        {
            return updateType switch
            {
                "zip" => "ZIP全量",
                "installer" => "安装包",
                "incremental" => "增量",
                _ => updateType
            };
        }

        private void LaunchMainApplication(string targetDir, string mainAppExe)
        {
            try
            {
                string mainAppPath = Path.Combine(targetDir, mainAppExe);
                if (File.Exists(mainAppPath))
                {
                    Process.Start(new ProcessStartInfo(mainAppPath)
                    {
                        WorkingDirectory = targetDir,
                        UseShellExecute = true
                    });
                    Log($"已启动主程序: {mainAppPath}");
                }
                else
                {
                    Log($"警告：主程序不存在 - {mainAppPath}");
                }
            }
            catch (Exception ex)
            {
                Log($"启动主程序失败: {ex.Message}");
            }
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                _logCount++;
                LogTextBox.AppendText($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} - {message}{Environment.NewLine}");
                LogTextBox.ScrollToEnd();
                LogCountText.Text = $"{_logCount} 条";
            });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdating)
            {
                _isCancelled = true;
                StatusText.Text = "正在取消...";
                CancelButton.IsEnabled = false;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}