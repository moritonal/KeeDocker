using ICSharpCode.SharpZipLib.Tar;
using KeePass;
using KeePass.Plugins;
using KeePass.Resources;
using KeePass.Util;
using KeePass.Util.Spr;
using KeePassLib;
using KeePassLib.Utility;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace KeeDocker
{
    public sealed class KeeDockerExt : Plugin
    {
        internal static string CompileUrl(string url, PwEntry entry, string baseUrl)
        {
            PwDatabase database = null;

            try
            {
                database = Program.MainForm.DocumentManager.SafeFindContainerOf(entry);
            }
            catch (Exception)
            {
                Debug.Assert(false);
            }

            url = url.TrimStart(new char[] { ' ', '\t', '\r', '\n' });

            bool bEncodeAsAutoTypeSequence = WinUtil.IsCommandLineUrl(url);

            var context = new SprContext(entry, database, SprCompileFlags.All, false, bEncodeAsAutoTypeSequence)
            {
                Base = baseUrl,
                BaseIsEncoded = false
            };

            return SprEngine.Compile(url, context);
        }

        public override bool Initialize(IPluginHost host)
        {
            if (host == null)
                return false;

            WinUtil.OpenUrlPre += (a, b) =>
            {
                var uri = new Uri(b.Url);

                if (uri.Scheme == "docker")
                {
                    string strUrl = CompileUrl(b.Url, b.Entry, b.BaseRaw);

                    var commandLine = strUrl.Replace("docker://", "");

                    var fileDictionary = new Dictionary<string, string>();

                    var createContainerProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = "create -it --rm " + commandLine,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    });
                    createContainerProcess.WaitForExit();
                    var containerID = createContainerProcess.StandardOutput.ReadToEnd().Trim();

                    using (var stream = new MemoryStream())
                    {
                        var archive = new TarOutputStream(stream);

                        foreach (var binary in b.Entry.Binaries)
                        {
                            byte[] data = binary.Value.ReadData();

                            var entry = TarEntry.CreateTarEntry(binary.Key);
                            entry.Size = data.Length;

                            archive.PutNextEntry(entry);

                            archive.Write(data, 0, data.Length);

                            archive.CloseEntry();
                        }

                        archive.IsStreamOwner = false;
                        archive.Close();

                        stream.Position = 0;

                        using (var reader = new StreamReader(stream))
                        {
                            var data = reader.ReadToEnd();

                            var copyContainerProcess = new Process
                            {
                                StartInfo = new ProcessStartInfo
                                {
                                    FileName = "docker",
                                    Arguments = "cp - " + containerID + ":/tmp",
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardInput = true,
                                    RedirectStandardError = true,
                                    CreateNoWindow = true
                                }
                            };
                            copyContainerProcess.Start();
                            copyContainerProcess.StandardInput.WriteLine(data);
                            copyContainerProcess.StandardInput.Close();

                            copyContainerProcess.WaitForExit();

                            var error = copyContainerProcess.StandardError.ReadToEnd().Trim();
                            var output = copyContainerProcess.StandardOutput.ReadToEnd().Trim();
                        }
                    }

                    try
                    {
                        var dockerProcess = Process.Start("docker", "start -a -i " + containerID);
                    }
                    catch (Exception exCmd)
                    {
                        string strMsg = KPRes.FileOrUrl + ": " + "docker";

                        MessageService.ShowWarning(strMsg, exCmd);
                    }

                    // This causes the url not to be lauched via Windows
                    b.Url = "";
                }
            };

            return base.Initialize(host);
        }
    }
}
