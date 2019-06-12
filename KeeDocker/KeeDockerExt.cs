using ICSharpCode.SharpZipLib.Tar;
using KeePass;
using KeePass.Forms;
using KeePass.Plugins;
using KeePass.Resources;
using KeePass.Util;
using KeePass.Util.Spr;
using KeePassLib;
using KeePassLib.Utility;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KeeDocker
{
    public sealed class KeeDockerExt : Plugin
    {
        private IPluginHost Host { get; set; } = null;

        private static void StartWithoutShellExecute(string strApp, string strArgs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = strApp;
                if (!string.IsNullOrEmpty(strArgs)) psi.Arguments = strArgs;
                psi.UseShellExecute = false;

                Process p = Process.Start(psi);
                if (p != null) p.Dispose();
            }
            catch (Exception ex)
            {
                string strMsg = KPRes.FileOrUrl + ": " + strApp;
                if (!string.IsNullOrEmpty(strArgs))
                    strMsg += MessageService.NewParagraph +
                        KPRes.Arguments + ": " + strArgs;

                MessageService.ShowWarning(strMsg, ex);
            }
        }

        internal static string CompileUrl(string strUrlToOpen, PwEntry pe,
            bool bAllowOverride, string strBaseRaw, bool? obForceEncCmd)
        {
            MainForm mf = Program.MainForm;
            PwDatabase pd = null;
            try { if (mf != null) pd = mf.DocumentManager.SafeFindContainerOf(pe); }
            catch (Exception) { Debug.Assert(false); }

            string strUrlFlt = strUrlToOpen;
            strUrlFlt = strUrlFlt.TrimStart(new char[] { ' ', '\t', '\r', '\n' });

            bool bEncCmd = (obForceEncCmd.HasValue ? obForceEncCmd.Value :
                WinUtil.IsCommandLineUrl(strUrlFlt));

            SprContext ctx = new SprContext(pe, pd, SprCompileFlags.All, false, bEncCmd);
            ctx.Base = strBaseRaw;
            ctx.BaseIsEncoded = false;

            string strUrl = SprEngine.Compile(strUrlFlt, ctx);

            string strOvr = Program.Config.Integration.UrlSchemeOverrides.GetOverrideForUrl(
                strUrl);
            if (!bAllowOverride) strOvr = null;
            if (strOvr != null)
            {
                bool bEncCmdOvr = WinUtil.IsCommandLineUrl(strOvr);

                SprContext ctxOvr = new SprContext(pe, pd, SprCompileFlags.All,
                    false, bEncCmdOvr);
                ctxOvr.Base = strUrl;
                ctxOvr.BaseIsEncoded = bEncCmd;

                strUrl = SprEngine.Compile(strOvr, ctxOvr);
            }

            return strUrl;
        }

        public override bool Initialize(IPluginHost host)
        {
            if (host == null)
                return false;

            Host = host;

            WinUtil.OpenUrlPre += (a, b) =>
            {
                var uri = new Uri(b.Url);

                if (uri.Scheme == "docker")
                {
                    Process p = null;

                    string strUrl = CompileUrl(b.Url, b.Entry, false, b.BaseRaw, null);

                    var commandLine = strUrl.Replace("docker://", "");

                    var fileDictionary = new Dictionary<string, string>();

                    var createContainerProcess = Process.Start(new ProcessStartInfo
                    {
                        FileName = "docker",
                        Arguments = $"create -it --rm {commandLine}",//ssh-over-tor",
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
                                    Arguments = $"cp - {containerID}:/tmp",
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
                        p = Process.Start("docker", $"start -a -i {containerID}");
                    }
                    catch (Exception exCmd)
                    {

                        string strMsg = KPRes.FileOrUrl + ": " + "docker";

                        MessageService.ShowWarning(strMsg, exCmd);
                    }

                    // Cause the following
                    b.Url = "";
                }
            };

            return base.Initialize(host);
        }
    }
}
