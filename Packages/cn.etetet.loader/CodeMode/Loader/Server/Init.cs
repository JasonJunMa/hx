using System;
using CommandLine;

namespace ET
{
    public class Init
    {
        public void Start()
        {
            try
            {
                AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
                {
                    Log.Error(e.ExceptionObject.ToString());
                };

                // 命令行参数
                Parser.Default.ParseArguments<Options>(System.Environment.GetCommandLineArgs())
                        .WithNotParsed(error => throw new Exception($"命令行格式错误! {error}"))
                        .WithParsed((o) => World.Instance.AddSingleton(o));

                // 测试用例使用单线程模式，方便重置测试环境
                if (Options.Instance.SceneName == "Test")
                {
                    Options.Instance.SingleThread = 1;
                    Options.Instance.Console = 1;
                }

                Console.WriteLine();
                Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
                Console.WriteLine("║             华夏 MMORPG 服务端  (ET10)                    ║");
                Console.WriteLine("╠══════════════════════════════════════════════════════════╣");
                Console.WriteLine($"║  SceneName  : {Options.Instance.SceneName,-46}║");
                Console.WriteLine($"║  StartConfig: {Options.Instance.StartConfig,-46}║");
                Console.WriteLine($"║  Process    : {Options.Instance.Process,-46}║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
                Console.WriteLine();
                
                World.Instance.AddSingleton<Logger>().Log = new NLogger(Options.Instance.SceneName);
				
                ETTask.ExceptionHandler += Log.Error;
                
                World.Instance.AddSingleton<TimeInfo>();
                World.Instance.AddSingleton<FiberManager>();
                World.Instance.AddSingleton<CodeLoader>().Start();
                Console.WriteLine("[OK] 启动完成");
                Console.WriteLine();
            }
            catch (Exception e)
            {
                Console.WriteLine();
                Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
                Console.WriteLine("║  ❌ 启动异常!                                            ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
                Console.WriteLine(e);
                Console.WriteLine();
            }
        }

        public void Update()
        {
            FiberManager.Instance.Update();
        }

        public void LateUpdate()
        {
            FiberManager.Instance.LateUpdate();
        }
    }
}