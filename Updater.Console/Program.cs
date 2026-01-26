using System;
using System.IO;
using System.Threading.Tasks;

namespace Updater.Console
{
    class Program
    {
        static async Task Main(string[] args)
        {
            try
            {
                // 检查是否包含GUI参数，如果有则提示使用GUI版本
                if (args.Length > 0 && (args[0] == "--gui" || args[0] == "-g"))
                {
                    Console.WriteLine("GUI模式需要运行Updater.exe（带GUI版本）");
                    Console.WriteLine("请使用不带--gui参数运行控制台版本");
                    return;
                }

                // 创建Updater实例并运行
                var updater = new Updater.Program();
                await updater.Run(args);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"更新失败: {ex.Message}");
                Console.WriteLine("详细错误信息:");
                Console.WriteLine(ex.ToString());
                Environment.Exit(1);
            }
        }
    }
}