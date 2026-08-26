using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OVIA.Desktop
{
    internal static class OviaSingleInstanceService
    {
        private const string MutexName = @"Local\OVIA.Desktop.SingleInstance";
        private const string PipeName = "OVIA.Desktop.CommandPipe";
        private const string ActivateCommand = "__OVIA_ACTIVATE__";

        private static Mutex instanceMutex;
        private static volatile bool serverRunning;

        public static bool TryBecomePrimary()
        {
            bool createdNew = false;

            try
            {
                instanceMutex = new Mutex(true, MutexName, out createdNew);
                if (!createdNew)
                {
                    instanceMutex.Dispose();
                    instanceMutex = null;
                }
                return createdNew;
            }
            catch
            {
                return true;
            }
        }

        public static string GetForwardCommand(string[] args)
        {
            if (args != null)
            {
                int i;
                for (i = 0; i < args.Length; i++)
                {
                    string value = (args[i] ?? "").Trim();
                    if (value.StartsWith("ovia://", StringComparison.OrdinalIgnoreCase))
                    {
                        return value;
                    }
                }
            }

            return ActivateCommand;
        }

        public static bool IsActivateCommand(string command)
        {
            return string.Equals(command ?? "", ActivateCommand, StringComparison.Ordinal);
        }

        public static bool ForwardToPrimary(string command)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(
                    ".",
                    PipeName,
                    PipeDirection.Out,
                    PipeOptions.None))
                {
                    client.Connect(1800);

                    using (StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false)))
                    {
                        writer.AutoFlush = true;
                        writer.WriteLine(command ?? ActivateCommand);
                    }
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void StartServer(Control dispatcher, Action<string> commandHandler)
        {
            if (serverRunning || dispatcher == null || commandHandler == null)
            {
                return;
            }

            serverRunning = true;

            Task.Factory.StartNew(
                delegate
                {
                    while (serverRunning)
                    {
                        try
                        {
                            using (NamedPipeServerStream server = new NamedPipeServerStream(
                                PipeName,
                                PipeDirection.In,
                                1,
                                PipeTransmissionMode.Byte,
                                PipeOptions.None))
                            {
                                server.WaitForConnection();

                                string command = "";
                                using (StreamReader reader = new StreamReader(server, Encoding.UTF8, true, 1024, true))
                                {
                                    command = reader.ReadLine() ?? "";
                                }

                                if (!serverRunning)
                                {
                                    break;
                                }

                                if (!dispatcher.IsDisposed && dispatcher.IsHandleCreated)
                                {
                                    string captured = command;
                                    dispatcher.BeginInvoke(new MethodInvoker(delegate
                                    {
                                        commandHandler(captured);
                                    }));
                                }
                            }
                        }
                        catch
                        {
                            if (!serverRunning)
                            {
                                break;
                            }

                            Thread.Sleep(100);
                        }
                    }
                },
                TaskCreationOptions.LongRunning
            );
        }

        public static void Stop()
        {
            serverRunning = false;

            try
            {
                // WaitForConnection을 깨워 서버 스레드가 정리되게 합니다.
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(100);
                    using (StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false)))
                    {
                        writer.AutoFlush = true;
                        writer.WriteLine(ActivateCommand);
                    }
                }
            }
            catch
            {
            }

            if (instanceMutex != null)
            {
                try
                {
                    instanceMutex.ReleaseMutex();
                }
                catch
                {
                }

                try
                {
                    instanceMutex.Dispose();
                }
                catch
                {
                }

                instanceMutex = null;
            }
        }
    }
}
